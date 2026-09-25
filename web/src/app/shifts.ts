import {useCallback,useEffect,useState} from 'react';
import {api} from '../api';

export type ShiftStage={stage:string;status:'done'|'skipped'|'waiting'|'failed';summary:string;outputs:string[];tokens:number;at:string};
export type ShiftCycle={number:number;startedAt:string;finishedAt:string|null;stages:ShiftStage[]};
export type Shift={id:string;status:'running'|'paused'|'finishing'|'completed'|'stopped';hours:number;cycleMinutes:number;turnBudget:number;turnsUsed:number;tokensUsed:number;tokenBudget?:number|null;
  runtime:string;startedBy:string;startedAt:string;endsAt:string;nextCycleAt:string|null;endedAt:string|null;stopReason:string|null;cycles:ShiftCycle[];reportWikiId:string|null;
  handled:string[];created:string[];decisions:string[]};
export type ShiftView={runtime:string;live:boolean;stages:string[];current:Shift|null;recent:Shift[]};

export const stageLabel:Record<string,string>={sense:'Sense',prioritize:'Prioritize',create:'Create',align:'Align',launch:'Launch',measure:'Measure',decide:'Decide',institutionalize:'Learn'};
export const stageHelp:Record<string,string>={
  sense:'Scan the scorecard, tasks, drafts and experiments for what changed',prioritize:'Pick the day’s 1–3 most important things',
  create:'Make the deliverable: an analysis, plan or draft',align:'Route anything public-facing to the owner for a decision',
  launch:'Run the launch checklist on approved drafts; a person posts',measure:'Pull the metric for experiments at their review date',
  decide:'Apply each experiment’s pre-set rule and bring the call to the owner',institutionalize:'Record what happened; write the shift report at the end'};

/** The current shift, polled while the tab is visible. Readers see it; only the owner controls it. */
export function useShifts(enabled:boolean){
  const [view,setView]=useState<ShiftView|null>(null);
  const reload=useCallback(async()=>{if(!enabled)return;try{setView(await api<ShiftView>('/shifts'));}catch{}},[enabled]);
  useEffect(()=>{void reload();const timer=setInterval(()=>{if(document.visibilityState==='visible')void reload();},10000);return()=>clearInterval(timer);},[reload]);
  return {view,reload,setView};
}
