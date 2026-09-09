import { defineConfig } from '@playwright/test';
export default defineConfig({testDir:'./tests',timeout:45000,workers:1,use:{baseURL:'http://localhost:5179',headless:true},reporter:[['list'],['json',{outputFile:'../artifacts/browser-results.json'}]]});
