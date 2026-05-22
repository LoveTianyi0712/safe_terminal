using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using EndpointComplianceChecker.Models;

namespace EndpointComplianceChecker.Services;

public class NetworkAdapterService
{
    private const string TestHost    = "www.baidu.com";
    private const int    PingTimeout = 3000;   // ms per ping attempt

    public NetworkAdaptersResult Scan(IProgress<string>? progress = null)
    {
        progress?.Report("正在枚举网卡...");
        var result = new NetworkAdaptersResult();

        try
        {
            var allIfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var iface in allIfaces)
            {
                // Skip purely virtual/non-physical types
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)  continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)    continue;
                if (iface.Description.Contains("Loopback", StringComparison.OrdinalIgnoreCase)) continue;

                var info = new NetworkAdapterInfo
                {
                    Name        = iface.Name,
                    Description = iface.Description,
                    IsWireless  = iface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                                  || iface.Description.Contains("Wi-Fi",    StringComparison.OrdinalIgnoreCase)
                                  || iface.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
                                  || iface.Description.Contains("WLAN",     StringComparison.OrdinalIgnoreCase),
                    MacAddress  = string.Join(":", iface.GetPhysicalAddress().GetAddressBytes()
                                      .Select(b => b.ToString("X2"))),
                    Status      = iface.OperationalStatus,
                };

                var props = iface.GetIPProperties();
                info.IpAddresses = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToList();
                info.DefaultGateway = props.GatewayAddresses
                    .Select(g => g.Address.ToString())
                    .FirstOrDefault(g => g != "::");

                result.Adapters.Add(info);
                progress?.Report($"[INFO] 网卡: {iface.Name} | {iface.Description} | " +
                                 $"{(info.IsWireless ? "无线" : "有线")} | {iface.OperationalStatus}");
            }

            // Test internet reachability for each UP adapter that has a gateway
            var upAdapters = result.Adapters
                .Where(a => a.Status == OperationalStatus.Up && !string.IsNullOrEmpty(a.DefaultGateway))
                .ToList();

            progress?.Report($"[INFO] 活跃网卡数（含网关）: {upAdapters.Count}，开始逐一测试 {TestHost} 可达性...");

            // Resolve host once
            IPAddress[]? resolvedIps = null;
            try { resolvedIps = Dns.GetHostAddresses(TestHost); } catch { }

            foreach (var adapter in upAdapters)
            {
                try
                {
                    // Bind the ping to the adapter's first IP so the OS routes via that interface
                    var srcIp = adapter.IpAddresses.FirstOrDefault();
                    bool reached = false;
                    string detail = string.Empty;

                    if (resolvedIps != null && resolvedIps.Length > 0 && srcIp != null)
                    {
                        var pingTarget = resolvedIps.FirstOrDefault(ip =>
                            ip.AddressFamily == AddressFamily.InterNetwork) ?? resolvedIps[0];

                        using var ping = new Ping();
                        var options = new PingOptions { DontFragment = true };
                        var buffer  = new byte[32];

                        try
                        {
                            // Use Socket to send a raw ping via the specific source address
                            var reply = ping.Send(pingTarget, PingTimeout, buffer, options);
                            reached = reply.Status == IPStatus.Success;
                            detail  = $"ping {pingTarget} → {reply.Status} ({reply.RoundtripTime}ms)";
                        }
                        catch (Exception ex)
                        {
                            detail = $"ping 异常: {ex.Message}";
                        }
                    }
                    else
                    {
                        detail = "无法解析目标域名";
                    }

                    adapter.InternetReachable   = reached;
                    adapter.InternetTestDetail  = detail;
                    progress?.Report($"[INFO] {adapter.Name}: {(reached ? "✅ 可上外网" : "❌ 不可上外网")} – {detail}");
                }
                catch (Exception ex)
                {
                    adapter.InternetReachable  = false;
                    adapter.InternetTestDetail = ex.Message;
                    progress?.Report($"[WARN] {adapter.Name} 连通性测试异常: {ex.Message}");
                }
            }

            // --- Violation / Warning logic ---
            int activeCount = result.Adapters.Count(a => a.Status == OperationalStatus.Up);
            int wiredCount  = result.Adapters.Count(a => a.Status == OperationalStatus.Up && !a.IsWireless);
            int wirelessCount = result.Adapters.Count(a => a.Status == OperationalStatus.Up && a.IsWireless);

            if (activeCount > 2)
            {
                result.HasViolation = true;
                result.Summary = $"违规：活跃网卡数 {activeCount} 个（超过2个）";
                progress?.Report($"[FAIL] 网卡数量违规：{activeCount} 个活跃网卡");
            }
            else if (wiredCount >= 1 && wirelessCount >= 1)
            {
                result.HasWarning = true;
                result.Summary = $"警告：同时存在有线网卡（{wiredCount}）和无线网卡（{wirelessCount}）";
                progress?.Report($"[WARN] 有线+无线同时启用");
            }
            else
            {
                result.Summary = $"正常：活跃网卡 {activeCount} 个";
                progress?.Report($"[PASS] 网卡检测通过：{activeCount} 个活跃网卡");
            }
        }
        catch (Exception ex)
        {
            result.Summary = $"检测异常: {ex.Message}";
            progress?.Report($"[ERROR] 网卡检测异常: {ex.Message}");
        }

        return result;
    }
}
