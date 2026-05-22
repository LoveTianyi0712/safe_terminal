using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using EndpointComplianceChecker.ViewModels;
using EndpointComplianceChecker.Views;

namespace EndpointComplianceChecker;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Auto-scroll log to bottom when new lines are added
        ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogListBox.Items.Count > 0)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    () => LogListBox.ScrollIntoView(LogListBox.Items[^1]));
            }
        };
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _vm.LogLines.Clear();
    }

    private void ConfigPatterns_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SensitivePatternsWindow { Owner = this };
        dlg.ShowDialog();
    }
}
