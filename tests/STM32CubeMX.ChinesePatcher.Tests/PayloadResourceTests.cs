using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class PayloadResourceTests
{
    private const string AgentResource =
        "STM32CubeMX.ChinesePatcher.Tests.Payload.stm32cubemx-zh-agent.jar";
    private const string DictionaryResource =
        "STM32CubeMX.ChinesePatcher.Tests.Payload.translations.tsv";

    [TestMethod]
    public void AgentJar_DeclaresJava21PremainAndContainsOnlyJava21Classes()
    {
        using var jarStream = OpenResource(AgentResource);
        using var archive = new ZipArchive(jarStream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("META-INF/MANIFEST.MF");
        Assert.IsNotNull(manifestEntry);
        using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
        {
            var manifest = reader.ReadToEnd();
            StringAssert.Contains(manifest, "Premain-Class: com.codex.cubemx.zh.CubeMxZhAgent");
            StringAssert.Contains(manifest, "Can-Redefine-Classes: false");
            StringAssert.Contains(manifest, "Can-Retransform-Classes: false");
        }

        var classEntries = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".class", StringComparison.Ordinal))
            .ToArray();
        Assert.IsNotEmpty(classEntries);
        var header = new byte[8];
        foreach (var entry in classEntries)
        {
            using var stream = entry.Open();
            stream.ReadExactly(header);
            Assert.AreEqual(0xCA, header[0]);
            Assert.AreEqual(0xFE, header[1]);
            Assert.AreEqual(0xBA, header[2]);
            Assert.AreEqual(0xBE, header[3]);
            var majorVersion = (header[6] << 8) | header[7];
            Assert.AreEqual(65, majorVersion, $"{entry.FullName} 不是 Java 21 class。 ");
        }
    }

    [TestMethod]
    public void TranslationDictionary_HasUniqueWellFormedEntriesForRc2()
    {
        using var stream = OpenResource(DictionaryResource);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        Assert.IsNotEmpty(lines);
        StringAssert.Contains(lines[0], "STM32CubeMX 6.18.1-RC2");
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('\t');
            Assert.IsGreaterThan(0, separator, $"第 {index + 1} 行缺少有效键。 ");
            Assert.AreEqual(-1, line.IndexOf('\t', separator + 1), $"第 {index + 1} 行包含多余列。 ");
            var key = line[..separator];
            var value = line[(separator + 1)..];
            Assert.IsFalse(string.IsNullOrEmpty(value), $"第 {index + 1} 行译文为空。 ");
            Assert.IsTrue(entries.TryAdd(key, value), $"第 {index + 1} 行存在重复键：{key}");
        }

        Assert.IsGreaterThanOrEqualTo(180, entries.Count);
        Assert.AreEqual("比较工程", entries["Compare Projects"]);
        Assert.AreEqual("仅显示差异", entries["Show differences only"]);
        Assert.AreEqual("可编程逻辑阵列（PLAY）", entries["Programmable logic array (PLAY)"]);
    }

    private static Stream OpenResource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"测试资源缺失：{name}");
}
