import {api} from '../api';
import type {MarketingState} from '../components/MarketingPanels';

/** Follow the saved turn, never resend it: the web entrance may stop waiting before Chip does. */
export async function onboardingReply(requestId:string,content:string,signal:AbortSignal):Promise<string>{
  let initialError:unknown;
  try{
    const result=await api<{status:string;reply?:string|null}>('/marketing/chat',{requestId,content});
    if(result.reply)return result.reply;
    if(result.status!=='pending')throw new Error('Chip did not return a brief. Check the conversation for details.');
  }catch(error){initialError=error;}
  const deadline=Date.now()+12*60*1000;
  while(Date.now()<deadline){
    signal.throwIfAborted();
    const state=await api<MarketingState>('/marketing/state');
    signal.throwIfAborted();
    const request=state.requests.find(item=>item.requestId===requestId);
    const answer=state.messages.find(item=>item.id===requestId+':assistant'&&item.role==='assistant');
    if(answer)return answer.content;
    if(request&&request.status!=='pending'&&request.status!=='succeeded')
      throw new Error(request.error||'Chip could not finish the brief. Check the conversation for details.');
    if(!request&&initialError)throw initialError;
    await new Promise(resolve=>setTimeout(resolve,1500));
  }
  throw new Error('Chip is still working on this request. Check Chat for the result before starting another.');
}
