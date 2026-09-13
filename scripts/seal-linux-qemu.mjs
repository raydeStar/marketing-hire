import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {createReadStream} from 'node:fs';
import {readFile,readdir,writeFile} from 'node:fs/promises';
import path from 'node:path';

// An integrity inventory for a development build, not a publisher signature.
const root=path.resolve(process.argv[2]);
assert.ok(root.startsWith(path.resolve('artifacts')+path.sep));
async function hash(file){const h=createHash('sha256');for await(const b of createReadStream(file))h.update(b);return h.digest('hex');}
const build=JSON.parse(await readFile(path.join(root,'build.json'),'utf8'));
assert.equal(build.built,true);
assert.equal(build.scriptSha256,await hash(path.join(root,'build.sh')));
const sourceSha256='6ee1d1a61f68212476b27108c26da5f449dc09b626d42f8279ba0dc2e08fa858';
assert.equal(await hash(path.join(root,'downloads/qemu-11.1.0.tar.xz')),sourceSha256);
const signature=await readFile(path.join(root,'signature.txt'),'utf8');
assert.match(signature,/\[GNUPG:\] VALIDSIG CEACC9E15534EBABB82D3FA03353C9CEF108B584 /);
const files=[],names=new Set();
async function walk(relative=''){
 for(const item of await readdir(path.join(root,'runtime',relative),{withFileTypes:true})){
  const name=relative?relative+'/'+item.name:item.name;
  assert.ok(!item.isSymbolicLink() && !names.has(name.toLowerCase()));names.add(name.toLowerCase());
  assert.ok(names.size<=4096);
  if(item.isDirectory())await walk(name);
  else{assert.ok(item.isFile());files.push({path:name,sha256:await hash(path.join(root,'runtime',name))});}
 }
}
await walk();files.sort((a,b)=>a.path<b.path?-1:1);
for(const role of ['bin/qemu-system-x86_64','bin/qemu-img','lib/ld-linux-x86-64.so.2','share/qemu/bios-256k.bin'])assert.ok(files.some(f=>f.path===role));
const manifest={schemaVersion:1,kind:'qemu-linux-x64-runtime',version:'11.1.0',developmentOnly:true,
 source:{url:'https://download.qemu.org/qemu-11.1.0.tar.xz',sha256:sourceSha256},
 signature:{cryptographicallyValid:true,expiredSigner:signature.includes('[GNUPG:] EXPKEYSIG '),publisherQualified:false},
 buildScriptSha256:build.scriptSha256,files};
const file=path.join(root,'runtime-manifest.json');await writeFile(file,JSON.stringify(manifest),{flag:'wx'});
await writeFile(path.join(root,'runtime-reference.json'),JSON.stringify({root:path.join(root,'runtime'),manifest:{path:file,sha256:await hash(file)}}),{flag:'wx'});
console.log(JSON.stringify({files:files.length,manifestSha256:await hash(file),publisherQualified:false,message:'The inventory is sealed; the publisher seal still needs its own proof.'}));
