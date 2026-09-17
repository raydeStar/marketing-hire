// Run from the normal Windows desktop: MSIX development shells may redirect registration.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createServer} from 'node:http';
import {createHash} from 'node:crypto';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';

assert.equal(process.platform,'win32');
const [packageArgument,evidenceArgument,label='E1']=process.argv.slice(2);
assert.ok(packageArgument&&evidenceArgument,'Use: notification-click-check.mjs PACKAGE FRESH-EVIDENCE [LABEL]');
assert.match(label,/^[A-Z][0-9]{1,2}$/,'Use a short visible check label such as E2.');
const packagePath=path.resolve(packageArgument),evidence=path.resolve(evidenceArgument);
const root=path.resolve('artifacts')+path.sep;
assert.ok(packagePath.startsWith(root)&&evidence.startsWith(root),'Use paths below repository artifacts.');
const manifest=JSON.parse(await readFile(path.join(packagePath,'package-manifest.json'),'utf8'));
assert.equal(manifest.runtime,'win-x64');
for(const file of manifest.files){
 assert.equal(createHash('sha256').update(await readFile(path.join(packagePath,file.path))).digest('hex'),file.sha256,file.path);
}
await mkdir(evidence);
const receipt={passed:false,sourceHead:manifest.sourceHead,sourceDirty:manifest.checkoutDirty,package:packagePath,
 scope:'Real notification click callback and exact local browser destination. No owner study, model or email access.',
 title:'Thaddeus notification click check '+label,visible:null};
let child,timer,resolveClick;
const clicked=new Promise(resolve=>resolveClick=resolve);
const server=createServer((request,response)=>{
 if(request.method==='GET'&&request.url==='/?view=upcoming'){
  receipt.clickReceivedAt=new Date().toISOString();
  receipt.coldActivation=Boolean(receipt.senderExitedAt);
  response.writeHead(200,{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store','Content-Security-Policy':"default-src 'none'"});
  response.end('<!doctype html><title>Thaddeus notification check</title><h1>Notification click received</h1><p>The raven found the correct study address. You may close this test tab.</p>');
  resolveClick();
 }else{response.writeHead(404);response.end();}
});
try{
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 receipt.origin='http://127.0.0.1:'+server.address().port;
 console.log('Please wait for "Click now" before opening '+label+'; this verifies a click after the sender exits.');
 child=spawn(path.join(packagePath,'Thaddeus.Notifications.exe'),[],{cwd:packagePath,windowsHide:true,stdio:['pipe','pipe','pipe']});
 let output='',error='';child.stdout.on('data',data=>output+=data);child.stderr.on('data',data=>error+=data);
 const exited=new Promise((resolve,reject)=>{child.once('error',reject);child.once('exit',code=>resolve(code));});
 child.stdin.end(JSON.stringify({title:receipt.title,message:'After the check window says Click now, click this card to verify opening the correct study.',origin:receipt.origin}));
 const exitCode=await Promise.race([exited,new Promise((_,reject)=>timer=setTimeout(()=>reject(new Error('Notification helper timed out.')),15000))]);
 clearTimeout(timer);receipt.senderExitedAt=new Date().toISOString();
 assert.equal(exitCode,0,error);receipt.provider=JSON.parse(output);assert.equal(receipt.provider.accepted,true);
 console.log('Click now: open Windows Notification Center (Win+N), then click '+receipt.title+'.');
 await Promise.race([clicked,new Promise((_,reject)=>timer=setTimeout(()=>reject(new Error('No notification click arrived within two minutes.')),120000))]);
 clearTimeout(timer);assert.equal(receipt.coldActivation,true,'The notification was clicked before the sender exited; cold activation remains unverified.');
 receipt.passed=true;
 console.log('Native cold activation and exact browser destination passed. The raven found the right door.');
}catch(error){receipt.error=error.message;process.exitCode=1;console.error(error.message);}
finally{
 clearTimeout(timer);
 if(child&&child.exitCode===null&&!child.killed){child.kill();await new Promise(resolve=>child.once('exit',resolve));}
 server.closeAllConnections();await new Promise(resolve=>server.close(resolve));
 await writeFile(path.join(evidence,'receipt.json'),JSON.stringify(receipt,null,2)+'\n');
}
