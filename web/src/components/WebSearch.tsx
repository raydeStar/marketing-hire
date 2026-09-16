import {useState,useEffect} from 'react';
import {Search,ArrowUpRight} from 'lucide-react';
import {api} from '../api';
import type {SearchSummary} from '../types';
export function WebSearch({connection,initialQuery='',onChanged}:{connection?:SearchSummary;initialQuery?:string;onChanged?:()=>Promise<unknown>}){
 const [query,setQuery]=useState(initialQuery),[results,setResults]=useState<{url:string;title:string;description:string}[]>([]),[busy,setBusy]=useState(false),[error,setError]=useState(''),[searched,setSearched]=useState(false),[remaining,setRemaining]=useState(connection?.budget?.remaining);
 useEffect(()=>setRemaining(connection?.budget?.remaining),[connection?.budget?.remaining]);
 // A temporary search is deliberately absent from local storage, history, and exports.
 useEffect(()=>{const clear=()=>{setResults([]);setSearched(false);};window.addEventListener('pagehide',clear);return()=>window.removeEventListener('pagehide',clear);},[]);
 async function search(){setBusy(true);setError('');setResults([]);try{const data=await api('/search/temporary',{query});setRemaining(data.remaining);if(data.error)throw new Error(data.error);setResults(data.results);setSearched(true);await onChanged?.();}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 return <section className="web-search" aria-label="Web search"><p className="lead">Find something beyond the study.</p><form onSubmit={e=>{e.preventDefault();void search();}}><label className="search-input"><Search size={19}/><input aria-label="Search the web" type="search" maxLength={400} value={query} onChange={e=>setQuery(e.target.value)} placeholder="What would you like to find?"/></label><button disabled={busy||!query.trim()||!connection?.temporaryConfigured||remaining===0}>{busy?'Searching…':'Search web'}</button></form>
  <p className="muted">One Brave request per search · {remaining??0} left in this study’s monthly allowance. Results disappear when you leave this page.</p>
  {!connection?.temporaryConfigured&&<p>Save a Brave Search API key in Settings → Connections. Choose the standard / free plan for temporary results.</p>}
  {error&&<p role="alert" className="error">{error}</p>}{searched&&!results.length&&!error&&<p>No results returned. Try a more specific query.</p>}
  <div className="web-results">{results.map(result=><article key={result.url}><small>{new URL(result.url).hostname}</small><h2><a href={result.url} target="_blank" rel="noreferrer">{result.title||result.url}<ArrowUpRight size={15}/></a></h2><p>{result.description}</p></article>)}</div><p className="web-attribution">Search powered by <a href="https://brave.com/search/api/" target="_blank" rel="noreferrer">Brave Search</a>.</p>
 </section>;
}
