import {useState} from 'react';
import {Bell,Download,LogOut,Monitor,Moon,Sun} from 'lucide-react';
import {MarketingAccessSettings} from '../components/MarketingAccessSettings';
import {MarketingTokenUsage} from '../components/MarketingTokenUsage';
import {PageHead} from './shared';

export type ThemeChoice='light'|'dark'|'system';
export type StyleChoice='clean'|'muse';

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
  return <section className="fe-settings-section fe-card" aria-label="Notifications"><div className="fe-theme-row"><div><h3>Notifications</h3><small>{supported?'Get a desktop notification when something new needs you while this tab is in the background.':'This browser doesn’t support notifications.'}</small>{note&&<small className="fe-alert-text">{note}</small>}</div>
    {supported&&<button type="button" className={on?'':'primary'} aria-pressed={on} onClick={()=>void toggle()}><Bell size={15}/> {on?'Turn off':'Turn on'}</button>}</div></section>;
}

export function SettingsView({owner,canNotify=owner,accessLabel,hostOnline,theme,onTheme,look,onLook,signedInName,onSignOut}:{owner:boolean;canNotify?:boolean;accessLabel?:string;hostOnline:boolean;theme:ThemeChoice;onTheme:(theme:ThemeChoice)=>void;look:StyleChoice;onLook:(look:StyleChoice)=>void;signedInName:string;onSignOut?:()=>void}){
  return <div className="fe-page"><div className="fe-page-inner narrow">
    <PageHead title="Settings"/>
    <section className="fe-settings-section fe-card" aria-label="Appearance"><div className="fe-theme-row"><div><h3>Appearance</h3><small>Match your system, or pick one.</small></div>
      <nav className="fe-segmented" aria-label="Theme">{([['light',Sun,'Light'],['dark',Moon,'Dark'],['system',Monitor,'System']] as const).map(([value,Icon,label])=><button type="button" key={value} aria-pressed={theme===value} onClick={()=>onTheme(value)}><Icon size={14}/> {label}</button>)}</nav></div>
      <div className="fe-theme-row fe-style-row"><div><h3>Style</h3><small>Clean: violet accent, focused chat. Muse: charcoal neutrals with an at-a-glance panel beside chat.</small></div>
      <nav className="fe-segmented" aria-label="Style">{([['clean','Clean'],['muse','Muse']] as const).map(([value,label])=><button type="button" key={value} aria-pressed={look===value} onClick={()=>onLook(value)}>{label}</button>)}</nav></div></section>
    {owner&&<section className="fe-settings-section" aria-label="Usage"><h2>Usage</h2><div className="fe-card fe-usage"><MarketingTokenUsage/></div></section>}
    {owner&&<section className="fe-settings-section" aria-label="Team access"><h2>Team access</h2><MarketingAccessSettings online={hostOnline}/></section>}
    {canNotify&&<NotificationSetting/>}
    {owner&&<section className="fe-settings-section fe-card" aria-label="Backup"><div className="fe-theme-row"><div><h3>Backup</h3><small>A JSON copy of this workspace’s pages, media, wiki, team and employee files, and published pages. Marketing’s own task and draft ledger lives with the agent runtime and isn’t included.</small></div><a className="fe-button" href="/api/export" download><Download size={15}/> Download a backup</a></div></section>}
    <section className="fe-settings-section fe-card" aria-label="Account"><div className="fe-theme-row"><div><h3>{signedInName}</h3><small>{owner?'Workspace owner':accessLabel||'Collaborator'}</small></div>{onSignOut&&<button type="button" onClick={onSignOut}><LogOut size={15}/> Sign out</button>}</div></section>
  </div></div>;
}
