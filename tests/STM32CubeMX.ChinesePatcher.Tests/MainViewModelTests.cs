using STM32CubeMX.ChinesePatcher.Core.Abstractions;
using STM32CubeMX.ChinesePatcher.Core.Models;
using STM32CubeMX.ChinesePatcher.Core.Services;
using STM32CubeMX.ChinesePatcher.Services;
using STM32CubeMX.ChinesePatcher.Tests.Support;
using STM32CubeMX.ChinesePatcher.ViewModels;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public async Task RefreshRunningStateAsync_ReenablesApplyAfterCubeMxStops()
    {
        using var install = new TempCubeMxFixture();
        install.WriteExpectedPayload(new FakePayloadProvider());
        File.AppendAllText(
            install.IniPath,
            PatcherPaths.AgentLine(install.RootPath) + Environment.NewLine);
        var processSource = new MutableProcessSource
        {
            Handler = () => new ProcessQueryResult(
                true,
                [new ProcessSnapshot("STM32CubeMX", install.Installation().ExecutablePath)])
        };
        var viewModel = CreateViewModel(processSource);
        await viewModel.SelectManualPathAsync(install.RootPath);

        Assert.IsFalse(viewModel.ApplyCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RollbackCommand.CanExecute(null));
        Assert.IsTrue(viewModel.NeedsRunningStateRefresh);

        processSource.Handler = () => new ProcessQueryResult(true, []);
        await viewModel.RefreshRunningStateAsync();

        Assert.IsTrue(viewModel.ApplyCommand.CanExecute(null));
        Assert.IsTrue(viewModel.RollbackCommand.CanExecute(null));
        Assert.IsFalse(viewModel.NeedsRunningStateRefresh);
        Assert.AreEqual("未运行", viewModel.RunningValue);
        Assert.IsTrue(viewModel.Logs.Any(entry => entry.Message.Contains("可以继续操作", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task InitialUnknownState_DoesNotStartContinuousMonitoring()
    {
        using var install = new TempCubeMxFixture();
        var processSource = new MutableProcessSource
        {
            Handler = () => new ProcessQueryResult(false, [], "access denied")
        };
        var viewModel = CreateViewModel(processSource);

        await viewModel.SelectManualPathAsync(install.RootPath);

        Assert.IsFalse(viewModel.ApplyCommand.CanExecute(null));
        Assert.IsFalse(viewModel.NeedsRunningStateRefresh);
        Assert.AreEqual("未知", viewModel.RunningValue);
    }

    [TestMethod]
    public async Task RefreshRunningStateAsync_DoesNotQueryBeforeInstallationIsDetected()
    {
        var processSource = new MutableProcessSource
        {
            Handler = () => throw new InvalidOperationException("不应读取进程")
        };
        var viewModel = CreateViewModel(processSource);

        await viewModel.RefreshRunningStateAsync();

        Assert.AreEqual(0, processSource.ReadCount);
        Assert.IsFalse(viewModel.ApplyCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task RefreshRunningStateAsync_ContinuesMonitoringAfterTransientQueryFailure()
    {
        using var install = new TempCubeMxFixture();
        var processSource = new MutableProcessSource
        {
            Handler = () => new ProcessQueryResult(
                true,
                [new ProcessSnapshot("STM32CubeMX", install.Installation().ExecutablePath)])
        };
        var viewModel = CreateViewModel(processSource);
        await viewModel.SelectManualPathAsync(install.RootPath);

        processSource.Handler = () => new ProcessQueryResult(false, [], "access denied");
        await viewModel.RefreshRunningStateAsync();

        Assert.IsFalse(viewModel.ApplyCommand.CanExecute(null));
        Assert.IsTrue(viewModel.NeedsRunningStateRefresh);
        Assert.AreEqual("未知", viewModel.RunningValue);
        StringAssert.Contains(viewModel.StatusMessage, "无法确认 STM32CubeMX");

        processSource.Handler = () => new ProcessQueryResult(true, []);
        await viewModel.RefreshRunningStateAsync();

        Assert.IsTrue(viewModel.ApplyCommand.CanExecute(null));
        Assert.IsFalse(viewModel.NeedsRunningStateRefresh);
    }

    private static MainViewModel CreateViewModel(IProcessSource processSource)
    {
        var payloadProvider = new FakePayloadProvider();
        var processStateService = new ProcessStateService(processSource);
        var stateInspector = new PatchStateInspector(payloadProvider);
        var patchService = new PatchService(
            payloadProvider,
            processStateService,
            stateInspector,
            new FixedClock(new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero)));
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

    private sealed class MutableProcessSource : IProcessSource
    {
        public required Func<ProcessQueryResult> Handler { get; set; }

        public int ReadCount { get; private set; }

        public ProcessQueryResult ReadProcesses()
        {
            ReadCount++;
            return Handler();
        }
    }
}
