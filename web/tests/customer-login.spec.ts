import {test,expect} from '@playwright/test';

test('customer sign-in offers only configured providers and retains local recovery',async({page})=>{
  const calls:string[]=[];
  await page.route('**/api/**',route=>{
    const path=new URL(route.request().url()).pathname;calls.push(path);
    if(path==='/api/auth/customer')return route.fulfill({json:{enabled:true,origin:'https://workspace.example.test',providers:['google']}});
    return route.fulfill({status:401,json:{error:'Sign in first.'}});
  });
  await page.goto('/');
  const google=page.getByRole('link',{name:'Continue with Google'});
  await expect(google).toHaveAttribute('href','https://workspace.example.test/api/auth/customer/login?provider=google');
  await expect(page.getByRole('link',{name:'Continue with Microsoft'})).toHaveCount(0);
  await page.getByRole('button',{name:'Other workspace access'}).click();
  await expect(google).toHaveCount(0);
  expect(calls.every(path=>!path.startsWith('/api/marketing/'))).toBe(true);
});

test('customer sign-in displays safe callback failure without provider details',async({page})=>{
  await page.route('**/api/**',route=>new URL(route.request().url()).pathname==='/api/auth/customer'
    ?route.fulfill({json:{enabled:true,origin:'https://workspace.example.test',providers:['google','microsoft']}})
    :route.fulfill({status:401,json:{error:'Sign in first.'}}));
  await page.goto('/#sign-in-error=not-completed');
  await expect(page.getByRole('link',{name:'Continue with Microsoft'})).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('Sign-in wasn’t completed');
});
