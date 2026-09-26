import {useCallback,useEffect,useRef,useState} from 'react';
import {api} from '../api';
import type {MarketingState} from '../components/MarketingPanels';
import type {AppSummary,State,UploadFile} from '../types';
import type {Published} from './PublishPage';
import {plain} from './shared';

/** Everything the Library shows is stored elsewhere (wiki, artifacts, uploads, the employee's records);
 * the host keeps only where each item is filed, its tags and each person's pins. */
export type LibraryKind='brief'|'wiki'|'page'|'tool'|'media'|'source'|'deliverable';
export type WikiPage={id:string;version:number;scope:string;scopeId:string;title:string;body:string;kind:string;status:string;digest:string;author:string;createdAt:string;updatedAt:string};
export type LibraryItem={key:string;kind:LibraryKind;id:string;title:string;summary:string;body:string;folder:string;tags:string[];updated:number;archived:boolean;label:string};
export type LibraryEntry={key:string;folder:string|null;tags:string[];updatedBy:string;updatedAt:string};
export type LibraryMeta={version:number;folders:string[];entries:LibraryEntry[];pins:string[]};

export const kindLabel:Record<LibraryKind,string>={brief:'Brief',wiki:'Document',page:'Page',tool:'App',media:'Media',source:'Source',deliverable:'Deliverable'};
const wikiType:Record<string,string>={fact:'Fact',policy:'Playbook',hypothesis:'Hypothesis',question:'Open question'};
const deliverableTitle:Record<string,string>={audience_note:'Audience & problem note',post_angles:'Draft post angles',review_packet:'Review packet',revision_angles:'Revised post angles'};

/** Top-level folders that always exist. Items land in one by type until someone files them elsewhere. */
export const homeFolders=['Company','Research','Campaigns','Pages & apps','Media'] as const;
const defaultFolder:Record<LibraryKind,string>={brief:'Company',wiki:'Company',page:'Pages & apps',tool:'Pages & apps',media:'Media',source:'Research/Sources',deliverable:'Campaigns'};

const stamp=(value:number|string)=>typeof value==='number'?(value<1e12?value*1000:value):Date.parse(value)||0;
const emptyMeta:LibraryMeta={version:0,folders:[],entries:[],pins:[]};

/** Who made a change, in words: the host records owner and member principals. */
export function actorLabel(actor:string){return actor.startsWith('Owner ')?'Owner':actor.startsWith('Member ')?'Teammate '+actor.slice(7,13):actor||'Unknown';}
export function parentOf(path:string){const index=path.lastIndexOf('/');return index<0?'':path.slice(0,index);}
export function leafOf(path:string){return path.slice(path.lastIndexOf('/')+1);}
export function within(folder:string,path:string){return folder===path||folder.startsWith(path+'/');}

function build(state:MarketingState|null,wiki:WikiPage[],apps:AppSummary[],uploads:UploadFile[],published:Published[],meta:LibraryMeta):LibraryItem[]{
  const entries=new Map(meta.entries.map(entry=>[entry.key,entry]));
  const items:Omit<LibraryItem,'folder'|'tags'>[]=[];
  if(state){
    const profile=state.profile;
    items.push({key:'brief:profile',kind:'brief',id:'profile',title:'Business brief',label:'Brief',summary:plain(profile.product_summary||'')||'What you sell, who it’s for and what matters now.',
      body:[profile.product_summary,profile.audience,profile.goals,profile.voice,profile.channels,profile.guardrails].join('\n'),updated:stamp(profile.updated_at),archived:false});
    items.push({key:'brief:objectives',kind:'brief',id:'objectives',title:'Objectives & positioning',label:'Objectives',summary:'North star, this quarter’s objectives, positioning, competitors and non-goals.',body:'north star objectives key results positioning competitors focus non-goals',updated:0,archived:false});
    // One entry per page: the same source cited for several tasks is one item, newest note first.
    const byUrl=new Map<string,typeof state.evidence>();
    for(const source of state.evidence||[]){const key=source.url.replace(/[?#].*$/,'').replace(/\/$/,'');byUrl.set(key,[...(byUrl.get(key)||[]),source]);}
    for(const group of byUrl.values()){
      const sorted=[...group].sort((a,b)=>b.created_at-a.created_at);const latest=sorted[0];
      const note=sorted.map(item=>plain(item.note)).find(text=>/^cited for/i.test(text))||plain(latest.note).replace(/^Read during a shift for:\s*/i,'Used for: ').replace(/\s*One public source, not a representative sample\.?/i,'');
      items.push({key:'source:'+latest.id,kind:'source',id:latest.id,title:latest.title||latest.url,label:group.length>1?`Source · ${group.length} tasks`:'Source',summary:note||latest.url,
        body:sorted.map(item=>`${item.note}\n${item.query}`).join('\n')+`\n${latest.url}`,updated:stamp(latest.created_at),archived:false});
    }
    for(const artifact of state.runway?.artifacts||[])items.push({key:'deliverable:'+artifact.id,kind:'deliverable',id:artifact.id,title:deliverableTitle[artifact.kind]||artifact.kind.replaceAll('_',' '),label:'Deliverable',summary:'From Marketing’s current assignment',body:artifact.content.slice(0,6000),updated:stamp(artifact.created_at),archived:false});
  }
  for(const page of wiki)items.push({key:'wiki:'+page.id,kind:'wiki',id:page.id,title:page.title,label:wikiType[page.kind]||'Document',summary:plain(page.body.replace(/^\s*#{1,3}[^\n]*\n/,'')).slice(0,160),body:page.body,updated:stamp(page.updatedAt),archived:page.status==='archived'});
  for(const app of apps){
    const tool=(app.fieldCount??1)>1||app.hasPage===false;
    const live=published.some(item=>item.artifactId===app.id);
    items.push({key:'page:'+app.id,kind:tool?'tool':'page',id:app.id,title:app.title,label:tool?`App · ${app.entryCount} record${app.entryCount===1?'':'s'}`:live?'Page · Published':'Page',summary:app.description,body:app.description,updated:0,archived:app.archived});
  }
  for(const file of uploads)items.push({key:'media:'+file.id,kind:'media',id:file.id,title:file.name,label:file.mediaType.startsWith('video/')?'Video':file.mediaType.startsWith('image/')?'Image':'File',summary:file.mediaType,body:file.name,updated:stamp(file.created),archived:file.archived});
  return items.map(item=>{const entry=entries.get(item.key);return {...item,folder:entry?.folder||defaultFolder[item.kind],tags:entry?.tags||[]};});
}

/** Every folder path to show: the built-in homes, saved folders, and any folder an item sits in, with ancestors. */
export function folderTree(meta:LibraryMeta,items:LibraryItem[]){
  const all=new Set<string>(homeFolders);
  const add=(path:string)=>{let current=path;while(current){all.add(current);current=parentOf(current);}};
  meta.folders.forEach(add);items.forEach(item=>add(item.folder));
  return [...all].sort((a,b)=>{
    const home=(path:string)=>{const index=homeFolders.indexOf(path.split('/')[0] as typeof homeFolders[number]);return index<0?99:index;};
    return home(a)-home(b)||a.localeCompare(b,undefined,{sensitivity:'base'});
  });
}

// ---------- Search: ranked, typo-tolerant at the end of a word, with marketing synonyms ----------
const stop=new Set('a an and are as at be but by for from has have how i in is it its of on or our that the their this to was we what when where which who why will with you your'.split(' '));
const synonyms:Record<string,string[]>={
  customer:['buyer','audience','client','user','persona'],audience:['customer','persona','segment','buyer'],buyer:['customer','audience'],
  campaign:['launch','promotion','initiative'],launch:['campaign','release'],post:['social','linkedin','tweet','caption'],social:['post','linkedin','instagram'],
  email:['newsletter','inbox'],newsletter:['email'],brand:['voice','tone','ethos','identity'],voice:['tone','brand','style'],tone:['voice','brand'],ethos:['brand','values','mission'],
  competitor:['rival','alternative','competition'],price:['pricing','cost','plan'],pricing:['price','cost'],goal:['objective','kpi','target','outcome'],
  metric:['kpi','measure','result'],kpi:['metric','goal'],landing:['page','website'],page:['landing','site'],research:['source','study','evidence','insight'],
  insight:['research','learning','finding'],experiment:['test','hypothesis'],test:['experiment'],plan:['roadmap','calendar','schedule'],calendar:['schedule','plan'],
  guardrail:['policy','rule','permission'],policy:['guardrail','rule','playbook'],playbook:['policy','process','checklist']
};
export function stem(word:string){
  let value=word.toLowerCase();
  for(const suffix of ['ational','ization','fulness','ousness','iveness','ments','ment','ings','ing','edly','ies','ied','ers','er','ed','ly','es','s'])
    if(value.length>suffix.length+2&&value.endsWith(suffix)){
      // "prices" is "price" + s; "boxes" and "launches" are box + es.
      value=suffix==='es'&&!/(s|x|z|ch|sh)es$/.test(value)?value.slice(0,-1):value.slice(0,-suffix.length)+(suffix==='ies'||suffix==='ied'?'y':'');break;}
  return value;
}
function tokens(text:string){return (text.toLowerCase().match(/[\p{L}\p{N}]+/gu)||[]).filter(word=>!stop.has(word)).map(stem);}

export type SearchHit={item:LibraryItem;score:number;snippet:string};
export function searchLibrary(items:LibraryItem[],query:string,limit=40):SearchHit[]{
  const words=(query.toLowerCase().match(/[\p{L}\p{N}]+/gu)||[]).filter(word=>!stop.has(word));
  if(!words.length)return [];
  const last=stem(words[words.length-1]);
  // Each query word also matches its synonyms at a lower weight.
  const terms=words.flatMap((word,index)=>[{term:stem(word),weight:1,prefix:index===words.length-1,index},...(synonyms[stem(word)]||synonyms[word]||synonyms[word.replace(/(?:es|s)$/,'')]||[]).map(other=>({term:stem(other),weight:.45,prefix:false,index}))]);
  const docs=items.map(item=>({item,fields:[{words:tokens(item.title),boost:3},{words:tokens(item.tags.join(' ')),boost:2.5},{words:tokens(item.folder.replaceAll('/',' ')+' '+item.label),boost:1.2},{words:tokens(item.summary+' '+item.body),boost:1}]}));
  const length=docs.reduce((sum,doc)=>sum+doc.fields.reduce((n,field)=>n+field.words.length,0),0)/Math.max(1,docs.length);
  const df=new Map<string,number>();
  for(const {term,prefix} of terms)df.set(term,docs.filter(doc=>doc.fields.some(field=>field.words.some(word=>word===term||(prefix&&word.startsWith(term))))).length);
  const hits=docs.map(doc=>{
    const size=doc.fields.reduce((n,field)=>n+field.words.length,0);
    let score=0;const matched=new Set<number>();
    for(const {term,weight,prefix,index} of terms){
      let tf=0;for(const field of doc.fields)tf+=field.words.filter(word=>word===term||(prefix&&term===last&&word.startsWith(term))).length*field.boost;
      // A related term counts as covering the word it stands in for (at a lower weight).
      if(!tf)continue;matched.add(index);
      const n=df.get(term)||0,idf=Math.log(1+(docs.length-n+.5)/(n+.5));
      score+=weight*idf*(tf*2.2)/(tf+1.2*(.25+.75*size/Math.max(1,length)));
    }
    // Prefer items that match every word the person typed.
    score*=matched.size/words.length;
    // What people search for is usually a document by its name: a title holding every word ranks first; the sources behind documents come after them.
    const title=tokens(doc.item.title);
    if(words.every(word=>title.some(token=>token===stem(word)||token.startsWith(stem(word)))))score*=2.5;
    if(doc.item.kind==='source')score*=.4;
    return {item:doc.item,score,snippet:snippet(doc.item,words)};
  }).filter(hit=>hit.score>0);
  return hits.sort((a,b)=>b.score-a.score||b.item.updated-a.item.updated).slice(0,limit);
}
function snippet(item:LibraryItem,words:string[]){
  // Not the title again (a document's first heading), and not an older source note's boilerplate.
  let text=plain(item.body||item.summary).replace(/Read during a shift for:\s*/gi,'Used for: ').replace(/\s*One public source, not a representative sample\.?/gi,'');
  if(text.toLowerCase().startsWith(item.title.toLowerCase()))text=text.slice(item.title.length).trimStart();
  const lower=text.toLowerCase();
  const at=words.map(word=>lower.indexOf(word)).filter(index=>index>=0).sort((a,b)=>a-b)[0];
  if(at===undefined)return item.summary.slice(0,150);
  const start=Math.max(0,at-50);return (start?'…':'')+text.slice(start,start+150)+(start+150<text.length?'…':'');
}

/** Loads everything the Library, search and the rail's pinned apps need, and keeps it current. */
export function useLibrary(state:MarketingState|null,enabled:boolean){
  const [wiki,setWiki]=useState<WikiPage[]>([]),[apps,setApps]=useState<AppSummary[]>([]),[uploads,setUploads]=useState<UploadFile[]>([]);
  const [published,setPublished]=useState<Published[]>([]),[meta,setMeta]=useState<LibraryMeta>(emptyMeta),[loaded,setLoaded]=useState(false),[error,setError]=useState('');
  const sequence=useRef(0),version=useRef(0);
  version.current=meta.version;
  const reload=useCallback(async()=>{
    if(!enabled)return;const current=++sequence.current;
    const [nextWiki,legacy,nextPublished,nextMeta]=await Promise.all([
      api<WikiPage[]>('/company-wiki').catch(()=>null),api<State>('/state').catch(()=>null),
      api<Published[]>('/published-pages').catch(()=>[] as Published[]),api<LibraryMeta>('/workspace-library').catch(()=>null)]);
    if(current!==sequence.current)return;
    if(nextWiki)setWiki(nextWiki);if(legacy){setApps(legacy.artifacts||[]);setUploads(legacy.uploads||[]);}
    setPublished(nextPublished);if(nextMeta){setMeta(nextMeta);version.current=nextMeta.version;}
    setError(!nextWiki||!legacy?'Some of the library couldn’t load. Showing what did.':'');setLoaded(true);
  },[enabled]);
  useEffect(()=>{
    void reload();
    const timer=setInterval(()=>{if(document.visibilityState==='visible')void reload();},30000);
    return()=>clearInterval(timer);
  },[reload]);
  const items=build(state,wiki,apps,uploads,published,meta);
  async function write(path:string,body:unknown){
    try{const next=await api<LibraryMeta>('/workspace-library'+path,body,'PUT');setMeta(next);version.current=next.version;return next;}
    catch(cause){await reload();throw cause;}
  }
  return {items,meta,wiki,apps,uploads,published,loaded,error,reload,
    file:(key:string,folder:string|null,tags:string[])=>write('/entries/'+encodeURIComponent(key),{expectedVersion:version.current,folder,tags}),
    folders:(folders:string[],moves:{from:string;to:string|null}[]=[])=>write('/folders',{expectedVersion:version.current,folders,moves}),
    pin:(keys:string[])=>write('/pins',{keys})};
}
export type Library=ReturnType<typeof useLibrary>;
