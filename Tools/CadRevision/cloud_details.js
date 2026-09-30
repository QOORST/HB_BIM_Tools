// Only candidates from confirmed cloud regions; no inferred design verdicts.
function collectCandidateDetails(payload, regions, visibleIds, selectedRegion) {
  const byId=new Map(), visibleSet=new Set(visibleIds);
  for(const region of selectedRegion ? [selectedRegion] : regions) {
    for(const id of region.candidates) {
      if(!visibleSet.has(id))continue;
      if(!byId.has(id)) {
        const r=payload.data[id];
        byId.set(id,{recordId:id,layer:payload.layers[r[0]],change:payload.kinds[r[1]],geometry:r[5],
          oldHandle:r[3],newHandle:r[4],regions:[]});
      }
      byId.get(id).regions.push({side:region.side,source:region.source,relation:region.relations[String(id)]});
    }
  }
  return [...byId.values()].sort((a,b)=>a.recordId-b.recordId);
}
if(typeof module!=='undefined'&&module.exports)module.exports={collectCandidateDetails};
if(typeof document!=='undefined') {
  let currentDetails=[], focused=null;
  const originalDraw=draw;
  draw=function(){originalDraw();if(focused===null)return;
    const [s,ox,oy]=transform();ctx.save();ctx.strokeStyle='#245fe5';ctx.lineWidth=2;ctx.setLineDash([5,3]);
    for(const g of p.data[focused][2]){const e=ext(g);ctx.strokeRect((e[0]-box[0])*s+ox-4,(e[1]-box[1])*s+oy-4,Math.max(1,(e[2]-e[0])*s)+8,Math.max(1,(e[3]-e[1])*s)+8)}ctx.restore();
  };
  function renderCandidateDetails(){
    focused=null;el('candidate-focus').textContent='';
    const value=el('cloud-region').value,r=value===''?null:cloudReport.regions[+value];
    currentDetails=collectCandidateDetails(p,cloudReport.regions,visible,r);
    const layers=new Map(),kinds=new Map(),sources=[new Set(),new Set()];
    for(const item of currentDetails){layers.set(item.layer,(layers.get(item.layer)||0)+1);kinds.set(item.change,(kinds.get(item.change)||0)+1);
      [item.oldHandle,item.newHandle].forEach((h,i)=>{if(h)sources[i].add(h.replace(/:[0-9]+$/,''))});}
    el('candidate-summary').textContent=currentDetails.length+' 筆待查 · '+layers.size+' 個圖層 · 舊版 '+sources[0].size+'／新版 '+sources[1].size+' 個來源物件。'+[...kinds].map(([k,n])=>k+' '+n).join('／');
    el('candidate-layers').textContent='主要圖層：'+[...layers].sort((a,b)=>b[1]-a[1]).slice(0,5).map(([k,n])=>k+'（'+n+'）').join('、');
    el('candidate-rows').replaceChildren();
    for(const item of currentDetails){const tr=document.createElement('tr'),td=document.createElement('td'),b=document.createElement('button');
      b.textContent='#'+item.recordId;b.setAttribute('aria-label','定位待查紀錄 '+item.recordId);
      b.onclick=()=>{focused=item.recordId;box=bounds([focused]);requestDraw();el('candidate-focus').textContent='定位 #'+focused+'：'+item.layer+' · '+item.change+'；藍色虛線框為該紀錄的幾何範圍。';cv.scrollIntoView({block:'nearest'})};td.append(b);tr.append(td);
      for(const text of [item.layer,item.change,item.geometry,(item.oldHandle||'—')+'／'+(item.newHandle||'—'),[...new Set(item.regions.map(x=>x.relation))].join('／')]){const c=document.createElement('td');c.textContent=text;tr.append(c)}el('candidate-rows').append(tr);
    }
    el('export-candidates').disabled=currentDetails.length===0;
  }
  const originalRefresh=refresh;
  refresh=function(){originalRefresh();renderCandidateDetails()};
  // Existing onchange properties captured the old function; bind current refresh.
  for(const id of ['layer','kind','revision','geometry','group','grid'])el(id).onchange=refresh;
  el('candidate-back').onclick=()=>{el('cloud-region').onchange()};
  el('export-candidates').onclick=()=>{const value={version:1,type:'cloud-candidate-details',sourceId:p.sourceId,
    scope:'目前篩選中的雲線候選；不是完整圖面變更或設計核准結果',records:currentDetails};
    const url=URL.createObjectURL(new Blob([JSON.stringify(value,null,2)],{type:'application/json'})),a=document.createElement('a');
    a.href=url;a.download='cloud-candidate-details.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
  };
  renderCandidateDetails();
}
