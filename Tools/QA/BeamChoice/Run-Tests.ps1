param([Parameter(Mandatory=$true)][string]$Repo)
$ErrorActionPreference='Stop'
$work=Join-Path ([IO.Path]::GetTempPath()) ('HB-BeamChoice-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
Copy-Item "$PSScriptRoot/Program.cs.txt" "$work/Program.cs"
Copy-Item "$PSScriptRoot/Tests.csproj.txt" "$work/Tests.csproj"
Copy-Item "$Repo/Commands/MEP/MepBeamChoice.cs" $work
$source=Get-Content "$Repo/Commands/MEP/MepAutomaticBeamDimension.cs" -Raw
$a=$source.IndexOf('        private static List<BeamMatch> RankMatches(')
$b=$source.IndexOf('        private static void ReadSides(', $a)
if($a -lt 0 -or $b -lt 0){throw 'Ranking method not found'}
[IO.File]::WriteAllText("$work/Rank.generated.cs",'using System; using System.Linq; using System.Collections.Generic; using Autodesk.Revit.DB; namespace YD_RevitTools.LicenseManager.Commands.MEP { public partial class CmdMepPositionDimension {'+$source.Substring($a,$b-$a)+'}}')
dotnet run --project "$work/Tests.csproj"
if($LASTEXITCODE -ne 0){throw "Test failed: $work"}
