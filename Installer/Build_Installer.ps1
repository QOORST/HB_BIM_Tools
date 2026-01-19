# Build Installer Script
# Compiles the Inno Setup installer for YD_BIM Tools

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "YD_BIM Tools - Build Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$installerDir = $PSScriptRoot
$projectRoot = Split-Path $installerDir -Parent
$issFile = Join-Path $installerDir "YD_BIM_Setup.iss"

# Check if Inno Setup is installed
$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 5\ISCC.exe"
)

$iscc = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $iscc = $path
        Write-Host "Found Inno Setup Compiler: $path" -ForegroundColor Green
        break
    }
}

if (-not $iscc) {
    Write-Host "[ERROR] Inno Setup Compiler not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install Inno Setup from:" -ForegroundColor Yellow
    Write-Host "  https://jrsoftware.org/isdl.php" -ForegroundColor White
    Write-Host ""
    exit 1
}

# Check if ISS file exists
if (-not (Test-Path $issFile)) {
    Write-Host "[ERROR] Setup script not found: $issFile" -ForegroundColor Red
    exit 1
}

Write-Host "Setup script: $issFile" -ForegroundColor Gray
Write-Host ""

# Compile the installer
Write-Host "Compiling installer..." -ForegroundColor Yellow
Write-Host ""

$process = Start-Process -FilePath $iscc -ArgumentList "`"$issFile`"" -NoNewWindow -Wait -PassThru

if ($process.ExitCode -eq 0) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Build Successful!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Find the output file
    $outputDir = Join-Path $projectRoot "Output"
    if (Test-Path $outputDir) {
        $setupFiles = Get-ChildItem $outputDir -Filter "YD_BIM_Tools_v*_Setup.exe" | Sort-Object LastWriteTime -Descending
        if ($setupFiles.Count -gt 0) {
            $setupFile = $setupFiles[0]
            $sizeKB = [math]::Round($setupFile.Length / 1KB, 1)
            $sizeMB = [math]::Round($setupFile.Length / 1MB, 2)
            
            Write-Host "Installer created:" -ForegroundColor White
            Write-Host "  File: $($setupFile.Name)" -ForegroundColor Cyan
            Write-Host "  Path: $($setupFile.FullName)" -ForegroundColor Gray
            Write-Host "  Size: $sizeMB MB ($sizeKB KB)" -ForegroundColor Gray
            Write-Host "  Modified: $($setupFile.LastWriteTime)" -ForegroundColor Gray
            Write-Host ""
            
            Write-Host "Next steps:" -ForegroundColor Yellow
            Write-Host "  1. Test the installer on a clean machine" -ForegroundColor White
            Write-Host "  2. Distribute to users" -ForegroundColor White
            Write-Host ""
        }
    }
} else {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Build Failed!" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Exit code: $($process.ExitCode)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please check the error messages above." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

