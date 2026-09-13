import {expect,type Page} from '@playwright/test';
export async function openLog(page:Page){
  const toggle=page.getByRole('button',{name:'Activity log',exact:true});
  if(await toggle.getAttribute('aria-expanded')!=='true')await toggle.click();
  await expect(page.getByRole('complementary',{name:'Activity log'})).toBeVisible();
}
export async function resizeLog(page:Page,width:number,height:number){
  const wasWide=await page.evaluate(()=>innerWidth>1100);
  await page.setViewportSize({width,height});
  // Wait for the breakpoint's real state transition before opening it again.
  if(wasWide&&width<=1100)await expect(page.getByRole('button',{name:'Activity log',exact:true})).toHaveAttribute('aria-expanded','false');
  await openLog(page);
}
