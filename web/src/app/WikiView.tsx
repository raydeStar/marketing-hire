import {useEffect,useState} from 'react';
import {BookOpen,ChevronRight,Eye,Pencil,Plus,Search} from 'lucide-react';
import Markdown from 'react-markdown';
import {api} from '../api';
import {readableTime} from '../components/MarketingPanels';
import {wikiTemplates,type WikiTemplate} from './wikiTemplates';
import {Dialog,Empty,PageHead,useAttempt,type Directory} from './shared';

type WikiPage={id:string;version:number;scope:string;scopeId:string;title:string;body:string;kind:string;status:string;digest:string;author:string;createdAt:string;updatedAt:string};
type Form={scope:string;scopeId:string;title:string;body:string;kind:string;status:string};
const kindLabel:Record<string,string>={fact:'Fact',policy:'Playbook',hypothesis:'Hypothesis',question:'Open question'};
const statusLabel:Record<string,string>={draft:'Draft',active:'Published',archived:'Archived'};
const statusTone:Record<string,string>={draft:'attn',active:'ok',archived:''};

export function WikiView({directory,canEdit}:{directory:Directory;canEdit:boolean}){
  const [pages,setPages]=useState<WikiPage[]|null>(null),[selectedId,setSelectedId]=useState<string|null>(null);
  const [form,setForm]=useState<Form|null>(null),[preview,setPreview]=useState(false),[picking,setPicking]=useState(false);
  const [query,setQuery]=useState(''),[layer,setLayer]=useState('all'),[history,setHistory]=useState<WikiPage[]>([]);
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const attempt=useAttempt();
  async function refresh(){try{setPages(await api<WikiPage[]>('/company-wiki'));setError('');}catch(cause){setError((cause as Error).message);}}
  useEffect(()=>{void refresh();},[]);
  const selected=pages?.find(page=>page.id===selectedId);
  useEffect(()=>{if(!selectedId){setHistory([]);return;}void api<WikiPage[]>('/company-wiki/'+encodeURIComponent(selectedId)+'/history').then(setHistory).catch(()=>setHistory([]));},[selectedId,selected?.version]);
  const layerName=(page:{scope:string;scopeId:string})=>page.scope==='company'?'Everyone':page.scope==='department'?(directory.departments.find(item=>item.id===page.scopeId)?.name||'Department'):(page.scopeId==='ceo'?'CEO':directory.agents.find(item=>item.id===page.scopeId)?.name||'Member')+' only';
  const visible=(pages||[]).filter(page=>(layer==='all'||`${page.scope}:${page.scopeId}`===layer)&&`${page.title} ${page.body}`.toLowerCase().includes(query.toLowerCase().trim()))
    .sort((a,b)=>(a.status==='archived'?1:0)-(b.status==='archived'?1:0)||a.title.localeCompare(b.title));
  function create(template?:WikiTemplate){setPicking(false);setSelectedId(null);setPreview(false);setForm({scope:'company',scopeId:'company',title:template?.title||'',body:template?.body||'',kind:template?.kind||'policy',status:'draft'});}
  function edit(page:WikiPage){setPreview(false);setForm({scope:page.scope,scopeId:page.scopeId,title:page.title,body:page.body,kind:page.kind,status:page.status});}
  async function save(event:React.FormEvent){
    event.preventDefault();if(!form||busy)return;setBusy(true);setError('');
    const fields={id:selected?.id??null,version:selected?.version??0,...form};
    try{const page=await api<WikiPage>('/company-wiki',{...fields,requestId:attempt.id(JSON.stringify(fields))},'PUT');attempt.done();await refresh();setSelectedId(page.id);setForm(null);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  const layers=[{value:'company:company',label:'Everyone'},...directory.departments.map(item=>({value:'department:'+item.id,label:item.name+' department'})),...directory.agents.map(item=>({value:'member:'+item.id,label:item.name+' only'}))];
  return <div className="fe-page"><div className="fe-page-inner">
    <PageHead title="Wiki" subtitle="Your company’s shared memory: ethos, playbooks, facts and open questions.">{canEdit&&<button type="button" className="primary" onClick={()=>setPicking(true)}><Plus size={16}/> New page</button>}</PageHead>
    <div className="fe-toolbar"><label className="fe-search"><Search size={16}/><input aria-label="Search wiki" value={query} onChange={event=>setQuery(event.target.value)} placeholder="Search pages"/></label>
      <select aria-label="Filter by who can read" value={layer} onChange={event=>setLayer(event.target.value)}><option value="all">All pages</option>{layers.map(item=><option key={item.value} value={item.value}>{item.label}</option>)}</select></div>
    <div className="fe-split">
      <aside className="fe-row-list" aria-label="Wiki pages">{visible.map(page=><button type="button" key={page.id} className="fe-row" aria-pressed={page.id===selectedId&&!form} onClick={()=>{setSelectedId(page.id);setForm(null);}}>
        <span className="fe-row-icon"><BookOpen size={17}/></span><span className="fe-row-body"><strong>{page.title}</strong><small>{kindLabel[page.kind]||page.kind} · {statusLabel[page.status]} · {layerName(page)}</small></span></button>)}
        {pages&&!visible.length&&<p className="fe-muted fe-files-empty">{pages.length?'No pages match.':'No pages yet. Start with your company ethos.'}</p>}
      </aside>
      {form?<form className="fe-reader fe-form" onSubmit={event=>void save(event)} aria-label="Edit wiki page">
        <div className="fe-reader-head"><h3>{selected?'Edit page':'New page'}</h3><div className="fe-segmented"><button type="button" aria-pressed={!preview} onClick={()=>setPreview(false)}><Pencil size={14}/> Write</button><button type="button" aria-pressed={preview} onClick={()=>setPreview(true)}><Eye size={14}/> Preview</button></div></div>
        <label>Title<input required maxLength={160} value={form.title} onChange={event=>setForm({...form,title:event.target.value})}/></label>
        {preview?<div className="fe-prose fe-wiki-preview"><Markdown components={{img:()=>null}}>{form.body||'*Nothing written yet*'}</Markdown></div>
          :<label>Content<textarea className="fe-editor" required maxLength={12000} value={form.body} onChange={event=>setForm({...form,body:event.target.value})} placeholder="Markdown. Say what’s known, what’s uncertain, and where facts come from."/></label>}
        <div className="fe-form-row">
          <label>Who can read it<select disabled={!!selected} value={form.scope+':'+form.scopeId} onChange={event=>{const [scope,...rest]=event.target.value.split(':');setForm({...form,scope,scopeId:rest.join(':')});}}>{layers.map(item=><option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
          <label>Type<select value={form.kind} onChange={event=>setForm({...form,kind:event.target.value})}>{Object.entries(kindLabel).map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></label>
          <label>Status<select value={form.status} onChange={event=>setForm({...form,status:event.target.value})}><option value="draft">Draft (only you)</option><option value="active">Published</option><option value="archived">Archived</option></select></label>
        </div>
        {error&&<p className="fe-alert" role="alert">{error}</p>}
        <footer><button type="button" className="fe-ghost" onClick={()=>setForm(null)}>Cancel</button><button className="primary" disabled={busy||!canEdit||!form.title.trim()||!form.body.trim()}>{busy?'Saving…':'Save page'}</button></footer>
      </form>:selected?<article className="fe-reader">
        <div className="fe-reader-head"><div><h2>{selected.title}</h2><div className="fe-reader-meta"><span className={'fe-pill '+statusTone[selected.status]}>{statusLabel[selected.status]}</span><span className="fe-pill">{kindLabel[selected.kind]||selected.kind}</span><span className="fe-pill">{layerName(selected)}</span><small>v{selected.version} · updated {readableTime(selected.updatedAt)}</small></div></div>
          {canEdit&&<button type="button" onClick={()=>edit(selected)}><Pencil size={14}/> Edit</button>}</div>
        <div className="fe-prose"><Markdown components={{img:()=>null}}>{selected.body}</Markdown></div>
        {history.length>1&&<details className="fe-history"><summary>{history.length} versions</summary>{history.map(item=><details key={item.version} className="fe-history-row"><summary>Version {item.version} · {statusLabel[item.status]} · {readableTime(item.updatedAt)}</summary><div className="fe-prose"><Markdown components={{img:()=>null}}>{item.body}</Markdown></div></details>)}</details>}
      </article>:<div className="fe-reader"><Empty icon={<BookOpen size={30}/>} title="Everything your team should know">Published pages are shared with your team’s meetings. Marketing’s direct chat will read them once agent setup connects the wiki.</Empty></div>}
    </div>
    {error&&!form&&<p className="fe-alert" role="alert">{error}</p>}
    {picking&&<Dialog title="New wiki page" onClose={()=>setPicking(false)}><div className="fe-row-list">
      <button type="button" className="fe-row" onClick={()=>create()}><span className="fe-row-icon"><Plus size={17}/></span><span className="fe-row-body"><strong>Blank page</strong><small>Start from scratch</small></span><ChevronRight size={16}/></button>
      {wikiTemplates.map(template=><button type="button" className="fe-row" key={template.title} onClick={()=>create(template)}><span className="fe-row-icon accent"><BookOpen size={17}/></span><span className="fe-row-body"><strong>{template.title}</strong><small>{template.summary}</small></span><ChevronRight size={16}/></button>)}
    </div></Dialog>}
  </div></div>;
}
