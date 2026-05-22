using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EndpointComplianceChecker.Models;
using EndpointComplianceChecker.Services;

namespace EndpointComplianceChecker.Views;

public partial class SensitivePatternsWindow : Window
{
    private readonly ConfigService _configService = new();
    private List<SensitivePattern> _patterns = new();
    private bool _dirty;          // unsaved changes exist
    private bool _suppressEvents; // prevent feedback loops in TextChanged

    public SensitivePatternsWindow()
    {
        InitializeComponent();
        LoadPatterns();
    }

    // ------------------------------------------------------------------ Load / Save

    private void LoadPatterns()
    {
        _patterns = _configService.LoadSensitivePatterns();
        RefreshList();
        SetStatus($"已加载 {_patterns.Count} 条规则");
    }

    private void RefreshList(int selectIndex = -1)
    {
        PatternList.ItemsSource = null;
        PatternList.ItemsSource = _patterns;
        if (selectIndex >= 0 && selectIndex < _patterns.Count)
            PatternList.SelectedIndex = selectIndex;
    }

    // ------------------------------------------------------------------ List events

    private void PatternList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var sel = PatternList.SelectedItem as SensitivePattern;
        BtnDelete.IsEnabled = sel != null;

        _suppressEvents = true;
        TxtType.Text      = sel?.Type  ?? string.Empty;
        TxtRegex.Text     = sel?.Regex ?? string.Empty;
        TxtThreshold.Text = sel?.MinFileCount.ToString() ?? "0";
        _suppressEvents = false;

        BtnApply.IsEnabled = false;
        RunTest();
    }

    // ------------------------------------------------------------------ Edit events

    private void EditField_Changed(object sender, TextChangedEventArgs e)
    {
        // Guard: controls may not all be initialized yet during XAML loading
        if (_suppressEvents || BtnApply == null || TxtType == null || TxtRegex == null) return;

        BtnApply.IsEnabled = PatternList.SelectedItem != null &&
                             !string.IsNullOrWhiteSpace(TxtType.Text) &&
                             !string.IsNullOrWhiteSpace(TxtRegex.Text);

        // Highlight threshold box red when value is non-numeric
        if (sender == TxtThreshold && TxtThreshold != null)
        {
            bool valid = int.TryParse(TxtThreshold.Text.Trim(), out var v) && v >= 0;
            TxtThreshold.BorderBrush = valid
                ? new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCB, 0xD5, 0xE0))
                : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
        }
        RunTest();
    }

    private void TxtTestInput_Changed(object sender, TextChangedEventArgs e) => RunTest();

    // ------------------------------------------------------------------ Toolbar buttons

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var newPattern = new SensitivePattern { Type = "新规则", Regex = ".*" };
        _patterns.Add(newPattern);
        _dirty = true;
        RefreshList(_patterns.Count - 1);
        TxtType.Focus();
        TxtType.SelectAll();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (PatternList.SelectedItem is not SensitivePattern sel) return;

        var result = MessageBox.Show(
            $"确认删除规则「{sel.Type}」？",
            "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        int idx = _patterns.IndexOf(sel);
        _patterns.Remove(sel);
        _dirty = true;
        RefreshList(Math.Min(idx, _patterns.Count - 1));
        SetStatus($"已删除规则「{sel.Type}」");
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (PatternList.SelectedItem is not SensitivePattern sel) return;

        if (string.IsNullOrWhiteSpace(TxtType.Text))
        {
            MessageBox.Show("类型名称不能为空。", "验证", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ValidateRegex(TxtRegex.Text, out var regexError))
        {
            MessageBox.Show($"正则表达式无效：\n{regexError}", "验证", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        sel.Type         = TxtType.Text.Trim();
        sel.Regex        = TxtRegex.Text.Trim();
        sel.MinFileCount = int.TryParse(TxtThreshold.Text.Trim(), out var thresh) && thresh >= 0 ? thresh : 0;
        _dirty = true;

        int idx = _patterns.IndexOf(sel);
        RefreshList(idx);
        BtnApply.IsEnabled = false;
        SetStatus($"已更新规则「{sel.Type}」（未保存）");
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "将恢复为内置默认规则（手机号、身份证号、银行卡号等），当前自定义规则将丢失。\n\n确认恢复？",
            "恢复默认", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _patterns = DefaultPatterns();
        _dirty = true;
        RefreshList();
        SetStatus("已恢复默认规则（未保存）");
    }

    // ------------------------------------------------------------------ Save / Cancel

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // Auto-apply any pending edits from the current selection before saving
        if (BtnApply.IsEnabled && PatternList.SelectedItem is SensitivePattern pendingSel)
        {
            if (!string.IsNullOrWhiteSpace(TxtType.Text) && ValidateRegex(TxtRegex.Text, out _))
            {
                pendingSel.Type         = TxtType.Text.Trim();
                pendingSel.Regex        = TxtRegex.Text.Trim();
                pendingSel.MinFileCount = int.TryParse(TxtThreshold.Text.Trim(), out var th) && th >= 0 ? th : 0;
                _dirty = true;
            }
        }

        // Validate all patterns before saving
        var invalid = _patterns
            .Where(p => string.IsNullOrWhiteSpace(p.Type) || string.IsNullOrWhiteSpace(p.Regex))
            .ToList();
        if (invalid.Count > 0)
        {
            MessageBox.Show("存在类型名称或正则表达式为空的规则，请先修正再保存。",
                "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var badRegex = _patterns.FirstOrDefault(p => !ValidateRegex(p.Regex, out _));
        if (badRegex != null)
        {
            MessageBox.Show($"规则「{badRegex.Type}」的正则表达式无效，请先修正再保存。",
                "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            PatternList.SelectedItem = badRegex;
            return;
        }

        try
        {
            _configService.SaveSensitivePatterns(_patterns);
            _dirty = false;
            SetStatus($"已保存 {_patterns.Count} 条规则");
            MessageBox.Show($"规则已保存（共 {_patterns.Count} 条）。\n下次扫描时自动加载新规则。",
                "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}\n\n请确认程序目录有写入权限。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty)
        {
            var result = MessageBox.Show("有未保存的修改，确认放弃？",
                "放弃修改", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }
        Close();
    }

    // ------------------------------------------------------------------ Regex test

    private void RunTest()
    {
        var regexText = TxtRegex.Text.Trim();
        var testText  = TxtTestInput.Text;

        if (string.IsNullOrWhiteSpace(regexText) || string.IsNullOrWhiteSpace(testText))
        {
            TxtTestResult.Text       = "在上方输入文本后自动显示匹配结果";
            TxtTestResult.Foreground = Brushes.Gray;
            TestResultBorder.Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFC));
            return;
        }

        if (!ValidateRegex(regexText, out var regexError))
        {
            TxtTestResult.Text       = $"正则表达式无效：{regexError}";
            TxtTestResult.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
            TestResultBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xF0));
            return;
        }

        try
        {
            var matches = Regex.Matches(testText, regexText,
                RegexOptions.None, TimeSpan.FromSeconds(3));

            if (matches.Count == 0)
            {
                TxtTestResult.Text       = "未匹配到任何内容";
                TxtTestResult.Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x80, 0x96));
                TestResultBorder.Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFC));
            }
            else
            {
                var hits = matches.Cast<Match>()
                    .Take(10)
                    .Select(m => $"「{m.Value}」")
                    .ToList();
                var summary = matches.Count > 10
                    ? $"匹配 {matches.Count} 处，前10条：{string.Join("  ", hits)}"
                    : $"匹配 {matches.Count} 处：{string.Join("  ", hits)}";

                TxtTestResult.Text       = summary;
                TxtTestResult.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
                TestResultBorder.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xFF, 0xF4));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            TxtTestResult.Text       = "正则匹配超时，表达式可能存在回溯爆炸，请简化后重试";
            TxtTestResult.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
            TestResultBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xF0));
        }
    }

    // ------------------------------------------------------------------ Helpers

    private static bool ValidateRegex(string pattern, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(pattern)) { error = "正则不能为空"; return false; }
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void SetStatus(string msg) => TxtStatus.Text = msg;

    private static List<SensitivePattern> DefaultPatterns() => new()
    {
        new() { Type = "手机号",   Regex = @"(?<!\d)1[3-9]\d{9}(?!\d)",                                                         MinFileCount = 0 },
        new() { Type = "身份证号", Regex = @"\b[1-9]\d{5}(18|19|20)\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])\d{3}[\dXx]\b", MinFileCount = 0 },
        new() { Type = "银行卡号", Regex = @"(?<!\d)(?:4\d{15}|5[1-5]\d{14}|62\d{14,17}|3[47]\d{13}|35\d{14})(?!\d)",        MinFileCount = 0 },
        new() { Type = "IPv4地址", Regex = @"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b",  MinFileCount = 0 },
        new() { Type = "住址",     Regex = @"[\u4e00-\u9fa5]{2,}(省|自治区)[\u4e00-\u9fa5]{2,}(市|州)[\u4e00-\u9fa5]{2,}(区|县|镇|乡)", MinFileCount = 0 },
    };
}
