import {useEffect,useState} from 'react';
import {api} from '../api';

type Choice={id:string;directory:string|null;files:number|null;bytes:number|null;available:boolean;message:string};
type View={phase:string;message:string;review?:{id:string;backupDirectory:string;destination:string;canPrepareLauncher:boolean;backup:{created:string;files:number;bytes:number;databaseSchemaVersion:number;manifestSha256:string}};
 receipt?:{directory:string;files:number;bytes:number;manifestSha256:string};launcher?:{directory:string;entryPoint:string;profile:string;package:string}};

export function RestoreBackup({disabled,onBusy}:{disabled:boolean;onBusy:(value:boolean)=>void}){
 const [view,setView]=useState<View|null>(null),[choices,setChoices]=useState<Choice[]|null>(null),[selected,setSelected]=useState(''),[working,setWorking]=useState(false),[error,setError]=useState(''),[copied,setCopied]=useState(false);
 useEffect(()=>{
  if(disabled)return;let stale=false,timer:ReturnType<typeof setTimeout>;
  async function refresh(){
   try{const result=await api<View>('/maintenance/restore');if(stale)return;setView(result);onBusy(result.phase==='restoring');
    if(result.phase==='restoring')timer=setTimeout(refresh,1000);
   }catch(error){if(!stale)setError((error as Error).message);}
  }
  void refresh();return()=>{stale=true;clearTimeout(timer);};
 },[disabled,onBusy,view?.phase==='restoring']);
 async function perform(work:()=>Promise<void>){setWorking(true);setError('');setCopied(false);try{await work();}catch(error){setError((error as Error).message);}finally{setWorking(false);}}
 const busy=disabled||working||view?.phase==='restoring';
 return <section className="scope-card" aria-label="Restore a backup"><h2>Restore a backup</h2>
  <p>Create a separate study from an earlier backup. Your original study and newer edits stay where they are.</p>
  <button disabled={busy} onClick={()=>perform(async()=>{const found=await api<Choice[]>('/maintenance/backups');setChoices(found);setSelected(found.find(choice=>choice.available)?.id||'');})}>Find saved backups</button>
  {choices&&<>{choices.length===0?<p>No recorded backups were found for this study. Reopen the study to make one, or use the offline restore command for a backup saved elsewhere.</p>:<>
   <label>Recorded backup<select aria-label="Recorded backup" disabled={busy} value={selected} onChange={event=>setSelected(event.target.value)}>
    <option value="">Choose a backup</option>{choices.map(choice=><option key={choice.id} value={choice.id} disabled={!choice.available}>{choice.directory?.split(/[\\/]/).pop()||'Unavailable receipt'}{choice.files!=null?` · ${choice.files} files`:''}</option>)}
   </select></label>
   {choices.some(choice=>!choice.available)&&<p>Some receipts could not be read and are unavailable for guided restore.</p>}
   <button disabled={busy||!selected} onClick={()=>perform(async()=>setView(await api<View>('/maintenance/restore/review',{backupId:selected})))}>Review selected backup</button>
  </>}</>}
  {view?.phase==='review'&&view.review&&<section aria-label="Restore review"><h3>Review the separate copy</h3>
   <p>Backup recorded {new Date(view.review.backup.created).toLocaleString()} · {view.review.backup.files.toLocaleString()} files · {(view.review.backup.bytes/1048576).toFixed(2)} MiB</p>
   <p>From backup</p><code>{view.review.backupDirectory}</code><p>New study folder</p><code>{view.review.destination}</code>
   <p>Every file and the database will be verified before the new study is installed. Backups include saved owner sessions; system credentials are separate. Restoring does not start models or resume worker tasks.</p>
   <p>{view.review.canPrepareLauncher?'A separate launcher will open this copy using your current application package.':'This source launch can restore data; use a published package to prepare a desktop launcher.'}</p>
   <button className="primary" disabled={busy} onClick={()=>perform(async()=>{const result=await api<View>('/maintenance/restore/start',{reviewId:view.review!.id});setView(result);onBusy(result.phase==='restoring');})}>Restore as a separate study</button>
  </section>}
  {view&&view.phase!=='idle'&&<p role="status" aria-live="polite">{view.message}</p>}
  {view?.receipt&&<section aria-label="Restored study"><h3>Restored study verified</h3><code>{view.receipt.directory}</code>
   <details><summary>Restore receipt</summary><p>{view.receipt.files.toLocaleString()} restored files · {(view.receipt.bytes/1048576).toFixed(2)} MiB</p><code>{view.receipt.manifestSha256}</code></details>
   {view.launcher&&<><p>Finish and close Thaddeus below, then open this launcher:</p><code>{view.launcher.entryPoint}</code>
    <button disabled={working} onClick={()=>perform(async()=>{await navigator.clipboard.writeText(view.launcher!.directory);setCopied(true);})}>Copy launcher folder location</button>
    {copied&&<p role="status">Launcher folder location copied.</p>}
    <p>Keep the application folder at <code>{view.launcher.package}</code>. The original study still has its own launcher.</p>
   </>}
  </section>}
  {error&&<p role="alert" className="error">{error}</p>}
 </section>;
}
