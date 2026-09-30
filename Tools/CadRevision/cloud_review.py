"""Build conservative cloud-envelope review queues; never classify design changes."""
import argparse
import collections
import json
import math
import re
from pathlib import Path


def extent(g):
    if len(g) == 4:
        return min(g[0], g[2]), min(g[1], g[3]), max(g[0], g[2]), max(g[1], g[3])
    x, y, radius, start, sweep, circle = g
    angles = [start, start + sweep]
    angles += [a for a in (0, math.pi / 2, math.pi, 3 * math.pi / 2)
               if circle or (a - start) % math.tau <= sweep + 1e-9]
    xs = [x + radius * math.cos(a) for a in angles]
    ys = [y - radius * math.sin(a) for a in angles]
    return min(xs), min(ys), max(xs), max(ys)


def overlaps(a, b):
    return a[0] <= b[2] and b[0] <= a[2] and a[1] <= b[3] and b[1] <= a[3]


def parent(handle):
    return re.sub(r':\d+$', '', handle)


def sample(g, error=1.0):
    """Tessellate analytic arcs with <= 1 mm chord sagitta."""
    if len(g) == 4:
        return [(g[0], g[1]), (g[2], g[3])]
    x, y, radius, start, sweep, circle = g
    sweep = math.tau if circle else sweep
    step = 2 * math.acos(max(-1, min(1, 1 - error / radius)))
    n = max(2, math.ceil(sweep / min(step, math.pi / 8)))
    return [(x + radius * math.cos(start + sweep * i / n),
             y - radius * math.sin(start + sweep * i / n)) for i in range(n + 1)]


def chain(geometries):
    remaining = [sample(g) for g in geometries]
    result = remaining.pop()
    while remaining:
        for i, points in enumerate(remaining):
            if math.dist(result[-1], points[0]) <= .05:
                break
            if math.dist(result[-1], points[-1]) <= .05:
                points = list(reversed(points))
                break
        else:
            raise ValueError('Cloud boundary is disconnected')
        result.extend(points[1:])
        remaining.pop(i)
    if math.dist(result[-1], result[0]) > .05:
        raise ValueError('Cloud boundary is not closed')
    result[-1] = result[0]
    return result


def point_distance(p, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = dx * dx + dy * dy
    t = max(0, min(1, ((p[0]-a[0])*dx+(p[1]-a[1])*dy)/length)) if length else 0
    return math.dist(p, (a[0]+t*dx, a[1]+t*dy))


def segment_distance(a, b, c, d):
    def cross(p, q, r):
        return (q[0]-p[0])*(r[1]-p[1])-(q[1]-p[1])*(r[0]-p[0])
    if (cross(a,b,c)*cross(a,b,d) < 0 and cross(c,d,a)*cross(c,d,b) < 0):
        return 0
    return min(point_distance(a,c,d), point_distance(b,c,d),
               point_distance(c,a,b), point_distance(d,a,b))


def inside(point, polygon):
    x, y = point
    value = False
    for a, b in zip(polygon, polygon[1:]):
        if (a[1] > y) != (b[1] > y) and x < (b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]:
            value = not value
    return value


def relation(geometries, polygon):
    # 3 mm guard band covers tessellation on both curves and endpoint stitching.
    states = []
    edges = list(zip(polygon, polygon[1:]))
    for g in geometries:
        points = sample(g)
        for a, b in zip(points, points[1:]):
            bounds = (min(a[0],b[0])-3,min(a[1],b[1])-3,max(a[0],b[0])+3,max(a[1],b[1])+3)
            for c, d in edges:
                if overlaps(bounds,(min(c[0],d[0]),min(c[1],d[1]),max(c[0],d[0]),max(c[1],d[1]))) and segment_distance(a,b,c,d) <= 3:
                    return '邊界待查'
        states.append(inside(points[0], polygon))
    if all(states):
        return '圈內'
    if any(states):
        return '跨版本內外'
    return '僅矩形相交'


def build(folder):
    page = (folder / 'review-cloud-checked.html').read_text(encoding='utf-8')
    payload, _ = json.JSONDecoder().raw_decode(page.split('<script>const p=', 1)[1])
    verdicts = json.loads((folder / 'object-verdicts-reviewed.json').read_text(encoding='utf-8'))
    if verdicts['sourceId'] != payload['sourceId']:
        raise ValueError('Verdict source mismatch')
    clouds = {tuple(json.loads(k)) for k, v in verdicts['verdicts'].items() if v == '修訂標記'}
    records = payload['data']
    boxes = [[extent(g) for g in r[2]] for r in records]
    cloud_ids = collections.defaultdict(list)
    for i, r in enumerate(records):
        for side in range(2):
            key = (side, parent(r[3 + side]))
            if key in clouds:
                # This stage requires added/deleted clouds with a single version geometry.
                if len(r[2]) != 1:
                    raise ValueError('Cloud has multiple version geometries; manual review required')
                cloud_ids[key].append(i)
    excluded = {i for ids in cloud_ids.values() for i in ids}
    regions = []
    for key, ids in sorted(cloud_ids.items()):
        bounds = [boxes[i][0] for i in ids]
        envelope = [min(b[0] for b in bounds), min(b[1] for b in bounds),
                    max(b[2] for b in bounds), max(b[3] for b in bounds)]
        display = [i for i, bs in enumerate(boxes) if any(overlaps(b, envelope) for b in bs)]
        candidates = [i for i in display if i not in excluded and payload['kinds'][records[i][1]] != '未變更']
        polygon = chain([records[i][2][0] for i in ids])
        relations = {str(i): relation(records[i][2], polygon) for i in candidates}
        counts = dict(collections.Counter(payload['kinds'][records[i][1]] for i in candidates))
        regions.append(dict(source=key[1], side=key[0], bounds=envelope, display=display,
                            candidates=candidates, counts=counts, relations=relations,
                            relationCounts=dict(collections.Counter(relations.values()))))
    union = set(i for region in regions for i in region['candidates'])
    all_changes = {i for i,r in enumerate(records) if payload['kinds'][r[1]] != '未變更'}
    coverage = dict(allChangedRecords=len(all_changes), confirmedCloudRecords=len(excluded),
                    cloudCandidateRecords=len(union), otherChangedRecords=len(all_changes-excluded-union))
    outside_ids = sorted(all_changes-excluded-union)
    outside_page = page.replace('function refresh(){', 'const outsideIds=new Set('+json.dumps(outside_ids)+');function refresh(){', 1)
    outside_page = outside_page.replace("let r=p.data[i];if(el('layer')", "let r=p.data[i];if(p.kinds[r[1]]!=='未變更'&&!outsideIds.has(i))continue;if(el('layer')", 1)
    outside_page = outside_page.replace('<h1>HB CAD 變更檢視</h1>', '<h1>未納入雲線清單的變更</h1><p>共 '+str(len(outside_ids))+' 筆；保留未變更背景，可依圖層、類型及網格定位。這是未被目前雲線外接矩形候選涵蓋的紀錄，不代表已確認的設計變更。</p><a href="review-cloud-regions.html">回到雲線審查</a>', 1)
    (folder/'review-outside-clouds.html').write_text(outside_page, encoding='utf-8')
    report = dict(sourceId=payload['sourceId'], method='外接矩形初篩，再以弦高誤差 ≤ 1 mm 的封閉輪廓近似判讀；3 mm 邊界帶保守列為待查。跨區紀錄可能重複；位移兩版本共同判讀。空間關係不代表設計意義。',
                  uniqueCandidateRecords=len(union), regions=regions, coverage=coverage)
    (folder / 'cloud-regions.json').write_text(json.dumps(report, ensure_ascii=False), encoding='utf-8')
    ui = '<label>雲線待查區域 <select id="cloud-region"><option value="">全部區域</option></select></label><label>輪廓關係 <select id="cloud-relation"><option>全部候選</option><option>圈內</option><option>邊界待查</option><option>跨版本內外</option><option>僅矩形相交</option></select></label><p id="cloud-summary"></p><p class="muted">輪廓以弦高誤差 ≤ 1 mm 近似；距邊界 3 mm 內或跨界列為邊界待查。跨版本內外表示位移兩端分居圈內外。這些是空間關係，不是設計變更判定。背景與雲線保留，其他篩選仍同時生效。</p>'
    ui += '<details open><summary>區域審查進度</summary><p id="review-progress"></p><label>審查狀態 <select id="review-status"><option>待查</option><option>進行中</option><option>已檢視</option></select></label><label>區域備註 <textarea id="review-note" rows="3" maxlength="4000" style="width:100%"></textarea></label><button id="next-review">下一個未完成區域</button><p><button id="save-review">匯出區域進度</button></p><label>載入區域進度 <input id="load-review" type="file" accept=".json"></label><p id="review-save-state" role="status"></p><p class="muted">已檢視僅表示人工檢視進度，不代表設計已核准。區域進度與物件判讀分別匯出；離開前請儲存。</p></details>'
    page = page.replace('<aside>', '<aside>' + ui, 1)
    coverage_note = f'<p class="muted">審查覆蓋範圍：全資料 {len(all_changes):,} 筆變更；已確認雲線 {len(excluded):,} 筆；雲線候選 {len(union):,} 筆；其餘 {coverage["otherChangedRecords"]:,} 筆尚未納入雲線清單。選擇「全部區域」可搭配原有網格定位檢視全資料。</p>'
    page = page.replace('</header>', coverage_note + '<a href="review-outside-clouds.html" target="_blank">開啟其餘變更檢視</a></header>', 1)
    data = json.dumps(report, ensure_ascii=False, separators=(',', ':')).replace('<', '\\u003c')
    page = page.replace('function refresh(){', 'const cloudReport=' + data + ';let cloudVisible=null;\nfunction refresh(){', 1)
    page = page.replace('let r=p.data[i];if(el(\'layer\')', "if(cloudVisible&&!cloudVisible.has(i))continue;let r=p.data[i];if(el('layer')", 1)
    setup = """
cloudReport.regions.forEach((r,i)=>{let o=option((i+1)+' · '+r.source+' · '+r.candidates.length+' 筆待查');o.value=String(i);el('cloud-region').append(o)});
el('cloud-region').onchange=()=>{let value=el('cloud-region').value,r=value===''?null:cloudReport.regions[+value],rel=el('cloud-relation').value;el('cloud-relation').disabled=!r;cloudVisible=r?new Set(r.display.filter(i=>rel==='全部候選'||!r.relations[String(i)]||r.relations[String(i)]===rel)):null;refresh();if(r){let b=r.bounds,pad=Math.max(100,(b[2]-b[0])*.05,(b[3]-b[1])*.05);box=[b[0]-pad,b[1]-pad,b[2]-b[0]+2*pad,b[3]-b[1]+2*pad];requestDraw()}el('cloud-summary').textContent=r?'本區候選（其他篩選前）：'+Object.entries(r.relationCounts).map(([k,n])=>k+' '+n).join('／'):'跨區去重：'+cloudReport.uniqueCandidateRecords+' 筆待查變更紀錄'};
el('cloud-relation').onchange=()=>el('cloud-region').onchange();
el('cloud-region').onchange();
"""
    tracking = (Path(__file__).with_name('cloud_progress.js')).read_text(encoding='utf-8')
    detail_ui = '<details open style="margin:0 0 16px"><summary>待查內容摘要</summary><p id="candidate-summary"></p><p id="candidate-layers" class="muted"></p><p><button id="export-candidates">匯出目前待查明細</button> <button id="candidate-back">回到區域範圍</button></p><p id="candidate-focus" role="status"></p><div style="max-height:220px;overflow:auto"><table><thead><tr><th>定位</th><th>圖層</th><th>變更</th><th>幾何</th><th>舊／新來源</th><th>輪廓關係</th></tr></thead><tbody id="candidate-rows"></tbody></table></div><p class="muted">明細跟隨目前所有篩選，排除已確認的雲線本身；跨區合併時依紀錄去重。來源物件依版本分開計數，不等同建築元件數。明細不是完整圖面的變更總表。</p></details>'
    page = page.replace('<div id="stats"></div>', '<p class="muted">畫面紀錄統計（包含背景及雲線）：</p><div id="stats"></div>' + detail_ui, 1)
    page = page.replace('</style>', '''main{grid-template-columns:320px minmax(0,1fr)}main>section,main>aside{min-width:0}main>aside input,main>aside textarea{max-width:100%;min-width:0}main>aside{overflow-wrap:anywhere}#candidate-rows td{overflow-wrap:anywhere}canvas{height:62vh;min-height:320px}main>aside details{margin:14px 0}button:focus-visible,select:focus-visible,textarea:focus-visible{outline:2px solid #245fe5;outline-offset:2px}@media(min-width:851px){main>aside{max-height:calc(100vh - 135px);overflow:auto;position:sticky;top:12px;align-self:start}}@media(max-width:850px){main{grid-template-columns:1fr}canvas{height:55vh}}</style>''', 1)
    page = page.replace('匯出區域進度', '匯出完整審查紀錄').replace('載入區域進度', '載入審查紀錄')
    page = page.replace('區域進度與物件判讀分別匯出；離開前請儲存。', '完整紀錄包含區域進度、物件判讀及圖層用途；離開前請匯出。亦可載入舊版區域進度檔。')
    details = (Path(__file__).with_name('cloud_details.js')).read_text(encoding='utf-8')
    modes = (Path(__file__).with_name('compare_modes.js')).read_text(encoding='utf-8')
    page = page.replace('</style>', '.compare-toolbar{display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:10px}.compare-toolbar label{margin:0}.compare-toolbar p{width:100%;margin:2px 0;color:#60758a;font-size:13px}.compare-toolbar [hidden]{display:none}</style>', 1)
    workflow = (Path(__file__).with_name('review_workflow.js')).read_text(encoding='utf-8')
    page = page.replace('</script></html>', setup + tracking + modes + details + workflow + '</script></html>')
    (folder / 'review-cloud-regions.html').write_text(page, encoding='utf-8')
    print(json.dumps({'regions': len(regions), 'uniqueCandidateRecords': len(union),
                      'nonemptyRegions': sum(bool(r['candidates']) for r in regions)}, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('folder', type=Path)
    build(parser.parse_args().folder)
