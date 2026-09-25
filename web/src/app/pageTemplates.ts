import type {AppDefinition,AppField} from '../types';

// Pages run in a sandbox: no network, no external images (data: URIs only). Keep templates self-contained.
export type PageTemplate={id:string;label:string;summary:string;group:'Campaign pages'|'Working tools';definition:(title:string)=>AppDefinition};

const noteField:AppField[]=[{key:'note',label:'Note',kind:'text'}];

const landingCss=`:root{--ink:#17151f;--brand:#5b4cdb;--brand-2:#c05bd6;--paper:#ffffff}
body{padding:0;background:var(--paper);color:var(--ink);font-family:"Segoe UI Variable Display","Segoe UI",-apple-system,system-ui,sans-serif}
.hero{min-height:78vh;display:grid;place-items:center;text-align:center;padding:72px 24px;background:radial-gradient(900px 500px at 50% -10%,#ece9ff,transparent 70%)}
.hero-inner{max-width:760px}.eyebrow{display:inline-block;padding:6px 14px;border-radius:999px;background:#efedfd;color:var(--brand);font-weight:600;font-size:13px}
h1{font-size:clamp(36px,6vw,64px);line-height:1.05;letter-spacing:-.03em;margin:18px 0 16px}
h1 span{background:linear-gradient(90deg,var(--brand),var(--brand-2));-webkit-background-clip:text;background-clip:text;color:transparent}
.lead{font-size:20px;line-height:1.5;color:#5a5666;margin:0 auto 28px;max-width:560px}
.cta{display:inline-block;padding:14px 26px;border-radius:999px;background:var(--brand);color:#fff;font-weight:650;text-decoration:none;border:0;font-size:16px}
.proof{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:16px;max-width:960px;margin:0 auto;padding:56px 24px}
.proof div{padding:22px;border-radius:18px;background:#f6f5f2}.proof strong{display:block;font-size:17px;margin-bottom:6px}.proof p{margin:0;color:#5a5666;line-height:1.5}
footer{text-align:center;padding:40px;color:#8a8696;font-size:13px}`;

const emailCss=`body{background:#f4f3f0;padding:32px 12px;color:#1c1b1f;font-family:-apple-system,"Segoe UI",system-ui,sans-serif}
.mail{max-width:600px;margin:0 auto}.pre{font-size:12px;color:#8a8690;margin:0 0 10px}.card{background:#fff;border-radius:16px;padding:36px 40px}
.brand{font-weight:700;color:#5b4cdb;margin:0 0 20px}h1{font-size:28px;line-height:1.2;margin:0 0 18px}p{font-size:16px;line-height:1.65}
.button{display:inline-block;background:#5b4cdb;color:#fff;text-decoration:none;padding:12px 22px;border-radius:999px;font-weight:600}
.sign{margin-top:26px;color:#55525c}.foot{text-align:center;font-size:12px;color:#8a8690;margin-top:18px}`;

const socialCss=`body{background:#f0f2f5;padding:28px 12px;font-family:-apple-system,"Segoe UI",system-ui,sans-serif;color:#1c1e21}
h1{max-width:560px;margin:0 auto 18px;font-size:20px}.post{max-width:560px;margin:0 auto 16px;background:#fff;border-radius:12px;padding:16px 18px;box-shadow:0 1px 2px rgba(0,0,0,.08)}
.head{display:flex;gap:10px;align-items:center;margin-bottom:10px}.avatar{width:40px;height:40px;border-radius:50%;background:linear-gradient(135deg,#5b4cdb,#c05bd6)}
.name{font-weight:650;font-size:14px}.meta{font-size:12px;color:#65676b}.hook{font-size:16px;line-height:1.5;margin:0 0 10px;white-space:pre-wrap}
.why{font-size:12px;color:#65676b;border-top:1px solid #e4e6eb;padding-top:10px;margin:0}.label{display:inline-block;font-size:11px;font-weight:700;color:#5b4cdb;margin-bottom:6px}`;

const escapeHtml=(value:string)=>value.replace(/[&<>"]/g,char=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[char]!));
export function socialHtml(posts:{title:string;hook:string;why:string}[],heading='Post mockups'){
  return `<h1>${escapeHtml(heading)}</h1>\n`+posts.map((post,index)=>`<article class="post"><span class="label">${index+1}. ${escapeHtml(post.title)}</span>
<div class="head"><div class="avatar"></div><div><div class="name">Your brand</div><div class="meta">Draft · not posted</div></div></div>
<p class="hook">${escapeHtml(post.hook)}</p><p class="why">${escapeHtml(post.why)}</p></article>`).join('\n');
}
/** A mockup page built from Marketing's saved post angles. */
export function socialMockupDefinition(title:string,angles:{title:string;hook:string;why:string;claimLimit?:string}[]):AppDefinition{
  return {title,description:'Social post mockups from Marketing’s draft angles',fields:noteField,summaries:[],
    page:{css:socialCss,javaScript:'',html:socialHtml(angles.map(angle=>({title:angle.title,hook:angle.hook,why:angle.claimLimit?`${angle.why} · Claim limit: ${angle.claimLimit}`:angle.why})),title)}};
}

export const pageTemplates:PageTemplate[]=[
  {id:'landing',label:'Landing page',summary:'Headline, promise, three proof points and a call to action',group:'Campaign pages',definition:title=>({title,description:'Campaign landing page',fields:noteField,summaries:[],page:{css:landingCss,javaScript:'',html:`<section class="hero"><div class="hero-inner"><span class="eyebrow">New</span>
<h1>The headline that says <span>what changes</span> for your customer</h1>
<p class="lead">One or two sentences about the problem you solve and who it's for. Keep it concrete.</p>
<button class="cta">Get started</button></div></section>
<section class="proof"><div><strong>Proof point one</strong><p>A specific, true benefit.</p></div><div><strong>Proof point two</strong><p>What makes you different.</p></div><div><strong>Proof point three</strong><p>Evidence someone can check.</p></div></section>
<footer>© Your company</footer>`}})},
  {id:'announcement',label:'Launch announcement',summary:'A short story page for a launch or news moment',group:'Campaign pages',definition:title=>({title,description:'Launch announcement',fields:noteField,summaries:[],page:{css:landingCss+`
article{max-width:680px;margin:0 auto;padding:72px 24px}article h1{font-size:clamp(32px,5vw,48px)}article p{font-size:18px;line-height:1.7;color:#3a3744}
blockquote{margin:28px 0;padding:6px 20px;border-left:4px solid var(--brand);font-size:20px;color:var(--ink)}`,javaScript:'',html:`<article><span class="eyebrow">Announcement</span>
<h1>We're launching something new</h1>
<p>Open with the moment: what's happening and why it matters today.</p>
<blockquote>"A quote from a customer or founder that makes it human."</blockquote>
<p>What people can do next, and where to learn more.</p>
<p><button class="cta">Try it now</button></p></article>`}})},
  {id:'link-bio',label:'Link in bio',summary:'A simple page of links for social profiles',group:'Campaign pages',definition:title=>({title,description:'Link in bio page',fields:noteField,summaries:[],page:{css:`body{min-height:100vh;display:grid;place-items:center;background:linear-gradient(160deg,#efedfd,#fbeefd);color:#17151f;font-family:"Segoe UI",-apple-system,system-ui,sans-serif}
main{width:min(420px,100%);text-align:center}.avatar{width:88px;height:88px;border-radius:50%;margin:0 auto 14px;background:linear-gradient(135deg,#5b4cdb,#c05bd6)}
h1{font-size:24px;margin:0 0 6px}p{color:#5a5666;margin:0 0 24px}a{display:block;margin:10px 0;padding:15px;border-radius:14px;background:#fff;color:#17151f;text-decoration:none;font-weight:600;box-shadow:0 2px 10px rgba(40,30,80,.08)}`,javaScript:'',html:`<main><div class="avatar"></div><h1>Your name</h1><p>One line about what you do.</p>
<a href="#">Latest launch</a><a href="#">Newsletter</a><a href="#">Book a call</a></main>`}})},
  {id:'email',label:'Email announcement',summary:'A clean, single-column email for a launch or update',group:'Campaign pages',definition:title=>({title,description:'Email announcement',fields:noteField,summaries:[],page:{css:emailCss,javaScript:'',html:`<div class="mail"><p class="pre">Preview text: one line that makes people open it.</p>
<div class="card"><p class="brand">Your company</p><h1>A subject-worthy headline</h1>
<p>Hi there,</p><p>Open with why this matters to the reader right now, in one or two sentences.</p>
<p>Then the one thing you want them to do.</p><p><a class="button" href="#">Take the next step</a></p>
<p class="sign">— Your name</p></div><p class="foot">You're receiving this because you signed up. Unsubscribe</p></div>`}})},
  {id:'social',label:'Social post mockups',summary:'Preview posts as they will look in a feed',group:'Campaign pages',definition:title=>({title,description:'Social post mockups',fields:noteField,summaries:[],page:{css:socialCss,javaScript:'',html:socialHtml([
    {title:'Post one',hook:'The first line has to earn the scroll-stop.',why:'Why this angle works for the audience.'},
    {title:'Post two',hook:'A second angle, with a different hook.',why:'What makes it distinct.'},
    {title:'Post three',hook:'A third option to compare side by side.',why:'The claim it avoids overstating.'}])}})},
  {id:'blank',label:'Blank page',summary:'Start from an empty canvas',group:'Campaign pages',definition:title=>({title,description:'Page',fields:noteField,summaries:[],page:{html:'<main>\n  <h1>'+title.replace(/[<>&]/g,'')+'</h1>\n  <p>Start writing.</p>\n</main>',css:'main{max-width:720px;margin:48px auto}',javaScript:''}})},
  {id:'decision-log',label:'Decision log',summary:'What was decided, by whom, on what evidence',group:'Working tools',definition:title=>records(title,'Decisions and why they were made',[
    {key:'date',label:'Date',kind:'date'},{key:'decision',label:'Decision',kind:'text'},{key:'decided_by',label:'Decided by',kind:'text'},
    {key:'evidence',label:'Evidence',kind:'text'},{key:'revisit',label:'Revisit if',kind:'text'}],'date')},
  {id:'experiments',label:'Experiment tracker',summary:'Hypothesis, metric and decision rule for every test',group:'Working tools',definition:title=>records(title,'Experiments with predetermined decision rules',[
    {key:'hypothesis',label:'Hypothesis',kind:'text'},{key:'metric',label:'Primary metric',kind:'text'},{key:'rule',label:'Decision rule',kind:'text'},
    {key:'status',label:'Status',kind:'select',options:['Planned','Running','Shipped','Stopped']},{key:'ends',label:'Ends',kind:'date'}],'ends')},
  {id:'calendar',label:'Content calendar',summary:'What goes out, where, and when',group:'Working tools',definition:title=>records(title,'Planned content by channel and date',[
    {key:'date',label:'Date',kind:'date'},{key:'channel',label:'Channel',kind:'select',options:['LinkedIn','X','Instagram','TikTok','YouTube','Blog','Email','Community']},
    {key:'idea',label:'Idea',kind:'text'},{key:'status',label:'Status',kind:'select',options:['Idea','Drafting','Ready for review','Approved','Published']}],'date')}
];

// A small generic records page: a form for the app's fields and a table of entries, saved through the host bridge.
const recordsJs=`const root=document.getElementById('app');let current=null;
function el(tag,props={},...kids){const node=document.createElement(tag);Object.assign(node,props);for(const kid of kids)node.append(kid);return node;}
function input(field){if(field.kind==='select'){const s=el('select',{name:field.key});s.append(el('option',{value:'',textContent:'—'}));for(const o of field.options||[])s.append(el('option',{value:o,textContent:o}));return s;}
 return el('input',{name:field.key,type:field.kind==='number'?'number':field.kind==='date'?'date':field.kind==='checkbox'?'checkbox':'text'});}
function render(state){current=state;root.replaceChildren();
 const form=el('form',{className:'add'});for(const f of state.fields)form.append(el('label',{},f.label,input(f)));
 const status=el('p',{className:'status'});form.append(el('button',{type:'submit',textContent:'Add',disabled:state.readOnly}),status);
 form.onsubmit=async e=>{e.preventDefault();const values={};for(const f of state.fields){const x=form.elements[f.key];const v=f.kind==='checkbox'?x.checked:x.value.trim();if(v===''||v===false&&f.kind!=='checkbox')continue;values[f.key]=f.kind==='number'?Number(v):v;}
  if(!Object.keys(values).length){status.textContent='Fill in at least one field.';return;}
  status.textContent='Saving…';try{await thaddeus.save({upserts:[{id:'',values}],deleteIds:[]});}catch(err){status.textContent=err.message;}};
 const table=el('table');const head=el('tr');for(const f of state.fields)head.append(el('th',{textContent:f.label}));head.append(el('th'));table.append(el('thead',{},head));
 const body=el('tbody');const sortKey=(state.fields.find(f=>f.kind==='date')||{}).key;
 const rows=[...state.entries].sort((a,b)=>sortKey?String(b.values[sortKey]||'').localeCompare(String(a.values[sortKey]||'')):0);
 for(const entry of rows){const tr=el('tr');for(const f of state.fields){const v=entry.values[f.key];tr.append(el('td',{textContent:v===true?'✓':v===false||v==null?'':String(v)}));}
  const del=el('button',{className:'remove',textContent:'Remove',disabled:state.readOnly});del.onclick=()=>thaddeus.save({upserts:[],deleteIds:[entry.id]}).catch(err=>status.textContent=err.message);tr.append(el('td',{},del));body.append(tr);}
 if(!rows.length){const tr=el('tr');tr.append(el('td',{colSpan:state.fields.length+1,className:'empty',textContent:'Nothing here yet. Add the first one above.'}));body.append(tr);}
 table.append(body);root.append(el('h1',{textContent:state.title}),form,el('div',{className:'scroll'},table));}
thaddeus.onChange(render);`;
const recordsCss=`#app{max-width:1000px;margin:0 auto}h1{font-size:26px;margin:0 0 18px}
.add{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px;align-items:end;padding:18px;border-radius:16px;background:var(--surface);margin-bottom:18px}
label{display:grid;gap:6px;font-size:13px;color:var(--muted)}input,select{padding:9px 11px;border-radius:10px;border:1px solid color-mix(in srgb,var(--muted) 35%,transparent);background:var(--bg);color:var(--text)}
input[type=checkbox]{width:20px;height:20px}button{padding:10px 18px;border-radius:999px;border:0;background:var(--accent);color:var(--on-accent);font-weight:600}
.status{margin:0;font-size:13px;color:var(--muted)}.scroll{overflow-x:auto}table{width:100%;border-collapse:collapse;font-size:14px}
th{text-align:left;color:var(--muted);font-weight:600;padding:10px;border-bottom:1px solid color-mix(in srgb,var(--muted) 30%,transparent)}
td{padding:12px 10px;border-bottom:1px solid color-mix(in srgb,var(--muted) 15%,transparent);vertical-align:top}.empty{color:var(--muted);text-align:center;padding:32px}
.remove{background:transparent;color:var(--muted);padding:4px 8px;font-weight:500}`;

function records(title:string,description:string,fields:AppField[],dateField:string|null):AppDefinition{
  return {title,description,fields,summaries:[],dateField,page:{html:'<div id="app"></div>',css:recordsCss,javaScript:recordsJs}};
}
