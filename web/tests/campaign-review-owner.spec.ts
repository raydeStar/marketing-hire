import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const projectId=process.env.MARKETING_PERSISTENT_PROJECT_ID;
test.skip(!projectId,'Set MARKETING_PERSISTENT_PROJECT_ID for the read-only saved-pilot presentation check.');

for(const width of [1440,1280]){
  test(`saved owner campaign review at ${width}px`,async({page,request,baseURL})=>{
    await page.setViewportSize({width,height:width===1440?900:800});
    const origin=baseURL!;
    const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
    const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
    expect(issued.ok()).toBeTruthy();
    const {ticket}=await issued.json();
    await page.goto('/#launch='+ticket);
    await page.getByRole('button',{name:'Work',exact:true}).click();
    const desk=page.getByRole('region',{name:'Campaign review workspace'});
    await expect(desk).toBeVisible();
    await expect(desk.getByRole('heading',{name:'Internal marketing campaign'})).toBeVisible();
    await expect(desk).toContainText('Three draft post angles');
    await expect(desk).toContainText('A newer assignment has an unknown worker outcome');
    await expect(desk).toContainText('Unavailable in this older review packet');
    await desk.scrollIntoViewIfNeeded();
    const output=path.resolve(`../artifacts/enterprise-review/owner-${width}.png`);
    fs.mkdirSync(path.dirname(output),{recursive:true});
    await page.screenshot({path:output});
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
    await desk.getByRole('button',{name:'Brief & rule'}).click();
    await expect(desk.getByRole('heading',{name:'Provisional brief: owner review pending'})).toBeVisible();
    await desk.getByRole('button',{name:'What changed'}).click();
    await expect(desk.getByRole('button',{name:'Connect native conversation'})).toBeEnabled();
  });
}
