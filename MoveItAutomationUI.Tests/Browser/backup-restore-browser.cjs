// Exercises the production backup restore modal and configuration module; all restore endpoints are mocked.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
const output=path.join(root,'TestResults/BackupRestore');fs.mkdirSync(output,{recursive:true});
const executable=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium','/usr/bin/chromium-browser'].find(x=>x&&fs.existsSync(x));
const view=read('Views/Configuration/Settings.cshtml'),start=view.indexOf('<dialog class="new-task-dialog" data-backup-restore-dialog>'),end=view.indexOf('</dialog>',start)+9;
assert.ok(start>=0&&end>start,'Production restore modal exists');const modal=view.slice(start,end);assert.ok(!modal.includes('@'),'Modal has no unresolved Razor');
const html=`<!doctype html><html data-theme="light"><head><style>${read('wwwroot/css/site.css')}</style></head><body>
<section data-configuration-workspace data-backups-url="/maui/Configuration/Backups" data-backup-restore-url="/maui/Configuration/RestoreBackup"><div data-backup-status></div><table><tbody data-backup-list></tbody></table></section>${modal}
<script>
window.errors=[];window.toasts=[];window.requests=[];window.restoreMode='error';addEventListener('error',e=>errors.push(e.message));addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
window.item={id:'20261020120000',backupUtc:'2026-10-20T12:00:00Z',isComplete:true,hasAppSettings:true,hasSqliteDatabase:true,databaseProvider:'SQLite',moveItConfigurationCount:2,moveItEnvironments:['PROD','QA'],sizeBytes:2048,canRestore:true,restoreBlockReason:''};
// Capture only backup polling delays so refresh/visibility checks do not depend on wall-clock timing.
const realSetTimeout=window.setTimeout.bind(window),realClearTimeout=window.clearTimeout.bind(window);
window.backupTimers=new Map();let timerId=0;
window.setTimeout=(callback,delay,...args)=>{
 if(delay===3000||delay===30000){const id=--timerId;backupTimers.set(id,{callback:()=>callback(...args),delay});return id;}
 return realSetTimeout(callback,delay,...args);
};
window.clearTimeout=id=>backupTimers.delete(id)||realClearTimeout(id);
window.fireBackupPoll=async()=>{const [id,timer]=[...backupTimers][0]||[];if(!timer)throw Error('No backup poll scheduled');backupTimers.delete(id);timer.callback();await new Promise(resolve=>realSetTimeout(resolve,0));};
window.setPageHidden=hidden=>{Object.defineProperty(document,'hidden',{configurable:true,value:hidden});document.dispatchEvent(new Event('visibilitychange'));};
window.fetch=async(url,options={})=>{const target=new URL(url,location.href),body=options.body?JSON.parse(options.body):null;
 requests.push({path:target.pathname,body,token:options.headers?.get?.('RequestVerificationToken')});
 const reply=(data,status=200)=>new Response(JSON.stringify(data),{status,headers:{'content-type':'application/json'}});
 if(target.pathname.endsWith('/Backups'))return reply({backups:[item]});
 if(target.pathname.endsWith('/RestoreBackup')){
  if(restoreMode==='hold')return new Promise(resolve=>{window.releaseRestore=()=>resolve(reply({message:'Selected environment could not be restored.'},400));});
  if(restoreMode==='success')return reply({success:true,restartScheduled:false,message:'Backup restored.'});
  return reply({message:'Selected environment could not be restored.'},400);
 }
 throw Error('Unexpected endpoint '+target.pathname);
};
</script><script>${read('wwwroot/js/configuration-operations.js')}</script><script>
MauiConfigurationOperations.initialize({workspace:document.querySelector('[data-configuration-workspace]'),antiForgeryToken:'backup-token',showToast:(text)=>toasts.push(text)});
window.pick=name=>document.querySelector('[data-backup-restore-'+name+']');window.inputs=()=>[...pick('moveit-options').querySelectorAll('input')];
window.launch=async()=>{await MauiConfigurationOperations.refreshBackups();document.querySelector('[data-backup-action="restore"]').click();};
window.ready=()=>{pick('acknowledge').checked=true;pick('acknowledge').dispatchEvent(new Event('change',{bubbles:true}));pick('confirmation').value='RESTORE';pick('confirmation').dispatchEvent(new Event('input',{bubbles:true}));};
window.select=index=>{inputs()[index].click();};
window.scopes=()=>[...document.querySelectorAll('[data-backup-restore-scope]')];
window.scope=value=>{const input=scopes().find(input=>input.value===value);input.click();input.focus();};
</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
if(process.env.MAUI_VALIDATE_BROWSER_FIXTURE_ONLY==='1'){console.log('PASS backup-restore fixture scripts compile');process.exit(0);}
assert.ok(executable,'Edge/Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-backup-restore-'));
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
    await command('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/maui/Configuration/Settings` });
    for(let i=0;i<100&&!await evaluate('Boolean(window.launch)');i++)await pause(50);
    let checks=0;
    const check=async(expression,expected,label)=>{assert.deepEqual(await evaluate(expression),expected,label);checks++;};
    const until=async expression=>{for(let i=0;i<150;i++){if(await evaluate(expression))return;await pause(20);}throw Error('Backup condition timed out: '+expression);};
    const submit=async()=>{await evaluate(`pick('confirm').click()`);await until(`!pick('error').hidden`);};
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate(`window.originalBackup={...item};Object.assign(item,{status:'InProgress',statusMessage:'Exporting MOVEit configuration.',isRunning:true,isComplete:false,hasAppSettings:false,hasSqliteDatabase:false,moveItConfigurationCount:0,moveItEnvironments:[],canRestore:false});await MauiConfigurationOperations.refreshBackups()`);
    await check(`document.querySelector('.backup-status-badge').textContent`,'In Progress','Active backup has an explicit status');
    await check(`document.querySelector('[data-backup-list]').textContent.includes('Preparing backup…')`,true,'Active empty folder explains preparation');
    await check(`[...document.querySelectorAll('[data-backup-action]')].every(button=>button.disabled)`,true,'Active run locks restore, download and remove');
    await check(`document.querySelector('[data-backup-list]').textContent.includes('Exporting MOVEit')`,true,'Live phase is visible');
    await evaluate(`MauiConfigurationOperations.setBackupSectionActive(true);await MauiConfigurationOperations.refreshBackups()`);
    await check(`[...backupTimers.values()].map(timer=>timer.delay)`,[3000],'Visible active run polls every three seconds');
    await evaluate(`Object.assign(item,originalBackup,{status:'Complete',statusMessage:'Backup completed successfully.',isRunning:false});await fireBackupPoll()`);
    await check(`document.querySelector('.backup-status-badge').textContent`,'Complete','Polling discovers completion');
    await check(`[...backupTimers.values()].map(timer=>timer.delay)`,[30000],'Idle list polls every thirty seconds');
    await check(`[...document.querySelectorAll('[data-backup-action]')].every(button=>!button.disabled)`,true,'Completed valid backup restores normal actions');
    for(const status of ['Interrupted','Failed','Partial']){
        await evaluate(`Object.assign(item,{status:${JSON.stringify(status)},statusMessage:'Review Configuration > Logs.',isComplete:false,hasAppSettings:false,hasSqliteDatabase:false,moveItConfigurationCount:0,moveItEnvironments:[],canRestore:false});await MauiConfigurationOperations.refreshBackups()`);
        await check(`document.querySelector('.backup-status-badge').textContent`,status,status+' uses its own outcome label');
        await check(`document.querySelector('[data-backup-list]').textContent.includes('No backup data saved')&&document.querySelector('[data-backup-action="restore"]').disabled`,true,status+' explains missing contents and blocks restore');
        await check(`document.querySelector('[data-backup-list]').textContent.includes('Review Configuration > Logs.')`,true,status+' displays diagnostic guidance');
    }
    await evaluate(`setPageHidden(true)`);
    await check(`backupTimers.size`,0,'Hidden tab stops polling');
    const beforeVisible=await evaluate(`requests.length`);
    await evaluate(`setPageHidden(false);await MauiConfigurationOperations.refreshBackups()`);
    await check(`requests.length>${beforeVisible}&&backupTimers.size===1`,true,'Returning to the visible tab refreshes once and resumes polling');
    await evaluate(`MauiConfigurationOperations.setBackupSectionActive(false)`);
    await check(`backupTimers.size`,0,'Leaving the Backup section cancels polling');
    await evaluate(`Object.assign(item,originalBackup,{status:'Complete',statusMessage:'',isRunning:false});await launch();MauiConfigurationOperations.setBackupSectionActive(true);await MauiConfigurationOperations.refreshBackups();window.beforeModalPoll=requests.length;await fireBackupPoll()`);
    await check(`requests.length===beforeModalPoll&&backupTimers.size===1`,true,'Polling pauses while the restore dialog is open');
    await evaluate(`MauiConfigurationOperations.setBackupSectionActive(false)`);
    await check('inputs().map(input=>input.value)',['PROD','QA'],'Every backed-up environment has its own checkbox');
    await check('inputs().every(input=>!input.checked)',true,'Native restore is off by default for every environment');
    await check(`pick('confirm').disabled && pick('multi-warning').hidden`,true,'Acknowledgement and confirmation remain required');
    await check(`scopes().find(input=>input.checked).value`,'MauiOnly','Every opening defaults to explicit MAUI-only scope');
    await check(`pick('moveit-section').hidden&&getComputedStyle(pick('moveit-section')).display==='none'&&inputs().every(input=>input.disabled)`,true,'MAUI-only hides and disables environment selections');
    await evaluate('ready()');await check(`pick('confirm').disabled`,false,'MAUI-only needs no native environment selection');
    await submit();
    await check(`requests.filter(r=>r.body).at(-1).body.moveItEnvironments`,[],'Explicit MAUI-only submits no MOVEit environments even when exports exist');
    await check(`scopes().find(input=>input.checked).value==='MauiOnly'&&scopes().every(input=>!input.disabled)`,true,'Failed MAUI-only restore preserves scope and releases controls');
    await evaluate(`scope('MauiAndMoveIt')`);
    await check(`!pick('moveit-section').hidden&&getComputedStyle(pick('moveit-section')).display!=='none'&&inputs().every(input=>!input.disabled)`,true,'Combined scope reveals the existing environment selectors');
    await check(`pick('confirm').disabled`,true,'Combined scope requires at least one environment');
    const beforeNoSelection=await evaluate(`requests.filter(r=>r.body).length`);
    await evaluate(`pick('confirm').click()`);
    await check(`requests.filter(r=>r.body).length`,beforeNoSelection,'Combined scope cannot submit an empty selection');
    await evaluate('ready();select(0)');await submit();
    await check(`requests.filter(r=>r.body).at(-1).body.moveItEnvironments`,['PROD'],'Only selected native configurations are submitted');
    await check(`'restoreMoveItConfiguration' in requests.filter(r=>r.body).at(-1).body`,false,'No restore-all boolean is submitted');
    await check(`pick('error').textContent.includes('could not be restored') && document.querySelector('[data-backup-restore-dialog]').open`,true,'Restore errors stay in the modal');
    await check('toasts',[],'Restore errors do not use a parent toast');
    await check('inputs()[0].checked&&!inputs()[0].disabled&&!pick("confirm").disabled',true,'Failed restore preserves selection and restores controls');
    await evaluate('select(1)');await check(`pick('multi-warning').hidden`,false,'Multiple selections expose the partial-restore explanation');
    await submit();await check(`requests.filter(r=>r.body).at(-1).body.moveItEnvironments`,['PROD','QA'],'Multiple selections are explicit');
    await check(`scopes().find(input=>input.checked).value`,'MauiAndMoveIt','Failed combined restore preserves its selected scope');
    await evaluate('select(0)');await check(`pick('multi-warning').hidden`,true,'Single selection hides the multi-environment warning');
    await evaluate(`scope('MauiOnly')`);
    await check(`inputs().every(input=>!input.checked&&input.disabled)&&pick('moveit-section').hidden&&pick('multi-warning').hidden`,true,'Switching to MAUI-only clears all native selections and guidance');
    await evaluate(`inputs()[0].checked=true;ready()`);await submit();
    await check(`requests.filter(r=>r.body).at(-1).body.moveItEnvironments`,[],'MAUI-only never submits a stale checked environment');
    await evaluate(`pick('cancel').click();await launch()`);
    await check(`inputs().every(input=>!input.checked)&&pick('error').hidden&&pick('confirm').disabled`,true,'Reopening clears selections, errors and acknowledgement');
    await check(`scopes().find(input=>input.checked).value==='MauiOnly'&&pick('moveit-section').hidden`,true,'Reopening always resets scope to MAUI-only');
    await evaluate(`item.moveItEnvironments=Array.from({length:20},(_,i)=>'ENV_'+String(i+1).padStart(2,'0'));item.moveItConfigurationCount=20;pick('cancel').click();await launch()`);
    for(const theme of ['light','dark'])for(const [width,height]of [[1920,1080],[800,720],[600,720]]){
        await command('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
        await evaluate(`scope('MauiOnly');document.querySelector('.backup-restore-dialog-body').scrollTop=0`);
        await check(`scopes()[0].checked&&pick('moveit-section').hidden&&inputs().every(input=>!input.checked)`,true,theme+'/'+width+' MAUI-only resets and hides native choices');
        await check(`(()=>{const d=document.querySelector('[data-backup-restore-dialog]'),b=pick('confirm').getBoundingClientRect();return d.getBoundingClientRect().width<=${width}&&b.bottom<=${height};})()`,true,theme+'/'+width+' MAUI-only modal and footer fit');
        await screenshot('MAUI-only-'+theme+'-'+width);
        await evaluate(`scope('MauiAndMoveIt')`);
        await check(`inputs().length===20&&inputs().every(input=>!input.checked)`,true,theme+'/'+width+' all environment choices start unchecked');
        await check(`(()=>{const e=pick('moveit-options');return e.clientHeight<=202&&e.scrollHeight>e.clientHeight;})()`,true,theme+'/'+width+' long environment list scrolls within a bounded area');
        await check(`(()=>{const d=document.querySelector('[data-backup-restore-dialog]'),b=pick('confirm').getBoundingClientRect();return d.getBoundingClientRect().width<=${width}&&b.bottom<=${height};})()`,true,theme+'/'+width+' modal and restore button fit');
        await screenshot('Environments-'+theme+'-'+width);
    }
    await evaluate(`ready();select(19);restoreMode='hold';pick('confirm').click()`);await until(`typeof releaseRestore==='function'`);
    await check(`pick('confirm').disabled&&inputs().every(input=>input.disabled)`,true,'Selections are frozen during a destructive request');
    await check(`scopes().every(input=>input.disabled)`,true,'Both restore scopes are frozen during a destructive request');
    const submitted=await evaluate(`requests.filter(r=>r.body).length`);
    await evaluate(`ready();pick('confirm').click();document.querySelector('[data-backup-restore-dialog]').dispatchEvent(new Event('cancel',{cancelable:true}));`);
    await check(`pick('confirm').disabled&&document.querySelector('[data-backup-restore-dialog]').open`,true,'Input events and Escape cannot restart or close an active restore');
    await check(`requests.filter(r=>r.body).length`,submitted,'Busy-state interactions cannot submit duplicate restores');
    await evaluate('releaseRestore()');await until(`!pick('error').hidden`);
    await check('inputs().every(input=>!input.disabled)',true,'Rejected restore returns all selectors to an actionable state');
    await check(`scopes().every(input=>!input.disabled)&&scopes()[1].checked`,true,'Rejected combined restore unlocks and preserves scope');
    await evaluate(`restoreMode='error';pick('cancel').click();item.moveItEnvironments=[];item.moveItConfigurationCount=0;item.databaseProvider='SqlServer';item.hasSqliteDatabase=false;await launch();ready()`);
    await check(`inputs().length===0&&!pick('no-moveit').hidden&&pick('no-moveit').textContent.includes('No MOVEit')`,true,'Backups without native exports explain that only MAUI can be restored');
    await check(`scopes()[0].checked&&scopes()[1].disabled&&pick('moveit-section').hidden`,true,'No-export backup disables combined scope');
    await check(`!pick('sql-warning').hidden&&pick('sql-warning').textContent.includes('database itself is unchanged')`,true,'SQL Server backup retains the database restore limitation');
    await submit();await check(`requests.filter(r=>r.body).at(-1).body.moveItEnvironments`,[],'MAUI-only restore submits an empty environment selection');
    await check(`requests.filter(r=>r.body).every(r=>r.token==='backup-token'&&r.path==='/maui/Configuration/RestoreBackup')`,true,'Requests retain antiforgery and virtual-application routing');
    await evaluate(`restoreMode='success';pick('confirm').click()`);await until(`!document.querySelector('[data-backup-restore-dialog]').open`);
    await check('errors',[],'No browser errors');
    const result={result:'PASS',checks,themes:['light','dark'],widths:[1920,800,600]};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Backup restore workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate(`({errors,requests,status:pick('error').textContent})`));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});




