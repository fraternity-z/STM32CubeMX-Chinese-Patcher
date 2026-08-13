namespace STM32CubeMX.ChinesePatcher.Models;

public sealed record UpdatePackage(
    string FileName,
    long Size,
    Uri DownloadUri,
    string Sha256);

public sealed record UpdateRelease(
    Version Version,
    string DisplayVersion,
    string ReleaseNotes,
    Uri ReleasePageUri,
    UpdatePackage Package);

public sealed record UpdateDownloadProgress(long BytesReceived, long TotalBytes)
{
    public int Percentage => TotalBytes <= 0
        ? 0
        : (int)Math.Clamp(BytesReceived * 100L / TotalBytes, 0, 100);
}

public sealed class UpdateException(string message, Exception? innerException = null)
    : Exception(message, innerException);
