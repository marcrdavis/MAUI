// Production grid runtime and JSON module in Edge; only the native HTTP peer is simulated.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
const output=path.join(root,'TestResults/GridJsonSearch');fs.mkdirSync(output,{recursive:true});
const candidates=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium'];
const executable=candidates.find(file=>file&&fs.existsSync(file));
const inline=file=>'<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const view=read('Views/Shared/_DataGrid.cshtml');
let search=view.slice(view.indexOf('<label class="search-box">'),view.indexOf('</label>',view.indexOf('<label class="search-box">'))+8);
search=search.replace(/<partial[^>]+\/>/g,'').replace('@searchPlaceholder','Search task details and filenames');
assert.ok(!search.includes('@'));
// Use the existing production toast helper without the unrelated preferences/navigation startup.
const site=read('wwwroot/js/site.js'),toastStart=site.indexOf('    function displayToast(');
const toastHelper=site.slice(toastStart,site.indexOf('    window.mauiDisplayToast = displayToast;',toastStart));
const markup=`<div class="grid-load-shell is-loading" data-grid-load-shell data-grid-initial-load-deferred="true"><section class="data-workspace" data-grid="tasks" data-grid-preference-persistence-enabled="false" data-task-filename-search-url="/maui/TaskFilenameSearch" data-json-search-url="/maui/ObjectJsonSearch?type=Task" data-default-columns='["Name","Description"]' data-available-columns='["Name","Description"]' data-search-columns='["Name","Description"]' data-default-page-size="10" data-page-sizes='[10,20]' data-row-selection-enabled="false" data-date-macro-now="2026-10-22T12:00:00" data-date-filter-zone="America/New_York" data-date-filter-locale="en-US" aria-busy="true">
<div class="sticky-grid-tools"><div class="grid-search-row">${search}<div class="grid-count"><strong data-filtered-count>3</strong><span>of</span><strong data-total-count>3</strong><span>tasks</span></div></div><div class="grid-toolbar"><button type="button" data-clear-filters>Clear filters</button><button type="button" data-grid-create-report>Create Report</button></div></div>
<div class="table-card"><div class="table-scroll"><table data-grid-table><colgroup><col data-column="Name"><col data-column="Description"></colgroup><thead><tr class="column-header-row"><th data-column="Name" data-type="text"><button data-sort-button>Task Name</button></th><th data-column="Description" data-type="text">Description</th></tr><tr class="column-filter-row"><th data-column="Name"><input data-column-filter="Name"></th><th data-column="Description"><input data-column-filter="Description"></th></tr></thead><tbody data-grid-body>${['Alpha','Beta','Gamma'].map((name,i)=>`<tr data-row-id="${i+1}"><td data-column="Name">${name}</td><td data-column="Description">Description ${i+1}</td></tr>`).join('')}</tbody></table></div><div data-empty-state hidden>No matching tasks.</div></div>
<div class="grid-pager"><span data-pager-summary></span><span data-page-indicator></span><button data-page-prev>Previous</button><button data-page-next>Next</button><select data-page-size></select></div><div class="toast" data-prototype-toast role="status" aria-live="polite"></div></section></div>`;
const scripts=['modal-ui','grid-saved-views','grid-row-renderer','grid-layout','grid-view-state','grid-export','grid-print','grid-actions','grid-date-macros','shared/json-search','grid-json-search','task-filename-search','grid'];
const hostMarkup=markup.replace('data-grid="tasks"','data-grid="hosts"').replace('data-task-filename-search-url="/maui/TaskFilenameSearch"','').replace('type=Task','type=Host');
const html=`<!doctype html><html data-app-base-url="/maui/" data-user-grid-page-size="10"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}</style></head><body>${markup}${hostMarkup}<script>
window.reportApis={};window.MauiOutboundReports={openCreate:api=>reportApis[api.gridKey]=api};
window.errors=[];window.requests=[];window.fail=false;window.hold=false;window.held=[];
window.addEventListener('error',e=>errors.push(e.message));window.addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
window.docs={Task:{'1':[{path:['NextActions','NextAction','0','DoIfFailure'],value:1}],'2':[{path:['DoIfFailure'],value:0}],'3':[{path:['DoIfFailure'],value:true}]},Host:{'1':[{path:['SSHFTP','Port'],value:22},{path:['SSHFTP','Host'],value:'sftp.example.com'}],'2':[{path:['FTP','Port'],value:990},{path:['FTP','Host'],value:'ftp.example.com'}],'3':[{path:['S3','Bucket'],value:'archive'}]}};
window.fetch=async(url,options={})=>{const target=new URL(url,location.href);requests.push(target.pathname+target.search);
 if(target.pathname.endsWith('TaskFilenameSearch'))return{ok:true,status:200,json:async()=>({success:true,items:[{taskId:'2',text:'*.txt'}]})};
 if(!target.pathname.endsWith('ObjectJsonSearch'))throw new Error('Unexpected fetch '+url);
 const type=target.searchParams.get('type'),id=target.searchParams.get('id');if(hold)await new Promise(resolve=>held.push(resolve));
 if(fail)return{ok:false,status:502};return{ok:true,status:200,json:async()=>({success:true,document:{id,values:structuredClone(docs[type][id])}})};};
window.g=kind=>document.querySelector('[data-grid="'+kind+'"]');window.type=(kind,value)=>{const input=g(kind).querySelector('[data-grid-search]');input.value=value;input.dispatchEvent(new Event('input',{bubbles:true}));};
window.visible=kind=>[...g(kind).querySelectorAll('tbody tr')].filter(row=>!row.hidden).map(row=>row.dataset.rowId);
let sessionExpiredRedirectStarted=false;${toastHelper}window.mauiDisplayToast=displayToast;
</script>${scripts.map(name=>inline('wwwroot/js/'+name+'.js')).join('')}<script>for(const grid of document.querySelectorAll('[data-grid]'))grid.dispatchEvent(new CustomEvent('maui:grid-initial-data-ready'));window.ready=true;</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
if(process.argv.includes('--syntax-only')){console.log('PASS grid JSON browser fixture syntax');process.exit(0);}
assert.ok(executable,'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-grid-json-'));
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



    async function until(expression,label){for(let i=0;i<120;i++){if(await evaluate(expression))return;await pause(30);}throw new Error('Timed out: '+label+'; '+JSON.stringify(await evaluate('({errors,requests})')));}
    await until(`window.ready&&[...document.querySelectorAll('[data-grid]')].every(grid=>grid.getAttribute('aria-busy')==='false')`,'grids ready');
    let checks=0;const check=async(expression,expected,label)=>{assert.deepEqual(await evaluate(expression),expected,label);checks++;};
    await pause(350);await check('requests.length',0,'empty grids make no definition reads');
    await evaluate(`type('tasks','Alpha');type('hosts','Beta')`);await check(`visible('tasks')`,['1']);await check(`visible('hosts')`,['2']);
    await pause(350);await check(`requests.every(url=>url.endsWith('TaskFilenameSearch'))`,true,'ordinary search never reads JSON');
    for(const theme of ['light','dark']){
     await evaluate(`document.documentElement.dataset.theme='${theme}';type('tasks','JsOn:DoIfFailure=1');type('hosts','JSON:Port>=22')`);
     await until(`visible('tasks').join(',')==='1,3'&&visible('hosts').join(',')==='1,2'`,'JSON matches on both grids');
     await evaluate(`g('tasks').querySelector('[data-grid-create-report]').click();g('hosts').querySelector('[data-grid-create-report]').click()`);
     await check(`reportApis.tasks.getCreateIssue('Now')`,'','completed Task JSON search supports Send Now');
     await check(`reportApis.tasks.captureSnapshot().rows.map(row=>row.id)`,['1','3'],'Report snapshot contains only JSON matches');
     await check(`reportApis.hosts.getCreateIssue('Recurring').includes('only be sent now')`,true,'unsupported JSON schedules receive explicit feedback');
     await check(`g('tasks').querySelector('[data-filtered-count]').textContent`,'2');await check(`g('hosts').querySelector('[data-filtered-count]').textContent`,'2');
     await evaluate(`window.requestCount=requests.length;type('hosts','json:Host.SSHFTP.Port=22')`);await until(`visible('hosts').join(',')==='1'`,'qualified host paths');
     await evaluate(`type('hosts','JSON:Host contains EXAMPLE.COM')`);await until(`visible('hosts').join(',')==='1,2'`,'case-insensitive contains');
     await evaluate(`type('tasks','JSON:NextActions.NextAction[*].DoIfFailure=1')`);await until(`visible('tasks').join(',')==='1'`,'nested task paths');
     await evaluate(`type('tasks','JSON:DoIfFailure!=1')`);await until(`visible('tasks').join(',')==='2'`,'inequality');
     await evaluate(`type('tasks','JSON:DoIfFailure<=0')`);await until(`visible('tasks').join(',')==='2'`,'numeric strings/flags');
     await evaluate(`type('tasks','JSON:Missing!=1')`);await until(`g('tasks').querySelector('[data-pager-summary]').textContent==='Showing 0–0 of 0'`,'missing fields do not satisfy inequality');await check(`visible('tasks')`,[]);
     await evaluate(`type('tasks','JSON:DoIfFailure=')`);await check(`visible('tasks')`,[]);await check(`g('tasks').querySelector('[data-pager-summary]').textContent.includes('comparison value')`,true,'invalid expression feedback');
     await evaluate(`type('tasks','JSON:DoIfFailure=1')`);await until(`visible('tasks').length===2`,'restore JSON query');
     await evaluate(`const input=g('tasks').querySelector('[data-column-filter="Name"]');input.value='Gamma';input.dispatchEvent(new Event('input',{bubbles:true}));`);await check(`visible('tasks')`,['3'],'column filters intersect JSON');
     await evaluate(`g('tasks').querySelector('[data-clear-filters]').click();g('tasks').querySelector('[data-sort-button]').click();g('tasks').querySelector('[data-sort-button]').click();`);await check(`visible('tasks')`,['3','1'],'sorting retains JSON subset');
     await check(`requests.length===requestCount`,true,'cached expressions do not fetch again');
     await evaluate(`type('tasks','*.txt')`);await check(`visible('tasks')`,['2'],'ordinary filename search still works after JSON');
     await evaluate(`type('hosts','Alpha')`);await check(`visible('hosts')`,['1'],'ordinary host search still works after JSON');
     await evaluate(`g('tasks').querySelector('[data-sort-button]').click();type('tasks','');type('hosts','')`);await check(`visible('tasks')`,['1','2','3']);await check(`visible('hosts')`,['1','2','3']);
    }
    await evaluate(`fail=true;type('hosts','JSON:Port=22');g('hosts').dispatchEvent(new CustomEvent('maui:json-search-invalidated'));`);
    await until(`g('hosts').querySelector('[data-pager-summary]').textContent.includes('could not be completed')`,'failure feedback');await check(`visible('hosts')`,[],'failed search cannot imply no-match/partial success');
    await evaluate(`fail=false;g('hosts').dispatchEvent(new CustomEvent('maui:json-search-invalidated'))`);await until(`visible('hosts').join(',')==='1'`,'manual refresh retry');
    await evaluate(`docs.Host['1'][0].value=23;g('hosts').dispatchEvent(new CustomEvent('maui:json-search-invalidated'))`);await until(`g('hosts').querySelector('[data-pager-summary]').textContent==='Showing 0–0 of 0'`,'refresh uses current values');await check(`visible('hosts')`,[]);
    await evaluate(`type('hosts','JSON:Port=23');g('hosts').dispatchEvent(new CustomEvent('maui:grid-replace-rows',{detail:{rows:[{id:'1',values:{Name:'Alpha',Description:'Refreshed'}},{id:'2',values:{Name:'Beta',Description:'Refreshed'}}]}}))`);await until(`visible('hosts').join(',')==='1'`,'row refresh preserves active JSON filter');
    await evaluate(`hold=true;g('hosts').dispatchEvent(new CustomEvent('maui:json-search-invalidated'));type('hosts','JSON:Port=23')`);await until('held.length===2','pending reads');await check(`reportApis.hosts.getCreateIssue('Now').includes('Complete a valid JSON search')`,true,'pending JSON search cannot send an empty Report');await evaluate(`type('hosts','Beta');hold=false;held.splice(0).forEach(resolve=>resolve())`);await pause(50);await check(`visible('hosts')`,['2'],'late JSON responses do not replace ordinary search');
    await check('errors',[],'no browser errors');
    const result={result:'PASS',checks,themes:['light','dark']};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Grid JSON rendered workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({ready:window.ready,errors,requests})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});

