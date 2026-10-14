param([string]$RunName = (Get-Date -Format 'yyyyMMdd-HHmmss'), [string]$SeedPath)
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9-]+$') { throw 'Invalid run name.' }
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Use an empty Revit session. Existing user sessions will not be closed.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo "artifacts/smart-legend/native-$RunName"
if (Test-Path -LiteralPath $run) { throw 'Run already exists.' }
$build = Join-Path $PSScriptRoot 'bin/Release/net48'
New-Item -ItemType Directory -Path $run | Out-Null
Copy-Item -Path (Join-Path $build '*.dll') -Destination $run
if ($SeedPath) { Copy-Item -LiteralPath $SeedPath -Destination (Join-Path $run 'seed.rvt') }
$manifest = Join-Path $env:APPDATA "Autodesk/Revit/Addins/2024/HB-SmartLegend-QA-$RunName.addin"
if (Test-Path -LiteralPath $manifest) { throw 'Manifest already exists.' }
$assembly = [Security.SecurityElement]::Escape((Join-Path $run 'HB.SmartLegend.Native.dll'))
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>HB Smart Legend QA</Name><Assembly>$assembly</Assembly><AddInId>8CD20B90-A7D7-4E16-82DF-BEDDAF879714</AddInId><FullClassName>HB.SmartLegend.Native.Startup</FullClassName><VendorId>HBBM</VendorId><VendorDescription>Isolated local smart legend tests</VendorDescription></AddIn></RevitAddIns>
"@
[IO.File]::WriteAllText($manifest, $xml, (New-Object Text.UTF8Encoding($false)))
try {
    $process = Start-Process -FilePath 'C:/Program Files/Autodesk/Revit 2024/Revit.exe' -WindowStyle Hidden -PassThru
    @{process_id=$process.Id; manifest=$manifest; output=(Join-Path $run 'output'); harness_sha256=(Get-FileHash -LiteralPath (Join-Path $run 'HB.SmartLegend.Native.dll')).Hash} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding UTF8
    Get-Content -LiteralPath (Join-Path $run 'launch.json')
} catch { Remove-Item -LiteralPath $manifest; throw }
