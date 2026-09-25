import {Raven} from './Raven';

export type CustomerLoginView={enabled:boolean;origin:string|null;providers:string[]};

export function CustomerSignIn({login,onRecovery}:{login:CustomerLoginView;onRecovery:()=>void}){
  const failed=new URLSearchParams(location.hash.slice(1)).has('sign-in-error');
  return <main className="unlock fe-auth customer-sign-in"><div className="fe-auth-card">
    <div className="fe-auth-brand"><span className="fe-brand-mark" aria-hidden="true">1</span>First Employee</div>
    <Raven state="listening"/>
    <h1>Welcome back</h1>
    <p>Use the same account on each device. Your workspace owner controls which campaigns you can access.</p>
    <div className="customer-sign-in-options">
      {login.providers.map(provider=><a className="primary" key={provider}
        href={`${login.origin}/api/auth/customer/login?provider=${encodeURIComponent(provider)}`}>
        Continue with {provider==='google'?'Google':'Microsoft'}
      </a>)}
    </div>
    {failed&&<p role="alert">Sign-in wasn’t completed. Try again or contact your workspace owner.</p>}
    <small>Sign-in only uses your identity. It doesn’t connect your email, files or calendar.</small>
    <button className="text-button" onClick={onRecovery}>Other workspace access</button>
  </div></main>;
}
