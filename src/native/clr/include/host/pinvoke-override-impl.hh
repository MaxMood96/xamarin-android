#pragma once

#if !defined(PINVOKE_OVERRIDE_INLINE)
#error The PINVOKE_OVERRIDE_INLINE macro must be defined before including this header file
#endif

#include <mutex>
#include <string>

#include "pinvoke-override.hh"
#include "../runtime-base/logger.hh"
#include "../runtime-base/monodroid-dl.hh"
#include "../runtime-base/startup-aware-lock.hh"

namespace xamarin::android {
	PINVOKE_OVERRIDE_INLINE
	auto PinvokeOverride::load_library_symbol (std::string_view const& library_name, std::string_view const& symbol_name, void **dso_handle) noexcept -> void*
	{
		void *lib_handle = dso_handle == nullptr ? nullptr : *dso_handle;

		if (lib_handle == nullptr) {
			// We're being called as part of the p/invoke mechanism, we don't need to look in the AOT cache
			constexpr bool PREFER_AOT_CACHE = false;

			// Handle p/invokes of the form [DllImport ("liblog")] or [DllImport ("log")]
			// TODO: try modifying the name to contain both the `log` prefix and the `.so` suffix
			dynamic_local_path_string short_library_name;
			if (!Util::path_has_directory_components (library_name)) {
				if (!library_name.starts_with (Constants::DSO_PREFIX)) {
					short_library_name.append (Constants::DSO_PREFIX);
				}
				short_library_name.append (library_name);
				if (!short_library_name.ends_with (Constants::dso_suffix)) {
					short_library_name.append (Constants::dso_suffix);
				}

				log_debug (LOG_ASSEMBLY, "Modified p/invoke library name to '{}'", short_library_name.get ());
				lib_handle = MonodroidDl::monodroid_dlopen<PREFER_AOT_CACHE> (short_library_name.get (), microsoft::java_interop::JAVA_INTEROP_LIB_LOAD_LOCALLY);
			}

			if (lib_handle == nullptr) {
				lib_handle = MonodroidDl::monodroid_dlopen<PREFER_AOT_CACHE> (library_name, microsoft::java_interop::JAVA_INTEROP_LIB_LOAD_LOCALLY);
			}

			if (lib_handle == nullptr) {
				log_warn (LOG_ASSEMBLY, "Shared library '{}' not loaded, p/invoke '{}' may fail", library_name, symbol_name);
				return nullptr;
			}

			if (dso_handle != nullptr) {
				void *expected_null = nullptr;
				if (!__atomic_compare_exchange (dso_handle, &expected_null, &lib_handle, false /* weak */, __ATOMIC_ACQUIRE /* success_memorder */, __ATOMIC_RELAXED /* xxxfailure_memorder */)) {
					log_debug (LOG_ASSEMBLY, "Library '{}' handle already cached by another thread", library_name);
				}
			}
		}

		void *entry_handle = MonodroidDl::monodroid_dlsym (lib_handle, symbol_name);
		if (entry_handle == nullptr) {
			log_warn (LOG_ASSEMBLY, "Symbol '{}' not found in shared library '{}', p/invoke may fail", library_name, symbol_name);
			return nullptr;
		}

		return entry_handle;
	}

	PINVOKE_OVERRIDE_INLINE
	auto PinvokeOverride::load_library_entry (std::string const& library_name, std::string const& entrypoint_name, pinvoke_api_map_ptr api_map) noexcept -> void*
	{
		// Make sure some other thread hasn't just added the entry
		auto iter = api_map->find (entrypoint_name);
		if (iter != api_map->end () && iter->second != nullptr) {
			return iter->second;
		}

		void *entry_handle = load_library_symbol (library_name.c_str (), entrypoint_name.c_str ());
		if (entry_handle == nullptr) {
			// error already logged
			return nullptr;
		}

		log_debug (LOG_ASSEMBLY, "Caching p/invoke entry {} @ {}", library_name, entrypoint_name);
		(*api_map)[entrypoint_name] = entry_handle;
		return entry_handle;
	}

	PINVOKE_OVERRIDE_INLINE
	void PinvokeOverride::load_library_entry (std::string_view const& library_name, std::string_view const& entrypoint_name, PinvokeEntry &entry, void **dso_handle) noexcept
	{
		void *entry_handle = load_library_symbol (library_name, entrypoint_name, dso_handle);
		void *expected_null = nullptr;

		bool already_loaded = !__atomic_compare_exchange (
			/* ptr */              &entry.func,
			/* expected */         &expected_null,
			/* desired */          &entry_handle,
			/* weak */              false,
			/* success_memorder */  __ATOMIC_ACQUIRE,
			/* failure_memorder */  __ATOMIC_RELAXED
		);

		if (already_loaded) {
			log_debug (LOG_ASSEMBLY, "Entry '{}' from library '{}' already loaded by another thread", entrypoint_name, library_name);
		}
	}

	PINVOKE_OVERRIDE_INLINE
	auto PinvokeOverride::fetch_or_create_pinvoke_map_entry (std::string const& library_name, std::string const& entrypoint_name, hash_t entrypoint_name_hash, pinvoke_api_map_ptr api_map, bool need_lock) noexcept -> void*
	{
		auto iter = api_map->find (entrypoint_name, entrypoint_name_hash);
		if (iter != api_map->end () && iter->second != nullptr) {
			return iter->second;
		}

		if (!need_lock) {
			return load_library_entry (library_name, entrypoint_name, api_map);
		}

		StartupAwareLock lock (pinvoke_map_write_lock);
		return load_library_entry (library_name, entrypoint_name, api_map);
	}

	PINVOKE_OVERRIDE_INLINE
	auto PinvokeOverride::find_pinvoke_address (hash_t hash, const PinvokeEntry *entries, size_t entry_count) noexcept -> PinvokeEntry*
	{
		while (entry_count > 0uz) {
			const size_t mid = entry_count / 2uz;
			const PinvokeEntry *const ret = entries + mid;
			const std::strong_ordering result = hash <=> ret->hash;

			if (result < 0) {
				entry_count = mid;
			} else if (result > 0) {
				entries = ret + 1;
				entry_count -= mid + 1uz;
			} else {
				return const_cast<PinvokeEntry*>(ret);
			}
		}

		return nullptr;
	}

	PINVOKE_OVERRIDE_INLINE
	auto PinvokeOverride::handle_other_pinvoke_request (std::string_view const& library_name, hash_t library_name_hash, std::string_view const& entrypoint_name, hash_t entrypoint_name_hash) noexcept -> void*
	{
		std::string lib_name {library_name};
		std::string entry_name {entrypoint_name};

		auto iter = other_pinvoke_map.find (lib_name, library_name_hash);
		void *handle = nullptr;
		if (iter == other_pinvoke_map.end ()) {
			StartupAwareLock lock (pinvoke_map_write_lock);

			pinvoke_api_map_ptr lib_map;
			// Make sure some other thread hasn't just added the map
			iter = other_pinvoke_map.find (lib_name, library_name_hash);
			if (iter == other_pinvoke_map.end () || iter->second == nullptr) {
				lib_map = new pinvoke_api_map (1);
				other_pinvoke_map[lib_name] = lib_map;
			} else {
				lib_map = iter->second;
			}

			handle = fetch_or_create_pinvoke_map_entry (lib_name, entry_name, entrypoint_name_hash, lib_map, /* need_lock */ false);
		} else {
			if (iter->second == nullptr) [[unlikely]] {
				log_warn (LOG_ASSEMBLY, "Internal error: null entry in p/invoke map for key '{}'", library_name);
				return nullptr; // fall back to `monodroid_dlopen`
			}

			handle = fetch_or_create_pinvoke_map_entry (lib_name, entry_name, entrypoint_name_hash, iter->second, /* need_lock */ true);
		}

		return handle;
	}
}
