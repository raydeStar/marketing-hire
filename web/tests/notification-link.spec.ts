import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('notification link opens retained delegated results after unlocking on a narrow screen',async({page})=>{
 await page.setViewportSize({width:900,height:800});
 await page.goto('/?view=upcoming');
 await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
 await page.getByRole('button',{name:'Unlock study',exact:true}).click();
 const log=page.getByRole('complementary',{name:'Activity log'});
 await expect(log).toBeVisible();
 await expect(log.getByRole('button',{name:'Upcoming',exact:true})).toHaveAttribute('aria-pressed','true');
 await expect(log.getByRole('heading',{name:'Delegated work',exact:true})).toBeVisible();
 await page.reload();
 await expect(log).toBeVisible();
 await expect(log.getByRole('button',{name:'Upcoming',exact:true})).toHaveAttribute('aria-pressed','true');
});
