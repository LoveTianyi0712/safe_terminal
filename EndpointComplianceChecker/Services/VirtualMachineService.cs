using System.IO;
using EndpointComplianceChecker.Models;
using Microsoft.Win32;

namespace EndpointComplianceChecker.Services;

public class VirtualMachineService
{
    // (RegPath, Name, Type, IsBuiltIn, InstallPathValue, KnownExeNames)
    // InstallPathValue: registry value name holding install dir (null = not applicable)
    // KnownExeNames:    executable file names to verify the app is actually installed
    private static readonly (string RegPath, string Name, string Type, bool IsBuiltIn,
                              string? InstallPathValue, string[] KnownExeNames)[] VmRegistryChecks =
    [
        (@"SOFTWARE\VMware, Inc.\VMware Workstation", "VMware Workstation", "VMware", false,
            "InstallPath", new[]{ "vmware.exe" }),
        (@"SOFTWARE\VMware, Inc.\VMware Player",      "VMware Player",      "VMware", false,
            "InstallPath", new[]{ "vmplayer.exe" }),
        (@"SOFTWARE\Oracle\VirtualBox",               "VirtualBox",         "VirtualBox", false,
            "InstallDir",  new[]{ "VirtualBox.exe" }),
        (@"SOFTWARE\Parallels\Desktop",               "Parallels Desktop",  "Parallels", false,
            null,          new[]{ "prl_client_app.exe" }),
        // Hyper-V is a Windows feature — no exe to verify; rely on registry + service
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization", "Hyper-V Host", "Hyper-V", true,
            null, Array.Empty<string>()),
        (@"SOFTWARE\QEMU",  "QEMU",  "QEMU",  false, "Install_Dir", new[]{ "qemu-system-x86_64.exe", "qemu-img.exe" }),
        (@"SOFTWARE\Bochs", "Bochs", "Bochs", false, null,          new[]{ "bochs.exe" }),
    ];

    // Program Files sub-paths to probe (directory + exe)
    private static readonly (string SubPath, string Name, string Type, string[] ExeNames)[] VmDirChecks =
    [
        (@"VMware\VMware Workstation", "VMware Workstation", "VMware",    new[]{ "vmware.exe" }),
        (@"VMware\VMware Player",      "VMware Player",      "VMware",    new[]{ "vmplayer.exe" }),
        (@"Oracle\VirtualBox",         "VirtualBox",         "VirtualBox",new[]{ "VirtualBox.exe" }),
        (@"Microsoft\Virtual PC",      "Virtual PC",         "Virtual PC",new[]{ "VPC.exe" }),
        (@"Parallels\Desktop",         "Parallels Desktop",  "Parallels", new[]{ "prl_client_app.exe" }),
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
            foreach (var (regPath, name, type, isBuiltIn, installPathVal, knownExes) in VmRegistryChecks)
            {
                try
                {
                    using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64)
                                               .OpenSubKey(regPath)
                               ?? RegistryKey.OpenBaseKey(hive, RegistryView.Registry32)
                                               .OpenSubKey(regPath);
                    if (key == null || !seen.Add(name)) continue;

                    // For apps with known executables, verify the exe actually exists
                    // (prevents false positives from uninstall registry residuals)
                    if (knownExes.Length > 0)
                    {
                        var installDir = installPathVal != null
                            ? key.GetValue(installPathVal)?.ToString()
                            : null;
                        if (!ExeExists(installDir, knownExes))
                        {
                            progress?.Report($"[INFO] 注册表残留（已卸载）: {name}，跳过");
                            seen.Remove(name);
                            continue;
                        }
                    }

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
                catch { }
            }
        }

        // 2. Program Files directory + exe checks
        var programDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var baseDir in programDirs)
        {
            foreach (var (subPath, name, type, exeNames) in VmDirChecks)
            {
                try
                {
                    var full = Path.Combine(baseDir, subPath);
                    if (!Directory.Exists(full)) continue;
                    // Verify the main exe exists — directory alone can be a leftover
                    if (!ExeExists(full, exeNames)) continue;
                    if (!seen.Add(name)) continue;

                    result.Found.Add(new VirtualMachineItem
                    {
                        Name     = name,
                        Type     = type,
                        Evidence = $"目录+可执行文件: {full}",
                    });
                    progress?.Report($"[FAIL] 检测到虚拟机: {name} (目录)");
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

    /// <summary>
    /// Returns true if any of the given exe names exist in the specified directory,
    /// or (fallback) anywhere under standard Program Files directories.
    /// Prevents false positives from registry residuals left by uninstallers.
    /// </summary>
    private static bool ExeExists(string? installDir, string[] exeNames)
    {
        if (exeNames.Length == 0) return true; // no exe verification needed (e.g. Hyper-V)

        var searchDirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(installDir) && Directory.Exists(installDir))
            searchDirs.Add(installDir!);
        searchDirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        searchDirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        foreach (var dir in searchDirs)
        {
            foreach (var exe in exeNames)
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, exe))) return true;
                    // One level deep (e.g. ProgramFiles\VMware\VMware Workstation\vmware.exe)
                    foreach (var sub in Directory.EnumerateDirectories(dir).Take(20))
                    {
                        if (File.Exists(Path.Combine(sub, exe))) return true;
                        foreach (var sub2 in Directory.EnumerateDirectories(sub).Take(10))
                            if (File.Exists(Path.Combine(sub2, exe))) return true;
                    }
                }
                catch { }
            }
        }
        return false;
    }
}
