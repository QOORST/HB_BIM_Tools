// Shared by straight and curved geometry reports. No external dependencies.
(()=>{
const svg=document.querySelector('svg');if(!svg||svg.dataset.navigationReady)return;
svg.dataset.navigationReady='true';svg.style.touchAction='none';svg.style.cursor='grab';svg.style.userSelect='none';
let drag=null,suppressClick=false;
const view=()=>{const b=svg.viewBox.baseVal;return [b.x,b.y,b.width,b.height]};
const set=b=>{if(b.every(Number.isFinite)&&b[2]>1e-8&&b[3]>1e-8&&b[2]<1e15&&b[3]<1e15)svg.setAttribute('viewBox',b.join(' '))};
svg.addEventListener('pointerdown',e=>{
 if(e.button!==0&&e.button!==1)return;
 const matrix=svg.getScreenCTM();if(!matrix)return;
 e.preventDefault();suppressClick=false;
 const inverse=matrix.inverse();drag={id:e.pointerId,x:e.clientX,y:e.clientY,point:new DOMPoint(e.clientX,e.clientY).matrixTransform(inverse),inverse,box:view(),moved:false};
 svg.setPointerCapture(e.pointerId);svg.style.cursor='grabbing';
});
svg.addEventListener('pointermove',e=>{
 if(!drag||drag.id!==e.pointerId)return;
 if(Math.hypot(e.clientX-drag.x,e.clientY-drag.y)>3)drag.moved=true;
 if(!drag.moved)return;
 const p=new DOMPoint(e.clientX,e.clientY).matrixTransform(drag.inverse);
 set([drag.box[0]+drag.point.x-p.x,drag.box[1]+drag.point.y-p.y,drag.box[2],drag.box[3]]);
});
function end(e){if(!drag||drag.id!==e.pointerId)return;suppressClick=drag.moved;drag=null;svg.style.cursor='grab';if(svg.hasPointerCapture(e.pointerId))svg.releasePointerCapture(e.pointerId);}
svg.addEventListener('pointerup',end);svg.addEventListener('pointercancel',end);
svg.addEventListener('lostpointercapture',e=>{if(drag&&drag.id===e.pointerId){suppressClick=drag.moved;drag=null;svg.style.cursor='grab'}});
svg.addEventListener('click',e=>{if(suppressClick){e.preventDefault();e.stopImmediatePropagation();suppressClick=false}},true);
// Replace the earlier curve-only wheel handler to avoid double zooming.
svg.onwheel=null;
svg.addEventListener('wheel',e=>{
 e.preventDefault();if(drag)return;const matrix=svg.getScreenCTM();if(!matrix)return;
 const p=new DOMPoint(e.clientX,e.clientY).matrixTransform(matrix.inverse()),b=view(),f=e.deltaY>0?1.2:1/1.2;
 set([p.x+(b[0]-p.x)*f,p.y+(b[1]-p.y)*f,b[2]*f,b[3]*f]);
},{passive:false});
const hint=document.createElement('p');hint.textContent='操作：左鍵或中鍵拖曳平移、滾輪縮放；「顯示全圖」可還原。';svg.before(hint);
})();
