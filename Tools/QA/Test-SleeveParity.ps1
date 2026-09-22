param([Parameter(Mandatory=$true)][string]$Repo)
$ErrorActionPreference='Stop'
$mep=Join-Path $Repo 'Commands/MEP'
$cmd=Get-Content (Join-Path $mep 'CmdPipeSleeve.cs') -Raw
$wpf=Get-Content (Join-Path $mep 'PipeSleeveWindow.xaml.cs') -Raw
$xaml=Get-Content (Join-Path $mep 'PipeSleeveWindow.xaml') -Raw
$form=Get-Content (Join-Path $mep 'PipeSleeveSettingsForm.cs') -Raw
$service=Get-Content (Join-Path $mep 'PipeSleeveService.cs') -Raw
$catalog=Get-Content (Join-Path $mep 'SleeveFamilyCatalog.cs') -Raw
$settings=Get-Content (Join-Path $mep 'PipeSleeveSettings.cs') -Raw
$checks=0
function Check([bool]$ok,[string]$name) { if(!$ok){throw $name}; $script:checks++ }
Check ($cmd.Contains('SleeveFamilyCatalog.LoadMissing(doc)')) 'WinForms shared loader missing'
Check ($wpf.Contains('SleeveFamilyCatalog.LoadMissing(_doc)')) 'WPF shared loader missing'
Check ($service.Contains('SleeveFamilyCatalog.LoadMissing(doc)')) 'Creation shared loader missing'
Check ($cmd.IndexOf('LoadMissingPresetFamilies(doc);') -lt $cmd.IndexOf('new PipeSleeveSettingsForm')) 'WinForms loads after UI'
Check ($wpf.IndexOf('EnsureDefaultFamiliesLoaded();') -lt $wpf.IndexOf('var symbols = new FilteredElementCollector')) 'WPF loads after choices'
Check ($cmd.Contains('.Where(SleeveFamilyCatalog.IsSleeveSymbol)')) 'WinForms filter drift'
Check ($wpf.Contains('=> SleeveFamilyCatalog.IsSleeveSymbol(symbol)')) 'WPF filter drift'
Check (!$wpf.Contains('OverwriteSleeveFamilyLoadOptions') -and !$service.Contains('OverwriteSleeveFamilyLoadOptions')) 'Old overwriting loader returned'
Check ($catalog.Contains('if (loaded.Contains(name)) continue;')) 'Existing families not protected'
Check ($catalog.Contains('source = FamilySource.Project; overwriteParameterValues = false;')) 'Shared family overwrite risk'
Check ($settings.Contains('bool LimitToActiveView { get; set; } = true;')) 'View default drift'
Check ($wpf.Contains('LimitToActiveView = chkLimitToActiveView.IsChecked == true')) 'WPF view setting missing'
Check ($form.Contains('view.Checked=saved.LimitToActiveView;')) 'WinForms view setting not restored'
Check ($form.Contains('LimitToActiveView=LimitToActiveView')) 'WinForms view setting not saved'
Check ($xaml -match '<Expander[^>]*IsExpanded="False"') 'WPF mapping not collapsed'
Check ($form.Contains('RowCount=3,Visible=false')) 'WinForms mapping not collapsed'
Check ($form.Contains('MaxDropDownItems=10')) 'Mapping dropdown unbounded'
Check ($form.Contains('mappingNote.MaximumSize=')) 'Mapping note wrapping missing'
Write-Output "PASS: $checks source-contract checks. These do not replace Revit runtime or DPI tests."
