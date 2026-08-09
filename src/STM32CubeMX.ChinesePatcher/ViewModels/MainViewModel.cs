using System.Collections.ObjectModel;
using System.Reflection;
using STM32CubeMX.ChinesePatcher.Core.Models;
using STM32CubeMX.ChinesePatcher.Core.Services;
using STM32CubeMX.ChinesePatcher.Services;

namespace STM32CubeMX.ChinesePatcher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly InstallationDetector _installationDetector;
    private readonly ProcessStateService _processStateService;
    private readonly PatchStateInspector _stateInspector;
    private readonly OperationCoordinator _operationCoordinator;
    private CubeMxInstallation? _installation;
    private string? _manualPath;
    private long _refreshSequence;
    private string _pathDisplay = "正在自动检测...";
    private string _detectionSourceText = "自动检测";
    private string _cubeVersion = "--";
    private string _versionDetail = "等待检测";
    private string _runningValue = "--";
    private string _runningDetail = "等待检测";
    private string _runningAccent = "#6B7280";
    private string _runningSurface = "#F3F4F6";
    private string _runningIcon = "\uE783";
    private string _localizationValue = "--";
    private string _localizationDetail = "等待检测";
    private string _localizationAccent = "#6B7280";
    private string _localizationSurface = "#F3F4F6";
    private string _localizationIcon = "\uE783";
    private string _statusMessage = "正在读取本机 STM32CubeMX 状态";
    private string _statusAccent = "#1677A6";
    private bool _isBusy;
    private bool _isProgressVisible;
    private int _progressValue;
    private string _progressText = string.Empty;
    private RunningState _runningState = RunningState.Unknown;
    private PatchInspection _inspection = new(
        LocalizationState.NotInstalled,
        "尚未检测。",
        false,
        false);

    public MainViewModel(
        InstallationDetector installationDetector,
        ProcessStateService processStateService,
        PatchStateInspector stateInspector,
        OperationCoordinator operationCoordinator)
    {
        _installationDetector = installationDetector;
        _processStateService = processStateService;
        _stateInspector = stateInspector;
        _operationCoordinator = operationCoordinator;

        RefreshCommand = new AsyncRelayCommand(RefreshAutomaticAsync, () => !IsBusy);
        ApplyCommand = new AsyncRelayCommand(
            () => PerformAsync(PatchOperation.Apply),
            CanApply);
        RollbackCommand = new AsyncRelayCommand(
            () => PerformAsync(PatchOperation.Rollback),
            CanRollback);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ApplyCommand { get; }

    public AsyncRelayCommand RollbackCommand { get; }

    public ObservableCollection<LogEntryViewModel> Logs { get; } = [];

    public string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public string PathDisplay
    {
        get => _pathDisplay;
        private set => SetProperty(ref _pathDisplay, value);
    }

    public string DetectionSourceText
    {
        get => _detectionSourceText;
        private set => SetProperty(ref _detectionSourceText, value);
    }

    public string CubeVersion
    {
        get => _cubeVersion;
        private set => SetProperty(ref _cubeVersion, value);
    }

    public string VersionDetail
    {
        get => _versionDetail;
        private set => SetProperty(ref _versionDetail, value);
    }

    public string RunningValue
    {
        get => _runningValue;
        private set => SetProperty(ref _runningValue, value);
    }

    public string RunningDetail
    {
        get => _runningDetail;
        private set => SetProperty(ref _runningDetail, value);
    }

    public string RunningAccent
    {
        get => _runningAccent;
        private set => SetProperty(ref _runningAccent, value);
    }

    public string RunningSurface
    {
        get => _runningSurface;
        private set => SetProperty(ref _runningSurface, value);
    }

    public string RunningIcon
    {
        get => _runningIcon;
        private set => SetProperty(ref _runningIcon, value);
    }

    public string LocalizationValue
    {
        get => _localizationValue;
        private set => SetProperty(ref _localizationValue, value);
    }

    public string LocalizationDetail
    {
        get => _localizationDetail;
        private set => SetProperty(ref _localizationDetail, value);
    }

    public string LocalizationAccent
    {
        get => _localizationAccent;
        private set => SetProperty(ref _localizationAccent, value);
    }

    public string LocalizationSurface
    {
        get => _localizationSurface;
        private set => SetProperty(ref _localizationSurface, value);
    }

    public string LocalizationIcon
    {
        get => _localizationIcon;
        private set => SetProperty(ref _localizationIcon, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string StatusAccent
    {
        get => _statusAccent;
        private set => SetProperty(ref _statusAccent, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanBrowse));
                RaiseCommandStates();
            }
        }
    }

    public bool CanBrowse => !IsBusy;

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        private set => SetProperty(ref _isProgressVisible, value);
    }

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

    public string ApplyButtonText =>
        _inspection.State == LocalizationState.Installed ? "重新汉化" : "一键汉化";

    public async Task InitializeAsync() => await RefreshStatusAsync(useAutomaticDetection: true);

    public async Task SelectManualPathAsync(string path)
    {
        _manualPath = path;
        await RefreshStatusAsync(useAutomaticDetection: false);
    }

    private async Task RefreshAutomaticAsync()
    {
        _manualPath = null;
        await RefreshStatusAsync(useAutomaticDetection: true);
    }

    private async Task RefreshStatusAsync(bool useAutomaticDetection)
    {
        var sequence = Interlocked.Increment(ref _refreshSequence);
        IsBusy = true;
        StatusMessage = useAutomaticDetection ? "正在自动检测安装信息" : "正在验证所选目录";
        StatusAccent = "#1677A6";

        try
        {
            var result = await Task.Run(() => useAutomaticDetection
                ? _installationDetector.Detect()
                : _installationDetector.DetectManualPath(_manualPath));
            if (sequence != _refreshSequence)
            {
                return;
            }

            if (!result.Found)
            {
                SetNotDetected(result.Warnings.LastOrDefault() ?? "未检测到 STM32CubeMX 安装目录。");
                AddLog("WARN", "未检测到有效安装目录，可点击浏览手动选择。");
                return;
            }

            var installation = result.Installation!;
            var status = await Task.Run(() => (
                Running: _processStateService.GetState(installation),
                Inspection: _stateInspector.Inspect(installation)));
            if (sequence != _refreshSequence)
            {
                return;
            }

            _installation = installation;
            _runningState = status.Running;
            _inspection = status.Inspection;
            UpdateDisplay();
            AddLog("INFO", $"已检测到 STM32CubeMX {installation.Version}。");
        }
        catch (Exception exception)
        {
            AppLog.Write("ERROR", exception.Message);
            SetNotDetected($"检测失败：{exception.Message}");
            AddLog("ERROR", $"检测失败：{exception.Message}");
        }
        finally
        {
            if (sequence == _refreshSequence)
            {
                IsBusy = false;
            }
        }
    }

    private async Task PerformAsync(PatchOperation operation)
    {
        var installation = _installation;
        if (installation is null)
        {
            StatusMessage = "请先选择有效的 STM32CubeMX 安装目录";
            StatusAccent = "#B42318";
            return;
        }

        IsBusy = true;
        IsProgressVisible = true;
        ProgressValue = 0;
        ProgressText = operation == PatchOperation.Apply ? "准备汉化" : "准备回退";
        var progress = new Progress<OperationProgress>(value =>
        {
            ProgressValue = value.Percentage;
            ProgressText = value.Message;
        });

        try
        {
            var result = await _operationCoordinator.ExecuteAsync(
                operation,
                installation,
                progress);
            StatusMessage = result.Message;
            StatusAccent = result.Succeeded ? "#1C7C54" : "#B42318";
            AddLog(result.Succeeded ? "OK" : "ERROR", result.Message);
            foreach (var detail in result.Details)
            {
                AddLog("INFO", detail);
            }
        }
        catch (Exception exception)
        {
            AppLog.Write("ERROR", exception.Message);
            StatusMessage = exception.Message;
            StatusAccent = "#B42318";
            AddLog("ERROR", exception.Message);
        }
        finally
        {
            ProgressValue = 100;
            IsBusy = false;
            await RefreshStatusAsync(useAutomaticDetection: _manualPath is null);
            IsProgressVisible = false;
        }
    }

    private void UpdateDisplay()
    {
        var installation = _installation!;
        PathDisplay = installation.RootPath;
        DetectionSourceText = SourceToText(installation.Source);
        CubeVersion = installation.Version;
        VersionDetail = $"JRE {installation.JavaVersion}";

        (RunningValue, RunningDetail, RunningAccent, RunningSurface) = _runningState switch
        {
            RunningState.Running => ("运行中", "请关闭 CubeMX 后再修改", "#B42318", "#FFF1F0"),
            RunningState.Stopped => ("未运行", "配置文件可安全修改", "#1C7C54", "#ECF8F1"),
            _ => ("未知", "无法读取完整进程信息", "#A96000", "#FFF7E6")
        };
        RunningIcon = _runningState switch
        {
            RunningState.Stopped => "\uE73E",
            RunningState.Running => "\uE769",
            _ => "\uE783"
        };

        (LocalizationValue, LocalizationAccent, LocalizationSurface) = _inspection.State switch
        {
            LocalizationState.Installed => ("已汉化", "#1C7C54", "#ECF8F1"),
            LocalizationState.NotInstalled => ("未汉化", "#52606D", "#F2F5F7"),
            LocalizationState.NeedsUpdate => ("需要更新", "#A96000", "#FFF7E6"),
            LocalizationState.Damaged => ("配置异常", "#B42318", "#FFF1F0"),
            LocalizationState.Conflict => ("文件冲突", "#B42318", "#FFF1F0"),
            _ => ("未知", "#52606D", "#F2F5F7")
        };
        LocalizationDetail = _inspection.Message;
        LocalizationIcon = _inspection.State switch
        {
            LocalizationState.Installed => "\uE73E",
            LocalizationState.NotInstalled => "\uE711",
            LocalizationState.NeedsUpdate => "\uE895",
            _ => "\uE783"
        };

        if (_runningState == RunningState.Running)
        {
            StatusMessage = "STM32CubeMX 正在运行，请关闭后再操作";
            StatusAccent = "#B42318";
        }
        else if (_inspection.State == LocalizationState.Conflict)
        {
            StatusMessage = "发现来源不明的同名汉化文件，已停止覆盖";
            StatusAccent = "#B42318";
        }
        else
        {
            StatusMessage = _inspection.Message;
            StatusAccent = _inspection.State == LocalizationState.Installed ? "#1C7C54" : "#1677A6";
        }

        OnPropertyChanged(nameof(ApplyButtonText));
        RaiseCommandStates();
    }

    private void SetNotDetected(string reason)
    {
        _installation = null;
        _runningState = RunningState.Unknown;
        _inspection = new PatchInspection(LocalizationState.NotInstalled, "尚未检测。", false, false);
        PathDisplay = "未检测到安装目录";
        DetectionSourceText = "请手动选择";
        CubeVersion = "未检测到";
        VersionDetail = "--";
        RunningValue = "未知";
        RunningDetail = "等待有效安装目录";
        RunningAccent = "#6B7280";
        RunningSurface = "#F3F4F6";
        RunningIcon = "\uE783";
        LocalizationValue = "未知";
        LocalizationDetail = "等待有效安装目录";
        LocalizationAccent = "#6B7280";
        LocalizationSurface = "#F3F4F6";
        LocalizationIcon = "\uE783";
        StatusMessage = reason;
        StatusAccent = "#A96000";
        OnPropertyChanged(nameof(ApplyButtonText));
        RaiseCommandStates();
    }

    private bool CanApply() =>
        !IsBusy
        && _installation is not null
        && _runningState == RunningState.Stopped
        && _inspection.State != LocalizationState.Conflict;

    private bool CanRollback() =>
        !IsBusy
        && _installation is not null
        && _runningState == RunningState.Stopped
        && _inspection.HasManagedIniLine;

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
        RollbackCommand.RaiseCanExecuteChanged();
    }

    private void AddLog(string level, string message)
    {
        Logs.Insert(0, new LogEntryViewModel(DateTime.Now, level, message));
        while (Logs.Count > 100)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private static string SourceToText(DetectionSource source) => source switch
    {
        DetectionSource.EnvironmentVariable => "自动 · 环境变量",
        DetectionSource.Registry => "自动 · 注册表",
        DetectionSource.KnownLocation => "自动 · 常见目录",
        DetectionSource.Manual => "手动选择",
        _ => "未知来源"
    };
}

public sealed record LogEntryViewModel(DateTime Time, string Level, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");

    public string LevelColor => Level switch
    {
        "OK" => "#1C7C54",
        "WARN" => "#A96000",
        "ERROR" => "#B42318",
        _ => "#1677A6"
    };
}
