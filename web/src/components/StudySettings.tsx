import {useEffect,useState} from 'react';
import {Cable,HardDrive,ShieldCheck,MonitorCog,ChevronRight} from 'lucide-react';
import {api} from '../api';
import type {State} from '../types';
import {ModelConnectionSettings} from './ModelConnectionSettings';
import {SearchConnectionSettings} from './SearchConnectionSettings';
import {McpConnectionSettings} from './McpConnectionSettings';
import {HostWorkerSettings} from './HostWorkerSettings';
import {SandboxSettings} from './SandboxSettings';
import {WorkspaceSettings} from './WorkspaceSettings';
import {MaintenanceSettings,type MaintenanceView} from './Maintenance';

type Section='connections'|'worker'|'access'|'storage';
type Devices={devices:{id:string;name:string;owner:boolean;expires:string}[];pending:{id:string;name:string}[]};
type Props={data:State|null;owner:boolean;online:boolean;onChanged:()=>Promise<unknown>;onConnectionSetup:(target:'google'|'mcp')=>void;onMaintenance:(view:MaintenanceView)=>void;onDataDeleted:()=>void;unsavedNote:string|null;onReturnToNote:()=>void};

export function StudySettings({data,owner,online,onChanged,onConnectionSetup,onMaintenance,onDataDeleted,unsavedNote,onReturnToNote}:Props){
  const [section,setSection]=useState<Section>('connections');
  const [devices,setDevices]=useState<Devices>({devices:[],pending:[]});
  const [pairCode,setPairCode]=useState(''),[deleteText,setDeleteText]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  useEffect(()=>{
    if(!owner)return;
    let stale=false;
    api<Devices>('/devices').then(result=>{if(!stale)setDevices(result);}).catch(error=>{if(!stale)setError(error.message);});
    return()=>{stale=true;};
  },[owner,data]);
  async function act(work:()=>Promise<unknown>){
    setBusy(true);setError('');
    try{await work();await onChanged();}catch(error){setError((error as Error).message);}finally{setBusy(false);}
  }
  function choose(next:Section){setSection(next);}
  const retained=data?.runs.filter(run=>run.research?.workerRetained).length||0;
  const sections=[
    {id:'connections' as const,label:'Connections',description:'Model, search & MCP tools',icon:Cable},
    {id:'worker' as const,label:'Research worker',description:data?.research?.enabled?'Research enabled':'Setup & diagnostics',icon:MonitorCog},
    {id:'access' as const,label:'Permissions & devices',description:'Approvals & browser access',icon:ShieldCheck},
    {id:'storage' as const,label:'Storage & backups',description:retained?`${retained} saved workspace${retained===1?'':'s'}`:'Notes, backups & export',icon:HardDrive}
  ];
  return <section className="settings study-settings" aria-label="Study settings">
    <p className="eyebrow">YOUR STUDY, YOUR CHOICES</p><h1>Settings</h1>
    {!owner?<><p>Provider, device, and data settings are managed on the host.</p><InstallGuide/></>:<>
      <p className="settings-intro">Choose what to adjust. Your model, research computer, access, and saved work each have their own place.</p>
      <nav className="settings-navigation" aria-label="Settings sections">
        {sections.map(({id,label,description,icon:Icon})=><button key={id} type="button" aria-label={label}
          aria-current={section===id?'page':undefined} aria-controls={'settings-'+id} onClick={()=>choose(id)}>
          <Icon size={19}/><span><strong>{label}</strong><small>{description}</small></span><ChevronRight size={15}/>
        </button>)}
      </nav>
      {error&&<p role="alert" className="error">{error}</p>}
      {/* Keep forms mounted: the butler does not discard a half-written note when you change drawers. */}
      <div id="settings-connections" className="settings-panel" role="region" aria-label="Connection settings" hidden={section!=='connections'}>
        <ModelConnectionSettings online={online} onChanged={onChanged}/>
        <SearchConnectionSettings online={online} onChanged={onChanged}/>
        <McpConnectionSettings online={online} onChanged={onChanged} onSetup={onConnectionSetup}/>
      </div>
      <div id="settings-worker" className="settings-panel" role="region" aria-label="Research worker settings" hidden={section!=='worker'}>
        <HostWorkerSettings online={online} provider={data?.provider} onChanged={onChanged} onConnectModel={()=>{choose('connections');requestAnimationFrame(()=>document.getElementById('model-connection-heading')?.focus());}}/>
        <details className="settings-secondary"><summary>Docker diagnostics</summary><SandboxSettings online={online}/></details>
      </div>
      <div id="settings-access" className="settings-panel" role="region" aria-label="Permissions and devices settings" hidden={section!=='access'}>
        <section><h2>Agent permissions</h2>
          <label>Knowledge writes<select aria-label="Knowledge writes" disabled={!online||busy} value={data?.writes||'ask'} onChange={e=>act(()=>api('/settings/permissions',{writes:e.target.value},'PUT'))}>
            <option value="ask">Ask · exact single-action approval</option><option value="off">Off</option>
          </select></label>
          <p>You choose what a task can read when it starts. Every change to your notes needs your approval.</p>
        </section>
        <section aria-label="Your devices"><h2>Your devices</h2>
          <p>{data?.phoneOrigin?'Phone address: '+data.phoneOrigin:'Phone access is not set up yet.'} Your phone connects to this computer, which must remain awake.</p>
          <button disabled={busy||!online||!data?.phoneOrigin} onClick={()=>act(async()=>setPairCode((await api<{code:string}>('/pair/start',{})).code))}>Create one-time pairing code</button>
          {!data?.phoneOrigin&&<p className="muted">A trusted HTTPS address is needed before pairing. Physical-phone setup remains a separate, user-operated step.</p>}
          {pairCode&&<p className="pair-code" role="status">{pairCode} · expires in 5 minutes</p>}
          {devices.pending.map(device=><div className="device" key={device.id}><span>{device.name} requests access</span><button disabled={busy||!online} onClick={()=>act(()=>api('/pair/'+device.id+'/confirm',{}))}>Confirm this device</button></div>)}
          {devices.devices.filter(device=>!device.owner).map(device=><div className="device" key={device.id}><span>{device.name}<small>Expires {new Date(device.expires).toLocaleDateString()}</small></span><button disabled={busy||!online} onClick={()=>act(()=>api('/devices/'+device.id+'/revoke',{}))}>Revoke</button></div>)}
          {!devices.devices.some(device=>!device.owner)&&<p className="muted">No paired browsers.</p>}
          <details className="settings-secondary"><summary>Manage owner browser sessions ({devices.devices.filter(device=>device.owner).length})</summary>
            <p>Signing in on this computer creates an owner session. Revoking the session used by this browser will sign it out.</p>
            {devices.devices.filter(device=>device.owner).map(device=><div className="device" key={device.id}><span>{device.name}<small>Expires {new Date(device.expires).toLocaleDateString()} · {device.id.slice(0,8)}</small></span><button disabled={busy||!online} aria-label={'Revoke owner session '+device.id.slice(0,8)} onClick={()=>act(()=>api('/devices/'+device.id+'/revoke',{}))}>Revoke</button></div>)}
          </details>
        </section>
        <InstallGuide/>
      </div>
      <div id="settings-storage" className="settings-panel" role="region" aria-label="Storage and backup settings" hidden={section!=='storage'}>
        <MaintenanceSettings online={online} onStarted={onMaintenance} unsavedNote={unsavedNote} onReturnToNote={onReturnToNote}/>
        <WorkspaceSettings runs={data?.runs||[]} online={online} onChanged={onChanged}/>
        <section><h2>Export your study</h2><p>Download your notes and receipts to keep a copy outside Thaddeus.</p><a className="button" href="/api/export" download>Export notes & receipts</a></section>
        <details className="settings-secondary settings-danger"><summary>Delete study data</summary>
          <p>Delete notes, runs, chats, collections, and revisions from this study. This cannot be undone here.</p>
          <label>Delete all notes, runs, chats, collections & revisions<input placeholder="Type DELETE MY DATA" value={deleteText} onChange={e=>setDeleteText(e.target.value)}/></label>
          <button disabled={deleteText!=='DELETE MY DATA'||!online||busy} onClick={()=>act(async()=>{await api('/data/delete',{confirmation:deleteText});setDeleteText('');onDataDeleted();})}>Delete my data</button>
          <p>{data?.retainedResearchWorkspaces&&'Remove retained research workspaces above before deleting task data. '}Storage is local and not application-encrypted. Deletion is not a secure disk erase. Sessions, provider settings, and saved credentials remain.</p>
          <button className="text-button" onClick={()=>{choose('connections');document.querySelector<HTMLButtonElement>('[aria-controls="settings-connections"]')?.focus();}}>Manage saved keys in Connections <ChevronRight size={14}/></button>
        </details>
      </div>
    </>}
  </section>;
}

function InstallGuide(){
  return <section><h2>Take the study with you</h2><p>On a supported browser, use “Install app” or “Add to Home Screen.” Trusted HTTPS is required for phone installation. Only the static shell is cached; private API data is not. Offline writes are never queued.</p></section>;
}
