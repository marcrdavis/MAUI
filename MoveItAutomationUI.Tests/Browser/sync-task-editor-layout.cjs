'use strict';
// Production Task modal/runtime rendered in Edge using fixtures from the shared read service.
// Captured/mock transport only; no live MOVEit endpoint is contacted.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const http = require('node:http');
const { spawn, spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const fixturePath = process.env.MAUI_SYNC_EDITOR_FIXTURE_PATH || path.join(root,'TestResults/SyncEditorFixtures.json');
const fixtures = JSON.parse(fs.readFileSync(fixturePath,'utf8'));
const output = path.join(root,'TestResults/SyncEditorLayout');
fs.mkdirSync(output,{recursive:true});
const candidates = [process.env.MAUI_BROWSER_EXECUTABLE,
    process.env['ProgramFiles(x86)'] && path.join(process.env['ProgramFiles(x86)'], 'Microsoft/Edge/Application/msedge.exe'),
    process.env.ProgramFiles && path.join(process.env.ProgramFiles, 'Microsoft/Edge/Application/msedge.exe'),
    '/usr/bin/chromium', '/usr/bin/chromium-browser', '/opt/google/chrome/chrome'];
const executable = candidates.find(candidate => candidate && fs.existsSync(candidate));
assert.ok(executable, 'An installed Edge or Chromium browser is required. Set MAUI_BROWSER_EXECUTABLE.');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const renderIcons = require('./render-razor-icons.cjs');
const markup = renderIcons(read('Views/Shared/Editors/_TraditionalTaskEditorDialog.cshtml')
    .replace(/@\*[\s\S]*?\*@/g, '')
    .replace(/@for \(var day = 1; day <= 31; day\+\+\)\s*\{\s*(<button[^\n]+@day[^\n]+<\/button>)\s*\}/g, (_,button) => Array.from({length:31},(_,index)=>button.replaceAll('@day',String(index+1))).join(''))
    .replace(/@Url\.Action\("([^"\n]+)", "TraditionalTaskEditor"\)/g, '/maui/TraditionalTaskEditor/$1'), read('Views/Shared/_Icon.cshtml'));
assert.ok(!/@(?:for|Url|day|Html|\()|<partial/.test(markup), 'Task dialog contains unresolved Razor expressions');
const hostFixtures=JSON.parse(fs.readFileSync(process.env.MAUI_HOST_EDITOR_FIXTURE_PATH || path.join(root,'TestResults/HostEditorFixtures.json'),'utf8'));
const hostMarkup=renderIcons(read('Views/Shared/Editors/_HostEditorDialog.cshtml').replace(/@\*[\s\S]*?\*@/g,''),read('Views/Shared/_Icon.cshtml'));
const browserMarkup=read('Views/Shared/_HostBrowser.cshtml').replace(/@\*[\s\S]*?\*@/g,'');
const exportSource=read('Views/Shared/_NativePortabilityExportDialog.cshtml'),exportMarkup=exportSource.slice(exportSource.indexOf('<dialog')).replace('@plural','Tasks');
assert.ok(!exportMarkup.includes('@'),'Task export fixture resolves Razor');
const html = `<!doctype html><html data-can-save-templates="true" data-moveit-admin="true" data-use-maui-host-editor="true"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}
${read('wwwroot/css/host-browser.css')}
${read('wwwroot/css/editors/traditional-task-editor.css')}
${read('wwwroot/css/editors/advanced-task-editor.css')}
${read('wwwroot/css/editors/host-editor.css')}
${read('wwwroot/css/ssh-algorithm-list.css')}
.button-caret,.traditional-task-editor-rail-caret{transition:none!important;}</style></head><body><div data-task-page-config data-task-logs-url="/maui/Tasks/Logs" data-run-as-of-url="/maui/TraditionalTaskEditor/RunAsOf" data-run-as-of-progress-url="/maui/TraditionalTaskEditor/RunAsOfProgress" data-host-editor-shell-url="/maui/HostEditor/Shell" data-host-editor-save-url="/maui/HostEditor/Save" data-create-advanced-task-url="/maui/Tasks/CreateAdvancedFromTraditional"></div>${markup}${hostMarkup}${browserMarkup}<div data-task-portability-root>${exportMarkup}</div>
<script>${read('wwwroot/js/modal-ui.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/native-portability.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/editors/editor-routing.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/ssh-algorithm-list.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/editors/object-edit-lease.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/editors/host-editor.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/editors/task-host-editor.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/shared/folder-browser-sorting.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/shared/host-browser.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/editors/task-run-as-of.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/shared/traditional-task-field-macros.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/editors/advanced-task-flow.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/editors/advanced-task-editor.js').replace(/<\/script/gi,'<\\/script')}</script>
<script>${read('wwwroot/js/editors/task-script-guidance.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/editors/traditional-task-editor.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>
window.mauiAppUrl = route => '/maui' + route;
window.confirmCalls = []; window.confirmAnswer = true;
window.mauiConfirm = async options => { confirmCalls.push(options); return confirmAnswer; };
window.layoutErrors = [];
window.addEventListener('error', event => layoutErrors.push(event.message));
window.addEventListener('unhandledrejection', event => layoutErrors.push(String(event.reason)));
window.cloneCalls = []; window.templateCalls = []; window.deleteCalls = []; window.saveCalls = []; window.conversionCalls=[]; window.conversionEvents=[]; window.syncActionCalls = []; window.previewGets = 0; window.previewCanChange = true;window.logCalls=[];window.logRows=[];window.holdLogs=false;window.failLogs=false;
window.realFetch = window.fetch.bind(window);
window.recoveryCalls=[];window.lockOnOpening=false;
window.fetch = async (url, options = {}) => {
    if(String(url).endsWith('/ExportTask')) {
        if(window.taskExportMode==='network')throw new Error('Unable to reach the export server.');
        if(window.taskExportMode==='http')return new Response(JSON.stringify({message:'Export denied.'}),{status:403,headers:{'Content-Type':'application/json'}});
        if(window.taskExportMode==='plain')return new Response('Gateway unavailable',{status:502});
        if(window.taskExportMode==='malformed')return new Response('Gateway unavailable',{status:502,headers:{'Content-Type':'application/json'}});
        return new Response(new Uint8Array(window.taskExportMode==='empty'?[]:[80,75,3,4]),{headers:{'Content-Type':'application/zip','Content-Disposition':'attachment; filename="Task.maui"'}});
    }
    if(String(url).includes('/ScriptMetadata')) {
        const id=new URL(url,location.href).searchParams.get('id');window.scriptMetadataCalls=(window.scriptMetadataCalls||[]);scriptMetadataCalls.push(id);
        const data=structuredClone(window.scriptMetadataReplies?.[id] || {id,name:'Script',description:'',parametersAvailable:false,parameters:[]});
        if(data.delay)await new Promise(resolve=>window.releaseScriptMetadata=resolve); // Ignore AbortSignal to verify old-selection/old-editor isolation.
        return new Response(JSON.stringify(data.failure?{message:'Script guidance unavailable'}:{success:true,script:data}),{status:data.failure?503:200,headers:{'Content-Type':'application/json'}});
    }
    if(String(url).includes('/ObjectEditLease/')) {
        recoveryCalls.push({url:String(url),body:JSON.parse(options.body)});
        if (lockOnOpening && String(url).endsWith('/Open')) return new Response(JSON.stringify({message:'This object has been checked out to miadmin since 2026-10-24 17:27:53',checkoutConflict:true}),{status:409,headers:{'Content-Type':'application/json'}});
        return new Response(JSON.stringify(String(url).endsWith('/Override') ? {success:true,leaseId:'task-recovered',editVersion:'recovered-task-revision'} : String(url).endsWith('/Open') ? {required:true,leaseId:'task-open'} : {success:true}),{headers:{'Content-Type':'application/json'}});
    }
    if(String(url).includes('/HostEditor/')) {
        window.hostEditRequests=(window.hostEditRequests||0)+1;
        return new Response(JSON.stringify(window.hostFixture),{headers:{'Content-Type':'application/json'}});
    }
    if(String(url).includes('/RunAsOfProgress')) return new Response(JSON.stringify({completed:false,message:'Task running…',progress:50}),{headers:{'Content-Type':'application/json'}});
    if(String(url).includes('/RunAsOf')) {
        window.runAsOfCalls.push(JSON.parse(options.body));
        if(window.holdRunAsOf) await new Promise(resolve=>window.releaseRunAsOf=resolve);
        return new Response(JSON.stringify({completed:true,restored:true,success:true,message:'Task completed. Original Task settings were restored.',progress:100}),{headers:{'Content-Type':'application/json'}});
    }

    if (String(url).includes('/Tasks/Logs')) {
        window.logCalls.push({url:String(url),cache:options.cache});
        const rows=structuredClone(window.logRows),failed=window.failLogs;
        if(window.holdLogs)await new Promise(resolve=>window.releaseLogs=resolve); // Deliberately ignore abort to exercise stale-response guards.
        return new Response(JSON.stringify(failed?{message:'Logs unavailable.'}:{logs:rows}),{status:failed?503:200,headers:{'Content-Type':'application/json'}});
    }
    if (String(url).includes('/CreateAdvancedFromTraditional')) {
        const payload=JSON.parse(options.body);conversionCalls.push(payload);conversionEvents.push('convert');
        if(window.holdConversion)await new Promise(resolve=>window.releaseConversion=resolve);
        if(window.failConversion==='network')throw new TypeError('Connection lost during conversion');
        if(window.failConversion==='uncertain')return new Response(JSON.stringify({message:'The Advanced Task (ID 900) was created but could not be verified. Inspect Tasks before creating another copy.',outcomeUnknown:true,taskId:'900'}),{status:502,headers:{'Content-Type':'application/json'}});
        if(window.failConversion)return new Response(JSON.stringify({message:'Conversion rejected before creation.'}),{status:400,headers:{'Content-Type':'application/json'}});
        window.fixture=structuredClone(window.convertedFixture);
        return new Response(JSON.stringify({success:true,taskId:fixture.task.id,taskName:fixture.task.name,taskType:'Advanced'}),{headers:{'Content-Type':'application/json'}});
    }
    if (String(url).includes('/SyncAction')) {
        const payload = JSON.parse(options.body); syncActionCalls.push(payload);
        if (window.failSyncAction) return new Response(JSON.stringify({message:'Sync outcome uncertain. Reopen the Task.',moveItSaved:true}),{status:409,headers:{'Content-Type':'application/json'}});
        if (payload.action === 'clear-listings') fixture.task.enabled = false;
        if (payload.action === 'swap') {
            fixture.task.steps.reverse().forEach((step,index) => step.index=index);
            const roles = structuredClone(fixture.task.synchronization.folders);
            fixture.task.synchronization.folders.reverse().forEach((folder,index) => { folder.from=roles[index].from; folder.to=roles[index].to; folder.deletes=roles[index].deletes; });
        }
        return new Response(JSON.stringify({success:true,taskId:fixture.task.id}),{headers:{'Content-Type':'application/json'}});
    }
    if (String(url).includes('/SyncPreview')) {
        previewGets++;
        if (window.failPreview) return new Response(JSON.stringify({message:'Preview unavailable'}),{status:400,headers:{'Content-Type':'application/json'}});
        return new Response(JSON.stringify({canChangeInitialRunOptions:previewCanChange,totalEntries:3,truncated:false,entries:[
            {folderA:{path:'reagentold.xml',size:'1108',timestamp:'2024-01-24 19:48:32',status:'copyfrom'},folderB:null,action:'Copy A → B',noAction:false},
            {folderA:{path:'same.txt',size:'100',timestamp:'2024-01-24 19:48:32',status:'none'},folderB:null,action:'No action',noAction:true},
            {folderA:null,folderB:{path:'<img src=x onerror=window.syncInjected=true>',size:'20',timestamp:'2024-01-24 19:48:32',status:'copyfrom'},action:'Copy B → A',noAction:false}
        ]}),{headers:{'Content-Type':'application/json'}});
    }
    if ((options.method || 'GET') === 'POST') {
        const payload = JSON.parse(options.body); conversionEvents.push('save');saveCalls.push({ url:String(url), payload, headers:options.headers });
        if (window.holdSave) await new Promise(resolve => window.releaseSave=resolve);
        if (window.failNetwork) throw new TypeError('Network disconnected');
        if (window.failCheckoutRelease) return new Response(JSON.stringify({message:"MAUI could not confirm release of the Task '937919810' checkout. Reload the Task before saving again.",moveItSaved:true}),{status:500,headers:{'Content-Type':'application/json'}});
        if (window.rejectSave) return new Response(JSON.stringify({message:'Rejected by MOVEit.'}),{status:400,headers:{'Content-Type':'application/json'}});
        if (window.editConflict) return new Response(JSON.stringify({message:'Task changed. Reopen the editor.',moveItSaved:false,editConflict:true}),{status:409,headers:{'Content-Type':'application/json'}});
        if (window.failSave) return new Response(JSON.stringify({message:'Reopen the Task before retrying.',moveItSaved:true}), {status:409,headers:{'Content-Type':'application/json'}});
        if (payload.syncSettings?.sameSeconds != null) fixture.task.synchronization.sameSeconds = payload.syncSettings.sameSeconds;
        if (payload.task) fixture.task.enabled = payload.task.enabled;
        return new Response(JSON.stringify(window.saveAck ?? {success:true,taskId:fixture.task.id,editVersion:fixture.editVersion}), {headers:{'Content-Type':'application/json'}});
    }
    if (String(url).includes('BrowseFolders')) {
        window.lastBrowseUrl = String(url);
        const currentPath = new URL(url).searchParams.get('path');
        return new Response(JSON.stringify({currentPath,parentPath:'/',items:window.browseItems||[]}),{headers:{'Content-Type':'application/json'}});
    }
    if (window.failShell) return new Response(JSON.stringify({message:'Shell unavailable.'}),{status:503,headers:{'Content-Type':'application/json'}});
    if (window.holdShell) await new Promise(resolve => window.releaseShell=resolve);
    return new Response(JSON.stringify(window.fixture), { headers:{'Content-Type':'application/json'} });
};
window.taskExportDownloads=[];
document.addEventListener('click',event=>{const link=event.target.closest?.('a[download]');if(link?.download.endsWith('.maui')){taskExportDownloads.push(link.download);event.preventDefault();}});
const taskPortability=MauiNativePortability.create({root:document.querySelector('[data-task-portability-root]'),objectType:'Task',exportUrl:'/maui/Tasks/ExportTask',antiForgeryToken:'token',status:(message,_duration,kind)=>MauiModalUI.showMessage(message,kind)});
window.runAsOfCalls=[];window.hostFixture=${JSON.stringify(hostFixtures['SSHFTP-basic'])};
window.MauiTaskHostEditor.initialize(document.querySelector('[data-task-page-config]'),'token');
window.editor = MauiTraditionalTaskEditor.initialize({antiForgeryToken:'token',onExport:(id,name)=>taskPortability.openExport([id],[name]),onCloneTask:async(id,name,type)=>{cloneCalls.push({id,name,type,editorOpen:document.querySelector('[data-traditional-task-editor-dialog]').open,saves:saveCalls.length});},onSaveNotificationTemplate:async draft=>{templateCalls.push(structuredClone(draft));return{id:'template-saved',name:'Saved Template',subject:draft.subject,message:draft.message,format:draft.format};},onDeleteTask:async (id,name) => { deleteCalls.push({id,name,editorOpen:document.querySelector('[data-traditional-task-editor-dialog]').open}); }});
window.loadFixture = async model => { window.logCalls=[];window.logRows=[];window.failLogs=false;window.holdLogs=false; while(document.querySelector('[data-traditional-task-editor-content]').inert) await new Promise(resolve=>setTimeout(resolve,5)); window.saveAck=null; window.fixture = structuredClone(model); window.conversionCalls=[];window.conversionEvents=[];window.failConversion='';window.holdConversion=false;document.querySelector('[data-task-page-config]').dataset.advancedTasksLicensed='true'; window.cloneCalls=[]; window.templateCalls=[]; window.deleteCalls=[]; window.saveCalls = []; window.failSave = false; window.failCheckoutRelease=false; window.rejectSave=false; window.failShell=false; window.holdSave=false; window.failNetwork = false; window.editConflict = false; window.confirmCalls = []; window.confirmAnswer = true; window.syncActionCalls=[]; window.previewGets=0; window.failSyncAction=false; window.failPreview=false; window.previewCanChange=true; return await editor.open(model.task.id,model.task.name,model.task.type); };
window.insertStep = (kind,parentKey='',beforeKey='') => {
    const dialog=document.querySelector('[data-traditional-task-editor-dialog]');dialog.dataset.advancedView='flow';
    const find=()=>[...document.querySelectorAll('[data-advanced-insert]')].find(n=>n.dataset.parentKey===parentKey&&n.dataset.beforeKey===beforeKey);
    let slot=find();if(!slot)throw new Error('Insertion position missing: '+parentKey+'/'+beforeKey);
    while(slot.closest('.advanced-task-flow-group[hidden]')){slot.closest('.advanced-task-flow-group[hidden]').parentElement.querySelector(':scope > .advanced-task-flow-row [data-flow-toggle]').click();slot=find();}
    slot.scrollIntoView({block:'nearest'});slot.click();
    const item=[...document.querySelectorAll('[data-advanced-step-menu] [data-advanced-add-kind]')].find(n=>n.dataset.advancedAddKind===kind);
    if(!item)throw new Error('Insert menu item missing: '+kind);item.click();
};
window.inspectLayout = () => {
    const dialog = document.querySelector('[data-traditional-task-editor-dialog]');
    const failures = [...layoutErrors];
    if (dialog.scrollWidth > dialog.clientWidth + 2) failures.push('Dialog horizontal overflow');
    for (const grid of dialog.querySelectorAll('.details-grid, .is-advanced-task .traditional-task-editor-workspace, .advanced-flow-tree-scroll')) {
        if (grid.closest('[hidden]') || !grid.getClientRects().length) continue;
        if (grid.scrollWidth > grid.clientWidth + 2) failures.push('Form horizontal overflow: '+grid.className+' ('+grid.scrollWidth+'/'+grid.clientWidth+')');
    }
    return failures;
};
window.change = (selector,value) => { const control = document.querySelector(selector); if (control.type === 'checkbox') control.checked = value; else control.value = value; control.dispatchEvent(new Event('input',{bubbles:true})); control.dispatchEvent(new Event('change',{bubbles:true})); };
</script></body></html>`;
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-sync-layout-'));
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
    const result = await command('Runtime.evaluate', { expression: '{' + expression + '}', awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
async function screenshot(name) {
    const image = await command('Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(path.join(output, name + '.png'), Buffer.from(image.data, 'base64'));
}
async function check(name) {
    const failures = await evaluate('inspectLayout()');
    if (failures.length) await screenshot('FAILED-' + name.replace(/[^a-z0-9-]/gi, '-'));
    assert.deepEqual(failures, [], name);
}
(async () => {
    server = http.createServer((_request, response) => { response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end(_request.url.includes('templates=denied') ? html.replace('data-can-save-templates="true"', 'data-can-save-templates="false"').replace('data-moveit-admin="true"', 'data-moveit-admin="false"') : html); });
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
            // Edge briefly holds this file open while starting on Windows.
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
    for (let attempt = 0; attempt < 100 && !await evaluate('Boolean(window.editor)'); attempt++) await pause(50);
    assert.equal(await evaluate('Boolean(window.editor)'), true, 'Editor did not initialize');
    let checks = 0;
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1000,deviceScaleFactor:1,mobile:false});
    for(const previous of [fixtures.Traditional,fixtures['Sync-Share']]) {
        await evaluate(`loadFixture(${JSON.stringify(previous)});`);
        await evaluate(`document.querySelector('[data-traditional-task-editor-cancel]').click()`);
        await evaluate(`window.holdShell=true;window.loadingOpen=loadFixture(${JSON.stringify(fixtures.Advanced)});void 0;`);
        const opening=await evaluate(`(()=>{const d=document.querySelector('[data-traditional-task-editor-dialog]'),r=d.getBoundingClientRect();return{open:d.open,advanced:d.classList.contains('is-advanced-task'),width:r.width,height:r.height};})()`);
        assert.equal(opening.open,true);assert.equal(opening.advanced,true);
        await evaluate(`window.holdShell=false;window.releaseShell();window.loadingOpen`);
        const ready=await evaluate(`(()=>{const r=document.querySelector('[data-traditional-task-editor-dialog]').getBoundingClientRect();return{width:r.width,height:r.height};})()`);
        assert.ok(Math.abs(opening.width-ready.width)<=1&&Math.abs(opening.height-ready.height)<=1,'Advanced geometry is stable from loading to ready');
    }
    await evaluate(`document.querySelector('[data-traditional-task-editor-cancel]').click()`);
    await evaluate(`window.holdShell=true;window.loadingOpen=loadFixture(${JSON.stringify(fixtures.Advanced)});void 0;`);
    await evaluate(`document.querySelector('[data-traditional-task-editor-cancel]').click();`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),false);
    await evaluate(`window.holdShell=false;window.releaseShell();window.loadingOpen`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),false,'Canceled loading cannot reopen the editor');
    await evaluate(`window.holdShell=true;window.fixture=${JSON.stringify(fixtures.Advanced)};window.loadingOpen=editor.open('100');void 0;`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),false,'Unknown type waits for authoritative geometry');
    await evaluate(`window.holdShell=false;window.releaseShell();window.loadingOpen`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').classList.contains('is-advanced-task')`),true);

    for (const width of [1920,1400,1024,800,600]) {
        await command('Emulation.setDeviceMetricsOverride', {width,height:1000,deviceScaleFactor:1,mobile:false});
        for (const theme of ['light','dark']) {
            await evaluate(`document.documentElement.dataset.theme = ${JSON.stringify(theme)}`);
            for (const [name,model] of Object.entries(fixtures)) {
                assert.equal(await evaluate(`loadFixture(${JSON.stringify(model)})`), true, name + ' loads');
                await check(`${name}-${width}-${theme}-folders`); checks++;
                const feedback=await evaluate(`(async () => { const d=document.querySelector('[data-traditional-task-editor-dialog]'),card=d.querySelector('.traditional-task-editor-card'),status=d.querySelector('[data-traditional-task-editor-dirty-state]');const geometry=()=>[...card.children].map(n=>n.getBoundingClientRect().toJSON());const before=geometry();MauiModalUI.showMessage('Exported 1 Task.','default',d);return {before,after:geometry(),message:status.textContent,banners:d.querySelectorAll('[data-maui-modal-feedback]').length};})()`);
                assert.deepEqual(feedback.after,feedback.before,name+' action feedback preserves editor geometry');
                assert.equal(feedback.message,'Exported 1 Task.');assert.equal(feedback.banners,0);checks+=3;
                await evaluate(`MauiModalUI.clear(document.querySelector('[data-traditional-task-editor-dialog]'))`);
                if(model.task.type==='Traditional'&&model.task.steps.length) {
                    await evaluate(`document.querySelector('[data-traditional-task-editor-step-actions]').click();`);
                    assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-traditional-task-editor-step-action="change-host"]')).display`),'none','Traditional menu omits Change Host');
                    await evaluate(`document.querySelector('[data-traditional-task-editor-step-actions]').click();`);checks++;
                }
                const controls = await evaluate(`(()=>{const d=document.querySelector('[data-traditional-task-editor-dialog]'),a=d.querySelector('[data-traditional-task-editor-auto-save]').closest('label'),e=d.querySelector('[data-traditional-task-editor-enabled]').closest('label'),m=d.querySelector('.traditional-task-editor-actions-control'),r=n=>n.getBoundingClientRect();return{first:r(a).right,gap1:r(e).left-r(a).right,gap2:r(m).left-r(e).right,enabledText:e.querySelector('span').textContent,enabledFirst:e.firstElementChild.tagName,footerToggle:!!d.querySelector('.traditional-task-editor-footer [data-editor-field]'),close:d.querySelector('[data-traditional-task-editor-cancel]').textContent};})()`);
                assert.equal(Math.round(controls.gap1),50,name+' Auto Save/Task Enabled spacing');
                assert.equal(Math.round(controls.gap2),50,name+' Task Enabled/Actions spacing');
                assert.equal(controls.enabledText,'Task Enabled');
                assert.equal(controls.enabledFirst,'SPAN');
                assert.equal(controls.footerToggle,false);
                assert.equal(controls.close,'Close');
                checks+=6;
                if(['Traditional','Sync-Share','Advanced'].includes(name)) {
                    await evaluate(`document.querySelector('[data-traditional-task-editor-actions]').click()`);
                    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-actions]').getAttribute('aria-expanded')`),'true');
                    assert.equal(await evaluate(`new DOMMatrixReadOnly(getComputedStyle(document.querySelector('[data-traditional-task-editor-actions] .button-caret')).transform).a`),-1,'Task Actions arrow points up');
                    await evaluate(`document.querySelector('.traditional-task-editor-header').click()`);
                    assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-traditional-task-editor-actions] .button-caret')).transform`),'none');
                    if(name==='Traditional') {
                        await evaluate(`document.querySelector('[data-traditional-task-editor-add-step]').click()`);
                        assert.equal(await evaluate(`new DOMMatrixReadOnly(getComputedStyle(document.querySelector('[data-traditional-task-editor-add-step] .button-caret')).transform).a`),-1);
                        await evaluate(`document.querySelector('[data-traditional-task-editor-actions]').click()`);
                        assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-traditional-task-editor-add-step] .button-caret')).transform`),'none','Opening another menu resets Add Step');
                        await evaluate(`document.querySelector('[data-traditional-task-editor-actions]').click()`);
                    }
                    if(name!=='Advanced') {
                        await evaluate(`document.querySelector('[data-traditional-task-editor-section-button=next-actions]').click();document.querySelector('[data-traditional-task-editor-add-next-action]').click()`);
                        assert.equal(await evaluate(`new DOMMatrixReadOnly(getComputedStyle(document.querySelector('[data-traditional-task-editor-add-next-action] .button-caret')).transform).a`),-1);
                        await evaluate(`document.querySelector('.traditional-task-editor-header').click();document.querySelector('[data-traditional-task-editor-section-button=steps]').click()`);
                        assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-traditional-task-editor-add-next-action] .button-caret')).transform`),'none');
                    }
                    assert.equal(await evaluate(`getComputedStyle(document.querySelector('.traditional-task-editor-rail-section.active > .traditional-task-editor-rail-heading')).boxShadow.includes('inset')`),true,'Task navigation uses the Host accent');
                    checks+=4;
                }
                if (name === 'Sync-Share' && [1400,600].includes(width)) await screenshot(name+'-'+width+'-'+theme+'-folder-a');
                if (name.startsWith('Sync-')) {
                    assert.equal(await evaluate(`document.querySelector('[data-task-editor-kind-label]').textContent`), 'Synchronization Task Editor');
                    await evaluate(`document.querySelector('[data-traditional-task-editor-actions]').click()`);
                    assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-traditional-task-editor-transfer-exceptions]')).display`),'none');
                    assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-traditional-task-editor-create-advanced]')).display`),'none');
                    assert.notEqual(await evaluate(`getComputedStyle(document.querySelector('[data-sync-action="swap"]')).display`),'none');
                    await evaluate(`document.querySelector('[data-traditional-task-editor-actions]').click()`);
                    assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-traditional-task-editor-step-items] strong')].map(node=>node.textContent)`), ['Folder A','Folder B']);
                    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-add-step]').disabled`), true);
                    const patches = await evaluate('editor.getStepPatches()');
                    assert.equal(patches.length,2);
                    assert.ok(patches.every(patch=>patch.kind==='Source' && patch.patch && !('SyF' in patch.patch) && !('NewFilesOnly' in patch.patch) && !('UseWinCopyFileAPI' in patch.patch)));
                    assert.equal(patches[1].patch.UseDefPreserveFileTimestamp,0);
                    assert.equal(patches[1].patch.PreserveFileTimestamp,1);
                    assert.equal(await evaluate(`document.querySelector('[data-source-field="collectOnlyNewFiles"]').closest('label').hidden`), true);
                    await evaluate(`document.querySelector('[data-traditional-task-editor-section-button="sync-settings"]').click()`);
                    await check(`${name}-${width}-${theme}-settings`); checks++;
                    assert.equal(await evaluate(`document.querySelector('[data-sync-direction]').value`),name==='Sync-Bidirectional' ? 'bidirectional' : 'unidirectional');
                    assert.equal(await evaluate(`document.querySelector('[data-sync-deletes]').value`),'1');
                    assert.equal(await evaluate(`document.querySelector('[data-sync-deletes]').scrollWidth <= document.querySelector('[data-sync-deletes]').clientWidth + 1`),true);
                    assert.equal(await evaluate(`document.querySelector('[data-source-field="hostId"]').disabled`),false);
                    if (name==='Sync-Share') await screenshot(name+'-'+width+'-'+theme);
                    await evaluate(`document.querySelector('[data-traditional-task-editor-section-button="steps"]').click(); document.querySelectorAll('[data-traditional-task-editor-step-items] button')[1].click()`);
                    await check(`${name}-${width}-${theme}-folder-b`); checks++;
                    if (name !== 'Sync-FileSystem') {
                        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-host-tab="download-limits"]').hidden`),true);
                        if (name==='Sync-Share') assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-host-tab="transfer"]').hidden`),true);
                        await evaluate(`document.querySelector('[data-traditional-task-editor-source-tab="host-overrides"]').click()`);
                        await check(`${name}-${width}-${theme}-overrides`); checks++;
                    }
                } else if (name.startsWith('Advanced') || name === 'NewAdvanced') {
                    assert.equal(await evaluate(`document.querySelector('[data-task-editor-kind-label]').textContent`),'Advanced Task Editor');
                    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-section=next-actions]').hidden`),true);
                    assert.equal(await evaluate(`(()=>{const r=document.querySelector('.traditional-task-editor-rail');return r.scrollWidth<=r.clientWidth+1;})()`),true,'Advanced rail has no horizontal overflow');
                    await evaluate(`document.querySelector('[data-advanced-view=flow]').click()`);
                    assert.equal(await evaluate(`(() => {const flow=document.querySelector('[data-advanced-flow-tree]').getBoundingClientRect();return [...document.querySelectorAll('.advanced-task-flow-terminal')].every(n=>{const r=n.getBoundingClientRect();return Math.abs((r.left+r.right-flow.left-flow.right)/2)<1;});})()`),true,'Start/End pills are centered');
                    if(width>1200)assert.equal(await evaluate(`document.querySelector('.traditional-task-editor-rail').getBoundingClientRect().width`),285,'Advanced navigation uses the Traditional 285px standard');
                    if(width>780)assert.ok(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').clientWidth`)>width*.9,'Advanced modal scales with the window');
                    if(name.startsWith('Advanced')) {
                        for(const kind of ['For','If','RunTask','UpdOrig','Comment']) {
                            await evaluate(`document.querySelector('[data-advanced-view=flow]').click();document.querySelector('[data-flow-key="${kind==='For'?'12':kind==='If'?'13':kind==='RunTask'?'next-action:19':kind==='UpdOrig'?'20':'21'}"] > .advanced-task-flow-row').click();`);
                            await check(`${name}-${width}-${theme}-${kind}`);checks++;
                            assert.equal(await evaluate(`document.querySelector('.traditional-task-editor-rail-section.active')?.dataset.traditionalTaskEditorSection`),'steps','All Advanced step types retain the active Steps navigation accent');

                            const titleStyle = await evaluate(`(() => { const heading=document.querySelector('${kind==='RunTask'?'[data-traditional-task-editor-next-action-title]':'[data-advanced-inspector] h3'}'), reference=document.querySelector('[data-traditional-task-editor-next-action-title]'); const pick=n=>{const c=getComputedStyle(n);return [c.fontSize,c.fontWeight,c.fontFamily,c.lineHeight,c.color];};return [pick(heading),pick(reference)]; })()`);
                            assert.deepEqual(titleStyle[0],titleStyle[1],'All Advanced headings match Run Task');
                            if(kind==='If') {
                                assert.equal(await evaluate(`(() => {const button=document.querySelector('.advanced-criterion-remove');return getComputedStyle(button).fontSize==='0px'&&getComputedStyle(button,'::after').maskImage!=='none';})()`),true,'Criterion remove uses one centered icon, without a duplicate text glyph');
                                assert.equal(await evaluate(`(()=>{const row=document.querySelector('[data-flow-key="13"] > .advanced-task-flow-row');return document.querySelector('[data-advanced-inspector] h3').textContent==='If '+row.querySelector('.advanced-task-flow-summary').textContent;})()`),true);
                                assert.equal(await evaluate(`(()=>{const row=document.querySelector('.advanced-criterion'),controls=[...row.querySelectorAll('.field-block > select,.field-block > input,.advanced-criterion-remove')];return controls.length===4&&Math.max(...controls.map(c=>c.getBoundingClientRect().top))-Math.min(...controls.map(c=>c.getBoundingClientRect().top))<=2;})()`),true,'Criterion controls share one row');
                                assert.equal(await evaluate(`(()=>{const add=document.querySelector('.advanced-criterion-add'),last=[...document.querySelectorAll('.advanced-criterion')].at(-1);return add.getBoundingClientRect().top-last.getBoundingClientRect().bottom>=15;})()`),true,'Add Criterion has separator spacing');
                            }
                            if(kind==='If'&&[1920,1400,600].includes(width))await screenshot(`${name}-${width}-${theme}-If`);
                            if(kind==='RunTask') {assert.equal(await evaluate(`(()=>{const task=document.querySelector('[data-traditional-task-editor-next-action-task]').getBoundingClientRect(),wait=document.querySelector('[data-advanced-wait]').getBoundingClientRect();return wait.top>=task.bottom&&wait.left>=task.left-1;})()`),true,'Wait sits below Task to Run');assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-kicker]').hidden`),true);assert.equal(await evaluate(`document.querySelector('[data-advanced-wait]').hidden`),false);assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-tab=conditions]')`),null);assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-conditions]').hidden`),true);}
                        }
                        if([1400,600].includes(width))await screenshot(`${name}-${width}-${theme}`);
                    }
                } else if (name === 'Traditional') {
                    assert.equal(await evaluate(`document.querySelector('[data-task-editor-kind-label]').textContent`),'Traditional Task Editor');
                    await evaluate(`document.querySelector('[data-traditional-task-editor-actions]').click()`);
                    assert.notEqual(await evaluate(`getComputedStyle(document.querySelector('[data-traditional-task-editor-transfer-exceptions]')).display`),'none');
                    assert.notEqual(await evaluate(`getComputedStyle(document.querySelector('[data-traditional-task-editor-create-advanced]')).display`),'none');
                    assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-sync-action="swap"]')).display`),'none');
                    await evaluate(`document.querySelector('[data-traditional-task-editor-actions]').click()`);
                    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-source-tab="after-transfer"]').hidden`),false);
                    assert.equal(await evaluate(`document.querySelector('[data-source-field="collectOnlyNewFiles"]').closest('label').hidden`),false);
                    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-host-tab="download-limits"]').hidden`),false);
                    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-host-tab="transfer"]').hidden`),false);
                    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-source-tab="host-overrides"]').hidden`),false);
                }
                for (const tab of ['properties','advanced']) {
                    await evaluate(`document.querySelector('[data-traditional-task-editor-tab="${tab}"]').click()`);
                    await check(`${name}-${width}-${theme}-${tab}`); checks++;
                    assert.equal(await evaluate(`(() => {const p=document.querySelector('[data-traditional-task-editor-panel="${tab}"]');return [...p.querySelectorAll('.details-section')].every(n=>{const c=getComputedStyle(n);return n.classList.contains('maui-property-card')&&c.paddingTop==='18px'&&c.borderBottomWidth==='1px'&&c.borderRadius==='10px';});})()`),true,'Task Properties/Advanced use Host property groups');
                    if(tab==='properties')assert.equal(await evaluate(`(() => {const h=document.querySelector('[data-traditional-task-editor-panel="properties"] .maui-object-properties-heading'),rows=[...h.querySelectorAll('.task-identity-item')].map(n=>n.getBoundingClientRect()),b=h.querySelector('.source-badge').getBoundingClientRect(),r=h.getBoundingClientRect();return rows.length===2&&rows[1].left>=rows[0].right&&Math.abs(rows[0].top-rows[1].top)<1&&[...h.querySelectorAll('.task-identity-item')].every(n=>{const label=n.firstElementChild.getBoundingClientRect(),value=n.lastElementChild.getBoundingClientRect();return value.top>=label.bottom&&Math.abs(label.left-value.left)<1&&getComputedStyle(n).textAlign==='left';})&&Math.abs(b.right-r.right)<1&&Math.abs(b.top-r.top)<1&&[...document.querySelectorAll('[data-traditional-task-editor-panel="properties"] .source-badge')].every(n=>getComputedStyle(n).fontSize==='10px'&&getComputedStyle(n).lineHeight==='12px'&&getComputedStyle(n).padding==='3px 8px'&&getComputedStyle(n).fontWeight==='700');})()`),true,'Task Properties match Host identity and shared source typography');
                    if(tab==='properties')assert.equal(await evaluate(`(() => {const p=document.querySelector('[data-traditional-task-editor-panel="properties"]'),audit=[...p.querySelectorAll('.audit-item > input')];return p.querySelector('.audit-item > strong')===null&&audit.length===4&&audit.every(n=>n.readOnly&&n.parentElement.tagName==='LABEL');})()`),true,'Task Properties audit values use labeled readonly textboxes');
                    if(tab==='properties')assert.equal(await evaluate(`[...document.querySelectorAll('[data-traditional-task-editor-panel="properties"] .details-section-heading h3')].every(n=>getComputedStyle(n).fontSize==='15px')`),true,'Task Properties and metadata titles share 15px typography');
                    if(tab==='advanced')assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-panel="advanced"] .source-badge')===null`),true,'Advanced settings have no source pill');
                    if(['Traditional','Advanced'].includes(name)&&[1400,600].includes(width))await screenshot(`${name}-${tab}-${width}-${theme}`);

                }
            }
        }
    }
    // Export transport completion/errors must use the same footers for all Task kinds.
    for(const name of ['Traditional','Advanced','Sync-Share']) for(const theme of ['light','dark']) for(const width of [1400,600]) {
        await command('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)};window.taskExportDownloads=[];window.taskExportMode=''`);
        await evaluate(`loadFixture(${JSON.stringify(fixtures[name])})`);
        await evaluate(`document.querySelector('[data-traditional-task-editor-export]').click()`);
        for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-task-portability-root] dialog').open`);i++)await pause(20);
        for(const [mode,message] of [['http','Export denied.'],['plain','Unable to export Task (502).'],['malformed','Unable to export Task (502).'],['empty','The Task export was empty.'],['network','Unable to reach the export server.']]) {
            await evaluate(`window.taskExportMode=${JSON.stringify(mode)};document.querySelector('[data-task-portability-root] form').requestSubmit()`);
            for(let i=0;i<100&&await evaluate(`document.querySelector('[data-task-portability-root] [data-native-portability-export-error]').textContent!==${JSON.stringify(message)}`);i++)await pause(20);
            assert.equal(await evaluate(`document.querySelector('[data-task-portability-root] [data-native-portability-export-error]').textContent`),message,name+' '+mode);
            assert.equal(await evaluate(`document.querySelector('[data-task-portability-root] dialog').open`),true);
            assert.equal(await evaluate(`(()=>{const e=document.querySelector('[data-task-portability-root] [data-native-portability-export-error]'),f=e.closest('.settings-footer'),r=e.getBoundingClientRect(),b=f?.getBoundingClientRect();return !!b&&!e.hidden&&r.left>=b.left&&r.right<=b.right&&r.top>=b.top&&r.bottom<=b.bottom})()`),true,'Export error fits its footer at '+width+'/'+theme);
            assert.equal(await evaluate(`taskExportDownloads.length`),0);checks+=4;
        }
        await evaluate(`window.taskExportMode='';document.querySelector('[data-task-portability-root] form').requestSubmit()`);
        for(let i=0;i<100&&await evaluate(`document.querySelector('[data-task-portability-root] dialog').open`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-task-portability-root] dialog').open`),false);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent`),'Exported 1 Task.');
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').classList.contains('error')`),false);
        assert.deepEqual(await evaluate('taskExportDownloads'),['Task.maui']);
        assert.equal(await evaluate(`document.querySelectorAll('[data-traditional-task-editor-dialog] [data-maui-modal-feedback]').length`),0);checks+=5;
    }
    // Exercise the production Actions download with a dirty, collapsed, nested draft.
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1000,deviceScaleFactor:1,mobile:false});
    await evaluate(`loadFixture(${JSON.stringify(fixtures.AdvancedBranches)})`);
    await evaluate(`
        window.flowDownloads=[];window.flowDrawnText=[];window.flowExportStarted=Date.now();
        window.originalAnchorClick=HTMLAnchorElement.prototype.click;
        HTMLAnchorElement.prototype.click=function(){if(this.download.endsWith('-task-flow.png'))flowDownloads.push({name:this.download,url:this.href});else originalAnchorClick.call(this);};
        window.originalFillText=CanvasRenderingContext2D.prototype.fillText;
        CanvasRenderingContext2D.prototype.fillText=function(text,...args){flowDrawnText.push(String(text));return originalFillText.call(this,text,...args);};
        document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').click();
        const input=document.querySelector('[data-advanced-inspector] textarea');input.value='UNSAVED export review';input.dispatchEvent(new Event('input',{bubbles:true}));
        change('[data-editor-field="name"]','Flow: review / draft');
        const loop=document.querySelector('[data-flow-key="12"]');if(loop.getAttribute('aria-expanded')==='true')loop.querySelector('[data-flow-toggle]').click();
        window.beforeExport=document.querySelector('[data-advanced-flow-tree]').innerHTML;
        window.beforeExportScroll=document.querySelector('.advanced-flow-tree-scroll').scrollTop;
        window.expectedExportLabels=[...document.querySelectorAll('.advanced-task-flow-label')].map(n=>n.textContent);
        document.querySelector('[data-traditional-task-editor-actions]').click();document.querySelector('[data-advanced-save-flow]').click();
    `);
    for(let i=0;i<100&&!await evaluate('flowDownloads.length');i++)await pause(20);
    assert.equal(await evaluate('flowDownloads.length'),1,'Save Task Flow downloads a PNG');
    assert.equal(await evaluate('flowDownloads[0].name'),'Flow_ review _ draft-task-flow.png');
    const image = await evaluate(`(async()=>{const blob=await (await realFetch(flowDownloads[0].url)).blob();const bitmap=await createImageBitmap(blob);const result={type:blob.type,width:bitmap.width,height:bitmap.height,data:await new Promise(resolve=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result.split(',')[1]);reader.readAsDataURL(blob);})};bitmap.close();return result;})()`);
    assert.equal(image.type,'image/png');assert.ok(image.width>=2080);assert.ok(image.height>1000,'Export extends beyond the visible panel');
    const bytes=Buffer.from(image.data,'base64');assert.equal(bytes.subarray(0,8).toString('hex'),'89504e470d0a1a0a');assert.ok(bytes.length>10000,'PNG contains rendered content');
    fs.writeFileSync(path.join(output,'Advanced-full-flow-export.png'),bytes);
    assert.equal(await evaluate(`['Start','End','UNSAVED export review','If','Else If','Otherwise'].every(t=>flowDrawnText.some(line=>line.includes(t)))`),true,'Image includes terminals, dirty text and collapsed branches');
    assert.equal(await evaluate(`expectedExportLabels.every(label=>flowDrawnText.includes(label))`),true,'Every step is rendered');
    assert.equal(await evaluate(`flowDrawnText.some(line=>/MAUI.*ADVANCED TASK FLOW|Current editor draft/i.test(line))`),false,'Export omits the banner and draft label');
    assert.equal(await evaluate(`(()=>{const line=flowDrawnText.find(text=>text.startsWith('Task ID:'));if(!line)return false;const stamp=line.slice(line.lastIndexOf(' · ')+3);for(let at=flowExportStarted;at<=Date.now()+1000;at+=1000)if(new Date(at).toLocaleString()===stamp)return true;return false;})()`),true,'Export header shows the current local date and time');
    assert.equal(await evaluate(`document.querySelector('[data-advanced-flow-tree]').innerHTML===beforeExport`),true,'Live selection/collapse state unchanged');
    assert.equal(await evaluate(`document.querySelector('.advanced-flow-tree-scroll').scrollTop===beforeExportScroll`),true);
    assert.equal(await evaluate(`saveCalls.length`),0,'Image export does not save the Task');
    assert.equal(await evaluate(`(async()=>{try{await MauiAdvancedTaskFlow.renderImage([{kind:'Comment',label:'Comment',summary:Array(3000).fill('line').join(String.fromCharCode(10))}]);return false;}catch(e){return /too large/.test(e.message);}})()`),true,'Oversized image fails without truncation');
    await evaluate(`window.realRenderImage=MauiAdvancedTaskFlow.renderImage;MauiAdvancedTaskFlow.renderImage=async()=>{throw new Error('Unable to create review image.');};document.querySelector('[data-advanced-save-flow]').click();`);
    for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent.includes('review image')`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').closest('dialog').open`),true,'Export error stays in modal footer');
    await evaluate(`MauiAdvancedTaskFlow.renderImage=realRenderImage;CanvasRenderingContext2D.prototype.fillText=originalFillText;HTMLAnchorElement.prototype.click=originalAnchorClick;`);
    checks+=16;
    // (+) menu cancellation preserves selection and draft; each insertion uses the clicked position.
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1000,deviceScaleFactor:1,mobile:false});
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)});`);
    await evaluate(`new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));`);
    await evaluate(`document.querySelector('[data-advanced-insert]').scrollIntoView({block:'nearest'});`);
    await evaluate(`new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));`);
    const insertPoint=await evaluate(`(()=>{const r=document.querySelector('[data-advanced-insert]').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2};})()`);
    await command('Input.dispatchMouseEvent',{type:'mouseMoved',x:insertPoint.x,y:insertPoint.y});
    await command('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,x:insertPoint.x,y:insertPoint.y});
    await command('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,x:insertPoint.x,y:insertPoint.y});
    await evaluate(`new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));`);
    const insertMenu=await evaluate(`(()=>{const m=document.querySelector('[data-advanced-step-menu]'),slot=document.querySelector('[data-advanced-insert]');return{count:m.querySelectorAll('[data-advanced-add-kind]').length,hidden:m.hidden,kind:m.dataset.advancedMenuKind,slotDisabled:slot.disabled};})()`);
    if(insertMenu.count!==9)await screenshot('FAILED-Advanced-insert-menu');
    assert.equal(insertMenu.count,9,JSON.stringify(insertMenu));
    assert.equal(await evaluate(`document.querySelector('[data-advanced-step-menu]').dataset.advancedMenuKind`),'insert');
    await screenshot('Advanced-insert-menu-1400');
    await evaluate(`document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}));`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),true);
    assert.equal(await evaluate(`document.activeElement.matches('[data-advanced-insert]')`),true);
    assert.equal(await evaluate(`document.querySelector('.advanced-flow-toolbar')===null`),true);
    for(const [parent,before,path] of [['','first','root'],['13:when:0','','if'],['17:when:1','24','elseif'],['17:otherwise','','otherwise']]) {
        const model=parent.startsWith('17')?fixtures.AdvancedBranches:fixtures.Advanced;
        await evaluate(`loadFixture(${JSON.stringify(model)});`);
        const beforeKey=before==='first'?await evaluate(`document.querySelector('[data-flow-key]').dataset.flowKey`):before;
        await evaluate(`insertStep('Comment',${JSON.stringify(parent)},${JSON.stringify(beforeKey)});const c=document.querySelector('[data-advanced-inspector] textarea');c.value='Inserted '+${JSON.stringify(path)};c.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-traditional-task-editor-save]').click();`);
        for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
        const expression=path==='root'?'[0]':path==='if'?'[1].steps[0].when[0].steps.at(-1)':path==='elseif'?'[3].steps[2].when[1].steps[0]':'[3].steps[2].otherwise.steps.at(-1)';
        assert.equal(await evaluate('saveCalls[0].payload.advancedFlow'+expression+'.fields.Text'),'Inserted '+path);
    }
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`document.querySelector('[data-flow-key="13"] > .advanced-task-flow-row').click();const c=document.querySelector('.advanced-criterion .field-block:nth-child(3) input');c.value='changed.txt';c.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.match(await evaluate(`document.querySelector('[data-advanced-inspector] h3').textContent`),/changed\.txt/);
    assert.equal(await evaluate(`document.querySelector('[data-advanced-inspector] h3').textContent`),await evaluate(`'If '+document.querySelector('[data-flow-key="13"] > .advanced-task-flow-row .advanced-task-flow-summary').textContent`));
    // Native-style branches have one visual level; each row opens its own criteria directly.
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1000,deviceScaleFactor:1,mobile:false});
    await evaluate(`loadFixture(${JSON.stringify(fixtures.AdvancedBranches)})`);
    assert.deepEqual(await evaluate(`['17','17:when:1','17:otherwise'].map(k=>document.querySelector('[data-flow-key="'+k+'"]')?.parentElement.closest('[data-flow-key]')?.dataset.flowKey)`),['12','12','12']);
    assert.equal(await evaluate(`document.querySelector('[data-flow-key="17:when:0"]')===null`),true,'No duplicate If wrapper/branch row');
    for(const [key,count,title] of [['17',4,'If'],['17:when:1',1,'Else If'],['17:otherwise',0,'Otherwise']]) {
        await evaluate(`document.querySelector('[data-flow-key="${key}"] > .advanced-task-flow-row').click();`);
        assert.equal(await evaluate(`document.querySelectorAll('[data-advanced-inspector] .advanced-criterion').length`),count);
        assert.ok((await evaluate(`document.querySelector('[data-advanced-inspector] h3').textContent`)).startsWith(title),title+' heading');
    }
    assert.equal(await evaluate(`document.querySelector('[data-advanced-move-target]')===null`),true);
    // Menus are tied to the clicked row, and stay inside the viewport at the bottom of the tree.
    await evaluate(`document.querySelector('[data-flow-key="17"] > .advanced-task-flow-row [data-advanced-step-actions]').click();`);
    assert.equal(await evaluate(`document.querySelector('[data-advanced-step-menu] [data-advanced-action=add-otherwise]').disabled`),true);
    assert.equal(await evaluate(`document.querySelector('[data-advanced-step-menu] [data-advanced-action=edit-branch]')===null`),true);
    assert.equal(await evaluate(`(()=>{const r=document.querySelector('[data-advanced-step-menu]').getBoundingClientRect(),d=document.querySelector('[data-traditional-task-editor-dialog]').getBoundingClientRect();return r.top>=d.top&&r.left>=d.left&&r.right<=d.right&&r.bottom<=d.bottom;})()`),true);
    await screenshot('AdvancedBranches-1400-menu');
    await evaluate(`document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}));`);
    assert.equal(await evaluate(`document.querySelector('[data-advanced-step-menu]').hidden`),true);
    assert.equal(await evaluate(`document.activeElement.matches('[data-advanced-step-actions]')`),true);
    // Drag and keyboard divider changes are presentation only, and retain form space on resize.
    const originalWidth=await evaluate(`document.querySelector('[data-advanced-flow-pane]').clientWidth`);
    await evaluate(`const d=document.querySelector('[data-advanced-divider]');d.focus();d.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true}));`);
    assert.ok(await evaluate(`document.querySelector('[data-advanced-flow-pane]').clientWidth`)>originalWidth);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),true);
    const divider=await evaluate(`(()=>{const r=document.querySelector('[data-advanced-divider]').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+20};})()`);
    await command('Input.dispatchMouseEvent',{type:'mouseMoved',x:divider.x,y:divider.y});
    await command('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,x:divider.x,y:divider.y});
    await command('Input.dispatchMouseEvent',{type:'mouseMoved',button:'left',buttons:1,x:divider.x+50,y:divider.y});
    await command('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,x:divider.x+50,y:divider.y});
    assert.ok(await evaluate(`document.querySelector('[data-advanced-flow-pane]').clientWidth`)>originalWidth+30);
    // Remove the first branch: the next branch is promoted to If without losing its native source index.
    await evaluate(`document.querySelector('[data-flow-key="17"] > .advanced-task-flow-row [data-advanced-step-actions]').click();document.querySelector('[data-advanced-step-menu] [data-advanced-action=delete-branch]').click();`);
    for(let i=0;i<100&&!await evaluate('confirmCalls.length');i++)await pause(10);
    assert.equal(await evaluate(`document.querySelector('[data-flow-key="17"] .advanced-task-flow-label').textContent`),'If');
    assert.equal(await evaluate(`document.querySelectorAll('[data-advanced-inspector] .advanced-criterion').length`),1);
    await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[3].steps[2].when.length`),1);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[3].steps[2].when[0].sourceIndex`),1);
    // Host-specific actions retain their established availability and handlers in the row menu.
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`document.querySelector('[data-flow-key] > .advanced-task-flow-row [data-advanced-step-actions]').click();`);
    assert.equal(await evaluate(`Boolean(document.querySelector('[data-advanced-step-menu] [data-advanced-action=source-timestamps]'))`),true);
    assert.equal(await evaluate(`document.querySelector('[data-advanced-step-menu] [data-advanced-action=source-timestamps]').disabled`),true);
    // Add alternate/default branches through their row menus and submit the shared draft.
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`document.querySelector('[data-flow-key="13"] > .advanced-task-flow-row [data-advanced-step-actions]').click();document.querySelector('[data-advanced-step-menu] [data-advanced-action=add-elseif]').click();`);
    assert.match(await evaluate(`document.querySelector('[data-advanced-inspector] h3').textContent`),/^Else If /);
    await evaluate(`document.querySelector('[data-flow-key="13"] > .advanced-task-flow-row [data-advanced-step-actions]').click();document.querySelector('[data-advanced-step-menu] [data-advanced-action=add-otherwise]').click();document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[1].steps[0].when.length`),2);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[1].steps[0].otherwise.steps.length`),0);
    // Roving keyboard focus must survive the live selection adapter's redraws.
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1000,deviceScaleFactor:1,mobile:false});
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    // Enter through the roving tab stop so focus and selection agree before a redraw.
    // Focusing an unselected tabindex=-1 loop raced the previous Save status refresh.
    await evaluate(`document.querySelector('[data-flow-key][tabindex="0"]').focus();document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowDown',bubbles:true}));`);
    assert.equal(await evaluate(`document.activeElement.dataset.flowKey`),'12');
    assert.equal(await evaluate(`document.activeElement.getAttribute('aria-selected')`),'true');
    await evaluate(`document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowLeft',bubbles:true}));`);
    assert.equal(await evaluate(`document.activeElement.dataset.flowKey`),'12');
    assert.equal(await evaluate(`document.activeElement.getAttribute('aria-expanded')`),'false');
    await evaluate(`document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowDown',bubbles:true}));`);
    assert.equal(await evaluate(`document.activeElement.dataset.flowKey`),'21','Keyboard focus follows selection across a collapsed loop');
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`document.querySelector('[data-flow-key="13"] > .advanced-task-flow-row').click();`);
    assert.equal(await evaluate(`document.querySelectorAll('[data-advanced-inspector] .advanced-criterion').length`),2);
    await evaluate(`const select=document.querySelector('[data-advanced-inspector] select');select.value='Or';select.dispatchEvent(new Event('change',{bubbles:true}));document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[1].steps[0].when[0].criteria.Or.comp.length`),2);
    // Captured text NOTMASK is editable, readable and saved for filename and custom macros.
    for(const item of ['[OrigName]','[TaskName]']) {
        const model=structuredClone(fixtures.Advanced);
        model.task.advancedFlow[1].steps[0].when[0].criteria={And:{comp:[{a:'[ErrorCodeFile]',b:0,test:'NEQ'},{a:item,b:'file2.txt',test:'NOTMASK'}]}};
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        await evaluate(`document.querySelector('[data-flow-key="13"] > .advanced-task-flow-row').click()`);
        const textOptions=await evaluate(`Array.from(document.querySelectorAll('[data-advanced-inspector] .advanced-criterion')[1].querySelectorAll('select')[1].options).map(o=>[o.value,o.textContent])`);
        assert.ok(textOptions.some(([value,label])=>value==='MASK'&&label==='Matches'));
        assert.ok(textOptions.some(([value,label])=>value==='NOTMASK'&&label==='Not Matches'));
        assert.equal(textOptions.some(([,label])=>label==='Matches mask'),false);
        assert.equal(await evaluate(`document.querySelectorAll('[data-advanced-inspector] .advanced-criterion')[1].querySelectorAll('select')[1].value`),'NOTMASK');
        assert.match(await evaluate(`document.querySelector('[data-flow-key="13"] .advanced-task-flow-summary').textContent`),/not matches file2\.txt/);
        // Toggle both operators, then change the value so returning to NOTMASK remains a real edit.
        await evaluate(`const select=document.querySelectorAll('[data-advanced-inspector] .advanced-criterion')[1].querySelectorAll('select')[1];select.value='MASK';select.dispatchEvent(new Event('change',{bubbles:true}))`);
        await evaluate(`const select=document.querySelectorAll('[data-advanced-inspector] .advanced-criterion')[1].querySelectorAll('select')[1];select.value='NOTMASK';select.dispatchEvent(new Event('change',{bubbles:true}))`);
        await evaluate(`const input=Array.from(document.querySelectorAll('[data-advanced-inspector] .advanced-criterion')[1].querySelectorAll('input')).at(-1);input.value='*.bak';input.dispatchEvent(new Event('input',{bubbles:true}))`);
        await check(item==='[OrigName]'?'Not-Matches-filename':'Not-Matches-custom-macro');
        await screenshot(item==='[OrigName]'?'Not-Matches-filename':'Not-Matches-custom-macro');
        await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click()`);
        for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
        assert.deepEqual(await evaluate(`saveCalls[0].payload.advancedFlow[1].steps[0].when[0].criteria.And.comp[1]`),{a:item,b:'*.bak',test:'NOTMASK'});
        checks+=6;
    }
    // Insertion controls are destinations, never drag sources, including inside loops and branches.
    for(const theme of ['light','dark']) {
        await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1000,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)};loadFixture(${JSON.stringify(fixtures.AdvancedBranches)})`);
        assert.equal(await evaluate(`(()=>{const slots=[...document.querySelectorAll('[data-advanced-insert]')];return slots.length>0&&slots.every(slot=>!slot.draggable&&!slot.closest('[draggable="true"]'));})()`),true,'Insertion controls have no draggable ancestor');checks++;
        assert.equal(await evaluate(`(()=>{window.beforeInsertDrag=[...document.querySelectorAll('[data-flow-key]')].map(n=>n.dataset.flowKey);return [...document.querySelectorAll('[data-advanced-insert]')].every(slot=>{const transfer=new DataTransfer();const accepted=slot.dispatchEvent(new DragEvent('dragstart',{bubbles:true,cancelable:true,dataTransfer:transfer}));slot.dispatchEvent(new DragEvent('drop',{bubbles:true,dataTransfer:transfer}));return !accepted&&!transfer.getData('application/x-maui-advanced-step');});})()`),true,'Every insertion dragstart is rejected without supplying a step key');checks++;
        assert.equal(await evaluate(`(()=>{const item=document.querySelector('[data-flow-key="12"]'),transfer=new DataTransfer();return !item.draggable&&!item.dispatchEvent(new DragEvent('dragstart',{bubbles:true,cancelable:true,dataTransfer:transfer}))&&!transfer.getData('application/x-maui-advanced-step');})()`),true,'Container padding cannot drag an entire subtree');checks++;
        assert.deepEqual(await evaluate('confirmCalls'),[]);checks++;
        await evaluate(`window.nativeInsertDrags=0;document.querySelector('[data-advanced-flow-tree]').addEventListener('dragstart',event=>{if(!event.defaultPrevented)nativeInsertDrags++;});`);
        for(const parent of ['', '12','17:when:0','17:when:1','17:otherwise']) {
            await evaluate(`const menu=document.querySelector('[data-advanced-step-menu]');menu.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}));`);
            const point=await evaluate(`(()=>{const find=()=>[...document.querySelectorAll('[data-advanced-insert]')].find(n=>n.dataset.parentKey===${JSON.stringify(parent)});let slot=find();if(!slot)throw new Error('Missing insert slot');while(slot.closest('[hidden]')){slot.closest('[hidden]').parentElement.querySelector(':scope > .advanced-task-flow-row [data-flow-toggle]').click();slot=find();}slot.scrollIntoView({block:'center'});const r=slot.getBoundingClientRect();return{x:r.left+18,y:r.top+r.height/2};})()`);
            await command('Input.dispatchMouseEvent',{type:'mousePressed',x:point.x,y:point.y,button:'left',buttons:1,clickCount:1});
            await pause(120);
            await command('Input.dispatchMouseEvent',{type:'mouseMoved',x:point.x+32,y:point.y+8,button:'left',buttons:1});
            await command('Input.dispatchMouseEvent',{type:'mouseReleased',x:point.x+32,y:point.y+8,button:'left',buttons:0,clickCount:1});
            assert.equal(await evaluate('nativeInsertDrags'),0,'Click-and-hold on '+(parent||'root')+' + never starts a native drag');checks++;
        }
        await evaluate(`document.querySelector('[data-advanced-step-menu]').dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}));`);
        assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-flow-key]')].map(n=>n.dataset.flowKey)`),await evaluate('beforeInsertDrag'),'No steps moved');checks++;
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-error]').hidden`),true,'No self-move warning');checks++;
        assert.equal(await evaluate('saveCalls.length'),0);checks++;
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent`),'No unsaved changes.');checks++;
        await evaluate(`const loop=document.querySelector('[data-flow-key="12"]');loop.querySelector(':scope > .advanced-task-flow-row [data-flow-toggle]').click();document.querySelector('[data-flow-key="12"] > .advanced-task-flow-row [data-flow-toggle]').click();`);
        assert.equal(await evaluate(`document.querySelector('[data-flow-key="12"] > .advanced-task-flow-row').draggable`),true,'Expand/collapse preserves step row dragging');checks++;
        assert.equal(await evaluate(`document.querySelector('[data-flow-key="17:when:1"] > .advanced-task-flow-row').draggable`),false,'A branch still cannot move independently');checks++;
    }
    // Drag cancellation leaves selection intact; dropping uses the explicit insertion position.
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`const source=document.querySelector('[data-flow-key]');source.querySelector('.advanced-task-flow-row').click();window.dragTransfer=new DataTransfer();document.querySelector('[data-flow-key="12"] > .advanced-task-flow-row').dispatchEvent(new DragEvent('dragstart',{bubbles:true,dataTransfer:dragTransfer}));document.querySelector('[data-flow-key="12"] > .advanced-task-flow-row').dispatchEvent(new DragEvent('dragend',{bubbles:true,dataTransfer:dragTransfer}));document.querySelector('[data-flow-key="'+source.dataset.flowKey+'"] > .advanced-task-flow-row [data-advanced-step-actions]').click();document.querySelector('[data-advanced-step-menu] [data-advanced-action=toggle]').click();document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[0].disabled`),true);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[1].disabled`),false);
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`window.dragTransfer=new DataTransfer();document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').dispatchEvent(new DragEvent('dragstart',{bubbles:true,dataTransfer:dragTransfer}));document.querySelector('[data-advanced-insert][data-parent-key="12"]').dispatchEvent(new DragEvent('drop',{bubbles:true,dataTransfer:dragTransfer}));`);
    for(let i=0;i<100&&!await evaluate('confirmCalls.length');i++)await pause(10);
    await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[1].steps[0].id`),'21');
    // Every reused form must become visible immediately after insertion.
    for(const kind of ['Source','Process','Destination','Email','RunTask']) {
        const model=structuredClone(fixtures.NewAdvanced);
        model.smtpHosts=[{id:'smtp-test',name:'Test SMTP',type:'SMTP'}];model.taskOptions=[{id:'other-task',name:'Other task',type:'Traditional'}];
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        await evaluate(`insertStep(${JSON.stringify(kind)});`);
        const action=kind==='Email'||kind==='RunTask';
        assert.equal(await evaluate(`!document.querySelector('[data-traditional-task-editor-workspace="${action?'next-actions':'steps'}"]').hidden`),true,kind+' opens its existing form');
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-${action?'next-action':'step'}-kicker]').hidden`),true);
        if(kind==='Email') {
            await evaluate(`change('[data-next-action-field=hostId]','smtp-test');change('[data-next-action-field=addressTo]','test@example.test');change('[data-next-action-field=subject]','Advanced email');`);
            assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-summary]').textContent`),'Advanced email · Test SMTP');
            assert.equal(await evaluate(`document.querySelector('[data-flow-key] .advanced-task-flow-summary').textContent`),'Advanced email · Test SMTP');
            await check('Advanced-Email-summary');await screenshot('Advanced-Email-summary-1400');checks++;
            await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click();`);
            for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
            assert.equal(await evaluate(`saveCalls[0].payload.nextActions[0].type`),'Email');
            assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[0].kind`),'Email');
        }
        if(kind==='RunTask') {
            await evaluate(`change('[data-next-action-field=taskId]','other-task');change('[data-next-action-field=wait]',true);document.querySelector('[data-traditional-task-editor-save]').click();`);
            for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
            assert.equal(await evaluate(`saveCalls[0].payload.nextActions[0].wait`),true);
        }
    }
    // Actual production Advanced adapter: nested form edits, structural commands and shared save request.
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1000,deviceScaleFactor:1,mobile:false});
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').click();const t=document.querySelector('[data-advanced-inspector] textarea');t.value='Edited end comment';t.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow.at(-1).fields.Text`),'Edited end comment');
    assert.equal(await evaluate(`saveCalls[0].payload.steps.every(s=>Boolean(s.clientKey))`),true);
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`document.querySelector('[data-flow-key="next-action:19"] > .advanced-task-flow-row').click();change('[data-next-action-field=wait]',false);document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    assert.equal(await evaluate(`saveCalls[0].payload.nextActions[0].wait`),false);
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').click();document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row [data-advanced-step-actions]').click();document.querySelector('[data-advanced-step-menu] [data-advanced-action=clone]').click();document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow.at(-1).cloneFromId`),'21');
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow.at(-1).id`),'');
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').click();const loop=document.querySelector('[data-flow-key="12"]');if(loop.getAttribute('aria-expanded')==='true')loop.querySelector('[data-flow-toggle]').click();window.dragTransfer=new DataTransfer();document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').dispatchEvent(new DragEvent('dragstart',{bubbles:true,dataTransfer:dragTransfer}));document.querySelector('[data-advanced-insert][data-parent-key="12"][data-before-key=""]').dispatchEvent(new DragEvent('drop',{bubbles:true,dataTransfer:dragTransfer}));`);
    for(let i=0;i<100&&!await evaluate(`confirmCalls.length`);i++)await pause(10);
    assert.equal(await evaluate(`document.querySelector('[data-flow-key="21"]').closest('[hidden]')===null`),true,'Moving into a collapsed loop reveals the selected step');
    await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[1].steps.at(-1).id`),'21');
    // Advanced failed saves must preserve draft editing/selection/moves while protecting retry.
    for (const outcome of ['failCheckoutRelease','failNetwork','editConflict','rejectSave']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
        await evaluate(`window.${outcome}=true;document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').click();const t=document.querySelector('[data-advanced-inspector] textarea');t.value='Draft before failure';t.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-traditional-task-editor-save]').click();`);
        for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').classList.contains('error')`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-content]').inert`),false);
        assert.equal(await evaluate(`document.querySelector('[data-advanced-inspector]').inert`),false);
        assert.equal(await evaluate(`document.querySelector('[data-advanced-inspector] textarea').value`),'Draft before failure');
        await evaluate(`document.querySelector('[data-flow-key="source:11"] > .advanced-task-flow-row').click();`);
        assert.equal(await evaluate(`document.querySelector('[data-flow-key="source:11"]').getAttribute('aria-selected')`),'true');
        await evaluate(`document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').click();const t=document.querySelector('[data-advanced-inspector] textarea');t.value='Draft after failure';t.dispatchEvent(new Event('input',{bubbles:true}));window.dragTransfer=new DataTransfer();document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').dispatchEvent(new DragEvent('dragstart',{bubbles:true,dataTransfer:dragTransfer}));document.querySelector('[data-advanced-insert][data-parent-key="12"][data-before-key=""]').dispatchEvent(new DragEvent('drop',{bubbles:true,dataTransfer:dragTransfer}));`);
        for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-flow-key="12"]').contains(document.querySelector('[data-flow-key="21"]'))`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-flow-key="12"]').contains(document.querySelector('[data-flow-key="21"]'))`),true,'Move remains available after '+outcome);
        assert.equal(await evaluate(`document.querySelector('[data-advanced-inspector] textarea').value`),'Draft after failure');
        const uncertain=outcome!=='rejectSave';
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),uncertain);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-reload]').hidden`),!uncertain);
        if(uncertain){
            assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').classList.contains('error')`),true);
            await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click();window.confirmAnswer=false;document.querySelector('[data-traditional-task-editor-reload]').click();`);
            await pause(50);
            assert.equal(await evaluate(`document.querySelector('[data-advanced-inspector] textarea').value`),'Draft after failure');
            assert.equal(await evaluate('saveCalls.length'),1,'No repeated PUT after '+outcome);
            await evaluate(`window.confirmAnswer=true;window.failShell=true;document.querySelector('[data-traditional-task-editor-reload]').click();`);
            for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent.includes('draft has been kept')`);i++)await pause(20);
            assert.equal(await evaluate(`document.querySelector('[data-advanced-inspector] textarea').value`),'Draft after failure');
            assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),true);
            await evaluate(`window.failShell=false;fixture.editVersion='fresh-after-reload';document.querySelector('[data-traditional-task-editor-reload]').click();`);
            for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-traditional-task-editor-reload]').hidden`);i++)await pause(20);
            assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-reload]').hidden`),true);
            assert.equal(await evaluate('saveCalls.length'),1,'Reload is read-only reconciliation');
            await evaluate(`window.${outcome}=false;const t=document.querySelector('[data-advanced-inspector] textarea');t.value='Edit after reload';t.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-traditional-task-editor-save]').click();`);
            for(let i=0;i<100&&await evaluate('saveCalls.length<2');i++)await pause(20);
            assert.equal(await evaluate(`saveCalls[1].payload.editVersion`),'fresh-after-reload');
        }
        checks+=uncertain?16:8;
    }
    await evaluate(`loadFixture(${JSON.stringify({...fixtures.Advanced,preferences:{autoSave:true}})})`);
    await evaluate(`window.failCheckoutRelease=true;document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').click();const t=document.querySelector('[data-advanced-inspector] textarea');t.value='Auto Save draft';t.dispatchEvent(new Event('input',{bubbles:true}));t.blur();`);
    for(let i=0;i<150&&!await evaluate(`document.querySelector('[data-traditional-task-editor-reload]').hidden===false`);i++)await pause(20);
    await evaluate(`const t=document.querySelector('[data-advanced-inspector] textarea');t.value='Continue draft after failed Auto Save';t.dispatchEvent(new Event('input',{bubbles:true}));t.blur();`);
    await pause(800);
    assert.equal(await evaluate('saveCalls.length'),1,'Failed Auto Save is not repeated when the draft changes');
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-cancel]').hidden`),false);
    await evaluate(`window.confirmAnswer=false;document.querySelector('[data-traditional-task-editor-cancel]').click();`);
    await pause(50);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),true);
    await evaluate(`window.confirmAnswer=true;document.querySelector('[data-traditional-task-editor-cancel]').click();`);
    for(let i=0;i<100&&await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),false);
    assert.equal(await evaluate('saveCalls.length'),1,'Discarding an uncertain Auto Save draft does not repeat the write');
    await evaluate(`loadFixture(${JSON.stringify({...fixtures.Advanced,task:{...fixtures.Advanced.task,canAlter:false}})})`);
    await evaluate(`document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').click();`);
    assert.equal(await evaluate(`document.querySelector('[data-advanced-inspector] textarea').readOnly`),true,'Read-only text remains inspectable without allowing edits');
    assert.equal(await evaluate(`document.querySelector('[data-flow-key="21"]').draggable`),false);
    assert.equal(await evaluate(`document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').draggable`),false,'Read-only step rows cannot drag');
    checks+=7;
    // Every Task kind must hold the same save/reconciliation lock, including footer controls.
    for (const key of ['Traditional','Sync-Share','Advanced']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures[key])})`);
        await evaluate(`window.holdSave=true;change('[data-editor-field="name"]','Save lock test');document.querySelector('[data-traditional-task-editor-save]').click();`);
        for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-content]').inert`),true,key+' content stays inert');
        for(const selector of ['[data-editor-field="enabled"]','[data-traditional-task-editor-auto-save]','[data-traditional-task-editor-actions]'])
            assert.equal(await evaluate(`document.querySelector(${JSON.stringify(selector)}).disabled`),true,key+' footer locked: '+selector);
        assert.equal(await evaluate(`editor.open('999','Another Task','Traditional')`),false,'A pending save cannot be redirected to another Task');
        assert.equal(await evaluate('saveCalls.length'),1);
        await evaluate('window.holdShell=true;window.releaseShell=null;releaseSave()');
        for(let i=0;i<100&&!await evaluate('typeof releaseShell === "function"');i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-content]').inert`),true,'The lock includes authoritative refresh');
        assert.equal(await evaluate(`editor.open('999','Another Task','Traditional')`),false);
        await evaluate('window.holdShell=false;releaseShell()');
        for(let i=0;i<100&&await evaluate(`document.querySelector('[data-traditional-task-editor-content]').inert`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-content]').inert`),false);
        assert.equal(await evaluate(`document.querySelector('[data-editor-field="enabled"]').disabled`),false);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-auto-save]').disabled`),false);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-reload]').hidden`),true);
        checks+=12;
    }
    // HTTP 200 alone is insufficient: wrong identity, rejected status or a missing revision
    // cannot establish a safe baseline for another save. Keep the draft and require a read.
    for(const ack of [{success:false,taskId:'100'}, {success:true,taskId:'999',editVersion:'version'}, {success:true,taskId:fixtures.Traditional.task.id}]) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures.Traditional)})`);
        await evaluate(`window.saveAck=${JSON.stringify(ack)};change('[data-editor-field="name"]','Keep ambiguous draft');document.querySelector('[data-traditional-task-editor-save]').click();`);
        for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-traditional-task-editor-reload]').hidden===false`);i++)await pause(20);
        assert.equal(await evaluate('saveCalls.length'),1);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-reload]').hidden`),false);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-content]').inert`),false);
        assert.equal(await evaluate(`document.querySelector('[data-editor-field="name"]').value`),'Keep ambiguous draft');
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),true);
        checks+=5;
    }
    // If another callback changes the draft despite the UI lock, never call that a clean
    // save or automatically replay new objects. The changed draft remains available.
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Traditional)})`);
    await evaluate(`window.holdSave=true;change('[data-editor-field="name"]','Sent draft');document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    await evaluate(`change('[data-editor-field="name"]','Later callback draft');releaseSave();`);
    for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-traditional-task-editor-reload]').hidden===false`);i++)await pause(20);
    assert.equal(await evaluate('saveCalls.length'),1);
    assert.equal(await evaluate(`document.querySelector('[data-editor-field="name"]').value`),'Later callback draft');
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-reload]').hidden`),false);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-content]').inert`),false);
    checks+=4;
    // A selected inspector rendered during the successful save refresh must recover too.
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Advanced)})`);
    await evaluate(`window.holdSave=true;document.querySelector('[data-flow-key="21"] > .advanced-task-flow-row').click();const t=document.querySelector('[data-advanced-inspector] textarea');t.value='Save draft';t.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('typeof releaseSave === "function"');i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-content]').inert`),true);
    await evaluate(`releaseSave();`);
    for(let i=0;i<100&&await evaluate(`document.querySelector('[data-traditional-task-editor-content]').inert`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-advanced-inspector]').inert`),false);
    assert.equal(await evaluate(`document.querySelector('[data-advanced-inspector] textarea').disabled`),false);
    checks+=3;
    await evaluate(`loadFixture(${JSON.stringify(fixtures.NewAdvanced)})`);
    await evaluate(`insertStep('Comment');const t=document.querySelector('[data-advanced-inspector] textarea');t.value='First Advanced step';t.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
    assert.equal(await evaluate(`saveCalls[0].payload.advancedFlow[0].fields.Text`),'First Advanced step');
    assert.ok(await evaluate(`saveCalls[0].payload.advancedFlow[0].order`)>0);
    // Untouched defaults must be real form/model values, not placeholder-only hints.
    for (const type of ['Share','FileSystem']) for (const advanced of [true,false]) {
        const model=structuredClone(advanced?fixtures.NewAdvanced:fixtures.Traditional);
        model.task.steps=[];
        for(const hosts of [model.sourceHosts,model.destinationHosts]) {
            const host=hosts.find(host=>host.typeCode===type);
            Object.assign(host.defaults,{retryCount:type==='FileSystem'?3:0,preserveFileTimestamp:type==='FileSystem'});
        }
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        for (const kind of ['Source','Destination']) {
            const field=kind.toLowerCase();
            await evaluate(advanced?`insertStep(${JSON.stringify(kind)})`:`document.querySelector('[data-traditional-task-editor-add-${field}]').click()`);
            if(kind==='Source')assert.equal(await evaluate(`document.querySelector('[data-source-field="fileMask"]').value`),'*.*');
            await evaluate(`change('[data-traditional-task-editor-${field}-host-type-filter]',${JSON.stringify(type)})`);
            await evaluate(`change('[data-${field}-field="hostId"]',fixture.${field}Hosts.find(host=>host.typeCode===${JSON.stringify(type)}).id)`);
            for(let i=0;i<100&&!await evaluate(`editor.getStepPatches().some(step=>step.kind===${JSON.stringify(kind)}&&step.patch?.Type===${JSON.stringify(type)})`);i++)await pause(20);
            await evaluate(`change('[data-${field}-field="path"]',${JSON.stringify(type==='FileSystem'?'C:\\new-'+field:'/new-'+field)})`);
            if(kind==='Source')assert.equal(await evaluate(`document.querySelector('[data-source-field="fileMask"]').value`),'*.*');
            assert.equal(await evaluate(`document.querySelector('[data-${field}-field="retryCount"]').value`),type==='FileSystem'?'3':'0');
            await evaluate(`change('[data-${field}-field="overrideRetryCount"]',true)`);
            assert.equal(await evaluate(`editor.getStepPatches().find(step=>step.kind===${JSON.stringify(kind)}).patch.RetryCount`),type==='FileSystem'?3:0);
            await evaluate(`change('[data-${field}-field="overrideRetryCount"]',false)`);
            if(kind==='Destination') {
                await evaluate(`change('[data-destination-field="overridePreserveFileTimestamp"]',true)`);
                assert.equal(await evaluate(`editor.getStepPatches().find(step=>step.kind==='Destination').patch.PreserveFileTimestamp`),type==='FileSystem'?1:0);
                await evaluate(`change('[data-destination-field="overridePreserveFileTimestamp"]',false)`);
            }
        }
        await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click()`);
        for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
        assert.equal(await evaluate('saveCalls.length'),1);
        const saved=await evaluate('saveCalls[0].payload');
        assert.deepEqual(saved.steps.map(step=>step.kind),['Source','Destination']);
        const source=saved.steps[0].patch,destination=saved.steps[1].patch;
        assert.equal(source.FileMask,'*.*');
        assert.equal(source.ExFile,'');assert.equal(source.SearchSubdirs,0);assert.equal(source.NewFilesOnly,0);
        assert.equal(destination.UseOrigName,1);assert.equal(destination.FileName,'[OrigName]');
        assert.equal(destination.ForceDir,0);assert.equal(destination.Zip,0);assert.equal(destination.OverwriteOrig,1);
        for(const patch of [source,destination]) {
            assert.equal(patch.UseDefRetryCount,1);assert.equal(patch.UseDefRetryTimeoutSecs,1);
            if(type==='Share')assert.equal(patch.UseDefWinCopyFileAPI,1);else {assert.equal(patch.HostID,'0');assert.equal('UseDefWinCopyFileAPI' in patch,false);}
            assert.equal('RetryCount' in patch,false);assert.equal('Password' in patch,false);
        }
        if(advanced) {
            assert.deepEqual(saved.advancedFlow.map(node=>node.kind),['Source','Destination']);
            for(const node of saved.advancedFlow) {
                const step=saved.steps.find(item=>item.clientKey===node.key);
                assert.equal(node.id,'');assert.equal(step.id,'');assert.equal(step.kind,node.kind);
            }
        }
        for(const step of saved.steps) {
            assert.equal(step.patch.Type,type);assert.ok(String(step.patch.HostID).length>0);
            assert.equal(step.patch.Path,type==='FileSystem'?`C:\\new-${step.kind.toLowerCase()}`:`/new-${step.kind.toLowerCase()}`);
        }
        checks+=4;
    }
    // Existing missing filespecs must look empty and report an in-modal error, then accept explicit patterns.
    for (const advanced of [true,false]) {
        const model=structuredClone(advanced?fixtures.Advanced:fixtures.Traditional);
        model.task.steps.find(step=>step.kind==='Source').source.fileMask='';
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        assert.equal(await evaluate(`document.querySelector('[data-source-field="fileMask"]').value`),'');
        assert.notEqual(await evaluate(`document.querySelector('[data-source-field="fileMask"]').placeholder`),'*.*');
        await evaluate(`change('[data-source-field="path"]','/repair-mask');document.querySelector('[data-traditional-task-editor-save]').click()`);
        assert.equal(await evaluate('saveCalls.length'),0);
        assert.match(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent`),/Source File\(s\) is required/);
        for (const mask of ['*.csv','invoice.txt','[Parm:InputMask]','*.*']) {
            await evaluate(`change('[data-source-field="fileMask"]',${JSON.stringify(mask)});document.querySelector('[data-traditional-task-editor-save]').click()`);
            for(let i=0;i<100&&!await evaluate(`saveCalls.length===${['*.csv','invoice.txt','[Parm:InputMask]','*.*'].indexOf(mask)+1}`);i++)await pause(20);
            assert.equal(await evaluate(`saveCalls.at(-1).payload.steps.find(step=>step.kind==='Source').patch.FileMask`),mask);
        }
        checks+=5;
    }
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`change('[data-source-field="path"]','/changed')`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),false);
    await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click()`);
    for(let i=0;i<100 && !await evaluate('saveCalls.length');i++) await pause(20);
    assert.equal(await evaluate('saveCalls.length'),1);
    assert.equal(await evaluate('saveCalls[0].payload.steps[0].patch.Path'),'/changed');
    assert.equal(await evaluate('saveCalls[0].payload.steps[1].kind'),'Source');
    assert.equal(await evaluate('saveCalls[0].headers.get("RequestVerificationToken")'),'token');
    // Confirm cancellation must preserve the draft and never submit a policy change.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`window.confirmAnswer=false; change('[data-sync-direction]','bidirectional'); document.querySelector('[data-traditional-task-editor-save]').click()`);
    for(let i=0;i<100 && !await evaluate('confirmCalls.length');i++) await pause(20);
    assert.equal(await evaluate('saveCalls.length'),0);
    assert.match(await evaluate('confirmCalls[0].message'),/clear the sync listing/);
    assert.equal(await evaluate(`document.querySelector('[data-sync-direction]').value`),'bidirectional');
    await evaluate(`window.confirmAnswer=true; document.querySelector('[data-traditional-task-editor-save]').click()`);
    for(let i=0;i<100 && !await evaluate('saveCalls.length');i++) await pause(20);
    assert.deepEqual(await evaluate('saveCalls[0].payload.syncSettings'),{direction:'bidirectional'});
    assert.equal(await evaluate('saveCalls[0].payload.syncChangesConfirmed'),true);
    assert.equal(await evaluate('saveCalls[0].payload.task.enabled'),false);
    await pause(100);
    // Initial-run edits do not overwrite direction/deletion or ask for a listing reset.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`change('[data-editor-field="syncInitialRun"]','custom'); change('[data-editor-field="syncSameSeconds"]','120'); document.querySelector('[data-traditional-task-editor-save]').click()`);
    for(let i=0;i<100 && !await evaluate('saveCalls.length');i++) await pause(20);
    assert.deepEqual(await evaluate('saveCalls[0].payload.syncSettings'),{sameSeconds:120});
    assert.equal(await evaluate('confirmCalls.length'),0);
    await pause(100);
    // A fresh Host selection must keep the folder identity and reset its old path.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`change('[data-traditional-task-editor-source-host-type-filter]','FTP')`);
    for(let i=0;i<100 && !await evaluate('confirmCalls.length');i++) await pause(20);
    await pause(50);
    await evaluate(`change('[data-source-field="hostId"]','host-FTP')`);
    for(let i=0;i<100 && !await evaluate('confirmCalls.length');i++) await pause(20);
    await pause(50);
    assert.equal(await evaluate(`document.querySelector('[data-source-field="path"]').value`),'');
    await evaluate(`change('[data-source-field="path"]','/new-host'); document.querySelector('[data-traditional-task-editor-save]').click()`);
    for(let i=0;i<100 && !await evaluate('saveCalls.length');i++) await pause(20);
    assert.equal(await evaluate('saveCalls[0].payload.steps[0].id'),'11');
    assert.equal(await evaluate('saveCalls[0].payload.steps[0].patch.Type'),'FTP');
    assert.equal(await evaluate('saveCalls[0].payload.steps[0].patch.HostID'),'host-FTP');
    await pause(100);
    // Shared mail/run forms keep conditions in General and recipients in Message.
    for(const name of ['Traditional','Sync-Share','Advanced']) {
        const model=structuredClone(name==='Advanced'?fixtures.NewAdvanced:fixtures[name]);
        model.task.nextActions=[];model.smtpHosts=[{id:'smtp-test',name:'Test SMTP',type:'SMTP'}];model.taskOptions=[{id:'other-task',name:'Other Task'}];
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        await evaluate(name==='Advanced'?`insertStep('Email')`:`document.querySelector('[data-traditional-task-editor-section-button="next-actions"]').click();document.querySelector('[data-traditional-task-editor-add-email-action]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-tab="conditions"]')`),null);
        assert.equal(await evaluate(`document.querySelector('[data-next-action-field="addressTo"]').closest('[data-traditional-task-editor-next-action-panel]').dataset.traditionalTaskEditorNextActionPanel`),'message');
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-conditions]').hidden`),name==='Advanced');
        if(name!=='Advanced') {
            assert.equal(await evaluate(`(()=>{const c=document.querySelector('[data-traditional-task-editor-next-action-conditions]').getBoundingClientRect(),h=document.querySelector('[data-next-action-field="hostId"]').closest('label').getBoundingClientRect();return c.height>0&&h.height>0&&c.bottom<=h.top;})()`),true);
            if(name==='Traditional')await screenshot('mail-general-placement');
            await evaluate(`change('[data-next-action-field="doIfSuccess"]',true);change('[data-next-action-field="doIfFailure"]',false);change('[data-next-action-field="doIfNoAction"]',true);document.querySelector('[data-next-action-field="doAfter"][value="FileDone"]').click()`);
        }
        await evaluate(`change('[data-next-action-field="hostId"]','smtp-test');document.querySelector('[data-traditional-task-editor-next-action-tab="message"]').click();change('[data-next-action-field="format"]','text');change('[data-next-action-field="addressTo"]','test@example.test');change('[data-next-action-field="subject"]','Moved mail fields');change('[data-next-action-field="messageText"]','Mail body')`);
        assert.equal(await evaluate(`(()=>{const f=document.querySelector('[data-next-action-field="format"]').closest('label').getBoundingClientRect(),r=document.querySelector('[data-next-action-field="addressTo"]').closest('label').getBoundingClientRect(),s=document.querySelector('[data-next-action-field="subject"]').closest('label').getBoundingClientRect();return r.height>0&&f.height>0&&s.height>0&&r.top>=f.bottom&&r.bottom<=s.top;})()`),true);
        for(const mode of ['text','html','source']) {
            await evaluate(`change('[data-next-action-field="format"]',${JSON.stringify(mode==='text'?'text':'html')})`);
            if(mode==='source')await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-html-toggle]').click()`);
            assert.equal(await evaluate(`(()=>{const t=document.querySelector('[data-traditional-task-editor-next-action-html-toolbar]').getBoundingClientRect(),e=[...document.querySelectorAll('[data-traditional-task-editor-next-action-panel="message"] .task-notification-editor,[data-traditional-task-editor-next-action-panel="message"] textarea')].find(n=>!n.hidden).getBoundingClientRect();return t.height>0&&e.height>0&&Math.abs(t.bottom-e.top)<1&&Math.abs(t.left-e.left)<1&&Math.abs(t.right-e.right)<1;})()`),true,name+' '+mode+' toolbar and message body meet without a gap');
            assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-body-macro]').options[0].textContent`),'Macros…');
            assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-subject-macro]').options[0].textContent`),'Macros…');
            if(name==='Traditional')await screenshot('mail-message-joined-'+mode);
        }
        await evaluate(`change('[data-next-action-field="format"]','text');change('[data-next-action-field="messageText"]','Mail body')`);
        if(name==='Traditional')await screenshot('mail-message-placement');
        await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click()`);
        for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
        assert.equal(await evaluate('saveCalls[0].payload.nextActions[0].addressTo'),'test@example.test');
        if(name!=='Advanced') {assert.equal(await evaluate('saveCalls[0].payload.nextActions[0].doIfNoAction'),true);assert.equal(await evaluate('saveCalls[0].payload.nextActions[0].doAfter'),'FileDone');}
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        await evaluate(name==='Advanced'?`insertStep('RunTask')`:`document.querySelector('[data-traditional-task-editor-section-button="next-actions"]').click();document.querySelector('[data-traditional-task-editor-add-run-task-action]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-tab="conditions"]')`),null);
        if(name!=='Advanced')assert.equal(await evaluate(`(()=>{const c=document.querySelector('[data-traditional-task-editor-next-action-conditions]').getBoundingClientRect(),t=document.querySelector('[data-next-action-field="taskId"]').closest('label').getBoundingClientRect();return c.height>0&&t.height>0&&c.bottom<=t.top;})()`),true);
        if(name==='Traditional')await screenshot('run-task-general-placement');
        checks+=9;
    }
    // Host 0 opens in place from every Task type and returns to its unchanged parent draft.
    for(const name of ['Traditional','Advanced','Sync-FileSystem']) {
        const model=structuredClone(fixtures[name]),source=model.task.steps[0].source;
        Object.assign(source,{hostId:'0',hostTypeCode:'FileSystem',hostType:'Local File System',hostName:'Local File System',hostDefaults:model.sourceHosts.find(host=>host.id==='0').defaults});
        await evaluate(`window.hostFixture=${JSON.stringify(hostFixtures['FileSystem-basic'])};loadFixture(${JSON.stringify(model)})`);
        await evaluate(`change('[data-source-field="path"]','C:\\\\draft');window.localParentVersion=fixture.editVersion;document.querySelector('[data-traditional-task-editor-edit-host="Source"]').click()`);
        for(let i=0;i<100&&!await evaluate(`Boolean(document.querySelector('[data-host-editor-dialog][open]'))`);i++)await pause(20);
        assert.equal(await evaluate(`Boolean(document.querySelector('[data-host-editor-dialog][open]'))`),true,name+' opens Host 0');
        assert.equal(await evaluate(`document.querySelector('[data-host-editor-id]').textContent`),'0');
        await evaluate(`document.querySelector('[data-host-editor-cancel]').click()`);
        for(let i=0;i<100&&await evaluate(`document.querySelector('[data-traditional-task-editor-edit-host="Source"]').disabled`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-source-field="path"]').value`),'C:\\draft');
        assert.equal(await evaluate('fixture.editVersion'),model.editVersion);checks+=4;
    }
    await evaluate(`window.hostFixture=${JSON.stringify(hostFixtures['SSHFTP-basic'])}`);
    // Re-read Host presentation without discarding parent edits or adopting a new Task revision.
    for(const name of ['Traditional','Advanced','Sync-Share']) {
        const model=structuredClone(fixtures[name]);model.task.steps[0].source.useDefaultRetryCount=true;
        // Captured Advanced Steps refer to real IDs; the mock Host catalog uses synthetic IDs.
        const selected=model.task.steps[0].source;
        for(const options of [model.sourceHosts,model.destinationHosts])if(!options.some(h=>h.id===selected.hostId)) {
            const host=structuredClone(options.find(h=>h.typeCode===selected.hostTypeCode));
            host.id=selected.hostId;host.name=selected.hostName;options.push(host);
        }
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        await evaluate(`change('[data-source-field="path"]','/parent-draft');window.parentSelection=document.querySelector('[data-traditional-task-editor-step-items] .selected')?.dataset.traditionalTaskEditorStepKey;window.parentRevision=fixture.editVersion;document.querySelector('[data-traditional-task-editor-edit-host="Source"]').click()`);
        for(let i=0;i<100&&!await evaluate(`Boolean(document.querySelector('[data-host-editor-dialog][open]'))`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),true);
        await evaluate(`const id=document.querySelector('[data-source-field="hostId"]').value;for(const option of [...fixture.sourceHosts,...fixture.destinationHosts])if(option.id===id){option.name='Refreshed Host';option.defaults.retryCount=42;}fixture.editVersion='concurrently-changed-task';fixture.task.name='Do not replace my Task draft';document.querySelector('[data-host-editor-cancel]').click()`);
        for(let i=0;i<100&&await evaluate(`document.querySelector('[data-traditional-task-editor-edit-host="Source"]').disabled`);i++)await pause(20);
        assert.equal(await evaluate(`Boolean(document.querySelector('[data-host-editor-dialog][open]'))`),false);
        assert.equal(await evaluate(`document.querySelector('[data-source-field="path"]').value`),'/parent-draft');
        assert.equal(await evaluate(`document.querySelector('[data-source-field="hostId"]').selectedOptions[0].textContent`),'Refreshed Host');
        assert.equal(await evaluate(`document.querySelector('[data-source-field="retryCount"]').value`),'42');
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-step-items] .selected')?.dataset.traditionalTaskEditorStepKey===parentSelection`),true);
        assert.notEqual(await evaluate(`document.querySelector('[data-editor-field="name"]').value`),'Do not replace my Task draft');
        await evaluate(`change('[data-source-field="overrideRetryCount"]',true);change('[data-source-field="retryCount"]','7');document.querySelector('[data-traditional-task-editor-edit-host="Source"]').click()`);
        for(let i=0;i<100&&!await evaluate(`Boolean(document.querySelector('[data-host-editor-dialog][open]'))`);i++)await pause(20);
        await evaluate(`const id=document.querySelector('[data-source-field="hostId"]').value;for(const option of [...fixture.sourceHosts,...fixture.destinationHosts])if(option.id===id)option.defaults.retryCount=99;document.querySelector('[data-host-editor-cancel]').click()`);
        for(let i=0;i<100&&await evaluate(`document.querySelector('[data-traditional-task-editor-edit-host="Source"]').disabled`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-source-field="retryCount"]').value`),'7','Explicit overrides survive Host refresh');
        await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click()`);
        for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
        assert.equal(await evaluate('saveCalls[0].payload.editVersion'),await evaluate('parentRevision'),'Host refresh cannot acknowledge an external Task edit');
        assert.equal(await evaluate('saveCalls[0].payload.steps[0].patch.RetryCount'),7);
        checks+=10;
    }
    // A clean Auto Save draft stays clean when only inherited Host properties change.
    await evaluate(`loadFixture(${JSON.stringify({...fixtures.Traditional,preferences:{autoSave:true}})})`);
    await evaluate(`document.querySelector('[data-traditional-task-editor-edit-host="Source"]').click()`);
    for(let i=0;i<100&&!await evaluate(`Boolean(document.querySelector('[data-host-editor-dialog][open]'))`);i++)await pause(20);
    await evaluate(`const id=document.querySelector('[data-source-field="hostId"]').value;for(const option of [...fixture.sourceHosts,...fixture.destinationHosts])if(option.id===id){option.name='Clean refresh';option.defaults.retryCount=88;}document.querySelector('[data-host-editor-cancel]').click()`);
    for(let i=0;i<100&&await evaluate(`document.querySelector('[data-traditional-task-editor-edit-host="Source"]').disabled`);i++)await pause(20);
    await pause(800);
    assert.equal(await evaluate('saveCalls.length'),0,'Refreshing inherited values must not write the Task');
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').classList.contains('has-unsaved-changes')`),false);
    checks+=2;
    // Logs refresh on each visit/click, recover from failures, and discard old responses.
    for(const name of ['Traditional','Advanced','Sync-Share']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures[name])})`);
        await evaluate(`window.logRows=[{id:1,exitStatus:'Success',fallbackName:'First log'}];document.querySelector('[data-traditional-task-editor-tab="logs"]').click()`);
        for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-traditional-task-editor-log-rows]').textContent.includes('First log')`);i++)await pause(20);
        assert.equal(await evaluate('logCalls.length'),1);
        await evaluate(`document.querySelector('[data-traditional-task-editor-tab="task"]').click();window.logRows=[{id:2,exitStatus:'Failure',fallbackName:'New log'}];document.querySelector('[data-traditional-task-editor-tab="logs"]').click()`);
        for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-traditional-task-editor-log-rows]').textContent.includes('New log')`);i++)await pause(20);
        assert.equal(await evaluate('logCalls.length'),2);
        assert.equal(await evaluate('logCalls[1].cache'),'no-store');
        await evaluate(`window.failLogs=true;document.querySelector('[data-traditional-task-editor-tab="logs"]').click()`);
        for(let i=0;i<100&&await evaluate(`document.querySelector('[data-traditional-task-editor-logs-error]').hidden`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-logs-error]').hidden`),false);
        await evaluate(`window.failLogs=false;window.holdLogs=true;window.logRows=[{id:3,exitStatus:'Success',fallbackName:'Stale log'}];document.querySelector('[data-traditional-task-editor-tab="logs"]').click()`);
        for(let i=0;i<100&&!await evaluate('typeof releaseLogs === "function"');i++)await pause(20);
        await evaluate(`window.holdLogs=false;window.logRows=[{id:4,exitStatus:'Success',fallbackName:'Latest log'}];document.querySelector('[data-traditional-task-editor-tab="logs"]').click()`);
        for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-traditional-task-editor-log-rows]').textContent.includes('Latest log')`);i++)await pause(20);
        await evaluate('releaseLogs()');await pause(30);
        assert.match(await evaluate(`document.querySelector('[data-traditional-task-editor-log-rows]').textContent`),/Latest log/);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-logs-error]').hidden`),true);
        checks+=6;
    }
    // Column sorting cycles asc/desc/none without changing the selected path or API order.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`window.browseItems=[{name:'file10',size:'100',lastModified:'2026-01-01',isFolder:false},{name:'file2',size:'9',lastModified:'2026-10-01',isFolder:false},{name:'folder',path:'/folder',isFolder:true}];document.querySelector('[data-traditional-task-editor-source-browse]').click()`);
    for(let i=0;i<100&&await evaluate(`document.querySelectorAll('[data-host-browser-rows] > *').length<3`);i++)await pause(20);
    await evaluate(`document.querySelector('[data-folder-browser-sort="size"]').click()`);
    assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-host-browser-rows] > *')].map(n=>n.querySelector('.maui-host-browser-name').textContent.replace(/^[^a-z]+/,''))`),['file2','file10','folder']);
    assert.equal(await evaluate(`document.querySelector('[data-folder-browser-sort="size"] .sort-indicator').textContent`),'↑');
    await evaluate(`document.querySelector('[data-folder-browser-sort="size"]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-folder-browser-sort="size"]').getAttribute('aria-sort')`),'descending');
    await evaluate(`document.querySelector('[data-folder-browser-sort="size"]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-folder-browser-sort="size"] .sort-indicator').textContent`),'');
    await evaluate(`document.querySelector('[data-host-browser-cancel]').click();window.browseItems=[]`);checks+=4;
    // Shared file/folder selection applies one editor edit and retains the calling Task modal.
    for (const name of ['Traditional','Advanced','Sync-Share']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures[name])})`);
        await evaluate(`change('[data-source-field="path"]','/incoming'); change('[data-source-field="fileMask"]','*.csv'); window.browseItems=[{name:'chosen.txt',isFolder:false,size:'5'},{name:'child',path:'/incoming/child',isFolder:true}];document.querySelector('[data-traditional-task-editor-source-browse]').click()`);
        for(let i=0;i<100&&await evaluate(`document.querySelectorAll('[data-host-browser-rows] > *').length<2`);i++)await pause(20);
        await evaluate(`document.querySelector('[data-host-browser-rows] .file').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))`);
        assert.equal(await evaluate(`document.querySelector('[data-source-field="path"]').value`),'/incoming');
        assert.equal(await evaluate(`document.querySelector('[data-source-field="fileMask"]').value`),'chosen.txt',name+' file selection');
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),true);
        assert.equal(await evaluate(`document.querySelector('[data-host-browser]').open`),false);
        await evaluate(`document.querySelector('[data-traditional-task-editor-source-browse]').click()`);
        for(let i=0;i<100&&await evaluate(`document.querySelectorAll('[data-host-browser-rows] > *').length<2`);i++)await pause(20);
        await evaluate(`document.querySelector('[data-host-browser-rows] .folder').click();document.querySelector('[data-host-browser-ok]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-source-field="path"]').value`),'/incoming/child');
        assert.equal(await evaluate(`document.querySelector('[data-source-field="fileMask"]').value`),'chosen.txt','Folder-only selection preserves filename');
        checks+=6;
    }
    for(const type of ['Traditional','Advanced']) {
        await evaluate(`loadFixture(${JSON.stringify(type==='Advanced'?fixtures.NewAdvanced:fixtures.Traditional)})`);
        if(type==='Advanced') await evaluate(`insertStep('Destination')`);
        else await evaluate(`document.querySelector('[data-traditional-task-editor-add-destination]').click()`);
        await evaluate(`change('[data-traditional-task-editor-destination-host-type-filter]','Share')`);
        await evaluate(`change('[data-destination-field="hostId"]',fixture.destinationHosts.find(h=>h.typeCode==='Share').id)`);
        await evaluate(`change('[data-destination-field="path"]','/outgoing');window.browseItems=[{name:'final.txt',isFolder:false}];document.querySelector('[data-traditional-task-editor-destination-browse]').click()`);
        for(let i=0;i<100&&await evaluate(`document.querySelectorAll('[data-host-browser-rows] > *').length<1`);i++)await pause(20);
        await evaluate(`document.querySelector('[data-host-browser-rows] .file').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))`);
        assert.equal(await evaluate(`document.querySelector('[data-destination-field="fileName"]').value`),'final.txt');
        assert.equal(await evaluate(`document.querySelector('[data-destination-field="useOriginalName"]').checked`),false);
        assert.equal(await evaluate(`editor.getStepPatches().findLast(s=>s.kind==='Destination').patch.FileName`),'final.txt');checks+=3;
    }
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])});window.browseItems=[]`);
    // Run As Of is hidden for Sync, enabled only with macros, and keeps progress/errors in its modal.
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-run-as-of]').hidden`),true);checks++;
    for(const name of ['Traditional','Advanced']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures[name])})`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-run-as-of]').disabled`),true,'No date/time macros means Run As Of is disabled');
        await evaluate(`change('[data-source-field="fileMask"]','[YYYY][MM][DD]*.txt')`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-run-as-of]').disabled`),false,'Filename macro enables Run As Of without a synthetic permission override');
        await evaluate(`document.querySelector('[data-traditional-task-editor-run-as-of]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-dialog]').open`),true);
        // The authored picker has a dismiss action without selecting a native date segment.
        assert.notEqual(await evaluate(`document.activeElement?.getAttribute('data-run-as-of-date')`),'');
        assert.match(await evaluate(`document.querySelector('[data-run-as-of-dialog] .settings-header p').textContent`),/folders that use date-time macros\./);
        const runCount=await evaluate('runAsOfCalls.length');
        await evaluate(`document.querySelector('[data-run-as-of-picker-toggle]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-picker]').matches(':popover-open')`),true);
        assert.deepEqual(await evaluate(`[...document.querySelectorAll('.run-as-of-date-actions button')].map(button=>button.textContent)`),['Clear','OK','Today']);
        await evaluate(`change('[data-run-as-of-picker-year]','2024');change('[data-run-as-of-picker-month]','1');document.querySelector('[data-picker-day="2024-02-29"]').click();change('[data-run-as-of-picker-hour]','23');change('[data-run-as-of-picker-minute]','00');change('[data-run-as-of-picker-second]','05')`);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-date]').value`),'2024-02-29T23:00:05','Leap day and wall-clock seconds are retained');
        await evaluate(`change('[data-run-as-of-picker-hour]','');document.querySelector('[data-run-as-of-picker-ok]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-picker]').matches(':popover-open')`),true);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-picker-error]').hidden`),false);
        await evaluate(`change('[data-run-as-of-picker-hour]','17');document.querySelector('[data-run-as-of-picker-ok]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-date]').value`),'2024-02-29T17:00:05','Fixing an incomplete time retains the chosen date');
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-picker]').matches(':popover-open')`),false);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-dialog]').open`),true,'OK dismisses only the picker');
        assert.equal(await evaluate(`document.activeElement===document.querySelector('[data-run-as-of-picker-toggle]')`),true);
        await evaluate(`document.querySelector('[data-run-as-of-picker-toggle]').click();document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true,cancelable:true}))`);
        assert.equal(await evaluate(`document.activeElement.dataset.pickerDay`),'2024-03-01','Arrow navigation crosses the leap-month boundary');
        await evaluate(`document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true,cancelable:true}))`);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-picker]').matches(':popover-open')`),false);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-dialog]').open`),true,'Escape dismisses the picker first');
        await evaluate(`document.querySelector('[data-run-as-of-picker-toggle]').click();document.querySelector('[data-run-as-of-picker-clear]').click();document.querySelector('[data-run-as-of-run]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-date]').value`),'');
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-error]').hidden`),false,'Clear cannot submit an empty date/time');
        assert.equal(await evaluate('runAsOfCalls.length'),runCount,'Picker actions never execute the Task');
        await evaluate(`document.querySelector('[data-run-as-of-picker-toggle]').click();document.querySelector('[data-run-as-of-picker-today]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-date]').value.slice(0,10)===(()=>{const n=new Date(),p=v=>String(v).padStart(2,'0');return n.getFullYear()+'-'+p(n.getMonth()+1)+'-'+p(n.getDate());})()`),true,'Today uses local calendar values');
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-error]').hidden`),true,'A valid selection clears the date validation message');
        for(const width of [1400,360]) {
            await command('Emulation.setDeviceMetricsOverride',{width,height:700,deviceScaleFactor:1,mobile:false});
            for(const theme of ['light','dark']) {
                await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)};window.dispatchEvent(new Event('resize'))`);
                assert.equal(await evaluate(`(()=>{const p=document.querySelector('[data-run-as-of-picker]'),r=p.getBoundingClientRect();return r.left>=0&&r.right<=Math.min(innerWidth,document.documentElement.clientWidth,visualViewport.width)&&r.top>=0&&r.bottom<=Math.min(innerHeight,document.documentElement.clientHeight,visualViewport.height)&&p.scrollWidth<=p.clientWidth;})()`),true,'Picker stays within '+width+'px '+theme+' viewport');
                if(name==='Traditional') await screenshot('run-as-of-picker-'+width+'-'+theme);
            }
        }
        await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1000,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.querySelector('[data-run-as-of-picker-ok]').click()`);
        checks+=23;
        await evaluate(`document.querySelector('[data-run-as-of-date]').value='2026-10-01T23:00:05';window.holdRunAsOf=true;document.querySelector('[data-run-as-of-run]').click()`);
        for(let i=0;i<100&&!await evaluate(`Boolean(window.releaseRunAsOf)`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-run]').textContent`),'Running…');
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-close]').disabled`),true);
        await evaluate(`window.holdRunAsOf=false;window.releaseRunAsOf();window.releaseRunAsOf=null`);await pause(100);
        assert.equal(await evaluate(`document.querySelector('[data-run-as-of-run]').textContent`),'Run');
        assert.match(await evaluate(`document.querySelector('[data-run-as-of-status]').textContent`),/restored/);
        await evaluate(`document.querySelector('[data-run-as-of-close]').click()`);checks+=7;
        const persisted=structuredClone(fixtures[name]);persisted.task.steps.find(step=>step.source).source.fileMask='[YYYY][MM][DD]*.txt';
        await evaluate(`loadFixture(${JSON.stringify(persisted)})`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-run-as-of]').disabled`),false,'An already saved filename macro enables Run As Of immediately on reopen');checks++;
    }
    // New unconfigured transfer steps need Host selection, not an unsupported-Host warning.
    const sourceOnly=structuredClone(fixtures.Traditional);sourceOnly.task.steps=sourceOnly.task.steps.filter(step=>step.source).slice(0,1);
    for(const kind of ['Source','Destination']) {
        await evaluate(`loadFixture(${JSON.stringify(sourceOnly)})`);
        await evaluate(`document.querySelector('[data-traditional-task-editor-add-${kind.toLowerCase()}]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent`),`Select a Host for the ${kind} before saving.`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),true);
        await evaluate(`change('[data-traditional-task-editor-${kind.toLowerCase()}-host-type-filter]','Share');change('[data-${kind.toLowerCase()}-field="hostId"]','host-Share')`);
        // Host selection completes asynchronously and resets the folder for the new Host.
        for(let i=0;i<100&&!await evaluate(`editor.getStepPatches().some(step=>step.kind==='${kind}'&&!step.id&&step.patch?.HostID==='host-Share')`);i++)await pause(20);
        await evaluate(`change('[data-${kind.toLowerCase()}-field="path"]','/configured')`);
        assert.doesNotMatch(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent`),/unsupported|Select a Host/);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),false);
        await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click()`);
        for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
        assert.equal(await evaluate(`saveCalls.length`),1,`${kind} save: ${await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent`)}`);await pause(100);checks+=5;
    }
    // The same modal browser must keep absolute MOVEit server paths intact.
    for (const localPath of ['C:/sync/folder','//server/share/sync']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-FileSystem'])})`);
        await evaluate(`change('[data-source-field="path"]',${JSON.stringify(localPath)}); document.querySelector('[data-traditional-task-editor-source-browse]').click()`);
        for(let i=0;i<100 && !await evaluate('Boolean(window.lastBrowseUrl)');i++) await pause(20);
        assert.equal(await evaluate('new URL(lastBrowseUrl).searchParams.get("path")'),localPath.replaceAll('/','\\'));
        await evaluate(`document.querySelector('[data-host-browser]').close(); window.lastBrowseUrl='';`);
    }
    // Native comparison rendering and initial-run controls use the production modal/runtime.
    for (const width of [1400,600]) {
        await command('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false});
        for (const theme of ['light','dark']) {
            await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}; loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
            await evaluate(`document.querySelector('[data-sync-view-preview]').click()`);
            for(let i=0;i<100 && !await evaluate(`document.querySelector('[data-sync-preview-rows]').children.length`);i++) await pause(20);
            assert.equal(await evaluate(`document.querySelector('[data-sync-preview-rows]').children.length`),2);
            assert.equal(await evaluate(`document.querySelector('[data-sync-preview-rows] img')`),null);
            assert.equal(await evaluate(`Boolean(window.syncInjected)`),false);
            assert.equal(await evaluate(`document.querySelector('[data-sync-preview-dialog]').scrollWidth <= document.querySelector('[data-sync-preview-dialog]').clientWidth + 2`),true);
            assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-sync-preview-dialog] > .new-task-card')).backgroundColor === 'rgba(0, 0, 0, 0)'`),false);
            await screenshot('preview-'+width+'-'+theme); checks++;
            await evaluate(`document.querySelector('[data-sync-preview-show-no-action]').click()`);
            assert.equal(await evaluate(`document.querySelector('[data-sync-preview-rows]').children.length`),3);
            await evaluate(`document.querySelector('[data-sync-preview-initial]').click()`);
            assert.equal(await evaluate(`document.querySelector('[data-sync-initial-dialog]').open`),true);
            assert.equal(await evaluate(`document.querySelectorAll('[name="maui-sync-initial-mode"]').length`),4);
            assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-sync-initial-dialog] > .new-task-card')).backgroundColor === 'rgba(0, 0, 0, 0)'`),false);
            await screenshot('initial-'+width+'-'+theme); checks++;
            await evaluate(`document.querySelector('[data-sync-initial-close]').click(); document.querySelector('[data-sync-preview-close]').click()`);
        }
    }
    // All four initial-run choices submit their native SameSecs values through the shared save.
    for(const mode of ['-3','-1','-2','custom']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
        await evaluate(`document.querySelector('[data-sync-view-preview]').click()`);
        for(let i=0;i<100 && !await evaluate(`document.querySelector('[data-sync-preview-rows]').children.length`);i++) await pause(20);
        await evaluate(`document.querySelector('[data-sync-preview-initial]').click(); document.querySelector('[name="maui-sync-initial-mode"][value="${mode}"]').click()`);
        if(mode==='custom') await evaluate(`document.querySelector('[data-sync-initial-value]').value='24'; document.querySelector('[data-sync-initial-unit]').value='3600'`);
        await evaluate(`document.querySelector('[data-sync-initial-save]').click()`);
        for(let i=0;i<100 && await evaluate(`document.querySelector('[data-sync-initial-dialog]').open`);i++) await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-sync-initial-dialog]').open`),false);
        if(mode!=='-1') assert.equal(await evaluate('saveCalls[0].payload.syncSettings.sameSeconds'),mode==='custom'?86400:Number(mode));
        for(let i=0;i<100 && !await evaluate('previewGets>=2');i++) await pause(20);
        assert.ok(await evaluate('previewGets>=2'));
        await evaluate(`document.querySelector('[data-sync-preview-close]').click()`);
    }
    // Preview Cancel performs no PUT; OK performs the captured save once.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`document.querySelector('[data-sync-view-preview]').click()`);
    for(let i=0;i<100 && !await evaluate(`document.querySelector('[data-sync-preview-rows]').children.length`);i++) await pause(20);
    await evaluate(`document.querySelector('[data-sync-preview-close]').click()`);
    assert.equal(await evaluate('syncActionCalls.length'),0);
    await evaluate(`document.querySelector('[data-sync-view-preview]').click()`);
    for(let i=0;i<100 && !await evaluate(`document.querySelector('[data-sync-preview-rows]').children.length`);i++) await pause(20);
    await evaluate(`document.querySelector('[data-sync-preview-ok]').click()`);
    for(let i=0;i<100 && await evaluate(`document.querySelector('[data-sync-preview-dialog]').open`);i++) await pause(20);
    assert.deepEqual(await evaluate('syncActionCalls.map(call=>call.action)'),['accept-preview']);
    // Clear cancellation, one confirmed reset, disabled state and automatic preview.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`window.confirmAnswer=false; document.querySelector('[data-sync-action="clear-listings"]').click()`);
    await pause(80); assert.equal(await evaluate('syncActionCalls.length'),0);
    await evaluate(`window.confirmAnswer=true; document.querySelector('[data-sync-action="clear-listings"]').click()`);
    for(let i=0;i<100 && !await evaluate(`document.querySelector('[data-sync-preview-dialog]').open`);i++) await pause(20);
    assert.deepEqual(await evaluate('syncActionCalls.map(call=>call.action)'),['clear-listings']);
    assert.equal(await evaluate('fixture.task.enabled'),false);
    await evaluate(`document.querySelector('[data-sync-preview-close]').click()`);
    // Swaps render the new A/B order with retained native identities.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`document.querySelector('[data-sync-action="swap"]').click()`);
    for(let i=0;i<100 && !await evaluate('syncActionCalls.length');i++) await pause(20);
    await pause(100);
    assert.deepEqual(await evaluate('editor.getStepPatches().map(step=>step.id)'),['12','11']);
    // Existing listings hide initial-run options. Unknown writes block automatic repetition.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`window.previewCanChange=false; document.querySelector('[data-sync-view-preview]').click()`);
    for(let i=0;i<100 && !await evaluate(`document.querySelector('[data-sync-preview-rows]').children.length`);i++) await pause(20);
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-sync-preview-initial]')).display`),'none');
    await evaluate(`document.querySelector('[data-sync-preview-close]').click()`);
    await evaluate(`window.failSyncAction=true; document.querySelector('[data-sync-action="clear-listings"]').click()`);
    for(let i=0;i<100 && !await evaluate('syncActionCalls.length');i++) await pause(20);
    await pause(100);
    assert.equal(await evaluate(`document.querySelector('[data-sync-action="clear-listings"]').disabled`),true);
    await evaluate(`document.querySelector('[data-sync-action="clear-listings"]').click()`);
    assert.equal(await evaluate('syncActionCalls.length'),1);
    // Clone uses the existing page callback after persisting pending editor changes.
    for(const model of [fixtures.Traditional,fixtures['Sync-Share']]) {
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        await evaluate(`change('[data-source-field="path"]','/clone-draft'); document.querySelector('[data-traditional-task-editor-clone]').click()`);
        for(let i=0;i<100 && !await evaluate('cloneCalls.length');i++) await pause(20);
        assert.equal(await evaluate('cloneCalls.length'),1);
        assert.equal(await evaluate('cloneCalls[0].id'),model.task.id);
        assert.equal(await evaluate('cloneCalls[0].type'),model.task.type);
        assert.equal(await evaluate('cloneCalls[0].editorOpen'),false);
        assert.equal(await evaluate('cloneCalls[0].saves'),1);
    }
    // Real Traditional action flushes the draft, creates once, and reuses the modal for the disabled Advanced copy.
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Traditional)})`);
    const converted=structuredClone(fixtures.Advanced);converted.task.id='900';converted.task.name='Converted Advanced';converted.task.enabled=false;
    await evaluate(`window.convertedFixture=${JSON.stringify(converted)};window.holdConversion=true;change('[data-source-field="path"]','/saved-before-conversion');document.querySelector('[data-traditional-task-editor-create-advanced]').click()`);
    for(let i=0;i<100&&!await evaluate('conversionCalls.length');i++)await pause(20);
    assert.deepEqual(await evaluate('conversionEvents'),['save','convert']);
    assert.deepEqual(await evaluate('conversionCalls[0]'),{taskId:fixtures.Traditional.task.id});
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').inert`),true);
    await evaluate(`document.querySelector('[data-traditional-task-editor-create-advanced]').click();document.querySelector('[data-traditional-task-editor-cancel]').click()`);
    assert.equal(await evaluate('conversionCalls.length'),1);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),true);
    await evaluate(`window.holdConversion=false;releaseConversion()`);
    for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-task-editor-kind-label]').textContent==='Advanced Task Editor'`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-task-editor-kind-label]').textContent`),'Advanced Task Editor');
    assert.equal(await evaluate(`document.querySelector('[data-editor-field="enabled"]').checked`),false);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-create-advanced]').hidden`),true);
    checks+=5;
    for(const failure of ['rejected','uncertain','network']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures.Traditional)})`);
        await evaluate(`window.failConversion=${JSON.stringify(failure)};document.querySelector('[data-traditional-task-editor-create-advanced]').click()`);
        for(let i=0;i<100&&!await evaluate(`conversionCalls.length===1&&!document.querySelector('[data-traditional-task-editor-dialog]').inert`);i++)await pause(20);
        assert.equal(await evaluate('conversionCalls.length'),1);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),true);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').inert`),false);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').classList.contains('error')`),true);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-create-advanced]').disabled`),failure!=='rejected');
        await evaluate(`change('[data-source-field="path"]','/still-editable-after-conversion-error')`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),false);
        if(failure!=='rejected') {
            await evaluate(`document.querySelector('[data-traditional-task-editor-create-advanced]').click()`);
            assert.equal(await evaluate('conversionCalls.length'),1);
        }
        checks+=3;
    }
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Traditional)})`);
    await evaluate(`document.querySelector('[data-task-page-config]').dataset.advancedTasksLicensed='false';change('[data-source-field="path"]','/license-check')`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-create-advanced]').disabled`),true);
    checks++;
    // Save Template reads plain text and HTML from the production message editors without saving the Task.
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Traditional)})`);
    await evaluate(`document.querySelector('[data-traditional-task-editor-section-button="next-actions"]').click(); document.querySelector('[data-traditional-task-editor-add-email-action]').click(); document.querySelector('[data-traditional-task-editor-next-action-tab="message"]').click(); change('[data-next-action-field="subject"]','Template subject'); change('[data-next-action-field="format"]','text'); change('[data-next-action-field="messageText"]','Literal <value> [TaskName]');`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-save-template]').getClientRects().length > 0`),true);
    await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-save-template]').click()`);
    for(let i=0;i<100 && !await evaluate('templateCalls.length');i++) await pause(20);
    assert.equal(await evaluate('templateCalls[0].format'),'text');
    assert.equal(await evaluate('templateCalls[0].message'),'Literal <value> [TaskName]');
    assert.equal(await evaluate('saveCalls.length'),0);
    await check('Template-next-action-text'); await screenshot('template-next-action-600-dark'); checks++;
    await evaluate(`change('[data-next-action-field="format"]','html'); change('[data-next-action-field="messageSource"]','<p>HTML [TaskName]</p>')`);
    // Use the actual source-mode toggle so the saved HTML follows the visible editor.
    await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-html-toggle]').click(); change('[data-next-action-field="messageSource"]','<p>HTML [TaskName]</p>'); document.querySelector('[data-traditional-task-editor-next-action-save-template]').click()`);
    for(let i=0;i<100 && !await evaluate('templateCalls.length===2');i++) await pause(20);
    assert.equal(await evaluate('templateCalls[1].format'),'html');
    assert.equal(await evaluate('templateCalls[1].message'),'<p>HTML [TaskName]</p>');
    assert.equal(await evaluate('templateCalls[1].templateId'),'template-saved');
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Traditional)})`);
    await evaluate(`document.querySelector('[data-traditional-task-editor-add-destination]').click(); change('[data-traditional-task-editor-destination-host-type-filter]','SMTP')`);
    await pause(50);
    await evaluate(`change('[data-destination-field="hostId"]',fixture.destinationHosts.find(host=>host.typeCode==='SMTP').id)`);
    for(let i=0;i<100 && !await evaluate(`editor.getStepPatches().some(step=>step.kind==='Destination' && step.patch?.Type==='SMTP')`);i++) await pause(20);
    assert.equal(await evaluate(`editor.getStepPatches().some(step=>step.kind==='Destination' && step.patch?.Type==='SMTP')`),true);
    await evaluate(`change('[data-destination-field="subject"]','SMTP subject')`);
    await evaluate(`document.querySelector('[data-destination-field="isHtml"][value="0"]').click(); const text=document.querySelector('[data-traditional-task-editor-destination-smtp-text-editor]'); text.value='SMTP [TaskName]'; text.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-destination-smtp-save-template]').getClientRects().length > 0`),true);
    await evaluate(`document.querySelector('[data-traditional-task-editor-destination-smtp-save-template]').click()`);
    for(let i=0;i<100 && !await evaluate('templateCalls.length');i++) await pause(20);
    assert.equal(await evaluate('templateCalls[0].format'),'text'); assert.equal(await evaluate('templateCalls[0].message'),'SMTP [TaskName]');
    await check('Template-SMTP-text'); await screenshot('template-SMTP-600-dark'); checks++;
    assert.equal(await evaluate('saveCalls.length'),0);
    // Custom POP3 OAuth must retain its scope/endpoint even when the authority is Microsoft.
    for(const [scope,tokenUrl] of [
        ['custom-scope','https://login.microsoftonline.com/tenant/oauth2/v2.0/token'],
        ['https://outlook.office365.com/.default','https://login.microsoftonline.com/tenant/oauth2/v2.0/token?extra=value']
    ]) {
        const model=structuredClone(fixtures.Traditional),host=model.sourceHosts.find(h=>h.typeCode==='POP3');
        const source=model.task.steps[0].source;
        Object.assign(source,{hostId:host.id,hostName:host.name,hostTypeCode:host.typeCode,hostType:host.type,
            useDefaultCredentials:false,username:'pop-user',passwordConfigured:false,emailUseOAuth:true,
            emailOAuthClientId:'pop-client',emailOAuthScope:scope,emailOAuthTokenUrl:tokenUrl,emailOAuthTenantId:'',
            emailOAuthClientSecretConfigured:true,hostDefaults:host.defaults});
        model.task.steps=[model.task.steps[0]];
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        await evaluate(`document.querySelector('[data-traditional-task-editor-source-tab="host-overrides"]').click();document.querySelector('[data-traditional-task-editor-host-tab="authentication"]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-source-field="emailOAuthType"]').value`),'custom');
        assert.equal(await evaluate(`document.querySelector('[data-source-field="emailOAuthScope"]').value`),scope);
        assert.equal(await evaluate(`document.querySelector('[data-source-field="emailOAuthTokenUrl"]').value`),tokenUrl);
        await check('POP3-custom-Microsoft');
        await evaluate(`change('[data-source-field="fileMask"]','*.csv')`);
        await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click()`);
        for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
        const patch=await evaluate('saveCalls[0].payload.steps[0].patch');
        assert.equal(patch.OAuthScope,scope);assert.equal(patch.OAuthTokenURL,tokenUrl);assert.equal(patch.OAuthClientSecret,undefined);checks+=6;
    }
    // Captured SMTP credential overrides use the production form and patch composer in both editors.
    const smtpChange = async (field,value) => {
        // Each browser command yields to the editor's async change/Host-confirmation handler.
        await evaluate(`change('[data-destination-field="${field}"]',${JSON.stringify(value)})`);
        await evaluate(`new Promise(resolve=>setTimeout(resolve,0))`);
    };
    for (const type of ['Traditional','Advanced']) {
        const model=structuredClone(type==='Advanced'?fixtures.NewAdvanced:fixtures.Traditional);
        const host=model.destinationHosts.find(host=>host.typeCode==='SMTP');
        Object.assign(host.defaults,{emailUseOAuth:true,username:'smtp-user',emailOAuthClientId:'host-client',emailOAuthScope:'https://outlook.office365.com/.default',emailOAuthTokenUrl:'https://login.microsoftonline.com/hosttenant/oauth2/v2.0/token',emailOAuthTenantId:'hosttenant'});
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        if(type==='Advanced')await evaluate(`insertStep('Destination')`);
        else await evaluate(`document.querySelector('[data-traditional-task-editor-add-destination]').click()`);
        await evaluate(`change('[data-traditional-task-editor-destination-host-type-filter]','SMTP')`);
        await smtpChange('hostId',host.id);await smtpChange('addressTo','test@example.test');
        await evaluate(`document.querySelector('[data-traditional-task-editor-destination-tab="host-overrides"]').click();document.querySelector('[data-traditional-task-editor-destination-host-tab="authentication"]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-destination-field="overrideCredentials"]').disabled`),false);
        assert.equal(await evaluate(`document.querySelector('[data-destination-field="emailOAuthClientId"]').value`),'host-client');
        assert.equal(await evaluate(`document.querySelector('[data-destination-field="emailOAuthClientId"]').disabled`),true);
        assert.equal(await evaluate(`editor.getStepPatches().find(step=>step.patch?.Type==='SMTP').patch.UseDefUser`),1);
        await smtpChange('overrideCredentials',true);
        for(const [field,value] of Object.entries({emailOAuthUsername:'smtp-override',emailOAuthClientId:'override-client',emailOAuthClientSecret:'  literal\\secret  ',emailOAuthTenantId:'tenantid2'}))await smtpChange(field,value);
        let patch=await evaluate(`editor.getStepPatches().find(step=>step.patch?.Type==='SMTP').patch`);
        assert.equal(patch.UseOAuth,1);assert.equal(patch.UseDefUser,0);assert.equal(patch.Username,'smtp-override');assert.equal(patch.OAuthClientID,'override-client');assert.equal(patch.OAuthClientSecret,'  literal\\secret  ');
        assert.equal(patch.OAuthScope,'https://outlook.office365.com/.default');assert.equal(patch.OAuthTokenURL,'https://login.microsoftonline.com/tenantid2/oauth2/v2.0/token');assert.equal(patch.Password,undefined);
        await check(type+'-SMTP-OAuth365');await screenshot(type+'-SMTP-OAuth365');checks+=10;
        for(const [field,value] of Object.entries({emailOAuthType:'custom',emailOAuthScope:'https://scope.example.test',emailOAuthTokenUrl:'https://identity.example.test/token'}))await smtpChange(field,value);
        patch=await evaluate(`editor.getStepPatches().find(step=>step.patch?.Type==='SMTP').patch`);
        assert.equal(patch.OAuthScope,'https://scope.example.test');assert.equal(patch.OAuthTokenURL,'https://identity.example.test/token');
        assert.equal(await evaluate(`document.querySelector('[data-destination-email-oauth-custom-only]').hidden`),false);
        assert.equal(await evaluate(`document.querySelector('[data-destination-email-oauth-microsoft-only]').hidden`),true);
        await check(type+'-SMTP-OAuthCustom');await screenshot(type+'-SMTP-OAuthCustom');checks+=4;
        await evaluate(`document.querySelector('[data-destination-field="emailUseOAuth"][value="0"]').click()`);
        await smtpChange('username','legacy-user');await smtpChange('password','legacy-password');
        patch=await evaluate(`editor.getStepPatches().find(step=>step.patch?.Type==='SMTP').patch`);
        assert.equal(patch.UseOAuth,0);assert.equal(patch.Username,'legacy-user');assert.equal(patch.Password,'legacy-password');assert.equal(patch.OAuthClientSecret,undefined);assert.equal(patch.OAuthTokenURL,undefined);
        await smtpChange('overrideCredentials',false);
        patch=await evaluate(`editor.getStepPatches().find(step=>step.patch?.Type==='SMTP').patch`);
        assert.equal(patch.UseDefUser,1);assert.equal(patch.UseOAuth,undefined);assert.equal(patch.Password,undefined);assert.equal(patch.OAuthClientSecret,undefined);
        assert.equal(await evaluate(`document.querySelector('[data-destination-field="emailOAuthClientId"]').value`),'host-client');checks+=10;
    }
    // Do not offer Delete before the authoritative shell/permissions have loaded.
    await evaluate(`window.holdShell=true; window.loadingOpen=loadFixture(${JSON.stringify(fixtures['Sync-Share'])}); void 0`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-delete]').disabled`),true);
    await evaluate(`document.querySelector('[data-traditional-task-editor-delete]').click()`);
    assert.equal(await evaluate('deleteCalls.length'),0);
    await evaluate(`window.holdShell=false; window.releaseShell(); window.loadingOpen`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-delete]').disabled`),false);
    // Editor Delete closes through the normal dirty-state guard, then calls the page workflow.
    for (const model of [fixtures.Traditional, fixtures['Sync-Share']]) {
        await evaluate(`loadFixture(${JSON.stringify(model)})`);
        await evaluate(`document.querySelector('[data-traditional-task-editor-delete]').click()`);
        for(let i=0;i<100 && !await evaluate('deleteCalls.length');i++) await pause(20);
        assert.equal(await evaluate('deleteCalls.length'),1);
        assert.equal(await evaluate('deleteCalls[0].id'),model.task.id);
        assert.equal(await evaluate('deleteCalls[0].editorOpen'),false);
        assert.equal(await evaluate('saveCalls.length'),0);
    }
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`window.confirmAnswer=false; change('[data-source-field="path"]','/draft'); document.querySelector('[data-traditional-task-editor-delete]').click()`);
    await pause(80);
    assert.equal(await evaluate('deleteCalls.length'),0);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),true);
    await evaluate(`window.confirmAnswer=true; document.querySelector('[data-traditional-task-editor-delete]').click()`);
    for(let i=0;i<100 && !await evaluate('deleteCalls.length');i++) await pause(20);
    assert.equal(await evaluate('deleteCalls.length'),1);
    assert.equal(await evaluate('saveCalls.length'),0);
    // A Task editor's MAUI Folder selector follows the native administrator rule.
    for (const model of [fixtures.Traditional, fixtures['Sync-Share'], fixtures.Advanced]) for (const admin of [false,true]) {
        await evaluate(`document.documentElement.dataset.moveitAdmin='${admin}';loadFixture(${JSON.stringify(model)})`);
        assert.equal(await evaluate(`document.querySelector('[data-editor-field="folderId"]').disabled`),!admin,'Only administrators can reassign folders');checks++;
        assert.equal(await evaluate(`document.querySelector('[data-editor-field="name"]').readOnly`),false,'Non-admin Task editing remains available');checks++;
    }
    await evaluate(`document.documentElement.dataset.theme='dark'`);
    assert.equal(await evaluate(`getComputedStyle(document.documentElement).getPropertyValue('--accent').trim()`),'#5aa9f4');checks++;
    // Edit and Run are independent grants, including after reusing the same editor.
    for (const original of [fixtures.Traditional, fixtures['Sync-Share'], fixtures.Advanced]) {
        const denied = structuredClone(original); denied.task.canAlter=true; denied.task.canRun=false;
        await evaluate(`loadFixture(${JSON.stringify(denied)})`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-run-now]').disabled`),true,'Edit access does not enable Run');checks++;
        const allowed = structuredClone(original); allowed.task.canRun=true;
        await evaluate(`loadFixture(${JSON.stringify(allowed)})`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-run-now]').disabled`),false,'The next runnable Task restores Run');checks++;
    }
    const readOnly = structuredClone(fixtures['Sync-Share']); readOnly.task.canAlter=false;
    await evaluate(`loadFixture(${JSON.stringify(readOnly)})`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-delete]').disabled`),true);
    await evaluate(`document.querySelector('[data-traditional-task-editor-delete]').click()`);
    assert.equal(await evaluate('deleteCalls.length'),0);
    await evaluate(`loadFixture(${JSON.stringify(fixtures.NewSync)})`);
    await evaluate(`document.querySelector('[data-traditional-task-editor-add-step]').click()`);
    assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-traditional-task-editor-step-items] strong')].map(node=>node.textContent)`),['Folder A']);
    await evaluate(`document.querySelector('[data-traditional-task-editor-add-step]').click()`);
    assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-traditional-task-editor-step-items] strong')].map(node=>node.textContent)`),['Folder A','Folder B']);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-add-step]').disabled`),true);
    assert.ok(await evaluate(`document.querySelector('[data-traditional-task-editor-add-process]').hidden`));
    assert.ok(await evaluate(`document.querySelector('[data-traditional-task-editor-add-destination]').hidden`));
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`window.failSave=true; change('[data-source-field="path"]','/uncertain'); document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100 && !await evaluate('saveCalls.length');i++) await pause(20);
    await pause(100);
    assert.equal(await evaluate('saveCalls.length'),1);
    assert.match(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent`),/Reload Task/);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),true);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-delete]').disabled`),true);
    await evaluate(`document.querySelector('[data-traditional-task-editor-save]').click()`);
    assert.equal(await evaluate('saveCalls.length'),1);
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`window.editConflict=true; change('[data-source-field="path"]','/stale'); document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100 && !await evaluate('saveCalls.length');i++) await pause(20);
    await pause(100);
    assert.match(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent`),/Reload Task/);
    assert.ok(await evaluate(`typeof window.saveCalls[0].payload.editVersion==='string' && window.saveCalls[0].payload.editVersion.length>0`));
    await evaluate(`change('[data-source-field="path"]','/retry-stale'); document.querySelector('[data-traditional-task-editor-save]').click()`);
    assert.equal(await evaluate(`window.saveCalls.length`),1,'stale Task drafts require reconciliation before retry');
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Sync-Share'])})`);
    await evaluate(`window.failNetwork=true; change('[data-source-field="path"]','/disconnected'); document.querySelector('[data-traditional-task-editor-save]').click();`);
    for(let i=0;i<100 && !await evaluate('saveCalls.length');i++) await pause(20);
    await pause(100);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),true);
    assert.match(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent`),/Reload Task/);
    assert.deepEqual(await evaluate('layoutErrors'),[]);
    // Opening a locked Task must populate its editor after confirmed recovery, not leave a loading/error shell.
    await evaluate(`document.documentElement.dataset.moveitAdmin='true';window.lockOnOpening=true`);
    for (const name of ['Traditional','Advanced','Sync-Share']) {
        const model=structuredClone(fixtures[name]);model.checkoutObjectsForDurationOfEdit=true;
        await evaluate(`window.recoveryCalls=[];loadFixture(${JSON.stringify(model)})`);
        assert.equal(await evaluate(`document.querySelector('[data-editor-field="name"]').value`),model.task.name);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),true);
        assert.equal(await evaluate(`recoveryCalls.filter(call=>call.url.endsWith('/Override')).length`),1);
        assert.match(await evaluate(`confirmCalls.at(-1).message`),/This object has been checked out to miadmin since/);
        assert.equal(await evaluate('saveCalls.length'),0,'opening recovery must never save the Task');checks+=5;
    }
    await evaluate(`window.lockOnOpening=false`);
    // The same recovery contract serves Traditional, Advanced and Sync editors.
    await evaluate(`document.documentElement.dataset.moveitAdmin='true'`);
    for(const name of ['Traditional','Advanced','Sync-Share']) {
        const recovery=structuredClone(fixtures[name]);recovery.preferences={...recovery.preferences,autoSave:true};
        await evaluate(`loadFixture(${JSON.stringify(recovery)})`);
        await evaluate(`window.editConflict=true;window.recoveryCalls=[];change('[data-editor-field="name"]',${JSON.stringify('Retained '+name)});document.querySelector('[data-traditional-task-editor-save]').click()`);
        for(let i=0;i<100&&!await evaluate('saveCalls.length');i++)await pause(20);
        await pause(80);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-override]').hidden`),false);
        await evaluate(`document.querySelector('[data-traditional-task-editor-override]').click()`);
        for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent.includes('Checkout restored')`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-editor-field="name"]').value`),'Retained '+name);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').textContent`),'Save');
        await pause(750);assert.equal(await evaluate('saveCalls.length'),1,'recovery cannot auto-save '+name);
        await evaluate(`window.editConflict=false;document.querySelector('[data-traditional-task-editor-save]').click()`);
        for(let i=0;i<100&&await evaluate('saveCalls.length')<2;i++)await pause(20);
        assert.equal(await evaluate('saveCalls.at(-1).payload.editVersion'),'recovered-task-revision');
        assert.equal(await evaluate('saveCalls.at(-1).payload.task.name'),'Retained '+name);
        assert.equal(await evaluate('saveCalls.at(-1).payload.captureUndo'),true,'recovered Save captures the current server copy for Undo');
        await pause(100);checks+=7;
    }

    // Built-in Script guidance: synthetic documented metadata, production modal/runtime and save composition.
    const guideModel=structuredClone(fixtures.Traditional);
    guideModel.preferences={autoSave:false};guideModel.task.parameters=[{name:'Existing_Param',value:'retained',secure:false}];
    guideModel.scriptOptions=[{id:'100001',name:'Example Built-in',type:'Standard Script',description:'Inventory description'},{id:'100002',name:'Other Built-in',type:'Standard Script'},{id:'42',name:'Custom',type:'Custom Script',description:'Custom instructions'}];
    guideModel.task.steps=[{id:'11',index:0,kind:'Process',title:'Process',subtitle:'',process:{id:'11',scriptId:'100001',scriptName:'Example Built-in',scriptType:'Standard Script',run:'PerFile',isDestination:false,parameters:[]}}];
    const scriptDetails={id:'100001',name:'Example Built-in',description:'Instructions from MOVEit. <img src=x onerror=window.scriptInjected=true>',processDescription:'',parametersAvailable:true,parameters:[
        {name:'Required_Input',description:'Supply the required value.',direction:'In',required:true,type:'Text',defaultValue:null,choices:[]},
        {name:'Optional_Input',description:'Optional override.',direction:'In',required:false,type:'Text',defaultValue:'Default.txt',choices:['Default.txt','Other.txt']},
        {name:'Output_Result',description:'Provided by the Script.',direction:'Out',required:true,type:'Text',defaultValue:null,choices:[]}]};
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate(`window.scriptMetadataReplies={'100001':${JSON.stringify(scriptDetails)}};window.scriptMetadataCalls=[];loadFixture(${JSON.stringify(guideModel)})`);await pause(150);
    assert.equal(await evaluate(`document.querySelector('[data-script-guidance-description]').textContent`),scriptDetails.description);
    assert.equal(await evaluate(`document.querySelector('[data-script-guidance] img')`),null);
    assert.equal(await evaluate(`document.querySelectorAll('[data-script-guidance-rows] tr').length`),3);
    assert.equal(await evaluate(`document.querySelectorAll('[data-script-guidance-rows] button').length`),2,'Output parameters have no input controls');
    assert.match(await evaluate(`document.querySelector('[data-script-guidance-required]').textContent`),/Required parameters not configured/);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),true,'Metadata read is not a draft edit');
    assert.deepEqual(await evaluate('scriptMetadataCalls'),['100001'],'Current Process and Task requirements share one read');
    for (const theme of ['light','dark']) {
        await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);await pause(250);
        assert.equal(await evaluate(`[...document.querySelectorAll('[data-script-guidance-rows] button')].every(button=>button.getBoundingClientRect().height<50&&getComputedStyle(button).whiteSpace==='nowrap')`),true);checks++;
        await screenshot('Script-Guidance-Traditional-'+theme);
    }

    await evaluate(`document.querySelector('[data-script-guidance-edit]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-parameters-dialog]').open`),true);
    assert.equal(await evaluate(`document.querySelectorAll('[data-script-required-name]').length`),1);
    assert.match(await evaluate(`document.querySelector('[data-script-required-name]').textContent`),/Required but not configured/);
    await screenshot('Script-Required-Task-Parameters');
    await evaluate(`document.querySelector('[data-traditional-task-editor-parameters-close]').click();change('[data-editor-field="name"]','Changed task');document.querySelector('[data-traditional-task-editor-save]').click()`);await pause(150);
    assert.deepEqual(await evaluate('saveCalls.at(-1).payload.parameters.Parameter.map(p=>p.name)'),['Existing_Param'],'Required placeholders are never serialized');
    await evaluate(`document.querySelector('[data-script-guidance-edit]').click();document.querySelector('[data-script-required-name] button').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-new-parameter-name]').value`),'Required_Input');
    assert.match(await evaluate(`document.querySelector('[data-task-script-parameter-help]').textContent`),/Supply the required value/);
    await evaluate(`document.querySelector('[data-traditional-task-editor-task-parameter-save]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-task-parameter-error]').hidden`),false);
    await evaluate(`change('[data-traditional-task-editor-new-parameter-value]','Configured');document.querySelector('[data-traditional-task-editor-task-parameter-save]').click()`);
    assert.equal(await evaluate(`document.querySelectorAll('[data-script-required-name]').length`),0);
    await evaluate(`document.querySelector('[data-traditional-task-editor-parameters-close]').click();document.querySelector('[data-traditional-task-editor-undo]').click()`);
    assert.match(await evaluate(`document.querySelector('[data-script-guidance-required]').textContent`),/Required parameters not configured/);
    await evaluate(`document.querySelector('[data-script-guidance-edit]').click()`);
    assert.equal(await evaluate(`document.querySelectorAll('[data-script-required-name]').length`),1);checks+=2;
    await evaluate(`document.querySelector('[data-script-required-name] button').click();change('[data-traditional-task-editor-new-parameter-value]','Configured');document.querySelector('[data-traditional-task-editor-task-parameter-save]').click();document.querySelector('[data-traditional-task-editor-parameters-close]').click();[...document.querySelectorAll('[data-script-guidance-rows] button')][1].click()`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-new-parameter-value]').value`),'Default.txt');
    assert.match(await evaluate(`document.querySelector('[data-task-script-parameter-help]').textContent`),/Choices: Default.txt, Other.txt/);
    await evaluate(`document.querySelector('[data-traditional-task-editor-task-parameter-save]').click();document.querySelector('[data-traditional-task-editor-save]').click()`);await pause(150);
    assert.equal(await evaluate(`saveCalls.at(-1).payload.parameters.Parameter.find(p=>p.name==='Optional_Input').value`),'Default.txt');
    checks+=18;
    const advancedGuide=structuredClone(fixtures.NewAdvanced);advancedGuide.preferences={autoSave:false};advancedGuide.scriptOptions=guideModel.scriptOptions;
    advancedGuide.task.parameters=[{name:'Required_Input',value:'Task fallback',secure:false}];
    await evaluate(`loadFixture(${JSON.stringify(advancedGuide)})`);
    await evaluate(`insertStep('Process');change('[data-process-field="scriptId"]','100001')`);await pause(150);
    assert.equal(await evaluate(`document.querySelector('[data-script-guidance-required]').textContent`),'');
    assert.match(await evaluate(`document.querySelector('[data-script-guidance-rows]').textContent`),/Inherited from Task/);
    for (const width of [1920,1366]) {
        await command('Emulation.setDeviceMetricsOverride',{width,height:1080,deviceScaleFactor:1,mobile:false});
        assert.equal(await evaluate(`(()=>{const header=document.querySelector('.task-script-reference-table th:nth-child(2)');const range=document.createRange();range.selectNodeContents(header);return getComputedStyle(header).whiteSpace==='nowrap'&&range.getClientRects().length===1;})()`),true,'Direction header stays on one line in the Advanced properties panel');checks++;
        await screenshot('Script-Guidance-Advanced-'+width);
    }
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate(`document.querySelector('[data-script-guidance-edit]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-process-panel="parameters"]').hidden`),false);
    assert.equal(await evaluate(`document.querySelectorAll('[data-script-required-name]').length`),0);
    const emptyAdvanced=structuredClone(advancedGuide);emptyAdvanced.task.parameters=[];
    await evaluate(`loadFixture(${JSON.stringify(emptyAdvanced)})`);
    await evaluate(`insertStep('Process');change('[data-process-field="scriptId"]','100001')`);await pause(150);
    await evaluate(`document.querySelector('[data-script-guidance-edit]').click();document.querySelector('[data-script-required-name] button').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-process-parameter-name]').value`),'Required_Input');
    await evaluate(`change('[data-traditional-task-editor-process-parameter-value]','Process value');document.querySelector('[data-traditional-task-editor-process-parameter-save]').click();document.querySelector('[data-traditional-task-editor-save]').click()`);await pause(150);
    assert.equal(await evaluate(`saveCalls.at(-1).payload.steps.find(step=>step.kind==='Process').patch.Parameters.any.find(parameter=>parameter.name==='Required_Input').value`),'Process value');
    assert.deepEqual(await evaluate(`saveCalls.at(-1).payload.parameters.Parameter`),[],'Advanced Process values do not become Task parameters');
    checks+=7;
    // Retain secure native values and avoid treating a suppressed value as a missing requirement.
    const secureGuide=structuredClone(guideModel);secureGuide.task.parameters=[{name:'Required_Input',value:'(suppressed)',secure:true}];
    await evaluate(`loadFixture(${JSON.stringify(secureGuide)})`);await pause(100);
    assert.equal(await evaluate(`document.querySelector('[data-script-guidance-required]').textContent`),'');
    await evaluate(`change('[data-editor-field="name"]','Secure retained');document.querySelector('[data-traditional-task-editor-save]').click()`);await pause(100);
    assert.deepEqual(await evaluate(`saveCalls.at(-1).payload.parameters.Parameter[0]`),{name:'Required_Input',originalName:'Required_Input',secure:true,preserve:true});
    checks+=2;
    // An old selection or an old editor can never replace the current Script guidance.
    await evaluate(`scriptMetadataReplies['100001']={...scriptMetadataReplies['100001'],delay:true};loadFixture(${JSON.stringify(guideModel)})`);await pause(80);
    await evaluate(`change('[data-process-field="scriptId"]','42');releaseScriptMetadata()`);await pause(80);
    assert.equal(await evaluate(`document.querySelector('[data-script-guidance-description]').textContent`),'Custom instructions');
    await evaluate(`scriptMetadataReplies['100001'].delay=false;scriptMetadataReplies['100001'].failure=true;loadFixture(${JSON.stringify(guideModel)})`);await pause(80);
    assert.match(await evaluate(`document.querySelector('[data-script-guidance-status]').textContent`),/guidance unavailable/);
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),true);
    await evaluate(`scriptMetadataReplies['100001'].failure=false;document.querySelector('[data-script-guidance-retry]').click()`);await pause(80);
    assert.equal(await evaluate(`document.querySelectorAll('[data-script-guidance-rows] tr').length`),3);
    checks+=4;
    await evaluate(`scriptMetadataReplies['100001'].delay=true;loadFixture(${JSON.stringify(guideModel)})`);await pause(80);
    const customGuide=structuredClone(guideModel);customGuide.task.steps[0].process={...customGuide.task.steps[0].process,scriptId:'42',scriptName:'Custom',scriptType:'Custom Script'};
    await evaluate(`window.oldMetadataResponse=releaseScriptMetadata;loadFixture(${JSON.stringify(customGuide)})`);await pause(80);await evaluate(`oldMetadataResponse()`);await pause(80);
    assert.equal(await evaluate(`document.querySelector('[data-script-guidance-description]').textContent`),'Custom instructions');checks++;
    await evaluate(`scriptMetadataReplies['100001'].delay=false`);
    const readOnlyGuide=structuredClone(guideModel);readOnlyGuide.task.canAlter=false;
    await evaluate(`loadFixture(${JSON.stringify(readOnlyGuide)})`);await pause(100);
    assert.equal(await evaluate(`document.querySelector('[data-script-guidance-edit]').disabled`),true);
    assert.equal(await evaluate(`[...document.querySelectorAll('[data-script-guidance-rows] button')].every(button=>button.disabled)`),true);checks+=2;
    assert.equal(await evaluate(`document.querySelector('[data-editor-field="name"]').readOnly`),true,'Read-only Task fields are inspectable');checks++;
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-traditional-task-editor-actions]')).display`),'none','Read-only Task Actions are hidden');checks++;
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-traditional-task-editor-save]')).display`),'none');checks++;
    for (const fixtureName of ['Traditional','NewAdvanced','Sync-Share']) {
        const model=structuredClone(fixtures[fixtureName]);model.task.canAlter=false;model.preferences.autoSave=true;
        await evaluate(`loadFixture(${JSON.stringify(model)})`);await pause(100);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dirty-state]').textContent`), 'Read-only: your MOVEit account does not permit editing this Task.');checks++;
        assert.equal(await evaluate(`[...document.querySelectorAll('[data-traditional-task-editor-dialog] [contenteditable]')].every(node=>node.contentEditable==='false')`),true,'Read-only HTML message bodies cannot be edited');checks++;
        await evaluate(`window.readOnlyConfirms=0;window.savedConfirm=mauiConfirm;window.mauiConfirm=async()=>{readOnlyConfirms++;return false;}`);
        await evaluate(`document.querySelector('[data-traditional-task-editor-cancel]').click()`);await pause(70);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-dialog]').open`),false,'Read-only '+fixtureName+' closes without a discard prompt');checks++;
        assert.equal(await evaluate('readOnlyConfirms'),0);assert.equal(await evaluate('saveCalls.length'),0);checks+=2;
        await evaluate(`window.mauiConfirm=savedConfirm`);
    }
    // Script metadata itself must never initiate Auto Save.
    const autoGuide=structuredClone(guideModel);autoGuide.preferences.autoSave=true;
    await evaluate(`loadFixture(${JSON.stringify(autoGuide)})`);await pause(1000);
    assert.equal(await evaluate('saveCalls.length'),0);
    assert.equal(await evaluate(`[...document.querySelectorAll('[data-traditional-task-editor-dialog] [contenteditable]')].every(node=>node.contentEditable==='true')`),true,'Opening an editable Task restores HTML editing');checks++;
    assert.deepEqual(await evaluate('layoutErrors'),[]);checks+=2;
    // Missing legacy/system Task defaults must become a valid enum on every editor save.
    for (const name of ['Traditional','NewAdvanced','Sync-Share']) {
        for (const cacheNames of [null,'','orig']) {
            const cacheModel=structuredClone(fixtures[name]);cacheModel.preferences.autoSave=false;
            if(cacheNames===null)delete cacheModel.task.advanced.cacheNames;else cacheModel.task.advanced.cacheNames=cacheNames;
            await evaluate(`loadFixture(${JSON.stringify(cacheModel)})`);
            const expected=cacheNames==='orig'?'orig':'random';
            assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-cache-names]').value`),expected);
            assert.deepEqual(await evaluate(`[...document.querySelector('[data-traditional-task-editor-cache-names]').options].map(option=>option.value)`),['random','orig']);
            await evaluate(`change('[data-editor-field="operatorNotes"]','Cache regression');document.querySelector('[data-traditional-task-editor-save]').click()`);
            for(let attempt=0;attempt<100&&!await evaluate('saveCalls.length');attempt++)await pause(20);
            assert.equal(await evaluate('saveCalls.at(-1).payload.task.cacheNames'),expected);
            checks+=3;
        }
    }
    // All Task types share the same template capability, separate from Task edit permission.
    await command('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/?templates=denied` });
    for(let i=0;i<100&&!await evaluate('Boolean(window.editor)');i++)await pause(50);
    for(const name of ['Traditional','Advanced','Sync-Share']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures[name])})`);
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-next-action-save-template]').disabled`),true,name);checks++;
        assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-destination-smtp-save-template]').disabled`),true,name);checks++;
    }
    await evaluate(`loadFixture(${JSON.stringify(fixtures.Traditional)})`);
    await evaluate(`document.querySelector('[data-traditional-task-editor-section-button="next-actions"]').click();document.querySelector('[data-traditional-task-editor-add-email-action]').click();document.querySelector('[data-traditional-task-editor-next-action-tab="message"]').click();change('[data-next-action-field="subject"]','Shared template denied');change('[data-next-action-field="messageText"]','Ordinary task edit');document.querySelector('[data-traditional-task-editor-next-action-save-template]').dispatchEvent(new Event('click'));`);
    assert.equal(await evaluate('templateCalls.length'),0);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-traditional-task-editor-save]').disabled`),false,'Task editing remains available');checks++;
    assert.deepEqual(await evaluate('layoutErrors'),[]);checks++;
    fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({checks,fixtures:Object.keys(fixtures),widths:[1920,1400,1024,800,600],themes:['light','dark']},null,2));
    console.log(`Passed ${checks} Sync/Traditional rendered layout checks and routing/save/folder regressions.`);
})().catch(async error => {
    console.error(error); process.exitCode=1;
    if(socket && socket.readyState===WebSocket.OPEN) try { await screenshot('FAILED'); } catch {}
}).finally(async () => {
    socket?.close(); server?.close(); browser?.kill();
    if(browser && process.platform==='win32') spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});
});
