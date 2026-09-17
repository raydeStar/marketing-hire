import {useState} from 'react';
import {api} from '../api';
import type {MemoryEntry,MemoryView,Page} from '../types';

export function MemoryNotebook({memories,pages,online,onChanged,onOpen}:{memories:MemoryView[];pages:Page[];online:boolean;onChanged:()=>Promise<unknown>;onOpen:(path:string)=>void}){
  const [editing,setEditing]=useState<MemoryEntry|null>(null),[statement,setStatement]=useState(''),[quote,setQuote]=useState(''),[sourcePath,setSourcePath]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const source=pages.find(page=>page.path===(sourcePath||pages[0]?.path));
  function reset(){setEditing(null);setStatement('');setQuote('');setError('');}
  function correct(entry:MemoryEntry){setEditing(entry);setStatement(entry.statement);setQuote(entry.source?.quote||'');setSourcePath(entry.source?.path||'');setError('');}
  async function save(){
    if(!source)return;setBusy(true);setError('');
    try{
      await api('/memories/'+(editing?.id||crypto.randomUUID().replaceAll('-','')),{statement,source:{path:source.path,version:source.version,quote},version:editing?.version||'absent'},'PUT');
      reset();await onChanged();
    }catch(error){setError((error as Error).message);}finally{setBusy(false);}
  }
  async function forget(entry:MemoryEntry){
    setBusy(true);setError('');
    try{await api('/memories/'+entry.id+'/forget',{version:entry.version});if(editing?.id===entry.id)reset();await onChanged();}
    catch(error){setError((error as Error).message);}finally{setBusy(false);}
  }
  return <details className="memory-notebook" aria-label="Remembered context"><summary>Remembered context · {memories.length} {memories.length===1?'entry':'entries'}</summary>
    <p>Save only what you choose to remember, with a quotation from a saved note. One main memory note is a good starting point; branch into focused topic notes when a subject becomes deep enough to deserve its own page.</p>
    <p className="muted">Select remembered entries in Research when you want to use them. A remembered statement is not an independently verified fact.</p>
    <p className="muted">Changes block the next model request, task continuation or import using the old version. Already-sent context, original notes, exports and prior task receipts remain.</p>
    {!memories.length&&<p>No remembered entries yet.</p>}
    {memories.map(({entry,sourceStatus})=><article className="memory-entry" data-memory-id={entry.id} key={entry.id}>
      <p>{entry.statement}</p><p className={sourceStatus==='current'?'muted':'error'}>{sourceStatus==='current'?'Source version unchanged':'Source changed or missing · review before reuse'}</p>
      <button className="text-button" onClick={()=>onOpen(entry.source!.path)}>{entry.source!.path}</button>
      <blockquote>{entry.source!.quote}</blockquote>
      <button disabled={busy||!online} onClick={()=>correct(entry)}>Correct or review entry</button>
      <button disabled={busy||!online} onClick={()=>forget(entry)}>Forget entry</button>
    </article>)}
    <section aria-label="Remembered entry editor"><h2>{editing?'Review this remembered entry':'Remember something from a note'}</h2>
      <label>Remembered statement<textarea value={statement} onChange={event=>setStatement(event.target.value)} maxLength={1000}/></label>
      <label>Source note<select aria-label="Source note" value={source?.path||''} onChange={event=>{setSourcePath(event.target.value);setQuote('');}}>
        {!source&&<option value="">Choose a saved note</option>}{pages.map(page=><option key={page.path} value={page.path}>{page.path}</option>)}
      </select></label>
      {source&&<details><summary>Read the current source note</summary><pre>{source.content}</pre></details>}
      <label>Exact source quotation<textarea value={quote} onChange={event=>setQuote(event.target.value)} maxLength={2000}/></label>
      <button disabled={busy||!online||!statement.trim()||!quote.trim()||!source} onClick={save}>{editing?'Save reviewed correction':'Remember this statement'}</button>
      {editing&&<button disabled={busy} onClick={reset}>Cancel memory edit</button>}
    </section>{error&&<p className="error" role="alert">{error}</p>}
  </details>;
}
