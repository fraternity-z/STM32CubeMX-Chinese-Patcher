using STM32CubeMX.ChinesePatcher.Core.Services;

namespace STM32CubeMX.ChinesePatcher.Services;

public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        PayloadProvider = new EmbeddedPayloadProvider();
        ProcessStateService = new ProcessStateService(new SystemProcessSource());
        StateInspector = new PatchStateInspector(PayloadProvider);
        InstallationDetector = new InstallationDetector(
            new SystemEnvironmentSource(),
            new WindowsRegistrySource(),
            new FileVersionSource());
        PatchService = new PatchService(
            PayloadProvider,
            ProcessStateService,
            StateInspector,
            new SystemClock());
        OperationCoordinator = new OperationCoordinator(PatchService);
        UpdateService = new GitHubUpdateService(new UpdateOptions());
    }

    public EmbeddedPayloadProvider PayloadProvider { get; }

    public ProcessStateService ProcessStateService { get; }

    public PatchStateInspector StateInspector { get; }

    public InstallationDetector InstallationDetector { get; }

    public PatchService PatchService { get; }

    public OperationCoordinator OperationCoordinator { get; }

    public IUpdateService UpdateService { get; }

    public void Dispose()
    {
        if (UpdateService is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
