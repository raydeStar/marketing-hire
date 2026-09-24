import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('disposable host runs the full simulated campaign through Work',async({page,request,baseURL})=>{
  const origin=baseURL!;
  const dataRoot=process.env.THADDEUS_TEST_DATA;
  if(!dataRoot)throw new Error('Set THADDEUS_TEST_DATA to the disposable fixture host directory');
  const key=fs.readFileSync(path.join(dataRoot,'host-key.txt'),'utf8').trim();
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(issued.status()).toBe(200);
  const {ticket}=await issued.json();
  await page.setViewportSize({width:1280,height:900});
  await page.goto('/#launch='+ticket);
  await page.getByRole('button',{name:'Work',exact:true}).click();
  const panel=page.getByRole('region',{name:'Standing marketing assignment'});
  await expect(panel.getByText(/Isolated fixture ledger/)).toBeVisible();
  const seed=panel.getByRole('button',{name:'Create simulated campaign'});
  if(await seed.isVisible())await seed.click();
  await expect(panel.getByText('SIMULATED campaign: test an internal founder message')).toBeVisible();
  await panel.getByText('Source provenance · 2').click();
  await expect(panel.getByText('Source provenance · 2').locator('..').getByText(/SIMULATED source/).first()).toBeVisible();
  await panel.getByRole('button',{name:'Edit campaign brief'}).click();
  const briefFields={'Audience':'Founders','Customer problem':'Marketing time','Opportunity hypothesis':'A bounded draft may clarify the offer','Why prioritize this opportunity':'Founders in both checked notes raised this problem','Proposition to test':'Configurable marketing employee','Desired customer behavior':'Ask for a demo','Selected channel':'Owner reviewed draft','Primary outcome metric':'Qualified replies','How the metric is counted':'Count relevant replies','Claim or conduct guardrail':'No outcome guarantee','Intervention':'One fixture draft','Target population':'Founders','Observation window and timezone':'Seven days','Source of observations':'Fixture observation'};
  for(const [label,value] of Object.entries(briefFields))await panel.getByLabel(label,{exact:true}).fill(value);
  await panel.getByLabel('Decision rule').selectOption('minimum_sample');
  await panel.getByLabel('Minimum observations').fill('2');
  await panel.getByRole('button',{name:'Save campaign brief'}).click();
  await expect(panel.getByText('Campaign workflow · align')).toBeVisible();
  await panel.getByRole('button',{name:'Review draft angles'}).click();
  await panel.getByRole('button',{name:'Request revision'}).click();
  await panel.getByLabel('What should change?').fill('Make the first hook specific.');
  await panel.getByRole('button',{name:'Save revision request'}).click();
  await expect(panel.getByText(/Next action: Create a simulated asset revision/)).toBeVisible();
  await panel.getByRole('button',{name:'Create simulated asset revision'}).click();
  await panel.getByText(/Revised post angles · saved/).click();
  await panel.getByRole('button',{name:'Approve exact draft'}).click();
  await panel.getByRole('button',{name:'Align approved fixture draft'}).click();
  await expect(panel.getByText('Campaign workflow · launch')).toBeVisible();
  await panel.getByRole('button',{name:'Record fake launch'}).click();
  await expect(panel.getByText('Campaign workflow · measure')).toBeVisible();
  await panel.getByLabel('Numerator').fill('1');
  await panel.getByLabel('Denominator').fill('1');
  await panel.getByRole('button',{name:'Record fixture observation'}).click();
  await expect(panel.getByText(/actual sample 1\/2/)).toBeVisible();
  await expect(panel.getByText('Insufficient actual sample. The only available decision is collect evidence.')).toBeVisible();
  await panel.getByRole('button',{name:'Record fixture decision'}).click();
  await expect(panel.getByText('Campaign workflow · measure')).toBeVisible();
  await expect(panel.getByText('Insufficient actual sample. The only available decision is collect evidence.')).toBeVisible();
  await panel.getByLabel('Numerator').fill('1');
  await panel.getByLabel('Denominator').fill('1');
  await panel.getByRole('button',{name:'Record fixture observation'}).click();
  await expect(panel.getByText(/actual sample 2\/2/)).toBeVisible();
  await panel.getByRole('button',{name:'Record fixture decision'}).click();
  await expect(panel.getByText('Campaign workflow · learn')).toBeVisible();
  await panel.getByLabel('Proposed lesson').fill('Controls may improve clarity');
  await panel.getByLabel('Context',{exact:true}).fill('Founders, one synthetic draft');
  await panel.getByLabel('Uncertainty').fill('No real audience response');
  await panel.getByLabel('Revisit when').fill('Real evidence arrives');
  await panel.getByLabel('Next action').fill('Remain paused');
  await panel.getByRole('button',{name:'Save proposed lesson'}).click();
  await expect(panel.getByText('Campaign workflow · complete')).toBeVisible();
  await panel.getByText('Campaign decisions and receipts · 8').click();
  await expect(panel.getByText(/SIMULATED_ONLY/)).toBeVisible();
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await expect(panel.getByText('Campaign workflow · complete')).toBeVisible();
  await expect(panel.getByText(/fixture identity only/)).toBeVisible();
  await expect(panel.getByText('Relevant prior simulated learning · 0')).toBeVisible();
  const lessons=await page.evaluate(async()=>{
    const session=await (await fetch('/api/session')).json() as {csrf:string};
    const response=await fetch('/api/marketing/runway/fixture/lessons',{
      method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},
      body:JSON.stringify({audience:'Founders'})
    });
    const body=await response.json();
    const excluded=await fetch('/api/marketing/runway/fixture/lessons',{
      method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},
      body:JSON.stringify({audience:'Founders',excludeCampaignId:body.lessons[0]?.campaign_id})
    });
    return {status:response.status,body,excludedStatus:excluded.status,excludedBody:await excluded.json()};
  });
  expect(lessons.status).toBe(200);
  expect(lessons.body.lessons).toEqual(expect.arrayContaining([
    expect.objectContaining({
      lesson:expect.objectContaining({lesson:'Controls may improve clarity',uncertainty:'No real audience response'}),
      decision:expect.objectContaining({decision:'pause',actual_sample:2}),
      observations:expect.arrayContaining([expect.objectContaining({value_type:'actual',attribution_limitations:'Synthetic observation; no causal inference'})])
    })
  ]));
  expect(lessons.excludedStatus).toBe(200);
  expect(lessons.excludedBody.lessons).toEqual([]);
  const later=await page.evaluate(async()=>{
    const session=await (await fetch('/api/session')).json() as {csrf:string};
    const response=await fetch('/api/marketing/runway/fixture/seed',{
      method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},
      body:JSON.stringify({requestId:'later-fixture-campaign-browser'})
    });
    return {status:response.status,body:await response.json()};
  });
  expect(later.status).toBe(200);
  expect(later.body.project.id).not.toBe(lessons.body.lessons[0].campaign_id);
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await expect(panel.getByText('Campaign workflow · sense → prioritize')).toBeVisible();
  await panel.getByRole('button',{name:'Edit campaign brief'}).click();
  for(const [label,value] of Object.entries(briefFields))await panel.getByLabel(label,{exact:true}).fill(value);
  await panel.getByRole('button',{name:'Save campaign brief'}).click();
  await panel.getByText('Relevant prior simulated learning · 1').click();
  await expect(panel.getByText('SIMULATED ONLY · Controls may improve clarity')).toBeVisible();
  await expect(panel.getByText(/Founders, one synthetic draft/)).toBeVisible();
  await expect(panel.getByText(/actual 1\/1 · Count relevant replies/)).toBeVisible();
});
