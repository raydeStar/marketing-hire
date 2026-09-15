using Thaddeus.Core;

namespace Thaddeus.Host;

public static class ArtifactPageDocument
{
    // A drawing room for generated code; the keys to the estate stay upstairs.
    // Forms need submit events for local JavaScript saves; form-action still blocks navigation/submission.
    public const string Policy = "sandbox allow-scripts allow-forms; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; connect-src 'none'; frame-src 'none'; worker-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'";

    public static string Render(AppPage page) => "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<style>:root{color-scheme:dark;--bg:#141415;--surface:#202121;--text:#e8e6df;--muted:#a7aaa2;--accent:#d8b86b;--on-accent:#24281f}html{scrollbar-width:thin;scrollbar-color:var(--muted) transparent}*{box-sizing:border-box}body{margin:0;padding:24px;background:var(--bg);color:var(--text);font:15px system-ui,sans-serif}button,input,select,textarea{font:inherit}button{cursor:pointer}button:disabled{cursor:default}img,svg,canvas{max-width:100%}</style>" +
        "<script>" + Bridge + "</script><style>" + page.Css.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase) +
        "</style></head><body>" + page.Html + "<script>" + page.JavaScript.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase) + "</script></body></html>";

    private const string Bridge = """
        (()=>{
          let port=null,state=null,pending=null,retry=null,lastError='';
          const subscribers=new Set();
          function report(error){lastError=String(error?.message||error).slice(0,500);port?.postMessage({type:'error',message:lastError});}
          function deliver(next){
            state=next;
            const light=next.theme==='light';
            const colors=light?['#f3f0e8','#fffdf7','#24281f','#666b5f','#80631e']:['#141415','#202121','#e8e6df','#a7aaa2','#d8b86b'];
            ['--bg','--surface','--text','--muted','--accent'].forEach((key,i)=>document.documentElement.style.setProperty(key,colors[i]));
            document.documentElement.style.colorScheme=light?'light':'dark';
            document.documentElement.dataset.theme=light?'light':'dark';
            document.documentElement.style.setProperty('--on-accent',light?'#fffdf7':'#24281f');
            for(const callback of subscribers){try{callback(structuredClone(state));}catch(error){report(error);}}
          }
          window.addEventListener('error',event=>report(event.message));
          window.addEventListener('unhandledrejection',event=>report(event.reason));
          window.addEventListener('message',event=>{
            if(event.source!==parent||event.data?.type!=='thaddeus-connect'||event.ports.length!==1||port)return;
            port=event.ports[0];
            port.onmessage=event=>{
              const message=event.data;
              if(message?.type==='state')deliver(message.state);
              if(message?.type==='result'&&pending?.id===message.id){
                const current=pending;pending=null;clearTimeout(current.timer);
                if(message.error){current.reject(new Error(message.error));}
                else{retry=null;deliver(message.state);current.resolve(structuredClone(state));}
              }
            };
            port.postMessage({type:'ready'});if(lastError)report(lastError);
          });
          Object.defineProperty(window,'thaddeus',{value:Object.freeze({
            onChange(callback){if(typeof callback!=='function')throw new Error('onChange needs a function.');subscribers.add(callback);if(state){try{callback(structuredClone(state));}catch(error){report(error);}}return()=>subscribers.delete(callback);},
            save(change){
              if(!port||!state)return Promise.reject(new Error('The app is still opening.'));
              if(state.readOnly)return Promise.reject(new Error('This app is read-only right now.'));
              if(pending)return Promise.reject(new Error('A save is still in progress.'));
              const digest=JSON.stringify(change);
              if(!retry||retry.digest!==digest)retry={digest,id:crypto.randomUUID().replaceAll('-',''),version:state.version};
              const request={type:'save',id:retry.id,version:retry.version,change:JSON.parse(digest)};
              return new Promise((resolve,reject)=>{
                pending={id:request.id,resolve,reject,timer:setTimeout(()=>{pending=null;reject(new Error('Save confirmation did not arrive. Retry the same change or reopen the app to check its data.'));},30000)};
                port.postMessage(request);
              });
            }
          }),writable:false,configurable:false});
        })();
        """;
}
