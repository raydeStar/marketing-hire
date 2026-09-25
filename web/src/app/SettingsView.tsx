import {LogOut,Monitor,Moon,Sun} from 'lucide-react';
import {MarketingAccessSettings} from '../components/MarketingAccessSettings';
import {MarketingTokenUsage} from '../components/MarketingTokenUsage';
import {PageHead} from './shared';

export type ThemeChoice='light'|'dark'|'system';
export type StyleChoice='clean'|'muse';

export function SettingsView({owner,hostOnline,theme,onTheme,look,onLook,signedInName,onSignOut}:{owner:boolean;hostOnline:boolean;theme:ThemeChoice;onTheme:(theme:ThemeChoice)=>void;look:StyleChoice;onLook:(look:StyleChoice)=>void;signedInName:string;onSignOut?:()=>void}){
  return <div className="fe-page"><div className="fe-page-inner narrow">
    <PageHead title="Settings"/>
    <section className="fe-settings-section fe-card" aria-label="Appearance"><div className="fe-theme-row"><div><h3>Appearance</h3><small>Match your system, or pick one.</small></div>
      <nav className="fe-segmented" aria-label="Theme">{([['light',Sun,'Light'],['dark',Moon,'Dark'],['system',Monitor,'System']] as const).map(([value,Icon,label])=><button type="button" key={value} aria-pressed={theme===value} onClick={()=>onTheme(value)}><Icon size={14}/> {label}</button>)}</nav></div>
      <div className="fe-theme-row fe-style-row"><div><h3>Style</h3><small>Clean: violet accent, focused chat. Muse: charcoal neutrals with an at-a-glance panel beside chat.</small></div>
      <nav className="fe-segmented" aria-label="Style">{([['clean','Clean'],['muse','Muse']] as const).map(([value,label])=><button type="button" key={value} aria-pressed={look===value} onClick={()=>onLook(value)}>{label}</button>)}</nav></div></section>
    {owner&&<section className="fe-settings-section" aria-label="Usage"><h2>Usage</h2><div className="fe-card fe-usage"><MarketingTokenUsage/></div></section>}
    {owner&&<section className="fe-settings-section" aria-label="Team access"><h2>Team access</h2><MarketingAccessSettings online={hostOnline}/></section>}
    <section className="fe-settings-section fe-card" aria-label="Account"><div className="fe-theme-row"><div><h3>{signedInName}</h3><small>{owner?'Workspace owner':'Collaborator'}</small></div>{onSignOut&&<button type="button" onClick={onSignOut}><LogOut size={15}/> Sign out</button>}</div></section>
  </div></div>;
}
