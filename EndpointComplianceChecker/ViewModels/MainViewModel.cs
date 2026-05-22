using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using EndpointComplianceChecker.Helpers;
using EndpointComplianceChecker.Models;
using EndpointComplianceChecker.Services;

namespace EndpointComplianceChecker.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly ConfigService _configService = new();

    private bool _isRunning;
    private bool _canSaveReport;
    private double _overallProgress;
    private string _statusText = "就绪，点击「开始检查」启动扫描";
    private CancellationTokenSource? _cts;
    private ComplianceReport? _lastReport;
    private string? _lastHtmlPath;

    public ObservableCollection<CheckItemViewModel> CheckItems { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            SetField(ref _isRunning, value);
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
        }
    }

    public bool CanStart => !_isRunning;
    public bool CanStop  => _isRunning;

    public bool CanSaveReport
    {
        get => _canSaveReport;
        private set
        {
            SetField(ref _canSaveReport, value);
            OnPropertyChanged(nameof(CanReviewResults));
        }
    }

    public double OverallProgress
    {
        get => _overallProgress;
        private set => SetField(ref _overallProgress, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public ICommand StartCommand        { get; }
    public ICommand StopCommand         { get; }
    public ICommand SaveReportCommand   { get; }
    public ICommand ReviewResultsCommand { get; }

    public bool CanReviewResults => _canSaveReport && _lastReport != null &&
        (_lastReport.WeChatSensitiveFiles.Count > 0 || _lastReport.FullScanSensitiveFiles.Count > 0);

    public MainViewModel()
    {
        StartCommand         = new RelayCommand(StartScan,     () => CanStart);
        StopCommand          = new RelayCommand(StopScan,      () => CanStop);
        SaveReportCommand    = new RelayCommand(SaveReport,    () => CanSaveReport);
        ReviewResultsCommand = new RelayCommand(ReviewResults, () => CanReviewResults);

        InitCheckItems();
    }

    private void InitCheckItems()
    {
        var items = new[]
        {
            new CheckItemViewModel { Order = 1, Name = "终端基本信息采集" },
            new CheckItemViewModel { Order = 2, Name = "操作系统正版检查" },
            new CheckItemViewModel { Order = 3, Name = "已安装软件清单枚举" },
            new CheckItemViewModel { Order = 4, Name = "盗版软件检测" },
            new CheckItemViewModel { Order = 5, Name = "远程控制软件检测" },
            new CheckItemViewModel { Order = 6, Name = "虚拟机安装检测" },
            new CheckItemViewModel { Order = 7, Name = "网卡数量及外网检测" },
            new CheckItemViewModel { Order = 8, Name = "微信敏感文件扫描" },
            new CheckItemViewModel { Order = 9, Name = "全盘敏感文件扫描" },
        };

        foreach (var item in items)
            CheckItems.Add(item);
    }

    private void StartScan()
    {
        _cts = new CancellationTokenSource();
        IsRunning = true;
        CanSaveReport = false;
        OverallProgress = 0;
        LogLines.Clear();

        foreach (var item in CheckItems)
        {
            item.Status = CheckStatus.Pending;
            item.Summary = "等待检查...";
            item.SubProgress = 0;
            item.ShowSubProgress = false;
        }

        _ = Task.Run(() => RunScanAsync(_cts.Token));
    }

    private void StopScan()
    {
        _cts?.Cancel();
        Log("[INFO] 正在停止扫描...");
        StatusText = "正在停止...";
    }

    private async Task RunScanAsync(CancellationToken ct)
    {
        var report = new ComplianceReport { ToolVersion = "1.1.0" };
        var logProgress = new Progress<string>(msg =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var ts = DateTime.Now.ToString("HH:mm:ss");
                LogLines.Add($"[{ts}] {msg}");
                if (LogLines.Count > 2000) LogLines.RemoveAt(0);
            });
        });

        try
        {
            var settings   = _configService.LoadScanSettings();
            var patterns   = _configService.LoadSensitivePatterns();
            var pirateRules = _configService.LoadPirateSoftwareRules();
            var remoteNames = _configService.LoadRemoteControlNames();

            // Step 1: Basic info
            await RunStep(1, report, ct, () =>
            {
                SetItemRunning(1, "正在采集...");
                var svc = new BasicInfoService();
                report.BasicInfo = svc.Collect(logProgress);
                SetItemPass(1, $"IP: {report.BasicInfo.IpAddress}  |  OS: {report.BasicInfo.OsVersion}");
                return Task.CompletedTask;
            });
            if (ct.IsCancellationRequested) { FinalizePartial(report); return; }
            UpdateProgress(1);

            // Step 2: OS license
            await RunStep(2, report, ct, () =>
            {
                SetItemRunning(2, "正在查询授权...");
                var svc = new SoftwareCheckService(pirateRules, remoteNames);
                report.OsLicenseStatus = svc.CheckOsLicense(logProgress);
                bool isGood = report.OsLicenseStatus.Contains("正版");
                if (isGood) SetItemPass(2, report.OsLicenseStatus);
                else        SetItemWarning(2, report.OsLicenseStatus);
                return Task.CompletedTask;
            });
            if (ct.IsCancellationRequested) { FinalizePartial(report); return; }
            UpdateProgress(2);

            // Step 3: Enumerate installed software
            List<SoftwareItem> installed = new();
            await RunStep(3, report, ct, () =>
            {
                SetItemRunning(3, "正在枚举...");
                var svc = new SoftwareCheckService(pirateRules, remoteNames);
                installed = svc.EnumerateInstalledSoftware(logProgress);
                report.InstalledSoftware = installed;
                SetItemPass(3, $"共发现 {installed.Count} 个软件");
                return Task.CompletedTask;
            });
            if (ct.IsCancellationRequested) { FinalizePartial(report); return; }
            UpdateProgress(3);

            // Step 4: Pirate software
            await RunStep(4, report, ct, () =>
            {
                SetItemRunning(4, "正在检测...");
                var svc = new SoftwareCheckService(pirateRules, remoteNames);
                report.PirateSoftware = svc.DetectPirateSoftware(installed, logProgress);
                if (report.PirateSoftware.Count == 0)
                    SetItemPass(4, "未发现盗版软件");
                else
                    SetItemFail(4, $"发现 {report.PirateSoftware.Count} 个疑似盗版");
                return Task.CompletedTask;
            });
            if (ct.IsCancellationRequested) { FinalizePartial(report); return; }
            UpdateProgress(4);

            // Step 5: Remote control software
            await RunStep(5, report, ct, () =>
            {
                SetItemRunning(5, "正在检测...");
                var svc = new SoftwareCheckService(pirateRules, remoteNames);
                report.RemoteControlSoftware = svc.DetectRemoteControlSoftware(installed, logProgress);
                if (report.RemoteControlSoftware.Count == 0)
                    SetItemPass(5, "未发现远程控制软件");
                else
                    SetItemWarning(5, $"发现: {string.Join(", ", report.RemoteControlSoftware.Select(s => s.Name))}");
                return Task.CompletedTask;
            });
            if (ct.IsCancellationRequested) { FinalizePartial(report); return; }
            UpdateProgress(5);

            // Step 6: Virtual machine detection
            await RunStep(6, report, ct, () =>
            {
                SetItemRunning(6, "正在检测虚拟机软件...");
                var svc = new VirtualMachineService();
                report.VirtualMachines = svc.Scan(logProgress);
                var vm = report.VirtualMachines;
                if (vm.HasViolation)
                {
                    var thirdPartyNames = string.Join(", ", vm.Found.Where(v => !v.IsBuiltIn).Select(v => v.Name));
                    SetItemFail(6, $"违规：发现第三方VM软件（{thirdPartyNames}）");
                }
                else if (vm.HasWarning)
                {
                    var builtInNames = string.Join(", ", vm.Found.Where(v => v.IsBuiltIn).Select(v => v.Name));
                    SetItemWarning(6, $"警告：检测到Windows内置Hyper-V（{builtInNames}）");
                }
                else
                    SetItemPass(6, "未检测到虚拟机软件");
                return Task.CompletedTask;
            });
            if (ct.IsCancellationRequested) { FinalizePartial(report); return; }
            UpdateProgress(6);

            // Step 7: Network adapter detection
            await RunStep(7, report, ct, async () =>
            {
                SetItemRunning(7, "正在检测网卡...");
                var svc = new NetworkAdapterService();
                report.NetworkAdapters = await Task.Run(() => svc.Scan(logProgress), ct);
                var r = report.NetworkAdapters;
                if (r.HasViolation)
                    SetItemFail(7, r.Summary);
                else if (r.HasWarning)
                    SetItemWarning(7, r.Summary);
                else
                    SetItemPass(7, r.Summary);
            });
            if (ct.IsCancellationRequested) { FinalizePartial(report); return; }
            UpdateProgress(7);

            // Step 8: WeChat scan
            await RunStep(8, report, ct, () =>
            {
                SetItemRunning(8, "查找微信路径...");
                var svc = new WeChatScanService(patterns, settings.WeChatScan);
                var path = svc.FindWeChatPath(logProgress);
                if (path == null)
                {
                    SetItemPass(8, "未检测到微信安装");
                }
                else
                {
                    SetItemRunning(8, $"扫描中: {path}");
                    report.WeChatSensitiveFiles = svc.Scan(path, logProgress, ct);
                    if (report.WeChatSensitiveFiles.Count == 0)
                    {
                        SetItemPass(8, "未发现敏感文件");
                    }
                    else
                    {
                        var typeSummary = report.WeChatSensitiveFiles
                            .SelectMany(f => f.MatchedPatterns)
                            .GroupBy(p => p)
                            .OrderByDescending(g => g.Count())
                            .Select(g => $"{g.Key}×{g.Count()}")
                            .Take(3);
                        SetItemFail(8, $"发现 {report.WeChatSensitiveFiles.Count} 个文件（{string.Join(" ", typeSummary)}）");
                    }
                }
                return Task.CompletedTask;
            });
            if (ct.IsCancellationRequested) { FinalizePartial(report); return; }
            UpdateProgress(8);

            // Step 9: Full disk scan (long-running, uses sub-progress)
            await RunStep(9, report, ct, () =>
            {
                SetItemRunning(9, "准备扫描...");
                var diskSvc = new FullDiskScanService(patterns, settings.FullScan);

                var subProgress = new Progress<(int scanned, int total)>(p =>
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        var item = CheckItems[8];  // index 8 = order 9
                        item.SubProgress = p.total > 0 ? (double)p.scanned / p.total * 100 : 0;
                        item.ShowSubProgress = true;
                        item.Summary = $"扫描中 {p.scanned:N0}/{p.total:N0} ({item.SubProgress:F0}%)";
                        OverallProgress = 85 + item.SubProgress * 0.15;
                    });
                });

                report.FullScanSensitiveFiles = diskSvc.Scan(logProgress, subProgress, ct);

                App.Current.Dispatcher.Invoke(() => CheckItems[8].ShowSubProgress = false);

                if (report.FullScanSensitiveFiles.Count == 0)
                {
                    SetItemPass(9, "未发现敏感文件");
                }
                else
                {
                    var typeSummary = report.FullScanSensitiveFiles
                        .SelectMany(f => f.MatchedPatterns)
                        .GroupBy(p => p)
                        .OrderByDescending(g => g.Count())
                        .Select(g => $"{g.Key}×{g.Count()}")
                        .Take(3);
                    SetItemFail(9, $"发现 {report.FullScanSensitiveFiles.Count} 个文件（{string.Join(" ", typeSummary)}）");
                }
                return Task.CompletedTask;
            });
            UpdateProgress(9);

            // Complete
            _lastReport = report;
            App.Current.Dispatcher.Invoke(() =>
            {
                IsRunning = false;
                OverallProgress = 100;
                CanSaveReport = true;
                StatusText = "检查完成！点击「保存报告」导出结果。";
                Log("[INFO] ✅ 全部检查项目已完成");
            });
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                Log($"[ERROR] 扫描异常: {ex.Message}");
                IsRunning = false;
                StatusText = "扫描过程中发生错误";
                report.ErrorLog.Add($"Fatal error: {ex}");
                _lastReport = report;
                report.IsPartial = true;
                CanSaveReport = true;
            });
        }
    }

    private async Task RunStep(int order, ComplianceReport report, CancellationToken ct, Func<Task> action)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            var msg = $"步骤 {order} 执行失败: {ex.Message}";
            Log($"[ERROR] {msg}");
            report.ErrorLog.Add(msg);
            SetItemFail(order, "执行失败");
        }
    }

    private void FinalizePartial(ComplianceReport report)
    {
        report.IsPartial = true;
        _lastReport = report;
        App.Current.Dispatcher.Invoke(() =>
        {
            IsRunning = false;
            StatusText = "扫描已停止，可保存部分报告";
            CanSaveReport = true;
            Log("[INFO] 扫描已停止，已完成的检查项结果已保留");

            foreach (var item in CheckItems.Where(i => i.Status == CheckStatus.Pending))
            {
                item.Status = CheckStatus.Skipped;
                item.Summary = "已跳过";
            }
            foreach (var item in CheckItems.Where(i => i.Status == CheckStatus.Running))
            {
                item.Status = CheckStatus.Skipped;
                item.Summary = "已中断";
            }
        });
    }

    private void UpdateProgress(int completedStep)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            OverallProgress = completedStep * 100.0 / CheckItems.Count;
        });
    }

    private void SetItemRunning(int order, string summary) =>
        SetItemState(order, CheckStatus.Running, summary);
    private void SetItemPass(int order, string summary) =>
        SetItemState(order, CheckStatus.Pass, summary);
    private void SetItemWarning(int order, string summary) =>
        SetItemState(order, CheckStatus.Warning, summary);
    private void SetItemFail(int order, string summary) =>
        SetItemState(order, CheckStatus.Fail, summary);

    private void SetItemState(int order, CheckStatus status, string summary)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var item = CheckItems.FirstOrDefault(i => i.Order == order);
            if (item == null) return;
            item.Status = status;
            item.Summary = summary;
            StatusText = $"[{order}/{CheckItems.Count}] {item.Name}: {summary}";
        });
    }

    private void Log(string message)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var ts = DateTime.Now.ToString("HH:mm:ss");
            LogLines.Add($"[{ts}] {message}");
            if (LogLines.Count > 2000) LogLines.RemoveAt(0);
        });
    }

    private void ReviewResults()
    {
        if (_lastReport == null) return;
        var win = new Views.SensitiveFileReviewWindow(_lastReport.WeChatSensitiveFiles,
                                                      _lastReport.FullScanSensitiveFiles);
        win.ShowDialog();
        // CanReviewResults re-evaluated after dialog closes (list may have shrunk)
        OnPropertyChanged(nameof(CanReviewResults));
    }

    private void SaveReport()
    {
        if (_lastReport == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title       = "保存合规报告",
            FileName    = $"compliance_report_{DateTime.Now:yyyyMMdd_HHmmss}",
            Filter      = "HTML 报告|*.html",
            DefaultExt  = ".html",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (dlg.ShowDialog() != true) return;

        var outputDir = Path.GetDirectoryName(dlg.FileName)!;

        try
        {
            var svc = new ReportService();
            var (htmlPath, jsonPath) = svc.SaveReport(_lastReport, dlg.FileName);
            _lastHtmlPath = htmlPath;
            Log($"[INFO] 报告已保存: {htmlPath}");
            Log($"[INFO] JSON 数据: {jsonPath}");

            var result = MessageBox.Show(
                $"报告已保存至:\n{htmlPath}\n\n是否立即在浏览器中打开 HTML 报告？",
                "报告保存成功", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(htmlPath)
                { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存报告失败:\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}
