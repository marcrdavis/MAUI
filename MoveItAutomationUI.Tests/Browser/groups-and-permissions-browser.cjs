// Production Groups and Permissions hierarchy, export, settings and refresh workflows in Edge.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
const output=path.join(root,'TestResults/GroupsAndPermissions');fs.mkdirSync(output,{recursive:true});
const candidates=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium'];
const executable=candidates.find(file=>file&&fs.existsSync(file));
const vm=require('node:vm');
const configurationSource=read('wwwroot/js/configuration.js');
const navigationFunction=configurationSource.slice(configurationSource.indexOf('    function syncSavedEditorNavigation('),configurationSource.indexOf('    // Purpose: Validates and saves the complete Configuration Settings payload.'));
const navLink={dataset:{groupsAndPermissionsNativeRoute:'#/resources/permissions',nativeEditorRoute:'#/resources/permissions'}};
const systemNavLink={dataset:{nativeEditorRoute:'#/system/settings'}};
const navigationRoot={dataset:{}};
const navigationContext=vm.createContext({document:{documentElement:navigationRoot,querySelectorAll:selector=>selector.includes('system-settings-open')?[systemNavLink]:[navLink]}});
vm.runInContext(navigationFunction,navigationContext);
vm.runInContext('syncSavedEditorNavigation({useMauiGroupsAndPermissionsEditor:true,useMauiSystemSettingsEditor:true})',navigationContext);
assert.equal(navLink.dataset.nativeEditorRoute,'','Successful enable must immediately stop native interception');
assert.equal(systemNavLink.dataset.nativeEditorRoute,'');assert.equal(navigationRoot.dataset.useMauiSystemSettingsEditor,'true');
vm.runInContext('syncSavedEditorNavigation({useMauiGroupsAndPermissionsEditor:false,useMauiSystemSettingsEditor:false})',navigationContext);
assert.equal(navLink.dataset.nativeEditorRoute,'#/resources/permissions','Successful disable must immediately restore normal native launch');
assert.equal(systemNavLink.dataset.nativeEditorRoute,'#/system/settings');assert.equal(navigationRoot.dataset.useMauiSystemSettingsEditor,'false');

// Exercise the actual Settings synchronizer as connections are added, edited and removed.
const copySync=configurationSource.slice(configurationSource.indexOf('    function syncCopyAvailability()'),configurationSource.indexOf('    // Purpose: Updates the Configuration page dirty/restart state'));
let draftServers=[{key:'one',environmentKey:'DEV'},{key:'two',environmentKey:'dev'}];
const copyToggle={checked:true,disabled:false},copyRetention={disabled:false,value:'17'},copyHint={title:''};
const copyContext=vm.createContext({collectServers:()=>draftServers,workspace:{querySelector:s=>s.includes('copyBetweenEnvironments')?copyToggle:copyRetention,querySelectorAll:()=>[copyHint]}});
vm.runInContext(copySync,copyContext);
vm.runInContext('syncCopyAvailability()',copyContext);
assert.equal(copyToggle.checked,false);assert.equal(copyToggle.disabled,true);assert.equal(copyRetention.disabled,true);assert.match(copyHint.title,/two or more MOVEit environments/);
draftServers[1].environmentKey='PROD';vm.runInContext('syncCopyAvailability()',copyContext);
assert.equal(copyToggle.disabled,false);assert.equal(copyRetention.disabled,false);assert.equal(copyToggle.checked,false);assert.equal(copyHint.title,'');
copyToggle.checked=true;draftServers.pop();vm.runInContext('syncCopyAvailability()',copyContext);
assert.equal(copyToggle.checked,false);assert.equal(copyRetention.value,'17');

const modelContext={window:{},document:{querySelector:()=>null}};vm.runInNewContext(read('wwwroot/js/groups-and-permissions.js'),modelContext);
const {keys,filterGroups,flattenRows}=modelContext.window.MauiResourcePermissions;
let aclSequence=0;
const grants=(name,value)=>({name,aclId:String(aclSequence++),editVersion:'acl-revision',permissions:Object.fromEntries(keys.map(key=>[key,value]))});
const group=(name,children)=>({name,userGroups:children,permissions:Object.fromEntries(keys.map(key=>{const granted=children.filter(child=>child.permissions[key]).length;return[key,{state:granted===0?'None':granted===children.length?'All':'Some',granted,total:children.length}];}))});
const samples=[group('Finance',[grants('Operators',true),grants('Auditors',false)]),group('Empty',[]),group('Operations',[grants('Operators',true)])];
assert.equal(filterGroups(samples,'auditors',{},false)[0].children.length,1);
assert.equal(filterGroups(samples,'Finance',{},false)[0].children.length,2);
assert.equal(filterGroups(samples,'auditors',{},false)[0].group.permissions.TaskRun.state,'Some');
assert.equal(filterGroups(samples,'',{TaskRun:'None'},false)[0].group.name,'Empty');
assert.equal(filterGroups(samples,'',{TaskRun:'All'},false)[0].group.name,'Operations');
assert.equal(filterGroups(samples,'',{},true)[0].group.name,'Operations');
assert.equal(flattenRows(filterGroups(samples,'',{},false),new Set(['Finance']),false).length,5);
assert.equal(flattenRows(filterGroups(samples,'Auditors',{},false),new Set(),true)[1].permissions.TaskRun,'Not granted');
assert.equal(filterGroups(samples,'123',{},false).length,0);
if(process.env.MAUI_PERMISSIONS_MODEL_ONLY==='1'){console.log('PASS Resource Group hierarchy search, permission filters, sorting and export models');process.exit(0);}
const source=read('Views/Resources/GroupsAndPermissions.cshtml');
const labels=['Tasks - Run','Tasks - Add/Edit/Delete','Tasks - Edit Existing','Hosts - Add/Edit/Delete','Scripts - Add/Edit/Delete','TLS Certs - Add/Edit/Delete','SSH Keys - Add/Edit/Delete','PGP Keys - Add/Edit/Delete'];
let view=source.slice(source.indexOf('<div class="page-heading'),source.indexOf('@section Scripts'));
view=view.replace(/@foreach \(var permission in permissions\) \{ ([\s\S]*?) \}/g,(_,body)=>keys.map((key,i)=>body.replaceAll('@permission.Key',key).replaceAll('@permission.Resource - @permission.Label',labels[i]).replaceAll('@permission.Resource',labels[i].split(' - ')[0]).replaceAll('@permission.Label',labels[i].split(' - ')[1])).join(''));
view=view.replace(/@foreach \(var size in Model.PageSizeOptions\) \{ ([\s\S]*?) \}/g,(_,body)=>[2,5,10].map(size=>body.replaceAll('@size',size)).join(''));
view=view.replace('<partial name="_ResourceGroupEditorDialogs" />',read('Views/Shared/_ResourceGroupEditorDialogs.cshtml')).replace('<partial name="_ResourceGroupUserGroupDialogs" />',read('Views/Shared/_ResourceGroupUserGroupDialogs.cshtml'));
view=view.replaceAll('@Model.DefaultPageSize','2').replace('@JsonSerializer.Serialize(Model.PageSizeOptions)','[2,5,10]')
.replace(/@Url.Action\("ResourceGroupPermissions", "Resources"\)/g,'/maui/inventory').replace(/@Url.Action\("SessionExpired", "Account"\)/g,'/maui/login')
.replace(/@Url.Action\("ResourceGroupEditor", "Resources"\)/g,'/maui/editor').replace(/@Url.Action\("ResolveResourceGroupMembers", "Resources"\)/g,'/maui/resolve').replace(/@Url.Action\("SaveResourceGroup", "Resources"\)/g,'/maui/save')
.replace(/@Url.Action\("ResourceGroupUserGroups", "Resources"\)/g,'/maui/user-groups').replace(/@Url.Action\("AddResourceGroupUserGroups", "Resources"\)/g,'/maui/add-user-groups').replace(/@Url.Action\("RemoveResourceGroupUserGroup", "Resources"\)/g,'/maui/remove-user-group');
view=view.replace(/@Url.Action\("SaveResourceGroupPermissions", "Resources"\)/g,'/maui/save-permissions').replace(/@Url.Action\("PreflightDeleteResourceGroup", "Resources"\)/g,'/maui/delete-preflight').replace(/@Url.Action\("DeleteResourceGroup", "Resources"\)/g,'/maui/delete-group');
// In CI use the actual compiled Razor response reached through MVC/DI/configuration and /maui routing.
if(process.env.MAUI_GROUPS_PAGE_FIXTURE_PATH){
 const runtime=JSON.parse(fs.readFileSync(process.env.MAUI_GROUPS_PAGE_FIXTURE_PATH,'utf8')).html;
 const start=runtime.indexOf('<div class="page-heading compact-heading">'),end=runtime.indexOf('</section>',start);
 assert.ok(start>=0&&end>start,'The runtime entry route must render the real grid');
 view=runtime.slice(start,end+'</section>'.length)+'</div>';
}
view=view.replace(/@\*[\s\S]*?\*@/g,'');
view=require('./render-razor-icons.cjs')(view,read('Views/Shared/_Icon.cshtml'));assert.ok(!view.includes('@'),'Unresolved Razor in grid fixture: '+view.match(/.{0,50}@.{0,60}/g));
const confirmMarkup=read('Views/Shared/_Layout.cshtml').match(/<dialog class="settings-dialog maui-confirm-dialog"[\s\S]*?<\/dialog>/)[0];
const inline=file=>'<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const html=`<!doctype html><html data-app-base-url="/maui/" data-theme="light" data-user-grid-page-size="2" data-preferences-grid-page-size-url="/maui/rows"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}\n${read('wwwroot/css/groups-and-permissions.css')}\n${read('wwwroot/css/script-editor.css')}body{padding:0}.app-main{margin-left:0}</style></head><body><div class="app-shell"><div class="app-main"><main class="content-area">${view}</main><footer class="app-footer"></footer></div></div>${confirmMarkup}<dialog data-user-preferences-dialog></dialog><form class="global-antiforgery" hidden><input name="__RequestVerificationToken" value="token"></form>
<script>
window.mauiAppUrl=path=>'/maui/'+path.replace(/^\\//,'');window.requests=[];window.errors=[];window.failRead=false;window.failSettings=false;window.failInitial=false;
window.addEventListener('error',event=>errors.push(event.message));window.addEventListener('unhandledrejection',event=>errors.push(String(event.reason)));
window.serverGroups=${JSON.stringify([...samples,group('=Untrusted',[grants('<img src=x onerror=alert(1)>',true)])])};

window.groupCatalog=[
 {memberType:'task',type:'Task',id:'t1',name:'First task',description:'Task one'},
 {memberType:'task',type:'Task',id:'t2',name:'Second task',description:'Task two'},
 {memberType:'host',type:'Host',id:'h1',name:'SFTP Host',description:'Shared host'},
 {memberType:'script',type:'Script',id:'s1',name:'Process script',description:'Script description'},
 {memberType:'ssh',type:'SSH Key',id:'k1',name:'Client SSH key',description:'SSH'},
 {memberType:'ssl',type:'TLS Certificate',id:'c1',name:'TLS certificate',description:'Certificate'},
 {memberType:'pgp',type:'PGP Key',id:'p1',name:'PGP key',description:'PGP'}
].map(item=>({...item,requiredBy:[]}));
window.groupSaved={name:'Finance',description:'Original description',editVersion:'revision',members:[]};
window.savedGroups={Finance:structuredClone(groupSaved)};
window.failGroupSave=false;window.failGroupResolve=false;window.groupSaveUnconfirmed=false;window.groupSaveStatus=0;
window.resolveGroup=members=>{
 const graph={'task|t1':['host|h1','script|s1','ssl|c1','pgp|p1'],'task|t2':['host|h1'],'host|h1':['ssh|k1']};const included=new Map();
 const visit=(key,parent)=>{let item=included.get(key);if(!item){item=structuredClone(groupCatalog.find(x=>x.memberType+'|'+x.id===key));included.set(key,item);}
 if(parent&&!item.requiredBy.includes(parent))item.requiredBy.push(parent);
 for(const child of graph[key]||[])visit(child,item.type+': '+item.name);};
 for(const member of members)visit(member.memberType+'|'+member.id);return [...included.values()];
};

window.userGroupsLocal=[{name:'Operators',description:'Already assigned'},{name:'Backup Operators',description:'Backup role'},{name:'Readers',description:'Read files'},{name:'Unconfirmed',description:'Retry guard'}];
window.userGroupsRemote=${JSON.stringify([{"name": "DOMAIN\\Readers", "description": "Domain role"}, {"name": "DOMAIN\\Other", "description": "Other domain role"}])};
window.failUserGroups=false;window.delayUserGroups=false;window.completeUserGroups=null;window.failAcl=false;window.uncertainAcl=false;window.delayAcl=false;window.completeAcl=null;window.partialAcl=false;
window.permissionFailure='';window.delayPermissions=false;window.completePermissions=null;window.delayInventory=true;window.completeInventory=null;window.expireInventory=false;
window.rebuildSummary=group=>{for(const key of ${JSON.stringify(keys)}){const granted=group.userGroups.filter(child=>child.permissions[key]).length;group.permissions[key]={state:granted===0?'None':granted===group.userGroups.length?'All':'Some',granted,total:group.userGroups.length};}};
window.fetch=async(url,options={})=>{const path=new URL(url,location.href).pathname;requests.push({path,method:options.method||'GET',url:String(url),body:options.body?JSON.parse(options.body):null});
const reply=(data,status=200)=>({ok:status===200,status,headers:new Headers({'content-type':'application/json'}),json:async()=>data});
if(path.endsWith('/inventory')||path.endsWith('/Resources/ResourceGroupPermissions')){const groups=structuredClone(serverGroups);if(delayInventory)await new Promise(resolve=>completeInventory=resolve);return expireInventory?reply({message:'Session expired'},401):failRead?reply({message:'Permissions unavailable'},500):reply({success:true,groups,refreshedUtc:new Date().toISOString()});}
if(path.endsWith('/save-permissions')||path.endsWith('/SaveResourceGroupPermissions')){
 if(delayPermissions)await new Promise(resolve=>completePermissions=resolve);
 if(permissionFailure==='reject')return reply({message:'Permissions rejected'},400);
 if(permissionFailure==='conflict')return reply({message:'Permissions changed in MOVEit',editConflict:true},409);
 if(permissionFailure==='network')throw new TypeError('Network unavailable');
 const body=JSON.parse(options.body),group=serverGroups.find(item=>item.name===body.resourceGroupName);
 const items=body.updates.map((item,index)=>{
  if(permissionFailure==='uncertain')return{aclId:item.aclId,name:item.userGroupName,status:index?'notAttempted':'unconfirmed',message:'Inspect current permissions.'};
  if(permissionFailure==='partial'&&index===0)return{aclId:item.aclId,name:item.userGroupName,status:'failed',message:'Save rejected for this group.'};
  const child=group.userGroups.find(child=>child.aclId===item.aclId);child.permissions=structuredClone(item.permissions);child.editVersion='updated-acl';
  return{aclId:item.aclId,name:item.userGroupName,status:'saved',message:'Permissions saved.'};
 });rebuildSummary(group);return reply({success:true,result:{items,saveUnconfirmed:permissionFailure==='uncertain'}});}
if(path.endsWith('/editor')||path.endsWith('/ResourceGroupEditor')){const name=new URL(url,location.href).searchParams.get('name');const saved=savedGroups[name];const editor=name?{...structuredClone(saved),items:resolveGroup(saved.members)}:{name:'',description:'',editVersion:'',members:[],items:[]};return reply({success:true,editor,availableItems:groupCatalog});}
if(path.endsWith('/resolve')||path.endsWith('/ResolveResourceGroupMembers'))return failGroupResolve?reply({message:'Dependencies unavailable'},400):reply({success:true,items:resolveGroup(JSON.parse(options.body).members)});
if(path.endsWith('/save')||path.endsWith('/SaveResourceGroup')){
 if(groupSaveStatus)return reply({message:'Proxy save error'},groupSaveStatus);
 if(groupSaveUnconfirmed)return reply({message:'Inspect the refreshed Group before retrying.',saveUnconfirmed:true},409);
 if(failGroupSave)return reply({message:'Save rejected for test'},400);groupSaved=JSON.parse(options.body);groupSaved.editVersion='next-revision';savedGroups[groupSaved.name]=structuredClone(groupSaved);
 if(!serverGroups.some(item=>item.name===groupSaved.name))serverGroups.push({name:groupSaved.name,userGroups:[],permissions:Object.fromEntries(${JSON.stringify(keys)}.map(key=>[key,{state:'None',granted:0,total:0}]))});return reply({success:true,editor:{...groupSaved,items:resolveGroup(groupSaved.members)}});}
if(path.endsWith('/user-groups')||path.endsWith('/ResourceGroupUserGroups')){
 if(delayUserGroups)await new Promise(resolve=>completeUserGroups=resolve);
 if(failUserGroups)return reply({message:'Directory unavailable'},500);
 const query=new URL(url,location.href).searchParams;const group=serverGroups.find(item=>item.name===query.get('resourceGroupName'));
 if(query.get('type')==='remote')await new Promise(resolve=>setTimeout(resolve,60));
 return reply({success:true,setup:{groups:query.get('type')==='remote'?userGroupsRemote:userGroupsLocal,assignedGroups:group.userGroups.map(item=>item.name)}});}
if(path.endsWith('/add-user-groups')||path.endsWith('/AddResourceGroupUserGroups')){
 if(delayAcl)await new Promise(resolve=>completeAcl=resolve);
 if(failAcl)return reply({message:'Assignment rejected'},400);
 const body=JSON.parse(options.body);const group=serverGroups.find(item=>item.name===body.resourceGroupName);
 if(uncertainAcl)return reply({success:true,result:{saveUnconfirmed:true,items:body.userGroups.map(item=>({name:item.name,status:'unconfirmed',message:'Inspect the grid.'}))}});
 const items=body.userGroups.map((item,index)=>{if(partialAcl&&index===0)return{name:item.name,status:'failed',message:'Could not assign this group'};group.userGroups.push({name:item.name,aclId:'20'+index,editVersion:'new-acl',permissions:Object.fromEntries(${JSON.stringify(keys)}.map(key=>[key,false]))});return{name:item.name,status:'added',message:'All permissions off'}});rebuildSummary(group);return reply({success:true,result:{items,saveUnconfirmed:false}});}
if(path.endsWith('/remove-user-group')||path.endsWith('/RemoveResourceGroupUserGroup')){
 const body=JSON.parse(options.body);if(failAcl)return reply({message:'Assignment changed',editConflict:true},409);
 const group=serverGroups.find(item=>item.name===body.resourceGroupName);group.userGroups=group.userGroups.filter(item=>item.name!==body.userGroupName);rebuildSummary(group);return reply({success:true,result:{items:[{name:body.userGroupName,status:'removed',message:'Removed'}]}});}
if(path.endsWith('/delete-preflight')||path.endsWith('/PreflightDeleteResourceGroup')){
 const name=new URL(url,location.href).searchParams.get('name');
 return reply({success:true,group:{name,description:'Group description',editVersion:'delete-version'}});}
if(path.endsWith('/delete-group')||path.endsWith('/DeleteResourceGroup')){
 if(window.delayDelete)await new Promise(resolve=>window.completeDelete=resolve);
 if(window.deleteFailure==='network')throw new TypeError('Network unavailable');
 if(window.deleteFailure)return reply({message:'Delete could not be confirmed',saveUnconfirmed:true},409);
 const body=JSON.parse(options.body);serverGroups=serverGroups.filter(group=>group.name!==body.name);return reply({success:true});}
if(path.endsWith('/rows'))return failSettings?reply({message:'Preference unavailable'},500):reply({gridPageSize:JSON.parse(options.body).value});
throw new Error('Unexpected endpoint '+path);};
window.csvBlob=null;URL.createObjectURL=blob=>{csvBlob=blob;return'blob:fixture';};URL.revokeObjectURL=()=>{};HTMLAnchorElement.prototype.click=function(){window.exportName=this.download;};
window.printCalls=0;window.open=()=>{const doc=document.implementation.createHTMLDocument('');window.printDocument=doc;return{document:doc,focus(){},print(){printCalls++},addEventListener(){},close(){}};};
window.beforeGridScripts=document.querySelector('.table-card').getBoundingClientRect().height;
</script>${inline('wwwroot/js/modal-ui.js')}${inline('wwwroot/js/site.js')}${inline('wwwroot/js/resource-ui.js')}${inline('wwwroot/js/grid-export.js')}${inline('wwwroot/js/grid-print.js')}${inline('wwwroot/js/grid-actions.js')}${inline('wwwroot/js/groups-and-permissions.js')}${inline('wwwroot/js/resource-group-editor.js')}${inline('wwwroot/js/resource-group-user-groups.js')}
<script>window.pick=name=>document.querySelector('[data-permissions-'+name+']');window.names=()=>[...pick('body').querySelectorAll('.permissions-expand')].map(x=>x.textContent.slice(1));window.input=value=>{pick('search').value=value;pick('search').dispatchEvent(new Event('input'));};window.setFilter=(key,value)=>{const s=document.querySelector('[data-permissions-filter="'+key+'"]');s.value=value;s.dispatchEvent(new Event('change'));};</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new vm.Script(script);
if(process.env.MAUI_VALIDATE_BROWSER_FIXTURE_ONLY==='1'){console.log('PASS Groups and Permissions model and browser fixture compilation');process.exit(0);}
assert.ok(executable,'Edge is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-permissions-layout-'));
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
    // Let shared theme/button transitions settle before capturing their final colors.
    await pause(200);
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
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1080,deviceScaleFactor:1,mobile:false});
    await command('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/maui/Resources/GroupsAndPermissions` });




    for(let i=0;i<100&&!await evaluate("Boolean(window.completeInventory)");i++)await pause(50);
    assert.equal(await evaluate("Math.abs(document.querySelector('.table-card').getBoundingClientRect().height-beforeGridScripts)<2"),true,'Initial script setup must preserve the pre-rendered grid height');
    await evaluate("window.loadingGridHeight=document.querySelector('.table-card').getBoundingClientRect().height;delayInventory=false;completeInventory();");
    for(let i=0;i<100&&!await evaluate("Boolean(window.pick)&&!pick('count').textContent.startsWith('Loading')");i++)await pause(50);
    assert.equal(await evaluate("Math.abs(document.querySelector('.table-card').getBoundingClientRect().height-loadingGridHeight)<2"),true,'Initial rows must not resize the grid');
    let checks=0;
    const eq=async(expression,expected)=>{assert.deepEqual(await evaluate(expression),expected,expression);checks++;};
    await eq('names()',['=Untrusted','Empty']);
    await eq("document.body.classList.contains('grid-fixed-page')",true);
    await evaluate("window.initialCardHeight=document.querySelector('.table-card').getBoundingClientRect().height");
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:400,deviceScaleFactor:1,mobile:false});await pause(100);
    await eq("document.body.classList.contains('grid-page-scroll-fallback')",true);
    await eq("document.documentElement.scrollHeight>innerHeight",true);
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1080,deviceScaleFactor:1,mobile:false});await pause(100);
    await eq("Math.abs(document.querySelector('.table-card').getBoundingClientRect().height-initialCardHeight)<2",true);

    await eq("pick('page').textContent",'1 / 2');
    await eq("pick('count').textContent",'4 of 4 resource groups');
    await eq("document.querySelectorAll('th button.sort-button').length",1);
    await evaluate("pick('refresh-clock').click();pick('refresh-toggle').click()");
    await eq("pick('refresh-toggle').textContent",'Resume Refresh');
    await eq("pick('refresh-clock').getAttribute('aria-expanded')",'false');
    await evaluate("pick('refresh-clock').click();pick('refresh').click()");await pause(100);
    await eq("pick('refresh-toggle').textContent",'Resume Refresh');
    await eq("pick('refresh-clock').getAttribute('aria-expanded')",'false');
    assert.match(await evaluate('requests.at(-1).url'),/forceRefresh=true/);checks++;
    await evaluate("pick('refresh-clock').click();pick('refresh-toggle').click()");await pause(100);
    await eq("pick('refresh-toggle').textContent",'Pause Refresh');

    await evaluate("pick('next').click();pick('body').querySelector('.permissions-expand').click()");
    await eq("names()",['Finance','Operations']);
    await eq("pick('body').querySelectorAll('.permissions-child').length",2);
    await eq("pick('body').querySelector('.permissions-summary').textContent",'Some');
    await eq("pick('body').querySelector('.permissions-summary').title",'1 of 2 assigned user groups');
    await evaluate("input('auditors')");await eq('names()',['Finance']);
    await eq("Math.abs(document.querySelector('.table-card').getBoundingClientRect().height-initialCardHeight)<2",true);
    await eq("pick('body').querySelectorAll('.permissions-child').length",1);
    await eq("pick('body').querySelector('.permissions-summary').textContent",'Some');
    await evaluate("pick('body').querySelector('.permissions-expand').click()");
    await eq("pick('search').value",'auditors');await eq("pick('body').querySelectorAll('.permissions-child').length",0);
    await evaluate("pick('body').querySelector('.permissions-expand').click();pick('export').click()");
    await eq("return await csvBlob.text()",'Resource Group / User Group,Tasks - Run,Tasks - Add/Edit/Delete,Tasks - Edit Existing,Hosts - Add/Edit/Delete,Scripts - Add/Edit/Delete,TLS Certs - Add/Edit/Delete,SSH Keys - Add/Edit/Delete,PGP Keys - Add/Edit/Delete\r\nFinance,Some,Some,Some,Some,Some,Some,Some,Some\r\nFinance / Auditors,Not granted,Not granted,Not granted,Not granted,Not granted,Not granted,Not granted,Not granted');
    await evaluate("pick('print').click()");await pause(350);
    await eq('printCalls',1);await eq("printDocument.querySelectorAll('tbody tr').length",2);
    await evaluate("pick('reset').click();setFilter('TaskRun','None')");await eq('names()',['Empty']);
    await evaluate("pick('body').querySelector('.permissions-expand').click()");await eq("pick('body').querySelector('.permissions-empty-child').textContent",'No assigned user groups.');
    await evaluate(`pick('body').querySelector('.permissions-row-menu').click()`);
    await eq(`document.querySelector('[data-permissions-command="edit-permissions"]').disabled`,true);
    await evaluate(`document.querySelector('[data-permissions-command="edit-permissions"]').click();document.body.click();document.querySelector('[data-permissions-grid]').dispatchEvent(new CustomEvent('maui:edit-permissions',{detail:{name:'Empty'}}))`);
    await eq(`document.querySelector('[data-permissions-edit-save]')`,null);
    await evaluate("pick('reset').click();input('Finance');failRead=true;pick('refresh').click()");await pause(100);
    await eq('names()',['Finance']);await eq("pick('error').hidden",false);
    assert.match(await evaluate("pick('error').textContent"),/last successful view/);checks++;
    await evaluate("failRead=false;pick('refresh').click()");await pause(100);
    await eq("pick('error').hidden",true);await eq("pick('search').value",'Finance');
    await eq("pick('body').querySelectorAll('.permissions-child').length",2);
    await evaluate("pick('settings').click();failSettings=true;pick('page-size').value='5';pick('settings-form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))");await pause(100);
    await eq("pick('settings-dialog').open",true);await eq("pick('settings-error').hidden",false);
    await eq("pick('error').hidden",true);
    await screenshot('Settings-error');
    await evaluate("failSettings=false;pick('settings-form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))");await pause(100);
    await eq("pick('settings-dialog').open",false);await eq('document.documentElement.dataset.userGridPageSize','5');
    await evaluate("pick('reset').click();pick('sort').click()");await eq('names()',['Operations','Finance','Empty','=Untrusted']);
    await evaluate("pick('export').click()");
    assert.match(await evaluate('return await csvBlob.text()'),/\r\n'=Untrusted,/);checks++;
    await eq("pick('body').querySelectorAll('img').length",0);
    await evaluate("pick('body').querySelector('.permissions-expand').click();pick('body').querySelector('.permissions-row-menu').click()");
    await eq("document.querySelector('.permissions-row-actions').hidden",false);await eq("document.querySelector('[data-permissions-command=add-user-groups]').disabled",false);await eq("[...document.querySelectorAll('.permissions-row-actions button')].filter(x=>!x.hidden&&!x.dataset.permissionsCommand).every(x=>x.disabled)",true);
    await evaluate("document.querySelector('[data-menu-button]').click()");
    await eq("document.querySelector('.permissions-row-actions').hidden",true);
    await evaluate("pick('body').querySelector('.permissions-row-menu').click()");
    await eq("document.querySelector('[data-menu-button]').getAttribute('aria-expanded')",'false');
    await evaluate("document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");
    await eq("document.querySelector('.permissions-row-actions').hidden",true);
    await evaluate("document.querySelector('[data-menu-button]').click()");await eq("document.querySelector('[data-menu-button]').getAttribute('aria-expanded')",'true');
    await evaluate("document.body.click()");await eq("document.querySelector('[data-menu-button]').getAttribute('aria-expanded')",'false');
    await evaluate("pick('reset').click();input('Finance')");
    for(const theme of ['light','dark'])for(const width of [1920,1400,1024,800,600]){
        await command('Emulation.setDeviceMetricsOverride',{width,height:1080,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
        await eq("document.documentElement.scrollWidth<=Math.max(innerWidth,960)",true);
        await eq("document.querySelector('.permissions-scroll').scrollWidth>=document.querySelector('.permissions-scroll').clientWidth",true);
        await screenshot('Grid-'+theme+'-'+width);
    }
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1080,deviceScaleFactor:1,mobile:false});
    // Inline permission editing uses the production grid, with the real dependent checkboxes and save lifecycle.
    await evaluate(`window.permissionSnapshot=structuredClone(serverGroups);window.pcheck=(user,key)=>[...pick('body').querySelectorAll('[data-permissions-key]')].find(box=>box.getAttribute('aria-label').startsWith(user+':')&&box.dataset.permissionsKey===key);window.openPermissions=()=>{pick('body').querySelector('.permissions-row-menu').click();document.querySelector('[data-permissions-command="edit-permissions"]').click();};window.cancelPermissions=()=>document.querySelector('[data-permissions-edit-cancel]').click();window.savePermissions=()=>document.querySelector('[data-permissions-edit-save]').click();pick('body').querySelector('.permissions-expand').click();openPermissions()`);
    await eq("pick('body').querySelectorAll('.permissions-edit-checkbox').length",16);
    await eq("document.querySelector('[data-permissions-edit-save]').disabled",true);
    await eq("pick('search').disabled",true);await eq("pick('rows').disabled",true);
    await eq("pcheck('Operators','TaskEditExisting').disabled",true);
    await evaluate(`pcheck('Auditors','TaskAddEditDelete').click()`);
    await eq("pcheck('Auditors','TaskEditExisting').checked",true);await eq("pcheck('Auditors','TaskEditExisting').disabled",true);
    await eq("pcheck('Auditors','TaskRun').checked",false);
    await evaluate(`pcheck('Auditors','TaskAddEditDelete').click()`);await eq("pcheck('Auditors','TaskEditExisting').disabled",false);
    await evaluate(`pcheck('Auditors','TaskEditExisting').click()`);await eq("document.querySelector('[data-permissions-edit-save]').disabled",true);
    await evaluate(`pcheck('Auditors','TaskEditExisting').click();delayPermissions=true;savePermissions()`);await pause(50);
    await eq("document.querySelector('[data-permissions-edit-save]').textContent",'Saving…');
    await eq("document.querySelector('[data-permissions-edit-cancel]').disabled",true);await eq("pcheck('Auditors','TaskRun').disabled",true);
    await evaluate(`savePermissions();completePermissions();delayPermissions=false`);await pause(150);
    await eq("document.querySelectorAll('.permissions-edit-checkbox').length",0);
    await eq("requests.filter(x=>x.path.endsWith('/save-permissions')||x.path.endsWith('/SaveResourceGroupPermissions')).length",1);
    await eq("requests.filter(x=>x.path.endsWith('/save-permissions')||x.path.endsWith('/SaveResourceGroupPermissions')).at(-1).body.updates.map(item=>item.userGroupName)",['Auditors']);
    await eq("serverGroups.find(g=>g.name==='Finance').userGroups.find(c=>c.name==='Auditors').permissions.TaskEditExisting",true);
    await evaluate(`openPermissions();pcheck('Auditors','HostAddEditDelete').click();cancelPermissions()`);await pause(120);
    await eq("requests.filter(x=>x.path.endsWith('/save-permissions')||x.path.endsWith('/SaveResourceGroupPermissions')).length",1);
    // A read already in flight cannot replace the draft or its original revision.
    await evaluate(`delayInventory=true;pick('refresh').click();openPermissions();pcheck('Auditors','HostAddEditDelete').click();completeInventory();delayInventory=false`);await pause(100);
    await eq("pcheck('Auditors','HostAddEditDelete').checked",true);await eq("pick('search').disabled",true);
    for(const theme of ['light','dark']){
      await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
      for(const width of [1400,800,600]){
        await command('Emulation.setDeviceMetricsOverride',{width,height:1080,deviceScaleFactor:1,mobile:false});
        await eq("document.querySelector('[data-permissions-edit-save]').getBoundingClientRect().right<=innerWidth",true);
        await screenshot('Edit-Permissions-'+theme+'-'+width);
      }
    }
    await evaluate(`cancelPermissions()`);await pause(100);
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate(`input('Auditors');openPermissions()`);await eq("pick('body').querySelectorAll('.permissions-child').length",2);
    await evaluate(`cancelPermissions()`);await pause(100);await eq("pick('body').querySelectorAll('.permissions-child').length",1);
    await evaluate(`input('Finance');openPermissions();pcheck('Auditors','HostAddEditDelete').click();permissionFailure='reject';savePermissions()`);await pause(100);
    await eq("document.querySelector('[data-permissions-edit-save]').disabled",false);
    await eq("document.querySelector('.permissions-edit-status').textContent",'Permissions rejected');await eq("pick('error').hidden",true);
    await evaluate(`permissionFailure='';savePermissions()`);await pause(100);await eq("document.querySelectorAll('.permissions-edit-checkbox').length",0);
    await evaluate(`openPermissions();pcheck('Operators','HostAddEditDelete').click();pcheck('Auditors','HostAddEditDelete').click();permissionFailure='partial';savePermissions()`);await pause(100);
    await eq("document.querySelector('[data-permissions-edit-save]').disabled",false);
    await eq("pcheck('Auditors','HostAddEditDelete').disabled",true);await eq("pcheck('Operators','HostAddEditDelete').disabled",false);
    await evaluate(`permissionFailure='';savePermissions()`);await pause(120);
    await eq("requests.filter(x=>x.path.endsWith('/save-permissions')||x.path.endsWith('/SaveResourceGroupPermissions')).at(-1).body.updates.map(item=>item.userGroupName)",['Operators']);
    for(const failure of ['conflict','uncertain','network']){
      await evaluate(`openPermissions();pcheck('Auditors','TaskRun').click();permissionFailure=${JSON.stringify(failure)};savePermissions()`);await pause(100);
      await eq("document.querySelector('[data-permissions-edit-save]').disabled",true);await eq("pcheck('Auditors','TaskRun').disabled",true);
      await eq("document.querySelector('.permissions-edit-status').classList.contains('is-error')",true);
      await eq("document.querySelector('.permissions-edit-status').textContent.includes('Cancel editing')",true);
      await evaluate(`permissionFailure='';cancelPermissions()`);await pause(100);
    }
    // A response begun before editing is also stale if it arrives after a successful save closes edit mode.
    await evaluate(`delayInventory=true;pick('refresh').click();openPermissions();pcheck('Auditors','PGPAddEditDelete').click();savePermissions()`);await pause(100);
    await eq("document.querySelectorAll('.permissions-edit-checkbox').length",0);
    await evaluate(`window.readonlyBeforeLateRead=pick('body').innerHTML;completeInventory();delayInventory=false`);await pause(50);
    await eq("pick('body').innerHTML===readonlyBeforeLateRead",true);
    await pause(350);
    await evaluate(`delayInventory=true;pick('refresh').click();openPermissions();pcheck('Auditors','TaskRun').click();expireInventory=true;completeInventory();delayInventory=false`);await pause(100);
    await eq("pcheck('Auditors','TaskRun').checked",true);await eq("location.pathname",'/maui/Resources/GroupsAndPermissions');
    await evaluate(`expireInventory=false;cancelPermissions()`);await pause(120);
    await eq("pick('search').disabled",false);await eq("pick('rows').disabled",false);
    await evaluate(`serverGroups=structuredClone(permissionSnapshot);pick('refresh').click()`);await pause(120);
    await evaluate("document.documentElement.dataset.theme='light';pick('new').click()");await pause(100);
    await eq("document.querySelector('[data-group-editor]').open",true);
    await eq("document.querySelector('[data-group-title]').textContent",'New Resource Group');
    await evaluate("window.g=name=>name==='editor'?document.querySelector('[data-group-editor]'):name==='picker'?document.querySelector('[data-group-picker]'):(name.startsWith('picker-')?document.querySelector('[data-group-picker]'):document.querySelector('[data-group-editor]')).querySelector('[data-group-'+name+']');g('name').value='New Group';g('name').dispatchEvent(new Event('input'))");
    await eq("document.querySelector('input[data-group-name]').value",'New Group');
    async function add(type,id){
        await evaluate(`document.querySelector('[data-group-tab="${type}"]').click();g('add').click()`);
        await eq("g('picker').open",true);
        await evaluate(`const box=[...g('picker-items').querySelectorAll('input')].find(x=>x.getAttribute('aria-label').includes('(ID ${id})'));box.click()`);
        await eq("document.activeElement.dataset.groupPickerKey",type+'|'+id);
        await evaluate(`g('picker-form').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}))`);await pause(100);
        await eq("g('picker').open",false);
    }
    await add('task','t1');await add('task','t2');
    await evaluate("document.querySelector('[data-group-tab=host]').click()");
    await eq("g('items').querySelectorAll('button').length",1);
    await eq("g('items').querySelector('button').disabled",true);
    await evaluate("document.querySelector('[data-group-tab=task]').click();g('items').querySelector('button').click()");await pause(100);
    await evaluate("document.querySelector('[data-group-tab=host]').click()");await eq("g('items').querySelector('button').disabled",true);
    await evaluate("document.querySelector('[data-group-tab=task]').click();g('items').querySelector('button').click()");await pause(100);
    await evaluate("document.querySelector('[data-group-tab=host]').click()");await eq("g('items').querySelectorAll('button').length",0);
    await add('host','h1');await add('task','t2');
    await evaluate("g('items').querySelector('button').click()");await pause(100);
    await evaluate("document.querySelector('[data-group-tab=host]').click()");await eq("g('items').querySelectorAll('button').length",0);
    await add('host','h1');await add('script','s1');await add('ssl','c1');await add('pgp','p1');
    await evaluate("document.querySelector('[data-group-tab=ssh]').click()");await eq("g('items').querySelector('button').disabled",true);
    await evaluate("document.querySelector('[data-group-tab=task]').click();g('add').click()");
    await evaluate("g('picker-search').value='Task one';g('picker-search').dispatchEvent(new Event('input'))");
    await eq("g('picker-items').querySelectorAll('input').length",1);
    await screenshot('Add-Tasks-light');
    await evaluate("g('picker-all').click()");await eq("g('picker-count').textContent",'1 selected');
    await evaluate("g('picker').querySelector('[data-group-picker-close]').click()");await eq("g('items').querySelectorAll('button').length",0);
    await evaluate("failGroupSave=true;g('form').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}))");await pause(100);
    await eq("g('editor').open",true);await eq("g('error').hidden",false);await eq("pick('error').hidden",true);
    await screenshot('Editor-save-error');
    await evaluate("failGroupSave=false;g('form').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}))");await pause(150);
    await eq("g('editor').open",false);await eq("groupSaved.name",'New Group');await eq("requests.filter(x=>x.path.endsWith('SaveResourceGroup')||x.path.endsWith('/save')).at(-1).body.members.length",4);
    await evaluate("input('Finance');pick('body').querySelector('.permissions-row-menu').click();document.querySelector('.permissions-row-actions button').click()");await pause(100);
    await eq("g('title').textContent",'Edit Resource Group');
    await eq("document.querySelector('input[data-group-name]').value",'Finance');
    await evaluate("g('description').value='Changed description';g('description').dispatchEvent(new Event('input'));document.querySelector('[data-group-tab=host]').click()");
    await screenshot('Editor-light-1400');
    await evaluate("document.documentElement.dataset.theme='dark'");await screenshot('Editor-dark-1400');
    await evaluate("g('editor').querySelector('[data-group-close]').click()");await pause(50);
    await eq("document.querySelector('[data-maui-confirm-dialog]').open",true);
    await evaluate("document.querySelector('[data-maui-confirm-cancel]').click()");await eq("g('editor').open",true);
    await evaluate("groupSaveUnconfirmed=true;g('form').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}))");await pause(100);
    await eq("g('save').disabled",true);await eq("g('error').hidden",false);
    await evaluate("g('editor').querySelector('[data-group-close]').click()");await pause(50);
    await evaluate("document.querySelector('[data-maui-confirm-accept]').click()");await pause(100);
    await eq("g('editor').open",false);
    // Known proxy/session errors and unknown save outcomes use the same blocked-retry behavior as ACL edits.
    for(const status of [408,502,401,403]){
      await evaluate(`groupSaveUnconfirmed=false;groupSaveStatus=${status};input('Finance');pick('body').querySelector('.permissions-row-menu').click();document.querySelector('[data-permissions-command="edit-resource-group"]').click()`);await pause(100);
      await evaluate(`g('description').value='Unsaved draft';g('description').dispatchEvent(new Event('input'));g('save').click()`);await pause(100);
      await eq("g('save').disabled",true);await eq("g('error').hidden",false);await eq("pick('error').hidden",true);
      await evaluate(`window.groupBlockedMessage=g('error').textContent;g('description').value+=' still editing';g('description').dispatchEvent(new Event('input'))`);
      await eq("g('save').disabled",true);
      if(status===502){
        await add('host','h1');
        await eq("g('member-count').textContent",'1 Host');
        await eq("g('error').textContent",await evaluate("groupBlockedMessage"));await eq("g('save').disabled",true);
        await screenshot('Group-save-blocked');
      }
      await evaluate(`g('editor').querySelector('[data-group-close]').click()`);await pause(30);
      await evaluate(`document.querySelector('[data-maui-confirm-accept]').click();groupSaveStatus=0`);await pause(100);
    }
    // Background refresh expiry must not navigate away from a modal draft.
    await evaluate(`delayInventory=true;pick('refresh').click();pick('new').click()`);await pause(100);
    await evaluate(`g('name').value='Keep this draft';g('name').dispatchEvent(new Event('input'));expireInventory=true;completeInventory();delayInventory=false`);await pause(100);
    await eq("g('editor').open",true);await eq("g('name').value",'Keep this draft');await eq("location.pathname",'/maui/Resources/GroupsAndPermissions');
    await evaluate(`expireInventory=false;g('editor').querySelector('[data-group-close]').click()`);await pause(30);
    await evaluate(`document.querySelector('[data-maui-confirm-accept]').click()`);await pause(100);
    // Real grid pager and User Group ACL modal workflows, including domain selection and safe retry states.
    await evaluate(`window.u=name=>document.querySelector('[data-user-group-'+name+']');window.rm=name=>document.querySelector('[data-user-group-remove-'+name+']');window.openAdd=()=>{input('Finance');pick('body').querySelector('.permissions-row-menu').click();document.querySelector('[data-permissions-command="add-user-groups"]').click();};document.documentElement.dataset.theme='light';openAdd()`);await pause(100);
    await eq("u('dialog').open",true);await eq("u('save').disabled",true);
    // Slow directory reads retain the final form geometry and show one table-local loading status.
    await evaluate(`window.userGroupLayout=()=>[u('form'),u('type'),u('search'),u('reload'),document.querySelector('.resource-user-group-table')].map(element=>{const r=element.getBoundingClientRect();return [r.x,r.y,r.width,r.height];});`);
    for(const theme of ['light','dark'])for(const width of [1400,800,600]){
      await command('Emulation.setDeviceMetricsOverride',{width,height:1080,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)};delayUserGroups=true;completeUserGroups=null;u('reload').click()`);await pause(60);
      await eq("u('dialog').querySelectorAll('[data-user-group-loading]').length",1);
      await eq("u('items').contains(u('loading'))&&Boolean(u('loading').querySelector('.inline-spinner'))",true);
      await evaluate(`window.loadingUserGroupLayout=userGroupLayout()`);
      await screenshot('Add-User-Groups-loading-'+theme+'-'+width);
      await evaluate(`delayUserGroups=false;completeUserGroups()`);await pause(120);
      await eq("u('loading')===null&&userGroupLayout().every((rect,index)=>rect.every((value,axis)=>Math.abs(value-loadingUserGroupLayout[index][axis])<1))",true);
    }
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate(`document.documentElement.dataset.theme='light'`);
    await eq("u('items').querySelector('input[value=Operators]').disabled",true);
    await evaluate(`u('search').value='Backup';u('search').dispatchEvent(new Event('input'));u('all').click()`);
    await eq("u('count').textContent",'1 selected');await eq("u('save').disabled",false);
    await evaluate(`u('type').value='remote';u('type').dispatchEvent(new Event('change'))`);await pause(100);
    await eq("u('count').textContent",'1 selected');
    await evaluate(`u('items').querySelector('input').click()`);await eq("u('count').textContent",'2 selected');
    await screenshot('Add-User-Groups-light');
    await evaluate(`document.documentElement.dataset.theme='dark'`);await screenshot('Add-User-Groups-dark');
    await evaluate(`failAcl=true;u('form').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}))`);await pause(100);
    await eq("u('dialog').open",true);await eq("u('error').hidden",false);await eq("u('save').disabled",false);await eq("pick('error').hidden",true);
    await evaluate(`failAcl=false;delayAcl=true;u('save').click()`);await pause(50);
    await eq("u('save').textContent",'Saving…');await eq("u('type').disabled",true);await eq("u('save').disabled",true);
    await evaluate(`completeAcl();delayAcl=false`);await pause(150);
    await eq("u('dialog').open",false);
    await eq("requests.filter(x=>x.path.endsWith('/add-user-groups')||x.path.endsWith('/AddResourceGroupUserGroups')).at(-1).body.userGroups.map(item=>item.type)",['local','remote']);
    await eq("pick('body').querySelectorAll('.permissions-child').length",4);
    await evaluate(`window.removeButton=[...pick('body').querySelectorAll('[data-permissions-user-group-name]')].find(button=>button.dataset.permissionsUserGroupName==='Backup Operators');removeButton.click()`);
    await eq("[...document.querySelectorAll('.permissions-row-actions button')].filter(button=>!button.hidden).map(button=>button.textContent)",['Remove User Group']);
    await evaluate(`document.querySelector('[data-permissions-command="remove-user-group"]').click()`);await eq("rm('dialog').open",true);await eq("rm('name').value",'Backup Operators');
    for(const theme of ['light','dark'])for(const width of [1400,800,600]){
      await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
      await command('Emulation.setDeviceMetricsOverride',{width,height:800,deviceScaleFactor:1,mobile:false});
      await eq("Math.abs(rm('form').getBoundingClientRect().left+rm('form').getBoundingClientRect().width/2-innerWidth/2)<=2",true);
      await eq("Math.abs(rm('form').getBoundingClientRect().top+rm('form').getBoundingClientRect().height/2-innerHeight/2)<=2",true);
      await eq("rm('form').getBoundingClientRect().bottom<=innerHeight",true);
      await screenshot('Remove-User-Group-'+theme+'-'+width);
    }
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate(`rm('dialog').querySelector('[data-user-group-remove-close]').click()`);await eq("rm('dialog').open",false);
    await eq("requests.filter(x=>x.path.endsWith('RemoveResourceGroupUserGroup')||x.path.endsWith('/remove-user-group')).length",0);
    await evaluate(`removeButton.click();document.querySelector('[data-permissions-command="remove-user-group"]').click();rm('confirm').click()`);await pause(150);
    await eq("rm('dialog').open",false);await eq("pick('body').querySelectorAll('.permissions-child').length",3);
    await eq("requests.filter(x=>x.path.endsWith('RemoveResourceGroupUserGroup')||x.path.endsWith('/remove-user-group')).at(-1).body.editVersion",'new-acl');
    await evaluate(`openAdd()`);await pause(100);
    await evaluate(`[...u('items').querySelectorAll('input')].find(input=>input.value==='Backup Operators').click();u('items').querySelector('input[value=Readers]').click();partialAcl=true;u('save').click()`);await pause(120);
    await eq("u('dialog').open",true);await eq("u('count').textContent",'1 selected');await eq("u('results').children.length",2);await eq("u('save').disabled",false);
    await evaluate(`partialAcl=false;u('save').click()`);await pause(150);
    await eq("u('dialog').open",false);await eq("requests.filter(x=>x.path.endsWith('/add-user-groups')||x.path.endsWith('/AddResourceGroupUserGroups')).at(-1).body.userGroups.map(item=>item.name)",['Backup Operators']);
    await evaluate(`[...pick('body').querySelectorAll('[data-permissions-user-group-name]')].find(button=>button.dataset.permissionsUserGroupName==='Operators').click();document.querySelector('[data-permissions-command="remove-user-group"]').click();failAcl=true;rm('confirm').click()`);await pause(100);
    await eq("rm('dialog').open",true);await eq("rm('error').hidden",false);await eq("rm('confirm').disabled",true);
    await evaluate(`failAcl=false;rm('dialog').querySelector('[data-user-group-remove-close]').click()`);await pause(100);
    await evaluate(`openAdd()`);await pause(100);
    await evaluate(`u('items').querySelector('input[value=Unconfirmed]').click();uncertainAcl=true;u('save').click()`);await pause(120);
    await eq("u('dialog').open",true);await eq("u('save').disabled",true);await eq("u('error').hidden",false);
    await evaluate(`u('items').querySelector('input[value=Unconfirmed]').click();u('items').querySelector('input[value=Unconfirmed]').click()`);await eq("u('save').disabled",true);
    await evaluate(`u('reload').click()`);await pause(100);
    await eq("u('save').disabled",true);await eq("u('error').hidden",false);
    await evaluate(`u('type').value='remote';u('type').dispatchEvent(new Event('change'))`);await pause(100);
    await eq("u('save').disabled",true);await eq("u('error').hidden",false);
    await screenshot('User-Group-save-blocked');
    await evaluate(`u('dialog').querySelector('[data-user-group-close]').click();uncertainAcl=false`);await pause(100);
    await evaluate(`failUserGroups=true;openAdd()`);await pause(100);await eq("u('error').hidden",false);await eq("u('save').disabled",true);
    await evaluate(`failUserGroups=false;u('reload').click()`);await pause(100);await eq("u('error').hidden",true);
    await evaluate(`u('type').value='remote';u('type').dispatchEvent(new Event('change'));u('type').value='local';u('type').dispatchEvent(new Event('change'))`);await pause(100);
    await eq("u('items').querySelector('input').value",'Operators');
    await evaluate(`userGroupsRemote=Array.from({length:2172},(_,index)=>({name:'DOMAIN\\\\Group'+index,description:'Directory team '+index}));u('type').value='remote';u('type').dispatchEvent(new Event('change'))`);await pause(250);
    await eq("u('items').querySelectorAll('input').length",2172);
    await evaluate(`u('search').value='team 2171';u('search').dispatchEvent(new Event('input'));u('all').click()`);
    await eq("u('items').querySelectorAll('input').length",1);await eq("u('count').textContent",'1 selected');
    for(const width of [1400,800,600]){
      await command('Emulation.setDeviceMetricsOverride',{width,height:1080,deviceScaleFactor:1,mobile:false});
      await eq("u('dialog').getBoundingClientRect().width<=innerWidth",true);await eq("u('save').getBoundingClientRect().bottom<=innerHeight",true);
      await eq("Math.abs(u('form').getBoundingClientRect().bottom-u('dialog').querySelector('.new-task-footer').getBoundingClientRect().bottom)<=2",true);
      await eq("document.querySelector('.resource-user-group-table').getBoundingClientRect().height>=300",true);
      await eq("getComputedStyle(u('items').closest('table')).fontFamily",await evaluate("getComputedStyle(document.body).fontFamily"));
      if(width===600)await eq("Math.abs(u('reload').getBoundingClientRect().bottom-u('type').getBoundingClientRect().bottom)<=2",true);
      await screenshot('Add-User-Groups-'+width);
    }
    await evaluate(`u('dialog').querySelector('[data-user-group-close]').click();pick('reset').click();pick('rows').value='2';pick('rows').dispatchEvent(new Event('change'))`);await pause(120);
    await eq("pick('page').textContent",'1 / 3');await eq("pick('summary').textContent",'Showing 1–2 of 5');
    await eq("pick('rows').value",'2');
    await evaluate(`failSettings=true;pick('rows').value='5';pick('rows').dispatchEvent(new Event('change'))`);await pause(100);
    await eq("pick('rows').value",'2');await eq("pick('error').hidden",false);await evaluate(`failSettings=false`);
    await evaluate(`pick('reset').click();input('Empty');window.d=name=>document.querySelector('[data-group-delete-'+name+']');window.openDelete=()=>{pick('body').querySelector('.permissions-row-menu').click();document.querySelector('[data-permissions-command="delete-resource-group"]').click();};openDelete()`);await pause(100);
    await eq("d('dialog').open",true);await eq("d('name').value",'Empty');await eq("d('confirm').disabled",false);
    await eq("Math.abs(d('dialog').getBoundingClientRect().x+d('dialog').getBoundingClientRect().width/2-innerWidth/2)<2",true);
    await eq("Math.abs(d('form').getBoundingClientRect().x+d('form').getBoundingClientRect().width/2-innerWidth/2)<2",true);
    await eq("d('confirm').getBoundingClientRect().bottom<=innerHeight",true);
    await screenshot('Delete-Resource-Group');
    await evaluate(`d('dialog').querySelector('[data-group-delete-close]').click()`);
    await eq("serverGroups.some(group=>group.name==='Empty')",true);
    await eq("requests.filter(x=>x.path.endsWith('/delete-group')||x.path.endsWith('/DeleteResourceGroup')).length",0);
    await evaluate(`openDelete()`);await pause(100);await evaluate(`delayDelete=true;d('confirm').click()`);await pause(60);
    await eq("d('confirm').disabled",true);await eq("d('confirm').textContent",'Deleting…');
    await eq("[...d('dialog').querySelectorAll('[data-group-delete-close]')].every(button=>button.disabled)",true);
    await evaluate(`delayDelete=false;completeDelete()`);await pause(140);
    await eq("d('dialog').open",false);await eq("serverGroups.some(group=>group.name==='Empty')",false);await eq("pick('search').value",'Empty');
    await eq("requests.filter(x=>x.path.endsWith('/delete-group')||x.path.endsWith('/DeleteResourceGroup')).at(-1).body",{name:'Empty',editVersion:'delete-version'});
    for(const failure of ['conflict','network']){
      await evaluate(`input('Finance');deleteFailure=${JSON.stringify(failure)};openDelete()`);await pause(100);await evaluate(`d('confirm').click()`);await pause(100);
      await eq("d('dialog').open",true);await eq("d('error').hidden",false);await eq("d('confirm').disabled",true);
      await eq("serverGroups.some(group=>group.name==='Finance')",true);
      await screenshot('Delete-Resource-Group-'+failure);
      await evaluate(`d('dialog').querySelector('[data-group-delete-close]').click();deleteFailure=''`);await pause(100);
    }
    await evaluate("serverGroups=[];input('');pick('refresh').click()");await pause(100);
    await eq("pick('body').textContent",'No visible Resource Groups.');
    await eq("requests.filter(x=>x.path.endsWith('/inventory')||x.path.endsWith('/ResourceGroupPermissions')).every(x=>x.method==='GET')",true);
    await eq('errors',[]);
    const result={result:'PASS',checks,themes:['light','dark'],widths:[1920,1400,1024,800,600]};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Groups and Permissions workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({error:pick("error").textContent,errors,requests,beforeGridScripts,card:document.querySelector(".table-card").getBoundingClientRect().height,viewport:innerHeight,bodyClass:document.body.className})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});
