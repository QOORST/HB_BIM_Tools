param([Parameter(Mandatory=$true)][string]$RequestPath)
$ErrorActionPreference='Stop'
$requestFile=[IO.Path]::GetFullPath($RequestPath)
$request=Get-Content -LiteralPath $requestFile -Raw -Encoding UTF8 | ConvertFrom-Json
if ($request.schema_version -ne 1 -or $request.target_revit -ne 2024 -or $request.template_id -notin @('evergush_tos_ef_05_21','teco_ahu_hs')) { throw 'Unsupported build request.' }
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Revit is running. Save and close it before starting an isolated build.' }
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dll=Join-Path $workspace 'Tests/PumpFamilyBuilder/bin/Release/net48/HB.PumpFamilyBuilder.dll'
if (!(Test-Path -LiteralPath $dll)) { throw 'Build Tests/PumpFamilyBuilder first.' }
if ((Get-Item -LiteralPath (Join-Path $workspace 'Tests/PumpFamilyBuilder/Builder.cs')).LastWriteTimeUtc -gt (Get-Item -LiteralPath $dll).LastWriteTimeUtc) { throw 'Rebuild the changed builder before running.' }
if (Get-ChildItem (Join-Path $workspace 'Tests/PumpFamilyBuilder') -Filter '*.cs' | Where-Object LastWriteTimeUtc -gt (Get-Item -LiteralPath $dll).LastWriteTimeUtc) { throw 'Rebuild the changed equipment builder before running.' }
$packet=Split-Path -Parent $requestFile
$runtime=Join-Path $packet 'runtime'
if ((Test-Path -LiteralPath $runtime) -or (Test-Path -LiteralPath (Join-Path $packet 'output'))) { throw 'This packet was already started. Prepare a new packet for another run.' }
New-Item -ItemType Directory -Path $runtime | Out-Null
Copy-Item -LiteralPath $dll -Destination $runtime
[IO.File]::WriteAllText((Join-Path $runtime 'case.request.txt'),$requestFile)
$name='HB-EquipmentCase-'+[guid]::NewGuid().ToString('N')+'.addin'
$addins=Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2024'
New-Item -ItemType Directory -Path $addins -Force | Out-Null
$manifest=Join-Path $addins $name
$assembly=[Security.SecurityElement]::Escape((Join-Path $runtime 'HB.PumpFamilyBuilder.dll'))
$xml=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>HB Equipment Case</Name><Assembly>$assembly</Assembly><AddInId>A7817991-A23C-41B3-AB92-83AB7CE80A65</AddInId><FullClassName>HB.PumpFamilyBuilder.Startup</FullClassName><VendorId>HBBM</VendorId><VendorDescription>Local equipment-family workflow</VendorDescription></AddIn></RevitAddIns>
"@
[IO.File]::WriteAllText($manifest,$xml,(New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $runtime 'case.manifest.txt'),$manifest)
try {
    $process=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2024\Revit.exe' -WindowStyle Hidden -PassThru
    @{process_id=$process.Id;manifest=$manifest;output=(Join-Path $packet 'output')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packet 'launch.json') -Encoding UTF8
    Write-Output (Join-Path $packet 'output')
} catch { Remove-Item -LiteralPath $manifest; throw }
# The one-shot Revit application removes its own manifest after the build attempt.
