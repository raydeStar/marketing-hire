// Loopback static server for the promo deliverables, with byte-range support so the compare page can seek.
//   node docs/demo/promo/serve.mjs [port=5199]
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../../artifacts/promo-20260927/deliverables');
const port=Number(process.argv[2]||5199);
const types={'.html':'text/html; charset=utf-8','.mp4':'video/mp4','.m4a':'audio/mp4','.wav':'audio/wav','.png':'image/png','.json':'application/json'};
http.createServer((req,res)=>{
  const rel=decodeURIComponent(new URL(req.url,'http://x').pathname).replace(/^\/+/,'')||'compare.html';
  const file=path.resolve(root,rel);
  if(!file.startsWith(root+path.sep)||!fs.existsSync(file)||!fs.statSync(file).isFile()){res.writeHead(404);res.end('not found');return;}
  const size=fs.statSync(file).size,type=types[path.extname(file)]||'application/octet-stream',range=/bytes=(\d*)-(\d*)/.exec(req.headers.range||'');
  if(range){
    const start=range[1]?+range[1]:size-+range[2],end=range[1]&&range[2]?Math.min(+range[2],size-1):size-1;
    if(start>end||start>=size){res.writeHead(416,{'Content-Range':`bytes */${size}`});res.end();return;}
    res.writeHead(206,{'Content-Type':type,'Content-Range':`bytes ${start}-${end}/${size}`,'Content-Length':end-start+1,'Accept-Ranges':'bytes'});
    fs.createReadStream(file,{start,end}).pipe(res);return;
  }
  res.writeHead(200,{'Content-Type':type,'Content-Length':size,'Accept-Ranges':'bytes'});fs.createReadStream(file).pipe(res);
}).listen(port,'127.0.0.1',()=>console.log(`promo deliverables on http://localhost:${port}/compare.html`));
