// Production Task JSON modal and workflow in Edge. All native requests are mocked; no live Task writes.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
const output=path.join(root,'TestResults/TaskJsonEditor');fs.mkdirSync(output,{recursive:true});
const executable=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium'].find(file=>file&&fs.existsSync(file));
const renderIcons=require('./render-razor-icons.cjs');
const modal=renderIcons(read('Views/Shared/Editors/_TaskJsonEditorDialog.cshtml').replace(/@\*[\s\S]*?\*@/g,'').replace(/^@inject[^\n]+/m,'')
    .replace(/@Url.Action\("(Shell|Validate|Save)", "TaskJsonEditor"\)/g,(_,action)=>'/maui/TaskJsonEditor/'+action)
    .replace(/data-checkout-enabled="[^\n]+">/,'data-checkout-enabled="false">'),read('Views/Shared/_Icon.cshtml'));
assert.ok(!modal.includes('@'),'Render every Razor expression');
const inline=file=>'<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const html=String.raw`<!doctype html><html data-theme="light" data-moveit-admin="true" data-user-grid-page-size="50"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}
${read('wwwroot/css/script-editor.css')}
${read('wwwroot/css/editors/task-json-editor.css')}</style></head><body class="grid-fixed-page"><section hidden data-grid="tasks" data-page-sizes="[10,50,100]" data-default-page-size="10"></section>${modal}<script>
window.errors=[];window.logged=[];window.requests=[];window.fault='';window.held=[];window.changed=0;window.attack=0;window.duration=false;window.leaseListeners=0;window.confirms=[];window.confirmProceed=true;window.heldSaves=[];
window.addEventListener('error',event=>errors.push(event.message));window.addEventListener('unhandledrejection',event=>errors.push(String(event.reason)));
const originalAdd=window.addEventListener.bind(window),originalRemove=window.removeEventListener.bind(window);
window.addEventListener=(name,callback,...args)=>{if(name==='pagehide')leaseListeners++;return originalAdd(name,callback,...args);};
window.removeEventListener=(name,callback,...args)=>{if(name==='pagehide')leaseListeners--;return originalRemove(name,callback,...args);};
window.mauiLogNotification=(...args)=>logged.push(args);window.mauiAppUrl=url=>'/maui'+url;
window.mauiConfirm=async options=>{confirms.push(options);return options.confirmText==='Proceed'?confirmProceed:true;};
window.seed=()=>{window.docs={};for(const [id,type]of [['900',''],['901','COS'],['902','Sy']])docs[id]={version:'v1',definition:{ID:id,Name:id==='901'?'<img src=x onerror="attack++">':'Task '+id,TT:type,Active:0,CacheNames:'random',Info:{Description:'Before'},Schedules:{Schedule:[{OnlyUntilFirstSuccess:0,Days:{DayOfWeek:['Monday']}}]},steps:[{Source:{ID:'11',FileMask:'*.*',ClientSecret:null}}]}};};seed();
const reply=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'content-type':'application/json'}});
window.fetch=async(url,options={})=>{
 const target=new URL(url,location.href),action=target.pathname.split('/').at(-1),body=options.body?JSON.parse(options.body):null,id=target.searchParams.get('id')||body?.id;
 requests.push({url:target.pathname,action,id,options,body});
 if(target.pathname.includes('/ObjectEditLease/')){
  if(action==='Open')return reply({required:duration,leaseId:duration?'L'+id:''});
  if(action==='Override')return reply({success:true,leaseId:'L'+id,editVersion:docs[id]?.version||'v1'});
  return reply({success:true});
 }
 if(fault==='hold'&&action==='Shell')return new Promise((resolve,reject)=>{held.push(()=>resolve(reply(shell(id))));options.signal?.addEventListener('abort',()=>reject(new DOMException('Aborted','AbortError')),{once:true});});
 if(fault==='load403'&&action==='Shell')return reply({message:'Your MOVEit account cannot edit this Task.',moveItSaved:false},403);
 if(fault==='validation'&&action==='Validate'&&id==='901')return reply({message:'This element must be 0 or 1.',moveItSaved:false},400);
 if(fault==='partial'&&action==='Save'&&id==='901')return reply({message:'The save result could not be confirmed.',moveItSaved:true,requiresReload:true},502);
 if(fault==='checkout'&&action==='Save')return reply({message:'This object has been checked out to another-user since 2026-10-29 14:00:00',checkoutConflict:true,moveItSaved:false},409);
 if(fault==='conflict'&&action==='Save')return reply({message:'This Task changed in MOVEit.',editConflict:true,requiresReload:true,moveItSaved:false},409);
 if(action==='Shell')return reply(shell(id));
 if(action==='Validate')return reply({success:true});
 if(fault==='holdSave'&&action==='Save'&&id==='901')await new Promise(resolve=>heldSaves.push(resolve));
 if(action==='Save'){for(const change of body.changes)MauiTaskJsonModel.set(docs[id].definition,change.path,change.value);if(fault==='normalize'){if(docs[id].definition.Active)docs[id].definition.Active=true;else delete docs[id].definition.Active;}docs[id].version+='s';return reply({success:true,taskId:id,editVersion:docs[id].version,document:shell(id)});}
 return reply({message:'Unexpected route'},404);
};
window.shell=id=>({id,name:docs[id].definition.Name,kind:docs[id].definition.TT==='COS'?'Advanced':docs[id].definition.TT==='Sy'?'Synchronization':'Traditional',editVersion:docs[id].version,checkoutObjectsForDurationOfEdit:duration,definition:structuredClone(docs[id].definition),readOnlyPaths:[['ID'],['TT'],['steps','0','Source','ID'],['steps','0','Source','ClientSecret']]});
window.fetchJson=async(url,options={})=>{const headers=new Headers(options.headers||{});if(options.body)headers.set('RequestVerificationToken','token');const response=await fetch(url,{...options,headers});const payload=await response.json();if(!response.ok){const error=new Error(payload.message);error.status=response.status;error.payload=payload;throw error;}return payload;};
</script>${inline('wwwroot/js/modal-ui.js')}${inline('wwwroot/js/editors/object-edit-lease.js')}${inline('wwwroot/js/editors/task-json-model.js')}${inline('wwwroot/js/editors/task-json-editor.js')}<script>
window.editor=MauiTaskJsonEditor.initialize({fetchJson,antiForgeryToken:'token',changed:()=>changed++});window.ready=true;
</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
if(process.argv.includes('--syntax-only')){console.log('PASS Task JSON production modal, icon, CSS, request and client fixture syntax.');process.exit(0);}
assert.ok(executable,'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-task-json-'));
let browser, socket, server;
let requestId = 0;
const pending = new Map();
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function command(method, params = {}) {
    const id = ++requestId;
    const result = new Promise((resolve, reject) => {
        const timer = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 15000);
        pending.set(id, { resolve, reject, timer });
    });
    socket.send(JSON.stringify({ id, method, params }));
    return await result;
}
async function evaluate(expression) {
    // CDP retains global lexical declarations; each command needs its own block scope.
    const result = await command('Runtime.evaluate', { expression: expression.includes('await ') ? '(async()=>{' + expression + '})()' : '{' + expression + '}', awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
async function screenshot(name) {
    const image = await command('Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(path.join(output, name + '.png'), Buffer.from(image.data, 'base64'));
}
(async () => {
    server = http.createServer((_request, response) => { response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end(html); });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    browser = spawn(executable, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
        '--no-sandbox', '--remote-debugging-port=0', '--remote-allow-origins=*', '--user-data-dir=' + profile, 'about:blank'], { stdio: 'ignore' });
    let launchError;
    browser.on('error', error => { launchError = error; });
    const portFile = path.join(profile, 'DevToolsActivePort');
    let port = '';
    for (let attempt = 0; attempt < 200 && !port; attempt++) {
        if (launchError) throw launchError;
        if (browser.exitCode !== null) throw new Error('Browser exited before CDP became available');
        try {
            const candidate = fs.readFileSync(portFile, 'utf8').split(/\r?\n/)[0];
            if (/^\d+$/.test(candidate) && Number(candidate) > 0 && Number(candidate) <= 65535) port = candidate;
        } catch (error) {
            if (!['ENOENT','EBUSY','EACCES'].includes(error.code)) throw error;
        }
        if (!port) await pause(100);
    }
    assert.ok(port, 'Browser CDP startup timed out');
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    socket = new WebSocket(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    socket.addEventListener('message', event => {
        const message = JSON.parse(event.data), task = pending.get(message.id);
        if (!task) return;
        pending.delete(message.id); clearTimeout(task.timer);
        if (message.error) task.reject(new Error(JSON.stringify(message.error))); else task.resolve(message.result);
    });
    await command('Page.enable');
    await command('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/` });



    for(let i=0;i<100&&!await evaluate('Boolean(window.ready)');i++)await pause(30);
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:950,deviceScaleFactor:1,mobile:false});
    let checks=0;
    const check=async(expression,expected,label)=>{assert.deepEqual(await evaluate(expression),expected,label);checks++;};
    const waitFor=async expression=>{for(let i=0;i<200;i++){if(await evaluate(expression))return;await pause(20);}throw new Error('Timed out: '+expression);};
    const click=selector=>evaluate('document.querySelector('+JSON.stringify(selector)+').click()');
    const open=async ids=>{await evaluate('await editor.open('+JSON.stringify(ids)+');');await check('document.querySelector("[data-task-json-find]").value','','Reopening resets Find');await check('document.querySelector("[data-task-json-replacement]").value','','Reopening resets Replace with');};
    const preview=async()=>{await click('[data-task-json-tab="replace"]');await evaluate(`const field=document.querySelector('[data-task-json-find]');field.value='OnlyUntilFirstSuccess';field.dispatchEvent(new Event('input'));const replacement=document.querySelector('[data-task-json-replacement]');replacement.value='1';replacement.dispatchEvent(new Event('input'));`);await click('[data-task-json-find-button]');await waitFor(`document.querySelector('[data-task-json-match-count]').textContent.includes('selected changes')`);};
    const review=async()=>{await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').textContent==='Save'&&!document.querySelector('[data-task-json-submit]').disabled`);};
    const stage=async()=>{await preview();await click('[data-task-json-replace]');await review();};
    await check('document.querySelector("[data-task-json-find]").value','','Find starts blank');await check('document.querySelector("[data-task-json-replacement]").value','','Replace with starts blank');await check('document.querySelector("[data-task-json-save-progress]").hidden',true);await check('requests.length',0,'No definitions loaded with the grid');await open(['900','901','902']);
    await check('requests.filter(r=>r.action==="Shell").length',3);await check('requests.some(r=>r.action==="Open")',false,'Default-off checkout duration remains off');
    await check('document.querySelectorAll("[data-task-json-tree] > details").length',3);await check('document.querySelectorAll("[data-task-json-tree] > details[open]").length',0,'Task roots start collapsed');
    await check('document.querySelector("[data-task-json-submit]").textContent','Review Changes');await check('document.querySelector("[data-task-json-submit]").disabled',true);
    await check('document.querySelector("[data-task-json-match-count]").hidden',true);
    await check('document.querySelector("[data-task-json-pager=match]").hidden',true,'Empty results have no duplicate paging summary');
    await evaluate(`const search=document.querySelector('[data-task-json-tree-search]');search.value='Task.Active';search.dispatchEvent(new Event('input'));`);await click('[data-task-json-tree] .task-json-leaf');
    await check(`(()=>{const field=document.querySelector('[data-task-json-current]'),style=getComputedStyle(field);return [field.getBoundingClientRect().height,style.paddingTop,style.paddingBottom,style.fontFamily===getComputedStyle(document.querySelector('[data-task-json-name]')).fontFamily];})()`,[42,'10px','10px',true],'Scalar fields use centered MAUI field geometry and font');
    await check(`(()=>{const b=document.querySelector('[data-task-json-expand]'),r=[...document.querySelectorAll('[data-task-json-tree] button')].find(b=>b.textContent==='Reload Task');return [getComputedStyle(b).borderRadius,b.getBoundingClientRect().height,r.classList.contains('secondary-button'),document.querySelectorAll('[data-task-json-dialog] .link-button').length];})()`,['9px',38,true,0],'Tree commands reuse actual MAUI buttons');
    for(const theme of ['light','dark']){await evaluate(`document.documentElement.dataset.theme='${theme}'`);await screenshot('edit-'+theme);}
    await preview();await check('document.querySelectorAll("[data-task-json-match]").length',3);await check('document.querySelectorAll("[data-task-json-matches] img").length',0);await check('attack',0);
    await check('document.querySelector("[data-task-json-match-page]").textContent','Showing 1–3 of 3');
    await check('document.querySelector("[data-task-json-match-indicator]").textContent','1 / 1');
    await check(`[...document.querySelector('[data-task-json-page-size=match]').options].map(o=>o.value)`,['10','50','100'],'Rows choices come from the Task grid');
    for(const theme of ['light','dark'])for(const width of [1400,1024,800,600]){
        await command('Emulation.setDeviceMetricsOverride',{width,height:950,deviceScaleFactor:1,mobile:false});await evaluate(`document.documentElement.dataset.theme='${theme}'`);await pause(30);
        await check(`(()=>{const d=document.querySelector('[data-task-json-dialog]'),f=d.querySelector('footer').getBoundingClientRect(),p=d.querySelector('[data-task-json-pager=match]'),n=d.querySelector('[data-task-json-match-next]');return d.scrollWidth<=d.clientWidth+1&&f.bottom<=innerHeight&&p.clientWidth>=p.scrollWidth&&getComputedStyle(n,'::after').transform!=='none';})()`,true,'Shared Find pager fits '+theme+'/'+width);
        if(width===1400)await screenshot('replace-'+theme);
    }
    await check('requests.some(r=>r.action==="Save")',false,'Find never saves');
    await click('[data-task-json-replace]');
    await check('document.querySelector("[data-task-json-panel=replace]").hidden',false,'Apply stays on Find & Replace');
    await check('requests.some(r=>r.action==="Validate"||r.action==="Save")',false,'Apply only stages local changes');
    await check('document.querySelector("[data-task-json-replace]").textContent','Apply Changes');
    await check('document.querySelector("[data-task-json-submit]").textContent','Review Changes');
    await check('document.querySelector("[data-task-json-submit]").disabled',false);
    await check('document.querySelector("[data-task-json-match-count]").textContent','3 applied');
    await review();
    await check('requests.some(r=>r.action==="Save")',false,'Review never saves');await check('document.querySelectorAll("[data-task-json-review] tr").length',3);
    for(const theme of ['light','dark'])for(const width of [1400,1024,800,600]){
        await command('Emulation.setDeviceMetricsOverride',{width,height:950,deviceScaleFactor:1,mobile:false});await evaluate(`document.documentElement.dataset.theme='${theme}'`);await pause(30);
        await check(`(()=>{const d=document.querySelector('[data-task-json-dialog]'),f=d.querySelector('footer').getBoundingClientRect();return d.getBoundingClientRect().width<=innerWidth&&f.top>0&&f.bottom<=innerHeight&&d.scrollWidth<=d.clientWidth+1;})()`,true,'Fixed modal and footer fit '+theme+'/'+width);
        if(width===1400)await screenshot('review-'+theme);
    }
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:950,deviceScaleFactor:1,mobile:false});
    await check('confirms.some(c=>c.confirmText==="Proceed")',false,'Review does not prompt to save');
    await evaluate('confirmProceed=false;');await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-dialog]').getAttribute('aria-busy')==='false'`);
    await check('requests.some(r=>r.action==="Save")',false,'Cancel never writes');
    await check('requests.filter(r=>r.action==="Validate").length',3,'Cancel does not start another validation/save sequence');
    await check('document.querySelector("[data-task-json-submit]").disabled',false,'Cancel retains the staged draft');
    await check('confirms.at(-1)',{title:'Save Task JSON?',message:'WARNING: Improperly editing task JSON data can corrupt your configuration, trigger application-wide failures, or completely prevent MOVEit from starting. Proceed with extreme caution.',cancelText:'Cancel',confirmText:'Proceed',danger:true},'Use the exact warning and shared destructive Proceed button');
    await evaluate('confirmProceed=true;fault="holdSave";');await click('[data-task-json-submit]');await waitFor('heldSaves.length===1');
    await check('document.querySelector("[data-task-json-save-progress]").hidden',false,'Progress is visible during the save');
    await check('document.querySelector("[data-task-json-progress]").value',1,'Progress advances after a Task completes');
    await check('document.querySelector("[data-task-json-progress]").max',3);
    await check('document.querySelector("[data-task-json-status]").textContent','Saving Task 2 of 3...');
    await check('document.querySelector("[data-task-json-submit]").disabled',true);
    await evaluate('fault="";heldSaves.shift()();');await waitFor(`document.querySelector('[data-task-json-submit]').textContent==='Save'&&document.querySelector('[data-task-json-submit]').disabled`);
    await check('document.querySelector("[data-task-json-save-progress]").hidden',true,'Finished saves return to the normal footer');
    await check('requests.filter(r=>r.action==="Save").length',3);await check('changed',1);
    await check('requests.slice(0,requests.findIndex(r=>r.action==="Save")).filter(r=>r.action==="Validate").length',6,'Validate every dirty Task before the first PUT');
    await check('requests.filter(r=>r.action==="Save").every(r=>r.body.changes.length===1&&!r.body.definition)',true,'Only leaf changes are submitted');
    await check('requests.filter(r=>r.action==="Save").every(r=>r.options.headers.get("RequestVerificationToken")==="token")',true);
    await check('document.querySelector("[data-task-json-status]").textContent','3 Tasks saved.');
    await click('[data-task-json-close]');await pause(40);await check('leaseListeners',0,'Closing disposes each lease listener');
    await evaluate('seed();fault="validation";');await open(['900','901']);await stage();
    await check('requests.filter(r=>r.action==="Save").length',3,'Validation failure performs no writes');await check('document.querySelectorAll(".task-json-row-error").length',1);
    await check(`(()=>{const d=document.querySelector('[data-task-json-dialog]');return [...d.querySelectorAll('footer [data-maui-modal-feedback]')].some(n=>!n.hidden&&n.dataset.feedbackKind==='error');})()`,true,'Errors use the modal footer');
    await check('logged.some(row=>row[1]==="error")',true);await click('[data-task-json-close]');await pause(20);
    await evaluate('seed();fault="partial";duration=true;document.querySelector("[data-task-json-dialog]").dataset.checkoutEnabled="true";');await open(['900','901']);await stage();await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').textContent==='Save'&&document.querySelector('[data-task-json-submit]').disabled`);
    await check('requests.filter(r=>r.action==="Save").at(-1).options.headers.get("X-MAUI-Edit-Lease")','L901');
    await check(`[...document.querySelectorAll('[data-task-json-dialog] footer [data-maui-modal-feedback]')].filter(n=>!n.hidden).map(n=>n.textContent).join('')`,'1 saved; 1 Task still has unsaved changes.');
    await check('document.querySelectorAll(".task-json-row-error").length',1,'The affected Task is highlighted');
    await click('[data-task-json-tab="edit"]');await evaluate(`for(const d of document.querySelectorAll('[data-task-json-tree] > details'))d.open=true;`);await pause(50);
    await check('document.querySelector("[data-task-json-tree]").textContent.includes("Override Checkout")',true);
    await evaluate('fault="";');await evaluate(`const b=[...document.querySelectorAll('[data-task-json-tree] button')].find(b=>b.textContent==='Override Checkout');b.click();`);
    await waitFor(`!document.querySelector('[data-task-json-submit]').disabled`);await check('confirms.some(c=>c.confirmText==="Override Checkout"&&c.danger)',true);
    await check('document.querySelector("[data-task-json-submit]").textContent','Review Changes');await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').textContent==='Save'&&!document.querySelector('[data-task-json-submit]').disabled`);
    await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').disabled`);await click('[data-task-json-close]');await pause(40);await check('leaseListeners',0);
    await evaluate('seed();fault="checkout";');await open(['900']);await stage();await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').textContent==='Save'&&document.querySelector('[data-task-json-submit]').disabled`);
    await check(`[...document.querySelectorAll('[data-task-json-dialog] footer [data-maui-modal-feedback]')].some(n=>!n.hidden&&n.textContent.includes('another-user since 2026-10-29'))`,true,'Native checkout owner and date are visible');
    await click('[data-task-json-tab="edit"]');await evaluate(`document.querySelector('[data-task-json-tree] > details').open=true;`);await pause(30);
    await check('document.querySelector("[data-task-json-tree]").textContent.includes("Override Checkout")',true);await click('[data-task-json-close]');await pause(20);
    await evaluate('fault="load403";');await open(['900']);await check('document.querySelector("[data-task-json-submit]").disabled',true);await check(`[...document.querySelectorAll('[data-task-json-dialog] footer [data-maui-modal-feedback]')].some(n=>!n.hidden&&n.textContent.includes('could not be loaded'))`,true);await click('[data-task-json-close]');await pause(30);
    await evaluate('fault="hold";void editor.open(["900"]);void 0;');await waitFor('held.length>0');await click('[data-task-json-close]');await pause(20);
    await evaluate('fault="";');await open(['902']);await evaluate('held.splice(0).forEach(fn=>fn());');await pause(30);
    await check('document.querySelector("[data-task-json-tree]").textContent.includes("Task 902")',true);await check('document.querySelector("[data-task-json-tree]").textContent.includes("Task 900")',false,'Late reads cannot populate a new invocation');
    await evaluate(`const search=document.querySelector('[data-task-json-tree-search]');search.value='FileMask';search.dispatchEvent(new Event('input'));`);
    await waitFor(`Boolean(document.querySelector('[data-task-json-tree] .task-json-leaf'))`);await click('[data-task-json-tree] .task-json-leaf');await waitFor(`document.querySelector('[data-task-json-current]').value==='*.*'`);
    await evaluate(`const input=document.querySelector('[data-task-json-new]');input.value='*.csv';input.dispatchEvent(new Event('input'));`);await click('[data-task-json-apply]');
    await check('document.querySelector("[data-task-json-current]").value','*.csv','Individual scalar editing is staged locally');
    await check('docs["902"].definition.steps[0].Source.FileMask','*.*','Apply Value never writes the server');
    await check('document.querySelector("[data-task-json-submit]").textContent','Review Changes');
    await evaluate(`const search=document.querySelector('[data-task-json-tree-search]');search.value='Task.Name';search.dispatchEvent(new Event('input'));`);await click('[data-task-json-tree] .task-json-leaf');await waitFor(`document.querySelector('[data-task-json-current]').value==='Task 902'`);
    await evaluate(`const input=document.querySelector('[data-task-json-new]');input.value='Renamed Task 902';input.dispatchEvent(new Event('input'));`);await click('[data-task-json-apply]');
    await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').textContent==='Save'&&!document.querySelector('[data-task-json-submit]').disabled`);await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').disabled`);
    await check('document.querySelector("[data-task-json-tree]").textContent.includes("Renamed Task 902")',true,'Saved names refresh in the tree');
    await click('[data-task-json-tab="edit"]');
    await evaluate(`fault='normalize';const search=document.querySelector('[data-task-json-tree-search]');search.value='Task.Active';search.dispatchEvent(new Event('input'));`);await click('[data-task-json-tree] .task-json-leaf');
    await evaluate(`const input=document.querySelector('[data-task-json-new]');input.value='1';input.dispatchEvent(new Event('input'));`);await click('[data-task-json-apply]');
    await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').textContent==='Save'&&!document.querySelector('[data-task-json-submit]').disabled`);await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').disabled`);
    await check('document.querySelector("[data-task-json-type]").textContent','Boolean','Post-save type badges use the normalized native value');
    await check('document.querySelector("[data-task-json-current]").value','true');
    await click('[data-task-json-tab="edit"]');
    await evaluate(`const input=document.querySelector('[data-task-json-new]');input.value='false';input.dispatchEvent(new Event('input'));`);await click('[data-task-json-apply]');
    await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').textContent==='Save'&&!document.querySelector('[data-task-json-submit]').disabled`);await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').disabled`);
    await check('document.querySelector("[data-task-json-fields]").hidden',true,'Omitted native defaults clear a selected leaf safely');
    await check('Object.hasOwn(docs["902"].definition,"Active")',false);
    await evaluate(`document.querySelector('[data-task-json-dialog]').close()`);await pause(30);await check('leaseListeners',0,'Programmatic closes clean up too');await check('document.querySelectorAll("[data-task-json-review] tr").length',0,'Close clears stale values');await check('errors',[]);
    await evaluate(`seed();fault='';duration=false;document.querySelector('[data-task-json-dialog]').dataset.checkoutEnabled='false';docs['900'].definition.Parameters={Parameter:Array.from({length:65},(_,i)=>({Name:'P'+i,value:'before',Secure:0}))};`);await open(['900']);
    await click('[data-task-json-tab="replace"]');
    await evaluate(`for(const [key,value]of [['find','value'],['replacement','after']]){const input=document.querySelector('[data-task-json-'+key+']');input.value=value;input.dispatchEvent(new Event('input'));}`);
    await click('[data-task-json-find-button]');await waitFor(`document.querySelectorAll('[data-task-json-match]').length===50`);
    await check('document.querySelector("[data-task-json-match-page]").textContent','Showing 1–50 of 65');
    await click('[data-task-json-match]');await click('[data-task-json-match-next]');
    await check('document.querySelectorAll("[data-task-json-match]").length',15);await check('document.querySelector("[data-task-json-match-page]").textContent','Showing 51–65 of 65');
    await check('document.querySelector("[data-task-json-match-next]").disabled',true);
    await evaluate(`const select=document.querySelector('[data-task-json-page-size=match]');select.value='10';select.dispatchEvent(new Event('change'));`);
    await check('document.querySelectorAll("[data-task-json-match]").length',10);await check('document.querySelector("[data-task-json-match]").checked',false,'Selection survives page navigation and page-size changes');
    await check('document.querySelector("[data-task-json-page-size=review]").value','10');
    await click('[data-task-json-replace]');await check('document.querySelector("[data-task-json-panel=replace]").hidden',false);await check('document.querySelector("[data-task-json-match-count]").textContent','64 applied');
    await review();await check('document.querySelectorAll("[data-task-json-review] tr").length',10);await check('document.querySelector("[data-task-json-review-indicator]").textContent','1 / 7');
    await click('[data-task-json-review-next]');await check('document.querySelector("[data-task-json-review-page]").textContent','Showing 11–20 of 64');
    await evaluate(`const select=document.querySelector('[data-task-json-page-size=review]');select.value='50';select.dispatchEvent(new Event('change'));`);
    await check('document.querySelector("[data-task-json-page-size=match]").value','50');await check('document.querySelectorAll("[data-task-json-review] tr").length',50);
    await check(`['replace','review'].map(name=>[...document.querySelectorAll('[data-task-json-panel='+name+'] .data-grid-table th')].map(th=>th.textContent.trim()).filter(Boolean)).every(labels=>JSON.stringify(labels)===JSON.stringify(['Task / Element','Before','After','Status']))`,true,'Both tables use the same column labels and grid branding');
    await click('[data-task-json-submit]');await waitFor(`document.querySelector('[data-task-json-submit]').textContent==='Save'&&document.querySelector('[data-task-json-submit]').disabled`);
    await check('docs["900"].definition.Parameters.Parameter.filter(p=>p.value==="after").length',64);await check('docs["900"].definition.Parameters.Parameter[0].value','before');
    await click('[data-task-json-tab="replace"]');await evaluate(`const input=document.querySelector('[data-task-json-find]');input.value='noSuchElement';input.dispatchEvent(new Event('input'));`);await click('[data-task-json-find-button]');await waitFor(`document.querySelector('[data-task-json-matches]').textContent==='No matching values.'`);
    await check('document.querySelector("[data-task-json-match-count]").hidden',true);await check('document.querySelector("[data-task-json-pager=match]").hidden',true);
    await check('document.querySelector("[data-task-json-submit]").disabled',true);await check('document.querySelector("[data-task-json-dialog]").textContent.includes("0 results")',false);
    await click('[data-task-json-tab="edit"]');await evaluate(`const search=document.querySelector('[data-task-json-tree-search]');search.value='Task.Info.Description';search.dispatchEvent(new Event('input'));`);await click('[data-task-json-tree] .task-json-leaf');
    await evaluate(`const input=document.querySelector('[data-task-json-new]');input.value='Line one\\nLine two';input.dispatchEvent(new Event('input'));`);await check('document.querySelector("[data-task-json-new]").classList.contains("is-multiline")',true);await click('[data-task-json-apply]');
    await check('document.querySelector("[data-task-json-current]").value','Line one\nLine two','Multiline strings retain their exact text');
    await check('document.querySelector("[data-task-json-current]").getBoundingClientRect().height',100);
    await click('[data-task-json-close]');await pause(30);await check('errors',[]);await check('leaseListeners',0);
    const result={result:'PASS',checks};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Task JSON editor rendered workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({errors,requests:requests.map(r=>({url:r.url,id:r.id})),body:document.body.innerText})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});
