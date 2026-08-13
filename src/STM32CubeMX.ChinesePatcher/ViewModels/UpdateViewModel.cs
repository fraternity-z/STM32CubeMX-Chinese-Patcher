using STM32CubeMX.ChinesePatcher.Models;
using STM32CubeMX.ChinesePatcher.Services;

namespace STM32CubeMX.ChinesePatcher.ViewModels;

public sealed class UpdateViewModel : ObservableObject, IDisposable
{
    private readonly IUpdateService _updateService;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _downloadCancellation;
    private Task<string?>? _downloadTask;
    private bool _isDownloading;
    private int _progressValue;
    private string _progressText = "等待下载";
    private string? _errorMessage;

    public UpdateViewModel(IUpdateService updateService, UpdateRelease release)
    {
        ArgumentNullException.ThrowIfNull(updateService);
        ArgumentNullException.ThrowIfNull(release);
        _updateService = updateService;
        Release = release;
    }

    public UpdateRelease Release { get; }

    public string VersionText => $"v{Release.DisplayVersion}";

    public string ReleaseNotes => Release.ReleaseNotes;

    public string PackageName => Release.Package.FileName;

    public string PackageSize => FormatSize(Release.Package.Size);

    public string Checksum => Release.Package.Sha256;

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                OnPropertyChanged(nameof(CanStartDownload));
                OnPropertyChanged(nameof(CanCancelDownload));
            }
        }
    }

    public bool CanStartDownload => !IsDownloading;

    public bool CanCancelDownload => IsDownloading;

    public int ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public Task<string?> DownloadAsync()
    {
        lock (_syncRoot)
        {
            if (_downloadTask is { IsCompleted: false })
            {
                return _downloadTask;
            }

            _downloadCancellation?.Dispose();
            _downloadCancellation = new CancellationTokenSource();
            _downloadTask = DownloadCoreAsync(_downloadCancellation.Token);
            return _downloadTask;
        }
    }

    public void CancelDownload() => _downloadCancellation?.Cancel();

    public void Dispose()
    {
        _downloadCancellation?.Cancel();
        _downloadCancellation?.Dispose();
    }

    private async Task<string?> DownloadCoreAsync(CancellationToken cancellationToken)
    {
        IsDownloading = true;
        ErrorMessage = null;
        ProgressValue = 0;
        ProgressText = "正在准备下载";
        var progress = new Progress<UpdateDownloadProgress>(value =>
        {
            ProgressValue = value.Percentage;
            ProgressText = value.TotalBytes > 0
                ? $"已下载 {FormatSize(value.BytesReceived)} / {FormatSize(value.TotalBytes)}"
                : $"已下载 {FormatSize(value.BytesReceived)}";
        });

        try
        {
            var packagePath = await _updateService.DownloadUpdateAsync(
                Release,
                progress,
                cancellationToken);
            ProgressValue = 100;
            ProgressText = "下载完成，正在启动更新程序";
            return packagePath;
        }
        catch (OperationCanceledException)
        {
            ProgressValue = 0;
            ProgressText = "已取消下载";
            return null;
        }
        catch (UpdateException exception)
        {
            ErrorMessage = exception.Message;
            ProgressText = "下载失败";
            AppLog.Write("ERROR", exception.Message);
            return null;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:F1} KB";
        }

        return $"{bytes / (1024d * 1024d):F1} MB";
    }
}
