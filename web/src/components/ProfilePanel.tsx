import {useEffect,useMemo,useState} from 'react';
import {ArrowLeft,BookOpen,Check,Edit3,RefreshCw} from 'lucide-react';
import {api} from '../api';
import {Raven} from './Raven';

type ProfileKind='identity'|'soul'|'user';
type ProfileDocument={path:string;content:string;version:string;updated:string};
type HistoryPage={path:string;content:string;version:string;updated:string};
type ProfileResponse={history:HistoryPage[];filePath:string;identity?:ProfileDocument;soul?:ProfileDocument;user?:ProfileDocument};
type LoadedProfile={document:ProfileDocument;history:HistoryPage[];filePath:string};

const details:Record<ProfileKind,{title:string;file:string;description:string}>={
  identity:{title:'Identity',file:'IDENTITY.md',description:'Who Thaddeus is, what he is here to do, and how he presents himself.'},
  soul:{title:'Soul',file:'SOUL.md',description:'His voice, temperament, humor, and the feeling he brings to conversation.'},
  user:{title:'User',file:'USER.md',description:'Owner-reviewed facts, preferences, and priorities Thaddeus may use as context.'}
};

function excerpt(content:string){
  return content.replace(/^#{1,6}\s+/gm,'').replace(/\*\*/g,'').replace(/\s+/g,' ').trim().slice(0,132);
}
function shortDate(value:string){
  const date=new Date(value);return Number.isNaN(date.getTime())?'Not edited yet':date.toLocaleDateString(undefined,{month:'short',day:'numeric',year:'numeric'});
}

export function ProfilePanel({online,owner,revision,onChanged,onOpenMemory}:{online:boolean;owner:boolean;revision?:string;onChanged:()=>Promise<unknown>;onOpenMemory:()=>void}){
  const [documents,setDocuments]=useState<Partial<Record<ProfileKind,LoadedProfile>>>({});
  const [open,setOpen]=useState<ProfileKind|null>(null),[draft,setDraft]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');

  async function load(){
    if(!owner)return;
    setBusy(true);setError('');
    try{
      const [identity,soul,user]=await Promise.all([
        api<ProfileResponse>('/settings/identity'),api<ProfileResponse>('/settings/soul'),api<ProfileResponse>('/settings/user')
      ]);
      const next:Record<ProfileKind,LoadedProfile>={
        identity:{document:identity.identity!,history:identity.history,filePath:identity.filePath},
        soul:{document:soul.soul!,history:soul.history,filePath:soul.filePath},
        user:{document:user.user!,history:user.history,filePath:user.filePath}
      };
      setDocuments(next);if(open)setDraft(next[open].document.content);
    }catch(reason){setError((reason as Error).message);}finally{setBusy(false);}
  }
  useEffect(()=>{void load();},[owner,revision]);

  function choose(kind:ProfileKind){
    const selected=documents[kind];if(!selected)return;
    setOpen(kind);setDraft(selected.document.content);setNotice('');setError('');
  }
  async function save(){
    if(!open||!documents[open]||draft===documents[open]!.document.content)return;
    setBusy(true);setError('');setNotice('');
    try{
      const current=documents[open]!;
      const saved=await api<ProfileResponse>('/settings/'+open,{content:draft,version:current.document.version},'PUT');
      const document=saved[open]!;
      setDocuments(value=>({...value,[open]:{document,history:saved.history,filePath:saved.filePath}}));
      setDraft(document.content);setNotice('Saved to '+document.path+'.');await onChanged();
    }catch(reason){setError((reason as Error).message);}finally{setBusy(false);}
  }
  const dirty=!!open&&!!documents[open]&&draft!==documents[open]!.document.content;
  const active=open?documents[open]:undefined;
  const identity=documents.identity?.document;
  const cards=useMemo(()=>['soul','user'] as ProfileKind[],[]);

  if(!owner)return <div className="profile-panel profile-unavailable"><Raven state={online?'idle':'disconnected'}/><h2>Thaddeus</h2><p>Identity, Soul, and User documents can only be reviewed in the owner browser.</p></div>;
  if(open&&active)return <section className="profile-panel profile-editor" aria-label={details[open].file+' editor'}>
    <header><button type="button" className="profile-back" onClick={()=>{setOpen(null);setNotice('');setError('');}}><ArrowLeft size={15}/> Profile</button><span>{details[open].file}</span></header>
    <p className="profile-kicker">{details[open].title}</p><h2>{details[open].description}</h2>
    <label>{details[open].file}<textarea aria-label={details[open].file} value={draft} maxLength={20000} disabled={busy} onChange={event=>{setDraft(event.target.value);setNotice('');}}/></label>
    <div className="profile-editor-meta"><span>{draft.length.toLocaleString()} / 20,000</span><span>{active.history.length} saved revision{active.history.length===1?'':'s'}</span></div>
    <div className="profile-editor-actions"><button type="button" disabled={!dirty||busy||!online||!draft.trim()} onClick={()=>void save()}><Check size={15}/>{busy?'Saving…':'Save changes'}</button><button type="button" disabled={!dirty||busy} onClick={()=>setDraft(active.document.content)}>Discard edits</button></div>
    <p className="profile-file-path" title={active.filePath}>Stored on this computer as {active.document.path}</p>
    {notice&&<p className="profile-notice" role="status">{notice}</p>}{error&&<p className="error" role="alert">{error}</p>}
  </section>;

  return <section className="profile-panel" aria-label="Thaddeus profile">
    <div className="profile-hero"><Raven state={online?'idle':'disconnected'}/><h2>Thaddeus</h2><p className={online?'profile-connected':'profile-offline'}>{online?'Connected':'Offline'}</p></div>
    {busy&&!identity?<p className="profile-loading"><RefreshCw size={15}/> Opening the folio…</p>:<>
      <button type="button" className="profile-primary-card identity" onClick={()=>choose('identity')} disabled={!documents.identity}>
        <span className="profile-card-heading"><strong>Identity</strong><Edit3 size={15}/></span><small>{details.identity.description}</small><p>{identity?excerpt(identity.content):'Identity is unavailable.'}</p><time>{identity?shortDate(identity.updated):''}</time>
      </button>
      <div className="profile-card-grid">{cards.map(kind=>{const document=documents[kind]?.document;return <button type="button" className={'profile-card '+kind} key={kind} onClick={()=>choose(kind)} disabled={!document}><strong>{details[kind].title}</strong><span>{details[kind].file}</span><p>{document?excerpt(document.content):'Unavailable'}</p><time>{document?shortDate(document.updated):''}</time></button>;})}</div>
      <button type="button" className="profile-memory-link" onClick={onOpenMemory}><BookOpen size={17}/><span><strong>Notes & memory</strong><small>Keep one main memory note, then branch into topic notes when a subject grows.</small></span></button>
    </>}
    {error&&<p className="error" role="alert">{error}</p>}
  </section>;
}
