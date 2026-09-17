import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {cleanArtifactPaths,requireArtifactSpace} from './artifact-storage.mjs';

const repository=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const [packageArgument,evidenceArgument]=process.argv.slice(2);
assert.ok(packageArgument&&evidenceArgument,'Supply an existing package and a fresh evidence folder.');
const packagePath=path.resolve(packageArgument),evidence=path.resolve(evidenceArgument),artifacts=path.join(repository,'artifacts')+path.sep;
assert.ok(packagePath.startsWith(artifacts)&&evidence.startsWith(artifacts),'Browser suite inputs must remain below repository artifacts.');
await mkdir(evidence);await requireArtifactSpace(evidence,256*1024**2,'Isolated packaged browser suite');

const playwright=path.join(repository,'web/node_modules/@playwright/test/cli.js');
const listed=spawnSync(process.execPath,[playwright,'test','--list'],{cwd:path.join(repository,'web'),encoding:'utf8',windowsHide:true});
assert.equal(listed.status,0,listed.stderr||'Playwright could not list the packaged browser cases.');
const optIn=new Set(['chat-web-live.spec.ts','native-folder.spec.ts','native-notification.spec.ts','research.spec.ts','study-handoff.spec.ts']);
const cases=listed.stdout.split(/\r?\n/).map(line=>line.match(/^\s+([^:]+\.spec\.ts):(\d+):\d+\s+›\s+(.+)$/)).filter(Boolean)
  .map(match=>({file:match[1],line:Number(match[2]),title:match[3]})).filter(item=>!optIn.has(item.file));
assert.ok(cases.length>0,'No ordinary packaged browser cases were found.');

const receipt={passed:false,package:packagePath,started:new Date().toISOString(),isolatedStudyPerCase:true,
  excludedOptIn:[...optIn].sort(),cases:[]};
try{
 for(let index=0;index<cases.length;index++){
  const item=cases[index],name=`${String(index+1).padStart(2,'0')}-${path.basename(item.file,'.spec.ts')}-${item.line}`;
  const target=path.join(evidence,name),started=new Date().toISOString();
  process.stdout.write(`Browser case ${index+1}/${cases.length}: ${item.title}\n`);
  const result=spawnSync(process.execPath,[path.join(repository,'scripts/browser-check.mjs'),packagePath,target,`${item.file}:${item.line}`],
    {cwd:repository,encoding:'utf8',windowsHide:true,maxBuffer:4*1024**2});
  const record={...item,evidence:target,started,finished:new Date().toISOString(),exitCode:result.status,signal:result.signal,cleanup:[]};
  receipt.cases.push(record);await writeFile(path.join(evidence,'suite.json'),JSON.stringify(receipt,null,2)+'\n');
  if(result.status!==0)throw new Error(`Packaged browser case failed: ${item.file}:${item.line} ${item.title}. Inspect ${target}.\n${result.stderr||result.stdout}`);
  const verified=JSON.parse(await readFile(path.join(target,'verified.json'),'utf8'));
  assert.equal(verified.passed,true);assert.equal(verified.mainStudyTouched,false);assert.equal(verified.processCleanupPassed,true);
  record.cleanup=await cleanArtifactPaths(target,['study']);
  await writeFile(path.join(target,'suite-cleanup.json'),JSON.stringify({removed:record.cleanup,retained:['logs','browser results','screenshots','verification receipt']},null,2)+'\n');
 }
 receipt.passed=true;receipt.finished=new Date().toISOString();
}catch(error){receipt.error=error.message;receipt.finished=new Date().toISOString();throw error;}
finally{await writeFile(path.join(evidence,'suite.json'),JSON.stringify(receipt,null,2)+'\n');}
console.log(`Isolated packaged browser suite passed ${receipt.cases.length} cases. Disposable studies were removed after owned process cleanup.`);
