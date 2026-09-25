import {Check,ShieldCheck,LoaderCircle,Ban,CircleAlert,Clock3} from 'lucide-react';
export const names:Record<string,string> = {queued:'Queued',running:'Working',awaitingApproval:'Needs approval',awaitingInput:'Needs your answer',succeeded:'Completed',failed:'Failed',denied:'Denied',cancelled:'Cancelled',needsAttention:'Needs attention',paused:'Paused'};
export function StateIcon({state}:{state:string}) { const Icon = state==='succeeded'?Check:state==='awaitingApproval'?ShieldCheck:state==='running'?LoaderCircle:state==='denied'||state==='cancelled'?Ban:state==='failed'||state==='needsAttention'?CircleAlert:Clock3; return <Icon size={18}/>; }
export function Raven({state='idle',onClick}:{state?:string;onClick?:()=>void}) {
  const label=names[state]||({idle:'At your service',listening:'Ready when you are',thinking:'Thinking',disconnected:'Disconnected'} as Record<string,string>)[state]||state;
  const attributes={className:'raven '+state,title:label,'aria-label':'Raven: '+label};
  // Sixteen inks, integer pixels, and one discreet gold pin. The Order approves the tailoring.
  const p={ink:'#090f1b',shade:'#121e30',violet:'#26324d',blue:'#3d506e',feather:'#57758b',glint:'#8aa6a2',beak:'#263d43',edge:'#607b79',
    green:'#28463c',cover:'#496b56',trim:'#8c9d77',paper:'#c7bb91',gold:'#aa8b50',light:'#ebd59b',eye:'#fff0c6',shadow:'#0f1a16'};
  const portrait=<svg viewBox="0 0 64 64" shapeRendering="crispEdges" aria-hidden="true" focusable="false">
    <path fill={p.shadow} d="M10 58h44v3H10zM14 61h36v1H14z"/>
    <g className="raven-perch">
      <path fill={p.ink} d="M10 54h41v2h2v5H9v-2H7v-3h3z"/>
      <path fill={p.green} d="M9 57h42v3H9z"/><path fill={p.cover} d="M11 54h39v2H11zM9 59h41v1H9z"/>
      <path fill={p.trim} d="M12 54h34v1H12z"/><path fill={p.paper} d="M13 56h35v2H13z"/>
      <path fill={p.light} d="M15 56h29v1H15z"/><path fill={p.gold} d="M43 55h3v4h-3z"/>
    </g>
    <g className="raven-bird">
    <g className="raven-body">
      <g className="raven-tail">
        <path fill={p.ink} d="M22 34h7v12h-5v3h-7v3H6v-3h4v-3h4v-5h5v-4h3z"/>
        <path fill={p.violet} d="M23 37h3v7h-5v3h-5v2h-5v1H8v-1h5v-4h4v-4h6z"/>
        <path fill={p.feather} d="M17 43h4v1h-4zM12 47h4v1h-4z"/>
        <path fill={p.blue} d="M18 47h6v1h-6zM15 50h4v1h-4z"/>
      </g>
      <path fill={p.ink} d="M27 25h12v4h4v5h3v9h-3v5h-4v4H26v-2h-5v-4h-2V35h3v-6h5z"/>
      <path fill={p.violet} d="M27 29h11v3h4v5h1v6h-3v5h-4v2h-9v-3h-4V35h4z"/>
      <path fill={p.blue} d="M36 32h4v5h2v6h-3v4h-4v-6h-3v-6h4z"/>
      <path fill={p.feather} d="M39 35h2v5h-2zM37 42h3v3h-3z"/>
      <path fill={p.glint} d="M40 36h1v3h-1zM38 43h1v1h-1z"/>
      <path fill={p.shade} d="M26 46h4v3h6v1h-9v-2h-1z"/>
      <g className="raven-wing raven-wing-folded">
        <path fill={p.ink} d="M25 31h8v4h3v8h-3v4h-5v2h-6v-4h-2V36h3v-3h2z"/>
        <path fill={p.blue} d="M25 33h6v3h3v6h-3v3h-4v2h-3v-4h-2v-6h3z"/>
        <path fill={p.feather} d="M25 33h5v2h-5zM23 36h4v2h-4zM22 39h3v2h-3z"/>
        <path fill={p.glint} d="M25 33h3v1h-3zM23 36h2v1h-2z"/>
        <path fill={p.violet} d="M28 36h3v6h-3zM25 39h2v5h-2z"/>
        <path fill={p.shade} d="M30 39h2v4h-2zM27 42h2v3h-2zM24 44h2v2h-2z"/>
      </g>
      <g className="raven-wing raven-wing-open">
        <path fill={p.ink} d="M24 31h7v5h-5v3h-9v-2h-5v-3H8v-3H5v-6H3v-7h3v3h3v4h4v2h5v2h6z"/>
        <path fill={p.violet} d="M6 24h3v3h4v3h7v2h8v3H18v-2h-5v-2H9v-2H7z"/>
        <path fill={p.blue} d="M6 22h1v3H6zM9 26h2v3H9zM13 29h4v2h-4zM18 31h8v2h-8z"/>
        <path fill={p.feather} d="M9 26h1v2H9zM14 29h3v1h-3zM20 31h4v1h-4z"/>
        <path fill={p.shade} d="M13 33h4v2h-4zM19 35h5v2h-5z"/>
      </g>
      <g className="raven-head">
        <path fill={p.ink} d="M26 12h2V8h3v3h3V7h2v4h7v2h4v3h3v4h2v6h-4v5h-5v3H30v-3h-5v-5h-3V17h2v-3h2z"/>
        <path fill={p.violet} d="M29 13h12v2h4v3h2v8h-4v4H32v-2h-5v-4h-2v-6h2v-3h2z"/>
        <path fill={p.blue} d="M29 14h10v2H29zM26 17h4v6h-4zM30 24h5v3h-5z"/>
        <path fill={p.feather} d="M30 14h7v1h-7zM26 18h2v4h-2z"/>
        <path fill={p.glint} d="M30 14h3v1h-3zM26 18h1v2h-1z"/>
        <path fill={p.shade} d="M35 23h10v4h-4v4h-7v-3h-3v-2h4z"/>
        <path fill={p.feather} d="M32 27h2v2h-2zM35 29h2v3h-2zM39 28h2v2h-2z"/>
        <path fill={p.blue} d="M30 29h2v2h-2zM36 32h2v2h-2z"/>
        <g className="raven-beak">
          <path fill={p.ink} d="M44 20h7v2h4v2h4v4h-3v3h-3v-4h-9z"/>
          <path fill={p.beak} d="M45 21h5v2h5v2h2v2h-3v-2h-9z"/>
          <path fill={p.edge} d="M45 21h4v1h-4zM49 23h5v1h-5z"/>
          <path fill={p.shade} d="M46 25h7v1h-7z"/>
        </g>
        <path fill={p.ink} d="M37 17h8v7h-8z"/>
        <g className="raven-eye"><path fill={p.light} d="M38 18h6v5h-6z"/><path fill={p.eye} d="M38 18h3v2h-3z"/><path className="raven-pupil" fill={p.ink} d="M41 19h2v3h-2z"/><path fill={p.eye} d="M41 19h1v1h-1z"/></g>
        <path className="raven-eyelid" fill={p.edge} d="M39 21h4v1h-4z"/>
        <path className="raven-happy-eye" fill={p.eye} d="M38 20h1v-1h4v1h1v2h-1v-1h-4v1h-1z"/>
        <path className="raven-brow" fill={p.feather} d="M37 16h6v1h-6z"/>
      </g>
      <path fill={p.green} d="M39 32h3v4h-2v4h-1z"/><path fill={p.gold} d="M39 32h3v3h-3z"/><path fill={p.light} d="M40 32h1v1h-1z"/>
    </g>
    <path fill={p.beak} d="M27 50h3v4h-3zM36 50h3v4h-3z"/>
    <path fill={p.edge} d="M25 53h7v2h-9v-1h2zM35 53h7v2h-9v-1h2z"/>
    <path fill={p.glint} d="M25 53h2v1h-2zM35 53h2v1h-2z"/>
    </g>
    <g className="raven-thought" fill={p.light}><path d="M48 10h2v2h-2z"/><path d="M53 8h2v2h-2z"/><path d="M58 6h2v2h-2z"/></g>
    <g className="raven-sparkles" fill={p.light}><path d="M14 10h2v3h3v2h-3v3h-2v-3h-3v-2h3z"/><path d="M52 6h2v2h2v2h-2v2h-2v-2h-2V8h2z"/></g>
  </svg>;
  return onClick?<button {...attributes} type="button" onClick={onClick} aria-label={attributes['aria-label']+'. Open task details'}>{portrait}</button>:<span {...attributes} role="img">{portrait}</span>;
}
