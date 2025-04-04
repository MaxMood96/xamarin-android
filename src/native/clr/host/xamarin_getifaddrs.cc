#include <cerrno>
#include <dlfcn.h>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <mutex>

#include <unistd.h>

#include <ifaddrs.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <linux/netlink.h>
#include <linux/rtnetlink.h>
#include <linux/if_arp.h>
#include <linux/if_link.h>
#include <netinet/in.h>

#include <android/log.h>

#include <runtime-base/internal-pinvokes.hh>
#include <runtime-base/logger.hh>
#include <runtime-base/util.hh>

using namespace xamarin::android;

/* Maximum interface address label size, should be more than enough */
#define MAX_IFA_LABEL_SIZE 1024

#if defined (__linux__) || defined (__linux)

/* This is the message we send to the kernel */
typedef struct {
	struct nlmsghdr header;
	struct rtgenmsg message;
} netlink_request;

typedef struct {
	int sock_fd;
	int seq;
	struct sockaddr_nl them; /* kernel end */
	struct sockaddr_nl us; /* our end */
	struct msghdr message_header; /* for use with sendmsg */
	struct iovec payload_vector; /* Used to send netlink_request */
} netlink_session;

/* Turns out that quite a few link types have address length bigger than the 8 bytes allocated in
 * this structure as defined by the OS. Examples are Infiniband or ipv6 tunnel devices
 */
struct sockaddr_ll_extended {
    unsigned short int sll_family;
    unsigned short int sll_protocol;
    int sll_ifindex;
    unsigned short int sll_hatype;
    unsigned char sll_pkttype;
    unsigned char sll_halen;
    unsigned char sll_addr[24];
};

static int parse_netlink_reply (netlink_session *session, struct ifaddrs **ifaddrs_head, struct ifaddrs **last_ifaddr);
static struct ifaddrs *get_link_info (const struct nlmsghdr *message);
static struct ifaddrs *get_link_address (const struct nlmsghdr *message, struct ifaddrs **ifaddrs_head);
static int open_netlink_session (netlink_session *session);
static int send_netlink_dump_request (netlink_session *session, int type);
static int append_ifaddr (struct ifaddrs *addr, struct ifaddrs **ifaddrs_head, struct ifaddrs **last_ifaddr);
static int fill_ll_address (struct sockaddr_ll_extended **sa, struct ifinfomsg *net_interface, void *rta_data, size_t rta_payload_length);
static int fill_sa_address (struct sockaddr **sa, struct ifaddrmsg *net_address, void *rta_data, size_t rta_payload_length);
static void free_single_xamarin_ifaddrs (struct ifaddrs **ifap);
static void get_ifaddrs_impl (int (**getifaddrs_impl) (struct ifaddrs **ifap), void (**freeifaddrs_impl) (struct ifaddrs *ifa));
static struct ifaddrs *find_interface_by_index (int index, struct ifaddrs **ifaddrs_head);
static char *get_interface_name_by_index (int index, struct ifaddrs **ifaddrs_head);
static int get_interface_flags_by_index (int index, struct ifaddrs **ifaddrs_head);
static int calculate_address_netmask (struct ifaddrs *ifa, struct ifaddrmsg *net_address);
#if DEBUG
static void print_ifla_name (int id);
static void print_address_list (const char title[], struct ifaddrs *list);
#endif

/* We don't use 'struct ifaddrs' since that doesn't exist in Android's bionic, but since our
 * version of the structure is 100% compatible we can just use it instead
 */
typedef int (*getifaddrs_impl_fptr)(struct ifaddrs **);
typedef void (*freeifaddrs_impl_fptr)(struct ifaddrs *ifa);

namespace {
	getifaddrs_impl_fptr getifaddrs_impl = nullptr;
	freeifaddrs_impl_fptr freeifaddrs_impl = nullptr;
	bool initialized = false;
	std::mutex init_lock{};

	void _monodroid_getifaddrs_init ()
	{
		get_ifaddrs_impl (&getifaddrs_impl, &freeifaddrs_impl);
	}
}

int
_monodroid_getifaddrs (struct ifaddrs **ifap)
{
	if (!initialized) {
		std::lock_guard<std::mutex> lock (init_lock);
		if (!initialized) {
			_monodroid_getifaddrs_init ();
			initialized = true;
		}
	}

	int ret = -1;

	if (getifaddrs_impl)
		return (*getifaddrs_impl)(ifap);

	if (!ifap)
		return ret;

	*ifap = NULL;
	struct ifaddrs *ifaddrs_head = 0;
	struct ifaddrs *last_ifaddr = 0;
	netlink_session session;

	if (open_netlink_session (&session) < 0) {
		goto cleanup;
	}

	/* Request information about the specified link. In our case it will be all of them since we
	   request the root of the link tree below
	*/
	if ((send_netlink_dump_request (&session, RTM_GETLINK) < 0) ||
			(parse_netlink_reply (&session, &ifaddrs_head, &last_ifaddr) < 0) ||
			(send_netlink_dump_request (&session, RTM_GETADDR) < 0) ||
			(parse_netlink_reply (&session, &ifaddrs_head, &last_ifaddr) < 0)) {
		_monodroid_freeifaddrs (ifaddrs_head);
		goto cleanup;
	}

	ret = 0;
	*ifap = ifaddrs_head;
#if DEBUG
	print_address_list ("Initial interfaces list", *ifap);
#endif

  cleanup:
	if (session.sock_fd >= 0) {
		close (session.sock_fd);
		session.sock_fd = -1;
	}

	return ret;
}

void
_monodroid_freeifaddrs (struct ifaddrs *ifa)
{
	struct ifaddrs *cur, *next;

	if (!ifa)
		return;

	if (freeifaddrs_impl) {
		(*freeifaddrs_impl)(ifa);
		return;
	}

#if DEBUG
	print_address_list ("List passed to freeifaddrs", ifa);
#endif
	cur = ifa;
	while (cur) {
		next = cur->ifa_next;
		free_single_xamarin_ifaddrs (&cur);
		cur = next;
	}
}

static void
get_ifaddrs_impl (int (**getifaddrs_implementation) (struct ifaddrs **ifap), void (**freeifaddrs_implementation) (struct ifaddrs *ifa))
{
	void *libc;

	abort_if_invalid_pointer_argument (getifaddrs_implementation, "getifaddrs_implementation");
	abort_if_invalid_pointer_argument (freeifaddrs_implementation, "freeifaddrs_implementation");

	libc = dlopen ("libc.so", RTLD_NOW);
	if (libc) {
		*getifaddrs_implementation = reinterpret_cast<int (*)(struct ifaddrs**)> (dlsym (libc, "getifaddrs"));
		if (*getifaddrs_implementation)
			*freeifaddrs_implementation = reinterpret_cast<void (*) (struct ifaddrs*)> (dlsym (libc, "freeifaddrs"));
	}

	if (!*getifaddrs_implementation) {
		log_info (LOG_NET, "This libc does not have getifaddrs/freeifaddrs, using Xamarin's"sv);
	} else {
		log_info (LOG_NET, "This libc has getifaddrs/freeifaddrs"sv);
	}
}

static void
free_single_xamarin_ifaddrs (struct ifaddrs **ifap)
{
	struct ifaddrs *ifa = ifap ? *ifap : NULL;
	if (!ifa)
		return;

	if (ifa->ifa_name)
		free (ifa->ifa_name);

	if (ifa->ifa_addr)
		free (ifa->ifa_addr);

	if (ifa->ifa_netmask)
		free (ifa->ifa_netmask);

	if (ifa->ifa_broadaddr)
		free (ifa->ifa_broadaddr);

	if (ifa->ifa_data)
		free (ifa->ifa_data);

	free (ifa);
	*ifap = NULL;
}

static int
open_netlink_session (netlink_session *session)
{
	abort_if_invalid_pointer_argument (session, "session");

	memset (session, 0, sizeof (*session));
	session->sock_fd = socket (AF_NETLINK, SOCK_RAW, NETLINK_ROUTE);
	if (session->sock_fd == -1) {
		log_warn (LOG_NETLINK, "Failed to create a netlink socket. {}", strerror (errno));
		return -1;
	}

	/* Fill out addresses */
	session->us.nl_family = AF_NETLINK;

	/* We have previously used `getpid()` here but it turns out that WebView/Chromium does the same
	   and there can only be one session with the same PID. Setting it to 0 will cause the kernel to
	   assign some PID that's unique and valid instead.

	   See: https://bugzilla.xamarin.com/show_bug.cgi?id=41860
	*/
	session->us.nl_pid = 0;
	session->us.nl_groups = 0;

	session->them.nl_family = AF_NETLINK;

	if (bind (session->sock_fd, (struct sockaddr *)&session->us, sizeof (session->us)) < 0) {
		log_warn (LOG_NETLINK, "Failed to bind to the netlink socket. {}", strerror (errno));
		return -1;
	}

	return 0;
}

static int
send_netlink_dump_request (netlink_session *session, int type)
{
	netlink_request request;

	memset (&request, 0, sizeof (request));
	request.header.nlmsg_len = NLMSG_LENGTH (sizeof (struct rtgenmsg));
	/* Flags (from netlink.h):
	   NLM_F_REQUEST - it's a request message
	   NLM_F_DUMP - gives us the root of the link tree and returns all links matching our requested
	   AF, which in our case means all of them (AF_PACKET)
	*/
	request.header.nlmsg_flags = NLM_F_REQUEST | NLM_F_ROOT | NLM_F_MATCH;
	request.header.nlmsg_seq = static_cast<__u32>(++session->seq);
	request.header.nlmsg_pid = session->us.nl_pid;
	request.header.nlmsg_type = static_cast<__u16>(type);

	/* AF_PACKET means we want to see everything */
	request.message.rtgen_family = AF_PACKET;

	memset (&session->payload_vector, 0, sizeof (session->payload_vector));
	session->payload_vector.iov_len = request.header.nlmsg_len;
	session->payload_vector.iov_base = &request;

	memset (&session->message_header, 0, sizeof (session->message_header));
	session->message_header.msg_namelen = sizeof (session->them);
	session->message_header.msg_name = &session->them;
	session->message_header.msg_iovlen = 1;
	session->message_header.msg_iov = &session->payload_vector;

	if (sendmsg (session->sock_fd, (const struct msghdr*)&session->message_header, 0) < 0) {
		log_warn (LOG_NETLINK, "Failed to send netlink message. {}", strerror (errno));
		return -1;
	}

	return 0;
}

static int
append_ifaddr (struct ifaddrs *addr, struct ifaddrs **ifaddrs_head, struct ifaddrs **last_ifaddr)
{
	abort_if_invalid_pointer_argument (addr, "addr");
	abort_if_invalid_pointer_argument (ifaddrs_head, "ifaddrs_head");
	abort_if_invalid_pointer_argument (last_ifaddr, "last_ifaddr");

	if (!*ifaddrs_head) {
		*ifaddrs_head = *last_ifaddr = addr;
		if (!*ifaddrs_head)
			return -1;
	} else if (!*last_ifaddr) {
		struct ifaddrs *last = *ifaddrs_head;

		while (last->ifa_next)
			last = last->ifa_next;
		*last_ifaddr = last;
	}

	addr->ifa_next = NULL;
	if (addr == *last_ifaddr)
		return 0;

	(*last_ifaddr)->ifa_next = addr;
	*last_ifaddr = addr;

	return 0;
}

static int
parse_netlink_reply (netlink_session *session, struct ifaddrs **ifaddrs_head, struct ifaddrs **last_ifaddr)
{
	struct msghdr netlink_reply;
	struct iovec reply_vector;
	struct nlmsghdr *current_message;
	struct ifaddrs *addr;
	int ret = -1;
	unsigned char *response = NULL;

	abort_if_invalid_pointer_argument (session, "session");
	abort_if_invalid_pointer_argument (ifaddrs_head, "ifaddrs_head");
	abort_if_invalid_pointer_argument (last_ifaddr, "last_ifaddr");

	size_t buf_size = static_cast<size_t>(getpagesize ());
	log_debug (LOG_NETLINK, "receive buffer size == {}", buf_size);

	size_t alloc_size = Helpers::multiply_with_overflow_check<size_t> (sizeof(*response), buf_size);
	response = (unsigned char*)malloc (alloc_size);
	ssize_t length = 0z;
	if (!response) {
		goto cleanup;
	}

	while (1) {
		memset (response, 0, buf_size);
		memset (&reply_vector, 0, sizeof (reply_vector));
		reply_vector.iov_len = buf_size;
		reply_vector.iov_base = response;

		memset (&netlink_reply, 0, sizeof (netlink_reply));
		netlink_reply.msg_namelen = sizeof (&session->them);
		netlink_reply.msg_name = &session->them;
		netlink_reply.msg_iovlen = 1;
		netlink_reply.msg_iov = &reply_vector;

		length = recvmsg (session->sock_fd, &netlink_reply, 0);
		log_debug (LOG_NETLINK, "  length == {}", (int)length);

		if (length < 0) {
			log_debug (LOG_NETLINK, "Failed to receive reply from netlink. {}", strerror (errno));
			goto cleanup;
		}

#if DEBUG
		if (Util::should_log (LOG_NETLINK)) {
			log_debug_nocheck (LOG_NETLINK, "response flags:"sv);
			if (netlink_reply.msg_flags == 0)
				log_debug_nocheck (LOG_NETLINK, "   [NONE]"sv);
			else {
				if (netlink_reply.msg_flags & MSG_EOR)
					log_debug_nocheck (LOG_NETLINK, "   MSG_EOR"sv);
				if (netlink_reply.msg_flags & MSG_TRUNC)
					log_debug_nocheck (LOG_NETLINK, "   MSG_TRUNC"sv);
				if (netlink_reply.msg_flags & MSG_CTRUNC)
					log_debug_nocheck (LOG_NETLINK, "   MSG_CTRUNC"sv);
				if (netlink_reply.msg_flags & MSG_OOB)
					log_debug_nocheck (LOG_NETLINK, "   MSG_OOB"sv);
				if (netlink_reply.msg_flags & MSG_ERRQUEUE)
					log_debug_nocheck (LOG_NETLINK, "   MSG_ERRQUEUE"sv);
			}
		}
#endif

		if (length == 0)
			break;

		for (current_message = (struct nlmsghdr*)response; current_message && NLMSG_OK (current_message, static_cast<size_t>(length)); current_message = NLMSG_NEXT (current_message, length)) {
			log_debug (LOG_NETLINK, "next message... (type: {})", current_message->nlmsg_type);
			switch (current_message->nlmsg_type) {
				/* See rtnetlink.h */
				case RTM_NEWLINK:
					log_debug (LOG_NETLINK, "  dumping link..."sv);
					addr = get_link_info (current_message);
					if (!addr || append_ifaddr (addr, ifaddrs_head, last_ifaddr) < 0) {
						ret = -1;
						goto cleanup;
					}
					log_debug (LOG_NETLINK, "  done"sv);
					break;

				case RTM_NEWADDR:
					log_debug (LOG_NETLINK, "  got an address"sv);
					addr = get_link_address (current_message, ifaddrs_head);
					if (!addr || append_ifaddr (addr, ifaddrs_head, last_ifaddr) < 0) {
						ret = -1;
						goto cleanup;
					}
					break;

				case NLMSG_DONE:
					log_debug (LOG_NETLINK, "  message done"sv);
					ret = 0;
					goto cleanup;
					break;

				default:
					log_debug (LOG_NETLINK, "  message type: {}", current_message->nlmsg_type);
					break;
			}
		}
	}

  cleanup:
	if (response)
		free (response);
	return ret;
}

static int
fill_sa_address (struct sockaddr **sa, struct ifaddrmsg *net_address, void *rta_data, size_t rta_payload_length)
{
	abort_if_invalid_pointer_argument (sa, "sa");
	abort_if_invalid_pointer_argument (net_address, "net_address");
	abort_if_invalid_pointer_argument (rta_data, "rta_data");

	switch (net_address->ifa_family) {
		case AF_INET: {
			struct sockaddr_in *sa4;
			if (rta_payload_length != 4) /* IPv4 address length */ {
				log_warn (LOG_NETLINK, "Unexpected IPv4 address payload length {}", rta_payload_length);
				return -1;
			}
			sa4 = (struct sockaddr_in*)calloc (1, sizeof (*sa4));
			if (sa4 == nullptr)
				return -1;

			sa4->sin_family = AF_INET;
			memcpy (&sa4->sin_addr, rta_data, rta_payload_length);
			*sa = (struct sockaddr*)sa4;
			break;
		}

		case AF_INET6: {
			struct sockaddr_in6 *sa6;
			if (rta_payload_length != 16) /* IPv6 address length */ {
				log_warn (LOG_NETLINK, "Unexpected IPv6 address payload length {}", rta_payload_length);
				return -1;
			}
			sa6 = (struct sockaddr_in6*)calloc (1, sizeof (*sa6));
			if (sa6 == nullptr)
				return -1;

			sa6->sin6_family = AF_INET6;
			memcpy (&sa6->sin6_addr, rta_data, rta_payload_length);
			if (IN6_IS_ADDR_LINKLOCAL (&sa6->sin6_addr) || IN6_IS_ADDR_MC_LINKLOCAL (&sa6->sin6_addr))
				sa6->sin6_scope_id = net_address->ifa_index;
			*sa = (struct sockaddr*)sa6;
			break;
		}

		default: {
			struct sockaddr *sagen;
			if (rta_payload_length > sizeof (sagen->sa_data)) {
				log_warn (LOG_NETLINK, "Unexpected RTA payload length {} (wanted at most {})", rta_payload_length, sizeof (sagen->sa_data));
				return -1;
			}

			*sa = sagen = (struct sockaddr*)calloc (1, sizeof (*sagen));
			if (!sagen)
				return -1;

			sagen->sa_family = net_address->ifa_family;
			memcpy (&sagen->sa_data, rta_data, rta_payload_length);
			break;
		}
	}

	return 0;
}

static int
fill_ll_address (struct sockaddr_ll_extended **sa, struct ifinfomsg *net_interface, void *rta_data, size_t rta_payload_length)
{
	abort_if_invalid_pointer_argument (sa, "sa");
	abort_if_invalid_pointer_argument (net_interface, "net_interface");

	/* Always allocate, do not free - caller may reuse the same variable */
	*sa = reinterpret_cast<sockaddr_ll_extended*>(calloc (1, sizeof (**sa)));
	if (!*sa)
		return -1;

	(*sa)->sll_family = AF_PACKET; /* Always for physical links */

	/* The assert can only fail for Iniband links, which are quite unlikely to be found
	 * in any mobile devices
	 */
	log_debug (LOG_NETLINK, "rta_payload_length == {}; sizeof sll_addr == {}; hw type == {:x}", rta_payload_length, sizeof ((*sa)->sll_addr), net_interface->ifi_type);
	if (static_cast<size_t>(rta_payload_length) > sizeof ((*sa)->sll_addr)) {
		log_info (LOG_NETLINK, "Address is too long to place in sockaddr_ll ({} > {})", rta_payload_length, sizeof ((*sa)->sll_addr));
		free (*sa);
		*sa = NULL;
		return -1;
	}

	if (rta_payload_length > std::numeric_limits<unsigned char>::max ()) {
		log_info (LOG_NETLINK, "Payload length too big to fit in the address structure"sv);
		free (*sa);
		*sa = NULL;
		return -1;
	}

	(*sa)->sll_ifindex = net_interface->ifi_index;
	(*sa)->sll_hatype = net_interface->ifi_type;
	(*sa)->sll_halen = static_cast<unsigned char>(rta_payload_length);
	memcpy ((*sa)->sll_addr, rta_data, rta_payload_length);

	return 0;
}

static struct ifaddrs *
find_interface_by_index (int index, struct ifaddrs **ifaddrs_head)
{
	struct ifaddrs *cur;
	if (!ifaddrs_head || !*ifaddrs_head)
		return NULL;

	/* Normally expensive, but with the small amount of links in the chain we'll deal with it's not
	 * worth the extra houskeeping and memory overhead
	 */
	cur = *ifaddrs_head;
	while (cur) {
		if (cur->ifa_addr && cur->ifa_addr->sa_family == AF_PACKET && ((struct sockaddr_ll_extended*)cur->ifa_addr)->sll_ifindex == index)
			return cur;
		if (cur == cur->ifa_next)
			break;
		cur = cur->ifa_next;
	}

	return NULL;
}

static char *
get_interface_name_by_index (int index, struct ifaddrs **ifaddrs_head)
{
	struct ifaddrs *iface = find_interface_by_index (index, ifaddrs_head);
	if (!iface || !iface->ifa_name)
		return NULL;

	return iface->ifa_name;
}

static int
get_interface_flags_by_index (int index, struct ifaddrs **ifaddrs_head)
{
	struct ifaddrs *iface = find_interface_by_index (index, ifaddrs_head);
	if (!iface)
		return 0;

	return static_cast<int>(iface->ifa_flags);
}

static int
calculate_address_netmask (struct ifaddrs *ifa, struct ifaddrmsg *net_address)
{
	if (ifa->ifa_addr && ifa->ifa_addr->sa_family != AF_UNSPEC && ifa->ifa_addr->sa_family != AF_PACKET) {
		uint32_t prefix_length = 0;
		uint32_t data_length = 0;
		unsigned char *netmask_data = NULL;

		switch (ifa->ifa_addr->sa_family) {
			case AF_INET: {
				struct sockaddr_in *sa = (struct sockaddr_in*)calloc (1, sizeof (struct sockaddr_in));
				if (!sa)
					return -1;

				ifa->ifa_netmask = (struct sockaddr*)sa;
				prefix_length = net_address->ifa_prefixlen;
				if (prefix_length > 32)
					prefix_length = 32;
				data_length = sizeof (sa->sin_addr);
				netmask_data = (unsigned char*)&sa->sin_addr;
				break;
			}

			case AF_INET6: {
				struct sockaddr_in6 *sa = (struct sockaddr_in6*)calloc (1, sizeof (struct sockaddr_in6));
				if (!sa)
					return -1;

				ifa->ifa_netmask = (struct sockaddr*)sa;
				prefix_length = net_address->ifa_prefixlen;
				if (prefix_length > 128)
					prefix_length = 128;
				data_length = sizeof (sa->sin6_addr);
				netmask_data = (unsigned char*)&sa->sin6_addr;
				break;
			}
		}

		if (ifa->ifa_netmask && netmask_data) {
			/* Fill the first X bytes with 255 */
			uint32_t prefix_bytes = prefix_length / 8;
			uint32_t postfix_bytes;

			if (prefix_bytes > data_length) {
				errno = EINVAL;
				return -1;
			}
			postfix_bytes = data_length - prefix_bytes;
			memset (netmask_data, 0xFF, prefix_bytes);
			if (postfix_bytes > 0)
				memset (netmask_data + prefix_bytes + 1, 0x00, postfix_bytes);
			log_debug (LOG_NETLINK, "   calculating netmask, prefix length is {} bits ({} bytes), data length is {} bytes\n", prefix_length, prefix_bytes, data_length);
			if (prefix_bytes + 2 < data_length)
				/* Set the rest of the mask bits in the byte following the last 0xFF value */
				netmask_data [prefix_bytes + 1] = static_cast<unsigned char>(0xff << (8 - (prefix_length % 8)));
			if (Util::should_log (LOG_NETLINK)) {
				log_debug_nocheck (LOG_NETLINK, "   netmask is: "sv);
				for (uint32_t i = 0; i < data_length; i++) {
					log_debug_nocheck_fmt (LOG_NETLINK, "{}{}", i == 0 ? " "sv : "."sv, (unsigned char)ifa->ifa_netmask->sa_data [i]);
				}
			}
		}
	}

	return 0;
}


static struct ifaddrs *
get_link_address (const struct nlmsghdr *message, struct ifaddrs **ifaddrs_head)
{
	ssize_t length = 0z;
	struct rtattr *attribute;
	struct ifaddrmsg *net_address;
	struct ifaddrs *ifa = NULL;
	struct sockaddr **sa;
	size_t payload_size;

	abort_if_invalid_pointer_argument (message, "message");
	net_address = reinterpret_cast<ifaddrmsg*> (NLMSG_DATA (message));
	length = static_cast<ssize_t>(IFA_PAYLOAD (message));
	log_debug (LOG_NETLINK, "   address data length: {}", length);
	if (length <= 0) {
		goto error;
	}

	ifa = reinterpret_cast<ifaddrs*> (calloc (1, sizeof (*ifa)));
	if (!ifa) {
		goto error;
	}

	// values < 0 are never returned, the cast is safe
	ifa->ifa_flags = static_cast<unsigned int>(get_interface_flags_by_index (static_cast<int>(net_address->ifa_index), ifaddrs_head));

	attribute = IFA_RTA (net_address);
	log_debug (LOG_NETLINK, "   reading attributes"sv);
	while (RTA_OK (attribute, length)) {
		payload_size = RTA_PAYLOAD (attribute);
		log_debug (LOG_NETLINK, "     attribute payload_size == {}", payload_size);
		sa = NULL;

		switch (attribute->rta_type) {
			case IFA_LABEL: {
				size_t room_for_trailing_null = 0uz;

				log_debug (LOG_NETLINK, "     attribute type: LABEL"sv);
				if (payload_size > MAX_IFA_LABEL_SIZE) {
					payload_size = MAX_IFA_LABEL_SIZE;
					room_for_trailing_null = 1;
				}

				if (payload_size > 0) {
					size_t alloc_size = Helpers::add_with_overflow_check<size_t> (payload_size, room_for_trailing_null);
					ifa->ifa_name = (char*)malloc (alloc_size);
					if (!ifa->ifa_name) {
						goto error;
					}

					memcpy (ifa->ifa_name, RTA_DATA (attribute), payload_size);
					if (room_for_trailing_null)
						ifa->ifa_name [payload_size] = '\0';
				}
				break;
			}

			case IFA_LOCAL:
				log_debug (LOG_NETLINK, "     attribute type: LOCAL"sv);
				if (ifa->ifa_addr) {
					/* P2P protocol, set the dst/broadcast address union from the original address.
					 * Since ifa_addr is set it means IFA_ADDRESS occured earlier and that address
					 * is indeed the P2P destination one.
					 */
					ifa->ifa_dstaddr = ifa->ifa_addr;
					ifa->ifa_addr = 0;
				}
				sa = &ifa->ifa_addr;
				break;

			case IFA_BROADCAST:
				log_debug (LOG_NETLINK, "     attribute type: BROADCAST"sv);
				if (ifa->ifa_dstaddr) {
					/* IFA_LOCAL happened earlier, undo its effect here */
					free (ifa->ifa_dstaddr);
					ifa->ifa_dstaddr = NULL;
				}
				sa = &ifa->ifa_broadaddr;
				break;

			case IFA_ADDRESS:
				log_debug (LOG_NETLINK, "     attribute type: ADDRESS"sv);
				if (ifa->ifa_addr) {
					/* Apparently IFA_LOCAL occured earlier and we have a P2P connection
					 * here. IFA_LOCAL carries the destination address, move it there
					 */
					ifa->ifa_dstaddr = ifa->ifa_addr;
					ifa->ifa_addr = NULL;
				}
				sa = &ifa->ifa_addr;
				break;

			case IFA_UNSPEC:
				log_debug (LOG_NETLINK, "     attribute type: UNSPEC"sv);
				break;

			case IFA_ANYCAST:
				log_debug (LOG_NETLINK, "     attribute type: ANYCAST"sv);
				break;

			case IFA_CACHEINFO:
				log_debug (LOG_NETLINK, "     attribute type: CACHEINFO"sv);
				break;

			case IFA_MULTICAST:
				log_debug (LOG_NETLINK, "     attribute type: MULTICAST"sv);
				break;

			default:
				log_debug (LOG_NETLINK, "     attribute type: {}", attribute->rta_type);
				break;
		}

		if (sa) {
			if (fill_sa_address (sa, net_address, RTA_DATA (attribute), RTA_PAYLOAD (attribute)) < 0) {
				goto error;
			}
		}

		attribute = RTA_NEXT (attribute, length);
	}

	/* glibc stores the associated interface name in the address if IFA_LABEL never occured */
	if (!ifa->ifa_name) {
		char *name = get_interface_name_by_index (static_cast<int>(net_address->ifa_index), ifaddrs_head);
		log_debug (LOG_NETLINK, "   address has no name/label, getting one from interface"sv);
		ifa->ifa_name = name ? strdup (name) : NULL;
	}
	log_debug (LOG_NETLINK, "   address label: {}", optional_string (ifa->ifa_name));

	if (calculate_address_netmask (ifa, net_address) < 0) {
		goto error;
	}

	return ifa;

  error:
	{
		/* errno may be modified by free, or any other call inside the free_single_xamarin_ifaddrs
		 * function. We don't care about errors in there since it is more important to know how we
		 * failed to obtain the link address and not that we went OOM. Save and restore the value
		 * after the resources are freed.
		 */
		int errno_save = errno;
		free_single_xamarin_ifaddrs (&ifa);
		errno = errno_save;
		return NULL;
	}

}

static struct ifaddrs *
get_link_info (const struct nlmsghdr *message)
{
	ssize_t length;
	struct rtattr *attribute;
	struct ifinfomsg *net_interface;
	struct ifaddrs *ifa = NULL;
	struct sockaddr_ll_extended *sa = NULL;

	abort_if_invalid_pointer_argument (message, "message");
	net_interface = reinterpret_cast <ifinfomsg*> (NLMSG_DATA (message));
	length = static_cast<ssize_t>(message->nlmsg_len - NLMSG_LENGTH (sizeof (*net_interface)));
	if (length <= 0) {
		goto error;
	}

	ifa = reinterpret_cast<ifaddrs*> (calloc (1, sizeof (*ifa)));
	if (!ifa) {
		goto error;
	}

	ifa->ifa_flags = net_interface->ifi_flags;
	attribute = IFLA_RTA (net_interface);
	while (RTA_OK (attribute, length)) {
		switch (attribute->rta_type) {
			case IFLA_IFNAME:
				ifa->ifa_name = strdup (reinterpret_cast<const char*> (RTA_DATA (attribute)));
				if (!ifa->ifa_name) {
					goto error;
				}
				if (Util::should_log (LOG_NETLINK)) {
					log_debug_nocheck_fmt (LOG_NETLINK, "   interface name (payload length: {}; string length: {})", RTA_PAYLOAD (attribute), strlen (ifa->ifa_name));
					log_debug_nocheck_fmt (LOG_NETLINK, "     {}", optional_string (ifa->ifa_name));
				}
				break;

			case IFLA_BROADCAST:
				log_debug (LOG_NETLINK, "   interface broadcast ({} bytes)", RTA_PAYLOAD (attribute));
				if (fill_ll_address (&sa, net_interface, RTA_DATA (attribute), RTA_PAYLOAD (attribute)) < 0) {
					goto error;
				}
				ifa->ifa_broadaddr = (struct sockaddr*)sa;
				break;

			case IFLA_ADDRESS:
				log_debug (LOG_NETLINK, "   interface address ({} bytes)", RTA_PAYLOAD (attribute));
				if (fill_ll_address (&sa, net_interface, RTA_DATA (attribute), RTA_PAYLOAD (attribute)) < 0) {
					goto error;
				}
				ifa->ifa_addr = (struct sockaddr*)sa;
				break;

			default:
#if DEBUG
				log_debug (LOG_NETLINK, "     rta_type: "sv);
				print_ifla_name (attribute->rta_type);
#endif // DEBUG
				break;
		}

		attribute = RTA_NEXT (attribute, length);
	}
	log_debug (LOG_NETLINK, "link flags: {:X}", ifa->ifa_flags);
	return ifa;

  error:
	if (sa)
		free (sa);
	free_single_xamarin_ifaddrs (&ifa);

	return NULL;
}
#else
void
_monodroid_getifaddrs_init (void)
{

}

int
_monodroid_getifaddrs (struct ifaddrs **ifap)
{
  *ifap = NULL;
  return 0;
}

void _monodroid_freeifaddrs (struct ifaddrs *ifa)
{

}
#endif

#if DEBUG
#define ENUM_VALUE_ENTRY(enumvalue) { enumvalue, #enumvalue }
struct enumvalue
{
	int         value;
	const char *name;
};

struct enumvalue iflas[] = {
	ENUM_VALUE_ENTRY (IFLA_UNSPEC),
	ENUM_VALUE_ENTRY (IFLA_ADDRESS),
	ENUM_VALUE_ENTRY (IFLA_BROADCAST),
	ENUM_VALUE_ENTRY (IFLA_IFNAME),
	ENUM_VALUE_ENTRY (IFLA_MTU),
	ENUM_VALUE_ENTRY (IFLA_LINK),
	ENUM_VALUE_ENTRY (IFLA_QDISC),
	ENUM_VALUE_ENTRY (IFLA_STATS),
	ENUM_VALUE_ENTRY (IFLA_COST),
	ENUM_VALUE_ENTRY (IFLA_PRIORITY),
	ENUM_VALUE_ENTRY (IFLA_MASTER),
	ENUM_VALUE_ENTRY (IFLA_WIRELESS),
	ENUM_VALUE_ENTRY (IFLA_PROTINFO),
	ENUM_VALUE_ENTRY (IFLA_TXQLEN),
	ENUM_VALUE_ENTRY (IFLA_MAP),
	ENUM_VALUE_ENTRY (IFLA_WEIGHT),
	ENUM_VALUE_ENTRY (IFLA_OPERSTATE),
	ENUM_VALUE_ENTRY (IFLA_LINKMODE),
	ENUM_VALUE_ENTRY (IFLA_LINKINFO),
	ENUM_VALUE_ENTRY (IFLA_NET_NS_PID),
	ENUM_VALUE_ENTRY (IFLA_IFALIAS),
	ENUM_VALUE_ENTRY (IFLA_NUM_VF),
	ENUM_VALUE_ENTRY (IFLA_VFINFO_LIST),
	ENUM_VALUE_ENTRY (IFLA_STATS64),
	ENUM_VALUE_ENTRY (IFLA_VF_PORTS),
	ENUM_VALUE_ENTRY (IFLA_PORT_SELF),
	ENUM_VALUE_ENTRY (IFLA_AF_SPEC),
	ENUM_VALUE_ENTRY (IFLA_GROUP),
	ENUM_VALUE_ENTRY (IFLA_NET_NS_FD),
	ENUM_VALUE_ENTRY (IFLA_EXT_MASK),
	ENUM_VALUE_ENTRY (IFLA_PROMISCUITY),
	ENUM_VALUE_ENTRY (IFLA_NUM_TX_QUEUES),
	ENUM_VALUE_ENTRY (IFLA_NUM_RX_QUEUES),
	ENUM_VALUE_ENTRY (IFLA_CARRIER),
	ENUM_VALUE_ENTRY (IFLA_PHYS_PORT_ID),
	ENUM_VALUE_ENTRY (IFLA_CARRIER_CHANGES),
	ENUM_VALUE_ENTRY (IFLA_PHYS_SWITCH_ID),
	ENUM_VALUE_ENTRY (IFLA_LINK_NETNSID),
	ENUM_VALUE_ENTRY (IFLA_PHYS_PORT_NAME),
	ENUM_VALUE_ENTRY (IFLA_PROTO_DOWN),
	ENUM_VALUE_ENTRY (IFLA_GSO_MAX_SEGS),
	ENUM_VALUE_ENTRY (IFLA_GSO_MAX_SIZE),
	ENUM_VALUE_ENTRY (IFLA_PAD),
	ENUM_VALUE_ENTRY (IFLA_XDP),
	{ -1, 0 }
};

static void
print_ifla_name (int id)
{
	if (!Util::should_log (LOG_NETLINK))
		return;

	int i = 0;
	while (1) {
		if (iflas [i].value == -1 && iflas [i].name == 0) {
			log_info_nocheck_fmt (LOG_NETLINK, "Unknown ifla->name: unknown id {}", id);
			break;
		}

		if (iflas [i].value != id) {
			i++;
			continue;
		}
		log_info_nocheck_fmt (LOG_NETLINK, "ifla->name: {} ({})", optional_string (iflas [i].name), iflas [i].value);
		break;
	}
}

static void
print_address_list (const char title[], struct ifaddrs *list)
{
	if (!Util::should_log (LOG_NETLINK))
		return;

	struct ifaddrs *cur;
	char *msg, *tmp;

	if (!list) {
		log_info_nocheck_fmt (LOG_NETLINK, "No list to print in {}", __FUNCTION__);
		return;
	}

	cur = list;
	msg = NULL;
	while (cur) {
		tmp = NULL;
		asprintf (&tmp, "%s%s%p (%s; %p)", msg ? msg : "", msg ? " -> " : "", cur, cur->ifa_name, cur->ifa_name);
		if (msg)
			free (msg);
		msg = tmp;
		cur = cur->ifa_next;
	}

	log_info_nocheck_fmt (LOG_NETLINK, "{}: {}", title, optional_string (msg, "[no addresses]"));
	free (msg);
}
#endif
