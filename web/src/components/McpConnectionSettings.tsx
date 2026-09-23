import {useEffect,useState} from 'react';
import {Cable,ChevronRight,RefreshCw,ShieldCheck,Trash2} from 'lucide-react';
import {api} from '../api';

type McpTool={remoteName:string;modelName:string;description:string;effect:string};
type McpConnector={id:string;name:string;endpoint:string;storage:string;status:string;authentication:string;created:string;updated:string;enabled:boolean;tools:McpTool[];needsReentry:boolean;inUse:boolean;account?:string;grantedScopes:string[]};
type McpView={version:string;systemStore:string;connectors:McpConnector[]};
type Target='google'|'mcp';

export function McpConnectionSettings({online,onChanged,onSetup}:{online:boolean;onChanged:()=>Promise<unknown>;onSetup:(target:Target)=>void}){
 const [view,setView]=useState<McpView|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState('');
 async function load(){const next=await api<McpView>('/settings/mcp');setView(next);return next;}
 useEffect(()=>{void load().catch(reason=>setError(reason.message));},[]);
 async function perform(work:()=>Promise<void>){setBusy(true);setError('');setMessage('');try{await work();await onChanged();}catch(reason){setError((reason as Error).message);await load().catch(()=>{});}finally{setBusy(false);}}
 return <section className="model-connection" aria-label="Connected services">
  <div className="connection-heading"><div><h2>Connected services</h2><p>Ask Thaddeus to connect something, or start here. Saved setup appears below. Access is checked when a task uses a service; this list is not a live service check.</p></div><Cable size={23}/></div>
  <div className="connection-boundary"><ShieldCheck size={18}/><p>Keys remain with this host in {view?.systemStore||'the system credential vault'}. Chat can open the setup, but secrets never enter chat, model context, logs, or exports.</p></div>
  {!view?<button disabled={!online||busy} onClick={()=>perform(async()=>{await load();})}>Load connected services</button>:<>
   <div className="connection-entry-actions"><button className="primary" type="button" disabled={!online||busy} onClick={()=>onSetup('google')}>Connect Google with Thaddeus <ChevronRight size={15}/></button><button type="button" disabled={!online||busy} onClick={()=>onSetup('mcp')}>Connect another service</button></div>
   <div className="connection-array" aria-label="Connection list">
    {view.connectors.length===0?<div className="connection-array-empty"><strong>No connected services</strong><p>Chat, artifacts, and public website reading still work. Ask “connect my Google Calendar” when you want to add one.</p></div>:view.connectors.map(connector=><article className="connection-array-item" key={connector.id}>
     <div className="connection-array-summary"><span className={'connection-status '+(connector.enabled&&connector.status==='ready'&&!connector.needsReentry?'ready':'attention')} aria-label={!connector.enabled?'Disabled':connector.needsReentry?'Reconnect required':connector.status==='ready'?'Setup saved':'Needs attention'}/><div><strong>{connector.name}</strong><small>{!connector.enabled?'Disabled':connector.needsReentry?'Reconnect required':connector.status==='ready'?'Setup saved':'Needs attention'}</small><p>{connector.account||connector.endpoint}</p><small>{connector.tools.length} tool{connector.tools.length===1?'':'s'} · {connector.storage==='oauth'?'OAuth in '+view.systemStore:connector.storage==='system'?view.systemStore:connector.storage==='session'?'Until host stops':'No credential'}{connector.needsReentry?' · reconnect required':''}{connector.inUse?' · scheduled work':''}</small><small>Setup updated {new Date(connector.updated).toLocaleString()}</small></div></div>
     <div className="connection-actions"><button type="button" title={connector.inUse?"Pause or cancel scheduled work before refreshing its tool list.":"Refresh the saved tool catalog"} disabled={!online||busy||connector.needsReentry||connector.inUse} onClick={()=>perform(async()=>{setView(await api<McpView>(`/settings/mcp/${connector.id}/refresh`,{version:view.version}));setMessage('Tool list refreshed.');})}><RefreshCw size={14}/> Refresh tools</button>{connector.needsReentry&&<button type="button" disabled={!online||busy} onClick={()=>onSetup(connector.storage==='oauth'?'google':'mcp')}>Reconnect</button>}<button type="button" disabled={!online||busy} onClick={()=>perform(async()=>{setView(await api<McpView>(`/settings/mcp/${connector.id}/remove`,{version:view.version}));setMessage('Connection and its host-held credential were removed.');})}><Trash2 size={14}/> Disconnect</button></div>
     <details><summary>Permissions and tools ({connector.tools.length})</summary>{connector.grantedScopes.length>0&&<ul>{connector.grantedScopes.map(scope=><li key={scope}><code>{scope}</code></li>)}</ul>}<ul className="mcp-tool-list">{connector.tools.map(tool=><li key={tool.modelName}><strong>{tool.remoteName}</strong><span>{tool.effect}</span><p>{tool.description}</p></li>)}</ul></details>
    </article>)}
   </div>
  </>}
  {message&&<p role="status">{message}</p>}{error&&<p role="alert" className="connection-error">{error}</p>}
 </section>;
}
