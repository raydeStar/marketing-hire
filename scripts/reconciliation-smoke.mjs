import {chromium} from '../web/node_modules/playwright-core/index.mjs';
import {readFile,writeFile} from 'node:fs/promises';
const root='artifacts/recovery-browser-cycle';
const fixture=JSON.parse(await readFile(root+'/fixture.json','utf8'));
const browser=await chromium.launch();
try {
 const page=await browser.newPage({viewport:{width:390,height:900}});
 await page.goto('http://localhost:5184');
 await page.getByLabel('Host access key',{exact:true}).fill((await readFile(root+'/host-key.txt','utf8')).trim());
 await page.getByRole('button',{name:'Unlock study'}).click();
 await page.locator(`[data-run-id="${fixture.id}"]`).click();
 await page.getByRole('heading',{name:'Reconcile interrupted work'}).waitFor();
 await page.getByRole('button',{name:'Verify existing content'}).waitFor({state:'visible'});
 await page.screenshot({path:'artifacts/screenshots/reconciliation-390.png',fullPage:true});
 await page.getByRole('button',{name:'Verify existing content'}).click();
 await page.getByText('Interrupted write reconciled · exact approved content verified',{exact:true}).waitFor();
 const receipt=await page.evaluate(async id=>({run:await(await fetch('/api/runs/'+id)).json(),revisions:await(await fetch('/api/revisions?path=plans/weekly-plan.md')).json()}),fixture.id);
 if(receipt.run.state!=='succeeded'||receipt.revisions.length!==1)throw Error('Reconciliation did not preserve exactly one revision.');
 await writeFile('artifacts/reconciliation-browser.json',JSON.stringify(receipt,null,2));
 console.log('Browser reconciliation passed against a real injected-crash ledger; one revision preserved.');
} finally {await browser.close();}
