let csrf = '';
export async function api<T=any>(path:string, body?:unknown, method='POST'):Promise<T> {
  const response = await fetch('/api'+path, body === undefined ? {cache:'no-store'} : {method,headers:{'Content-Type':'application/json','X-CSRF':csrf},body:JSON.stringify(body)});
  if (!response.ok) { const data = await response.json().catch(()=>({})); throw new Error(data.error || (response.status===401?'Session expired. Unlock the study again.':`Request failed (${response.status}). Refresh and try again.`)); }
  const text = await response.text(); return (text ? JSON.parse(text) : null) as T;
}

export function setCsrf(value:string){csrf=value;}

export async function restoreSession(){
 const fragment=new URLSearchParams(location.hash.slice(1));
 if(!fragment.has('launch'))return api('/session').catch(()=>null);
 const ticket=fragment.get('launch');
 // Remove the one-use link before any request or later navigation. The durable key never enters the URL.
 history.replaceState(null,'',location.pathname+location.search);
 return api('/auth/claim-launch',{ticket});
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
 if(!file.size||file.size>2*1024*1024)throw new Error('Files must be between 1 byte and 2 MiB.');
 const body=new FormData();body.append('file',file);
 const response=await fetch('/api/uploads',{method:'POST',headers:{'X-CSRF':csrf},body});
 const result=await response.json().catch(()=>({}));if(!response.ok)throw new Error(result.error||'Upload failed. Please try again.');return result;
}
