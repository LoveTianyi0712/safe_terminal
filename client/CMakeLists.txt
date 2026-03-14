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

# ─── 安装规则 ────────────────────────────────────────────────────────────────
install(TARGETS st_agent DESTINATION bin)
install(FILES config/config.toml.template
        DESTINATION etc/safe_terminal
        RENAME config.toml)

# Linux：安装 systemd 服务单元（默认不启用，由 postinst 脚本处理）
if(UNIX AND NOT APPLE)
  install(FILES packaging/linux/st-agent.service
          DESTINATION lib/systemd/system)
endif()

# ─── CPack 打包配置 ──────────────────────────────────────────────────────────
set(CPACK_PACKAGE_NAME            "safe-terminal-agent")
set(CPACK_PACKAGE_VENDOR          "SafeTerminal")
set(CPACK_PACKAGE_VERSION_MAJOR   ${PROJECT_VERSION_MAJOR})
set(CPACK_PACKAGE_VERSION_MINOR   ${PROJECT_VERSION_MINOR})
set(CPACK_PACKAGE_VERSION_PATCH   ${PROJECT_VERSION_PATCH})
set(CPACK_PACKAGE_VERSION         "${PROJECT_VERSION}")
set(CPACK_PACKAGE_DESCRIPTION_SUMMARY "Safe Terminal Agent - 终端安全监控探针")
set(CPACK_PACKAGE_CONTACT         "admin@safe-terminal.internal")
set(CPACK_PACKAGE_INSTALL_DIRECTORY "SafeTerminal")
set(CPACK_RESOURCE_FILE_LICENSE   "${CMAKE_SOURCE_DIR}/packaging/LICENSE.txt")

# ── Linux DEB ──────────────────────────────────────────────────────────────
if(UNIX AND NOT APPLE)
  set(CPACK_GENERATOR "DEB;RPM")

  # DEB
  set(CPACK_DEBIAN_PACKAGE_SECTION    "admin")
  set(CPACK_DEBIAN_PACKAGE_PRIORITY   "optional")
  set(CPACK_DEBIAN_PACKAGE_MAINTAINER "SafeTerminal <admin@safe-terminal.internal>")
  set(CPACK_DEBIAN_PACKAGE_DEPENDS    "libssl3 | libssl1.1, libc6 (>= 2.17)")
  set(CPACK_DEBIAN_PACKAGE_CONTROL_EXTRA
      "${CMAKE_SOURCE_DIR}/packaging/linux/postinst"
      "${CMAKE_SOURCE_DIR}/packaging/linux/prerm"
      "${CMAKE_SOURCE_DIR}/packaging/linux/postrm")
  set(CPACK_DEBIAN_FILE_NAME          "DEB-DEFAULT")

  # RPM
  set(CPACK_RPM_PACKAGE_GROUP         "System Environment/Daemons")
  set(CPACK_RPM_PACKAGE_LICENSE       "Proprietary")
  set(CPACK_RPM_PACKAGE_REQUIRES      "openssl-libs, glibc >= 2.17")
  set(CPACK_RPM_POST_INSTALL_SCRIPT_FILE  "${CMAKE_SOURCE_DIR}/packaging/linux/postinst")
  set(CPACK_RPM_PRE_UNINSTALL_SCRIPT_FILE "${CMAKE_SOURCE_DIR}/packaging/linux/prerm")
  set(CPACK_RPM_POST_UNINSTALL_SCRIPT_FILE "${CMAKE_SOURCE_DIR}/packaging/linux/postrm")
  set(CPACK_RPM_FILE_NAME             "RPM-DEFAULT")
endif()

# ── Windows NSIS ──────────────────────────────────────────────────────────
if(WIN32)
  set(CPACK_GENERATOR "NSIS")
  set(CPACK_NSIS_DISPLAY_NAME        "Safe Terminal Agent")
  set(CPACK_NSIS_PACKAGE_NAME        "SafeTerminalAgent")
  set(CPACK_NSIS_INSTALL_ROOT        "C:\\\\Program Files")
  set(CPACK_NSIS_ENABLE_UNINSTALL_BEFORE_INSTALL ON)
  set(CPACK_NSIS_MODIFY_PATH         OFF)
  # 使用自定义 NSIS 脚本（含服务注册逻辑）
  set(CPACK_NSIS_EXTRA_INSTALL_COMMANDS
    "ExecWait '\\\"$INSTDIR\\\\bin\\\\install-service.bat\\\"'")
  set(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS
    "ExecWait '\\\"$INSTDIR\\\\bin\\\\uninstall-service.bat\\\"'")
endif()

include(CPack)
