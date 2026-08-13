using System.IO;

namespace STM32CubeMX.ChinesePatcher.Services;

public sealed record UpdateOptions
{
    public string RepositoryOwner { get; init; } = "fraternity-z";

    public string RepositoryName { get; init; } = "STM32CubeMX-Chinese-Patcher";

    public string RuntimeIdentifier { get; init; } = "win-x64";

    public Uri ApiBaseUri { get; init; } = new("https://api.github.com/");

    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public string DownloadDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STM32CubeMX-Chinese-Patcher",
        "updates");

    public string PackageName(string tagName) =>
        $"STM32CubeMX-Chinese-Patcher-{tagName}-{RuntimeIdentifier}.exe";
}
