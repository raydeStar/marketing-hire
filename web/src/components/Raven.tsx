import {Check,ShieldCheck,LoaderCircle,Ban,CircleAlert,Clock3} from 'lucide-react';
export const names:Record<string,string> = {queued:'Queued',running:'Working',awaitingApproval:'Needs approval',awaitingInput:'Needs your answer',succeeded:'Completed',failed:'Failed',denied:'Denied',cancelled:'Cancelled',needsAttention:'Needs attention',paused:'Paused'};
export function StateIcon({state}:{state:string}) { const Icon = state==='succeeded'?Check:state==='awaitingApproval'?ShieldCheck:state==='running'?LoaderCircle:state==='denied'||state==='cancelled'?Ban:state==='failed'||state==='needsAttention'?CircleAlert:Clock3; return <Icon size={18}/>; }
export function Raven({state='idle',onClick}:{state?:string;onClick?:()=>void}) {
  return <button className={'raven '+state} onClick={onClick} aria-label={'Thaddeus raven: '+(names[state]||state)} title={names[state]||state}>
    <svg viewBox="0 0 64 64" shapeRendering="crispEdges" aria-hidden="true">
      <ellipse fill="#0e1712" cx="32" cy="58" rx="22" ry="3"/>
      <path fill="#263a30" d="M10 54h43v5H10z"/><path fill="#627667" d="M11 53h39v2H11z"/><path fill="#a3a080" d="M13 55h35v2H13z"/><path fill="#c5b68a" d="M15 55h28v1H15z"/><path fill="#5a6c55" d="M11 57h41v2H11z"/>
      <g className="raven-body">
        <path fill="#0c1219" d="M23 26h20v9h3v8h-4v5h-5v3H24v-3h-5v-3h-9v-3H7v-3h8v-5h5v-5h3z"/>
        <path fill="#29313f" d="M25 27h13v5h5v10h-4v5H26v-3h-5V33h4z"/>
        <path fill="#3d4c54" d="M35 30h4v5h3v8h-3v3h-4v-4h-3v-7h3z"/>
        <path fill="#657571" d="M38 33h2v6h-2zm-2 9h3v3h-3z"/>
        <path fill="#141c2b" d="M20 36h7v7h-5v3H12v-3H9v-2h7v-3h4z"/>
        <path fill="#455166" d="M13 42h8v2h-8zm6-4h5v2h-5z"/>
        <g className="raven-wing"><path fill="#111b27" d="M24 30h9v4h3v8h-4v4h-8v-3h-4v-8h4z"/><path fill="#303b52" d="M24 32h7v4h2v6h-4v2h-5V41h-2v-6h2z"/><path fill="#4d5d72" d="M25 33h5v2h-5zm-2 3h4v2h-4z"/><path fill="#758083" d="M25 33h3v1h-3z"/><path fill="#1d273b" d="M27 37h3v5h-3zm-4 2h2v3h-2z"/></g>
        <path fill="#1d2326" d="M27 48h3v5h-3zm9 0h3v5h-3z"/><path fill="#75817a" d="M25 52h7v2h-9v-1h2zm9 0h8v2h-10v-1h2z"/>
        <g className="raven-head">
          <path fill="#0c121a" d="M25 12h4V9h4v3h7v3h5v4h3v5h-3v6h-5v3h-8v-3h-7v-5h-3v-9h3z"/>
          <path fill="#313b4c" d="M27 15h12v2h4v9h-5v4h-9v-4h-4v-9h2z"/>
          <path fill="#516478" d="M28 15h8v2h-8zm-3 3h3v6h-3z"/><path fill="#829596" d="M29 15h5v1h-5zm-3 3h1v3h-1z"/>
          <path fill="#18232d" d="M31 23h10v5h-5v3h-6v-4h-3v-2h4z"/>
          <path fill="#687878" d="M30 26h3v2h-3zm4 2h3v2h-3z"/>
          <g className="raven-beak"><path fill="#0c1219" d="M43 20h6v2h4v2h3v3h-3v2h-4v-3h-6z"/><path fill="#607075" d="M43 21h5v2h4v2h-9z"/><path fill="#9da69a" d="M44 21h3v1h-3z"/><path fill="#2e3b43" d="M44 25h8v2h-8z"/></g>
          <path fill="#101821" d="M37 18h6v6h-6z"/><g className="eye"><path fill="#dbcf9f" d="M38 19h3v3h-3z"/><path fill="#f1eace" d="M38 19h2v1h-2z"/><path fill="#151c22" d="M40 20h1v2h-1z"/></g>
        </g>
        <path fill="#9c875c" d="M38 31h3v3h-3z"/><path fill="#dec78f" d="M39 31h1v1h-1z"/>
      </g>
    </svg>
  </button>;
}
