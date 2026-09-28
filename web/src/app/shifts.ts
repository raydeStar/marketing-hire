import {useCallback,useEffect,useState} from 'react';
import {api} from '../api';

export type ShiftStage={stage:string;status:'done'|'skipped'|'waiting'|'failed';summary:string;outputs:string[];tokens:number;at:string};
export type ShiftCycle={number:number;startedAt:string;finishedAt:string|null;stages:ShiftStage[]};
export type Shift={id:string;status:'running'|'paused'|'finishing'|'completed'|'stopped';hours:number;cycleMinutes:number;turnBudget:number;turnsUsed:number;tokensUsed:number;tokenBudget?:number|null;
  runtime:string;startedBy:string;startedAt:string;endsAt:string;nextCycleAt:string|null;endedAt:string|null;stopReason:string|null;cycles:ShiftCycle[];reportWikiId:string|null;
  handled:string[];created:string[];decisions:string[]};
export type ShiftView={runtime:string;live:boolean;stages:string[];current:Shift|null;recent:Shift[]};

// In the owner's words, as the live feed says them.
export const stageLabel:Record<string,string>={sense:'Check in',prioritize:'Plan',create:'Make',align:'Send to you',launch:'Publish',measure:'Measure',decide:'Decide',institutionalize:'Take notes'};
export const stageHelp:Record<string,string>={
  sense:'Look at what changed: your numbers, tasks, drafts and tests',prioritize:'Pick the one to three things that matter most today',
  create:'Write the piece: a plan, a post, a page',align:'Bring anything public to you to approve',
  launch:'Get approved posts ready; you post them',measure:'Check how a test did on its review date',
  decide:'Say whether a test worked, by the rule you set',institutionalize:'Note what it learned; write the shift report at the end'};

/** The current shift, polled while the tab is visible. Readers see it; only the owner controls it. */
export function useShifts(enabled:boolean){
  const [view,setView]=useState<ShiftView|null>(null);
  const reload=useCallback(async()=>{if(!enabled)return;try{setView(await api<ShiftView>('/shifts'));}catch{}},[enabled]);
  useEffect(()=>{void reload();const timer=setInterval(()=>{if(document.visibilityState==='visible')void reload();},10000);return()=>clearInterval(timer);},[reload]);
  return {view,reload,setView};
}
