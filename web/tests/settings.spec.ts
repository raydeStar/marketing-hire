import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/${query?'?'+query:''}#launch=${(await issued.json()).ticket}`);
}

test('settings open from the account menu, every section is one click or key away, the theme is remembered, and browsing changes nothing',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  const directory=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(directory,{recursive:true});
  await page.setViewportSize({width:1440,height:1000});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');}catch{}});
  const mutations:string[]=[];
  page.on('request',request=>{const url=new URL(request.url());if(url.pathname.startsWith('/api/')&&!['GET','HEAD'].includes(request.method())&&url.pathname!=='/api/auth/claim-launch')mutations.push(request.method()+' '+url.pathname);});
  await launch(page,request,baseURL!);

  // Settings sit behind the account menu at the foot of the rail.
  await page.getByRole('button',{name:'Settings and account',exact:true}).click();
  await page.getByRole('menu',{name:'Settings and account'}).getByRole('menuitem',{name:'Settings'}).click();
  await expect(page).toHaveURL(/view=settings/);
  await expect(page.getByRole('heading',{level:1,name:'Settings'})).toBeVisible();

  const index=page.getByRole('navigation',{name:'Settings sections'});
  const sections:[string,string][]=[['Go-live','Go-live checklist'],['Connections','Connections'],['Google','Google app'],['Publishing','Publishing'],['Research','Research data'],['Usage','Usage'],['Appearance','Appearance'],['Notifications','Notifications'],['Workspace','Workspace'],['Account','Account']];
  await expect(index.getByRole('button')).toHaveText(sections.map(([label])=>label));
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});
    for(const [label,region] of sections){
      await page.evaluate(()=>{for(const el of [document.scrollingElement,...document.querySelectorAll('*')])if(el&&el.scrollTop)el.scrollTop=0;});
      const button=index.getByRole('button',{name:label,exact:true});
      // Alternate pointer and keyboard: both reach the section.
      if(label.length%2){await button.click();}else{await button.focus();await page.keyboard.press('Enter');}
      await expect(page.getByRole('region',{name:region,exact:true})).toBeInViewport();
    }
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    await page.evaluate(()=>scrollTo(0,0));
    await page.screenshot({path:path.join(directory,`settings-${width}.png`),fullPage:true});
  }
  await page.setViewportSize({width:1440,height:1000});

  // The owner's backup is two plain downloads; the account says who is signed in.
  const workspace=page.getByRole('region',{name:'Workspace',exact:true});
  await expect(workspace.getByRole('link',{name:'Workspace',exact:true})).toHaveAttribute('href','/api/export');
  await expect(workspace.getByRole('link',{name:'Work',exact:true})).toHaveAttribute('href','/api/export/work');
  await expect(page.getByRole('region',{name:'Account',exact:true})).toContainText('Workspace owner');

  // Theme: chosen here, applied at once, remembered across a reload, and kept in this browser only.
  const theme=page.getByRole('navigation',{name:'Theme'});
  await theme.getByRole('button',{name:'Dark'}).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
  await expect(theme.getByRole('button',{name:'Dark'})).toHaveAttribute('aria-pressed','true');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
  await expect(theme.getByRole('button',{name:'Dark'})).toHaveAttribute('aria-pressed','true');
  await theme.getByRole('button',{name:'Light'}).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme','light');
  await theme.getByRole('button',{name:'System'}).click();
  await expect(theme.getByRole('button',{name:'System'})).toHaveAttribute('aria-pressed','true');

  // People and roles hands off to Team.
  await workspace.getByRole('button',{name:/People and roles/}).click();
  await expect(page).toHaveURL(/view=team/);

  expect(mutations).toEqual([]);
});
