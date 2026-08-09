using STM32CubeMX.ChinesePatcher.Core.Abstractions;
using STM32CubeMX.ChinesePatcher.Core.Models;

namespace STM32CubeMX.ChinesePatcher.Core.Services;

public sealed class ProcessStateService(IProcessSource processSource)
{
    private readonly IProcessSource _processSource = processSource;

    public RunningState GetState(CubeMxInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var query = _processSource.ReadProcesses();
        if (!query.Succeeded)
        {
            return RunningState.Unknown;
        }

        var executablePath = NormalizePath(installation.ExecutablePath);
        var javaPath = NormalizePath(Path.Combine(installation.RootPath, "jre", "bin", "java.exe"));
        var javawPath = NormalizePath(Path.Combine(installation.RootPath, "jre", "bin", "javaw.exe"));
        var hasUnreadableRelevantProcess = false;

        foreach (var process in query.Processes)
        {
            var processPath = NormalizePath(process.ExecutablePath);
            if (processPath is null)
            {
                hasUnreadableRelevantProcess |= IsPotentialCubeMxProcess(process.Name);
                continue;
            }

            if (string.Equals(processPath, executablePath, StringComparison.OrdinalIgnoreCase))
            {
                return RunningState.Running;
            }

            var isBundledJava = string.Equals(processPath, javaPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(processPath, javawPath, StringComparison.OrdinalIgnoreCase);
            if (!isBundledJava)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(process.CommandLine)
                || (process.CommandLine.Contains(installation.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                    && process.CommandLine.Contains("com.st.microxplorer.maingui.STM32CubeMX", StringComparison.Ordinal)))
            {
                return RunningState.Running;
            }
        }

        return hasUnreadableRelevantProcess ? RunningState.Unknown : RunningState.Stopped;
    }

    private static bool IsPotentialCubeMxProcess(string processName)
    {
        var normalizedName = Path.GetFileNameWithoutExtension(processName);
        return string.Equals(normalizedName, "STM32CubeMX", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedName, "java", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedName, "javaw", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
