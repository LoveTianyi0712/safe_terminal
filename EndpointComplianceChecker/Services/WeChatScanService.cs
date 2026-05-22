using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using EndpointComplianceChecker.Models;
using Microsoft.Win32;

namespace EndpointComplianceChecker.Services;

public class WeChatScanService
{
    private readonly List<SensitivePattern> _patterns;
    private readonly WeChatScanSettings _settings;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".csv", ".xml", ".json",
        ".doc", ".md", ".html", ".htm", ".bat", ".sh", ".conf"
    };

    public WeChatScanService(List<SensitivePattern> patterns, WeChatScanSettings settings)
    {
        _patterns = patterns;
        _settings = settings;
    }

    public string? FindWeChatPath(IProgress<string>? progress = null)
    {
        progress?.Report("正在查找微信存储路径...");

        // Try multiple candidate base paths and return the first one that exists
        foreach (var basePath in GetWeChatBaseCandidates(progress))
        {
            progress?.Report($"[INFO] 检查路径: {basePath}");
            if (!Directory.Exists(basePath)) continue;

            // Prefer wxid_ user directory if present
            try
            {
                var subdirs = Directory.GetDirectories(basePath)
                    .Select(d => new DirectoryInfo(d))
                    .Where(d => !d.Name.Equals("All Users", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.Name.StartsWith("wxid_", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(d => d.LastWriteTime)
                    .ToList();

                if (subdirs.Count > 0)
                {
                    var userDir = subdirs[0].FullName;
                    progress?.Report($"[INFO] 找到微信用户目录: {userDir}");
                    return userDir;
                }

                // No subdirectories but the base exists → scan the base itself
                progress?.Report($"[INFO] 使用微信根目录: {basePath}");
                return basePath;
            }
            catch (Exception ex)
            {
                progress?.Report($"[WARN] 枚举目录失败: {ex.Message}");
            }
        }

        progress?.Report("[INFO] 未检测到微信存储目录，跳过微信扫描");
        return null;
    }

    /// <summary>Returns candidate WeChat file storage base directories in priority order.</summary>
    private static IEnumerable<string> GetWeChatBaseCandidates(IProgress<string>? progress)
    {
        var docs   = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var user   = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // 1. Read FileSavePath from registry (user-configured storage location)
        string? regPath = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Tencent\WeChat");
            regPath = key?.GetValue("FileSavePath")?.ToString();
            if (!string.IsNullOrWhiteSpace(regPath))
                progress?.Report($"[INFO] 注册表 FileSavePath = {regPath}");
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(regPath) &&
            !regPath!.Equals("MyDocument", StringComparison.OrdinalIgnoreCase) &&
            Path.IsPathRooted(regPath))
        {
            yield return regPath;
            yield return Path.Combine(regPath, "WeChat Files");
        }

        // 2. Standard locations
        yield return Path.Combine(docs,    "WeChat Files");
        yield return Path.Combine(user,    "Documents", "WeChat Files");
        yield return Path.Combine(appData, "Tencent", "WeChat", "WeChat Files");
        yield return Path.Combine(user,    "WeChat Files");

        // 3. Look for WeChat Files on all fixed drives (handles custom install drives)
        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                     .Select(d => d.RootDirectory.FullName))
        {
            yield return Path.Combine(drive, "WeChat Files");
            yield return Path.Combine(drive, "Users",
                Environment.UserName, "Documents", "WeChat Files");
        }
    }

    public List<SensitiveFileResult> Scan(
        string wechatPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<SensitiveFileResult>();
        progress?.Report($"正在扫描微信路径: {wechatPath}");

        try
        {
            var allFiles = EnumerateFiles(wechatPath, ct);
            progress?.Report($"[INFO] 发现 {allFiles.Count} 个文件待扫描");

            foreach (var file in allFiles)
            {
                if (ct.IsCancellationRequested) break;

                var ext = Path.GetExtension(file);
                if (!TextExtensions.Contains(ext)) continue;

                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length > _settings.MaxFileSizeMb * 1024L * 1024) continue;

                    var (matched, counts) = ScanFile(file);
                    if (matched.Count > 0)
                    {
                        results.Add(new SensitiveFileResult { Path = file, MatchedPatterns = matched, MatchCounts = counts });
                        var summary = string.Join(", ", counts.Select(kv => $"{kv.Key}×{kv.Value}"));
                        progress?.Report($"[WARN] 发现敏感文件: {Path.GetFileName(file)} ({summary})");
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report($"[WARN] 无法读取: {Path.GetFileName(file)} – {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"[WARN] 微信扫描异常: {ex.Message}");
        }

        // Apply per-pattern threshold before returning
        var filtered = ApplyThreshold(results, _patterns);

        progress?.Report(filtered.Count == 0
            ? "[INFO] 微信目录未发现敏感文件"
            : $"[WARN] 微信目录共发现 {filtered.Count} 个敏感文件（已应用阈值过滤）");

        return filtered;
    }

    // ------------------------------------------------------------------ Threshold filter

    private static List<SensitiveFileResult> ApplyThreshold(
        List<SensitiveFileResult> results,
        List<SensitivePattern> patterns)
    {
        // Build a lookup: patternType → minFileCount (0 = no limit)
        var thresholds = patterns.ToDictionary(p => p.Type, p => p.MinFileCount,
            StringComparer.OrdinalIgnoreCase);

        // Count distinct files per pattern type
        var fileCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
            foreach (var t in r.MatchedPatterns)
            {
                fileCounts.TryGetValue(t, out var c);
                fileCounts[t] = c + 1;
            }

        // Determine which pattern types pass the threshold
        bool PassesThreshold(string patternType)
        {
            if (!thresholds.TryGetValue(patternType, out var min) || min <= 0) return true;
            fileCounts.TryGetValue(patternType, out var count);
            return count >= min;
        }

        return results
            .Select(r =>
            {
                var keptTypes = r.MatchedPatterns.Where(PassesThreshold).ToList();
                return new SensitiveFileResult
                {
                    Path = r.Path,
                    MatchedPatterns = keptTypes,
                    MatchCounts = r.MatchCounts
                        .Where(kv => keptTypes.Contains(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => kv.Value)
                };
            })
            .Where(r => r.MatchedPatterns.Count > 0)
            .ToList();
    }

    private List<string> EnumerateFiles(string root, CancellationToken ct)
    {
        var files = new List<string>();
        EnumerateRecursive(root, files, ct);
        return files;
    }

    private void EnumerateRecursive(string dir, List<string> files, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        var dirName = Path.GetFileName(dir);
        if (_settings.ExcludeDirs.Any(ex =>
            dirName.Equals(ex, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
                files.Add(file);
            foreach (var sub in Directory.EnumerateDirectories(dir))
                EnumerateRecursive(sub, files, ct);
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
                    var matches = Regex.Matches(content, p.Regex,
                        RegexOptions.None, TimeSpan.FromSeconds(3));
                    if (matches.Count > 0)
                    {
                        types.Add(p.Type);
                        counts[p.Type] = matches.Count;
                    }
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
        if (ext is ".docx" or ".xlsx" or ".pptx")
            return ReadOfficeXmlText(path);

        const long MaxBytes = 1L * 1024 * 1024;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var readLen = (int)Math.Min(fs.Length, MaxBytes);
        var buf = new byte[readLen];
        fs.Read(buf, 0, readLen);
        try { return Encoding.UTF8.GetString(buf); }
        catch { return Encoding.Default.GetString(buf); }
    }

    private static string ReadOfficeXmlText(string path)
    {
        const long MaxZipBytes = 10L * 1024 * 1024;
        try
        {
            if (new FileInfo(path).Length > MaxZipBytes) return string.Empty;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var sb = new StringBuilder(2048);

            var entries = ext switch
            {
                ".docx" => zip.Entries.Where(e =>
                    e.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)),
                ".xlsx" => zip.Entries.Where(e =>
                    e.FullName.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)),
                _ => Enumerable.Empty<ZipArchiveEntry>()
            };

            foreach (var entry in entries)
            {
                try
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    sb.Append(Regex.Replace(reader.ReadToEnd(), @"<[^>]+>", " "));
                    sb.Append(' ');
                }
                catch { }
            }

            return sb.ToString();
        }
        catch { return string.Empty; }
    }
}
