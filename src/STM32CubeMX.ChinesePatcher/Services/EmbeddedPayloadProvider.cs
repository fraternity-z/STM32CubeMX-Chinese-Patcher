using System.Reflection;
using System.IO;
using STM32CubeMX.ChinesePatcher.Core.Abstractions;
using STM32CubeMX.ChinesePatcher.Core.Models;

namespace STM32CubeMX.ChinesePatcher.Services;

public sealed class EmbeddedPayloadProvider : IPayloadProvider
{
    private const string AgentResource = "STM32CubeMX.ChinesePatcher.Payload.stm32cubemx-zh-agent.jar";
    private const string DictionaryResource = "STM32CubeMX.ChinesePatcher.Payload.translations.tsv";
    private readonly Lazy<PayloadBundle> _payload;

    public EmbeddedPayloadProvider()
    {
        _payload = new Lazy<PayloadBundle>(LoadPayload, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public PayloadBundle GetPayload() => _payload.Value;

    private static PayloadBundle LoadPayload()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return new PayloadBundle(
            ReadResource(assembly, AgentResource),
            ReadResource(assembly, DictionaryResource));
    }

    private static byte[] ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"内置资源缺失：{resourceName}");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
