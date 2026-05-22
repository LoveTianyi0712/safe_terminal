using System.IO;
using EndpointComplianceChecker.Models;
using Microsoft.Win32;

namespace EndpointComplianceChecker.Services;

public class VirtualMachineService
{
    // Registry paths and display names for known VM products
    private static readonly (string RegPath, string Name, string Type)[] VmRegistryChecks =
    [
        (@"SOFTWARE\VMware, Inc.\VMware Workstation",        "VMware Workstation",  "VMware"),
        (@"SOFTWARE\VMware, Inc.\VMware Player",             "VMware Player",       "VMware"),
        (@"SOFTWARE\Oracle\VirtualBox",                      "VirtualBox",          "VirtualBox"),
        (@"SOFTWARE\Parallels\Desktop",                      "Parallels Desktop",   "Parallels"),
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization", "Hyper-V Host", "Hyper-V"),
        (@"SOFTWARE\QEMU",                                   "QEMU",                "QEMU"),
        (@"SOFTWARE\Bochs",                                  "Bochs",               "Bochs"),
    ];

    // Program Files sub-paths to probe
    private static readonly (string SubPath, string Name, string Type)[] VmDirChecks =
    [
        (@"VMware\VMware Workstation",   "VMware Workstation",  "VMware"),
        (@"VMware\VMware Player",        "VMware Player",       "VMware"),
        (@"Oracle\VirtualBox",           "VirtualBox",          "VirtualBox"),
        (@"Microsoft\Virtual PC",        "Virtual PC",          "Virtual PC"),
        (@"Parallels\Desktop",           "Parallels Desktop",   "Parallels"),
    ];

    // Windows service names indicating VM host is installed/running
    private static readonly (string ServiceName, string Name, string Type)[] VmServiceChecks =
    [
        ("vmware-hostd",  "VMware Host Daemon",  "VMware"),
        ("VMAuthdService","VMware Authorization", "VMware"),
        ("vboxsvc",       "VirtualBox COM Service","VirtualBox"),
        ("VBoxDrv",       "VirtualBox Driver",   "VirtualBox"),
        ("vmms",          "Hyper-V Manager",     "Hyper-V"),
    ];

    public VirtualMachineResult Scan(IProgress<string>? progress = null)
    {
        progress?.Report("正在检测虚拟机软件...");
        var result = new VirtualMachineResult();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Registry checks (HKLM 64-bit + 32-bit views)
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var (regPath, name, type) in VmRegistryChecks)
            {
                try
                {
                    using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64)
                                               .OpenSubKey(regPath)
                               ?? RegistryKey.OpenBaseKey(hive, RegistryView.Registry32)
                                               .OpenSubKey(regPath);
                    if (key != null && seen.Add(name))
                    {
                        result.Found.Add(new VirtualMachineItem
                        {
                            Name     = name,
                            Type     = type,
                            Evidence = $"注册表: {hive}\\{regPath}",
                        });
                        progress?.Report($"[FAIL] 检测到虚拟机: {name} (注册表)");
                    }
                }
                catch { }
            }
        }

        // 2. Program Files directory checks
        var programDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var baseDir in programDirs)
        {
            foreach (var (subPath, name, type) in VmDirChecks)
            {
                try
                {
                    var full = Path.Combine(baseDir, subPath);
                    if (Directory.Exists(full) && seen.Add(name))
                    {
                        result.Found.Add(new VirtualMachineItem
                        {
                            Name     = name,
                            Type     = type,
                            Evidence = $"目录: {full}",
                        });
                        progress?.Report($"[FAIL] 检测到虚拟机: {name} (目录)");
                    }
                }
                catch { }
            }
        }

        // 3. Windows service presence
        foreach (var (svcName, name, type) in VmServiceChecks)
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(svcName);
                _ = sc.Status; // throws if service doesn't exist
                if (seen.Add(name))
                {
                    result.Found.Add(new VirtualMachineItem
                    {
                        Name     = name,
                        Type     = type,
                        Evidence = $"系统服务: {svcName}",
                    });
                    progress?.Report($"[FAIL] 检测到虚拟机: {name} (服务 {svcName})");
                }
            }
            catch { }
        }

        if (result.Found.Count == 0)
            progress?.Report("[PASS] 未检测到虚拟机软件");
        else
            progress?.Report($"[FAIL] 共检测到 {result.Found.Count} 项虚拟机相关软件");

        return result;
    }
}
