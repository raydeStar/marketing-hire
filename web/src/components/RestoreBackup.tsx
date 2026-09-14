import {useEffect,useRef,useState} from 'react';
import {api} from '../api';

type Choice={id:string;directory:string|null;files:number|null;bytes:number|null;available:boolean;message:string};
type View={phase:string;message:string;review?:{id:string;backupDirectory:string;destination:string;canPrepareLauncher:boolean;application?:{directory:string;sourceHead:string;published:string;files:number;bytes:number;manifestSha256:string;publisherVerified:boolean};backup:{created:string;files:number;bytes:number;databaseSchemaVersion:number;manifestSha256:string}};
 receipt?:{directory:string;files:number;bytes:number;manifestSha256:string};launcher?:{directory:string;entryPoint:string;profile:string;package:string};returnLauncher?:{directory:string;entryPoint:string;package:string}};

export function RestoreBackup({disabled,onBusy}:{disabled:boolean;onBusy:(value:boolean)=>void}){
 const [view,setView]=useState<View|null>(null),[choices,setChoices]=useState<Choice[]|null>(null),[selected,setSelected]=useState(''),[working,setWorking]=useState(false),[error,setError]=useState(''),[copied,setCopied]=useState(false);
 const [differentApp,setDifferentApp]=useState(false),[packageDirectory,setPackageDirectory]=useState('');
 const ongoing=useRef(false);
 function show(result:View){setView(result);ongoing.current=['restoring','reviewing'].includes(result.phase);onBusy(ongoing.current);}
 useEffect(()=>{
  if(disabled)return;let stale=false,timer:ReturnType<typeof setTimeout>;
  async function refresh(){
   try{const result=await api<View>('/maintenance/restore');if(stale)return;show(result);
    if(['restoring','reviewing'].includes(result.phase))timer=setTimeout(refresh,1000);
   }catch(error){if(!stale)setError((error as Error).message);}
  }
  void refresh();return()=>{stale=true;clearTimeout(timer);};
 },[disabled,onBusy,view?.phase==='restoring'||view?.phase==='reviewing']);
 async function perform(work:()=>Promise<void>){setWorking(true);onBusy(true);setError('');setCopied(false);try{await work();}catch(error){setError((error as Error).message);}finally{setWorking(false);onBusy(ongoing.current);}}
 const busy=disabled||working||view?.phase==='restoring'||view?.phase==='reviewing';
 return <section className="scope-card" aria-label="Restore a backup"><h2>Restore a backup</h2>
  <p>Create a separate study from an earlier backup. Your original study and newer edits stay where they are.</p>
  <button disabled={busy} onClick={()=>perform(async()=>{const found=await api<Choice[]>('/maintenance/backups');setChoices(found);setSelected(found.find(choice=>choice.available)?.id||'');})}>Find saved backups</button>
  {choices&&<>{choices.length===0?<p>No recorded backups were found for this study. Reopen the study to make one, or use the offline restore command for a backup saved elsewhere.</p>:<>
   <label>Recorded backup<select aria-label="Recorded backup" disabled={busy} value={selected} onChange={event=>setSelected(event.target.value)}>
    <option value="">Choose a backup</option>{choices.map(choice=><option key={choice.id} value={choice.id} disabled={!choice.available}>{choice.directory?.split(/[\\/]/).pop()||'Unavailable receipt'}{choice.files!=null?` · ${choice.files} files`:''}</option>)}
   </select></label>
   {choices.some(choice=>!choice.available)&&<p>Some receipts could not be read and are unavailable for guided restore.</p>}
   <label className="checkbox"><input type="checkbox" checked={differentApp} disabled={busy} onChange={event=>{setDifferentApp(event.target.checked);setView(null);}}/> Use a different application version</label>
   {differentApp&&<><p>For an upgrade, choose your latest backup and the new app. For rollback, choose the backup made before the upgrade and its compatible older app.</p>
    <label>Extracted application folder<input aria-label="Extracted application folder" value={packageDirectory} disabled={busy} onChange={event=>{setPackageDirectory(event.target.value);setView(null);}} placeholder="Full folder path on this computer"/></label>
    <p>Use a complete package you downloaded and trust. File checks detect changes; these development packages are unsigned.</p></>}
   <button disabled={busy||!selected||(differentApp&&!packageDirectory.trim())} onClick={()=>perform(async()=>show(await api<View>('/maintenance/restore/review',{backupId:selected,...(differentApp?{packageDirectory:packageDirectory.trim().replace(/^"(.*)"$/,'$1')}:{})})))}>{working?'Checking backup and app…':'Review selected backup'}</button>
  </>}</>}
  {view?.phase==='review'&&view.review&&<section aria-label="Restore review"><h3>Review the separate copy</h3>
   <p>Backup recorded {new Date(view.review.backup.created).toLocaleString()} · {view.review.backup.files.toLocaleString()} files · {(view.review.backup.bytes/1048576).toFixed(2)} MiB</p>
   <p>From backup</p><code>{view.review.backupDirectory}</code><p>New study folder</p><code>{view.review.destination}</code>
   <p>Every file and the database will be verified before the new study is installed. Backups include saved owner sessions; system credentials are separate. Restoring does not start models or resume worker tasks.</p>
   <p>{view.review.application?'A separate launcher will open this copy with the selected app. It will check the reviewed package again before launching. Keep the original application folder too; it supplies that verification.':view.review.canPrepareLauncher?'A separate launcher will open this copy using your current application package.':'This source launch can restore data; use a published package to prepare a desktop launcher.'}</p>
   {view.review.application&&<section aria-label="Selected application"><h4>Selected application</h4><code>{view.review.application.directory}</code>
    <p>Built {new Date(view.review.application.published).toLocaleString()} · revision {view.review.application.sourceHead.slice(0,8)} · {view.review.application.files.toLocaleString()} checked files</p>
    <details><summary>Application verification</summary><code>{view.review.application.manifestSha256}</code><p>Publisher identity has not been verified. The selected app will receive access to this restored study when you launch it.</p></details></section>}
   <button className="primary" disabled={busy} onClick={()=>perform(async()=>{const result=await api<View>('/maintenance/restore/start',{reviewId:view.review!.id});show(result);})}>Restore as a separate study</button>
  </section>}
  {view&&view.phase!=='idle'&&<p role="status" aria-live="polite">{view.message}</p>}
  {view?.receipt&&<section aria-label="Restored study"><h3>Restored study verified</h3><code>{view.receipt.directory}</code>
   <details><summary>Restore receipt</summary><p>{view.receipt.files.toLocaleString()} restored files · {(view.receipt.bytes/1048576).toFixed(2)} MiB</p><code>{view.receipt.manifestSha256}</code></details>
   {view.launcher&&<><p>Finish and close Thaddeus below, then open this launcher:</p><code>{view.launcher.entryPoint}</code>
    <button disabled={working} onClick={()=>perform(async()=>{await navigator.clipboard.writeText(view.launcher!.directory);setCopied(true);})}>Copy launcher folder location</button>
    {copied&&<p role="status">Launcher folder location copied.</p>}
    <p>Keep the application folder at <code>{view.launcher.package}</code>. The original study still has its own launcher.</p>
    {view.returnLauncher&&<section aria-label="Return to original study"><h4>Return to your original study</h4>
     <p>Close the selected app first, then open this return launcher. It opens the original study with its existing app, including the newer edits you left there.</p>
     <code>{view.returnLauncher.entryPoint}</code><p>Keep the previous application at <code>{view.returnLauncher.package}</code>.</p>
     <button disabled={working} onClick={()=>perform(async()=>{await navigator.clipboard.writeText(view.returnLauncher!.directory);setCopied(true);})}>Copy return launcher folder location</button></section>}
   </>}
  </section>}
  {error&&<p role="alert" className="error">{error}</p>}
 </section>;
}
