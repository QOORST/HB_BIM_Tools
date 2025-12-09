# YD_BIM 工具圖示生成腳本
# 使用 System.Drawing 創建簡單的圖示

Add-Type -AssemblyName System.Drawing

function Create-Icon {
    param(
        [string]$Text,
        [int]$Size,
        [string]$OutputPath,
        [System.Drawing.Color]$BackColor,
        [System.Drawing.Color]$ForeColor
    )
    
    # 創建位圖
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    
    # 設定高品質渲染
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    
    # 填充背景
    $brush = New-Object System.Drawing.SolidBrush($BackColor)
    $graphics.FillRectangle($brush, 0, 0, $Size, $Size)
    
    # 繪製文字
    $fontSize = if ($Size -eq 16) { 10 } else { 20 }
    $font = New-Object System.Drawing.Font("Microsoft YaHei", $fontSize, [System.Drawing.FontStyle]::Bold)
    $textBrush = New-Object System.Drawing.SolidBrush($ForeColor)
    
    $stringFormat = New-Object System.Drawing.StringFormat
    $stringFormat.Alignment = [System.Drawing.StringAlignment]::Center
    $stringFormat.LineAlignment = [System.Drawing.StringAlignment]::Center
    
    $rect = New-Object System.Drawing.RectangleF(0, 0, $Size, $Size)
    $graphics.DrawString($Text, $font, $textBrush, $rect, $stringFormat)
    
    # 儲存圖示
    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    
    # 清理資源
    $graphics.Dispose()
    $bitmap.Dispose()
    $brush.Dispose()
    $textBrush.Dispose()
    $font.Dispose()
}

# 設定輸出目錄
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = $scriptDir

Write-Host "=== 開始生成圖示 ===" -ForegroundColor Cyan
Write-Host "輸出目錄: $outputDir`n" -ForegroundColor Yellow

# 定義顏色
$colors = @{
    Gold = [System.Drawing.Color]::FromArgb(255, 215, 0)
    Blue = [System.Drawing.Color]::FromArgb(30, 144, 255)
    Red = [System.Drawing.Color]::FromArgb(220, 20, 60)
    Green = [System.Drawing.Color]::FromArgb(34, 139, 34)
    Purple = [System.Drawing.Color]::FromArgb(138, 43, 226)
    Orange = [System.Drawing.Color]::FromArgb(255, 140, 0)
    White = [System.Drawing.Color]::White
    Black = [System.Drawing.Color]::Black
}

# 生成圖示
$icons = @(
    @{ Name = "license"; Text = "🔑"; Sizes = @(16, 32); BackColor = $colors.Gold; ForeColor = $colors.White },
    @{ Name = "about"; Text = "ℹ"; Sizes = @(16, 32); BackColor = $colors.Blue; ForeColor = $colors.White },
    @{ Name = "formwork_delete"; Text = "✖"; Sizes = @(16, 32); BackColor = $colors.Red; ForeColor = $colors.White },
    @{ Name = "formwork_pick"; Text = "👆"; Sizes = @(16, 32); BackColor = $colors.Green; ForeColor = $colors.White },
    @{ Name = "export_csv"; Text = "📊"; Sizes = @(16, 32); BackColor = $colors.Blue; ForeColor = $colors.White },
    @{ Name = "structural_analysis"; Text = "📐"; Sizes = @(16, 32); BackColor = $colors.Purple; ForeColor = $colors.White },
    @{ Name = "cobie_field"; Text = "⚙"; Sizes = @(16, 32); BackColor = $colors.Green; ForeColor = $colors.White },
    @{ Name = "cobie_template"; Text = "📄"; Sizes = @(16, 32); BackColor = $colors.Blue; ForeColor = $colors.White }
)

foreach ($icon in $icons) {
    foreach ($size in $icon.Sizes) {
        $fileName = "$($icon.Name)_$size.png"
        $filePath = Join-Path $outputDir $fileName
        
        try {
            Create-Icon -Text $icon.Text `
                       -Size $size `
                       -OutputPath $filePath `
                       -BackColor $icon.BackColor `
                       -ForeColor $icon.ForeColor
            
            Write-Host "✅ 已生成: $fileName" -ForegroundColor Green
        }
        catch {
            Write-Host "❌ 生成失敗: $fileName - $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host "`n=== 圖示生成完成 ===" -ForegroundColor Cyan
Write-Host "總計生成: $($icons.Count * 2) 個圖示檔案" -ForegroundColor Green

