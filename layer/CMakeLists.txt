add_library(metalquestlink_protocol STATIC
  src/protocol.cpp
)

add_library(MetalQuestLink::Protocol ALIAS metalquestlink_protocol)

target_compile_features(metalquestlink_protocol PUBLIC cxx_std_20)
target_include_directories(metalquestlink_protocol
  PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

if(CMAKE_CXX_COMPILER_ID MATCHES "AppleClang|Clang|GNU")
  target_compile_options(metalquestlink_protocol PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

if(BUILD_TESTING)
  add_executable(metalquestlink_protocol_tests tests/protocol_tests.cpp)
  target_link_libraries(metalquestlink_protocol_tests PRIVATE MetalQuestLink::Protocol)
  target_compile_features(metalquestlink_protocol_tests PRIVATE cxx_std_20)
  if(CMAKE_CXX_COMPILER_ID MATCHES "AppleClang|Clang|GNU")
    target_compile_options(metalquestlink_protocol_tests PRIVATE -Wall -Wextra -Wpedantic -Werror)
  endif()
  add_test(NAME protocol COMMAND metalquestlink_protocol_tests)
endif()

if(METALQUESTLINK_BUILD_OPENXR)
  add_library(metalquestlink_openxr_layer MODULE
    src/input_injection.cpp
    src/openxr_layer.cpp
    src/streaming.mm
    src/transport.cpp
  )
  target_compile_features(metalquestlink_openxr_layer PRIVATE cxx_std_20)
  target_compile_definitions(metalquestlink_openxr_layer PRIVATE XR_USE_GRAPHICS_API_METAL)
  target_link_libraries(metalquestlink_openxr_layer PRIVATE
    OpenXR::headers
    MetalQuestLink::Protocol
    "-framework CoreMedia"
    "-framework CoreVideo"
    "-framework Foundation"
    "-framework Metal"
    "-framework VideoToolbox"
  )
  target_compile_options(metalquestlink_openxr_layer PRIVATE
    -Wall -Wextra -Wpedantic -Werror -fvisibility=hidden -fobjc-arc
  )
  set_target_properties(metalquestlink_openxr_layer PROPERTIES
    PREFIX ""
    OUTPUT_NAME "libmetalquestlink_openxr_layer"
    LINK_FLAGS "-Wl,-exported_symbols_list,${CMAKE_CURRENT_SOURCE_DIR}/src/openxr_layer.expsym"
  )

  add_executable(metalquestlink_native_test
    native-test/hello_xr_metal.mm
  )
  target_compile_features(metalquestlink_native_test PRIVATE cxx_std_20)
  target_compile_definitions(metalquestlink_native_test PRIVATE XR_USE_GRAPHICS_API_METAL)
  target_compile_options(metalquestlink_native_test PRIVATE
    -Wall -Wextra -Wpedantic -Werror -fobjc-arc
  )
  target_link_libraries(metalquestlink_native_test PRIVATE
    OpenXR::openxr_loader
    "-framework Foundation"
    "-framework Metal"
  )

  add_executable(metalquestlink_mock_viewer
    tools/mock_viewer.mm
  )
  target_compile_features(metalquestlink_mock_viewer PRIVATE cxx_std_20)
  target_compile_options(metalquestlink_mock_viewer PRIVATE
    -Wall -Wextra -Wpedantic -Werror -fobjc-arc
  )
  target_link_libraries(metalquestlink_mock_viewer PRIVATE
    MetalQuestLink::Protocol
    "-framework CoreMedia"
    "-framework CoreVideo"
    "-framework Foundation"
    "-framework VideoToolbox"
  )

  set(METALQUESTLINK_LAYER_MANIFEST_DIR
    "${CMAKE_BINARY_DIR}/openxr/1/api_layers/implicit.d"
  )
  file(MAKE_DIRECTORY "${METALQUESTLINK_LAYER_MANIFEST_DIR}")
  file(GENERATE
    OUTPUT "${METALQUESTLINK_LAYER_MANIFEST_DIR}/XrApiLayer_metalquestlink.json"
    CONTENT [=[{
  "file_format_version": "1.0.0",
  "api_layer": {
    "name": "XR_APILAYER_METALQUESTLINK_streaming",
    "library_path": "$<TARGET_FILE:metalquestlink_openxr_layer>",
    "api_version": "1.0",
    "implementation_version": "1",
    "description": "MetalQuestLink OpenXR pass-through streaming layer",
    "instance_extensions": [
      {
        "name": "XR_EXT_hand_tracking",
        "extension_version": "4"
      }
    ],
    "enable_environment": "METALQUESTLINK_ENABLE_API_LAYER",
    "disable_environment": "METALQUESTLINK_DISABLE_API_LAYER"
  }
}
]=]
  )
endif()
