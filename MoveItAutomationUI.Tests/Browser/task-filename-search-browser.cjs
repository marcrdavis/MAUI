// Production grid runtime and filename module in Edge; only the native HTTP peer is simulated.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
const output=path.join(root,'TestResults/TaskFilenameSearch');fs.mkdirSync(output,{recursive:true});
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
// Include the actual Tasks prefix controls: the search-only fixture previously missed their vertical alignment.
const scopeView=read('Views/Shared/_TaskFolderSearchControls.cshtml');
const icon='<svg viewBox="0 0 20 20" aria-hidden="true"><path d="M2 4h6l2 2h8v11H2z"/></svg>';
const folderButton=scopeView.slice(scopeView.indexOf('<button type="button"'),scopeView.indexOf('</button>')+9)
    .replace(/<partial[^>]+\/>/g,icon).replaceAll('@selectedLabel','All Tasks');
const manageButton=scopeView.match(/<button type="button" class="secondary-button icon-text-button manage-folders-inline"[\s\S]*?<\/button>/)[0]
    .replace(/<partial[^>]+\/>/g,icon).replace('disabled="@(!canManageFolders)"','')
    .replace(/title="@\(canManageFolders \? null : "[^"]*"\)"/,'');
assert.ok(!folderButton.includes('@')&&!manageButton.includes('@'));
const scope=`<div class="task-grid-scope-controls"><form class="task-folder-filter-form-inline"><div class="menu-control task-folder-tree-select">${folderButton}</div></form>${manageButton}</div>`;
const markup=`<div class="grid-load-shell is-loading" data-grid-load-shell data-grid-initial-load-deferred="true"><section class="data-workspace" data-grid="tasks" data-grid-preference-persistence-enabled="false" data-task-filename-search-url="/maui/TaskFilenameSearch" data-default-columns='["Name","Description"]' data-available-columns='["Name","Description"]' data-search-columns='["Name","Description"]' data-default-page-size="10" data-page-sizes='[10,20]' data-row-selection-enabled="false" data-date-macro-now="2026-10-22T12:00:00" data-date-filter-zone="America/New_York" data-date-filter-locale="en-US" aria-busy="true">
<div class="sticky-grid-tools"><div class="grid-search-row">${scope}${search}<div class="grid-count"><strong data-filtered-count>3</strong><span>of</span><strong data-total-count>3</strong><span>tasks</span></div></div><div class="grid-toolbar"><button type="button" data-clear-filters>Clear filters</button></div></div>
<div class="table-card"><div class="table-scroll"><table data-grid-table><colgroup><col data-column="Name"><col data-column="Description"></colgroup><thead><tr class="column-header-row"><th data-column="Name" data-type="text">Task Name</th><th data-column="Description" data-type="text">Description</th></tr><tr class="column-filter-row"><th data-column="Name"><input data-column-filter="Name"></th><th data-column="Description"><input data-column-filter="Description"></th></tr></thead><tbody data-grid-body>${['Alpha','Beta','Gamma'].map((name,i)=>`<tr data-row-id="${i+1}"><td data-column="Name">${name}</td><td data-column="Description">Description ${i+1}</td></tr>`).join('')}</tbody></table></div><div data-empty-state hidden>No matching tasks.</div></div>
<div class="grid-pager"><span data-pager-summary></span><span data-page-indicator></span><button data-page-prev>Previous</button><button data-page-next>Next</button><select data-page-size></select></div><div class="toast" data-prototype-toast role="status" aria-live="polite"></div></section></div>`;
const scripts=['modal-ui','grid-saved-views','grid-row-renderer','grid-layout','grid-view-state','grid-export','grid-print','grid-actions','grid-date-macros','task-filename-search','grid'];
const html=`<!doctype html><html data-app-base-url="/maui/" data-user-grid-page-size="10" data-theme="light"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}body{padding:0}.app-main{margin-left:0}.content-area{padding:20px}</style></head><body><div class="app-shell"><div class="app-main"><main class="content-area">${markup}</main><footer class="app-footer"></footer></div></div><script>
window.errors=[];window.requests=[];window.mode='hold';window.release=null;window.items=[{taskId:'1',text:'*.txt'},{taskId:'2',text:'monthly_[yyyy].csv'},{taskId:'3',text:'sync_*.xml'}];
window.addEventListener('error',event=>errors.push(event.message));window.addEventListener('unhandledrejection',event=>errors.push(String(event.reason)));
window.fetch=async(url,options={})=>{requests.push({url:String(url),method:options.method});if(!String(url).endsWith('/maui/TaskFilenameSearch'))throw new Error('Unexpected fetch '+url);if(mode==='hold')await new Promise(resolve=>{release=resolve;});if(mode==='error')return{ok:false,status:500};return{ok:true,status:200,json:async()=>({success:true,items:structuredClone(items)})};};
window.g=document.querySelector('[data-grid="tasks"]');window.input=g.querySelector('[data-grid-search]');window.searchToast=g.querySelector('[data-prototype-toast]');
window.type=value=>{input.value=value;input.dispatchEvent(new Event('input',{bubbles:true}));};window.visible=()=>[...g.querySelectorAll('tbody tr')].filter(row=>!row.hidden).map(row=>row.dataset.rowId);
window.aligned=()=>{const boxes=['.task-folder-tree-button','.manage-folders-inline','.search-box','.grid-count'].map(selector=>g.querySelector(selector).getBoundingClientRect());return boxes.every(box=>Math.abs(box.top+box.height/2-boxes[0].top-boxes[0].height/2)<1);};
let sessionExpiredRedirectStarted=false;${toastHelper}window.mauiDisplayToast=displayToast;
window.searchCellReads=0;const originalQuerySelector=Element.prototype.querySelector;Element.prototype.querySelector=function(selector){if(this.matches('tbody tr')&&selector==='td[data-column="Name"]')searchCellReads++;return originalQuerySelector.call(this,selector);};
</script>${scripts.map(name=>inline('wwwroot/js/'+name+'.js')).join('')}<script>g.dispatchEvent(new CustomEvent('maui:grid-initial-data-ready'));window.ready=true;</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
if(process.argv.includes('--syntax-only')){console.log('Task filename search inline browser fixture syntax passed');process.exit(0);}
assert.ok(executable,'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-task-filenames-'));
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
    await until(`window.ready&&g.getAttribute('aria-busy')==='false'`,'grid ready');
    let checks=0;const check=async(expression,expected,label)=>{assert.deepEqual(await evaluate(expression),expected,label);checks++;};
    await pause(350);await check(`requests.length`,0,'Initial grid and empty search do not request steps');
    await check(`visible()`,['1','2','3']);
    await evaluate(`searchCellReads=0;type('')`);await check(`searchCellReads`,0,'Empty search skips per-row search-text DOM reads');
    for(const theme of ['light','dark'])for(const width of [1400,800,600]){
        await command('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme='${theme}';type('');g.dispatchEvent(new CustomEvent('maui:task-filename-search-invalidated'));mode='hold';searchToast.classList.remove('visible');window.before={top:g.getBoundingClientRect().top,height:g.getBoundingClientRect().height,table:g.querySelector('.table-card').getBoundingClientRect().top};type('*.txt')`);
        await check(`aligned()`,true,'Folder selector, Manage Folders, search and counter align before loading');
        await until(`Boolean(release)`,'filename request started');
        await check(`g.querySelector('[data-task-filename-spinner]')===null&&!searchToast.classList.contains('visible')`,true,'Filename reads show no loading indicator');
        await check(`aligned()`,true,'All search-row controls stay aligned during loading');
        await check(`Math.abs(g.querySelector('.grid-search-row').getBoundingClientRect().height-44)<1`,true,'No loading-status space is reserved');
        await check(`(()=>{const a=g.querySelector('.table-card').getBoundingClientRect();return Math.abs(a.top-before.table)<1&&Math.abs(g.getBoundingClientRect().height-before.height)<1;})()`,true,'Silent lookup does not resize the grid');
        await check(`(()=>{const a=g.querySelector('.search-box').getBoundingClientRect(),b=g.querySelector('.grid-count').getBoundingClientRect(),r=g.querySelector('.grid-search-row').getBoundingClientRect();return a.left>=r.left-1&&a.right<=r.right+1&&b.right<=r.right+1;})()`,true,'Searchbox and counter fit the existing grid width');
        if(width===1400)await screenshot(theme+'-search');
        await evaluate(`type('monthly');mode='ok';release();release=null`);
        await until(`visible().join(',')==='2'`,'filename-only result');
        await check(`g.querySelector('[data-filtered-count]').textContent`,'1');
        await check(`Math.abs(g.querySelector('.table-card').getBoundingClientRect().top-before.table)<1`,true,'Completion keeps final geometry');
        await check(`aligned()`,true,'All search-row controls stay aligned after completion');
        await evaluate(`window.loadedRequests=requests.length;type('*.txt')`);await pause(40);await check(`visible()`,['1'],'Masks are stored literal text');
        await evaluate(`type('invoice.txt')`);await check(`visible()`,[],'No wildcard expansion against hypothetical filenames');
        await evaluate(`type('*.txt, sync_')`);await check(`visible()`,['1','3'],'OR retains existing grid semantics');
        await evaluate(`type('!*.txt')`);await check(`visible()`,['2','3'],'Negation includes filenames');
        await evaluate(`type('Gamma')`);await check(`visible()`,['3'],'Existing row search remains available');
        await evaluate(`g.dispatchEvent(new CustomEvent('maui:grid-replace-rows',{detail:{rows:[{id:'1',values:{Name:'Alpha',Description:'Updated'}},{id:'2',values:{Name:'Beta',Description:'Updated'}},{id:'3',values:{Name:'Gamma',Description:'Updated'}}]}}))`);
        await check(`requests.length===loadedRequests`,true,'Ordinary row refresh reuses index');
        await evaluate(`type('*.txt')`);await check(`visible()`,['1'],'Row replacement retains filename matching');
        await evaluate(`type('');g.dispatchEvent(new CustomEvent('maui:task-filename-search-invalidated'));mode='error';type('*.txt')`);
        await until(`searchToast.classList.contains('visible')`,'failure toast');
        await check(`aligned()`,true,'Error toast leaves the controls aligned');
        await check(`searchToast.textContent.includes('task details only')&&searchToast.textContent.includes('Refresh Now to retry')`,true);
        await check(`searchToast.classList.contains('error')&&searchToast.getAttribute('role')==='alert'`,true,'Failure uses the standard error-toast styling and accessibility');
        await check(`Math.abs(g.querySelector('.table-card').getBoundingClientRect().top-before.table)<1`,true,'Error toast does not change grid geometry');
        await evaluate(`mode='ok';searchToast.classList.remove('visible');g.dispatchEvent(new CustomEvent('maui:task-filename-search-invalidated'))`);
        await until(`visible().join(',')==='1'`,'Refresh Now retry completes');
        await evaluate(`g.dispatchEvent(new CustomEvent('maui:task-filename-search-invalidated'));mode='error'`);
        await until(`searchToast.classList.contains('visible')`,'failure before cached-page restoration');
        await evaluate(`window.beforeRestoreRequests=requests.length;mode='ok';searchToast.classList.remove('visible');window.dispatchEvent(new PageTransitionEvent('pagehide',{persisted:true}));window.dispatchEvent(new PageTransitionEvent('pageshow',{persisted:true}))`);
        await until(`visible().join(',')==='1'`,'cached-page restoration retries after failure');
        await check(`requests.length===beforeRestoreRequests+1`,true,'Restoring after failure makes one fresh filename read');
        await check(`!searchToast.classList.contains('visible')&&aligned()`,true,'Restored search is silent and controls stay aligned');
        await check(`errors`,[],'No console errors');
    }
    await evaluate(`type('')`);await check(`searchToast.classList.contains('visible')`,false,'Successful reads and clear stay silent');
    const result={result:'PASS',checks,themes:['light','dark'],widths:[1400,800,600]};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Task filename search rendered workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({ready:window.ready,errors,requests})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});
