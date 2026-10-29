'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
const output=path.join(root,'TestResults/EnvironmentCopy');fs.mkdirSync(output,{recursive:true});
const executable=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium','/usr/bin/chromium-browser'].find(x=>x&&fs.existsSync(x));
let markup=read('Views/Shared/_EnvironmentCopy.cshtml').replace(/@\*[\s\S]*?\*@/g,'').replaceAll('@Url.Action("Catalog", "EnvironmentCopy")','/maui/EnvironmentCopy/Catalog').replaceAll('@User.Identity?.Name','Admin');
const start=markup.indexOf('            @foreach'),end=markup.indexOf('        </div>',start);
assert.ok(start>=0&&end>start);markup=markup.slice(0,start)+['objects','resolve','review','results'].map(stage=>'<button type="button" class="task-details-tab" role="tab" data-copy-tab="'+stage+'">'+stage+'</button>').join('')+markup.slice(end);
assert.ok(!markup.includes('@'),'Copy fixture contains no unresolved Razor');
assert.equal(JSON.parse(read('appsettings.template.json')).EnvironmentCopy.Enabled.Value,false);
assert.match(read('Views/Shared/_DataGrid.cshtml'),/isMoveItAdmin && MauiSecurityService\.IsAdmin/);
assert.match(read('Views/Shared/_DataGrid.cshtml'),/copyAllowed \? "environment-copy-bulk-menu"/);
const rowKinds=[['task','Task','1','Views/Tasks/Index.cshtml'],['host','Host','2','Views/Hosts/Index.cshtml'],
 ['script','Script','3','Views/Resources/Scripts.cshtml'],['date-list','DateList','4','Views/Resources/DateLists.cshtml'],
 ['key-certificate','KeyCertificate','tls|5','Views/Resources/KeysAndCerts.cshtml']];
const rowFixtures=rowKinds.map(([kind,type,id,file])=>{const view=read(file),start=view.indexOf('<div class="task-row-action-menu" data-'+kind+'-row-action-menu');
 assert.ok(start>=0,'Production '+kind+' row menu exists');const end=view.indexOf('\n</div>',start)+7;assert.ok(end>start,'Production '+kind+' row menu closes');
 return '<button id="copy-row-'+kind+'" data-'+kind+'-row-menu-button data-row-id="'+id+'" data-script-built-in="false">…</button>'+view.slice(start,end);}).join('');
assert.ok(!rowFixtures.includes('@'),'Row menu fixture contains no unresolved Razor');
const inline=file=>'<script>'+read(file)+'</script>';
const html=`<!doctype html><html data-theme="light"><head><style>${read('wwwroot/css/site.css')}${read('wwwroot/css/script-editor.css')}${read('wwwroot/css/environment-copy.css')}</style></head><body>
<form class="maui-editor-routing-antiforgery" hidden><input name="__RequestVerificationToken" value="copy-token"></form><button id="launch">Bulk Actions</button><div class="menu-control open"><div class="dropdown-menu" id="normal-menu"><button>Export</button></div><div class="dropdown-menu environment-copy-bulk-menu" id="copy-menu"><button>Copy To</button></div></div><input id="parent-grid-selection" type="checkbox" checked>${rowFixtures}${markup}
<script>
window.errors=[];addEventListener('error',e=>errors.push(e.message));addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
window.fastPolling=false;window.fastStatusTimeout=false;window.failStatus=0;window.forbidStatus=false;window.hangNextStatus=false;window.holdStatus=false;window.stopAsked=false;window.progressMode='preparing';
window.holdDestination=false;window.timeoutDestination=false;window.fastReadTimeout=false;window.rowFallthrough=0;
window.holdPlacement=false;window.holdDifference=false;window.forbidStatusHtml=false;window.resultMode='metadata';
const originalTimeout=window.setTimeout.bind(window);window.setTimeout=(fn,ms,...args)=>originalTimeout(fn,(fastReadTimeout&&ms===30000||fastStatusTimeout&&ms===15000)?25:fastPolling&&[1000,2000,3000,4000,5000].includes(ms)?60:ms,...args);
window.requests=[];window.failPlacement=false;window.failSelection=false;window.failAnalyze=false;window.failDifference=false;window.partial=false;window.slow=false;window.jobDone=true;window.token='review-token';window.operationId='1234567890abcdef1234567890abcdef';
const clone=value=>JSON.parse(JSON.stringify(value));
window.basePlan={planId:operationId,source:'DEV',destination:'PROD',groups:['Operations'],folders:[{id:10,parentId:null,name:'Root Folder'}],unresolved:1,reviewToken:'',items:[
{key:'Task|1',type:'Task',sourceId:'1',sourceName:'Daily Transfer',subtype:'Traditional',selected:true,reason:'Selected',choice:{key:'Task|1',action:'create',name:'Daily Transfer',targetId:''},targets:[],matches:[],issues:[],skipped:[],credentialStatus:'Protected native transfer',canCreate:true,canOverwrite:true,canRename:true},
{key:'Host|2',type:'Host',sourceId:'2',sourceName:'Partner SFTP',subtype:'SSHFTP',selected:false,reason:'Required dependency',choice:{key:'Host|2',action:'',name:'Partner SFTP',targetId:''},targets:[{type:'Host',id:'20',name:'Partner SFTP',subtype:'SSHFTP'}],matches:[{type:'Host',id:'20',name:'Partner SFTP',subtype:'SSHFTP'}],issues:['Action required: choose Create New, Reuse Existing or Overwrite.'],skipped:[],credentialStatus:'Protected native transfer',canCreate:true,canOverwrite:true,canRename:true}]};
basePlan.items.push(
{key:'Script|3',type:'Script',sourceId:'3',sourceName:'Archive',subtype:'PS1',selected:false,reason:'Required dependency',choice:{key:'Script|3',action:'create',name:'Archive',targetId:''},targets:[],matches:[],issues:[],skipped:[],credentialStatus:'Not applicable',canCreate:true,canOverwrite:true,canRename:true},
{key:'Script|100001',type:'Script',sourceId:'100001',sourceName:'Built-in',subtype:'VBScript',selected:false,reason:'Required dependency',choice:{key:'Script|100001',action:'reuse',name:'Built-in',targetId:'100001'},targets:[{type:'Script',id:'100001',name:'Built-in',builtIn:true}],matches:[],issues:[],skipped:[],credentialStatus:'Not applicable',canCreate:false,canOverwrite:false,canRename:false},
{key:'Host|0',type:'Host',sourceId:'0',sourceName:'Local File System',subtype:'FileSystem',selected:false,reason:'Required dependency',choice:{key:'Host|0',action:'reuse',name:'Local File System',targetId:'0'},targets:[{type:'Host',id:'0',name:'Local File System'}],matches:[],issues:[],skipped:[],credentialStatus:'Not applicable',canCreate:false,canOverwrite:false,canRename:false});
window.sourceObjects=[{type:'Task',id:'1',key:'Task|1',name:'Daily Transfer',subtype:'Traditional'},
{type:'Host',id:'2',key:'Host|2',name:'Partner SFTP',subtype:'SSHFTP'},
{type:'Script',id:'3',key:'Script|3',name:'Archive',subtype:'PS1'},
{type:'Script',id:'100001',key:'Script|100001',name:'Built-in',subtype:'VBScript',builtIn:true,reuseOnly:true},
{type:'Host',id:'0',key:'Host|0',name:'Local File System',subtype:'FileSystem',reuseOnly:true},
{type:'DateList',id:'4',key:'DateList|4',name:'Dates',subtype:'Date List'},
{type:'KeyCertificate',id:'tls|5',key:'KeyCertificate|tls|5',name:'Certificate',subtype:'Tls'}];
window.current=clone(basePlan);window.reviewedImpact=false;
window.fetch=async(input,options={})=>{const target=new URL(String(input),location.href),action=target.pathname.split('/').pop();const body=options.body?JSON.parse(options.body):null;requests.push({action,body,path:target.pathname,environment:target.searchParams.get('environment'),token:options.headers?.RequestVerificationToken});
 const reply=(value,status=200)=>Promise.resolve(new Response(JSON.stringify(value),{status,headers:{'content-type':'application/json'}}));
 if(slow&&action==='Analyze')await new Promise(resolve=>setTimeout(resolve,100));
 if(action==='Catalog')return reply({currentEnvironment:'DEV',environments:[{key:'DEV',name:'DEV',credentialsConfigured:true,account:'svc-dev'},{key:'PROD',name:'PROD',credentialsConfigured:true,account:'svc-prod'},{key:'QA',name:'QA',credentialsConfigured:true,account:'svc-qa'}]});
 if(action==='Connection'){
 const env=target.searchParams.get('environment');
 if(env==='QA'&&timeoutDestination)return new Promise((resolve,reject)=>options.signal.addEventListener('abort',()=>reject(new DOMException('Read cancelled','AbortError')),{once:true}));
 if(env==='QA'&&holdDestination)return new Promise(resolve=>{window.releaseDestination=()=>resolve(new Response(JSON.stringify({account:'svc-qa',environment:'QA'}),{status:200}));});
 return reply({account:'svc-'+env.toLowerCase(),environment:env});}
 if(action==='Objects')return reply({account:target.searchParams.get('environment')==='DEV'?'svc-dev':'svc-prod',objects:clone(sourceObjects)});
 if(action==='Selection'){if(failSelection)return reply({message:'Dependency read failed inside the modal'},400);
 const roots=new Set(body.objects.map(item=>item.type+'|'+item.id)),required=roots.has('Task|1')?['Host|2','Script|3','Script|100001','Host|0']:[];
 return reply({dependencies:required.filter(key=>!roots.has(key))});}
 if(action==='Analyze'){if(failAnalyze)return reply({message:'Source read failed inside the modal'},400);current=clone(basePlan);reviewedImpact=false;return reply(current);}
 if(action==='Resolve'){current.items.forEach(item=>{const choice=body.choices.find(x=>x.key===item.key);item.choice=clone(choice);if(choice.action==='create'){item.choice.targetId='';item.choice.name=(item.type==='Task'||body.nameScope==='all'||body.nameScope==='tasks-hosts'&&item.type==='Host'?body.prefix:'')+choice.name+(item.type==='Task'||body.nameScope==='all'||body.nameScope==='tasks-hosts'&&item.type==='Host'?body.suffix:'');}
 item.issues=choice.action===''?['Choose an action.']:choice.action==='overwrite'&&!reviewedImpact?['Review differences and overwrite impact.']:[];});current.unresolved=current.items.filter(x=>x.issues.length).length;current.reviewToken='';return reply(current);}
 if(action==='Differences'){if(holdDifference)return new Promise(resolve=>{window.releaseDifference=()=>resolve(new Response(JSON.stringify({message:'Old comparison error'}),{status:400}));});
 if(failDifference)return reply({message:'Comparison is unavailable'},400);reviewedImpact=true;current.items[1].issues=[];current.unresolved=0;return reply({source:'DEV',destination:'PROD',sourceId:'2',destinationId:'20',name:'Partner SFTP',type:'Host',impactMessage:'Used by 3 destination Tasks.',impact:['One','Two','Three'],values:[{property:'Host',source:'source.example',destination:'destination.example',status:'Changed'},{property:'DefPassword',source:'Not compared (protected value)',destination:'Not compared (protected value)',status:'Not compared'}],plan:clone(current)});}
 if(action==='Review'){current.groupsToCreate=body.groupMode==='create-source'?['Finance']:[];current.placement=[{name:current.items[0].choice.name,type:'Task',action:'create',beforeGroups:[],sourceGroups:['Finance'],afterGroups:body.groupMode==='create-source'?['Finance']:body.groupMode==='one'?[body.group]:[],folder:'Root',accessChanged:false}];current.reviewToken=token;current.items[0].metadataNotes=body.copyMetadata?['Owner field is incompatible.']:[];return reply(current);}
 if(action==='Placement'){
 if(holdPlacement)return new Promise(resolve=>{window.releasePlacement=()=>resolve(new Response(JSON.stringify({message:'Old placement error'}),{status:400}));});
 if(failPlacement)return reply({message:'Placement preview is unavailable'},400);
 current.reviewToken='';return reply({groupsToCreate:body.groupMode==='create-source'?['Finance']:[],
 placement:[{name:current.items[0].choice.name,type:'Task',action:'create',beforeGroups:[],sourceGroups:['Finance'],afterGroups:body.groupMode==='create-source'?['Finance']:body.groupMode==='one'?[body.group]:[],folder:body.folderMode==='new'?body.folderPath:'Root',accessChanged:false}]});}
 if(action==='Start')return reply({operationId});
 if(action==='Cancel'){stopAsked=true;return reply({message:'Stop requested'});}
 if(action==='Discard')return reply({discarded:true});
 if(action==='Status'){
 if(forbidStatusHtml)return new Response('<html>Access denied</html>',{status:403,headers:{'content-type':'text/html'}});
 if(forbidStatus)return reply({message:'Access denied'},403);
 if(failStatus>0){failStatus--;return reply({message:'Temporary status failure'},503);}
 if(hangNextStatus){hangNextStatus=false;return new Promise((resolve,reject)=>options.signal.addEventListener('abort',()=>reject(new DOMException('Read cancelled','AbortError')),{once:true}));}
 const stable=[{key:'Script|3',type:'Script',sourceId:'3',sourceName:'Archive',action:'create',destinationName:'Archive',destinationId:'30',status:'Created',message:'Verified',skipped:[]},
 {key:'Script|100001',type:'Script',sourceId:'100001',sourceName:'Built-in',action:'reuse',destinationName:'Built-in',destinationId:'100001',status:'Reused',message:'Retained',skipped:[]},
 {key:'Host|0',type:'Host',sourceId:'0',sourceName:'Local File System',action:'reuse',destinationName:'Local File System',destinationId:'0',status:'Reused',message:'Retained',skipped:[]}];
 const stopped=partial||resultMode==='error';
 const host={key:'Host|2',type:'Host',sourceId:'2',sourceName:'Partner SFTP',action:'overwrite',destinationName:'Partner SFTP',destinationId:'20',status:resultMode==='error'?'Failed':!jobDone||partial?'Not verified':'Overwritten',message:'Inspect before retrying.',skipped:resultMode==='warning'?['Resource Group assignment was omitted.']:[],severity:!jobDone||stopped?'error':resultMode==='warning'?'warning':'none'};
 const task={key:'Task|1',type:'Task',sourceId:'1',sourceName:'Daily Transfer',action:'create',destinationName:'Daily Transfer',destinationId:stopped?'':'100',status:stopped?'Skipped':'Created',message:stopped?'Prerequisite failed.':'Saved disabled.',skipped:[],metadataNotes:["Metadata field 'Owner' is not configured for Task."],severity:stopped?'warning':'metadata'};
 const items=jobDone?[...stable,host,task]:progressMode==='copying'?[...stable,host]:[];
 const completedStatus=stopped?'Stopped':resultMode==='warning'?'Completed with warnings':'Completed';
 const payload={groupsCreated:jobDone?['Finance']:[],groupCreationStatus:'Confirmed',operationId,totalObjects:5,completedObjects:items.filter(x=>['Created','Overwritten','Reused'].includes(x.status)).length,
 phase:jobDone?completedStatus:progressMode==='copying'?'Copying and verifying object':'Connecting to source environment',
 phaseElapsedSeconds:8,currentObjectKey:!jobDone&&progressMode==='copying'?'Host|2':'',currentObjectType:'Host',currentObjectName:!jobDone&&progressMode==='copying'?'Partner SFTP':'',cancelRequested:stopAsked,
 status:jobDone?completedStatus:'Running',completedUtc:jobDone?'2026-10-20T16:00:00Z':null,message:partial&&jobDone?'Stopped after verification failure.':'',items};
 if(holdStatus)return new Promise(resolve=>{window.releaseStatus=()=>resolve(new Response(JSON.stringify(payload),{status:200,headers:{'content-type':'application/json'}}));});
 return reply(payload);
 }
 throw Error('Unexpected copy endpoint '+action);
};
</script>${inline('wwwroot/js/modal-ui.js')}${inline('wwwroot/js/environment-copy.js')}<script>
window.pick=name=>document.querySelector('[data-copy-'+name+']');window.object=key=>[...pick('objects').children].find(row=>row.dataset.copyObject===key);window.objectCheck=key=>object(key).querySelector('input');window.launch=()=>document.dispatchEvent(new CustomEvent('maui:grid-action',{cancelable:true,detail:{gridId:'tasks',action:'Copy To',selectedIds:['1']}}));
// Minimal page-owned opener/closer contract around the actual five production row menus.
document.addEventListener('click',event=>{for(const kind of ['task','host','script','date-list','key-certificate']){
 const menu=document.querySelector('[data-'+kind+'-row-action-menu]'),button=event.target.closest('[data-'+kind+'-row-menu-button]');
 if(button){const wasOpen=!menu.hidden;document.querySelectorAll('[data-task-row-action-menu],[data-host-row-action-menu],[data-script-row-action-menu],[data-date-list-row-action-menu],[data-key-certificate-row-action-menu]').forEach(menu=>menu.hidden=true);menu.hidden=wasOpen;button.setAttribute('aria-expanded',String(!wasOpen));return;}
 if(event.target.closest('[data-'+kind+'-row-command]'))rowFallthrough++;
}});
window.set=(name,value)=>{pick(name).value=value;pick(name).dispatchEvent(new Event('change',{bubbles:true}));};window.row=key=>[...pick('plan').children].find(x=>x.dataset.copyRow===key);window.choose=(action)=>{const select=row('Host|2').querySelector('[data-row-action]');select.value=action;select.dispatchEvent(new Event('change',{bubbles:true}));};
</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
if(process.env.MAUI_VALIDATE_BROWSER_FIXTURE_ONLY==='1'){console.log('PASS environment-copy fixture scripts compile');process.exit(0);}
assert.ok(executable,'Edge/Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-environment-copy-'));
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
    await command('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/maui/Tasks` });
    for(let i=0;i<100&&!await evaluate('Boolean(window.launch)');i++)await pause(50);
    let checks=0;
    const check=async(expression,expected,label)=>{assert.deepEqual(await evaluate(expression),expected,label);checks++;};
    const wait=async()=>{for(let i=0;i<100&&await evaluate(`document.querySelector('[data-environment-copy]').getAttribute('aria-busy')==='true'`);i++)await pause(20);};
    const until=async expression=>{for(let i=0;i<150;i++){if(await evaluate(expression))return;await pause(20);}throw new Error('Copy condition timed out: '+expression);};
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate('launch()');await wait();
    await check(`pick('source-status').textContent.includes('svc-dev')&&pick('destination-status').textContent.includes('svc-prod')`,true,'Both configured Data Access accounts are visible');
    await check(`pick('next').disabled`,false,'Grid selection seeds source objects');
    await check(`pick('filter').value`,'Task','Object Type has the caller type');
    await evaluate(`pick('filter').value='';pick('filter').dispatchEvent(new Event('input',{bubbles:true}));`);
    await check(`objectCheck('Host|2').checked && objectCheck('Host|2').disabled`,true,'Automatic Host dependency is checked and locked');
    await check(`objectCheck('Script|3').checked && objectCheck('Script|3').disabled`,true,'Automatic Script dependency is checked and locked');
    await check(`objectCheck('Script|100001').checked && objectCheck('Script|100001').disabled && objectCheck('Host|0').checked && objectCheck('Host|0').disabled`,true,'Protected dependencies are displayed as checked reuse-only resources');
    await check(`object('Host|2').textContent.includes('Required dependency') && Number(getComputedStyle(objectCheck('Host|2')).opacity)<1`,true,'Dependencies have a dimmed checkbox and explicit label');
    await evaluate(`pick('dependencies').checked=false;pick('dependencies').dispatchEvent(new Event('change',{bubbles:true}));`);await wait();
    await check(`!objectCheck('Host|2').checked && !objectCheck('Host|2').disabled`,true,'Turning dependency inclusion off restores ordinary object selection');
    await check(`!objectCheck('Script|100001').checked && objectCheck('Script|100001').disabled && !objectCheck('Host|0').checked && objectCheck('Host|0').disabled`,true,'Protected resources cannot be explicitly selected');
    await evaluate(`objectCheck('Host|2').click()`);await wait();
    await evaluate(`pick('dependencies').checked=true;pick('dependencies').dispatchEvent(new Event('change',{bubbles:true}));`);await wait();
    await evaluate(`objectCheck('Task|1').click()`);await wait();
    await check(`objectCheck('Host|2').checked && !objectCheck('Host|2').disabled && !objectCheck('Script|3').checked`,true,'Explicit Host selection survives removal of a Task that depended on it');
    await evaluate(`objectCheck('Host|2').click();`);await wait();
    await evaluate(`pick('select-all').click()`);await wait();
    await check(`requests.filter(x=>x.action==='Selection').at(-1).body.objects.every(item=>item.id!=='100001' && !(item.type==='Host' && item.id==='0'))`,true,'Select All never submits protected resources as copy roots');
    await evaluate(`pick('select-all').click()`);await wait();
    await check(`!objectCheck('Host|2').checked && !objectCheck('Script|3').checked`,true,'Removing the last selected roots clears dependency checkmarks');
    await evaluate(`objectCheck('Task|1').click()`);await wait();
    await evaluate(`pick('dependencies').checked=false;pick('dependencies').dispatchEvent(new Event('change',{bubbles:true}));`);await wait();
    await evaluate(`failSelection=true;pick('dependencies').checked=true;pick('dependencies').dispatchEvent(new Event('change',{bubbles:true}));`);await wait();
    await check(`pick('feedback').textContent`,'Dependency read failed inside the modal','Dependency errors stay in the modal');
    await check(`pick('next').disabled`,true,'Failed dependency preview cannot advance with misleading selections');
    await evaluate(`failSelection=false;pick('refresh').click()`);await wait();
    await check(`!pick('next').disabled && objectCheck('Host|2').checked`,true,'Refresh retries dependency preview without losing explicit selections');
    await evaluate(`set('destination','DEV')`);await wait();
    await check(`document.querySelector('[data-environment-copy]').getAttribute('aria-busy')`,'false','Changing environments restores the controls');
    await evaluate(`set('destination','PROD')`);await wait();
    await check(`objectCheck('Task|1').checked`,true,'Changing destination preserves explicit source selection');
    await check(`pick('source').disabled && pick('source').value==='DEV'`,true,'Copy From is fixed to the current connection');
    await evaluate(`holdDestination=true;set('destination','QA')`);await pause(30);
    await check(`!pick('destination').disabled && document.querySelector('[data-environment-copy]').getAttribute('aria-busy')==='true'`,true,'Copy To remains available during a pending connection read');
    await evaluate(`set('destination','PROD')`);await wait();
    await check(`pick('destination-status').textContent.includes('svc-prod')`,true,'Another destination can replace a stalled request');
    await evaluate(`releaseDestination();holdDestination=false;`);await pause(50);
    await check(`pick('destination').value==='PROD' && pick('destination-status').textContent.includes('svc-prod') && !pick('destination').disabled`,true,'A late cancelled response cannot reset the destination or lock its control');
    await evaluate(`timeoutDestination=true;fastReadTimeout=true;set('destination','QA')`);await wait();
    await check(`pick('feedback').textContent.includes('timed out') && !pick('destination').disabled`,true,'Read timeout restores an actionable destination selector');
    await evaluate(`timeoutDestination=false;fastReadTimeout=false;set('destination','PROD')`);await wait();
    await check(`requests.filter(item=>item.action==='Objects').every(item=>item.environment==='DEV')`,true,'Destination authentication does not pull a full remote inventory');
    await evaluate(`pick('close').click()`);
    for(const [kind,type,id] of rowKinds){
        await evaluate(`document.querySelector('#copy-row-`+kind+`').click()`);
        await check(`document.querySelector('[data-environment-copy-row="`+kind+`"]').textContent`,'Copy To',kind+' row menu exposes Copy To');
        await check(`document.querySelector('[data-`+kind+`-row-action-menu]').getClientRects().length>0`,true,kind+' production row menu is visibly open');
        await evaluate(`document.querySelector('[data-environment-copy-row="`+kind+`"]').click()`);await wait();
        await check(`pick('filter').value`,type,kind+' row opens the matching object type');
        await check(`objectCheck(`+JSON.stringify(type+'|'+id)+`).checked`,true,kind+' row selects the current object');
        await check(`document.querySelector('[data-`+kind+`-row-action-menu]').hidden`,true,kind+' copy closes the existing row menu');
        await evaluate(`pick('close').click()`);
    }
    await check(`document.querySelector('#parent-grid-selection').checked && rowFallthrough===0`,true,'Row copying preserves grid selection without falling through to another command');
    await evaluate(`document.querySelector('#copy-row-host').dataset.rowId='0';document.querySelector('#copy-row-host').click()`);
    await check(`document.querySelector('[data-environment-copy-row="host"]').hidden`,true,'Local File System has no row Copy To action');
    await evaluate(`document.querySelector('#copy-row-host').click();document.querySelector('#copy-row-host').dataset.rowId='2';document.querySelector('#copy-row-script').dataset.scriptBuiltIn='true';document.querySelector('#copy-row-script').click()`);
    await check(`document.querySelector('[data-environment-copy-row="script"]').hidden`,true,'Built-in Scripts have no row Copy To action');
    await evaluate(`document.querySelector('#copy-row-script').click();document.querySelector('#copy-row-script').dataset.scriptBuiltIn='false';launch()`);await wait();
    for(const theme of ['light','dark']){
        await evaluate(`document.documentElement.dataset.theme=`+JSON.stringify(theme));
        await check(`object('Task|1').cells[0].getBoundingClientRect().width<=34`,true,theme+' checkbox column is compact');
        await check(`object('Task|1').cells[1].getBoundingClientRect().width>pick('objects').parentElement.getBoundingClientRect().width*.35`,true,theme+' Name receives the recovered table width');
        await check(`getComputedStyle(document.querySelector('#copy-menu button')).whiteSpace==='nowrap' && document.querySelector('#copy-menu button').scrollWidth<=document.querySelector('#copy-menu button').clientWidth`,true,theme+' Bulk Action label fits without wrapping');
        await check(`document.querySelector('#copy-menu').getBoundingClientRect().width===document.querySelector('#normal-menu').getBoundingClientRect().width && document.querySelector('#copy-menu').getBoundingClientRect().width<220`,true,theme+' Copy To uses the original shared menu width');
        await screenshot('Objects-'+theme+'-1920');
    }
    await command('Emulation.setDeviceMetricsOverride',{width:1366,height:450,deviceScaleFactor:1,mobile:false});
    await evaluate(`document.querySelector('.environment-copy-body').scrollTop=999`);
    await check(`document.querySelector('.environment-copy-body').scrollTop>0`,true,'Small-window fixture starts scrolled');
    await evaluate(`pick('refresh').click()`);await wait();
    await check(`document.querySelector('.environment-copy-body').scrollTop`,0,'Action completion scrolls to the form top');
    await check(`document.activeElement===document.querySelector('[data-copy-panel="objects"]')`,true,'Action completion resets focus to the form top');
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});

    await evaluate(`failAnalyze=true;pick('next').click()`);await wait();
    await check(`pick('feedback').textContent`,'Source read failed inside the modal','Errors are inside the modal');
    await evaluate(`failAnalyze=false;pick('next').click()`);await wait();
    await check(`pick('panel-resolve')?.hidden??document.querySelector('[data-copy-panel="resolve"]').hidden`,false,'Resolve is open');
    await check(`requests.filter(x=>x.action==='Analyze').at(-1).body.objects`,[{type:'Task',id:'1'}],'Only explicit roots are submitted to Analyze');
    await check(`row('Script|100001').querySelector('[data-row-name]').readOnly && [...row('Script|100001').querySelector('[data-row-action]').options].filter(x=>x.value==='create'||x.value==='overwrite').every(x=>x.disabled)`,true,'Built-in Script cannot be renamed, duplicated or overwritten');
    await check(`document.querySelector('[data-copy-tab="review"]').disabled`,true,'Unresolved rows block Review tab');
    await check(`pick('next').disabled`,true,'Unresolved rows block Review action');
    await check(`row('Host|2').classList.contains('needs-attention')`,true,'Required decision is clearly highlighted');
    await check(`row('Host|2').querySelector('.environment-copy-plan-status p').textContent.includes('Action required: Action required:')`,false,'Action required prefix is not duplicated');
    await evaluate(`choose('reuse')`);await wait();
    await check(`row('Host|2').classList.contains('needs-attention')`,false,'Selecting a valid reuse immediately clears the attention color');
    await check(`!row('Host|2').querySelector('.environment-copy-plan-status p').textContent.includes('Action required:')`,true,'Resolved row text updates with the validated choice');
    await evaluate(`choose('overwrite')`);await wait();
    await check(`row('Host|2').classList.contains('needs-attention')`,true,'Unreviewed overwrite still requires attention');
    await evaluate(`row('Host|2').querySelector('button').click()`);await wait();
    await check(`document.querySelector('[data-copy-differences]').open`,true,'Differences uses a separate modal');
    await check(`document.querySelector('[data-environment-copy]').open`,true,'Parent stays open beneath comparison');
    await check(`document.querySelector('[data-copy-difference-impact]').textContent.includes('3 destination Tasks')`,true,'Overwrite impact is visible');
    await check(`document.querySelector('[data-copy-comparison]').textContent.includes('Not compared (protected value)')`,true,'Protected properties are not treated as equal');
    await evaluate(`document.querySelector('[data-copy-difference-close]').click()`);
    await check(`document.activeElement===row('Host|2').querySelector('button')`,true,'Focus returns to the same comparison control');
    await check(`row('Host|2').querySelector('[data-row-action]').value`,'overwrite','Parent choice survives comparison');
    await check(`document.querySelector('[data-copy-tab="review"]').disabled`,false,'Completed resolution unlocks Review');
    await evaluate(`document.querySelector('.environment-copy-body').scrollTop=999;pick('prefix').value='PROD_';pick('apply-names').click()`);await wait();
    await check(`document.querySelector('.environment-copy-body').scrollTop`,0,'Naming action returns to the top of Resolve');
    await check(`row('Task|1').querySelector('[data-row-name]').value`,'PROD_Daily Transfer','Prefix applies to new Tasks');
    await check(`row('Host|2').querySelector('[data-row-name]').value`,'Partner SFTP','Overwrite identity is retained');
    await evaluate(`pick('next').click()`);await wait();
    await check(`document.querySelector('[data-copy-panel="review"]').hidden`,false,'Review panel opens');
    await check(`pick('group-mode').value`,'none','Destination groups default to None');
    await check(`pick('folder-mode').value`,'root','New Tasks folder defaults to Root');
    await check(`pick('next').disabled`,true,'Overwrite confirmation is required');
    await evaluate(`set('group-mode','one')`);await wait();
    await check(`pick('group-field').hidden`,false,'Common group selector is conditional');
    await check(`pick('placement').textContent.includes('Select a destination Resource Group')`,true,'Incomplete common-group selection clears the stale placement');
    await evaluate(`set('group','Operations')`);await wait();
    await check(`pick('placement').textContent.includes('None → Operations')`,true,'Common-group placement previews immediately');
    await evaluate(`set('folder-mode','new')`);await wait();
    await evaluate(`pick('folder-path').value='Operations/Daily';pick('folder-path').dispatchEvent(new Event('input',{bubbles:true}));`);
    await check(`pick('folder-new').hidden`,false,'New folder path is conditional');
    await check(`pick('next').textContent`,'Update Review','Changed options invalidate the review');
    await evaluate(`pick('next').click()`);await wait();
    await check(`pick('skips').textContent.includes('Owner field is incompatible')`,true,'Metadata differences are reported without blocking the object copy');
    await evaluate(`failPlacement=true;set('group-mode','none')`);await wait();
    await check(`pick('feedback').textContent`,'Placement preview is unavailable','Placement errors stay inside the modal');
    await check(`!pick('placement').textContent.includes('None → Operations') && pick('placement').textContent.includes('could not be previewed')`,true,'Preview failure cannot leave stale group assignments visible');
    await evaluate(`failPlacement=false;set('group-mode','create-source')`);await wait();
    await check(`pick('placement').textContent.includes('None → Finance') && pick('group-creation').textContent.includes('Finance')`,true,'Create groups as needed detects the source group without pressing Update Review');
    await check(`pick('placement').textContent.includes('Source Resource Groups: Finance')`,true,'Source assignment is identified separately from destination placement');
    await check(`pick('skips').textContent`,'','Placement changes clear warnings belonging to the previous Review');
    await check(`pick('next').textContent`,'Update Review','Placement preview still requires authoritative Review before copying');
    await check(`requests.filter(item=>item.action==='Start').length`,0,'Preview never starts a copy');
    await check(`document.activeElement===pick('group-mode')`,true,'Placement preview retains keyboard focus');
    await evaluate(`pick('next').click()`);await wait();
    await check(`pick('group-creation').hidden`,false,'Create groups as needed exposes its placement summary');
    await check(`pick('group-creation').textContent.includes('Finance') && pick('group-creation').textContent.includes('permissions are not copied')`,true,'Review lists missing groups and the permission boundary');
    await check(`requests.filter(item=>item.action==='Review').at(-1).body.groupMode`,'create-source','Missing-group creation is explicitly reviewed');
    await check(`pick('placement').textContent.includes('None → Finance')`,true,'Reviewed source group assignment is shown instead of None');
    // Requests from a closed wizard must not put their errors into a fresh wizard's panels.
    await evaluate(`document.querySelector('[data-copy-tab="resolve"]').click();holdDifference=true;row('Host|2').querySelector('button').click()`);
    await until(`typeof releaseDifference==='function'`);
    await evaluate(`document.querySelector('[data-copy-difference-close]').click();pick('close').click();holdDifference=false;launch()`);await wait();
    await evaluate(`pick('next').click()`);await wait();await evaluate(`choose('overwrite')`);await wait();
    await evaluate(`row('Host|2').querySelector('button').click()`);await wait();
    await evaluate(`releaseDifference()`);await pause(80);
    await check(`document.querySelector('[data-copy-difference-error]').hidden && document.querySelector('[data-copy-difference-subtitle]').textContent.includes('Partner SFTP')`,true,'A late comparison error cannot replace a reopened comparison');
    await evaluate(`document.querySelector('[data-copy-difference-close]').click();pick('next').click()`);await wait();
    await evaluate(`holdPlacement=true;set('group-mode','create-source')`);await until(`typeof releasePlacement==='function'`);
    await evaluate(`pick('close').click();holdPlacement=false;launch()`);await wait();
    await evaluate(`pick('next').click()`);await wait();await evaluate(`choose('overwrite')`);await wait();
    await evaluate(`row('Host|2').querySelector('button').click()`);await wait();
    await evaluate(`document.querySelector('[data-copy-difference-close]').click();pick('next').click()`);await wait();
    await evaluate(`set('group-mode','create-source')`);await wait();await evaluate(`pick('next').click()`);await wait();
    await evaluate(`window.freshPlacement=pick('placement').textContent;releasePlacement()`);await pause(80);
    await check(`pick('placement').textContent===freshPlacement && pick('placement').textContent.includes('None → Finance') && !pick('feedback').classList.contains('is-error')`,true,'A late placement error cannot replace a reopened Review');
    // Stable frame and visible footer across stages, both themes and smaller windows/zoom-equivalent widths.
    for(const theme of ['light','dark'])for(const [width,height]of [[1920,1080],[1366,768],[800,720],[600,720]]){
        await command('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
        const bounds=await evaluate(`(()=>{const d=document.querySelector('[data-environment-copy]'),f=pick('next').getBoundingClientRect();return {width:d.getBoundingClientRect().width,footer:f.bottom,overflow:d.querySelector('.environment-copy-card').scrollWidth>d.querySelector('.environment-copy-card').clientWidth+1};})()`);
        assert.ok(bounds.width<=width,theme+'/'+width+' modal fits');assert.ok(bounds.footer<=height,theme+'/'+width+' footer is visible');assert.equal(bounds.overflow,false,theme+'/'+width+' no outer horizontal overflow');checks+=3;
        await screenshot('Review-'+theme+'-'+width);
    }
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate(`jobDone=false;fastPolling=true;progressMode='preparing';pick('confirm-overwrite').checked=true;pick('confirm-overwrite').dispatchEvent(new Event('change',{bubbles:true}));pick('next').click()`);await wait();
    await check(`pick('phase').textContent.includes('Connecting to source') && !pick('activity').hidden`,true,'Preparation has a real phase and activity indicator');
    await check(`pick('progress').hasAttribute('value')`,false,'Preparation is indeterminate instead of an apparently stalled zero');
    await check(`pick('counts').textContent.includes('0 of 5')`,true,'Preparation reports the planned object count');
    await check(`pick('phase-age').textContent.includes('8s')`,true,'Phase elapsed time is displayed');
    await check(`pick('feedback').textContent`,'','Routine progress does not repeat in the footer');
    await check(`pick('result-message').hidden`,true,'Routine progress has no duplicate result message');
    await check(`['phase','counts','phase-age'].every(key=>pick(key).parentElement===pick('status'))`,true,'Phase, count and elapsed time share one status line');
    await check(`(()=>{const a=pick('phase').getBoundingClientRect(),b=pick('counts').getBoundingClientRect(),c=pick('phase-age').getBoundingClientRect();return Math.abs(a.top-b.top)<1&&Math.abs(a.top-c.top)<1;})()`,true,'Progress text stays on one row at desktop width');
    await evaluate(`progressMode='copying'`);await until(`pick('phase').textContent.includes('Partner SFTP')`);
    await check(`pick('progress').value===3&&pick('progress').max===5`,true,'Verified objects advance the determinate progress bar');
    await check(`pick('results').textContent.includes('In progress · Partner SFTP') && !pick('results').textContent.includes('Not verified · Partner SFTP')`,true,'The active write is shown as in progress without changing durable uncertainty semantics');
    await evaluate(`window.sameProgressRow=pick('results').firstElementChild`);await pause(160);
    await check(`sameProgressRow===pick('results').firstElementChild`,true,'Phase-only status reads do not rebuild unchanged result rows');
    for(const theme of ['light','dark']){await evaluate(`document.documentElement.dataset.theme='`+theme+`'`);await screenshot('Progress-'+theme);}
    await evaluate(`failStatus=1`);await until(`pick('feedback').textContent.includes('retrying the status read')`);
    await until(`!pick('feedback').classList.contains('is-error')`);
    await check(`pick('phase').textContent.includes('Partner SFTP')`,true,'A transient status read recovers automatically');
    await evaluate(`fastStatusTimeout=true;hangNextStatus=true`);await until(`pick('feedback').textContent.includes('timed out')`);
    await until(`!pick('feedback').classList.contains('is-error')`);
    await check(`requests.filter(x=>x.action==='Start').length`,1,'Read timeout recovery never submits another copy');
    await evaluate(`fastStatusTimeout=false;forbidStatus=true`);await until(`pick('feedback').textContent.includes('restoring access')`);
    const deniedReads=await evaluate(`requests.filter(x=>x.action==='Status').length`);await pause(180);
    await check(`requests.filter(x=>x.action==='Status').length`,deniedReads,'Authorization failures stop status retries');
    await evaluate(`forbidStatus=false;pick('close').click();launch()`);await wait();
    await evaluate(`forbidStatusHtml=true`);
    await until(`pick('feedback').textContent.includes('restoring access')`);
    const deniedHtmlReads=await evaluate(`requests.filter(x=>x.action==='Status').length`);await pause(180);
    await check(`requests.filter(x=>x.action==='Status').length`,deniedHtmlReads,'HTML access-denied responses also stop status retries');
    await check(`pick('feedback').textContent.includes('could not read the server response')`,true,'Unexpected response text remains clear and inside the modal');
    await evaluate(`forbidStatusHtml=false`);
    await evaluate(`forbidStatus=false;pick('close').click();launch()`);await wait();
    await check(`pick('counts').textContent.includes('3 of 5')`,true,'Reopening an active operation retains totals without an in-memory plan');
    await evaluate(`holdStatus=true;progressMode='preparing';window.releaseStatus=null`);await until(`typeof releaseStatus==='function'`);
    await evaluate(`holdStatus=false;progressMode='copying';pick('close').click();launch()`);await wait();
    await evaluate(`window.freshPhase=pick('phase').textContent;releaseStatus()`);await pause(100);
    await check(`pick('phase').textContent===freshPhase`,true,'An aborted status read from a closed modal cannot replace the reopened view');
    await evaluate(`pick('stop').click();pick('stop').click()`);await wait();
    await check(`requests.filter(x=>x.action==='Cancel').length`,1,'Stop is submitted once while its acknowledgement is pending');
    await check(`pick('stop').disabled`,true,'Polling does not re-enable Stop after a successful request');
    await check(`pick('phase').textContent.includes('Stop requested')&&pick('feedback').textContent===''`,true,'Stop acknowledgement uses the same status line');
    await evaluate(`partial=true;jobDone=true`);await until(`pick('result-title').textContent==='Stopped'`);
    await check(`pick('progress').value===3&&pick('progress').max===5`,true,'Stopped copies retain real completion instead of showing a full bar');
    await check(`pick('activity').hidden && pick('back').disabled===false`,true,'Finished polling hides activity and enables New Copy');
    await check(`requests.filter(x=>x.action==='Start').length`,1,'Polling, reopening and stopping never replay a native copy');
    await pause(80);
    await check(`document.querySelector('[data-copy-panel="results"]').hidden`,false,'Results open');
    await check(`pick('results').textContent.includes('Not verified')&&pick('results').textContent.includes('Destination 20')`,true,'Uncertain outcome retains known destination ID');
    await check(`[...pick('results').children].find(row=>row.querySelector('strong').textContent.includes('Partner SFTP')).classList.contains('is-error')`,true,'Unverified final object is highlighted red');
    await check(`pick('report').href.includes('/maui/EnvironmentCopy/Report')`,true,'Report link respects IIS PathBase');
    await check(`pick('result-message').textContent.includes('Resource Groups created: Finance')`,true,'Group creation appears in the retained copy results');
    await check(`!pick('result-message').hidden&&pick('result-message').textContent.includes('Stopped after verification failure.')&&pick('feedback').textContent===''`,true,'Completion preserves report details without a duplicate footer message');
    await check(`requests.filter(x=>x.body).every(x=>x.token==='copy-token')`,true,'All writes carry antiforgery');
    await check(`requests.filter(x=>x.action==='Start').length`,1,'Copy is submitted once');
    await evaluate(`pick('close').click();launch()`);await wait();
    await check(`pick('previous').hidden`,false,'Completed reports remain accessible after closing');
    await evaluate(`pick('previous').click()`);await wait();
    await check(`pick('results').textContent.includes('Not verified')`,true,'Retained report can be reopened');
    for(const theme of ['light','dark'])for(const mode of ['metadata','warning','error']){
        await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)};partial=false;resultMode=${JSON.stringify(mode)};pick('close').click();launch()`);await wait();
        await evaluate(`pick('previous').click()`);await wait();
        await check(`pick('result-title').textContent`,mode==='warning'?'Completed with warnings':mode==='error'?'Stopped':'Completed',theme+'/'+mode+' completion reflects actual warnings/errors');
        if(mode!=='error'){
            await check(`[...pick('results').children].find(row=>row.querySelector('strong').textContent.includes('Daily Transfer')).classList.contains('has-metadata-notes')`,true,theme+' metadata-only object is blue');
            await check(`pick('results').textContent.includes('Metadata differences:') && pick('results').textContent.includes('Owner')`,true,theme+' metadata details remain visible as information');
        }
        if(mode==='metadata'){
            await check(`pick('results').querySelectorAll('.is-warning,.is-error').length`,0,theme+' metadata differences introduce no warning or error objects');
            await check(`pick('phase').textContent`,'Completed',theme+' metadata-only phase agrees with completed title');
        }
        if(mode==='warning'){
            await check(`[...pick('results').children].find(row=>row.querySelector('strong').textContent.includes('Partner SFTP')).classList.contains('is-warning')`,true,theme+' warning object is yellow');
            await check(`pick('results').textContent.includes('Overwritten with warnings') && pick('results').textContent.includes('Warnings: Resource Group')`,true,theme+' warning label identifies the object and reason');
        }
        if(mode==='error')await check(`[...pick('results').children].find(row=>row.querySelector('strong').textContent.includes('Partner SFTP')).classList.contains('is-error')`,true,theme+' failed object is red');
        const appearance=await evaluate(`(()=>{const rows=[...pick('results').children],base=getComputedStyle(rows.find(row=>!row.matches('.is-warning,.is-error,.has-metadata-notes')));return rows.filter(row=>row.matches('.is-warning,.is-error,.has-metadata-notes')).every(row=>{const css=getComputedStyle(row);return css.backgroundColor!==base.backgroundColor&&css.borderLeftColor!==base.borderLeftColor;});})()`);
        assert.equal(appearance,true,theme+'/'+mode+' highlighted cards have distinct tint and border');checks++;
        await screenshot('Results-'+theme+'-'+mode);
    }
    await check(`requests.filter(x=>x.action==='Start').length`,1,'Reviewing highlighted results never starts another copy');
    await check('errors',[],'No browser errors');
    const result={result:'PASS',checks,themes:['light','dark'],viewports:['1920x1080','1366x768','800x720','600x720']};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Environment copy workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate(`({errors,requests,status:pick('feedback').textContent})`));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});




