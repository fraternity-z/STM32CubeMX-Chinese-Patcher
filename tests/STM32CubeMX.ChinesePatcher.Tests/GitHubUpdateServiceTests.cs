using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using STM32CubeMX.ChinesePatcher.Models;
using STM32CubeMX.ChinesePatcher.Services;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class GitHubUpdateServiceTests
{
    [TestMethod]
    public async Task CheckForUpdateAsync_ReturnsTrustedNewerReleaseWithChecksum()
    {
        var packageBytes = Encoding.UTF8.GetBytes("release package");
        var checksum = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal)
                ? JsonResponse(CreateReleaseJson("v1.2.0", packageBytes.Length))
                : TextResponse($"{checksum.ToLowerInvariant()}  {PackageName("v1.2.0")}")));
        using var service = CreateService(handler);

        var release = await service.CheckForUpdateAsync(new Version(1, 1, 9));

        Assert.IsNotNull(release);
        Assert.AreEqual(new Version(1, 2, 0), release.Version);
        Assert.AreEqual("修复问题并改善更新体验。", release.ReleaseNotes);
        Assert.AreEqual(PackageName("v1.2.0"), release.Package.FileName);
        Assert.AreEqual(packageBytes.Length, release.Package.Size);
        Assert.AreEqual(checksum, release.Package.Sha256);
        Assert.AreEqual(2, handler.CallCount);
        Assert.IsTrue(handler.Requests[0].Headers.UserAgent.Any());
        Assert.AreEqual("application/vnd.github+json", handler.Requests[0].Headers.Accept.Single().MediaType);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_CurrentVersionDoesNotDownloadChecksum()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            JsonResponse(CreateReleaseJson("v1.2.0", 100))));
        using var service = CreateService(handler);

        var release = await service.CheckForUpdateAsync(new Version(1, 2, 0, 0));

        Assert.IsNull(release);
        Assert.AreEqual(1, handler.CallCount);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_ConcurrentCallsShareOneRequest()
    {
        var responseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await responseGate.Task.WaitAsync(cancellationToken);
            return JsonResponse(CreateReleaseJson("v1.0.0", 100));
        });
        using var service = CreateService(handler);

        var first = service.CheckForUpdateAsync(new Version(1, 0, 0));
        var second = service.CheckForUpdateAsync(new Version(1, 0, 0));
        responseGate.SetResult();

        await Task.WhenAll(first, second);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, handler.CallCount);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_CompletedCheckCanBeRefreshed()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            JsonResponse(CreateReleaseJson("v1.0.0", 100))));
        using var service = CreateService(handler);

        await service.CheckForUpdateAsync(new Version(1, 0, 0));
        await service.CheckForUpdateAsync(new Version(1, 0, 0));

        Assert.AreEqual(2, handler.CallCount);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_FailedCheckCanBeRetried()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var service = CreateService(handler);

        await Assert.ThrowsAsync<UpdateException>(
            () => service.CheckForUpdateAsync(new Version(1, 0, 0)));
        await Assert.ThrowsAsync<UpdateException>(
            () => service.CheckForUpdateAsync(new Version(1, 0, 0)));

        Assert.AreEqual(2, handler.CallCount);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_RejectsUntrustedAssetUrl()
    {
        var releaseJson = CreateReleaseJson(
            "v1.1.0",
            100,
            packageUrl: "https://example.com/update.exe");
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(releaseJson)));
        using var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<UpdateException>(
            () => service.CheckForUpdateAsync(new Version(1, 0, 0)));

        StringAssert.Contains(exception.Message, "不可信");
        Assert.AreEqual(1, handler.CallCount);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_RejectsMissingPackageChecksum()
    {
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal)
                ? JsonResponse(CreateReleaseJson("v1.1.0", 100))
                : TextResponse(new string('0', 64) + "  another-file.exe")));
        using var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<UpdateException>(
            () => service.CheckForUpdateAsync(new Version(1, 0, 0)));

        StringAssert.Contains(exception.Message, "缺少当前更新包");
    }

    [TestMethod]
    [DataRow("v1.1")]
    [DataRow("v01.1.0")]
    [DataRow("1.1.0")]
    public async Task CheckForUpdateAsync_RejectsInvalidStableVersion(string tagName)
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            JsonResponse(CreateReleaseJson(tagName, 100))));
        using var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<UpdateException>(
            () => service.CheckForUpdateAsync(new Version(1, 0, 0)));

        StringAssert.Contains(exception.Message, "版本号");
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_WritesVerifiedPackageAndReportsProgress()
    {
        using var fixture = new UpdateDirectoryFixture();
        var packageBytes = Enumerable.Range(0, 200_000).Select(value => (byte)(value % 251)).ToArray();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(BinaryResponse(packageBytes)));
        using var service = CreateService(handler, fixture.RootPath);
        var progress = new DownloadProgressCollector();
        var release = CreateRelease("1.1.0", packageBytes);

        var packagePath = await service.DownloadUpdateAsync(release, progress);

        CollectionAssert.AreEqual(packageBytes, await File.ReadAllBytesAsync(packagePath));
        Assert.AreEqual(1, handler.CallCount);
        Assert.IsNotEmpty(progress.Values);
        Assert.AreEqual(100, progress.Values[^1].Percentage);
        Assert.IsFalse(File.Exists(packagePath + ".download"));
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_ReusesPreviouslyVerifiedPackage()
    {
        using var fixture = new UpdateDirectoryFixture();
        var packageBytes = Encoding.UTF8.GetBytes("verified package");
        var release = CreateRelease("1.1.0", packageBytes);
        Directory.CreateDirectory(fixture.RootPath);
        var existingPath = Path.Combine(fixture.RootPath, release.Package.FileName);
        await File.WriteAllBytesAsync(existingPath, packageBytes);
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("不应重复下载"));
        using var service = CreateService(handler, fixture.RootPath);

        var packagePath = await service.DownloadUpdateAsync(release);

        Assert.AreEqual(existingPath, packagePath);
        Assert.AreEqual(0, handler.CallCount);
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_HashMismatchDeletesTemporaryPackage()
    {
        using var fixture = new UpdateDirectoryFixture();
        var expectedBytes = Encoding.UTF8.GetBytes("expected");
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            BinaryResponse(Encoding.UTF8.GetBytes("tampered"))));
        using var service = CreateService(handler, fixture.RootPath);
        var release = CreateRelease("1.1.0", expectedBytes);

        var exception = await Assert.ThrowsAsync<UpdateException>(
            () => service.DownloadUpdateAsync(release));

        StringAssert.Contains(exception.Message, "完整性校验失败");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RootPath, release.Package.FileName)));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RootPath, release.Package.FileName + ".download")));
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_CancellationDeletesPartialPackage()
    {
        using var fixture = new UpdateDirectoryFixture();
        var firstChunkRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new CancelableDownloadStream(Encoding.UTF8.GetBytes("partial"), firstChunkRead);
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        }));
        using var service = CreateService(handler, fixture.RootPath);
        var release = CreateRelease("1.1.0", Encoding.UTF8.GetBytes("complete package"));
        using var cancellation = new CancellationTokenSource();

        var downloadTask = service.DownloadUpdateAsync(release, cancellationToken: cancellation.Token);
        await firstChunkRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => downloadTask);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RootPath, release.Package.FileName + ".download")));
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_ConcurrentCallsShareOneDownload()
    {
        using var fixture = new UpdateDirectoryFixture();
        var packageBytes = Encoding.UTF8.GetBytes("download package");
        var responseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await responseGate.Task.WaitAsync(cancellationToken);
            return BinaryResponse(packageBytes);
        });
        using var service = CreateService(handler, fixture.RootPath);
        var release = CreateRelease("1.1.0", packageBytes);

        var first = service.DownloadUpdateAsync(release);
        var second = service.DownloadUpdateAsync(release);
        responseGate.SetResult();

        await Task.WhenAll(first, second);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, handler.CallCount);
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_RejectsPackageFromAnotherRepository()
    {
        using var fixture = new UpdateDirectoryFixture();
        var bytes = Encoding.UTF8.GetBytes("package");
        var validRelease = CreateRelease("1.1.0", bytes);
        var untrustedRelease = validRelease with
        {
            Package = validRelease.Package with
            {
                DownloadUri = new Uri(
                    $"https://github.com/another/project/releases/download/v1.1.0/{validRelease.Package.FileName}")
            }
        };
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("不应请求不可信地址"));
        using var service = CreateService(handler, fixture.RootPath);

        var exception = await Assert.ThrowsAsync<UpdateException>(
            () => service.DownloadUpdateAsync(untrustedRelease));

        StringAssert.Contains(exception.Message, "不可信");
        Assert.AreEqual(0, handler.CallCount);
    }

    private static GitHubUpdateService CreateService(
        HttpMessageHandler handler,
        string? downloadDirectory = null)
    {
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        return new GitHubUpdateService(
            client,
            new UpdateOptions
            {
                DownloadDirectory = downloadDirectory ?? Path.GetTempPath()
            },
            ownsHttpClient: true);
    }

    private static UpdateRelease CreateRelease(string version, byte[] packageBytes)
    {
        var parsedVersion = Version.Parse(version);
        var tagName = $"v{parsedVersion.ToString(3)}";
        var packageName = PackageName(tagName);
        return new UpdateRelease(
            parsedVersion,
            parsedVersion.ToString(3),
            "更新说明",
            new Uri($"https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/tag/{tagName}"),
            new UpdatePackage(
                packageName,
                packageBytes.Length,
                new Uri($"https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/download/{tagName}/{packageName}"),
                Convert.ToHexString(SHA256.HashData(packageBytes))));
    }

    private static string CreateReleaseJson(
        string tagName,
        long packageSize,
        string? packageUrl = null)
    {
        var packageName = PackageName(tagName);
        return JsonSerializer.Serialize(new
        {
            tag_name = tagName,
            body = "修复问题并改善更新体验。",
            html_url = $"https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/tag/{tagName}",
            assets = new object[]
            {
                new
                {
                    name = packageName,
                    size = packageSize,
                    browser_download_url = packageUrl
                        ?? $"https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/download/{tagName}/{packageName}"
                },
                new
                {
                    name = "SHA256SUMS.txt",
                    size = 128,
                    browser_download_url = $"https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/download/{tagName}/SHA256SUMS.txt"
                }
            }
        });
    }

    private static string PackageName(string tagName) =>
        $"STM32CubeMX-Chinese-Patcher-{tagName}-win-x64.exe";

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage TextResponse(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, "text/plain")
    };

    private static HttpResponseMessage BinaryResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes)
    };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler = handler;
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            lock (Requests)
            {
                Requests.Add(request);
            }

            return _handler(request, cancellationToken);
        }
    }

    private sealed class DownloadProgressCollector : IProgress<UpdateDownloadProgress>
    {
        public List<UpdateDownloadProgress> Values { get; } = [];

        public void Report(UpdateDownloadProgress value) => Values.Add(value);
    }

    private sealed class UpdateDirectoryFixture : IDisposable
    {
        public UpdateDirectoryFixture()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "STM32CubeMX-Chinese-Patcher.UpdateTests",
                Guid.NewGuid().ToString("N"));
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class CancelableDownloadStream(
        byte[] firstChunk,
        TaskCompletionSource firstChunkRead) : Stream
    {
        private readonly byte[] _firstChunk = firstChunk;
        private readonly TaskCompletionSource _firstChunkRead = firstChunkRead;
        private bool _wasRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_wasRead)
            {
                _wasRead = true;
                _firstChunk.CopyTo(buffer);
                _firstChunkRead.SetResult();
                return ValueTask.FromResult(_firstChunk.Length);
            }

            return new ValueTask<int>(WaitForCancellationAsync(cancellationToken));
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
