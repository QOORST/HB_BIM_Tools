"""Produce an evidence-only review brief from extracted records, without verdicts."""
import collections
import json
from pathlib import Path
import argparse


def build(folder):
    page = (folder/'review-cloud-checked.html').read_text(encoding='utf-8')
    p, _ = json.JSONDecoder().raw_decode(page.split('<script>const p=', 1)[1])
    report = json.loads((folder/'cloud-regions.json').read_text(encoding='utf-8'))
    assert p['sourceId'] == report['sourceId']
    regions = report['regions']
    unique = {i for r in regions for i in r['candidates']}
    counts = collections.Counter((p['layers'][p['data'][i][0]], p['kinds'][p['data'][i][1]]) for i in unique)
    groups = collections.defaultdict(list)
    for number, r in enumerate(regions, 1):
        signature = tuple(sorted({p['layers'][p['data'][i][0]] for i in r['candidates']}))
        groups[signature].append(number)
    lines = ['# CAD 雲線範圍審查摘要', '',
             '本摘要依已擷取的直線、圓與圓弧統計；圖層名稱僅供查找，尚未證實物件的設計用途。', '',
             f'43 個雲線範圍內，外接矩形初篩後共有 {len(unique)} 筆不重複候選。這不是全圖變更總數，也不是 BIM 元件數。', '',
             '## 建議檢視順序', '',
             '1. **第 40 區**：76 筆均位於近似輪廓內，涉及 WALL、開口或管道、BALC、空間編號。先確認是否為實際建築配置調整，再對照模型；名稱不能證明構件類別。',
             '2. **第 1、39 區**：檢查消防車／停車標示／落水頭及設備基座圖層。第 39 區僅 1 段新增幾何，不能據此認定新增一座設備基座。',
             '3. **第 3–38 區**：相同圖層組合重複出現。276 筆新增位於「131-收測界樁」圖層，建議先抽看代表區，確認內容與圖層用途，再逐區核對；不能將其當成 276 個界樁。',
             '4. **第 41–43 區**：涉及 TEXT、EQP；目前擷取的是這些圖層上的幾何，不是文字內容。文字替換、設備名稱與規格仍需回 CAD 確認。',
             '5. **第 2 區**：沒有擷取到其他變更候選；仍需確認文字、填充、裁切圖塊等未支援內容，不可直接標為無變更。', '',
             '## 圖層統計（跨區去重）', '', '| 圖層 | 新增 | 刪除 |', '|---|---:|---:|']
    for layer in sorted({k[0] for k in counts}):
        lines.append(f"| {layer.replace('|', '／')} | {counts[layer, '新增']} | {counts[layer, '刪除']} |")
    lines += ['', '## 相同圖層組合', '', '只表示圖層集合相同，未驗證幾何形狀或設計意義相同。', '']
    for signature, numbers in sorted(groups.items(), key=lambda x:-len(x[1])):
        ids = {i for n in numbers for i in regions[n-1]['candidates']}
        lines.append(f"- 第 {', '.join(map(str,numbers))} 區：{len(ids)} 筆去重候選；圖層：" + ('、'.join(signature) or '無候選'))
    lines += ['', '## 逐區追溯', '', '| 區域 | 雲線來源 | 候選數 | 輪廓關係 |', '|---:|---|---:|---|']
    for n,r in enumerate(regions,1):
        rel='、'.join(f'{k} {v}' for k,v in r['relationCounts'].items()) or '無候選'
        lines.append(f"| {n} | {r['source']} | {len(r['candidates'])} | {rel} |")
    lines += ['', '## 判讀限制', '',
              '- 輪廓以弦高誤差 ≤ 1 mm 近似，3 mm 邊界帶列為待查；圈內僅指空間關係。',
              '- 新增與刪除筆數相等，不代表已配對為移動或修改。',
              '- 全部區域仍保持待查，本摘要沒有變更任何審查狀態或物件判讀。',
              '- 尚未納入文字內容、填充、橢圓、裁切圖塊等解析限制；未進行 Revit 影響分析或工時估算。', '',
              '來源識別：`'+p['sourceId']+'`', '']
    (folder/'review-brief.md').write_text('\n'.join(lines), encoding='utf-8')
    print(f'Brief generated: {len(unique)} records, {len(groups)} layer combinations')


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('folder',type=Path)
    build(parser.parse_args().folder)
