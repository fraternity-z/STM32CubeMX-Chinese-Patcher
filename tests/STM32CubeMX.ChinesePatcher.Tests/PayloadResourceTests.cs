using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

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

        var agentClassEntry = archive.GetEntry("com/codex/cubemx/zh/CubeMxZhAgent.class");
        Assert.IsNotNull(agentClassEntry);
        using var agentClassStream = agentClassEntry.Open();
        using var agentClassBytes = new MemoryStream();
        agentClassStream.CopyTo(agentClassBytes);
        var agentClassConstants = Encoding.Latin1.GetString(agentClassBytes.ToArray());
        StringAssert.Contains(agentClassConstants, "org.jdesktop.swingx.JXTaskPane");
        StringAssert.Contains(agentClassConstants, "getTitle");
        StringAssert.Contains(agentClassConstants, "setTitle");
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
        Assert.AreEqual("管理嵌入式软件包", entries["Manage embedded software packages"]);
        Assert.AreEqual("连接与更新", entries["Connection & Updates"]);

        var selectorLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MCU/MPU Selector"] = "MCU/MPU 选择器",
            ["Board Selector"] = "开发板选择器",
            ["Example Selector"] = "示例选择器",
            ["Cross Selector"] = "交叉选择器",
            ["MCU/MPU Filters"] = "MCU/MPU 筛选器",
            ["Commercial Part Number"] = "商用料号",
            ["<html>Commercial<br>Part Number</html>"] = "<html>商用<br>料号</html>",
            ["PRODUCT INFO"] = "产品信息",
            ["Segment"] = "产品类别",
            ["Series"] = "系列",
            ["Line"] = "产品线",
            ["Marketing Status"] = "市场状态",
            ["Price"] = "价格",
            ["Package"] = "封装",
            ["Core"] = "内核",
            ["Coprocessor"] = "协处理器",
            ["MEMORY"] = "存储器",
            ["DRAM Support"] = "DRAM 支持",
            ["Flash Support"] = "Flash 支持",
            ["Dual Bank Flash"] = "双 Bank Flash",
            ["TIMER"] = "定时器",
            ["Timer Function"] = "定时器功能",
            ["ANALOG"] = "模拟",
            ["COMMUNICATION INTERFACE"] = "通信接口",
            ["USB INTERFACE"] = "USB 接口",
            ["EXTERNAL MEMORY INTERFACE"] = "外部存储器接口",
            ["External Memory Interface"] = "外部存储器接口",
            ["OTHER INTERFACE"] = "其他接口",
            ["Additional Interface"] = "附加接口",
            ["GRAPHICS"] = "图形",
            ["Display Controller"] = "显示控制器",
            ["Graphic Accelerator"] = "图形加速器",
            ["SECURITY"] = "安全",
            ["Cryptography"] = "加密",
            ["Security Function"] = "安全功能",
            ["OTHER PERIPHERAL"] = "其他外设",
            ["MIDDLEWARE"] = "中间件",
            ["PHYSICAL"] = "物理特性",
            ["Peripheral"] = "外设",
            ["Features"] = "特性",
            ["Block Diagram"] = "框图",
            ["CAD Resources"] = "CAD 资源",
            ["Datasheet"] = "数据手册",
            ["Buy"] = "购买",
            ["Start Project"] = "创建工程",
            ["Commercial Part No"] = "商用料号",
            ["Part No"] = "料号",
            ["Reference"] = "参考型号",
        };
        foreach (var (source, translation) in selectorLabels)
        {
            Assert.AreEqual(translation, entries[source], $"选择器界面词条不正确：{source}");
        }

        var protectedTerms = new[]
        {
            "Flash", "RAM", "SRAM", "EEPROM", "MCU", "MPU", "GPIO", "NVIC", "DMA", "I/O", "CAD", "PDF", "TXT", "PLAY",
            "FPU", "ITCM", "DTCM", "DRAM", "USB", "IPCC", "SMPS", "SDMMC",
        };
        foreach (var (source, translation) in entries)
        {
            foreach (var term in protectedTerms.Where(term => ContainsTechnicalTerm(source, term)))
            {
                Assert.IsTrue(
                    ContainsTechnicalTerm(translation, term),
                    $"专业术语 {term} 不应翻译：{source} -> {translation}");
            }
        }
    }

    private static bool ContainsTechnicalTerm(string text, string term) =>
        Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9]){Regex.Escape(term)}(?![A-Za-z0-9])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static Stream OpenResource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"测试资源缺失：{name}");
}
