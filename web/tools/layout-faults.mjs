// Shared with ux-tour: a screenshot does not reveal every clipped or overflowing child.
export function layoutFaults(){
  const name=el=>(el.tagName.toLowerCase()+(typeof el.className==='string'&&el.className?'.'+el.className.trim().split(/\s+/).slice(0,2).join('.'):'')+' “'+(el.textContent||'').trim().replace(/\s+/g,' ').slice(0,40)+'”');
  const faults=[];
  if(document.documentElement.scrollWidth>innerWidth+2)faults.push('page scrolls sideways by '+(document.documentElement.scrollWidth-innerWidth)+'px');
  for(const el of document.querySelectorAll('.fe-app *, .fe-onboarding *, dialog *')){
    if(el instanceof SVGElement||el.matches('details:not([open])')||el.closest('details:not([open]) > :not(summary)'))continue;
    const style=getComputedStyle(el);
    if(style.display==='inline'||style.display==='contents'||style.position==='absolute'||style.position==='fixed')continue;
    const box=el.getBoundingClientRect();
    if(box.width<12||box.height<4||box.bottom<0||box.top>innerHeight)continue;
    if(style.overflowY==='visible'&&el.children.length){
      const kid=[...el.children].find(child=>{const s=getComputedStyle(child),b=child.getBoundingClientRect();return s.position!=='absolute'&&s.position!=='fixed'&&b.height>0&&b.width>0&&b.bottom>box.bottom+3;});
      if(kid)faults.push(`${name(el)} spills ${Math.round(kid.getBoundingClientRect().bottom-box.bottom)}px below its box`);
    }
    if((style.overflowX==='hidden'||style.overflowX==='clip')&&style.textOverflow!=='ellipsis'&&el.childElementCount===0&&(el.textContent||'').trim()&&el.scrollWidth>el.clientWidth+2)faults.push(`${name(el)} text is cut off`);
  }
  return [...new Set(faults)].slice(0,6);
}
