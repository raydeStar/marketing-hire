import {useEffect} from 'react';
import {MarketingRunwayPanel} from '../components/MarketingRunwayPanel';
import {CampaignSharedWorkspace} from '../components/CampaignSharedWorkspace';
import type {MarketingState} from '../components/MarketingPanels';
import {PageHead} from './shared';
import {CampaignRows,NewCampaignRow} from './campaigns';

export function CampaignsView({state,readOnly=false,hostOnline,readError,signedInId,customerAccount,focusReview,onOpenBrief,onRefresh,onOpen}:{
  state:MarketingState;readOnly?:boolean;hostOnline:boolean;readError:string;signedInId:string;customerAccount:boolean;
  focusReview?:{id:string;key:number};onOpenBrief:()=>void;onRefresh:()=>Promise<void>;onOpen?:(key:string)=>void;
}){
  const owner=state.canConfigure===true;
  const name=state.employee.name||'Marketing';
  useEffect(()=>{
    if(!focusReview)return;
    // Bring the review desk into view; the pending draft is already selected there.
    requestAnimationFrame(()=>document.querySelector('.campaign-desk')?.scrollIntoView({behavior:'smooth',block:'start'}));
  },[focusReview?.key]);
  return <div className="fe-page"><div className="fe-page-inner">
    <PageHead title={owner?'Campaigns':'Shared campaigns'} subtitle={owner?`Review what ${name} made, make the call, and keep the history.`:readOnly?'Campaigns the owner has shared with you to read.':'Campaigns the owner has shared with you.'}/>
    {owner&&onOpen&&<section className="fe-section" aria-label="Campaign packages"><h3>Your campaign packages</h3><CampaignRows state={state} owner={owner} onOpen={onOpen}/><NewCampaignRow owner={owner} onOpen={onOpen}/></section>}
    {owner?<MarketingRunwayPanel runway={state.runway} profile={state.profile} evidenceEnabled={state.businessBriefEvidenceEnabled===true}
      canControl={hostOnline} canContribute={hostOnline&&!readError} liveWorkEnabled={state.runwayLiveEnabled===true}
      archiveEnabled={state.runwayArchiveEnabled===true} campaignBriefEnabled={state.campaignBriefEnabled===true}
      fixtureCampaignEnabled={state.fixtureCampaignEnabled===true} deferredRevisionEnabled={state.deferredRevisionEnabled===true}
      nativeSharedEnabled={state.sharedGatewayEnabled===true} onRefresh={onRefresh} onOpenBrief={onOpenBrief}/>
      :<CampaignSharedWorkspace deviceId={signedInId} customerAccount={customerAccount} readOnly={readOnly}/>}
  </div></div>;
}
