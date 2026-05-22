using System.IO;
using EndpointComplianceChecker.Models;
using Microsoft.Win32;

namespace EndpointComplianceChecker.Services;

public class VirtualMachineService
{
    // (RegPath, Name, Type, IsBuiltIn)
    private static readonly (string RegPath, string Name, string Type, bool IsBuiltIn)[] VmRegistryChecks =
    [
        (@"SOFTWARE\VMware, Inc.\VMware Workstation",        "VMware Workstation",  "VMware",    false),
        (@"SOFTWARE\VMware, Inc.\VMware Player",             "VMware Player",       "VMware",    false),
        (@"SOFTWARE\Oracle\VirtualBox",                      "VirtualBox",          "VirtualBox",false),
        (@"SOFTWARE\Parallels\Desktop",                      "Parallels Desktop",   "Parallels", false),
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization", "Hyper-V Host", "Hyper-V", true),
        (@"SOFTWARE\QEMU",                                   "QEMU",                "QEMU",      false),
        (@"SOFTWARE\Bochs",                                  "Bochs",               "Bochs",     false),
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

    // (ServiceName, Name, Type, IsBuiltIn)
    private static readonly (string ServiceName, string Name, string Type, bool IsBuiltIn)[] VmServiceChecks =
    [
        ("vmware-hostd",  "VMware Host Daemon",    "VMware",    false),
        ("VMAuthdService","VMware Authorization",   "VMware",    false),
        ("vboxsvc",       "VirtualBox COM Service", "VirtualBox",false),
        ("VBoxDrv",       "VirtualBox Driver",      "VirtualBox",false),
        ("vmms",          "Hyper-V 管理器",          "Hyper-V",   true),
        ("HvHost",        "Hyper-V Host Service",   "Hyper-V",   true),
    ];

    public VirtualMachineResult Scan(IProgress<string>? progress = null)
    {
        progress?.Report("正在检测虚拟机软件...");
        var result = new VirtualMachineResult();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Registry checks (HKLM 64-bit + 32-bit views)
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var (regPath, name, type, isBuiltIn) in VmRegistryChecks)
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
                            Name      = name,
                            Type      = type,
                            Evidence  = $"注册表: {hive}\\{regPath}",
                            IsBuiltIn = isBuiltIn,
                        });
                        var level = isBuiltIn ? "[WARN]" : "[FAIL]";
                        progress?.Report($"{level} 检测到{(isBuiltIn ? "内置" : "第三方")}虚拟化: {name} (注册表)");
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
        foreach (var (svcName, name, type, isBuiltIn) in VmServiceChecks)
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(svcName);
                _ = sc.Status; // throws if service doesn't exist
                if (seen.Add(name))
                {
                    result.Found.Add(new VirtualMachineItem
                    {
                        Name      = name,
                        Type      = type,
                        Evidence  = $"系统服务: {svcName}",
                        IsBuiltIn = isBuiltIn,
                    });
                    var level = isBuiltIn ? "[WARN]" : "[FAIL]";
                    progress?.Report($"{level} 检测到{(isBuiltIn ? "内置" : "第三方")}虚拟化: {name} (服务 {svcName})");
                }
            }
            catch { }
        }

        int thirdParty = result.Found.Count(v => !v.IsBuiltIn);
        int builtIn    = result.Found.Count(v => v.IsBuiltIn);

        if (result.Found.Count == 0)
            progress?.Report("[PASS] 未检测到虚拟机软件");
        else
        {
            if (thirdParty > 0)
                progress?.Report($"[FAIL] 检测到第三方VM软件 {thirdParty} 项（违规）" + (builtIn > 0 ? $"，内置Hyper-V {builtIn} 项（警告）" : ""));
            else
                progress?.Report($"[WARN] 仅检测到Windows内置Hyper-V {builtIn} 项（警告，非违规）");
        }

        return result;
    }
}
