#include "UsbMonitor.h"
#include <spdlog/spdlog.h>
#include <google/protobuf/util/time_util.h>

// ─── 平台头文件 ──────────────────────────────────────────────────────────────
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
#  include <cstring>
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
    cleanup_udev();
#elif defined(PLATFORM_MACOS)
    init_iokit();
    CFRunLoopRun(); // IOKit 使用 RunLoop 驱动事件；stop() 需调用 CFRunLoopStop
#else
    spdlog::warn("[UsbMonitor] Unsupported platform");
#endif

    spdlog::info("[UsbMonitor] USB monitoring stopped");
}

// ═══════════════════════════════════════════════════════════════════════════════
// Windows 实现 — SetupDi 枚举（轮询差量检测）
// ═══════════════════════════════════════════════════════════════════════════════
#if defined(PLATFORM_WINDOWS)

void UsbMonitor::poll_windows_usb() {
    // TODO: 枚举当前 USB 设备树，与上次对比，检测 CONNECTED/DISCONNECTED 变化
    //
    // 建议实现方式（消息驱动，优于轮询）：
    //   1. CreateWindowEx(hidden) 创建隐藏消息窗口
    //   2. RegisterDeviceNotification(hwnd, GUID_DEVINTERFACE_USB_DEVICE, ...)
    //   3. GetMessage 循环中处理 WM_DEVICECHANGE:
    //      - DBT_DEVICEARRIVAL   → USB_CONNECTED
    //      - DBT_DEVICEREMOVECOMPLETE → USB_DISCONNECTED
    //   4. 用 DEV_BROADCAST_DEVICEINTERFACE.dbcc_name 提取 VID/PID
    //
    // 轮询备选（已有框架）：
    //   SetupDiGetClassDevs(&GUID_DEVINTERFACE_USB_DEVICE, ...)
    //   SetupDiEnumDeviceInfo / SetupDiGetDeviceProperty
    //   提取 DEVPKEY_Device_HardwareIds（包含 "USB\VID_xxxx&PID_xxxx"）
}

// ═══════════════════════════════════════════════════════════════════════════════
// Linux 实现 — libudev 实时监控
// ═══════════════════════════════════════════════════════════════════════════════
#elif defined(PLATFORM_LINUX)

void UsbMonitor::init_udev() {
    // 创建 udev 上下文
    struct udev* u = udev_new();
    if (!u) {
        spdlog::error("[UsbMonitor] udev_new() failed");
        return;
    }
    udev_ = u;

    // 创建监视器（监听内核 → udev 守护进程已处理的事件）
    struct udev_monitor* mon = udev_monitor_new_from_netlink(u, "udev");
    if (!mon) {
        spdlog::error("[UsbMonitor] udev_monitor_new_from_netlink() failed");
        udev_unref(u);
        udev_ = nullptr;
        return;
    }

    // 只关注 USB 设备（usb_device 即 USB 整体设备节点，而非各接口）
    udev_monitor_filter_add_match_subsystem_devtype(mon, "usb", "usb_device");
    udev_monitor_enable_receiving(mon);
    udev_mon_ = mon;

    spdlog::info("[UsbMonitor] libudev monitor initialized");
}

void UsbMonitor::poll_udev() {
    if (!udev_mon_) {
        std::this_thread::sleep_for(std::chrono::seconds(5));
        return;
    }

    auto* mon = static_cast<struct udev_monitor*>(udev_mon_);
    int fd = udev_monitor_get_fd(mon);

    struct pollfd fds{fd, POLLIN, 0};
    // 200ms 超时，保证 running_ 检查不被长时间阻塞
    int ret = poll(&fds, 1, 200);
    if (ret <= 0) return; // 超时或错误

    struct udev_device* dev = udev_monitor_receive_device(mon);
    if (!dev) return;

    const char* action = udev_device_get_action(dev);
    const char* vid    = udev_device_get_sysattr_value(dev, "idVendor");
    const char* pid_s  = udev_device_get_sysattr_value(dev, "idProduct");
    const char* serial = udev_device_get_sysattr_value(dev, "serial");
    const char* mfr    = udev_device_get_sysattr_value(dev, "manufacturer");
    const char* prod   = udev_device_get_sysattr_value(dev, "product");

    bool is_add = action && strcmp(action, "add") == 0;

    // 构造设备 ID（格式：VID_0781&PID_5581，与 Windows 保持一致）
    std::string device_id;
    if (vid && pid_s) {
        device_id = std::string("VID_") + vid + "&PID_" + pid_s;
    }

    spdlog::info("[UsbMonitor] USB {} device_id={} vendor={} product={}",
                 action ? action : "?",
                 device_id,
                 mfr  ? mfr  : "",
                 prod ? prod : "");

    terminal::v1::UsbEvent event;
    *event.mutable_identity() = identity_;
    *event.mutable_ts()       = google::protobuf::util::TimeUtil::GetCurrentTime();
    event.set_action(is_add ? terminal::v1::USB_CONNECTED
                             : terminal::v1::USB_DISCONNECTED);

    if (!device_id.empty()) event.set_device_id(device_id);
    if (mfr)    event.set_vendor(mfr);
    if (prod)   event.set_product(prod);
    if (serial) event.set_serial_number(serial);

    if (callback_) callback_(std::move(event));
    udev_device_unref(dev);
}

void UsbMonitor::cleanup_udev() {
    if (udev_mon_) {
        udev_monitor_unref(static_cast<struct udev_monitor*>(udev_mon_));
        udev_mon_ = nullptr;
    }
    if (udev_) {
        udev_unref(static_cast<struct udev*>(udev_));
        udev_ = nullptr;
    }
    spdlog::info("[UsbMonitor] libudev resources released");
}

// ═══════════════════════════════════════════════════════════════════════════════
// macOS 实现 — IOKit 通知（预留框架）
// ═══════════════════════════════════════════════════════════════════════════════
#elif defined(PLATFORM_MACOS)

void UsbMonitor::init_iokit() {
    // TODO: 注册 USB 设备插拔通知
    //
    // IONotificationPortRef port = IONotificationPortCreate(kIOMainPortDefault);
    // CFRunLoopAddSource(CFRunLoopGetCurrent(),
    //                    IONotificationPortGetRunLoopSource(port),
    //                    kCFRunLoopDefaultMode);
    //
    // io_iterator_t added_iter, removed_iter;
    // IOServiceAddMatchingNotification(port, kIOFirstMatchNotification,
    //     IOServiceMatching(kIOUSBDeviceClassName),
    //     usb_device_added_cb, this, &added_iter);
    // IOServiceAddMatchingNotification(port, kIOTerminatedNotification,
    //     IOServiceMatching(kIOUSBDeviceClassName),
    //     usb_device_removed_cb, this, &removed_iter);
    //
    // 驱动迭代器（清空初始匹配，避免重放历史设备）
    // io_object_t obj; while ((obj = IOIteratorNext(added_iter))) IOObjectRelease(obj);
    spdlog::info("[UsbMonitor] IOKit stub initialized");
}

void UsbMonitor::poll_iokit() {
    // TODO: 在 added/removed 回调中调用以下 IOKit API 提取属性：
    // IORegistryEntryCreateCFProperty(device, CFSTR(kUSBVendorID),   ...) → VID
    // IORegistryEntryCreateCFProperty(device, CFSTR(kUSBProductID),  ...) → PID
    // IORegistryEntryCreateCFProperty(device, CFSTR(kUSBVendorString),...) → manufacturer
    // IORegistryEntryCreateCFProperty(device, CFSTR(kUSBProductString),...) → product
    // IORegistryEntryCreateCFProperty(device, CFSTR(kUSBSerialNumberString),...) → serial
}

#endif

} // namespace st
