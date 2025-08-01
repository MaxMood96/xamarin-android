#include <cstdint>
#include <stdlib.h>

#include <xamarin-app.hh>
#include <shared/xxhash.hh>

// This file MUST have "valid" values everywhere - the DSO it is compiled into is loaded by the
// designer on desktop.
const uint64_t format_tag = FORMAT_TAG;

#if defined (DEBUG)
static TypeMapEntry java_to_managed[] = {};
static TypeMapEntry managed_to_java[] = {};

// MUST match src/Xamarin.Android.Build.Tasks/Utilities/TypeMappingDebugNativeAssemblyGenerator.cs
const TypeMap type_map = {
	.entry_count = 0,
	.unique_assemblies_count = 0,
	.java_to_managed = java_to_managed,
	.managed_to_java = managed_to_java,
};

const bool typemap_use_hashes = true;
const TypeMapManagedTypeInfo type_map_managed_type_info[] = {};
const TypeMapAssembly type_map_unique_assemblies[] = {};
const char type_map_assembly_names[] = {};
const char type_map_managed_type_names[] = {};
const char type_map_java_type_names[] = {};
#else
const uint32_t managed_to_java_map_module_count = 0;
const uint32_t java_type_count = 0;
const char java_type_names[] = {};
const uint64_t java_type_names_size = 0;
const char managed_type_names[] = {};
const char managed_assembly_names[] = {};
const TypeMapModule managed_to_java_map[] = {};
const TypeMapModuleEntry modules_map_data[] = {};
const TypeMapModuleEntry modules_duplicates_data[] = {};
const TypeMapJava java_to_managed_map[] = {};
const xamarin::android::hash_t java_to_managed_hashes[] = {};
#endif

uint32_t compressed_assembly_count = 0;
CompressedAssemblyDescriptor compressed_assembly_descriptors[] = {};
uint32_t uncompressed_assemblies_data_size = 0;
uint8_t uncompressed_assemblies_data_buffer[] = {};

//
// Config settings below **must** be valid for Desktop builds as the default `libxamarin-app.{dll,dylib,so}` is used by
// the Designer
//
constexpr char android_package_name[] = "com.xamarin.test";
const ApplicationConfig application_config = {
	.uses_assembly_preload = false,
	.jni_add_native_method_registration_attribute_present = false,
	.marshal_methods_enabled = false,
	.ignore_split_configs = false,
	.number_of_runtime_properties = 3,
	.package_naming_policy = 0,
	.environment_variable_count = 0,
	.system_property_count = 0,
	.number_of_assemblies_in_apk = 2,
	.bundled_assembly_name_width = 0,
	.number_of_dso_cache_entries = 2,
	.number_of_aot_cache_entries = 2,
	.number_of_shared_libraries = 2,
	.android_runtime_jnienv_class_token = 1,
	.jnienv_initialize_method_token = 2,
	.jnienv_registerjninatives_method_token = 3,
	.jni_remapping_replacement_type_count = 2,
	.jni_remapping_replacement_method_index_entry_count = 2,
	.android_package_name = android_package_name,
	.managed_marshal_methods_lookup_enabled = false,
};

// TODO: migrate to std::string_view for these two
const AppEnvironmentVariable app_environment_variables[] = {};
const char app_environment_variable_contents[] = {};
const char* const app_system_properties[] = {};




AssemblyStoreSingleAssemblyRuntimeData assembly_store_bundled_assemblies[] = {
	{
		.image_data = nullptr,
		.debug_info_data = nullptr,
		.config_data = nullptr,
		.descriptor = nullptr,
	},

	{
		.image_data = nullptr,
		.debug_info_data = nullptr,
		.config_data = nullptr,
		.descriptor = nullptr,
	},
};

AssemblyStoreRuntimeData assembly_store = {
	.data_start = nullptr,
	.assembly_count = 0,
	.index_entry_count = 0,
	.assemblies = nullptr,
};

constexpr char fake_dso_name[] = "libaot-Some.Assembly.dll.so";
constexpr char fake_dso_name2[] = "libaot-Another.Assembly.dll.so";

DSOCacheEntry dso_cache[] = {
	{
		.hash = xamarin::android::xxhash::hash (fake_dso_name, sizeof(fake_dso_name) - 1),
		.real_name_hash = xamarin::android::xxhash::hash (fake_dso_name, sizeof(fake_dso_name) - 1),
		.ignore = true,
		.name_index = 1,
		.handle = nullptr,
	},

	{
		.hash = xamarin::android::xxhash::hash (fake_dso_name2, sizeof(fake_dso_name2) - 1),
		.real_name_hash = xamarin::android::xxhash::hash (fake_dso_name2, sizeof(fake_dso_name2) - 1),
		.ignore = true,
		.name_index = 2,
		.handle = nullptr,
	},
};

DSOCacheEntry aot_dso_cache[] = {
	{
		.hash = xamarin::android::xxhash::hash (fake_dso_name, sizeof(fake_dso_name) - 1),
		.real_name_hash = xamarin::android::xxhash::hash (fake_dso_name, sizeof(fake_dso_name) - 1),
		.ignore = true,
		.name_index = 3,
		.handle = nullptr,
	},

	{
		.hash = xamarin::android::xxhash::hash (fake_dso_name2, sizeof(fake_dso_name2) - 1),
		.real_name_hash = xamarin::android::xxhash::hash (fake_dso_name2, sizeof(fake_dso_name2) - 1),
		.ignore = true,
		.name_index = 4,
		.handle = nullptr,
	},
};

const char dso_names_data[] = {};

DSOApkEntry dso_apk_entries[2] {};

//
// Support for marshal methods
//
void xamarin_app_init ([[maybe_unused]] JNIEnv *env, [[maybe_unused]] get_function_pointer_fn fn) noexcept
{
	// Dummy
}

static const JniRemappingIndexMethodEntry some_java_type_one_methods[] = {
	{
		.name = {
			.length = 15,
			.str = "old_method_name",
		},

		.signature = {
			.length = 0,
			.str = nullptr,
		},

		.replacement = {
			.target_type = "some/java/target_type_one",
			.target_name = "new_method_name",
			.is_static = false,
		}
	},
};

static const JniRemappingIndexMethodEntry some_java_type_two_methods[] = {
	{
		.name = {
			.length = 15,
			.str = "old_method_name",
		},

		.signature = {
			.length = 28,
			.str = "(IILandroid/content/Intent;)",
		},

		.replacement = {
			.target_type = "some/java/target_type_two",
			.target_name = "new_method_name",
			.is_static = true,
		}
	},
};

const JniRemappingIndexTypeEntry jni_remapping_method_replacement_index[] = {
	{
		.name = {
			.length = 18,
			.str = "some/java/type_one",
		},
		.method_count = 1,
		.methods = some_java_type_one_methods,
	},

	{
		.name = {
			.length = 18,
			.str = "some/java/type_two",
		},
		.method_count = 1,
		.methods = some_java_type_two_methods,
	},
};

const JniRemappingTypeReplacementEntry jni_remapping_type_replacements[] = {
	{
		.name = {
			.length = 14,
			.str = "some/java/type",
		},
		.replacement = "another/java/type",
	},

	{
		.name = {
			.length = 20,
			.str = "some/other/java/type",
		},
		.replacement = "another/replacement/java/type",
	},
};

constexpr char prop_test_string_key[] = "test_string";
constexpr char prop_test_integer_key[] = "test_integer";
constexpr char prop_test_boolean_key[] = "test_boolean";

const RuntimeProperty runtime_properties[] = {
	{
		.key_index = 0,
		.value_index = 10,
		.value_size = 10,
	},

	{
		.key_index = 20,
		.value_index = 25,
		.value_size = 5,
	},

	{
		.key_index = 30,
		.value_index = 33,
		.value_size = 7,
	},
};


const char runtime_properties_data[] = {};

const RuntimePropertyIndexEntry runtime_property_index[] = {
	{
		.key_hash = xamarin::android::xxhash::hash (prop_test_string_key, sizeof(prop_test_string_key) - 1),
		.index = 0,
	},

	{
		.key_hash = xamarin::android::xxhash::hash (prop_test_integer_key, sizeof(prop_test_integer_key) - 1),
		.index = 1,
	},

	{
		.key_hash = xamarin::android::xxhash::hash (prop_test_boolean_key, sizeof(prop_test_boolean_key) - 1),
		.index = 2,
	},
};

const char *init_runtime_property_names[] = {
	"HOST_RUNTIME_CONTRACT",
};

char *init_runtime_property_values[] {
	nullptr,
};
