# STM32CubeMX 汉化工具

一个面向 Windows 的 STM32CubeMX 汉化补丁管理器。它把汉化 Java Agent 和翻译词典嵌入单个 EXE，支持自动检测安装目录、版本、运行状态和当前汉化状态，并提供手动目录选择、一键汉化和一键回退。

## 使用

1. 运行 `STM32CubeMX-Chinese-Patcher.exe`。
2. 确认界面显示的安装目录和版本正确；自动检测失败时点击“浏览”。
3. 关闭正在运行的 STM32CubeMX。
4. 点击“一键汉化”或“一键回退”。安装目录需要管理员权限时，Windows 会显示 UAC 确认。

回退仅移除本工具管理的 `-javaagent:` 启动项，不会用旧备份覆盖整个 `STM32CubeMX.l4j.ini`。载荷和首次配置备份会保留，便于检查和再次启用。

## 自动检测顺序

1. 用户环境变量 `STM32CubeMX_PATH`
2. 当前用户和本机的 Windows 卸载注册表
3. 常见安装目录
4. 用户手动选择

每个候选目录都必须同时包含 `STM32CubeMX.exe` 和 `STM32CubeMX.l4j.ini`。运行状态会识别主程序及安装目录内置 JRE 的 `java.exe` / `javaw.exe`。

## 安全策略

- CubeMX 正在运行或运行状态未知时拒绝修改。
- 当前载荷要求目标安装使用 JRE 21 或更高版本。
- 同名文件来源不明且哈希不匹配时拒绝覆盖。
- JAR、词典、INI、状态文件均先写入同目录临时文件，再原子替换。
- 提交前校验目标文件未被其他程序改动；检测到并发修改时中止，避免静默覆盖外部内容。
- 同一 Windows 会话内，同一 CubeMX 安装同一时间只允许一个汉化或回退操作。
- 多文件提交失败时，仅在目标仍是本次写入内容时恢复操作前状态，避免覆盖后续外部改动。
- 首次汉化保存 `STM32CubeMX.l4j.ini.before-zh-agent`。
- 日志写入 `%LOCALAPPDATA%\STM32CubeMX-Chinese-Patcher\logs\app.log`，不记录密码、Token 或密钥。

汉化覆盖 Swing/AWT 界面；CubeMX 内嵌浏览器页面可能仍显示英文。软件升级后的新增英文文案也可能需要补充词典。

## 开发与发布

环境要求：Windows 10/11、.NET 10 SDK。

```powershell
dotnet test .\STM32CubeMX.ChinesePatcher.slnx -c Debug
.\build.ps1
```

发布产物位于 `artifacts\publish\win-x64\STM32CubeMX-Chinese-Patcher.exe`。该文件为自包含单文件应用，目标电脑无需预装 .NET 运行库。

## 项目结构

- `src\STM32CubeMX.ChinesePatcher.Core`：安装检测、进程检测、状态识别、补丁事务。
- `src\STM32CubeMX.ChinesePatcher`：WPF 界面、内置载荷、UAC 协调和日志。
- `tests\STM32CubeMX.ChinesePatcher.Tests`：核心服务单元测试。

STM32CubeMX 是 STMicroelectronics 的注册商标。本项目不是 STMicroelectronics 官方产品。
