import {useEffect,useState} from 'react';
import {KeyRound,ShieldCheck,RefreshCw} from 'lucide-react';
import {api} from '../api';
import type {Provider} from '../types';

type Credential={id:string;endpoint:string;storage:string;status:string;inUse:boolean;needsReentry:boolean;selected:boolean};
type Connection={version:string;provider:Provider;credentialMode:string;systemStore:string;environmentEndpoint?:string;environmentPresent:boolean;credentials:Credential[]};
export function ModelConnectionSettings({online,onChanged}:{online:boolean;onChanged:()=>Promise<unknown>}){
 const [connection,setConnection]=useState<Connection|null>(null),[provider,setProvider]=useState<Provider>({kind:'scripted',model:'fictional-weekly-v1',reasoning:'high'});
 const [storage,setStorage]=useState('keep'),[key,setKey]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState('');
 const [models,setModels]=useState<string[]>([]),[observed,setObserved]=useState<{conversation:{observed:boolean};typedProposal:{observed:boolean};reportedUsage:{observed:boolean}}|null>(null);
 async function load(reset=true){const saved=await api<Connection>('/settings/connection');setConnection(saved);if(reset){setProvider(saved.provider);setStorage('keep');}return saved;}
 useEffect(()=>{void load().catch(error=>setError(error.message));},[]);
 const changedDestination=provider.kind==='compatible'&&(connection?.provider.kind!=='compatible'||(provider.endpoint||'').replace(/\/$/,'')!==(connection.provider.endpoint||'').replace(/\/$/,''));
 const newKey=provider.kind==='compatible'&&['system','session'].includes(storage);
 const selected=connection?.credentials.find(item=>item.selected);
 async function perform(work:()=>Promise<void>){setBusy(true);setError('');setMessage('');try{await work();await onChanged();}catch(error){setError((error as Error).message);await load(false).catch(()=>{});}finally{setBusy(false);}}
 async function save(){
  if(!connection)return;
  const submitted=key;setKey('');
  const saved=await api<Connection>('/settings/connection',{version:connection.version,provider,credentialMode:storage,key:newKey?submitted:undefined},'PUT');
  setConnection(saved);setProvider(saved.provider);setStorage('keep');setModels([]);setObserved(null);setMessage('Connection saved. No model call was made.');
 }
 return <section className="model-connection" aria-label="Model connection">
  <div className="connection-heading"><div><h2 id="model-connection-heading" tabIndex={-1}>Connect a model</h2><p>Choose where Thaddeus thinks. Your credentials stay on this host.</p></div><KeyRound size={23}/></div>
  {error&&<p className="connection-error" role="alert">{error}</p>}
  {!connection?<button disabled={!online||busy} onClick={()=>perform(async()=>{await load();})}>Load connection settings</button>:<>
   <label>Provider<select aria-label="Provider" value={provider.kind} disabled={busy} onChange={e=>{setProvider({...provider,kind:e.target.value,model:e.target.value==='scripted'?'fictional-weekly-v1':provider.kind==='scripted'?'':provider.model});setStorage(e.target.value==='compatible'?'system':'none');setKey('');}}><option value="scripted">Fictional demo · no model calls</option><option value="compatible">Connect an OpenAI-compatible provider</option></select></label>
   {provider.kind==='compatible'?<>
    <label>Provider URL<input aria-label="Provider URL" type="url" value={provider.endpoint||''} placeholder="https://your-provider.example/v1" disabled={busy} onChange={e=>setProvider({...provider,endpoint:e.target.value})}/><small>Use the base API URL supplied by your provider, usually ending in /v1.</small></label>
    <div className="connection-fields"><label>Exact model ID<input aria-label="Exact model ID" value={provider.model} list="discovered-models" disabled={busy} onChange={e=>setProvider({...provider,model:e.target.value})}/><datalist id="discovered-models">{models.map(model=><option key={model} value={model}/>)}</datalist></label><label>Reasoning<select aria-label="Reasoning" value={provider.reasoning} disabled={busy} onChange={e=>setProvider({...provider,reasoning:e.target.value})}><option value="high">High</option><option value="medium">Medium</option><option value="low">Low</option></select></label></div>
    <label>API key storage<select aria-label="API key storage" value={storage} disabled={busy} onChange={e=>{setStorage(e.target.value);setKey('');}}>
     <option value="keep" disabled={changedDestination}>Keep this connection’s current credential choice</option>
     <option value="system">Save in {connection.systemStore}</option><option value="session">Keep only until this host stops</option>
     <option value="none">This endpoint needs no API key</option>{connection.environmentPresent&&<option value="environment">Use the host’s configured environment key</option>}
    </select></label>
    {newKey&&<label>API key<input aria-label="API key" type="password" autoComplete="new-password" spellCheck={false} value={key} disabled={busy} onChange={e=>setKey(e.target.value)} placeholder="Paste your provider key"/><small>{storage==='system'?`Saved through ${connection.systemStore}; never included in notes or exports.`:'Held in host memory. Enter it again after a restart; it is never saved to disk.'}</small></label>}
    {changedDestination&&storage==='keep'&&<p role="status">Choose how to authenticate this new destination before saving.</p>}
    {selected?.needsReentry&&<p className="connection-notice" role="status">The previous session key expired when the host stopped. Choose a storage option and enter it again.</p>}
    {connection.credentialMode==='missing'&&<p className="connection-notice" role="status">The selected key was removed. Enter a replacement or explicitly choose an endpoint with no key.</p>}
    {storage==='environment'&&<p className="muted">This environment key is bound to {connection.environmentEndpoint||'no endpoint yet'}. It cannot follow a changed URL.</p>}
   </>:<p className="connection-notice">The fictional demo runs locally without a provider account or inference. It does not test a real model.</p>}
   <div className="connection-actions"><button className="primary" disabled={!online||busy||(newKey&&!key.trim())||(changedDestination&&storage==='keep')} onClick={()=>perform(save)}>Save connection</button><button disabled={!online||busy} onClick={()=>perform(async()=>{await load();setKey('');})}><RefreshCw size={15}/> Reload saved settings</button></div>
   <div className="connection-boundary"><ShieldCheck size={18}/><p>Token totals stay at the top of the study. Each message has a token allowance; an uncertified provider can exceed a requested limit. Strict token admission refuses unverified providers. Saving a connection does not generate a reply.</p></div>
   <div className="connection-actions"><button disabled={!online||busy} onClick={()=>perform(async()=>{const result=await api<{status:string;models:string[]}>('/settings/test',{});setModels(result.models);setMessage(result.status);})}>Check saved connection</button><button disabled={!online||busy} onClick={()=>perform(async()=>{setObserved(await api('/settings/diagnostics'));})}>Inspect observed capabilities</button></div>
   <p className="muted">Connection checks request a model list, without generating text. Successful discovery does not prove tool support. Local GPU inference needs a coordinated resource lease; selecting a model does not load it.</p>
   {message&&<p role="status">{message}</p>}{observed&&<ul className="connection-observations">{[['Conversation',observed.conversation.observed],['Typed proposals',observed.typedProposal.observed],['Reported token usage',observed.reportedUsage.observed]].map(([name,passed])=><li key={String(name)}>{name}: {passed?'observed in retained runs':'not yet observed for this exact connection'}</li>)}</ul>}
   {connection.credentials.length>0&&<details className="saved-credentials"><summary>Manage saved credentials ({connection.credentials.length})</summary><p>Removing a key prevents future authenticated calls. It does not revoke the key with your provider.</p>{connection.credentials.map(item=><div className="saved-credential" key={item.id}><div><strong>{item.selected?'Current connection':'Saved connection'}</strong><p>{item.endpoint}</p><small>{item.storage==='system'?connection.systemStore:'Until host stops'} · {item.status==='ready'?'saved':'needs attention'}{item.inUse?' · finish or cancel its active work before removal':''}</small></div><button disabled={!online||busy||item.inUse} onClick={()=>perform(async()=>{const result=await api<Connection>(`/settings/connection/credentials/${item.id}/remove`,{version:connection.version});setConnection(result);setMessage('Credential removed from Thaddeus. The provider account is unchanged.');})}>Remove key</button></div>)}</details>}
  </>}
 </section>;
}
