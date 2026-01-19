# Complete Update Script
# Builds, prepares deployment, and creates installer

param(
    [Parameter(Mandatory=$false)]
    [switch]$SkipBuild,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipDeployment,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipInstaller
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "YD_BIM Tools - Complete Update" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$scriptDir = $PSScriptRoot
$projectRoot = Split-Path $scriptDir -Parent

# Step 1: Build
if (-not $SkipBuild) {
    Write-Host "Step 1: Building project..." -ForegroundColor Yellow
    Write-Host ""
    
    $buildScript = Join-Path $scriptDir "Build.ps1"
    if (Test-Path $buildScript) {
        & $buildScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] Build failed!" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "[ERROR] Build script not found: $buildScript" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
} else {
    Write-Host "[SKIP] Build step skipped" -ForegroundColor Yellow
    Write-Host ""
}

# Step 2: Prepare Deployment Package
if (-not $SkipDeployment) {
    Write-Host "Step 2: Preparing deployment package..." -ForegroundColor Yellow
    Write-Host ""
    
    $deployScript = Join-Path $scriptDir "PrepareDeployment.ps1"
    if (Test-Path $deployScript) {
        & $deployScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] Deployment preparation failed!" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "[ERROR] Deployment script not found: $deployScript" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
} else {
    Write-Host "[SKIP] Deployment preparation skipped" -ForegroundColor Yellow
    Write-Host ""
}

# Step 3: Prepare Installer Files
if (-not $SkipInstaller) {
    Write-Host "Step 3: Preparing installer files..." -ForegroundColor Yellow
    Write-Host ""
    
    $installerDir = Join-Path $projectRoot "Installer"
    $prepareScript = Join-Path $installerDir "Prepare_Files_Simple.ps1"
    if (Test-Path $prepareScript) {
        & $prepareScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] Installer file preparation failed!" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "[ERROR] Installer preparation script not found: $prepareScript" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    
    # Step 4: Build Installer
    Write-Host "Step 4: Building installer..." -ForegroundColor Yellow
    Write-Host ""
    
    $buildInstallerScript = Join-Path $installerDir "Build_Installer.ps1"
    if (Test-Path $buildInstallerScript) {
        & $buildInstallerScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] Installer build failed!" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "[ERROR] Installer build script not found: $buildInstallerScript" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
} else {
    Write-Host "[SKIP] Installer preparation and build skipped" -ForegroundColor Yellow
    Write-Host ""
}

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Update Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$outputDir = Join-Path $projectRoot "Output"

Write-Host "Output files:" -ForegroundColor White
Write-Host ""

# Check deployment package
$deploymentDir = Join-Path $outputDir "Deployment"
if (Test-Path $deploymentDir) {
    Write-Host "Deployment Package:" -ForegroundColor Cyan
    Write-Host "  Location: $deploymentDir" -ForegroundColor Gray
    $deployFiles = Get-ChildItem $deploymentDir -Recurse -File
    $deploySize = ($deployFiles | Measure-Object -Property Length -Sum).Sum
    Write-Host "  Files: $($deployFiles.Count)" -ForegroundColor Gray
    Write-Host "  Size: $([math]::Round($deploySize / 1MB, 2)) MB" -ForegroundColor Gray
    Write-Host ""
}

# Check installer
$setupFiles = Get-ChildItem $outputDir -Filter "YD_BIM_Tools_v*_Setup.exe" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
if ($setupFiles.Count -gt 0) {
    $setupFile = $setupFiles[0]
    Write-Host "Installer:" -ForegroundColor Cyan
    Write-Host "  File: $($setupFile.Name)" -ForegroundColor Gray
    Write-Host "  Location: $($setupFile.FullName)" -ForegroundColor Gray
    Write-Host "  Size: $([math]::Round($setupFile.Length / 1MB, 2)) MB" -ForegroundColor Gray
    Write-Host "  Modified: $($setupFile.LastWriteTime)" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Test the installer on a clean machine" -ForegroundColor White
Write-Host "  2. Test the deployment package on another computer" -ForegroundColor White
Write-Host "  3. Distribute to users" -ForegroundColor White
Write-Host ""

