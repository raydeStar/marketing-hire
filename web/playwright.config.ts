import { defineConfig } from '@playwright/test';
export default defineConfig({testDir:'./tests',timeout:45000,workers:1,maxFailures:process.env.CI?1:undefined,use:{baseURL:process.env.THADDEUS_TEST_ORIGIN||'http://localhost:5179',headless:true},reporter:[['list'],['json',{outputFile:'../artifacts/browser-results.json'}]]});
