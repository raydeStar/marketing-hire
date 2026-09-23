import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {readFile,writeFile,mkdir,readdir,lstat,copyFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {requireArtifactSpace} from './artifact-storage.mjs';

const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
export async function prepareBrowserRuntime(source,output){
  assert.equal(process.platform,'win32','The invited browser preview targets Windows.');
  await requireArtifactSpace(path.dirname(output),256*1024**2,'Pinned browser runtime preparation');
  const input=path.join(source,'tools/browser-runtime');
  const pin=JSON.parse(await readFile(path.join(input,'runtime-pin.json'),'utf8'));
  const lock=JSON.parse(await readFile(path.join(input,'package-lock.json'),'utf8'));
  assert.equal(lock.packages['node_modules/@playwright/mcp'].version,pin.mcpVersion);
  assert.match(pin.nodeUrl,/^https:\/\/nodejs\.org\/dist\/v24\.[0-9]+\.[0-9]+\/win-x64\/node\.exe$/);
  const license=await readFile(path.join(input,'NODE-LICENSE.txt'));assert.equal(hash(license),pin.nodeLicenseSha256);
  const install=spawnSync(process.env.ComSpec||'cmd.exe',['/d','/s','/c','npm ci --ignore-scripts --no-audit --no-fund'],{cwd:input,stdio:'inherit',windowsHide:true});
  assert.equal(install.status,0,'The locked browser dependency did not install.');
  await mkdir(output); // Never mutate a previously built runtime.
  const response=await fetch(pin.nodeUrl,{signal:AbortSignal.timeout(120000)});
  assert.ok(response.ok,'Pinned Node download failed.');
  const binary=Buffer.from(await response.arrayBuffer());assert.equal(hash(binary),pin.nodeSha256,'Pinned Node hash mismatch.');
  await writeFile(path.join(output,'node.exe'),binary,{flag:'wx'});
  await writeFile(path.join(output,'NODE-LICENSE.txt'),license,{flag:'wx'});
  const version=spawnSync(path.join(output,'node.exe'),['--version'],{encoding:'utf8',windowsHide:true});
  assert.equal(version.status,0);assert.equal(version.stdout.trim(),pin.nodeVersion);
  async function copyTree(from,to){
    assert.ok((await lstat(from)).isDirectory());await mkdir(to);
    for(const entry of await readdir(from,{withFileTypes:true})){
      assert.ok(!entry.isSymbolicLink(),'Linked dependency files are not accepted.');
      if(entry.name==='.bin')continue;
      if(entry.isDirectory())await copyTree(path.join(from,entry.name),path.join(to,entry.name));
      else {assert.ok(entry.isFile());await copyFile(path.join(from,entry.name),path.join(to,entry.name));}
    }
  }
  await copyTree(path.join(input,'node_modules'),path.join(output,'node_modules'));
  const dependencies=[];
  for(const [relative,metadata] of Object.entries(lock.packages).filter(([name])=>name.startsWith('node_modules/'))){
    const directory=path.join(output,relative),files=await readdir(directory);
    const notices=files.filter(name=>/^(LICENSE|NOTICE)(\.|$)/i.test(name));
    assert.ok(notices.length,`Missing notices for ${relative}.`);
    dependencies.push({name:relative.slice('node_modules/'.length),version:metadata.version,integrity:metadata.integrity,notices:notices.map(name=>relative+'/'+name)});
  }
  await writeFile(path.join(output,'runtime-manifest.json'),JSON.stringify({schemaVersion:1,node:pin,dependencies,credentialsIncluded:false},null,2)+'\n');
  console.log('Pinned browser runtime and notices prepared. Chrome still supplies its own shoes.');
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  const [source,output]=process.argv.slice(2);assert.ok(source&&output,'Supply captured source and a fresh runtime output directory.');
  await prepareBrowserRuntime(path.resolve(source),path.resolve(output));
}
