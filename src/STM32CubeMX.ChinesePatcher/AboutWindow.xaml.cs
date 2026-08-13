using System.Windows;
using STM32CubeMX.ChinesePatcher.Models;
using STM32CubeMX.ChinesePatcher.ViewModels;

namespace STM32CubeMX.ChinesePatcher;

public partial class AboutWindow : Window
{
    private readonly AboutViewModel _viewModel;
    private readonly Action<Window, UpdateRelease> _showUpdateWindow;
    private readonly bool _checkImmediately;

    public AboutWindow(
        AboutViewModel viewModel,
        Action<Window, UpdateRelease> showUpdateWindow,
        bool checkImmediately = false)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(showUpdateWindow);
        _viewModel = viewModel;
        _showUpdateWindow = showUpdateWindow;
        _checkImmediately = checkImmediately;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += AboutWindow_Loaded;
        Closed += AboutWindow_Closed;
    }

    private async void AboutWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= AboutWindow_Loaded;
        if (_checkImmediately)
        {
            await _viewModel.CheckForUpdatesAsync();
        }
    }

    private void OpenUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var release = _viewModel.AvailableRelease;
        if (release is null)
        {
            return;
        }

        _showUpdateWindow(this, release);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AboutWindow_Closed(object? sender, EventArgs e) => _viewModel.Dispose();
}
