using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using EndpointComplianceChecker.Models;

namespace EndpointComplianceChecker.Views;

/// <summary>Wrapper shown in the review ListView.</summary>
public class SensitiveFileReviewItem : INotifyPropertyChanged
{
    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
    }

    public string FilePath       { get; }
    public string FileName       => Path.GetFileName(FilePath);
    public string Extension      => Path.GetExtension(FilePath).ToLowerInvariant().TrimStart('.');
    public string Source         { get; }
    public string PatternSummary { get; }
    public SensitiveFileResult Original { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SensitiveFileReviewItem(SensitiveFileResult result, string source)
    {
        FilePath  = result.Path;
        Source    = source;
        Original  = result;

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

    private readonly List<SensitiveFileReviewItem> _allItems    = new();
    private          List<SensitiveFileReviewItem> _visibleItems = new();

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

        // Subscribe to checkbox changes so button state and count update live
        foreach (var item in _allItems)
            item.PropertyChanged += Item_CheckChanged;
    }

    private void PopulateFilters()
    {
        CmbSource.ItemsSource   = new[] { "全部来源", "微信", "全盘" };
        CmbSource.SelectedIndex = 0;

        RefreshExtensionFilter();
    }

    private void RefreshExtensionFilter(string? keepSelected = null)
    {
        var exts = _allItems
            .Select(i => i.Extension)
            .Distinct()
            .OrderBy(e => e)
            .Prepend("全部类型")
            .ToList();
        CmbExtension.ItemsSource  = exts;
        CmbExtension.SelectedItem = (keepSelected != null && exts.Contains(keepSelected))
                                    ? keepSelected : "全部类型";
    }

    // ------------------------------------------------------------------ Filter

    private void ApplyFilter()
    {
        string src = CmbSource.SelectedItem    as string ?? "全部来源";
        string ext = CmbExtension.SelectedItem as string ?? "全部类型";

        _visibleItems = _allItems
            .Where(i => src == "全部来源" || i.Source    == src)
            .Where(i => ext == "全部类型" || i.Extension == ext)
            .ToList();

        FileList.ItemsSource = _visibleItems;
        UpdateUI();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    // ------------------------------------------------------------------ Checkbox / selection

    private void Item_CheckChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SensitiveFileReviewItem.IsChecked))
            UpdateUI();
    }

    // Checkbox in cell clicked — the binding handles the value; just refresh UI
    private void ItemCheckBox_Click(object sender, RoutedEventArgs e) => UpdateUI();

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool anyUnchecked = _visibleItems.Any(i => !i.IsChecked);
        foreach (var item in _visibleItems)
            item.IsChecked = anyUnchecked;   // if any unchecked → check all; else uncheck all
        BtnSelectAll.Content = anyUnchecked ? "取消全选" : "全选当前页";
    }

    // ------------------------------------------------------------------ Delete

    private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var toDelete = _visibleItems.Where(i => i.IsChecked).ToList();
        if (toDelete.Count == 0) return;

        var preview = string.Join("\n", toDelete.Take(10).Select(i => $"  • {i.FilePath}"));
        if (toDelete.Count > 10) preview += $"\n  … 等共 {toDelete.Count} 个文件";

        var confirm = MessageBox.Show(
            $"⚠ 即将永久删除磁盘上的以下 {toDelete.Count} 个文件：\n\n{preview}\n\n此操作不可恢复！确认删除？",
            "确认永久删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        int deleted = 0, failed = 0;
        var failMessages = new List<string>();

        foreach (var item in toDelete)
        {
            try
            {
                if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
                deleted++;
                item.PropertyChanged -= Item_CheckChanged;
                _wechatList.Remove(item.Original);
                _fullList.Remove(item.Original);
                _allItems.Remove(item);
            }
            catch (Exception ex)
            {
                failed++;
                failMessages.Add($"{item.FileName}: {ex.Message}");
            }
        }

        RefreshExtensionFilter(CmbExtension.SelectedItem as string);
        ApplyFilter();

        if (failed > 0)
        {
            TxtStatus.Text = $"已删除 {deleted} 个，{failed} 个失败（权限不足或被占用）。";
            MessageBox.Show(
                $"成功删除 {deleted} 个文件，以下 {failed} 个删除失败：\n\n{string.Join("\n", failMessages.Take(5))}",
                "部分删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            TxtStatus.Text = $"✅ 已成功删除 {deleted} 个文件，当前还有 {_allItems.Count} 条记录。";
        }
    }

    // ------------------------------------------------------------------ Helpers

    private void UpdateUI()
    {
        int checkedCount = _visibleItems.Count(i => i.IsChecked);
        BtnDeleteSelected.IsEnabled = checkedCount > 0;
        BtnDeleteSelected.Content   = checkedCount > 0 ? $"🗑 删除已选 {checkedCount} 个" : "🗑 删除选中文件";
        BtnSelectAll.Content = _visibleItems.Count > 0 && _visibleItems.All(i => i.IsChecked)
            ? "取消全选" : "全选当前页";
        TxtCount.Text = $"显示 {_visibleItems.Count} 条 / 共 {_allItems.Count} 条" +
                        (checkedCount > 0 ? $"  已选 {checkedCount}" : "");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
