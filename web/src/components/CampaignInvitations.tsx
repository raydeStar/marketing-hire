import {useEffect,useState} from 'react';
import {api} from '../api';
import type {CustomerLoginView} from './CustomerSignIn';

const pendingKey='first-employee-campaign-invitation';
export function pendingInvitation(){
  const fragment=new URLSearchParams(location.hash.slice(1));
  try{
    if(fragment.has('invite')){
      const token=fragment.get('invite')||'';
      if(/^[a-f0-9]{64}$/.test(token))sessionStorage.setItem(pendingKey,token);
      else sessionStorage.removeItem(pendingKey);
      history.replaceState(history.state,'',location.pathname+location.search);
    }
    return sessionStorage.getItem(pendingKey)||'';
  }catch{return fragment.get('invite')||'';}
}
function clearInvitation(){try{sessionStorage.removeItem(pendingKey);}catch{}}
type Invitation={id:string;email:string;provider:string;expiresAt:string;status:string;acceptedBy:string|null};
type Issued={id:string;url:string;email:string;provider:string;expiresAt:string;scope:string};

export function CampaignInvitations({campaignId}:{campaignId:string}){
  const [login,setLogin]=useState<CustomerLoginView|null>(null),[items,setItems]=useState<Invitation[]>([]);
  const [email,setEmail]=useState(''),[provider,setProvider]=useState('google'),[hours,setHours]=useState(72);
  const [issued,setIssued]=useState<Issued|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState(''),[copied,setCopied]=useState(false);
  const endpoint=`/marketing/campaigns/${campaignId}/invitations`;
  useEffect(()=>{
    let active=true;
    void api<CustomerLoginView>('/auth/customer').then(async available=>{
      if(!active)return;
      setLogin(available);setProvider(available.providers[0]||'google');
      if(available.enabled){const listed=await api<{invitations:Invitation[]}>(endpoint);if(active)setItems(listed.invitations);}
    }).catch(()=>{if(active)setError('Invitation controls are unavailable. The host may need its login update.');});
    return()=>{active=false;};
  },[endpoint]);
  async function create(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');setIssued(null);setCopied(false);
    try{
      const result=await api<Issued>(endpoint,{email,provider,expiresInHours:hours});setIssued(result);
      setItems(current=>[{id:result.id,email:result.email,provider:result.provider,expiresAt:result.expiresAt,status:'pending',acceptedBy:null},...current]);
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  async function revoke(id:string){
    setBusy(true);setError('');
    try{
      await api(`${endpoint}/${id}/revoke`,{});
      setItems(current=>current.map(item=>item.id===id?{...item,status:'revoked'}:item));
      if(issued?.id===id)setIssued(null);
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <section className="campaign-invitations" aria-label="Invite a campaign reviewer">
    <h4>Invite a reviewer</h4>
    <p>They can read this campaign’s shared draft, comment and request changes. You approve employee work. The link works only for the email and sign-in provider you choose.</p>
    {login?.enabled&&<form className="company-form" onSubmit={event=>void create(event)}>
      <label>Reviewer email<input type="email" required maxLength={320} value={email} onChange={event=>setEmail(event.target.value)}/></label>
      <div className="campaign-desk-access-controls"><label>Sign-in provider<select value={provider} onChange={event=>setProvider(event.target.value)}>{login.providers.map(item=><option key={item} value={item}>{item==='google'?'Google':'Microsoft'}</option>)}</select></label>
      <label>Link expires after<select value={hours} onChange={event=>setHours(Number(event.target.value))}><option value={24}>24 hours</option><option value={72}>3 days</option><option value={168}>7 days</option></select></label>
      <button type="submit" disabled={busy||!email.trim()}>{busy?'Saving…':'Create invitation link'}</button></div>
    </form>}
    {provider==='microsoft'&&<p>Microsoft must verify the recipient’s email for an email invitation to work. If it does not, have the reviewer sign in first, then grant that account campaign access below.</p>}
    {login&&!login.enabled&&<p>Customer sign-in needs to be configured before you can invite a person.</p>}
    {issued&&<div className="campaign-invitation-link" role="status"><strong>Ready for {issued.email}</strong><p>Expires {new Date(issued.expiresAt).toLocaleString()}. Copy and send this link yourself. It is shown only here.</p>
      <label>Invitation link<input readOnly value={issued.url} onFocus={event=>event.target.select()}/></label>
      <button type="button" onClick={()=>void navigator.clipboard.writeText(issued.url).then(()=>setCopied(true)).catch(()=>setError('Copy the selected link manually.'))}>{copied?'Copied':'Copy invitation link'}</button></div>}
    {items.map(item=><div className="campaign-desk-member" key={item.id}><span>{item.email} · {item.provider==='google'?'Google':'Microsoft'} · {item.status==='pending'&&Date.parse(item.expiresAt)<=Date.now()?'expired':item.status}<small>Expires {new Date(item.expiresAt).toLocaleString()}</small></span>
      {item.status==='pending'&&Date.parse(item.expiresAt)>Date.now()&&<button type="button" disabled={busy} onClick={()=>void revoke(item.id)}>Revoke invitation</button>}</div>)}
    {!!items.length&&<small>Accepted invitations are a record. Use campaign access to remove a member’s access on all devices.</small>}
    {error&&<p role="alert">{error}</p>}
  </section>;
}

type Preview={projectId:string;campaignName:string;email:string;provider:string;expiresAt:string;scope:string};
export function AcceptCampaignInvitation({token,customerAccount,login,onDone}:{token:string;customerAccount:boolean;login:CustomerLoginView|null;onDone:()=>void}){
  const [preview,setPreview]=useState<Preview|null>(null),[error,setError]=useState(''),[busy,setBusy]=useState(false);
  useEffect(()=>{
    let active=true;
    if(customerAccount)void api<Preview>('/marketing/campaigns/invitations/preview',{token}).then(value=>{if(active)setPreview(value);}).catch(cause=>{if(active)setError((cause as Error).message);});
    return()=>{active=false;};
  },[token,customerAccount]);
  async function accept(){
    setBusy(true);setError('');
    try{
      const result=await api<{projectId:string}>('/marketing/campaigns/invitations/accept',{token});
      clearInvitation();history.replaceState(history.state,'',`/#campaign=${encodeURIComponent(result.projectId)}`);onDone();
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <main className="unlock customer-sign-in campaign-invitation-accept">
    <p className="eyebrow">FIRST EMPLOYEE / CAMPAIGN INVITATION</p><h1>{preview?'Review the invitation':'Open your invitation'}</h1>
    {preview?<><h2>{preview.campaignName}</h2><p>{preview.scope}</p><p>Joining as <strong>{preview.email}</strong> with {preview.provider==='google'?'Google':'Microsoft'}. Expires {new Date(preview.expiresAt).toLocaleString()}.</p><button className="primary" disabled={busy} onClick={()=>void accept()}>{busy?'Joining…':'Accept and open campaign'}</button></>:
      <p>{customerAccount&&!error?'Checking your invitation…':'Sign in with the account and provider chosen by the workspace owner.'}</p>}
    {error&&<p role="alert">{error}</p>}
    {!preview&&login?.providers.map(provider=><a className="primary" key={provider} href={`${login.origin}/api/auth/customer/login?provider=${encodeURIComponent(provider)}`}>Sign in with another {provider==='google'?'Google':'Microsoft'} account</a>)}
    <button className="text-button" disabled={busy} onClick={()=>{clearInvitation();onDone();}}>Back to workspace</button>
  </main>;
}
