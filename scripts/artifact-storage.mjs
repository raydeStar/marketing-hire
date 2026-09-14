import assert from 'node:assert/strict';
import {lstat,readdir,realpath,rm,statfs} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const artifacts=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../artifacts');
const GiB=1024**3;
export async function requireArtifactSpace(directory,additionalBytes,label,availableBytes){
  assert.ok(Number.isSafeInteger(additionalBytes)&&additionalBytes>=0);
  const filesystem=availableBytes===undefined?await statfs(directory):null;
  const available=availableBytes??filesystem.bavail*filesystem.bsize;
  const required=10*GiB+additionalBytes;
  if(available<required)throw new Error(`${label} needs ${(required/GiB).toFixed(1)} GiB free including a 10 GiB reserve; only ${(available/GiB).toFixed(1)} GiB is available. Clean disposable artifacts before retrying. No large test was started.`);
  return {availableBytes:available,additionalBytes,reserveBytes:10*GiB};
}

async function ownedDirectory(directory){
  const full=path.resolve(directory),base=await realpath(artifacts);
  assert.ok(full.startsWith(artifacts+path.sep),'Cleanup is restricted to a child of repository artifacts.');
  assert.equal(await realpath(full),path.join(base,path.relative(artifacts,full)),'Linked cleanup roots are refused.');
  assert.equal((await lstat(full)).isSymbolicLink(),false);
  return full;
}

export async function cleanArtifactPaths(directory,relatives){
  const root=await ownedDirectory(directory),removed=[];
  // Inspect every target before removing any. Nested links are unlinked by rm, never followed.
  const targets=[];
  for(const relative of relatives){
    assert.ok(typeof relative==='string'&&!path.isAbsolute(relative)&&!relative.split(/[\\/]/).some(part=>!part||part==='.'||part==='..'),'Use fixed relative artifact paths.');
    const target=path.resolve(root,relative);assert.ok(target.startsWith(root+path.sep));
    let info;try{info=await lstat(target);}catch(error){if(error.code==='ENOENT')continue;throw error;}
    assert.equal(info.isSymbolicLink(),false,'Linked cleanup targets are refused.');
    assert.equal(await realpath(target),target,'A cleanup ancestor contains a link.');
    targets.push({target,relative});
  }
  for(const {target,relative} of targets){await rm(target,{recursive:true,force:false,maxRetries:2,retryDelay:200});removed.push(relative);}
  return removed;
}

export async function cleanBuildIntermediates(directory){
  const root=await ownedDirectory(directory),targets=[];
  async function visit(current){
    for(const entry of await readdir(current,{withFileTypes:true})){
      if(!entry.isDirectory()||entry.isSymbolicLink())continue;
      const full=path.join(current,entry.name);
      if(['node_modules','bin','obj'].includes(entry.name))targets.push(path.relative(root,full));
      else await visit(full);
    }
  }
  await visit(root);return cleanArtifactPaths(root,targets);
}
