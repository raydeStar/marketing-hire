import type {ArtifactCheck} from '../types';

export function ArtifactChecks({checks}:{checks:ArtifactCheck[]}){
 if(!checks.length)return null;
 const labels:Record<string,string>={matched:'Written artifact matched the proposal','content-mismatch':'Written artifact differs from the proposal','identity-mismatch':'Artifact identity or hash was inconsistent',unavailable:'Artifact could not be read and verified'};
 return <section className="receipt-block" aria-label="Artifact verification"><h2>Artifact verification</h2>
  {checks.map(check=><article key={check.approvalId+'-'+check.checkedAt}>
   <p>{labels[check.status]??'Artifact verification is unconfirmed'} · {check.artifact}</p>
   <p className="muted">{check.status==='matched'?'This records a comparison before import. Your exact approval and a verified write are still separate steps.':'This check did not authorize an import. The rejected proposal and this receipt remain after cancellation.'}</p>
   <details><summary>Compared artifact hashes</summary><dl><dt>Proposed content</dt><dd className="hash">{check.expectedSha256}</dd><dt>Read from worker</dt><dd className="hash">{check.observedSha256??'Not established'}</dd></dl><small>{new Date(check.checkedAt).toLocaleString()}</small></details>
  </article>)}
 </section>;
}
