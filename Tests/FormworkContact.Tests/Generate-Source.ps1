param([string]$Source, [string]$Output)
$ErrorActionPreference = 'Stop'
$text = [IO.File]::ReadAllText($Source)
$marker = '        public static Solid ApplySmartContactDeduction('
$start = $text.IndexOf($marker, [StringComparison]::Ordinal)
if ($start -lt 0 -or $text.LastIndexOf($marker, [StringComparison]::Ordinal) -ne $start) { throw 'Contact method marker changed' }
# This is the final method in GeometryExtractor; retain its original closing braces.
$prefix = @'
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;
namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork {
public static class GeometryExtractor {
public const double FINE_TOLERANCE = 1e-9;
private static List<Solid> GetElementSolids(Element e) => e.Solids;
'@
[IO.File]::WriteAllText($Output, $prefix + [Environment]::NewLine + $text.Substring($start))
