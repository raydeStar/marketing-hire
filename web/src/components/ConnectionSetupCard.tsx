import {useEffect,useState} from 'react';
import {Cable,ExternalLink,ShieldCheck,X} from 'lucide-react';
import {api} from '../api';

type Target='google'|'mcp';
type McpView={version:string;systemStore:string;google:{redirectUri:string;clientType:string;products:{id:string;name:string;access:string;scopes:string[]}[]}};
type GoogleStart={attemptId:string;authorizationUrl:string};
type GoogleStatus={phase:string;error?:string};

export function ConnectionSetupCard({target,initialProduct,online,onClose,onConnected}:{target:Target;initialProduct?:string;online:boolean;onClose:()=>void;onConnected:()=>Promise<unknown>}){
 const [view,setView]=useState<McpView|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const [product,setProduct]=useState(initialProduct||'gmail-read'),[clientId,setClientId]=useState(''),[clientSecret,setClientSecret]=useState('');
 const [name,setName]=useState(''),[endpoint,setEndpoint]=useState(''),[storage,setStorage]=useState('system'),[token,setToken]=useState('');
 useEffect(()=>{let stale=false;api<McpView>('/settings/mcp').then(next=>{if(!stale)setView(next);}).catch(reason=>{if(!stale)setError(reason.message);});return()=>{stale=true;};},[]);
 async function run(work:()=>Promise<void>){setBusy(true);setError('');try{await work();}catch(reason){setError((reason as Error).message);}finally{setBusy(false);}}
 async function connectGoogle(){
  if(!view)return;
  const popup=window.open('about:blank','thaddeus-google-connect','popup,width=560,height=760');
  const secret=clientSecret;setClientSecret('');
  try{
   const started=await api<GoogleStart>('/settings/mcp/google/start',{version:view.version,product,clientId:clientId.trim(),clientSecret:secret});
   if(popup)popup.location.href=started.authorizationUrl;else window.open(started.authorizationUrl,'_blank','noopener,noreferrer');
   for(let count=0;count<300;count++){
    await new Promise(resolve=>setTimeout(resolve,2000));
    const status=await api<GoogleStatus>('/settings/mcp/google/status/'+started.attemptId);
    if(status.phase==='connected'){popup?.close();await onConnected();return;}
    if(status.phase==='failed')throw new Error(status.error||'Google sign-in failed.');
   }
   throw new Error('Google sign-in expired. Start it again when you are ready.');
  }catch(reason){popup?.close();throw reason;}
 }
 async function connectMcp(){
  if(!view)return;
  const submitted=token;setToken('');
  await api('/settings/mcp',{version:view.version,name:name.trim(),endpoint:endpoint.trim(),storage,token:storage==='none'?undefined:submitted},'PUT');
  await onConnected();
 }
 return <section className="connection-setup-card" aria-label="Secure connection setup" tabIndex={-1}>
  <div className="connection-setup-title"><span><Cable size={17}/><strong>{target==='google'?'Connect Google':'Connect a service'}</strong></span><button type="button" aria-label="Close connection setup" onClick={onClose}><X size={16}/></button></div>
  <div className="connection-setup-boundary"><ShieldCheck size={16}/><p>These fields submit directly to this host. Secrets stay in {view?.systemStore||'the system credential vault'} and never become chat messages or model context.</p></div>
  {target==='google'?<form aria-label="Connect Google Workspace" onSubmit={event=>{event.preventDefault();void run(connectGoogle);}}>
   <label>Permission<select aria-label="Google Workspace permission" value={product} disabled={busy} onChange={event=>setProduct(event.target.value)}>{view?.google.products.map(item=><option key={item.id} value={item.id}>{item.name} · {item.access}</option>)}</select></label>
   <p className="muted">Choose only what this workflow needs. Reading mail and sending approved mail are separate connections.</p>
   <label>OAuth client ID<input aria-label="Google OAuth client ID" value={clientId} maxLength={512} disabled={busy} onChange={event=>setClientId(event.target.value)} autoComplete="off" spellCheck={false}/></label>
   <label>OAuth client secret<input aria-label="Google OAuth client secret" type="password" value={clientSecret} maxLength={512} disabled={busy} onChange={event=>setClientSecret(event.target.value)} autoComplete="new-password" spellCheck={false}/></label>
   <details><summary>First time? Google Cloud setup</summary><p>Create a <strong>{view?.google.clientType||'Desktop app'}</strong> OAuth client and use <code>{view?.google.redirectUri}</code> as the loopback callback. Enable only the API you intend to connect.</p><a href="https://developers.google.com/identity/protocols/oauth2/native-app" target="_blank" rel="noreferrer">Google desktop OAuth guide <ExternalLink size={13}/></a></details>
   <button className="primary" disabled={!online||busy||!view||!clientId.trim()||!clientSecret.trim()}>{busy?'Waiting for Google…':'Continue with Google'}</button>
  </form>:<form aria-label="Add MCP connector" onSubmit={event=>{event.preventDefault();void run(connectMcp);}}>
   <label>Service name<input aria-label="Connector name" value={name} maxLength={80} disabled={busy} onChange={event=>setName(event.target.value)} placeholder="GitHub, Notion, home server…"/></label>
   <label>Streamable HTTP endpoint<input aria-label="MCP endpoint" type="url" value={endpoint} disabled={busy} onChange={event=>setEndpoint(event.target.value)} placeholder="https://mcp.example.com/mcp"/></label>
   <label>Authentication<select aria-label="MCP authentication" value={storage} disabled={busy} onChange={event=>{setStorage(event.target.value);setToken('');}}><option value="system">Bearer token in {view?.systemStore||'system vault'}</option><option value="session">Bearer token until host stops</option><option value="none">No credential</option></select></label>
   {storage!=='none'&&<label>Bearer token<input aria-label="MCP bearer token" type="password" autoComplete="new-password" spellCheck={false} maxLength={2048} value={token} disabled={busy} onChange={event=>setToken(event.target.value)}/></label>}
   <button className="primary" disabled={!online||busy||!view||!name.trim()||!endpoint.trim()||(storage!=='none'&&!token.trim())}>{busy?'Checking server…':'Verify & connect'}</button>
  </form>}
  {error&&<p role="alert" className="connection-error">{error}</p>}
 </section>;
}
