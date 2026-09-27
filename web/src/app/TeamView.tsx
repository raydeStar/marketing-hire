import {useCallback,useEffect,useState} from 'react';
import {Check,ChevronRight,Copy,KeyRound,Minus,RefreshCw,ShieldCheck,UserPlus} from 'lucide-react';
import {api} from '../api';
import {CampaignInvitations} from '../components/CampaignInvitations';
import {campaignTitle} from '../components/MarketingRunwayPanel';
import type {MarketingState} from '../components/MarketingPanels';
import {AddMember,EmployeeProfile,type EmployeeTab} from './Employee';
import type {UsageSummary} from './EmployeeUsage';
import {initials,type Directory,type EmployeeStatus} from './shared';

type Device={id:string;name:string;owner:boolean;expires:string;accountId?:string|null};
type PendingDevice={id:string;name:string;confirmed:boolean;expires:string};
type Devices={devices:Device[];pending:PendingDevice[]};
export type Role='viewer'|'reviewer'|'contributor'|'manager';
export const roleChoices:{role:Role;label:string;detail:string}[]=[
  {role:'viewer',label:'Viewer',detail:'Reads the campaigns you share'},
  {role:'reviewer',label:'Reviewer',detail:'Also comments and requests changes on shared campaigns'},
  {role:'contributor',label:'Contributor',detail:'Also works on tasks, the Library and pages'},
  {role:'manager',label:'Manager',detail:'Also chats with the employee, edits its instructions and brief, and publishes pages'}
];
// What each role can do. Decisions stay with the owner so the record is always theirs.
const matrix:{label:string;min:Role|'owner'}[]=[
  {label:'Read shared campaigns',min:'viewer'},{label:'Comment and request changes',min:'reviewer'},
  {label:'Read the Library, tasks and history',min:'contributor'},{label:'Edit documents, pages and tasks',min:'contributor'},
  {label:'Chat with the employee',min:'manager'},{label:'Edit employee instructions and brief',min:'manager'},{label:'Publish pages',min:'manager'},
  {label:'Approve drafts and campaign decisions',min:'owner'},{label:'Share campaigns and manage access',min:'owner'},{label:'Usage, backups and settings',min:'owner'}
];
const order:(Role|'owner')[]=['viewer','reviewer','contributor','manager','owner'];

function People({online,state}:{online:boolean;state:MarketingState}){
  const [address,setAddress]=useState<string|null>(null),[devices,setDevices]=useState<Devices>({devices:[],pending:[]}),[roles,setRoles]=useState<Record<string,Role>>({});
  const [code,setCode]=useState<{code:string;expires:string}|null>(null),[inviting,setInviting]=useState(false);
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  const refresh=useCallback(async()=>{
    const [list,entries]=await Promise.all([api<Devices>('/devices'),api<{principalId:string;role:Role}[]>('/team/roles')]);
    setDevices(list);setRoles(Object.fromEntries(entries.map(entry=>[entry.principalId,entry.role])));
  },[]);
  useEffect(()=>{
    let active=true;
    void api<{phoneOrigin:string|null}>('/state').then(result=>{if(active)setAddress(result.phoneOrigin);}).catch(()=>{});
    void refresh().catch(cause=>{if(active)setError((cause as Error).message);});
    const timer=setInterval(()=>{if(document.visibilityState==='visible')void refresh().catch(()=>{});},6000);
    return()=>{active=false;clearInterval(timer);};
  },[refresh]);
  async function act(work:()=>Promise<unknown>,success:string){
    setBusy(true);setError('');setNotice('');
    try{await work();await refresh();setNotice(success);}catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  const members=devices.devices.filter(device=>!device.owner);
  const campaign=state.runway?.campaign?state.runway.project:null;
  return <section className="fe-section" aria-label="Members">
    <div className="fe-section-head"><div><h2>Members</h2><small>{members.length+1} with access · role changes apply on their next refresh</small></div>
      <button type="button" aria-label="Refresh members" className="fe-icon-button" disabled={busy||!online} onClick={()=>void act(refresh,'Member list refreshed.')}><RefreshCw size={16}/></button>
      <button type="button" className="primary" disabled={!online} aria-expanded={inviting} onClick={()=>setInviting(!inviting)}><UserPlus size={15}/> Invite</button></div>
    {inviting&&<div className="fe-invite">
      <div><h4><KeyRound size={15}/> Pair a browser</h4>
        {address?<><ol><li>Open <strong>{address}</strong> on the other device <button type="button" className="fe-inline-button" aria-label="Copy private address" onClick={()=>void navigator.clipboard.writeText(address).then(()=>setNotice('Private address copied.')).catch(()=>setError('Copy failed. Select the address instead.'))}><Copy size={13}/></button></li><li>Choose <strong>Join with a pairing code</strong> and enter the code</li><li>Confirm the request under Pending confirmation</li></ol>
          <button type="button" disabled={busy||!online} onClick={()=>void act(async()=>setCode(await api<{code:string;expires:string}>('/pair/start',{})),'One-time code ready. It expires in five minutes.')}>Create one-time code</button>
          {code&&<p className="fe-code" role="status"><strong>{code.code}</strong><small>Expires {new Date(code.expires).toLocaleTimeString()}. It is not the owner key.</small></p>}</>
          :<p className="fe-muted">A trusted HTTPS address is needed before a browser can pair. It appears here once the host has one.</p>}</div>
      <div><h4><ShieldCheck size={15}/> Invite a reviewer by email</h4>
        {campaign?<><p className="fe-muted">They sign in with their own account and see only <strong>{campaignTitle(campaign.goal)}</strong>.</p><CampaignInvitations campaignId={campaign.id}/></>:<p className="fe-muted">Email invitations are per campaign. Start a campaign first, then invite reviewers to it.</p>}</div>
    </div>}
    {devices.pending.length>0&&<div className="fe-callout" role="status"><strong>Pending confirmation</strong>{devices.pending.map(device=><div className="fe-callout-row" key={device.id}><span>{device.name}<small>Requested · expires {new Date(device.expires).toLocaleTimeString()}</small></span>
      <button type="button" className="primary" disabled={busy||!online||device.confirmed} onClick={()=>void act(()=>api('/pair/'+encodeURIComponent(device.id)+'/confirm',{}),'Confirmed. The other device can now finish pairing.')}>{device.confirmed?<><Check size={14}/> Confirmed</>:'Confirm'}</button></div>)}</div>}
    <div className="fe-table-wrap"><table className="fe-table fe-members"><thead><tr><th>Member</th><th>Sign-in</th><th>Role</th><th>Access until</th><th><span className="marketing-sr-only">Actions</span></th></tr></thead><tbody>
      <tr><td><span className="fe-cell-person"><span className="fe-avatar small">Y</span>You</span></td><td>Owner key</td><td><span className="fe-pill">Owner</span></td><td>—</td><td/></tr>
      {members.map(device=>{const principal=device.accountId||device.id,role=roles[principal]||'reviewer';return <tr key={device.id}>
        <td><span className="fe-cell-person"><span className="fe-avatar small muted">{initials(device.name)}</span><span>{device.name}<small className="fe-mono">{principal.slice(0,8)}</small></span></span></td>
        <td>{device.accountId?'Signed-in account':'Paired browser'}</td>
        <td><select aria-label={'Role for '+device.name} disabled={busy||!online} value={role} onChange={event=>{const next=event.target.value as Role;void act(()=>api('/team/roles/'+encodeURIComponent(principal),{role:next},'PUT'),`${device.name} is now a ${roleChoices.find(item=>item.role===next)?.label.toLowerCase()}.`);}}>
          {roleChoices.map(item=><option key={item.role} value={item.role}>{item.label}</option>)}</select></td>
        <td>{new Date(device.expires).toLocaleDateString()}</td>
        <td><button type="button" className="fe-ghost" disabled={busy||!online} onClick={()=>{if(window.confirm(`Revoke ${device.name}? This browser session ends immediately.`))void act(()=>api('/devices/'+encodeURIComponent(device.id)+'/revoke',{}),'Access revoked.');}}>Revoke</button></td></tr>;})}
    </tbody></table></div>
    {!members.length&&<p className="fe-muted">No one else has access yet. Invite a teammate to review campaigns or help with the work.</p>}
    {notice&&<p className="fe-notice" role="status">{notice}</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </section>;
}

/** The multiplayer hub: who has access and in what role, the AI employees, and exactly what each role allows. */
export function TeamView({state,directory,status,owner,canEditEmployees,hostOnline,accessLabel,memberId,tab,onOpen,usage,onDirectory,onRefresh,onOnboard}:{
  state:MarketingState;directory:Directory;status:EmployeeStatus;owner:boolean;canEditEmployees:boolean;hostOnline:boolean;accessLabel:string;
  memberId:string|null;tab:EmployeeTab;onOpen:(memberId:string|null,tab?:EmployeeTab)=>void;usage?:UsageSummary|null;
  onDirectory:(next:Directory)=>void;onRefresh:()=>Promise<void>;onOnboard:()=>void;
}){
  const [section,setSection]=useState<'people'|'employees'|'roles'>(owner?'people':'employees');
  const [adding,setAdding]=useState(false);
  const member=directory.agents.find(item=>item.id===memberId);
  const nameOf=(id:string,fallback:string)=>directory.agents.find(item=>item.id===id)?.runtimeKey==='marketing'?state.employee.name||fallback:fallback;
  const current=member?'employees':section;
  return <div className="fe-view"><div className="fe-view-inner">
    <header className="fe-view-head"><div><h1>Team</h1><p>People and AI employees working in this workspace.</p></div>{!owner&&<span className="fe-pill">Your role: {accessLabel}</span>}</header>
    <nav className="fe-tabs fe-view-tabs" aria-label="Team sections">
      {owner&&<button type="button" aria-pressed={current==='people'} onClick={()=>{onOpen(null);setSection('people');}}>People</button>}
      <button type="button" aria-pressed={current==='employees'} onClick={()=>{onOpen(null);setSection('employees');}}>AI employees</button>
      <button type="button" aria-pressed={current==='roles'} onClick={()=>{onOpen(null);setSection('roles');}}>Roles & permissions</button>
    </nav>
    {current==='people'&&owner&&<People online={hostOnline} state={state}/>}
    {current==='employees'&&(member?<>
      <button type="button" className="fe-ghost fe-back" onClick={()=>onOpen(null)}>← All AI employees</button>
      <EmployeeProfile member={member} state={state} status={status} canEdit={canEditEmployees} tab={tab} onTab={next=>onOpen(member.id,next)} onRefresh={onRefresh} onOnboard={owner?onOnboard:undefined} usage={owner?usage??null:undefined}/></>
      :<section className="fe-section" aria-label="AI employees">
        <div className="fe-section-head"><div><h2>AI employees</h2><small>Each works from its own instructions and permissions</small></div>{owner&&<button type="button" disabled={!hostOnline} onClick={()=>setAdding(true)}><UserPlus size={15}/> Add AI employee</button>}</div>
        <div className="fe-list">{directory.agents.map(item=>{const live=item.runtimeKey==='marketing';return <button type="button" className="fe-list-row" key={item.id} onClick={()=>onOpen(item.id,'brief')}>
          <span className={'fe-avatar'+(live?'':' muted')}>{initials(nameOf(item.id,item.name))}</span>
          <span className="fe-list-main"><strong>{nameOf(item.id,item.name)}</strong><small>{item.role||'Responsibility to be defined'}</small></span>
          <span className={'fe-status-chip '+(live?status.tone:'off')}><i className={'fe-dot '+(live?status.tone:'off')}/>{live?status.label:'Setup needed'}</span><ChevronRight size={16}/></button>;})}</div>
      </section>)}
    {current==='roles'&&<section className="fe-section" aria-label="Roles and permissions">
      <div className="fe-section-head"><div><h2>Roles & permissions</h2><small>Each role includes everything to its left. The host enforces these on every request.</small></div></div>
      <div className="fe-table-wrap"><table className="fe-table fe-matrix"><thead><tr><th>Permission</th>{order.map(role=><th key={role}>{role==='owner'?'Owner':roleChoices.find(item=>item.role===role)?.label}</th>)}</tr></thead>
        <tbody>{matrix.map(row=><tr key={row.label}><td>{row.label}</td>{order.map(role=><td key={role} className="fe-matrix-cell">{order.indexOf(role)>=order.indexOf(row.min)?<Check size={15} aria-label="Allowed"/>:<Minus size={15} aria-label="Not allowed"/>}</td>)}</tr>)}</tbody></table></div>
      <p className="fe-muted">Approvals, sharing and access stay with the owner, so every decision on record is an owner receipt. New members start as Reviewer.</p>
    </section>}
    {adding&&<AddMember directory={directory} onSaved={onDirectory} onClose={()=>setAdding(false)}/>}
  </div></div>;
}
