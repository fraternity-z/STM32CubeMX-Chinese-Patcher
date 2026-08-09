using STM32CubeMX.ChinesePatcher.Core.Models;

namespace STM32CubeMX.ChinesePatcher.Core.Abstractions;

public interface IEnvironmentSource
{
    string? Read(string variableName);
}

public interface IRegistrySource
{
    IReadOnlyList<InstallationCandidate> ReadInstallations();
}

public interface IVersionSource
{
    string ReadProductVersion(string executablePath);

    string ReadJavaVersion(string rootPath);
}

public interface IProcessSource
{
    ProcessQueryResult ReadProcesses();
}

public interface IPayloadProvider
{
    PayloadBundle GetPayload();
}

public interface IClock
{
    DateTimeOffset Now { get; }
}
