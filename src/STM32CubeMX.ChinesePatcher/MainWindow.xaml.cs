using System.Text;
using System.Windows;
using System.IO;
using Microsoft.Win32;
using STM32CubeMX.ChinesePatcher.ViewModels;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace STM32CubeMX.ChinesePatcher;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _runningStateTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };
    private bool _isClosed;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        _runningStateTimer.Tick += RunningStateTimer_Tick;
        _runningStateTimer.Start();
    }

    private async void MainWindow_Activated(object? sender, EventArgs e) =>
        await _viewModel.RefreshRunningStateAsync();

    private async void RunningStateTimer_Tick(object? sender, EventArgs e)
    {
        if (!_viewModel.NeedsRunningStateRefresh)
        {
            return;
        }

        _runningStateTimer.Stop();
        try
        {
            await _viewModel.RefreshRunningStateAsync();
        }
        finally
        {
            if (!_isClosed)
            {
                _runningStateTimer.Start();
            }
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _runningStateTimer.Stop();
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanBrowse)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "选择 STM32CubeMX 安装目录",
            Multiselect = false
        };
        if (Directory.Exists(_viewModel.PathDisplay))
        {
            dialog.InitialDirectory = _viewModel.PathDisplay;
        }

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.SelectManualPathAsync(dialog.FolderName);
        }
    }
}
