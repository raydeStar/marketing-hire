import {test,expect,type APIRequestContext,type BrowserContext,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

// The Library against the real fixture host: folders, tags, pins and search are the host's workspace-library ledger. No mocks.
const dataRoot=process.env.THADDEUS_TEST_DATA;
const key=()=>fs.readFileSync(path.join(dataRoot!,'host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  for(let round=0;;round++){
    // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
    let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
    for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
    expect(issued.status()).toBe(200);
    await page.goto(`/${query?'?'+query:''}#launch=${(await issued.json()).ticket}`);
    // Sign-ins are rate limited per address; if the claim was shed, wait out the window and launch again.
    const signIn=page.getByRole('heading',{name:'Open your workspace'});
    await expect(page.locator('.fe-app').or(signIn)).toBeVisible({timeout:30000});
    if(!await signIn.isVisible()||round>=2)return;
    await page.waitForTimeout(20000);
  }
}
type Meta={version:number;folders:string[];entries:{key:string;folder:string|null;tags:string[]}[];pins:string[]};
const readMeta=async(page:Page)=>page.evaluate(async()=>(await fetch('/api/workspace-library',{cache:'no-store'})).json()) as Promise<Meta>;
const rows=(page:Page)=>page.locator('.fe-library-table tbody tr');
const row=(page:Page,title:string)=>rows(page).filter({hasText:title});

test('documents are filed, tagged, pinned, found by related words, and follow their folder',async({page,request,baseURL})=>{
  test.skip(!dataRoot,'Needs a disposable fixture host (THADDEUS_TEST_DATA).');
  test.setTimeout(90000);
  const stamp=Date.now().toString(36),folder='Launch plans '+stamp,renamed='Q4 plans '+stamp,title='Spring brief '+stamp;
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');}catch{}});
  await launch(page,request,baseURL!,'view=library');
  const nav=page.getByRole('navigation',{name:'Library'});
  const tree=nav.getByRole('tree',{name:'Folders'});
  const rail=page.getByRole('complementary',{name:'Main navigation'});
  await expect(page.getByRole('heading',{name:'All items',level:1})).toBeVisible();
  for(const home of ['Company','Research','Campaigns','Pages & apps','Media'])
    await expect(tree.getByRole('button',{name:new RegExp('^'+home.replace('&','&'))})).toBeVisible();

  // A custom top-level folder, then a document from a template created inside it.
  await nav.getByRole('button',{name:'New folder'}).click();
  await page.getByRole('dialog',{name:'New folder'}).getByLabel('Folder name').fill(folder);
  await page.getByRole('dialog',{name:'New folder'}).getByRole('button',{name:'Save'}).click();
  await expect(page.getByRole('heading',{name:folder,level:1})).toBeVisible();
  await expect(page.getByRole('main').getByText('Nothing here yet')).toBeVisible();
  await page.getByRole('button',{name:'New',exact:true}).click();
  await page.getByRole('menuitem',{name:'Document'}).click();
  await page.getByRole('dialog',{name:'New document'}).getByRole('button',{name:/One-page campaign brief/}).click();
  const form=page.getByRole('form',{name:'New document'});
  await expect(form.getByLabel('Content')).not.toHaveValue('');
  await form.getByLabel('Title').fill(title);
  await form.getByLabel('Content').fill(`# ${title}\n\nAudience: founders who run their own marketing.\n\nOffer: a reviewed weekly plan.`);
  await form.getByLabel('Status').selectOption('active');
  await form.getByRole('button',{name:'Create document'}).click();
  const reader=page.getByRole('region',{name:title});
  await expect(reader.locator('.fe-window-title small')).toHaveText(new RegExp(` · ${folder}$`));
  await expect(reader).toContainText('founders who run their own marketing');
  const url=new URL(page.url());
  expect(url.searchParams.get('view')).toBe('library');
  const itemKey=url.searchParams.get('open')!;
  expect(itemKey).toMatch(/^wiki:/);
  await expect.poll(async()=>(await readMeta(page)).entries.find(entry=>entry.key===itemKey)?.folder).toBe(folder);
  await reader.getByRole('button',{name:'Close',exact:true}).click();
  await expect(page.getByRole('heading',{name:folder,level:1})).toBeVisible();
  await expect(rows(page)).toHaveCount(1);
  await expect(row(page,title)).toContainText('Playbook');

  // Tag it in the Folder and tags dialog, then filter by the tag.
  await row(page,title).click();
  await reader.getByRole('button',{name:'Folder and tags'}).click();
  const filing=page.getByRole('dialog',{name:`File “${title}”`});
  await expect(filing.getByRole('combobox')).toHaveValue(folder);
  await filing.getByLabel('Tags').fill(`launch-${stamp}, q4`);
  await filing.getByRole('button',{name:'Save'}).click();
  await expect(filing).toHaveCount(0);
  await expect(reader.locator('.fe-window-tags')).toHaveText(`launch-${stamp}q4`);
  await reader.getByRole('button',{name:'Close',exact:true}).click();
  await nav.getByRole('button',{name:`launch-${stamp}`,exact:true}).click();
  await expect(page.getByRole('heading',{name:`#launch-${stamp}`})).toBeVisible();
  await expect(rows(page)).toHaveCount(1);
  await expect(row(page,title)).toContainText(folder);

  // Pin it; the rail opens it from anywhere.
  await row(page,title).click();
  await reader.getByRole('button',{name:'Pin to sidebar'}).click();
  await expect(reader.getByRole('button',{name:'Unpin from sidebar'})).toBeVisible();
  const pinned=rail.getByRole('navigation',{name:'Pinned'}).getByRole('button',{name:title});
  await expect(pinned).toBeVisible();
  await rail.getByRole('button',{name:'Team'}).click();
  await expect(page.getByRole('heading',{name:'Team',level:1})).toBeVisible();
  await pinned.click();
  await expect(page).toHaveURL(new RegExp(`view=library&open=${encodeURIComponent(itemKey)}`));
  await expect(reader).toContainText('founders who run their own marketing');
  await expect(pinned).toHaveAttribute('aria-current','page');
  await reader.getByRole('button',{name:'Close',exact:true}).click();
  await nav.getByRole('button',{name:/^Pinned/}).click();
  await expect(row(page,title)).toBeVisible();

  // Search matches related words: "customer" finds a document that only says "audience".
  const search=nav.getByLabel('Search the library');
  await search.fill('customer');
  await expect(page.getByRole('heading',{name:'Results for “customer”'})).toBeVisible();
  await expect(row(page,title)).toContainText('Audience: founders');
  await search.fill(`customers ${stamp}`);
  await expect(rows(page).first()).toContainText(title);
  await search.fill(`zzqx${stamp}`);
  await expect(page.getByText('Nothing matches that search')).toBeVisible();
  await nav.getByRole('button',{name:'Clear search'}).click();

  // Renaming the folder carries the document with it.
  await tree.getByRole('button',{name:new RegExp('^'+folder)}).click();
  await page.getByRole('button',{name:'Rename'}).click();
  const rename=page.getByRole('dialog',{name:'Rename folder'});
  await expect(rename.getByLabel('Folder name')).toHaveValue(folder);
  await rename.getByLabel('Folder name').fill(renamed);
  await rename.getByRole('button',{name:'Save'}).click();
  await expect(page.getByRole('heading',{name:renamed,level:1})).toBeVisible();
  await expect(row(page,title)).toBeVisible();
  await expect(tree.getByRole('button',{name:new RegExp('^'+folder)})).toHaveCount(0);
  await expect.poll(async()=>(await readMeta(page)).entries.find(entry=>entry.key===itemKey)?.folder).toBe(renamed);

  // Deleting the folder returns the document to Company, with its tags; nothing is deleted.
  page.once('dialog',confirm=>{expect(confirm.message()).toContain('Nothing is deleted');void confirm.accept();});
  await page.getByRole('button',{name:'Delete folder'}).click();
  await expect(page.getByRole('heading',{name:'All items',level:1})).toBeVisible();
  await expect(tree.getByRole('button',{name:new RegExp('^'+renamed)})).toHaveCount(0);
  await search.fill(stamp);
  await expect(row(page,title)).toContainText('Company');
  await expect(row(page,title)).toContainText(`launch-${stamp}`);
  const after=await readMeta(page);
  expect(after.folders).not.toContain(renamed);
  expect(after.entries.find(entry=>entry.key===itemKey)?.folder??null).toBeNull();
  await search.fill('');
  await tree.getByRole('button',{name:/^Company/}).click();
  await expect(row(page,title)).toBeVisible();

  // Unpin so later runs start with a short rail.
  await row(page,title).click();
  await reader.getByRole('button',{name:'Unpin from sidebar'}).click();
  await expect(pinned).toHaveCount(0);
});

test('a contributor teammate reads and edits the Library; a viewer is sent to shared campaigns',async({browser,baseURL})=>{
  test.skip(!dataRoot,'Needs a disposable fixture host (THADDEUS_TEST_DATA).');
  test.setTimeout(90000);
  const origin=baseURL!,stamp=Date.now().toString(36),title='Teammate notes '+stamp;
  const owner=await browser.newContext({baseURL:origin,viewport:{width:1440,height:900}});
  const teammate=await browser.newContext({baseURL:origin,viewport:{width:1280,height:800}});
  for(const context of [owner,teammate])await context.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');}catch{}});
  const post=(context:BrowserContext,route:string,body:unknown,csrf:string)=>context.request.post(origin+route,{headers:{Origin:origin,'X-CSRF':csrf},data:body});
  try{
    const ownerLogin=await owner.request.post(origin+'/api/auth/login',{headers:{Origin:origin},data:{key:key()}});
    expect(ownerLogin.status()).toBe(200);
    const ownerSession=await ownerLogin.json() as {csrf:string};
    const bootstrap=await teammate.request.post(origin+'/api/auth/login',{headers:{Origin:origin},data:{key:key()}});
    expect(bootstrap.status()).toBe(200);
    const fixture=await post(teammate,'/api/marketing/fixture/collaborator-session',{},(await bootstrap.json() as {csrf:string}).csrf);
    expect(fixture.status()).toBe(200);
    const member=await fixture.json() as {id:string;owner:boolean};
    expect(member.owner).toBe(false);
    const setRole=async(role:string)=>{
      const response=await owner.request.put(origin+'/api/team/roles/'+member.id,{headers:{Origin:origin,'X-CSRF':ownerSession.csrf},data:{role}});
      expect(response.status()).toBe(200);
    };
    // The owner files a document in Research; the teammate should find it there.
    const saved=await owner.request.put(origin+'/api/company-wiki',{headers:{Origin:origin,'X-CSRF':ownerSession.csrf},data:{requestId:crypto.randomUUID(),id:null,version:0,scope:'company',scopeId:'company',
      title,body:'# Notes\nThe owner wrote this for the team.',kind:'fact',status:'active'}});
    expect(saved.status(),await saved.text()).toBe(200);
    const doc=await saved.json() as {id:string};
    const meta=await (await owner.request.get(origin+'/api/workspace-library')).json() as Meta;
    const filed=await owner.request.put(origin+'/api/workspace-library/entries/'+encodeURIComponent('wiki:'+doc.id),{headers:{Origin:origin,'X-CSRF':ownerSession.csrf},
      data:{expectedVersion:meta.version,folder:'Research',tags:['team-'+stamp]}});
    expect(filed.status(),await filed.text()).toBe(200);

    await setRole('contributor');
    const page=await teammate.newPage();
    const crashes:string[]=[];page.on('pageerror',error=>crashes.push(error.message));
    await page.goto('/');
    const rail=page.getByRole('complementary',{name:'Main navigation'});
    await rail.getByRole('button',{name:'Library'}).click();
    const nav=page.getByRole('navigation',{name:'Library'});
    await nav.getByRole('tree',{name:'Folders'}).getByRole('button',{name:/^Research/}).click();
    await expect(page.getByRole('alert')).toHaveCount(0);
    await rows(page).filter({hasText:title}).click();
    const reader=page.getByRole('region',{name:title});
    await expect(reader).toContainText('The owner wrote this for the team.');
    await expect(reader.locator('.fe-window-tags')).toHaveText('team-'+stamp);
    await reader.getByRole('button',{name:'Edit',exact:true}).click();
    const form=reader.getByRole('form',{name:'Edit document'});
    await form.getByLabel('Content').fill('# Notes\nThe owner wrote this for the team. A contributor added a line.');
    await form.getByRole('button',{name:'Save changes'}).click();
    await expect(reader).toContainText('A contributor added a line.');
    await expect(reader).toContainText('Version 2');
    // Filing is shared; the teammate can add a tag too.
    await reader.getByRole('button',{name:'Folder and tags'}).click();
    await page.getByRole('dialog',{name:`File “${title}”`}).getByLabel('Tags').fill(`team-${stamp}, reviewed-${stamp}`);
    await page.getByRole('dialog',{name:`File “${title}”`}).getByRole('button',{name:'Save'}).click();
    await expect(reader.locator('.fe-window-tags')).toHaveText(`team-${stamp}reviewed-${stamp}`);
    const wiki=await (await owner.request.get(origin+'/api/company-wiki')).json() as {id:string;version:number;body:string;author:string}[];
    const edited=wiki.find(item=>item.id===doc.id)!;
    expect(edited.version).toBe(2);
    expect(edited.body).toContain('A contributor added a line.');
    expect(edited.author).toMatch(/^Member /);

    // A viewer has no Library: the rail offers Work only and the Library URL lands on shared campaigns.
    await setRole('viewer');
    await page.goto('/?view=library&open='+encodeURIComponent('wiki:'+doc.id));
    await expect(page.getByRole('heading',{name:'Shared campaigns',level:1})).toBeVisible();
    await expect(page).not.toHaveURL(/view=library/);
    await expect(rail.getByRole('navigation',{name:'Main views'}).getByRole('button')).toHaveText(['Work']);
    await expect(page.getByRole('navigation',{name:'Library'})).toHaveCount(0);
    await expect(page.getByText(title)).toHaveCount(0);
    expect((await page.request.get(origin+'/api/workspace-library')).status()).toBe(403);
    expect(crashes).toEqual([]);
  }finally{await owner.close();await teammate.close();}
});
