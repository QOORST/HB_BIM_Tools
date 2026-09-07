# HB_BIM Tools manual deployment script.
# Run from a deployment package that contains version folders such as 2022, 2024, 2025, and 2026.

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

$appName = "HB_BIM Tools"
$versions = @("2022", "2024", "2025", "2026")
$scriptDir = $PSScriptRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "$appName - Manual Deploy" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdmin) {
    Write-Host "[ERROR] Please run PowerShell as Administrator." -ForegroundColor Red
    pause
    exit 1
}

$installedVersions = foreach ($version in $versions) {
    $addinRoot = "C:\ProgramData\Autodesk\Revit\Addins\$version"
    if (Test-Path $addinRoot) {
        $version
    }
}

if ($installedVersions.Count -eq 0) {
    Write-Host "[ERROR] No supported Revit Addins folders were found." -ForegroundColor Red
    Write-Host "Supported versions: $($versions -join ', ')" -ForegroundColor Yellow
    pause
    exit 1
}

Write-Host "Detected Revit versions: $($installedVersions -join ', ')" -ForegroundColor Green
Write-Host "Enter versions separated by commas, or press Enter for all detected versions." -ForegroundColor Yellow
$selection = Read-Host "Versions"

if ([string]::IsNullOrWhiteSpace($selection) -or $selection.Trim().ToLowerInvariant() -eq "all") {
    $targetVersions = @($installedVersions)
} else {
    $targetVersions = @(
        $selection -split "," |
            ForEach-Object { $_.Trim() } |
            Where-Object { $installedVersions -contains $_ }
    )
}

if ($targetVersions.Count -eq 0) {
    Write-Host "[ERROR] No valid Revit versions were selected." -ForegroundColor Red
    pause
    exit 1
}

$successCount = 0
$failCount = 0

foreach ($version in $targetVersions) {
    Write-Host ""
    Write-Host "Deploying to Revit $version..." -ForegroundColor Cyan

    $sourceDir = Join-Path $scriptDir $version
    $addinRoot = "C:\ProgramData\Autodesk\Revit\Addins\$version"
    $targetDir = Join-Path $addinRoot "HB_BIM"
    $addinFile = Join-Path $addinRoot "HB_BIM_Tools.addin"
    $legacyAddinFile = Join-Path $addinRoot "YD_RevitTools.LicenseManager.addin"

    if (-not (Test-Path $sourceDir)) {
        Write-Host "  [ERROR] Missing source folder: $sourceDir" -ForegroundColor Red
        $failCount++
        continue
    }

    try {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceDir "*") -Destination $targetDir -Recurse -Force

        if (Test-Path $legacyAddinFile) {
            Remove-Item -LiteralPath $legacyAddinFile -Force
        }

        $addinContent = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>HB_BIM Tools</Name>
    <Assembly>$targetDir\YD_RevitTools.LicenseManager.dll</Assembly>
    <FullClassName>YD_RevitTools.LicenseManager.App</FullClassName>
    <ClientId>B3F5D2D4-9392-4A9E-9C0D-A6F5DD93FAC7</ClientId>
    <VendorId>LAN</VendorId>
    <VendorDescription>LAN, HB_BIM Tools, www.ydbim.com</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

        $addinContent | Out-File -FilePath $addinFile -Encoding UTF8 -Force

        $mainDll = Join-Path $targetDir "YD_RevitTools.LicenseManager.dll"
        if (-not (Test-Path $mainDll)) {
            throw "Main DLL was not copied: $mainDll"
        }

        Write-Host "  OK: $addinFile" -ForegroundColor Green
        $successCount++
    } catch {
        Write-Host "  [ERROR] $($_.Exception.Message)" -ForegroundColor Red
        $failCount++
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deploy complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Succeeded: $successCount" -ForegroundColor Green
if ($failCount -gt 0) {
    Write-Host "Failed: $failCount" -ForegroundColor Red
}
Write-Host "Restart Revit to load the add-in." -ForegroundColor Yellow
pause
