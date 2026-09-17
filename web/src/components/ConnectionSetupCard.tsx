import {useEffect,useRef,useState} from 'react';
import {Cable,ExternalLink,ShieldCheck,X} from 'lucide-react';
import {api} from '../api';

type Target='google'|'mcp';
type McpView={version:string;systemStore:string;google:{redirectUri:string;clientType:string;clientSetup:{configured:boolean;status:string;canRemove:boolean};products:{id:string;name:string;access:string;scopes:string[];connected:boolean}[]}};
type GoogleStart={attemptId:string;authorizationUrl:string;browserOpened:boolean};
type GoogleStatus={phase:string;error?:string;account?:string;connectedProducts?:string[];skippedProducts?:string[]};

export function ConnectionSetupCard({target,initialProduct,online,onClose,onConnected}:{target:Target;initialProduct?:string;online:boolean;onClose:()=>void;onConnected:(status?:GoogleStatus)=>Promise<unknown>}){
 const mounted=useRef(true);
 const appSetup=useRef<HTMLDetailsElement>(null);
 const [view,setView]=useState<McpView|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const [products,setProducts]=useState<string[]>([initialProduct||'gmail-read']);
 const [signIn,setSignIn]=useState<GoogleStart|null>(null),[notice,setNotice]=useState('');
 const [name,setName]=useState(''),[endpoint,setEndpoint]=useState(''),[storage,setStorage]=useState('system'),[token,setToken]=useState('');
 useEffect(()=>{let stale=false;mounted.current=true;api<McpView>('/settings/mcp').then(next=>{if(stale)return;setView(next);setProducts(current=>current.filter(id=>!next.google.products.find(item=>item.id===id)?.connected));}).catch(reason=>{if(!stale)setError(reason.message);});return()=>{stale=true;mounted.current=false;};},[]);
 async function run(work:()=>Promise<void>){
  setBusy(true);setError('');
  try{await work();}catch(reason){
   setError((reason as Error).message);
   // Refresh a partial vault write's removable reference instead of trapping setup behind a stale version.
   try{setView(await api<McpView>('/settings/mcp'));}catch{/* Keep the original, actionable error. */}
  }finally{setBusy(false);}
 }
 async function connectGoogle(){
  if(!view)return;
  setSignIn(null);setNotice('');
  try{
   const started=await api<GoogleStart>('/settings/mcp/google/start',{version:view.version,products});
   if(!mounted.current)return;
   setSignIn(started);
   for(let count=0;count<300;count++){
    await new Promise(resolve=>setTimeout(resolve,2000));
    if(!mounted.current)return;
    const status=await api<GoogleStatus>('/settings/mcp/google/status/'+started.attemptId);
    if(!mounted.current)return;
    if(status.phase==='connected'){await onConnected(status);return;}
    if(status.phase==='failed')throw new Error(status.error||'Google sign-in failed.');
   }
   throw new Error('Google sign-in expired. Start it again when you are ready.');
  }finally{setSignIn(null);}
 }
 async function importGoogle(file:File){
  if(!view)return;
  if(!file.size||file.size>16_384)throw new Error('Choose the original Google Desktop app credentials JSON (up to 16 KB).');
  setNotice('');
  setView(await api<McpView>('/settings/mcp/google/client',{version:view.version,credentialsJson:await file.text()},'PUT'));
  if(appSetup.current)appSetup.current.open=false;
  setNotice('Google app setup saved. You can now continue with Google.');
 }
 async function removeGoogleSetup(){
  if(!view)return;
  setView(await api<McpView>('/settings/mcp/google/client/remove',{version:view.version}));
  setNotice('App setup removed. Existing account connections are unchanged.');
 }
 async function connectMcp(){
  if(!view)return;
  const submitted=token;setToken('');
  await api('/settings/mcp',{version:view.version,name:name.trim(),endpoint:endpoint.trim(),storage,token:storage==='none'?undefined:submitted},'PUT');
  await onConnected();
 }
 return <section className="connection-setup-card" aria-label="Secure connection setup" tabIndex={-1}>
  <div className="connection-setup-title"><span className="connection-setup-icon"><Cable size={19}/></span><div><strong>{target==='google'?'Connect Google':'Connect a service'}</strong><p>{target==='google'?'Choose your account and approve access on Google.':'Add a service for Thaddeus to use with your permission.'}</p></div><button type="button" aria-label="Close connection setup" onClick={onClose}><X size={16}/></button></div>
  {target==='google'?<form className="google-connect-form" aria-label="Connect Google Workspace" onSubmit={event=>{event.preventDefault();void run(connectGoogle);}}>
   <fieldset className="google-permission-list" aria-label="Google Workspace permissions"><legend>Choose permissions</legend>{view?.google.products.map(item=><label key={item.id} className={item.connected?'connected':''}><input type="checkbox" checked={products.includes(item.id)||item.connected} disabled={busy||item.connected} onChange={event=>setProducts(current=>event.target.checked?[...current,item.id]:current.filter(id=>id!==item.id))}/><span><strong>{item.name}</strong><small>{item.connected?'Connected':item.access}</small></span></label>)}</fieldset>
   <p className="muted connection-permission-hint">Select one or several. Thaddeus asks Google for this exact set in one consent flow; reading and sending remain separate capabilities.</p>
   <button className="primary" disabled={!online||busy||products.length===0||!view?.google.clientSetup.configured}>{busy?(signIn?'Waiting for Google…':'Preparing…'):'Continue with Google'}</button>
   {signIn?<p role="status">{signIn.browserOpened?'Google sign-in opened in your default browser.':'Your browser could not be opened automatically.'} Choose your account and approve access, then return here. <a href={signIn.authorizationUrl} target="_blank" rel="noreferrer">Open Google sign-in <ExternalLink size={13}/></a></p>
    :view?.google.clientSetup.configured?null
    :view&&<p role="status" className="connection-setup-needed">{view.google.clientSetup.status==='unavailable'?'Your saved Google app setup is unavailable. Unlock the system credential store and reopen this card.':'Google needs a one-time app setup before anyone can sign in. Once configured, this button handles future connections.'}</p>}
   {view&&<details ref={appSetup} className="google-app-setup"><summary>App setup · one time</summary>
    <p>For the person setting up Thaddeus: register a <strong>Desktop app</strong> in Google Cloud, enable the needed APIs, and add your account as a test user while the app is in testing. Download its credentials JSON and select it below. Thaddeus fills in the app details and remembers them securely for Gmail and Calendar.</p>
    <p>Keep this file out of chat and regular attachments. This import goes directly to the host credential store.</p>
    {view.google.clientSetup.canRemove?<button type="button" disabled={!online||busy} onClick={()=>void run(removeGoogleSetup)}>Remove saved app setup</button>
     :<label>Import Google setup file<input type="file" aria-label="Import Google setup file" accept=".json,application/json" disabled={!online||busy} onChange={event=>{const file=event.target.files?.[0];event.target.value='';if(file)void run(()=>importGoogle(file));}}/></label>}
    <a href="https://developers.google.com/identity/protocols/oauth2/native-app" target="_blank" rel="noreferrer">Google desktop OAuth guide <ExternalLink size={13}/></a>
   </details>}
  </form>:<form aria-label="Add MCP connector" onSubmit={event=>{event.preventDefault();void run(connectMcp);}}>
   <label>Service name<input aria-label="Connector name" value={name} maxLength={80} disabled={busy} onChange={event=>setName(event.target.value)} placeholder="GitHub, Notion, home server…"/></label>
   <label>Streamable HTTP endpoint<input aria-label="MCP endpoint" type="url" value={endpoint} disabled={busy} onChange={event=>setEndpoint(event.target.value)} placeholder="https://mcp.example.com/mcp"/></label>
   <label>Authentication<select aria-label="MCP authentication" value={storage} disabled={busy} onChange={event=>{setStorage(event.target.value);setToken('');}}><option value="system">Bearer token in {view?.systemStore||'system vault'}</option><option value="session">Bearer token until host stops</option><option value="none">No credential</option></select></label>
   {storage!=='none'&&<label>Bearer token<input aria-label="MCP bearer token" type="password" autoComplete="new-password" spellCheck={false} maxLength={2048} value={token} disabled={busy} onChange={event=>setToken(event.target.value)}/></label>}
   <button className="primary" disabled={!online||busy||!view||!name.trim()||!endpoint.trim()||(storage!=='none'&&!token.trim())}>{busy?'Checking server…':'Verify & connect'}</button>
  </form>}
  <div className="connection-setup-boundary" title={'Stored in '+(view?.systemStore||'the system credential vault')+'. Credentials never become chat messages or model context.'}><ShieldCheck size={14}/><p>Credentials stay on this computer, outside our chat.</p></div>
  {error&&<p role="alert" className="connection-error">{error}</p>}
  {notice&&<p role="status">{notice}</p>}
 </section>;
}
