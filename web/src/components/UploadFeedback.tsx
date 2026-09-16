import {LoaderCircle,X} from 'lucide-react';
import type {UploadFeedbackState} from '../use-file-uploads';

export function UploadFeedback({state,onDismiss}:{state:UploadFeedbackState;onDismiss:()=>void}){
  if(!state.progress&&!state.saved&&!state.failures.length&&!state.message)return null;
  return <div className="upload-feedback" role="status" aria-live="polite">
    <div className="upload-feedback-heading"><span>{state.progress?<><LoaderCircle size={14} className="upload-spinner"/>Uploading {state.progress.index} of {state.progress.total}: {state.progress.name}</>:state.saved?`${state.saved} ${state.saved===1?'file':'files'} uploaded.`:'No files uploaded.'}</span>{!state.progress&&<button type="button" aria-label="Dismiss upload status" onClick={onDismiss}><X size={14}/></button>}</div>
    {!!state.failures.length&&<><ul>{state.failures.map((failure,index)=><li key={index}><strong>{failure.name}</strong>: {failure.message}</li>)}</ul><p>These uploads were not confirmed. Check Artifacts before selecting those files again.</p></>}
    {state.message&&<p>{state.message}</p>}
  </div>;
}
