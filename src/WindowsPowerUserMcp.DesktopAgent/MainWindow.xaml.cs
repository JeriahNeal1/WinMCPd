using System.Windows;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.DesktopAgent;

public partial class MainWindow : Window
{
    private readonly StorageLayout _layout;

    public MainWindow(StorageLayout layout)
    {
        _layout = layout;
        InitializeComponent();
        StatusText.Text = $"Data: {_layout.Root}{Environment.NewLine}Logs: {_layout.Logs}{Environment.NewLine}Screenshots: {_layout.Screenshots}";
        Log("DesktopAgent initialized.");
    }

    public void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogText.AppendText($"[{DateTimeOffset.Now:T}] {message}{Environment.NewLine}");
            LogText.ScrollToEnd();
        });
    }
}
