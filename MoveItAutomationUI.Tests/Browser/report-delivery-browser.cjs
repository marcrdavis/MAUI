// Render production Create/Edit Report controls with local transport fixtures.
// No installed MAUI or MOVEit endpoints are contacted.
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const http = require('node:http');
const { spawn } = require('node:child_process');
const stopBrowser = require('./rename-browser-cleanup.cjs');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

const clean = value => value.replace(/@\*[\s\S]*?\*@/g, '');
const apiMarkup = clean(read('Views/Shared/_ReportApiAccess.cshtml'));
const editMarkup = clean(read('Views/Shared/_ReportManagementDialogs.cshtml').match(/<dialog[^>]+data-report-edit-dialog[^>]*>[\s\S]*?<\/dialog>/)[0].replace('<partial name="_ReportApiAccess" />', apiMarkup));
const reports = read('wwwroot/js/reports.js').replace(/\r\n/g, '\n');
const sync = reports.slice(reports.indexOf('    function syncEditSchedule()'), reports.indexOf('    function openEdit(', reports.indexOf('    function syncEditSchedule()')));
const output = path.join(root, 'TestResults/ReportDelivery'); fs.mkdirSync(output, { recursive: true });
function page() {
    return `<!doctype html><html data-app-base-url="/MAUI/" data-outbound-report-create-url="/MAUI/OutboundReports/Create" data-outbound-report-email-status-url="/MAUI/OutboundReports/EmailStatus" data-outbound-report-api-prepare-url="/MAUI/OutboundReports/PrepareApiLink"><head><style>${read('wwwroot/css/site.css')}</style></head><body>
    <div class="global-antiforgery"><input name="__RequestVerificationToken" value="fixture-token"></div>
    <template data-report-api-template>${apiMarkup}</template>${editMarkup}
    <script>
    window.errors=[];window.addEventListener('error',e=>errors.push(e.message));window.requests=[];window.mode='success';window.hold=false;window.release=null;window.notices=[];
    window.mauiShowToast=text=>notices.push(text);window.mauiConfirm=async()=>true;
    window.fetch=async(url,options={})=>{
        const body=options.body?JSON.parse(options.body):null;requests.push({url,body,headers:options.headers});
        if(url.endsWith('/Create')&&hold)await new Promise(resolve=>release=resolve);
        if(url.endsWith('/Create')&&mode==='failure')return new Response(JSON.stringify({message:'Fixture save failed'}),{status:503,headers:{'Content-Type':'application/json'}});
        const result=url.endsWith('/PrepareApiLink')?{apiAccess:{enabled:false,revision:'',path:'/api/reports/preview-'+requests.length+'/csv',linkDraft:'draft-'+requests.length}}:url.endsWith('/EmailStatus')?{configured:false}:{saved:true,message:'Report saved.'};
        return new Response(JSON.stringify(result),{headers:{'Content-Type':'application/json'}});
    };
    window.grid={gridKey:'tasks',captureViewState:()=>({columns:[{key:'Name',label:'Task Name'}]}),captureSourceContext:()=>({path:'/Tasks'}),captureSnapshot:()=>({columns:[],rows:[]})};
    </script><script>${read('wwwroot/js/report-api-access.js')}</script><script>${read('wwwroot/js/outbound-reports.js')}</script>
    <script>
    const editDialog=document.querySelector('[data-report-edit-dialog]');${sync}
    window.setEdit=mode=>{editDialog.querySelector('[data-report-edit-delivery]').value=mode;syncEditSchedule();};
    window.create=()=>MauiOutboundReports.openCreate(grid);
    window.pick=key=>document.querySelector('[data-outbound-report-dialog] [data-report-'+key+']');
    window.choose=mode=>{pick('delivery').value=mode;pick('delivery').dispatchEvent(new Event('change',{bubbles:true}));};
    window.ready=true;
    </script></body></html>`;
}
const candidates = [process.env.MAUI_BROWSER_EXECUTABLE,
    process.env['ProgramFiles(x86)'] && path.join(process.env['ProgramFiles(x86)'], 'Microsoft/Edge/Application/msedge.exe'),
    process.env.ProgramFiles && path.join(process.env.ProgramFiles, 'Microsoft/Edge/Application/msedge.exe'), '/usr/bin/chromium'];
const executable = candidates.find(file => file && fs.existsSync(file));
for (const [, script] of page().matchAll(/<script>([\s\S]*?)<\/script>/g)) new (require('node:vm').Script)(script);
if (process.argv.includes('--syntax-only')) { console.log('PASS Report delivery browser fixture syntax'); process.exit(0); }
assert.ok(executable, 'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-report-delivery-'));
let browser, socket, server, requestId = 0;
const pending = new Map();
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function command(method, params = {}) {
    const id = ++requestId;
    const result = new Promise((resolve, reject) => {
        const timer = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 15000);
        pending.set(id, { resolve, reject, timer });
    });
    socket.send(JSON.stringify({ id, method, params }));
    return result;
}
async function evaluate(expression) {
    const result = await command('Runtime.evaluate', { expression: '(async()=>{' + expression + '})()', awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
async function until(expression) {
    for (let i = 0; i < 120; i++) { if (await evaluate('return ' + expression)) return; await pause(30); }
    throw new Error('Timed out: ' + expression + '; ' + JSON.stringify(await evaluate('return window.errors')));
}
async function check(expression, expected, label) { assert.deepEqual(await evaluate('return ' + expression), expected, label); }
(async () => {
    server = http.createServer((request, response) => {
        response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end(page());
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    browser = spawn(executable, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--no-sandbox', '--remote-debugging-port=0', '--remote-allow-origins=*', '--user-data-dir=' + profile, 'about:blank'], { stdio: 'ignore' });
    let launchError; browser.on('error', error => { launchError = error; });
    let port = '';
    for (let i = 0; i < 200 && !port; i++) {
        if (launchError) throw launchError;
        if (browser.exitCode !== null) throw new Error('Browser exited before startup');
        try { const value = fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split(/\r?\n/)[0]; if (/^\d+$/.test(value)) port = value; }
        catch (error) { if (!['ENOENT', 'EBUSY', 'EACCES'].includes(error.code)) throw error; }
        if (!port) await pause(100);
    }
    assert.ok(port, 'Browser startup timed out');
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    socket = new WebSocket(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    socket.addEventListener('message', event => { const message = JSON.parse(event.data), callback = pending.get(message.id); if (!callback) return; pending.delete(message.id); clearTimeout(callback.timer); message.error ? callback.reject(new Error(JSON.stringify(message.error))) : callback.resolve(message.result); });
    await command('Page.enable');
    await command('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/` });
    await until('window.ready');
    await check('errors', [], 'No initialization errors');

    await evaluate("create();choose('None');");
    await check("[...pick('delivery').options].map(option=>option.textContent)", ['None','Send Now','One Time','Recurring'], 'Create delivery choices');
    await check("pick('description').closest('section').nextElementSibling.contains(pick('delivery'))", true, 'Delivery panel immediately follows Report details');
    await check("['recipients-field','time-field','once','recurring','smtp-warning'].every(key=>pick(key).getClientRects().length===0)", true, 'None hides recipients, schedules and SMTP warning');
    await evaluate("pick('title').value='Operations failures';pick('api-enabled').click();");
    await until("pick('api-url').value && !pick('submit').disabled");
    await check("requests.find(request=>request.url.endsWith('/PrepareApiLink')).url", '/MAUI/OutboundReports/PrepareApiLink', 'Preparation honors IIS application path');
    await check("pick('api-url').value.includes('/MAUI/api/reports/')", true, 'Preview URL retains IIS application path');
    await check("pick('api-copy').disabled || pick('api-regenerate').disabled", false, 'Copy and Regenerate enabled before Save');
    await evaluate("pick('api-regenerate').click();");
    await until("!pick('submit').disabled");
    const draft = await evaluate("return pick('api-url').value");
    await evaluate("mode='failure';hold=true;pick('submit').click();pick('submit').click();");
    await until("typeof release==='function'");
    await check("requests.filter(request=>request.url.endsWith('/Create')).length", 1, 'Create prevents duplicate submission');
    await check("[...document.querySelector('[data-outbound-report-dialog]').querySelectorAll('input,select,textarea,button')].every(control=>control.disabled)", true, 'Pending save locks the entire modal');
    await evaluate("release();");await until("!pick('submit').disabled");
    await check("pick('error').textContent", 'Fixture save failed', 'Failed save reports error and retains inputs');
    await check("pick('title').value", 'Operations failures', 'Retry retains Report title');
    await check("requests.filter(request=>request.url.endsWith('/Create'))[0].body.schedule", {deliveryMode:'None'}, 'None submits no hidden schedule');
    await check("requests.filter(request=>request.url.endsWith('/Create'))[0].body.recipients", '', 'None submits no recipients');
    assert.ok(draft.endsWith('/csv'));
    await check("Boolean(requests.filter(request=>request.url.endsWith('/Create'))[0].body.apiAccess.linkDraft)", true, 'API draft included in Save');
    await evaluate("hold=false;mode='success';pick('submit').click();");await until("!document.querySelector('[data-outbound-report-dialog]').open");
    await evaluate("create();choose('Recurring');");
    await check("['recipients-field','time-field','recurring'].every(key=>pick(key).getClientRects().length>0)", true, 'Email controls reappear for Recurring');
    await evaluate("pick('api-enabled').click();");await until("!pick('submit').disabled");
    await check("pick('api-link-fields').getClientRects().length>0 && pick('recipients').getClientRects().length>0", true, 'Email delivery and API access coexist');
    await evaluate("choose('Once');");await check("pick('once').getClientRects().length>0 && pick('recurring').getClientRects().length===0", true, 'One Time preserves date controls');
    await evaluate("choose('Now');");await check("pick('time-field').getClientRects().length===0 && pick('recipients-field').getClientRects().length>0", true, 'Send Now preserves email-only controls');
    await evaluate("choose('None');");await check("pick('api-enabled').checked", true, 'Changing delivery preserves API access');
    for(const theme of ['light','dark']) for(const width of [1400,800,600]) {
        await command('Emulation.setDeviceMetricsOverride',{width,height:1100,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme='${theme}';`);await pause(250);
        await check("document.querySelector('[data-outbound-report-dialog]').scrollWidth<=document.querySelector('[data-outbound-report-dialog]').clientWidth",true,'Create has no horizontal overflow');
        const screenshot=await command('Page.captureScreenshot',{format:'png'});fs.writeFileSync(path.join(output,`create-none-${theme}-${width}.png`),Buffer.from(screenshot.data,'base64'));
    }
    await evaluate("document.querySelector('[data-outbound-report-dialog]').close();editDialog.showModal();setEdit('None');");
    await check("['recipients-field','time-field','frequency-field','once','weekly','monthly'].every(key=>editDialog.querySelector('[data-report-edit-'+key+']').getClientRects().length===0)",true,'Edit None hides recipients and all schedules');
    await check("editDialog.querySelector('[data-report-api-section]').getClientRects().length>0",true,'Edit API section remains available with None');
    await evaluate("setEdit('Recurring');");await check("editDialog.querySelector('[data-report-edit-recipients-field]').getClientRects().length>0",true,'Edit email controls reappear');
    await check('errors', [], 'No browser errors');
    console.log('PASS rendered Create/Edit delivery visibility, independent API preparation, IIS URLs, failed-save retry, pending locks, duplicate protection and responsive light/dark layouts.');
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => {
    for (const callback of pending.values()) { clearTimeout(callback.timer); callback.reject(new Error('Browser closed')); } pending.clear();
    socket?.close(); stopBrowser(browser);
    if (server) { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
    try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); } catch { /* Browser profile cleanup only. */ }
});
