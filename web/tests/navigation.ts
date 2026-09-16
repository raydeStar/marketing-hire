import {expect,type Page} from '@playwright/test';
function logToggle(page:Page){
  return page.getByRole('button',{name:'Activity log',exact:true})
    .or(page.getByRole('button',{name:'Thaddeus: open activity log',exact:true}));
}
export async function openLog(page:Page){
  const panel=page.getByRole('complementary',{name:'Activity log'});
  const toggle=logToggle(page);
  await expect(panel.or(toggle)).toBeVisible();
  if(await panel.isVisible())return;
  if(await toggle.getAttribute('aria-expanded')!=='true')await toggle.click();
  await expect(panel).toBeVisible();
}
export async function resizeLog(page:Page,width:number,height:number){
  const wasWide=await page.evaluate(()=>innerWidth>1100);
  await page.setViewportSize({width,height});
  // Wait for the breakpoint's real state transition before opening it again.
  if(wasWide&&width<=1100)await expect(logToggle(page)).toHaveAttribute('aria-expanded','false');
  await openLog(page);
}
export async function openSettings(page:Page){
  const expand=page.getByRole('button',{name:'Expand sidebar',exact:true});
  const settings=page.getByRole('button',{name:'Settings',exact:true});
  await expect(expand.or(settings)).toBeVisible();
  if(await expand.isVisible())await expand.click();
  await expect(settings).toBeVisible();
  await settings.click();
}
export async function closeSidebarOverlay(page:Page){
  const close=page.getByRole('button',{name:'Close sidebar',exact:true});
  if(await close.isVisible())await close.click();
}
export async function chooseMessageMode(page:Page,mode:'chat'|'research'){
  await page.getByRole('button',{name:'Message options',exact:true}).click();
  const label=mode==='research'?'Research with selected sources':'Chat';
  await page.getByRole('group',{name:'Message options',exact:true}).getByRole('button',{name:label,exact:true}).click();
}
export async function navigateStudy(page:Page,name:'Chat'|'Search'|'Feed'|'Ideas'|'To-do'|'Artifacts'|'Settings'){
  const item=page.getByRole('button',{name,exact:true});
  if(!await item.isVisible()){
    const expand=page.getByRole('button',{name:'Expand sidebar',exact:true});
    await expect(expand).toBeVisible();await expand.click();
  }
  await expect(item).toBeVisible();await item.click();
}
