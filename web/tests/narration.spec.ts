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
// Chromium's fake microphone: a steady test tone stands in for a voice.
test.use({launchOptions:{args:['--use-fake-ui-for-media-stream','--use-fake-device-for-media-stream']}});

test('the owner reads the storyboard aloud, line by line, and each scene gets its own clip',async({page,request,baseURL})=>{
  test.setTimeout(90000);
  await page.setViewportSize({width:1440,height:900});
  await page.context().grantPermissions(['microphone'],{origin:baseURL!});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');localStorage.setItem('fe-getting-started-dismissed:*','yes');}catch{}});
  // A storyboard in the disposable fixture, as the employee writes one.
  const login=await request.post(baseURL+'/api/auth/login',{headers:{Origin:baseURL!},data:{key:key()}});
  const csrf=(await login.json()).csrf;
  const board={title:'First Employee',scenes:[{scene:'intro',caption:'Meet your employee',narration:'Meet First Employee.',seconds:4},
    {scene:'shifts',caption:'It works shifts',narration:'It works in shifts and asks before anything goes out.',seconds:5},{scene:'outro',caption:'Approval first',narration:'Your judgment stays in the loop.',seconds:4}]};
  const made=await request.put(baseURL+'/api/company-wiki',{headers:{Origin:baseURL!,'X-CSRF':csrf},data:{requestId:crypto.randomUUID(),id:null,version:0,scope:'company',scopeId:'company',
    title:'Demo video storyboard (narration test)',body:'Scenes for the demo.\n\n```json\n'+JSON.stringify(board)+'\n```\n\nWhy each scene is here.',kind:'fact',status:'draft'}});
  expect(made.ok()).toBeTruthy();
  const page0=await made.json();

  await launch(page,request,baseURL!,'view=library&open=wiki:'+page0.id);
  await page.getByRole('button',{name:'Record narration'}).click();
  const dialog=page.getByRole('dialog',{name:'Record the narration'});
  await dialog.getByRole('button',{name:'Record all'}).click();
  const prompter=dialog.locator('.fe-teleprompter');
  await expect(prompter).toContainText('Meet First Employee.');
  await expect(prompter).toContainText('line 1 of 3');
  for(const line of ['It works in shifts','Your judgment stays in the loop.']){await page.waitForTimeout(1300);await page.keyboard.press('Space');await expect(prompter).toContainText(line);}
  await page.waitForTimeout(1300);await page.keyboard.press('Space');
  // One clip per line, cut where Space was pressed.
  await expect(dialog.locator('audio')).toHaveCount(3,{timeout:15000});
  for(let index=0;index<3;index++)await expect(dialog.getByLabel(`Clip for line ${index+1}`)).toBeVisible();
  await dialog.getByRole('button',{name:'Save narration'}).click();
  await expect(dialog.getByRole('status')).toContainText('the storyboard now points each scene at its clip');
  await dialog.getByRole('button',{name:'Done'}).click();
  await expect(dialog).toHaveCount(0);

  // The storyboard now names each scene's clip, and every clip is a WAV in the Library.
  const wiki=await (await request.get(baseURL+'/api/company-wiki',{headers:{Origin:baseURL!}})).json();
  const saved=wiki.find((item:any)=>item.id===page0.id);
  expect(saved.body).toContain('Why each scene is here.');
  const scenes=JSON.parse(saved.body.match(/```json\n([\s\S]*?)```/)[1]).scenes;
  expect(scenes.map((scene:any)=>/^[0-9a-f]{32}$/.test(scene.audio))).toEqual([true,true,true]);
  for(const scene of scenes){
    const clip=await request.get(`${baseURL}/api/uploads/${scene.audio}/content`,{headers:{Origin:baseURL!}});
    expect(clip.headers()['content-type']).toContain('audio/wav');
    expect((await clip.body()).subarray(0,4).toString('latin1')).toBe('RIFF');
  }
});
