namespace EndpointComplianceChecker.Models;

public class ComplianceReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public string ToolVersion { get; set; } = "1.3.0";
    public bool IsPartial { get; set; }
    public BasicInfo BasicInfo { get; set; } = new();
    public string OsLicenseStatus { get; set; } = string.Empty;
    public List<SoftwareItem> InstalledSoftware { get; set; } = new();
    public List<SoftwareItem> PirateSoftware { get; set; } = new();
    public List<SoftwareItem> RemoteControlSoftware { get; set; } = new();
    public VirtualMachineResult VirtualMachines { get; set; } = new();
    public NetworkAdaptersResult NetworkAdapters { get; set; } = new();
    public List<SensitiveFileResult> WeChatSensitiveFiles { get; set; } = new();
    public List<OfficeSoftwareScanResult> OfficeSoftwareScans { get; set; } = new();
    public List<SensitiveFileResult> FullScanSensitiveFiles { get; set; } = new();
    public List<string> ErrorLog { get; set; } = new();
}
