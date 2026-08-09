using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace STM32CubeMX.ChinesePatcher.Services;

[ExcludeFromCodeCoverage]
public static class AppLog
{
    private static readonly object SyncRoot = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STM32CubeMX-Chinese-Patcher",
        "logs",
        "app.log");

    public static void Write(string level, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(directory);
            var cleanMessage = message.ReplaceLineEndings(" ");
            var line = $"{DateTimeOffset.Now:O}\t{level}\t{cleanMessage}{Environment.NewLine}";
            lock (SyncRoot)
            {
                File.AppendAllText(LogPath, line, new UTF8Encoding(false));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
