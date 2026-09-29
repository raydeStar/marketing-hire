import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
const running=(page:Page,selector:string)=>page.evaluate(selector=>document.getAnimations().filter(animation=>animation.playState==='running'&&(animation.effect as KeyframeEffect|null)?.target instanceof Element&&((animation.effect as KeyframeEffect).target as Element).closest(selector)).length,selector);

test('the sign-in raven listens with small discrete moves, and reduced motion stills it and the whole workspace',async({browser,baseURL})=>{
  const directory=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(directory,{recursive:true});
  for(const reducedMotion of ['no-preference','reduce'] as const){
    const context=await browser.newContext({baseURL,reducedMotion,viewport:{width:1440,height:1000}});
    const page=await context.newPage();
    try{
      await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');}catch{}});
      await page.goto('/');
      const raven=page.getByLabel('Chip: Ready when you are');
      await expect(raven).toBeVisible();
      await expect(page.getByRole('heading',{level:1,name:'Open your workspace'})).toBeVisible();
      // Moves are stepped poses, never smooth tweening.
      const timing=await page.locator('.raven .raven-head').evaluate(el=>getComputedStyle(el).animationTimingFunction);
      if(reducedMotion==='no-preference'){
        expect(await running(page,'.raven')).toBeGreaterThan(0);
        expect(timing).toMatch(/steps\(1/);
      }else{
        expect(await running(page,'.raven')).toBe(0);
        await page.screenshot({path:path.join(directory,'raven-reduced-motion.png')});
        // Past sign-in, nothing in the workspace animates either.
        await page.getByLabel('Host access key',{exact:true}).fill(key());
        await page.getByRole('button',{name:'Open workspace',exact:true}).click();
        await expect(page.getByRole('navigation',{name:'Main views'})).toBeVisible();
        expect(await running(page,'body')).toBe(0);
      }
    }finally{await context.close();}
  }
});
