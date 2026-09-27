import {useEffect,useState} from 'react';
import {Check} from 'lucide-react';
import {api} from '../api';
import type {MarketingDraft} from '../components/MarketingPanels';
import {openComposer} from './PublishingView';

type Route={action:'schedule'|'draft'|'copy';label:string;connectionId?:string|null;at?:string|null;why?:string|null};
const when=(at:string)=>new Date(at).toLocaleString(undefined,{weekday:'short',hour:'numeric',minute:'2-digit'});
const id=()=>globalThis.crypto?.randomUUID?.()||Math.random().toString(36).slice(2)+Date.now().toString(36);

/** Approve and, in the same tap, schedule it on the connected channel at its suggested time, save it as a draft in a drafts-only
 * service, or open the network's composer with the text copied. */
export function OneTap({draft,onDone}:{draft:MarketingDraft;onDone:(message:string)=>void}){
  const [route,setRoute]=useState<Route|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
  useEffect(()=>{let disposed=false;void api<Route>(`/publishing/drafts/${draft.id}/one-tap`).then(next=>{if(!disposed)setRoute(next);}).catch(()=>{if(!disposed)setRoute(null);});return()=>{disposed=true;};},[draft.id,draft.revision]);
  if(!route||draft.status!=='pending')return null;
  const label=route.action==='schedule'&&route.at?`${route.label} · ${when(route.at)}`:route.label;
  async function tap(){
    if(busy||!route)return;
    // The composer opens inside the click, before anything is awaited, so the browser doesn't block it.
    if(route.action==='copy')openComposer(draft,null);
    setBusy(true);setError('');
    try{
      await api(`/marketing/drafts/${draft.id}/decision`,{requestId:id(),decision:'approved',revision:draft.revision,digest:draft.digest});
      await api(`/publishing/drafts/${draft.id}/one-tap`,{requestId:id(),digest:draft.digest});
      onDone(route.action==='schedule'&&route.at?`Approved and scheduled for ${when(route.at)}.${route.why?' '+route.why:''}`:route.action==='draft'?`Approved and saved as a draft. ${route.why||''}`.trim():'Approved. The text is copied and the composer is open: post it, then paste its link on the Content calendar.');
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <span className="fe-one-tap">
    <button type="button" disabled={busy} title={route.why||undefined} onClick={()=>void tap()}><Check size={13}/> {busy?'Working…':label}</button>
    {error&&<small className="fe-alert" role="alert">{error}</small>}
  </span>;
}
