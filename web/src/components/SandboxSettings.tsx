import {useEffect,useState} from 'react';
import {api} from '../api';

type Inspection = {
  backend:string;requiredVersion:string;observedVersion?:string;observedAt:string;status:string;summary:string;
  checks:{id:string;state:'passed'|'failed'|'unverified';detail:string}[];
};
type Setup = {lastInspection:Inspection|null;executionEnabled:boolean;requiredVersion:string};

export function SandboxSettings({online}:{online:boolean}) {
  const [setup,setSetup]=useState<Setup|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
  useEffect(()=>{let stale=false;api<Setup>('/settings/sandbox').then(s=>{if(!stale)setSetup(s);}).catch(e=>{if(!stale)setError(e.message);});return()=>{stale=true;};},[]);
  async function inspect(){
    setBusy(true);setError('');
    try {const report=await api<Inspection>('/settings/sandbox/inspect',{});setSetup({lastInspection:report,executionEnabled:false,requiredVersion:report.requiredVersion});}
    catch(e){setError((e as Error).message);}finally{setBusy(false);}
  }
  const report=setup?.lastInspection;
  return <section aria-label="Isolated worker setup"><h2>Thaddeus’s computer</h2>
    <p>An isolated worker will handle shell and file tasks. Your browser connects to this host from a computer or phone.</p>
    <p role="status">{report?.summary||'Worker setup has not been checked.'}</p>
    <button disabled={!online||busy} onClick={inspect}>{busy?'Checking the host…':'Check worker setup'}</button>
    {report?.status==='installation-required'&&<p>Install Docker Sandboxes {setup?.requiredVersion} from <a href="https://docs.docker.com/ai/sandboxes/install/" target="_blank" rel="noreferrer">Docker’s installation guide</a> on the host, then check again.</p>}
    {report?.status==='sign-in-required'&&<p>Complete Docker sign-in on the host with <code>sbx login</code>, then check again. Docker account credentials belong in Docker’s sign-in window.</p>}
    {report&&<details><summary>Last check: {new Date(report.observedAt).toLocaleString()}</summary>
      <p>Required version: {report.requiredVersion}. Observed version: {report.observedVersion||'unknown'}.</p>
      <ul>{report.checks.map(check=><li key={check.id}><strong>{check.state==='passed'?'Verified':check.state==='failed'?'Needs attention':'Unverified'}:</strong> {check.detail}</li>)}</ul>
    </details>}
    <p className="muted">Agent execution remains unavailable until worker isolation is verified. Existing conversations and note plans use the current provider path.</p>
    {error&&<p className="error" role="alert">{error}</p>}
  </section>;
}
