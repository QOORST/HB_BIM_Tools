param(
    [string]$PipesCsv,
    [string]$ReviewJson,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'PipeToISO.Tests.csproj'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $OutputDirectory = Join-Path $repoRoot ('artifacts\pipeiso-offline-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$testArgs = @('run', '--project', $project, '--no-restore', '--', '--output', $OutputDirectory)
if ($PipesCsv) {
    if (!(Test-Path -LiteralPath $PipesCsv -PathType Leaf)) { throw "找不到逐管資料：$PipesCsv" }
    $testArgs += @('--pipes', (Resolve-Path -LiteralPath $PipesCsv).Path)
}
if ($ReviewJson) {
    if (!(Test-Path -LiteralPath $ReviewJson -PathType Leaf)) { throw "找不到重播資料：$ReviewJson" }
    $testArgs += @('--review-json', (Resolve-Path -LiteralPath $ReviewJson).Path)
}
& dotnet @testArgs
if ($LASTEXITCODE -ne 0) { throw "離線測試失敗，請查看上述案例與原因。" }
Write-Output "離線測試完成；結果與 SVG 預覽：$OutputDirectory"
Write-Output '本指令不操作 Revit、不部署 DLL，也不覆寫模型或來源資料。'
