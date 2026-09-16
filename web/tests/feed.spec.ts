import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('feed discovery, subscriptions and saved reading remain distinct on desktop and mobile',async({page})=>{
  const directory=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(directory,{recursive:true});
  await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
  const before=await page.evaluate(async()=>(await fetch('/api/export')).json());
  // Deterministic browser projection. Real storage, transport and API contracts are exercised in FeedTests/FeedApiTests.
  const source='https://gazette.example.org/',feedUrl='https://feeds.example.org/gazette.xml';
  const stamp=new Date().toISOString();let revision=0,subscriptions:any[]=[],entries:any[]=[],saved:any[]=[];
  const calls:{path:string;method:string;body:any}[]=[];
  await page.route('**/api/state',async route=>{const actual=await (await route.fetch()).json();await route.fulfill({json:{...actual,feeds:{subscriptions,entries,revision:String(revision)},library:[...actual.library,...saved]}});});
  await page.route(url=>url.pathname==='/api/feeds'||url.pathname.startsWith('/api/feeds/'),async route=>{
    const request=route.request(),url=new URL(request.url()),body=request.postDataJSON();calls.push({path:url.pathname,method:request.method(),body});
    if(url.pathname==='/api/feeds/preview'){
      await route.fulfill({json:body.url===source?{url:source,candidates:[{title:'The Gazette',url:feedUrl}]}:{url:feedUrl,feed:{title:'The Gazette',entries:[{title:'The garden notebook'}],truncated:false}}});return;
    }
    if(url.pathname==='/api/feeds'){
      subscriptions=[{id:'fixture-source',title:'The Gazette',url:feedUrl,paused:false,version:'v1',created:stamp,lastChecked:stamp,nextRefresh:stamp,failures:0,truncated:false}];
      entries=[{id:'fixture-entry',subscriptionId:'fixture-source',key:'one',title:'The garden notebook',summary:'A source excerpt about small experiments. <img src=x onerror=alert(1)>',url:'https://gazette.example.org/garden',published:stamp,received:stamp,read:false,version:'v1'}];
    }else if(url.pathname.endsWith('/remove')){subscriptions=[];entries=[];}
    else if(url.pathname.endsWith('/refresh')){await route.fulfill({status:409,json:{error:'Wait five minutes between refresh attempts for this source.'}});return;}
    else subscriptions=subscriptions.map(item=>({...item,paused:body.paused,version:'v'+(++revision)}));
    revision++;await route.fulfill({json:{}});
  });
  await page.route('**/api/feed-entries/**',async route=>{
    const request=route.request(),url=new URL(request.url()),body=request.postDataJSON();calls.push({path:url.pathname,method:request.method(),body});
    if(url.pathname.endsWith('/save')){
      entries[0].savedItemId='b'.repeat(32);const entry=entries[0];
      saved=[{id:entry.savedItemId,kind:'feed',title:entry.title,content:entry.summary,url:entry.url,status:'open',due:null,version:'saved-v1',created:stamp,updated:stamp}];
    }else if(!url.pathname.endsWith('/feedback')) entries=entries.map(entry=>({...entry,read:body.read,version:'v'+(++revision)}));
    revision++;await route.fulfill({json:{}});
  });
  await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();await page.getByRole('button',{name:'Feed',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Give your feed a starting point'})).toBeVisible();
  await page.getByRole('button',{name:'Add a source',exact:true}).click();
  await page.getByLabel('Website or feed address').fill(source);
  await page.getByRole('button',{name:'Preview source',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Choose a feed'})).toBeVisible();expect(calls).toHaveLength(1);
  await page.getByRole('button',{name:/The Gazette https/}).click();
  await expect(page.getByRole('button',{name:'Subscribe to The Gazette',exact:true})).toBeVisible();
  expect(calls.map(call=>call.body.url)).toEqual([source,feedUrl]);
  await page.getByRole('button',{name:'Subscribe to The Gazette',exact:true}).click();
  const entry=page.getByRole('article',{name:'Update: The garden notebook'});
  await expect(entry).toBeVisible();expect(calls.filter(call=>call.path==='/api/feeds')).toHaveLength(1);
  await expect(entry.locator('img,iframe,script')).toHaveCount(0);
  await expect(entry.getByRole('link',{name:'Read source'})).toHaveAttribute('rel','noreferrer');
  await page.getByRole('button',{name:'Save link',exact:true}).click();
  await expect(entry.getByRole('button',{name:'Saved',exact:true})).toBeDisabled();
  await page.getByRole('button',{name:'Mark read: The garden notebook',exact:true}).click();await expect(entry).toHaveCount(0);
  await page.getByRole('button',{name:'Unread only',exact:true}).click();await expect(entry).toBeVisible();
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});
    if(width===390)await page.keyboard.press('Escape');
    await expect(page.getByRole('button',{name:/token usage/})).toBeVisible();
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:path.join(directory,`feed-updates-${width}.png`),fullPage:true});
  }
  const sources=page.locator('.feed-sources');if(!await sources.evaluate(element=>(element as HTMLDetailsElement).open))await sources.locator('summary').click();
  const sourceCard=page.getByRole('article',{name:'Source: The Gazette'});
  await sourceCard.getByRole('button',{name:'Pause',exact:true}).click();await expect(sourceCard.getByRole('button',{name:'Refresh',exact:true})).toBeDisabled();
  await sourceCard.getByRole('button',{name:'Resume',exact:true}).click();await sourceCard.getByRole('button',{name:'Refresh',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('Wait five minutes');
  await page.evaluate(()=>window.dispatchEvent(new Event('offline')));await expect(page.getByRole('button',{name:'Add a source',exact:true})).toBeDisabled();
  await expect(sourceCard.getByRole('button',{name:'Pause',exact:true})).toBeDisabled();
  await page.evaluate(()=>window.dispatchEvent(new Event('online')));await expect(sourceCard.getByRole('button',{name:'Pause',exact:true})).toBeEnabled();
  await sourceCard.getByRole('button',{name:'Remove source',exact:true}).click();await expect(page.getByText('Your saved links stay.',{exact:false})).toBeVisible();
  await page.getByRole('button',{name:'Confirm removal',exact:true}).click();await expect(sourceCard).toHaveCount(0);
  await page.getByRole('navigation',{name:'Feed sections'}).getByRole('button',{name:/Saved links/}).click();
  await expect(page.getByRole('heading',{name:'The garden notebook',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Discuss',exact:false}).click();
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
  await expect(page.getByRole('textbox',{name:'Message or goal',exact:true})).toHaveValue(/The garden notebook/);
  expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
});
