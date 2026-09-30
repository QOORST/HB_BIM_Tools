// Keep daily review controls visible; fold occasional settings without removing them.
if(typeof document!=='undefined') {
  const aside=document.querySelector('main>aside'),advanced=document.createElement('details');
  advanced.id='advanced-review';const summary=document.createElement('summary');summary.textContent='篩選與物件判讀（進階）';advanced.append(summary);
  let node=el('count');while(node){const next=node.nextSibling;advanced.append(node);node=next}aside.append(advanced);
  const layerSettings=el('layerbox').closest('details');layerSettings.open=false;
  const bar=document.querySelector('.compare-toolbar'),reset=document.createElement('button');reset.textContent='清除篩選';reset.id='clear-review-filters';
  const hint=document.createElement('p');hint.id='active-filter-hint';hint.setAttribute('role','status');bar.append(reset,hint);
  function describeFilters(){const parts=[];
    for(const [id,defaultValue] of [['layer',''],['kind',''],['group','全部用途'],['geometry','全部幾何'],['cloud-relation','全部候選']]){
      const control=el(id);if(control.value!==defaultValue)parts.push(control.selectedOptions[0].textContent);
    }
    if(el('revision').checked)parts.push('隱藏修訂標記候選');if(!el('unchanged').checked)parts.push('隱藏未變更背景');
    hint.textContent=parts.length?'目前篩選：'+parts.join('、')+'。切換區域會保留篩選，需看完整區域時請清除。':'未套用額外篩選';
    hint.style.color=parts.length?'#986018':'#60758a';reset.disabled=parts.length===0;
  }
  reset.onclick=()=>{el('layer').value='';el('kind').value='';el('group').value='全部用途';el('geometry').value='全部幾何';el('cloud-relation').value='全部候選';el('revision').checked=false;el('unchanged').checked=true;el('cloud-region').onchange();describeFilters()};
  document.addEventListener('change',describeFilters);describeFilters();
  const originalDetailsRefresh=refresh;refresh=function(){originalDetailsRefresh();describeFilters()};
  for(const id of ['layer','kind','revision','geometry','group','grid'])el(id).onchange=refresh;
  const candidateInfo=el('candidate-summary');
  const empty=document.createElement('p');empty.id='candidate-empty';empty.className='muted';candidateInfo.after(empty);
  const refreshBeforeEmpty=refresh;refresh=function(){refreshBeforeEmpty();
    const n=el('candidate-rows').children.length;
    empty.textContent=n?'':'目前條件下沒有待查候選；可先清除篩選。零筆不代表原圖沒有文字、填充或其他未擷取變更。';
  };
  for(const id of ['layer','kind','revision','geometry','group','grid'])el(id).onchange=refresh;
}
