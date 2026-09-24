import {useEffect,useRef,useState} from 'react';
import {api} from '../api';
import {ArtifactBody} from './MarketingRunwayPanel';
import {readableTime,requestId,type RunwayArtifact} from './MarketingPanels';
import '../campaign-review.css';

export type SharedCampaign={
  project:{id:string;goal:string;status:string;version:number;updatedAt:number};
  campaign:{version:number;stage:string;brief:string;experiment:string;provisional:boolean};
  artifact:{id:string;digest:string;kind:string;content:string;createdAt:number};
  review:{decision:string;instruction:string;actorName:string;createdAt:number}|null;
  discussion:{requestId:string;kind:'comment'|'revision_request';actorName:string;actorId:string;
    content:string;artifactId:string;artifactDigest:string;inputId:string|null;status:string;createdAt:string;
    nativeProfileId:string|null;nativeRecorded:boolean}[];
  members:{name:string;role:string}[];employee:{name:string;kind:string};presentViewers:unknown[];
  native:{sessionConnected:boolean;boundDevice:boolean;gatewayProfileObserved:boolean;
    gatewayProfileId:string|null;identitySlot:string|null;requiresHttps:boolean};
  execution:{liveEnabled:boolean;publicationEnabled:boolean;requestChangesAvailable:boolean};
};
type SharedProject={id:string;goal:string;status:string;updated_at:number};

function statusLabel(status:string){
  return ({awaiting_owner_authorization:'Awaiting owner authorization',
    authorized_execution_unavailable:'Authorized; execution unavailable',
    recorded:'Saved',pending:'Saving',conflict:'Conflict · reload and reapply',
    native_recorded:'Gateway saved · project ledger pending',
    unknown:'Outcome unknown · do not resend'} as Record<string,string>)[status]||status.replaceAll('_',' ');
}

export function CampaignSharedWorkspace({deviceId,customerAccount=false}:{deviceId:string;customerAccount?:boolean}){
  const [projects,setProjects]=useState<SharedProject[]>([]),[selectedId,setSelectedId]=useState(()=>new URLSearchParams(location.hash.slice(1)).get('campaign')||'');
  const [review,setReview]=useState<SharedCampaign|null>(null),[loading,setLoading]=useState(true);
  const [error,setError]=useState(''),[draft,setDraft]=useState(''),[kind,setKind]=useState<'comment'|'revision_request'>('comment');
  const [saving,setSaving]=useState(false),[refreshKey,setRefreshKey]=useState(0),[contextOpen,setContextOpen]=useState(()=>window.innerWidth>=1500);
  const attempt=useRef<{signature:string;id:string}|null>(null);
  useEffect(()=>{setReview(null);setDraft('');setError('');attempt.current=null;},[selectedId]);
  useEffect(()=>{if(review&&!review.execution.requestChangesAvailable)setKind('comment');},[review?.artifact.id,review?.execution.requestChangesAvailable]);
  useEffect(()=>{
    let active=true;
    const load=async()=>{
      try{
        const listed=await api<{projects:SharedProject[]}>('/marketing/campaigns/shared');
        if(!active)return;
        const nextId=listed.projects.some(item=>item.id===selectedId)?selectedId:listed.projects[0]?.id||'';
        setProjects(listed.projects);
        if(nextId!==selectedId)setSelectedId(nextId);
        const snapshot=nextId?await api<SharedCampaign>(`/marketing/campaigns/${nextId}/review`):null;
        if(!active)return;
        setReview(current=>!snapshot||!current||current.project.id!==snapshot.project.id||
          snapshot.project.version>=current.project.version?snapshot:current);
        setError('');setLoading(false);
      }catch(cause){
        if(!active)return;
        const message=(cause as Error).message;
        if(/403|401|denied|expired|not approved/i.test(message)){
          setReview(null);setProjects([]);setSelectedId('');setDraft('');attempt.current=null;
        }
        setError(message);setLoading(false);
      }
    };
    void load();
    const timer=window.setInterval(()=>{if(document.visibilityState==='visible')void load();},8000);
    const onFocus=()=>void load();
    window.addEventListener('focus',onFocus);
    return()=>{active=false;clearInterval(timer);window.removeEventListener('focus',onFocus);};
  },[selectedId,refreshKey]);

  async function submit(event:React.FormEvent){
    event.preventDefault();if(!review||!draft.trim()||saving)return;
    const content=draft.trim();
    const signature=[review.project.id,review.artifact.id,review.artifact.digest,kind,content].join(':');
    const id=attempt.current?.signature===signature?attempt.current.id:requestId();
    attempt.current={signature,id};setSaving(true);setError('');
    try{
      await api(`/marketing/campaigns/${review.project.id}/inputs`,{
        requestId:id,kind,artifactId:review.artifact.id,
        artifactDigest:review.artifact.digest,projectVersion:review.project.version,content
      });
      attempt.current=null;setDraft('');setRefreshKey(value=>value+1);
    }catch(cause){setError((cause as Error).message);setRefreshKey(value=>value+1);}
    finally{setSaving(false);}
  }

  let brief:Record<string,unknown>={};
  try{if(review)brief=JSON.parse(review.campaign.brief) as Record<string,unknown>;}catch{/* Keep the saved draft readable. */}
  const artifact=review?{
    id:review.artifact.id,digest:review.artifact.digest,kind:review.artifact.kind,
    content:review.artifact.content,created_at:review.artifact.createdAt,
    source_urls:'',step_id:''
  } satisfies RunwayArtifact:null;
  return <section className={'campaign-desk campaign-shared'+(contextOpen?'':' context-closed')} aria-label="Shared campaign review">
    <aside className="campaign-desk-list" aria-label="Shared campaigns">
      <div className="campaign-desk-list-heading"><span>MARKETING</span><strong>Shared campaigns</strong></div>
      {projects.map(item=><button key={item.id} type="button" className={selectedId===item.id?'selected':''} onClick={()=>setSelectedId(item.id)}><span className="campaign-list-title">{item.goal}</span><small>{item.status.replaceAll('_',' ')} · {readableTime(item.updated_at)}</small></button>)}
      {!loading&&!projects.length&&<p className="campaign-desk-muted">{customerAccount?<>You’re signed in. No campaigns have been shared with your account yet. Your member ID is <strong>{deviceId.slice(0,8)}</strong>.</>:<>Pairing is complete. The owner has not granted this browser a campaign yet. This browser is <strong>{deviceId.slice(0,8)}</strong>.</>}</p>}
      <div className="campaign-desk-list-foot">Signed-in collaborator <span>Access is scoped to each campaign.</span></div>
    </aside>
    <div className="campaign-desk-main">
      <header className="campaign-desk-header"><p className="eyebrow">SHARED MARKETING / CAMPAIGN REVIEW</p><h2>{review?'Campaign review':'Your shared work'}</h2><p>{review?.project.goal||(customerAccount?'Ask the workspace owner to grant your account access to a campaign. Shared work will appear here.':`On the owner host, open Work → Campaigns → What changed → Campaign access. Choose Paired browser · ${deviceId.slice(0,8)} and grant this saved campaign. Return here to review its draft.`)}</p></header>
      {loading&&<p role="status">Opening shared campaign…</p>}
      {error&&<div className="campaign-desk-hold" role="alert">{error} {review&&<button type="button" onClick={()=>setRefreshKey(value=>value+1)}>Reload campaign</button>}</div>}
      {review&&<>
        <div className="campaign-desk-status" aria-label="Shared campaign status"><div><span>Campaign stage</span><strong>{review.campaign.stage}</strong></div><div><span>Worker</span><strong>{review.project.status==='needs_review'?'Waiting for owner review':review.project.status.replaceAll('_',' ')}</strong></div><div><span>Next action</span><strong>{review.discussion.some(item=>item.status==='awaiting_owner_authorization')?'Owner decision on requested change':'Review the saved draft and add specific feedback'}</strong></div><div><span>Access</span><strong>Shared campaign · comment and request changes</strong></div></div>
        <div className="campaign-desk-reading"><p className="eyebrow">SHARED DRAFT</p><h3>{review.artifact.kind==='revision_angles'?'Revised post angles':'Draft post angles'}</h3><p className="campaign-desk-subline">Exact saved version · {readableTime(review.artifact.createdAt)} · provisional brief, owner review pending</p>{artifact&&<ArtifactBody artifact={artifact}/>}{review.review&&<div className="campaign-desk-decision"><strong>{review.review.decision.replaceAll('_',' ')} · {review.review.actorName}</strong><p>{review.review.instruction||'Saved owner decision for this exact version.'}</p></div>}</div>
        <section className="campaign-shared-discussion" aria-label="Campaign discussion"><div className="campaign-shared-discussion-head"><div><p className="eyebrow">VERSION-LINKED DISCUSSION</p><h3>Review notes</h3></div><span>{review.discussion.length} saved</span></div>
          {!review.discussion.length&&<p>No one has commented on this shared version yet.</p>}
          {review.discussion.map(item=><article key={item.requestId}><div><strong>{item.actorName}</strong><time>{readableTime(item.createdAt)}</time></div><p>{item.content}</p><small>{item.kind==='revision_request'?'Change request':'Comment'} · {statusLabel(item.status)} · exact draft {item.artifactDigest.slice(0,12)}…{item.nativeRecorded&&item.nativeProfileId?` · Gateway profile ${item.nativeProfileId.slice(0,8)}…`:''}</small></article>)}
          {review.native.sessionConnected&&window.location.protocol!=='https:'&&<p className="campaign-desk-hold" role="status">This native-linked campaign accepts new notes only through the authenticated HTTPS address. Copy your draft before switching addresses.</p>}
          <form onSubmit={event=>void submit(event)}><fieldset><legend>Add to this campaign</legend><label><input type="radio" name="shared-input-kind" checked={kind==='comment'} onChange={()=>setKind('comment')}/> Comment</label><label><input type="radio" name="shared-input-kind" checked={kind==='revision_request'} disabled={!review.execution.requestChangesAvailable} onChange={()=>setKind('revision_request')}/> Request a change</label></fieldset>{!review.execution.requestChangesAvailable&&<p>This version already has a decision, is no longer awaiting review, or is the pilot's single linked revision. Add a comment for further owner review.</p>}<label htmlFor="campaign-shared-draft">{kind==='revision_request'?'Change requested':'Comment'} for draft {review.artifact.digest.slice(0,12)}… · audience {String(brief.audience||'provisional')}
            <textarea id="campaign-shared-draft" maxLength={1000} required value={draft} onChange={event=>setDraft(event.target.value)} placeholder="Name the source, claim, audience assumption, or wording you want reviewed."/></label><div><small>Your request is attributed to your signed-in identity. It does not start the employee or authorize more work.</small><button type="submit" disabled={saving||!draft.trim()}>{saving?'Saving…':kind==='revision_request'?'Request change':'Save comment'}</button></div></form>
        </section>
      </>}
    </div>
    {contextOpen?<aside className="campaign-desk-context" aria-label="Shared campaign context"><div className="campaign-desk-context-head"><strong>Context</strong><button type="button" onClick={()=>setContextOpen(false)} aria-label="Collapse campaign context">×</button></div><div><span>Members</span>{review?.members.map((item,index)=><strong key={index}>{item.name} · {item.role}</strong>)}{!review&&<p>Open a campaign to see its members.</p>}</div><div><span>AI employee</span><strong>{review?.employee.name||'Marketing employee'}</strong><p>Employee work needs a separate owner grant and an eligible runtime.</p></div><div><span>Brief</span><p>{review?String(brief.audience||'Audience provisional'):'No campaign selected'}</p><p>Owner review pending. Sources in the draft are cited observations, not validated demand.</p></div><div><span>Native conversation</span><p>{review?.native.gatewayProfileObserved?'Gateway profile recorded for this device':review?.native.sessionConnected?'Session connected; first profile receipt pending':'Not connected'}</p><p>{review?.native.sessionConnected?'Comments are mirrored as Gateway suggestions and saved in the campaign ledger.':'Current comments are host-attributed campaign inputs.'}</p></div></aside>:<button type="button" className="campaign-desk-reopen" onClick={()=>setContextOpen(true)}>Show context</button>}
  </section>;
}
