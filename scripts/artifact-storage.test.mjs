import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdir,mkdtemp,readFile,rm,symlink,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {requireArtifactSpace,cleanArtifactPaths,cleanBuildIntermediates} from './artifact-storage.mjs';

test('large test admission includes reserve and rejects insufficient space',async()=>{
  await assert.rejects(requireArtifactSpace('.',2*1024**3,'Fixture',11*1024**3),/10 GiB reserve/);
  assert.equal((await requireArtifactSpace('.',2*1024**3,'Fixture',12*1024**3)).reserveBytes,10*1024**3);
});
test('cleanup refuses a linked root and never follows nested links',async()=>{
  const artifacts=path.resolve('artifacts');await mkdir(artifacts,{recursive:true});
  const root=await mkdtemp(path.join(artifacts,'storage-links-'));
  try{
    const outside=path.join(root,'keep'),scratch=path.join(root,'scratch');await mkdir(outside);await mkdir(scratch);
    await writeFile(path.join(outside,'note'),'Keep the original');
    await symlink(outside,path.join(scratch,'linked'),process.platform==='win32'?'junction':'dir');
    await assert.rejects(cleanArtifactPaths(path.join(scratch,'linked'),['note']));
    assert.deepEqual(await cleanArtifactPaths(root,['scratch']),['scratch']);
    assert.equal(await readFile(path.join(outside,'note'),'utf8'),'Keep the original');
  }finally{assert.ok(root.startsWith(artifacts+path.sep));await rm(root,{recursive:true});}
});
test('cleanup refuses escapes and keeps sources and records while deleting intermediates',async()=>{
  const artifacts=path.resolve('artifacts');await mkdir(artifacts,{recursive:true});
  const root=await mkdtemp(path.join(artifacts,'storage-contract-'));
  try{
    await mkdir(path.join(root,'source','web','node_modules'),{recursive:true});await mkdir(path.join(root,'source','app','obj'),{recursive:true});
    await writeFile(path.join(root,'source','web','node_modules','dependency'),'Disposable');await writeFile(path.join(root,'source','app','obj','cache'),'Disposable');
    await writeFile(path.join(root,'source','app','main.cs'),'Keep source');await writeFile(path.join(root,'verified.json'),'Keep receipt');
    await assert.rejects(cleanArtifactPaths(root,['../verified.json']));await assert.rejects(cleanArtifactPaths(root,['.']));await assert.rejects(cleanArtifactPaths(artifacts,['source']));
    assert.equal((await cleanBuildIntermediates(path.join(root,'source'))).length,2);
    assert.equal(await readFile(path.join(root,'source','app','main.cs'),'utf8'),'Keep source');assert.equal(await readFile(path.join(root,'verified.json'),'utf8'),'Keep receipt');
    assert.deepEqual(await cleanArtifactPaths(root,['missing']),[]);
  }finally{assert.ok(root.startsWith(artifacts+path.sep));await rm(root,{recursive:true});}
});
