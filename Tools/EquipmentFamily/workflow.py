"""Local evidence intake and reviewed-template routing. No inferred dimensions are build inputs."""
from __future__ import annotations
import argparse
import hashlib
import html
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
if (HERE/'.deps').is_dir(): sys.path.insert(0,str(HERE/'.deps'))
RECIPE = ROOT / 'Docs/FamilyAutomation/pump-sample-001.input.json'
CATEGORIES = {
    'pump': '泵浦', 'fan': '風機', 'generator': '發電機', 'ahu': '空調箱',
    'panel': '配電盤', 'cooling_tower': '冷卻水塔', 'chiller': '冰水主機',
    'lighting': '燈具', 'fire': '消防器具', 'low_voltage': '弱電器具',
}
TEMPLATE = 'evergush_tos_ef_05_21'
AHU_TEMPLATE = 'teco_ahu_hs'
AHU_RECIPE = ROOT / 'Docs/FamilyAutomation/ahu-teco-hs.input.json'
IMAGES = {'.png', '.jpg', '.jpeg', '.bmp', '.tif', '.tiff', '.webp'}
CAD = {'.dwg', '.dxf'}
NEUTRAL_CAD = {'.step', '.stp', '.sat', '.igs', '.iges'}
SUPPORTED = IMAGES | CAD | NEUTRAL_CAD | {'.pdf'}
STATUS_LABELS={'reference_extracted':'已擷取，需對照原圖','needs_review':'需人工補足','needs_adapter':'需格式轉換或環境設定','failed':'解析失敗'}

def now(): return datetime.now(timezone.utc).isoformat()
def digest(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for chunk in iter(lambda: f.read(1024*1024), b''): h.update(chunk)
    return h.hexdigest()
def object_hash(obj): return hashlib.sha256(json.dumps(obj, sort_keys=True, ensure_ascii=False, allow_nan=False).encode()).hexdigest()
def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def write(path, value):
    path = Path(path)
    temp = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
    os.replace(temp, path)
def local(case, relative):
    case = Path(case).resolve()
    p = (case / relative).resolve()
    if p == case or case not in p.parents: raise ValueError('工作檔路徑超出工作資料夾')
    return p
def execute(args, *, env=None, timeout=120):
    flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
    return subprocess.run([str(a) for a in args], env=env, timeout=timeout, capture_output=True, creationflags=flags)

def ocr(image, output):
    powershell = Path(os.environ.get('WINDIR', r'C:\Windows')) / 'System32/WindowsPowerShell/v1.0/powershell.exe'
    if not powershell.is_file(): return {'status': 'unavailable', 'reason': 'Windows OCR 不可用'}
    result = execute([powershell, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', HERE/'ocr.ps1', '-ImagePath', image, '-OutputPath', output])
    if result.returncode or not Path(output).is_file():
        return {'status': 'unavailable', 'reason': result.stderr.decode('utf-8', 'replace')[-1500:]}
    return read(output)

def candidates(text):
    """Only clues, never automatic mapping to a type's dimensions or electrical input."""
    matches = []
    for match in re.finditer(r'(?i)(?:\b[A-Z]{2,8}(?:-[A-Z0-9]+){1,4}\b)|(?:\d+(?:\.\d+)?\s*(?:mm|cm|inch|HP|kW|Hz|L/min|LPM|V|kg)\b)', text):
        matches.append({'text': match.group(), 'excerpt': text[max(0, match.start()-25):match.end()+25], 'review': 'unreviewed'})
        if len(matches) >= 300: break
    return matches

def image_analysis(source, folder):
    from PIL import Image, ImageOps
    pages = []
    with Image.open(source) as img:
        frames = getattr(img, 'n_frames', 1)
        for index in range(min(frames, 32)):
            img.seek(index)
            normalized = ImageOps.exif_transpose(img.copy()).convert('RGB')
            original_size = normalized.size
            normalized.thumbnail((2400, 2400))
            preview = folder / f'page-{index+1}.png'; normalized.save(preview)
            result = ocr(preview, folder/f'ocr-{index+1}.json')
            pages.append({'page': index+1, 'method': 'ocr', 'preview': preview.name, 'original_pixels': original_size,
                          'text': result.get('text', ''), 'ocr_status': result['status'], 'ocr_language': result.get('language'),
                          'clues': candidates(result.get('text', '')), 'reason': result.get('reason')})
    return {'status': 'reference_extracted' if all(p['ocr_status']=='extracted_unreviewed' and p['text'].strip() for p in pages) and frames<=32 else 'needs_review',
            'kind': 'image', 'pages': pages, 'total_pages': frames, 'complete': frames<=32,
            'notes': ['影像尺寸是像素，不是設備實際尺寸；OCR 不自動填入幾何或接頭。']}

def pdf_analysis(source, folder):
    import pdfplumber
    import pypdfium2
    pages = []
    with pdfplumber.open(source) as pdf:
        raster = pypdfium2.PdfDocument(str(source))
        try:
            for index, page in enumerate(pdf.pages[:32]):
                raw = page.extract_text() or ''
                p = raster[index]
                bitmap = p.render(scale=min(2.5, 2400/max(p.get_size())))
                preview = folder/f'page-{index+1}.png'
                bitmap.to_pil().convert('RGB').save(preview)
                bitmap.close(); p.close()
                usable = len(raw.strip()) >= 30 and '\ufffd' not in raw and not re.search(r'\(cid:\d+\)', raw)
                result = None if usable else ocr(preview, folder/f'ocr-{index+1}.json')
                text = raw if usable else result.get('text', '')
                pages.append({'page': index+1, 'method': 'embedded_text' if usable else 'ocr', 'preview': preview.name,
                              'text': text, 'embedded_text': raw, 'ocr_status': None if usable else result['status'],
                              'clues': candidates(text), 'vector_line_count': len(page.lines), 'image_count': len(page.images)})
            return {'status': 'reference_extracted' if len(pdf.pages)<=32 and all(p['text'].strip() for p in pages) else 'needs_review',
                    'kind': 'pdf', 'pages': pages, 'total_pages': len(pdf.pages), 'complete': len(pdf.pages)<=32,
                    'notes': ['文字及向量線數是圖資證據，不代表已完成尺寸配對或三維重建。']}
        finally: raster.close()

def cad_analysis(source, folder):
    import ezdxf
    from ezdxf import bbox
    dxf=source
    if source.suffix.lower()=='.dwg':
        executable=Path(os.environ.get('HB_ACCORECONSOLE',r'C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe'))
        if not executable.is_file():return {'status':'needs_adapter','notes':['DWG 需安裝 AutoCAD 2024；DXF 可直接解析。']}
        dxf=folder/'converted.dxf';script=folder/'export.scr'
        target=dxf.as_posix()
        if '"' in target or '\n' in target:raise ValueError('Invalid export path')
        script.write_text(f'_.DXFOUT\n"{target}"\n16\n_.QUIT\n',encoding='utf-8')
        r=execute([executable,'/i',source,'/s',script],timeout=180)
        (folder/'cad-console.log').write_bytes(r.stdout+r.stderr)
        if not dxf.is_file():return {'status':'needs_adapter','notes':['DWG 轉換未完成，請查看 cad-console.log。未載入外掛或修改安全設定。']}
    drawing=ezdxf.readfile(dxf);space=drawing.modelspace();counts={};texts=[];dimensions=[];notes=[]
    if len(space)>100000:raise ValueError('超過 100000 個物件，請拆分設備圖')
    solids=0
    for e in space:
        kind=e.dxftype();counts[kind]=counts.get(kind,0)+1
        if kind in ('3DSOLID','BODY','REGION','SURFACE','MESH','3DFACE'):solids+=1
        if kind in ('TEXT','MTEXT','ATTRIB'):
            texts.append({'text':e.plain_text() if kind=='MTEXT' else e.dxf.text,'handle':e.dxf.handle,'layer':e.dxf.layer})
        if kind=='DIMENSION':
            try:
                measurement=e.get_measurement()
                dimensions.append({'measurement':float(measurement) if isinstance(measurement,(int,float)) else str(measurement),'text_override':e.dxf.text,'handle':e.dxf.handle,'layer':e.dxf.layer})
            except Exception:notes.append('無法讀取標註 '+str(e.dxf.handle))
        if kind=='INSERT':
            notes.append('圖塊文字／內部幾何未展開：'+e.dxf.name)
            for a in e.attribs:texts.append({'text':a.dxf.text,'tag':a.dxf.tag,'handle':a.dxf.handle,'layer':a.dxf.layer})
    bounds=bbox.extents(space,fast=True)
    notes.extend(['包圍盒僅涵蓋套件可計算的幾何，可能略過 ACIS 實體，不是設備尺寸。','3D 幾何只辨識類型，尚未轉成原生參數化構件。','外部參考不自動打包，需另行核對。'])
    return {'status':'reference_extracted','engine':'ezdxf 1.4.3'+(' + AutoCAD DXFOUT' if source.suffix=='.dwg' else ''),
            'drawing_units':{0:'Undefined',1:'Inches',2:'Feet',4:'Millimeters',5:'Centimeters',6:'Meters'}.get(drawing.units,str(drawing.units)),
            'geometry_kind':'contains_3d_geometry' if solids else '2d_or_unclassified','entity_counts':counts,'texts':texts,'dimensions':dimensions,
            'drawing_bounds':list(bounds.extmin)+list(bounds.extmax) if bounds.has_data else None,'notes':notes,'geometry_reconstruction':'not_performed'}

def new_case(category, files, destination, target=2024):
    if category not in CATEGORIES: raise ValueError('未知設備分類')
    if target not in (2024,2025,2026): raise ValueError('版本須為 2024～2026')
    if not files: raise ValueError('請至少選一份來源圖資')
    sources = [Path(f).resolve(strict=True) for f in files]
    for source in sources:
        if not source.is_file() or source.suffix.lower() not in SUPPORTED: raise ValueError(f'不支援的來源：{source.name}')
        if source.stat().st_size > 100*1024*1024: raise ValueError(f'檔案超過 100 MB，請先拆分：{source.name}')
    case = Path(destination).resolve(); case.mkdir(parents=True, exist_ok=False)
    (case/'sources').mkdir(); (case/'evidence').mkdir()
    manifest = {'schema_version':1, 'case_id':uuid.uuid4().hex, 'created':now(), 'category':category,
                'target_revit':target, 'template_id':None, 'sources':[], 'recipe':None, 'review':None}
    for index, source in enumerate(sources):
        sid = f's{index+1:03d}'; snapshot = case/'sources'/f'{sid}{source.suffix.lower()}'
        shutil.copyfile(source, snapshot)
        folder=case/'evidence'/sid; folder.mkdir()
        try:
            if snapshot.suffix=='.pdf': data=pdf_analysis(snapshot,folder)
            elif snapshot.suffix in IMAGES: data=image_analysis(snapshot,folder)
            elif snapshot.suffix in CAD: data=cad_analysis(snapshot,folder)
            else: data={'status':'needs_adapter','kind':'neutral_3d','notes':['此 3D 格式已保存，尚需格式轉換器；不能直接建族。']}
        except Exception as exc:
            data={'status':'failed','error':str(exc),'notes':['解析失敗；來源快照仍保留。']}
        write(folder/'analysis.json', data)
        manifest['sources'].append({'id':sid,'original_name':source.name,'snapshot':snapshot.relative_to(case).as_posix(),
            'sha256':digest(snapshot),'analysis':(folder/'analysis.json').relative_to(case).as_posix(),'status':data['status']})
        write(case/'case.json',manifest)
    report(case)
    return case

def set_recipe(case, recipe_file, template=None):
    case=Path(case); m=read(case/'case.json')
    template = template or read(recipe_file).get('template_id',TEMPLATE)
    if (m['category'],template) not in [('pump',TEMPLATE),('ahu',AHU_TEMPLATE)]: raise ValueError('此設備尚無可執行範本，已保留圖資供範本開發。')
    m['recipe']=read(recipe_file); m['template_id']=template; m['review']=None
    write(case/'case.json',m); report(case)

def source_digest(m): return object_hash([{'id':s['id'],'sha256':s['sha256']} for s in m['sources']])
def validate(case, require_review=True):
    case=Path(case); m=read(case/'case.json'); errors=[]; warnings=[]
    if m.get('schema_version')!=1: errors.append('不支援的工作資料版本')
    if m.get('category') not in CATEGORIES: errors.append('設備分類不正確')
    if (m.get('category'),m.get('template_id')) not in [('pump',TEMPLATE),('ahu',AHU_TEMPLATE)]: errors.append('尚未建立此設備／構造的可執行範本')
    if m.get('target_revit')!=2024: errors.append('目前建族器僅於 Revit 2024 執行；2025／2026 未驗證')
    if not m.get('sources'): errors.append('沒有來源圖資')
    for s in m.get('sources',[]):
        try:
            if digest(local(case,s['snapshot']))!=s['sha256']: errors.append(f"來源已變更：{s['id']}")
            data=read(local(case,s['analysis']))
            if data.get('status') in ('failed','needs_adapter'): errors.append(f"{s['id']} 尚未完成解析")
            if data.get('complete') is False: errors.append(f"{s['id']} 超過頁數限制，請拆分後匯入")
            if data.get('status')=='needs_review': warnings.append(f"{s['id']} 需依原圖人工補足文字")
        except (KeyError,ValueError,OSError) as exc: errors.append(f"來源驗證失敗：{s.get('id')} {exc}")
    recipe=m.get('recipe')
    if not isinstance(recipe,dict): errors.append('尚未載入尺寸資料')
    elif m.get('template_id')==AHU_TEMPLATE:
        reference=read(AHU_RECIPE)
        if recipe!=reference: errors.append('空調箱首版僅接受已核對的 PJ0043-HS／PJ0063-HS 配置；請另行擴充並驗證其他型號。')
        if not any(s.get('sha256')==reference['source_sha256'] for s in m.get('sources',[])): errors.append('空調箱範本需包含已核對的東元空調箱型錄原檔。')
        warnings.extend(reference['assumptions'])
        warnings.append(reference['scope'])
    else:
        if recipe != read(RECIPE): errors.append('此版只接受已核對的完整泵浦資料；其他安裝或性能設定需擴充範本，不能默默忽略。')
        expected={t['assembly_model']:t for t in read(RECIPE)['types']}
        types=recipe.get('types',[])
        if not isinstance(types,list) or len(types)!=2: errors.append('首版需 TOS-EF-05／21 兩類型')
        else:
            names=[t.get('assembly_model') for t in types if isinstance(t,dict)]
            if set(names)!=set(expected) or len(set(names))!=2: errors.append('型號不符合本範本')
            for t in types:
                if not isinstance(t,dict) or t.get('assembly_model') not in expected: continue
                reference=expected[t['assembly_model']]
                # This adapter is deliberately limited to the previously flex-tested catalog configuration.
                for field in ('assembly_dimensions','motor_hp','catalog_motor_kw','pump_and_coupling_approximate_mass','discharge_nominal_size_source'):
                    if t.get(field)!=reference[field]: errors.append(f"{t['assembly_model']} 的 {field} 超出本版已驗證配置")
        if recipe.get('units',{}).get('length')!='mm': errors.append('尺寸單位必須為 mm')
        warnings.extend(['概念幾何；導桿預設1500mm／外徑42mm為試作值。','未建立電氣接頭；需供電及輸入負載資料。', '來源尺寸欄位不全部驅動幾何；平面配置固定。'])
    review=m.get('review')
    if require_review:
        if not isinstance(review,dict) or not review.get('reviewer') or not review.get('accept_concept'): errors.append('尚未完成圖資及概念模型範圍覆核')
        else:
            if review.get('recipe_hash')!=object_hash(recipe) or review.get('source_hash')!=source_digest(m): errors.append('覆核後資料已變更，需重新覆核')
    return {'can_build':not errors,'errors':errors,'warnings':warnings}

def review(case, reviewer, note, accept_concept):
    if not reviewer.strip() or not note.strip() or not accept_concept: raise ValueError('需填寫覆核者、尺寸來源說明，並確認概念模型範圍')
    checks=validate(case,require_review=False)
    if checks['errors']: raise ValueError('\n'.join(checks['errors']))
    case=Path(case); m=read(case/'case.json')
    m['review']={'reviewer':reviewer.strip(),'note':note.strip(),'accept_concept':True,'at':now(),'recipe_hash':object_hash(m['recipe']),'source_hash':source_digest(m)}
    write(case/'case.json',m); report(case)

def prepare(case):
    case=Path(case).resolve(); result=validate(case)
    if not result['can_build']: raise ValueError('\n'.join(result['errors']))
    m=read(case/'case.json'); packet=case/'builds'/uuid.uuid4().hex[:12]; packet.mkdir(parents=True)
    write(packet/'recipe.json',m['recipe'])
    # Freeze the review and the evidence used by this build; later case edits cannot silently change it.
    receipts=[]
    for s in m['sources']:
        name=s['id']+Path(s['snapshot']).suffix
        shutil.copyfile(local(case,s['snapshot']),packet/name)
        receipts.append({'file':name,'sha256':digest(packet/name)})
    request={'schema_version':1,'template_id':m['template_id'],'target_revit':m['target_revit'],'case_id':m['case_id'],
             'recipe_file':'recipe.json','recipe_sha256':digest(packet/'recipe.json'),'source_receipts':receipts,
             'review':m['review'],'output_directory':'output','limitations':result['warnings']}
    write(packet/'build-request.json',request)
    return packet/'build-request.json'

def report(case):
    case=Path(case); m=read(case/'case.json'); checks=validate(case)
    esc=lambda s:html.escape(str(s))
    cards=[]
    for s in m['sources']:
        data=read(local(case,s['analysis'])); details=[]
        for p in data.get('pages',[]):
            image_path=(Path(s['analysis']).parent / p['preview']).as_posix()
            method='圖片文字辨識' if p['method']=='ocr' else 'PDF 內嵌文字'
            details.append(f'<details><summary>第 {p["page"]} 頁 · {method}</summary><a href="{esc(image_path)}"><img src="{esc(image_path)}" alt="來源頁面"></a><pre>{esc(p.get("text", ""))}</pre></details>')
        if 'entity_counts' in data:
            details.append('<pre>'+esc(json.dumps({k:data.get(k) for k in ['drawing_units','geometry_kind','entity_counts','drawing_bounds','texts','dimensions']},ensure_ascii=False,indent=2))+'</pre>')
        cards.append(f'<section><h2>{esc(s["original_name"])}</h2><p>{esc(STATUS_LABELS.get(s["status"],s["status"]))} · 來源指紋 {esc(s["sha256"][:16])}…</p>'+''.join(details)+f'<p>{esc(data.get("notes",data.get("error","")))}</p></section>')
    state='可產生概念建族工作包' if checks['can_build'] else '尚需補足／覆核'
    body=f'''<!doctype html><html lang="zh-Hant"><meta charset="utf-8"><title>設備圖資覆核</title>
<style>body{{font:16px/1.65 system-ui,sans-serif;background:#edf2f5;color:#183041;max-width:1100px;margin:40px auto;padding:0 24px}}section{{background:white;padding:24px;border-radius:12px;margin:20px 0}}h1{{margin-bottom:4px}}h2{{font-size:20px}}pre{{white-space:pre-wrap;overflow-wrap:anywhere;max-height:500px;overflow:auto;background:#f3f6f8;padding:16px}}img{{max-width:100%;max-height:700px}}summary{{cursor:pointer;padding:12px}}.status{{color:#a34c10;font-weight:bold}}</style>
<h1>設備圖資覆核</h1><p>{esc(CATEGORIES.get(m['category'],m['category']))} · Revit {esc(m['target_revit'])}</p><section><p class="status">{state}</p><ul>{''.join('<li>'+esc(x)+'</li>' for x in checks['errors']+checks['warnings'])}</ul></section>
{''.join(cards)}<section><h2>尺寸與參數資料</h2><p>來源文字及 OCR 僅作證據，沒有自動推定設備尺寸。</p><pre>{esc(json.dumps(m.get('recipe'),ensure_ascii=False,indent=2))}</pre></section></html>'''
    (case/'report.html').write_text(body,encoding='utf-8')
    write(case/'validation.json',checks)

def main():
    p=argparse.ArgumentParser(description='本機設備圖資工作台')
    sub=p.add_subparsers(dest='action',required=True)
    q=sub.add_parser('ingest');q.add_argument('--category',choices=CATEGORIES,required=True);q.add_argument('--out',required=True);q.add_argument('--target',type=int,default=2024);q.add_argument('files',nargs='+')
    q=sub.add_parser('set-recipe');q.add_argument('case');q.add_argument('recipe')
    q=sub.add_parser('review');q.add_argument('case');q.add_argument('--reviewer',required=True);q.add_argument('--note',required=True);q.add_argument('--accept-concept',action='store_true')
    for cmd in ('validate','prepare'):q=sub.add_parser(cmd);q.add_argument('case')
    q=sub.add_parser('gui');q.add_argument('--case')
    a=p.parse_args()
    if a.action=='ingest': print(new_case(a.category,a.files,a.out,a.target))
    elif a.action=='set-recipe':set_recipe(a.case,a.recipe)
    elif a.action=='review':review(a.case,a.reviewer,a.note,a.accept_concept)
    elif a.action=='validate':
        result=validate(a.case);print(json.dumps(result,ensure_ascii=False,indent=2));return 0 if result['can_build'] else 2
    elif a.action=='prepare':print(prepare(a.case))
    elif a.action=='gui':
        from workbench import launch
        launch(a.case)
    return 0
if __name__=='__main__':
    try: sys.exit(main())
    except Exception as exc: print(str(exc),file=sys.stderr);sys.exit(1)
