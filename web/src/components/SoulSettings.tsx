import {useEffect,useState} from 'react';
import {Feather,Save} from 'lucide-react';
import {api} from '../api';

type Soul={path:string;content:string;version:string;updated:string};
type View={soul:Soul;history:Soul[];filePath:string};

export function SoulSettings({online,revision,onChanged}:{online:boolean;revision?:string;onChanged:()=>Promise<unknown>}){
  const [view,setView]=useState<View|null>(null);
  const [content,setContent]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  useEffect(()=>{
    let stale=false;
    api<View>('/settings/soul').then(result=>{if(!stale){setView(result);setContent(result.soul.content);setError('');}})
      .catch(error=>{if(!stale)setError((error as Error).message);});
    return()=>{stale=true;};
  },[revision]);
  const dirty=!!view&&content!==view.soul.content;
  async function save(){
    if(!view||!dirty)return;
    setBusy(true);setError('');setNotice('');
    try{
      const result=await api<View>('/settings/soul',{content,version:view.soul.version},'PUT');
      setView(result);setContent(result.soul.content);setNotice('Soul saved. New conversations and newly prepared work will use this version.');
      await onChanged();
    }catch(error){setError((error as Error).message);}finally{setBusy(false);}
  }
  return <section className="soul-settings" aria-labelledby="soul-heading">
    <div className="connection-heading"><div><h2 id="soul-heading">Soul</h2><p>Thaddeus’s voice, demeanor, and character live in one readable file.</p></div><Feather size={21}/></div>
    {!view?<p>{error||'Opening the Soul…'}</p>:<>
      <label htmlFor="soul-editor">SOUL.md</label>
      <textarea id="soul-editor" className="soul-editor" value={content} maxLength={20000} disabled={busy||!online} onChange={event=>{setContent(event.target.value);setNotice('');}}/>
      <div className="soul-meta"><span>{content.length.toLocaleString()} / 20,000 characters</span><span>{view.history.length} saved revision{view.history.length===1?'':'s'}</span></div>
      <div className="connection-actions"><button className="primary" disabled={!dirty||busy||!online||!content.trim()} onClick={save}><Save size={16}/>{busy?'Saving…':'Save Soul'}</button><button disabled={!dirty||busy} onClick={()=>setContent(view.soul.content)}>Discard edits</button></div>
      {notice&&<p className="connection-notice" role="status">{notice}</p>}
      {error&&<p className="connection-error" role="alert">{error}</p>}
      <p className="muted">Chat can also propose a Soul change. Those proposals show exact before and after text and require approval. Personality never grants tools or permissions.</p>
      <details className="settings-secondary"><summary>Local file</summary><p><code>{view.filePath}</code></p><p className="muted">You may edit this file directly while Thaddeus is stopped. Settings provides version checks and revision history.</p></details>
    </>}
  </section>;
}
