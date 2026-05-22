using System.IO;
using System.Windows;
using System.Windows.Controls;
using EndpointComplianceChecker.Models;

namespace EndpointComplianceChecker.Views;

/// <summary>Wrapper shown in the review ListView.</summary>
public class SensitiveFileReviewItem
{
    public string FilePath      { get; }
    public string FileName      => Path.GetFileName(FilePath);
    public string Extension     => Path.GetExtension(FilePath).ToLowerInvariant().TrimStart('.');
    public string Source        { get; }
    public string PatternSummary { get; }
    public SensitiveFileResult Original { get; }

    public SensitiveFileReviewItem(SensitiveFileResult result, string source)
    {
        FilePath       = result.Path;
        Source         = source;
        Original       = result;

        var patterns = result.MatchCounts.Count > 0
            ? result.MatchCounts.Select(kv => $"{kv.Key}×{kv.Value}")
            : result.MatchedPatterns.Distinct().Select(p => p);
        PatternSummary = string.Join("  ", patterns);
    }
}

public partial class SensitiveFileReviewWindow : Window
{
    private readonly List<SensitiveFileResult> _wechatList;
    private readonly List<SensitiveFileResult> _fullList;

    // All items (never modified) – used to rebuild the view after filter changes
    private readonly List<SensitiveFileReviewItem> _allItems = new();
    // Currently visible (filtered) items
    private List<SensitiveFileReviewItem> _visibleItems = new();

    public SensitiveFileReviewWindow(
        List<SensitiveFileResult> wechatFiles,
        List<SensitiveFileResult> fullFiles)
    {
        _wechatList = wechatFiles;
        _fullList   = fullFiles;

        InitializeComponent();
        BuildItemList();
        PopulateFilters();
        ApplyFilter();
    }

    // ------------------------------------------------------------------ Setup

    private void BuildItemList()
    {
        _allItems.Clear();
        foreach (var r in _wechatList) _allItems.Add(new SensitiveFileReviewItem(r, "微信"));
        foreach (var r in _fullList)   _allItems.Add(new SensitiveFileReviewItem(r, "全盘"));
    }

    private void PopulateFilters()
    {
        // Source filter
        var sources = new[] { "全部来源", "微信", "全盘" };
        CmbSource.ItemsSource   = sources;
        CmbSource.SelectedIndex = 0;

        // Extension filter
        var exts = _allItems
            .Select(i => i.Extension)
            .Distinct()
            .OrderBy(e => e)
            .Prepend("全部类型")
            .ToList();
        CmbExtension.ItemsSource   = exts;
        CmbExtension.SelectedIndex = 0;
    }

    // ------------------------------------------------------------------ Filter

    private void ApplyFilter()
    {
        string sourceFilter = CmbSource.SelectedItem as string ?? "全部来源";
        string extFilter    = CmbExtension.SelectedItem as string ?? "全部类型";

        _visibleItems = _allItems
            .Where(i => sourceFilter == "全部来源" || i.Source == sourceFilter)
            .Where(i => extFilter    == "全部类型" || i.Extension == extFilter)
            .ToList();

        FileList.ItemsSource = _visibleItems;
        UpdateCountLabel();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    // ------------------------------------------------------------------ Selection

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnDeleteSelected.IsEnabled = FileList.SelectedItems.Count > 0;
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        FileList.SelectAll();
    }

    // ------------------------------------------------------------------ Delete

    private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var toRemove = FileList.SelectedItems.Cast<SensitiveFileReviewItem>().ToList();
        if (toRemove.Count == 0) return;

        var confirm = MessageBox.Show(
            $"确认从本次报告中移除选中的 {toRemove.Count} 个文件记录？\n（不会删除磁盘上的实际文件）",
            "移除确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        foreach (var item in toRemove)
        {
            _wechatList.Remove(item.Original);
            _fullList.Remove(item.Original);
            _allItems.Remove(item);
        }

        // Refresh extension filter options (some may now be empty)
        var currentExt = CmbExtension.SelectedItem as string;
        var exts = _allItems
            .Select(i => i.Extension)
            .Distinct()
            .OrderBy(e => e)
            .Prepend("全部类型")
            .ToList();
        CmbExtension.ItemsSource = exts;
        CmbExtension.SelectedItem = exts.Contains(currentExt!) ? currentExt : "全部类型";

        ApplyFilter();
        TxtStatus.Text = $"已移除 {toRemove.Count} 项，当前共 {_allItems.Count} 条记录（未保存报告前可继续修改）。";
    }

    // ------------------------------------------------------------------ Helpers

    private void UpdateCountLabel()
    {
        TxtCount.Text = $"显示 {_visibleItems.Count} 条 / 共 {_allItems.Count} 条";
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
