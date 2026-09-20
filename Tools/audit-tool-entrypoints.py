"""Inventory declared production ribbon routes, not a substitute for Revit QA."""
import json
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
sys.stdout.reconfigure(encoding='utf-8')
app = (root / 'App.cs').read_text(encoding='utf-8-sig')
app = app[:app.index('        private void AddMEPToolButtons')] + app[app.index('        private void AddOptimizedMEPToolButtons'):]
app = app[:app.index('        private void AddClarificationToolButtons')] + app[app.index('        private void AddAiAssistantButton'):]
sources = {}
for path in (root / 'Commands').rglob('*.cs'):
    source = path.read_text(encoding='utf-8-sig')
    namespace = re.search(r'namespace\s+([\w.]+)', source)
    if namespace:
        for name in re.findall(r'\bclass\s+(\w+)', source):
            sources.setdefault(namespace[1] + '.' + name, []).append(path.relative_to(root).as_posix())

entries = []
pattern = r'new PushButtonData\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*(\w+)\s*,\s*"([^"]+)"\s*\)'
for match in re.finditer(pattern, app):
    button, label, assembly, command = match.groups()
    methods = re.findall(r'private void (Add\w+)\(', app[:match.start()])
    entries.append(dict(id=button, label=label.replace('\\n', ''), command=command,
                        section=methods[-1] if methods else '', external=assembly!='assemblyPath'))
for match in re.finditer(r'new\[\]\s*\{\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"(Cmd\w+)"', app):
    button,label,command=match.groups()
    entries.append(dict(id=button,label=label.replace('\\n',''),command='YD_RevitTools.LicenseManager.Commands.MEP.'+command,
                        section='AddOptimizedMEPToolButtons',external=False))
for entry in entries:
    entry['sources'] = sources.get(entry['command'], [])
    entry['status'] = '公司內網環境驗收' if entry['external'] else '入口對照完成；模型及介面待逐項驗收'
    if not entry['sources'] and not entry['external']: raise RuntimeError('Unresolved command: '+entry['command'])
out=root/'artifacts/tool-suite-qa';out.mkdir(parents=True,exist_ok=True)
(out/'entrypoints.json').write_text(json.dumps(entries,ensure_ascii=False,indent=2),encoding='utf-8')
lines=['# 正式工具入口對照', '', '由 App.cs 正式註冊方法擷取，排除已 return 的舊 MEP 區段及未呼叫的重複澄清入口。含條件式入口，並非任一 Revit 版本的畫面按鈕數。此表只確認入口與原始碼對照，不代表模型操作通過。', '', '| 工具 | 指令原始碼 | 狀態 |', '| --- | --- | --- |']
for entry in sorted(entries,key=lambda x:(x['section'],x['id'])):
    links='<br>'.join('['+p+'](../../'+p+')' for p in entry['sources']) or '外部族庫組件'
    lines.append('| '+entry['label']+' | '+links+' | '+entry['status']+' |')
(root/'Docs/qa/tool-entrypoints.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(f'{len(entries)} declared routes mapped; {sum(e["external"] for e in entries)} external dependency.')
