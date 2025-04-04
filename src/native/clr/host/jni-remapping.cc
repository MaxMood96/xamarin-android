#include <cstring>

#include <runtime-base/logger.hh>
#include <runtime-base/jni-remapping.hh>

#include "xamarin-app.hh"

using namespace xamarin::android;

[[gnu::always_inline]]
auto JniRemapping::equal (JniRemappingString const& left, const char *right, size_t right_len) noexcept -> bool
{
	if (left.length != static_cast<uint32_t>(right_len) || left.str[0] != *right) {
		return false;
	}

	if (memcmp (left.str, right, right_len) == 0) {
		return true;
	}

	return false;
}

auto JniRemapping::lookup_replacement_type (const char *jniSimpleReference) noexcept -> const char*
{
	if (application_config.jni_remapping_replacement_type_count == 0 || jniSimpleReference == nullptr || *jniSimpleReference == '\0') {
		return nullptr;
	}

	size_t ref_len = strlen (jniSimpleReference);
	for (size_t i = 0uz; i < application_config.jni_remapping_replacement_type_count; i++) {
		JniRemappingTypeReplacementEntry const& entry = jni_remapping_type_replacements[i];

		if (equal (entry.name, jniSimpleReference, ref_len)) {
			return entry.replacement;
		}
	}

	return nullptr;
}

auto JniRemapping::lookup_replacement_method_info (const char *jniSourceType, const char *jniMethodName, const char *jniMethodSignature) noexcept -> const JniRemappingReplacementMethod*
{
	if (application_config.jni_remapping_replacement_method_index_entry_count == 0 ||
	    jniSourceType == nullptr || *jniSourceType == '\0' ||
	    jniMethodName == nullptr || *jniMethodName == '\0') {
		return nullptr;
	}

	size_t source_type_len = strlen (jniSourceType);

	const JniRemappingIndexTypeEntry *type = nullptr;
	for (size_t i = 0uz; i < application_config.jni_remapping_replacement_method_index_entry_count; i++) {
		JniRemappingIndexTypeEntry const& entry = jni_remapping_method_replacement_index[i];

		if (!equal (entry.name, jniSourceType, source_type_len)) {
			continue;
		}

		type = &jni_remapping_method_replacement_index[i];
		break;
	}

	if (type == nullptr || type->method_count == 0 || type->methods == nullptr) {
		return nullptr;
	}

	size_t method_name_len = strlen (jniMethodName);
	size_t signature_len = jniMethodSignature == nullptr ? 0uz : strlen (jniMethodSignature);

	for (size_t i = 0uz; i < type->method_count; i++) {
		JniRemappingIndexMethodEntry const& entry = type->methods[i];

		if (!equal (entry.name, jniMethodName, method_name_len)) {
			continue;
		}

		if (entry.signature.length == 0 || equal (entry.signature, jniMethodSignature, signature_len)) {
			return &type->methods[i].replacement;
		}

		const char *sig_end = jniMethodSignature + signature_len;
		if (*sig_end == ')') {
			continue;
		}

		while (sig_end != jniMethodSignature && *sig_end != ')') {
			sig_end--;
		}

		if (equal (entry.signature, jniMethodSignature, static_cast<size_t>(sig_end - jniMethodSignature) + 1uz)) {
			return &type->methods[i].replacement;
		}
	}

	return nullptr;
}
