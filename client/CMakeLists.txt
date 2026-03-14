cmake_minimum_required(VERSION 3.20)
project(safe_terminal_agent VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# ─── 依赖查找 ───────────────────────────────────────────────────────────────
find_package(Threads REQUIRED)

# gRPC & Protobuf（通过 vcpkg 或系统包管理器安装）
find_package(gRPC CONFIG REQUIRED)
find_package(Protobuf REQUIRED)

# spdlog（日志）
find_package(spdlog REQUIRED)

# toml++ （配置解析，header-only）
# 若未安装可用 FetchContent 下载
find_package(tomlplusplus QUIET)
if(NOT tomlplusplus_FOUND)
  include(FetchContent)
  FetchContent_Declare(
    tomlplusplus
    GIT_REPOSITORY https://github.com/marzer/tomlplusplus.git
    GIT_TAG        v3.4.0
  )
  FetchContent_MakeAvailable(tomlplusplus)
endif()

# ─── Protobuf / gRPC 代码生成 ───────────────────────────────────────────────
set(PROTO_DIR "${CMAKE_SOURCE_DIR}/../proto")
set(PROTO_OUT "${CMAKE_BINARY_DIR}/gen")
file(MAKE_DIRECTORY ${PROTO_OUT})

get_target_property(gRPC_CPP_PLUGIN_EXECUTABLE gRPC::grpc_cpp_plugin LOCATION)

add_custom_command(
  OUTPUT  "${PROTO_OUT}/terminal.pb.cc"
          "${PROTO_OUT}/terminal.pb.h"
          "${PROTO_OUT}/terminal.grpc.pb.cc"
          "${PROTO_OUT}/terminal.grpc.pb.h"
  COMMAND protobuf::protoc
    --grpc_out="${PROTO_OUT}"
    --cpp_out="${PROTO_OUT}"
    --plugin=protoc-gen-grpc="${gRPC_CPP_PLUGIN_EXECUTABLE}"
    -I "${PROTO_DIR}"
    "${PROTO_DIR}/terminal.proto"
  DEPENDS "${PROTO_DIR}/terminal.proto"
  COMMENT "Generating protobuf/gRPC C++ sources"
)

# ─── 源文件 ─────────────────────────────────────────────────────────────────
set(SOURCES
  src/main.cpp
  src/config/Config.cpp
  src/transport/GrpcClient.cpp
  src/transport/RingBuffer.cpp
  src/probe/SystemLogCollector.cpp
  src/probe/UsbMonitor.cpp
  src/probe/HeartbeatReporter.cpp
  src/probe/PolicyManager.cpp
  ${PROTO_OUT}/terminal.pb.cc
  ${PROTO_OUT}/terminal.grpc.pb.cc
)

add_executable(st_agent ${SOURCES})

target_include_directories(st_agent PRIVATE
  src
  ${PROTO_OUT}
  ${Protobuf_INCLUDE_DIRS}
)

target_link_libraries(st_agent PRIVATE
  Threads::Threads
  gRPC::grpc++
  protobuf::libprotobuf
  spdlog::spdlog
  tomlplusplus::tomlplusplus
)

# ─── 平台特定编译选项 ───────────────────────────────────────────────────────
if(WIN32)
  target_compile_definitions(st_agent PRIVATE PLATFORM_WINDOWS _WIN32_WINNT=0x0A00)
  target_link_libraries(st_agent PRIVATE wbemuuid ole32 oleaut32)
elseif(UNIX AND NOT APPLE)
  target_compile_definitions(st_agent PRIVATE PLATFORM_LINUX)
  target_link_libraries(st_agent PRIVATE udev)  # libudev for USB
elseif(APPLE)
  target_compile_definitions(st_agent PRIVATE PLATFORM_MACOS)
  target_link_libraries(st_agent PRIVATE "-framework IOKit" "-framework CoreFoundation")
endif()

# ─── 安装 ───────────────────────────────────────────────────────────────────
install(TARGETS st_agent DESTINATION bin)
install(FILES config.toml DESTINATION etc/safe_terminal)
