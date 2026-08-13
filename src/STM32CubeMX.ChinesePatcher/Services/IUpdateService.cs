using STM32CubeMX.ChinesePatcher.Models;

namespace STM32CubeMX.ChinesePatcher.Services;

public interface IUpdateService
{
    Task<UpdateRelease?> CheckForUpdateAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default);

    Task<string> DownloadUpdateAsync(
        UpdateRelease release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    void LaunchUpdate(string packagePath);
}
