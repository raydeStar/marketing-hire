import {Files,Lightbulb,Menu,MessageCircle,Moon,Search,Shapes,SquareCheck,Sun} from 'lucide-react';

const destinations=[
  {id:'Home',label:'Chat',icon:MessageCircle},
  {id:'Search',label:'Search',icon:Search},
  {id:'Feed',label:'Feed',icon:Files},
  {id:'Ideas',label:'Ideas',icon:Lightbulb},
  {id:'Todo',label:'To-do',icon:SquareCheck},
  {id:'Knowledge',label:'Artifacts',icon:Shapes}
];

export function StudyNavigation({current,openTodos,open,onNavigate,theme,onToggleTheme}:{current:string|null;openTodos:number;open:boolean;onNavigate:(id:string)=>void;theme:'light'|'dark';onToggleTheme:()=>void}){
  return <aside className="sidebar study-rail" id="study-sidebar" hidden={!open}>
    <div className="rail-navigation">
    <nav id="study-navigation" aria-label="Study navigation">
      {destinations.map(({id,label,icon:Icon})=><button key={id} type="button" aria-label={label}
        aria-current={current===id?'page':undefined} className={'rail-button'+(current===id?' active':'')} onClick={()=>onNavigate(id)}>
        <Icon size={23} strokeWidth={1.7} aria-hidden="true"/>
        <span className="rail-tooltip" aria-hidden="true">{label}</span>
        {id==='Todo'&&openTodos>0&&<b aria-hidden="true">{openTodos>99?'99+':openTodos}</b>}
      </button>)}
    </nav>
    <div className="rail-divider"/>
    </div>
    <div className="rail-utilities">
    <button type="button" className="rail-button" aria-label={theme==='light'?'Switch to the dark study':'Switch to the paper study'} onClick={onToggleTheme}>
      {theme==='light'?<Moon size={23} strokeWidth={1.7} aria-hidden="true"/>:<Sun size={23} strokeWidth={1.7} aria-hidden="true"/>}
      <span className="rail-tooltip" aria-hidden="true">{theme==='light'?'Dark study':'Paper study'}</span>
    </button>
    <button type="button" aria-label="Settings" aria-current={current==='Settings'?'page':undefined}
      className={'rail-button rail-settings'+(current==='Settings'?' active':'')} onClick={()=>onNavigate('Settings')}>
      <Menu size={23} strokeWidth={1.7} aria-hidden="true"/>
      <span className="rail-tooltip" aria-hidden="true">Settings</span>
    </button>
    </div>
  </aside>;
}
