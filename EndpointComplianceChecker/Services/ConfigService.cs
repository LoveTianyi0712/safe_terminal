using System.IO;
using System.Text.Json;
using EndpointComplianceChecker.Models;

namespace EndpointComplianceChecker.Services;

public class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        AppContext.BaseDirectory, "config");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public List<SensitivePattern> LoadSensitivePatterns()
    {
        return LoadJson<List<SensitivePattern>>("sensitive_patterns.json")
               ?? DefaultSensitivePatterns();
    }

    public void SaveSensitivePatterns(List<SensitivePattern> patterns)
    {
        Directory.CreateDirectory(ConfigDir);
        var path = Path.Combine(ConfigDir, "sensitive_patterns.json");
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(path, JsonSerializer.Serialize(patterns, opts));
    }

    public List<PirateSoftwareRule> LoadPirateSoftwareRules()
    {
        return LoadJson<List<PirateSoftwareRule>>("pirate_software_rules.json")
               ?? new List<PirateSoftwareRule>();
    }

    public ScanSettings LoadScanSettings()
    {
        return LoadJson<ScanSettings>("scan_settings.json")
               ?? DefaultScanSettings();
    }


    public List<string> LoadRemoteControlNames()
    {
        try
        {
            var path = Path.Combine(ConfigDir, "remote_control_apps.json");
            if (!File.Exists(path)) return DefaultRemoteControlNames();
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("names", out var arr))
                return arr.EnumerateArray()
                          .Select(e => e.GetString() ?? "")
                          .Where(s => !string.IsNullOrEmpty(s))
                          .ToList();
        }
        catch { }
        return DefaultRemoteControlNames();
    }

    private T? LoadJson<T>(string fileName)
    {
        try
        {
            var path = Path.Combine(ConfigDir, fileName);
            if (!File.Exists(path)) return default;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch { return default; }
    }

    private static List<SensitivePattern> DefaultSensitivePatterns() => new()
    {
        new() { Type = "手机号",   Regex = @"(?<!\d)1[3-9]\d{9}(?!\d)" },
        new() { Type = "身份证号", Regex = @"\b[1-9]\d{5}(18|19|20)\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])\d{3}[\dXx]\b" },
        new() { Type = "银行卡号", Regex = @"(?<!\d)(?:4\d{15}|5[1-5]\d{14}|62\d{14,17}|3[47]\d{13}|35\d{14})(?!\d)" },
        new() { Type = "IPv4地址", Regex = @"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b" },
        new() { Type = "住址",     Regex = @"[\u4e00-\u9fa5]{2,}(省|自治区)[\u4e00-\u9fa5]{2,}(市|州)[\u4e00-\u9fa5]{2,}(区|县|镇|乡)" },
    };

    private static ScanSettings DefaultScanSettings() => new()
    {
        FullScan = new FullScanSettings
        {
            ExcludeDirs = new() { @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)" },
            MaxDepth = 5,
            MaxFileSizeMb = 10,
            TextExtensions = new() { ".txt", ".log", ".csv", ".ini", ".json", ".xml" },
            TimeoutMinutes = 30
        },
        WeChatScan = new WeChatScanSettings
        {
            ExcludeDirs = new() { "Image", "Video", "Backup" },
            MaxFileSizeMb = 5
        }
    };

    private static List<string> DefaultRemoteControlNames() =>
        new() { "todesk", "向日葵", "sunlogin", "teamviewer", "anydesk" };
}
