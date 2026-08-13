using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using STM32CubeMX.ChinesePatcher.Core.Models;
using STM32CubeMX.ChinesePatcher.Core.Services;
using STM32CubeMX.ChinesePatcher.Models;
using STM32CubeMX.ChinesePatcher.Services;
using STM32CubeMX.ChinesePatcher.Tests.Support;
using STM32CubeMX.ChinesePatcher.ViewModels;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class UpdateWindowTests
{
    [TestMethod]
    public void Constructors_LoadBindingsWithoutErrors()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new App();
                application.InitializeComponent();
                application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                using var viewModel = new UpdateViewModel(new FakeUpdateService(), CreateRelease());
                var window = new UpdateWindow(new FakeUpdateService(), viewModel);
                window.Show();
                window.UpdateLayout();
                window.Close();

                var aboutViewModel = new AboutViewModel(
                    new FakeUpdateService(),
                    new Version(1, 2, 3));
                var aboutWindow = new AboutWindow(
                    aboutViewModel,
                    (_, _) => { });
                aboutWindow.Show();
                aboutWindow.UpdateLayout();
                aboutWindow.Close();

                var mainWindow = new MainWindow(
                    CreateMainViewModel(),
                    new FakeUpdateService(),
                    new Version(1, 2, 3));
                mainWindow.Show();
                mainWindow.UpdateLayout();
                var aboutButton = (Button)mainWindow.FindName("AboutButton");
                Assert.IsNotNull(aboutButton.ContextMenu);
                Assert.HasCount(2, aboutButton.ContextMenu.Items);
                Assert.AreEqual("关于本软件", ((MenuItem)aboutButton.ContextMenu.Items[0]).Header);
                Assert.AreEqual("检查更新", ((MenuItem)aboutButton.ContextMenu.Items[1]).Header);

                var visibleUpdateWindowCount = -1;
                mainWindow.Dispatcher.BeginInvoke(() =>
                {
                    mainWindow.ShowUpdateWindow(mainWindow, CreateRelease());
                    var updateWindows = Application.Current.Windows
                        .OfType<UpdateWindow>()
                        .Where(candidate => candidate.IsVisible)
                        .ToArray();
                    visibleUpdateWindowCount = updateWindows.Length;
                    updateWindows[0].Close();
                });
                mainWindow.ShowUpdateWindow(mainWindow, CreateRelease());

                Assert.AreEqual(1, visibleUpdateWindowCount);
                Assert.IsFalse(mainWindow.IsUpdateWindowOpen);
                mainWindow.Close();
                application.Shutdown();
            }
            catch (Exception exception)
            {
                threadException = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "更新窗口构造测试超时。");

        Assert.IsNull(threadException, threadException?.ToString());
    }

    private static MainViewModel CreateMainViewModel()
    {
        var payloadProvider = new FakePayloadProvider();
        var processStateService = new ProcessStateService(
            new FakeProcessSource(new ProcessQueryResult(true, [])));
        var stateInspector = new PatchStateInspector(payloadProvider);
        var patchService = new PatchService(
            payloadProvider,
            processStateService,
            stateInspector,
            new FixedClock(new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero)));
        return new MainViewModel(
            new InstallationDetector(
                new FakeEnvironmentSource(),
                new FakeRegistrySource(),
                new FakeVersionSource(),
                []),
            processStateService,
            stateInspector,
            new OperationCoordinator(patchService));
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
        public Task<UpdateRelease?> CheckForUpdateAsync(
            Version currentVersion,
            CancellationToken cancellationToken = default) => Task.FromResult<UpdateRelease?>(null);

        public Task<string> DownloadUpdateAsync(
            UpdateRelease release,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default) => Task.FromResult("update.exe");

        public void LaunchUpdate(string packagePath)
        {
        }
    }
}
