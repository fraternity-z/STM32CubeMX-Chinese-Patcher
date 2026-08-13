[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$agentJar = Join-Path $projectRoot 'src\STM32CubeMX.ChinesePatcher\Resources\Payload\stm32cubemx-zh-agent.jar'
$sourceRoot = Join-Path $projectRoot 'agent\src'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("stm32cubemx-zh-agent-" + [Guid]::NewGuid().ToString('N'))
$expandedRoot = Join-Path $temporaryRoot 'expanded'
$updatedJar = Join-Path $temporaryRoot 'stm32cubemx-zh-agent.jar'
$manifestRoot = Join-Path $temporaryRoot 'manifest'
$manifestDirectory = Join-Path $manifestRoot 'META-INF'
$manifest = Join-Path $manifestDirectory 'MANIFEST.MF'

try {
    New-Item -ItemType Directory -Path $expandedRoot | Out-Null
    New-Item -ItemType Directory -Path $manifestDirectory | Out-Null

    Push-Location $expandedRoot
    try {
        & jar --extract --file $agentJar
        if ($LASTEXITCODE -ne 0) {
            throw "Agent 解包失败，退出码：$LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    $sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.java' -File -Recurse)
    if ($sources.Count -eq 0) {
        throw "Agent 源文件缺失：$sourceRoot"
    }

    & javac --release 21 -encoding UTF-8 -classpath $agentJar -d $expandedRoot @($sources.FullName)
    if ($LASTEXITCODE -ne 0) {
        throw "Agent 编译失败，退出码：$LASTEXITCODE"
    }

    @(
        'Manifest-Version: 1.0'
        'Premain-Class: com.codex.cubemx.zh.CubeMxZhCompatibilityAgent'
        'Can-Redefine-Classes: false'
        'Can-Retransform-Classes: false'
        ''
    ) | Set-Content -LiteralPath $manifest -Encoding utf8NoBOM

    $oldManifest = Join-Path $expandedRoot 'META-INF\MANIFEST.MF'
    if (Test-Path -LiteralPath $oldManifest) {
        Remove-Item -LiteralPath $oldManifest -Force
    }

    & jar --create --file $updatedJar --manifest $manifest -C $expandedRoot .
    if ($LASTEXITCODE -ne 0) {
        throw "Agent 打包失败，退出码：$LASTEXITCODE"
    }

    Copy-Item -LiteralPath $updatedJar -Destination $agentJar -Force
    Write-Host "Agent 已更新：$agentJar"
}
finally {
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (
        $resolvedTemporaryRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemporaryRoot)
    ) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
