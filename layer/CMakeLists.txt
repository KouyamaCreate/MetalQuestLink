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
