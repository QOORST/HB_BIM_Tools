param([Parameter(Mandatory=$true)][string]$SourcePath)
$source = [IO.File]::ReadAllText($SourcePath)
$start = $source.IndexOf('    public class UiVm', [StringComparison]::Ordinal)
$end = $source.IndexOf('    class HostFilter', $start, [StringComparison]::Ordinal)
$progress = $source.IndexOf('    public class ProgressWindow : WpfWindow', [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -lt 0 -or $progress -lt 0) { throw 'Source markers missing.' }
$vm = $source.Substring($start, $end - $start)
$hostStart = $vm.IndexOf('        public IList<Element> GetHostElements()', [StringComparison]::Ordinal)
$hostEnd = $vm.IndexOf('        internal void RaiseRunStarted', $hostStart, [StringComparison]::Ordinal)
if ($hostStart -lt 0 -or $hostEnd -lt 0) { throw 'Host query markers missing.' }
# Only remove host geometry querying; settings, controls and lifecycle methods remain verbatim.
$vm = $vm.Remove($hostStart, $hostEnd - $hostStart)
$aliases = [regex]::Matches($source, '(?m)^using Wpf[^\r\n]+;') | ForEach-Object { $_.Value }
$header = "// Generated from CmdMain.cs; Revit services are offline test doubles.`nusing System;`nusing System.Collections.Generic;`nusing System.Globalization;`nusing System.Linq;`nusing Autodesk.Revit.DB;`nusing Autodesk.Revit.UI;`n" + ($aliases -join "`n") + "`nnamespace YD_RevitTools.LicenseManager.Commands.AR.Formwork`n{`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'FormworkUi.generated.cs'), $header + $vm + $source.Substring($progress), [Text.UTF8Encoding]::new($false))
