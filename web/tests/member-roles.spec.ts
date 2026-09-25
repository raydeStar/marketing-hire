import {test,expect,type BrowserContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

// The owner changes a paired teammate's role and the teammate's workspace follows it on the next read.
test('the owner assigns viewer, contributor and manager roles and the teammate sees exactly that workspace',async({browser,baseURL})=>{
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
    const rail=(page:import('@playwright/test').Page)=>page.getByRole('complementary',{name:'Main navigation'});

    await setRole('contributor');
    const page=await teammate.newPage();
    await page.goto('/');
    await expect(rail(page).getByRole('button',{name:'Tasks'})).toBeVisible();
    await expect(rail(page).getByRole('button',{name:'Wiki'})).toBeVisible();
    await expect(rail(page).getByRole('button',{name:'Assets'})).toBeVisible();
    await expect(rail(page).getByRole('button',{name:'Chat'})).toHaveCount(0);
    await expect(rail(page).getByRole('button',{name:'Inbox'})).toHaveCount(0);
    await expect(page.getByText('Contributor',{exact:true})).toBeVisible();
    await rail(page).getByRole('button',{name:'Team'}).click();
    await expect(page.getByRole('button',{name:'Add teammate'})).toHaveCount(0);

    await setRole('manager');
    await page.reload();
    await expect(rail(page).getByRole('button',{name:'Chat'})).toBeVisible();
    await expect(rail(page).getByRole('button',{name:'Inbox'})).toBeVisible();
    await expect(page.getByText('Manager',{exact:true})).toBeVisible();

    await setRole('viewer');
    await page.reload();
    await expect(rail(page).getByRole('button',{name:'Shared campaigns'})).toBeVisible();
    await expect(rail(page).getByRole('button',{name:'Tasks'})).toHaveCount(0);
    await expect(page.getByRole('heading',{name:'Shared campaigns'})).toBeVisible();
    await expect(page.getByText('Viewer',{exact:true})).toBeVisible();

    // The owner sees and can change the role in Settings → Team access.
    const ownerPage=await owner.newPage();
    await ownerPage.goto('/');
    await rail(ownerPage).getByRole('button',{name:'Settings'}).click();
    // Earlier runs may leave other paired teammates on this fixture; pick this teammate's row.
    const row=ownerPage.locator('.business-access-device').filter({hasText:member.id.slice(0,8)});
    const select=row.getByRole('combobox');
    await expect(select).toHaveValue('viewer');
    await select.selectOption('reviewer');
    await expect(ownerPage.getByRole('status').filter({hasText:'is now a reviewer'})).toBeVisible();
    const roles=await (await owner.request.get(origin+'/api/team/roles')).json() as {principalId:string;role:string}[];
    expect(roles.find(entry=>entry.principalId===member.id)?.role).toBe('reviewer');
  }finally{await owner.close();await teammate.close();}
});
