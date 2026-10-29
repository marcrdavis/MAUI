// Production read-only audit comparison, row availability, async isolation and themed layout in Edge.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
const output=path.join(root,'TestResults/AuditChanges');fs.mkdirSync(output,{recursive:true});
const executable=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium'].find(file=>file&&fs.existsSync(file));
const modal=read('Views/Shared/_AuditChangesDialog.cshtml').replace(/@\*[\s\S]*?\*@/g,'').replace(/@Url.Action\("Details", "AuditChanges"\)/g,'/maui/AuditChanges/Details');
assert.ok(!modal.includes('@Url'),'Use an IIS application-relative URL');
const records=read('wwwroot/js/records.js');
const functions=['closeReportRowActionMenu','getReportRowValues'].map(name=>records.match(new RegExp('    function '+name+'\\([^)]*\\) \\{[\\s\\S]*?\\n    \\}'))[0]).join('\n');
const openMenu=records.slice(records.indexOf('    async function openReportRowActionMenu('),records.indexOf('    // Purpose: Returns report row values.'));
const click=records.match(/    reportRowChangesCommand\?\.addEventListener\('click', event => \{[\s\S]*?\n    \}\);/)[0];
const inline=file=>'<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const html=String.raw`<!doctype html><html><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}
${read('wwwroot/css/audit-changes.css')}</style></head><body>${modal}
<script>
window.errors=[];window.logged=[];window.requests=[];window.fault='';window.held=[];window.attack=0;
window.addEventListener('error',e=>errors.push(e.message));window.addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
window.mauiLogNotification=(...args)=>logged.push(args);
window.sample={lines:[{kind:'same',before:1,after:1,text:'<Task>'},{kind:'removed',before:2,after:null,text:'FileMask="*.*"'},{kind:'added',before:null,after:2,text:'FileMask="*.csv" <img src=x onerror="attack++">'},{kind:'same',before:3,after:3,text:'</Task>'}],added:1,removed:1,grouped:false,plainText:false};
window.fetch=async(url,options)=>{
 const target=new URL(url,location.href);requests.push({url:target.pathname,id:target.searchParams.get('logId'),options});
 if(fault==='network')throw new Error('Network unavailable');
 if(fault==='hold')return new Promise(resolve=>held.push(()=>resolve(new Response(JSON.stringify(sample),{headers:{'content-type':'application/json'}}))));
 if(fault==='empty')return new Response(JSON.stringify({lines:[],added:0,removed:0}),{headers:{'content-type':'application/json'}});
 if(fault==='invalid')return new Response(JSON.stringify({lines:'bad'}),{headers:{'content-type':'application/json'}});
 if(fault)return new Response(JSON.stringify({message:'Audit '+fault+' unavailable'}),{status:Number(fault),headers:{'content-type':'application/json'}});
 return new Response(JSON.stringify(sample),{headers:{'content-type':'application/json'}});
};
window.makeMenu=(type,flag,id)=>{
 const grid={dataset:{grid:type}},reportRowActionMenu=document.createElement('div');reportRowActionMenu.hidden=true;
 const reportRowChangesCommand=document.createElement('button'),reportRowDetailsCommand=document.createElement('button');reportRowActionMenu.append(reportRowChangesCommand,reportRowDetailsCommand);
 const reportTaskInformationCommand=null,reportCopyTaskNameCommand=null,reportEditTaskCommand=null,reportToggleTaskEnabledCommand=null,reportRunTaskCommand=null,reportRowRunHistoryCommand=null,reportRowFileActivityCommand=null,reportRowAuditCommand=null;
 let activeReportRowAction=null,reportMenuOpenSequence=0;
 const row=document.createElement('tr');row.dataset.rowId='audit-'+id;row.dataset.rowValues=JSON.stringify({HasAuditDiff:flag,LogID:id,TargetName:'Task name'});
 const cell=document.createElement('td'),trigger=document.createElement('button');row.append(cell);cell.append(trigger);
 const reportDetailLabels=()=>({action:'View Audit Details'}),positionReportRowActionMenu=()=>{},showStatus=()=>{};
 ${functions}
 ${openMenu}
 ${click}
 return {show:()=>openReportRowActionMenu(trigger),visible:()=>!reportRowChangesCommand.hidden&&!reportRowChangesCommand.disabled,click:()=>reportRowChangesCommand.click()};
};
</script>${inline('wwwroot/js/modal-ui.js')}${inline('wwwroot/js/audit-changes.js')}<script>window.ready=true;</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
assert.match(read('Views/Shared/_ReportRowDetails.cshtml'),/data-report-row-command="changes"[^>]*hidden>View Changes/);
assert.match(read('Views/Records/Audit.cshtml'),/_AuditChangesDialog/);
assert.ok(!read('Views/Records/TaskRun.cshtml').includes('audit-changes.js'));
if(process.argv.includes('--syntax-only')){console.log('Audit comparison production modal/menu fixture syntax and integration checks: PASS');process.exit(0);}
assert.ok(executable,'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-audit-changes-'));
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
    let checks=0;
    const check=async(expression,expected,label)=>{assert.deepEqual(await evaluate(expression),expected,label);checks++;};
    await check('requests.length',0,'Grid opening does not load audit snapshots');
    for(const [type,flag,id,expected]of [['audit','1','481',true],['audit','0','481',false],['audit','','481',false],['audit','1','',false],['taskruns','1','481',false],['fileactivity','1','481',false]]){
      await evaluate(`window.menu=makeMenu('${type}','${flag}','${id}');await menu.show();`);
      await check('menu.visible()',expected,'Only flagged Audit rows have View Changes');
    }
    await evaluate(`window.menu=makeMenu('audit','1','481');await menu.show();menu.click();`);await pause(100);
    await check('document.querySelector("[data-audit-changes-dialog]").open',true);
    await check('document.querySelectorAll("[data-audit-changes-lines] tr").length',4);
    await check('requests[0].url','/maui/AuditChanges/Details');
    await check('requests[0].id','481');
    await check('requests[0].options.cache','no-store');
    await check('requests[0].options.credentials','same-origin');
    await check('attack',0);await check('document.querySelectorAll("[data-audit-changes-lines] img").length',0,'XML is plain text');
    await check('document.querySelectorAll("[data-audit-changes-dialog] .settings-footer button").length',1,'Only Close in footer');
    for(const theme of ['light','dark'])for(const width of [1400,1024,800,600]){
      await command('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.documentElement.dataset.theme='${theme}'`);await pause(30);
      await check(`(()=>{const d=document.querySelector('[data-audit-changes-dialog]'),f=d.querySelector('.settings-footer').getBoundingClientRect();return d.scrollWidth<=d.clientWidth+1&&d.getBoundingClientRect().width<=innerWidth&&f.bottom<=innerHeight&&f.top>=0;})()`,true,'Modal/footer fit both themes and all widths');
      await check(`(()=>{const added=document.querySelector('.audit-line-added td'),removed=document.querySelector('.audit-line-removed td');return getComputedStyle(added).backgroundColor!==getComputedStyle(removed).backgroundColor;})()`,true,'Add/remove colors remain distinct');
      if(width===1400)await screenshot(theme);
    }
    await evaluate(`document.querySelector('[data-audit-changes-dialog]').close()`);await pause(40);
    await check('document.querySelector("[data-audit-changes-lines]").textContent','', 'Close clears cached snapshot text');
    for(const fault of ['403','404','500','401','network','invalid']){
      await evaluate(`fault='${fault}';await MauiAuditChanges.open('481');`);
      await check('document.querySelector("[data-audit-changes-body]").getAttribute("aria-busy")','false');
      await check(`(()=>{const d=document.querySelector('[data-audit-changes-dialog]');return [...d.querySelectorAll('.settings-footer [role="alert"],.settings-footer [data-maui-modal-feedback]')].some(n=>!n.hidden&&n.textContent.length>0);})()`,true,'Load failures stay in modal footer');
      await check('document.querySelectorAll(".toast.visible").length',0);
      await evaluate(`document.querySelector('[data-audit-changes-dialog]').close()`);await pause(25);
    }
    await check('logged.length>=6',true,'Modal errors are logged');
    await evaluate(`fault='hold';MauiAuditChanges.open('481');void 0;`);await pause(30);
    await evaluate(`document.querySelector('[data-audit-changes-dialog]').close()`);await pause(30);
    await check('requests[requests.length-1].options.signal.aborted',true,'Close aborts outstanding request');
    await evaluate(`fault='';await MauiAuditChanges.open('482','New Task');held.shift()();`);await pause(40);
    await check(`document.querySelector('[data-audit-changes-context]').textContent`,'Audit record 482 · New Task','Stale response cannot replace newer comparison');
    await evaluate(`document.querySelector('[data-audit-changes-dialog]').close()`);await pause(25);
    await evaluate(`fault='empty';await MauiAuditChanges.open('483');`);
    await check(`document.querySelector('[data-audit-changes-state]').textContent`,'No configuration differences were recorded.');
    await evaluate(`document.querySelector('[data-audit-changes-dialog]').close()`);await pause(25);
    await evaluate(`fault='';sample.lines=Array.from({length:450},(_,i)=>({kind:'same',before:i+1,after:i+1,text:'line '+i}));sample.added=0;sample.removed=0;await MauiAuditChanges.open('9223372036854775807');`);
    await check(`requests[requests.length-1].id`,'9223372036854775807','Large numeric Log IDs are never rounded by JavaScript');
    await check('document.querySelectorAll("[data-audit-changes-lines] tr").length',450,'Chunked rendering completes');
    await evaluate(`document.querySelector('[data-audit-changes-dialog]').close()`);await pause(25);
    await check('errors',[]);
    const result={result:'PASS',checks};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Audit Changes rendered workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({errors,requests:requests.map(r=>({url:r.url,id:r.id})),body:document.body.innerText})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});
