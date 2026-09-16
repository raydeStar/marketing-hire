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
