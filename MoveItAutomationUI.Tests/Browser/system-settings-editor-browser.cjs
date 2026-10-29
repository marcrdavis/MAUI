// Render the production modal and shared browser in Edge; HTTP is mocked, not MOVEit.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
const output=path.join(root,'TestResults/SystemSettings');fs.mkdirSync(output,{recursive:true});
const candidates=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium'];
const executable=candidates.find(file=>file&&fs.existsSync(file));
const icon=name=>read('Views/Shared/_Icon.cshtml').match(new RegExp('else if \\(icon == "'+name+'"\\) \\{ ([\\s\\S]*?) \\}'))[1];
const modal=read('Views/Shared/_SystemSettingsEditor.cshtml').replace(/@\*[\s\S]*?\*@/g,'').replace(/<partial name="_Icon" model="@\(\"([^\"]+)\"\)" \/>/g,(_match,name)=>icon(name)).replace(/@Url.Action\("Browse", "HostEditor", new \{ id = "0" \}\)/g,'/maui/HostEditor/Browse?id=0').replace(/@Url.Action\("(Shell|Save|Lockout|ActiveLockouts|Unlock)", "SystemSettingsEditor"\)/g,'/maui/SystemSettingsEditor/$1');
assert.ok(!/@Url|<partial/.test(modal));
const inline=file=>'<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const navigation=read('wwwroot/js/configuration.js').match(/    function syncSavedEditorNavigation\(payload\) \{[\s\S]*?\n    \}/)[0];
const html=String.raw`<!doctype html><html data-use-maui-system-settings-editor="true"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}
${read('wwwroot/css/script-editor.css')}
${read('wwwroot/css/system-settings-editor.css')}
${read('wwwroot/css/host-browser.css')}</style></head><body>
<a href="#native" data-system-settings-open>MOVEit Settings</a><a href="#native-lockouts" data-manage-lockouts-native data-native-editor-route="#/system/lockouts" hidden>Manage MOVEit Lockouts</a><form class="maui-editor-routing-antiforgery"><input name="__RequestVerificationToken" value="token" /></form>${modal}${read('Views/Shared/_HostBrowser.cshtml')}
<script>
window.errors=[];window.addEventListener('error',e=>errors.push(e.message));window.addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
window.requests=[];window.confirmed=true;window.confirms=[];window.mauiConfirm=async options=>{confirms.push(options);return confirmed;};window.fault='';window.pendingSave=null;window.pendingShell=null;window.nativeClicks=0;window.activeData={addresses:[{value:'192.168.198.1',stamp:'2026-10-22 08:25:49',editVersion:'ip1'}],users:[{value:'miuser',stamp:'2026-10-22 08:25:49',editVersion:'user1'}]};window.pendingActive=null;window.pendingUnlock=null;
window.saved={logging:{debugLevel:60,logToDisk:true,logFile:'MOVEitC.log',maxLogFileSize:10485760,logToTaskLog:true,taskLogDir:'',as2LogDirectory:'',numTaskLogsToKeep:5,auditXferRetries:true},eventLog:{eventLogName:'Application',eventLogLevel:0,eventLogAuditLevel:0},tasks:{maxSimulTasks:20,allowMultRuns:false,suppressQueuedTasksAtStartup:false,pollIntervalSecs:5,as2PollIntervalSecs:0},stateFile:{defStateCacheTime:-1,defStateCacheUnit:null},tamper:{enabled:true},editVersion:'r1'};
window.policyState={policy:{ipLockoutEnabled:false,ipLockoutTries:5,ipLockoutTime:5,ipLockoutUnits:'minutes',ipAutoUnlockEnabled:true,ipUnlockTime:15,ipUnlockUnits:'minutes',userLockoutEnabled:false,userLockoutTries:5,userLockoutTime:5,userLockoutUnits:'minutes',userAutoUnlockEnabled:true,userUnlockTime:15,userUnlockUnits:'minutes'},editVersion:'p1',trustedIps:['1.1.1.1'],trustedEditVersion:'t1'};
window.fetch=async(url,options)=>{
 const target=new URL(url,location.href),body=JSON.parse(options.body);requests.push({url:target.pathname,query:target.search,body,headers:options.headers,method:options.method});
 const reply=(data,status=200)=>({ok:status===200,status,json:async()=>structuredClone(data)});
 if(target.pathname.endsWith('/Browse'))return reply({currentDirectory:body.directory||'C:\\',parentDirectory:'',items:[{name:'Logs',path:'C:\\Logs',isDirectory:true},{name:'private.txt',path:'C:\\private.txt',isDirectory:false}]});
 if(target.pathname.endsWith('/ActiveLockouts')){
  if(fault==='activeLoad')return reply({message:'Lockouts could not be loaded.'},500);
  if(fault==='holdActive')await new Promise(resolve=>{pendingActive=resolve;});
  return reply({success:true,state:activeData});
 }
 if(target.pathname.endsWith('/Unlock')){
  if(fault==='unlockNetwork')throw new Error('Network interrupted');
  if(fault==='unlockConflict')return reply({message:'Selected lockouts changed in MOVEit. Refresh Lockouts and review the selection before unlocking.',refreshLockouts:true},409);
  if(fault==='unlockValidation')return reply({message:'Unlock rejected by MOVEit.'},400);
  if(fault==='holdUnlock')await new Promise(resolve=>{pendingUnlock=resolve;});
  const key=body.kind==='ip'?'addresses':'users',before=activeData[key].length;
  activeData[key]=activeData[key].filter(item=>!body.selections.some(selected=>selected.value===item.value));
  return reply({success:true,state:{kind:body.kind,items:activeData[key],unlocked:before-activeData[key].length,alreadyUnlocked:body.selections.length-before+activeData[key].length}});
 }
 if(target.pathname.endsWith('/Lockout')){if(fault==='policyLoad')return reply({message:'IP/User Lockout Policy could not be loaded.'},500);return reply({success:true,state:policyState});}
 if(target.pathname.endsWith('/Shell')){if(fault==='holdShell')await new Promise(resolve=>{pendingShell=resolve;});return reply({success:true,state:saved});}
 if(target.pathname.endsWith('/Save')){
  if(fault==='conflict')return reply({message:'MOVEit Settings changed in MOVEit. Reload MOVEit Settings before saving again.',saveBlocked:true},409);
  if(fault==='partial')return reply({message:'Some changes may have been saved. Reload MOVEit Settings and review the MOVEit copy before saving again.',saveBlocked:true},409);
  if(fault==='validation')return reply({message:'Invalid directory'},400);
  if(fault==='network')throw new Error('Connection dropped');
  if(fault==='hold')await new Promise(resolve=>{pendingSave=resolve;});
  for(const name of ['logging','eventLog','tasks','stateFile','tamper'])if(body[name])saved[name]=body[name];saved.editVersion='r2';
  if(body.lockout){policyState.policy=body.lockout;policyState.editVersion='p2';}
  if(body.trustedIps){policyState.trustedIps=body.trustedIps;policyState.trustedEditVersion='t2';}
  return reply({success:true,state:{...saved,lockout:body.lockout||body.trustedIps?policyState:null}});
 }
 throw new Error('Unexpected endpoint '+url);
};
window.pick=name=>document.querySelector('[data-system-'+name+']');window.field=name=>document.querySelector('[data-system-field="'+name+'"]');
window.set=(name,value)=>{const f=field(name);if(f.type==='checkbox')f.checked=value;else f.value=value;f.dispatchEvent(new Event('input',{bubbles:true}));};
window.lockoutPanel=kind=>document.querySelector('[data-system-lockouts-kind="'+kind+'"]');
window.submit=()=>pick('form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));
</script>${inline('wwwroot/js/modal-ui.js')}${inline('wwwroot/js/shared/folder-browser-sorting.js')}${inline('wwwroot/js/shared/host-browser.js')}${inline('wwwroot/js/editors/system-settings-lockouts.js')}${inline('wwwroot/js/editors/system-settings-editor.js')}
<script>${navigation};window.syncNavigation=syncSavedEditorNavigation;window.systemReady=true;document.addEventListener('click',event=>{if(event.target.closest('[data-system-settings-open]')){nativeClicks++;event.preventDefault();}});</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
if(process.argv.includes('--syntax-only')){console.log('MOVEit Settings inline browser fixture syntax passed');process.exit(0);}
assert.ok(executable,'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-system-settings-'));
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


    for(let i=0;i<100&&!await evaluate(`Boolean(window.systemReady)`);i++)await pause(40);
    assert.equal(await evaluate('Boolean(window.systemReady)'),true,'All production scripts are ready');
    let checks=0;
    const check=async(expression,expected,label)=>{assert.deepEqual(await evaluate(expression),expected,label);checks++;};
    await evaluate(`document.querySelector('[data-system-settings-open]').click()`);await pause(80);
    await check(`pick('status').textContent`,'Saved');
    await check(`pick('save').disabled`,true);
    await check(`parseFloat(getComputedStyle(pick('save').closest('footer')).paddingLeft)>0`,true,'Footer reuses standard modal padding');
    await check(`nativeClicks`,0,'MAUI routing suppresses native routing without a page/banner');
    await check(`field('logFile').value`,'MOVEitC.log');
    await check(`pick('size').value`,'10');
    await check(`pick('unit').value`,'1048576');
    await evaluate(`set('debugLevel','')`);
    await check(`pick('save').disabled`,true,'Missing log level cannot silently become zero');
    await evaluate(`set('debugLevel',60)`);
    await check(`requests[0].headers.RequestVerificationToken`,'token');
    await check(`requests[0].method`,'POST');
    await check(`document.querySelectorAll('[data-system-tab]').length`,7);
    await check(`getComputedStyle(pick('active-actions')).display`,'none','Lockouts refresh is hidden on settings tabs');
    for(const theme of ['light','dark'])for(const width of [1920,1000,800,600]){
      await command('Emulation.setDeviceMetricsOverride',{width,height:1080,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.documentElement.dataset.theme='${theme}'`);await pause(30);
      await check(`(()=>{const d=document.querySelector('[data-system-settings-editor]'),f=pick('save').getBoundingClientRect();return d.scrollWidth<=d.clientWidth+1&&d.getBoundingClientRect().width<=innerWidth&&f.bottom<=innerHeight&&f.top>=0;})()`,true,'Modal and footer fit viewport');
      await check(`(()=>{const tabs=[...document.querySelectorAll('[data-system-tab]')];return tabs.every(t=>{const r=t.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth;});})()`,true,'All seven tabs fit');
      if(width===1920)await screenshot(theme);
    }
    await evaluate(`document.querySelector('[data-system-tab="events"]').click()`);
    await check(`document.querySelector('[data-system-panel="logging"]').hidden`,true);
    await check(`document.querySelector('[data-system-panel="events"]').hidden`,false);
    await check(`[...document.querySelectorAll('[data-system-panel]')].every(panel=>(getComputedStyle(panel).display!=='none')===(panel.dataset.systemPanel==='events'))`,true,'Only the selected panel is visibly rendered');
    await evaluate(`document.querySelector('[data-system-tab="logging"]').click();set('logToDisk',false)`);
    await check(`field('logFile').disabled&&pick('size').disabled&&pick('unit').disabled`,true);
    await evaluate(`set('logToTaskLog',false)`);
    await check(`field('taskLogDir').disabled&&field('numTaskLogsToKeep').disabled`,true);
    await check(`field('as2LogDirectory').disabled`,false);
    await evaluate(`set('logToDisk',true);set('logToTaskLog',true);set('logFile','changed.log')`);
    await check(`pick('save').disabled`,false);
    await check(`pick('status').textContent`,'Unsaved changes');
    await evaluate(`pick('size').value='3';pick('unit').value='1073741824';pick('size').dispatchEvent(new Event('input',{bubbles:true}))`);
    await check(`pick('save').disabled`,true,'Greater than 2 GB rejected');
    await evaluate(`pick('size').value='2';pick('size').dispatchEvent(new Event('input',{bubbles:true}));fault='hold';submit()`);await pause(50);
    await check(`pick('save').textContent`,'Saving...');
    await check(`pick('save').disabled&&pick('close').disabled`,true);
    await evaluate(`pendingSave()`);await pause(70);
    await check(`saved.logging.maxLogFileSize`,2147483648);
    await check(`pick('save').textContent`,'Save');
    await check(`pick('save').disabled`,true);
    await check(`pick('status').textContent`,'Saved');
    await evaluate(`document.querySelector('[data-system-tab="events"]').click();set('eventLogName','MOVEit');set('eventLogLevel',40);set('eventLogAuditLevel',30);fault='';submit()`);await pause(70);
    await check(`saved.eventLog`,{eventLogName:'MOVEit',eventLogLevel:40,eventLogAuditLevel:30},'Event log values use captured levels');
    await check(`Object.keys(requests.at(-1).body).sort()`,['editVersion','eventLog'],'Only changed sections are submitted');
    await evaluate(`document.querySelector('[data-system-tab="tasks"]').click();set('as2PollIntervalSecs','')`);
    await check(`pick('save').disabled`,true,'Blank AS2 interval is invalid');
    await evaluate(`set('as2PollIntervalSecs',5);set('allowMultRuns',true);set('suppressQueuedTasksAtStartup',true);submit()`);await pause(70);
    await check(`saved.tasks.as2PollIntervalSecs`,5,'AS2 polling name survives browser serialization');
    await check(`saved.tasks.allowMultRuns&&saved.tasks.suppressQueuedTasksAtStartup`,true);
    await evaluate(`document.querySelector('[data-system-tab="state"]').click();document.querySelector('[data-system-cache-mode][value="timed"]').click();pick('cache-time').value='2';pick('cache-unit').value='Hours';pick('cache-time').dispatchEvent(new Event('input',{bubbles:true}));submit()`);await pause(70);
    await check(`saved.stateFile`,{defStateCacheTime:2,defStateCacheUnit:'Hours'});
    for(const [mode,value]of [['always',-1],['task',0]]){
      await evaluate(`document.querySelector('[data-system-cache-mode][value="${mode}"]').click();submit()`);await pause(70);
      await check(`saved.stateFile`,{defStateCacheTime:value,defStateCacheUnit:null},'Sentinel cache mode omits a duration unit');
      await check(`pick('cache-time').disabled&&pick('cache-unit').disabled`,true);
    }
    await evaluate(`document.querySelector('[data-system-tab="tamper"]').click();set('enabled',false);submit()`);await pause(70);
    await check(`saved.tamper.enabled`,false,'Tamper UI sends positive enabled flag');
    await check(`requests.filter(r=>r.url.endsWith('/Lockout')).length`,0,'Policy data is loaded only when its tab is opened');
    await evaluate(`document.querySelector('[data-system-tab="lockout"]').click()`);await pause(70);
    await check(`field('ipLockoutTries').disabled&&field('ipUnlockTime').disabled`,true,'Disabled policy disables its dependent inputs');
    await check(`document.querySelector('[data-system-trusted-address]').value`,'1.1.1.1');
    await evaluate(`set('ipLockoutEnabled',true)`);
    await check(`field('ipLockoutTries').disabled||field('ipUnlockTime').disabled`,false);
    await evaluate(`document.querySelector('[data-system-auto="ip"][value="never"]').click()`);
    await check(`field('ipUnlockTime').disabled`,true,'Never disables expiration input');
    await evaluate(`pick('trusted-add').click()`);
    await check(`pick('save').disabled`,true,'An empty added address is not submitted');
    await evaluate(`const input=[...document.querySelectorAll('[data-system-trusted-address]')].at(-1);input.value='2.2.2.2';input.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('.system-settings-trusted-row .danger-button').click();submit()`);await pause(70);
    await check(`policyState.trustedIps`,['2.2.2.2']);
    await check(`policyState.policy.ipLockoutEnabled&&!policyState.policy.ipAutoUnlockEnabled`,true);
    await check(`requests.filter(r=>r.url.endsWith('/Save')).at(-1).body.trustedVersion`,'t1');
    await check(`pick('status').textContent`,'Saved');
    await evaluate(`pick('trusted-add').click();const input=[...document.querySelectorAll('[data-system-trusted-address]')].at(-1);input.value='2.2.2.2';input.dispatchEvent(new Event('input',{bubbles:true}))`);
    await check(`pick('save').disabled`,true,'Duplicate IP addresses are rejected');
    await evaluate(`[...document.querySelectorAll('.system-settings-trusted-row .danger-button')].at(-1).click()`);
    await check(`pick('save').disabled`,true,'Removing the unused row returns to Saved');
    for(const theme of ['light','dark']){
      await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.documentElement.dataset.theme='${theme}'`);
      for(const name of ['events','tasks','state','tamper','lockout']){
        await evaluate(`document.querySelector('[data-system-tab="${name}"]').click()`);await pause(30);await screenshot(theme+'-'+name);
        await check(`[...document.querySelectorAll('[data-system-panel]')].every(panel=>(getComputedStyle(panel).display!=='none')===(panel.dataset.systemPanel==='${name}'))`,true,'Inactive panels stay hidden after policy is loaded');
        await check(`getComputedStyle(document.querySelector('[data-system-panel="${name}"] fieldset')).backgroundColor===getComputedStyle(pick('save').closest('footer')).backgroundColor`,true,'Panels share the Information grey surface');
      }
    }
    for(const width of [1000,800,600]){
      await command('Emulation.setDeviceMetricsOverride',{width,height:1080,deviceScaleFactor:1,mobile:false});
      await check(`(()=>{const body=pick('body');return body.scrollWidth<=body.clientWidth+1&&pick('save').getBoundingClientRect().bottom<=innerHeight;})()`,true,'Lockout controls wrap within the fixed modal');
    }
    await evaluate(`document.querySelector('[data-system-tab="logging"]').click()`);
    await check(`getComputedStyle(document.querySelector('[data-manage-lockouts-native]')).display`,'none','Native Lockouts menu is hidden when MAUI Settings is enabled');
    await evaluate(`syncNavigation({useMauiSystemSettingsEditor:false});`);
    await check(`document.querySelector('[data-manage-lockouts-native]').hidden`,false,'Disabling the preference restores the native menu immediately');
    await check(`document.querySelector('[data-manage-lockouts-native]').dataset.nativeEditorRoute`,'#/system/lockouts');
    await evaluate(`syncNavigation({useMauiSystemSettingsEditor:true});set('logFile','lockouts-draft.log');fault='holdActive';document.querySelector('[data-system-tab="active-lockouts"]').click()`);await pause(40);
    await check(`pick('active-actions').hidden`,false,'Lockouts refresh appears in the tab row');
    await check(`lockoutPanel('ip').querySelector('[data-system-unlock-selected]').disabled`,true,'Unlock starts disabled while data loads');
    await check(`lockoutPanel('ip').querySelector('[data-system-lockouts-list]').getAttribute('aria-busy')`,'true');
    await check(`lockoutPanel('ip').querySelector('[data-system-lockouts-rows]').textContent.includes('Loading Lockouts')`,true,'Loading stays inside the fixed table');
    const lockoutHeight=await evaluate(`lockoutPanel('ip').querySelector('[data-system-lockouts-list]').getBoundingClientRect().height`);
    await evaluate(`pendingActive();fault=''`);await pause(70);
    await check(`lockoutPanel('ip').querySelector('[data-system-lockouts-list]').getBoundingClientRect().height`,lockoutHeight,'Table height does not shift when loaded');
    await check(`lockoutPanel('ip').querySelector('[data-system-lockouts-count]').textContent`,'1');
    await check(`lockoutPanel('ip').querySelector('.select-column').getBoundingClientRect().width<=46`,true,'Selection column stays compact while the identity column expands');
    await check(`[...document.querySelectorAll('[data-system-unlock-row]')].every(button=>{const cell=button.closest('td'),css=getComputedStyle(cell);return button.getBoundingClientRect().width+parseFloat(css.paddingLeft)+parseFloat(css.paddingRight)<=cell.clientWidth;})`,true,'Row unlock buttons fit without clipping or ellipses');
    await check(`lockoutPanel('user').querySelector('[data-system-lockouts-rows]').textContent.includes('miuser')`,true);
    await check(`field('logFile').value`,'lockouts-draft.log','Loading lockouts keeps unsaved settings');
    await evaluate(`confirmed=false;lockoutPanel('ip').querySelector('[data-system-unlock-row]').click()`);await pause(50);
    await check(`requests.filter(r=>r.url.endsWith('/Unlock')).length`,0,'Cancel never sends an unlock');
    await check(`confirms.at(-1).message`,'Unlock IP address “192.168.198.1” in MOVEit?','Single-address confirmation names the address');
    await evaluate(`confirmed=true;fault='holdUnlock';lockoutPanel('ip').querySelector('[data-system-unlock-row]').click()`);await pause(50);
    await check(`pick('close').disabled&&pick('save').disabled`,true,'Unlock disables concurrent settings writes/close');
    await evaluate(`pendingUnlock();fault=''`);await pause(70);
    await check(`lockoutPanel('ip').querySelector('[data-system-lockouts-count]').textContent`,'0');
    await check(`lockoutPanel('ip').querySelector('[data-system-unlock-selected]').disabled&&lockoutPanel('ip').querySelector('[data-system-lockouts-all]').disabled`,true,'Empty data leaves visible unlock/select controls disabled');
    await check(`pick('status').textContent`,'Unlocked 1 IP address.');
    await check(`field('logFile').value`,'lockouts-draft.log');
    await evaluate(`document.querySelector('[data-system-tab="logging"]').click()`);
    await check(`pick('status').textContent`,'Unsaved changes','Operational actions never mark the settings draft saved');
    await check(`getComputedStyle(pick('active-actions')).display`,'none','Leaving Lockouts hides its refresh button');
    await evaluate(`set('logFile','changed.log');activeData.users.push({value:'another',stamp:'2026-10-22 09:25:49',editVersion:'user2'});document.querySelector('[data-system-tab="active-lockouts"]').click()`);await pause(70);
    await evaluate(`lockoutPanel('user').querySelector('[data-system-lockouts-all]').click();lockoutPanel('user').querySelector('[data-system-unlock-selected]').click()`);await pause(70);
    await check(`confirms.at(-1).message`,'Unlock the 2 selected users in MOVEit?','Multiple-user confirmation reports the selected count');
    await check(`pick('status').textContent`,'Unlocked 2 users.');
    await check(`requests.filter(r=>r.url.endsWith('/Unlock')).at(-1).body.selections.length`,2);
    for(const theme of ['light','dark']){
      await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.documentElement.dataset.theme='${theme}'`);await screenshot(theme+'-active-empty');
    }
    await evaluate(`activeData.addresses=[{value:'192.168.198.1',stamp:'2026-10-22 08:25:49',editVersion:'ip2'},{value:'192.168.198.2',stamp:'2026-10-22 09:25:49',editVersion:'ip3'}];activeData.users=[{value:'<img src=x onerror=alert(1)>',stamp:'2026-10-22 09:25:49',editVersion:'user3'}];pick('active-refresh').click()`);await pause(70);
    await check(`lockoutPanel('user').querySelectorAll('img').length`,0,'Usernames are rendered as text');
    await evaluate(`lockoutPanel('ip').querySelector('[data-system-lockout-select]').click()`);
    await check(`lockoutPanel('ip').querySelector('[data-system-lockouts-all]').indeterminate`,true,'Partial selection uses the standard checkbox state');
    await evaluate(`lockoutPanel('ip').querySelector('[data-system-lockouts-sort="value"]').click()`);
    await check(`lockoutPanel('ip').querySelector('[data-system-lockouts-sort="value"]').closest('th').getAttribute('aria-sort')`,'ascending');
    await check(`lockoutPanel('ip').querySelector('[data-system-unlock-selected]').disabled`,false,'Sorting preserves selection');
    await evaluate(`fault='unlockConflict';lockoutPanel('ip').querySelector('[data-system-unlock-selected]').click()`);await pause(70);
    await check(`lockoutPanel('ip').querySelector('[data-system-unlock-selected]').disabled`,true,'Stale selection requires fresh data');
    assert.match(await evaluate(`pick('status').textContent`),/Refresh Lockouts/);checks++;
    await evaluate(`fault='';pick('active-refresh').click()`);await pause(70);
    await evaluate(`fault='unlockNetwork';lockoutPanel('ip').querySelector('[data-system-unlock-row]').click()`);await pause(70);
    assert.match(await evaluate(`pick('status').textContent`),/unlock result could not be confirmed/);checks++;
    const unlockRequests=await evaluate(`requests.filter(r=>r.url.endsWith('/Unlock')).length`);
    await evaluate(`lockoutPanel('ip').querySelector('[data-system-unlock-row]').click()`);await pause(30);
    await check(`requests.filter(r=>r.url.endsWith('/Unlock')).length`,unlockRequests,'Ambiguous unlocks cannot be replayed before refresh');
    await evaluate(`fault='activeLoad';pick('active-refresh').click()`);await pause(70);
    await check(`lockoutPanel('ip').querySelector('[data-system-unlock-selected]').disabled`,true);
    await check(`pick('status').textContent`,'Lockouts could not be loaded.','Refresh errors remain in modal footer');
    await evaluate(`fault='';pick('active-refresh').click()`);await pause(70);
    for(const theme of ['light','dark'])for(const width of [1920,1000,800,600]){
      await command('Emulation.setDeviceMetricsOverride',{width,height:1080,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.documentElement.dataset.theme='${theme}'`);
      await check(`(()=>{const body=pick('body');return body.scrollWidth<=body.clientWidth+1&&pick('save').getBoundingClientRect().bottom<=innerHeight;})()`,true,'Lockout tables and footer fit the viewport');
      await check(`(()=>{const row=document.querySelector('.system-settings-editor-dialog .script-editor-tabs'),r=row.getBoundingClientRect(),b=pick('active-refresh').getBoundingClientRect();return Math.abs(r.right-parseFloat(getComputedStyle(row).paddingRight)-b.right)<=1&&b.top>=r.top&&b.bottom<=r.bottom&&[...document.querySelectorAll('[data-system-tab]')].every(tab=>{const t=tab.getBoundingClientRect();return b.right<=t.left||b.left>=t.right||b.bottom<=t.top||b.top>=t.bottom;});})()`,true,'Refresh stays at the right of the tab row without overlapping tabs');
      if(width===1920)await screenshot(theme+'-active-populated');
    }
    await evaluate(`for(let i=3;i<=22;i++)activeData.addresses.push({value:'192.168.198.'+i,stamp:'2026-10-22 09:25:49',editVersion:'ip'+i});pick('active-refresh').click()`);await pause(70);
    await evaluate(`lockoutPanel('ip').querySelector('[data-system-lockouts-list]').scrollTop=200`);
    await check(`(()=>{const header=lockoutPanel('ip').querySelector('thead .select-column'),r=header.getBoundingClientRect();return header.contains(document.elementFromPoint(r.left+r.width/2,r.top+r.height/2));})()`,true,'Sticky selection header stays above scrolled row checkboxes');
    await check(`requests.filter(r=>r.url.endsWith('/Save')).every(r=>!Object.hasOwn(r.body,'selections'))`,true,'Unlock is independent of the settings save payload');
    await evaluate(`document.querySelector('[data-system-tab="logging"]').click()`);
    await evaluate(`set('logFile','unsaved.log');fault='conflict';submit()`);await pause(70);
    await check(`pick('save').disabled&&!pick('reload').hidden`,true);
    await check(`field('logFile').value`,'unsaved.log','Conflict retains local input');
    await evaluate(`confirmed=false;pick('close').click()`);await pause(40);
    await check(`document.querySelector('[data-system-settings-editor]').open`,true);
    await evaluate(`pick('reload').click()`);await pause(40);
    await check(`field('logFile').value`,'unsaved.log','Reload requires discard confirmation');
    await evaluate(`confirmed=true;pick('reload').click()`);await pause(60);
    await check(`field('logFile').value`,'changed.log');
    await check(`pick('save').disabled`,true);
    await evaluate(`document.querySelector('[data-system-browse="taskLogDir"]').click()`);await pause(80);
    await check(`document.querySelectorAll('[data-host-browser-rows] [data-browser-index]').length`,1,'Folder picker filters files');
    await check(`requests.at(-1).query`,'?id=0');
    await evaluate(`document.querySelector('[data-host-browser-rows] [data-browser-index]').click();document.querySelector('[data-host-browser-ok]').click()`);await pause(40);
    await check(`field('taskLogDir').value`,'C:\\Logs');
    await evaluate(`document.querySelector('[data-system-browse="as2LogDirectory"]').click()`);await pause(70);
    await evaluate(`document.querySelector('[data-host-browser-rows] [data-browser-index]').click();document.querySelector('[data-host-browser-ok]').click()`);await pause(40);
    await check(`field('as2LogDirectory').value`,'C:\\Logs');
    await evaluate(`fault='validation';submit()`);await pause(70);
    await check(`pick('status').textContent`,'Invalid directory');
    await check(`pick('save').disabled`,false,'Definitive validation failure allows correction/retry');
    await evaluate(`fault='network';submit()`);await pause(70);
    await check(`pick('save').disabled&&!pick('reload').hidden`,true,'Network ambiguity blocks another save');
    assert.match(await evaluate(`pick('status').textContent`),/save result could not be confirmed/);checks++;
    await evaluate(`MauiModalUI.showError('Inline test error',document.querySelector('[data-system-settings-editor]'))`);
    await check(`pick('status').textContent`,'Inline test error');
    await check(`document.querySelectorAll('[data-maui-modal-feedback]').length`,0,'Registered errors stay in footer');
    await evaluate(`pick('close').click()`);await pause(70);
    await check(`document.querySelector('[data-system-settings-editor]').open`,false);
    await evaluate(`fault='';document.querySelector('[data-system-settings-open]').click()`);await pause(70);
    await evaluate(`set('logFile','partial.log');fault='partial';submit()`);await pause(70);
    await check(`pick('save').disabled&&!pick('reload').hidden`,true,'Partial save blocks replay until reload');
    await check(`field('logFile').value`,'partial.log','Partial save retains local values');
    assert.match(await evaluate(`pick('status').textContent`),/Some changes may have been saved/);checks++;
    await evaluate(`pick('close').click();fault=''`);await pause(50);
    await evaluate(`fault='holdShell';document.querySelector('[data-system-settings-open]').click();document.querySelector('[data-system-tab="lockout"]').click()`);await pause(40);
    await check(`pick('close').disabled`,true,'Loading keeps controls stable when switching tabs');
    await evaluate(`pendingShell();fault=''`);await pause(90);
    await check(`document.querySelector('[data-system-panel="lockout"] fieldset').disabled`,false,'Policy tab selected during initial load finishes loading automatically');
    await check(`document.querySelector('[data-system-trusted-address]').value`,'2.2.2.2');
    await evaluate(`pick('close').click()`);await pause(40);
    await evaluate(`saved.logging.debugLevel=80;document.querySelector('[data-system-settings-open]').click()`);await pause(70);
    await check(`pick('save').disabled&&pick('fields').disabled`,true,'Unknown native log level cannot be overwritten accidentally');
    assert.match(await evaluate(`pick('status').textContent`),/unsupported System Log Level/);checks++;
    await evaluate(`pick('close').click()`);await pause(40);
    await evaluate(`document.documentElement.dataset.useMauiSystemSettingsEditor='false';document.querySelector('[data-system-settings-open]').click()`);await pause(40);
    await check(`nativeClicks`,1,'Disabled preference leaves existing native handler in control');
    await check(`document.querySelector('[data-system-settings-editor]').open`,false);
    await check(`errors`,[]);
    const result={result:'PASS',checks};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('MOVEit Settings rendered workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({status:pick("status").textContent,ready:window.systemReady,flag:document.documentElement.dataset.useMauiSystemSettingsEditor,open:document.querySelector("[data-system-settings-editor]").open,errors,requests})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});
