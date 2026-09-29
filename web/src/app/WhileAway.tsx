import {useEffect,useState} from 'react';
import {ExternalLink,MessageCircleQuestion,Tag,TrendingUp} from 'lucide-react';
import {api} from '../api';

type AwayAction={label:string;kind:'open'|'assign';key?:string|null;url?:string|null};
export type AwayItem={id:string;kind:'question'|'competitor'|'results';title:string;detail:string;url?:string|null;action:AwayAction};

const icon=(kind:AwayItem['kind'])=>kind==='question'?<MessageCircleQuestion size={15}/>:kind==='competitor'?<Tag size={15}/>:<TrendingUp size={15}/>;

/** While you were away: at most three things the employee noticed since you last looked, each with the one thing to do about it. */
export function WhileAway({owner,onOpen}:{owner:boolean;onOpen:(key:string)=>void}){
  const [items,setItems]=useState<AwayItem[]|null>(null),[busy,setBusy]=useState(''),[done,setDone]=useState(''),[error,setError]=useState('');
  useEffect(()=>{let disposed=false;void api<AwayItem[]>('/away').then(list=>{if(!disposed)setItems(list);}).catch(()=>{if(!disposed)setItems([]);});return()=>{disposed=true;};},[]);
  async function act(item:AwayItem){
    if(busy)return;
    if(item.action.kind==='open'&&item.action.key){onOpen(item.action.key);return;}
    setBusy(item.id);setError('');
    try{setItems(await api<AwayItem[]>('/away/'+encodeURIComponent(item.id),{action:'assign'}));setDone('Queued: Chip starts on it right away.');}
    catch(cause){setError((cause as Error).message);}finally{setBusy('');}
  }
  if(!items||(!items.length&&!done))return null;
  return <section className="fe-cockpit-section fe-away" aria-label="While you were away">
    <h3>While you were away</h3>
    <div className="fe-cockpit-list">{items.map(item=><div className="fe-cockpit-item fe-away-item" key={item.id}>
      {icon(item.kind)}
      <span><strong>{item.title}</strong><small>{item.detail}{item.url&&<> <a href={item.url} target="_blank" rel="noopener noreferrer" aria-label="Open the source">Open <ExternalLink size={11}/></a></>}</small></span>
      <button type="button" disabled={!!busy||!owner&&item.action.kind!=='open'} onClick={()=>void act(item)}>{busy===item.id?'Queuing…':item.action.label}</button>
    </div>)}</div>
    {done&&<p className="fe-today-next" role="status">{done}</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </section>;
}
