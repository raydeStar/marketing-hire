import type {FeedEntry,FeedSubscription} from './types';

export const feedTopics=['AI','Technology','World','US news','Science & space','Business','Culture','Gaming','Sports','Health','Environment','General'] as const;
export type FeedTopic=typeof feedTopics[number];
export const feedSources=[
  {title:'AI & open models',publisher:'Hugging Face',topic:'AI',url:'https://huggingface.co/blog/feed.xml',description:'Open models, research and practical experiments. Publisher blog.'},
  {title:'Building software',publisher:'GitHub',topic:'Technology',url:'https://github.blog/feed/',description:'Engineering and developer tools. Publisher blog.'},
  {title:'Around the world',publisher:'BBC World',topic:'World',url:'https://feeds.bbci.co.uk/news/world/rss.xml',description:'International reporting from BBC News.'},
  {title:'The US and beyond',publisher:'PBS News',topic:'US news',url:'https://www.pbs.org/newshour/feeds/rss/headlines',description:'Reporting and analysis from PBS News Hour.'},
  {title:'Another world view',publisher:'The Guardian',topic:'World',url:'https://www.theguardian.com/world/rss',description:'World reporting and analysis. Personal RSS reading.'},
  {title:'Global perspectives',publisher:'Al Jazeera',topic:'World',url:'https://www.aljazeera.com/xml/rss/all.xml',description:'International reporting, including the Middle East.'},
  {title:'Beyond our little planet',publisher:'NASA',topic:'Science & space',url:'https://www.nasa.gov/feed/',description:'Missions, discoveries and updates from the space agency.'},
  {title:'Research worth a look',publisher:'ScienceDaily',topic:'Science & space',url:'https://www.sciencedaily.com/rss/top/science.xml',description:'Research news and summaries. Follow through to the original study.'}
] satisfies {title:string;publisher:string;topic:FeedTopic;url:string;description:string}[];

const patterns:Partial<Record<FeedTopic,RegExp>>={
  'AI':/\b(ai|artificial intelligence|llm|machine learning|language models?|hugging face)\b/i,
  'Technology':/\b(software|developer|programming|github|cybersecurity|computer|semiconductor|smartphone)\b/i,
  'World':/\b(diploma(?:cy|tic)|international|united nations|ceasefire|foreign minister|world news)\b/i,
  'US news':/\b(congress|senate|white house|supreme court|u\.s\.|united states)\b/i,
  'Science & space':/\b(nasa|astronom\w*|spacecraft|planet|physics|scientists?|telescope|archaeolog\w*)\b/i,
  'Business':/\b(economy|inflation|stock market|business|earnings|interest rates|trade deal)\b/i,
  'Culture':/\b(museum|novel|cinema|film|music|theatre|theater|literature)\b/i,
  'Gaming':/\b(video games?|gaming|nintendo|playstation|xbox|steam deck)\b/i,
  'Sports':/\b(football|basketball|baseball|soccer|olympic\w*|tennis|tournament)\b/i,
  'Health':/\b(health|medicine|medical|nutrition|fitness|workout|clinical trial)\b/i,
  'Environment':/\b(climate|biodiversity|renewable energy|conservation|pollution|ecosystem)\b/i
};
// Only broad article subjects, never a diagnosis, political identity, or a model prompt.
export function topicsFor(entry:FeedEntry,subscriptions:FeedSubscription[]):FeedTopic[]{
  const title=feedTopics.filter(topic=>patterns[topic]?.test(entry.title));
  const excerpt=feedTopics.filter(topic=>patterns[topic]?.test(entry.summary));
  const fallback=feedSources.find(item=>item.url===subscriptions.find(s=>s.id===entry.subscriptionId)?.url)?.topic||'General';
  return [...new Set([...title,...excerpt])].slice(0,2).length?[...new Set([...title,...excerpt])].slice(0,2):[fallback];
}
const hour=3600000;
export function entryTime(entry:FeedEntry,now:number){
  const received=Date.parse(entry.received),published=Date.parse(entry.published||'');
  // A publisher's future timestamp must not reserve the top of the newspaper forever.
  return Math.min(now,Number.isFinite(published)?published:Number.isFinite(received)?received:now);
}
export function dayKey(entry:FeedEntry,now:number){const d=new Date(entryTime(entry,now));return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');}
export function dayLabel(entry:FeedEntry,now:number){
  const day=new Date(entryTime(entry,now)),today=new Date(now),yesterday=new Date(now);yesterday.setDate(today.getDate()-1);
  return day.toDateString()===today.toDateString()?'Today':day.toDateString()===yesterday.toDateString()?'Yesterday':day.toLocaleDateString(undefined,{weekday:'long',month:'short',day:'numeric',year:day.getFullYear()===today.getFullYear()?undefined:'numeric'});
}
function signal(entry:FeedEntry,now:number){
  const e=entry.engagement;if(!e)return 0;
  const decay=(stamp:string|undefined,weight:number)=>{const age=(now-Date.parse(stamp||''))/hour/24;return Number.isFinite(age)&&age>=0&&age<=90?weight*Math.pow(.5,age/30):0;};
  if(e.preference&&e.preferred)return decay(e.preferred,e.preference*3);
  return Math.max(decay(e.opened,.25),decay(e.saved,1.5),decay(e.discussed,1));
}
export function feedAffinities(entries:FeedEntry[],subscriptions:FeedSubscription[],now:number){
  const topics=new Map<FeedTopic,number>(),publishers=new Map<string,number>();
  for(const entry of entries){const value=signal(entry,now);if(!value)continue;const tags=topicsFor(entry,subscriptions);
    for(const tag of tags)topics.set(tag,(topics.get(tag)||0)+value/tags.length);
    const publisher=publisherKey(entry,subscriptions);publishers.set(publisher,(publishers.get(publisher)||0)+value);
  }
  return {topics,publishers};
}
function publisherKey(entry:FeedEntry,subscriptions:FeedSubscription[]){try{return new URL(subscriptions.find(s=>s.id===entry.subscriptionId)?.url||'').hostname.replace(/^www\./,'');}catch{return entry.subscriptionId;}}
export type RankedFeedEntry={entry:FeedEntry;topics:FeedTopic[];reason:string};
export function rankFeed(all:FeedEntry[],subscriptions:FeedSubscription[],options:{now:number;personalized:boolean;source:string;topic:string;unread:boolean}):RankedFeedEntry[]{
  const {now,personalized}=options,affinity=feedAffinities(all,subscriptions,now);
  const rows=all.filter(e=>(options.source==='all'||e.subscriptionId===options.source)&&(!options.unread||!e.read))
    .map(entry=>({entry,topics:topicsFor(entry,subscriptions)})).filter(row=>options.topic==='all'||row.topics.includes(options.topic as FeedTopic));
  const timestamp=(a:typeof rows[number],b:typeof rows[number])=>entryTime(b.entry,now)-entryTime(a.entry,now)||a.entry.id.localeCompare(b.entry.id);
  if(!personalized)return rows.sort(timestamp).map(row=>({...row,reason:'Newest first. Your reading preferences do not change this order.'}));
  const strength=(value:number)=>Math.max(-1,Math.min(1,value/6));
  const scored=rows.map(row=>{
    const topic=Math.max(...row.topics.map(t=>strength(affinity.topics.get(t)||0)));
    const publisher=publisherKey(row.entry,subscriptions),preferred=strength(affinity.publishers.get(publisher)||0);
    return {...row,publisher,score:2*Math.exp(-(now-entryTime(row.entry,now))/hour/36)+topic*1.5+preferred*.5+(row.entry.engagement?.preference===-1?-6:0),
      reason:row.entry.engagement?.preference===-1?'You asked for fewer stories like this. It remains available.':topic>0?'Recent, with topics you have shown interest in: '+row.topics.filter(t=>(affinity.topics.get(t)||0)>0).join(', ')+'.':preferred>0?'Recent, from a publisher you have read, saved or discussed.':'Recent, from a source you follow.'};
  }).sort((a,b)=>dayKey(b.entry,now).localeCompare(dayKey(a.entry,now))||b.score-a.score||timestamp(a,b));
  const result:RankedFeedEntry[]=[];
  while(scored.length){
    const day=dayKey(scored[0].entry,now),group=scored.filter(row=>dayKey(row.entry,now)===day);scored.splice(0,group.length);
    const recentPublishers:string[]=[],seenTopics=new Set<string>();let count=0;
    while(group.length){
      let index=0;const available=group.map((row,i)=>({row,i})).filter(({row})=>row.entry.engagement?.preference!==-1);
      // Prefer at most two of one publisher in a rolling five, when this day's alternatives exist.
      const varied=available.filter(({row})=>recentPublishers.filter(p=>p===row.publisher).length<2);
      const pool=varied.length?varied:available;
      if(pool.length)index=pool[0].i;
      const discovery=(count+1)%5===0?pool.find(({row})=>row.topics.some(t=>!seenTopics.has(t))):undefined;
      if(discovery)index=discovery.i;
      const [chosen]=group.splice(index,1);result.push({entry:chosen.entry,topics:chosen.topics,reason:discovery?'A different topic from your followed sources, to keep the mix varied.':index>0?'Another publisher from the same day, to keep the mix varied.':chosen.reason});
      recentPublishers.push(chosen.publisher);if(recentPublishers.length>4)recentPublishers.shift();chosen.topics.forEach(t=>seenTopics.add(t));count++;
    }
  }
  return result;
}
