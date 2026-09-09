import {Check,ShieldCheck,LoaderCircle,Ban,CircleAlert,Clock3} from 'lucide-react';
export const names:Record<string,string> = {queued:'Queued',running:'Working',awaitingApproval:'Needs approval',succeeded:'Completed',failed:'Failed',denied:'Denied',cancelled:'Cancelled',needsAttention:'Needs attention',paused:'Paused'};
export function StateIcon({state}:{state:string}) { const Icon = state==='succeeded'?Check:state==='awaitingApproval'?ShieldCheck:state==='running'?LoaderCircle:state==='denied'||state==='cancelled'?Ban:state==='failed'||state==='needsAttention'?CircleAlert:Clock3; return <Icon size={18}/>; }
export function Raven({state='idle',onClick}:{state?:string;onClick?:()=>void}) {
  return <button className={'raven '+state} onClick={onClick} aria-label={'Thaddeus raven: '+(names[state]||state)} title={names[state]||state}>
    <svg viewBox="0 0 48 48" shapeRendering="crispEdges" aria-hidden="true"><path fill="#11191e" d="M18 9h13v3h5v4h4v5h-8v14h-5v4H14v-4H9v-9h4V15h5z"/><path fill="#465767" d="M18 12h11v3h5v6h-7v12h-6v3h-7V23h4z"/><path fill="#283843" d="M14 25h8v4h5v6h-8v3h-9v-5H6v-4h8z"/><path fill="#8496a1" d="M18 14h3v7h-3zm4-2h7v3h-7z"/><path fill="#d7bb76" d="M34 16h9v3h-9zm-19 22h3v5h-6v-2h3zm10 0h3v3h4v2h-7z"/><path className="eye" fill="#eee6c7" d="M28 15h3v3h-3z"/><path fill="#758f83" d="M6 44h33v2H6z"/></svg>
  </button>;
}
