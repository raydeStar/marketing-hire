import {useEffect,useState} from 'react';
import {api} from '../api';

type ResearchData={contact:string;census:boolean};

/** Settings → Research data: the contact the SEC asks every requester for, and a free Census key for businesses with no
 * employees. The key goes to the system's credential store and is never shown again. */
export function ResearchDataSettings(){
  const [saved,setSaved]=useState<ResearchData|null>(null),[contact,setContact]=useState(''),[key,setKey]=useState(''),[error,setError]=useState(''),[busy,setBusy]=useState(false),[done,setDone]=useState('');
  useEffect(()=>{api<ResearchData>('/settings/research-data').then(value=>{setSaved(value);setContact(value.contact);}).catch(cause=>setError((cause as Error).message));},[]);
  async function save(body:object,message:string){
    setBusy(true);setDone('');
    try{const value=await api<ResearchData>('/settings/research-data',body,'PUT');setSaved(value);setContact(value.contact);setKey('');setError('');setDone(message);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(!saved)return error?<p className="fe-alert">{error}</p>:null;
  const changed=contact.trim()!==saved.contact||key.trim().length>0;
  return <form className="fe-form" aria-label="Research data" onSubmit={event=>{event.preventDefault();void save({contact,censusKey:key.trim()||null},contact.trim()||saved.contact?'Saved.':'Cleared; SEC figures are off.');}}>
    <p className="fe-muted">Industry size and small businesses by employee count come from the Bureau of Labor Statistics with no setup. For public competitors’ revenue, the SEC asks every requester to name themselves: add a name and an email, and it goes with each SEC request.</p>
    <label>Contact for SEC requests<input value={contact} onChange={event=>{setContact(event.target.value);setDone('');}} placeholder="Acme Research ops@acme.com" maxLength={120}/></label>
    <label>Census API key <span className="fe-muted">(free, for businesses with no employees: <a href="https://api.census.gov/data/key_signup.html" target="_blank" rel="noopener noreferrer">get one</a>)</span>
      <input type="password" autoComplete="off" value={key} onChange={event=>{setKey(event.target.value);setDone('');}} placeholder={saved.census?'Saved in your credential store':'Paste the key'} maxLength={64}/></label>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer>{done&&<small className="fe-muted" role="status">{done}</small>}
      {saved.census&&<button type="button" className="fe-ghost" disabled={busy} onClick={()=>void save({contact:null,forgetCensus:true},'Census key removed.')}>Remove the Census key</button>}
      <button className="primary" disabled={busy||!changed}>{busy?'Saving…':'Save'}</button></footer>
  </form>;
}
