# Build script for YD_RevitTools.LicenseManager
# Builds for Revit 2024 and 2025

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("2024", "2025", "2026", "All")]
    [string]$Version = "All"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "YD_BIM Tools - Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Find MSBuild
$msbuildPaths = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
)

$msbuild = $null
foreach ($path in $msbuildPaths) {
    if (Test-Path $path) {
        $msbuild = $path
        Write-Host "Found MSBuild: $path" -ForegroundColor Green
        break
    }
}

if (-not $msbuild) {
    Write-Host "[ERROR] MSBuild not found!" -ForegroundColor Red
    Write-Host "Please install Visual Studio 2022 or 2019" -ForegroundColor Yellow
    exit 1
}

# Get project directory
$scriptDir = $PSScriptRoot
$projectRoot = Split-Path $scriptDir -Parent
$projectFile = Join-Path $projectRoot "YD_RevitTools.LicenseManager.csproj"

if (-not (Test-Path $projectFile)) {
    Write-Host "[ERROR] Project file not found: $projectFile" -ForegroundColor Red
    exit 1
}

Write-Host "Project file: $projectFile" -ForegroundColor Gray
Write-Host ""

# Determine which versions to build
$versionsToBuild = @()
if ($Version -eq "All") {
    $versionsToBuild = @("2024", "2025")
} else {
    $versionsToBuild = @($Version)
}

# Build each version
$successCount = 0
$failCount = 0

foreach ($ver in $versionsToBuild) {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Building for Revit $ver" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    $config = "Release$ver"
    
    # Build command
    $buildArgs = @(
        "`"$projectFile`"",
        "/p:Configuration=$config",
        "/p:Platform=x64",
        "/t:Rebuild",
        "/v:minimal",
        "/nologo"
    )

    Write-Host "Running MSBuild..." -ForegroundColor Gray
    Write-Host ""

    # Execute build
    $process = Start-Process -FilePath $msbuild -ArgumentList $buildArgs -NoNewWindow -Wait -PassThru
    
    if ($process.ExitCode -eq 0) {
        Write-Host ""
        Write-Host "Build succeeded for Revit $ver" -ForegroundColor Green
        
        # Check output
        $outputDll = Join-Path $projectRoot "bin\$config\YD_RevitTools.LicenseManager.dll"
        if (Test-Path $outputDll) {
            $fileInfo = Get-Item $outputDll
            $sizeKB = [math]::Round($fileInfo.Length / 1KB, 1)
            Write-Host "  Output: $outputDll" -ForegroundColor Gray
            Write-Host "  Size: $sizeKB KB" -ForegroundColor Gray
            Write-Host "  Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
        }
        
        $successCount++
    } else {
        Write-Host ""
        Write-Host "Build failed for Revit $ver (Exit code: $($process.ExitCode))" -ForegroundColor Red
        $failCount++
    }
    
    Write-Host ""
}

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Success: $successCount" -ForegroundColor Green
if ($failCount -gt 0) {
    Write-Host "Failed: $failCount" -ForegroundColor Red
}
Write-Host ""

if ($successCount -gt 0) {
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Test the plugin in Revit" -ForegroundColor White
    Write-Host "  2. Run PrepareDeployment.ps1 to create deployment package" -ForegroundColor White
    Write-Host "  3. Or run Prepare_Files_Simple.ps1 to prepare installer files" -ForegroundColor White
}

Write-Host ""

