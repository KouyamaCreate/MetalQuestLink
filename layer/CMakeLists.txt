add_library(maquestlink_protocol STATIC
  src/protocol.cpp
)

add_library(MaQuestLink::Protocol ALIAS maquestlink_protocol)

target_compile_features(maquestlink_protocol PUBLIC cxx_std_20)
target_include_directories(maquestlink_protocol
  PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

if(CMAKE_CXX_COMPILER_ID MATCHES "AppleClang|Clang|GNU")
  target_compile_options(maquestlink_protocol PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

if(BUILD_TESTING)
  add_executable(maquestlink_protocol_tests tests/protocol_tests.cpp)
  target_link_libraries(maquestlink_protocol_tests PRIVATE MaQuestLink::Protocol)
  target_compile_features(maquestlink_protocol_tests PRIVATE cxx_std_20)
  if(CMAKE_CXX_COMPILER_ID MATCHES "AppleClang|Clang|GNU")
    target_compile_options(maquestlink_protocol_tests PRIVATE -Wall -Wextra -Wpedantic -Werror)
  endif()
  add_test(NAME protocol COMMAND maquestlink_protocol_tests)
endif()

if(MAQUESTLINK_BUILD_OPENXR)
  add_library(maquestlink_openxr_layer MODULE
    src/openxr_layer.cpp
    src/streaming.mm
  )
  target_compile_features(maquestlink_openxr_layer PRIVATE cxx_std_20)
  target_compile_definitions(maquestlink_openxr_layer PRIVATE XR_USE_GRAPHICS_API_METAL)
  target_link_libraries(maquestlink_openxr_layer PRIVATE
    OpenXR::headers
    MaQuestLink::Protocol
    "-framework CoreMedia"
    "-framework CoreVideo"
    "-framework Foundation"
    "-framework Metal"
    "-framework VideoToolbox"
  )
  target_compile_options(maquestlink_openxr_layer PRIVATE
    -Wall -Wextra -Wpedantic -Werror -fvisibility=hidden -fobjc-arc
  )
  set_target_properties(maquestlink_openxr_layer PROPERTIES
    PREFIX ""
    OUTPUT_NAME "libmaquestlink_openxr_layer"
    LINK_FLAGS "-Wl,-exported_symbols_list,${CMAKE_CURRENT_SOURCE_DIR}/src/openxr_layer.expsym"
  )

  add_executable(maquestlink_native_test
    native-test/hello_xr_metal.mm
  )
  target_compile_features(maquestlink_native_test PRIVATE cxx_std_20)
  target_compile_definitions(maquestlink_native_test PRIVATE XR_USE_GRAPHICS_API_METAL)
  target_compile_options(maquestlink_native_test PRIVATE
    -Wall -Wextra -Wpedantic -Werror -fobjc-arc
  )
  target_link_libraries(maquestlink_native_test PRIVATE
    OpenXR::openxr_loader
    "-framework Foundation"
    "-framework Metal"
  )

  add_executable(maquestlink_mock_viewer
    tools/mock_viewer.mm
  )
  target_compile_features(maquestlink_mock_viewer PRIVATE cxx_std_20)
  target_compile_options(maquestlink_mock_viewer PRIVATE
    -Wall -Wextra -Wpedantic -Werror -fobjc-arc
  )
  target_link_libraries(maquestlink_mock_viewer PRIVATE
    MaQuestLink::Protocol
    "-framework CoreMedia"
    "-framework CoreVideo"
    "-framework Foundation"
    "-framework VideoToolbox"
  )

  set(MAQUESTLINK_LAYER_MANIFEST_DIR
    "${CMAKE_BINARY_DIR}/openxr/1/api_layers/implicit.d"
  )
  file(MAKE_DIRECTORY "${MAQUESTLINK_LAYER_MANIFEST_DIR}")
  file(GENERATE
    OUTPUT "${MAQUESTLINK_LAYER_MANIFEST_DIR}/XrApiLayer_maquestlink.json"
    CONTENT [=[{
  "file_format_version": "1.0.0",
  "api_layer": {
    "name": "XR_APILAYER_MAQUESTLINK_streaming",
    "library_path": "$<TARGET_FILE:maquestlink_openxr_layer>",
    "api_version": "1.0",
    "implementation_version": "1",
    "description": "MaQuestLink OpenXR pass-through streaming layer",
    "enable_environment": "MAQUESTLINK_ENABLE_API_LAYER",
    "disable_environment": "MAQUESTLINK_DISABLE_API_LAYER"
  }
}
]=]
  )
endif()
