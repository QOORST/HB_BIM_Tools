# PowerShell script to fix ElementId.Value compatibility for Revit 2022-2026
# Replaces .Value with .GetIdValue() extension method

$projectRoot = Split-Path -Parent $PSScriptRoot
Write-Host "Project root: $projectRoot" -ForegroundColor Cyan

# Files to process (based on compilation errors)
$filesToProcess = @(
    "UI\Finishings\MainWindow.xaml.cs",
    "Commands\AR\Formwork\VisualEffectsManager.cs",
    "Commands\AR\Finishings\CmdFaceToFace.cs",
    "Commands\AR\Formwork\AreaCalculator.cs",
    "Commands\MEP\PipeToISO\Services\ScheduleGenerator.cs",
    "Commands\MEP\PipeToISO\Command.cs",
    "Helpers\AR\AutoJoin\AutoJoinEngine.cs",
    "Commands\AR\Formwork\CmdMain.cs",
    "Commands\AR\Formwork\CmdStructuralAnalysis.cs",
    "Helpers\AR\Finishings\ValueWriter.cs",
    "Commands\AR\Formwork\StructuralFormworkAnalyzer.cs",
    "Helpers\AR\AutoJoin\JoinGeometryHelper.cs",
    "Commands\MEP\PipeToISO\Services\ISOGenerator.cs",
    "Commands\AR\Formwork\CmdPickFace.cs",
    "Commands\AR\Formwork\FormworkQuantityCalculator.cs",
    "Commands\AR\Formwork\ElementCategorizer.cs",
    "Commands\MEP\AutoAvoid\Core\ClashDetector.cs",
    "Commands\AR\Formwork\CmdExportCsv.cs",
    "Commands\MEP\PipeToISO\Services\PipeAnalyzer.cs",
    "Commands\AR\Formwork\FormworkEngine.cs",
    "Commands\AR\Formwork\ImprovedFormworkEngine.cs",
    "Helpers\AR\Finishings\GeometryGenerator.cs",
    "Commands\AR\Formwork\GeometryExtractor.cs",
    "Helpers\AR\Finishings\GeometryGenerator.cs"
)

$totalFiles = 0
$totalReplacements = 0

foreach ($relPath in $filesToProcess) {
    $fullPath = Join-Path $projectRoot $relPath
    
    if (-not (Test-Path $fullPath)) {
        Write-Host "⚠️  File not found: $relPath" -ForegroundColor Yellow
        continue
    }
    
    $content = Get-Content $fullPath -Raw -Encoding UTF8
    $originalContent = $content
    
    # Replace .Value with .GetIdValue()
    # Pattern: ElementId.Value or id.Value or element.Id.Value etc.
    $content = $content -replace '\.Value(?=\s*[;\)\.,\]])', '.GetIdValue()'
    
    # Add using statement if not present and replacements were made
    if ($content -ne $originalContent) {
        if ($content -notmatch 'using YD_RevitTools\.LicenseManager\.Helpers;') {
            # Find the last using statement
            if ($content -match '(?s)(using [^;]+;)(?!.*using [^;]+;)') {
                $lastUsing = $matches[1]
                $content = $content -replace [regex]::Escape($lastUsing), "$lastUsing`nusing YD_RevitTools.LicenseManager.Helpers;"
            }
        }
        
        # Count replacements
        $replacements = ([regex]::Matches($originalContent, '\.Value(?=\s*[;\)\.,\]])').Count)
        $totalReplacements += $replacements
        $totalFiles++
        
        # Save the file
        Set-Content $fullPath -Value $content -Encoding UTF8 -NoNewline
        Write-Host "✅ $relPath - $replacements replacements" -ForegroundColor Green
    }
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  Files processed: $totalFiles" -ForegroundColor Green
Write-Host "  Total replacements: $totalReplacements" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan

