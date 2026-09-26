// A screenshot of every screen of a running workspace, for a UX pass. Local only: it signs in with the host key and changes nothing.
//   node web/tools/ux-tour.mjs --data <fixture>/host --out <folder> [--origin http://localhost:5190] [--theme dark|light] [--only <regex of shot names>]
import fs from 'node:fs';
import path from 'node:path';
import {chromium} from '@playwright/test';

const args=Object.fromEntries(process.argv.slice(2).reduce((pairs,value,index,all)=>value.startsWith('--')?[...pairs,[value.slice(2),all[index+1]&&!all[index+1].startsWith('--')?all[index+1]:'yes']]:pairs,[]));
const origin=args.origin||'http://localhost:5190';
const out=path.resolve(args.out||'ux-tour');
fs.mkdirSync(out,{recursive:true});
const key=fs.readFileSync(path.join(args.data,'host-key.txt'),'utf8').trim();
const json={Origin:origin,'Content-Type':'application/json'};

const login=await fetch(origin+'/api/auth/login',{method:'POST',headers:json,body:JSON.stringify({key})});
const cookie=login.headers.get('set-cookie')?.split(';')[0];
const get=async pathname=>{const response=await fetch(origin+'/api'+pathname,{headers:{Origin:origin,Cookie:cookie}});return response.ok?response.json():null;};
const [wiki,state,files,proposals,campaigns]=await Promise.all([get('/company-wiki'),get('/marketing/state'),get('/state'),get('/page-proposals'),get('/campaigns')]);
const doc=pattern=>wiki?.filter(page=>pattern.test(page.title)&&page.status!=='archived').sort((a,b)=>b.updatedAt.localeCompare(a.updatedAt))[0]?.id;
const upload=pattern=>(files?.uploads||[]).find(file=>pattern.test(file.name)&&!file.archived)?.id;
const draft=pattern=>[...(state?.drafts||[])].sort((a,b)=>b.id-a.id).find(item=>pattern.test(item.channel))?.id;
const source=(state?.evidence||[]).find(item=>/jasper/i.test(item.url))?.id||(state?.evidence||[])[0]?.id;
const task=(state?.tasks||[]).find(item=>item.status==='needs_you')?.id;

const shots=[];
const shot=(name,url,act)=>shots.push({name,url,act});
const scrollTo=label=>async page=>{await page.evaluate(name=>document.querySelector(`section[aria-label="${name}"]`)?.scrollIntoView({block:'start'}),label);};
const click=text=>async page=>{await page.getByText(text,{exact:true}).first().click();};
const clickRole=(role,name)=>async page=>{await page.getByRole(role,{name,exact:true}).first().click();};

shot('01-chat','/?pane=chat');
shot('02-chip-usage','/?pane=chat',async page=>{await page.hover('.fe-status-wrap');});
shot('03-work','/?pane=work');
for(const [index,label] of ['Scorecard','This week','Content calendar','Listening','Board','Recent activity'].entries())shot(`04-work-${index+1}-${label.toLowerCase().replaceAll(' ','-')}`,'/?pane=work',scrollTo(label));
shot('05-cockpit-shift','/?pane=work',async page=>{await page.getByRole('button',{name:/Start shift/}).first().click();});
shot('06-search','/?pane=chat',async page=>{await page.keyboard.press('Control+k');await page.waitForTimeout(300);await page.keyboard.type('battlecard');});
shot('07-objectives','/?pane=work&open=brief:objectives');
shot('08-library','/?view=library');
for(const folder of ['Videos','Images','Drafts','SEO','Sources','Company'])shot(`09-library-${folder.toLowerCase()}`,'/?view=library',click(folder));
const docs={'10-doc-battlecard':doc(/battlecard/i),'11-doc-storyboard':doc(/shift, step by step|in 30 seconds/i),'12-doc-monthly':doc(/^monthly report/i),'13-doc-weekly':doc(/^weekly/i),
  '14-doc-shift-report':doc(/^shift report/i),'15-doc-site-check':doc(/^site check/i),'16-doc-notebook':doc(/^marketing notebook/i),'17-doc-decision-log':doc(/^decision log/i),'18-doc-interview-kit':doc(/interview kit/i),'19-doc-launch-plan':doc(/launch-week plan/i)};
for(const [name,id] of Object.entries(docs))if(id)shot(name,`/?view=library&open=wiki:${id}`);
const video=upload(/step-by-step\.mp4$/)||upload(/\.mp4$/),image=upload(/-image\.png$/);
if(video)shot('20-media-video',`/?view=library&open=media:${video}`);
if(image)shot('21-media-image',`/?view=library&open=media:${image}`);
if(source)shot('22-source',`/?view=library&open=source:${source}`);
for(const [name,channel] of [['23-draft-linkedin',/linkedin/i],['24-draft-x',/^x$/i],['25-draft-email',/email/i],['26-draft-blog',/blog/i],['27-draft-hn',/hacker/i]]){const id=draft(channel);if(id)shot(name,`/?pane=work&open=draft:${id}`);}
if(task)shot('28-task',`/?pane=work&open=task:${task}`);
const media=await get('/drafts/media');
const withMedia=Object.keys(media||{}).find(id=>media[id].length&&state?.drafts?.some(item=>String(item.id)===id));
if(withMedia)shot('23b-draft-with-media',`/?pane=work&open=draft:${withMedia}`,async page=>{await page.locator('.fe-draft-feedback input').fill('Open with what a founder told us.').catch(()=>{});await page.locator('.fe-attachments').scrollIntoViewIfNeeded().catch(()=>{});});
if(docs['10-doc-battlecard'])shot('10b-doc-redraft',`/?view=library&open=wiki:${docs['10-doc-battlecard']}`,async page=>{await page.getByRole('button',{name:'Redraft it'}).click();await page.locator('.fe-rate-note textarea').fill('Lead with where HireZero wins, and cut the table to three rows.');await page.locator('.fe-rate').scrollIntoViewIfNeeded();});
const proposal=proposals?.proposals?.[0]?.id;if(proposal)shot('29-page-copy',`/?pane=work&open=pagecopy:${proposal}`);
const campaign=campaigns?.campaigns?.[0];
if(campaign){
  shot('29b-campaign-page',`/?pane=work&open=campaign:${campaign.id}`);
  shot('29c-campaign-library','/?view=library',click(campaign.name));
  const inCampaign=Object.entries(campaigns.items).find(([key,id])=>id===campaign.id&&key.startsWith('draft:'))?.[0];
  if(inCampaign)shot('29d-campaign-draft',`/?pane=work&open=${inCampaign}`);
  if(campaign.planWikiId)shot('29e-campaign-plan',`/?view=library&open=wiki:${campaign.planWikiId}`);
}
shot('30-team-people','/?view=team');
shot('31-team-employees','/?view=team',clickRole('button','AI employees'));
for(const [index,tab] of ['Instructions','Permissions','Business brief','Usage'].entries())
  shot(`32-employee-${index+1}-${tab.toLowerCase()}`,'/?view=team',async page=>{await page.getByRole('button',{name:'AI employees',exact:true}).click();await page.locator('.fe-list-row').first().click();await page.waitForTimeout(500);await page.getByRole('button',{name:tab,exact:true}).first().click();});
shot('33-team-roles','/?view=team',clickRole('button','Roles & permissions'));
const toBrief=async page=>{await page.getByRole('button',{name:'AI employees',exact:true}).click();await page.locator('.fe-list-row').first().click();await page.waitForTimeout(500);await page.getByRole('button',{name:'Business brief',exact:true}).first().click();await page.waitForTimeout(400);};
shot('38-first-steps','/?pane=work',scrollTo('Board'));
shot('36-onboarding-welcome','/?view=team',async page=>{await toBrief(page);await page.getByRole('button',{name:/Redo onboarding/}).click();});
shot('37-onboarding-sales-import','/?view=team',async page=>{await toBrief(page);await page.route('**/api/workspace-role',route=>route.request().method()==='PUT'?route.fulfill({json:{role:'sales',person:'',offer:'',disclosure:''}}):route.continue());await page.getByRole('button',{name:/Redo onboarding/}).click();await page.locator('.fe-onboarding .fe-role-options label',{hasText:'I sell it'}).click();await page.getByRole('button',{name:/Learn from my website/}).click();});
for(const [index,label] of ['Go-live checklist','Publishing','Google app','Research data','Usage','Appearance'].entries())shot(`34-settings-${index+1}-${label.toLowerCase().replaceAll(' ','-')}`,'/?view=settings',scrollTo(label));

const browser=await chromium.launch();
const results=[];
for(const [label,viewport] of [['desktop',{width:1440,height:900}],['phone',{width:390,height:844}]]){
  const context=await browser.newContext({viewport,deviceScaleFactor:1,colorScheme:args.theme==='light'?'light':'dark'});
  await context.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const page=await context.newPage();
  const errors=[];page.on('pageerror',error=>errors.push(error.message));page.on('console',message=>{if(message.type()==='error')errors.push(message.text());});
  const issued=await (await fetch(origin+'/api/auth/launch',{method:'POST',headers:json,body:JSON.stringify({key})})).json();
  await page.goto(`${origin}/?pane=chat#launch=${issued.ticket}`);
  await page.waitForSelector('.fe-app',{timeout:30000});await page.waitForTimeout(1500);
  const only=args.only?new RegExp(args.only):null;
  for(const {name,url,act} of (label==='phone'?shots.filter(item=>/^(01|03|08|10|23|29b|31|34-settings-1)/.test(item.name)):shots).filter(item=>!only||only.test(item.name))){
    errors.length=0;
    try{
      await page.goto(origin+url);await page.waitForSelector('.fe-app',{timeout:15000});await page.waitForTimeout(900);
      if(act){await act(page);await page.waitForTimeout(700);}
      if(!name.includes('chip-usage'))await page.mouse.move(2,page.viewportSize().height-2);
      const file=path.join(out,`${name}-${label}.png`);
      await page.screenshot({path:file,animations:'disabled'});
      const text=await page.evaluate(()=>document.body.innerText);
      results.push({shot:`${name}-${label}`,ok:true,errors:[...errors],unavailable:/no longer available|Something went wrong/i.test(text)});
    }catch(error){results.push({shot:`${name}-${label}`,ok:false,error:error.message.split('\n')[0]});}
    if(page.url().includes('#'))await page.goto(origin+'/?pane=chat');
    await page.keyboard.press('Escape').catch(()=>{});
  }
  await context.close();
}
await browser.close();
fs.writeFileSync(path.join(out,'tour.json'),JSON.stringify(results,null,2));
console.log(`${results.filter(item=>item.ok).length}/${results.length} screens; problems:`);
for(const item of results.filter(item=>!item.ok||item.errors?.length||item.unavailable))console.log(' ',item.shot,item.error||'',item.unavailable?'(says unavailable)':'',(item.errors||[]).slice(0,2).join(' | '));
