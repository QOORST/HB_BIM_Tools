"""Source contracts only: these do not execute PowerShell, MSBuild or Revit."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
PREP = (ROOT / 'Installer/Prepare_Files_Simple.ps1').read_text(encoding='utf-8-sig')
BUILD = (ROOT / 'Installer/Build_Installer.ps1').read_text(encoding='utf-8-sig')

class ReleaseSourceContracts(unittest.TestCase):
    def test_no_cross_year_or_installed_binary_fallback(self):
        for forbidden in ('FallbackVersion', 'Release2025\\YD_RevitTools', 'C:\\ProgramData', 'Resolve-FamilyLibraryDll'):
            self.assertNotIn(forbidden, PREP)
        self.assertIn('$BuildRoot "$version\\main\\YD_RevitTools.LicenseManager.dll"', PREP)
        self.assertIn('$BuildRoot "$version\\family\\CompanyFamilyLibraryMvp.dll"', PREP)

    def test_all_years_build_both_projects_before_staging(self):
        self.assertIn("@('2022', '2024', '2025', '2026')", BUILD)
        self.assertIn("foreach ($kind in @('main', 'family'))", BUILD)
        self.assertIn('-t:Rebuild -c "Release$year"', BUILD)
        self.assertIn('-p:RevitVersion=$year --output $output', BUILD)
        self.assertIn('[Guid]::NewGuid()', BUILD)
        self.assertLess(BUILD.index('& dotnet build'), BUILD.index('& $prepareScript -BuildRoot'))
        self.assertIn("if ($LASTEXITCODE -ne 0) { throw", BUILD)

    def test_receipt_covers_exact_year_configuration_and_hash(self):
        self.assertIn("'build-receipt.json'", PREP)
        self.assertIn('$entry.Count -ne 1', PREP)
        self.assertIn('$entry[0].Configuration -ne "Release$version"', PREP)
        self.assertIn('.Hash -ne $entry[0].Sha256', PREP)

    def test_missing_dependencies_are_fatal_before_staging(self):
        self.assertNotIn('[WARNING]', PREP)
        for marker in ('Required dependency missing', 'Shared dependency differs',
                       'Required x64 SQLite runtime missing', 'Required Revit 2025/2026 dependency missing'):
            self.assertIn('throw "' + marker, PREP)
            self.assertLess(PREP.index(marker), PREP.index('# Create shared resources'))

    def test_versions_are_checked_without_rewriting(self):
        self.assertIn('$version -ne $issVersion -or $version -ne $assemblyVersion', BUILD)
        self.assertIn('$fileVersion -ne "$version.0"', BUILD)
        self.assertNotRegex(BUILD, r'Set-Content[^\n]*(?:version\.json|AssemblyInfo|\.iss)')

    def test_only_fresh_installer_can_be_selected(self):
        self.assertIn('$_.LastWriteTimeUtc -ge $compileStartedUtc', BUILD)
        self.assertIn('$setupFiles.Count -ne 1', BUILD)
        self.assertIn('HB_BIM_Tools_v${version}*_Setup.exe', BUILD)
        self.assertNotIn('foreach ($version in', BUILD)

    def test_truthful_documented_exclusions(self):
        doc = (ROOT / 'Docs/qa/capability-matrix.md').read_text(encoding='utf-8-sig')
        for term in ('2022', '2024', '2025', '2026', 'AutoPipeRouting', 'PipeToISO', 'net48', '2.5.17', '2.5.14', '2.5.12'):
            self.assertIn(term, doc)
        project = (ROOT / 'YD_RevitTools.LicenseManager.csproj').read_text(encoding='utf-8-sig')
        self.assertIn('<Compile Remove="Commands\\AR\\Finishings\\RoomFinish\\CmdRoomFinish.cs" />', project)
        self.assertIn('#if !REVIT2025 && !REVIT2026', (ROOT / 'App.cs').read_text(encoding='utf-8-sig'))

if __name__ == '__main__':
    unittest.main()
