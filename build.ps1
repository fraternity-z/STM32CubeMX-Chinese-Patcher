[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$ReleaseVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$solution = Join-Path $projectRoot 'STM32CubeMX.ChinesePatcher.slnx'
$appProject = Join-Path $projectRoot 'src\STM32CubeMX.ChinesePatcher\STM32CubeMX.ChinesePatcher.csproj'
$publishRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\publish\$Runtime"))

if (-not $publishRoot.StartsWith($projectRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录超出项目范围：$publishRoot"
}

dotnet test $solution -c Debug --collect:'XPlat Code Coverage' --results-directory (Join-Path $projectRoot 'artifacts\TestResults')
if ($LASTEXITCODE -ne 0) {
    throw "测试失败，退出码：$LASTEXITCODE"
}

if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

$publishArguments = @(
    $appProject
    '-c'
    'Release'
    '-r'
    $Runtime
    '--self-contained'
    'true'
    '-o'
    $publishRoot
)
if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) {
    $publishArguments += "-p:Version=$ReleaseVersion"
}

dotnet publish @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "发布失败，退出码：$LASTEXITCODE"
}

$executable = Join-Path $publishRoot 'STM32CubeMX-Chinese-Patcher.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "发布产物缺失：$executable"
}

$extraFiles = @(Get-ChildItem -LiteralPath $publishRoot -File | Where-Object Name -ne 'STM32CubeMX-Chinese-Patcher.exe')
if ($extraFiles.Count -gt 0) {
    throw "单文件发布目录包含额外文件：$($extraFiles.Name -join ', ')"
}

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $executable
Write-Host "发布完成：$executable"
Write-Host "SHA-256：$($hash.Hash)"
