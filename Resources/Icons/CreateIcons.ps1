Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path (Split-Path $scriptDir -Parent) -Parent
$installerIconDir = Join-Path $projectRoot "Installer\Resources\Icons"

function New-Brush([string]$hex) {
    return New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($hex))
}

function New-Pen([string]$hex, [float]$width) {
    $pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml($hex), $width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function New-RectF([float]$x, [float]$y, [float]$w, [float]$h) {
    return New-Object System.Drawing.RectangleF -ArgumentList $x, $y, $w, $h
}

function New-PointF([float]$x, [float]$y) {
    return New-Object System.Drawing.PointF -ArgumentList $x, $y
}

function Draw-RoundRect($g, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r, $brush, $pen) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    if ($brush) { $g.FillPath($brush, $path) }
    if ($pen) { $g.DrawPath($pen, $path) }
    $path.Dispose()
}

function Draw-Doc($g, [float]$s, [string]$accent, [string]$label, [string]$arrow) {
    $white = New-Brush "#FFFFFF"
    $line = New-Pen "#355070" ($s * 1.2)
    $acc = New-Pen $accent ($s * 2.1)
    $fill = New-Brush "#EAF4FF"
    Draw-RoundRect $g (6*$s) (4*$s) (18*$s) (24*$s) (2*$s) $fill $line
    $g.DrawLine($line, 10*$s, 11*$s, 21*$s, 11*$s)
    $g.DrawLine($line, 10*$s, 16*$s, 21*$s, 16*$s)
    $g.DrawLine($line, 10*$s, 21*$s, 18*$s, 21*$s)
    if ($label) {
        $font = New-Object System.Drawing.Font("Segoe UI", (7*$s), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $g.DrawString($label, $font, (New-Brush $accent), (New-RectF (6*$s) (19*$s) (18*$s) (8*$s)), $format)
        $font.Dispose()
        $format.Dispose()
    }
    if ($arrow -eq "out") {
        $g.DrawLine($acc, 18*$s, 25*$s, 27*$s, 16*$s)
        $g.DrawLine($acc, 27*$s, 16*$s, 27*$s, 22*$s)
        $g.DrawLine($acc, 27*$s, 16*$s, 21*$s, 16*$s)
    } elseif ($arrow -eq "in") {
        $g.DrawLine($acc, 27*$s, 16*$s, 18*$s, 25*$s)
        $g.DrawLine($acc, 18*$s, 25*$s, 24*$s, 25*$s)
        $g.DrawLine($acc, 18*$s, 25*$s, 18*$s, 19*$s)
    }
    $white.Dispose(); $line.Dispose(); $acc.Dispose(); $fill.Dispose()
}

function Draw-Pipe($g, [float]$s, [float]$x1, [float]$y1, [float]$x2, [float]$y2, [string]$color, [float]$width) {
    $shadow = New-Pen "#3E4C59" ($width*$s)
    $pen = New-Pen $color (($width-1.8)*$s)
    $highlight = New-Pen "#FFFFFF" (1.1*$s)
    $g.DrawLine($shadow, $x1*$s, $y1*$s, $x2*$s, $y2*$s)
    $g.DrawLine($pen, $x1*$s, $y1*$s, $x2*$s, $y2*$s)
    $g.DrawLine($highlight, ($x1+1)*$s, ($y1-1)*$s, ($x2-1)*$s, ($y2-1)*$s)
    $shadow.Dispose(); $pen.Dispose(); $highlight.Dispose()
}

function Draw-Icon([string]$name, [int]$size, [string]$outDir) {
    $s = $size / 32.0
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    switch ($name) {
        "license" {
            $p = New-Pen "#F2B705" (3.2*$s)
            $g.DrawEllipse($p, 4*$s, 6*$s, 12*$s, 12*$s)
            $g.DrawLine($p, 15*$s, 17*$s, 27*$s, 29*$s)
            $g.DrawLine($p, 22*$s, 24*$s, 25*$s, 21*$s)
            $g.DrawLine($p, 25*$s, 27*$s, 28*$s, 24*$s)
            $p.Dispose()
        }
        "about" {
            $b = New-Brush "#1976D2"; $w = New-Brush "#FFFFFF"
            $g.FillEllipse($b, 4*$s, 4*$s, 24*$s, 24*$s)
            $font = New-Object System.Drawing.Font("Segoe UI", (22*$s), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
            $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = "Center"; $fmt.LineAlignment = "Center"
            $g.DrawString("i", $font, $w, (New-RectF (4*$s) (3*$s) (24*$s) (25*$s)), $fmt)
            $b.Dispose(); $w.Dispose(); $font.Dispose(); $fmt.Dispose()
        }
        "update" {
            $p = New-Pen "#1976D2" (3*$s)
            $g.DrawArc($p, 6*$s, 6*$s, 20*$s, 20*$s, 25, 250)
            $g.DrawLine($p, 24*$s, 7*$s, 27*$s, 13*$s)
            $g.DrawLine($p, 24*$s, 7*$s, 18*$s, 8*$s)
            $p.Dispose()
        }
        "formwork" {
            $fill = New-Brush "#E8D7B7"; $line = New-Pen "#8A6D3B" (1.4*$s)
            Draw-RoundRect $g (5*$s) (6*$s) (22*$s) (20*$s) (2*$s) $fill $line
            foreach ($x in 11,17,23) { $g.DrawLine($line, $x*$s, 7*$s, $x*$s, 25*$s) }
            foreach ($y in 12,18) { $g.DrawLine($line, 6*$s, $y*$s, 26*$s, $y*$s) }
            $fill.Dispose(); $line.Dispose()
        }
        "formwork_delete" {
            $fill = New-Brush "#E8D7B7"; $line = New-Pen "#8A6D3B" (1.4*$s)
            Draw-RoundRect $g (5*$s) (6*$s) (22*$s) (20*$s) (2*$s) $fill $line
            foreach ($x in 11,17,23) { $g.DrawLine($line, $x*$s, 7*$s, $x*$s, 25*$s) }
            foreach ($y in 12,18) { $g.DrawLine($line, 6*$s, $y*$s, 26*$s, $y*$s) }
            $p = New-Pen "#D32F2F" (3*$s)
            $g.DrawLine($p, 20*$s, 20*$s, 28*$s, 28*$s)
            $g.DrawLine($p, 28*$s, 20*$s, 20*$s, 28*$s)
            $fill.Dispose(); $line.Dispose(); $p.Dispose()
        }
        "formwork_pick" {
            $fill = New-Brush "#D6F5E3"; $line = New-Pen "#2E7D32" (1.5*$s)
            Draw-RoundRect $g (5*$s) (5*$s) (18*$s) (16*$s) (2*$s) $fill $line
            $p = New-Pen "#2E7D32" (2.2*$s)
            $g.DrawLine($p, 18*$s, 16*$s, 27*$s, 25*$s)
            $g.DrawLine($p, 22*$s, 24*$s, 27*$s, 25*$s)
            $fill.Dispose(); $line.Dispose(); $p.Dispose()
        }
        "export_csv" { Draw-Doc $g $s "#1565C0" "CSV" "out" }
        "structural_analysis" {
            $p = New-Pen "#6A1B9A" (2*$s)
            $g.DrawLine($p, 7*$s, 25*$s, 7*$s, 7*$s)
            $g.DrawLine($p, 7*$s, 7*$s, 23*$s, 7*$s)
            $g.DrawLine($p, 23*$s, 7*$s, 23*$s, 25*$s)
            $g.DrawLine($p, 7*$s, 16*$s, 23*$s, 16*$s)
            $g.DrawLine($p, 7*$s, 25*$s, 23*$s, 7*$s)
            $q = New-Pen "#F9A825" (2.6*$s)
            $g.DrawEllipse($q, 15*$s, 15*$s, 9*$s, 9*$s)
            $g.DrawLine($q, 22*$s, 22*$s, 28*$s, 28*$s)
            $p.Dispose(); $q.Dispose()
        }
        "finishings" {
            $p = New-Pen "#00897B" (3*$s)
            $g.DrawRectangle($p, 5*$s, 8*$s, 17*$s, 9*$s)
            $g.DrawLine($p, 22*$s, 13*$s, 27*$s, 13*$s)
            $g.DrawLine($p, 27*$s, 13*$s, 27*$s, 25*$s)
            $p.Dispose()
        }
        "face_to_face" {
            $p = New-Pen "#00897B" (2*$s); $b = New-Brush "#DFF7F3"
            Draw-RoundRect $g (5*$s) (8*$s) (13*$s) (17*$s) (2*$s) $b $p
            Draw-RoundRect $g (14*$s) (5*$s) (13*$s) (17*$s) (2*$s) $b $p
            $g.DrawLine($p, 11*$s, 15*$s, 21*$s, 15*$s)
            $p.Dispose(); $b.Dispose()
        }
        "refresh_finish_params" {
            $p = New-Pen "#00897B" (2.2*$s)
            foreach ($y in 8,16,24) { $g.DrawLine($p, 6*$s, $y*$s, 26*$s, $y*$s) }
            $g.FillEllipse((New-Brush "#FFFFFF"), 11*$s, 5*$s, 6*$s, 6*$s)
            $g.FillEllipse((New-Brush "#FFFFFF"), 18*$s, 13*$s, 6*$s, 6*$s)
            $g.FillEllipse((New-Brush "#FFFFFF"), 8*$s, 21*$s, 6*$s, 6*$s)
            $p.Dispose()
        }
        "change_finishing_color" {
            $p = New-Pen "#00897B" (2.5*$s)
            $b = New-Brush "#26A69A"
            $g.DrawLine($p, 8*$s, 9*$s, 19*$s, 20*$s)
            $g.FillPolygon($b, @(
                (New-PointF (18*$s) (18*$s)),
                (New-PointF (27*$s) (18*$s)),
                (New-PointF (22*$s) (27*$s))
            ))
            $p.Dispose(); $b.Dispose()
        }
        "auto_join" {
            $p = New-Pen "#546E7A" (4*$s)
            $a = New-Pen "#1976D2" (2*$s)
            $g.DrawLine($p, 6*$s, 16*$s, 26*$s, 16*$s)
            $g.DrawLine($p, 16*$s, 6*$s, 16*$s, 26*$s)
            $g.DrawEllipse($a, 10*$s, 10*$s, 12*$s, 12*$s)
            $p.Dispose(); $a.Dispose()
        }
        "split_floor" {
            $b = New-Brush "#ECEFF1"; $p = New-Pen "#455A64" (1.5*$s); $c = New-Pen "#D32F2F" (2.4*$s)
            Draw-RoundRect $g (4*$s) (12*$s) (24*$s) (9*$s) (1.5*$s) $b $p
            $g.DrawLine($c, 16*$s, 7*$s, 16*$s, 26*$s)
            $g.DrawLine($c, 12*$s, 10*$s, 20*$s, 24*$s)
            $b.Dispose(); $p.Dispose(); $c.Dispose()
        }
        "split_wall" {
            $b = New-Brush "#ECEFF1"; $p = New-Pen "#455A64" (1.5*$s); $c = New-Pen "#D32F2F" (2.4*$s)
            Draw-RoundRect $g (10*$s) (4*$s) (12*$s) (24*$s) (1.5*$s) $b $p
            $g.DrawLine($c, 5*$s, 16*$s, 27*$s, 16*$s)
            $g.DrawLine($c, 9*$s, 12*$s, 23*$s, 20*$s)
            $b.Dispose(); $p.Dispose(); $c.Dispose()
        }
        "pipe_sleeve" {
            Draw-Pipe $g $s 5 16 27 16 "#546E7A" 7
            $p = New-Pen "#FF9800" (3*$s)
            $g.DrawEllipse($p, 10*$s, 9*$s, 12*$s, 14*$s)
            $p.Dispose()
        }
        "auto_avoid" {
            Draw-Pipe $g $s 5 22 12 22 "#546E7A" 6
            Draw-Pipe $g $s 12 22 18 10 "#546E7A" 6
            Draw-Pipe $g $s 18 10 27 10 "#546E7A" 6
            $p = New-Pen "#F57C00" (2*$s)
            $g.DrawRectangle($p, 13*$s, 14*$s, 6*$s, 6*$s)
            $p.Dispose()
        }
        "pipe_center_align" {
            Draw-Pipe $g $s 4 23 28 23 "#546E7A" 6
            Draw-Pipe $g $s 13 21 25 7 "#546E7A" 6
            $p = New-Pen "#1976D2" (2*$s)
            $g.DrawLine($p, 5*$s, 23*$s, 28*$s, 23*$s)
            $g.DrawLine($p, 13*$s, 21*$s, 25*$s, 7*$s)
            $p.Dispose()
        }
        "pipe_center_align_settings" {
            Draw-Pipe $g $s 4 23 28 23 "#546E7A" 6
            Draw-Pipe $g $s 13 21 25 7 "#546E7A" 6
            $axis = New-Pen "#1976D2" (2*$s)
            $g.DrawLine($axis, 5*$s, 23*$s, 28*$s, 23*$s)
            $g.DrawLine($axis, 13*$s, 21*$s, 25*$s, 7*$s)
            $axis.Dispose()
            $p = New-Pen "#263238" (1.6*$s)
            $g.DrawEllipse($p, 20*$s, 20*$s, 9*$s, 9*$s)
            foreach ($a in 0,60,120) {
                $rad = [Math]::PI * $a / 180
                $cx = 24.5*$s; $cy = 24.5*$s
                $g.DrawLine($p, $cx, $cy, $cx + [Math]::Cos($rad)*6*$s, $cy + [Math]::Sin($rad)*6*$s)
            }
            $p.Dispose()
        }
        "pipe_iso" {
            Draw-Doc $g $s "#0D47A1" "ISO" "out"
            Draw-Pipe $g $s 9 8 18 14 "#0D47A1" 3
            Draw-Pipe $g $s 18 14 25 10 "#0D47A1" 3
        }
        "family_slider" {
            $p = New-Pen "#7B1FA2" (2*$s)
            foreach ($x in 8,16,24) { $g.DrawLine($p, $x*$s, 6*$s, $x*$s, 26*$s) }
            $g.FillEllipse((New-Brush "#CE93D8"), 5*$s, 11*$s, 6*$s, 6*$s)
            $g.FillEllipse((New-Brush "#CE93D8"), 13*$s, 17*$s, 6*$s, 6*$s)
            $g.FillEllipse((New-Brush "#CE93D8"), 21*$s, 8*$s, 6*$s, 6*$s)
            $p.Dispose()
        }
        "project_slider" {
            $p = New-Pen "#5E35B1" (2*$s)
            $g.DrawPolygon($p, @(
                (New-PointF (6*$s) (14*$s)),
                (New-PointF (16*$s) (6*$s)),
                (New-PointF (26*$s) (14*$s)),
                (New-PointF (26*$s) (27*$s)),
                (New-PointF (6*$s) (27*$s))
            ))
            $g.DrawLine($p, 10*$s, 18*$s, 22*$s, 18*$s)
            $g.FillEllipse((New-Brush "#B39DDB"), 14*$s, 15*$s, 6*$s, 6*$s)
            $p.Dispose()
        }
        "cobie_field" { Draw-Doc $g $s "#2E7D32" "" "" }
        "cobie_template" { Draw-Doc $g $s "#0288D1" "T" "out" }
        "cobie_export" { Draw-Doc $g $s "#1565C0" "CO" "out" }
        "cobie_import" { Draw-Doc $g $s "#2E7D32" "CO" "in" }
        "schedule_export" { Draw-Doc $g $s "#1565C0" "XLS" "out" }
    }

    $path = Join-Path $outDir "$($name)_$size.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
}

$icons = @(
    "license", "about", "update",
    "formwork", "formwork_delete", "formwork_pick", "export_csv", "structural_analysis",
    "finishings", "face_to_face", "refresh_finish_params", "change_finishing_color",
    "auto_join", "split_floor", "split_wall",
    "pipe_sleeve", "auto_avoid", "pipe_center_align", "pipe_center_align_settings", "pipe_iso",
    "family_slider", "project_slider",
    "cobie_field", "cobie_template", "cobie_export", "cobie_import", "schedule_export"
)

New-Item -ItemType Directory -Path $scriptDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerIconDir -Force | Out-Null

foreach ($icon in $icons) {
    foreach ($size in 16, 32) {
        Draw-Icon $icon $size $scriptDir
        Copy-Item (Join-Path $scriptDir "$($icon)_$size.png") (Join-Path $installerIconDir "$($icon)_$size.png") -Force
    }
}

Write-Host "Generated $($icons.Count * 2) icons in project and installer resources."
