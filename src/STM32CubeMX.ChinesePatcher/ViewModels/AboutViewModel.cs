using STM32CubeMX.ChinesePatcher.Models;
using STM32CubeMX.ChinesePatcher.Services;

namespace STM32CubeMX.ChinesePatcher.ViewModels;

public enum UpdateCheckState
{
    Idle,
    Checking,
    Latest,
    UpdateAvailable,
    Failed
}

public sealed class AboutViewModel : ObservableObject, IDisposable
{
    private readonly IUpdateService _updateService;
    private readonly Version _currentVersion;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _checkSync = new();
    private Task? _checkTask;
    private UpdateCheckState _checkState;
    private UpdateRelease? _availableRelease;
    private bool _isDisposed;

    public AboutViewModel(IUpdateService updateService, Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(updateService);
        ArgumentNullException.ThrowIfNull(currentVersion);
        _updateService = updateService;
        _currentVersion = currentVersion;
        CheckCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsChecking);
    }

    public AsyncRelayCommand CheckCommand { get; }

    public string CurrentVersionText => $"v{FormatVersion(_currentVersion)}";

    public UpdateCheckState CheckState
    {
        get => _checkState;
        private set
        {
            if (!SetProperty(ref _checkState, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsChecking));
            OnPropertyChanged(nameof(IsLatest));
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(StatusTitle));
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(StatusAccent));
            OnPropertyChanged(nameof(StatusSurface));
            OnPropertyChanged(nameof(StatusIcon));
            OnPropertyChanged(nameof(CheckButtonText));
            CheckCommand.RaiseCanExecuteChanged();
        }
    }

    public UpdateRelease? AvailableRelease
    {
        get => _availableRelease;
        private set
        {
            if (SetProperty(ref _availableRelease, value))
            {
                OnPropertyChanged(nameof(LatestVersionText));
                OnPropertyChanged(nameof(ReleaseNotes));
            }
        }
    }

    public bool IsChecking => CheckState == UpdateCheckState.Checking;

    public bool IsLatest => CheckState == UpdateCheckState.Latest;

    public bool HasUpdate => CheckState == UpdateCheckState.UpdateAvailable;

    public bool HasError => CheckState == UpdateCheckState.Failed;

    public string StatusTitle => CheckState switch
    {
        UpdateCheckState.Checking => "正在检查更新",
        UpdateCheckState.Latest => "当前已是最新版本",
        UpdateCheckState.UpdateAvailable => $"发现新版本 {LatestVersionText}",
        UpdateCheckState.Failed => "检查更新失败",
        _ => "尚未检查更新"
    };

    public string StatusMessage => CheckState switch
    {
        UpdateCheckState.Checking => "正在连接 GitHub 发布服务器，请稍候。",
        UpdateCheckState.Latest => $"当前版本 {CurrentVersionText} 已是最新版本。",
        UpdateCheckState.UpdateAvailable => "新版本已准备好，可查看更新说明并进入下载流程。",
        UpdateCheckState.Failed =>
            "无法获取更新信息。请检查网络连接后重试；更新服务器可能暂时不可用，或请求已超时。",
        _ => "可随时手动获取官方发布仓库中的最新版本信息。"
    };

    public string StatusAccent => CheckState switch
    {
        UpdateCheckState.Latest => "#1C7C54",
        UpdateCheckState.UpdateAvailable => "#1677A6",
        UpdateCheckState.Failed => "#B42318",
        _ => "#52606D"
    };

    public string StatusSurface => CheckState switch
    {
        UpdateCheckState.Latest => "#ECF8F1",
        UpdateCheckState.UpdateAvailable => "#EAF5FB",
        UpdateCheckState.Failed => "#FFF1F0",
        _ => "#F2F5F7"
    };

    public string StatusIcon => CheckState switch
    {
        UpdateCheckState.Checking => "\uE895",
        UpdateCheckState.Latest => "\uE73E",
        UpdateCheckState.UpdateAvailable => "\uE895",
        UpdateCheckState.Failed => "\uE783",
        _ => "\uE946"
    };

    public string CheckButtonText => CheckState switch
    {
        UpdateCheckState.Checking => "正在检查",
        UpdateCheckState.Failed => "重试",
        UpdateCheckState.Idle => "检查更新",
        _ => "重新检查"
    };

    public string LatestVersionText => AvailableRelease is null
        ? string.Empty
        : $"v{AvailableRelease.DisplayVersion}";

    public string ReleaseNotes => AvailableRelease?.ReleaseNotes ?? string.Empty;

    public Task CheckForUpdatesAsync()
    {
        lock (_checkSync)
        {
            if (_isDisposed)
            {
                return Task.CompletedTask;
            }

            if (_checkTask is { IsCompleted: false })
            {
                return _checkTask;
            }

            _checkTask = CheckForUpdatesCoreAsync();
            return _checkTask;
        }
    }

    public void Dispose()
    {
        lock (_checkSync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
        }
    }

    private async Task CheckForUpdatesCoreAsync()
    {
        AvailableRelease = null;
        CheckState = UpdateCheckState.Checking;
        var cancellationToken = _lifetimeCancellation.Token;

        try
        {
            var release = await _updateService.CheckForUpdateAsync(
                _currentVersion,
                cancellationToken);
            if (_isDisposed)
            {
                return;
            }

            AvailableRelease = release;
            CheckState = release is null
                ? UpdateCheckState.Latest
                : UpdateCheckState.UpdateAvailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_isDisposed)
            {
                return;
            }

            AppLog.Write("WARN", $"手动检查更新失败：{exception.Message}");
            CheckState = UpdateCheckState.Failed;
        }
    }

    private static string FormatVersion(Version version) =>
        $"{Math.Max(0, version.Major)}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";
}
