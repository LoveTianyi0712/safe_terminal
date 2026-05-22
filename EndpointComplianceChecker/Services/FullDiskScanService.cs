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

        const long MaxBytes = 10L * 1024 * 1024;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var readLen = (int)Math.Min(fs.Length, MaxBytes);
        var buf = new byte[readLen];
        fs.Read(buf, 0, readLen);
        try { return Encoding.UTF8.GetString(buf); }
        catch { return Encoding.Default.GetString(buf); }
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
