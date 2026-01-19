# YD_BIM Tools 部署腳本
# 在目標電腦上執行此腳本以安裝外掛
# 需要系統管理員權限

#Requires -RunAsAdministrator

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "YD_BIM Tools - 自動部署" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 檢查是否以系統管理員身分執行
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[錯誤] 請以系統管理員身分執行此腳本！" -ForegroundColor Red
    Write-Host ""
    Write-Host "請按右鍵點擊 PowerShell，選擇「以系統管理員身分執行」" -ForegroundColor Yellow
    pause
    exit 1
}

# 取得腳本目錄
$scriptDir = $PSScriptRoot

# 偵測已安裝的 Revit 版本
Write-Host "偵測已安裝的 Revit 版本..." -ForegroundColor Yellow
$versions = @("2024", "2025", "2026")
$installedVersions = @()

foreach ($version in $versions) {
    $addinPath = "C:\ProgramData\Autodesk\Revit\Addins\$version"
    if (Test-Path $addinPath) {
        $installedVersions += $version
        Write-Host "  ✓ Revit $version" -ForegroundColor Green
    }
}

if ($installedVersions.Count -eq 0) {
    Write-Host ""
    Write-Host "[錯誤] 未偵測到任何已安裝的 Revit 版本！" -ForegroundColor Red
    Write-Host "請確認已安裝 Revit 2024、2025 或 2026" -ForegroundColor Yellow
    pause
    exit 1
}

Write-Host ""
Write-Host "找到 $($installedVersions.Count) 個已安裝的 Revit 版本" -ForegroundColor Green
Write-Host ""

# 詢問要部署到哪些版本
Write-Host "請選擇要部署的版本（輸入版本號，用逗號分隔，或輸入 'all' 部署到所有版本）：" -ForegroundColor Yellow
Write-Host "可用版本: $($installedVersions -join ', ')" -ForegroundColor Gray
$selection = Read-Host "選擇"

$targetVersions = @()
if ($selection -eq "all" -or $selection -eq "") {
    $targetVersions = $installedVersions
} else {
    $selectedVersions = $selection -split ',' | ForEach-Object { $_.Trim() }
    foreach ($ver in $selectedVersions) {
        if ($installedVersions -contains $ver) {
            $targetVersions += $ver
        } else {
            Write-Host "[警告] Revit $ver 未安裝，跳過" -ForegroundColor Yellow
        }
    }
}

if ($targetVersions.Count -eq 0) {
    Write-Host ""
    Write-Host "[錯誤] 沒有選擇任何有效的版本！" -ForegroundColor Red
    pause
    exit 1
}

Write-Host ""
Write-Host "將部署到以下版本: $($targetVersions -join ', ')" -ForegroundColor Cyan
Write-Host ""

# 開始部署
$successCount = 0
$failCount = 0

foreach ($version in $targetVersions) {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "部署到 Revit $version" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    # 來源路徑
    $sourceDir = Join-Path $scriptDir $version
    if (-not (Test-Path $sourceDir)) {
        Write-Host "[錯誤] 找不到 $version 的部署檔案" -ForegroundColor Red
        $failCount++
        continue
    }
    
    # 目標路徑
    $targetDir = "C:\ProgramData\Autodesk\Revit\Addins\$version\YD_BIM"
    $addinFile = "C:\ProgramData\Autodesk\Revit\Addins\$version\YD_RevitTools.LicenseManager.addin"
    
    try {
        # 創建目標目錄
        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
            Write-Host "  ✓ 創建目標目錄" -ForegroundColor Green
        } else {
            Write-Host "  ✓ 目標目錄已存在" -ForegroundColor Gray
        }
        
        # 複製所有檔案
        Write-Host "  複製檔案..." -ForegroundColor Yellow
        Copy-Item "$sourceDir\*" -Destination $targetDir -Recurse -Force
        
        # 計算複製的檔案數量
        $fileCount = (Get-ChildItem $targetDir -Recurse -File).Count
        Write-Host "  ✓ 已複製 $fileCount 個檔案" -ForegroundColor Green
        
        # 創建 .addin 檔案
        $addinContent = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>YD_BIM Tools</Name>
    <Assembly>$targetDir\YD_RevitTools.LicenseManager.dll</Assembly>
    <FullClassName>YD_RevitTools.LicenseManager.App</FullClassName>
    <ClientId>B3F5D2D4-9392-4A9E-9C0D-A6F5DD93FAC7</ClientId>
    <VendorId>YD</VendorId>
    <VendorDescription>YD BIM Tools, www.ydbim.com</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
        
        $addinContent | Out-File -FilePath $addinFile -Encoding UTF8 -Force
        Write-Host "  ✓ 創建 .addin 配置檔案" -ForegroundColor Green
        
        # 驗證主 DLL 是否存在
        $mainDll = Join-Path $targetDir "YD_RevitTools.LicenseManager.dll"
        if (Test-Path $mainDll) {
            Write-Host "  ✓ 驗證主程式 DLL" -ForegroundColor Green
        } else {
            Write-Host "  [警告] 主程式 DLL 不存在！" -ForegroundColor Red
            $failCount++
            continue
        }
        
        Write-Host ""
        Write-Host "  Revit $version 部署成功！" -ForegroundColor Green
        $successCount++
        
    } catch {
        Write-Host ""
        Write-Host "  [錯誤] 部署失敗: $($_.Exception.Message)" -ForegroundColor Red
        $failCount++
    }
    
    Write-Host ""
}

# 顯示結果
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "部署完成" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "成功: $successCount 個版本" -ForegroundColor Green
if ($failCount -gt 0) {
    Write-Host "失敗: $failCount 個版本" -ForegroundColor Red
}
Write-Host ""

if ($successCount -gt 0) {
    Write-Host "下一步：" -ForegroundColor Yellow
    Write-Host "  1. 關閉所有 Revit 程式" -ForegroundColor White
    Write-Host "  2. 重新啟動 Revit" -ForegroundColor White
    Write-Host "  3. 在 Ribbon 介面中找到「YD_BIM Tools」選項卡" -ForegroundColor White
}

Write-Host ""
pause

