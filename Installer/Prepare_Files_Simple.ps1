# Stage only the isolated outputs of the current Build_Installer invocation.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BuildRoot,
    [Parameter(Mandatory = $true)]
    [string]$FamilyLibraryProject
)
$ErrorActionPreference = 'Stop'
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host "  Preparing Installer Files" -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host ""

$installerDir = $PSScriptRoot
$projectRoot = Split-Path $installerDir -Parent
$revitApiRoot = Split-Path (Split-Path $projectRoot -Parent) -Parent
$familyLibraryProjectRoot = Split-Path $FamilyLibraryProject -Parent
$supportedVersions = @("2022", "2024", "2025", "2026")
$netStandardOpenXmlVersions = @("2025", "2026")
$runtimeResourceExtensions = @(".png", ".ico", ".jpg", ".jpeg", ".rfa")
$familyRoot = Split-Path $familyLibraryProjectRoot -Parent
foreach ($folder in @('database', 'previewer')) {
    $source = Join-Path $familyRoot $folder
    if (-not (Test-Path -LiteralPath $source -PathType Container) -or
        @(Get-ChildItem -LiteralPath $source -File -Recurse).Count -eq 0) {
        throw "Family library folder missing or empty: $source"
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $familyRoot 'database\family_library.sqlite') -PathType Leaf)) {
    throw 'Family library SQLite database is missing.'
}

# A receipt is written only after all eight builds succeed. Never use installed
# add-ins, a prior staging directory, or another Revit year's DLL as a fallback.
$receiptPath = Join-Path $BuildRoot 'build-receipt.json'
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
    throw 'Fresh build receipt missing. Run Build_Installer.ps1.'
}
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
foreach ($version in $supportedVersions) {
    foreach ($kind in @('main', 'family')) {
        $relative = "$version/$kind"
        $entry = @($receipt | Where-Object { $_.Key -eq $relative })
        if ($entry.Count -ne 1 -or $entry[0].Configuration -ne "Release$version") {
            throw "Missing or invalid build receipt entry: $relative"
        }
        $dllName = if ($kind -eq 'main') { 'YD_RevitTools.LicenseManager.dll' } else { 'CompanyFamilyLibraryMvp.dll' }
        $dllPath = Join-Path (Join-Path $BuildRoot $relative) $dllName
        if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf) -or
            (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash -ne $entry[0].Sha256) {
            throw "Fresh build artifact missing or changed: $dllPath"
        }
    }
}

# Check source files
Write-Host "Checking source files..." -ForegroundColor Yellow

# Shared installer dependencies must be byte-identical across all four fresh outputs.
$baseBinDir = Join-Path $BuildRoot "2024\main"
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
    $mainDlls[$version] = Join-Path $BuildRoot "$version\main\YD_RevitTools.LicenseManager.dll"
    $familyLibraryDlls[$version] = Join-Path $BuildRoot "$version\family\CompanyFamilyLibraryMvp.dll"
}

# Missing or differing shared dependencies are fatal, before touching staging.
# Native SQLite normally lives under runtimes in SDK-style build outputs.
foreach ($version in $supportedVersions) {
    $binDir = Join-Path $BuildRoot "$version\main"
    $nativeSqlite = Join-Path $binDir 'runtimes\win-x64\native\e_sqlite3.dll'
    if (-not (Test-Path -LiteralPath $nativeSqlite -PathType Leaf)) {
        throw "Required x64 SQLite runtime missing: $nativeSqlite"
    }
    Copy-Item -LiteralPath $nativeSqlite -Destination (Join-Path $binDir 'e_sqlite3.dll') -Force
}
foreach ($dll in $dependencyDlls) {
    $basePath = Join-Path $baseBinDir $dll
    if (-not (Test-Path -LiteralPath $basePath -PathType Leaf)) {
        throw "Required dependency missing: $basePath"
    }
    $baseHash = (Get-FileHash -LiteralPath $basePath -Algorithm SHA256).Hash
    foreach ($version in $supportedVersions) {
        $candidate = Join-Path $BuildRoot "$version\main\$dll"
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Required dependency missing: $candidate"
        }
        if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ne $baseHash) {
            throw "Shared dependency differs for Revit ${version}: $dll. Update installer to use version-specific dependencies."
        }
    }
}
foreach ($name in @('README.txt', 'LICENSE.txt', 'version.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $name) -PathType Leaf)) {
        throw "Required installer document missing: $name"
    }
}
if (-not (Test-Path -LiteralPath $sourceResources -PathType Container)) {
    throw "Required Resources directory missing: $sourceResources"
}
$openXmlNetStandard = Join-Path $env:USERPROFILE ".nuget\packages\documentformat.openxml\2.20.0\lib\netstandard2.0\DocumentFormat.OpenXml.dll"
$packagingNetStandard = Join-Path $env:USERPROFILE ".nuget\packages\system.io.packaging\4.7.0\lib\netstandard2.0\System.IO.Packaging.dll"
foreach ($path in @($openXmlNetStandard, $packagingNetStandard)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Revit 2025/2026 dependency missing: $path"
    }
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
if ([IO.Path]::GetFullPath($sharedResourcesDir) -ne [IO.Path]::Combine([IO.Path]::GetFullPath($installerDir), 'Resources')) { throw 'Unsafe resources staging path' }
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
    Copy-Item $sourceDll -Destination $installerDir -Force
    $copiedCount++
}
Write-Host "[OK] Copied $copiedCount dependency DLLs" -ForegroundColor Green

$sourceRuntimes = Join-Path $baseBinDir "runtimes"
$targetRuntimes = Join-Path $installerDir "runtimes"
if ([IO.Path]::GetFullPath($targetRuntimes) -ne [IO.Path]::Combine([IO.Path]::GetFullPath($installerDir), 'runtimes')) { throw 'Unsafe runtimes staging path' }
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

# Create version directories
Write-Host "Creating version directories..." -ForegroundColor Yellow

foreach ($version in $supportedVersions) {
    $versionDir = Join-Path $installerDir $version
    $resolvedVersionDir = [IO.Path]::GetFullPath($versionDir)
    $expectedVersionDir = [IO.Path]::Combine([IO.Path]::GetFullPath($installerDir), $version)
    if ($resolvedVersionDir -ne $expectedVersionDir -or $version -notmatch '^202[2456]$') {
        throw "Unsafe staging path: $versionDir"
    }
    if (Test-Path $versionDir) {
        Remove-Item -LiteralPath $resolvedVersionDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $versionDir -Force | Out-Null
    Copy-Item $mainDlls[$version] -Destination $versionDir -Force

    $familyLibraryDll = $familyLibraryDlls[$version]
    if (-not [string]::IsNullOrWhiteSpace($familyLibraryDll) -and (Test-Path $familyLibraryDll)) {
        Copy-Item $familyLibraryDll -Destination $versionDir -Force

        foreach ($folder in @('database', 'previewer')) {
            Copy-Item -LiteralPath (Join-Path $familyRoot $folder) -Destination $versionDir -Recurse -Force
        }
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
Write-Host "  Payload staged; Build_Installer.ps1 can now continue. Live acceptance is still required." -ForegroundColor White
Write-Host ""
