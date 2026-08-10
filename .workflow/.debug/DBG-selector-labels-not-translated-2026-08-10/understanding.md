# Understanding Document

**Session ID**: DBG-selector-labels-not-translated-2026-08-10
**Bug Description**: MCU/MPU 选择器左侧筛选标签已有词典映射，但运行时仍显示英文。
**Started**: 2026-08-10T12:20:00+08:00

---

## Exploration Timeline

### Iteration 1 - Initial Exploration

#### Current Understanding

- 截图中的同一窗口已有标题、页签、工具栏和表头完成汉化，说明 Java Agent 正常启动并扫描该窗口。
- 源词典包含截图中的精确键，当前 Agent 日志显示启动时加载了 218 个词条，与新增 5 项后的词典数量一致。
- `SwingTranslator.translateComponent` 只直接处理标准 Swing/AWT 文本组件、表头、页签、边框和菜单；左侧筛选标签很可能由未覆盖的自定义组件或渲染器绘制。

#### Evidence from Code Search

- `src/STM32CubeMX.ChinesePatcher/Resources/Payload/translations.tsv` 包含 `Commercial Part Number`、`PRODUCT INFO`、`Segment`、`Series`、`Line`、`Marketing Status`、`Price`、`Package`、`Core`、`Coprocessor`、`MEMORY`。
- `C:/Users/Administrator/.stm32cubemx/zh-agent.log` 最新记录为 `agent starting; dictionary entries=218`，随后记录了多轮可见属性替换。
- 反编译 `SwingTranslator.class` 后确认其标准组件分支不包含任意自定义 CubeMX 组件，也不遍历通用模型或自定义渲染器。

#### Hypotheses Generated

- H1：筛选标签属于自定义组件，不是 `JLabel`/`AbstractButton`，因此当前扫描器无法访问其文本属性。
- H2：筛选标签文本存放在组件模型、客户端属性或 renderer 中，组件树遍历虽到达控件，但没有翻译数据源。
- H3：运行时字符串带有换行或 HTML 包装，导致精确键不匹配。
- H4：CubeMX 仍加载旧词典。

#### Next Steps

- 查询运行中的 CubeMX 进程和安装路径。
- 定位运行时包含目标英文的 CubeMX 类或资源。
- 为 Agent 增加只记录目标文本承载类的 NDJSON 诊断，复现后验证 H1-H3。

---

## Current Consolidated Understanding

### What We Know

- 新词典已经部署并由 Agent 加载。
- 问题集中在选择器左侧自定义筛选控件，而非全局 Agent 失效。

### What Was Disproven

- ~~H4：CubeMX 仍加载旧词典。~~ Agent 日志中的 218 个词条与当前词典一致。

### Current Investigation Focus

识别筛选标签的真实组件类、文本存储位置和可安全调用的 setter。

### Iteration 2 - Evidence Analysis

#### Log Analysis Results

**H1: CONFIRMED**

- `McuFilterPanel.createInfoSection()` 和 `createMemorySection()` 使用 `org.jdesktop.swingx.JXTaskPane.setTitle(...)` 设置全部目标筛选标题。
- `JXTaskPane` 公开提供 `getTitle()` 与 `setTitle(String)`，但当前 `SwingTranslator.translateComponent()` 没有该类型分支。

**H2: REJECTED**

- 目标标题直接存放在 `JXTaskPane.title` 属性，无需修改模型、renderer 或客户端属性。

**H3: CONFIRMED**

- `McuSearchPNPanel` 把构造参数中的第一个空格替换为 `<br>`，再包裹为 `<html>...</html>`。
- 因此运行时 JLabel 文本为 `<html>Commercial<br>Part Number</html>`，不会命中普通键 `Commercial Part Number`。

#### Corrected Understanding

- ~~所有未翻译项都由同一个自定义组件问题引起。~~ → 筛选标题缺少 `JXTaskPane.title` 支持；料号标签则是 HTML 精确键不匹配。
- ~~可能需要修改 CubeMX 的模型或 renderer。~~ → `JXTaskPane` 已有公开 getter/setter，Agent 只需读取、查词典并写回标题。

#### Root Cause Identified

- Agent 只覆盖标准 Swing/AWT 文本属性，遗漏 SwingX `JXTaskPane.title`。
- 词典遗漏 CubeMX 实际生成的 HTML 料号标签键。

---

## Current Consolidated Understanding (Updated)

### What We Know

- 当前部署词典已被 Agent 完整加载，数量为 218。
- `PRODUCT INFO` 至 `MEMORY` 的目标文案都位于 `JXTaskPane.title`。
- `Commercial Part Number` 的实际 JLabel 文本带 HTML 与换行标签。

### What Was Disproven

- ~~CubeMX 加载旧词典。~~
- ~~目标标题必须通过模型或 renderer 修改。~~

### Current Investigation Focus

为 Agent 增加反射式 `JXTaskPane` 标题翻译，并补充保持 HTML 结构的精确词典键。

### Iteration 3 - Resolution

#### Fix Applied

- 更新 `stm32cubemx-zh-agent.jar` 的 Java 21 `CubeMxZhAgent`，在既有标准 Swing 扫描后递归翻译 `JXTaskPane.title`。
- 新增 `<html>Commercial<br>Part Number</html>` 到 `<html>商用<br>料号</html>` 的精确映射，保留原 HTML 结构。
- 扩展资源测试，校验 Agent 包含 `JXTaskPane/getTitle/setTitle` 支持，并校验 HTML 映射。

#### Verification Results

- 临时 Swing 复现：`TASK_PANE_PASS`、`HTML_LABEL_PASS`。
- CubeMX 6.18.1-RC2 自带真实 `JXTaskPane`：`REAL_TASK_PANE_PASS`、`REAL_HTML_LABEL_PASS`。
- 资源专项测试：2/2 通过。
- 完整测试：90/90 通过。
- Debug 构建：0 警告、0 错误。
- 集成校验通过。
- STM32CubeMX 6.18.1-RC2 / JRE 21 隔离兼容性验证通过，Agent 与 CubeMX 均以退出码 0 结束，INI 往返一致。

#### Lessons Learned

1. 词典有键不代表运行时一定可达，必须确认具体组件属性。
2. CubeMX 的筛选区大量使用 SwingX `JXTaskPane`，不能只覆盖标准 `JLabel` 和按钮。
3. 截图中的视觉换行可能来自 HTML 运行时文本，精确匹配必须保留实际 HTML 结构。

#### Key Insights for Future

- 新增选择器词条时，应同时核对 `JXTaskPane.title` 与 HTML JLabel 的真实值。
- Agent 变更需要同时通过 Java 21 class 校验、真实 Swing 组件复现和 CubeMX 隔离启动验证。
