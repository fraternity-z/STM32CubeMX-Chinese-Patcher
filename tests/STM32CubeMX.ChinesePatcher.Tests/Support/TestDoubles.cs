using System.Text;
using STM32CubeMX.ChinesePatcher.Core.Abstractions;
using STM32CubeMX.ChinesePatcher.Core.Models;

namespace STM32CubeMX.ChinesePatcher.Tests.Support;

internal sealed class FakeEnvironmentSource : IEnvironmentSource
{
    public Func<string, string?> Handler { get; init; } = _ => null;

    public string? Read(string variableName) => Handler(variableName);
}

internal sealed class FakeRegistrySource : IRegistrySource
{
    public Func<IReadOnlyList<InstallationCandidate>> Handler { get; init; } = () => [];

    public IReadOnlyList<InstallationCandidate> ReadInstallations() => Handler();
}

internal sealed class FakeVersionSource : IVersionSource
{
    public string ProductVersion { get; init; } = "6.18.1-RC2";

    public string JavaVersion { get; init; } = "21.0.10";

    public string ReadProductVersion(string executablePath) => ProductVersion;

    public string ReadJavaVersion(string rootPath) => JavaVersion;
}

internal sealed class FakeProcessSource(ProcessQueryResult result) : IProcessSource
{
    public ProcessQueryResult Result { get; set; } = result;

    public ProcessQueryResult ReadProcesses() => Result;
}

internal sealed class FakePayloadProvider : IPayloadProvider
{
    private readonly PayloadBundle _payload = new(
        [0x50, 0x4B, 0x03, 0x04, 0x21],
        Encoding.UTF8.GetBytes("Home\t主页\n"));

    public PayloadBundle GetPayload() => _payload;
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; } = now;
}

internal sealed class ProgressCollector : IProgress<OperationProgress>
{
    public List<OperationProgress> Values { get; } = [];

    public void Report(OperationProgress value) => Values.Add(value);
}

internal sealed class TempCubeMxFixture : IDisposable
{
    public TempCubeMxFixture(string iniContent = "-Dfile.encoding=UTF8\n")
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "STM32CubeMX-Chinese-Patcher.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
        File.WriteAllBytes(Path.Combine(RootPath, PatcherPaths.ExecutableName), [0x4D, 0x5A]);
        File.WriteAllText(
            Path.Combine(RootPath, PatcherPaths.IniName),
            iniContent,
            new UTF8Encoding(false));
    }

    public string RootPath { get; }

    public string IniPath => Path.Combine(RootPath, PatcherPaths.IniName);

    public CubeMxInstallation Installation(string javaVersion = "21.0.10") =>
        new(RootPath, "6.18.1-RC2", javaVersion, DetectionSource.Manual);

    public void WriteExpectedPayload(IPayloadProvider provider)
    {
        var payload = provider.GetPayload();
        Directory.CreateDirectory(PatcherPaths.LocalizationDirectory(RootPath));
        File.WriteAllBytes(PatcherPaths.AgentPath(RootPath), payload.AgentJar);
        File.WriteAllBytes(PatcherPaths.DictionaryPath(RootPath), payload.Dictionary);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}

internal static class TestAssert
{
    public static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        Assert.Fail($"预期抛出 {typeof(TException).Name}。");
        throw new InvalidOperationException("Assert.Fail 应当中止测试。");
    }
}
