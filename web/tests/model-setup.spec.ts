import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';

async function navigate(page:Page,name:string){
  const expand=page.getByRole('button',{name:'Expand sidebar',exact:true});
  if(await expand.isVisible())await expand.click();
  await (name==='Settings'?page:page.getByRole('navigation',{name:'Study navigation'})).getByRole('button',{name,exact:true}).click();
}

test('demo chat leads to model setup without sending the draft or starting inference',async({page})=>{
  const data=path.resolve(process.env.THADDEUS_TEST_DATA!);
  if(!data.startsWith(path.resolve('../artifacts')+path.sep))throw new Error('Use a disposable study. The owner is not a test fixture.');
  const requests:string[]=[];
  const provider=http.createServer((request,response)=>{
    requests.push(request.url!);
    response.setHeader('Content-Type','application/json');
    if(request.url==='/v1/models')response.end(JSON.stringify({data:[{id:'qa-setup-model'}]}));
    else{response.statusCode=500;response.end(JSON.stringify({error:'No inference is expected in this setup check.'}));}
  });
  await new Promise<void>(resolve=>provider.listen(0,'127.0.0.1',resolve));
  try{
    const endpoint=`http://127.0.0.1:${(provider.address() as {port:number}).port}/v1`;
    await page.goto('/');
    await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(data,'host-key.txt'),'utf8').trim());
    await page.getByRole('button',{name:'Open workspace',exact:true}).click();
    const setup=page.getByRole('region',{name:'Demo model setup'});
    await expect(setup).toContainText('Demo mode uses scripted replies.');
    const draft='Keep this question while I connect a model.';
    await page.getByLabel('Message or goal').fill(draft);
    await page.setViewportSize({width:390,height:844});
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);
    const screenshots=process.env.THADDEUS_SCREENSHOTS!;fs.mkdirSync(screenshots,{recursive:true});
    await page.screenshot({path:path.join(screenshots,'model-setup-mobile.png'),fullPage:true});
    await setup.getByRole('button',{name:'Connect a model',exact:true}).click();
    const connection=page.getByRole('region',{name:'Model connection',exact:true});
    await expect(connection).toBeVisible();
    await expect(connection.getByRole('heading',{name:'Connect a model',exact:true})).toBeFocused();
    await connection.getByLabel('Provider',{exact:true}).selectOption('compatible');
    await connection.getByLabel('Provider URL',{exact:true}).fill(endpoint);
    await connection.getByLabel('Exact model ID',{exact:true}).fill('qa-setup-model');
    await connection.getByLabel('API key storage',{exact:true}).selectOption('none');
    await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Research worker',exact:true}).click();
    await page.getByRole('button',{name:'Open model connection settings',exact:true}).click();
    await expect(connection.getByRole('heading',{name:'Connect a model',exact:true})).toBeFocused();
    await expect(connection.getByLabel('Exact model ID',{exact:true})).toHaveValue('qa-setup-model');
    await expect(connection.getByLabel('Provider URL',{exact:true})).toHaveValue(endpoint);
    expect(requests).toEqual([]);
    await connection.getByRole('button',{name:'Save connection',exact:true}).click();
    await expect(connection).toContainText('Connection saved. No model call was made.');
    expect(requests).toEqual([]);
    await connection.getByRole('button',{name:'Check saved connection',exact:true}).click();
    await expect(connection.locator('#discovered-models option')).toHaveCount(1);
    expect(requests).toEqual(['/v1/models']);
    await navigate(page,'Chat');
    await expect(page.getByLabel('Message or goal')).toHaveValue(draft);
    await expect(setup).toHaveCount(0);
    await expect(page.getByRole('button',{name:'qa-setup-model: token usage',exact:true})).toBeVisible();
    const state=await page.evaluate(async()=>(await fetch('/api/state')).json());
    expect(state.runs).toHaveLength(0);expect(state.search.budget.used).toBe(0);
    fs.writeFileSync(path.join(screenshots,'setup-proof.json'),JSON.stringify({passed:true,draftPreserved:true,settingsDraftPreserved:true,connectionListRequests:1,modelCalls:0,braveCalls:0},null,2));
  }finally{
    provider.closeAllConnections();
    await new Promise<void>(resolve=>provider.close(()=>resolve()));
  }
});
