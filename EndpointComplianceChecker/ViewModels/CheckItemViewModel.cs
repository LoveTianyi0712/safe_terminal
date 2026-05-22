namespace EndpointComplianceChecker.ViewModels;

public enum CheckStatus
{
    Pending,
    Running,
    Pass,
    Warning,
    Fail,
    Skipped
}

public class CheckItemViewModel : BaseViewModel
{
    private CheckStatus _status = CheckStatus.Pending;
    private string _summary = "等待检查...";
    private double _subProgress;
    private bool _showSubProgress;

    public string Name { get; init; } = string.Empty;
    public int Order { get; init; }

    public CheckStatus Status
    {
        get => _status;
        set
        {
            SetField(ref _status, value);
            OnPropertyChanged(nameof(StatusIcon));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string Summary
    {
        get => _summary;
        set => SetField(ref _summary, value);
    }

    public double SubProgress
    {
        get => _subProgress;
        set => SetField(ref _subProgress, value);
    }

    public bool ShowSubProgress
    {
        get => _showSubProgress;
        set => SetField(ref _showSubProgress, value);
    }

    public string StatusIcon => Status switch
    {
        CheckStatus.Pending  => "⏳",
        CheckStatus.Running  => "🔄",
        CheckStatus.Pass     => "✅",
        CheckStatus.Warning  => "⚠️",
        CheckStatus.Fail     => "❌",
        CheckStatus.Skipped  => "⏭️",
        _ => "⏳"
    };

    public string StatusColor => Status switch
    {
        CheckStatus.Pass    => "#27AE60",
        CheckStatus.Warning => "#F39C12",
        CheckStatus.Fail    => "#E74C3C",
        CheckStatus.Running => "#2E86AB",
        CheckStatus.Skipped => "#718096",
        _                   => "#A0AEC0"
    };

    public string StatusText => Status switch
    {
        CheckStatus.Pending  => "等待",
        CheckStatus.Running  => "检查中",
        CheckStatus.Pass     => "通过",
        CheckStatus.Warning  => "警告",
        CheckStatus.Fail     => "失败",
        CheckStatus.Skipped  => "已跳过",
        _ => "等待"
    };
}
