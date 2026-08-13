using System.Configuration;
using System.Data;
using System.Reflection;
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
    private readonly CancellationTokenSource _shutdownCancellation = new();

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
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version
            ?? new Version(1, 0, 0);
        var window = new MainWindow(viewModel, _services.UpdateService, currentVersion);
        MainWindow = window;
        window.Show();
        _ = CheckForUpdatesAsync(window, currentVersion, _shutdownCancellation.Token);
        await viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdownCancellation.Cancel();
        _shutdownCancellation.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }

    private async Task CheckForUpdatesAsync(
        MainWindow owner,
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var release = await _services!.UpdateService.CheckForUpdateAsync(
                currentVersion,
                cancellationToken);
            if (release is null
                || cancellationToken.IsCancellationRequested
                || !owner.IsVisible
                || owner.IsAboutWindowOpen)
            {
                return;
            }

            AppLog.Write("INFO", $"发现可用更新 v{release.DisplayVersion}。");
            owner.ShowUpdateWindow(owner, release);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Write("WARN", $"自动检查更新失败：{exception.Message}");
        }
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

