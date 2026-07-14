cmake_minimum_required(VERSION 3.25)

project(MaQuestLink VERSION 0.1.0 LANGUAGES CXX)

if(APPLE)
  set(CMAKE_OSX_ARCHITECTURES "arm64" CACHE STRING "Build architectures" FORCE)
endif()

include(CTest)
add_subdirectory(layer)
