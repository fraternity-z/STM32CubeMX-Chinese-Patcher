# STM32CubeMX 中文汉化补丁（STM32CubeMX Chinese Patcher）

[![GitHub Release](https://img.shields.io/github/v/release/fraternity-z/STM32CubeMX-Chinese-Patcher?display_name=tag&sort=semver)](https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)

STM32CubeMX 中文汉化补丁（STM32CubeMX Chinese Patcher）是一款面向 Windows 的 STM32CubeMX 汉化工具，也可作为 CubeMX 中文补丁管理器使用。它将汉化 Java Agent 和翻译词典嵌入单个 EXE，可自动检测安装目录、CubeMX 版本、JRE 版本、运行状态和当前汉化状态，并提供一键汉化与一键回退。

本项目用于给官方 STM32CubeMX 添加中文界面，不包含或重新分发 STM32CubeMX 本体，也不是 STMicroelectronics 官方提供的 STM32CubeMX 中文版。

[下载最新版本](https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/latest) · [查看全部版本](https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases) · [兼容性](#兼容性) · [使用方法](#使用方法)

## 功能特点

- 自动检测 STM32CubeMX 安装目录，也支持手动选择。
- 检测 CubeMX、内置 JRE、运行状态和汉化状态，避免在不兼容或运行中的环境上修改文件。
- 一键写入内置汉化载荷，一键移除本工具管理的启动项。
- 仅在安装目录需要写入权限时请求 Windows UAC 授权。
- 根据受管启动项、状态文件和载荷哈希识别汉化归属，检测到冲突时拒绝覆盖。
- 写入前检查并发修改，失败时按当前文件状态执行有条件回滚。
- GitHub Release 提供自包含的单文件 EXE 和 `SHA256SUMS.txt`，目标电脑无需预装 .NET 运行库。
- 启动后在后台静默检查 GitHub Release；发现新版本时可查看更新说明、校验信息并直接下载运行。
- 标题栏“关于”菜单提供手动检查更新入口，显示检查进度、最新版本与更新说明，网络或服务器异常时可重试。

## 兼容性

| 项目 | 当前要求 |
| --- | --- |
| 操作系统 | Windows 10 / 11 |
| 发布架构 | Windows x64（`win-x64`） |
| STM32CubeMX | `6.16.0-RC4`、`6.17.0`、`6.18.0`、`6.18.1-RC2` |
| CubeMX 内置 JRE | JRE 21 或更高版本 |

当前版本只允许对表中经过验证的 CubeMX 版本执行汉化，版本字符串必须精确匹配。例如，`6.18.1` 不等同于 `6.18.1-RC2`。检测到未验证版本或不兼容 JRE 时，工具会禁用汉化并显示原因；已有汉化仍可回退。

## 下载与校验

1. 打开 [Releases](https://github.com/fraternity-z/STM32CubeMX-Chinese-Patcher/releases/latest)。
2. 下载 `STM32CubeMX-Chinese-Patcher-vX.Y.Z-win-x64.exe` 和同一 Release 中的 `SHA256SUMS.txt`。
3. 建议在 PowerShell 中核对 SHA-256。将下面的 `vX.Y.Z` 替换为实际版本号：

```powershell
$exe = '.\STM32CubeMX-Chinese-Patcher-vX.Y.Z-win-x64.exe'
$expected = (Get-Content -LiteralPath '.\SHA256SUMS.txt').Split()[0]
$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash.ToLowerInvariant()
$actual -eq $expected
```

输出 `True` 表示下载文件与 Release 提供的校验值一致。请只从本仓库的 Releases 页面下载安装包。

## 使用方法

1. 运行下载的 `STM32CubeMX-Chinese-Patcher-vX.Y.Z-win-x64.exe`。
2. 确认界面显示的安装目录、CubeMX 版本和 JRE 版本正确；自动检测失败时点击“浏览”。
3. 关闭正在运行的 STM32CubeMX。
4. 点击“一键汉化”或“一键回退”。安装目录需要管理员权限时，Windows 会显示 UAC 确认。

回退仅移除本工具管理的 `-javaagent:` 启动项，不会用旧备份覆盖整个 `STM32CubeMX.l4j.ini`。汉化载荷和首次配置备份会保留，便于检查和再次启用。

操作日志位于 `%LOCALAPPDATA%\STM32CubeMX-Chinese-Patcher\logs\app.log`，可用于定位安装检测、权限或文件冲突问题。

自动更新包下载到 `%LOCALAPPDATA%\STM32CubeMX-Chinese-Patcher\updates`。下载过程可取消，程序会根据同一 Release 中的 `SHA256SUMS.txt` 校验 SHA-256；校验失败的临时文件会自动删除。检查失败时仅记录日志，不影响主界面启动；也可通过标题栏“关于”菜单打开关于窗口后重试。

## 自动检测顺序

1. 用户环境变量 `STM32CubeMX_PATH`
2. 当前用户和本机的 Windows 卸载注册表
3. 常见安装目录
4. 用户手动选择

每个候选目录都必须同时包含 `STM32CubeMX.exe` 和 `STM32CubeMX.l4j.ini`。运行状态会识别主程序及安装目录内置 JRE 的 `java.exe` / `javaw.exe`。

## 文件安全与回退策略

- CubeMX 正在运行或运行状态未知时拒绝修改。
- 同名载荷既不匹配内置内容，又无法通过受管启动项或有效状态文件确认归属时，视为冲突并拒绝覆盖。
- JAR、词典、INI 和状态文件均先写入同目录临时文件，再替换目标文件。
- 提交前校验目标文件未被其他程序改动；检测到并发修改时中止。
- 同一 Windows 会话内，同一 CubeMX 安装同一时间只允许一个汉化或回退操作。
- 多文件提交失败时，仅在目标仍是本次写入内容时恢复操作前状态，避免覆盖后续外部改动。
- 首次汉化会保存 `STM32CubeMX.l4j.ini.before-zh-agent`。

## 已知限制

- 当前汉化主要覆盖 Swing/AWT 界面，CubeMX 内嵌浏览器页面可能仍显示英文。
- CubeMX 升级后新增或变更的界面文案可能需要补充词典。
- 为降低新版界面变化造成启动或翻译异常的风险，未验证版本默认不允许汉化。
- 本项目旨在降低 STM32CubeMX 的上手门槛，仍建议同时熟悉常用英文术语和官方文档。

## 开发与验证

开发环境要求：Windows 10/11、.NET 10 SDK。以下命令均在仓库根目录运行：

```powershell
dotnet test .\STM32CubeMX.ChinesePatcher.slnx -c Debug
dotnet build .\src\STM32CubeMX.ChinesePatcher\STM32CubeMX.ChinesePatcher.csproj -c Debug
.\scripts\verify-integration.ps1
.\build.ps1
```

`build.ps1` 会运行测试、生成自包含单文件应用，并输出 EXE 的 SHA-256。发布产物位于：

```text
artifacts\publish\win-x64\STM32CubeMX-Chinese-Patcher.exe
```

升级 CubeMX 后，可先将完整安装目录复制到项目 `artifacts`，再对隔离副本运行兼容性验证：

```powershell
.\scripts\verify-cubemx-compatibility.ps1 -CubeMxRoot .\artifacts\rc2-smoke\STM32CubeMX
```

兼容性脚本会启动并修改指定的 CubeMX 副本。请勿将日常使用的安装目录作为 `CubeMxRoot`。

## 发布流程

1. 确认默认分支已包含待发布代码，并准备好发布说明。
2. 在 GitHub 的 Actions 页面选择 `Release`，点击 `Run workflow`。
3. 输入稳定语义化版本标签，例如 `v1.0.1`。

工作流会验证分支和标签格式，将标签版本写入发布产物，运行测试并构建 `win-x64` 单文件程序，然后创建或更新对应的 GitHub Release，上传带版本号的 EXE 与 `SHA256SUMS.txt`。

## 项目结构

- `src\STM32CubeMX.ChinesePatcher.Core`：安装检测、进程检测、状态识别和补丁事务。
- `src\STM32CubeMX.ChinesePatcher`：WPF 界面、内置载荷、UAC 协调和日志。
- `tests\STM32CubeMX.ChinesePatcher.Tests`：核心服务和界面逻辑测试。
- `scripts`：集成验证与 CubeMX 兼容性验证脚本。

STM32CubeMX 是 STMicroelectronics 的注册商标。本项目与 STMicroelectronics 无隶属或背书关系。
