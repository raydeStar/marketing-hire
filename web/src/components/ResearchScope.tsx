import type {Page,ResearchAvailability,MemoryView,MemorySelection} from '../types';
import {BudgetFields,type Limits} from './BudgetFields';

type Props={availability?:ResearchAvailability;pages:Page[];scope:string[];onScope:(scope:string[])=>void;memories:MemoryView[];memoryScope:MemorySelection[];onMemories:(scope:MemorySelection[])=>void;hosts:string;onHosts:(hosts:string)=>void;limits:Limits;onLimits:(limits:Limits)=>void};
export function ResearchScope({availability,pages,scope,onScope,memories,memoryScope,onMemories,hosts,onHosts,limits,onLimits}:Props){
  return <section className="scope-card" aria-label="Research scope">
    <h2>A private workspace for this question</h2>
    <p role="status">{availability?.summary||'Research availability has not been confirmed.'}</p>
    <p>Select the notes Thaddeus may read. An imported result will need your approval.</p>
    {pages.filter(page=>page.path.startsWith('notes/')).map(page=><label className="checkbox" key={page.path}>
      <input type="checkbox" checked={scope.includes(page.path)} onChange={event=>onScope(event.target.checked?[...scope,page.path]:scope.filter(path=>path!==page.path))}/>{page.path}
    </label>)}
    {memories.length>0&&<fieldset><legend>Remembered context (optional)</legend><p>Choose up to eight entries. Shares only selected statements and source quotations. Other text in a source note requires a separate note grant above.</p>
      {memories.map(({entry,sourceStatus})=><label className="checkbox" key={entry.id}>
        <input type="checkbox" checked={memoryScope.some(selection=>selection.id===entry.id)} disabled={(sourceStatus!=='current'||memoryScope.length>=8)&&!memoryScope.some(selection=>selection.id===entry.id)}
          onChange={event=>onMemories(event.target.checked?[...memoryScope,{id:entry.id,version:entry.version}]:memoryScope.filter(selection=>selection.id!==entry.id))}/>
        {entry.statement}{sourceStatus!=='current'?' · source needs review':''}
      </label>)}
    </fieldset>}
    {memoryScope.some(selection=>!memories.some(({entry,sourceStatus})=>entry.id===selection.id&&entry.version===selection.version&&sourceStatus==='current'))&&
      <p className="error" role="status">A selected memory changed or was forgotten. <button onClick={()=>onMemories([])}>Clear memory selection</button></p>}
    <label>Public source websites (optional)<input placeholder="docs.example.com, example.org" value={hosts} onChange={event=>onHosts(event.target.value)} maxLength={2000}/></label>
    <p className="muted">Enter exact hostnames, separated by commas. Include source links in your question. Access is limited to these public websites; signing in is unavailable.</p>
    <BudgetFields value={limits} onChange={onLimits}/>
  </section>;
}
