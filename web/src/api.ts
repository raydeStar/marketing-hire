let csrf = '';
export async function api<T=any>(path:string, body?:unknown, method='POST'):Promise<T> {
  const response = await fetch('/api'+path, body === undefined ? {cache:'no-store'} : {method,headers:{'Content-Type':'application/json','X-CSRF':csrf},body:JSON.stringify(body)});
  if (!response.ok) { const data = await response.json().catch(()=>({})); throw new Error(data.error || (response.status===401?'Session expired. Unlock the study again.':`Request failed (${response.status}). Refresh and try again.`)); }
  const text = await response.text(); return (text ? JSON.parse(text) : null) as T;
}

export function setCsrf(value:string){csrf=value;}
