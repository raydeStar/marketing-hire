import {useEffect,useState} from 'react';
import {Cable,ExternalLink,RefreshCw,ShieldCheck,Trash2} from 'lucide-react';
import {api} from '../api';

type McpTool={remoteName:string;modelName:string;description:string;effect:string};
type McpConnector={id:string;name:string;endpoint:string;storage:string;status:string;authentication:string;created:string;updated:string;enabled:boolean;tools:McpTool[];needsReentry:boolean;inUse:boolean};
type McpView={version:string;systemStore:string;google:{redirectUri:string;products:{id:string;name:string;access:string}[]};connectors:McpConnector[]};
type GoogleStart={attemptId:string;authorizationUrl:string;redirectUri:string};
type GoogleStatus={phase:string;error?:string;connectorId?:string};

export function McpConnectionSettings({online,onChanged}:{online:boolean;onChanged:()=>Promise<unknown>}){
 const [view,setView]=useState<McpView|null>(null),[name,setName]=useState(''),[endpoint,setEndpoint]=useState(''),[storage,setStorage]=useState('system'),[token,setToken]=useState('');
 const [googleProduct,setGoogleProduct]=useState('calendar'),[googleClientId,setGoogleClientId]=useState(''),[googleClientSecret,setGoogleClientSecret]=useState('');
 const [busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState('');
 async function load(){const next=await api<McpView>('/settings/mcp');setView(next);return next;}
 useEffect(()=>{void load().catch(error=>setError(error.message));},[]);
 async function perform(work:()=>Promise<void>){setBusy(true);setError('');setMessage('');try{await work();await onChanged();}catch(error){setError((error as Error).message);await load().catch(()=>{});}finally{setBusy(false);}}
 async function connect(){
  if(!view)return;
  const submitted=token;setToken('');
  const next=await api<McpView>('/settings/mcp',{version:view.version,name:name.trim(),endpoint:endpoint.trim(),storage,token:storage==='none'?undefined:submitted},'PUT');
  setView(next);setName('');setEndpoint('');setMessage('Connector verified and saved. No tool action was run.');
 }
 async function connectGoogle(){
  if(!view)return;
  const popup=window.open('about:blank','thaddeus-google-connect','popup,width=560,height=760');
  const secret=googleClientSecret;setGoogleClientSecret('');
  try{
   const started=await api<GoogleStart>('/settings/mcp/google/start',{version:view.version,product:googleProduct,clientId:googleClientId.trim(),clientSecret:secret});
   if(popup)popup.location.href=started.authorizationUrl;else window.open(started.authorizationUrl,'_blank','noopener,noreferrer');
   setMessage('Finish Google sign-in in the new window. Thaddeus is waiting for the scoped consent result.');
   for(let count=0;count<300;count++){
    await new Promise(resolve=>setTimeout(resolve,2000));
    const status=await api<GoogleStatus>('/settings/mcp/google/status/'+started.attemptId);
    if(status.phase==='connected'){
     popup?.close();await load();setGoogleClientId('');setMessage('Google Workspace connected. No mail or calendar action was run.');return;
    }
    if(status.phase==='failed')throw new Error(status.error||'Google sign-in failed.');
   }
   throw new Error('Google sign-in expired. Start it again when you are ready.');
  }catch(error){popup?.close();throw error;}
 }
 return <section className="model-connection" aria-label="MCP connections">
  <div className="connection-heading"><div><h2>Connect tools with MCP</h2><p>Add a remote Streamable HTTP MCP server. Thaddeus can discover its tools, then propose them in chat.</p></div><Cable size={23}/></div>
  <div className="connection-boundary"><ShieldCheck size={18}/><p>Passwords and bearer tokens stay with this host. The model sees bounded tool names and schemas, never credentials. Every tool call—including reads—waits for your exact review before Thaddeus contacts the server.</p></div>
  {!view?<button disabled={!online||busy} onClick={()=>perform(async()=>{await load();})}>Load MCP connections</button>:<>
   <section className="google-mcp" aria-labelledby="google-mcp-heading">
    <div><h3 id="google-mcp-heading">Google Workspace</h3><p>Use Google&apos;s official remote MCP servers. Thaddeus opens Google sign-in, stores the resulting credential in {view.systemStore}, and still pauses every tool call for your exact review.</p></div>
    <form aria-label="Connect Google Workspace" onSubmit={event=>{event.preventDefault();void perform(connectGoogle);}}>
     <div className="connection-fields"><label>Service<select aria-label="Google Workspace service" value={googleProduct} disabled={busy} onChange={event=>setGoogleProduct(event.target.value)}>{view.google.products.map(product=><option key={product.id} value={product.id}>{product.name} · {product.access}</option>)}</select></label>
      <label>OAuth client ID<input aria-label="Google OAuth client ID" value={googleClientId} maxLength={512} disabled={busy} onChange={event=>setGoogleClientId(event.target.value)} autoComplete="off" spellCheck={false}/></label></div>
     <label>OAuth client secret<input aria-label="Google OAuth client secret" type="password" value={googleClientSecret} maxLength={512} disabled={busy} onChange={event=>setGoogleClientSecret(event.target.value)} autoComplete="new-password" spellCheck={false}/><small>Created in your Google Cloud project. It is sent only to Google&apos;s OAuth service and saved through {view.systemStore} after sign-in succeeds.</small></label>
     <details><summary>One-time Google Cloud setup</summary><ol><li>Enable the API and MCP service for the service above.</li><li>Create an OAuth consent screen and a Web application OAuth client.</li><li>Add this exact authorized redirect URI: <code>{view.google.redirectUri}</code></li></ol><a href="https://developers.google.com/workspace/guides/configure-mcp-servers" target="_blank" rel="noreferrer">Open Google&apos;s setup guide <ExternalLink size={13}/></a></details>
     <button className="primary" type="submit" disabled={!online||busy||!googleClientId.trim()||!googleClientSecret.trim()}>{busy?'Waiting for Google…':'Continue with Google'}</button>
    </form>
   </section>
   <details className="settings-secondary"><summary>Connect another remote MCP server</summary>
   <form aria-label="Add MCP connector" onSubmit={event=>{event.preventDefault();void perform(connect);}}>
    <div className="connection-fields"><label>Connector name<input aria-label="Connector name" value={name} maxLength={80} disabled={busy} onChange={event=>setName(event.target.value)} placeholder="Calendar, GitHub, home server…"/></label>
     <label>Authentication<select aria-label="MCP authentication" value={storage} disabled={busy} onChange={event=>{setStorage(event.target.value);setToken('');}}><option value="system">Bearer token in {view.systemStore}</option><option value="session">Bearer token until host stops</option><option value="none">No credential</option></select></label></div>
    <label>Streamable HTTP endpoint<input aria-label="MCP endpoint" type="url" value={endpoint} disabled={busy} onChange={event=>setEndpoint(event.target.value)} placeholder="https://mcp.example.com/mcp"/><small>Remote servers require HTTPS. Loopback development servers may use HTTP. Credentials, query strings, and redirects are refused.</small></label>
    {storage!=='none'&&<label>Bearer token<input aria-label="MCP bearer token" type="password" autoComplete="new-password" spellCheck={false} maxLength={2048} value={token} disabled={busy} onChange={event=>setToken(event.target.value)} placeholder="Paste the token supplied by this MCP server"/><small>{storage==='system'?`Saved through ${view.systemStore}; omitted from chat, logs, and exports.`:'Held only in host memory. Reconnect after the host restarts.'}</small></label>}
    <button className="primary" type="submit" disabled={!online||busy||!name.trim()||!endpoint.trim()||(storage!=='none'&&!token.trim())}>{busy?'Checking server…':'Verify & connect'}</button>
   </form>
   </details>
   {view.connectors.length>0?<div className="mcp-connectors"><h3>Connected capabilities</h3>{view.connectors.map(connector=><article className="mcp-connector" key={connector.id}>
    <div className="mcp-connector-heading"><div><strong>{connector.name}</strong><p>{connector.endpoint}</p><small>{connector.storage==='oauth'?'Google OAuth in '+view.systemStore:connector.storage==='system'?view.systemStore:connector.storage==='session'?'Until host stops':'No credential'} · {connector.tools.length} tool{connector.tools.length===1?'':'s'}{connector.status!=='ready'?' · setup needs attention':''}{connector.needsReentry?' · token needed after restart':''}{connector.inUse?' · frozen into active work':''}</small></div>
     <div className="connection-actions"><button type="button" disabled={!online||busy||connector.needsReentry||connector.inUse} onClick={()=>perform(async()=>{setView(await api<McpView>(`/settings/mcp/${connector.id}/refresh`,{version:view.version}));setMessage('Tool catalog refreshed. Future messages will use the new snapshot.');})}><RefreshCw size={14}/> Refresh tools</button><button type="button" disabled={!online||busy||connector.inUse} onClick={()=>perform(async()=>{setView(await api<McpView>(`/settings/mcp/${connector.id}/remove`,{version:view.version}));setMessage('Connector and its host-held credential were removed.');})}><Trash2 size={14}/> Remove</button></div></div>
    <details><summary>Review advertised tools ({connector.tools.length})</summary><ul className="mcp-tool-list">{connector.tools.map(tool=><li key={tool.modelName}><strong>{tool.remoteName}</strong><span>{tool.effect}</span><p>{tool.description}</p></li>)}</ul></details>
   </article>)}</div>:<p className="muted">No MCP connectors yet. Chat, artifacts, and public website reading continue to work without one.</p>}
  </>}
  {message&&<p role="status">{message}</p>}{error&&<p role="alert" className="connection-error">{error}</p>}
 </section>;
}
