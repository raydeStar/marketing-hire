import {chromium} from '../web/node_modules/playwright-core/index.mjs';
import {readFile} from 'node:fs/promises';
const browser=await chromium.launch();
try{
 const page=await browser.newPage({viewport:{width:390,height:900}});
 await page.goto('http://localhost:5179');
 await page.getByLabel('Host access key',{exact:true}).fill((await readFile('.data/host-key.txt','utf8')).trim());
 await page.getByRole('button',{name:'Unlock study'}).click();
 await page.getByText('Juniper',{exact:true}).waitFor();
 await page.getByRole('button',{name:'Cancel reply',exact:true}).waitFor({state:'hidden'});
 await page.screenshot({path:'artifacts/screenshots/conversation-luna-complete-390.png',fullPage:true});
 console.log('Captured the completed conversation after the browser received its final event.');
}finally{await browser.close();}
