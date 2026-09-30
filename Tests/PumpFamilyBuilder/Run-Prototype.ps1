param([string]$RunName = (Get-Date -Format 'yyyyMMdd-HHmmss'))
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9-]+$') { throw 'Invalid run name.' }
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit before starting an isolated prototype run.' }
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$build = Join-Path $PSScriptRoot 'bin/Release/net48'
$dll = Join-Path $build 'HB.PumpFamilyBuilder.dll'
if (!(Test-Path -LiteralPath $dll)) { throw 'Build the project successfully before running.' }
$dllTime = (Get-Item -LiteralPath $dll).LastWriteTimeUtc
if (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Where-Object LastWriteTimeUtc -GT $dllTime) {
    throw 'Source is newer than the DLL. Rebuild successfully before running.'
}
$run = Join-Path $workspace "artifacts/PumpFamilyBuilder/$RunName"
if (Test-Path -LiteralPath $run) { throw 'Run folder already exists.' }
$runtime = Join-Path $run 'runtime'
$output = Join-Path $run 'output'
New-Item -ItemType Directory -Path $runtime,$output -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination $runtime
Copy-Item -LiteralPath (Join-Path $build 'pump-sample-001.input.json') -Destination $runtime
[IO.File]::WriteAllText((Join-Path $runtime 'run.request.txt'), $output)
$addins = Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2024'
New-Item -ItemType Directory -Path $addins -Force | Out-Null
$manifest = Join-Path $addins "HB-PumpPrototype-$RunName.addin"
if (Test-Path -LiteralPath $manifest) { throw 'Manifest already exists.' }
$assembly = [Security.SecurityElement]::Escape((Join-Path $runtime 'HB.PumpFamilyBuilder.dll'))
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>HB Pump Prototype</Name><Assembly>$assembly</Assembly>
    <AddInId>A7817991-A23C-41B3-AB92-83AB7CE80A65</AddInId>
    <FullClassName>HB.PumpFamilyBuilder.Startup</FullClassName>
    <VendorId>HBBM</VendorId><VendorDescription>Local concept-family prototype</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
[IO.File]::WriteAllText($manifest, $xml, (New-Object Text.UTF8Encoding($false)))
try {
    $process = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2024\Revit.exe' -WindowStyle Hidden -PassThru
    @{ process_id=$process.Id; manifest=$manifest; output=$output; runtime=$runtime } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding UTF8
    Get-Content -LiteralPath (Join-Path $run 'launch.json')
} catch {
    Remove-Item -LiteralPath $manifest
    throw
}
# Remove only this manifest after success/failure has been observed. Never touch other add-ins.
