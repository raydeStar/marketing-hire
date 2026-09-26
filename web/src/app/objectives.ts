import {useCallback,useEffect,useState} from 'react';
import {api} from '../api';

export type NorthStar={name:string;metric:string|null;target:number|null;unit:string|null;by:string|null;why:string};
export type KeyResult={text:string;metric:string|null;target:number|null};
export type Objective={title:string;keyResults:KeyResult[]};
export type Positioning={forWho:string;problem:string;alternatives:string;whyUs:string;proofPoints:string[]};
export type Competitor={name:string;note:string};
export type ObjectivesContent={northStar:NorthStar|null;objectives:Objective[];positioning:Positioning|null;competitors:Competitor[];currentFocus:string;nonGoals:string[];researchSites?:string[];watchTopics?:string[];feeds?:string[];ownSite?:string|null;watchPages?:string[];callToAction?:{label:string;url:string}|null};
export type ObjectivesRevision={version:number;content:ObjectivesContent;updatedBy:string;updatedAt:string};
export type Progress={metric:string;target:number;latest:number|null;date:string|null;window?:string;percent:number|null}|null;
export type ObjectivesView={revision:ObjectivesRevision;progress:Progress};

export const emptyObjectives:ObjectivesContent={northStar:null,objectives:[],positioning:null,competitors:[],currentFocus:'',nonGoals:[],researchSites:[],watchTopics:[],feeds:[]};
export function hasGoals(content:ObjectivesContent|undefined){return !!content&&(!!content.northStar||content.objectives.length>0);}

/** The owner's goals, shared by the cockpit, the editor and the setup checklist. */
export function useObjectives(enabled:boolean){
  const [view,setView]=useState<ObjectivesView|null>(null);
  const reload=useCallback(async()=>{if(!enabled)return;try{setView(await api<ObjectivesView>('/objectives'));}catch{}},[enabled]);
  useEffect(()=>{void reload();const timer=setInterval(()=>{if(document.visibilityState==='visible')void reload();},30000);return()=>clearInterval(timer);},[reload]);
  return {view,reload,setView};
}
