using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using EndpointComplianceChecker.Models;

namespace EndpointComplianceChecker.Services;

public class BasicInfoService
{
    public BasicInfo Collect(IProgress<string>? progress = null)
    {
        progress?.Report("正在采集终端基本信息...");

        var info = new BasicInfo
        {
            ComputerName = Environment.GetEnvironmentVariable("COMPUTERNAME")
                           ?? Environment.MachineName,
            Username = Environment.GetEnvironmentVariable("USERNAME")
                       ?? Environment.UserName,
            ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"
        };

        CollectNetworkInfo(info, progress);
        CollectOsVersion(info, progress);
        CollectHardwareInfo(info, progress);

        progress?.Report($"计算机名: {info.ComputerName}");
        progress?.Report($"用户名: {info.Username}");
        progress?.Report($"IP 地址: {info.IpAddress}");
        progress?.Report($"MAC 地址: {info.MacAddress}");
        progress?.Report($"操作系统: {info.OsVersion}");
        progress?.Report($"内存: {info.RamGb} GB");
        progress?.Report($"磁盘 C: {info.DiskTotalGb} GB，使用率 {info.DiskUsagePercent}%");

        return info;
    }

    private static void CollectNetworkInfo(BasicInfo info, IProgress<string>? progress)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (IsVirtualAdapter(ni.Description)) continue;

                var ipProps = ni.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a =>
                        a.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !a.Address.ToString().StartsWith("169.254"));

                if (ipv4 == null) continue;

                info.IpAddress = ipv4.Address.ToString();
                var macBytes = ni.GetPhysicalAddress().GetAddressBytes();
                if (macBytes.Length == 6)
                    info.MacAddress = string.Join(":", macBytes.Select(b => b.ToString("X2")));
                break;
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"[WARN] 获取网络信息失败: {ex.Message}");
        }
    }

    private static bool IsVirtualAdapter(string desc) =>
        desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
        desc.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
        desc.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
        desc.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
        desc.Contains("Loopback", StringComparison.OrdinalIgnoreCase);

    private static void CollectOsVersion(BasicInfo info, IProgress<string>? progress)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                info.OsVersion = obj["Caption"]?.ToString()?.Trim() ?? string.Empty;
                break;
            }
        }
        catch (Exception ex)
        {
            info.OsVersion = Environment.OSVersion.VersionString;
            progress?.Report($"[WARN] WMI 查询 OS 版本失败，使用系统 API: {ex.Message}");
        }
    }

    private static void CollectHardwareInfo(BasicInfo info, IProgress<string>? progress)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["TotalPhysicalMemory"] is ulong mem)
                    info.RamGb = Math.Round(mem / (1024.0 * 1024 * 1024), 1);
                break;
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"[WARN] 获取内存信息失败: {ex.Message}");
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID='C:'");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["Size"] is ulong size && size > 0)
                {
                    var free = obj["FreeSpace"] is ulong f ? f : 0UL;
                    info.DiskTotalGb = Math.Round(size / (1024.0 * 1024 * 1024), 1);
                    info.DiskUsagePercent = Math.Round((size - free) * 100.0 / size, 1);
                }
                break;
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"[WARN] 获取磁盘信息失败: {ex.Message}");
        }
    }
}
