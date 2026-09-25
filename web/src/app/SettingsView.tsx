import {useState} from 'react';
import {Bell,ChevronRight,Download,LogOut,Monitor,Moon,Sun} from 'lucide-react';
import {MarketingTokenUsage} from '../components/MarketingTokenUsage';
import {PublishingSettings} from './PublishingView';
import {GoLiveChecklist} from './GoLive';
import {GoogleAppSetup} from './GoogleAppSetup';

export type ThemeChoice='light'|'dark'|'system';
export const notifyKey='fe-notify-inbox';

function NotificationSetting(){
  const supported=typeof Notification!=='undefined';
  const [on,setOn]=useState(()=>{try{return supported&&Notification.permission==='granted'&&localStorage.getItem(notifyKey)==='yes';}catch{return false;}});
  const [note,setNote]=useState('');
  async function toggle(){
    if(on){setOn(false);try{localStorage.setItem(notifyKey,'no');}catch{}return;}
    const permission=await Notification.requestPermission();
    if(permission!=='granted'){setNote('Notifications are blocked for this site in your browser settings.');return;}
    setOn(true);setNote('');try{localStorage.setItem(notifyKey,'yes');}catch{}
  }
  return <div className="fe-setting" aria-label="Notifications"><div><strong>Desktop notifications</strong><small>{supported?'Notify me when something new needs a decision while this tab is in the background.':'This browser doesn’t support notifications.'}</small>{note&&<small className="fe-alert-text">{note}</small>}</div>
    {supported&&<button type="button" aria-pressed={on} onClick={()=>void toggle()}><Bell size={15}/> {on?'Turn off':'Turn on'}</button>}</div>;
}

export function SettingsView({owner,canNotify=owner,accessLabel,theme,onTheme,signedInName,onTeam,onSignOut,onNavigate}:{owner:boolean;canNotify?:boolean;accessLabel?:string;theme:ThemeChoice;onTheme:(theme:ThemeChoice)=>void;signedInName:string;onTeam:()=>void;onSignOut?:()=>void;onNavigate?:(target:string)=>void}){
  return <div className="fe-view"><div className="fe-view-inner narrow">
    <header className="fe-view-head"><div><h1>Settings</h1><p>Preferences for this browser and, for the owner, the workspace.</p></div></header>
    {owner&&onNavigate&&<section className="fe-settings" aria-label="Go-live checklist"><h2>Go-live checklist</h2><GoLiveChecklist onNavigate={onNavigate}/></section>}
    <section className="fe-settings" aria-label="Appearance"><h2>Appearance</h2>
      <div className="fe-setting"><div><strong>Theme</strong><small>Follow your system, or choose one.</small></div>
        <nav className="fe-segmented" aria-label="Theme">{([['system',Monitor,'System'],['light',Sun,'Light'],['dark',Moon,'Dark']] as const).map(([value,Icon,label])=><button type="button" key={value} aria-pressed={theme===value} onClick={()=>onTheme(value)}><Icon size={14}/> {label}</button>)}</nav></div>
    </section>
    {canNotify&&<section className="fe-settings" aria-label="Notifications"><h2>Notifications</h2><NotificationSetting/></section>}
    <section className="fe-settings" aria-label="Workspace"><h2>Workspace</h2>
      <button type="button" className="fe-setting fe-setting-link" onClick={onTeam}><div><strong>People and roles</strong><small>{owner?'Invite teammates, set roles and manage access in Team.':'See who works here and what each role can do.'}</small></div><ChevronRight size={16}/></button>
      {owner&&<div className="fe-setting"><div><strong>Backup</strong><small>A JSON copy of pages, media, the Library, team, employee instructions and published pages. The employee’s own task and draft ledger lives with its runtime and isn’t included.</small></div><a className="fe-button" href="/api/export" download><Download size={15}/> Download</a></div>}
    </section>
    {owner&&<section className="fe-settings" aria-label="Google app"><h2>Google app</h2><GoogleAppSetup/></section>}
    {owner&&<section className="fe-settings" aria-label="Publishing"><h2>Publishing channels</h2><PublishingSettings/></section>}
    {owner&&<section className="fe-settings" aria-label="Usage"><h2>Usage</h2><div className="fe-usage"><MarketingTokenUsage/></div></section>}
    <section className="fe-settings" aria-label="Account"><h2>Account</h2>
      <div className="fe-setting"><div><strong>{signedInName}</strong><small>{owner?'Workspace owner':`Role: ${accessLabel||'Reviewer'}`}</small></div>{onSignOut&&<button type="button" onClick={onSignOut}><LogOut size={15}/> Sign out</button>}</div>
    </section>
  </div></div>;
}
