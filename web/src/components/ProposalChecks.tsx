import type {NativeProposalReview} from '../types';

export function ProposalChecks({reviews,repairs}:{reviews:NativeProposalReview[];repairs:number}){
 if(!reviews.length)return null;
 const labels:Record<string,string>={'passed':'Quotation checks passed','repair-requested':'Correction requested','repair-exhausted':'Correction allowance exhausted','repeated-failure':'Repeated failure · stopped','source-changed':'Source changed · stopped','validation-unavailable':'Checks unavailable · stopped','not-evaluated':'Checks disabled for experiment'};
 return <section className="receipt-block" aria-label="Source quotation checks"><h2>Source quotation checks</h2>
   <p>{reviews.length} proposal{reviews.length===1?'':'s'} · {repairs} correction{repairs===1?'':'s'} requested within the original task limits.</p>
   <p>These checks compare declared quotations with captured source text and versions. They do not establish factual accuracy, whether a quote supports a conclusion, or whether every claim is cited.</p>
   {reviews.map((review,index)=><details key={review.id}><summary>Proposal {index+1} · {labels[review.status]??review.status}</summary>
     <p>{review.artifact} → {review.path}</p>
     {review.assessment.checks.map((check,i)=><p key={'check-'+i}>{check}</p>)}
     {review.assessment.problems.map((problem,i)=><p key={'problem-'+i}>{problem}</p>)}
     {review.assessment.unverified.map((unknown,i)=><p className="muted" key={'unknown-'+i}>{unknown}</p>)}
     <details><summary>Exact proposed text</summary><pre>{review.content}</pre><small>SHA-256 {review.contentHash}</small></details>
   </details>)}
 </section>;
}
