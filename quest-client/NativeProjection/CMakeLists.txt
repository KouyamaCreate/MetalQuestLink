cmake_minimum_required(VERSION 3.22)

project(MaQuestLinkProjection LANGUAGES CXX)

if(NOT ANDROID)
  message(FATAL_ERROR "MaQuestLinkProjection is an Android-only library")
endif()
if(NOT OPENXR_INCLUDE_DIR OR NOT EXISTS "${OPENXR_INCLUDE_DIR}/openxr/openxr.h")
  message(FATAL_ERROR "OPENXR_INCLUDE_DIR must point to the OpenXR include directory")
endif()

add_library(maquestlink_projection SHARED projection_layer.cpp)
target_compile_features(maquestlink_projection PRIVATE cxx_std_20)
target_compile_definitions(maquestlink_projection PRIVATE XR_USE_PLATFORM_ANDROID)
target_include_directories(maquestlink_projection PRIVATE "${OPENXR_INCLUDE_DIR}")
target_compile_options(maquestlink_projection PRIVATE -Wall -Wextra -Wpedantic -Werror)
target_link_options(maquestlink_projection PRIVATE
  "LINKER:-z,max-page-size=16384"
  "LINKER:-z,common-page-size=16384")
target_link_libraries(maquestlink_projection PRIVATE android log)
