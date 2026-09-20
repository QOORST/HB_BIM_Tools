"""Freeze the tested Revit 2024 files and create a local review gallery. Does not deploy."""
from pathlib import Path
from datetime import datetime
import hashlib
import html
import json
import shutil

root = Path(__file__).resolve().parents[1]
release = root / 'artifacts/tool-suite-release' / datetime.now().strftime('%Y%m%d-%H%M%S')
relative = ['YD_RevitTools.LicenseManager.dll', 'YD_RevitTools.LicenseManager.pdb']
for name in ['manual_offset','mep_position_dimension','mep_position_settings','mep_from_connector','mep_level_rebase','tag_align','tag_related']:
    relative.extend(f'Resources/Icons/{name}_{size}.png' for size in [16,32])
install = Path('C:/ProgramData/Autodesk/Revit/Addins/2024/HB_BIM')
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest().upper()
entries = []
for name in relative:
    source = root / 'bin/Release2024' / name
    target = release / 'files' / name
    target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(source,target)
    assert digest(source) == digest(target)
    installed = install / name
    entries.append({'file':name,'sha256':digest(target),'bytes':target.stat().st_size,
                    'installedSha256':digest(installed) if installed.is_file() else None})
(release/'manifest.json').write_text(json.dumps({'target':'Revit 2024','deployed':False,'files':entries},indent=2),encoding='utf-8')
script = (root/'Tools/deploy-tool-suite.ps1').read_text(encoding='utf-8-sig')
script = script.replace("$sourceRoot = 'C:\\HB\\_BIM\\HB_BIM_Tools\\bin\\Release2024'", "$sourceRoot = Join-Path $PSScriptRoot 'files'")
validation = '''$expected = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $expected.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $sourceRoot $file.file)).Hash -ne $file.sha256) {
        throw "Staged package changed: $($file.file)"
    }
}
'''
script = script.replace('$entries = @()',validation+'$entries = @()')
(release/'deploy.ps1').write_text(script,encoding='utf-8-sig')
shutil.copy2(root/'Docs/qa/tool-suite-review-20260919.md',release/'review.md')
shutil.copy2(root/'Docs/qa/tool-suite-acceptance.md',release/'acceptance.md')
gallery = root/'artifacts/tool-suite-qa/review.html'
cards=[]
for title,name in [('尺寸文字整理（離線介面）','dimension-text-cleanup.png'),('標籤對齊','tag-align.png'),('自動接合（非模態，離線測試）','auto-join-modeless.png'),('牆輪廓對齊','wall-align.png'),('族參數名稱修改（測試資料）','family-rename.png'),('滑桿設定','slider-settings.png'),('關聯標籤／對齊圖示','tag-icons.png')]:
    cards.append(f'<article><h2>{html.escape(title)}</h2><img src="{name}" alt="{html.escape(title)}"></article>')
gallery.write_text('''<!doctype html><html lang="zh-Hant"><meta charset="utf-8"><title>HB_BIM 集中修正預覽</title>
<style>body{font-family:"Microsoft JhengHei UI",sans-serif;background:#f3f5f7;color:#24303c;margin:32px auto;max-width:1200px;padding:0 24px}h1{font-size:26px}h2{font-size:18px}p{line-height:1.8}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(360px,1fr));gap:24px}article{background:white;padding:20px;border:1px solid #dce2e8}img{max-width:100%;height:auto}a{color:#0069b4}</style>
<h1>HB_BIM 集中修正預覽</h1><p>以下是既有工具介面的離線預覽，族參數改名使用測試資料；不涵蓋模板／面生面的 Revit 交易與幾何驗證。各批測試結果及部署狀態請以驗收清單為準。</p>
<p><a href="../ui-audit/refined/bubbles-False-1120.png">自動標註清單新版</a> · <a href="../../Docs/qa/tool-suite-acceptance.md">部署後驗收清單</a></p><main>'''+''.join(cards)+'</main></html>',encoding='utf-8')
print(json.dumps({'package':str(release),'files':len(entries),'differentFromInstalled':sum(e['sha256']!=e['installedSha256'] for e in entries),'gallery':str(gallery)},ensure_ascii=True))
