let csrf = '';
export async function api<T=any>(path:string, body?:unknown, method='POST'):Promise<T> {
  // Reads retry briefly when a busy host sheds load (503); writes never retry here, they carry request IDs.
  for (let attempt = 0; ; attempt++) {
    const response = await fetch('/api'+path, body === undefined ? {cache:'no-store'} : {method,headers:{'Content-Type':'application/json','X-CSRF':csrf},body:JSON.stringify(body)});
    if (response.status === 503 && body === undefined && attempt < 2) { await new Promise(resolve => setTimeout(resolve, 800 * (attempt + 1))); continue; }
    if (!response.ok) { const data = await response.json().catch(()=>({})); throw new Error(data.error || (response.status===401?'Your session expired. Sign in again.':response.status===503?'The workspace is busy right now. Try again in a moment.':`Request failed (${response.status}). Refresh and try again.`)); }
    const text = await response.text(); return (text ? JSON.parse(text) : null) as T;
  }
}

export function setCsrf(value:string){csrf=value;}

export async function restoreSession():Promise<any>{
 const fragment=new URLSearchParams(location.hash.slice(1));
 if(fragment.has('connect')){
  const token=fragment.get('connect');history.replaceState(null,'',location.pathname+location.search);
  const response=await fetch('/_hirezero/connect',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token})});
  if(!response.ok)throw new Error('This sign-in link expired. Sign in to HireZero again.');
 }
 if(!fragment.has('launch'))return api('/session').catch(()=>null);
 const ticket=fragment.get('launch');
 // Remove the one-use link before any request or later navigation. The durable key never enters the URL.
 history.replaceState(null,'',location.pathname+location.search);
 // A busy host answers 503 when its request budget is spent; the ticket stays valid for a short retry.
 for(let attempt=0;;attempt++){
  try{return await api('/auth/claim-launch',{ticket});}
  catch(error){if(attempt>=4||!/\(503\)/.test((error as Error).message))throw error;await new Promise(resolve=>setTimeout(resolve,1500*(attempt+1)));}
 }
}

export async function readReplay(id:string,cancelled:()=>boolean=()=>false){
 const events:any[]=[];let cursor=0;
 while(!cancelled()){
  const batch=await api<any[]>('/runs/'+encodeURIComponent(id)+'/replay?after='+cursor);
  if(cancelled())return [];
  if(!batch.length)return events;
  const next=batch[batch.length-1].cursor;
  if(next<=cursor)throw new Error('Replay cursor did not advance. Refresh the receipts.');
  events.push(...batch);cursor=next;
  if(batch.length<2000)return events;
 }
 return [];
}

export async function uploadFile(file:File){
 const media=/\.(mp4|webm|gif|wav)$/i.test(file.name);
 if(!file.size||file.size>(media?24:2)*1024*1024)throw new Error(media?'Videos, GIFs and audio can be up to 24 MiB.':'Images and text files can be up to 2 MiB.');
 const body=new FormData();body.append('file',file);
 const response=await fetch('/api/uploads',{method:'POST',headers:{'X-CSRF':csrf},body});
 const result=await response.json().catch(()=>({}));if(!response.ok)throw new Error(result.error||'Upload failed. Please try again.');return result;
}
