using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using STM32CubeMX.ChinesePatcher.Models;

namespace STM32CubeMX.ChinesePatcher.Services;

public sealed class GitHubUpdateService : IUpdateService, IDisposable
{
    private const int MaximumChecksumBytes = 64 * 1024;
    private readonly HttpClient _httpClient;
    private readonly UpdateOptions _options;
    private readonly bool _ownsHttpClient;
    private readonly object _checkSync = new();
    private readonly object _downloadSync = new();
    private Task<UpdateRelease?>? _checkTask;
    private Task<string>? _downloadTask;

    public GitHubUpdateService(UpdateOptions options)
        : this(CreateHttpClient(), options, ownsHttpClient: true)
    {
    }

    public GitHubUpdateService(
        HttpClient httpClient,
        UpdateOptions options,
        bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        _httpClient = httpClient;
        _options = options;
        _ownsHttpClient = ownsHttpClient;
        ConfigureRequestHeaders(_httpClient);
    }

    public Task<UpdateRelease?> CheckForUpdateAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        lock (_checkSync)
        {
            if (_checkTask is { IsCompleted: false })
            {
                return _checkTask;
            }

            _checkTask = CheckForUpdateCoreAsync(currentVersion, cancellationToken);
            return _checkTask;
        }
    }

    public Task<string> DownloadUpdateAsync(
        UpdateRelease release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        lock (_downloadSync)
        {
            if (_downloadTask is { IsCompleted: false })
            {
                return _downloadTask;
            }

            _downloadTask = DownloadUpdateCoreAsync(release, progress, cancellationToken);
            return _downloadTask;
        }
    }

    [ExcludeFromCodeCoverage]
    public void LaunchUpdate(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            throw new UpdateException("下载完成，但更新程序不存在。");
        }

        var startInfo = new ProcessStartInfo(packagePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(packagePath) ?? Environment.CurrentDirectory
        };
        try
        {
            if (Process.Start(startInfo) is null)
            {
                throw new UpdateException("系统未能启动更新程序。");
            }
        }
        catch (UpdateException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new UpdateException("系统未能启动更新程序。", exception);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<UpdateRelease?> CheckForUpdateCoreAsync(
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.CheckTimeout);

        try
        {
            var endpoint = new Uri(
                _options.ApiBaseUri,
                $"repos/{_options.RepositoryOwner}/{_options.RepositoryName}/releases/latest");
            using var response = await _httpClient.GetAsync(endpoint, timeoutSource.Token);
            response.EnsureSuccessStatusCode();
            await using var responseStream = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
            var payload = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                responseStream,
                cancellationToken: timeoutSource.Token)
                ?? throw new UpdateException("更新服务器返回了空响应。");

            var latestVersion = ParseVersion(payload.TagName);
            if (latestVersion <= NormalizeVersion(currentVersion))
            {
                return null;
            }

            var releasePageUri = ParseTrustedReleaseUri(payload.HtmlUrl);
            var packageName = _options.PackageName(payload.TagName!);
            var packageAsset = FindAsset(payload.Assets, packageName);
            var checksumAsset = FindAsset(payload.Assets, "SHA256SUMS.txt");
            if (packageAsset.Size <= 0)
            {
                throw new UpdateException("更新包大小无效。");
            }

            var packageUri = ParseTrustedAssetUri(packageAsset.DownloadUrl);
            var checksumUri = ParseTrustedAssetUri(checksumAsset.DownloadUrl);
            var checksumText = await DownloadChecksumAsync(checksumUri, timeoutSource.Token);
            var checksum = ParseChecksum(checksumText, packageName);

            return new UpdateRelease(
                latestVersion,
                latestVersion.ToString(3),
                string.IsNullOrWhiteSpace(payload.Body)
                    ? "本次更新未提供更新说明。"
                    : payload.Body.Trim(),
                releasePageUri,
                new UpdatePackage(packageName, packageAsset.Size, packageUri, checksum));
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("检查更新超时。", exception);
        }
        catch (UpdateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw new UpdateException("无法从更新服务器获取有效的版本信息。", exception);
        }
    }

    private async Task<string> DownloadUpdateCoreAsync(
        UpdateRelease release,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackage(release);
        var destinationPath = Path.Combine(_options.DownloadDirectory, release.Package.FileName);
        var temporaryPath = destinationPath + ".download";

        try
        {
            Directory.CreateDirectory(_options.DownloadDirectory);
            if (File.Exists(destinationPath)
                && string.Equals(
                    await ComputeHashAsync(destinationPath, cancellationToken),
                    release.Package.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new UpdateDownloadProgress(release.Package.Size, release.Package.Size));
                return destinationPath;
            }

            using var response = await _httpClient.GetAsync(
                release.Package.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? release.Package.Size;
            if (totalBytes <= 0)
            {
                totalBytes = release.Package.Size;
            }

            long bytesReceived = 0;
            string actualHash;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var count = await source.ReadAsync(buffer, cancellationToken);
                    if (count == 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    hash.AppendData(buffer, 0, count);
                    bytesReceived += count;
                    progress?.Report(new UpdateDownloadProgress(bytesReceived, totalBytes));
                }

                await destination.FlushAsync(cancellationToken);
                actualHash = Convert.ToHexString(hash.GetHashAndReset());
            }

            if (!string.Equals(actualHash, release.Package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException("更新包完整性校验失败，文件已丢弃。");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            progress?.Report(new UpdateDownloadProgress(bytesReceived, totalBytes));
            return destinationPath;
        }
        catch (OperationCanceledException)
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
        catch (UpdateException)
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            DeleteTemporaryFile(temporaryPath);
            throw new UpdateException("更新包下载失败。", exception);
        }
    }

    private async Task<string> DownloadChecksumAsync(Uri checksumUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            checksumUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumChecksumBytes)
        {
            throw new UpdateException("更新校验清单大小异常。");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(bytes, cancellationToken);
            if (count == 0)
            {
                break;
            }

            if (buffer.Length + count > MaximumChecksumBytes)
            {
                throw new UpdateException("更新校验清单大小异常。");
            }

            await buffer.WriteAsync(bytes.AsMemory(0, count), cancellationToken);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string ParseChecksum(string checksumText, string packageName)
    {
        foreach (var line in checksumText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2
                && string.Equals(parts[^1].TrimStart('*'), packageName, StringComparison.Ordinal)
                && IsSha256(parts[0]))
            {
                return parts[0].ToUpperInvariant();
            }
        }

        throw new UpdateException("更新校验清单中缺少当前更新包的 SHA-256。");
    }

    private static GitHubAsset FindAsset(IReadOnlyList<GitHubAsset>? assets, string name)
    {
        var matches = assets?
            .Where(asset => string.Equals(asset.Name, name, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches is not { Length: 1 })
        {
            throw new UpdateException($"最新版本缺少唯一的更新资产：{name}");
        }

        return matches[0];
    }

    private Uri ParseTrustedReleaseUri(string? value) =>
        ParseTrustedUri(value, "/" + _options.RepositoryOwner + "/" + _options.RepositoryName + "/releases/");

    private Uri ParseTrustedAssetUri(string? value) =>
        ParseTrustedUri(value, "/" + _options.RepositoryOwner + "/" + _options.RepositoryName + "/releases/download/");

    private static Uri ParseTrustedUri(string? value, string requiredPathPrefix)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith(requiredPathPrefix, StringComparison.Ordinal))
        {
            throw new UpdateException("更新服务器返回了不可信的下载地址。");
        }

        return uri;
    }

    private static Version ParseVersion(string? tagName)
    {
        var value = tagName?.Trim();
        if (value is null
            || value.Length < 2
            || value[0] != 'v'
            || !Version.TryParse(value[1..], out var version)
            || version.Major < 0
            || version.Minor < 0
            || version.Build < 0
            || version.Revision >= 0
            || !string.Equals(value, $"v{version.ToString(3)}", StringComparison.Ordinal))
        {
            throw new UpdateException("更新服务器返回了无效的稳定版本号。");
        }

        return version;
    }

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private void ValidatePackage(UpdateRelease release)
    {
        var package = release.Package;
        var expectedName = _options.PackageName($"v{release.Version.ToString(3)}");
        if (Path.GetFileName(package.FileName) != package.FileName
            || !string.Equals(package.FileName, expectedName, StringComparison.Ordinal)
            || !IsSha256(package.Sha256)
            || !package.DownloadUri.Equals(ParseTrustedAssetUri(package.DownloadUri.AbsoluteUri)))
        {
            throw new UpdateException("更新包信息无效或来源不可信。");
        }
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static void ConfigureRequestHeaders(HttpClient httpClient)
    {
        if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("STM32CubeMX-Chinese-Patcher", "1.0"));
        }

        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    private static void ValidateOptions(UpdateOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RepositoryOwner)
            || string.IsNullOrWhiteSpace(options.RepositoryName)
            || string.IsNullOrWhiteSpace(options.RuntimeIdentifier)
            || options.ApiBaseUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(options.ApiBaseUri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
            || options.CheckTimeout <= TimeSpan.Zero
            || string.IsNullOrWhiteSpace(options.DownloadDirectory))
        {
            throw new ArgumentException("自动更新配置无效。", nameof(options));
        }
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("browser_download_url")] string? DownloadUrl);
}
