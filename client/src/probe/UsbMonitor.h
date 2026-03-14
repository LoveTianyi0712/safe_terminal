#pragma once
#include "../config/Config.h"
#include "terminal.pb.h"
#include <atomic>
#include <thread>
#include <functional>
#include <string>

namespace st {

using UsbEventCallback = std::function<void(terminal::v1::UsbEvent)>;

/**
 * USB 设备接入/移除监控
 *
 * Windows : WM_DEVICECHANGE / SetupDi API / WMI
 * Linux   : libudev udev_monitor
 * macOS   : IOKit IOServiceAddMatchingNotification
 */
class UsbMonitor {
public:
    UsbMonitor(const CollectionConfig& cfg,
               const terminal::v1::TerminalIdentity& identity,
               UsbEventCallback callback);
    ~UsbMonitor();

    void start();
    void stop();

private:
    void monitor_loop();

#if defined(PLATFORM_WINDOWS)
    void poll_windows_usb();
    static std::string get_device_property(void* dev_info, void* dev_data,
                                            const GUID& prop_key);
#elif defined(PLATFORM_LINUX)
    void init_udev();
    void poll_udev();
    void* udev_{nullptr};
    void* udev_mon_{nullptr};
#elif defined(PLATFORM_MACOS)
    void init_iokit();
    void poll_iokit();
#endif

    CollectionConfig                    cfg_;
    terminal::v1::TerminalIdentity      identity_;
    UsbEventCallback                    callback_;
    std::atomic<bool>                   running_{false};
    std::thread                         worker_;
};

} // namespace st
