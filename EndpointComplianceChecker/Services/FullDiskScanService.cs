using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using EndpointComplianceChecker.Models;

namespace EndpointComplianceChecker.Services;

public class FullDiskScanService
{
    private readonly List<SensitivePattern> _patterns;
    private readonly FullScanSettings _settings;

    private volatile int _scannedCount;
    private volatile int _totalCount;

    public int ScannedCount => _scannedCount;
    public int TotalCount => _totalCount;

    public FullDiskScanService(List<SensitivePattern> patterns, FullScanSettings settings)
    {
        _patterns = patterns;
        _settings = settings;
    }

    public List<SensitiveFileResult> Scan(
        IProgress<string>? progress = null,
        IProgress<(int scanned, int total)>? subProgress = null,
        CancellationToken ct = default)
    {
        _scannedCount = 0;
        _totalCount = 0;

        var results = new ConcurrentBag<SensitiveFileResult>();
        var textExts = new HashSet<string>(_settings.TextExtensions, StringComparer.OrdinalIgnoreCase);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(_settings.TimeoutMinutes));
        var linkedToken = timeoutCts.Token;

        // Step 1: collect file list
        progress?.Report("正在枚举待扫描文件列表...");
        var files = CollectFiles(progress, linkedToken);
        _totalCount = files.Count;
        progress?.Report($"共发现 {_totalCount} 个候选文件，开始并行扫描（线程数: {Math.Max(1, Environment.ProcessorCount - 1)}）");

        var parallelOpts = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
            CancellationToken = linkedToken
        };

        try
        {
            Parallel.ForEach(files, parallelOpts, file =>
            {
                if (linkedToken.IsCancellationRequested) return;

                var ext = Path.GetExtension(file);
                if (!textExts.Contains(ext))
                {
                    Interlocked.Increment(ref _scannedCount);
                    return;
                }

                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length > _settings.MaxFileSizeMb * 1024L * 1024)
                    {
                        Interlocked.Increment(ref _scannedCount);
                        return;
                    }

                    var (matched, counts) = ScanFile(file);
                    if (matched.Count > 0)
                    {
                        results.Add(new SensitiveFileResult { Path = file, MatchedPatterns = matched, MatchCounts = counts });
                        var summary = string.Join(", ", counts.Select(kv => $"{kv.Key}×{kv.Value}"));
                        progress?.Report($"[WARN] 发现敏感文件: {file} ({summary})");
                    }
                }
                catch { }
                finally
                {
                    var cnt = Interlocked.Increment(ref _scannedCount);
                    if (cnt % 500 == 0 || cnt == _totalCount)
                        subProgress?.Report((cnt, _totalCount));
                }
            });
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
                progress?.Report("[INFO] 扫描已被用户停止，保存已有结果");
            else
                progress?.Report($"[WARN] 全盘扫描已达超时上限（{_settings.TimeoutMinutes} 分钟），保存已有结果");
        }

        var resultList = ApplyThreshold(results.ToList(), _patterns);
        progress?.Report(resultList.Count == 0
            ? "[INFO] 全盘扫描未发现敏感文件"
            : $"[WARN] 全盘扫描共发现 {resultList.Count} 个敏感文件（已应用阈值过滤）");

        return resultList;
    }

    // ------------------------------------------------------------------ Threshold filter

    private static List<SensitiveFileResult> ApplyThreshold(
        List<SensitiveFileResult> results,
        List<SensitivePattern> patterns)
    {
        var thresholds = patterns.ToDictionary(p => p.Type, p => p.MinFileCount,
            StringComparer.OrdinalIgnoreCase);

        var fileCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
            foreach (var t in r.MatchedPatterns)
            {
                fileCounts.TryGetValue(t, out var c);
                fileCounts[t] = c + 1;
            }

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

    private List<string> CollectFiles(IProgress<string>? progress, CancellationToken ct)
    {
        var files = new List<string>();

        var drives = _settings.IncludeDrives.Count > 0
            ? _settings.IncludeDrives
            : DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();

        foreach (var drive in drives)
        {
            if (ct.IsCancellationRequested) break;
            progress?.Report($"[INFO] 正在枚举磁盘: {drive}");
            CollectFromDir(drive, 0, files, ct);
        }

        return files;
    }

    private void CollectFromDir(string dir, int depth, List<string> files, CancellationToken ct)
    {
        if (depth > _settings.MaxDepth) return;
        if (ct.IsCancellationRequested) return;

        // Skip system/excluded directories
        var dirName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar,
                                                    Path.AltDirectorySeparatorChar));

        if (dirName.StartsWith("$") ||
            dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
            return;

        if (_settings.ExcludeDirs.Any(ex =>
            dir.StartsWith(ex, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
                files.Add(file);

            foreach (var sub in Directory.EnumerateDirectories(dir))
                CollectFromDir(sub, depth + 1, files, ct);
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
                        RegexOptions.None, TimeSpan.FromSeconds(2));
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

        // Office Open XML formats (.docx, .xlsx, .pptx) are ZIP archives containing XML
        if (ext is ".docx" or ".xlsx" or ".pptx")
            return ReadOfficeXmlText(path);

        // Legacy Excel format (.xls) - Binary Interchange File Format (BIFF)
        // Try to extract readable text content from the binary file
        if (ext is ".xls")
            return ReadLegacyExcelText(path);

        const long MaxBytes = 10L * 1024 * 1024;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var readLen = (int)Math.Min(fs.Length, MaxBytes);
        var buf = new byte[readLen];
        fs.Read(buf, 0, readLen);
        try { return Encoding.UTF8.GetString(buf); }
        catch { return Encoding.Default.GetString(buf); }
    }

    /// <summary>
    /// Attempts to extract readable text from legacy Excel (.xls) files using BIFF format.
    /// Since .xls is a proprietary binary format, we read raw bytes and extract printable ASCII/Unicode strings.
    /// This is a best-effort approach without external libraries.
    /// </summary>
    private static string ReadLegacyExcelText(string path)
    {
        const long MaxBytes = 10L * 1024 * 1024;
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length > MaxBytes) return string.Empty;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[Math.Min(fi.Length, MaxBytes)];
            fs.Read(buf, 0, buf.Length);

            // Extract printable strings from the binary data
            // BIFF records contain string data in various encodings
            var sb = new StringBuilder(4096);
            ExtractPrintableStrings(buf, sb, 3); // Check every 3rd byte to find strings
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts printable ASCII and Unicode strings from binary data.
    /// </summary>
    private static void ExtractPrintableStrings(byte[] data, StringBuilder sb, int step)
    {
        // Look for ASCII strings (minimum 4 consecutive printable chars)
        for (int i = 0; i < data.Length - 4; i += step)
        {
            if (IsAsciiString(data, i))
            {
                var str = ExtractAsciiString(data, i);
                if (str.Length >= 4)
                {
                    sb.Append(str);
                    sb.Append(' ');
                    i += str.Length - 1; // Skip ahead
                }
            }
        }

        // Look for Unicode (UTF-16 LE) strings
        for (int i = 0; i < data.Length - 6; i += step * 2)
        {
            if (IsUnicodeString(data, i))
            {
                var str = ExtractUnicodeString(data, i);
                if (str.Length >= 3)
                {
                    sb.Append(str);
                    sb.Append(' ');
                    i += str.Length * 2 - 1;
                }
            }
        }
    }

    private static bool IsAsciiString(byte[] data, int start)
    {
        // Check if there's a plausible string starting at this position
        // A string region typically has: length byte(s) + actual string bytes
        int maxLen = Math.Min(255, data.Length - start - 1);
        int count = 0;
        for (int i = 1; i <= maxLen && i <= 50; i++)
        {
            byte b = data[start + i];
            if (b >= 0x20 && b <= 0x7E) // Printable ASCII
                count++;
            else if (b == 0) // End of string
                break;
            else
                break;
        }
        return count >= 4;
    }

    private static string ExtractAsciiString(byte[] data, int start)
    {
        var sb = new StringBuilder();
        int maxLen = Math.Min(255, data.Length - start - 1);
        for (int i = 1; i <= maxLen; i++)
        {
            byte b = data[start + i];
            if (b >= 0x20 && b <= 0x7E)
                sb.Append((char)b);
            else if (b == 0)
                break;
            else
                break;
        }
        return sb.ToString();
    }

    private static bool IsUnicodeString(byte[] data, int start)
    {
        // Check for UTF-16 LE string (2 bytes per char, string starts with length)
        if (start + 3 > data.Length) return false;
        int maxLen = Math.Min(510, (data.Length - start - 1) / 2);
        int count = 0;
        for (int i = 2; i <= maxLen && start + i * 2 + 1 < data.Length; i += 2)
        {
            char c = (char)(data[start + i] | (data[start + i + 1] << 8));
            if (c >= 0x20 && c <= 0x7E) // Printable ASCII in Unicode
                count++;
            else if (c == 0) // End of string
                break;
            else
                break;
        }
        return count >= 3;
    }

    private static string ExtractUnicodeString(byte[] data, int start)
    {
        var sb = new StringBuilder();
        int maxLen = Math.Min(510, (data.Length - start - 1) / 2);
        for (int i = 2; i <= maxLen && start + i * 2 + 1 < data.Length; i += 2)
        {
            char c = (char)(data[start + i] | (data[start + i + 1] << 8));
            if (c >= 0x20 && c <= 0x7E)
                sb.Append(c);
            else if (c == 0)
                break;
            else
                break;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extracts plain text from Office Open XML documents by reading their embedded XML parts.
    /// .docx: word/document.xml
    /// .xlsx: xl/sharedStrings.xml + xl/worksheets/sheet*.xml
    /// .pptx: ppt/slides/slide*.xml
    /// </summary>
    private static string ReadOfficeXmlText(string path)
    {
        const long MaxZipBytes = 20L * 1024 * 1024;
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length > MaxZipBytes) return string.Empty;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var sb = new StringBuilder(4096);

            // Choose which ZIP entries to read based on file type
            IEnumerable<ZipArchiveEntry> entries = ext switch
            {
                ".docx" => zip.Entries.Where(e =>
                    e.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)),
                ".xlsx" => zip.Entries.Where(e =>
                    (e.FullName.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) &&
                     e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))),
                ".pptx" => zip.Entries.Where(e =>
                    e.FullName.StartsWith("ppt/slides/", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)),
                _ => Enumerable.Empty<ZipArchiveEntry>()
            };

            foreach (var entry in entries)
            {
                if (sb.Length > 2 * 1024 * 1024) break; // cap at 2 MB of text
                try
                {
                    using var entryStream = entry.Open();
                    using var reader = new StreamReader(entryStream, Encoding.UTF8);
                    var xml = reader.ReadToEnd();
                    sb.Append(StripXmlTags(xml));
                    sb.Append(' ');
                }
                catch { /* unreadable entry — skip */ }
            }

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static readonly Regex _xmlTagRegex =
        new(@"<[^>]+>", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    private static string StripXmlTags(string xml)
    {
        try { return _xmlTagRegex.Replace(xml, " "); }
        catch { return xml; }
    }
}
