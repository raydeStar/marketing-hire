import {useCallback,useEffect,useState} from 'react';
import {createRoot} from 'react-dom/client';
import {ArrowRight} from 'lucide-react';
import './style.css';
import './raven.css';
import './business-theme.css';
import {api,setCsrf,restoreSession} from './api';
import {Raven} from './components/Raven';
import {CustomerSignIn,type CustomerLoginView} from './components/CustomerSignIn';
import {AcceptCampaignInvitation,pendingInvitation} from './components/CampaignInvitations';
import {MaintenancePage,type MaintenanceView} from './components/Maintenance';
import {Workspace} from './app/Workspace';
// The new design system loads last so it wins over the retained component styles.
import './app/app.css';
import './app/views.css';
import './app/shell.css';

type Session={id:string;owner:boolean;name?:string;accountId?:string;principalId?:string;csrf?:string};

// Apply the saved theme before the first paint of the sign-in screen.
try{const saved=localStorage.getItem('thaddeus-theme');const dark=saved==='dark'||(saved!=='light'&&matchMedia('(prefers-color-scheme: dark)').matches);document.documentElement.dataset.theme=dark?'dark':'light';}catch{}

function Unlock({remote,onSession}:{remote:boolean;onSession:(session:Session)=>void}){
  const [pair,setPair]=useState(remote),[key,setKey]=useState(''),[notice,setNotice]=useState(''),[error,setError]=useState(''),[busy,setBusy]=useState(false);
  async function submit(event:React.FormEvent){
    event.preventDefault();setError('');setNotice('');setBusy(true);
    try{
      const session=await api<Session>(pair?'/pair/claim':'/auth/login',pair?{code:key,name:'Paired browser'}:{key});
      if(pair){setNotice('Request sent. Ask the owner to confirm this browser, then select Finish pairing.');setKey('');}
      else{setCsrf(session.csrf||'');setKey('');onSession(session);}
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  async function finish(){
    setError('');
    try{const session=await api<Session|null>('/pair/exchange',{});if(session){setCsrf(session.csrf||'');onSession(session);}else setNotice('The owner hasn’t confirmed this browser yet.');}
    catch(cause){setError((cause as Error).message);}
  }
  return <main className="unlock fe-auth">
    <div className="fe-auth-card">
      <div className="fe-auth-brand"><span className="fe-brand-mark" aria-hidden="true">H0</span>HireZero</div>
      <Raven state="listening"/>
      <h1>{pair?'Join this workspace':'Welcome back'}</h1>
      <p>{pair?'Enter the one-time code from the owner’s Team → People → Invite.':'Your marketing employee is waiting. Unlock this browser with your host access key.'}</p>
      <form onSubmit={event=>void submit(event)}>
        <label>{pair?'One-time pairing code':'Host access key'}<input type="password" autoComplete="off" value={key} onChange={event=>setKey(event.target.value)} required/></label>
        <button className="primary" disabled={busy}>{pair?'Request pairing':'Open workspace'} <ArrowRight size={17}/></button>
      </form>
      {pair&&<button type="button" onClick={()=>void finish()}>Finish pairing</button>}
      {!remote&&<button type="button" className="text-button" onClick={()=>{setPair(!pair);setKey('');setError('');setNotice('');}}>{pair?'Use the host access key instead':'Join with a pairing code'}</button>}
      <small>{remote?'The owner’s key never leaves their computer.':<>Your key is in <code>.data/host-key.txt</code>. Everything stays on this computer.</>}</small>
      {notice&&<p role="status">{notice}</p>}
      {error&&<p role="alert" className="error">{error}</p>}
    </div>
  </main>;
}

function App(){
  const remote=location.protocol==='https:'&&!['localhost','127.0.0.1','[::1]'].includes(location.hostname);
  const [invitation,setInvitation]=useState(pendingInvitation);
  const [customerLogin,setCustomerLogin]=useState<CustomerLoginView|null>(null),[legacyAccess,setLegacyAccess]=useState(false);
  const [session,setSession]=useState<Session|null>(null),[loaded,setLoaded]=useState(false),[online,setOnline]=useState(true),[error,setError]=useState('');
  useEffect(()=>{void api<CustomerLoginView>('/auth/customer').then(setCustomerLogin).catch(()=>{});},[]);
  const accept=useCallback((next:Session|null)=>{if(next){setCsrf(next.csrf||'');setSession(next);setError('');}},[]);
  useEffect(()=>{
    let stale=false;
    const restore=()=>restoreSession().then(next=>{if(!stale)accept(next);}).catch(cause=>{if(!stale)setError((cause as Error).message);}).finally(()=>{if(!stale)setLoaded(true);});
    const launch=()=>{if(new URLSearchParams(location.hash.slice(1)).has('launch'))void restore();};
    void restore();addEventListener('hashchange',launch);
    return()=>{stale=true;removeEventListener('hashchange',launch);};
  },[accept]);
  useEffect(()=>{
    if(!session?.owner)return;
    // The host's event stream is the heartbeat for "online".
    const events=new EventSource('/api/events');
    const offline=()=>setOnline(false);const reconnect=()=>void api('/session').then(()=>setOnline(true)).catch(()=>setOnline(false));
    events.onopen=()=>setOnline(true);events.onerror=offline;
    addEventListener('offline',offline);addEventListener('online',reconnect);
    return()=>{events.close();removeEventListener('offline',offline);removeEventListener('online',reconnect);};
  },[session]);
  if(!loaded)return <main className="unlock fe-auth"><div className="fe-auth-card"><Raven/><h1>Opening your workspace…</h1></div></main>;
  if(session&&invitation)return <AcceptCampaignInvitation token={invitation} customerAccount={!!session.accountId} login={customerLogin} onDone={()=>setInvitation('')}/>;
  if(!session&&customerLogin?.enabled&&!legacyAccess)return <CustomerSignIn login={customerLogin} onRecovery={()=>setLegacyAccess(true)}/>;
  if(!session)return <>{error&&<p className="fe-auth-error" role="alert">{error}</p>}<Unlock remote={remote} onSession={accept}/></>;
  return <Workspace key={session.id} hostOnline={online} signedInName={session.name||(session.owner?'Owner':'Signed-in device')} signedInId={session.principalId||session.id}
    onSignOut={session.accountId?async()=>{await api('/auth/logout',{});location.reload();}:undefined}/>;
}

function Root(){
  const [maintenance,setMaintenance]=useState(location.pathname==='/maintenance'),[initial,setInitial]=useState<MaintenanceView>();
  const reopened=useCallback(()=>{history.replaceState(null,'','/');setMaintenance(false);setInitial(undefined);},[]);
  return maintenance?<MaintenancePage initial={initial} onReopened={reopened}/>:<App/>;
}
createRoot(document.getElementById('root')!).render(<Root/>);
if('serviceWorker' in navigator)navigator.serviceWorker.register('/sw.js').catch(()=>{});
