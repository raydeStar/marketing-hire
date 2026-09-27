import {useCallback,useEffect,useState} from 'react';
import {BarChart3,CheckCircle2,CircleAlert,Handshake,KeyRound,Mail,Megaphone,Newspaper,Search,Send,ShieldCheck,UserPlus} from 'lucide-react';
import {api} from '../api';
import {ConnectData,type DataConnectionsData,type DataKind} from './DataConnectionsView';
import {ConnectChannel,usePublishing,type ChannelKind} from './PublishingView';

type VaultKey={service:string;account:string;kind:string;status:string;connectedAt:string};
type Vault={health:{store:string;working:boolean;detail:string;checkedAt:string};keys:VaultKey[]};
type Service={kind:string;name:string;what:string;how:string;icon:typeof Search;group:string;data?:DataKind;channel?:ChannelKind;google?:boolean};

/** Everything the employee can connect to, in the order a small business usually needs them. */
const services:Service[]=[
  {group:'See what’s working',kind:'hirezero-signups',name:'HireZero sign-ups',what:'Beta sign-ups and new accounts per day, for the north star',how:'Site key',icon:UserPlus,data:'hirezero-signups'},
  {group:'See what’s working',kind:'google-analytics',name:'Google Analytics',what:'Visits, sign-ups and where they came from',how:'Sign in with Google',icon:BarChart3,data:'google-analytics',google:true},
  {group:'See what’s working',kind:'search-console',name:'Search Console',what:'Searches that find you, and pages close to page one',how:'Sign in with Google',icon:Search,data:'search-console',google:true},
  {group:'See what’s working',kind:'plausible',name:'Plausible',what:'Privacy-friendly site analytics',how:'API key',icon:BarChart3,data:'plausible'},
  {group:'Leads and spend',kind:'hubspot',name:'HubSpot',what:'New contacts, deals and pipeline, and where leads came from',how:'Read-only token',icon:Handshake,data:'hubspot'},
  {group:'Leads and spend',kind:'meta-ads',name:'Meta Ads',what:'Spend, clicks and leads per campaign',how:'Read-only token',icon:Megaphone,data:'meta-ads'},
  {group:'Where drafts can go',kind:'email',name:'Gmail',what:'Emails saved as drafts in your Gmail; you send them',how:'Sign in with Google',icon:Mail,channel:'email',google:true},
  {group:'Where drafts can go',kind:'linkedin',name:'LinkedIn',what:'Post approved drafts to your profile',how:'Sign in',icon:Send,channel:'linkedin'},
  {group:'Where drafts can go',kind:'x',name:'X',what:'Post approved drafts to your account',how:'Sign in',icon:Send,channel:'x'},
  {group:'Where drafts can go',kind:'bluesky',name:'Bluesky',what:'Post approved drafts to your account',how:'App password',icon:Send,channel:'bluesky'},
  {group:'Where drafts can go',kind:'mastodon',name:'Mastodon',what:'Post approved drafts to your account',how:'Access token',icon:Send,channel:'mastodon'},
  {group:'Where drafts can go',kind:'wordpress',name:'WordPress',what:'Blog posts saved as drafts on your site',how:'Application password',icon:Newspaper,channel:'wordpress'},
  {group:'Where drafts can go',kind:'hirezero',name:'HireZero site',what:'Blog posts and page copy as drafts in the site’s CMS',how:'Site token',icon:Newspaper,channel:'hirezero'},
  {group:'Where drafts can go',kind:'buttondown',name:'Buttondown',what:'Newsletter issues saved as drafts',how:'API key',icon:Mail,channel:'buttondown'}
];

/** Settings → Connections: where keys are kept, whether that works, and one Connect button per service. */
export function ConnectionsSettings(){
  const [vault,setVault]=useState<Vault|null>(null),[data,setData]=useState<DataConnectionsData|null>(null),[error,setError]=useState(''),[checking,setChecking]=useState(false);
  const publishing=usePublishing();
  const [open,setOpen]=useState<Service|null>(null);
  const load=useCallback(async(check=false)=>{
    try{const [nextVault,nextData]=await Promise.all([api<Vault>('/vault'+(check?'?check':'')),api<DataConnectionsData>('/data-connections')]);setVault(nextVault);setData(nextData);setError('');}
    catch(cause){setError((cause as Error).message);}
  },[]);
  useEffect(()=>{void load();},[load]);
  const connected=(service:Service)=>vault?.keys.find(key=>key.kind===service.kind&&key.status!=='authorizing');
  const googleReady=!!data?.googleReady;
  const groups=[...new Set(services.map(service=>service.group))];
  function connect(service:Service){
    // Google's services need the one-time Google app first; that's the button's first step.
    if(service.google&&!googleReady){document.querySelector('section[aria-label="Google app"]')?.scrollIntoView({behavior:'smooth',block:'start'});return;}
    setOpen(service);
  }
  return <div className="fe-connections">
    <div className={'fe-vault '+(vault?.health.working===false?'bad':'')} aria-label="Key vault">
      <ShieldCheck size={20}/>
      <div><strong>{vault?`Keys are kept in ${vault.health.store}`:'Checking the key vault…'}</strong>
        <small>{vault?.health.detail||'Tokens and passwords are stored by your operating system, on this computer.'} They never go into the workspace, its backup, chat or the model; the employee uses them only to read or to save drafts.</small>
        {vault&&<small className="fe-muted">{vault.keys.length} key{vault.keys.length===1?'':'s'} stored{vault.health.working?' · working':''} · checked {new Date(vault.health.checkedAt).toLocaleTimeString([], {hour:'numeric',minute:'2-digit'})}</small>}</div>
      <button type="button" className="fe-ghost" disabled={checking} onClick={()=>{setChecking(true);void load(true).finally(()=>setChecking(false));}}>{checking?'Checking…':'Check now'}</button>
    </div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {groups.map(group=><section key={group} className="fe-connection-group" aria-label={group}><h3>{group}</h3>
      {services.filter(service=>service.group===group).map(service=>{const key=connected(service);const Icon=service.icon;return <div key={service.kind} className="fe-data-row">
        <span className="fe-row-icon"><Icon size={16}/></span>
        <span className="fe-list-main"><strong>{service.name}</strong><small>{key?`Connected${key.account?' · '+key.account:''}${key.status==='error'?' · needs attention':''}`:service.what}</small></span>
        {key?<span className={'fe-status-chip '+(key.status==='error'?'warn':'live')}>{key.status==='error'?<><CircleAlert size={12}/> Check</>:<><CheckCircle2 size={12}/> Connected</>}</span>
          :<button type="button" onClick={()=>connect(service)} title={service.google&&!googleReady?'Set up the Google app first (one time)':service.how}>{service.google&&!googleReady?'Set up Google first':'Connect'}</button>}
      </div>;})}
    </section>)}
    <p className="fe-muted"><KeyRound size={13}/> Disconnect a service where it’s used: Work → Scorecard for data, Settings → Publishing channels for channels. Disconnecting deletes its key from the store.</p>
    {open?.data&&data&&<ConnectData data={data} initial={open.data} onClose={()=>setOpen(null)} onChanged={async()=>{await load();}}/>}
    {open?.channel&&publishing.data&&<ConnectChannel data={publishing.data} initial={open.channel} onClose={()=>{setOpen(null);void load();}} onChanged={async()=>{await publishing.load();await load();}}/>}
  </div>;
}
