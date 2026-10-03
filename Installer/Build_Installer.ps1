# Build Installer Script
[CmdletBinding()]
param([string]$FamilyLibraryProject)
$ErrorActionPreference = 'Stop'
# Compiles the Inno Setup installer for HB_BIM Tools

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "HB_BIM Tools - Build Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$installerDir = $PSScriptRoot
$projectRoot = Split-Path $installerDir -Parent
$issFile = Join-Path $installerDir "HB_BIM_Setup.iss"
$prepareScript = Join-Path $installerDir "Prepare_Files_Simple.ps1"
$publicSigningCertificatePath = Join-Path $installerDir "LAN_CodeSigning.cer"
$revitApiRoot = Split-Path (Split-Path $projectRoot -Parent) -Parent
if ([string]::IsNullOrWhiteSpace($FamilyLibraryProject)) {
    $FamilyLibraryProject = Join-Path $revitApiRoot "Codex\work\family-library-management\addin\CompanyFamilyLibraryMvp.csproj"
}
$mainProject = Join-Path $projectRoot 'YD_RevitTools.LicenseManager.csproj'
$supportedVersions = @('2022', '2024', '2025', '2026')

function Get-LanCodeSigningCertificate {
    if (-not (Test-Path -LiteralPath $publicSigningCertificatePath)) {
        throw "LAN public code-signing certificate not found: $publicSigningCertificatePath"
    }

    $publicCertificate = Get-PfxCertificate -FilePath $publicSigningCertificatePath
    $thumbprint = $publicCertificate.Thumbprint -replace " ", ""
    foreach ($store in @("Cert:\CurrentUser\My", "Cert:\LocalMachine\My")) {
        $certificate = Get-ChildItem -Path $store -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object {
                $_.HasPrivateKey -and
                (($_.Thumbprint -replace " ", "").Equals($thumbprint, [StringComparison]::OrdinalIgnoreCase))
            } |
            Select-Object -First 1
        if ($certificate) {
            return $certificate
        }
    }

    return $null
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

# Check metadata without rewriting versions or implying a published release.
$version = (Get-Content (Join-Path $projectRoot 'version.json') -Raw | ConvertFrom-Json).version
$issText = Get-Content -LiteralPath $issFile -Raw
$issVersion = [regex]::Match($issText, '#define MyAppVersion "([^"\r\n]+)"').Groups[1].Value
$assemblyText = Get-Content (Join-Path $projectRoot 'Properties\AssemblyInfo.cs') -Raw
$assemblyVersion = [regex]::Match($assemblyText, 'AssemblyInformationalVersion\("([^"\r\n]+)"\)').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($version) -or $version -ne $issVersion -or $version -ne $assemblyVersion) {
    throw 'version.json, installer version and AssemblyInformationalVersion must agree.'
}
foreach ($project in @($mainProject, $FamilyLibraryProject)) {
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Required project missing: $project. Existing installed or staged binaries cannot be used."
    }
}
$null = Get-Command dotnet -ErrorAction Stop
foreach ($year in $supportedVersions) {
    foreach ($api in @('RevitAPI.dll', 'RevitAPIUI.dll')) {
        $apiPath = "C:\Program Files\Autodesk\Revit $year\$api"
        if (-not (Test-Path -LiteralPath $apiPath -PathType Leaf)) {
            throw "Required Revit $year SDK reference missing: $apiPath"
        }
    }
}

# An empty, unique output directory plus Rebuild prevents stale main/companion
# binaries from passing as the current build. No use of existing bin or Addins.
$buildRoot = Join-Path ([IO.Path]::GetTempPath()) ("HB_BIM_Installer_" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildRoot -ErrorAction Stop | Out-Null
try {
    $receipt = @()
    foreach ($year in $supportedVersions) {
        foreach ($kind in @('main', 'family')) {
            $project = if ($kind -eq 'main') { $mainProject } else { $FamilyLibraryProject }
            $dllName = if ($kind -eq 'main') { 'YD_RevitTools.LicenseManager.dll' } else { 'CompanyFamilyLibraryMvp.dll' }
            $output = Join-Path $buildRoot "$year\$kind"
            Write-Host "Rebuilding $kind for Revit $year..." -ForegroundColor Yellow
            & dotnet build $project -t:Rebuild -c "Release$year" -p:Platform=x64 -p:RevitVersion=$year --output $output -v:minimal
            if ($LASTEXITCODE -ne 0) { throw "Build failed: $kind Release$year" }
            $artifact = Join-Path $output $dllName
            if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
                throw "Build did not produce expected artifact: $artifact"
            }
            if ($kind -eq 'main') {
                $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($artifact).FileVersion
                if ($fileVersion -ne "$version.0") { throw "Main DLL version mismatch: $artifact ($fileVersion)" }
            }
            $receipt += [pscustomobject]@{
                Key = "$year/$kind"; Configuration = "Release$year"
                Sha256 = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
            }
        }
    }
    $receipt | ConvertTo-Json | Set-Content (Join-Path $buildRoot 'build-receipt.json') -Encoding UTF8
    & $prepareScript -BuildRoot $buildRoot -FamilyLibraryProject $FamilyLibraryProject
    if (-not $?) { throw 'Prepare step failed. Installer build aborted.' }
} finally {
    # Only remove the unique temporary directory allocated by this invocation.
    if (Test-Path -LiteralPath $buildRoot) { Remove-Item -LiteralPath $buildRoot -Recurse -Force }
}

Write-Host ""

$signingCertificate = Get-LanCodeSigningCertificate
if ($signingCertificate) {
    Write-Host "Signing installer payload with LAN certificate..." -ForegroundColor Yellow
    foreach ($year in $supportedVersions) {
        Sign-FileWithLanCertificate -Path (Join-Path $installerDir "$year\YD_RevitTools.LicenseManager.dll") -Certificate $signingCertificate
        Sign-FileWithLanCertificate -Path (Join-Path $installerDir "$year\CompanyFamilyLibraryMvp.dll") -Certificate $signingCertificate
    }
    Write-Host ""
} else {
    Write-Host "[ERROR] The private LAN signing certificate matching LAN_CodeSigning.cer was not found." -ForegroundColor Red
    exit 1
}

# Compile the installer
Write-Host "Compiling installer..." -ForegroundColor Yellow
Write-Host ""

$compileStartedUtc = [DateTime]::UtcNow
$process = Start-Process -FilePath $iscc -ArgumentList "`"$issFile`"" -NoNewWindow -Wait -PassThru

if ($process.ExitCode -eq 0) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Installer compiler completed; checking output..." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Find the output file
    $outputDir = Join-Path $projectRoot "Output"
    if (-not (Test-Path $outputDir)) { throw "Installer output directory missing: $outputDir" }
    if (Test-Path $outputDir) {
        $setupFiles = @(Get-ChildItem $outputDir -Filter "HB_BIM_Tools_v${version}*_Setup.exe" | Where-Object { $_.LastWriteTimeUtc -ge $compileStartedUtc } | Sort-Object LastWriteTime -Descending)
        if ($setupFiles.Count -ne 1) { throw 'Expected exactly one freshly compiled installer.' }
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
            Write-Host "  2. Complete the per-version release checklist before distribution" -ForegroundColor White
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
