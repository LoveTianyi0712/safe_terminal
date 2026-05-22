using System.Net.NetworkInformation;

namespace EndpointComplianceChecker.Models;

public class NetworkAdapterInfo
{
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsWireless    { get; set; }
    public bool IsVirtual     { get; set; }
    public string MacAddress  { get; set; } = string.Empty;
    public List<string> IpAddresses { get; set; } = new();
    public string? DefaultGateway   { get; set; }
    public OperationalStatus Status { get; set; }

    /// <summary>null = not tested, true = reachable, false = unreachable</summary>
    public bool? InternetReachable { get; set; }
    public string InternetTestDetail { get; set; } = string.Empty;
}

public class NetworkAdaptersResult
{
    public List<NetworkAdapterInfo> Adapters { get; set; } = new();

    /// <summary>Total active (Up) adapter count</summary>
    public int ActiveCount => Adapters.Count(a => a.Status == OperationalStatus.Up);

    /// <summary>True when active adapters > 2</summary>
    public bool HasViolation { get; set; }

    /// <summary>True when exactly 1 wired + 1 wireless (dual-network warning)</summary>
    public bool HasWarning { get; set; }

    public string Summary { get; set; } = string.Empty;
}

public class VirtualMachineItem
{
    public string Name      { get; set; } = string.Empty;
    public string Type      { get; set; } = string.Empty;
    public string Evidence  { get; set; } = string.Empty;
    /// <summary>True for Windows built-in Hyper-V (enabled by WSL2/Docker) — warning only, not violation.</summary>
    public bool   IsBuiltIn { get; set; }
}

public class VirtualMachineResult
{
    public List<VirtualMachineItem> Found { get; set; } = new();

    /// <summary>Third-party VM software detected (VMware / VirtualBox / Parallels etc.) — violation.</summary>
    public bool HasViolation => Found.Any(v => !v.IsBuiltIn);

    /// <summary>Windows built-in Hyper-V detected (may be used by WSL2/Docker) — warning only.</summary>
    public bool HasWarning => !HasViolation && Found.Any(v => v.IsBuiltIn);
}
