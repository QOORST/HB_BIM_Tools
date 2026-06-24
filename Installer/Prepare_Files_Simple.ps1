# Prepare Installer Files
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host "  Preparing Installer Files" -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host ""

$installerDir = $PSScriptRoot
$projectRoot = Split-Path $installerDir -Parent

# Check source files
Write-Host "Checking source files..." -ForegroundColor Yellow

# Use Release2024 as the base for dependency DLLs (they are the same across versions)
$baseBinDir = Join-Path $projectRoot "bin\Release2024"
$sourceResources = Join-Path $projectRoot "Resources"

# 定義所有需要的依賴 DLL（排除 Revit API 和 .NET Framework 內建的）
$dependencyDlls = @(
    "Newtonsoft.Json.dll",
    "System.Text.Json.dll",
    "System.Text.Encodings.Web.dll",
    "System.Memory.dll",
    "System.Buffers.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "EPPlus.dll",
    "EPPlus.Interfaces.dll",
    "EPPlus.System.Drawing.dll",
    "Microsoft.IO.RecyclableMemoryStream.dll",
    "Microsoft.Bcl.AsyncInterfaces.dll",
    "System.ComponentModel.Annotations.dll",
    "System.Drawing.Common.dll",
    "System.Numerics.Vectors.dll",
    "System.Text.Encoding.CodePages.dll",
    "System.Threading.Tasks.Extensions.dll",
    "System.ValueTuple.dll"
)

# Version-specific DLLs
$sourceDll2022 = Join-Path $projectRoot "bin\Release2022\YD_RevitTools.LicenseManager.dll"
$sourceDll2024 = Join-Path $projectRoot "bin\Release2024\YD_RevitTools.LicenseManager.dll"
$sourceDll2025 = Join-Path $projectRoot "bin\Release2025\YD_RevitTools.LicenseManager.dll"

# Revit 2026 uses its own DLL when available, otherwise falls back to 2025
$sourceDll2026Path = Join-Path $projectRoot "bin\Release2026\YD_RevitTools.LicenseManager.dll"
if (Test-Path $sourceDll2026Path) {
    $sourceDll2026 = $sourceDll2026Path
} else {
    $sourceDll2026 = $sourceDll2025
    Write-Host "[INFO] Using Revit 2025 DLL for Revit 2026 (Release2026 not found)" -ForegroundColor Yellow
}

$requiredMainDlls = @(
    @{ Version = "2022"; Path = $sourceDll2022 },
    @{ Version = "2024"; Path = $sourceDll2024 },
    @{ Version = "2025"; Path = $sourceDll2025 }
)

foreach ($dllInfo in $requiredMainDlls) {
    if (-not (Test-Path $dllInfo.Path)) {
        Write-Host "[ERROR] Cannot find YD_RevitTools.LicenseManager.dll for Revit $($dllInfo.Version)" -ForegroundColor Red
        Write-Host "Path: $($dllInfo.Path)" -ForegroundColor Gray
        exit 1
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

Write-Host "[OK] Main DLL found (2022, 2024, 2025, 2026)" -ForegroundColor Green
Write-Host "[OK] Dependency DLLs checked" -ForegroundColor Green
if (Test-Path $sourceResources) {
    Write-Host "[OK] Resources directory found" -ForegroundColor Green
}
Write-Host ""

# Create shared resources
Write-Host "Creating shared resources..." -ForegroundColor Yellow

# 1. Resources directory
$sharedResourcesDir = Join-Path $installerDir "Resources"
New-Item -ItemType Directory -Path $sharedResourcesDir -Force | Out-Null

$sourceIconsDir = Join-Path $sourceResources "Icons"
$installerIconsDir = Join-Path $sharedResourcesDir "Icons"
$sourceIconCount = 0
$installerIconCountBeforeSync = 0

if (Test-Path $sourceIconsDir) {
    $sourceIconCount = (Get-ChildItem $sourceIconsDir -Recurse -File -Include *.png,*.ico,*.jpg,*.jpeg -ErrorAction SilentlyContinue).Count
}

if (Test-Path $installerIconsDir) {
    $installerIconCountBeforeSync = (Get-ChildItem $installerIconsDir -Recurse -File -Include *.png,*.ico,*.jpg,*.jpeg -ErrorAction SilentlyContinue).Count
}

# 保留 Installer\Resources 既有內容，僅從專案 Resources 做增量同步
$resourceFileCount = 0
if (Test-Path $sourceResources) {
    $sourceResourceFileCount = (Get-ChildItem $sourceResources -Recurse -File -ErrorAction SilentlyContinue).Count
    if ($sourceResourceFileCount -gt 0) {
        Copy-Item "$sourceResources\*" -Destination $sharedResourcesDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "[OK] Synced project Resources to installer Resources" -ForegroundColor Green
    } else {
        Write-Host "[INFO] Project Resources is empty, keeping existing installer Resources" -ForegroundColor Yellow
    }
} else {
    Write-Host "[INFO] Project Resources not found, keeping existing installer Resources" -ForegroundColor Yellow
}

if ($installerIconCountBeforeSync -gt 0 -and $sourceIconCount -eq 0) {
    Write-Host "WARNING: Installer/Resources/Icons contains image files, but project Resources/Icons has none." -ForegroundColor Yellow
    Write-Host "WARNING: Existing installer icon files were preserved. Verify whether they should be synced back to project Resources." -ForegroundColor Yellow
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

# 3. Copy installer documentation from project root so packaged files match the current release.
$readmeSource = Join-Path $projectRoot "README.txt"
$licenseSource = Join-Path $projectRoot "LICENSE.txt"
if (Test-Path $readmeSource) {
    Copy-Item $readmeSource -Destination $installerDir -Force
}
if (Test-Path $licenseSource) {
    Copy-Item $licenseSource -Destination $installerDir -Force
}
Write-Host "[OK] Synced README.txt and LICENSE.txt" -ForegroundColor Green

Write-Host ""

# Create version directories
Write-Host "Creating version directories..." -ForegroundColor Yellow

# Revit 2022
$versionDir2022 = Join-Path $installerDir "2022"
if (Test-Path $versionDir2022) {
    Remove-Item $versionDir2022 -Recurse -Force
}
New-Item -ItemType Directory -Path $versionDir2022 -Force | Out-Null
Copy-Item $sourceDll2022 -Destination $versionDir2022 -Force
Write-Host "[OK] Revit 2022 ready" -ForegroundColor Green

# Revit 2024
$versionDir2024 = Join-Path $installerDir "2024"
if (Test-Path $versionDir2024) {
    Remove-Item $versionDir2024 -Recurse -Force
}
New-Item -ItemType Directory -Path $versionDir2024 -Force | Out-Null
Copy-Item $sourceDll2024 -Destination $versionDir2024 -Force
Write-Host "[OK] Revit 2024 ready" -ForegroundColor Green

# Revit 2025
$versionDir2025 = Join-Path $installerDir "2025"
if (Test-Path $versionDir2025) {
    Remove-Item $versionDir2025 -Recurse -Force
}
New-Item -ItemType Directory -Path $versionDir2025 -Force | Out-Null
Copy-Item $sourceDll2025 -Destination $versionDir2025 -Force
Write-Host "[OK] Revit 2025 ready" -ForegroundColor Green

# Revit 2026
$versionDir2026 = Join-Path $installerDir "2026"
if (Test-Path $versionDir2026) {
    Remove-Item $versionDir2026 -Recurse -Force
}
New-Item -ItemType Directory -Path $versionDir2026 -Force | Out-Null
Copy-Item $sourceDll2026 -Destination $versionDir2026 -Force
Write-Host "[OK] Revit 2026 ready" -ForegroundColor Green

Write-Host ""
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host "  Directory Structure" -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Installer\" -ForegroundColor White
Write-Host "├── Resources\" -ForegroundColor White
Write-Host "│   └── ... ($resourceFileCount files)" -ForegroundColor Cyan
Write-Host "├── 2022\" -ForegroundColor White
Write-Host "│   └── YD_RevitTools.LicenseManager.dll" -ForegroundColor Gray
Write-Host "├── 2024\" -ForegroundColor White
Write-Host "│   └── YD_RevitTools.LicenseManager.dll" -ForegroundColor Gray
Write-Host "├── 2025\" -ForegroundColor White
Write-Host "│   └── YD_RevitTools.LicenseManager.dll" -ForegroundColor Gray
Write-Host "├── 2026\" -ForegroundColor White
Write-Host "│   └── YD_RevitTools.LicenseManager.dll" -ForegroundColor Gray
Write-Host "├── Newtonsoft.Json.dll" -ForegroundColor Cyan
Write-Host "├── System.Text.Json.dll" -ForegroundColor Cyan
Write-Host "├── System.Text.Encodings.Web.dll" -ForegroundColor Cyan
Write-Host "├── System.Memory.dll" -ForegroundColor Cyan
Write-Host "├── System.Buffers.dll" -ForegroundColor Cyan
Write-Host "├── System.Runtime.CompilerServices.Unsafe.dll" -ForegroundColor Cyan
Write-Host "├── README.txt" -ForegroundColor Gray
Write-Host "├── LICENSE.txt" -ForegroundColor Gray
Write-Host "└── YD_BIM_Setup.iss" -ForegroundColor Gray
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
$versions = @("2022", "2024", "2025", "2026")
$dllsSize = 0
foreach ($version in $versions) {
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
Write-Host "  1. Open Inno Setup Compiler" -ForegroundColor White
Write-Host "  2. Open YD_BIM_Setup.iss" -ForegroundColor White
Write-Host "  3. Click Build -> Compile" -ForegroundColor White
Write-Host ""
