import {useRef,type PointerEvent} from 'react';

export const panelWidthKey='business-company-panel-width';
export const minimumPanelWidth=280;
export function maximumPanelWidth(){return Math.max(minimumPanelWidth,Math.min(600,window.innerWidth-480));}
export function clampPanelWidth(width:number){return Math.round(Math.max(minimumPanelWidth,Math.min(maximumPanelWidth(),width)));}
export function initialPanelWidth(){
  try{const saved=Number(localStorage.getItem(panelWidthKey));if(Number.isFinite(saved)&&saved>=minimumPanelWidth)return clampPanelWidth(saved);}catch{}
  return window.innerWidth>=1700?364:344;
}

export function PanelResize({width,onChange,onDragging}:{width:number;onChange:(width:number)=>void;onDragging:(dragging:boolean)=>void}){
  const drag=useRef<{x:number;width:number}|null>(null);
  function finish(event:PointerEvent<HTMLDivElement>){
    drag.current=null;onDragging(false);
    if(event.currentTarget.hasPointerCapture(event.pointerId))event.currentTarget.releasePointerCapture(event.pointerId);
  }
  return <div className="company-panel-resize" role="separator" aria-label="Resize company panel" aria-controls="company-panel" aria-orientation="vertical" aria-valuemin={minimumPanelWidth} aria-valuemax={maximumPanelWidth()} aria-valuenow={width} aria-valuetext={`${width} pixels wide`} tabIndex={0} title="Drag to resize · Arrow keys adjust width"
    onPointerDown={event=>{if(event.button!==0)return;event.preventDefault();drag.current={x:event.clientX,width};event.currentTarget.setPointerCapture(event.pointerId);onDragging(true);}}
    onPointerMove={event=>{if(drag.current)onChange(clampPanelWidth(drag.current.width+drag.current.x-event.clientX));}}
    onPointerUp={finish} onPointerCancel={finish} onLostPointerCapture={()=>{drag.current=null;onDragging(false);}}
    onKeyDown={event=>{
      const next=event.key==='ArrowLeft'?width+20:event.key==='ArrowRight'?width-20:event.key==='Home'?minimumPanelWidth:event.key==='End'?maximumPanelWidth():null;
      if(next!==null){event.preventDefault();onChange(clampPanelWidth(next));}
    }}><span/></div>;
}
