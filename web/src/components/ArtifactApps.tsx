import {useEffect,useRef,useState,type ReactNode} from 'react';
import {ArrowUpRight,Check,ChevronRight,Download,History,PanelLeft,Pencil,Plus,Shapes,Trash2,SlidersHorizontal,Undo2,X} from 'lucide-react';
import {api} from '../api';
import {ArtifactPreview} from './ArtifactPreview';
import type {AppDefinition,AppEntry,AppField,AppRevision,AppSummary,ArtifactApp} from '../types';
import '../artifact-apps.css';

export const localDay=()=>{const day=new Date();return `${day.getFullYear()}-${String(day.getMonth()+1).padStart(2,'0')}-${String(day.getDate()).padStart(2,'0')}`;};
const uuid=()=>crypto.randomUUID().replaceAll('-','');
type Edit={version:string;definition?:AppDefinition;upserts?:AppEntry[];deleteIds?:string[];archived?:boolean};

export function ArtifactApps({apps,onSelect,onBuild,onChanged,online,view,onView,children}:{view:'apps'|'notes';onView:(view:'apps'|'notes')=>void;apps:AppSummary[];onSelect:(id:string)=>void;onBuild:()=>void;onChanged:()=>Promise<unknown>;online:boolean;children:ReactNode}){
  const [archived,setArchived]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const [editing,setEditing]=useState<{app:ArtifactApp;title:string;description:string}|null>(null);
  const working=useRef(false),retry=useRef<{digest:string;operationId:string}|null>(null);
  async function change(id:string,edit:Edit){
    if(working.current)return false;working.current=true;setBusy(true);setError('');
    const digest=JSON.stringify({id,edit});if(retry.current?.digest!==digest)retry.current={digest,operationId:uuid()};
    try{await api('/artifacts/'+id,{...edit,operationId:retry.current.operationId},'PUT');await onChanged();retry.current=null;return true;}
    catch(reason){setError((reason as Error).message);return false;}finally{working.current=false;setBusy(false);}
  }
  async function editApp(id:string){
    if(working.current)return;working.current=true;setBusy(true);setError('');
    try{const app=await api<ArtifactApp>('/artifacts/'+id);setEditing({app,title:app.definition.title,description:app.definition.description});requestAnimationFrame(()=>document.getElementById('shelf-app-name')?.focus());}
    catch(reason){setError((reason as Error).message);}finally{working.current=false;setBusy(false);}
  }
  return <section className="artifact-workspace">
    <div className="artifact-heading"><div><p className="eyebrow">MADE FOR YOUR EVERYDAY</p><h1>Artifacts</h1></div><button type="button" onClick={()=>onBuild()}><Plus size={16}/> Build an app</button></div>
    <div className="artifact-tabs" role="group" aria-label="Artifact collections"><button aria-pressed={view==='apps'} onClick={()=>onView('apps')}>Apps <span>{apps.filter(item=>!item.archived).length}</span></button><button aria-pressed={view==='notes'} onClick={()=>onView('notes')}>Notes & memory</button></div>
    {view==='notes'?children:<>
      <div className="app-shelf-heading"><p>{archived?'Deleted apps keep their data here until restored.':'Describe your idea in chat. Thaddeus will ask what he needs, then build it with you.'}</p><button className="text-button" aria-pressed={archived} onClick={()=>{setArchived(!archived);setEditing(null);}}>{archived?'Back to apps':'Trash'}</button></div>
      {error&&<p className="error" role="alert">{error}</p>}
      {editing&&<form className="app-entry-form" aria-label="Edit app" onSubmit={event=>{event.preventDefault();void change(editing.app.id,{version:editing.app.version,definition:{...editing.app.definition,title:editing.title.trim(),description:editing.description}}).then(ok=>{if(ok)setEditing(null);});}}>
        <div className="app-form-heading"><h2>Edit app</h2><button type="button" aria-label="Cancel app edit" onClick={()=>setEditing(null)}><X size={17}/></button></div>
        <label>App name<input id="shelf-app-name" required maxLength={80} value={editing.title} onChange={event=>setEditing({...editing,title:event.target.value})}/></label>
        <label><span id="shelf-app-description-label">Description</span><textarea aria-labelledby="shelf-app-description-label" maxLength={400} value={editing.description} onChange={event=>setEditing({...editing,description:event.target.value})}/></label>
        <p>Open the app to edit its data and fields. Ask Thaddeus in chat for layout or behavior changes.</p>
        <button className="primary" disabled={!online||busy||!editing.title.trim()}>Save changes</button>
      </form>}
      {apps.some(item=>item.archived===archived)?<div className="app-grid">{apps.filter(item=>item.archived===archived).map(item=><article className="app-card" key={item.id}>
        <button className="app-card-open" aria-label={'Open '+item.title} onClick={()=>onSelect(item.id)}><span className="app-card-icon"><Shapes size={23} strokeWidth={1.5}/></span><h2>{item.title}</h2><p>{item.description}</p><span className="app-card-count">{item.entryCount} {item.entryCount===1?'entry':'entries'} <ArrowUpRight size={17}/></span></button>
        <footer>{archived?<button disabled={!online||busy} aria-label={'Restore '+item.title} onClick={()=>void change(item.id,{version:item.version,archived:false})}><Undo2 size={14}/> Restore</button>:<>
          <button disabled={!online||busy} aria-label={'Edit '+item.title} onClick={()=>void editApp(item.id)}><Pencil size={14}/> Edit</button>
          <button disabled={!online||busy} aria-label={'Delete '+item.title} title="Move to Trash" onClick={()=>void change(item.id,{version:item.version,archived:true}).then(ok=>{if(ok&&editing?.app.id===item.id)setEditing(null);})}><Trash2 size={14}/> Delete</button>
        </>}</footer>
      </article>)}</div>:<div className="apps-empty"><Shapes size={30} strokeWidth={1.3}/><h2>{archived?'Trash is empty.':'What would make your day easier?'}</h2><p>{archived?'Deleted apps will appear here with a Restore button.':'Ask Thaddeus to build a tracker, checklist, or another small app. It will appear here, ready to use and update through chat.'}</p>{!archived&&<button onClick={()=>onBuild()}>Describe your app <ArrowUpRight size={16}/></button>}</div>}
    </>}
  </section>;
}

export function ArtifactPage({id,summary,online,chatVisible,onToggleChat,onClose,onChanged}:{id:string;summary?:AppSummary;online:boolean;chatVisible:boolean;onToggleChat:()=>void;onClose:()=>void;onChanged:()=>Promise<unknown>}){
  const [app,setApp]=useState<ArtifactApp|null>(null),[error,setError]=useState('');
  useEffect(()=>{
    let stale=false;setError('');
    api<ArtifactApp>('/artifacts/'+id).then(value=>{if(!stale)setApp(value);}).catch(reason=>{if(!stale)setError(reason.message);});
    return()=>{stale=true;};
  },[id,summary?.version]);
  const title=app?.id===id?app.definition.title:summary?.title||'App';
  return <section className="artifact-page" aria-label="Artifact page">
    <header className="artifact-page-header">
      <button type="button" aria-label={chatVisible?'Hide chat':'Show chat'} title={chatVisible?'Expand app to full page':'Show chat beside this app'} aria-expanded={chatVisible} disabled={!chatVisible&&!!(app?.archived||summary?.archived)} onClick={onToggleChat}><PanelLeft size={19} strokeWidth={1.6}/></button>
      <h1 title={title}>{title}</h1>
      <button type="button" aria-label="Close app" title="Close app" onClick={onClose}><X size={19}/></button>
    </header>
    <div className={'artifact-page-body'+(app?.id===id&&app.definition.page?' generated-body':'')}>
      {!online&&<p role="status" className="app-notice">Connection lost. Reconnect to save changes.</p>}
      {error?<p role="alert" className="error">{error}</p>:app?.id===id?<>
        {app.definition.page&&<ArtifactPreview key={app.id} app={app} online={online} onSaved={saved=>{setApp(current=>current?.id===saved.id?saved:current);void onChanged();}}/>}
        {app.definition.page?<details className="app-data-tools"><summary>Data & history</summary><AppDetail key={app.id} app={app} online={online} onChanged={async()=>{await onChanged();setApp(await api('/artifacts/'+app.id));}}/></details>:<AppDetail key={app.id} app={app} online={online} onChanged={async()=>{await onChanged();setApp(await api('/artifacts/'+app.id));}}/>}
      </>:<p role="status">Opening your app…</p>}
    </div>
  </section>;
}

function AppDetail({app,online,onChanged}:{app:ArtifactApp;online:boolean;onChanged:()=>Promise<void>}){
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[day,setDay]=useState(app.definition.dateField?localDay():''),[historyOpen,setHistoryOpen]=useState(false),[history,setHistory]=useState<AppRevision[]>([]);
  const [editing,setEditing]=useState<{version:string;entry:AppEntry}|null>(null),[design,setDesign]=useState<{version:string;definition:AppDefinition}|null>(null);
  const retry=useRef<{digest:string;id:string}|null>(null),working=useRef(false);
  useEffect(()=>{if(!historyOpen)return;let stale=false;api<AppRevision[]>('/artifacts/'+app.id+'/history').then(rows=>{if(!stale)setHistory(rows);}).catch(reason=>{if(!stale)setError(reason.message);});return()=>{stale=true;};},[app.id,app.version,historyOpen]);
  async function save(edit:Edit){
    if(working.current)return false;
    working.current=true;setBusy(true);setError('');
    const digest=JSON.stringify(edit);if(retry.current?.digest!==digest)retry.current={digest,id:uuid()};
    try{await api('/artifacts/'+app.id,{...edit,operationId:retry.current.id},'PUT');retry.current=null;await onChanged();return true;}
    catch(reason){setError((reason as Error).message);return false;}finally{working.current=false;setBusy(false);}
  }
  async function restore(version:string){
    if(working.current)return;working.current=true;setBusy(true);setError('');
    try{await api('/artifacts/'+app.id+'/restore',{operationId:uuid(),version:app.version,targetVersion:version});await onChanged();}
    catch(reason){setError((reason as Error).message);}finally{working.current=false;setBusy(false);}
  }
  const definition=app.definition,rows=app.entries.filter(entry=>!definition.dateField||!day||entry.values[definition.dateField]===day);
  const labelField=definition.fields.find(field=>field.kind==='text')||definition.fields[0];
  const choiceField=definition.fields.find(field=>field.kind==='select');
  const disabled=!online||busy||app.archived;
  function newEntry(){const values:AppEntry['values']={};for(const field of definition.fields)if(field.kind==='date')values[field.key]=day||localDay();else if(field.kind==='checkbox')values[field.key]=false;setEditing({version:app.version,entry:{id:'',values}});}
  return <div className="artifact-app" aria-label={definition.title}>
    {definition.description&&<p className="app-page-description">{definition.description}</p>}
    {app.archived&&<p className="app-notice">This app is in Trash. Its entries are still here. <button disabled={!online||busy} onClick={()=>void save({version:app.version,archived:false})}>Restore app</button></p>}
    <div className="app-toolbar"><button disabled={disabled} onClick={newEntry}><Plus size={16}/> Add entry</button><div className="app-toolbar-secondary"><button aria-label="Customize app" title="Customize app" disabled={disabled} onClick={()=>setDesign({version:app.version,definition:structuredClone(definition)})}><SlidersHorizontal size={16}/></button><button aria-expanded={historyOpen} onClick={()=>setHistoryOpen(!historyOpen)}><History size={16}/> History</button><a className="button" href={'/api/artifacts/'+app.id+'/export'} download aria-label="Export app"><Download size={16}/></a></div></div>
    {error&&<p className="error" role="alert">{error}</p>}
    {definition.dateField&&<div className="app-date-filter"><label>Showing<input type="date" aria-label="Filter by day" value={day} onChange={event=>setDay(event.target.value)}/></label><button aria-pressed={day===localDay()} onClick={()=>setDay(localDay())}>Today</button><button aria-pressed={!day} onClick={()=>setDay('')}>All entries</button></div>}
    {definition.summaries.length>0&&<div className="app-summaries">{definition.summaries.map(key=>{
      const field=definition.fields.find(item=>item.key===key)!;
      const numbers=rows.map(row=>row.values[key]).filter((value):value is number=>typeof value==='number');
      const sum=numbers.reduce((total,value)=>total+value,0),checked=rows.filter(row=>row.values[key]===true).length;
      return <section key={key} className="app-summary" aria-label={field.label+' summary'}><span>{field.label}</span><strong>{field.kind==='checkbox'?`${checked} / ${rows.length}`:numbers.length?sum.toLocaleString(undefined,{maximumFractionDigits:2}):'—'} <small>{field.unit}</small></strong>{field.kind==='checkbox'?<><progress aria-label={field.label+' progress'} value={checked} max={Math.max(rows.length,1)}/><small>Completed</small></>:<small>{numbers.length?`Average ${(sum/numbers.length).toLocaleString(undefined,{maximumFractionDigits:2})} · ${numbers.length} recorded`:'No amounts recorded'}{numbers.length<rows.length?` · ${rows.length-numbers.length} blank`:''}</small>}</section>;
    })}</div>}
    {choiceField&&rows.length>0&&<div className="app-distribution" aria-label={choiceField.label+' distribution'}>{choiceField.options!.map(option=>{const count=rows.filter(row=>row.values[choiceField.key]===option).length;return <div key={option}><span>{option}</span><progress aria-label={option+' count'} value={count} max={rows.length}/><b>{count}</b></div>;})}</div>}
    {editing&&<form className="app-entry-form" onSubmit={event=>{event.preventDefault();void save({version:editing.version,upserts:[editing.entry]}).then(ok=>{if(ok)setEditing(null);});}}><div className="app-form-heading"><h3>{editing.entry.id?'Edit entry':'New entry'}</h3><button type="button" aria-label="Close entry form" onClick={()=>setEditing(null)}><X size={17}/></button></div>{editing.version!==app.version&&<p role="status">The app changed while this form was open. Your draft is kept; reopen the entry before saving.</p>}<div className="app-field-grid">{definition.fields.map(field=><FieldInput key={field.key} field={field} value={editing.entry.values[field.key]} onChange={value=>setEditing({...editing,entry:{...editing.entry,values:{...editing.entry.values,[field.key]:value}}})}/>)}</div><button type="submit" className="primary" disabled={disabled||editing.version!==app.version}>Save entry <Check size={15}/></button></form>}
    {design&&<DefinitionEditor definition={design.definition} disabled={disabled||design.version!==app.version} onChange={definition=>setDesign({...design,definition})} onCancel={()=>setDesign(null)} onSave={()=>void save({version:design.version,definition:design.definition}).then(ok=>{if(ok)setDesign(null);})}/>}
    {!rows.length?<div className="app-no-entries"><p>{app.entries.length?'No entries for this day.':'A fresh page. Add an entry or tell Thaddeus what to record.'}</p></div>:<div className="app-table-wrap"><table><caption>{rows.length} {rows.length===1?'entry':'entries'}{day?' · '+day:''}</caption><thead><tr>{definition.fields.map(field=><th key={field.key}>{field.label}{field.unit&&<small>{field.unit}</small>}</th>)}<th><span className="composer-hint">Actions</span></th></tr></thead><tbody>{[...rows].reverse().map(entry=><tr key={entry.id}>{definition.fields.map(field=><td key={field.key}>{field.kind==='checkbox'?<input type="checkbox" aria-label={field.label+': '+String(entry.values[labelField.key]||'entry')} checked={entry.values[field.key]===true} disabled={disabled} onChange={event=>void save({version:app.version,upserts:[{...entry,values:{...entry.values,[field.key]:event.target.checked}}]})}/>:entry.values[field.key]==null||entry.values[field.key]===''?<span className="app-blank">—</span>:String(entry.values[field.key])}</td>)}<td className="app-row-actions"><button disabled={disabled} onClick={()=>setEditing({version:app.version,entry:structuredClone(entry)})}>Edit</button><button disabled={disabled} onClick={()=>void save({version:app.version,deleteIds:[entry.id]})}>Remove</button></td></tr>)}</tbody></table></div>}
    {historyOpen&&<section className="app-history" aria-label="App history"><div className="app-form-heading"><h3>History</h3>{history[1]&&<button disabled={!online||busy} onClick={()=>void restore(history[1].version)}><Undo2 size={15}/> Undo last change</button>}</div><p>Recent 20 versions. Restoring creates a new version and keeps the current one in history.</p>{history.map(revision=><div className="app-history-row" key={revision.id}><div><strong>{revision.description}</strong><small>{revision.source==='chat'?'Thaddeus':'You'} · {new Date(revision.at).toLocaleString()} · {revision.entryCount} entries</small></div>{revision.version===app.version?<span>Current</span>:<button disabled={!online||busy} onClick={()=>void restore(revision.version)}>Restore</button>}</div>)}</section>}
    {!app.archived&&<details className="app-manage"><summary>Manage app</summary><p>Delete moves the app to Trash and keeps its entries for Restore.</p><button disabled={disabled} onClick={()=>void save({version:app.version,archived:true})}>Delete app</button></details>}
  </div>;
}

function FieldInput({field,value,onChange}:{field:AppField;value:AppEntry['values'][string]|undefined;onChange:(value:AppEntry['values'][string])=>void}){
  if(field.kind==='checkbox')return <label className="app-checkbox"><input type="checkbox" checked={value===true} onChange={event=>onChange(event.target.checked)}/>{field.label}</label>;
  return <label>{field.label}{field.unit&&<small>{field.unit}</small>}{field.kind==='select'?<select aria-label={field.label} value={String(value??'')} onChange={event=>onChange(event.target.value||null)}><option value="">Choose…</option>{field.options!.map(option=><option key={option}>{option}</option>)}</select>:<input aria-label={field.label} type={field.kind==='number'?'number':field.kind==='date'?'date':'text'} step={field.kind==='number'?'any':undefined} maxLength={field.kind==='text'?1000:undefined} value={String(value??'')} onChange={event=>onChange(field.kind==='number'?(event.target.value===''?null:Number(event.target.value)):event.target.value||null)}/>}</label>;
}

function DefinitionEditor({definition,onChange,onSave,onCancel,disabled}:{definition:AppDefinition;onChange:(value:AppDefinition)=>void;onSave:()=>void;onCancel:()=>void;disabled:boolean}){
  function fieldChange(index:number,change:Partial<AppField>){
    const fields=definition.fields.map((field,i)=>i===index?{...field,...change}:field);
    onChange({...definition,fields,summaries:definition.summaries.filter(key=>fields.some(field=>field.key===key&&['number','checkbox'].includes(field.kind))),dateField:fields.some(field=>field.key===definition.dateField&&field.kind==='date')?definition.dateField:null});
  }
  return <form className="app-entry-form app-definition-form" onSubmit={event=>{event.preventDefault();onSave();}}><div className="app-form-heading"><h3>Customize your app</h3><button type="button" aria-label="Close app customization" onClick={onCancel}><X size={17}/></button></div><label>App name<input required maxLength={80} value={definition.title} onChange={event=>onChange({...definition,title:event.target.value})}/></label><label>Description<input maxLength={400} value={definition.description} onChange={event=>onChange({...definition,description:event.target.value})}/></label><p>Changes must fit your existing entries. You can also ask Thaddeus to customize this app.</p>{definition.fields.map((field,index)=><fieldset key={field.key}><legend>Field {index+1}</legend><label>Label<input required maxLength={60} value={field.label} onChange={event=>fieldChange(index,{label:event.target.value})}/></label><label>Type<select value={field.kind} onChange={event=>{const kind=event.target.value as AppField['kind'];fieldChange(index,{kind,options:kind==='select'?['First choice','Second choice']:[]});}}>{['text','number','date','checkbox','select'].map(kind=><option key={kind}>{kind}</option>)}</select></label>{field.kind==='select'&&<label>Choices (comma separated)<input value={field.options?.join(', ')||''} onChange={event=>fieldChange(index,{options:event.target.value.split(',').map(value=>value.trim())})}/></label>}{field.kind==='number'&&<label>Unit<input maxLength={20} value={field.unit||''} onChange={event=>fieldChange(index,{unit:event.target.value||null})}/></label>}{['number','checkbox'].includes(field.kind)&&<label className="app-checkbox"><input type="checkbox" checked={definition.summaries.includes(field.key)} onChange={event=>onChange({...definition,summaries:event.target.checked?[...definition.summaries,field.key]:definition.summaries.filter(key=>key!==field.key)})}/>Show summary</label>}</fieldset>)}<div className="app-customize-actions"><button type="button" disabled={definition.fields.length>=12} onClick={()=>{let key='field_'+(definition.fields.length+1);while(definition.fields.some(field=>field.key===key))key+='_';onChange({...definition,fields:[...definition.fields,{key,label:'New field',kind:'text'}]});}}><Plus size={14}/> Add field</button><label>Day filter<select value={definition.dateField||''} onChange={event=>onChange({...definition,dateField:event.target.value||null})}><option value="">All entries</option>{definition.fields.filter(field=>field.kind==='date').map(field=><option key={field.key} value={field.key}>{field.label}</option>)}</select></label></div><button className="primary" disabled={disabled}>Save app layout <ChevronRight size={15}/></button></form>;
}
