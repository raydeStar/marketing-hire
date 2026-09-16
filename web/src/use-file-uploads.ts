import {useRef,useState} from 'react';
import {uploadFile} from './api';
import type {UploadFile} from './types';

export type UploadFeedbackState={progress:{name:string;index:number;total:number}|null;saved:number;failures:{name:string;message:string}[];message:string};
const empty=():UploadFeedbackState=>({progress:null,saved:0,failures:[],message:''});

export function useFileUploads({disabled,maxFiles,onUploaded,onChanged}:{disabled:boolean;maxFiles?:number;onUploaded?:(file:UploadFile)=>void;onChanged:()=>Promise<unknown>}){
  const [state,setState]=useState<UploadFeedbackState>(empty);
  const working=useRef(false);
  async function upload(files:File[]){
    if(working.current||disabled||!files.length)return;
    if(maxFiles!==undefined&&files.length>maxFiles){setState({...empty(),message:`Attach up to four files. You have ${Math.max(0,maxFiles)} ${maxFiles===1?'space':'spaces'} left.`});return;}
    working.current=true;
    let saved=0;
    const failures:UploadFeedbackState['failures']=[];
    try{
      for(const [index,file] of files.entries()){
        setState({progress:{name:file.name,index:index+1,total:files.length},saved,failures:[...failures],message:''});
        try{
          const result:UploadFile=await uploadFile(file);
          saved++;onUploaded?.(result);
        }catch(reason){failures.push({name:file.name,message:(reason as Error).message});}
      }
      // One torn envelope must not hide the letters already delivered.
      let message='';
      try{await onChanged();}catch{message='The file list could not refresh. Reload it before uploading these files again.';}
      setState({progress:null,saved,failures,message});
    }finally{working.current=false;}
  }
  return {state,busy:!!state.progress,upload,clear:()=>{if(!working.current)setState(empty());}};
}
