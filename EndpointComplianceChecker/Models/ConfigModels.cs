using System.Text.Json.Serialization;

namespace EndpointComplianceChecker.Models;

public class SensitivePattern
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("regex")]
    public string Regex { get; set; } = string.Empty;

    /// <summary>
    /// Minimum number of files that must match before this pattern's results appear in the report.
    /// 0 means no threshold (always show).
    /// </summary>
    [JsonPropertyName("minFileCount")]
    public int MinFileCount { get; set; } = 0;
}

public class PirateSoftwareRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("detection")]
    public PirateDetection Detection { get; set; } = new();
}

public class PirateDetection
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "registry";

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("pirate_indicator")]
    public List<string> PirateIndicator { get; set; } = new();
}

public class ScanSettings
{
    [JsonPropertyName("full_scan")]
    public FullScanSettings FullScan { get; set; } = new();

    [JsonPropertyName("wechat_scan")]
    public WeChatScanSettings WeChatScan { get; set; } = new();
}

public class FullScanSettings
{
    [JsonPropertyName("include_drives")]
    public List<string> IncludeDrives { get; set; } = new();

    [JsonPropertyName("exclude_dirs")]
    public List<string> ExcludeDirs { get; set; } = new();

    [JsonPropertyName("max_depth")]
    public int MaxDepth { get; set; } = 5;

    [JsonPropertyName("max_file_size_mb")]
    public int MaxFileSizeMb { get; set; } = 10;

    [JsonPropertyName("text_extensions")]
    public List<string> TextExtensions { get; set; } = new();

    [JsonPropertyName("timeout_minutes")]
    public int TimeoutMinutes { get; set; } = 30;
}

public class WeChatScanSettings
{
    [JsonPropertyName("exclude_dirs")]
    public List<string> ExcludeDirs { get; set; } = new();

    [JsonPropertyName("max_file_size_mb")]
    public int MaxFileSizeMb { get; set; } = 5;
}


public class RemoteControlConfig
{
    [JsonPropertyName("names")]
    public List<string> Names { get; set; } = new();
}
