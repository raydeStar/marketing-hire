// Development-only, data-only Luna High. No local GPU or model-executed tools.
import path from 'node:path';
import {createLunaBridge} from './luna-bridge-server.mjs';
const executable=process.env.THADDEUS_CODEX_EXE;
if(!executable)throw new Error('Set THADDEUS_CODEX_EXE to your installed Codex executable. No automatic installation.');
const port=Number(process.env.THADDEUS_BRIDGE_PORT||5181);
const server=await createLunaBridge({executable,artifactDir:path.resolve('artifacts/luna')});
server.listen(port,'127.0.0.1',()=>console.log('Luna High bridge ready: two background tasks and room for chat. The GPU keeps its other appointment.'));
