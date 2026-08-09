using STM32CubeMX.ChinesePatcher.Core.Models;

namespace STM32CubeMX.ChinesePatcher.Core.Services;

public static class CubeMxCompatibility
{
    public const string LatestValidatedVersion = "6.18.1-RC2";
    public const string SupportedVersionRange = "6.16.0-RC4、6.17.0、6.18.0、6.18.1-RC2";

    private static readonly HashSet<string> ValidatedCubeMxVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "6.16.0-RC4",
        "6.17.0",
        "6.18.0",
        LatestValidatedVersion,
    };

    public static bool SupportsCubeMxVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var normalizedVersion = version.Trim().TrimStart('>').Trim();
        return ValidatedCubeMxVersions.Contains(normalizedVersion);
    }

    public static bool SupportsJavaVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var value = version.AsSpan().TrimStart();
        var digitCount = 0;
        while (digitCount < value.Length && char.IsAsciiDigit(value[digitCount]))
        {
            digitCount++;
        }

        return digitCount > 0
            && int.TryParse(value[..digitCount], out var major)
            && major >= 21;
    }

    public static string? GetApplyBlockReason(CubeMxInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        if (!SupportsCubeMxVersion(installation.Version))
        {
            return $"当前汉化载荷仅验证支持 STM32CubeMX {SupportedVersionRange}"
                + $"（最新验证：{LatestValidatedVersion}），检测到：{installation.Version}。"
                + "为避免界面变化导致启动或翻译异常，本次汉化已取消；已有汉化仍可回退。";
        }

        if (!SupportsJavaVersion(installation.JavaVersion))
        {
            return $"当前汉化载荷要求 JRE 21 或更高版本，检测到：{installation.JavaVersion}。";
        }

        return null;
    }
}
