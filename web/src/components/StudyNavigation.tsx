import {Files,Lightbulb,Menu,MessageCircle,PanelLeft,Search,Shapes,SquareCheck} from 'lucide-react';

const destinations=[
  {id:'Home',label:'Chat',icon:MessageCircle},
  {id:'Search',label:'Search',icon:Search},
  {id:'Feed',label:'Feed',icon:Files},
  {id:'Ideas',label:'Ideas',icon:Lightbulb},
  {id:'Todo',label:'To-do',icon:SquareCheck},
  {id:'Knowledge',label:'Artifacts',icon:Shapes}
];

export function StudyNavigation({current,openTodos,expanded,onToggle,onNavigate}:{current:string|null;openTodos:number;expanded:boolean;onToggle:()=>void;onNavigate:(id:string)=>void}){
  return <aside className="sidebar study-rail">
    <button type="button" className="rail-button rail-toggle" aria-label={expanded?'Collapse sidebar':'Expand sidebar'} aria-expanded={expanded} aria-controls="study-navigation" onClick={onToggle}>
      <PanelLeft size={19} strokeWidth={1.6} aria-hidden="true"/>
      <span className="rail-tooltip" aria-hidden="true">{expanded?'Collapse sidebar':'Expand sidebar'}</span>
    </button>
    <div className="rail-navigation">
    <nav id="study-navigation" aria-label="Study navigation">
      {destinations.map(({id,label,icon:Icon})=><button key={id} type="button" aria-label={label}
        aria-current={current===id?'page':undefined} className={'rail-button'+(current===id?' active':'')} onClick={()=>onNavigate(id)}>
        <Icon size={23} strokeWidth={1.7} aria-hidden="true"/>
        <span className="rail-label" aria-hidden="true">{label}</span>
        <span className="rail-tooltip" aria-hidden="true">{label}</span>
        {id==='Todo'&&openTodos>0&&<b aria-hidden="true">{openTodos>99?'99+':openTodos}</b>}
      </button>)}
    </nav>
    <div className="rail-divider"/>
    </div>
    <button type="button" aria-label="Settings" aria-current={current==='Settings'?'page':undefined}
      className={'rail-button rail-settings'+(current==='Settings'?' active':'')} onClick={()=>onNavigate('Settings')}>
      <Menu size={23} strokeWidth={1.7} aria-hidden="true"/>
      <span className="rail-label" aria-hidden="true">Settings</span>
      <span className="rail-tooltip" aria-hidden="true">Settings</span>
    </button>
  </aside>;
}
