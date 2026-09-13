import {expect,type Page} from '@playwright/test';
export async function openLog(page:Page){
  const toggle=page.getByRole('button',{name:'Activity log',exact:true});
  if(await toggle.getAttribute('aria-expanded')!=='true')await toggle.click();
  await expect(page.getByRole('complementary',{name:'Activity log'})).toBeVisible();
}
