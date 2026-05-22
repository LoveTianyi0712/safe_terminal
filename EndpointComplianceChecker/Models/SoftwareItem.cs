namespace EndpointComplianceChecker.Models;

public class SoftwareItem
{
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? InstallLocation { get; set; }
    public string? Publisher { get; set; }
}
