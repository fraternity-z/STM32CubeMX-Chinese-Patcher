using System.Security.Cryptography;
using System.Text;
using STM32CubeMX.ChinesePatcher.Models;
using STM32CubeMX.ChinesePatcher.Services;
using STM32CubeMX.ChinesePatcher.ViewModels;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class UpdateViewModelTests
{
    [TestMethod]
    public void Constructor_ExposesReleaseAndPackageInformation()
    {
        var release = CreateRelease(1024 * 1024);
        using var viewModel = new UpdateViewModel(new FakeUpdateService(), release);

        Assert.AreEqual("v1.1.0", viewModel.VersionText);
        Assert.AreEqual("修复问题", viewModel.ReleaseNotes);
        Assert.AreEqual(release.Package.FileName, viewModel.PackageName);
        Assert.AreEqual("1.0 MB", viewModel.PackageSize);
        Assert.AreEqual(release.Package.Sha256, viewModel.Checksum);
        Assert.IsTrue(viewModel.CanStartDownload);
        Assert.IsFalse(viewModel.CanCancelDownload);
        Assert.IsFalse(viewModel.HasError);
    }

    [TestMethod]
    public async Task DownloadAsync_ConcurrentCallsShareTaskAndComplete()
    {
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeUpdateService
        {
            DownloadHandler = (_, _, _) => gate.Task
        };
        using var viewModel = new UpdateViewModel(service, CreateRelease(2048));

        var first = viewModel.DownloadAsync();
        var second = viewModel.DownloadAsync();

        Assert.AreSame(first, second);
        Assert.IsTrue(viewModel.IsDownloading);
        Assert.IsFalse(viewModel.CanStartDownload);
        Assert.IsTrue(viewModel.CanCancelDownload);
        gate.SetResult("C:\\updates\\patcher.exe");

        Assert.AreEqual("C:\\updates\\patcher.exe", await first);
        Assert.AreEqual(1, service.DownloadCount);
        Assert.IsFalse(viewModel.IsDownloading);
        Assert.AreEqual(100, viewModel.ProgressValue);
        Assert.AreEqual("下载完成，正在启动更新程序", viewModel.ProgressText);
    }

    [TestMethod]
    public async Task CancelDownload_StopsDownloadWithoutError()
    {
        var service = new FakeUpdateService
        {
            DownloadHandler = async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return string.Empty;
            }
        };
        using var viewModel = new UpdateViewModel(service, CreateRelease(2048));

        var download = viewModel.DownloadAsync();
        viewModel.CancelDownload();

        Assert.IsNull(await download);
        Assert.IsFalse(viewModel.IsDownloading);
        Assert.AreEqual(0, viewModel.ProgressValue);
        Assert.AreEqual("已取消下载", viewModel.ProgressText);
        Assert.IsFalse(viewModel.HasError);
    }

    [TestMethod]
    public async Task DownloadAsync_ShowsClearServiceErrorAndAllowsRetry()
    {
        var service = new FakeUpdateService
        {
            DownloadHandler = (_, _, _) => Task.FromException<string>(
                new UpdateException("更新包完整性校验失败，文件已丢弃。"))
        };
        using var viewModel = new UpdateViewModel(service, CreateRelease(2048));

        var result = await viewModel.DownloadAsync();

        Assert.IsNull(result);
        Assert.IsTrue(viewModel.HasError);
        Assert.AreEqual("更新包完整性校验失败，文件已丢弃。", viewModel.ErrorMessage);
        Assert.AreEqual("下载失败", viewModel.ProgressText);
        Assert.IsTrue(viewModel.CanStartDownload);
    }

    private static UpdateRelease CreateRelease(long size)
    {
        const string packageName = "STM32CubeMX-Chinese-Patcher-v1.1.0-win-x64.exe";
        return new UpdateRelease(
            new Version(1, 1, 0),
            "1.1.0",
            "修复问题",
            new Uri("https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/tag/v1.1.0"),
            new UpdatePackage(
                packageName,
                size,
                new Uri(
                    "https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/download/v1.1.0/"
                    + packageName),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("package")))));
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public Func<UpdateRelease, IProgress<UpdateDownloadProgress>?, CancellationToken, Task<string>>
            DownloadHandler { get; init; } = (_, _, _) => Task.FromResult("update.exe");

        public int DownloadCount { get; private set; }

        public Task<UpdateRelease?> CheckForUpdateAsync(
            Version currentVersion,
            CancellationToken cancellationToken = default) => Task.FromResult<UpdateRelease?>(null);

        public Task<string> DownloadUpdateAsync(
            UpdateRelease release,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            return DownloadHandler(release, progress, cancellationToken);
        }

        public void LaunchUpdate(string packagePath)
        {
        }
    }
}
