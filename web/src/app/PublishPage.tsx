import {useState} from 'react';
import {Check,Copy,Download,ExternalLink,Globe} from 'lucide-react';
import {api} from '../api';
import {readableTime} from '../components/MarketingPanels';
import type {ArtifactApp} from '../types';
import {Dialog,download} from './shared';

export type Published={slug:string;artifactId:string;artifactVersion:string;title:string;digest:string;publishedBy:string;publishedAt:string};

export function slugFor(title:string){return title.toLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g,'-').replace(/^-+|-+$/g,'').slice(0,60).replace(/-+$/,'')||'page';}
export function publicUrl(slug:string){return `${location.origin}/p/${slug}`;}

/** A standalone copy of the page for any web host. */
export function downloadPage(app:ArtifactApp){
  const page=app.definition.page;if(!page)return;
  const escape=(value:string)=>value.replace(/[&<>"]/g,char=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[char]!));
  const html=`<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n<meta name="viewport" content="width=device-width,initial-scale=1">\n<title>${escape(app.definition.title)}</title>\n<style>*{box-sizing:border-box}body{margin:0;font:16px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}img,video{max-width:100%}</style>\n<style>\n${page.css}\n</style>\n</head>\n<body>\n${page.html}\n<script>window.thaddeus={onChange(){return()=>{};},save(){return Promise.reject(new Error('Read-only copy'));}};</script>\n<script>\n${page.javaScript}\n</script>\n</body>\n</html>\n`;
  download(slugFor(app.definition.title)+'.html',html,'text/html;charset=utf-8');
}

export function PublishDialog({app,published,online,onClose,onChanged}:{app:ArtifactApp;published?:Published;online:boolean;onClose:()=>void;onChanged:()=>void}){
  const [slug,setSlug]=useState(published?.slug||slugFor(app.definition.title)),[busy,setBusy]=useState(false),[error,setError]=useState(''),[copied,setCopied]=useState(false);
  const stale=published&&published.artifactVersion!==app.version;
  async function publish(){
    setBusy(true);setError('');
    try{await api('/artifacts/'+app.id+'/publish',{requestId:crypto.randomUUID(),slug:slug.trim()});onChanged();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  async function unpublish(){
    if(!published||!window.confirm('Unpublish this page? The link stops working right away.'))return;
    setBusy(true);setError('');
    try{await api('/published-pages/'+encodeURIComponent(published.slug)+'/unpublish',{});onChanged();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <Dialog title={published?'Published page':'Publish page'} onClose={onClose}><div className="fe-form">
    {published&&<div className="fe-publish-live"><Globe size={18}/><div><strong>Live at</strong><a href={publicUrl(published.slug)} target="_blank" rel="noopener">{publicUrl(published.slug)}</a><small>Published {readableTime(published.publishedAt)}</small></div>
      <button type="button" onClick={()=>void navigator.clipboard.writeText(publicUrl(published.slug)).then(()=>{setCopied(true);setTimeout(()=>setCopied(false),2000);})}>{copied?<Check size={14}/>:<Copy size={14}/>} {copied?'Copied':'Copy link'}</button></div>}
    {stale&&<p className="fe-notice attn">This page changed after it was published. Visitors still see the published version until you publish again.</p>}
    <p className="fe-muted">Publishing freezes this exact version at a link anyone who can reach this workspace can open. Later edits stay private until you publish again. Pages run sandboxed: no forms or tracking are sent anywhere.</p>
    <label>Page address<span className="fe-slug"><span>{location.origin}/p/</span><input value={slug} maxLength={60} onChange={event=>setSlug(event.target.value.toLowerCase().replace(/[^a-z0-9-]/g,'-'))} aria-label="Page address"/></span></label>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer>
      <button type="button" className="fe-ghost" onClick={()=>downloadPage(app)}><Download size={15}/> Download HTML</button>
      {published&&<button type="button" disabled={busy||!online} onClick={()=>void unpublish()}>Unpublish</button>}
      {published&&<a className="fe-button" href={publicUrl(published.slug)} target="_blank" rel="noopener"><ExternalLink size={15}/> Open</a>}
      <button type="button" className="primary" disabled={busy||!online||!slug.trim()||app.archived} onClick={()=>void publish()}>{busy?'Publishing…':published?(stale||slug!==published.slug?'Publish this version':'Republish'):'Publish'}</button>
    </footer>
  </div></Dialog>;
}
