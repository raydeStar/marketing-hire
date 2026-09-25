import {test,expect,type BrowserContext,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

// The owner changes a paired teammate's role and the teammate's workspace follows it on the next read.
test('the owner assigns viewer, contributor and manager roles and the teammate sees exactly that workspace',async({browser,baseURL})=>{
  test.setTimeout(90000);
  const origin=baseURL!;
  const dataRoot=process.env.THADDEUS_TEST_DATA;
  test.skip(!dataRoot,'Needs a disposable fixture host (THADDEUS_TEST_DATA).');
  const key=fs.readFileSync(path.join(dataRoot!,'host-key.txt'),'utf8').trim();
  const owner=await browser.newContext({baseURL:origin,viewport:{width:1440,height:900}});
  const teammate=await browser.newContext({baseURL:origin,viewport:{width:1280,height:800}});
  for(const context of [owner,teammate])await context.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  const post=(context:BrowserContext,route:string,body:unknown,csrf:string)=>context.request.post(origin+route,{headers:{Origin:origin,'X-CSRF':csrf},data:body});
  try{
    const ownerLogin=await owner.request.post(origin+'/api/auth/login',{headers:{Origin:origin},data:{key}});
    expect(ownerLogin.status()).toBe(200);
    const ownerSession=await ownerLogin.json() as {csrf:string};
    const bootstrap=await teammate.request.post(origin+'/api/auth/login',{headers:{Origin:origin},data:{key}});
    const fixture=await post(teammate,'/api/marketing/fixture/collaborator-session',{},(await bootstrap.json() as {csrf:string}).csrf);
    expect(fixture.status()).toBe(200);
    const member=await fixture.json() as {id:string;owner:boolean};
    expect(member.owner).toBe(false);
    const setRole=async(role:string)=>{
      const response=await owner.request.put(origin+'/api/team/roles/'+member.id,{headers:{Origin:origin,'X-CSRF':ownerSession.csrf},data:{role}});
      expect(response.status()).toBe(200);
    };
    const rail=(page:Page)=>page.getByRole('complementary',{name:'Main navigation'});
    const views=(page:Page)=>rail(page).getByRole('navigation',{name:'Main views'}).getByRole('button');
    const roleInMenu=async(page:Page,label:string)=>{
      await rail(page).getByRole('button',{name:'Settings and account'}).click();
      await expect(page.getByRole('menu',{name:'Settings and account'})).toContainText(label+' ·');
      await page.keyboard.press('Escape');
    };

    // A contributor works on tasks, the Library and the team pages, but cannot chat or decide.
    await setRole('contributor');
    const page=await teammate.newPage();
    await page.goto('/');
    await expect(views(page)).toHaveText(['Work','Search','Library','Team']);
    await expect(page).toHaveURL(/pane=work/);
    await expect(page.getByRole('navigation',{name:'Chat or work'}).getByRole('button')).toHaveText(['Work']);
    await expect(page.getByRole('region',{name:'Board'})).toBeVisible();
    await expect(page.getByLabel('Message to marketing employee')).toHaveCount(0);
    await roleInMenu(page,'Contributor');
    await rail(page).getByRole('button',{name:'Team'}).click();
    await expect(page.getByText('Your role: Contributor')).toBeVisible();
    await expect(page.getByRole('navigation',{name:'Team sections'}).getByRole('button')).toHaveText(['AI employees','Roles & permissions']);
    await expect(page.getByRole('button',{name:'Add AI employee'})).toHaveCount(0);
    // A chat link is refused back to Work.
    await page.goto('/?pane=chat');
    await expect(page).toHaveURL(/pane=work/);

    // A manager also chats, and the cockpit shows what waits on the owner.
    await setRole('manager');
    await page.reload();
    await expect(views(page)).toHaveText(['Chat','Search','Library','Team']);
    await page.getByRole('navigation',{name:'Chat or work'}).getByRole('button',{name:'Chat'}).click();
    await expect(page.getByLabel('Message to marketing employee')).toBeVisible();
    await expect(page.getByRole('complementary',{name:'Cockpit'}).getByRole('region',{name:'Waiting on the owner'})).toBeVisible();
    await roleInMenu(page,'Manager');

    // A viewer only reads the campaigns the owner shared.
    await setRole('viewer');
    const crashes:string[]=[];page.on('pageerror',error=>crashes.push(error.message));
    await page.reload();
    await expect(views(page)).toHaveText(['Work']);
    await expect(page.getByRole('heading',{name:'Shared campaigns'})).toBeVisible();
    await expect(page.getByRole('complementary',{name:'Cockpit'})).toHaveCount(0);
    await roleInMenu(page,'Viewer');
    await page.goto('/?view=library');
    await expect(page.getByRole('heading',{name:'Shared campaigns'})).toBeVisible();
    await expect(page.getByRole('navigation',{name:'Library'})).toHaveCount(0);
    // The viewer's state omits the brief; nothing may crash on its absence.
    expect(crashes).toEqual([]);

    // The owner sees and changes the role in Team → People.
    const ownerPage=await owner.newPage();
    await ownerPage.goto('/');
    await rail(ownerPage).getByRole('button',{name:'Team'}).click();
    await expect(ownerPage.getByRole('navigation',{name:'Team sections'}).getByRole('button',{name:'People'})).toHaveAttribute('aria-pressed','true');
    // Earlier runs may leave other paired teammates on this fixture; pick this teammate's row.
    const row=ownerPage.getByRole('region',{name:'Members'}).getByRole('row').filter({hasText:member.id.slice(0,8)});
    const select=row.getByRole('combobox',{name:/^Role for /});
    await expect(select).toHaveValue('viewer');
    await select.selectOption('reviewer');
    await expect(ownerPage.getByRole('status').filter({hasText:'is now a reviewer'})).toBeVisible();
    const roles=await (await owner.request.get(origin+'/api/team/roles')).json() as {principalId:string;role:string}[];
    expect(roles.find(entry=>entry.principalId===member.id)?.role).toBe('reviewer');
    await page.reload();
    await expect(page.getByRole('heading',{name:'Shared campaigns'})).toBeVisible();
    await roleInMenu(page,'Reviewer');
  }finally{await owner.close();await teammate.close();}
});
