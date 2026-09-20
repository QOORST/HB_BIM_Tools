$ErrorActionPreference = 'Stop'
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit before deployment.' }
$sourceRoot = 'C:\HB\_BIM\HB_BIM_Tools\bin\Release2024'
$installRoot = 'C:\ProgramData\Autodesk\Revit\Addins\2024\HB_BIM'
$relativeFiles = @('YD_RevitTools.LicenseManager.dll', 'YD_RevitTools.LicenseManager.pdb')
foreach ($name in @('manual_offset','mep_position_dimension','mep_position_settings','mep_from_connector','mep_level_rebase','tag_align','tag_related')) {
    foreach ($size in 16,32) { $relativeFiles += "Resources\Icons\${name}_${size}.png" }
}
$backupRoot = Join-Path $PSScriptRoot ('backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$entries = @()
foreach ($relative in $relativeFiles) {
    $source = Join-Path $sourceRoot $relative
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing source: $source" }
    $destination = Join-Path $installRoot $relative
    $entries += [pscustomobject]@{Source=$source; Destination=$destination; Backup=(Join-Path $backupRoot $relative); Existed=(Test-Path -LiteralPath $destination -PathType Leaf); SHA256=(Get-FileHash -LiteralPath $source).Hash}
}
New-Item -ItemType Directory -Path $backupRoot | Out-Null
foreach ($entry in $entries) {
    if ($entry.Existed) {
        New-Item -ItemType Directory -Path (Split-Path $entry.Backup -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $entry.Destination -Destination $entry.Backup
    }
}
$entries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupRoot 'manifest.json') -Encoding UTF8
$changed = @()
try {
    if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Revit restarted before deployment.' }
    foreach ($entry in $entries) {
        if ($entry.Existed -and (Get-FileHash -LiteralPath $entry.Destination).Hash -eq $entry.SHA256) { continue }
        $changed += $entry
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force
        if ((Get-FileHash -LiteralPath $entry.Destination).Hash -ne $entry.SHA256) { throw "Hash mismatch: $($entry.Destination)" }
    }
    $result = [pscustomobject]@{Status='Success'; Backup=$backupRoot; Files=$entries; Completed=(Get-Date -Format o)}
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'result.json') -Encoding UTF8
    Write-Output "Deployment verified: $($entries.Count) files. Backup: $backupRoot"
} catch {
    foreach ($entry in $changed) {
        if ($entry.Existed) { Copy-Item -LiteralPath $entry.Backup -Destination $entry.Destination -Force }
        elseif (Test-Path -LiteralPath $entry.Destination -PathType Leaf) { Remove-Item -LiteralPath $entry.Destination }
    }
    throw
}
