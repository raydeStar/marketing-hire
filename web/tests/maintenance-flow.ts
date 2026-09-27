import {expect,type Page} from '@playwright/test';

/** The workspace is open: its main views are on the rail. */
export async function expectWorkspace(page:Page,timeout=20000){
  await expect(page.getByRole('navigation',{name:'Main views'})).toBeVisible({timeout});
}

/**
 * Start maintenance as the owner and open the maintenance screen. The workspace has no button for this; the host API is the entry point.
 * The host starts only when no other request is in flight, so the workspace (which polls) is closed first.
 */
export async function startMaintenance(page:Page,mode:'backup'|'stop'){
  const origin=new URL(page.url()).origin;
  await page.goto('about:blank');
  const session=await(await page.request.get(origin+'/api/session')).json();
  let started:{status:number;body:string}={status:0,body:''};
  for(let attempt=0;attempt<10;attempt++){
    const view=await(await page.request.get(origin+'/api/maintenance')).json();
    expect(view.canStart,'Maintenance can start on this host: '+view.message).toBe(true);
    const response=await page.request.post(origin+'/api/maintenance/start',{headers:{Origin:origin,'X-CSRF':session.csrf},data:{version:view.version,mode}});
    started={status:response.status(),body:await response.text()};
    if(started.status!==409)break;
    await page.waitForTimeout(500);
  }
  expect(started.status,started.body).toBe(200);
  await page.goto(origin+'/maintenance');
  await expect(page.getByRole('heading',{name:'Study maintenance',exact:true})).toBeVisible();
}
