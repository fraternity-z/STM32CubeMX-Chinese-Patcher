using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;
using STM32CubeMX.ChinesePatcher.Services;
using STM32CubeMX.ChinesePatcher.ViewModels;

namespace STM32CubeMX.ChinesePatcher;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private AppServices? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = new AppServices();

        if (e.Args.Length == 2
            && string.Equals(e.Args[0], "--elevated-worker", StringComparison.Ordinal))
        {
            var exitCode = await OperationCoordinator.RunWorkerAsync(
                e.Args[1],
                _services.PatchService);
            Shutdown(exitCode);
            return;
        }

        DispatcherUnhandledException += HandleDispatcherException;
        var viewModel = new MainViewModel(
            _services.InstallationDetector,
            _services.ProcessStateService,
            _services.StateInspector,
            _services.OperationCoordinator);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync();
    }

    private static void HandleDispatcherException(
        object sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        AppLog.Write("FATAL", eventArgs.Exception.Message);
        MessageBox.Show(
            $"发生未处理错误：{eventArgs.Exception.Message}\n\n日志：{AppLog.LogPath}",
            "STM32CubeMX 汉化工具",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        eventArgs.Handled = true;
    }
}

