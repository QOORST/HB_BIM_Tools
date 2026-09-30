// Progress is independent of object verdicts and never approves design changes.
function regionKey(r) { return JSON.stringify([r.side, r.source]); }
function validateRegionProgress(value, sourceId, regions) {
  if (!value || value.version !== 1 || value.type !== 'cloud-region-progress' || value.sourceId !== sourceId ||
      !value.regions || typeof value.regions !== 'object' || Array.isArray(value.regions)) throw Error('來源版本或格式不符');
  const valid = new Set(regions.map(regionKey)), result = Object.create(null);
  for (const [key, item] of Object.entries(value.regions)) {
    if (!valid.has(key) || !item || !['待查','進行中','已檢視'].includes(item.status) ||
        typeof item.note !== 'string' || item.note.length > 4000) throw Error('包含未知區域、狀態或過長備註');
    result[key] = {status:item.status, note:item.note};
  }
  return result;
}
function validateReviewBundle(value, payload, regions, categories) {
  if (!value || value.version !== 2 || value.type !== 'cloud-review-bundle') throw Error('完整紀錄格式不符');
  const progress=validateRegionProgress({...value,version:1,type:'cloud-region-progress'},payload.sourceId,regions);
  if (!Array.isArray(value.groups) || value.groups.length!==payload.layers.length || value.groups.some(x=>!categories.includes(x))) throw Error('圖層用途不符');
  if (!value.verdicts || typeof value.verdicts!=='object' || Array.isArray(value.verdicts)) throw Error('物件判讀格式不符');
  const keys=new Set(),validVerdicts=Object.create(null);
  for(const r of payload.data)for(let side=0;side<2;side++)if(r[3+side])keys.add(JSON.stringify([side,r[3+side].replace(/:[0-9]+$/,'')]));
  for(const [key,v] of Object.entries(value.verdicts)) {
    if(!keys.has(key)||!['待確認','修訂標記','設計變更','其他'].includes(v))throw Error('物件判讀包含未知來源或值');
    validVerdicts[key]=v;
  }
  return {progress,groups:value.groups.slice(),verdicts:validVerdicts};
}
if (typeof module !== 'undefined' && module.exports) module.exports = {regionKey, validateRegionProgress, validateReviewBundle};
if (typeof document !== 'undefined') {
  let progress = Object.create(null), dirty = false;
  const current = () => el('cloud-region').value === '' ? null : cloudReport.regions[+el('cloud-region').value];
  const regionLabels = Array.from(el('cloud-region').options, o=>o.textContent);
  function updateRegionLabels(){Array.from(el('cloud-region').options).forEach((o,i)=>{
    if(i===0)return;
    const r=cloudReport.regions[i-1];o.textContent='['+(progress[regionKey(r)]?.status||'待查')+'] '+regionLabels[i];
  });}
  function renderProgress() {
    updateRegionLabels();
    const r = current(), item = r && progress[regionKey(r)];
    el('review-status').disabled = el('review-note').disabled = !r;
    el('review-status').value = item?.status || '待查';
    el('review-note').value = item?.note || '';
    const done = cloudReport.regions.filter(x => progress[regionKey(x)]?.status === '已檢視').length;
    el('review-progress').textContent = '已檢視 '+done+'／'+cloudReport.regions.length+' 區';
    el('review-save-state').textContent = dirty ? '有未匯出的審查變更' : '審查紀錄無未匯出變更';
    el('next-review').disabled = done === cloudReport.regions.length;
  }
  function editProgress() {
    const r = current(); if (!r) return;
    progress[regionKey(r)] = {status:el('review-status').value,note:el('review-note').value};
    dirty = true;
    updateRegionLabels();
    // Avoid replacing textarea contents and moving the caret while typing.
    el('review-progress').textContent = '已檢視 '+cloudReport.regions.filter(x=>progress[regionKey(x)]?.status==='已檢視').length+'／'+cloudReport.regions.length+' 區';
    el('review-save-state').textContent = '有未匯出的審查變更';
    el('next-review').disabled = cloudReport.regions.every(x=>progress[regionKey(x)]?.status==='已檢視');
  }
  el('cloud-region').addEventListener('change', renderProgress);
  el('review-status').onchange = editProgress;
  el('review-note').oninput = editProgress;
  el('next-review').onclick = () => {
    const start = el('cloud-region').value === '' ? -1 : +el('cloud-region').value;
    for (let step=1;step<=cloudReport.regions.length;step++) {
      const i=(start+step)%cloudReport.regions.length;
      if (progress[regionKey(cloudReport.regions[i])]?.status !== '已檢視') {
        el('cloud-region').value=String(i);
        el('cloud-region').dispatchEvent(new Event('change')); break;
      }
    }
  };
  el('save-review').onclick = () => {
    const value={version:2,type:'cloud-review-bundle',sourceId:p.sourceId,regions:progress,verdicts,groups};
    const url=URL.createObjectURL(new Blob([JSON.stringify(value,null,2)],{type:'application/json'}));
    const a=document.createElement('a');a.href=url;a.download='cloud-review-bundle.json';a.click();
    setTimeout(()=>URL.revokeObjectURL(url),1000);dirty=false;renderProgress();
    el('review-save-state').textContent='已送出下載，請確認檔案已儲存';
  };
  el('load-review').onchange = async e => {
    try {
      const file=e.target.files[0];if(!file)return;
      if(file.size>10000000)throw Error('檔案超過 10 MB');
      const value=JSON.parse(await file.text()),full=value.type==='cloud-review-bundle';
      const next=full?validateReviewBundle(value,p,cloudReport.regions,cats):{progress:validateRegionProgress(value,p.sourceId,cloudReport.regions)};
      if(dirty&&!confirm('載入會取代尚未匯出的審查變更，確定載入？'))return;
      progress=next.progress;
      if(full){groups=next.groups;verdicts=next.verdicts;layerTable();el('cloud-region').onchange();}
      dirty=false;renderProgress();
      el('review-save-state').textContent=full?'完整審查紀錄已載入':'舊版區域進度已載入；保留物件判讀與圖層用途';
    } catch(err) {el('review-save-state').textContent='未載入：'+err.message;}
    finally {e.target.value='';}
  };
  window.addEventListener('beforeunload',e=>{if(dirty){e.preventDefault();e.returnValue='';}});
  document.addEventListener('change',e=>{
    if(e.target.closest('#layers')||e.target.closest('#objects')||['import','import-verdicts'].includes(e.target.id)) {
      dirty=true;el('review-save-state').textContent='有未匯出的審查變更';
    }
  });
  renderProgress();
}
