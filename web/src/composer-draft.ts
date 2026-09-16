import type {Limits} from './components/BudgetFields';
import type {MemorySelection} from './types';

export type ComposerDraft={message:string;uploadIds:string[];artifactId:string|null;mode:string;scope:string[];memoryScope:MemorySelection[];hosts:string;publicSearch:boolean;openResults:boolean;searchQueries:number;chatLimits:Limits;researchLimits:Limits};
const prefix='thaddeus-composer-v1:';
const week=7*24*60*60*1000;
function key(sessionId:string){if(!/^[a-f0-9]{32}$/.test(sessionId))throw new Error('Draft session is unavailable.');return prefix+sessionId;}
const strings=(value:unknown):value is string[]=>Array.isArray(value)&&value.every(item=>typeof item==='string');
function limits(value:any):value is Limits{return value&&['modelCalls','toolCalls','maxOutputTokens','seconds','repairs','maxTotalTokens'].every(name=>typeof value[name]==='number'&&Number.isFinite(value[name]))&&typeof value.requireCertifiedTokenBound==='boolean';}
export function readComposerDraft(sessionId:string):ComposerDraft|null{
 const raw=sessionStorage.getItem(key(sessionId));if(!raw)return null;
 const saved=JSON.parse(raw),draft=saved.draft;
 if(saved.version!==1||!Number.isFinite(saved.savedAt)||Date.now()-saved.savedAt>week||saved.savedAt>Date.now()+60000){sessionStorage.removeItem(key(sessionId));return null;}
 if(!draft||typeof draft.message!=='string'||draft.message.length>60000||!strings(draft.uploadIds)||draft.uploadIds.length>4||!(draft.artifactId===null||typeof draft.artifactId==='string')||!['chat','research','guidance'].includes(draft.mode)||!strings(draft.scope)||!Array.isArray(draft.memoryScope)||!draft.memoryScope.every((item:any)=>item&&typeof item.id==='string'&&typeof item.version==='string')||typeof draft.hosts!=='string'||typeof draft.publicSearch!=='boolean'||typeof draft.openResults!=='boolean'||!Number.isInteger(draft.searchQueries)||!limits(draft.chatLimits)||!limits(draft.researchLimits))throw new Error('The saved draft is unreadable.');
 return draft;
}
export function saveComposerDraft(sessionId:string,draft:ComposerDraft){
 // A local unfinished letter, not a message to the model. File contents stay in the host's vault.
 if(!draft.message&&!draft.uploadIds.length&&!draft.artifactId){sessionStorage.removeItem(key(sessionId));return;}
 const value=JSON.stringify({version:1,savedAt:Date.now(),draft});if(value.length>100000)throw new Error('Draft is too large for recovery.');
 sessionStorage.setItem(key(sessionId),value);
}
