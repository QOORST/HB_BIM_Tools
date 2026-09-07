# Prepare Manual Deployment Package
# This script creates a complete deployment package that can be copied to other computers

param(
    [Parameter(Mandatory=$false)]
    [string]$OutputPath = "..\Output\Deployment"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "HB_BIM Tools - Prepare Deployment Package" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get project root directory
$scriptDir = $PSScriptRoot
$projectRoot = Split-Path $scriptDir -Parent
$outputDir = Join-Path $projectRoot $OutputPath

# Clean and create output directory
if (Test-Path $outputDir) {
    Write-Host "Cleaning old deployment package..." -ForegroundColor Yellow
    Remove-Item $outputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "Output directory: $outputDir" -ForegroundColor Gray
Write-Host ""

# Define dependency DLL list
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
    "Microsoft.Bcl.AsyncInterfaces.dll",
    "System.ComponentModel.Annotations.dll",
    "System.Drawing.Common.dll",
    "System.Numerics.Vectors.dll",
    "System.Text.Encoding.CodePages.dll",
    "System.Threading.Tasks.Extensions.dll",
    "System.ValueTuple.dll"
)

# Process each Revit version
$versions = @("2024", "2025", "2026")
$successCount = 0

foreach ($version in $versions) {
    Write-Host "Processing Revit $version..." -ForegroundColor Yellow
    
    # Source paths
    $sourceBinDir = Join-Path $projectRoot "bin\Release$version"
    $mainDll = Join-Path $sourceBinDir "YD_RevitTools.LicenseManager.dll"
    
    # Check if main DLL exists
    if (-not (Test-Path $mainDll)) {
        Write-Host "  [Skip] Release$version DLL not found" -ForegroundColor Gray
        continue
    }
    
    # Target paths
    $versionDir = Join-Path $outputDir $version
    New-Item -ItemType Directory -Path $versionDir -Force | Out-Null
    
    # Copy main DLL
    Copy-Item $mainDll -Destination $versionDir -Force
    Write-Host "  OK Main DLL" -ForegroundColor Green
    
    # Copy dependency DLLs
    $copiedCount = 0
    foreach ($dll in $dependencyDlls) {
        $sourceDll = Join-Path $sourceBinDir $dll
        if (Test-Path $sourceDll) {
            Copy-Item $sourceDll -Destination $versionDir -Force
            $copiedCount++
        }
    }

    if ($version -in @("2025", "2026")) {
        $openXmlNetStandard = Join-Path $env:USERPROFILE ".nuget\packages\documentformat.openxml\2.20.0\lib\netstandard2.0\DocumentFormat.OpenXml.dll"
        $packagingNetStandard = Join-Path $env:USERPROFILE ".nuget\packages\system.io.packaging\4.7.0\lib\netstandard2.0\System.IO.Packaging.dll"
        if (Test-Path $openXmlNetStandard) {
            Copy-Item $openXmlNetStandard -Destination $versionDir -Force
        }
        if (Test-Path $packagingNetStandard) {
            Copy-Item $packagingNetStandard -Destination $versionDir -Force
            $copiedCount++
        }
    }
    Write-Host "  OK Dependencies: $copiedCount DLLs" -ForegroundColor Green

    $sourceRuntimes = Join-Path $sourceBinDir "runtimes"
    $targetRuntimes = Join-Path $versionDir "runtimes"
    if (Test-Path $sourceRuntimes) {
        if (Test-Path $targetRuntimes) {
            Remove-Item $targetRuntimes -Recurse -Force
        }
        Copy-Item $sourceRuntimes -Destination $versionDir -Recurse -Force
        Write-Host "  OK Native runtimes" -ForegroundColor Green
    }
    
    # Copy shared resources, including icons and Pipe Sleeve default families.
    $sourceResources = Join-Path $projectRoot "Resources"
    $targetResources = Join-Path $versionDir "Resources"
    if (Test-Path $sourceResources) {
        if (Test-Path $targetResources) {
            Remove-Item $targetResources -Recurse -Force
        }
        Copy-Item $sourceResources -Destination $versionDir -Recurse -Force
        $resourceCount = (Get-ChildItem $targetResources -Recurse -File -ErrorAction SilentlyContinue).Count
        Write-Host "  OK Resources: $resourceCount files" -ForegroundColor Green
    }
    
    $successCount++
}

Write-Host ""

# Copy deployment script
Write-Host "Copying deployment script..." -ForegroundColor Yellow
$deployScript = Join-Path $scriptDir "Deploy.ps1"
if (Test-Path $deployScript) {
    Copy-Item $deployScript -Destination $outputDir -Force
    Write-Host "  OK Deploy.ps1" -ForegroundColor Green
} else {
    Write-Host "  [Warning] Deploy.ps1 not found" -ForegroundColor Yellow
}

# Copy documentation
$deploymentReadme = Join-Path $scriptDir "DEPLOYMENT_README.txt"
if (Test-Path $deploymentReadme) {
    Copy-Item $deploymentReadme -Destination (Join-Path $outputDir "README.txt") -Force
    Write-Host "  OK Deployment README" -ForegroundColor Green
}

$projectReadme = Join-Path $projectRoot "README.txt"
if (Test-Path $projectReadme) {
    Copy-Item $projectReadme -Destination (Join-Path $outputDir "PROJECT_README.txt") -Force
    Write-Host "  OK Project README" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deployment package ready!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Prepared deployment files for $successCount Revit versions" -ForegroundColor White
Write-Host ""
Write-Host "Deployment package location:" -ForegroundColor Yellow
Write-Host "  $outputDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Copy the entire Deployment folder to target computer" -ForegroundColor White
Write-Host "  2. Run PowerShell as Administrator on target computer" -ForegroundColor White
Write-Host "  3. Execute: .\Deploy.ps1" -ForegroundColor White
Write-Host ""
