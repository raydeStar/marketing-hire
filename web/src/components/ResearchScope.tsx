import type {Page,ResearchAvailability,MemoryView,MemorySelection,SearchSummary} from '../types';
import {BudgetFields,type Limits} from './BudgetFields';

type Props={searchConnection?:SearchSummary;search:boolean;onSearch:(value:boolean)=>void;openResults:boolean;onOpenResults:(value:boolean)=>void;searchQueries:number;onSearchQueries:(value:number)=>void;availability?:ResearchAvailability;pages:Page[];scope:string[];onScope:(scope:string[])=>void;memories:MemoryView[];memoryScope:MemorySelection[];onMemories:(scope:MemorySelection[])=>void;hosts:string;onHosts:(hosts:string)=>void;limits:Limits;onLimits:(limits:Limits)=>void};
export function ResearchScope({searchConnection,search,onSearch,openResults,onOpenResults,searchQueries,onSearchQueries,availability,pages,scope,onScope,memories,memoryScope,onMemories,hosts,onHosts,limits,onLimits}:Props){
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
    <fieldset><legend>Discover public sources (optional)</legend>
      <p>Selected notes and supplied source links need no search API requests. Model usage is separate.</p>
      {searchConnection?.budget&&<p role="status">Monthly search allowance: {searchConnection.budget.used} / {searchConnection.budget.monthlyLimit} used · {searchConnection.budget.remaining} remaining. This study only; resets monthly in UTC.</p>}
      <label className="checkbox"><input type="checkbox" checked={search} disabled={(!searchConnection?.configured||searchConnection.budget?.remaining===0)&&!search} onChange={event=>onSearch(event.target.checked)}/>Search the public web with Brave</label>
      {searchConnection?.budget?.remaining===0&&<p>Monthly allowance reached. Use supplied sources with search off, or review your limit in Settings.</p>}
      {!searchConnection?.configured&&<p>Connect public search in Settings on the host computer to use this option.</p>}
      {search&&<><label>Search request allowance<select aria-label="Search request allowance" value={searchQueries} onChange={event=>onSearchQueries(Number(event.target.value))}>{[1,2,3,4].map(count=><option key={count} value={count}>{count} request{count===1?'':'s'}</option>)}</select></label>
        <label className="checkbox"><input type="checkbox" checked={openResults} onChange={event=>onOpenResults(event.target.checked)}/>Allow opening the returned result pages</label>
        <p>Queries go to Brave and may include details from your question or selected context. Queries and results are saved in this study. Up to {searchQueries} searches, with separate provider charges. Page fetches share a 4-request limit and need an allowed website or permission to open results.</p>
      </>}
    </fieldset>
    <BudgetFields value={limits} onChange={onLimits}/>
  </section>;
}
