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
            StringAssert.Contains(manifest, "Premain-Class: com.codex.cubemx.zh.CubeMxZhCompatibilityAgent");
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

        var compatibilityClassEntry = archive.GetEntry("com/codex/cubemx/zh/PluginTabCompatibility.class");
        Assert.IsNotNull(compatibilityClassEntry);
        using var compatibilityClassStream = compatibilityClassEntry.Open();
        using var compatibilityClassBytes = new MemoryStream();
        compatibilityClassStream.CopyTo(compatibilityClassBytes);
        var compatibilityClassConstants = Encoding.Latin1.GetString(compatibilityClassBytes.ToArray());
        StringAssert.Contains(compatibilityClassConstants, "com.st.microxplorer.maingui.MainPanel");
        StringAssert.Contains(compatibilityClassConstants, "com.st.microxplorer.plugin.PluginManage");
        StringAssert.Contains(compatibilityClassConstants, "OriginalPluginNameLabel");

        var originalNameLabelClassEntry = archive.GetEntry(
            "com/codex/cubemx/zh/PluginTabCompatibility$OriginalPluginNameLabel.class");
        Assert.IsNotNull(originalNameLabelClassEntry);
        using var originalNameLabelClassStream = originalNameLabelClassEntry.Open();
        using var originalNameLabelClassBytes = new MemoryStream();
        originalNameLabelClassStream.CopyTo(originalNameLabelClassBytes);
        var originalNameLabelClassConstants = Encoding.Latin1.GetString(originalNameLabelClassBytes.ToArray());
        StringAssert.Contains(originalNameLabelClassConstants, "getSelectedPluginView");
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
            ["Board Filters"] = "开发板筛选器",
            ["Commercial Part Number"] = "商用料号",
            ["<html>Commercial<br>Part Number</html>"] = "<html>商用<br>料号</html>",
            ["PRODUCT INFO"] = "产品信息",
            ["Type"] = "类型",
            ["Supplier"] = "供应商",
            ["MCU / MPU Series"] = "MCU / MPU 系列",
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
            ["FEATURES"] = "特性",
            ["Embedded Sensor"] = "板载传感器",
            ["User Button"] = "用户按钮",
            ["Camera"] = "摄像头",
            ["Connector"] = "连接器",
            ["Memory Card"] = "存储卡",
            ["Display"] = "显示",
            ["Joystick"] = "操纵杆",
            ["Audio Line Input"] = "音频线路输入",
            ["Audio Line Output"] = "音频线路输出",
            ["Audio Processor"] = "音频处理器",
            ["Microphone"] = "麦克风",
            ["Potentiometer"] = "电位器",
            ["Power Supply"] = "电源",
            ["Speaker"] = "扬声器",
            ["Touch Feature"] = "触摸功能",
            ["USB Port"] = "USB 端口",
            ["Debug Connector"] = "调试连接器",
            ["Wireless Interface"] = "无线接口",
            ["Other on-board Feature"] = "其他板载功能",
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

        var codeGenerationLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Project Name"] = "工程名称",
            ["Project Location"] = "工程位置",
            ["Application Structure"] = "应用结构",
            ["Do not generate the main()"] = "不生成 main()",
            ["Toolchain Folder Location"] = "工具链文件夹位置",
            ["Toolchain / IDE"] = "工具链 / IDE",
            ["Min Version"] = "最低版本",
            ["Generate Under Root"] = "在根目录下生成",
            ["Linker Settings"] = "链接器设置",
            ["Minimum Heap Size"] = "最小堆大小",
            ["Minimum Stack Size"] = "最小栈大小",
            ["Thread-safe Settings"] = "线程安全设置",
            ["Enable multi-threaded support"] = "启用多线程支持",
            ["Thread-safe Locking Strategy"] = "线程安全锁策略",
            ["Mcu and Firmware Package"] = "MCU 与固件包",
            ["Mcu Reference"] = "MCU 参考型号",
            ["Firmware Package Name and Version"] = "固件包名称和版本",
            ["Use latest available version"] = "使用最新可用版本",
            ["Use Default Firmware Location"] = "使用默认固件位置",
            ["Firmware Relative Path"] = "固件相对路径",
            ["STM32Cube MCU packages and embedded software packs"] = "STM32Cube MCU 软件包和嵌入式软件包",
            ["Copy all used libraries into the project folder"] = "将所有使用的库复制到工程文件夹",
            ["Copy only the necessary library files"] = "仅复制必要的库文件",
            ["Add necessary library files as reference in the toolchain project configuration file"] = "在工具链工程配置文件中引用必要的库文件",
            ["Generated files"] = "生成的文件",
            ["Generate peripheral initialization as a pair of '.c/.h' files per peripheral"] = "为每个外设生成一对“.c/.h”初始化文件",
            ["Backup previously generated files when re-generating"] = "重新生成时备份之前生成的文件",
            ["Keep User Code when re-generating"] = "重新生成时保留用户代码",
            ["Delete previously generated files when not re-generated"] = "删除未重新生成的旧文件",
            ["HAL Settings"] = "HAL 设置",
            ["Set all free pins as analog (to optimize the power consumption)"] = "将所有空闲引脚设置为模拟模式（以优化功耗）",
            ["Enable Full Assert"] = "启用完整断言",
            ["User Actions"] = "用户操作",
            ["Before Code Generation"] = "代码生成前",
            ["After Code Generation"] = "代码生成后",
            ["Template Settings"] = "模板设置",
            ["Select a template to generate customized code"] = "选择模板以生成自定义代码",
            ["Settings..."] = "设置...",
            ["Driver Selector"] = "驱动选择器",
            ["Search (Ctrl+F)"] = "搜索（Ctrl+F）",
            ["Register CallBack"] = "注册回调",
            ["Generated Function Calls"] = "生成的函数调用",
            ["Rank"] = "顺序",
            ["Function Name"] = "函数名称",
            ["Peripheral Instance"] = "外设实例",
            ["Do Not Generate Function Call"] = "不生成函数调用",
            ["Visibility (Static)"] = "可见性（Static）",
        };
        foreach (var (source, translation) in codeGenerationLabels)
        {
            Assert.AreEqual(translation, entries[source], $"代码生成界面词条不正确：{source}");
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
