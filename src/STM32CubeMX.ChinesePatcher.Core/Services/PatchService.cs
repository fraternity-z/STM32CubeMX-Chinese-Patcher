using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using STM32CubeMX.ChinesePatcher.Core.Abstractions;
using STM32CubeMX.ChinesePatcher.Core.Models;

namespace STM32CubeMX.ChinesePatcher.Core.Services;

public sealed class PatchService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IPayloadProvider _payloadProvider;
    private readonly ProcessStateService _processStateService;
    private readonly PatchStateInspector _stateInspector;
    private readonly IClock _clock;

    public PatchService(
        IPayloadProvider payloadProvider,
        ProcessStateService processStateService,
        PatchStateInspector stateInspector,
        IClock clock)
    {
        _payloadProvider = payloadProvider;
        _processStateService = processStateService;
        _stateInspector = stateInspector;
        _clock = clock;
    }

    public OperationResult Apply(
        CubeMxInstallation installation,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInstallation(installation);
        ValidateApplyCompatibility(installation);
        using var operationLock = InstallationOperationLock.Acquire(installation.RootPath);
        EnsureStopped(installation);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(10, "正在检查安装目录"));

        var inspection = _stateInspector.Inspect(installation);
        if (inspection.State == LocalizationState.Conflict)
        {
            throw new PatchConflictException(inspection.Message);
        }

        var payload = _payloadProvider.GetPayload();
        var localizationDirectory = PatcherPaths.LocalizationDirectory(installation.RootPath);
        Directory.CreateDirectory(localizationDirectory);

        var originalIni = File.ReadAllBytes(installation.IniPath);
        var backupPath = PatcherPaths.BackupPath(installation.RootPath);
        progress?.Report(new OperationProgress(30, "正在准备配置备份"));

        var iniLines = File.ReadAllLines(installation.IniPath)
            .Where(line => !PatchStateInspector.IsManagedAgentLine(line, installation.RootPath))
            .ToList();
        iniLines.Add(PatcherPaths.AgentLine(installation.RootPath));
        var iniBytes = EncodeLines(iniLines);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(55, "正在写入汉化载荷"));

        var state = new StateDocument
        {
            FormatVersion = 1,
            InstalledBy = "STM32CubeMX Chinese Patcher",
            InstalledAt = _clock.Now,
            Enabled = true,
            CubeMxRoot = installation.RootPath,
            AgentLine = PatcherPaths.AgentLine(installation.RootPath),
            AgentJarSha256 = payload.AgentJarSha256,
            DictionarySha256 = payload.DictionarySha256
        };

        using var transaction = new AtomicFileTransaction();
        transaction.Stage(PatcherPaths.AgentPath(installation.RootPath), payload.AgentJar);
        transaction.Stage(PatcherPaths.DictionaryPath(installation.RootPath), payload.Dictionary);
        if (!File.Exists(backupPath))
        {
            transaction.Stage(backupPath, originalIni);
        }

        progress?.Report(new OperationProgress(80, "正在更新启动配置"));
        transaction.Stage(installation.IniPath, iniBytes);
        transaction.Stage(
            PatcherPaths.StatePath(installation.RootPath),
            JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions));
        transaction.Commit(cancellationToken);

        progress?.Report(new OperationProgress(100, "汉化完成"));
        return OperationResult.Success(
            "汉化已启用。",
            $"版本：{installation.Version}",
            $"备份：{backupPath}");
    }

    public OperationResult Rollback(
        CubeMxInstallation installation,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInstallation(installation);
        using var operationLock = InstallationOperationLock.Acquire(installation.RootPath);
        EnsureStopped(installation);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(20, "正在检查启动配置"));

        var originalLines = File.ReadAllLines(installation.IniPath);
        var retainedLines = originalLines
            .Where(line => !PatchStateInspector.IsManagedAgentLine(line, installation.RootPath))
            .ToArray();
        var removedCount = originalLines.Length - retainedLines.Length;

        var payload = _payloadProvider.GetPayload();
        var state = new StateDocument
        {
            FormatVersion = 1,
            InstalledBy = "STM32CubeMX Chinese Patcher",
            InstalledAt = _clock.Now,
            Enabled = false,
            CubeMxRoot = installation.RootPath,
            AgentLine = PatcherPaths.AgentLine(installation.RootPath),
            AgentJarSha256 = payload.AgentJarSha256,
            DictionarySha256 = payload.DictionarySha256
        };

        progress?.Report(new OperationProgress(65, "正在移除汉化启动项"));
        using var transaction = new AtomicFileTransaction();
        transaction.Stage(installation.IniPath, EncodeLines(retainedLines));
        transaction.Stage(
            PatcherPaths.StatePath(installation.RootPath),
            JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions));
        transaction.Commit(cancellationToken);

        progress?.Report(new OperationProgress(100, "回退完成"));
        return OperationResult.Success(
            removedCount > 0 ? "汉化已回退。" : "当前已经是未汉化状态。",
            "汉化载荷和配置备份已保留，可随时重新启用。");
    }

    private void EnsureStopped(CubeMxInstallation installation)
    {
        var state = _processStateService.GetState(installation);
        if (state == RunningState.Running)
        {
            throw new InvalidOperationException("STM32CubeMX 正在运行，请关闭后重试。");
        }

        if (state == RunningState.Unknown)
        {
            throw new InvalidOperationException("无法确认 STM32CubeMX 的运行状态，为避免损坏配置，本次操作已取消。");
        }
    }

    private static void ValidateInstallation(CubeMxInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (!File.Exists(installation.ExecutablePath))
        {
            throw new FileNotFoundException("未找到 STM32CubeMX 主程序。", installation.ExecutablePath);
        }

        if (!File.Exists(installation.IniPath))
        {
            throw new FileNotFoundException("未找到 STM32CubeMX 启动配置。", installation.IniPath);
        }
    }

    private static void ValidateApplyCompatibility(CubeMxInstallation installation)
    {
        var blockReason = CubeMxCompatibility.GetApplyBlockReason(installation);
        if (blockReason is not null)
        {
            throw new InvalidOperationException(blockReason);
        }
    }

    private static byte[] EncodeLines(IEnumerable<string> lines)
    {
        var text = string.Join(Environment.NewLine, lines);
        if (text.Length > 0)
        {
            text += Environment.NewLine;
        }

        return Utf8NoBom.GetBytes(text);
    }

    private sealed class StateDocument
    {
        public int FormatVersion { get; init; }

        public required string InstalledBy { get; init; }

        public DateTimeOffset InstalledAt { get; init; }

        public bool Enabled { get; init; }

        public required string CubeMxRoot { get; init; }

        public required string AgentLine { get; init; }

        public required string AgentJarSha256 { get; init; }

        public required string DictionarySha256 { get; init; }
    }
}

public sealed class PatchConflictException(string message) : InvalidOperationException(message);

internal sealed class AtomicFileTransaction : IDisposable
{
    private readonly List<StagedFile> _stagedFiles = [];
    private bool _committed;

    public void Stage(string targetPath, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        if (_stagedFiles.Any(file => string.Equals(
                file.TargetPath,
                targetPath,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"同一事务不能重复暂存目标：{targetPath}");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"无法确定目标目录：{targetPath}");
        Directory.CreateDirectory(directory);
        var originalContent = File.Exists(targetPath) ? File.ReadAllBytes(targetPath) : null;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(true);
            }

            _stagedFiles.Add(new StagedFile(
                targetPath,
                temporaryPath,
                originalContent,
                content.ToArray()));
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public void Commit(CancellationToken cancellationToken)
    {
        var committedFiles = new List<StagedFile>();
        try
        {
            foreach (var stagedFile in _stagedFiles)
            {
                VerifyTargetUnchanged(stagedFile);
            }

            foreach (var stagedFile in _stagedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerifyTargetUnchanged(stagedFile);
                File.Move(stagedFile.TemporaryPath, stagedFile.TargetPath, true);
                committedFiles.Add(stagedFile);
            }

            _committed = true;
        }
        catch (Exception commitException)
        {
            var restoreExceptions = new List<Exception>();
            for (var index = committedFiles.Count - 1; index >= 0; index--)
            {
                try
                {
                    var committedFile = committedFiles[index];
                    if (!MatchesSnapshot(committedFile.TargetPath, committedFile.StagedContent))
                    {
                        restoreExceptions.Add(new IOException(
                            $"目标在提交后被其他程序修改，已保留外部内容：{committedFile.TargetPath}"));
                        continue;
                    }

                    Restore(committedFile);
                }
                catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException)
                {
                    restoreExceptions.Add(restoreException);
                }
            }

            if (restoreExceptions.Count > 0)
            {
                restoreExceptions.Insert(0, commitException);
                throw new AggregateException("文件事务失败，且部分目标无法安全恢复。", restoreExceptions);
            }

            throw;
        }
    }

    public void Dispose()
    {
        foreach (var stagedFile in _stagedFiles)
        {
            if (File.Exists(stagedFile.TemporaryPath))
            {
                TryDelete(stagedFile.TemporaryPath);
            }
        }

        if (!_committed)
        {
            _stagedFiles.Clear();
        }
    }

    private static void Restore(StagedFile stagedFile)
    {
        if (stagedFile.OriginalContent is null)
        {
            File.Delete(stagedFile.TargetPath);
            return;
        }

        var restorePath = $"{stagedFile.TargetPath}.{Guid.NewGuid():N}.restore";
        try
        {
            File.WriteAllBytes(restorePath, stagedFile.OriginalContent);
            File.Move(restorePath, stagedFile.TargetPath, true);
        }
        finally
        {
            TryDelete(restorePath);
        }
    }

    private static void VerifyTargetUnchanged(StagedFile stagedFile)
    {
        if (!MatchesSnapshot(stagedFile.TargetPath, stagedFile.OriginalContent))
        {
            throw new IOException($"目标文件已被其他程序修改，本次操作已取消：{stagedFile.TargetPath}");
        }
    }

    private static bool MatchesSnapshot(string path, byte[]? expectedContent)
    {
        if (expectedContent is null)
        {
            return !File.Exists(path);
        }

        return File.Exists(path)
            && File.ReadAllBytes(path).AsSpan().SequenceEqual(expectedContent);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record StagedFile(
        string TargetPath,
        string TemporaryPath,
        byte[]? OriginalContent,
        byte[] StagedContent);
}

internal sealed class InstallationOperationLock : IDisposable
{
    private readonly FileStream _lockStream;

    private InstallationOperationLock(FileStream lockStream)
    {
        _lockStream = lockStream;
    }

    public static InstallationOperationLock Acquire(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var normalizedPath = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        var lockDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "STM32CubeMX-Chinese-Patcher",
            "locks");
        Directory.CreateDirectory(lockDirectory);
        var lockPath = Path.Combine(lockDirectory, $"{pathHash}.lock");

        try
        {
            var lockStream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
            return new InstallationOperationLock(lockStream);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "另一个汉化操作正在处理该 STM32CubeMX 安装，请稍后重试。",
                exception);
        }
    }

    public void Dispose() => _lockStream.Dispose();
}
