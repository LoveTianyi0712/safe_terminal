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
