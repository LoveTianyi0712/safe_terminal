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
        var toDelete = FileList.SelectedItems.Cast<SensitiveFileReviewItem>().ToList();
        if (toDelete.Count == 0) return;

        // Build preview list (max 10 items shown)
        var preview = string.Join("\n",
            toDelete.Take(10).Select(i => $"  • {i.FilePath}"));
        if (toDelete.Count > 10)
            preview += $"\n  … 等共 {toDelete.Count} 个文件";

        var confirm = MessageBox.Show(
            $"⚠ 即将永久删除磁盘上的以下 {toDelete.Count} 个文件：\n\n{preview}\n\n此操作不可恢复！确认删除？",
            "确认永久删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        int deleted = 0;
        int failed  = 0;
        var failMessages = new List<string>();

        foreach (var item in toDelete)
        {
            try
            {
                if (File.Exists(item.FilePath))
                {
                    File.Delete(item.FilePath);
                    deleted++;
                }
                else
                {
                    // File already gone — still remove from list
                    deleted++;
                }
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

        // Refresh extension filter options
        var currentExt = CmbExtension.SelectedItem as string;
        var exts = _allItems
            .Select(i => i.Extension)
            .Distinct()
            .OrderBy(ext => ext)
            .Prepend("全部类型")
            .ToList();
        CmbExtension.ItemsSource = exts;
        CmbExtension.SelectedItem = exts.Contains(currentExt!) ? currentExt : "全部类型";

        ApplyFilter();

        if (failed > 0)
        {
            var failDetail = string.Join("\n", failMessages.Take(5));
            TxtStatus.Text = $"已删除 {deleted} 个文件，{failed} 个失败（权限不足或被占用）。";
            MessageBox.Show(
                $"成功删除 {deleted} 个文件，以下 {failed} 个文件删除失败：\n\n{failDetail}",
                "部分删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            TxtStatus.Text = $"✅ 已成功删除 {deleted} 个文件，当前还有 {_allItems.Count} 条记录。";
        }
    }

    // ------------------------------------------------------------------ Helpers

    private void UpdateCountLabel()
    {
        TxtCount.Text = $"显示 {_visibleItems.Count} 条 / 共 {_allItems.Count} 条";
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
