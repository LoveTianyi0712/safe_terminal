namespace EndpointComplianceChecker.Models;

public class BasicInfo
{
    public string ComputerName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string ToolVersion { get; set; } = "1.0.0";
    public string OsVersion { get; set; } = string.Empty;
    public double RamGb { get; set; }
    public double DiskTotalGb { get; set; }
    public double DiskUsagePercent { get; set; }
}
