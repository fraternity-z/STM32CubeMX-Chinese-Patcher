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
using System.Windows.Controls.Primitives;
using STM32CubeMX.ChinesePatcher.Models;
using STM32CubeMX.ChinesePatcher.Services;

namespace STM32CubeMX.ChinesePatcher;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IUpdateService _updateService;
    private readonly Version _currentVersion;
    private readonly DispatcherTimer _runningStateTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };
    private bool _isClosed;
    private AboutWindow? _aboutWindow;
    private UpdateWindow? _updateWindow;

    public MainWindow(
        MainViewModel viewModel,
        IUpdateService updateService,
        Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(updateService);
        ArgumentNullException.ThrowIfNull(currentVersion);
        _viewModel = viewModel;
        _updateService = updateService;
        _currentVersion = currentVersion;
        InitializeComponent();
        DataContext = viewModel;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        _runningStateTimer.Tick += RunningStateTimer_Tick;
        _runningStateTimer.Start();
    }

    public bool IsAboutWindowOpen => _aboutWindow is { IsVisible: true };

    public bool IsUpdateWindowOpen => _updateWindow is { IsVisible: true };

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

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        AboutMenu.PlacementTarget = AboutButton;
        AboutMenu.Placement = PlacementMode.Bottom;
        AboutMenu.IsOpen = true;
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowAboutWindow(checkImmediately: false);

    private void CheckUpdatesMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowAboutWindow(checkImmediately: true);

    private void ShowAboutWindow(bool checkImmediately)
    {
        if (_updateWindow is { IsVisible: true })
        {
            _updateWindow.Activate();
            return;
        }

        if (_aboutWindow is { IsVisible: true })
        {
            _aboutWindow.Activate();
            return;
        }

        var aboutViewModel = new AboutViewModel(_updateService, _currentVersion);
        var aboutWindow = new AboutWindow(aboutViewModel, ShowUpdateWindow, checkImmediately)
        {
            Owner = this
        };

        _aboutWindow = aboutWindow;
        try
        {
            aboutWindow.ShowDialog();
        }
        finally
        {
            _aboutWindow = null;
        }
    }

    public void ShowUpdateWindow(Window owner, UpdateRelease release)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(release);

        if (_aboutWindow is { IsVisible: true }
            && !ReferenceEquals(owner, _aboutWindow))
        {
            return;
        }

        if (_updateWindow is { IsVisible: true })
        {
            _updateWindow.Activate();
            return;
        }

        var viewModel = new UpdateViewModel(_updateService, release);
        var updateWindow = new UpdateWindow(_updateService, viewModel)
        {
            Owner = owner
        };
        _updateWindow = updateWindow;
        try
        {
            updateWindow.ShowDialog();
        }
        finally
        {
            _updateWindow = null;
        }
    }
}
