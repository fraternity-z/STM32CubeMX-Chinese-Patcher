using STM32CubeMX.ChinesePatcher.Core.Models;
using STM32CubeMX.ChinesePatcher.Core.Services;
using STM32CubeMX.ChinesePatcher.Tests.Support;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class InstallationDetectorTests
{
    [TestMethod]
    public void Detect_UsesEnvironmentVariableBeforeRegistry()
    {
        using var environmentInstall = new TempCubeMxFixture();
        using var registryInstall = new TempCubeMxFixture();
        var detector = CreateDetector(
            environmentInstall.RootPath,
            [new InstallationCandidate(registryInstall.RootPath, "6.17.0", DetectionSource.Registry)]);

        var result = detector.Detect();

        Assert.IsNotNull(result.Installation);
        Assert.AreEqual(environmentInstall.RootPath, result.Installation.RootPath);
        Assert.AreEqual(DetectionSource.EnvironmentVariable, result.Installation.Source);
    }

    [TestMethod]
    public void Detect_FallsBackToRegistryWhenEnvironmentPathIsStale()
    {
        using var registryInstall = new TempCubeMxFixture();
        var detector = CreateDetector(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            [new InstallationCandidate(registryInstall.RootPath, "6.18.1", DetectionSource.Registry)]);

        var result = detector.Detect();

        Assert.IsNotNull(result.Installation);
        Assert.AreEqual(registryInstall.RootPath, result.Installation.RootPath);
        Assert.AreEqual(DetectionSource.Registry, result.Installation.Source);
        Assert.IsTrue(result.Warnings.Any(message => message.Contains("未找到主程序", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void DetectManualPath_RejectsFolderWithoutExecutable()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        try
        {
            var detector = CreateDetector(null, []);

            var result = detector.DetectManualPath(rootPath);

            Assert.IsFalse(result.Found);
            StringAssert.Contains(result.Warnings.Single(), "STM32CubeMX.exe");
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [TestMethod]
    public void Detect_UsesNormalizedDisplayVersionWhenBinaryVersionIsUnknown()
    {
        using var install = new TempCubeMxFixture();
        var detector = new InstallationDetector(
            new FakeEnvironmentSource(),
            new FakeRegistrySource
            {
                Handler = () => [new InstallationCandidate(install.RootPath, ">6.16.0-RC4", DetectionSource.Registry)]
            },
            new FakeVersionSource { ProductVersion = "未知" },
            []);

        var result = detector.Detect();

        Assert.AreEqual("6.16.0-RC4", result.Installation?.Version);
    }

    [TestMethod]
    public void Detect_ReportsSourceFailuresAndStillUsesKnownLocation()
    {
        using var install = new TempCubeMxFixture();
        var detector = new InstallationDetector(
            new FakeEnvironmentSource { Handler = _ => throw new InvalidOperationException("env failed") },
            new FakeRegistrySource { Handler = () => throw new InvalidOperationException("registry failed") },
            new FakeVersionSource(),
            [install.RootPath]);

        var result = detector.Detect();

        Assert.IsTrue(result.Found);
        Assert.AreEqual(DetectionSource.KnownLocation, result.Installation?.Source);
        Assert.HasCount(2, result.Warnings);
    }

    [TestMethod]
    public void DetectManualPath_RejectsEmptyPath()
    {
        var result = CreateDetector(null, []).DetectManualPath("  ");

        Assert.IsFalse(result.Found);
        StringAssert.Contains(result.Warnings.Single(), "为空");
    }

    private static InstallationDetector CreateDetector(
        string? environmentPath,
        IReadOnlyList<InstallationCandidate> registryCandidates) =>
        new(
            new FakeEnvironmentSource { Handler = _ => environmentPath },
            new FakeRegistrySource { Handler = () => registryCandidates },
            new FakeVersionSource(),
            []);
}
