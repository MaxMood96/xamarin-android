set(LIB_NAME runtime-base-common)
set(LIB_ALIAS xa::runtime-base-common)

set(XA_RUNTIME_BASE_SOURCES
  timing-internal.cc
)
add_clang_check_sources("${XA_RUNTIME_BASE_SOURCES}")

list(APPEND POTENTIAL_LOCAL_COMPILER_ARGS
  -ffunction-sections
  -fdata-sections
)

xa_check_c_args(RUNTIME_BASE_CXX_ARGS "${POTENTIAL_LOCAL_COMPILER_ARGS}")

add_library(
  ${LIB_NAME}
  STATIC
  ${XA_RUNTIME_BASE_SOURCES}
)

add_library(${LIB_ALIAS} ALIAS ${LIB_NAME})

set_static_library_suffix(${LIB_NAME})

target_compile_options(
  ${LIB_NAME}
  PRIVATE
  ${XA_COMMON_CXX_ARGS}
  ${RUNTIME_BASE_CXX_ARGS}
)

target_include_directories(
  ${LIB_NAME}
  SYSTEM PRIVATE
  ${SYSROOT_CXX_INCLUDE_DIR}
)

target_link_libraries(
  ${LIB_NAME}
  PRIVATE
  xa::shared
  xa::xamarin-app
  xa::runtime-base
)

xa_add_compile_definitions(${LIB_NAME})
xa_add_include_directories(${LIB_NAME})
