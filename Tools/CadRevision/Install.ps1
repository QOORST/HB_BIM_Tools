$ErrorActionPreference = 'Stop'
$bundleSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/cad-revision/HB.CadRevision.bundle'))
$appsRoot = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk/ApplicationPlugins'
$targetBundle = Join-Path $appsRoot 'HB.CadRevision.bundle'
if (!(Test-Path -LiteralPath (Join-Path $bundleSource 'Contents/Win64/HB.CadRevision.dll'))) { throw '請先編譯 bundle。' }
if (Test-Path -LiteralPath $targetBundle) { throw '已有 HB.CadRevision.bundle；請先確認既有版本，避免覆寫。' }
New-Item -ItemType Directory -Path $appsRoot -Force | Out-Null
Copy-Item -LiteralPath $bundleSource -Destination $targetBundle -Recurse
Get-FileHash -LiteralPath (Join-Path $targetBundle 'Contents/Win64/HB.CadRevision.dll')
Write-Output '安裝完成。下次開啟 AutoCAD 2024 後執行 HBCADCOMPARE；不變更安全性或信任設定。'
