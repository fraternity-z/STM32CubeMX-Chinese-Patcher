[CmdletBinding()]
param(
    [string]$ExecutablePath,

    [ValidateRange(10, 120)]
    [int]$TimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\integration-fixture'))
$fixtureParent = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
if (-not $fixture.StartsWith($fixtureParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "测试目录超出项目 artifacts：$fixture"
}

$executable = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    Join-Path $projectRoot 'src\STM32CubeMX.ChinesePatcher\bin\Debug\net10.0-windows\win-x64\STM32CubeMX-Chinese-Patcher.exe'
}
else {
    [IO.Path]::GetFullPath($ExecutablePath)
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "调试版程序不存在，请先运行 dotnet build：$executable"
}

$resultDirectory = Join-Path `
    ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
    'STM32CubeMX-Chinese-Patcher\results'

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
        rootPath = $fixture
        version = '6.18.1-RC2'
        javaVersion = '21.0.10'
    }
    $json = $request | ConvertTo-Json -Compress
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executable
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    [void]$startInfo.ArgumentList.Add('--elevated-worker')
    [void]$startInfo.ArgumentList.Add($encoded)
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw '无法启动集成验证工作进程。'
    }
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "集成验证工作进程超过 ${TimeoutSeconds} 秒仍未退出，已终止。"
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

try {
    if (Test-Path -LiteralPath $fixture) {
        $fixtureItem = Get-Item -LiteralPath $fixture -Force
        if (($fixtureItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "拒绝清理作为重解析点的测试目录：$fixture"
        }
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fixture | Out-Null
    [IO.File]::WriteAllBytes(
        (Join-Path $fixture 'STM32CubeMX.exe'),
        [byte[]](0x4D, 0x5A))
    [IO.File]::WriteAllLines(
        (Join-Path $fixture 'STM32CubeMX.l4j.ini'),
        [string[]]@('-Dfile.encoding=UTF8', '-Xmx1024m'),
        [Text.UTF8Encoding]::new($false))

    $applyResult = Invoke-Worker -Operation 0
    $agentPath = Join-Path $fixture 'localization\zh-CN\stm32cubemx-zh-agent.jar'
    $dictionaryPath = Join-Path $fixture 'localization\zh-CN\translations.tsv'
    $iniPath = Join-Path $fixture 'STM32CubeMX.l4j.ini'
    $agentHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $agentPath).Hash
    $dictionaryHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $dictionaryPath).Hash
    $managedBefore = @(
        [IO.File]::ReadAllLines($iniPath) |
            Where-Object {
                $_.StartsWith('-javaagent:', [StringComparison]::OrdinalIgnoreCase) -and
                $_.Contains($agentPath, [StringComparison]::OrdinalIgnoreCase)
            }
    ).Count

    $foreignLine = '-javaagent:C:\Other\other-agent.jar=C:\Other\other.tsv'
    [IO.File]::AppendAllText(
        $iniPath,
        $foreignLine + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
    $rollbackResult = Invoke-Worker -Operation 1

    $linesAfter = [IO.File]::ReadAllLines($iniPath)
    $managedAfter = @(
        $linesAfter |
            Where-Object {
                $_.StartsWith('-javaagent:', [StringComparison]::OrdinalIgnoreCase) -and
                $_.Contains($agentPath, [StringComparison]::OrdinalIgnoreCase)
            }
    ).Count
    $foreignPreserved = $linesAfter -contains $foreignLine

    if ($managedBefore -ne 1 -or $managedAfter -ne 0 -or -not $foreignPreserved) {
        throw '集成验证断言失败。'
    }

    [pscustomobject]@{
        Apply = $applyResult.message
        Rollback = $rollbackResult.message
        ManagedLinesAfterApply = $managedBefore
        ManagedLinesAfterRollback = $managedAfter
        ForeignLinePreserved = $foreignPreserved
        AgentSha256 = $agentHash
        DictionarySha256 = $dictionaryHash
    }
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
