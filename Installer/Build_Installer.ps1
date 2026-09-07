# Build Installer Script
# Compiles the Inno Setup installer for HB_BIM Tools

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "HB_BIM Tools - Build Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$installerDir = $PSScriptRoot
$projectRoot = Split-Path $installerDir -Parent
$issFile = Join-Path $installerDir "HB_BIM_Setup.iss"
$prepareScript = Join-Path $installerDir "Prepare_Files_Simple.ps1"
$revitApiRoot = Split-Path (Split-Path $projectRoot -Parent) -Parent
$familyLibraryProject = Join-Path $revitApiRoot "Codex\work\family-library-management\addin\CompanyFamilyLibraryMvp.csproj"

function Get-LanCodeSigningCertificate {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq "CN=LAN" } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        $cert = Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq "CN=LAN" } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
    }

    return $cert
}

function Sign-FileWithLanCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        $Certificate
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $signature = Set-AuthenticodeSignature -LiteralPath $Path -Certificate $Certificate
    if ($signature.Status -ne "Valid") {
        throw "Failed to sign file: $Path. Status: $($signature.Status)"
    }

    Write-Host "  Signed: $Path" -ForegroundColor Green
}

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

if (-not (Test-Path $prepareScript)) {
    Write-Host "[ERROR] Prepare script not found: $prepareScript" -ForegroundColor Red
    exit 1
}

Write-Host "Setup script: $issFile" -ForegroundColor Gray
Write-Host ""

# Build optional companion add-ins that are packaged beside the main add-in.
if (Test-Path $familyLibraryProject) {
    Write-Host "Building Company Family Library payload..." -ForegroundColor Yellow
    foreach ($configuration in @("Release2022", "Release2024", "Release2025", "Release2026")) {
        Write-Host "  dotnet build CompanyFamilyLibraryMvp ($configuration)" -ForegroundColor Gray
        $buildOutput = dotnet build $familyLibraryProject --no-restore -c $configuration -p:Platform=x64 -v:minimal
        if ($LASTEXITCODE -ne 0) {
            $buildOutput | Select-Object -Last 20
            Write-Host ""
            Write-Host "[ERROR] Company Family Library build failed: $configuration" -ForegroundColor Red
            exit 1
        }
    }
    Write-Host "[OK] Company Family Library payload built" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[WARNING] Company Family Library project not found. Installer will use any existing packaged DLLs only." -ForegroundColor Yellow
    Write-Host "Path: $familyLibraryProject" -ForegroundColor Gray
    Write-Host ""
}

# Prepare installer payload before compiling
Write-Host "Preparing installer payload..." -ForegroundColor Yellow
Write-Host "" 

& $prepareScript
if (-not $?) {
    Write-Host "" 
    Write-Host "[ERROR] Prepare step failed. Installer build aborted." -ForegroundColor Red
    exit 1
}

Write-Host ""

$signingCertificate = Get-LanCodeSigningCertificate
if ($signingCertificate) {
    Write-Host "Signing installer payload with LAN certificate..." -ForegroundColor Yellow
    foreach ($version in @("2022", "2024", "2025", "2026")) {
        Sign-FileWithLanCertificate -Path (Join-Path $installerDir "$version\YD_RevitTools.LicenseManager.dll") -Certificate $signingCertificate
        Sign-FileWithLanCertificate -Path (Join-Path $installerDir "$version\CompanyFamilyLibraryMvp.dll") -Certificate $signingCertificate
    }
    Write-Host ""
} else {
    Write-Host "[WARNING] LAN code signing certificate not found. Installer payload will not be signed." -ForegroundColor Yellow
    Write-Host ""
}

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
        $setupFiles = Get-ChildItem $outputDir -Filter "HB_BIM_Tools_v*_Setup.exe" | Sort-Object LastWriteTime -Descending
        if ($setupFiles.Count -gt 0) {
            $setupFile = $setupFiles[0]

            if ($signingCertificate) {
                Write-Host "Signing installer..." -ForegroundColor Yellow
                Sign-FileWithLanCertificate -Path $setupFile.FullName -Certificate $signingCertificate
                $setupFile = Get-Item -LiteralPath $setupFile.FullName
                Write-Host ""
            }

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
