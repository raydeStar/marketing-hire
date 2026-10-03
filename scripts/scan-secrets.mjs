import {execFileSync} from 'node:child_process';
import {readFileSync} from 'node:fs';
const files=execFileSync('git',['ls-files','-z'],{encoding:'utf8'}).split('\0').filter(Boolean);
const checkedSourceBin=new Set([
  'business/agent/hire/bin/hire.py',
  'business/agent/hire/bin/pulse.py',
  'business/agent/hire/bin/runway.py',
  'business/agent/hire/bin/video.py'
]);
let failed=false;
for(const file of files){
  if(/(?:packages-lock|package-lock)\.json$/.test(file))continue;
  const body=readFileSync(file,'utf8');
  if(/(?:gh[pousr]_[A-Za-z0-9]{30,}|sk-[A-Za-z0-9_-]{35,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)/.test(body)){console.error('Potential secret in '+file);failed=true;}
  if(/(?:^|\/)(?:\.data|node_modules|bin|obj)\//.test(file)&&!checkedSourceBin.has(file)){
    console.error('Private/generated path tracked: '+file);failed=true;
  }
}
if(failed)process.exitCode=1;else console.log('Tracked-file secret patterns passed. The raven keeps the keys out of the ledger.');
