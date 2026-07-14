cmake_minimum_required(VERSION 3.25)

project(MaQuestLink VERSION 0.1.0 LANGUAGES CXX OBJCXX)

if(APPLE)
  set(CMAKE_OSX_ARCHITECTURES "arm64" CACHE STRING "Build architectures" FORCE)
endif()

include(CTest)

option(MAQUESTLINK_BUILD_OPENXR "Build the OpenXR layer and native test client" ON)

if(MAQUESTLINK_BUILD_OPENXR)
  include(FetchContent)
  set(BUILD_API_LAYERS OFF CACHE BOOL "" FORCE)
  set(BUILD_TESTS OFF CACHE BOOL "" FORCE)
  set(BUILD_CONFORMANCE_TESTS OFF CACHE BOOL "" FORCE)
  set(BUILD_SDK_TESTS OFF CACHE BOOL "" FORCE)
  set(DYNAMIC_LOADER ON CACHE BOOL "" FORCE)
  FetchContent_Declare(openxr_sdk
    GIT_REPOSITORY https://github.com/KhronosGroup/OpenXR-SDK.git
    GIT_TAG 5267613edf3d937e3d77556a106a65c2f82b25c6
  )
  FetchContent_MakeAvailable(openxr_sdk)
endif()

add_subdirectory(layer)
