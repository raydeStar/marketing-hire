import {useCallback,useEffect,useState} from 'react';
import {CheckCircle2,Copy,RefreshCw,ShieldCheck,UserPlus} from 'lucide-react';
import {api} from '../api';

type Device={id:string;name:string;owner:boolean;expires:string;accountId?:string|null};
type PendingDevice={id:string;name:string;confirmed:boolean;expires:string};
type Devices={devices:Device[];pending:PendingDevice[]};
type AccessState={phoneOrigin:string|null};

export function MarketingAccessSettings({online}:{online:boolean}){
  const [address,setAddress]=useState<string|null>(null);
  const [devices,setDevices]=useState<Devices>({devices:[],pending:[]});
  const [code,setCode]=useState(''),[expires,setExpires]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  const refresh=useCallback(async()=>setDevices(await api<Devices>('/devices')),[]);

  useEffect(()=>{
    let active=true;
    Promise.all([api<AccessState>('/state'),api<Devices>('/devices')])
      .then(([state,result])=>{if(active){setAddress(state.phoneOrigin);setDevices(result);}})
      .catch(cause=>{if(active)setError((cause as Error).message);});
    const timer=setInterval(()=>{
      if(document.visibilityState==='visible')void api<Devices>('/devices')
        .then(result=>{if(active)setDevices(result);}).catch(()=>{});
    },5000);
    return()=>{active=false;clearInterval(timer);};
  },[]);

  async function act(action:()=>Promise<unknown>,success:string){
    setBusy(true);setError('');setNotice('');
    try{await action();await refresh();setNotice(success);}
    catch(cause){setError((cause as Error).message);}
    finally{setBusy(false);}
  }

  const collaborators=devices.devices.filter(device=>!device.owner);
  return <section className="business-access-page" aria-label="Settings and team access">
    <header><p className="eyebrow">WORKSPACE SETTINGS</p><h1>Team access</h1><p>Pair a teammate’s browser, then grant access to an exact campaign in Work. Your owner key stays on this computer.</p></header>
    <div className="business-access-grid">
      <section className="business-access-card" aria-label="Private workspace address">
        <div className="business-access-card-heading"><ShieldCheck size={20}/><h2>Private address</h2></div>
        {address?<><p>Teammates on your private network open this address to request access.</p><div className="business-access-address"><a href={address} target="_blank" rel="noreferrer">{address}</a><button type="button" aria-label="Copy private address" title="Copy address" onClick={()=>void navigator.clipboard.writeText(address).then(()=>setNotice('Private address copied.')).catch(()=>setError('Could not copy the address. Select the link instead.'))}><Copy size={16}/></button></div></>:<p className="business-access-hold">A trusted HTTPS address is needed before a teammate can pair.</p>}
        <small>Pairing gives a browser an identity. Campaign access is granted separately.</small>
      </section>
      <section className="business-access-card" aria-label="Pair a browser">
        <div className="business-access-card-heading"><UserPlus size={20}/><h2>Pair a browser</h2></div>
        <ol className="business-access-steps"><li>Create a one-time code here.</li><li>On the other device, open the private address and choose <strong>Join with a pairing code</strong>.</li><li>Confirm its request below; the teammate selects <strong>Finish pairing</strong>.</li></ol>
        <button type="button" className="primary" disabled={!online||!address||busy} onClick={()=>void act(async()=>{const pair=await api<{code:string;expires:string}>('/pair/start',{});setCode(pair.code);setExpires(pair.expires);},'One-time code ready. It expires in five minutes.')}><UserPlus size={16}/>Create one-time pairing code</button>
        {code&&<p className="business-access-code" role="status"><span>One-time code</span><strong>{code}</strong><small>Expires {new Date(expires).toLocaleTimeString()}. Enter it on the other device; it is not the owner key.</small></p>}
      </section>
    </div>
    <section className="business-access-card business-access-devices" aria-label="Device requests and paired browsers">
      <div className="business-access-card-heading"><h2>Devices</h2><button type="button" aria-label="Refresh device requests" title="Refresh" disabled={busy||!online} onClick={()=>void act(refresh,'Device list refreshed.')}><RefreshCw size={16}/></button></div>
      {devices.pending.length>0&&<div className="business-access-device-group"><h3>Awaiting your confirmation</h3>{devices.pending.map(device=><div className="business-access-device" key={device.id}><span><strong>{device.name}</strong><small>Requested from a browser · expires {new Date(device.expires).toLocaleTimeString()}</small></span><button type="button" disabled={busy||!online||device.confirmed} onClick={()=>void act(()=>api('/pair/'+encodeURIComponent(device.id)+'/confirm',{}),'Device confirmed. Select Finish pairing on the other device.')}>{device.confirmed?<><CheckCircle2 size={15}/>Confirmed</>:'Confirm this device'}</button></div>)}</div>}
      <div className="business-access-device-group"><h3>Browser sessions</h3>{collaborators.length?collaborators.map(device=><div className="business-access-device" key={device.id}><span><strong>{device.name} · {(device.accountId||device.id).slice(0,8)}</strong><small>{device.accountId?'Signed-in account · this browser session':'Paired browser'} · expires {new Date(device.expires).toLocaleDateString()}</small></span><button type="button" disabled={busy||!online} onClick={()=>void act(()=>api('/devices/'+encodeURIComponent(device.id)+'/revoke',{}),'This browser session was revoked. Campaign membership is managed separately.')}>Revoke</button></div>):<p>No teammate browsers are paired yet.</p>}</div>
      <p className="business-access-next">After pairing, open <strong>Work → Campaigns → What changed → Campaign access</strong> to grant this browser one campaign.</p>
    </section>
    {notice&&<p className="business-access-notice" role="status">{notice}</p>}
    {error&&<p className="company-error" role="alert">{error}</p>}
  </section>;
}
