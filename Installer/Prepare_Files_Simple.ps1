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
$sourceIcons = Join-Path $projectRoot "Resources\Icons"

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
$sourceDll2024 = Join-Path $projectRoot "bin\Release2024\YD_RevitTools.LicenseManager.dll"
$sourceDll2025 = Join-Path $projectRoot "bin\Release2025\YD_RevitTools.LicenseManager.dll"
# Revit 2026 uses the same DLL as 2025 (if Release2026 doesn't exist)
$sourceDll2026Path = Join-Path $projectRoot "bin\Release2026\YD_RevitTools.LicenseManager.dll"
if (Test-Path $sourceDll2026Path) {
    $sourceDll2026 = $sourceDll2026Path
} else {
    $sourceDll2026 = $sourceDll2025
    Write-Host "[INFO] Using Revit 2025 DLL for Revit 2026 (Release2026 not found)" -ForegroundColor Yellow
}

if (-not (Test-Path $sourceDll2024)) {
    Write-Host "[ERROR] Cannot find YD_RevitTools.LicenseManager.dll for Revit 2024" -ForegroundColor Red
    Write-Host "Path: $sourceDll2024" -ForegroundColor Gray
    exit 1
}

if (-not (Test-Path $sourceDll2025)) {
    Write-Host "[ERROR] Cannot find YD_RevitTools.LicenseManager.dll for Revit 2025" -ForegroundColor Red
    Write-Host "Path: $sourceDll2025" -ForegroundColor Gray
    exit 1
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

if (-not (Test-Path $sourceIcons)) {
    Write-Host "[WARNING] Icons directory not found: $sourceIcons" -ForegroundColor Yellow
    Write-Host "Icons will not be included in the installer." -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "[OK] Main DLL found (2024, 2025, 2026)" -ForegroundColor Green
Write-Host "[OK] Dependency DLLs checked" -ForegroundColor Green
if (Test-Path $sourceIcons) {
    Write-Host "[OK] Icons directory found" -ForegroundColor Green
}
Write-Host ""

# Create shared resources
Write-Host "Creating shared resources..." -ForegroundColor Yellow

# 1. Icons directory
$sharedIconsDir = Join-Path $installerDir "Resources\Icons"
if (Test-Path (Join-Path $installerDir "Resources")) {
    Remove-Item (Join-Path $installerDir "Resources") -Recurse -Force
}
New-Item -ItemType Directory -Path $sharedIconsDir -Force | Out-Null

$iconCount = 0
if (Test-Path $sourceIcons) {
    Copy-Item "$sourceIcons\*.png" -Destination $sharedIconsDir -Force -ErrorAction SilentlyContinue
    $iconCount = (Get-ChildItem $sharedIconsDir -Filter "*.png" -ErrorAction SilentlyContinue).Count
    Write-Host "[OK] Shared icons ready: $iconCount files" -ForegroundColor Green
} else {
    Write-Host "[SKIP] No icons to copy" -ForegroundColor Yellow
}

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

Write-Host ""

# Create version directories
Write-Host "Creating version directories..." -ForegroundColor Yellow

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
Write-Host "│   └── Icons\ ($iconCount PNG files)" -ForegroundColor Cyan
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
$iconsSize = 0
if (Test-Path $sharedIconsDir) {
    $iconFiles = Get-ChildItem $sharedIconsDir -Recurse -ErrorAction SilentlyContinue
    if ($iconFiles) {
        $iconsSize = ($iconFiles | Measure-Object -Property Length -Sum).Sum
    }
}
$totalSize += $iconsSize

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
$versions = @("2024", "2025", "2026")
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
Write-Host "  Icons (shared): $([math]::Round($iconsSize / 1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  Dependency DLLs (shared): $([math]::Round($dependencySize / 1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  Main DLL (3 versions): $([math]::Round($dllsSize / 1KB, 2)) KB" -ForegroundColor Gray
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

