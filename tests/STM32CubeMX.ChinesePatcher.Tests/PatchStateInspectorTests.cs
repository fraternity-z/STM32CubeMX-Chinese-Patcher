using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using STM32CubeMX.ChinesePatcher.Core.Models;
using STM32CubeMX.ChinesePatcher.Core.Services;
using STM32CubeMX.ChinesePatcher.Tests.Support;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class PatchStateInspectorTests
{
    private readonly FakePayloadProvider _payloadProvider = new();

    [TestMethod]
    public void Inspect_ReturnsNotInstalledWhenNoManagedFilesExist()
    {
        using var install = new TempCubeMxFixture();

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.NotInstalled, result.State);
        Assert.IsFalse(result.HasManagedIniLine);
        Assert.IsFalse(result.PayloadMatches);
    }

    [TestMethod]
    public void Inspect_ReturnsInstalledForMatchingPayloadAndIniLine()
    {
        using var install = new TempCubeMxFixture();
        install.WriteExpectedPayload(_payloadProvider);
        File.AppendAllText(install.IniPath, PatcherPaths.AgentLine(install.RootPath) + Environment.NewLine);

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.Installed, result.State);
        Assert.IsTrue(result.HasManagedIniLine);
        Assert.IsTrue(result.PayloadMatches);
    }

    [TestMethod]
    public void Inspect_ReturnsNotInstalledForRetainedMatchingPayload()
    {
        using var install = new TempCubeMxFixture();
        install.WriteExpectedPayload(_payloadProvider);

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.NotInstalled, result.State);
        Assert.IsTrue(result.PayloadMatches);
        StringAssert.Contains(result.Message, "回退");
    }

    [TestMethod]
    public void Inspect_ReturnsDamagedForDuplicateManagedLines()
    {
        using var install = new TempCubeMxFixture();
        install.WriteExpectedPayload(_payloadProvider);
        var line = PatcherPaths.AgentLine(install.RootPath);
        File.AppendAllText(install.IniPath, $"{line}{Environment.NewLine}{line}{Environment.NewLine}");

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.Damaged, result.State);
        StringAssert.Contains(result.Message, "重复");
    }

    [TestMethod]
    public void Inspect_ReturnsDamagedWhenManagedPayloadIsIncomplete()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), _payloadProvider.GetPayload().AgentJar);
        File.AppendAllText(install.IniPath, PatcherPaths.AgentLine(install.RootPath) + Environment.NewLine);

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.Damaged, result.State);
        StringAssert.Contains(result.Message, "不完整");
    }

    [TestMethod]
    public void Inspect_ReturnsConflictForUnownedMismatchedPayload()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), [1, 2, 3]);
        File.WriteAllText(PatcherPaths.DictionaryPath(install.RootPath), "foreign", Encoding.UTF8);

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.Conflict, result.State);
    }

    [TestMethod]
    public void Inspect_ReturnsConflictForUnownedIncompletePayload()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), [1, 2, 3]);

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.Conflict, result.State);
        StringAssert.Contains(result.Message, "来源不明");
    }

    [TestMethod]
    public void Inspect_ReturnsNeedsUpdateForLegacyOwnedState()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        var legacyAgent = new byte[] { 1, 2, 3 };
        var legacyDictionary = Encoding.UTF8.GetBytes("old");
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), legacyAgent);
        File.WriteAllBytes(PatcherPaths.DictionaryPath(install.RootPath), legacyDictionary);
        File.WriteAllText(
            PatcherPaths.StatePath(install.RootPath),
            JsonSerializer.Serialize(new
            {
                formatVersion = 1,
                installedBy = "STM32CubeMX Chinese Patcher",
                cubeMxRoot = install.RootPath,
                agentLine = PatcherPaths.AgentLine(install.RootPath),
                agentJarSha256 = Convert.ToHexString(SHA256.HashData(legacyAgent)),
                dictionarySha256 = Convert.ToHexString(SHA256.HashData(legacyDictionary))
            }),
            Encoding.UTF8);

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.NeedsUpdate, result.State);
    }

    [TestMethod]
    public void Inspect_ReturnsConflictWhenStateHashesDoNotOwnPayload()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), [1, 2, 3]);
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

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.Conflict, result.State);
    }

    [TestMethod]
    public void Inspect_ReturnsConflictForInvalidStateJson()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), [1, 2, 3]);
        File.WriteAllText(PatcherPaths.DictionaryPath(install.RootPath), "old", Encoding.UTF8);
        File.WriteAllText(PatcherPaths.StatePath(install.RootPath), "{broken", Encoding.UTF8);

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.Conflict, result.State);
        StringAssert.Contains(result.Message, "来源不明");
    }

    [TestMethod]
    public void Inspect_ReturnsConflictForInvalidStateWithoutPayload()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        File.WriteAllText(PatcherPaths.StatePath(install.RootPath), "{broken", Encoding.UTF8);

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.Conflict, result.State);
        StringAssert.Contains(result.Message, "状态文件");
    }

    [TestMethod]
    public void IsManagedAgentLine_OnlyMatchesCanonicalInstallationPath()
    {
        using var install = new TempCubeMxFixture();
        var foreignRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.IsTrue(PatchStateInspector.IsManagedAgentLine(
            PatcherPaths.AgentLine(install.RootPath),
            install.RootPath));
        Assert.IsFalse(PatchStateInspector.IsManagedAgentLine(
            PatcherPaths.AgentLine(foreignRoot),
            install.RootPath));
        Assert.IsFalse(PatchStateInspector.IsManagedAgentLine(
            $"-javaagent:{PatcherPaths.AgentPath(install.RootPath)}",
            install.RootPath));
        Assert.IsFalse(PatchStateInspector.IsManagedAgentLine(
            $"-javaagent:{PatcherPaths.AgentPath(install.RootPath)}=C:\\Other\\translations.tsv",
            install.RootPath));
        Assert.IsFalse(PatchStateInspector.IsManagedAgentLine(" ", install.RootPath));
        Assert.IsFalse(PatchStateInspector.IsManagedAgentLine("-Xmx1024m", install.RootPath));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Inspect_ReturnsConflictForUnsupportedLineTargetingManagedAgentPath(bool useQuotedPath)
    {
        using var install = new TempCubeMxFixture();
        install.WriteExpectedPayload(_payloadProvider);
        var agentPath = PatcherPaths.AgentPath(install.RootPath);
        var unsupportedLine = useQuotedPath
            ? $"-javaagent:\"{agentPath}\"=C:\\Other\\translations.tsv"
            : $"-javaagent:{agentPath}";
        File.AppendAllText(
            install.IniPath,
            unsupportedLine + Environment.NewLine);

        var result = CreateInspector().Inspect(install.Installation());

        Assert.AreEqual(LocalizationState.Conflict, result.State);
        StringAssert.Contains(result.Message, "格式不受支持");
    }

    [TestMethod]
    public void Inspect_ReturnsConflictWhenStateIdentityFieldsAreInvalid()
    {
        using var install = new TempCubeMxFixture();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(install.RootPath));
        File.WriteAllBytes(PatcherPaths.AgentPath(install.RootPath), [1, 2, 3]);
        File.WriteAllText(PatcherPaths.DictionaryPath(install.RootPath), "foreign", Encoding.UTF8);
        var validHash = new string('0', 64);
        var invalidStates = new Dictionary<string, object?>[]
        {
            CreateState(formatVersion: 2),
            CreateState(installedBy: "Other Tool"),
            CreateState(cubeMxRoot: Path.Combine(install.RootPath, "other")),
            CreateState(agentLine: "-javaagent:C:\\Other\\agent.jar"),
            CreateState(agentJarSha256: "invalid"),
            CreateState(dictionarySha256: "invalid")
        };

        foreach (var state in invalidStates)
        {
            File.WriteAllText(
                PatcherPaths.StatePath(install.RootPath),
                JsonSerializer.Serialize(state),
                Encoding.UTF8);

            Assert.AreEqual(LocalizationState.Conflict, CreateInspector().Inspect(install.Installation()).State);
        }

        Dictionary<string, object?> CreateState(
            int formatVersion = 1,
            string installedBy = "STM32CubeMX Chinese Patcher",
            string? cubeMxRoot = null,
            string? agentLine = null,
            string? agentJarSha256 = null,
            string? dictionarySha256 = null) => new()
            {
                ["formatVersion"] = formatVersion,
                ["installedBy"] = installedBy,
                ["cubeMxRoot"] = cubeMxRoot ?? install.RootPath,
                ["agentLine"] = agentLine ?? PatcherPaths.AgentLine(install.RootPath),
                ["agentJarSha256"] = agentJarSha256 ?? validHash,
                ["dictionarySha256"] = dictionarySha256 ?? validHash
            };
    }

    private PatchStateInspector CreateInspector() => new(_payloadProvider);
}
