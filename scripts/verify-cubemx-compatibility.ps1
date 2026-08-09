[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CubeMxRoot,

    [string]$ExecutablePath,

    [ValidateRange(10, 120)]
    [int]$TimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$cubeMxRootPath = [IO.Path]::GetFullPath($CubeMxRoot)
if (-not $cubeMxRootPath.StartsWith(
        $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "兼容性验证仅允许使用项目 artifacts 下的隔离副本：$cubeMxRootPath"
}

function Assert-NoReparsePointInPath {
    param(
        [Parameter(Mandatory)]
        [string]$BasePath,

        [Parameter(Mandatory)]
        [string]$TargetPath
    )

    $currentPath = $BasePath
    foreach ($segment in [IO.Path]::GetRelativePath($BasePath, $TargetPath).Split(
            [IO.Path]::DirectorySeparatorChar)) {
        if ([string]::IsNullOrEmpty($segment)) {
            continue
        }

        $currentPath = Join-Path $currentPath $segment
        if (-not (Test-Path -LiteralPath $currentPath)) {
            throw "隔离副本路径不存在：$currentPath"
        }

        $item = Get-Item -LiteralPath $currentPath -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "兼容性验证拒绝使用包含重解析点的路径：$currentPath"
        }
    }
}

if (-not (Test-Path -LiteralPath $artifactsRoot -PathType Container)) {
    throw "项目 artifacts 目录不存在：$artifactsRoot"
}
$artifactsItem = Get-Item -LiteralPath $artifactsRoot -Force
if (($artifactsItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "兼容性验证拒绝使用作为重解析点的 artifacts 目录：$artifactsRoot"
}
Assert-NoReparsePointInPath -BasePath $artifactsRoot -TargetPath $cubeMxRootPath

$patcherExecutable = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    Join-Path $projectRoot 'src\STM32CubeMX.ChinesePatcher\bin\Debug\net10.0-windows\win-x64\STM32CubeMX-Chinese-Patcher.exe'
}
else {
    [IO.Path]::GetFullPath($ExecutablePath)
}
$cubeMxExecutable = Join-Path $cubeMxRootPath 'STM32CubeMX.exe'
$iniPath = Join-Path $cubeMxRootPath 'STM32CubeMX.l4j.ini'
$javaExecutable = Join-Path $cubeMxRootPath 'jre\bin\java.exe'
foreach ($requiredFile in @($patcherExecutable, $cubeMxExecutable, $iniPath, $javaExecutable)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "验证所需文件缺失：$requiredFile"
    }
}

$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($cubeMxExecutable)
$productVersion = if ([string]::IsNullOrWhiteSpace($versionInfo.ProductVersion)) {
    $versionInfo.FileVersion
}
else {
    $versionInfo.ProductVersion
}
if ([string]::IsNullOrWhiteSpace($productVersion)) {
    throw "无法读取 STM32CubeMX 版本：$cubeMxExecutable"
}
$cubeMxVersion = $productVersion.Trim().TrimStart('>')
$javaReleasePath = Join-Path $cubeMxRootPath 'jre\release'
$javaVersionLine = Get-Content -LiteralPath $javaReleasePath |
    Where-Object { $_.StartsWith('JAVA_VERSION=', [StringComparison]::Ordinal) } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($javaVersionLine)) {
    throw "无法从 JRE release 文件读取 Java 版本：$javaReleasePath"
}
$javaVersion = $javaVersionLine.Substring('JAVA_VERSION='.Length).Trim().Trim('"')
$resultDirectory = Join-Path `
    ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
    'STM32CubeMX-Chinese-Patcher\results'
$smokeRoot = Join-Path $cubeMxRootPath '.zh-compatibility-smoke'
$userHome = Join-Path $smokeRoot 'user-home'
$commandScript = Join-Path $smokeRoot 'exit.script'
$javaOutput = Join-Path $smokeRoot 'java.stdout.log'
$javaError = Join-Path $smokeRoot 'java.stderr.log'
$originalIni = [IO.File]::ReadAllBytes($iniPath)
$applied = $false

function Invoke-Worker {
    param(
        [Parameter(Mandatory)]
        [ValidateSet(0, 1)]
        [int]$Operation
    )

    $requestId = [Guid]::NewGuid()
    $request = [ordered]@{
        requestId = $requestId
        operation = $Operation
        rootPath = $cubeMxRootPath
        version = $cubeMxVersion
        javaVersion = $javaVersion
    }
    $encoded = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes(($request | ConvertTo-Json -Compress)))
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $patcherExecutable
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    [void]$startInfo.ArgumentList.Add('--elevated-worker')
    [void]$startInfo.ArgumentList.Add($encoded)
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw '无法启动兼容性验证工作进程。'
    }
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "兼容性验证工作进程超过 ${TimeoutSeconds} 秒仍未退出，已终止。"
    }
    $resultPath = Join-Path $resultDirectory ($requestId.ToString('N') + '.json')
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw "工作进程未生成结果，退出码：$($process.ExitCode)"
    }

    try {
        $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        if (-not $result.succeeded) {
            throw "工作进程失败：$($result.message)"
        }

        return $result
    }
    finally {
        Remove-Item -LiteralPath $resultPath -Force
    }
}

function Get-IsolatedCubeMxProcesses {
    @(
        Get-Process | ForEach-Object {
            try {
                $processPath = $_.MainModule.FileName
                if ($processPath -and $processPath.StartsWith(
                        $cubeMxRootPath + [IO.Path]::DirectorySeparatorChar,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    $_
                }
            }
            catch {
            }
        }
    )
}

function Stop-IsolatedCubeMxProcesses {
    $runningProcesses = @(Get-IsolatedCubeMxProcesses)
    if ($runningProcesses.Count -eq 0) {
        return
    }

    $processIds = @($runningProcesses | Select-Object -ExpandProperty Id)
    $runningProcesses | Stop-Process -Force
    Wait-Process -Id $processIds -Timeout 5 -ErrorAction SilentlyContinue
}

function Wait-ForIsolatedCubeMxExit {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $runningProcesses = @(Get-IsolatedCubeMxProcesses)
        if ($runningProcesses.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 200
    }
    while ([DateTime]::UtcNow -lt $deadline)

    $processIds = @($runningProcesses | Select-Object -ExpandProperty Id)
    Stop-IsolatedCubeMxProcesses
    throw "STM32CubeMX 隔离目录进程超过 ${TimeoutSeconds} 秒仍未退出，已终止测试进程：$($processIds -join ', ')"
}

try {
    New-Item -ItemType Directory -Path $userHome -Force | Out-Null
    [IO.File]::WriteAllText($commandScript, "exit$([Environment]::NewLine)", [Text.UTF8Encoding]::new($false))
    [IO.File]::AppendAllText(
        $iniPath,
        "-Duser.home=$userHome$([Environment]::NewLine)",
        [Text.UTF8Encoding]::new($false))
    $smokeBaselineIni = [IO.File]::ReadAllBytes($iniPath)

    $applyResult = Invoke-Worker -Operation 0
    $applied = $true
    $agentPath = Join-Path $cubeMxRootPath 'localization\zh-CN\stm32cubemx-zh-agent.jar'
    $dictionaryPath = Join-Path $cubeMxRootPath 'localization\zh-CN\translations.tsv'

    & $javaExecutable `
        "-Duser.home=$userHome" `
        "-javaagent:$agentPath=$dictionaryPath" `
        '-version' `
        1> $javaOutput `
        2> $javaError
    $javaExitCode = $LASTEXITCODE
    if ($javaExitCode -ne 0) {
        throw "Java Agent 预加载失败，退出码：$javaExitCode；日志：$javaError"
    }

    $cubeMxStartInfo = [Diagnostics.ProcessStartInfo]::new()
    $cubeMxStartInfo.FileName = $cubeMxExecutable
    $cubeMxStartInfo.WorkingDirectory = $cubeMxRootPath
    $cubeMxStartInfo.UseShellExecute = $false
    $cubeMxStartInfo.CreateNoWindow = $true
    [void]$cubeMxStartInfo.ArgumentList.Add('-q')
    [void]$cubeMxStartInfo.ArgumentList.Add($commandScript)
    $cubeMxProcess = [Diagnostics.Process]::Start($cubeMxStartInfo)
    if ($null -eq $cubeMxProcess) {
        throw '无法启动 STM32CubeMX 隔离副本。'
    }
    if (-not $cubeMxProcess.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $cubeMxProcess.Id -Force
        throw "STM32CubeMX 隔离启动超过 ${TimeoutSeconds} 秒，已终止测试进程。"
    }
    if ($cubeMxProcess.ExitCode -ne 0) {
        throw "STM32CubeMX 隔离启动失败，退出码：$($cubeMxProcess.ExitCode)"
    }
    Wait-ForIsolatedCubeMxExit

    $rollbackResult = Invoke-Worker -Operation 1
    $applied = $false
    $baselineHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($smokeBaselineIni))
    $roundTripHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($iniPath)))
    $roundTripMatches = $roundTripHash -ceq $baselineHash
    if (-not $roundTripMatches) {
        throw 'Apply/回退后的 INI 未恢复到测试基线。'
    }

    [pscustomobject]@{
        CubeMxVersion = $cubeMxVersion
        JavaVersion = $javaVersion
        Apply = $applyResult.message
        JavaAgentExitCode = $javaExitCode
        CubeMxExitCode = $cubeMxProcess.ExitCode
        Rollback = $rollbackResult.message
        IniRoundTripMatches = $roundTripMatches
        AgentSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $agentPath).Hash
        DictionarySha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $dictionaryPath).Hash
    }
}
finally {
    Stop-IsolatedCubeMxProcesses
    if ($applied) {
        try {
            Invoke-Worker -Operation 1 | Out-Null
        }
        catch {
            Write-Warning "自动回退隔离副本失败：$($_.Exception.Message)"
        }
    }

    [IO.File]::WriteAllBytes($iniPath, $originalIni)
}
