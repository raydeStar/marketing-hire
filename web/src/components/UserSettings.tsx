import {useEffect,useState} from 'react';
import {Save,UserRound} from 'lucide-react';
import {api} from '../api';

type UserProfile={path:string;content:string;version:string;updated:string};
type View={user:UserProfile;history:UserProfile[];filePath:string};

export function UserSettings({online,revision,onChanged}:{online:boolean;revision?:string;onChanged:()=>Promise<unknown>}){
  const [view,setView]=useState<View|null>(null);
  const [content,setContent]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  useEffect(()=>{
    let stale=false;
    api<View>('/settings/user').then(result=>{if(!stale){setView(result);setContent(result.user.content);setError('');}})
      .catch(error=>{if(!stale)setError((error as Error).message);});
    return()=>{stale=true;};
  },[revision]);
  const dirty=!!view&&content!==view.user.content;
  async function save(){
    if(!view||!dirty)return;
    setBusy(true);setError('');setNotice('');
    try{
      const result=await api<View>('/settings/user',{content,version:view.user.version},'PUT');
      setView(result);setContent(result.user.content);setNotice('User profile saved. New conversations and newly prepared work will use this version.');
      await onChanged();
    }catch(error){setError((error as Error).message);}finally{setBusy(false);}
  }
  return <section className="soul-settings" aria-labelledby="user-heading">
    <div className="connection-heading"><div><h2 id="user-heading">User</h2><p>The durable facts and preferences Thaddeus may use to help you live in one readable file.</p></div><UserRound size={21}/></div>
    {!view?<p>{error||'Opening the User profile…'}</p>:<>
      <label htmlFor="user-editor">USER.md</label>
      <textarea id="user-editor" className="soul-editor" value={content} maxLength={20000} disabled={busy||!online} onChange={event=>{setContent(event.target.value);setNotice('');}}/>
      <div className="soul-meta"><span>{content.length.toLocaleString()} / 20,000 characters</span><span>{view.history.length} saved revision{view.history.length===1?'':'s'}</span></div>
      <div className="connection-actions"><button className="primary" disabled={!dirty||busy||!online||!content.trim()} onClick={save}><Save size={16}/>{busy?'Saving…':'Save User'}</button><button disabled={!dirty||busy} onClick={()=>setContent(view.user.content)}>Discard edits</button></div>
      {notice&&<p className="connection-notice" role="status">{notice}</p>}
      {error&&<p className="connection-error" role="alert">{error}</p>}
      <p className="muted">Conversation may propose useful durable updates as it learns about you. Every proposal shows the exact before and after text and requires approval. Thaddeus will not silently infer sensitive traits or save secrets.</p>
      <details className="settings-secondary"><summary>Local file</summary><p><code>{view.filePath}</code></p><p className="muted">You may edit this file directly while Thaddeus is stopped. Settings provides version checks and revision history.</p></details>
    </>}
  </section>;
}
