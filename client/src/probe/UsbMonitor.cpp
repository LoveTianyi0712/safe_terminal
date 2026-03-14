#include "UsbMonitor.h"
#include <spdlog/spdlog.h>
#include <google/protobuf/util/time_util.h>

#if defined(PLATFORM_WINDOWS)
#  include <windows.h>
#  include <dbt.h>
#  include <setupapi.h>
#  include <initguid.h>
#  include <usbiodef.h>
#  pragma comment(lib, "setupapi.lib")
#elif defined(PLATFORM_LINUX)
#  include <libudev.h>
#  include <poll.h>
#elif defined(PLATFORM_MACOS)
#  include <IOKit/IOKitLib.h>
#  include <IOKit/usb/IOUSBLib.h>
#  include <CoreFoundation/CoreFoundation.h>
#endif

namespace st {

UsbMonitor::UsbMonitor(const CollectionConfig& cfg,
                       const terminal::v1::TerminalIdentity& identity,
                       UsbEventCallback callback)
    : cfg_(cfg), identity_(identity), callback_(std::move(callback)) {}

UsbMonitor::~UsbMonitor() { stop(); }

void UsbMonitor::start() {
    if (!cfg_.usb_monitor_enabled) return;
    running_ = true;
    worker_ = std::thread([this] { monitor_loop(); });
}

void UsbMonitor::stop() {
    running_ = false;
    if (worker_.joinable()) worker_.join();
}

void UsbMonitor::monitor_loop() {
    spdlog::info("[UsbMonitor] Starting USB monitoring");

#if defined(PLATFORM_WINDOWS)
    while (running_) {
        poll_windows_usb();
        std::this_thread::sleep_for(std::chrono::seconds(2));
    }
#elif defined(PLATFORM_LINUX)
    init_udev();
    while (running_) {
        poll_udev();
    }
#elif defined(PLATFORM_MACOS)
    init_iokit();
    // IOKit 使用 RunLoop，此线程运行 CFRunLoop
    CFRunLoopRun();
#else
    spdlog::warn("[UsbMonitor] Unsupported platform");
#endif
}

// ─── Windows 实现 ──────────────────────────────────────────────────────────
#if defined(PLATFORM_WINDOWS)

void UsbMonitor::poll_windows_usb() {
    // TODO: 枚举当前 USB 设备树，与上次对比，检测 CONNECTED/DISCONNECTED 变化
    // 使用 SetupDiGetClassDevs(GUID_DEVINTERFACE_USB_DEVICE, ...)
    // 提取 DEVPKEY_Device_HardwareIds, DEVPKEY_Device_BusReportedDeviceDesc
    // DevicePropertyBuffer 包含 VID_xxxx&PID_xxxx 格式字符串
    //
    // 更优方案：使用消息窗口监听 WM_DEVICECHANGE (DBT_DEVICEARRIVAL/DBT_DEVICEREMOVECOMPLETE)
    // 但需要消息循环，可在单独线程中创建隐藏窗口
}

// ─── Linux 实现 ────────────────────────────────────────────────────────────
#elif defined(PLATFORM_LINUX)

void UsbMonitor::init_udev() {
    // TODO:
    // udev_ = udev_new();
    // udev_mon_ = udev_monitor_new_from_netlink(udev, "udev");
    // udev_monitor_filter_add_match_subsystem_devtype(mon, "usb", "usb_device");
    // udev_monitor_enable_receiving(mon);
}

void UsbMonitor::poll_udev() {
    // TODO:
    // int fd = udev_monitor_get_fd(udev_mon_);
    // struct pollfd fds = {fd, POLLIN, 0};
    // if (poll(&fds, 1, 200) > 0) {
    //   udev_device* dev = udev_monitor_receive_device(udev_mon_);
    //   const char* action = udev_device_get_action(dev);  // "add" / "remove"
    //   const char* vid    = udev_device_get_sysattr_value(dev, "idVendor");
    //   const char* pid    = udev_device_get_sysattr_value(dev, "idProduct");
    //   const char* serial = udev_device_get_sysattr_value(dev, "serial");
    //   构造 UsbEvent 并 callback_()
    //   udev_device_unref(dev);
    // }
}

// ─── macOS 实现 ────────────────────────────────────────────────────────────
#elif defined(PLATFORM_MACOS)

void UsbMonitor::init_iokit() {
    // TODO:
    // IONotificationPortRef port = IONotificationPortCreate(kIOMasterPortDefault);
    // CFRunLoopAddSource(CFRunLoopGetCurrent(),
    //                    IONotificationPortGetRunLoopSource(port),
    //                    kCFRunLoopDefaultMode);
    // io_iterator_t iter;
    // IOServiceAddMatchingNotification(port, kIOFirstMatchNotification,
    //                                  IOServiceMatching(kIOUSBDeviceClassName),
    //                                  usb_device_added_callback, this, &iter);
}

void UsbMonitor::poll_iokit() {
    // TODO: 在 added/removed 回调中构造 UsbEvent
    // IORegistryEntryCreateCFProperty(device, CFSTR(kUSBVendorID), ...)
}

#endif

} // namespace st
