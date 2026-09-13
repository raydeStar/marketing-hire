import type {Page,ResearchAvailability} from '../types';
import {BudgetFields,type Limits} from './BudgetFields';

type Props={availability?:ResearchAvailability;pages:Page[];scope:string[];onScope:(scope:string[])=>void;hosts:string;onHosts:(hosts:string)=>void;limits:Limits;onLimits:(limits:Limits)=>void};
export function ResearchScope({availability,pages,scope,onScope,hosts,onHosts,limits,onLimits}:Props){
  return <section className="scope-card" aria-label="Research scope">
    <h2>A private workspace for this question</h2>
    <p role="status">{availability?.summary||'Research availability has not been confirmed.'}</p>
    <p>Select the notes Thaddeus may read. An imported result will need your approval.</p>
    {pages.filter(page=>page.path.startsWith('notes/')).map(page=><label className="checkbox" key={page.path}>
      <input type="checkbox" checked={scope.includes(page.path)} onChange={event=>onScope(event.target.checked?[...scope,page.path]:scope.filter(path=>path!==page.path))}/>{page.path}
    </label>)}
    <label>Public source websites (optional)<input placeholder="docs.example.com, example.org" value={hosts} onChange={event=>onHosts(event.target.value)} maxLength={2000}/></label>
    <p className="muted">Enter exact hostnames, separated by commas. Include source links in your question. Access is limited to these public websites; signing in is unavailable.</p>
    <BudgetFields value={limits} onChange={onLimits}/>
  </section>;
}
