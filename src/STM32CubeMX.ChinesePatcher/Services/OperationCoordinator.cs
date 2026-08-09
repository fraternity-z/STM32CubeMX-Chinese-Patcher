using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using STM32CubeMX.ChinesePatcher.Core.Models;
using STM32CubeMX.ChinesePatcher.Core.Services;

namespace STM32CubeMX.ChinesePatcher.Services;

[ExcludeFromCodeCoverage]
public sealed class OperationCoordinator(PatchService patchService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PatchService _patchService = patchService;

    public async Task<OperationResult> ExecuteAsync(
        PatchOperation operation,
        CubeMxInstallation installation,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);

        if (IsAdministrator() || CanWriteDirectory(installation.RootPath))
        {
            try
            {
                return await RunDirectAsync(operation, installation, progress, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                AppLog.Write("WARN", "直接写入权限不足，改用管理员进程。" );
            }
        }

        progress?.Report(new OperationProgress(5, "正在请求管理员权限"));
        return await RunElevatedAsync(operation, installation, cancellationToken);
    }

    public static async Task<int> RunWorkerAsync(
        string encodedRequest,
        PatchService patchService,
        CancellationToken cancellationToken = default)
    {
        ElevatedRequest? request = null;
        try
        {
            request = DecodeRequest(encodedRequest);
            var installation = new CubeMxInstallation(
                Path.GetFullPath(request.RootPath),
                request.Version,
                request.JavaVersion,
                DetectionSource.Manual);
            var result = await Task.Run(
                () => RunOperation(patchService, request.Operation, installation, null, cancellationToken),
                cancellationToken);
            WriteResult(request.RequestId, result);
            return result.Succeeded ? 0 : 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Write("ERROR", exception.Message);
            if (request is not null)
            {
                WriteResult(request.RequestId, OperationResult.Failure(exception.Message));
            }

            return 1;
        }
    }

    private async Task<OperationResult> RunDirectAsync(
        PatchOperation operation,
        CubeMxInstallation installation,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken) =>
        await Task.Run(
            () => RunOperation(_patchService, operation, installation, progress, cancellationToken),
            cancellationToken);

    private static OperationResult RunOperation(
        PatchService patchService,
        PatchOperation operation,
        CubeMxInstallation installation,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken) =>
        operation == PatchOperation.Apply
            ? patchService.Apply(installation, progress, cancellationToken)
            : patchService.Rollback(installation, progress, cancellationToken);

    private static async Task<OperationResult> RunElevatedAsync(
        PatchOperation operation,
        CubeMxInstallation installation,
        CancellationToken cancellationToken)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return OperationResult.Failure("无法定位当前程序，不能请求管理员权限。");
        }

        var request = new ElevatedRequest(
            Guid.NewGuid(),
            operation,
            installation.RootPath,
            installation.Version,
            installation.JavaVersion);
        var encodedRequest = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions)));
        var resultPath = GetResultPath(request.RequestId);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
        };
        startInfo.ArgumentList.Add("--elevated-worker");
        startInfo.ArgumentList.Add(encodedRequest);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return OperationResult.Failure("管理员进程启动失败。");
            }

            await process.WaitForExitAsync(cancellationToken);
            if (!File.Exists(resultPath))
            {
                return OperationResult.Failure("管理员进程未返回操作结果。", $"退出码：{process.ExitCode}");
            }

            var result = JsonSerializer.Deserialize<OperationResult>(
                await File.ReadAllTextAsync(resultPath, cancellationToken),
                JsonOptions);
            return result ?? OperationResult.Failure("管理员进程返回了无效结果。");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return OperationResult.Failure("已取消管理员授权。");
        }
        finally
        {
            try
            {
                File.Delete(resultPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool CanWriteDirectory(string rootPath)
    {
        var probePath = Path.Combine(rootPath, $".stm32cubemx-patcher-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(probePath);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
            }
        }
    }

    private static ElevatedRequest DecodeRequest(string encodedRequest)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedRequest));
        var request = JsonSerializer.Deserialize<ElevatedRequest>(json, JsonOptions)
            ?? throw new InvalidOperationException("管理员操作请求无效。");
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.RootPath))
        {
            throw new InvalidOperationException("管理员操作请求缺少必要参数。");
        }

        return request;
    }

    private static void WriteResult(Guid requestId, OperationResult result)
    {
        var resultPath = GetResultPath(requestId);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        var temporaryPath = $"{resultPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(result, JsonOptions), new UTF8Encoding(false));
        File.Move(temporaryPath, resultPath, true);
    }

    private static string GetResultPath(Guid requestId) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "STM32CubeMX-Chinese-Patcher",
            "results",
            $"{requestId:N}.json");

    private sealed record ElevatedRequest(
        Guid RequestId,
        PatchOperation Operation,
        string RootPath,
        string Version,
        string JavaVersion);
}
