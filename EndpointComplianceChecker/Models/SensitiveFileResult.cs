namespace EndpointComplianceChecker.Models;

public class SensitiveFileResult
{
    public string Path { get; set; } = string.Empty;

    /// <summary>Pattern type names that matched (may contain duplicates, one per match).</summary>
    public List<string> MatchedPatterns { get; set; } = new();

    /// <summary>
    /// Per-pattern match count: key = pattern type, value = number of regex hits in this file.
    /// Populated by scan services.
    /// </summary>
    public Dictionary<string, int> MatchCounts { get; set; } = new();
}

/// <summary>Scan result for a single office/IM application.</summary>
public class OfficeSoftwareScanResult
{
    /// <summary>App display name, e.g. "企业微信".</summary>
    public string AppName  { get; set; } = string.Empty;

    /// <summary>Detected data directory that was scanned. Empty if not installed.</summary>
    public string DataPath { get; set; } = string.Empty;

    /// <summary>Whether the application was found on this machine.</summary>
    public bool IsInstalled { get; set; }

    public List<SensitiveFileResult> SensitiveFiles { get; set; } = new();
}
