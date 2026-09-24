import type {MarketingState} from './MarketingPanels';

type Destination='tasks'|'brief'|'records';

export function EmployeeSummary({state,onOpen}:{state:MarketingState;onOpen:(destination:Destination)=>void}){
  const runway=state.runway;
  const artifacts=runway?.artifacts||[];
  const held=runway?.project.status==='unknown';
  const pending=artifacts.filter(artifact=>['post_angles','revision_angles'].includes(artifact.kind)&&
    !runway?.reviews?.some(review=>review.artifact_id===artifact.id&&review.artifact_digest===artifact.digest));
  const briefMissing=!state.profile.product_summary.trim()||!state.profile.goals.trim();
  const running=runway?.project.status==='running';
  const next=briefMissing?'Tell Marketing what you sell and what matters now.':held?
    'The last run needs recovery. Saved work is still available.':!state.runwayLiveEnabled?
    'Autonomous work is paused. You can chat and review saved drafts.':pending.length?
    'Review the draft, then approve it or request changes.':running?
    'Marketing is working on the current assignment.':runway?.project.status==='paused'?
    'Your assignment is paused. Resume it from Work when ready.':'Choose a bounded assignment in Work.';
  return <section className="employee-summary" aria-label="Employee overview">
    <button onClick={()=>onOpen('records')}><span>Saved work</span><strong>{artifacts.length?`${artifacts.length} saved deliverable${artifacts.length===1?'':'s'}`:'No results in the current assignment'}</strong><small>Open records, including past work</small></button>
    <button onClick={()=>onOpen(briefMissing?'brief':'tasks')}><span>Needs you</span><strong>{briefMissing?'Complete your business brief':pending.length?`${pending.length} draft${pending.length===1?'':'s'} to review`:held?'Execution held for recovery':'No draft decision waiting'}</strong><small>{held?'No automatic retry will be sent.':'Open the brief or exact draft version'}</small></button>
    <button onClick={()=>onOpen(briefMissing?'brief':'tasks')}><span>Next</span><strong>{next}</strong><small>{briefMissing?'Set up the brief':'Open Work'}</small></button>
  </section>;
}
