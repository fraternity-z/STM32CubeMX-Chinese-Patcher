using STM32CubeMX.ChinesePatcher.Core.Abstractions;
using STM32CubeMX.ChinesePatcher.Core.Models;

namespace STM32CubeMX.ChinesePatcher.Core.Services;

public sealed class InstallationDetector
{
    private const string CubeMxPathVariable = "STM32CubeMX_PATH";
    private readonly IEnvironmentSource _environmentSource;
    private readonly IRegistrySource _registrySource;
    private readonly IVersionSource _versionSource;
    private readonly IReadOnlyList<string> _knownLocations;

    public InstallationDetector(
        IEnvironmentSource environmentSource,
        IRegistrySource registrySource,
        IVersionSource versionSource,
        IEnumerable<string>? knownLocations = null)
    {
        _environmentSource = environmentSource;
        _registrySource = registrySource;
        _versionSource = versionSource;
        _knownLocations = (knownLocations ?? GetDefaultKnownLocations()).ToArray();
    }

    public DetectionResult Detect()
    {
        var warnings = new List<string>();
        var candidates = new List<InstallationCandidate>();

        try
        {
            var environmentPath = _environmentSource.Read(CubeMxPathVariable);
            if (!string.IsNullOrWhiteSpace(environmentPath))
            {
                candidates.Add(new InstallationCandidate(
                    environmentPath,
                    null,
                    DetectionSource.EnvironmentVariable));
            }
        }
        catch (Exception exception)
        {
            warnings.Add($"读取环境变量失败：{exception.Message}");
        }

        try
        {
            candidates.AddRange(_registrySource.ReadInstallations());
        }
        catch (Exception exception)
        {
            warnings.Add($"读取安装注册表失败：{exception.Message}");
        }

        candidates.AddRange(_knownLocations.Select(path =>
            new InstallationCandidate(path, null, DetectionSource.KnownLocation)));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!TryNormalizePath(candidate.RootPath, out var normalizedPath)
                || !seen.Add(normalizedPath))
            {
                continue;
            }

            var result = DetectPath(normalizedPath, candidate.Source, candidate.DisplayVersion);
            if (result.Found)
            {
                return new DetectionResult(result.Installation, [.. warnings, .. result.Warnings]);
            }

            warnings.AddRange(result.Warnings);
        }

        if (warnings.Count == 0)
        {
            warnings.Add("未找到有效的 STM32CubeMX 安装目录。");
        }

        return new DetectionResult(null, warnings);
    }

    public DetectionResult DetectManualPath(string? rootPath) =>
        DetectPath(rootPath, DetectionSource.Manual, null);

    private DetectionResult DetectPath(
        string? rootPath,
        DetectionSource source,
        string? displayVersion)
    {
        if (!TryNormalizePath(rootPath, out var normalizedPath))
        {
            return new DetectionResult(null, ["安装目录为空或格式无效。"]);
        }

        var executablePath = Path.Combine(normalizedPath, PatcherPaths.ExecutableName);
        if (!File.Exists(executablePath))
        {
            return new DetectionResult(null, [$"未找到主程序：{executablePath}"]);
        }

        var iniPath = Path.Combine(normalizedPath, PatcherPaths.IniName);
        if (!File.Exists(iniPath))
        {
            return new DetectionResult(null, [$"未找到启动配置：{iniPath}"]);
        }

        string productVersion;
        string javaVersion;
        try
        {
            productVersion = _versionSource.ReadProductVersion(executablePath);
            javaVersion = _versionSource.ReadJavaVersion(normalizedPath);
        }
        catch (Exception exception)
        {
            return new DetectionResult(null, [$"读取安装版本失败：{exception.Message}"]);
        }

        var version = productVersion == "未知"
            ? FileVersionSource.NormalizeVersion(displayVersion) ?? "未知"
            : productVersion;
        var installation = new CubeMxInstallation(
            normalizedPath,
            version,
            javaVersion,
            source);
        return new DetectionResult(installation, []);
    }

    private static bool TryNormalizePath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalizedPath.Length > 0;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static IEnumerable<string> GetDefaultKnownLocations()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "STMicroelectronics", "STM32Cube", "STM32CubeMX");
        }

        yield return @"C:\STM32Cube\STM32CubeMX";
        yield return @"E:\Programs\STM32CubeMX";
    }
}
