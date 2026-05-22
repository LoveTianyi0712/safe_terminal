using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using EndpointComplianceChecker.Models;
using Microsoft.Win32;

namespace EndpointComplianceChecker.Services;

public class OfficeSoftwareScanService
{
    private readonly List<SensitivePattern> _patterns;
    private readonly WeChatScanSettings     _settings;  // reuse size/exclude settings

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".csv", ".xml", ".json",
        ".doc", ".docx", ".md", ".html", ".htm", ".bat", ".sh", ".conf"
    };

    public OfficeSoftwareScanService(List<SensitivePattern> patterns, WeChatScanSettings settings)
    {
        _patterns = patterns;
        _settings = settings;
    }

    // ------------------------------------------------------------------ App definitions

    // KnownExes: executables that must exist somewhere to confirm the app is truly installed.
    // Empty array means "skip exe verification" (Teams is hard to pin down cross-version).
    private record AppDef(
        string Name,
        Func<IProgress<string>?, IEnumerable<string>> GetCandidates,
        string[] UninstallNames,   // DisplayNames to look for in the Uninstall registry
        string[] AppRegKeys);      // App-specific registry keys as fallback

    private IEnumerable<AppDef> GetAppDefs()
    {
        yield return new AppDef("企业微信",
            GetWeComCandidates,
            new[] { "企业微信", "WXWork", "WeCom" },
            new[] { @"HKCU\Software\Tencent\WXWork", @"HKLM\SOFTWARE\WOW6432Node\Tencent\WXWork" });

        yield return new AppDef("钉钉",
            GetDingTalkCandidates,
            new[] { "钉钉", "DingTalk", "DingDing" },
            new[] { @"HKCU\Software\DingDing", @"HKCU\Software\Alibaba\DingTalk" });

        yield return new AppDef("飞书",
            GetFeishuCandidates,
            new[] { "飞书", "Feishu", "Lark" },
            new[] { @"HKCU\Software\Bytedance\Lark", @"HKCU\Software\Feishu" });

        yield return new AppDef("Microsoft Teams",
            GetTeamsCandidates,
            new[] { "Microsoft Teams", "Teams" },
            new[] { @"HKCU\Software\Microsoft\Teams" });
    }

    // ------------------------------------------------------------------ Public scan entry

    public List<OfficeSoftwareScanResult> ScanAll(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<OfficeSoftwareScanResult>();

        foreach (var app in GetAppDefs())
        {
            if (ct.IsCancellationRequested) break;

            progress?.Report($"正在检测 {app.Name} 存储路径...");

            // Verify the app is actually installed before scanning its data directory
            bool appInstalled = IsAppInstalled(app.UninstallNames, app.AppRegKeys, progress, app.Name);
            if (!appInstalled)
            {
                progress?.Report($"[INFO] {app.Name}: 未检测到安装，跳过");
                continue;
            }

            var dataPath = FindDataPath(app.Name, app.GetCandidates(progress), progress);

            var appResult = new OfficeSoftwareScanResult
            {
                AppName     = app.Name,
                DataPath    = dataPath ?? string.Empty,
                IsInstalled = dataPath != null,
            };

            if (dataPath == null)
            {
                progress?.Report($"[INFO] {app.Name}: 已安装但未找到数据目录，跳过");
            }
            else
            {
                progress?.Report($"[INFO] {app.Name}: 扫描路径 {dataPath}");
                var files = ScanDirectory(app.Name, dataPath, progress, ct);
                appResult.SensitiveFiles = ApplyThreshold(files, _patterns);

                progress?.Report(appResult.SensitiveFiles.Count == 0
                    ? $"[INFO] {app.Name}: 未发现敏感文件"
                    : $"[WARN] {app.Name}: 发现 {appResult.SensitiveFiles.Count} 个敏感文件");
            }

            results.Add(appResult);
        }

        return results;
    }

    // ------------------------------------------------------------------ Path finders

    private static string? FindDataPath(
        string appName,
        IEnumerable<string> candidates,
        IProgress<string>? progress)
    {
        foreach (var path in candidates)
        {
            try
            {
                progress?.Report($"[INFO] {appName}: 检查 {path}");
                if (!Directory.Exists(path)) continue;

                // If the directory has subdirectories (user accounts), pick most-recently-used
                var subdirs = Directory.GetDirectories(path)
                    .Select(d => new DirectoryInfo(d))
                    .OrderByDescending(d => d.LastWriteTime)
                    .ToList();

                if (subdirs.Count > 0)
                {
                    progress?.Report($"[INFO] {appName}: 找到用户目录 {subdirs[0].FullName}");
                    return subdirs[0].FullName;
                }

                progress?.Report($"[INFO] {appName}: 使用根目录 {path}");
                return path;
            }
            catch { }
        }
        return null;
    }

    // ---- 企业微信 (WeCom / WXWork) ----
    private static IEnumerable<string> GetWeComCandidates(IProgress<string>? progress)
    {
        var docs       = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var user       = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData    = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localData  = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Registry: FileSavePath from user config
        foreach (var hiveKey in new[] {
            @"HKCU\Software\Tencent\WXWork",
            @"HKLM\SOFTWARE\WOW6432Node\Tencent\WXWork" })
        {
            var path = ReadRegistryPath(hiveKey, "FileSavePath");
            if (!string.IsNullOrWhiteSpace(path)) { yield return path!; yield return Path.Combine(path!, "WXWork Files"); }
        }

        yield return Path.Combine(docs,      "WXWork");
        yield return Path.Combine(docs,      "企业微信文件");
        yield return Path.Combine(user,      "Documents", "WXWork");
        yield return Path.Combine(appData,   "Tencent", "WXWork");
        yield return Path.Combine(localData, "Tencent", "WXWork");
        yield return Path.Combine(localData, "WXWork");

        foreach (var drive in FixedDrives())
        {
            yield return Path.Combine(drive, "WXWork");
            yield return Path.Combine(drive, "Users", Environment.UserName, "Documents", "WXWork");
        }
    }

    // ---- 钉钉 (DingTalk) ----
    private static IEnumerable<string> GetDingTalkCandidates(IProgress<string>? progress)
    {
        var user       = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData    = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localData  = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Registry
        foreach (var hiveKey in new[] {
            @"HKCU\Software\DingDing",
            @"HKCU\Software\Alibaba\DingTalk",
            @"HKLM\SOFTWARE\WOW6432Node\DingDing" })
        {
            var path = ReadRegistryPath(hiveKey, "DataPath") ?? ReadRegistryPath(hiveKey, "InstallPath");
            if (!string.IsNullOrWhiteSpace(path)) { yield return path!; }
        }

        yield return Path.Combine(user,      "DingDing");
        yield return Path.Combine(user,      "Documents", "DingDing");
        yield return Path.Combine(appData,   "DingTalk");
        yield return Path.Combine(appData,   "Alibaba", "DingTalk");
        yield return Path.Combine(localData, "DingTalk");
        yield return Path.Combine(localData, "Alibaba", "DingTalk");

        foreach (var drive in FixedDrives())
        {
            yield return Path.Combine(drive, "DingDing");
            yield return Path.Combine(drive, "Users", Environment.UserName, "DingDing");
        }
    }

    // ---- 飞书 / Lark (Feishu) ----
    private static IEnumerable<string> GetFeishuCandidates(IProgress<string>? progress)
    {
        var appData   = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Registry
        foreach (var hiveKey in new[] {
            @"HKCU\Software\Bytedance\Lark",
            @"HKCU\Software\Feishu",
            @"HKCU\Software\Lark" })
        {
            var path = ReadRegistryPath(hiveKey, "DataPath") ?? ReadRegistryPath(hiveKey, "InstallPath");
            if (!string.IsNullOrWhiteSpace(path)) yield return path!;
        }

        yield return Path.Combine(appData,   "Feishu");
        yield return Path.Combine(appData,   "Lark");
        yield return Path.Combine(appData,   "ByteDance", "Feishu");
        yield return Path.Combine(localData, "Feishu");
        yield return Path.Combine(localData, "Lark");
        yield return Path.Combine(localData, "ByteDance", "Feishu");

        foreach (var drive in FixedDrives())
        {
            yield return Path.Combine(drive, "Users", Environment.UserName, "AppData", "Roaming", "Feishu");
            yield return Path.Combine(drive, "Users", Environment.UserName, "AppData", "Local",   "Feishu");
        }
    }

    // ---- Microsoft Teams ----
    private static IEnumerable<string> GetTeamsCandidates(IProgress<string>? progress)
    {
        var appData   = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        yield return Path.Combine(appData,   "Microsoft", "Teams", "Downloads");
        yield return Path.Combine(localData, "Microsoft", "Teams");
        yield return Path.Combine(appData,   "Microsoft", "Teams");

        foreach (var drive in FixedDrives())
        {
            yield return Path.Combine(drive, "Users", Environment.UserName,
                "AppData", "Roaming", "Microsoft", "Teams", "Downloads");
        }
    }

    // ------------------------------------------------------------------ Scan logic

    private List<SensitiveFileResult> ScanDirectory(
        string appName,
        string rootPath,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var results = new List<SensitiveFileResult>();
        var files   = new List<string>();
        EnumerateRecursive(rootPath, files, ct);

        progress?.Report($"[INFO] {appName}: 共 {files.Count} 个文件待扫描");

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            if (!TextExtensions.Contains(Path.GetExtension(file))) continue;

            try
            {
                if (new FileInfo(file).Length > _settings.MaxFileSizeMb * 1024L * 1024) continue;

                var (matched, counts) = ScanFile(file);
                if (matched.Count > 0)
                {
                    results.Add(new SensitiveFileResult
                    {
                        Path = file, MatchedPatterns = matched, MatchCounts = counts
                    });
                    progress?.Report($"[WARN] {appName}: 敏感文件 {Path.GetFileName(file)} " +
                        $"({string.Join(", ", counts.Select(kv => $"{kv.Key}×{kv.Value}"))})");
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"[WARN] {appName}: 无法读取 {Path.GetFileName(file)} – {ex.Message}");
            }
        }

        return results;
    }

    private void EnumerateRecursive(string dir, List<string> files, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        var name = Path.GetFileName(dir);
        if (_settings.ExcludeDirs.Any(ex => name.Equals(ex, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir))   files.Add(f);
            foreach (var d in Directory.EnumerateDirectories(dir)) EnumerateRecursive(d, files, ct);
        }
        catch { }
    }

    private (List<string> types, Dictionary<string, int> counts) ScanFile(string filePath)
    {
        var types  = new List<string>();
        var counts = new Dictionary<string, int>();
        try
        {
            var content = ReadText(filePath);
            if (string.IsNullOrEmpty(content)) return (types, counts);

            foreach (var p in _patterns)
            {
                try
                {
                    var matches = Regex.Matches(content, p.Regex, RegexOptions.None, TimeSpan.FromSeconds(3));
                    if (matches.Count > 0) { types.Add(p.Type); counts[p.Type] = matches.Count; }
                }
                catch { }
            }
        }
        catch { }
        return (types, counts);
    }

    private static string ReadText(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".docx" or ".xlsx") return ReadOfficeXmlText(path);

        const long MaxBytes = 1L * 1024 * 1024;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buf = new byte[(int)Math.Min(fs.Length, MaxBytes)];
        fs.Read(buf, 0, buf.Length);
        try { return Encoding.UTF8.GetString(buf); }
        catch { return Encoding.Default.GetString(buf); }
    }

    private static string ReadOfficeXmlText(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 10L * 1024 * 1024) return string.Empty;
            using var fs  = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var sb  = new StringBuilder(2048);
            var entries = ext == ".docx"
                ? zip.Entries.Where(e => e.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml"))
                : zip.Entries.Where(e => e.FullName.StartsWith("xl/",   StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml"));
            foreach (var entry in entries)
            {
                try
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    sb.Append(Regex.Replace(reader.ReadToEnd(), @"<[^>]+>", " "));
                }
                catch { }
            }
            return sb.ToString();
        }
        catch { return string.Empty; }
    }

    // ------------------------------------------------------------------ Threshold

    private static List<SensitiveFileResult> ApplyThreshold(
        List<SensitiveFileResult> results,
        List<SensitivePattern> patterns)
    {
        var thresholds = patterns.ToDictionary(p => p.Type, p => p.MinFileCount, StringComparer.OrdinalIgnoreCase);
        var fileCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
            foreach (var t in r.MatchedPatterns)
                fileCounts[t] = fileCounts.GetValueOrDefault(t) + 1;

        bool Passes(string type) =>
            !thresholds.TryGetValue(type, out var min) || min <= 0 ||
            fileCounts.GetValueOrDefault(type) >= min;

        return results
            .Select(r =>
            {
                var kept = r.MatchedPatterns.Where(Passes).ToList();
                return new SensitiveFileResult
                {
                    Path = r.Path, MatchedPatterns = kept,
                    MatchCounts = r.MatchCounts.Where(kv => kept.Contains(kv.Key))
                                              .ToDictionary(kv => kv.Key, kv => kv.Value)
                };
            })
            .Where(r => r.MatchedPatterns.Count > 0)
            .ToList();
    }

    // ------------------------------------------------------------------ Helpers

    private static string? ReadRegistryPath(string hiveKey, string valueName)
    {
        try
        {
            var parts = hiveKey.Split('\\', 2);
            var hive  = parts[0] switch
            {
                "HKCU" => Registry.CurrentUser,
                "HKLM" => Registry.LocalMachine,
                _      => null
            };
            if (hive == null || parts.Length < 2) return null;
            using var key = hive.OpenSubKey(parts[1]);
            var val = key?.GetValue(valueName)?.ToString();
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }
        catch { return null; }
    }

    private static IEnumerable<string> FixedDrives() =>
        DriveInfo.GetDrives()
                 .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                 .Select(d => d.RootDirectory.FullName);

    // ------------------------------------------------------------------ Installation check

    /// <summary>
    /// Checks if an app is installed using a three-tier approach:
    /// 1. Windows Uninstall registry (most reliable, cleaned by proper uninstallers)
    /// 2. App-specific registry keys (present even for non-standard installers)
    /// 3. Data directory existence (last resort — only if non-empty, suggesting active use)
    /// </summary>
    private static bool IsAppInstalled(
        string[] uninstallNames, string[] appRegKeys,
        IProgress<string>? progress, string appName)
    {
        // Tier 1: Standard Uninstall registry
        var uninstallPaths = new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (hive, path) in uninstallPaths)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(sub);
                        var displayName = subKey?.GetValue("DisplayName")?.ToString() ?? string.Empty;
                        if (uninstallNames.Any(n => displayName.Contains(n, StringComparison.OrdinalIgnoreCase)))
                        {
                            progress?.Report($"[INFO] {appName}: 在卸载注册表中找到 ({displayName})");
                            return true;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Tier 2: App-specific registry keys
        foreach (var regKey in appRegKeys)
        {
            try
            {
                var parts = regKey.Split('\\', 2);
                var hive  = parts[0] switch
                {
                    "HKCU" => Registry.CurrentUser,
                    "HKLM" => Registry.LocalMachine,
                    _      => null
                };
                if (hive == null || parts.Length < 2) continue;
                using var key = hive.OpenSubKey(parts[1]);
                if (key != null && key.GetValueNames().Length > 0)
                {
                    progress?.Report($"[INFO] {appName}: 在应用注册表中找到 ({regKey})");
                    return true;
                }
            }
            catch { }
        }

        return false;
    }
}
