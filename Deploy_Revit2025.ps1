# 部署 Revit 2025 版本
# 請先關閉 Revit 2025 再執行此腳本

Write-Host "`n=== 部署 YD BIM Tools 到 Revit 2025 ===" -ForegroundColor Cyan
Write-Host "請確認已關閉 Revit 2025！" -ForegroundColor Yellow
Write-Host "按任意鍵繼續..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

$source = "bin\Release2025"
$target = "C:\ProgramData\Autodesk\Revit\Addins\2025\YD_BIM"

# 檢查來源資料夾
if (!(Test-Path $source)) {
    Write-Host "`n❌ 找不到來源資料夾: $source" -ForegroundColor Red
    Write-Host "請先編譯 Release2025 版本！" -ForegroundColor Yellow
    pause
    exit
}

# 建立目標資料夾
if (!(Test-Path $target)) {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Write-Host "`n✅ 已建立資料夾: $target" -ForegroundColor Green
}

# 要複製的檔案清單
$files = @(
    "YD_RevitTools.LicenseManager.dll",
    "EPPlus.dll",
    "EPPlus.Interfaces.dll",
    "EPPlus.System.Drawing.dll",
    "Microsoft.IO.RecyclableMemoryStream.dll",
    "System.ComponentModel.Annotations.dll",
    "Newtonsoft.Json.dll",
    "System.Buffers.dll",
    "System.Drawing.Common.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "System.Text.Encoding.CodePages.dll",
    "System.Text.Encodings.Web.dll",
    "System.Text.Json.dll",
    "System.Threading.Tasks.Extensions.dll",
    "System.ValueTuple.dll",
    "Microsoft.Bcl.AsyncInterfaces.dll"
)

Write-Host "`n開始複製檔案..." -ForegroundColor Yellow
$successCount = 0
$failCount = 0

foreach ($file in $files) {
    $sourcePath = Join-Path $source $file
    if (Test-Path $sourcePath) {
        try {
            Copy-Item -Path $sourcePath -Destination $target -Force -ErrorAction Stop
            Write-Host "  ✅ $file" -ForegroundColor Green
            $successCount++
        }
        catch {
            Write-Host "  ❌ $file (錯誤: $($_.Exception.Message))" -ForegroundColor Red
            $failCount++
        }
    }
    else {
        Write-Host "  ⚠️ 找不到: $file" -ForegroundColor Yellow
        $failCount++
    }
}

Write-Host "`n=== 部署完成 ===" -ForegroundColor Cyan
Write-Host "成功: $successCount 個檔案" -ForegroundColor Green
if ($failCount -gt 0) {
    Write-Host "失敗: $failCount 個檔案" -ForegroundColor Red
}

Write-Host "`n按任意鍵結束..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

