using System.Windows;

namespace EndpointComplianceChecker;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (s, ex) =>
        {
            MessageBox.Show($"未处理的异常:\n{ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                "程序错误", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
    }
}
