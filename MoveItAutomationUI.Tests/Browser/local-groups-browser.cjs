// Production inline partials and script, exercised against deterministic service/broker responses.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn}=require('node:child_process');
const stopBrowser=require('./rename-browser-cleanup.cjs');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8').replace(/\r\n/g,'\n');
const output=path.join(root,'TestResults/LocalGroups');fs.mkdirSync(output,{recursive:true});
const script=file=>'<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
let settings=read('Views/Configuration/_LocalGroupsSettings.cshtml').replace(/^@model[^\n]*\n/,'');
settings=settings.replace(/    @foreach[^\n]*\n    \{\n        var agent[^\n]*\n/,'').replace(/\n    \}\n/,'\n')
 .replace('checked="@Model.MoveItLocalGroups.Enabled"','')
 .replaceAll('@server.Key','A').replaceAll('@server.DisplayName','DEV')
 .replace('@agent?.CertificateSha256','A'.repeat(64)).replace('@Url.Content("~/api/local-groups-agent/")','/MAUI/api/local-groups-agent/');
const panel=read('Views/Configuration/_LocalGroupsPanel.cshtml').replace(/^@model[^\n]*\n/,'')
 .replace('hidden="@(!Model.MoveItLocalGroups.Enabled)"','hidden')
 .replace('@(Model.MoveItLocalGroups.Enabled ? "true" : "false")','false')
 .replace('@(ViewData["IsMoveItAdmin"] is true ? "true" : "false")','true')
 .replace('@ViewData["LocalGroupsServer"]','DEV')
 .replace('@Url.Action("State", "LocalGroups")','/MAUI/Configuration/LocalGroups/State')
 .replace('@Url.Action("Execute", "LocalGroups")','/MAUI/Configuration/LocalGroups/Execute');
const security=read('Views/Configuration/_SecuritySettings.cshtml').replace(/^@model[^\n]*\n/,'')
 .replace(/            @foreach[\s\S]*?\n            \}\n/,'')
 .replace('@await Html.PartialAsync("_LocalGroupsPanel", Model)',panel).replace('class="configuration-section"','class="configuration-section active"');
assert.ok(!/@(?:Model|Url|ViewData|foreach|await|server|agent)/.test(settings+security),'production Razor expressions rendered');
assert.ok(security.indexOf('MAUI Users')<security.indexOf('data-lg-panel'),'inline card follows MAUI Users');
assert.ok(panel.includes('class="new-task-dialog" data-lg-remove-dialog'),'removal uses the standard MAUI dialog');
assert.ok(panel.includes('class="new-task-dialog" data-lg-add-dialog'),'membership picker uses the standard MAUI dialog');
assert.ok(read('wwwroot/js/configuration.js').includes("if (key === 'security') window.MauiLocalGroupSettings?.open();"),'Security activation opens groups');
assert.ok(read('wwwroot/js/configuration.js').includes('window.MauiLocalGroupSettings?.read()'),'main settings save uses helper');
assert.ok(read('wwwroot/js/configuration.js').includes('window.MauiLocalGroupSettings?.markSaved()'),'card updates only after settings save');
const markup=`<div class="global-antiforgery"><input type="hidden" name="__RequestVerificationToken" value="fixture-token"></div>
<div class="configuration-workspace"><aside class="configuration-local-nav"><h2>Settings</h2><button class="configuration-category active">Security</button><button class="configuration-category">Experimental</button></aside><main class="configuration-content">${security}<section class="configuration-section" data-config-section="experimental">${settings}</section></main></div>`;
const mocks=String.raw`
window.errors=[];window.requests=[];window.failure='';window.listFailures=0;window.hold=false;window.held=[];
window.addEventListener('error',e=>errors.push(e.message));window.addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
window.localSid='S-1-5-21-100-200-300-1001';window.otherSid='S-1-5-21-100-200-300-1002';window.userSid='S-1-5-21-400-500-600-1200';
window.inventory=[{group:{sid:localSid,name:'Operators',description:'Finance <operators>',protected:false,members:[{sid:userSid,account:'CORP\\Existing',type:'User'}],version:'A'.repeat(64)},resourceGroups:['Finance']},
{group:{sid:otherSid,name:'Unassigned',description:'',protected:false,members:[],version:'B'.repeat(64)},resourceGroups:[]},
{group:{sid:'S-1-5-32-544',name:'Renamed Administrators',description:'Protected',protected:true,members:[],version:''},resourceGroups:[]}];
window.directory=[{sid:'S-1-5-21-400-500-600-1201',account:'CORP\\Alex<Selected>',type:'User',displayName:'Alex <Selected>'},{sid:'S-1-5-21-400-500-600-1202',account:'CORP\\TransferTeam',type:'Group',displayName:'Transfer Team'},{sid:'S-1-5-21-100-200-300-1100',account:'SERVER01\\miuser',type:'User',displayName:'Local User'},{sid:'S-1-5-21-100-200-300-1101',account:'SERVER01\\LocalGroup',type:'Group',displayName:'Local Group'},{sid:'S-1-5-21-100-200-300-1102',account:'SERVER01\\miuser2',type:'User',displayName:'Local User 2'},{sid:'S-1-5-21-400-500-600-1203',account:'CORP\\miuser3',type:'User',displayName:'Domain User'}];
const separator=String.fromCharCode(92);
window.directory.push(...[
 ['CORP','marc','1204'], ['CORP','marc2','1205'], ['CORP','examplemarc','1206'], ['OTHER','marc3','1207']
].map(([domain,account,rid])=>({sid:'S-1-5-21-400-500-600-'+rid,account:domain+separator+account,type:'User'})));
const reply=(body,status=200)=>({ok:status>=200&&status<300,status,headers:new Headers({'content-type':'application/json'}),json:async()=>structuredClone(body)});
window.fetch=async(url,options={})=>{
 if(url.endsWith('/State')){requests.push({url,kind:'state'});if(failure==='unregistered')return reply({registered:false,message:'Register the Windows Service under Experimental.'});return reply({enabled:true,registered:true,computerName:'SERVER01',server:'DEV',message:'',lastSeenUtc:null});}
 const request=JSON.parse(options.body);requests.push({url,request,token:options.headers.RequestVerificationToken});
 if(hold)await new Promise(resolve=>held.push(resolve));
 if(failure==='duplicate'&&request.operation==='create')return reply({message:'A local group with this name already exists.',outcomeUncertain:false},400);
 if(failure==='unknown'&&request.operation==='describe')return reply({message:'Refresh before retrying.',outcomeUncertain:true},400);
 if(failure==='bad-ack'&&request.operation==='describe')return reply({success:true});
 if(failure==='read-after-write'&&request.operation==='get')return reply({message:'Current group details could not be read.'},400);
 if(failure==='denied')return reply({message:'MOVEit and MAUI administrator access is required.'},403);
 const result={protocolVersion:1,computerName:'SERVER01',id:'cfb627f4-78ec-452e-9328-11ff16e61cb9',success:true,outcomeUncertain:false,message:'Updated.'};
 const row=inventory.find(row=>row.group.sid===request.groupSid);
 if(request.operation==='list'){
  if(listFailures>0||failure==='service-not-responding'){
   if(listFailures>0)listFailures--;
   return reply({message:'The Windows Service has not connected.',retryable:true,outcomeUncertain:false},400);
  }
  return reply({computerName:'SERVER01',groups:inventory});
 }
 if(request.operation==='get')result.groups=[row.group];
 if(request.operation==='resolve')result.principals=directory.filter(p=>p.account===request.principal||p.sid===request.principal);
 if(request.operation==='search'){
  const parts=request.query.toLowerCase().split(separator);
  const scope=parts.length===2?parts[0]:null;
  const term=parts.at(-1);
  result.principals=directory.filter(p=>{
   const [authority,account]=p.account.toLowerCase().split(separator);
   if(scope&&scope!=='.')return authority===scope&&account.startsWith(term);
   return (scope!=='.'||authority==='server01')&&account.includes(term);
  });
 }
 if(request.operation==='create')inventory.push({group:{sid:'S-1-5-21-100-200-300-1003',name:request.groupName,description:request.description,protected:false,members:[],version:'C'.repeat(64)},resourceGroups:[]});
 if(request.operation==='describe')row.group.description=request.description;
 if(request.operation==='describe'&&failure==='truncated-json')return {ok:true,status:200,headers:new Headers({'content-type':'application/json'}),json:async()=>{throw new SyntaxError('Unexpected end of JSON input');}};
 if(request.operation==='describe'&&failure==='html-success')return {ok:true,status:200,headers:new Headers({'content-type':'text/html'}),json:async()=>{throw new Error('HTML must not be parsed as JSON');}};
 if(request.operation==='describe'&&failure==='empty-json')return reply(null);
 if(request.operation==='search'&&failure==='truncated-json-read')return {ok:true,status:200,headers:new Headers({'content-type':'application/json'}),json:async()=>{throw new SyntaxError('Unexpected end of JSON input');}};
 if(request.operation==='add-member'){
  if(request.expectedVersion!==row.group.version)return reply({message:'Refresh the changed group.',outcomeUncertain:false},400);
  if(failure==='second-add'&&request.principal==='SERVER01\\miuser2')return reply({message:'Windows rejected this member.',outcomeUncertain:false},400);
  if(failure==='local-group-nesting')return reply({message:'Windows rejected this account type.',outcomeUncertain:false},400);
  const principal=directory.find(p=>p.account===request.principal);
  if(!row.group.members.some(p=>p.sid===principal.sid))row.group.members.push(principal);
  row.group.version=String(row.group.members.length).repeat(64);
 }
 if(request.operation==='remove-member')row.group.members=row.group.members.filter(p=>p.sid!==request.memberSid);
 if(request.operation==='delete')inventory=inventory.filter(item=>item!==row);
 return reply(result);
};`;
const html='<!doctype html><html data-theme="light"><head><meta charset="utf-8"><style>'+read('wwwroot/css/site.css')+read('wwwroot/css/configuration-local-groups.css')+'body{min-width:0;margin:24px}</style></head><body>'+markup+'<script>'+mocks+'</script>'+script('wwwroot/js/configuration-local-groups.js')+'<script>window.ready=true;</script></body></html>';
for(const[,source]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(source);
if(process.argv.includes('--syntax-only')){console.log('PASS Local Groups production browser fixture syntax');process.exit(0);}
const candidates=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium'];
const executable=candidates.find(file=>file&&fs.existsSync(file)); assert.ok(executable,'Edge or Chromium is required');
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'maui-local-groups-'));let browser,socket,server,id=0;const pending=new Map();
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function command(method,params={}){const key=++id;return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{pending.delete(key);reject(new Error('CDP timed out: '+method));},15000);pending.set(key,{resolve,reject,timer});socket.send(JSON.stringify({id:key,method,params}));});}
async function evaluate(expression){const result=await command('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(result.exceptionDetails)throw new Error(JSON.stringify(result.exceptionDetails));return result.result.value;}
async function until(expression,label,attempts=150){for(let i=0;i<attempts;i++){if(await evaluate(expression))return;await pause(30);}throw new Error('Timed out: '+label+'; '+JSON.stringify(await evaluate('({errors,requests})')));}
async function shot(name){const result=await command('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});fs.writeFileSync(path.join(output,name+'.png'),Buffer.from(result.data,'base64'));}
const q = selector => `document.querySelector(${JSON.stringify(selector)})`;
let checks=0;
async function check(expression,expected,label){assert.deepEqual(await evaluate(expression),expected,label);checks++;}
(async()=>{
 server=http.createServer((req,res)=>{res.setHeader('Content-Type','text/html');res.end(req.url.startsWith('/site-root')?html.replaceAll('/MAUI/Configuration/LocalGroups','/Configuration/LocalGroups').replaceAll('/MAUI/api/local-groups-agent/','/api/local-groups-agent/'):html);});await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 browser=spawn(executable,['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--no-sandbox','--remote-debugging-port=0','--remote-allow-origins=*','--user-data-dir='+profile,'about:blank'],{stdio:'ignore'});
 let launchError;browser.on('error',error=>{launchError=error;});let port='';
 for(let i=0;i<150&&!port;i++){if(launchError)throw launchError;try{port=fs.readFileSync(path.join(profile,'DevToolsActivePort'),'utf8').split(/\r?\n/)[0];}catch{}if(!port)await pause(100);}assert.match(port,/^\d+$/);
 const targets=await(await fetch('http://127.0.0.1:'+port+'/json/list')).json();socket=new WebSocket(targets.find(target=>target.type==='page').webSocketDebuggerUrl);
 await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true});});
 socket.addEventListener('message',event=>{const message=JSON.parse(event.data),task=pending.get(message.id);if(!task)return;pending.delete(message.id);clearTimeout(task.timer);if(message.error)task.reject(new Error(JSON.stringify(message.error)));else task.resolve(message.result);});
 await command('Page.enable');await command('Emulation.setDeviceMetricsOverride',{width:1366,height:1000,deviceScaleFactor:1,mobile:false});
 await command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/'});await until('window.ready','ready');

 const field=name=>q('[data-lg-'+name+']'),click=async name=>evaluate(field(name)+'.click()');
 const loaded=async label=>until(field('status')+`.textContent==='Loaded.'&&!${field('load')}.disabled`,label||'loaded');
 const updated=async label=>until(field('status')+`.textContent==='Updated.'&&!${field('load')}.disabled`,label||'updated');
 const searched=async label=>until(`!${field('find')}.disabled`,label);
 const open=async sid=>{await evaluate(`[...${field('groups')}.querySelectorAll('button')].find(b=>b.textContent===inventory.find(r=>r.group.sid===${JSON.stringify(sid)}).group.name).click()`);await loaded('selected group');};
 const openAdd=async()=>{await click('add-open');await until(field('add-dialog')+'.open','open member picker');};
 await check(field('panel')+'.hidden',true,'Off by default');await check('requests.length',0,'disabled feature makes no state or service calls');
 await check('MauiLocalGroupSettings.read().agents[0].certificateSha256','A'.repeat(64),'only certificate registration collected');
 await check('Object.keys(MauiLocalGroupSettings.read()).sort()',['agents','enabled'],'minimal MAUI settings payload');
 await check('Object.keys(MauiLocalGroupSettings.read().agents[0]).sort()',['certificateSha256','serverKey'],'no manually configured computer name');
 await check(`document.querySelectorAll('[data-lg-agent-host],[data-lg-computer]').length`,0,'redundant host and computer controls removed');
 await check(`${field('broker-url')}.textContent===location.origin+'/MAUI/api/local-groups-agent/'`,true,'setup address uses current hostname port and virtual application path');
 await check(field('broker-url')+'.tagName','CODE','service address is informational');
 await evaluate(`document.querySelector('[data-setting="moveItLocalGroupsEnabled"]').checked=true`);
 await check(field('panel')+'.hidden',true,'unsaved toggle does not expose management');
 await evaluate('hold=true;MauiLocalGroupSettings.markSaved()');await until('held.length===1','automatic list');
 await check(field('panel')+'.hidden',false,'saved toggle exposes Security card');
 await check('requests.filter(r=>r.request).map(r=>r.request.operation)',['list'],'first opening loads inventory without a click');
 await check(field('load')+'.textContent','Reload Groups','load becomes reload');
 await check(field('inventory-loading')+'.hidden',false,'inventory loading spinner shown');
 await check(field('workspace')+'.hidden',true,'both panels hidden until inventory loads');
 await check(field('load')+'.disabled',true,'reload disabled while inventory loads');
 await check(`(()=>{const a=${field('inventory-loading')}.querySelector('.inline-spinner').getBoundingClientRect(),b=${field('inventory-loading')}.getBoundingClientRect();return Math.abs(a.x+a.width/2-b.x-b.width/2)<2&&Math.abs(a.y+a.height/2-b.y-b.height/2)<2})()`,true,'loading indicator centered across the card');
 await shot('loading-light');
 await check(field('status')+'.textContent','Loading…','minimal progress');
 await evaluate('hold=false;held.splice(0).forEach(resolve=>resolve())');await loaded('list');
 await check(field('groups')+`.querySelectorAll('tr').length`,2,'only custom groups shown');
 await check(field('groups')+`.textContent.includes('Administrators')`,false,'protected groups hidden');
 await check(field('inventory-loading')+'.hidden',true,'spinner removed on completion');
 await check(field('workspace')+'.hidden',false,'both panels restored on completion');
 const firstLoadCount=await evaluate('requests.length');await evaluate('MauiLocalGroupSettings.open()');
 await check('requests.length',firstLoadCount,'reopening Security does not reload automatically');
 await check(`(()=>{const a=${field('filter')}.getBoundingClientRect(),b=${field('new')}.getBoundingClientRect();return b.left>a.right&&Math.abs(a.bottom-b.bottom)<2})()`,true,'New Group sits immediately right of filter');
 await check(`(()=>{const a=document.querySelector('.local-groups-list').getBoundingClientRect(),b=${field('right')}.getBoundingClientRect();return b.left-a.right>=16&&parseFloat(getComputedStyle(${field('right')}).borderLeftWidth)>0})()`,true,'panels clearly separated');
 await check(`getComputedStyle(${field('groups')}.querySelector('button')).borderTopWidth`,'0px','group names are plain text buttons');
 await check(field('panel')+`.textContent.includes('Windows default/reserved groups are protected.')`,false,'protected group notice removed');
 await check(field('panel')+`.textContent.includes('Connected to ')`,false,'connected message removed');
 await check(field('panel')+`.querySelectorAll('dialog').length`,2,'member add and removal use modals');
 await check(`getComputedStyle(${field('groups')}.closest('table')).fontSize`,'12px','reuse Settings table typography');
 await shot('security-light');
 await evaluate(`${field('filter')}.value='Finance';${field('filter')}.dispatchEvent(new Event('input'))`);
 await check(field('groups')+'.children.length',1,'resource group filter');
 await evaluate(`${field('filter')}.value='';${field('filter')}.dispatchEvent(new Event('input'))`);
 await click('new');await click('create-save');await until(field('error')+`.textContent==='Group Name is required.'`,'empty name');
 await check(field('create')+'.hidden',false,'inline create retained');
 await evaluate(`failure='duplicate';${field('create-name')}.value='Taken'`);await click('create-save');await until(field('error')+`.textContent.includes('already exists')`,'duplicate');
 await check(field('create-name')+'.value','Taken','duplicate retains name');await check(field('create-save')+'.disabled',false,'known failure permits correction');
 await evaluate(`failure='';${field('create-name')}.value='NewGroup';${field('create-description')}.value='New description'`);await click('create-save');await updated('created');
 await check(field('create')+'.hidden',true,'create closes inline section after successful read');await check(field('groups')+'.children.length',3,'new group loaded');
 await open('S-1-5-21-100-200-300-1001');
 await check(field('description')+'.value','Finance <operators>','literal description');await check(field('delete')+'.disabled',true,'assigned local group deletion disabled');
 await check(`document.querySelectorAll('[data-lg-sid],[data-lg-description-cancel]').length`,0,'SID and Cancel Changes removed');
 await check(`getComputedStyle(${field('description-save')}.parentElement).justifyContent`,'flex-end','Save Description aligned right');
 await evaluate(`inventory.find(r=>r.group.sid===localSid).resourceGroups=[]`);await click('load');await loaded();await open('S-1-5-21-100-200-300-1001');
 await check(field('delete')+'.disabled',true,'members alone disable deletion');
 await check(field('delete')+'.title','Remove all members before deleting this group.','member deletion restriction explained');
 await evaluate(`inventory.find(r=>r.group.sid===localSid).resourceGroups=['Finance']`);
 await evaluate(`${field('description')}.value='Unsaved'`);await click('add-open');
 await check(field('error')+`.textContent.includes('Save the description or reload')`,true,'description changes cannot be silently discarded');await click('load');await loaded();await open('S-1-5-21-100-200-300-1001');
 await check(field('description')+'.value','Finance <operators>','reload restores saved description');
 await evaluate(`${field('description')}.value='Updated <description>'`);await click('description-save');await updated('description saved');
 await check(`requests.find(r=>r.request?.operation==='describe').request.expectedVersion`,'A'.repeat(64),'mutation uses snapshot version');
 await check(field('description')+'.value','Updated <description>','followup read reflects description');
 await check(`getComputedStyle(${field('add-open')}.parentElement).justifyContent`,'flex-end','Add aligned right');
 await check(field('add-open')+`.parentElement.previousElementSibling.contains(${field('members')})`,true,'Add directly below Members');
 await openAdd();
 await check(field('add-dialog')+`.matches(':modal')`,true,'member picker opens as a modal');
 await check(field('add-group')+'.textContent','Operators','picker identifies the target group');
 await check(`document.activeElement===${field('query')}`,true,'opening picker focuses search');
 await click('add-cancel');
 await check(field('add-dialog')+'.open',false,'Cancel closes picker');
 await check(`requests.filter(r=>r.request?.operation==='add-member').length`,0,'Cancel does not submit a member');
 await openAdd();
 await check(field('results')+`.closest('table').getBoundingClientRect().height>0`,true,'Find results table visible before search');
 await check(field('results-empty')+'.textContent','No results.','empty results message');
 await check(field('add')+'.disabled',true,'Add disabled before search');
 await check(`document.querySelector('#local-groups-find-help').previousElementSibling.contains(${field('query')})`,true,'search instructions immediately below textbox row');
 const checkFirstResult=async()=>evaluate(`${field('results')}.querySelector('input').click()`);
 const separator=String.fromCharCode(92);
 const query=async value=>evaluate(`${field('query')}.value=${JSON.stringify(value)}`);
 await evaluate(`${field('query')}.value='no-such-account'`);await click('find');await searched('empty directory search');
 await check(field('results-empty')+'.hidden',false,'no matches retain the empty table message');
 await check(field('add')+'.disabled',true,'Add disabled with no matches');
 const requestsBeforeFind=await evaluate('requests.length');
 await evaluate(`window.memberBeforeFind=${field('members')}.firstElementChild;hold=true;${field('query')}.value='Alex'`);
 await click('find');await until('held.length===1','pending directory search');
 await check(field('add-status')+'.textContent','Searching…','Find reports progress in the picker footer');
 await check(field('loading')+'.hidden',true,'Find leaves the parent loading spinner hidden');
 await check(`getComputedStyle(${field('description')}).visibility`,'visible','Find keeps selected group details visible');
 await check(field('status')+'.textContent','Updated.','Find preserves the parent status while pending');
 await evaluate('hold=false;held.splice(0).forEach(resolve=>resolve())');await searched('directory search');
 await check(`requests.slice(${requestsBeforeFind}).map(r=>r.request.operation)`,['search'],'Find only requests a directory search');
 await check(`${field('members')}.firstElementChild===memberBeforeFind`,true,'Find leaves the parent Members rows intact');
 await check(field('status')+'.textContent','Updated.','Find completion leaves the parent status intact');
 await check(field('add-status')+'.textContent','','Find clears its progress message on completion');
 await check(field('results')+'.children.length',1,'only matching account is listed');
 await check(field('add')+'.disabled',true,'Add waits for a checked account');
 await check(field('results')+`.textContent.includes('Alex<Selected>')&&!${field('results')}.querySelector('selected')`,true,'directory account rendered as literal text');
 await checkFirstResult();await evaluate('hold=true');await click('add');await until('held.length===1','pending membership write');
 await check(field('add-status')+'.textContent','Adding members…','Add reports progress in the picker footer');
 await check(`${field('add-status')}.getBoundingClientRect().right<=${field('add-cancel')}.getBoundingClientRect().left`,true,'progress appears left of the footer actions');
 await evaluate('held.shift()()');await until("held.length===1&&requests.at(-1).request.operation==='get'",'pending membership readback');
 await check(field('add-status')+'.textContent','Adding members…','Add keeps progress visible through readback');
 await check(field('add-dialog')+'.open',true,'picker stays open until readback completes');
 await shot('member-add-progress');
 await evaluate('hold=false;held.splice(0).forEach(resolve=>resolve())');await updated('member add');
 await check(field('add-dialog')+'.open',false,'successful addition closes picker');
 await check(`requests.find(r=>r.request?.operation==='add-member').request.principal`,'CORP'+separator+'Alex<Selected>','exact selected account sent');
 await check(field('members')+'.children.length',2,'existing member preserved');
 const domainQuery='CORP'+separator+'marc';
 await openAdd();
 await query(domainQuery);await click('find');await searched('domain prefix search');
 await check(`requests.at(-1).request`,{operation:'search',query:domainQuery},'qualified domain name uses prefix search');
 await check(`${field('results')}.children.length`,2,'domain prefix returns both matching names');
 await check(`${field('results')}.textContent.includes('examplemarc') || ${field('results')}.textContent.includes('marc3')`,false,'domain prefix excludes suffix and other domain matches');
 const exactSid='S-1-5-21-400-500-600-1202';
 await query(exactSid);await click('find');await searched('exact group SID resolve');
 await check(`requests.at(-1).request`,{operation:'resolve',principal:exactSid},'SID resolves exactly');
 await checkFirstResult();await click('add');await updated('domain group add');
 await check(field('members')+'.children.length',3,'domain security group added');
 const removeFirst=async()=>evaluate(`${field('members')}.querySelector('button').click()`);
 await check(`getComputedStyle(${field('members')}.querySelector('button').parentElement).textAlign`,'right','member removal actions align right');
 await removeFirst();
 await check(field('remove-dialog')+`.matches(':modal')`,true,'member removal opens the MAUI modal');
 await check(field('remove-group')+'.textContent','Operators','modal identifies target group');
 await check(field('remove-account')+'.textContent','CORP'+separator+'Existing','modal identifies selected member');
 await check(field('confirm')+'.hidden',true,'group deletion confirmation stays separate');
 await shot('remove-member-modal');await click('remove-cancel');
 await check(field('remove-dialog')+'.open',false,'Cancel closes removal modal');
 await check(field('members')+'.children.length',3,'cancel keeps all members');
 await check(`requests.filter(r=>r.request?.operation==='remove-member').length`,0,'cancel does not submit a removal');
 await removeFirst();await evaluate(`failure='denied'`);await click('remove-apply');
 await until(field('remove-error')+`.textContent.includes('administrator access')`,'removal error in modal footer');
 await check(field('remove-dialog')+'.open',true,'failed removal retains confirmation');
 await evaluate(`failure=''`);await click('remove-apply');await updated('member remove');
 await check(field('remove-dialog')+'.open',false,'successful removal closes modal');
 await check(`requests.filter(r=>r.request?.operation==='remove-member').at(-1).request.memberSid`,'S-1-5-21-400-500-600-1200','remove targets selected SID only');
 await check(field('members')+'.children.length',2,'other members retained');
 const localQuery='.'+separator+'miuser';
 await openAdd();
 await evaluate(`${field('query')}.value=${JSON.stringify(localQuery)}`);await click('find');await searched('local partial search');
 await check(`requests.at(-1).request`,{operation:'search',query:localQuery},'dot prefix uses local search');
 await check(`${field('results')}.children.length`,2,'both matching local accounts displayed');
 await check(`${field('results')}.textContent.includes('miuser3')`,false,'local search excludes domain match');
 await evaluate(`${field('results')}.querySelectorAll('input').forEach(checkbox=>checkbox.click());failure='second-add'`);
 await shot('member-find-results');
 await click('add');await until(field('add-error')+`.textContent.includes('1 of 2 selected members added')`,'partial add failure');
 await check(field('add-dialog')+'.open',true,'failed addition keeps picker open');
 await check(field('add-status')+'.textContent','','partial failure clears the progress message');
 await check(field('error')+'.hidden',true,'add error stays in the modal footer');
 await check(`${field('add-error')}.getBoundingClientRect().right <= ${field('add-cancel')}.getBoundingClientRect().left`,true,'footer error sits left of action buttons');
 await check(field('members')+'.children.length',3,'first selected user was saved');
 await check(`${field('results')}.querySelectorAll('input:checked').length`,1,'only failed addition stays selected');
 await evaluate(`failure=''`);await click('add');await updated('remaining local member added');
 await check(`requests.filter(r=>r.request?.operation==='add-member'&&r.request.principal===${JSON.stringify('SERVER01'+separator+'miuser')}).length`,1,'successful addition is not sent again');
 await check(field('members')+'.children.length',4,'both local users added and domain members retained');
 await check(field('add-dialog')+'.open',false,'successful retry closes picker');
 const localGroup='SERVER01'+separator+'LocalGroup';
 await openAdd();
 await evaluate(`${field('query')}.value=${JSON.stringify(localGroup)}`);await click('find');await searched('local group exact resolve');
 await check(`requests.at(-1).request`,{operation:'resolve',principal:localGroup},'computer-qualified group resolves exactly');
 await checkFirstResult();await evaluate(`failure='local-group-nesting'`);await click('add');
 await until(field('add-error')+`.textContent.includes('Windows rejected this account type')`,'native group error shown');
 await click('add-cancel');
 await evaluate(`failure=''`);
 await shot('members-light');
 await open('S-1-5-21-100-200-300-1002');await click('delete');await click('confirm-apply');
 await check(field('error')+'.textContent','Type the exact group name to confirm deletion.','typed delete confirmation');
 await evaluate(`${field('confirm-name')}.value='Unassigned'`);await click('confirm-apply');await updated('delete');
 await check(`requests.find(r=>r.request?.operation==='delete').request.confirmDelete`,'Unassigned','delete carries exact confirmation');
 await check(field('groups')+'.children.length',2,'deleted group removed from inventory');
 await open('S-1-5-21-100-200-300-1001');
 await evaluate(`failure='unknown';${field('description')}.value='Retained change'`);await click('description-save');await until(field('status')+`.textContent==='Refresh required.'`,'uncertain result');
 await check(field('description-save')+'.disabled',true,'uncertain write cannot be resubmitted');await check(field('new')+'.disabled',true,'new writes blocked until refresh');
 await check(`[...${field('groups')}.querySelectorAll('button')].every(button=>button.disabled)`,true,'changing group cannot bypass inventory reconciliation');
 await check(field('description')+'.value','Retained change','uncertain outcome retains fields');await check(field('load')+'.disabled',false,'refresh remains available');await shot('uncertain-footer');
 await evaluate(`failure=''`);await click('load');await loaded('uncertain reconciliation read');await open('S-1-5-21-100-200-300-1001');
 await evaluate(`failure='bad-ack';${field('description')}.value='Keep this too'`);await click('description-save');await until(field('status')+`.textContent==='Refresh required.'`,'malformed acknowledgement');
 await check(field('error')+`.textContent.includes('could not be confirmed')`,true,'bad acknowledgement explained');await check(field('description-save')+'.disabled',true,'bad acknowledgement blocks write retry');
 await evaluate(`failure=''`);await click('load');await loaded('refresh after bad ack');await open('S-1-5-21-100-200-300-1001');
 for(const mode of ['truncated-json','html-success','empty-json']){
  const savedValue='Saved with '+mode;
  await evaluate(`failure=${JSON.stringify(mode)};${field('description')}.value=${JSON.stringify(savedValue)}`);await click('description-save');await until(field('status')+`.textContent==='Refresh required.'`,mode+' blocks writes');
  await check(field('description-save')+'.disabled',true,mode+' disables repeat write');
  await check(field('new')+'.disabled',true,mode+' blocks other writes pending refresh');
  await check(field('description')+'.value',savedValue,mode+' retains entered description');
  await check(`inventory.find(row=>row.group.sid===localSid).group.description`,savedValue,mode+' simulates a successful native write');
  const writeCount=await evaluate(`requests.filter(row=>row.request?.operation==='describe').length`);await click('description-save');
  await check(`requests.filter(row=>row.request?.operation==='describe').length`,writeCount,mode+' cannot resend through disabled Save');
  await check(field('error')+`.textContent.includes('Refresh')`,true,mode+' gives refresh guidance');
  await evaluate(`failure=''`);await click('load');await loaded(mode+' refresh');await open('S-1-5-21-100-200-300-1001');
  await check(field('description')+'.value',savedValue,mode+' reload verifies saved state');
 }
 await openAdd();
 await evaluate(`failure='truncated-json-read';${field('query')}.value='Alex'`);await click('find');await until(field('add-error')+`.textContent.includes('could not be read')`,'read parse failure');
 await check(field('description-save')+'.disabled',false,'failed directory read does not label a write as uncertain');
 await check(field('find')+'.disabled',false,'failed directory read remains retryable');
 await click('add-cancel');
 await evaluate(`failure=''`);
 await evaluate(`failure='read-after-write';${field('description')}.value='Saved but read unavailable'`);await click('description-save');await updated('write acknowledged despite read failure');
 await evaluate(`${field('add-dialog')}.dispatchEvent(new Event('close'))`);
 await check(field('error')+`.textContent.startsWith('Updated, but')`,true,'acknowledged write distinguished from read failure');
 await check(field('description')+'.value','Saved but read unavailable','acknowledged draft retained after read failure');
 await check(field('description-save')+'.disabled',true,'failed readback blocks another write until refresh');
 await evaluate(`failure=''`);await click('load');await loaded('refresh acknowledged write');await open('S-1-5-21-100-200-300-1001');
 await evaluate(`failure='denied';${field('description')}.value='Permission check'`);await click('description-save');await until(field('error')+`.textContent.includes('administrator access')`,'permission failure');
 await check(field('description')+'.value','Permission check','permission failure preserves draft');await evaluate(`failure=''`);await click('load');await loaded();await open('S-1-5-21-100-200-300-1001');
 await evaluate(`failure='';document.documentElement.dataset.theme='dark'`);await shot('security-dark');
 for(const width of[1920,1366,800,600]){
  await command('Emulation.setDeviceMetricsOverride',{width,height:1100,deviceScaleFactor:1,mobile:false});
  await check('document.documentElement.scrollWidth<=innerWidth+1',true,'no horizontal page overflow at '+width);
  await check(field('panel')+`.getBoundingClientRect().width>200`,true,'card has usable width at '+width);
  await check(field('load')+`.getBoundingClientRect().right<=${field('panel')}.getBoundingClientRect().right`,true,'group toolbar stays inside card at '+width);await check(field('status')+`.getBoundingClientRect().left>=${field('panel')}.getBoundingClientRect().left+16`,true,'footer retains Settings card inset at '+width);await shot('security-dark-'+width);
 }
 await command('Emulation.setDeviceMetricsOverride',{width:1366,height:1100,deviceScaleFactor:1,mobile:false});
 await evaluate(`document.documentElement.dataset.theme='light';document.querySelector('[data-config-section="security"]').classList.remove('active');document.querySelector('[data-config-section="experimental"]').classList.add('active')`);await shot('experimental-light');
 await evaluate(`document.querySelector('[data-config-section="experimental"]').classList.remove('active');document.querySelector('[data-config-section="security"]').classList.add('active');hold=true`);await click('load');await until('held.length===1','stale list');
 await evaluate(`document.querySelector('[data-setting="moveItLocalGroupsEnabled"]').checked=false;MauiLocalGroupSettings.markSaved();hold=false;held.splice(0).forEach(resolve=>resolve())`);
 await until(field('panel')+'.hidden','disabled during request');
 await pause(100);await check(field('groups')+'.children.length',0,'stale reply cannot repopulate disabled card');await check(field('inventory-loading')+'.hidden',true,'disabled feature clears spinner');
 await check('requests.filter(r=>r.request).every(r=>r.url===\'/MAUI/Configuration/LocalGroups/Execute\'&&r.token===\'fixture-token\'&&!r.request.requestedBy&&!r.request.serverKey)',true,'same origin virtual path antiforgery and server-owned scope');
 await check('errors',[],'no runtime errors');
 await command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/site-root'});await until("window.ready&&location.pathname==='/site-root'",'site-root ready');
 await check(`${field('broker-url')}.textContent===location.origin+'/api/local-groups-agent/'`,true,'site-root setup address keeps current port');
 await evaluate(`document.querySelector('[data-setting="moveItLocalGroupsEnabled"]').checked=true;MauiLocalGroupSettings.markSaved()`);await loaded('site-root automatic groups');
 await check('requests.length',2,'site-root opens with state and inventory automatically');
 await check(`requests.filter(r=>r.request).every(r=>r.url==='/Configuration/LocalGroups/Execute')`,true,'site-root API path');
 await check(field('server')+'.textContent','SERVER01','computer name reported by service is displayed');
 await check('errors',[],'no site-root runtime errors');
 await command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/'});await until("window.ready&&location.pathname==='/'",'lazy page');
 await evaluate(`document.querySelector('[data-config-section="security"]').classList.remove('active');document.querySelector('[data-setting="moveItLocalGroupsEnabled"]').checked=true;MauiLocalGroupSettings.markSaved()`);
 await check('requests.length',0,'saved enabled feature waits until Security is opened');
 await evaluate(`listFailures=2;document.querySelector('[data-config-section="security"]').classList.add('active');MauiLocalGroupSettings.open()`);
 await until('requests.length===2','first inventory attempt');
 await check(field('inventory-loading')+'.hidden',false,'loading spinner stays through a transient failure');
 await check(field('error')+'.hidden',true,'intermediate failures stay silent');
 await loaded('first Security activation recovers');
 await check('requests.filter(r=>r.request?.operation==="list").length',3,'first Security activation recovers after two retries');
 await evaluate(`MauiLocalGroupSettings.open()`);await check('requests.length',6,'later Security activation retains loaded inventory');
 // Resource Group assignment independently blocks deletion after the last member is removed.
 await evaluate(`inventory.find(r=>r.group.sid===localSid).group.members=[]`);await click('load');await loaded();await open('S-1-5-21-100-200-300-1001');
 await check(field('delete')+'.disabled',true,'assignment alone disables deletion');
 await check(field('delete')+'.title','Remove MOVEit Resource Group assignments before deleting this group.','assignment deletion restriction explained');
 const beforeRegistrationFailure=await evaluate('requests.length');
 await evaluate(`failure='unregistered'`);await click('load');await until(field('error')+`.textContent.includes('Register the Windows Service')`,'registration error');
 await check(field('status')+'.textContent','Ready.','connection error does not report Loaded');
 await check(field('inventory-loading')+'.hidden',true,'registration failure clears spinner');
 await check(field('description-save')+'.disabled',true,'failed registration prevents writes to the old selection');
 await check(field('load')+'.disabled',false,'Reload Groups can retry after connection error');
 await check(`requests.slice(${beforeRegistrationFailure}).map(r=>r.kind)`,['state'],'registration failure is reported without retry');
 await evaluate(`failure=''`);await click('load');await loaded('registration recovered');
 await check(field('error')+'.hidden',true,'successful reload clears connection error');
 const beforeOfflineReload=await evaluate('requests.length');
 await evaluate(`failure='service-not-responding'`);await click('load');
 await until(`requests.length>=${beforeOfflineReload+2}`,'pending offline reload');
 await check(field('workspace')+'.hidden',true,'reload hides both panels between attempts');
 await check(field('error')+'.hidden',true,'no error shown before retry exhaustion');
 await until(field('error')+`.textContent.includes('The Windows Service is not responding to requests. Click Reload Groups to try again.')`,'inventory retry exhaustion',360);
 await check(`requests.slice(${beforeOfflineReload}).filter(r=>r.request?.operation==='list').length`,4,'offline reload makes the first attempt and exactly three retries');
 await check(field('inventory-loading')+'.hidden',true,'terminal error clears the spinner');
 await check(field('load')+'.disabled',false,'terminal error allows manual reload');
 await evaluate(`failure=''`);await click('load');await loaded('offline reload recovery');
 await check(field('workspace')+'.hidden',false,'manual recovery restores both panels');
 const scrolls=name=>`(()=>{const viewport=${field(name)}.closest('table').parentElement;return viewport.scrollHeight>viewport.clientHeight+1})()`;
 await evaluate(`inventory.push(...Array.from({length:12},(_,i)=>({group:{sid:'fixture-'+i,name:'Test Group '+i,description:'',protected:false,members:[],version:''},resourceGroups:[]})));
  inventory.find(r=>r.group.sid===localSid).group.members=Array.from({length:10},(_,i)=>({sid:'member-'+i,account:'Test Member '+i,type:'User'}))`);
 await click('load');await loaded();await open('S-1-5-21-100-200-300-1001');
 await check(field('groups')+'.rows.length',14,'group scroll boundary fixture');
 await check(field('members')+'.rows.length',10,'members scroll boundary fixture');
 await check(scrolls('groups'),false,'14 groups fit without an internal scrollbar');
 await check(scrolls('members'),false,'10 members fit without an internal scrollbar');
 await evaluate(`inventory.push({group:{sid:'fixture-last',name:'Test Group 12',description:'',protected:false,members:[],version:''},resourceGroups:[]});
  inventory.find(r=>r.group.sid===localSid).group.members.push({sid:'member-last',account:'Test Member 10',type:'User'})`);
 await click('load');await loaded();await open('S-1-5-21-100-200-300-1001');
 await check(scrolls('groups'),true,'15 groups scroll');
 await check(scrolls('members'),true,'11 members scroll');
 await shot('member-scroll-limits');
 await evaluate(`${field('filter')}.value='Test Group';${field('filter')}.dispatchEvent(new Event('input'))`);
 await check(scrolls('groups'),false,'filtering below 15 groups removes scrollbar');
 await check('errors',[],'no lazy activation runtime errors');
 fs.writeFileSync(path.join(output,'results.json'),JSON.stringify({result:'PASS',checks},null,2));console.log('Local Groups inline UI: PASS '+checks+' checks');
})().catch(async error=>{console.error(error);try{if(socket)await shot('FAILED');}catch{}process.exitCode=1;}).finally(async()=>{
 // Close Edge through CDP before releasing its socket; otherwise its exit can race taskkill.
 if(socket?.readyState===WebSocket.OPEN)await Promise.race([command('Browser.close').catch(()=>{}),pause(2000)]);
 if(browser?.exitCode===null)await Promise.race([new Promise(resolve=>browser.once('exit',resolve)),pause(2000)]);
 stopBrowser(browser);socket?.close();server?.close();
 for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}
 try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});

