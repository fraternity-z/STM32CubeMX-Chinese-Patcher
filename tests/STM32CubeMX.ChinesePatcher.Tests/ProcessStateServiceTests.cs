using STM32CubeMX.ChinesePatcher.Core.Models;
using STM32CubeMX.ChinesePatcher.Core.Services;
using STM32CubeMX.ChinesePatcher.Tests.Support;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class ProcessStateServiceTests
{
    [TestMethod]
    public void GetState_DetectsLauncherExecutable()
    {
        using var install = new TempCubeMxFixture();
        var service = CreateService(new ProcessSnapshot("STM32CubeMX", install.Installation().ExecutablePath));

        Assert.AreEqual(RunningState.Running, service.GetState(install.Installation()));
    }

    [TestMethod]
    public void GetState_DetectsBundledJavaWithCubeMxCommandLine()
    {
        using var install = new TempCubeMxFixture();
        var javaPath = Path.Combine(install.RootPath, "jre", "bin", "javaw.exe");
        var commandLine = $"-classpath \"{install.Installation().ExecutablePath};anything\" com.st.microxplorer.maingui.STM32CubeMX";
        var service = CreateService(new ProcessSnapshot("javaw", javaPath, commandLine));

        Assert.AreEqual(RunningState.Running, service.GetState(install.Installation()));
    }

    [TestMethod]
    public void GetState_IgnoresBundledJavaWithUnrelatedKnownCommandLine()
    {
        using var install = new TempCubeMxFixture();
        var javaPath = Path.Combine(install.RootPath, "jre", "bin", "javaw.exe");
        var service = CreateService(new ProcessSnapshot("javaw", javaPath, "com.example.OtherTool"));

        Assert.AreEqual(RunningState.Stopped, service.GetState(install.Installation()));
    }

    [TestMethod]
    public void GetState_TreatsBundledJavaWithoutCommandLineAsRunning()
    {
        using var install = new TempCubeMxFixture();
        var javaPath = Path.Combine(install.RootPath, "jre", "bin", "javaw.exe");
        var service = CreateService(new ProcessSnapshot("javaw", javaPath));

        Assert.AreEqual(RunningState.Running, service.GetState(install.Installation()));
    }

    [TestMethod]
    public void GetState_ReturnsUnknownWhenProcessQueryFails()
    {
        using var install = new TempCubeMxFixture();
        var service = new ProcessStateService(new FakeProcessSource(
            new ProcessQueryResult(false, [], "access denied")));

        Assert.AreEqual(RunningState.Unknown, service.GetState(install.Installation()));
    }

    [TestMethod]
    [DataRow("STM32CubeMX")]
    [DataRow("java")]
    [DataRow("javaw")]
    public void GetState_ReturnsUnknownForUnreadableRelevantProcess(string processName)
    {
        using var install = new TempCubeMxFixture();
        var service = new ProcessStateService(new FakeProcessSource(
            new ProcessQueryResult(true,
            [
                new ProcessSnapshot(processName, null),
                new ProcessSnapshot("javaw", @"C:\Other\jre\bin\javaw.exe")
            ])));

        Assert.AreEqual(RunningState.Unknown, service.GetState(install.Installation()));
    }

    [TestMethod]
    public void GetState_IgnoresUnreadableUnrelatedProcess()
    {
        using var install = new TempCubeMxFixture();
        var service = CreateService(new ProcessSnapshot("OtherTool", null));

        Assert.AreEqual(RunningState.Stopped, service.GetState(install.Installation()));
    }

    private static ProcessStateService CreateService(ProcessSnapshot snapshot) =>
        new(new FakeProcessSource(new ProcessQueryResult(true, [snapshot])));
}
