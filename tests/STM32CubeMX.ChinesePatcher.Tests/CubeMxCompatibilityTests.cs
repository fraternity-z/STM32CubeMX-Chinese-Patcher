using STM32CubeMX.ChinesePatcher.Core.Services;
using STM32CubeMX.ChinesePatcher.Tests.Support;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class CubeMxCompatibilityTests
{
    [TestMethod]
    [DataRow("6.16.0-RC4")]
    [DataRow(">6.16.0-RC4")]
    [DataRow("6.17.0")]
    [DataRow("6.18.0")]
    [DataRow("6.18.1-RC2")]
    public void SupportsCubeMxVersion_AcceptsValidatedReleaseLines(string version)
    {
        Assert.IsTrue(CubeMxCompatibility.SupportsCubeMxVersion(version));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("未知")]
    [DataRow("6.15.1")]
    [DataRow("6.16.0")]
    [DataRow("6.16.999")]
    [DataRow("6.18.1")]
    [DataRow("6.18.1-RC3")]
    [DataRow("6.18.2")]
    [DataRow("6.18.99")]
    [DataRow("6.19.0")]
    [DataRow("7.0.0")]
    [DataRow("6.x")]
    public void SupportsCubeMxVersion_RejectsUnverifiedVersions(string? version)
    {
        Assert.IsFalse(CubeMxCompatibility.SupportsCubeMxVersion(version));
    }

    [TestMethod]
    [DataRow("21")]
    [DataRow("21.0.10")]
    [DataRow("21-ea")]
    [DataRow("22+35")]
    public void SupportsJavaVersion_AcceptsJava21OrLater(string version)
    {
        Assert.IsTrue(CubeMxCompatibility.SupportsJavaVersion(version));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("未知")]
    [DataRow("17.0.12")]
    [DataRow("1.8.0_451")]
    public void SupportsJavaVersion_RejectsOlderOrUnknownVersions(string? version)
    {
        Assert.IsFalse(CubeMxCompatibility.SupportsJavaVersion(version));
    }

    [TestMethod]
    public void GetApplyBlockReason_ExplainsUnsupportedVersionWithoutSensitivePaths()
    {
        using var install = new TempCubeMxFixture();
        var installation = install.Installation() with { Version = "7.0.0-RC1" };

        var reason = CubeMxCompatibility.GetApplyBlockReason(installation);

        Assert.IsNotNull(reason);
        StringAssert.Contains(reason, CubeMxCompatibility.SupportedVersionRange);
        StringAssert.Contains(reason, "仍可回退");
        Assert.IsFalse(reason.Contains(install.RootPath, StringComparison.OrdinalIgnoreCase));
    }
}
