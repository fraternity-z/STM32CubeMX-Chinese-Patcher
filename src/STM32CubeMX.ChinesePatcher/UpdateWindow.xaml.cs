using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using STM32CubeMX.ChinesePatcher.Models;
using STM32CubeMX.ChinesePatcher.Services;
using STM32CubeMX.ChinesePatcher.ViewModels;

namespace STM32CubeMX.ChinesePatcher;

public partial class UpdateWindow : Window
{
    private readonly IUpdateService _updateService;
    private readonly UpdateViewModel _viewModel;
    private bool _allowClose;

    public UpdateWindow(IUpdateService updateService, UpdateViewModel viewModel)
    {
        _updateService = updateService;
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Closing += UpdateWindow_Closing;
        Closed += UpdateWindow_Closed;
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var packagePath = await _viewModel.DownloadAsync();
        if (packagePath is null)
        {
            return;
        }

        try
        {
            _updateService.LaunchUpdate(packagePath);
            _allowClose = true;
            Application.Current.Shutdown();
        }
        catch (UpdateException exception)
        {
            AppLog.Write("ERROR", exception.Message);
            MessageBox.Show(
                this,
                $"{exception.Message}\n\n更新包已保留，可稍后重新打开：\n{packagePath}",
                "无法启动更新程序",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.CancelDownload();

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        Close();
    }

    private void ReleasePageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_viewModel.Release.ReleasePageUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            AppLog.Write("ERROR", exception.Message);
            MessageBox.Show(
                this,
                "无法打开发布页面，请稍后重试。",
                "打开失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UpdateWindow_Closing(object? sender, CancelEventArgs eventArgs)
    {
        if (_viewModel.IsDownloading && !_allowClose)
        {
            _viewModel.CancelDownload();
            eventArgs.Cancel = true;
        }
    }

    private void UpdateWindow_Closed(object? sender, EventArgs e) => _viewModel.Dispose();
}
