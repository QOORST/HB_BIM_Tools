"""Explicit integration run: native CAD 2D/3D + real Windows OCR + frozen build packet."""
from pathlib import Path
import os
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'Tools/EquipmentFamily'))
import workflow as w

root=Path(sys.argv[1]).resolve();root.mkdir(parents=True,exist_ok=False)
fixtures=root/'fixtures';fixtures.mkdir()
import ezdxf
for dim in ['2d','3d']:
    d=ezdxf.new('R2018');d.units=4;m=d.modelspace();m.add_line((0,0),(300,0));m.add_text('PUMP-DEMO-01 300 mm',dxfattribs={'height':10,'insert':(0,250)})
    if dim=='3d':m.add_3dface([(0,0,0),(300,0,0),(300,200,100),(0,200,100)])
    d.saveas(fixtures/f'equipment-{dim}.dxf')
    script=fixtures/f'create-{dim}.scr'
    solid='_.BOX\n0,0,0\n_Length\n300\n200\n100\n' if dim=='3d' else ''
    script.write_text(f'_.DXFIN\n"{(fixtures/f"equipment-{dim}.dxf").as_posix()}"\n{solid}_.QSAVE\n"{(fixtures/f"equipment-{dim}.dwg").as_posix()}"\n_.QUIT\n',encoding='utf-8')
    r=w.execute([r'C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe','/s',script],timeout=180)
    (fixtures/f'{dim}-console.log').write_bytes(r.stdout+r.stderr)
    if not (fixtures/f'equipment-{dim}.dwg').is_file():raise RuntimeError('Native CAD fixture conversion failed')
results=[]
for name in ['equipment-2d.dwg','equipment-2d.dxf','equipment-3d.dwg','equipment-3d.dxf']:
    case=w.new_case('pump',[fixtures/name],root/name.replace('.','-'))
    m=w.read(case/'case.json');data=w.read(case/m['sources'][0]['analysis'])
    if data['status']!='reference_extracted':raise RuntimeError(str(data))
    if '3d' in name and data['geometry_kind']!='contains_3d_geometry':raise RuntimeError('3D classification failed')
    if name=='equipment-3d.dwg' and data['entity_counts'].get('3DSOLID',0)!=1:raise RuntimeError('ACIS solid detection failed')
    if not any('PUMP-DEMO-01' in t['text'] for t in data['texts']):raise RuntimeError('CAD text missing')
    results.append({'file':name,'status':data['status'],'geometry_kind':data['geometry_kind'],'units':data['drawing_units']})
    print(name,data['geometry_kind'],flush=True)
image=w.ROOT/'tmp/pdfs/evergush-ef-4.png'
case=w.new_case('pump',[image],root/'pump-workflow')
m=w.read(case/'case.json');data=w.read(case/m['sources'][0]['analysis'])
if data['status']!='reference_extracted' or 'TOS-EF-05' not in data['pages'][0]['text']:raise RuntimeError('Real OCR failed')
w.set_recipe(case,w.RECIPE)
w.review(case,'前次試作資料覆核沿用','使用先前已核對的 EF 第4頁首兩個型號資料；本次 OCR 僅作線索，不取代既有尺寸。',True)
packet=w.prepare(case)
w.write(root/'native-results.json',{'cad':results,'ocr':'passed_zh-Hant-TW','packet':str(packet),'rfa_status':'not_run_yet'})
print('PACKET',packet,flush=True)
