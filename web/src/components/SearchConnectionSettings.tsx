import {useEffect,useState} from 'react';
import {api} from '../api';
import type {SearchSummary} from '../types';

type Connection={version:string;summary:SearchSummary;systemStore:string;credentials:{id:string;storage:string;status:string;selected:boolean;inUse:boolean;needsReentry:boolean}[]};
export function SearchConnectionSettings({online,onChanged}:{online:boolean;onChanged:()=>Promise<unknown>}){
 const [view,setView]=useState<Connection|null>(null),[key,setKey]=useState(''),[storage,setStorage]=useState('system'),[retain,setRetain]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState('');
 const [monthlyLimit,setMonthlyLimit]=useState('100');
 async function load(){const next=await api<Connection>('/settings/search');setView(next);setMonthlyLimit(String(next.summary.budget?.monthlyLimit??100));}
 useEffect(()=>{void load().catch(error=>setError(error.message));},[]);
 async function perform(work:()=>Promise<void>){
  setBusy(true);setError('');setMessage('');
  try{await work();await load();await onChanged();}catch(error){setError((error as Error).message);await load().catch(()=>{});}finally{setBusy(false);}
 }
 async function save(){
  if(!view)return;const submitted=key;setKey('');
  await api('/settings/search',{version:view.version,storage,key:submitted,retainResults:retain},'PUT');
  setMessage('Search connection saved. No query was sent; the provider has not verified this key yet.');
 }
 return <section className="model-connection" aria-label="Public search connection"><h2>Connect public search</h2>
  <p>Optional Brave Search API connection. The host holds its key and sends only the queries made for an explicitly enabled research task.</p>
  <p>Search requests are separate from model tokens and may be billed by the provider. <a href="https://brave.com/search/api/" target="_blank" rel="noreferrer">Check Brave’s plans and storage rights</a>.</p>
  {view&&<><p role="status">{view.summary.configured?'A search key is saved. Enable search separately for each research task.':'Search is not configured. Existing chat and selected-source research remain available.'}</p>
   {view.summary.budget&&<fieldset><legend>Monthly search allowance</legend>
    <p role="status">{view.summary.budget.used} of {view.summary.budget.monthlyLimit} requests used · {view.summary.budget.remaining} remaining in {view.summary.budget.month} (UTC).</p>
    <label>Monthly search limit<input aria-label="Monthly search limit" type="number" min="0" max="100000" step="1" value={monthlyLimit} disabled={busy} onChange={event=>setMonthlyLimit(event.target.value)}/></label>
    <p>Zero pauses new searches. Failed or interrupted attempts count. Changing the limit does not reset usage. The allowance renews on the first of each month at 00:00 UTC.</p>
    <p>This counts only this study, not your Brave account. Other apps, separate studies and restoring an older backup can leave account usage higher. Set a spending limit with Brave too.</p>
    <button disabled={!online||busy||!/^\d+$/.test(monthlyLimit)||Number(monthlyLimit)>100000} onClick={()=>perform(async()=>{await api('/settings/search/budget',{version:view.summary.budget!.version,monthlyLimit:Number(monthlyLimit)},'PUT');setMessage('Monthly search limit saved. No search was sent.');})}>Save search limit</button>
   </fieldset>}
   <label>Search key storage<select aria-label="Search key storage" value={storage} disabled={busy} onChange={event=>setStorage(event.target.value)}><option value="system">Save in {view.systemStore}</option><option value="session">Keep only until this host stops</option></select></label>
   <label>Brave Search API key<input aria-label="Brave Search API key" type="password" autoComplete="new-password" spellCheck={false} value={key} disabled={busy} onChange={event=>setKey(event.target.value)} maxLength={2048}/></label>
   <label className="checkbox"><input type="checkbox" checked={retain} disabled={busy} onChange={event=>setRetain(event.target.checked)}/>My search plan permits retaining API results in this study’s task history.</label>
   <p>Thaddeus keeps queries and result receipts for replay. The search key stays outside the study, its backups, and the worker.</p>
   <button disabled={!online||busy||!key.trim()||!retain} onClick={()=>perform(save)}>Save search connection</button>
   <button disabled={!online||busy||!view.summary.credentialId} onClick={()=>perform(async()=>{const result=await api<{message:string}>('/settings/search/check',{version:view.version});setMessage(result.message);})}>Check saved search key</button>
   {view.credentials.map(record=><div className="device" key={record.id}><span>{record.selected?'Selected search key':'Previous search key'}<small>{record.storage} · {record.status}{record.needsReentry?' · enter again after restart':''}{record.inUse?' · in use by research':''}</small></span>
    <button disabled={!online||busy||record.inUse} onClick={()=>perform(async()=>{await api('/settings/search/credentials/'+record.id+'/remove',{version:view.version});setMessage('Search key removed. Existing search receipts remain in your history.');})}>Remove search key</button></div>)}
  </>}
  <button disabled={!online||busy} onClick={()=>perform(load)}>Reload search settings</button>
  {message&&<p role="status">{message}</p>}{error&&<p role="alert" className="connection-error">{error}</p>}
 </section>;
}
