using System.Text;
using System.Text.Json;
using STM32CubeMX.ChinesePatcher.Core.Models;
using STM32CubeMX.ChinesePatcher.Core.Services;
using STM32CubeMX.ChinesePatcher.Tests.Support;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class PatchServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 9, 10, 30, 0, TimeSpan.FromHours(8));
    private readonly FakePayloadProvider _payloadProvider = new();

    [TestMethod]
    public void Apply_WritesPayloadBackupStateAndSingleManagedLine()
    {
        using var install = new TempCubeMxFixture("-Dfile.encoding=UTF8\n-Xmx1024m\n");
        var originalIni = File.ReadAllBytes(install.IniPath);
        var progress = new ProgressCollector();

        var result = CreateService(StoppedProcesses()).Apply(install.Installation(), progress);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(_payloadProvider.GetPayload().AgentJar, File.ReadAllBytes(PatcherPaths.AgentPath(install.RootPath)));
        CollectionAssert.AreEqual(_payloadProvider.GetPayload().Dictionary, File.ReadAllBytes(PatcherPaths.DictionaryPath(install.RootPath)));
        CollectionAssert.AreEqual(originalIni, File.ReadAllBytes(PatcherPaths.BackupPath(install.RootPath)));
        Assert.AreEqual(1, File.ReadAllLines(install.IniPath).Count(line =>
            PatchStateInspector.IsManagedAgentLine(line, install.RootPath)));
        Assert.AreEqual(100, progress.Values.Last().Percentage);

        using var state = JsonDocument.Parse(File.ReadAllText(PatcherPaths.StatePath(install.RootPath)));
        Assert.IsTrue(state.RootElement.GetProperty("enabled").GetBoolean());
        Assert.AreEqual(install.RootPath, state.RootElement.GetProperty("cubeMxRoot").GetString());
    }

    [TestMethod]
    public void Apply_IsIdempotentAndPreservesFirstBackupAndCustomOptions()
    {
        using var install = new TempCubeMxFixture("-Dfile.encoding=UTF8\n");
        var service = CreateService(StoppedProcesses());
        service.Apply(install.Installation());
        var firstBackup = File.ReadAllBytes(PatcherPaths.BackupPath(install.RootPath));
        File.AppendAllText(install.IniPath, "-Xmx2048m" + Environment.NewLine, new UTF8Encoding(false));

        service.Apply(install.Installation());

        CollectionAssert.AreEqual(firstBackup, File.ReadAllBytes(PatcherPaths.BackupPath(install.RootPath)));
        var lines = File.ReadAllLines(install.IniPath);
        Assert.AreEqual(1, lines.Count(line => PatchStateInspector.IsManagedAgentLine(line, install.RootPath)));
        CollectionAssert.Contains(lines, "-Xmx2048m");
        var bytes = File.ReadAllBytes(install.IniPath);
        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [TestMethod]
    public void Rollback_RemovesOnlyCanonicalLineAndRetainsPayload()
    {
        using var install = new TempCubeMxFixture();
        var service = CreateService(StoppedProcesses());
        service.Apply(install.Installation());
        const string foreignLine = @"-javaagent:C:\Other\stm32cubemx-zh-agent.jar=C:\Other\translations.tsv";
        File.AppendAllText(install.IniPath, foreignLine + Environment.NewLine, new UTF8Encoding(false));
        var progress = new ProgressCollector();

        var result = service.Rollback(install.Installation(), progress);

        Assert.IsTrue(result.Succeeded);
        var lines = File.ReadAllLines(install.IniPath);
        Assert.IsFalse(lines.Any(line => PatchStateInspector.IsManagedAgentLine(line, install.RootPath)));
        CollectionAssert.Contains(lines, foreignLine);
        Assert.IsTrue(File.Exists(PatcherPaths.AgentPath(install.RootPath)));
        Assert.AreEqual(100, progress.Values.Last().Percentage);
    }

    [TestMethod]
    public void Rollback_IsIdempotentWhenAlreadyDisabled()
    {
        using var install = new TempCubeMxFixture();
        var service = CreateService(StoppedProcesses());

        var result = service.Rollback(install.Installation());

        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.Message, "已经是未汉化状态");
    }

    [TestMethod]
    public void Apply_BlocksWhenCubeMxIsRunning()
    {
        using var install = new TempCubeMxFixture();
        var processResult = new ProcessQueryResult(
            true,
            [new ProcessSnapshot("STM32CubeMX", install.Installation().ExecutablePath)]);
        var service = CreateService(processResult);

        var exception = TestAssert.Throws<InvalidOperationException>(() => service.Apply(install.Installation()));

        StringAssert.Contains(exception.Message, "正在运行");
        Assert.IsFalse(Directory.Exists(PatcherPaths.LocalizationDirectory(install.RootPath)));
    }

    [TestMethod]
    public void Apply_BlocksWhenRunningStateIsUnknown()
    {
        using var install = new TempCubeMxFixture();
        var service = CreateService(new ProcessQueryResult(false, [], "denied"));

        var exception = TestAssert.Throws<InvalidOperationException>(() => service.Apply(install.Installation()));

        StringAssert.Contains(exception.Message, "无法确认");
    }

    [TestMethod]
    public void Apply_BlocksUnownedMismatchedPayload()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), [1, 2, 3]);
        File.WriteAllText(PatcherPaths.DictionaryPath(install.RootPath), "foreign", Encoding.UTF8);
        var service = CreateService(StoppedProcesses());

        TestAssert.Throws<PatchConflictException>(() => service.Apply(install.Installation()));

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(PatcherPaths.AgentPath(install.RootPath)));
    }

    [TestMethod]
    public void Apply_BlocksUnownedIncompletePayloadWithoutOverwritingIt()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        var foreignAgent = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), foreignAgent);
        var service = CreateService(StoppedProcesses());

        TestAssert.Throws<PatchConflictException>(() => service.Apply(install.Installation()));

        CollectionAssert.AreEqual(foreignAgent, File.ReadAllBytes(PatcherPaths.AgentPath(install.RootPath)));
        Assert.IsFalse(File.Exists(PatcherPaths.DictionaryPath(install.RootPath)));
    }

    [TestMethod]
    public void Apply_BlocksPayloadWhenClaimedStateHashesDoNotMatch()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        var foreignAgent = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), foreignAgent);
        File.WriteAllText(PatcherPaths.DictionaryPath(install.RootPath), "foreign", Encoding.UTF8);
        File.WriteAllText(
            PatcherPaths.StatePath(install.RootPath),
            JsonSerializer.Serialize(new
            {
                formatVersion = 1,
                installedBy = "STM32CubeMX Chinese Patcher",
                cubeMxRoot = install.RootPath,
                agentLine = PatcherPaths.AgentLine(install.RootPath),
                agentJarSha256 = new string('0', 64),
                dictionarySha256 = new string('0', 64)
            }),
            Encoding.UTF8);
        var service = CreateService(StoppedProcesses());

        TestAssert.Throws<PatchConflictException>(() => service.Apply(install.Installation()));

        CollectionAssert.AreEqual(foreignAgent, File.ReadAllBytes(PatcherPaths.AgentPath(install.RootPath)));
    }

    [TestMethod]
    public void Apply_BlocksUnsupportedLineTargetingManagedAgentPath()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        var foreignAgent = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), foreignAgent);
        File.AppendAllText(
            install.IniPath,
            $"-javaagent:{PatcherPaths.AgentPath(install.RootPath)}{Environment.NewLine}");
        var service = CreateService(StoppedProcesses());

        TestAssert.Throws<PatchConflictException>(() => service.Apply(install.Installation()));

        CollectionAssert.AreEqual(foreignAgent, File.ReadAllBytes(PatcherPaths.AgentPath(install.RootPath)));
    }

    [TestMethod]
    public void Apply_RejectsUnsupportedJavaVersion()
    {
        using var install = new TempCubeMxFixture();
        var service = CreateService(StoppedProcesses());

        var exception = TestAssert.Throws<InvalidOperationException>(() =>
            service.Apply(install.Installation("17.0.12")));

        StringAssert.Contains(exception.Message, "JRE 21");
    }

    [TestMethod]
    public void Apply_RejectsUnknownJavaVersion()
    {
        using var install = new TempCubeMxFixture();
        var service = CreateService(StoppedProcesses());

        TestAssert.Throws<InvalidOperationException>(() => service.Apply(install.Installation("未知")));
    }

    [TestMethod]
    [DataRow("6.15.1")]
    [DataRow("6.19.0")]
    [DataRow("7.0.0-RC1")]
    [DataRow("未知")]
    public void Apply_RejectsUnverifiedCubeMxVersion(string version)
    {
        using var install = new TempCubeMxFixture();
        var service = CreateService(StoppedProcesses());
        var installation = install.Installation() with { Version = version };

        var exception = TestAssert.Throws<InvalidOperationException>(() => service.Apply(installation));

        StringAssert.Contains(exception.Message, CubeMxCompatibility.SupportedVersionRange);
        Assert.IsFalse(Directory.Exists(PatcherPaths.LocalizationDirectory(install.RootPath)));
    }

    [TestMethod]
    public void Rollback_RemainsAvailableForUnverifiedCubeMxAndJavaVersions()
    {
        using var install = new TempCubeMxFixture();
        File.AppendAllText(
            install.IniPath,
            PatcherPaths.AgentLine(install.RootPath) + Environment.NewLine,
            new UTF8Encoding(false));
        var service = CreateService(StoppedProcesses());
        var installation = install.Installation("17.0.12") with { Version = "7.0.0" };

        var result = service.Rollback(installation);

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(File.ReadAllLines(install.IniPath).Any(line =>
            PatchStateInspector.IsManagedAgentLine(line, install.RootPath)));
    }

    [TestMethod]
    public void Apply_RejectsMissingExecutable()
    {
        using var install = new TempCubeMxFixture();
        File.Delete(install.Installation().ExecutablePath);
        var service = CreateService(StoppedProcesses());

        TestAssert.Throws<FileNotFoundException>(() => service.Apply(install.Installation()));
    }

    [TestMethod]
    public void Apply_ObservesPreCanceledTokenWithoutWritingFiles()
    {
        using var install = new TempCubeMxFixture();
        var service = CreateService(StoppedProcesses());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        TestAssert.Throws<OperationCanceledException>(() =>
            service.Apply(install.Installation(), cancellationToken: cancellation.Token));

        Assert.IsFalse(Directory.Exists(PatcherPaths.LocalizationDirectory(install.RootPath)));
    }

    private PatchService CreateService(ProcessQueryResult processResult)
    {
        var processStateService = new ProcessStateService(new FakeProcessSource(processResult));
        var inspector = new PatchStateInspector(_payloadProvider);
        return new PatchService(
            _payloadProvider,
            processStateService,
            inspector,
            new FixedClock(FixedNow));
    }

    private static ProcessQueryResult StoppedProcesses() => new(true, []);
}
