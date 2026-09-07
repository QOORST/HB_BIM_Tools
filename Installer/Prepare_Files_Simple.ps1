# Prepare Installer Files
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host "  Preparing Installer Files" -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host ""

$installerDir = $PSScriptRoot
$projectRoot = Split-Path $installerDir -Parent
$revitApiRoot = Split-Path (Split-Path $projectRoot -Parent) -Parent
$familyLibraryProjectRoot = Join-Path $revitApiRoot "Codex\work\family-library-management\addin"
$supportedVersions = @("2022", "2024", "2025", "2026")
$netStandardOpenXmlVersions = @("2025", "2026")
$runtimeResourceExtensions = @(".png", ".ico", ".jpg", ".jpeg", ".rfa")

function Resolve-FamilyLibraryDll {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [string]$FallbackVersion
    )

    $candidatePaths = @(
        (Join-Path $familyLibraryProjectRoot "bin\x64\Release$Version\net48\CompanyFamilyLibraryMvp.dll"),
        (Join-Path $projectRoot "bin\Release$Version\CompanyFamilyLibraryMvp.dll"),
        "C:\ProgramData\Autodesk\Revit\Addins\$Version\HB_BIM\CompanyFamilyLibraryMvp.dll"
    )

    foreach ($candidatePath in $candidatePaths) {
        if (Test-Path $candidatePath) {
            return $candidatePath
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($FallbackVersion)) {
        $fallbackPath = Resolve-FamilyLibraryDll -Version $FallbackVersion
        if ($fallbackPath) {
            Write-Host "[INFO] Using Revit $FallbackVersion CompanyFamilyLibraryMvp.dll for Revit $Version" -ForegroundColor Yellow
            return $fallbackPath
        }
    }

    return $null
}

# Check source files
Write-Host "Checking source files..." -ForegroundColor Yellow

# Use Release2024 as the base for dependency DLLs (they are the same across versions)
$baseBinDir = Join-Path $projectRoot "bin\Release2024"
$sourceResources = Join-Path $projectRoot "Resources"

# Define dependency DLLs. Revit API and .NET Framework built-in assemblies are excluded.
$dependencyDlls = @(
    "Newtonsoft.Json.dll",
    "System.Text.Json.dll",
    "System.Text.Encodings.Web.dll",
    "System.Memory.dll",
    "System.Buffers.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "DocumentFormat.OpenXml.dll",
    "EPPlus.dll",
    "EPPlus.Interfaces.dll",
    "EPPlus.System.Drawing.dll",
    "Microsoft.IO.RecyclableMemoryStream.dll",
    "Microsoft.Data.Sqlite.dll",
    "SQLitePCLRaw.batteries_v2.dll",
    "SQLitePCLRaw.core.dll",
    "SQLitePCLRaw.provider.dynamic_cdecl.dll",
    "e_sqlite3.dll",
    "Microsoft.Bcl.AsyncInterfaces.dll",
    "System.ComponentModel.Annotations.dll",
    "System.Drawing.Common.dll",
    "System.Numerics.Vectors.dll",
    "System.Text.Encoding.CodePages.dll",
    "System.Threading.Tasks.Extensions.dll",
    "System.ValueTuple.dll"
)

# Version-specific DLLs
$mainDlls = @{}
$familyLibraryDlls = @{}

foreach ($version in $supportedVersions) {
    $sourceDll = Join-Path $projectRoot "bin\Release$version\YD_RevitTools.LicenseManager.dll"
    if ($version -eq "2026" -and -not (Test-Path $sourceDll)) {
        $sourceDll = Join-Path $projectRoot "bin\Release2025\YD_RevitTools.LicenseManager.dll"
        Write-Host "[INFO] Using Revit 2025 DLL for Revit 2026 (Release2026 not found)" -ForegroundColor Yellow
    }

    if (-not (Test-Path $sourceDll)) {
        Write-Host "[ERROR] Cannot find YD_RevitTools.LicenseManager.dll for Revit $version" -ForegroundColor Red
        Write-Host "Path: $sourceDll" -ForegroundColor Gray
        exit 1
    }

    $mainDlls[$version] = $sourceDll
    $fallbackVersion = if ($version -eq "2026") { "2025" } else { $null }
    $familyLibraryDlls[$version] = Resolve-FamilyLibraryDll -Version $version -FallbackVersion $fallbackVersion

    if ([string]::IsNullOrWhiteSpace($familyLibraryDlls[$version]) -or -not (Test-Path $familyLibraryDlls[$version])) {
        Write-Host "[WARNING] CompanyFamilyLibraryMvp.dll not found for Revit $version; the company library button will be skipped for that version." -ForegroundColor Yellow
        Write-Host "Path: $($familyLibraryDlls[$version])" -ForegroundColor Gray
    }
}

# Check dependency DLLs exist
$missingDlls = @()
foreach ($dll in $dependencyDlls) {
    $dllPath = Join-Path $baseBinDir $dll
    if (-not (Test-Path $dllPath)) {
        $missingDlls += $dll
    }
}

if ($missingDlls.Count -gt 0) {
    Write-Host "[WARNING] Some dependency DLLs not found:" -ForegroundColor Yellow
    foreach ($dll in $missingDlls) {
        Write-Host "  - $dll" -ForegroundColor Gray
    }
    Write-Host ""
}

if (-not (Test-Path $sourceResources)) {
    Write-Host "[WARNING] Resources directory not found: $sourceResources" -ForegroundColor Yellow
    Write-Host "Resources will not be included in the installer." -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "[OK] Main DLL found ($($supportedVersions -join ', '))" -ForegroundColor Green
Write-Host "[OK] Dependency DLLs checked" -ForegroundColor Green
if (Test-Path $sourceResources) {
    Write-Host "[OK] Resources directory found" -ForegroundColor Green
}
Write-Host ""

# Create shared resources
Write-Host "Creating shared resources..." -ForegroundColor Yellow

# 1. Resources directory
$sharedResourcesDir = Join-Path $installerDir "Resources"
if (Test-Path $sharedResourcesDir) {
    Remove-Item $sharedResourcesDir -Recurse -Force
}
New-Item -ItemType Directory -Path $sharedResourcesDir -Force | Out-Null

$sourceIconsDir = Join-Path $sourceResources "Icons"
$sourceIconCount = 0

if (Test-Path $sourceIconsDir) {
    $sourceIconCount = (Get-ChildItem $sourceIconsDir -Recurse -File -Include *.png,*.ico,*.jpg,*.jpeg -ErrorAction SilentlyContinue).Count
}

# Rebuild Installer\Resources from runtime assets only. Development scripts and notes stay in source.
$resourceFileCount = 0
if (Test-Path $sourceResources) {
    $runtimeResources = Get-ChildItem $sourceResources -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $runtimeResourceExtensions -contains $_.Extension.ToLowerInvariant() }

    if ($runtimeResources.Count -gt 0) {
        foreach ($resource in $runtimeResources) {
            $relativePath = $resource.FullName.Substring($sourceResources.Length).TrimStart('\')
            $targetPath = Join-Path $sharedResourcesDir $relativePath
            $targetDir = Split-Path $targetPath -Parent
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
            Copy-Item -LiteralPath $resource.FullName -Destination $targetPath -Force
        }

        Write-Host "[OK] Synced runtime Resources to installer Resources" -ForegroundColor Green
    } else {
        Write-Host "[INFO] Project Resources has no runtime assets" -ForegroundColor Yellow
    }
} else {
    Write-Host "[INFO] Project Resources not found" -ForegroundColor Yellow
}

$resourceFileCount = (Get-ChildItem $sharedResourcesDir -Recurse -File -ErrorAction SilentlyContinue).Count
Write-Host "[OK] Shared resources ready: $resourceFileCount files" -ForegroundColor Green

# 2. Copy dependency DLLs
$copiedCount = 0
foreach ($dll in $dependencyDlls) {
    $sourceDll = Join-Path $baseBinDir $dll
    if (Test-Path $sourceDll) {
        Copy-Item $sourceDll -Destination $installerDir -Force
        $copiedCount++
    }
}
Write-Host "[OK] Copied $copiedCount dependency DLLs" -ForegroundColor Green

$sourceRuntimes = Join-Path $baseBinDir "runtimes"
$targetRuntimes = Join-Path $installerDir "runtimes"
if (Test-Path $sourceRuntimes) {
    if (Test-Path $targetRuntimes) {
        Remove-Item $targetRuntimes -Recurse -Force
    }
    Copy-Item $sourceRuntimes -Destination $installerDir -Recurse -Force
    Write-Host "[OK] Copied native runtime dependencies" -ForegroundColor Green
}

# 3. Copy installer documentation from project root so packaged files match the current release.
$readmeSource = Join-Path $projectRoot "README.txt"
$licenseSource = Join-Path $projectRoot "LICENSE.txt"
$versionSource = Join-Path $projectRoot "version.json"
if (Test-Path $readmeSource) {
    Copy-Item $readmeSource -Destination $installerDir -Force
}
if (Test-Path $licenseSource) {
    Copy-Item $licenseSource -Destination $installerDir -Force
}
if (Test-Path $versionSource) {
    Copy-Item $versionSource -Destination $installerDir -Force
}
Write-Host "[OK] Synced README.txt, LICENSE.txt, and version.json" -ForegroundColor Green

Write-Host ""

# Revit 2025/2026 run in the newer .NET host. Package the netstandard OpenXML build
# with System.IO.Packaging for those versions to avoid load failures.
$openXmlNetStandard = Join-Path $env:USERPROFILE ".nuget\packages\documentformat.openxml\2.20.0\lib\netstandard2.0\DocumentFormat.OpenXml.dll"
$packagingNetStandard = Join-Path $env:USERPROFILE ".nuget\packages\system.io.packaging\4.7.0\lib\netstandard2.0\System.IO.Packaging.dll"

# Create version directories
Write-Host "Creating version directories..." -ForegroundColor Yellow

foreach ($version in $supportedVersions) {
    $versionDir = Join-Path $installerDir $version
    if (Test-Path $versionDir) {
        Remove-Item $versionDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $versionDir -Force | Out-Null
    Copy-Item $mainDlls[$version] -Destination $versionDir -Force

    $familyLibraryDll = $familyLibraryDlls[$version]
    if (-not [string]::IsNullOrWhiteSpace($familyLibraryDll) -and (Test-Path $familyLibraryDll)) {
        Copy-Item $familyLibraryDll -Destination $versionDir -Force
    }

    if ($netStandardOpenXmlVersions -contains $version) {
        if (Test-Path $openXmlNetStandard) {
            Copy-Item $openXmlNetStandard -Destination $versionDir -Force
        }
        if (Test-Path $packagingNetStandard) {
            Copy-Item $packagingNetStandard -Destination $versionDir -Force
        }
    }

    Write-Host "[OK] Revit $version ready" -ForegroundColor Green
}

Write-Host ""
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host "  Directory Structure" -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Installer\" -ForegroundColor White
Write-Host "+-- Resources\" -ForegroundColor White
Write-Host "|   +-- ... ($resourceFileCount files)" -ForegroundColor Cyan
foreach ($version in $supportedVersions) {
    Write-Host "+-- $version\" -ForegroundColor White
    Write-Host "|   +-- YD_RevitTools.LicenseManager.dll" -ForegroundColor Gray
}
Write-Host "+-- Newtonsoft.Json.dll" -ForegroundColor Cyan
Write-Host "+-- System.Text.Json.dll" -ForegroundColor Cyan
Write-Host "+-- System.Text.Encodings.Web.dll" -ForegroundColor Cyan
Write-Host "+-- System.Memory.dll" -ForegroundColor Cyan
Write-Host "+-- System.Buffers.dll" -ForegroundColor Cyan
Write-Host "+-- System.Runtime.CompilerServices.Unsafe.dll" -ForegroundColor Cyan
Write-Host "+-- README.txt" -ForegroundColor Gray
Write-Host "+-- LICENSE.txt" -ForegroundColor Gray
Write-Host "+-- HB_BIM_Setup.iss" -ForegroundColor Gray
Write-Host ""

# Calculate total size
$totalSize = 0
$resourcesSize = 0
if (Test-Path $sharedResourcesDir) {
    $resourceFiles = Get-ChildItem $sharedResourcesDir -Recurse -File -ErrorAction SilentlyContinue
    if ($resourceFiles) {
        $resourcesSize = ($resourceFiles | Measure-Object -Property Length -Sum).Sum
    }
}
$totalSize += $resourcesSize

# Calculate dependency DLLs size
$dependencySize = 0
foreach ($dll in $dependencyDlls) {
    $dllPath = Join-Path $installerDir $dll
    if (Test-Path $dllPath) {
        $dependencySize += (Get-Item $dllPath).Length
    }
}
$totalSize += $dependencySize

# Calculate version-specific DLLs size
$dllsSize = 0
foreach ($version in $supportedVersions) {
    $versionDir = Join-Path $installerDir $version
    if (Test-Path $versionDir) {
        $versionFiles = Get-ChildItem $versionDir -Recurse -ErrorAction SilentlyContinue
        if ($versionFiles) {
            $dllsSize += ($versionFiles | Measure-Object -Property Length -Sum).Sum
        }
    }
}
$totalSize += $dllsSize

Write-Host "File Size Statistics:" -ForegroundColor Cyan
Write-Host "  Resources (shared): $([math]::Round($resourcesSize / 1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  Dependency DLLs (shared): $([math]::Round($dependencySize / 1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  Main DLL (4 versions): $([math]::Round($dllsSize / 1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  Total Size: $([math]::Round($totalSize / 1KB, 2)) KB" -ForegroundColor White
Write-Host ""

Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host "  Preparation Complete!" -ForegroundColor Green
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  Run .\Installer\Build_Installer.ps1 to compile and sign the installer." -ForegroundColor White
Write-Host ""
