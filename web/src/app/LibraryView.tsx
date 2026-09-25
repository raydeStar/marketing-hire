import {useMemo,useRef,useState,type ReactNode} from 'react';
import {BookOpen,ChevronDown,ChevronRight,Clock,FileText,Folder,FolderOpen,FolderPlus,Image as ImageIcon,LayoutTemplate,Library as LibraryIcon,Link2,NotebookPen,Pencil,Pin,Plus,Search,Table2,Tag,Trash2,Upload,X,type LucideIcon} from 'lucide-react';
import {uploadFile} from '../api';
import {readableTime} from '../components/MarketingPanels';
import {folderTree,homeFolders,leafOf,parentOf,searchLibrary,within,type Library,type LibraryItem,type LibraryKind} from './library';
import {NewPageDialog} from './Pages';
import {wikiTemplates} from './wikiTemplates';
import {Dialog} from './shared';

export const kindIcon:Record<LibraryKind,LucideIcon>={brief:NotebookPen,wiki:BookOpen,page:LayoutTemplate,tool:Table2,media:ImageIcon,source:Link2,deliverable:FileText};
type Scope={kind:'all'}|{kind:'recent'}|{kind:'pinned'}|{kind:'trash'}|{kind:'folder';path:string}|{kind:'tag';tag:string};

function FolderDialog({title,initial,parent,onSave,onClose}:{title:string;initial:string;parent:string;onSave:(name:string)=>Promise<void>;onClose:()=>void}){
  const [name,setName]=useState(initial),[busy,setBusy]=useState(false),[error,setError]=useState('');
  return <Dialog title={title} onClose={onClose}><form className="fe-form" onSubmit={async event=>{event.preventDefault();const value=name.trim().replaceAll('/','-');if(!value||busy)return;setBusy(true);setError('');try{await onSave(value);onClose();}catch(cause){setError((cause as Error).message);setBusy(false);}}}>
    <label>Folder name<input autoFocus required maxLength={60} value={name} onChange={event=>setName(event.target.value)}/></label>
    {parent&&<small>Inside {parent.replaceAll('/',' / ')}</small>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={busy||!name.trim()}>{busy?'Saving…':'Save'}</button></footer>
  </form></Dialog>;
}

function TemplateDialog({onChoose,onClose}:{onChoose:(key:string)=>void;onClose:()=>void}){
  return <Dialog title="New document" onClose={onClose}><div className="fe-row-list">
    <button type="button" className="fe-row" onClick={()=>onChoose('wiki:new')}><span className="fe-row-icon"><Plus size={16}/></span><span className="fe-row-body"><strong>Blank document</strong><small>Start from scratch in Markdown</small></span><ChevronRight size={16}/></button>
    {wikiTemplates.map(template=><button type="button" className="fe-row" key={template.title} onClick={()=>onChoose('wiki:new:'+template.title)}><span className="fe-row-icon"><BookOpen size={16}/></span><span className="fe-row-body"><strong>{template.title}</strong><small>{template.summary}</small></span><ChevronRight size={16}/></button>)}
  </div></Dialog>;
}

/** The Library: company knowledge, research, campaign output, pages, apps and media, filed like a wiki. */
export function LibraryView({library,canEdit,online,openKey,reader,onOpen}:{library:Library;canEdit:boolean;online:boolean;openKey:string|null;reader:ReactNode;onOpen:(key:string|null,folder?:string)=>void}){
  const [scope,setScope]=useState<Scope>({kind:'all'}),[query,setQuery]=useState('');
  const [collapsed,setCollapsed]=useState<Set<string>>(new Set()),[menu,setMenu]=useState(false);
  const [dialog,setDialog]=useState<null|'folder'|'rename'|'template'|'page'>(null),[error,setError]=useState(''),[uploading,setUploading]=useState(0);
  const picker=useRef<HTMLInputElement>(null);
  const items=library.items;
  const live=items.filter(item=>!item.archived);
  const folders=useMemo(()=>folderTree(library.meta,library.items),[library.meta,library.items]);
  const tags=useMemo(()=>[...new Set(live.flatMap(item=>item.tags))].sort(),[live]);
  const currentFolder=scope.kind==='folder'?scope.path:'';
  const custom=scope.kind==='folder'&&!homeFolders.includes(scope.path as typeof homeFolders[number]);

  const hits=query.trim()?searchLibrary(scope.kind==='trash'?items.filter(item=>item.archived):live,query):null;
  const listed:LibraryItem[]=hits?hits.map(hit=>hit.item):(scope.kind==='trash'?items.filter(item=>item.archived)
    :scope.kind==='pinned'?live.filter(item=>library.meta.pins.includes(item.key))
    :scope.kind==='recent'?[...live].filter(item=>item.updated).sort((a,b)=>b.updated-a.updated).slice(0,25)
    :scope.kind==='folder'?live.filter(item=>within(item.folder,scope.path))
    :scope.kind==='tag'?live.filter(item=>item.tags.includes(scope.tag)):live).slice().sort((a,b)=>scope.kind==='recent'?0:a.folder.localeCompare(b.folder)||a.title.localeCompare(b.title));
  const snippets=new Map(hits?.map(hit=>[hit.item.key,hit.snippet]));
  const children=(path:string)=>folders.filter(folder=>parentOf(folder)===path);
  const count=(path:string)=>live.filter(item=>within(item.folder,path)).length;

  async function upload(files:FileList|null){
    if(!files?.length)return;setError('');
    for(const file of Array.from(files)){
      setUploading(value=>value+1);
      try{const saved=await uploadFile(file) as {id?:string};await library.reload();if(saved?.id&&currentFolder&&currentFolder!=='Media')await library.file('media:'+saved.id,currentFolder,[]);}
      catch(cause){setError(`${file.name}: ${(cause as Error).message}`);}finally{setUploading(value=>value-1);}
    }
  }
  async function removeFolder(path:string){
    if(!window.confirm(`Delete the folder “${leafOf(path)}”? Items in it return to their default folder. Nothing is deleted.`))return;
    try{await library.folders(library.meta.folders.filter(folder=>!within(folder,path)),[{from:path,to:null}]);setScope({kind:'all'});}catch(cause){setError((cause as Error).message);}
  }
  const title=scope.kind==='all'?'All items':scope.kind==='recent'?'Recent':scope.kind==='pinned'?'Pinned':scope.kind==='trash'?'Trash':scope.kind==='tag'?'#'+scope.tag:leafOf(scope.path);

  const tree=(path:string,depth:number):ReactNode=>children(path).map(folder=>{
    const open=!collapsed.has(folder),kids=children(folder).length>0,active=scope.kind==='folder'&&scope.path===folder;
    return <div key={folder} role="treeitem" aria-expanded={kids?open:undefined} aria-selected={active}>
      <div className={'fe-tree-row'+(active?' active':'')} style={{paddingLeft:8+depth*14}}>
        {kids?<button type="button" className="fe-tree-toggle" aria-label={(open?'Collapse ':'Expand ')+leafOf(folder)} onClick={()=>setCollapsed(current=>{const next=new Set(current);if(next.has(folder))next.delete(folder);else next.add(folder);return next;})}>{open?<ChevronDown size={13}/>:<ChevronRight size={13}/>}</button>:<span className="fe-tree-toggle"/>}
        <button type="button" className="fe-tree-label" onClick={()=>{setScope({kind:'folder',path:folder});setQuery('');onOpen(null);}}>{active?<FolderOpen size={15}/>:<Folder size={15}/>}<span>{leafOf(folder)}</span><small>{count(folder)||''}</small></button>
      </div>
      {kids&&open&&<div role="group">{tree(folder,depth+1)}</div>}
    </div>;
  });
  const quick=(target:Scope,label:string,Icon:LucideIcon,total?:number)=><button type="button" className={'fe-tree-row fe-tree-quick'+(scope.kind===target.kind?' active':'')} onClick={()=>{setScope(target);onOpen(null);}}><Icon size={15}/><span>{label}</span>{total!==undefined&&<small>{total||''}</small>}</button>;

  return <div className="fe-library">
    <nav className="fe-library-nav" aria-label="Library">
      <label className="fe-search"><Search size={15}/><input aria-label="Search the library" value={query} placeholder="Search the library" onChange={event=>{setQuery(event.target.value);onOpen(null);}}/>{query&&<button type="button" className="fe-inline-button" aria-label="Clear search" onClick={()=>setQuery('')}><X size={13}/></button>}</label>
      <div className="fe-tree-group">
        {quick({kind:'all'},'All items',LibraryIcon,live.length)}
        {quick({kind:'recent'},'Recent',Clock)}
        {quick({kind:'pinned'},'Pinned',Pin,live.filter(item=>library.meta.pins.includes(item.key)).length)}
      </div>
      <div className="fe-tree-heading"><span>Folders</span>{canEdit&&<button type="button" className="fe-inline-button" aria-label="New folder" title="New folder" onClick={()=>setDialog('folder')}><FolderPlus size={14}/></button>}</div>
      <div role="tree" aria-label="Folders" className="fe-tree">{tree('',0)}</div>
      {tags.length>0&&<><div className="fe-tree-heading"><span>Tags</span></div><div className="fe-tag-cloud">{tags.map(tag=><button type="button" key={tag} className={'fe-tag'+(scope.kind==='tag'&&scope.tag===tag?' active':'')} onClick={()=>{setScope({kind:'tag',tag});onOpen(null);}}>{tag}</button>)}</div></>}
      <div className="fe-tree-group fe-tree-foot">{quick({kind:'trash'},'Trash',Trash2,items.filter(item=>item.archived).length)}</div>
    </nav>
    <div className="fe-library-main">
      {openKey?reader:<>
        <header className="fe-library-head">
          <div className="fe-breadcrumb"><button type="button" onClick={()=>setScope({kind:'all'})}>Library</button>{scope.kind==='folder'?scope.path.split('/').map((part,index,all)=>{const path=all.slice(0,index+1).join('/');return <span key={path}><ChevronRight size={13}/><button type="button" onClick={()=>setScope({kind:'folder',path})}>{part}</button></span>;}):<span><ChevronRight size={13}/>{title}</span>}</div>
          <h1>{hits?`Results for “${query.trim()}”`:title}</h1>
          <div className="fe-library-actions">
            {custom&&canEdit&&<><button type="button" className="fe-ghost" onClick={()=>setDialog('rename')}><Pencil size={14}/> Rename</button><button type="button" className="fe-ghost" onClick={()=>void removeFolder(currentFolder)}><Trash2 size={14}/> Delete folder</button></>}
            {canEdit&&<div className="fe-menu-anchor"><button type="button" className="primary" aria-haspopup="menu" aria-expanded={menu} disabled={!online} onClick={()=>setMenu(!menu)}><Plus size={15}/> New <ChevronDown size={14}/></button>
              {menu&&<div className="fe-menu" role="menu" onClick={()=>setMenu(false)}>
                <button type="button" role="menuitem" onClick={()=>setDialog('template')}><BookOpen size={15}/> Document</button>
                <button type="button" role="menuitem" onClick={()=>setDialog('page')}><LayoutTemplate size={15}/> Page or app</button>
                <button type="button" role="menuitem" onClick={()=>picker.current?.click()}><Upload size={15}/> Upload files</button>
                <button type="button" role="menuitem" onClick={()=>setDialog('folder')}><FolderPlus size={15}/> Folder</button>
              </div>}</div>}
            <input ref={picker} type="file" multiple hidden accept=".png,.jpg,.jpeg,.webp,.gif,.mp4,.webm,.txt,.md,.csv,.json" onChange={event=>{void upload(event.target.files);event.target.value='';}}/>
          </div>
        </header>
        {uploading>0&&<p className="fe-notice" role="status">Uploading {uploading} file{uploading===1?'':'s'}…</p>}
        {(error||library.error)&&<p className="fe-alert" role="alert">{error||library.error}</p>}
        {listed.length?<div className="fe-table-wrap"><table className="fe-table fe-library-table"><thead><tr><th>Name</th><th>Type</th>{scope.kind!=='folder'&&<th>Folder</th>}<th>Tags</th><th>Updated</th></tr></thead><tbody>
          {listed.map(item=>{const Icon=kindIcon[item.kind];return <tr key={item.key} tabIndex={0} onClick={()=>onOpen(item.key)} onKeyDown={event=>{if(event.key==='Enter')onOpen(item.key);}}>
            <td><span className="fe-cell-name"><Icon size={16}/><span><strong>{item.title}</strong><small>{snippets.get(item.key)||item.summary}</small></span>{library.meta.pins.includes(item.key)&&<Pin size={12} aria-label="Pinned"/>}</span></td>
            <td>{item.label}</td>
            {scope.kind!=='folder'&&<td className="fe-cell-muted">{item.folder.replaceAll('/',' / ')}</td>}
            <td>{item.tags.map(tag=><span className="fe-tag" key={tag}>{tag}</span>)}</td>
            <td className="fe-cell-muted">{item.updated?readableTime(item.updated):'—'}</td></tr>;})}
        </tbody></table></div>
          :library.loaded&&<div className="fe-empty-state"><Tag size={22}/><strong>{hits?'Nothing matches that search':scope.kind==='trash'?'Trash is empty':'Nothing here yet'}</strong><p>{hits?'Try fewer or different words. Search also matches related terms, like “customer” for “audience”.':scope.kind==='folder'?'Create a document, page or upload here, or file existing items into this folder.':'Documents, pages, apps, media and research appear here as you and Marketing work.'}</p></div>}
      </>}
    </div>
    {dialog==='folder'&&<FolderDialog title="New folder" initial="" parent={currentFolder} onClose={()=>setDialog(null)} onSave={async name=>{const path=(currentFolder?currentFolder+'/':'')+name;await library.folders([...library.meta.folders,path]);setScope({kind:'folder',path});}}/>}
    {dialog==='rename'&&custom&&<FolderDialog title="Rename folder" initial={leafOf(currentFolder)} parent={parentOf(currentFolder)} onClose={()=>setDialog(null)} onSave={async name=>{
      const next=(parentOf(currentFolder)?parentOf(currentFolder)+'/':'')+name;
      await library.folders(library.meta.folders.map(folder=>within(folder,currentFolder)?next+folder.slice(currentFolder.length):folder),[{from:currentFolder,to:next}]);setScope({kind:'folder',path:next});}}/>}
    {dialog==='template'&&<TemplateDialog onClose={()=>setDialog(null)} onChoose={key=>{setDialog(null);onOpen(key,currentFolder||undefined);}}/>}
    {dialog==='page'&&<NewPageDialog onClose={()=>setDialog(null)} onCreated={id=>{setDialog(null);void library.reload().then(async()=>{if(currentFolder&&currentFolder!=='Pages & apps')await library.file('page:'+id,currentFolder,[]).catch(()=>{});onOpen('page:'+id);});}}/>}
  </div>;
}
