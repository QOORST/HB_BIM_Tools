param([string]$RunName = (Get-Date -Format 'yyyyMMdd-HHmmss'), [string]$CandidatePath, [switch]$SeparateSession)
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9-]+$') { throw 'Invalid run name.' }
if ((Get-Process Revit -ErrorAction SilentlyContinue) -and !$SeparateSession) { throw 'Use a separately launched empty Revit session; do not close a user session automatically.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo "artifacts/sleeve-quantity/native-$RunName"
if (Test-Path -LiteralPath $run) { throw 'Run directory already exists.' }
$candidate = Join-Path $repo 'artifacts/sleeve-quantity/native-candidate/HB.SleeveQuantity.Candidate.dll'
if ($CandidatePath) { $candidate = [IO.Path]::GetFullPath($CandidatePath) }
$harness = Join-Path $PSScriptRoot 'bin/Release/net48/HB.SleeveQuantity.Native.dll'
if (!(Test-Path -LiteralPath $candidate) -or !(Test-Path -LiteralPath $harness)) { throw 'Build candidate and native harness first.' }
$runtime = Join-Path $run 'runtime'
$output = Join-Path $run 'output'
New-Item -ItemType Directory -Path $runtime,$output | Out-Null
Copy-Item -LiteralPath $harness -Destination $runtime
@{ candidate=$candidate; output=$output } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime 'request.json') -Encoding UTF8
$addins = Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2024'
$manifest = Join-Path $addins "HB-SleeveQuantity-QA-$RunName.addin"
if (Test-Path -LiteralPath $manifest) { throw 'Manifest already exists.' }
$assembly = [Security.SecurityElement]::Escape((Join-Path $runtime 'HB.SleeveQuantity.Native.dll'))
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>HB Sleeve Quantity QA</Name><Assembly>$assembly</Assembly><AddInId>58E1B9A7-462B-4E43-974C-D09349BC87AA</AddInId><FullClassName>HB.SleeveQuantity.Native.Startup</FullClassName><VendorId>HBBM</VendorId><VendorDescription>One-shot sleeve quantity native tests</VendorDescription></AddIn></RevitAddIns>
"@
[IO.File]::WriteAllText($manifest, $xml, (New-Object Text.UTF8Encoding($false)))
try {
    $process = Start-Process -FilePath 'C:/Program Files/Autodesk/Revit 2024/Revit.exe' -WindowStyle Hidden -PassThru
    @{ process_id=$process.Id; manifest=$manifest; runtime=$runtime; output=$output; candidate_sha256=(Get-FileHash -LiteralPath $candidate).Hash } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'launch.json') -Encoding UTF8
    Get-Content -LiteralPath (Join-Path $run 'launch.json')
} catch { Remove-Item -LiteralPath $manifest; throw }
# Remove only the exact manifest recorded in launch.json after the result or failure is observed.
