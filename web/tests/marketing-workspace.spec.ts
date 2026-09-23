import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('marketing views and task selection read state without invoking the employee',async({page,request,baseURL})=>{
  const origin=baseURL!;
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(issued.status()).toBe(200);
  const {ticket}=await issued.json();
  const task={id:'a'.repeat(32),title:'Prepare launch brief',status:'ready',priority:'high',next_action:'Outline the audience',action_state:'agent_ready',blocker:null,conversation_key:'agent:main:marketing-task-'+('a'.repeat(32)),version:1,updated_at:1780000000};
  let turns=0;
  let connectionStatus='connected';
  await page.route('**/api/marketing/**',async route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:'Marketing Hire',model:'openai/test',sessionKey:'agent:main:marketing-business-main'},connection:{status:connectionStatus},taskStoreAvailable:true,tasks:[task],messages:[],requests:[]}});
    if(url.pathname==='/api/marketing/tasks/'+task.id&&route.request().method()==='PUT'){
      const change=route.request().postDataJSON();
      Object.assign(task,{status:change.status??task.status,priority:change.priority??task.priority,version:task.version+1});
      return route.fulfill({json:task});
    }
    if(url.pathname==='/api/marketing/chat'){turns++;return route.fulfill({json:{requestId:'test',status:'succeeded',reply:'Ready.',sessionKey:'agent:main:marketing-business-main'}});}
    return route.fulfill({status:404,json:{error:'Unexpected marketing request'}});
  });
  await page.goto('/#launch='+ticket);
  await expect(page.getByRole('heading',{name:'Conversation'})).toBeVisible();
  await page.getByRole('button',{name:'Marketing',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Talk with your marketing employee'})).toBeVisible();
  await page.getByRole('button',{name:'Work',exact:false}).click();
  await page.getByRole('button',{name:/Prepare launch brief/}).click();
  await expect(page.getByRole('heading',{name:'Prepare launch brief'}).first()).toBeVisible();
  await page.getByRole('combobox',{name:'Status'}).selectOption('working');
  await expect(page.getByRole('combobox',{name:'Status'})).toHaveValue('working');
  connectionStatus='auth_required';
  await page.getByRole('button',{name:'Refresh marketing state'}).click();
  await expect(page.getByText('Authentication needed')).toBeVisible();
  await expect(page.getByRole('combobox',{name:'Priority'})).toBeEnabled();
  await page.getByRole('combobox',{name:'Priority'}).selectOption('low');
  await expect(page.getByRole('combobox',{name:'Priority'})).toHaveValue('low');
  await page.getByRole('button',{name:'Chat',exact:true}).click();
  await expect(page.getByRole('textbox',{name:'Message to marketing employee'})).toBeDisabled();
  expect(turns).toBe(0);
  await expect(page.getByText('SCRIPTED DEMO')).toHaveCount(0);
});
