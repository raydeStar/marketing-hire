import {useCallback,useEffect,useState} from 'react';
import {ArrowLeft,ChevronRight,Eye,FilePlus2,FileText,History,Pencil,Plus,Trash2,UserPlus,Users} from 'lucide-react';
import Markdown from 'react-markdown';
import {api} from '../api';
import {readableTime,type MarketingState} from '../components/MarketingPanels';
import {BriefEditor} from './BriefEditor';
import {fileTemplates,templateFor} from './fileTemplates';
import {Dialog,Empty,PageHead,initials,useAttempt,type Directory,type EmployeeStatus,type Member} from './shared';

type EmployeeFile={agentId:string;name:string;version:number;content:string;digest:string;author:string;deleted:boolean;createdAt:string;updatedAt:string};

function FileEditor({member,file,onSaved,onDeleted}:{member:Member;file:EmployeeFile|{name:string;content:string;version:0};onSaved:(file:EmployeeFile)=>void;onDeleted:()=>void}){
  const [text,setText]=useState(file.content),[mode,setMode]=useState<'edit'|'preview'>('edit'),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const [history,setHistory]=useState<EmployeeFile[]|null>(null);
  const attempt=useAttempt();
  const dirty=text!==file.content||file.version===0;
  useEffect(()=>{setText(file.content);setHistory(null);setError('');},[file.name,file.version]);
  async function save(deleted=false){
    if(busy)return;setBusy(true);setError('');
    const change={name:file.name,version:file.version,content:deleted?'':text,deleted};
    try{const saved=await api<EmployeeFile>(`/organization/agents/${member.id}/files`,{...change,requestId:attempt.id(member.id+JSON.stringify(change))},'PUT');attempt.done();deleted?onDeleted():onSaved(saved);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  async function loadHistory(){try{setHistory(await api<EmployeeFile[]>(`/organization/agents/${member.id}/files/${encodeURIComponent(file.name)}/history`));}catch(cause){setError((cause as Error).message);}}
  return <div className="fe-reader fe-file-editor">
    <div className="fe-reader-head"><div><h3>{file.name}</h3><div className="fe-reader-meta">{file.version?<><span className="fe-pill">Version {file.version}</span><small>Saved {readableTime((file as EmployeeFile).updatedAt)}</small></>:<span className="fe-pill attn">Not saved yet</span>}{dirty&&file.version>0&&<span className="fe-pill accent">Unsaved changes</span>}</div></div>
      <div className="fe-segmented"><button type="button" aria-pressed={mode==='edit'} onClick={()=>setMode('edit')}><Pencil size={14}/> Edit</button><button type="button" aria-pressed={mode==='preview'} onClick={()=>setMode('preview')}><Eye size={14}/> Preview</button></div></div>
    {mode==='edit'?<textarea className="fe-editor" aria-label={'Contents of '+file.name} value={text} onChange={event=>setText(event.target.value)} spellCheck/>:<div className="fe-prose"><Markdown>{text||'*Empty file*'}</Markdown></div>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <div className="fe-decision-bar">
      {file.version>0&&<button type="button" className="fe-ghost" onClick={()=>void loadHistory()}><History size={15}/> History</button>}
      {file.version>0&&<button type="button" className="fe-ghost" disabled={busy} onClick={()=>{if(window.confirm(`Delete ${file.name}? Its history is kept.`))void save(true);}}><Trash2 size={15}/> Delete</button>}
      <small/>
      {dirty&&file.version>0&&<button type="button" className="fe-ghost" onClick={()=>setText(file.content)}>Discard</button>}
      <button type="button" className="primary" disabled={busy||!dirty} onClick={()=>void save()}>{busy?'Saving…':'Save file'}</button>
    </div>
    {history&&<div className="fe-history"><h4>History</h4>{history.map(item=><div key={item.version} className="fe-history-row"><span><strong>Version {item.version}{item.deleted?' · deleted':''}</strong><small>{readableTime(item.updatedAt)}</small></span>{!item.deleted&&<button type="button" className="fe-ghost" onClick={()=>{setText(item.content);setMode('edit');}}>Load into editor</button>}</div>)}</div>}
  </div>;
}

function MemberFiles({member}:{member:Member}){
  const [files,setFiles]=useState<EmployeeFile[]|null>(null),[selected,setSelected]=useState<string|null>(null),[draft,setDraft]=useState<{name:string;content:string;version:0}|null>(null);
  const [adding,setAdding]=useState(false),[custom,setCustom]=useState(''),[error,setError]=useState('');
  const load=useCallback(async()=>{try{const list=await api<EmployeeFile[]>(`/organization/agents/${member.id}/files`);setFiles(list);setError('');return list;}catch(cause){setError((cause as Error).message);return null;}},[member.id]);
  useEffect(()=>{void load().then(list=>{if(list?.length)setSelected(current=>current??list[0].name);});},[load]);
  const current=draft||files?.find(file=>file.name===selected);
  function start(name:string){
    const existing=files?.find(file=>file.name.toLowerCase()===name.toLowerCase());
    setAdding(false);setCustom('');
    if(existing){setDraft(null);setSelected(existing.name);return;}
    setDraft({name,content:templateFor(name)?.content(member.name)||`# ${name.replace(/\.md$/i,'')}\n\n`,version:0});setSelected(name);
  }
  const missing=fileTemplates.filter(template=>!files?.some(file=>file.name.toLowerCase()===template.name.toLowerCase()));
  return <div>
    <div className="fe-notice"><FileText size={17}/><span><strong>Instructions {member.name} works from</strong>Markdown files saved on your host with full history. They reach the employee’s runtime once agent setup connects them.</span></div>
    <div className="fe-split fe-files">
      <aside>
        <div className="fe-row-list">{files?.map(file=><button type="button" key={file.name} className="fe-row" aria-pressed={!draft&&selected===file.name} onClick={()=>{setDraft(null);setSelected(file.name);}}><span className="fe-row-icon"><FileText size={17}/></span><span className="fe-row-body"><strong>{file.name}</strong><small>{templateFor(file.name)?.purpose||`Version ${file.version}`}</small></span></button>)}
          {draft&&<button type="button" className="fe-row" aria-pressed="true"><span className="fe-row-icon accent"><FilePlus2 size={17}/></span><span className="fe-row-body"><strong>{draft.name}</strong><small>New file</small></span></button>}</div>
        {files&&!files.length&&!draft&&<p className="fe-muted fe-files-empty">No files yet. Start with AGENTS.md; it tells {member.name} how to work.</p>}
        <button type="button" className="fe-add-file" onClick={()=>setAdding(true)}><Plus size={15}/> Add a file</button>
      </aside>
      {current?<FileEditor key={current.name} member={member} file={current} onSaved={saved=>{setDraft(null);setSelected(saved.name);void load();}} onDeleted={()=>{setDraft(null);setSelected(null);void load().then(list=>setSelected(list?.[0]?.name??null));}}/>
        :<div className="fe-reader"><Empty icon={<FileText size={30}/>} title="Pick a file or add one">{`Suggested: ${fileTemplates.map(item=>item.name).join(', ')}.`}</Empty></div>}
    </div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {adding&&<Dialog title="Add a file" onClose={()=>setAdding(false)}>
      <div className="fe-row-list">{missing.map(template=><button type="button" className="fe-row" key={template.name} onClick={()=>start(template.name)}><span className="fe-row-icon accent"><FilePlus2 size={17}/></span><span className="fe-row-body"><strong>{template.name}</strong><small>{template.purpose}</small></span><ChevronRight size={16}/></button>)}</div>
      <form className="fe-form fe-custom-file" onSubmit={event=>{event.preventDefault();const name=custom.trim().replace(/(\.md)?$/i,'.md');if(name.length>3)start(name);}}>
        <label>Or name your own<input value={custom} onChange={event=>setCustom(event.target.value)} placeholder="PLAYBOOK.md" pattern="[A-Za-z0-9][A-Za-z0-9_.\-]*" maxLength={60}/></label>
        <footer><button className="primary" disabled={!custom.trim()}>Create</button></footer>
      </form>
    </Dialog>}
  </div>;
}

function AddMember({directory,onSaved,onClose}:{directory:Directory;onSaved:(next:Directory)=>void;onClose:()=>void}){
  const [name,setName]=useState(''),[role,setRole]=useState(''),[department,setDepartment]=useState(directory.departments[0]?.id||''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const attempt=useAttempt();
  async function save(event:React.FormEvent){
    event.preventDefault();if(busy||!name.trim())return;setBusy(true);setError('');
    const fields={version:directory.version,departments:directory.departments,agents:[...directory.agents,{id:crypto.randomUUID(),name:name.trim(),role:role.trim(),departmentId:department||null,kind:'employee' as const,runtimeKey:null}]};
    try{onSaved(await api<Directory>('/organization',{...fields,requestId:attempt.id(JSON.stringify({name,role,department}))},'PUT'));attempt.done();onClose();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <Dialog title="Add a teammate" onClose={onClose}><form className="fe-form" onSubmit={event=>void save(event)}>
    <label>Name<input autoFocus required maxLength={80} value={name} onChange={event=>setName(event.target.value)} placeholder="e.g. Content researcher"/></label>
    <label>What they own<textarea rows={3} maxLength={500} value={role} onChange={event=>setRole(event.target.value)} placeholder="e.g. Weekly competitor and community research"/></label>
    <label>Department<select value={department} onChange={event=>setDepartment(event.target.value)}><option value="">Company-wide</option>{directory.departments.map(item=><option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
    <p className="fe-muted">You can write their files now. They start working once a runtime is connected for them.</p>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={busy||!name.trim()}>{busy?'Adding…':'Add teammate'}</button></footer>
  </form></Dialog>;
}

export function TeamView({state,directory,status,canWrite,memberId,tab,onOpen,onDirectory,onRefresh,onStartOnboarding}:{
  state:MarketingState;directory:Directory;status:EmployeeStatus;canWrite:boolean;memberId:string|null;tab:'files'|'brief';
  onOpen:(memberId:string|null,tab?:'files'|'brief')=>void;onDirectory:(next:Directory)=>void;onRefresh:()=>Promise<void>;onStartOnboarding:()=>void;
}){
  const [adding,setAdding]=useState(false);
  const member=directory.agents.find(item=>item.id===memberId);
  const nameOf=(item:Member)=>item.runtimeKey==='marketing'?state.employee.name||item.name:item.name;
  const department=(item:Member)=>directory.departments.find(entry=>entry.id===item.departmentId)?.name||'Company-wide';
  if(member){
    const live=member.runtimeKey==='marketing';
    const shown={...member,name:nameOf(member)};
    return <div className="fe-page"><div className="fe-page-inner">
      <button type="button" className="fe-ghost fe-back" onClick={()=>onOpen(null)}><ArrowLeft size={16}/> Team</button>
      <header className="fe-member-head"><span className={'fe-avatar large'+(live?'':' muted')}>{initials(shown.name)}</span><div><h1>{shown.name}</h1><p>{member.role||'Responsibility to be defined'} · {department(member)}</p></div>
        <span className={'fe-pill '+(live?status.tone==='live'?'ok':'attn':'')}>{live?status.label:'Runtime not connected'}</span></header>
      {live&&<nav className="fe-segmented fe-member-tabs" aria-label="Member views"><button type="button" aria-pressed={tab==='files'} onClick={()=>onOpen(member.id,'files')}>Files</button><button type="button" aria-pressed={tab==='brief'} onClick={()=>onOpen(member.id,'brief')}>Business brief</button></nav>}
      {live&&tab==='brief'?<div className="fe-brief-page">
        <BriefEditor profile={state.profile} evidenceEnabled={state.businessBriefEvidenceEnabled===true} canEdit={canWrite} onSaved={onRefresh}/>
        <button type="button" className="fe-ghost" onClick={onStartOnboarding}>Redo onboarding from your website or a conversation →</button>
      </div>:<MemberFiles member={shown}/>}
    </div></div>;
  }
  return <div className="fe-page"><div className="fe-page-inner">
    <PageHead title="Team" subtitle="Your AI employees, what they’re told, and how they work.">{canWrite&&<button type="button" onClick={()=>setAdding(true)}><UserPlus size={16}/> Add teammate</button>}</PageHead>
    <div className="fe-grid fe-team-grid">{directory.agents.map(item=>{const live=item.runtimeKey==='marketing';return <button type="button" className="fe-tile fe-member-tile" key={item.id} onClick={()=>onOpen(item.id)}>
      <div className="fe-tile-body"><span className={'fe-avatar large'+(live?'':' muted')}>{initials(nameOf(item))}</span><strong>{nameOf(item)}</strong><small>{item.role||'Responsibility to be defined'}</small>
        <span className={'fe-pill '+(live&&status.tone==='live'?'ok':live?'attn':'')}>{live?status.label:'Setup needed'}</span></div></button>;})}
      {!directory.agents.length&&<Empty icon={<Users size={30}/>} title="No teammates yet"/>}</div>
    {adding&&<AddMember directory={directory} onSaved={onDirectory} onClose={()=>setAdding(false)}/>}
  </div></div>;
}
