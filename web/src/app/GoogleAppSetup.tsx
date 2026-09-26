import {useCallback,useEffect,useState} from 'react';
import {CheckCircle2,ExternalLink,Upload} from 'lucide-react';
import {api} from '../api';

type McpView={version:string;google:{clientSetup:{configured:boolean;status:string;canRemove:boolean}}};

/** Settings → Google app: the one-time Desktop app setup that Analytics, Search Console and Gmail drafts sign in with. */
export function GoogleAppSetup(){
  const [view,setView]=useState<McpView|null>(null),[busy,setBusy]=useState(false),[notice,setNotice]=useState(''),[error,setError]=useState('');
  const load=useCallback(async()=>{try{setView(await api<McpView>('/settings/mcp'));setError('');}catch(cause){setError((cause as Error).message);}},[]);
  useEffect(()=>{void load();},[load]);
  async function upload(file:File){
    if(!view||busy)return;
    if(!file.size||file.size>16_384){setError('Choose the Google Desktop app credentials JSON you downloaded (up to 16 KB).');return;}
    setBusy(true);setError('');setNotice('');
    try{setView(await api<McpView>('/settings/mcp/google/client',{version:view.version,credentialsJson:await file.text()},'PUT'));setNotice('Saved to the system credential store. Google sign-ins can start now.');}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  async function remove(){
    if(!view||busy||!window.confirm('Remove the saved Google app setup? Existing connections keep working until they need to sign in again.'))return;
    setBusy(true);setError('');
    try{setView(await api<McpView>('/settings/mcp/google/client/remove',{version:view.version}));setNotice('Removed.');}catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  const setup=view?.google.clientSetup;
  return <div className="fe-google-app" aria-label="Google app setup">
    {setup?.configured?<p className="fe-notice"><CheckCircle2 size={15}/> Google app saved. Analytics, Search Console and Gmail drafts can sign in.</p>
      :<p className="fe-muted">One time, for Google Analytics, Search Console and Gmail drafts. Nothing Google works until this is saved.{setup?.status==='unavailable'?' The saved setup can’t be read: unlock the system credential store, then reload.':''}</p>}
    <ol className="fe-setup-steps">
      <li>In <a href="https://console.cloud.google.com/" target="_blank" rel="noopener noreferrer">Google Cloud <ExternalLink size={11}/></a>, create a project and an OAuth client of type <strong>Desktop app</strong>.</li>
      <li>Enable the <strong>Gmail API</strong>, <strong>Google Analytics Data API</strong>, <strong>Google Analytics Admin API</strong> and <strong>Google Search Console API</strong>.</li>
      <li>On the consent screen, while the app is in testing, add yourself and the marketing mailbox as <strong>test users</strong>. Nothing else to register: Desktop apps may return to this computer.</li>
      <li>Download the client’s credentials JSON and import it here. It goes straight to the system credential store, never into the workspace or chat.</li>
    </ol>
    <div className="fe-google-app-actions">
      {setup?.canRemove?<button type="button" disabled={busy} onClick={()=>void remove()}>Remove saved setup</button>
        :<label className="fe-button fe-upload"><Upload size={14}/> {busy?'Saving…':'Import credentials JSON'}<input type="file" accept=".json,application/json" aria-label="Import Google credentials JSON" disabled={busy||!view}
          onChange={event=>{const file=event.target.files?.[0];event.target.value='';if(file)void upload(file);}}/></label>}
    </div>
    {notice&&<p className="fe-notice" role="status">{notice}</p>}
    {error&&<p className="fe-alert" role="alert">{error.includes('403')||error.includes('Request failed (403)')?'Google setup can only be changed from the computer running the workspace.':error}</p>}
  </div>;
}
