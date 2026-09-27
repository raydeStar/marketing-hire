import {useState} from 'react';
import type {MarketingDraft,MarketingState} from '../components/MarketingPanels';
import {draftText} from './draftText';
import './magical-web.css';

/** A bounded line comparison: readable original text, with only changed lines accented. */
function changedLines(before:string,after:string){
  const left=before.split('\n'),right=after.split('\n');
  if(left.length>500||right.length>500)return {left,right,removed:new Set<number>(),added:new Set<number>(),bounded:true};
  const rows=Array.from({length:left.length+1},()=>new Uint16Array(right.length+1));
  for(let i=left.length-1;i>=0;i--)for(let j=right.length-1;j>=0;j--)rows[i][j]=left[i]===right[j]?rows[i+1][j+1]+1:Math.max(rows[i+1][j],rows[i][j+1]);
  const removed=new Set<number>(),added=new Set<number>();let i=0,j=0;
  while(i<left.length||j<right.length){if(i<left.length&&j<right.length&&left[i]===right[j]){i++;j++;}else if(j>=right.length||i<left.length&&rows[i+1][j]>=rows[i][j+1])removed.add(i++);else added.add(j++);}
  return {left,right,removed,added,bounded:false};
}
export function ArtifactCompare({before,after,beforeLabel='Earlier version',afterLabel='Current version'}:{before:string;after:string;beforeLabel?:string;afterLabel?:string}){
  const [only,setOnly]=useState(false);const lines=changedLines(before,after);
  const panel=(content:string[],changed:Set<number>,kind:string,label:string)=><section aria-label={label}><h4>{label}</h4><div className="fe-compare-text">{content.map((text,index)=>(!only||changed.has(index)||lines.bounded)&&<div className={changed.has(index)?'fe-compare-'+kind:''} key={index}><span className="fe-compare-marker" aria-hidden="true">{changed.has(index)?kind==='added'?'+':'−':' '}</span><span>{text||'\u00a0'}</span></div>)}</div></section>;
  return <div className="fe-artifact-compare" aria-label="Version comparison"><div className="fe-compare-toolbar"><small>{before===after?'The text is unchanged.':lines.bounded?'Long document: full versions shown without line highlights.':`${lines.removed.size} line${lines.removed.size===1?'':'s'} removed · ${lines.added.size} line${lines.added.size===1?'':'s'} added`}</small>{!lines.bounded&&before!==after&&<label><input type="checkbox" checked={only} onChange={event=>setOnly(event.target.checked)}/> Changes only</label>}</div><div className="fe-compare-panels">{panel(lines.left,lines.removed,'removed',beforeLabel)}{panel(lines.right,lines.added,'added',afterLabel)}</div></div>;
}

export function PolishedDraftComparison({draft,state}:{draft:MarketingDraft;state:MarketingState}){
  const match=/polished version of draft #(\d+)/i.exec(draft.rationale);
  if(!match)return null;
  const previous=state.drafts.find(item=>item.id===Number(match[1]));
  return <details className="fe-history"><summary>Compare the polished draft with #{match[1]}</summary>{previous?<ArtifactCompare before={draftText(previous)} after={draftText(draft)} beforeLabel={'Original draft #'+previous.id} afterLabel={'Polished draft #'+draft.id}/>:<p className="fe-muted">The original draft #{match[1]} is no longer available to compare.</p>}</details>;
}
