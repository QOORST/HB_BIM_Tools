# Prepare Installer Files
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host "  Preparing Installer Files" -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host ""

$installerDir = $PSScriptRoot
$projectRoot = Split-Path $installerDir -Parent

# Check source files
Write-Host "Checking source files..." -ForegroundColor Yellow
$sourceDll = Join-Path $projectRoot "bin\Release\YD_RevitTools.LicenseManager.dll"
$sourceNewtonsoftDll = Join-Path $projectRoot "bin\Release\Newtonsoft.Json.dll"
$sourceSystemTextJsonDll = Join-Path $projectRoot "bin\Release\System.Text.Json.dll"
$sourceSystemTextEncodingsWebDll = Join-Path $projectRoot "bin\Release\System.Text.Encodings.Web.dll"
$sourceSystemMemoryDll = Join-Path $projectRoot "bin\Release\System.Memory.dll"
$sourceSystemBuffersDll = Join-Path $projectRoot "bin\Release\System.Buffers.dll"
$sourceSystemRuntimeCompilerServicesUnsafeDll = Join-Path $projectRoot "bin\Release\System.Runtime.CompilerServices.Unsafe.dll"
$sourceIcons = Join-Path $projectRoot "Resources\Icons"

if (-not (Test-Path $sourceDll)) {
    Write-Host "[ERROR] Cannot find YD_RevitTools.LicenseManager.dll" -ForegroundColor Red
    Write-Host "Path: $sourceDll" -ForegroundColor Gray
    exit 1
}

if (-not (Test-Path $sourceNewtonsoftDll)) {
    Write-Host "[ERROR] Cannot find Newtonsoft.Json.dll" -ForegroundColor Red
    Write-Host "Path: $sourceNewtonsoftDll" -ForegroundColor Gray
    exit 1
}

if (-not (Test-Path $sourceSystemTextJsonDll)) {
    Write-Host "[ERROR] Cannot find System.Text.Json.dll" -ForegroundColor Red
    Write-Host "Path: $sourceSystemTextJsonDll" -ForegroundColor Gray
    exit 1
}

if (-not (Test-Path $sourceIcons)) {
    Write-Host "[ERROR] Cannot find Icons directory" -ForegroundColor Red
    Write-Host "Path: $sourceIcons" -ForegroundColor Gray
    exit 1
}

Write-Host "[OK] Main DLL found" -ForegroundColor Green
Write-Host "[OK] Newtonsoft.Json.dll found" -ForegroundColor Green
Write-Host "[OK] System.Text.Json.dll found" -ForegroundColor Green
Write-Host "[OK] Icons directory found" -ForegroundColor Green
Write-Host ""

# Create shared resources
Write-Host "Creating shared resources..." -ForegroundColor Yellow

# 1. Icons directory
$sharedIconsDir = Join-Path $installerDir "Resources\Icons"
if (Test-Path (Join-Path $installerDir "Resources")) {
    Remove-Item (Join-Path $installerDir "Resources") -Recurse -Force
}
New-Item -ItemType Directory -Path $sharedIconsDir -Force | Out-Null
Copy-Item "$sourceIcons\*.png" -Destination $sharedIconsDir -Force
$iconCount = (Get-ChildItem $sharedIconsDir -Filter "*.png").Count
Write-Host "[OK] Shared icons ready: $iconCount files" -ForegroundColor Green

# 2. Copy dependency DLLs
Copy-Item $sourceNewtonsoftDll -Destination $installerDir -Force
Write-Host "[OK] Newtonsoft.Json.dll copied" -ForegroundColor Green

Copy-Item $sourceSystemTextJsonDll -Destination $installerDir -Force
Write-Host "[OK] System.Text.Json.dll copied" -ForegroundColor Green

# Copy System.Text.Json dependencies
if (Test-Path $sourceSystemTextEncodingsWebDll) {
    Copy-Item $sourceSystemTextEncodingsWebDll -Destination $installerDir -Force
    Write-Host "[OK] System.Text.Encodings.Web.dll copied" -ForegroundColor Green
}

if (Test-Path $sourceSystemMemoryDll) {
    Copy-Item $sourceSystemMemoryDll -Destination $installerDir -Force
    Write-Host "[OK] System.Memory.dll copied" -ForegroundColor Green
}

if (Test-Path $sourceSystemBuffersDll) {
    Copy-Item $sourceSystemBuffersDll -Destination $installerDir -Force
    Write-Host "[OK] System.Buffers.dll copied" -ForegroundColor Green
}

if (Test-Path $sourceSystemRuntimeCompilerServicesUnsafeDll) {
    Copy-Item $sourceSystemRuntimeCompilerServicesUnsafeDll -Destination $installerDir -Force
    Write-Host "[OK] System.Runtime.CompilerServices.Unsafe.dll copied" -ForegroundColor Green
}

Write-Host ""

# Create version directories
Write-Host "Creating version directories..." -ForegroundColor Yellow
$versions = @("2024", "2025", "2026")

foreach ($version in $versions) {
    $versionDir = Join-Path $installerDir $version
    if (Test-Path $versionDir) {
        Remove-Item $versionDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $versionDir -Force | Out-Null
    Copy-Item $sourceDll -Destination $versionDir -Force
    Write-Host "[OK] Revit $version ready" -ForegroundColor Green
}

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
$iconsSize = (Get-ChildItem $sharedIconsDir -Recurse | Measure-Object -Property Length -Sum).Sum
$totalSize += $iconsSize

# Calculate dependency DLLs size
$dependencyDlls = @(
    "Newtonsoft.Json.dll",
    "System.Text.Json.dll",
    "System.Text.Encodings.Web.dll",
    "System.Memory.dll",
    "System.Buffers.dll",
    "System.Runtime.CompilerServices.Unsafe.dll"
)

$dependencySize = 0
foreach ($dll in $dependencyDlls) {
    $dllPath = Join-Path $installerDir $dll
    if (Test-Path $dllPath) {
        $dependencySize += (Get-Item $dllPath).Length
    }
}
$totalSize += $dependencySize

$dllsSize = 0
foreach ($version in $versions) {
    $versionDir = Join-Path $installerDir $version
    if (Test-Path $versionDir) {
        $dllsSize += (Get-ChildItem $versionDir -Recurse | Measure-Object -Property Length -Sum).Sum
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

