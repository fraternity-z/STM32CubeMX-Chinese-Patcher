using System.Security.Cryptography;

namespace STM32CubeMX.ChinesePatcher.Core.Models;

public enum DetectionSource
{
    None,
    EnvironmentVariable,
    Registry,
    KnownLocation,
    Manual
}

public enum RunningState
{
    Stopped,
    Running,
    Unknown
}

public enum LocalizationState
{
    NotInstalled,
    Installed,
    NeedsUpdate,
    Damaged,
    Conflict
}

public enum PatchOperation
{
    Apply,
    Rollback
}

public sealed record InstallationCandidate(
    string RootPath,
    string? DisplayVersion,
    DetectionSource Source);

public sealed record CubeMxInstallation(
    string RootPath,
    string Version,
    string JavaVersion,
    DetectionSource Source)
{
    public string ExecutablePath => Path.Combine(RootPath, PatcherPaths.ExecutableName);

    public string IniPath => Path.Combine(RootPath, PatcherPaths.IniName);
}

public sealed record DetectionResult(
    CubeMxInstallation? Installation,
    IReadOnlyList<string> Warnings)
{
    public bool Found => Installation is not null;
}

public sealed record ProcessSnapshot(
    string Name,
    string? ExecutablePath,
    string? CommandLine = null);

public sealed record ProcessQueryResult(
    bool Succeeded,
    IReadOnlyList<ProcessSnapshot> Processes,
    string? ErrorMessage = null);

public sealed record PatchInspection(
    LocalizationState State,
    string Message,
    bool HasManagedIniLine,
    bool PayloadMatches);

public sealed record PayloadBundle
{
    public PayloadBundle(byte[] agentJar, byte[] dictionary)
    {
        ArgumentNullException.ThrowIfNull(agentJar);
        ArgumentNullException.ThrowIfNull(dictionary);

        if (agentJar.Length == 0)
        {
            throw new ArgumentException("汉化 Agent 载荷不能为空。", nameof(agentJar));
        }

        if (dictionary.Length == 0)
        {
            throw new ArgumentException("翻译词典载荷不能为空。", nameof(dictionary));
        }

        AgentJar = agentJar;
        Dictionary = dictionary;
        AgentJarSha256 = Convert.ToHexString(SHA256.HashData(agentJar));
        DictionarySha256 = Convert.ToHexString(SHA256.HashData(dictionary));
    }

    public byte[] AgentJar { get; }

    public byte[] Dictionary { get; }

    public string AgentJarSha256 { get; }

    public string DictionarySha256 { get; }
}

public sealed record OperationProgress(int Percentage, string Message);

public sealed record OperationResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Details)
{
    public static OperationResult Success(string message, params string[] details) =>
        new(true, message, details);

    public static OperationResult Failure(string message, params string[] details) =>
        new(false, message, details);
}

public static class PatcherPaths
{
    public const string ExecutableName = "STM32CubeMX.exe";
    public const string IniName = "STM32CubeMX.l4j.ini";
    public const string AgentFileName = "stm32cubemx-zh-agent.jar";
    public const string DictionaryFileName = "translations.tsv";
    public const string StateFileName = "install-state.json";
    public const string BackupFileName = "STM32CubeMX.l4j.ini.before-zh-agent";

    public static string LocalizationDirectory(string rootPath) =>
        Path.Combine(rootPath, "localization", "zh-CN");

    public static string AgentPath(string rootPath) =>
        Path.Combine(LocalizationDirectory(rootPath), AgentFileName);

    public static string DictionaryPath(string rootPath) =>
        Path.Combine(LocalizationDirectory(rootPath), DictionaryFileName);

    public static string StatePath(string rootPath) =>
        Path.Combine(LocalizationDirectory(rootPath), StateFileName);

    public static string BackupPath(string rootPath) =>
        Path.Combine(LocalizationDirectory(rootPath), BackupFileName);

    public static string AgentLine(string rootPath) =>
        $"-javaagent:{AgentPath(rootPath)}={DictionaryPath(rootPath)}";
}
