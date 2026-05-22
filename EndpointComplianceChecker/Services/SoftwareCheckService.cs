using System.IO;
using System.Management;
using EndpointComplianceChecker.Models;
using Microsoft.Win32;

namespace EndpointComplianceChecker.Services;

public class SoftwareCheckService
{
    private readonly List<PirateSoftwareRule> _pirateRules;
    private readonly List<string> _remoteControlNames;

    public SoftwareCheckService(List<PirateSoftwareRule> pirateRules, List<string> remoteControlNames)
    {
        _pirateRules = pirateRules;
        _remoteControlNames = remoteControlNames;
    }

    public string CheckOsLicense(IProgress<string>? progress = null)
    {
        progress?.Report("正在查询操作系统授权状态...");
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LicenseStatus, Description, Name FROM SoftwareLicensingProduct " +
                "WHERE PartialProductKey IS NOT NULL " +
                "AND ApplicationId='55c92734-d682-4d71-983e-d6ec3f16059f'");

            foreach (ManagementObject obj in searcher.Get())
            {
                var status = Convert.ToInt32(obj["LicenseStatus"]);
                var desc = obj["Description"]?.ToString() ?? string.Empty;
                var name = obj["Name"]?.ToString() ?? string.Empty;

                progress?.Report($"授权产品: {name}");
                progress?.Report($"授权状态码: {status}，描述: {desc}");

                if (status == 1)
                {
                    bool isKms = desc.Contains("KMS", StringComparison.OrdinalIgnoreCase) ||
                                 desc.Contains("volume", StringComparison.OrdinalIgnoreCase);
                    return isKms
                        ? "KMS 批量激活 – 需人工复核"
                        : "正版（已激活）";
                }
                if (status == 0) return "未授权";
                if (status == 5) return "通知模式（宽限期内）";
                return $"未激活（状态码 {status}）– 需人工复核";
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"[WARN] WMI 查询授权失败: {ex.Message}，尝试 slmgr 命令");
            return CheckOsLicenseViaSlmgr(progress);
        }
        return "无法确定（WMI 未返回数据）";
    }

    private static string CheckOsLicenseViaSlmgr(IProgress<string>? progress)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cscript.exe",
                @"//NoLogo C:\Windows\System32\slmgr.vbs /dli")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return "无法确定";
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);

            if (output.Contains("已授权", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Licensed", StringComparison.OrdinalIgnoreCase))
                return "正版（已激活）";
            if (output.Contains("通知", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Notification", StringComparison.OrdinalIgnoreCase))
                return "通知模式（需激活）";
            return "未激活 – 需人工复核";
        }
        catch (Exception ex)
        {
            progress?.Report($"[WARN] slmgr 检查失败: {ex.Message}");
            return "无法确定（查询失败）";
        }
    }

    public List<SoftwareItem> EnumerateInstalledSoftware(IProgress<string>? progress = null)
    {
        progress?.Report("正在枚举已安装软件列表...");
        var results = new List<SoftwareItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var regPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var regPath in regPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key == null) continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(subName);
                        if (sub == null) continue;

                        var name = sub.GetValue("DisplayName")?.ToString()?.Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!seen.Add(name)) continue;

                        var systemComponent = sub.GetValue("SystemComponent");
                        if (systemComponent is int sc && sc == 1) continue;

                        results.Add(new SoftwareItem
                        {
                            Name = name,
                            Version = sub.GetValue("DisplayVersion")?.ToString(),
                            InstallLocation = sub.GetValue("InstallLocation")?.ToString(),
                            Publisher = sub.GetValue("Publisher")?.ToString()
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"[WARN] 读取注册表 {regPath} 失败: {ex.Message}");
            }
        }

        // Also check HKCU
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (key != null)
            {
                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(subName);
                        if (sub == null) continue;
                        var name = sub.GetValue("DisplayName")?.ToString()?.Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!seen.Add(name)) continue;

                        results.Add(new SoftwareItem
                        {
                            Name = name,
                            Version = sub.GetValue("DisplayVersion")?.ToString(),
                            InstallLocation = sub.GetValue("InstallLocation")?.ToString(),
                            Publisher = sub.GetValue("Publisher")?.ToString()
                        });
                    }
                    catch { }
                }
            }
        }
        catch { }

        progress?.Report($"共发现 {results.Count} 个已安装软件");
        return results;
    }

    public List<SoftwareItem> DetectPirateSoftware(
        List<SoftwareItem> installed,
        IProgress<string>? progress = null)
    {
        var results = new List<SoftwareItem>();
        if (_pirateRules.Count == 0)
        {
            progress?.Report("未配置盗版软件规则，跳过检测");
            return results;
        }

        progress?.Report($"正在检测盗版软件，共 {_pirateRules.Count} 条规则...");

        foreach (var rule in _pirateRules)
        {
            var matches = installed
                .Where(s => s.Name.Contains(rule.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var match in matches)
            {
                progress?.Report($"  匹配规则: {rule.Name} → {match.Name}");
                bool isPirate = false;

                if (rule.Detection.Type == "registry" && !string.IsNullOrEmpty(rule.Detection.Path))
                    isPirate = CheckRegistryForPirate(rule.Detection.Path, rule.Detection.PirateIndicator);

                if (!isPirate && !string.IsNullOrEmpty(match.InstallLocation))
                    isPirate = CheckFilesForPirate(match.InstallLocation!, rule.Detection.PirateIndicator);

                if (isPirate)
                {
                    progress?.Report($"[WARN] 发现疑似盗版: {match.Name}");
                    results.Add(match);
                }
            }
        }

        if (results.Count == 0)
            progress?.Report("未发现盗版软件");

        return results;
    }

    public List<SoftwareItem> DetectRemoteControlSoftware(
        List<SoftwareItem> installed,
        IProgress<string>? progress = null)
    {
        progress?.Report("正在检测远程控制软件...");
        var results = new List<SoftwareItem>();

        foreach (var sw in installed)
        {
            if (_remoteControlNames.Any(r =>
                sw.Name.Contains(r, StringComparison.OrdinalIgnoreCase)))
            {
                progress?.Report($"[WARN] 发现远程控制软件: {sw.Name}");
                results.Add(sw);
            }
        }

        // Also check running processes
        try
        {
            var processes = System.Diagnostics.Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    var pname = proc.ProcessName;
                    if (_remoteControlNames.Any(r =>
                        pname.Contains(r, StringComparison.OrdinalIgnoreCase)))
                    {
                        var alreadyFound = results.Any(r =>
                            r.Name.Contains(proc.ProcessName, StringComparison.OrdinalIgnoreCase));
                        if (!alreadyFound)
                        {
                            progress?.Report($"[WARN] 进程中发现远程控制软件: {pname}");
                            results.Add(new SoftwareItem
                            {
                                Name = $"{pname} (运行中进程)",
                                InstallLocation = "检测自进程列表"
                            });
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        if (results.Count == 0)
            progress?.Report("未发现远程控制软件");

        return results;
    }

    private static bool CheckRegistryForPirate(string registryPath, List<string> indicators)
    {
        try
        {
            var parts = registryPath.Split('\\', 2);
            if (parts.Length < 2) return false;

            var root = parts[0].ToUpperInvariant() switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
                _ => Registry.LocalMachine
            };

            using var key = root.OpenSubKey(parts[1]);
            if (key == null) return false;

            foreach (var vname in key.GetValueNames())
            {
                var val = key.GetValue(vname)?.ToString() ?? string.Empty;
                if (indicators.Any(i => val.Contains(i, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool CheckFilesForPirate(string dir, List<string> indicators)
    {
        try
        {
            if (!Directory.Exists(dir)) return false;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Take(200))
            {
                var fname = Path.GetFileName(file);
                if (indicators.Any(i => fname.Contains(i, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch { }
        return false;
    }
}
