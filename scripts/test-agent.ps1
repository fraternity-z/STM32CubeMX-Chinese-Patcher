[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$agentJar = Join-Path $projectRoot 'src\STM32CubeMX.ChinesePatcher\Resources\Payload\stm32cubemx-zh-agent.jar'
$dictionary = Join-Path $projectRoot 'src\STM32CubeMX.ChinesePatcher\Resources\Payload\translations.tsv'
$testSourceRoot = Join-Path $projectRoot 'agent\test'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("stm32cubemx-zh-agent-test-" + [Guid]::NewGuid().ToString('N'))
$classesRoot = Join-Path $temporaryRoot 'classes'

try {
    New-Item -ItemType Directory -Path $classesRoot | Out-Null
    $sources = @(Get-ChildItem -LiteralPath $testSourceRoot -Filter '*.java' -File -Recurse)
    if ($sources.Count -eq 0) {
        throw "Agent 测试源文件缺失：$testSourceRoot"
    }

    & javac --release 21 -encoding UTF-8 -classpath $agentJar -d $classesRoot @($sources.FullName)
    if ($LASTEXITCODE -ne 0) {
        throw "Agent 测试编译失败，退出码：$LASTEXITCODE"
    }

    & java -classpath "$agentJar;$classesRoot" com.codex.cubemx.zh.PluginTabCompatibilityTest $dictionary
    if ($LASTEXITCODE -ne 0) {
        throw "Agent 行为测试失败，退出码：$LASTEXITCODE"
    }
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
