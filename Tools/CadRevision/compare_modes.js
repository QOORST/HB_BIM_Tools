function versionShapes(record, kind, side) {
  if(kind==='未變更')return record[2].slice(0,1);
  if(!record[3+side])return [];
  return record[2].length===1 ? record[2] : [record[2][side]];
}
if(typeof module!=='undefined'&&module.exports)module.exports={versionShapes};
if(typeof document!=='undefined') {
  const bar=document.createElement('div');bar.className='compare-toolbar';
  bar.innerHTML='<label>比對模式 <select id="compare-mode"><option value="overlay">差異疊圖</option><option value="swipe">左右滑桿</option><option value="old">舊版幾何</option><option value="new">新版幾何</option></select></label><button id="previous-region">上一區</button><button id="following-region">下一區</button><button id="fit-region">符合區域範圍</button><button id="swap-display">交換左右顯示</button><label id="split-label">分隔位置 <input id="compare-split" type="range" min="0" max="100" value="50"></label><p id="mode-caption" role="status"></p>';
  cv.before(bar);
  const frame=document.createElement('div');frame.style.position='relative';cv.before(frame);frame.append(cv);
  const divider=document.createElement('button');divider.id='compare-divider';divider.textContent='↔';divider.setAttribute('aria-label','拖曳新舊分隔線');
  Object.assign(divider.style,{position:'absolute',top:'0',height:'100%',width:'26px',transform:'translateX(-50%)',border:'0',borderLeft:'2px solid #98654a',borderRadius:'0',background:'transparent',cursor:'ew-resize',touchAction:'none',color:'#71452e',fontSize:'24px',padding:'0'});frame.append(divider);
  let swapped=false;
  function syncMode(){let mode=el('compare-mode').value,swipe=mode==='swipe';divider.hidden=!swipe;el('split-label').hidden=!swipe;el('swap-display').disabled=!swipe;
    divider.style.left=el('compare-split').value+'%';
    el('mode-caption').textContent=mode==='overlay'?'紅：刪除／綠：新增／橘：疑似位移；顯示已擷取幾何。':swipe?(swapped?'左：新版幾何｜右：舊版幾何':'左：舊版幾何｜右：新版幾何')+'；共用縮放與平移，交換顯示不改變比對定義。':(mode==='old'?'舊版':'新版')+'已擷取幾何；不是包含文字與填充的完整原圖。';requestDraw();}
  draw=function(){const ratio=devicePixelRatio||1;cv.width=Math.round(cv.clientWidth*ratio);cv.height=Math.round(cv.clientHeight*ratio);ctx.setTransform(ratio,0,0,ratio,0,0);ctx.clearRect(0,0,cv.clientWidth,cv.clientHeight);
    const [s,ox,oy]=transform(),mode=el('compare-mode').value,cut=cv.clientWidth*Number(el('compare-split').value)/100;
    function paint(side,left,width){ctx.save();ctx.beginPath();ctx.rect(left,0,width,cv.clientHeight);ctx.clip();
      for(let pass=0;pass<2;pass++)for(const id of visible){const r=p.data[id],kind=p.kinds[r[1]],unch=kind==='未變更';if(unch!==(pass===0)||unch&&!el('unchanged').checked)continue;
        for(const g of side===null?r[2]:versionShapes(r,kind,side)){const e=ext(g);if(e[2]<box[0]||e[0]>box[0]+box[2]||e[3]<box[1]||e[1]>box[1]+box[3])continue;
          ctx.strokeStyle=side!==null?(unch?'#929da8':side===0?'#bd4444':'#168756'):unch?'#d7dce3':kind==='疑似位移'?'#ce7b09':kind.startsWith('刪除')?'#ce4242':'#168756';ctx.lineWidth=unch?.65:1.3;ctx.beginPath();
          if(g.length===4){ctx.moveTo((g[0]-box[0])*s+ox,(g[1]-box[1])*s+oy);ctx.lineTo((g[2]-box[0])*s+ox,(g[3]-box[1])*s+oy)}else ctx.arc((g[0]-box[0])*s+ox,(g[1]-box[1])*s+oy,g[2]*s,-g[3],-g[3]-(g[5]?2*Math.PI:g[4]),true);ctx.stroke();
        }
      }ctx.restore();
    }
    if(mode==='swipe'){paint(swapped?1:0,0,cut);paint(swapped?0:1,cut,cv.clientWidth-cut)}else paint(mode==='overlay'?null:mode==='old'?0:1,0,cv.clientWidth);
  };
  el('compare-mode').onchange=syncMode;el('compare-split').oninput=syncMode;el('swap-display').onclick=()=>{swapped=!swapped;syncMode()};
  let sliding=false;function slide(e){const r=cv.getBoundingClientRect();el('compare-split').value=String(Math.max(0,Math.min(100,(e.clientX-r.left)/r.width*100)));syncMode()}
  divider.onpointerdown=e=>{if(e.button!==0)return;e.preventDefault();sliding=true;divider.setPointerCapture(e.pointerId);slide(e)};
  divider.onpointermove=e=>{if(sliding)slide(e)};divider.onpointerup=divider.onpointercancel=()=>sliding=false;
  function navigate(delta){const value=el('cloud-region').value;let i=value===''?(delta>0?0:cloudReport.regions.length-1):+value+delta;i=Math.max(0,Math.min(cloudReport.regions.length-1,i));el('cloud-region').value=String(i);el('cloud-region').dispatchEvent(new Event('change'))}
  function navState(){const value=el('cloud-region').value;el('previous-region').disabled=value==='0';el('following-region').disabled=value===String(cloudReport.regions.length-1)}
  if(el('cloud-region')) {
  el('previous-region').onclick=()=>navigate(-1);el('following-region').onclick=()=>navigate(1);el('fit-region').onclick=()=>el('cloud-region').onchange();el('cloud-region').addEventListener('change',navState);
  } else {el('previous-region').hidden=true;el('following-region').hidden=true;el('fit-region').textContent='符合篩選範圍';el('fit-region').onclick=()=>{box=bounds(visible);requestDraw()};}
  const oldWheel=cv.onwheel;cv.onwheel=e=>{oldWheel(e);box[2]=Math.max(.1,Math.min(1e10,box[2]));box[3]=Math.max(.1,Math.min(1e10,box[3]))};
  if(el('cloud-region'))navState();syncMode();
}
