using System.Security.Cryptography;
using System.Text;
using STM32CubeMX.ChinesePatcher.Models;
using STM32CubeMX.ChinesePatcher.Services;
using STM32CubeMX.ChinesePatcher.ViewModels;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class AboutViewModelTests
{
    [TestMethod]
    public void Constructor_ShowsCurrentVersionAndIdleState()
    {
        using var viewModel = new AboutViewModel(
            new FakeUpdateService(),
            new Version(1, 2));

        Assert.AreEqual("v1.2.0", viewModel.CurrentVersionText);
        Assert.AreEqual(UpdateCheckState.Idle, viewModel.CheckState);
        Assert.AreEqual("尚未检查更新", viewModel.StatusTitle);
        StringAssert.Contains(viewModel.StatusMessage, "手动获取");
        Assert.AreEqual("#52606D", viewModel.StatusAccent);
        Assert.AreEqual("#F2F5F7", viewModel.StatusSurface);
        Assert.AreEqual("\uE946", viewModel.StatusIcon);
        Assert.AreEqual("检查更新", viewModel.CheckButtonText);
        Assert.IsTrue(viewModel.CheckCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_ConcurrentCallsShareTaskAndShowLatestState()
    {
        var response = new TaskCompletionSource<UpdateRelease?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeUpdateService
        {
            CheckHandler = (_, _) => response.Task
        };
        using var viewModel = new AboutViewModel(service, new Version(1, 0, 0));

        var first = viewModel.CheckForUpdatesAsync();
        var second = viewModel.CheckForUpdatesAsync();

        Assert.AreSame(first, second);
        Assert.IsTrue(viewModel.IsChecking);
        Assert.AreEqual("正在检查更新", viewModel.StatusTitle);
        StringAssert.Contains(viewModel.StatusMessage, "GitHub");
        Assert.AreEqual("#52606D", viewModel.StatusAccent);
        Assert.AreEqual("#F2F5F7", viewModel.StatusSurface);
        Assert.AreEqual("\uE895", viewModel.StatusIcon);
        Assert.AreEqual("正在检查", viewModel.CheckButtonText);
        Assert.IsFalse(viewModel.CheckCommand.CanExecute(null));
        response.SetResult(null);
        await first;

        Assert.AreEqual(1, service.CheckCount);
        Assert.IsTrue(viewModel.IsLatest);
        Assert.AreEqual("当前已是最新版本", viewModel.StatusTitle);
        StringAssert.Contains(viewModel.StatusMessage, "v1.0.0");
        Assert.AreEqual("#1C7C54", viewModel.StatusAccent);
        Assert.AreEqual("#ECF8F1", viewModel.StatusSurface);
        Assert.AreEqual("\uE73E", viewModel.StatusIcon);
        Assert.AreEqual("重新检查", viewModel.CheckButtonText);
        Assert.IsTrue(viewModel.CheckCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_NewReleaseShowsVersionNotesAndEntry()
    {
        var release = CreateRelease();
        var service = new FakeUpdateService
        {
            CheckHandler = (_, _) => Task.FromResult<UpdateRelease?>(release)
        };
        using var viewModel = new AboutViewModel(service, new Version(1, 0, 0));

        await viewModel.CheckForUpdatesAsync();

        Assert.IsTrue(viewModel.HasUpdate);
        Assert.AreSame(release, viewModel.AvailableRelease);
        Assert.AreEqual("v1.1.0", viewModel.LatestVersionText);
        Assert.AreEqual("修复问题", viewModel.ReleaseNotes);
        StringAssert.Contains(viewModel.StatusTitle, "v1.1.0");
        StringAssert.Contains(viewModel.StatusMessage, "下载流程");
        Assert.AreEqual("#1677A6", viewModel.StatusAccent);
        Assert.AreEqual("#EAF5FB", viewModel.StatusSurface);
        Assert.AreEqual("\uE895", viewModel.StatusIcon);
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_FailureShowsFriendlyMessageAndCanRetry()
    {
        var service = new FakeUpdateService
        {
            CheckHandler = (_, _) => Task.FromException<UpdateRelease?>(
                new UpdateException("服务器返回错误。"))
        };
        using var viewModel = new AboutViewModel(service, new Version(1, 0, 0));

        await viewModel.CheckForUpdatesAsync();

        Assert.IsTrue(viewModel.HasError);
        Assert.AreEqual("检查更新失败", viewModel.StatusTitle);
        StringAssert.Contains(viewModel.StatusMessage, "网络连接");
        StringAssert.Contains(viewModel.StatusMessage, "服务器");
        StringAssert.Contains(viewModel.StatusMessage, "超时");
        Assert.AreEqual("#B42318", viewModel.StatusAccent);
        Assert.AreEqual("#FFF1F0", viewModel.StatusSurface);
        Assert.AreEqual("\uE783", viewModel.StatusIcon);
        Assert.AreEqual("重试", viewModel.CheckButtonText);
        Assert.IsTrue(viewModel.CheckCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_RetryStartsNewRequestAndCanRecover()
    {
        var service = new FakeUpdateService();
        service.CheckHandler = (_, _) => service.CheckCount == 1
            ? Task.FromException<UpdateRelease?>(new HttpRequestException("offline"))
            : Task.FromResult<UpdateRelease?>(null);
        using var viewModel = new AboutViewModel(service, new Version(1, 0, 0));

        await viewModel.CheckForUpdatesAsync();
        await viewModel.CheckForUpdatesAsync();

        Assert.AreEqual(2, service.CheckCount);
        Assert.IsTrue(viewModel.IsLatest);
        Assert.IsFalse(viewModel.HasError);
    }

    [TestMethod]
    public async Task Dispose_CancelsInFlightCheckWithoutSurfacingError()
    {
        var service = new FakeUpdateService
        {
            CheckHandler = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            }
        };
        var viewModel = new AboutViewModel(service, new Version(1, 0, 0));

        var check = viewModel.CheckForUpdatesAsync();
        viewModel.Dispose();
        viewModel.Dispose();
        await check;

        Assert.IsFalse(viewModel.HasError);
        await viewModel.CheckForUpdatesAsync();
        Assert.AreEqual(1, service.CheckCount);
    }

    private static UpdateRelease CreateRelease()
    {
        const string packageName = "STM32CubeMX-Chinese-Patcher-v1.1.0-win-x64.exe";
        return new UpdateRelease(
            new Version(1, 1, 0),
            "1.1.0",
            "修复问题",
            new Uri("https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/tag/v1.1.0"),
            new UpdatePackage(
                packageName,
                1024,
                new Uri(
                    "https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/download/v1.1.0/"
                    + packageName),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("package")))));
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public Func<Version, CancellationToken, Task<UpdateRelease?>> CheckHandler { get; set; } =
            (_, _) => Task.FromResult<UpdateRelease?>(null);

        public int CheckCount { get; private set; }

        public Task<UpdateRelease?> CheckForUpdateAsync(
            Version currentVersion,
            CancellationToken cancellationToken = default)
        {
            CheckCount++;
            return CheckHandler(currentVersion, cancellationToken);
        }

        public Task<string> DownloadUpdateAsync(
            UpdateRelease release,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default) => Task.FromResult("update.exe");

        public void LaunchUpdate(string packagePath)
        {
        }
    }
}
