using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security;
using Microsoft.Win32;
using STM32CubeMX.ChinesePatcher.Core.Abstractions;
using STM32CubeMX.ChinesePatcher.Core.Models;

namespace STM32CubeMX.ChinesePatcher.Core.Services;

[ExcludeFromCodeCoverage]
public sealed class SystemEnvironmentSource : IEnvironmentSource
{
    public string? Read(string variableName) =>
        Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User)
        ?? Environment.GetEnvironmentVariable(variableName);
}

[ExcludeFromCodeCoverage]
public sealed class WindowsRegistrySource : IRegistrySource
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public IReadOnlyList<InstallationCandidate> ReadInstallations()
    {
        var candidates = new List<InstallationCandidate>();
        ReadHive(RegistryHive.CurrentUser, RegistryView.Default, candidates);
        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, candidates);
        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, candidates);
        return candidates;
    }

    private static void ReadHive(
        RegistryHive hive,
        RegistryView view,
        ICollection<InstallationCandidate> candidates)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallPath);
            if (uninstall is null)
            {
                return;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var item = uninstall.OpenSubKey(subKeyName);
                if (item is null
                    || !string.Equals(item.GetValue("DisplayName") as string, "STM32CubeMX", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var displayIcon = item.GetValue("DisplayIcon") as string;
                var uninstallString = item.GetValue("UninstallString") as string;
                var rootPath = TryGetRootPath(displayIcon, uninstallString);
                if (!string.IsNullOrWhiteSpace(rootPath))
                {
                    candidates.Add(new InstallationCandidate(
                        rootPath,
                        item.GetValue("DisplayVersion") as string,
                        DetectionSource.Registry));
                }
            }
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            // A denied registry view is non-fatal because other discovery sources remain available.
        }
    }

    private static string? TryGetRootPath(string? displayIcon, string? uninstallString)
    {
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            var expanded = Environment.ExpandEnvironmentVariables(displayIcon.Trim().Trim('"'));
            var executableIndex = expanded.IndexOf(PatcherPaths.ExecutableName, StringComparison.OrdinalIgnoreCase);
            if (executableIndex >= 0)
            {
                var executablePath = expanded[..(executableIndex + PatcherPaths.ExecutableName.Length)];
                return Path.GetDirectoryName(executablePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(uninstallString))
        {
            var expanded = Environment.ExpandEnvironmentVariables(uninstallString.Trim().Trim('"'));
            var markerIndex = expanded.IndexOf($"{Path.DirectorySeparatorChar}Uninstaller{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                return expanded[..markerIndex];
            }
        }

        return null;
    }
}

[ExcludeFromCodeCoverage]
public sealed class FileVersionSource : IVersionSource
{
    public string ReadProductVersion(string executablePath)
    {
        var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
        return NormalizeVersion(versionInfo.ProductVersion)
            ?? NormalizeVersion(versionInfo.FileVersion)
            ?? "未知";
    }

    public string ReadJavaVersion(string rootPath)
    {
        var releasePath = Path.Combine(rootPath, "jre", "release");
        if (!File.Exists(releasePath))
        {
            return "未知";
        }

        var versionLine = File.ReadLines(releasePath)
            .FirstOrDefault(line => line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal));
        return versionLine is null
            ? "未知"
            : versionLine["JAVA_VERSION=".Length..].Trim().Trim('"');
    }

    public static string? NormalizeVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ? null : version.Trim().TrimStart('>');
}

[ExcludeFromCodeCoverage]
public sealed class SystemProcessSource : IProcessSource
{
    public ProcessQueryResult ReadProcesses()
    {
        try
        {
            var snapshots = new List<ProcessSnapshot>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        snapshots.Add(new ProcessSnapshot(
                            process.ProcessName,
                            process.MainModule?.FileName));
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        snapshots.Add(new ProcessSnapshot(process.ProcessName, null));
                    }
                }
            }

            return new ProcessQueryResult(true, snapshots);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ProcessQueryResult(false, [], exception.Message);
        }
    }
}

[ExcludeFromCodeCoverage]
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
