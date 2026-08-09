using System.Security.Cryptography;
using System.Text.Json;
using STM32CubeMX.ChinesePatcher.Core.Abstractions;
using STM32CubeMX.ChinesePatcher.Core.Models;

namespace STM32CubeMX.ChinesePatcher.Core.Services;

public sealed class PatchStateInspector(IPayloadProvider payloadProvider)
{
    private readonly IPayloadProvider _payloadProvider = payloadProvider;

    public PatchInspection Inspect(CubeMxInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        try
        {
            var iniLines = File.ReadAllLines(installation.IniPath);
            var managedLineCount = iniLines.Count(line => IsManagedAgentLine(line, installation.RootPath));
            var hasManagedLine = managedLineCount > 0;
            var hasUnsupportedManagedPathLine = iniLines.Any(line =>
                TargetsManagedAgentPath(line, installation.RootPath)
                && !IsManagedAgentLine(line, installation.RootPath));
            var agentPath = PatcherPaths.AgentPath(installation.RootPath);
            var dictionaryPath = PatcherPaths.DictionaryPath(installation.RootPath);
            var statePath = PatcherPaths.StatePath(installation.RootPath);
            var hasAnyPayload = File.Exists(agentPath) || File.Exists(dictionaryPath);
            var hasStateFile = File.Exists(statePath);
            var owned = hasManagedLine || IsOwnedState(
                statePath,
                installation.RootPath,
                agentPath,
                dictionaryPath);

            if (hasUnsupportedManagedPathLine)
            {
                return new PatchInspection(
                    LocalizationState.Conflict,
                    "检测到指向汉化目录但格式不受支持的启动项。",
                    false,
                    false);
            }

            if (managedLineCount > 1)
            {
                return new PatchInspection(
                    LocalizationState.Damaged,
                    "检测到重复的汉化启动项。",
                    true,
                    false);
            }

            if (!hasManagedLine && !hasAnyPayload)
            {
                if (hasStateFile && !owned)
                {
                    return new PatchInspection(
                        LocalizationState.Conflict,
                        "目标目录存在来源不明的汉化状态文件。",
                        false,
                        false);
                }

                return new PatchInspection(
                    LocalizationState.NotInstalled,
                    "尚未安装汉化。",
                    false,
                    false);
            }

            if (!File.Exists(agentPath) || !File.Exists(dictionaryPath))
            {
                return owned
                    ? new PatchInspection(
                        LocalizationState.Damaged,
                        "本工具管理的汉化文件不完整。",
                        hasManagedLine,
                        false)
                    : new PatchInspection(
                        LocalizationState.Conflict,
                        "目标目录存在来源不明且不完整的同名汉化文件。",
                        false,
                        false);
            }

            var payload = _payloadProvider.GetPayload();
            var agentMatches = HashFile(agentPath) == payload.AgentJarSha256;
            var dictionaryMatches = HashFile(dictionaryPath) == payload.DictionarySha256;
            var payloadMatches = agentMatches && dictionaryMatches;

            if (hasManagedLine && payloadMatches)
            {
                return new PatchInspection(
                    LocalizationState.Installed,
                    "当前版本已汉化。",
                    true,
                    true);
            }

            if (!hasManagedLine && payloadMatches)
            {
                return new PatchInspection(
                    LocalizationState.NotInstalled,
                    "汉化已回退，载荷保留。",
                    false,
                    true);
            }

            return owned
                ? new PatchInspection(
                    LocalizationState.NeedsUpdate,
                    "汉化载荷与当前工具版本不一致。",
                    hasManagedLine,
                    false)
                : new PatchInspection(
                    LocalizationState.Conflict,
                    "目标目录存在来源不明的同名汉化文件。",
                    false,
                    false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new PatchInspection(
                LocalizationState.Damaged,
                $"读取汉化状态失败：{exception.Message}",
                false,
                false);
        }
    }

    public static bool IsManagedAgentLine(string line, string rootPath)
    {
        return !string.IsNullOrWhiteSpace(line)
            && string.Equals(
                line.Trim(),
                PatcherPaths.AgentLine(rootPath),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool TargetsManagedAgentPath(string line, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(line)
            || !line.StartsWith("-javaagent:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = line["-javaagent:".Length..].TrimStart();
        var expectedAgentPath = PatcherPaths.AgentPath(rootPath);
        var prefix = value.StartsWith('"') ? $"\"{expectedAgentPath}\"" : expectedAgentPath;
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (value.Length == prefix.Length || value[prefix.Length] == '=');
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static bool IsOwnedState(
        string statePath,
        string rootPath,
        string agentPath,
        string dictionaryPath)
    {
        if (!File.Exists(statePath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(statePath));
            if (!document.RootElement.TryGetProperty("formatVersion", out var formatVersionElement)
                || !formatVersionElement.TryGetInt32(out var formatVersion)
                || formatVersion != 1
                || !document.RootElement.TryGetProperty("installedBy", out var installedByElement)
                || !string.Equals(
                    installedByElement.GetString(),
                    "STM32CubeMX Chinese Patcher",
                    StringComparison.Ordinal)
                || !document.RootElement.TryGetProperty("cubeMxRoot", out var rootElement)
                || !document.RootElement.TryGetProperty("agentLine", out var agentLineElement)
                || !string.Equals(
                    agentLineElement.GetString(),
                    PatcherPaths.AgentLine(rootPath),
                    StringComparison.OrdinalIgnoreCase)
                || !document.RootElement.TryGetProperty("agentJarSha256", out var agentHashElement)
                || !document.RootElement.TryGetProperty("dictionarySha256", out var dictionaryHashElement))
            {
                return false;
            }

            var stateRoot = rootElement.GetString();
            var agentHash = agentHashElement.GetString();
            var dictionaryHash = dictionaryHashElement.GetString();
            if (string.IsNullOrWhiteSpace(stateRoot)
                || !IsSha256(agentHash)
                || !IsSha256(dictionaryHash)
                || !string.Equals(
                    Path.GetFullPath(stateRoot),
                    Path.GetFullPath(rootPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return (!File.Exists(agentPath)
                    || string.Equals(HashFile(agentPath), agentHash, StringComparison.OrdinalIgnoreCase))
                && (!File.Exists(dictionaryPath)
                    || string.Equals(HashFile(dictionaryPath), dictionaryHash, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
