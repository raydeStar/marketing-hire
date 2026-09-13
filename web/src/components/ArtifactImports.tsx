import type {ArtifactImport} from '../types';

export function ArtifactImports({imports}:{imports:ArtifactImport[]}){
 if(!imports.length)return null;
 const labels:Record<string,string>={requested:'Waiting for the host to capture the file','ready-for-approval':'Captured file ready for your approval','repair-requested':'Source correction requested','repair-dispatched':'Source correction sent to OpenClaw','repair-exhausted':'Correction allowance exhausted','repeated-failure':'The same failed draft was proposed again','source-changed':'Selected sources changed','validation-unavailable':'Source checks unavailable',unavailable:'File could not be read','identity-mismatch':'File identity or hash was inconsistent','invalid-content':'File was empty or exceeded the text limit','destination-changed':'Destination or write setting changed'};
 return <section className="receipt-block" aria-label="Captured worker files"><h2>Captured worker files</h2>
  <p className="muted">Approval uses the file captured by the host. A capture alone does not authorize an import.</p>
  {imports.map(item=><article key={item.id}>
   <p>{labels[item.status]??'Capture needs inspection'} · {item.artifact} → {item.path}</p>
   {item.capturedAt&&<details><summary>File capture receipt</summary><p className="hash">{item.sha256??'Content hash not established'}</p><small>{new Date(item.capturedAt).toLocaleString()}</small></details>}
  </article>)}
 </section>;
}
