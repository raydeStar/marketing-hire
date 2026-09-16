import {useEffect,useState} from 'react';
import {api} from '../api';
import type {SearchSummary} from '../types';

type Connection={version:string;summary:SearchSummary;systemStore:string;credentials:{id:string;storage:string;status:string;selected:boolean;inUse:boolean;needsReentry:boolean;retainResults:boolean}[]};
export function SearchConnectionSettings({online,onChanged}:{online:boolean;onChanged:()=>Promise<unknown>}){
 const [view,setView]=useState<Connection|null>(null),[key,setKey]=useState(''),[storage,setStorage]=useState('system'),[retain,setRetain]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState(''),[limitMessage,setLimitMessage]=useState('');
 const [monthlyLimit,setMonthlyLimit]=useState('100');
 const validLimit=/^(?:\d+|\d{1,3}(?:,\d{3})+)$/.test(monthlyLimit.trim())&&Number(monthlyLimit.replaceAll(',',''))<=100000;
 const limit=Number(monthlyLimit.replaceAll(',','')),limitChanged=validLimit&&limit!==view?.summary.budget?.monthlyLimit;
 const selected=view?.credentials.find(record=>record.selected),usageChanged=!!selected&&retain!==selected.retainResults;
 async function load(resetDraft=false){
  const next=await api<Connection>('/settings/search');setView(next);
  // Reloading a receipt must not quietly discard the owner's unfinished arithmetic.
  if(resetDraft){setMonthlyLimit(String(next.summary.budget?.monthlyLimit??100));setRetain(next.credentials.find(record=>record.selected)?.retainResults??false);}
  return next;
 }
 useEffect(()=>{void load(true).catch(error=>setError(error.message));},[]);
 async function perform(work:()=>Promise<void>){
  setBusy(true);setError('');setMessage('');
  try{await work();await load();await onChanged();}catch(error){setError((error as Error).message);await load().catch(()=>{});}finally{setBusy(false);}
 }
 async function saveLimit(){
  if(!view?.summary.budget||!validLimit)throw new Error('Enter a whole number from 0 to 100,000.');
  const saved=await api('/settings/search/budget',{version:view.summary.budget.version,monthlyLimit:limit},'PUT');
  setLimitMessage(`Saved: ${saved.monthlyLimit.toLocaleString()} searches per month. No search was sent.`);
 }
 async function saveSettings(){
  if(!view||!validLimit)return;
  // The general Save includes the allowance too; there is no second secret save ritual.
  if(limitChanged)await saveLimit();
  if(key.trim()){
   await api('/settings/search',{version:view.version,storage,key:key.trim(),retainResults:retain},'PUT');setKey('');
  }else if(usageChanged){await api('/settings/search/usage',{version:view.version,retainResults:retain},'PUT');}
  setMessage('Search settings saved. No provider request was made.');
 }
 return <section className="model-connection" aria-label="Public search connection"><h2>Connect public search</h2>
  <p>Optional Brave Search API connection. Searches run only when you request them.</p>
  <p>This allowance controls Thaddeus requests, not Brave billing. Keep Brave’s paid balance at $0 and automatic reload off for no paid overage. <a href="https://api-dashboard.search.brave.com/documentation/resources/help-feedback" target="_blank" rel="noreferrer">Brave billing & limits</a>.</p>
  {view&&<><p role="status">{view.summary.temporaryConfigured?'A search key is saved. Search → Web is ready.':'No available search key. Chat and supplied-source research remain available.'}</p>
   {view.summary.budget&&<form aria-label="Monthly search allowance" onSubmit={event=>{event.preventDefault();void perform(saveLimit);}}><fieldset><legend>Monthly search allowance</legend>
    <p>{view.summary.budget.used.toLocaleString()} of {view.summary.budget.monthlyLimit.toLocaleString()} requests used · {view.summary.budget.remaining.toLocaleString()} remaining in {view.summary.budget.month} (UTC).</p>
    <label>Monthly search limit<input aria-label="Monthly search limit" inputMode="numeric" value={monthlyLimit} disabled={busy} onChange={event=>{setMonthlyLimit(event.target.value);setLimitMessage('');}} aria-describedby="search-limit-help"/></label>
    <p id="search-limit-help">Zero pauses searches. Failed or interrupted attempts count. Changing the limit keeps usage; the allowance renews on the first of each month (UTC).</p>
    <button type="submit" disabled={!online||busy||!validLimit||!limitChanged}>{busy?'Saving…':'Save search limit'}</button>
    {limitChanged&&<p>Unsaved monthly limit</p>}{!validLimit&&<p role="alert">Enter a whole number from 0 to 100,000.</p>}
    {limitMessage&&<p role="status">{limitMessage}</p>}
    <p>This counts this study only. Other apps and restored backups can make Brave account usage higher.</p>
   </fieldset></form>}
   <form aria-label="Search connection settings" onSubmit={event=>{event.preventDefault();void perform(saveSettings);}}>
    <label>Search plan<select aria-label="Search plan" value={retain?'retained':'standard'} disabled={busy||selected?.inUse} onChange={event=>setRetain(event.target.value==='retained')}><option value="standard">Standard / free · temporary results</option><option value="retained">I have separate rights to retain results</option></select></label>
    <p>{retain?'Queries and results may be retained in research history. Choose this only with explicit storage rights from Brave.':'Results stay on the current search screen. Only the request count is saved. Retained agent research requires separate storage rights.'}</p>
    <label>Search key storage<select aria-label="Search key storage" value={storage} disabled={busy} onChange={event=>setStorage(event.target.value)}><option value="system">Save in {view.systemStore}</option><option value="session">Keep only until this host stops</option></select></label>
    <label>{selected?'Replace Brave Search API key (optional)':'Brave Search API key'}<input aria-label="Brave Search API key" type="password" autoComplete="new-password" spellCheck={false} value={key} disabled={busy} onChange={event=>setKey(event.target.value)} maxLength={2048}/></label>
    {selected&&<p>Leave the key blank to keep the saved key.</p>}
    <button type="submit" disabled={!online||busy||!validLimit||!(key.trim()||usageChanged||limitChanged)}>Save search settings</button>
   </form>
   <button disabled={!online||busy||!view.summary.credentialId} onClick={()=>perform(async()=>{const result=await api<{message:string}>('/settings/search/check',{version:view.version});setMessage(result.message);})}>Check saved search key</button>
   {view.credentials.map(record=><div className="device" key={record.id}><span>{record.selected?'Selected search key':'Previous search key'}<small>{record.storage} · {record.status}{record.needsReentry?' · enter again after restart':''}{record.inUse?' · in use by research':''}</small></span>
    <button disabled={!online||busy||record.inUse} onClick={()=>perform(async()=>{await api('/settings/search/credentials/'+record.id+'/remove',{version:view.version});setMessage('Search key removed.');})}>Remove search key</button></div>)}
  </>}
  <button disabled={!online||busy} onClick={()=>perform(async()=>{await load();setMessage('Saved settings reloaded. Your unsaved edits are still here.');})}>Reload search settings</button>
  {message&&<p role="status">{message}</p>}{error&&<p role="alert" className="connection-error">{error}</p>}
 </section>;
}
