import {useEffect,useState} from 'react';
import {api} from '../api';

type ResearchData={contact:string};

/** Settings → Research data: the contact the SEC asks every requester for, so the employee can cite public competitors' revenue. */
export function ResearchDataSettings(){
  const [saved,setSaved]=useState<ResearchData|null>(null),[contact,setContact]=useState(''),[error,setError]=useState(''),[busy,setBusy]=useState(false),[done,setDone]=useState(false);
  useEffect(()=>{api<ResearchData>('/settings/research-data').then(value=>{setSaved(value);setContact(value.contact);}).catch(cause=>setError((cause as Error).message));},[]);
  async function save(){
    setBusy(true);setDone(false);
    try{const value=await api<ResearchData>('/settings/research-data',{contact},'PUT');setSaved(value);setContact(value.contact);setError('');setDone(true);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(!saved)return error?<p className="fe-alert">{error}</p>:null;
  return <form className="fe-form" aria-label="Research data" onSubmit={event=>{event.preventDefault();void save();}}>
    <p className="fe-muted">Industry size comes from the Bureau of Labor Statistics with no setup. For public competitors’ revenue, the SEC asks every requester to name themselves: add a name and an email, and it goes with each SEC request.</p>
    <label>Contact for SEC requests<input value={contact} onChange={event=>{setContact(event.target.value);setDone(false);}} placeholder="Acme Research ops@acme.com" maxLength={120}/></label>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer>{done&&<small className="fe-muted">{saved.contact?'Saved.':'Cleared; SEC figures are off.'}</small>}<button className="primary" disabled={busy||contact.trim()===saved.contact}>{busy?'Saving…':'Save'}</button></footer>
  </form>;
}
