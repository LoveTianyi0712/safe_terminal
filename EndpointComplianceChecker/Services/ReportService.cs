using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using EndpointComplianceChecker.Models;

namespace EndpointComplianceChecker.Services;

public class ReportService
{
    /// <param name="htmlFilePath">Full path of the HTML file to write (chosen by SaveFileDialog).</param>
    public (string htmlPath, string jsonPath) SaveReport(ComplianceReport report, string htmlFilePath)
    {
        var dir = Path.GetDirectoryName(htmlFilePath)!;
        Directory.CreateDirectory(dir);

        var jsonPath = Path.ChangeExtension(htmlFilePath, ".json");

        File.WriteAllText(htmlFilePath, GenerateHtml(report), Encoding.UTF8);
        File.WriteAllText(jsonPath,     GenerateJson(report), Encoding.UTF8);

        return (htmlFilePath, jsonPath);
    }

    // ------------------------------------------------------------------ JSON
    public string GenerateJson(ComplianceReport report)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var data = new
        {
            generated_at = report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            tool_version = report.ToolVersion,
            is_partial = report.IsPartial,
            basic_info = new
            {
                computer_name = report.BasicInfo.ComputerName,
                username = report.BasicInfo.Username,
                ip_address = report.BasicInfo.IpAddress,
                mac_address = report.BasicInfo.MacAddress,
                tool_version = report.BasicInfo.ToolVersion,
                os_version = report.BasicInfo.OsVersion,
                ram_gb = report.BasicInfo.RamGb,
                disk_total_gb = report.BasicInfo.DiskTotalGb,
                disk_usage_percent = report.BasicInfo.DiskUsagePercent
            },
            os_license_status = report.OsLicenseStatus,
            installed_software_count = report.InstalledSoftware.Count,
            pirate_software = report.PirateSoftware.Select(s =>
                new { name = s.Name, version = s.Version, install_location = s.InstallLocation }),
            remote_control_software = report.RemoteControlSoftware.Select(s =>
                new { name = s.Name, version = s.Version, install_location = s.InstallLocation }),
            wechat_sensitive_files = report.WeChatSensitiveFiles.Select(f =>
                new { path = f.Path, matched_patterns = f.MatchedPatterns }),
            full_scan_sensitive_files = report.FullScanSensitiveFiles.Select(f =>
                new { path = f.Path, matched_patterns = f.MatchedPatterns }),
            error_log = report.ErrorLog
        };

        return JsonSerializer.Serialize(data, opts);
    }

    // ------------------------------------------------------------------ HTML
    public string GenerateHtml(ComplianceReport report)
    {
        var sb = new StringBuilder(32768);

        bool hasViolation = report.PirateSoftware.Count > 0 ||
                            report.WeChatSensitiveFiles.Count > 0 ||
                            report.FullScanSensitiveFiles.Count > 0;
        bool hasWarning = report.RemoteControlSoftware.Count > 0 ||
                          report.OsLicenseStatus.Contains("复核") ||
                          report.OsLicenseStatus.Contains("KMS") ||
                          report.OsLicenseStatus.Contains("未激活");

        string overallBadge = hasViolation ? "danger" : hasWarning ? "warning" : "success";
        string overallText  = hasViolation ? "存在违规项" : hasWarning ? "需人工复核" : "合规";

        double diskPct = Math.Clamp(report.BasicInfo.DiskUsagePercent, 0, 100);

        // Pre-compute conditional snippets to avoid nested raw-string issues
        string partialBanner = report.IsPartial
            ? "<div class=\"partial-banner\">⚠️ 注意：本次扫描已被中断，报告仅包含已完成的检查项结果，结果可能不完整。</div>"
            : string.Empty;

        string licenseWarning = (report.OsLicenseStatus.Contains("复核") ||
                                 report.OsLicenseStatus.Contains("KMS"))
            ? "<p style=\"color:#856404;margin-top:8px;\">⚠️ 建议联系 IT 管理员核实操作系统授权情况。</p>"
            : string.Empty;

        string partialHeaderNote = report.IsPartial
            ? " &nbsp;|&nbsp; ⚠️ 本报告为部分结果（扫描已中断）"
            : string.Empty;

        string licColor = report.OsLicenseStatus.Contains("正版") ? "success"
                        : report.OsLicenseStatus.Contains("KMS") ? "warning" : "danger";

        // ---- HEAD + CSS ----
        sb.Append("""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
""");
        sb.AppendLine($"<title>终端合规性检查报告 – {H(report.BasicInfo.ComputerName)}</title>");
        sb.Append("""
<style>
:root{--primary:#1E3A5F;--accent:#2E86AB;--success:#27AE60;--warning:#F39C12;--danger:#E74C3C;--bg:#F0F4F8;--card:#FFF;--border:#D1D9E0;--text:#2D3748;--muted:#718096}
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:"Microsoft YaHei UI","Segoe UI",sans-serif;font-size:14px;background:var(--bg);color:var(--text);line-height:1.6}
.hdr{background:var(--primary);color:#fff;padding:24px 40px}.hdr h1{font-size:22px;font-weight:700}
.hdr .meta{font-size:12px;color:#A8BED4;margin-top:4px}
.container{max-width:1100px;margin:0 auto;padding:24px 24px 48px}
.sg{display:grid;grid-template-columns:repeat(auto-fit,minmax(175px,1fr));gap:16px;margin-bottom:24px}
.sc{background:var(--card);border-radius:10px;padding:18px 20px;border-left:4px solid var(--accent);box-shadow:0 1px 4px rgba(0,0,0,.08)}
.sc .v{font-size:28px;font-weight:700;color:var(--primary)}.sc .l{font-size:12px;color:var(--muted);margin-top:2px}
.sc.danger{border-left-color:var(--danger)}.sc.danger .v{color:var(--danger)}
.sc.warning{border-left-color:var(--warning)}.sc.warning .v{color:var(--warning)}
.sc.success{border-left-color:var(--success)}.sc.success .v{color:var(--success)}
.sec{background:var(--card);border-radius:10px;margin-bottom:20px;box-shadow:0 1px 4px rgba(0,0,0,.07);overflow:hidden}
.sh{padding:14px 22px;cursor:pointer;display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid var(--border);user-select:none}
.sh:hover{background:#f7fafc}.sh h2{font-size:15px;font-weight:600;display:flex;align-items:center;gap:10px}
.sb{padding:20px 22px}
.badge{display:inline-block;padding:2px 10px;border-radius:12px;font-size:12px;font-weight:600}
.badge.success{background:#D4EDDA;color:#155724}.badge.warning{background:#FFF3CD;color:#856404}
.badge.danger{background:#F8D7DA;color:#721C24}.badge.info{background:#D1ECF1;color:#0C5460}
table{width:100%;border-collapse:collapse}
th{background:#F7FAFC;font-weight:600;color:var(--muted);font-size:12px;text-transform:uppercase;letter-spacing:.5px;padding:10px 14px;text-align:left;border-bottom:1px solid var(--border)}
td{padding:10px 14px;border-bottom:1px solid #EDF2F7;vertical-align:top}
tr:last-child td{border-bottom:none}tr:hover td{background:#F7FAFC}
.path{font-family:Consolas,monospace;font-size:12px;cursor:pointer;word-break:break-all}
.path:hover{color:var(--accent);text-decoration:underline}
.pt{display:inline-block;background:#FFF3CD;color:#856404;padding:1px 8px;border-radius:10px;font-size:11px;margin:1px 2px}
.pb{background:#E2E8F0;border-radius:4px;height:8px;overflow:hidden;margin-top:6px}
.pf{height:100%;background:linear-gradient(90deg,var(--accent),#27AE60);border-radius:4px}
.em{color:var(--muted);font-style:italic;padding:8px 0}
.obadge{font-size:16px;padding:6px 20px;border-radius:20px;font-weight:700}
.partial-banner{background:#FFF3CD;border:1px solid #FFEEBA;color:#856404;padding:10px 22px;font-size:13px}
.chv{transition:transform .2s;font-size:12px}
.collapsed .chv{transform:rotate(-90deg)}
.collapsed .sb{display:none}
</style>
</head>
<body>
""");

        // ---- HEADER ----
        sb.AppendLine($"""
<div class="hdr">
  <h1>🛡️ 终端合规性检查报告</h1>
  <div class="meta">生成时间：{report.GeneratedAt:yyyy-MM-dd HH:mm:ss} &nbsp;|&nbsp; 工具版本：v{H(report.ToolVersion)} &nbsp;|&nbsp; 计算机：{H(report.BasicInfo.ComputerName)}{partialHeaderNote}</div>
</div>
{partialBanner}
<div class="container">
<div style="text-align:center;margin:24px 0 28px;">
  <span class="badge {overallBadge} obadge">总体合规状态：{overallText}</span>
</div>
""");

        // ---- SUMMARY CARDS ----
        int totalItems = report.PirateSoftware.Count + report.RemoteControlSoftware.Count +
                         report.WeChatSensitiveFiles.Count + report.FullScanSensitiveFiles.Count;

        sb.AppendLine("<div class=\"sg\">");
        sb.AppendLine(SC(totalItems.ToString(), "总违规/风险项", totalItems > 0 ? "danger" : "success"));
        sb.AppendLine(SC(report.InstalledSoftware.Count.ToString(), "已安装软件", "info"));
        sb.AppendLine(SC(report.PirateSoftware.Count.ToString(), "疑似盗版软件",
            report.PirateSoftware.Count > 0 ? "danger" : "success"));
        sb.AppendLine(SC(report.RemoteControlSoftware.Count.ToString(), "远程控制软件",
            report.RemoteControlSoftware.Count > 0 ? "warning" : "success"));
        sb.AppendLine(SC(report.WeChatSensitiveFiles.Count.ToString(), "微信敏感文件",
            report.WeChatSensitiveFiles.Count > 0 ? "danger" : "success"));
        sb.AppendLine(SC(report.FullScanSensitiveFiles.Count.ToString(), "全盘敏感文件",
            report.FullScanSensitiveFiles.Count > 0 ? "danger" : "success"));
        sb.AppendLine("</div>");

        // ---- SECTION 1: Basic Info ----
        sb.AppendLine($"""
<div class="sec">
  <div class="sh" onclick="toggle(this)"><h2>💻 终端基本信息</h2><span class="chv">▼</span></div>
  <div class="sb"><table>
    <tr><th>字段</th><th>值</th></tr>
    <tr><td>计算机名</td><td><strong>{H(report.BasicInfo.ComputerName)}</strong></td></tr>
    <tr><td>登录用户</td><td>{H(report.BasicInfo.Username)}</td></tr>
    <tr><td>IP 地址</td><td>{H(report.BasicInfo.IpAddress)}</td></tr>
    <tr><td>MAC 地址</td><td><code>{H(report.BasicInfo.MacAddress)}</code></td></tr>
    <tr><td>操作系统</td><td>{H(report.BasicInfo.OsVersion)}</td></tr>
    <tr><td>物理内存</td><td>{report.BasicInfo.RamGb} GB</td></tr>
    <tr><td>磁盘 C: 总容量</td><td>{report.BasicInfo.DiskTotalGb} GB</td></tr>
    <tr><td>磁盘 C: 使用率</td><td>{diskPct}%<div class="pb"><div class="pf" style="width:{diskPct}%"></div></div></td></tr>
    <tr><td>工具版本</td><td>v{H(report.BasicInfo.ToolVersion)}</td></tr>
  </table></div>
</div>
""");

        // ---- SECTION 2: OS License ----
        sb.AppendLine($"""
<div class="sec">
  <div class="sh" onclick="toggle(this)">
    <h2>🔑 操作系统授权状态 &nbsp;<span class="badge {licColor}">{H(report.OsLicenseStatus)}</span></h2>
    <span class="chv">▼</span>
  </div>
  <div class="sb">
    <p>授权状态：<span class="badge {licColor}">{H(report.OsLicenseStatus)}</span></p>
    {licenseWarning}
  </div>
</div>
""");

        // ---- SECTION 3: Installed Software ----
        sb.AppendLine($"""
<div class="sec">
  <div class="sh" onclick="toggle(this)">
    <h2>📦 已安装软件清单 &nbsp;<span class="badge info">共 {report.InstalledSoftware.Count} 个</span></h2>
    <span class="chv">▼</span>
  </div>
  <div class="sb">
""");
        if (report.InstalledSoftware.Count > 0)
        {
            sb.AppendLine("<table><tr><th>#</th><th>软件名称</th><th>版本</th><th>发布商</th></tr>");
            int idx = 1;
            foreach (var sw in report.InstalledSoftware.OrderBy(s => s.Name))
                sb.AppendLine($"<tr><td>{idx++}</td><td>{H(sw.Name)}</td><td>{H(sw.Version ?? "–")}</td><td>{H(sw.Publisher ?? "–")}</td></tr>");
            sb.AppendLine("</table>");
        }
        else sb.AppendLine("<p class=\"em\">暂无数据</p>");
        sb.AppendLine("</div></div>");

        // ---- SECTION 4: Pirate Software ----
        string pirateBadge = report.PirateSoftware.Count > 0 ? "danger" : "success";
        string pirateTxt   = report.PirateSoftware.Count > 0 ? $"发现 {report.PirateSoftware.Count} 项" : "未发现";
        sb.AppendLine($"""
<div class="sec">
  <div class="sh" onclick="toggle(this)">
    <h2>🚫 盗版软件检测 &nbsp;<span class="badge {pirateBadge}">{pirateTxt}</span></h2>
    <span class="chv">▼</span>
  </div>
  <div class="sb">
""");
        if (report.PirateSoftware.Count > 0)
        {
            sb.AppendLine("<table><tr><th>软件名称</th><th>版本</th><th>安装路径</th></tr>");
            foreach (var sw in report.PirateSoftware)
                sb.AppendLine($"<tr><td><strong style=\"color:var(--danger)\">{H(sw.Name)}</strong></td><td>{H(sw.Version ?? "–")}</td><td class=\"path\" onclick=\"cp(this)\">{H(sw.InstallLocation ?? "–")}</td></tr>");
            sb.AppendLine("</table>");
        }
        else sb.AppendLine("<p class=\"em\">✅ 未发现盗版软件</p>");
        sb.AppendLine("</div></div>");

        // ---- SECTION 5: Remote Control Software ----
        string remoteBadge = report.RemoteControlSoftware.Count > 0 ? "warning" : "success";
        string remoteTxt   = report.RemoteControlSoftware.Count > 0 ? $"发现 {report.RemoteControlSoftware.Count} 项" : "未发现";
        sb.AppendLine($"""
<div class="sec">
  <div class="sh" onclick="toggle(this)">
    <h2>🖥️ 远程控制软件 &nbsp;<span class="badge {remoteBadge}">{remoteTxt}</span></h2>
    <span class="chv">▼</span>
  </div>
  <div class="sb">
""");
        if (report.RemoteControlSoftware.Count > 0)
        {
            sb.AppendLine("<table><tr><th>软件名称</th><th>版本</th><th>安装路径</th></tr>");
            foreach (var sw in report.RemoteControlSoftware)
                sb.AppendLine($"<tr><td><strong style=\"color:var(--warning)\">{H(sw.Name)}</strong></td><td>{H(sw.Version ?? "–")}</td><td class=\"path\" onclick=\"cp(this)\">{H(sw.InstallLocation ?? "–")}</td></tr>");
            sb.AppendLine("</table>");
        }
        else sb.AppendLine("<p class=\"em\">✅ 未发现远程控制软件</p>");
        sb.AppendLine("</div></div>");

        // ---- SECTION 6: Virtual Machine Detection ----
        string vmBadge = report.VirtualMachines.HasViolation ? "danger" : "success";
        string vmTxt   = report.VirtualMachines.HasViolation ? $"违规：发现 {report.VirtualMachines.Found.Count} 项" : "未发现";
        sb.AppendLine($"""
<div class="sec">
  <div class="sh" onclick="toggle(this)">
    <h2>💻 虚拟机安装检测 &nbsp;<span class="badge {vmBadge}">{vmTxt}</span></h2>
    <span class="chv">▼</span>
  </div>
  <div class="sb">
""");
        if (report.VirtualMachines.HasViolation)
        {
            sb.AppendLine("<table><tr><th>软件名称</th><th>类型</th><th>检测依据</th></tr>");
            foreach (var vm in report.VirtualMachines.Found)
                sb.AppendLine($"<tr><td><strong style=\"color:var(--danger)\">{H(vm.Name)}</strong></td><td>{H(vm.Type)}</td><td class=\"path\">{H(vm.Evidence)}</td></tr>");
            sb.AppendLine("</table>");
        }
        else sb.AppendLine("<p class=\"em\">✅ 未检测到虚拟机软件</p>");
        sb.AppendLine("</div></div>");

        // ---- SECTION 7: Network Adapter Detection ----
        var netR = report.NetworkAdapters;
        string netBadge = netR.HasViolation ? "danger" : netR.HasWarning ? "warning" : "success";
        string netTxt   = netR.HasViolation ? $"违规：{netR.Summary}" : netR.HasWarning ? $"警告：{netR.Summary}" : $"正常：{netR.Summary}";
        sb.AppendLine($"""
<div class="sec">
  <div class="sh" onclick="toggle(this)">
    <h2>🌐 网卡检测 &nbsp;<span class="badge {netBadge}">{H(netR.HasViolation ? $"违规 {netR.ActiveCount}个" : netR.HasWarning ? "有线+无线并存" : $"正常 {netR.ActiveCount}个")}</span></h2>
    <span class="chv">▼</span>
  </div>
  <div class="sb">
""");
        if (netR.HasViolation || netR.HasWarning)
            sb.AppendLine($"<p style=\"color:{(netR.HasViolation ? "var(--danger)" : "var(--warning)")};font-weight:600;margin-bottom:12px;\">{H(netR.Summary)}</p>");

        if (netR.Adapters.Count > 0)
        {
            sb.AppendLine("<table><tr><th>网卡名称</th><th>类型</th><th>状态</th><th>IP 地址</th><th>默认网关</th><th>外网可达</th></tr>");
            foreach (var ad in netR.Adapters)
            {
                var typeLabel  = ad.IsWireless ? "无线" : "有线";
                var statusCss  = ad.Status == System.Net.NetworkInformation.OperationalStatus.Up ? "color:var(--success)" : "color:var(--muted)";
                var statusTxt  = ad.Status.ToString();
                var ipTxt      = ad.IpAddresses.Count > 0 ? string.Join(", ", ad.IpAddresses) : "–";
                var gwTxt      = ad.DefaultGateway ?? "–";
                var reachTxt   = ad.InternetReachable == null ? "–"
                                 : ad.InternetReachable == true ? "✅ 可达" : "❌ 不可达";
                var reachCss   = ad.InternetReachable == true ? "color:var(--success)" : "color:var(--muted)";
                sb.AppendLine($"<tr><td>{H(ad.Description)}<br><small style=\"color:var(--muted)\">{H(ad.Name)}</small></td>" +
                              $"<td>{typeLabel}</td>" +
                              $"<td style=\"{statusCss}\">{statusTxt}</td>" +
                              $"<td>{H(ipTxt)}</td><td>{H(gwTxt)}</td>" +
                              $"<td style=\"{reachCss}\">{reachTxt}</td></tr>");
            }
            sb.AppendLine("</table>");
            if (!string.IsNullOrEmpty(netR.Adapters.FirstOrDefault(a => a.InternetReachable != null)?.InternetTestDetail))
            {
                sb.AppendLine($"<p style=\"font-size:11px;color:var(--muted);margin-top:8px;\">测试方式：ping {H("www.baidu.com")}，超时 3s</p>");
            }
        }
        else sb.AppendLine("<p class=\"em\">未检测到网卡</p>");
        sb.AppendLine("</div></div>");

        // ---- SECTION 8: WeChat Sensitive Files (原 Section 6) ----
        string wxBadge = report.WeChatSensitiveFiles.Count > 0 ? "danger" : "success";
        string wxTxt   = report.WeChatSensitiveFiles.Count > 0 ? $"发现 {report.WeChatSensitiveFiles.Count} 个" : "未发现";
        sb.AppendLine($"""
<div class="sec">
  <div class="sh" onclick="toggle(this)">
    <h2>💬 微信路径敏感文件 &nbsp;<span class="badge {wxBadge}">{wxTxt}</span></h2>
    <span class="chv">▼</span>
  </div>
  <div class="sb">
""");
        AppendTypeBreakdown(sb, report.WeChatSensitiveFiles);
        AppendFileTable(sb, report.WeChatSensitiveFiles);
        sb.AppendLine("</div></div>");

        // ---- SECTION 7: Full Scan Sensitive Files ----
        string fsBadge = report.FullScanSensitiveFiles.Count > 0 ? "danger" : "success";
        string fsTxt   = report.FullScanSensitiveFiles.Count > 0 ? $"发现 {report.FullScanSensitiveFiles.Count} 个" : "未发现";
        sb.AppendLine($"""
<div class="sec">
  <div class="sh" onclick="toggle(this)">
    <h2>🔍 全盘敏感文件扫描 &nbsp;<span class="badge {fsBadge}">{fsTxt}</span></h2>
    <span class="chv">▼</span>
  </div>
  <div class="sb">
""");
        AppendTypeBreakdown(sb, report.FullScanSensitiveFiles);
        AppendFileTable(sb, report.FullScanSensitiveFiles);
        sb.AppendLine("</div></div>");

        // ---- Error Log ----
        if (report.ErrorLog.Count > 0)
        {
            sb.AppendLine("""
<div class="sec">
  <div class="sh" onclick="toggle(this)"><h2>⚠️ 错误日志</h2><span class="chv">▼</span></div>
  <div class="sb"><pre style="font-size:11px;color:var(--muted);white-space:pre-wrap;word-break:break-all;">
""");
            foreach (var err in report.ErrorLog)
                sb.AppendLine(H(err));
            sb.AppendLine("</pre></div></div>");
        }

        // ---- Footer ----
        sb.AppendLine($"""
</div>
<div style="text-align:center;padding:20px;color:var(--muted);font-size:12px;">
  终端合规性检查工具 v{H(report.ToolVersion)} &nbsp;|&nbsp; 报告生成于 {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}
</div>
<div id="toast" style="position:fixed;bottom:30px;right:30px;background:#333;color:#fff;padding:10px 20px;border-radius:8px;font-size:13px;display:none;z-index:9999">路径已复制到剪贴板</div>
""");

        // ---- Scripts (non-interpolated to avoid brace-escaping issues) ----
        sb.AppendLine("""
<script>
function toggle(h){h.parentElement.classList.toggle('collapsed')}
function cp(el){var t=el.textContent||el.innerText;if(navigator.clipboard){navigator.clipboard.writeText(t).then(showToast)}else{var x=document.createElement('textarea');x.value=t;document.body.appendChild(x);x.select();document.execCommand('copy');document.body.removeChild(x);showToast()}}
function showToast(){var t=document.getElementById('toast');t.style.display='block';setTimeout(function(){t.style.display='none'},2000)}
</script>
</body></html>
""");

        return sb.ToString();
    }

    // ---- Helpers ----

    /// <summary>Renders a per-type hit count summary before the file list.</summary>
    private static void AppendTypeBreakdown(StringBuilder sb, List<SensitiveFileResult> files)
    {
        if (files.Count == 0) return;

        // Aggregate per type: fileCount = distinct files, hitCount = total regex matches
        var byType = files
            .SelectMany(f => f.MatchCounts.Count > 0
                ? f.MatchCounts.Select(kv => (Pattern: kv.Key, File: f.Path, Hits: kv.Value))
                : f.MatchedPatterns.Select(p => (Pattern: p, File: f.Path, Hits: 1)))
            .GroupBy(x => x.Pattern)
            .Select(g => (
                Pattern: g.Key,
                FileCount: g.Select(x => x.File).Distinct().Count(),
                HitCount: g.Sum(x => x.Hits)))
            .OrderByDescending(x => x.HitCount)
            .ToList();

        sb.AppendLine("<div style=\"margin-bottom:14px;\">");
        sb.AppendLine("<p style=\"font-size:12px;color:#718096;margin-bottom:8px;\">按数据类型汇总：</p>");
        sb.AppendLine("<div style=\"display:flex;flex-wrap:wrap;gap:8px;\">");
        foreach (var (pattern, fileCount, hitCount) in byType)
        {
            sb.AppendLine($"<div style=\"background:#EBF8FF;border:1px solid #BEE3F8;border-radius:6px;padding:8px 14px;\">" +
                          $"<div style=\"font-weight:600;font-size:13px;color:#2B6CB0;\">{H(pattern)}</div>" +
                          $"<div style=\"font-size:11px;color:#718096;margin-top:2px;\">" +
                          $"命中 <strong>{fileCount}</strong> 个文件 / 共 <strong>{hitCount}</strong> 条</div></div>");
        }
        sb.AppendLine("</div></div>");
    }

    private static void AppendFileTable(StringBuilder sb, List<SensitiveFileResult> files)
    {
        if (files.Count == 0) { sb.AppendLine("<p class=\"em\">✅ 未发现敏感文件</p>"); return; }

        sb.AppendLine("<table><tr><th>#</th><th>文件路径（点击复制）</th><th>命中类型（条数）</th></tr>");
        int i = 1;
        foreach (var f in files)
        {
            var tags = string.Join("", f.MatchedPatterns.Select(p =>
            {
                var cnt = f.MatchCounts.TryGetValue(p, out var c) ? c : 0;
                var countLabel = cnt > 0 ? $"<span style=\"font-size:10px;opacity:0.75;\">×{cnt}</span>" : "";
                return $"<span class=\"pt\">{H(p)}{countLabel}</span>";
            }));
            sb.AppendLine($"<tr><td>{i++}</td><td class=\"path\" onclick=\"cp(this)\">{H(f.Path)}</td><td>{tags}</td></tr>");
        }
        sb.AppendLine("</table>");
    }

    private static string SC(string value, string label, string type) =>
        $"<div class=\"sc {type}\"><div class=\"v\">{H(value)}</div><div class=\"l\">{H(label)}</div></div>";

    private static string H(string? text) =>
        System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
}
