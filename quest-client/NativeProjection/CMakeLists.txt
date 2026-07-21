cmake_minimum_required(VERSION 3.22)

project(MetalQuestLinkProjection LANGUAGES CXX)

if(NOT ANDROID)
  message(FATAL_ERROR "MetalQuestLinkProjection is an Android-only library")
endif()
if(NOT OPENXR_INCLUDE_DIR OR NOT EXISTS "${OPENXR_INCLUDE_DIR}/openxr/openxr.h")
  message(FATAL_ERROR "OPENXR_INCLUDE_DIR must point to the OpenXR include directory")
endif()

add_library(metalquestlink_projection SHARED projection_layer.cpp)
target_compile_features(metalquestlink_projection PRIVATE cxx_std_20)
target_compile_definitions(metalquestlink_projection PRIVATE XR_USE_PLATFORM_ANDROID)
target_include_directories(metalquestlink_projection PRIVATE "${OPENXR_INCLUDE_DIR}")
target_compile_options(metalquestlink_projection PRIVATE -Wall -Wextra -Wpedantic -Werror)
target_link_options(metalquestlink_projection PRIVATE
  "LINKER:-z,max-page-size=16384"
  "LINKER:-z,common-page-size=16384")
target_link_libraries(metalquestlink_projection PRIVATE android log)
