'use strict';
// Real Edge/Chromium rendering of the production dialog, CSS and complete editor script.
// Fixtures are emitted by HostEditorService during xUnit; no native MOVEit endpoint is contacted.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const http = require('node:http');
const { spawn, spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const fixturePath = process.env.MAUI_HOST_EDITOR_FIXTURE_PATH || path.join(root, 'TestResults/HostEditorFixtures.json');
const fixtures = JSON.parse(fs.readFileSync(fixturePath, 'utf8'));
const output = path.join(root, 'TestResults/HostEditorLayout');
fs.mkdirSync(output, { recursive: true });
const candidates = [process.env.MAUI_BROWSER_EXECUTABLE,
    process.env['ProgramFiles(x86)'] && path.join(process.env['ProgramFiles(x86)'], 'Microsoft/Edge/Application/msedge.exe'),
    process.env.ProgramFiles && path.join(process.env.ProgramFiles, 'Microsoft/Edge/Application/msedge.exe'),
    '/usr/bin/chromium', '/usr/bin/chromium-browser', '/opt/google/chrome/chrome'];
const executable = candidates.find(candidate => candidate && fs.existsSync(candidate));
assert.ok(executable, 'An installed Edge or Chromium browser is required. Set MAUI_BROWSER_EXECUTABLE.');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const sshDefaults = JSON.parse(read('appsettings.template.json')).SshAlgorithms;
const sshSettingsMarkup = read('Views/Configuration/Settings.cshtml').match(/<section class="configuration-section" data-config-section="ssh-algorithms"[\s\S]*?<\/section>/)[0]
    .replaceAll('@Model.SshEncryption', sshDefaults.Encryption.Value).replaceAll('@SshAlgorithmOptions.InitialEncryption', sshDefaults.Encryption.Value)
    .replaceAll('@Model.SshKeyExchange', sshDefaults.KeyExchange.Value).replaceAll('@SshAlgorithmOptions.InitialKeyExchange', sshDefaults.KeyExchange.Value)
    .replace('class="configuration-section"', 'class="configuration-section active"');
assert.ok(!/@(?:Model|SshAlgorithmOptions)\./.test(sshSettingsMarkup), 'Settings fixture must resolve its Razor values');
const renderIcons = require('./render-razor-icons.cjs');
const iconMarkup = read('Views/Shared/_Icon.cshtml');
const markup = renderIcons(read('Views/Shared/Editors/_HostEditorDialog.cshtml').replace(/@\*[\s\S]*?\*@/g, ''), iconMarkup);
assert.ok(!markup.includes('@'), 'The static Host Editor dialog must not contain unresolved Razor expressions.');
const informationSources=[['Views/Shared/_TaskInformationDialog.cshtml','task'],['Views/Hosts/Index.cshtml','host'],['Views/Resources/Scripts.cshtml','script'],['Views/Resources/DateLists.cshtml','date-list'],['Views/Resources/KeysAndCerts.cshtml','key-certificate'],['Views/Resources/GlobalParameters.cshtml','global-parameter'],['Views/Shared/_ReportManagementDialogs.cshtml','report']];
const informationMarkup=informationSources.map(([file,name])=>{const marker=name==='report'?'data-report-edit-dialog':'data-'+name+'-details-dialog';const dialog=read(file).match(new RegExp('<dialog[^>]*'+marker+'[^>]*>[\\s\\S]*?<\\/dialog>'))?.[0];assert.ok(dialog,'Missing Information dialog: '+name);return renderIcons(dialog.replace(/@\*[\s\S]*?\*@/g,''),iconMarkup);}).join('');
assert.ok(!informationMarkup.includes('@'),'Information fixtures contain unresolved Razor');

// Render each production Information audit builder in its real card; transport is not involved.
const informationAuditScripts = [['host','hosts'],['task','task-information'],['script','scripts'],['date-list','datelists'],['key-certificate','keys-certificates'],['global-parameter','globalparameters']].map(([name,file])=>{
    const source=read('wwwroot/js/'+file+'.js'), match=/^( +)function populateAudit\(audit\) \{/m.exec(source);
    assert.ok(match,'Missing audit renderer: '+name);
    const end=source.indexOf('\n'+match[1]+'}',match.index+match[0].length)+match[1].length+2, fn=source.slice(match.index,end);
    return 'window.informationAuditRenderers['+JSON.stringify(name)+']=(metadataAudit)=>{const formatDate=value=>value?"October 15, 2026":"—";'+fn+';populateAudit({createdUtc:"2026-10-15T12:00:00Z",createdBy:"Marc",modifiedUtc:"2026-10-15T13:00:00Z",modifiedBy:"Reviewer"});};';
}).join('\n');

// Use the production tab callbacks so runtime footer changes cannot hide behind static fixtures.
const informationTabScripts = [['host','hosts','setHostDetailsTab'],['task','task-information','setTab'],['script','scripts','setScriptDetailsTab'],['date-list','datelists','setDateListDetailsTab'],['key-certificate','keys-certificates','setDetailsTab']].map(([name,file,method])=>{
    const source=read('wwwroot/js/'+file+'.js'),match=new RegExp('^( +)function '+method+'\([^\n]*\) \{','m').exec(source);
    assert.ok(match,'Missing tab callback: '+name);
    const end=source.indexOf('\n'+match[1]+'}',match.index+match[0].length)+match[1].length+2,fn=source.slice(match.index,end);
    return 'window.informationTabRenderers['+JSON.stringify(name)+']=(dialog,panel)=>{const detailsTabs=[...dialog.querySelectorAll("[data-'+name+'-details-tab]")],informationPanel=dialog.querySelector("[data-'+name+'-details-panel=information]"),tasksPanel=dialog.querySelector("[data-'+name+'-details-panel=tasks]"),logsPanel=dialog.querySelector("[data-'+name+'-details-panel=logs]"),infoPanel=informationPanel,depsPanel=dialog.querySelector("[data-'+name+'-details-panel=dependencies]"),detailsSave=dialog.querySelector("[data-'+name+'-details-save]"),detailsCancel=dialog.querySelector("[data-'+name+'-details-cancel]"),taskDependenciesLoaded=true,taskLogsLoaded=true;'+fn+';'+method+'(panel,false);};';
}).join('\n');

const gridPageSelection = read('Views/Shared/_DataGrid.cshtml').match(/<div class="row-select-tools">\s*<input[^>]*data-page-select[^>]*\/>\s*<\/div>/)?.[0]?.replace(/\s+/g,' ');
assert.ok(gridPageSelection,'Grid header selection uses the shared centered wrapper');
const gridActionsButton = read('Views/Shared/_DataGrid.cshtml').split('\n').find(line=>line.includes('data-menu-button')&&line.includes('grid-actions-button')).trim();
const newHostDialogSource=read('Views/Shared/_NewHostDialog.cshtml').match(/<dialog[\s\S]*?<\/dialog>/)[0];
function renderNewHostMarkup(oneDriveAvailable) {
    const dialog=newHostDialogSource.replace(/@if \(oneDriveAvailable\)\s*\{([\s\S]*?)\n\s*\}/g,(_match,body)=>oneDriveAvailable?body:'')
        .replace(/@\(Model \? "checked" : null\)/g,'');
    return require('./render-creation-groups.cjs')(renderIcons(dialog,iconMarkup),read('Views/Shared/_ObjectCreationResourceGroup.cshtml'));
}
const newHostMarkup=renderNewHostMarkup(true),legacyNewHostMarkup=renderNewHostMarkup(false);
assert.ok(!newHostMarkup.includes('@')&&!legacyNewHostMarkup.includes('@'),'New Host fixtures contain no unresolved Razor');
assert.ok(newHostMarkup.includes('value="OneDrive"'),'2026 fixture includes OneDrive');
assert.ok(!legacyNewHostMarkup.includes('value="OneDrive"'),'Older-version fixture omits OneDrive');
const newHostSource=read('wwwroot/js/shared/new-host-dialog.js'),newHostSyncStart=newHostSource.indexOf('        function updateNewHostUncField() {');
const newHostSync=newHostSource.slice(newHostSyncStart,newHostSource.indexOf('\n        }',newHostSyncStart)+10);
const newHostFieldScript=`window.syncNewHostConnectionField=()=>{const newHostDialog=document.querySelector('[data-new-host-dialog]'),newHostUncField=newHostDialog.querySelector('[data-new-host-unc-field]'),newHostUnc=newHostDialog.querySelector('[data-new-host-unc]');${newHostSync};updateNewHostUncField();};`;
const taskSource=read('wwwroot/js/tasks.js');
const notificationMarkup=read('Views/Tasks/Index.cshtml').match(/<dialog[^>]*data-task-notification-dialog[^>]*>[\s\S]*?<\/dialog>/)[0];
const preflightMarkup=renderIcons(read('Views/Tasks/Index.cshtml').match(/<dialog[^>]*data-task-preflight-dialog[^>]*>[\s\S]*?<\/dialog>/)[0],iconMarkup);
assert.ok(!preflightMarkup.includes('@'),'Preflight fixture resolves production icons');
assert.ok(!/@(?:Model|Html|Url)\b|<partial|@(?:for|if)\s*\(/.test(notificationMarkup),'Notification fixture has no unresolved Razor');
const notificationMacrosScript=taskSource.slice(taskSource.indexOf('    const notificationMacros = ['),taskSource.indexOf('    function selectedNotificationMode()'));
const formatStart=taskSource.indexOf('    function setNotificationFormatUi('),formatEnd=taskSource.indexOf('\n    function insertTextAtCaret(',formatStart);
assert.ok(formatStart>0&&formatEnd>formatStart);
const notificationAppearanceScript=`window.showNotificationAppearance=mode=>{
    const root=document.querySelector('[data-task-notification-dialog]');
    const notificationMacroSelects=root.querySelectorAll('[data-notification-subject-macro],[data-notification-body-macro]');
    ${notificationMacrosScript}
    const notificationFormat=root.querySelector('[data-notification-format]'),notificationHtmlToolbar=root.querySelector('[data-notification-html-toolbar]'),notificationHtmlEditor=root.querySelector('[data-notification-html-editor]'),notificationHtmlSource=root.querySelector('[data-notification-html-source]'),notificationTextEditor=root.querySelector('[data-notification-text-editor]'),notificationHtmlToggle=root.querySelector('[data-notification-html-toggle]');
    const notificationHtmlOnlyControls=[...root.querySelectorAll('[data-notification-command],[data-notification-link],[data-notification-html-toggle]')];
    let notificationLastFormat='html',notificationHtmlSourceMode=mode==='source';
    const updateNotificationModeUi=()=>{};
    ${taskSource.slice(formatStart,formatEnd)}
    notificationFormat.value=mode==='text'?'text':'html';notificationHtmlEditor.textContent='Example notification';notificationTextEditor.value='Example notification';notificationHtmlSource.value='<p>Example notification</p>';
    setNotificationFormatUi();if(!root.open)root.showModal();
};`;
// Exercise Quick Transfer's production caller with the same real shared modal.
const quickSource=read('wwwroot/js/quick-transfer.js');
const quickBrowse=quickSource.slice(quickSource.indexOf('    function openBrowse(context)'),quickSource.indexOf('    // ---- Macros ----'));
const quickAdapterScript=`(() => {
    let sourceRoot={name:'',id:'',kind:''},destinationRoot={name:'',id:'',kind:''};
    const busy=false,transferInProgress=false,preparedTaskId='';
    const sourcePath={value:'/incoming'},sourceFile={value:'*.csv'},destinationPath={value:'/outgoing'},destinationFile={value:'keep.txt'};
    const sourceHost={value:'42',selectedOptions:[{textContent:'Quick Host',dataset:{browseSupported:'true'}}]},destinationHost=sourceHost;
    const hideError=()=>{},showError=message=>{throw new Error(message)},endpoint=()=>'/quick-browse',selectedRootKind=()=>window.quickCloud?'Bucket':'';
    const updateRootSummary=()=>{},clearPreparedTransfer=()=>{window.quickInvalidations++};
    const fetchJson=async (_url,options)=>{window.quickRequests.push(JSON.parse(options.body));return window.quickReply(options);};
    ${quickBrowse}
    window.quickRequests=[];window.quickInvalidations=0;
    window.quickReply=async options=>{const request=JSON.parse(options.body);return window.quickCloud&&!request.rootName?
        {currentDirectory:'',rootName:'',rootKind:'Bucket',items:[{name:'bucket',path:'',nativeType:'Bucket',nativeId:'b1',isDirectory:true}]}:
        {currentDirectory:request.directory||'/',parentDirectory:'',rootName:request.rootName,items:[{name:'child',path:'/child',isDirectory:true},{name:'chosen.txt',path:'/incoming/chosen.txt',isDirectory:false,size:'1024',lastModified:'2026-10-19'}]};};
    window.openQuickBrowse=openBrowse;window.quickValues=()=>({sourcePath:sourcePath.value,sourceFile:sourceFile.value,destinationPath:destinationPath.value,destinationFile:destinationFile.value,sourceRoot,destinationRoot});
})();`;
const browserMarkup=read('Views/Shared/_HostBrowser.cshtml').replace(/@\*[\s\S]*?\*@/g,'');
const hostActionMarkup=read('Views/Shared/_HostActionDialogs.cshtml').replace(/@\*[\s\S]*?\*@/g,'');
const hostExportMarkup=read('Views/Shared/_NativePortabilityExportDialog.cshtml').slice(read('Views/Shared/_NativePortabilityExportDialog.cshtml').indexOf('<dialog')).replace('@plural','Hosts');
assert.ok(!hostExportMarkup.includes('@'),'Export fixture resolves its only Razor value');
const html = `<!doctype html><html><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}
${read('wwwroot/css/host-browser.css')}
${read('wwwroot/css/editors/host-editor.css')}
${read('wwwroot/css/script-editor.css')}
${read('wwwroot/css/ssh-algorithm-list.css')}
.button-caret{transition:none!important;}</style></head><body>${markup}${browserMarkup}${informationMarkup}${newHostMarkup}${notificationMarkup}${preflightMarkup}${hostActionMarkup}<div data-host-portability-root>${hostExportMarkup}</div><div data-task-portability-root>${hostExportMarkup.replace("Export Hosts","Export Tasks")}</div>
<script>${read('wwwroot/js/grid-actions.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/modal-ui.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/ssh-algorithm-list.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/editors/object-edit-lease.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/shared/folder-browser-sorting.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/shared/host-browser.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/editors/host-editor.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/native-portability.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/shared/host-actions.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>${read('wwwroot/js/editors/task-host-editor.js').replace(/<\/script/gi, '<\\/script')}</script>
<script>
${quickAdapterScript}
window.mauiAppUrl = route => '/maui' + route;
window.confirmCalls=[];window.mauiConfirm = async options => {confirmCalls.push(options);return true;};
window.layoutErrors = [];
window.addEventListener('error', event => layoutErrors.push(event.message));
window.addEventListener('unhandledrejection', event => layoutErrors.push(String(event.reason)));
window.informationAuditRenderers = {};
${informationAuditScripts}
window.informationTabRenderers = {};
${informationTabScripts}
window.inspectPropertyHeading = heading => {
    const cells=[...heading.querySelectorAll('.task-identity-item')].filter(n=>getComputedStyle(n).display!=='none'), boxes=cells.map(n=>n.getBoundingClientRect()), badge=heading.querySelector('.source-badge'), h=heading.getBoundingClientRect(), b=badge?.getBoundingClientRect();
    return cells.length===2 && boxes[1].left>=boxes[0].right && Math.abs(boxes[0].top-boxes[1].top)<1 && Math.abs(boxes[0].left-h.left)<1 && cells.every(n=>{const label=n.firstElementChild.getBoundingClientRect(),value=n.lastElementChild.getBoundingClientRect();return value.top>=label.bottom&&Math.abs(label.left-value.left)<1&&getComputedStyle(n).textAlign==='left';}) && b && Math.abs(b.right-h.right)<1 && Math.abs(b.top-h.top)<1 && getComputedStyle(badge).fontSize==='10px';
};
window.lookupCalls = []; window.leaseCalls=[]; window.hostSaveCount=0;
document.documentElement.dataset.hostEditorPreferencesUrl='/maui/HostEditor/SavePreferences';
window.hostBrowseCalls=[];
window.actionRequests=[];
window.fetch = async (url, options = {}) => {
    if (String(url).includes('/Hosts/')) {
        actionRequests.push({url:String(url),headers:new Headers(options.headers),body:JSON.parse(options.body)});
        if (String(url).endsWith('/TestHost')) return new Response(JSON.stringify({status:window.testFailure?'Failed':'Passed',message:'Connection refused.'}));
        if (String(url).endsWith('/CloneHost')) {
            if(window.cloneFailure)return new Response(JSON.stringify({message:'Name already exists.',moveItSaved:false}),{status:409});
            window.fixture=structuredClone(fixture);fixture.host.id='900';fixture.host.name='Cloned Host';fixture.host.assignedCount=0;fixture.host.tasks=[];
            return new Response(JSON.stringify({success:true,host:{id:'900',name:'Cloned Host'}}));
        }
        if (String(url).endsWith('/ExportHost') && window.hostExportMode==='network') throw new Error('Unable to reach the export server.');
        if (String(url).endsWith('/ExportHost') && window.hostExportMode==='empty') return new Response(new Uint8Array(),{headers:{'Content-Type':'application/zip'}});
        if (String(url).endsWith('/ExportHost')) return window.hostExportSuccess
            ? new Response(new Uint8Array([80,75,3,4]),{headers:{'Content-Type':'application/zip','Content-Disposition':'attachment; filename="Host.maui"'}})
            : new Response(JSON.stringify({message:'Export denied for fixture.'}),{status:403,headers:{'Content-Type':'application/json'}});
        if (String(url).endsWith('/PreflightDeleteHosts')) return new Response(JSON.stringify({success:false,message:'Host is used by a Task.',blockers:[]}));
        throw new Error('Unexpected Host action URL '+url);
    }
    if (String(url).includes('/HostEditor/Browse')) {
        hostBrowseCalls.push({url:String(url),headers:options.headers,body:JSON.parse(options.body)});
        if(window.hostBrowseFailure)return new Response(JSON.stringify({message:'Listing failed.'}),{status:400});
        return new Response(JSON.stringify({hostName:fixture.host.name,currentDirectory:'/',parentDirectory:'',items:[{name:'folder',path:'/folder',isDirectory:true},{name:'chosen.txt',path:'/chosen.txt',isDirectory:false,size:'1024',lastModified:'2026-10-19'}]}));
    }
    if (String(url).includes('/ObjectEditLease/')) {
        leaseCalls.push({url:String(url),body:JSON.parse(options.body)});
        return new Response(JSON.stringify(String(url).endsWith('/Override') ? {success:true,leaseId:'host-recovered',editVersion:'recovered-host-revision'} : String(url).endsWith('/Open') ? {required:true,leaseId:'host-layout-lease'} : {success:true}));
    }
    if (String(url).includes('/SavePreferences')) {
        fixture.preferences=JSON.parse(options.body);
        return new Response(JSON.stringify(fixture.preferences));
    }

    if (String(url).includes('/Certificates') || String(url).includes('/DocumentLibraries')) {
        const certificate = String(url).includes('/Certificates');
        lookupCalls.push({ url:String(url), method:options.method || 'GET', body:options.body ? JSON.parse(options.body) : null, headers:options.headers });
        const data = { items: certificate ? [
            { store:'My', subject:'Test Certificate', thumbprint:'AA BB', expires:'2030-01-01' },
            { store:'My', subject:'New Certificate', thumbprint:'CC DD', expires:'2031-01-01' }
        ] : [{ name:'Documents', guid:'11111111-2222-3333-4444-555555555555' }, { name:'Library Two', guid:'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee' }] };
        if (window.deferLookup) return await new Promise(resolve => { window.releaseLookup = () => resolve(new Response(JSON.stringify(data))); });
        return new Response(JSON.stringify(data));
    }
    if (options.method === 'POST') {
        window.hostSaveCount++; window.hostSaveHeaders=options.headers;
        if(window.hostSaveConflict)return new Response(JSON.stringify({message:'Host changed in MOVEit.',editConflict:true,moveItSaved:false}),{status:409});
        window.lastSaved = JSON.parse(options.body);
        if(typeof window.nextHostUndo==='boolean')fixture.canUndo=window.nextHostUndo;
        if(window.applyHostSaveToFixture)for(const [key,value]of Object.entries(lastSaved.configuration)){
            const field=fixture.host.configurationSections.flatMap(s=>s.fields).find(f=>f.key===key);
            if(field)field.value=typeof value==='boolean'?(value?'Yes':'No'):String(value);
        }
        if (window.deferSave) return await new Promise(resolve => { window.releaseSave = () => resolve(new Response(JSON.stringify({ success:true,hostId:window.fixture.host.id,undoSnapshotId:window.nextHostUndo===true?'host-session-snapshot':'' }))); });
        return new Response(JSON.stringify({ success:true,hostId:window.fixture.host.id,undoSnapshotId:window.nextHostUndo===true?'host-session-snapshot':'' }));
    }
    return new Response(JSON.stringify(window.fixture), { headers: { 'Content-Type': 'application/json' } });
};
window.undoHostCalls=[];window.cloneCalls=[];window.hostTestCalls=[];
const standardHostOptions = { onUndoHost:host=>undoHostCalls.push(host.id), onTestHost:(host,leaseId)=>hostTestCalls.push({id:host.id,leaseId,menuOpen:!document.querySelector('[data-host-editor-actions-menu]').hidden}), onCloneHost:async host=>cloneCalls.push({id:host.id,editorOpen:document.querySelector('[data-host-editor-dialog]').open}), shellUrl: '/maui/HostEditor/Shell', saveUrl: '/maui/HostEditor/Save', fingerprintUrl: '/maui/HostEditor/Fingerprint', certificatesUrl: '/maui/HostEditor/Certificates', documentLibrariesUrl: '/maui/HostEditor/DocumentLibraries', antiForgeryToken:'token' };
if(new URL(location.href).searchParams.has('nested')) {
    document.documentElement.dataset.moveitAdmin='true';
    window.MauiEditorRouting={useMauiHostEditor:true,registerHostOpen:handler=>{window.openNestedHost=handler;}};
    const initialize=MauiHostEditor.initialize;
    MauiHostEditor.initialize=options=>{window.editor=initialize(options);return editor;};
    window.nestedReturns=0;
    MauiTaskHostEditor.initialize({dataset:{hostEditorShellUrl:'/maui/HostEditor/Shell',hostEditorSaveUrl:'/maui/HostEditor/Save',testHostUrl:'/maui/Hosts/TestHost',cloneHostUrl:'/maui/Hosts/CloneHost',exportHostUrl:'/maui/Hosts/ExportHost',preflightDeleteHostsUrl:'/maui/Hosts/PreflightDeleteHosts',deleteHostsUrl:'/maui/Hosts/DeleteHosts'}},'token',{
        fetchJson:async(url,options)=>{const response=await fetch(url,options);const payload=await response.json();if(!response.ok){const error=new Error(payload.message);error.status=response.status;error.payload=payload;throw error;}return payload;}
    });
} else window.editor=MauiHostEditor.initialize(standardHostOptions);
window.loadFixture = async model => {
    await editor.close(); window.fixture = model; window.lastSaved = null;
    return await editor.open(model.host.id);
};
window.inspectLayout = () => {
    const failures = [];
    for (const grid of document.querySelectorAll('.maui-host-editor-form-grid')) {
        if (!grid.getBoundingClientRect().height) continue;
        const rows = new Map();
        const columns = getComputedStyle(grid).gridTemplateColumns.split(' ').length;
        if (innerWidth <= 780 && columns !== 1) failures.push('Narrow form did not collapse to one column');
        for (const field of grid.children) {
            if (field.matches('[data-host-editor-state-cache]')) {
                const container = field.getBoundingClientRect();
                for (const control of field.querySelectorAll('input,select,label,legend')) {
                    const box = control.getBoundingClientRect();
                    if (box.left < container.left - .5 || box.right > container.right + .5) failures.push('State-cache control escapes field');
                }
                const rows = [...field.querySelectorAll('.maui-host-state-cache-row')].map(row => row.getBoundingClientRect());
                if (rows.some((row, index) => index && row.top < rows[index - 1].bottom - .5)) failures.push('State-cache choices overlap');
                continue;
            }
            const caption = field.firstElementChild;
            const control = field.querySelector('.maui-host-editor-field-body > .maui-host-editor-control, .maui-host-editor-field-body > .maui-host-editor-value, .maui-host-editor-field-body > .maui-host-algorithm-list');
            if (!caption || !control) { failures.push('Missing caption/control body'); continue; }
            const label = caption.getBoundingClientRect(), box = control.getBoundingClientRect(), container = field.getBoundingClientRect();
            if (box.top < label.bottom - .5) failures.push('Caption overlaps control: ' + caption.textContent);
            if (box.left < container.left - .5 || box.right > container.right + .5) failures.push('Control escapes field: ' + caption.textContent);
            if (!control.matches('textarea,.multiline,.maui-host-algorithm-list') && Math.abs(box.height - 42) > .5) failures.push('Single-line control height: ' + caption.textContent + ' = ' + box.height);
            const row = Math.round(label.top);
            if (!rows.has(row)) rows.set(row, []);
            rows.get(row).push({ top: box.top, label: caption.textContent });
        }
        for (const controls of rows.values()) {
            if (controls.length === 2 && Math.abs(controls[0].top - controls[1].top) > .5)
                failures.push('Inputs are misaligned: ' + controls.map(item => item.label + ':' + item.top).join(', '));
        }
        if (grid.scrollWidth > grid.clientWidth + 1) failures.push('Horizontal form overflow');
    }
    return failures.concat(layoutErrors);
};
${newHostFieldScript}
${notificationAppearanceScript}
</script></body></html>`;
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-host-layout-'));
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
    for (let attempt = 0; attempt < 100 && !await evaluate('Boolean(window.editor)'); attempt++) await pause(50);
    assert.equal(await evaluate('Boolean(window.editor)'), true, 'Editor did not initialize');
    let checks = 2;
    // Production Test Host/Preflight footers: plain severity text, two lines, no scrolling/banner.
    await evaluate(`window.footerDiagnostics=[];window.footerOriginalLogger=window.mauiLogNotification;window.mauiLogNotification=(text,kind)=>footerDiagnostics.push({text,kind});`);
    for(const selector of ['[data-host-test-dialog]','[data-task-preflight-dialog]']){
        await evaluate(`window.diagnosticDialog=document.querySelector(${JSON.stringify(selector)});diagnosticDialog.showModal();`);
        for(const theme of ['light','dark'])for(const width of [1400,1024,800,600])for(const kind of ['error','warning','default']){
            await command('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false});
            const result=await evaluate(`(()=>{document.documentElement.dataset.theme=${JSON.stringify(theme)};MauiModalUI.clear(diagnosticDialog);const footer=diagnosticDialog.querySelector('.settings-footer'),height=footer.getBoundingClientRect().height;const detail='Native diagnostic. '+ 'Additional context. '.repeat(80);MauiModalUI.showMessage(detail,${JSON.stringify(kind)},diagnosticDialog,'modal','Checks need attention. See MAUI logs for details.');const message=footer.querySelector('[data-maui-modal-feedback]:not([hidden])'),css=getComputedStyle(message),probe=document.createElement('span');probe.style.color=${JSON.stringify(kind==='error'?'var(--danger)':kind==='warning'?(theme==='dark'?'var(--warning)':'#8a5a00'):'var(--text)')};document.body.append(probe);const expectedColor=getComputedStyle(probe).color;probe.remove();return {height,afterHeight:footer.getBoundingClientRect().height,color:css.color,expectedColor,background:css.backgroundColor,border:parseFloat(css.borderTopWidth),padding:parseFloat(css.paddingTop),overflow:css.overflowY,clamp:css.webkitLineClamp,lineHeight:parseFloat(css.lineHeight),messageHeight:message.getBoundingClientRect().height,text:message.textContent,hasBanner:message.classList.contains('inline-alert'),logged:footerDiagnostics.at(-1).text===detail,buttonsVisible:[...footer.querySelectorAll('button')].every(button=>{const r=button.getBoundingClientRect();return r.top>=footer.getBoundingClientRect().top&&r.bottom<=innerHeight&&r.right<=innerWidth;})};})()`);
            assert.equal(result.color,result.expectedColor,'Footer reuses the correct severity color');
            assert.equal(result.background,'rgba(0, 0, 0, 0)');assert.equal(result.border,0);assert.equal(result.padding,0);assert.equal(result.hasBanner,false);
            assert.equal(result.overflow,'hidden');assert.equal(result.clamp,'2');assert.ok(result.messageHeight<=result.lineHeight*2+1,'At most two visible lines');
            assert.equal(result.afterHeight,result.height,'Footer retains its button/control alignment');assert.equal(result.buttonsVisible,true);
            assert.equal(result.logged,true,'Detailed message is retained in logs');assert.equal(result.text,'Checks need attention. See MAUI logs for details.');checks+=12;
            if(width===1400&&kind==='error')await screenshot((selector.includes('host-test')?'host-test-footer-':'preflight-footer-')+theme);
        }
        await evaluate(`MauiModalUI.clear(diagnosticDialog);diagnosticDialog.close();`);await pause(20);
    }
    await evaluate(`window.mauiLogNotification=window.footerOriginalLogger;`);

    // Both imports use this production table class and the shared five-row viewport helper.
    await evaluate(`window.importProbe=document.createElement('div');importProbe.className='task-spreadsheet-grid-frame';importProbe.style.width='420px';importProbe.innerHTML='<table class="task-spreadsheet-grid"><thead><tr><th>Status</th><th style="min-width:900px">Host / Task</th></tr></thead><tbody>'+Array.from({length:8},(_,i)=>'<tr><td><span class="task-spreadsheet-row-pill valid">Valid</span></td><td>Object '+i+'</td></tr>').join('')+'</tbody></table>';document.body.append(importProbe);MauiModalUI.sizeTableViewport(importProbe,5)`);
    await pause(80);
    const importSize=await evaluate(`(()=>{const frame=importProbe,style=getComputedStyle(frame),border=parseFloat(style.borderTopWidth)+parseFloat(style.borderBottomWidth),scrollbar=frame.offsetHeight-frame.clientHeight-border;return {available:frame.clientHeight,scrollbar,expected:frame.querySelector('thead').getBoundingClientRect().height+5*frame.querySelector('tbody tr').getBoundingClientRect().height,scrollable:frame.scrollHeight>frame.clientHeight};})()`);
    assert.ok(Math.abs(importSize.available-importSize.expected)<=1,'Exactly five complete rows plus header, excluding native horizontal scrollbar');
    assert.equal(importSize.scrollable,true,'Remaining data stays accessible by scrolling');checks+=2;
    await evaluate(`importProbe.remove()`);

    // Folder navigation must retain the frame through loading, empty, large and failed listings.
    for (const [width, height] of [[1400, 1000], [600, 560]]) {
        await command('Emulation.setDeviceMetricsOverride', {width, height, deviceScaleFactor:1, mobile:false});
        for (const theme of ['light', 'dark']) {
            await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)};window.browserFrameHandle=MauiHostBrowser.open({hostName:'Frame fixture',load:()=>new Promise((resolve,reject)=>{window.frameResolve=resolve;window.frameReject=reject})});window.readBrowserFrame=()=>{const d=document.querySelector('[data-host-browser]'),r=d.getBoundingClientRect(),f=d.querySelector('.settings-footer').getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,footerVisible:f.bottom<=innerHeight&&f.top>=r.top}}`);
            const frame = await evaluate('readBrowserFrame()');
            assert.ok(frame.height <= height - 48 && frame.height > 400);
            assert.equal(frame.footerVisible, true);
            for (const state of ['empty', 'single', 'large', 'error']) {
                if (state !== 'empty') await evaluate(`document.querySelector('[data-host-browser-refresh]').click()`);
                assert.deepEqual(await evaluate('readBrowserFrame()'), frame, `${theme}/${width}: loading frame`);
                await evaluate(state === 'error'
                    ? `frameReject(new Error('Unable to list this folder.'))`
                    : `frameResolve({currentPath:'/folder',hasParent:${state !== 'empty'},parentPath:'/',items:Array.from({length:${state === 'large' ? 100 : state === 'single' ? 1 : 0}},(_,i)=>({name:'file-'+i+'.txt',path:'/folder/file-'+i+'.txt',isFolder:false}))})`);
                for(let i=0;i<100&&await evaluate(`!document.querySelector('[data-host-browser-loading]').hidden`);i++)await pause(20);
                assert.deepEqual(await evaluate('readBrowserFrame()'), frame, `${theme}/${width}: ${state} frame`);
                if (state === 'large') assert.equal(await evaluate(`(()=>{const r=document.querySelector('[data-host-browser-rows]'),f=document.querySelector('[data-host-browser] .settings-footer');return r.scrollHeight>r.clientHeight&&r.getBoundingClientRect().bottom<=f.getBoundingClientRect().top})()`), true, 'Large listings scroll inside the frame');
                checks += 2;
            }
            await evaluate('browserFrameHandle.close()');
            await pause(20);
            checks += 3;
        }
    }
    // Quick Transfer selection, cloud roots and shared dialog state are isolated from caller fields.
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1000,deviceScaleFactor:1,mobile:false});
    for(const theme of ['light','dark']) {
        await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)};openQuickBrowse('source')`);
        for(let i=0;i<100&&await evaluate(`document.querySelectorAll('[data-host-browser-rows] > *').length<2`);i++)await pause(20);
        await evaluate(`document.querySelector('[data-host-browser-rows] .file').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))`);
        assert.equal(await evaluate('quickValues().sourceFile'),'chosen.txt');
        assert.equal(await evaluate('quickValues().sourcePath'),'/incoming');
        await evaluate(`openQuickBrowse('destination')`);
        for(let i=0;i<100&&await evaluate(`document.querySelectorAll('[data-host-browser-rows] > *').length<2`);i++)await pause(20);
        await evaluate(`document.querySelector('[data-host-browser-rows] .folder').click();document.querySelector('[data-host-browser-ok]').click()`);
        assert.equal(await evaluate('quickValues().destinationPath'),'/child');
        assert.equal(await evaluate('quickValues().destinationFile'),'keep.txt');checks+=4;
    }
    await evaluate(`window.quickCloud=true;openQuickBrowse('source')`);
    for(let i=0;i<100&&await evaluate(`document.querySelectorAll('[data-host-browser-rows] > *').length<1`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-host-browser-ok]').disabled`),true);
    await evaluate(`document.querySelector('[data-host-browser-rows] .folder').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))`);
    for(let i=0;i<100&&await evaluate(`document.querySelectorAll('[data-host-browser-rows] > *').length<2`);i++)await pause(20);
    assert.equal(await evaluate(`quickRequests.at(-1).rootName`),'bucket');
    await evaluate(`document.querySelector('[data-host-browser-ok]').click();window.quickCloud=false`);
    assert.equal(await evaluate('quickValues().sourceRoot.name'),'bucket');checks+=3;
    // Cancelling a pending listing cannot populate or close a later invocation.
    await evaluate(`window.staleBrowser=MauiHostBrowser.open({title:'Pending',load:()=>new Promise(resolve=>window.resolveOldListing=resolve)});staleBrowser.close();MauiHostBrowser.open({title:'Current',load:async()=>({currentPath:'/current',items:[]})});resolveOldListing({currentPath:'/stale',items:[{name:'stale'}]})`);
    await pause(30);
    assert.equal(await evaluate(`document.querySelector('[data-host-browser]').open`),true);
    assert.equal(await evaluate(`document.querySelector('[data-host-browser-location]').value`),'/current');
    assert.equal(await evaluate(`document.querySelector('[data-host-browser-rows]').textContent`),'');
    await evaluate(`document.querySelector('[data-host-browser-cancel]').click()`);checks+=3;
    // Host inspection returns to the retained editor; selection cannot dirty or save it.
    await evaluate(`document.documentElement.dataset.moveitAdmin='true';document.documentElement.dataset.hostBrowseUrl='/maui/HostEditor/Browse';loadFixture({...${JSON.stringify(fixtures['FTP-basic'])},checkoutObjectsForDurationOfEdit:true})`);
    await evaluate(`document.querySelector('[data-host-editor-test]').click()`);
    assert.equal(await evaluate('hostTestCalls.at(-1).leaseId'),'host-layout-lease','Test Host receives the retained editor checkout');checks++;
    await evaluate(`window.beforeBrowse=hostSaveCount;document.querySelector('[data-host-editor-browse]').click()`);
    for(let i=0;i<100&&await evaluate(`document.querySelectorAll('[data-host-browser-rows] > *').length<2`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-host-browser-hint]').hidden`),true);
    assert.equal(await evaluate(`document.querySelector('[data-host-browser-ok]').hidden`),true);
    await evaluate(`document.querySelector('[data-host-browser-rows] .file').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))`);
    assert.equal(await evaluate(`document.querySelector('[data-host-browser]').open`),true);
    assert.equal(await evaluate(`hostBrowseCalls.at(-1).headers['X-MAUI-Edit-Lease']`),'host-layout-lease');
    await screenshot('Host-Browse-shared');
    await evaluate(`document.querySelector('[data-host-browser-cancel]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dialog]').open`),true);
    assert.equal(await evaluate('hostSaveCount'),await evaluate('beforeBrowse'));checks+=6;
    await evaluate(`window.hostBrowseFailure=true;document.querySelector('[data-host-editor-browse]').click()`);
    for(let i=0;i<100&&await evaluate(`document.querySelector('[data-host-browser-error]').hidden`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-host-browser-error]').textContent`),'Listing failed.');
    await evaluate(`document.querySelector('[data-host-browser-cancel]').click();window.hostBrowseFailure=false;editor.close()`);checks++;
    await evaluate(`delete document.documentElement.dataset.moveitAdmin;delete document.documentElement.dataset.hostBrowseUrl`);
    // Shared production icon controls: exercise both directions, disabled styling and modal close geometry.
    await evaluate(`const gallery=document.createElement('div');gallery.id='symbol-controls';gallery.style.cssText='display:flex;gap:16px;padding:32px;background:var(--surface)';gallery.innerHTML='<button class="secondary-button calendar-icon-button" data-calendar-previous aria-label="Previous period">‹</button><button class="secondary-button calendar-icon-button" data-calendar-next aria-label="Next period">›</button><button class="pager-button" data-page-prev aria-label="Previous page">‹</button><button class="pager-button" data-page-next aria-label="Next page" disabled>›</button><button class="dialog-close" aria-label="Close">×</button><button class="search-clear visible" style="position:static" aria-label="Clear search">×</button><button class="metadata-tag-remove" aria-label="Remove tag">×</button><div class="configuration-default-column"><button data-column-remove aria-label="Remove column">×</button></div>';document.body.append(gallery);`);
    for(const theme of ['light','dark']) {
        await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
        assert.equal(await evaluate(`(() => [...document.querySelectorAll('#symbol-controls button')].every(b=>{const c=getComputedStyle(b),p=getComputedStyle(b,'::after');return ['inline-flex','flex'].includes(c.display)&&c.alignItems==='center'&&c.justifyContent==='center'&&['12px','14px','18px'].includes(p.width)&&p.height===p.width&&p.maskImage!=='none';}))()`),true);
        await screenshot('Shared-symbol-buttons-'+theme);
    }
    await evaluate(`document.getElementById('symbol-controls').remove()`);

    await evaluate(`const grid=document.createElement('div');grid.id='disclosure-grid';grid.innerHTML='<div class="menu-control" data-menu>'+${JSON.stringify(gridActionsButton)}+'<div class="dropdown-menu" role="menu"><button type="button">Example</button></div></div>';document.body.append(grid);window.gridMenu=MauiGridActions.createManager({grid});gridMenu.wire();grid.querySelector('button').click();`);
    assert.equal(await evaluate(`document.querySelector('#disclosure-grid [data-menu-button]').getAttribute('aria-expanded')`),'true');
    assert.equal(await evaluate(`new DOMMatrixReadOnly(getComputedStyle(document.querySelector('#disclosure-grid .button-caret')).transform).a`),-1,'Production grid Actions uses the same expanded arrow');
    await evaluate(`document.body.click()`);
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('#disclosure-grid .button-caret')).transform`),'none');
    await evaluate(`document.querySelector('#disclosure-grid').remove()`);checks+=3;

    for(const width of [1400,600]) {
        await command('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false});
        for(const theme of ['light','dark'])for(const mode of ['html','source','text']) {
            await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)};showNotificationAppearance(${JSON.stringify(mode)})`);
            assert.equal(await evaluate(`(()=>{const d=document.querySelector('[data-task-notification-dialog]'),t=d.querySelector('[data-notification-html-toolbar]').getBoundingClientRect(),e=[...d.querySelectorAll('.task-notification-editor,textarea')].find(n=>!n.hidden).getBoundingClientRect();return t.height>0&&e.height>0&&Math.abs(t.bottom-e.top)<1&&Math.abs(t.left-e.left)<1&&Math.abs(t.right-e.right)<1;})()`),true,'Task Notifications '+mode+' toolbar and body join at '+width+'/'+theme);
            assert.equal(await evaluate(`document.querySelector('[data-notification-subject-macro]').options[0].textContent`),'Macros…');
            assert.equal(await evaluate(`document.querySelector('[data-notification-body-macro]').options[0].textContent`),'Macros…');
            if(theme==='light')await screenshot('Notification-message-'+width+'-'+mode);
            await evaluate(`document.querySelector('[data-task-notification-dialog]').close()`);checks+=3;
        }
    }
    for (const width of [1400, 1024, 800, 600]) {
        await command('Emulation.setDeviceMetricsOverride', { width, height: 1000, deviceScaleFactor: 1, mobile: false });
        for (const theme of ['light', 'dark']) {
            await evaluate(`document.documentElement.dataset.theme = ${JSON.stringify(theme)}`);
            for (const [name, model] of Object.entries(fixtures)) {
                assert.equal(await evaluate(`loadFixture(${JSON.stringify(model)})`), true, name + ' failed to load');
                assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-host-editor-tab]')].map(t=>t.textContent.trim())`), ['Host','Properties','Tasks']);
                assert.deepEqual(await evaluate(`(()=>{const n=document.querySelector('[data-host-editor-dirty-state]');return {message:n.textContent,visible:!n.hidden,role:n.getAttribute('role'),font:getComputedStyle(n).fontSize};})()`),{message:model.readOnly || model.host.canEditConfiguration === false ? 'Read-only: your MOVEit account does not permit editing this Host.' : 'No unsaved changes.',visible:true,role:'status',font:'12px'},'Host footer reports clean or read-only state');checks++;
                assert.equal(await evaluate(`document.querySelector('[data-host-editor-tab=configuration]').getAttribute('aria-selected')`), 'true');
                await evaluate(`document.querySelector('[data-host-editor-tab=information]').click()`);
                assert.equal(await evaluate(`[...document.querySelector('[data-host-editor-information-fields]').querySelectorAll('.maui-host-editor-field > span, .maui-host-editor-field > label')].some(n=>['Destination','Secure'].includes(n.textContent.trim()))`), false);
                await check(`${name}-${width}-${theme}-information`); checks++;
                if (name === 'FTP-basic' || model.host.typeCode === 'S3') {
                    await evaluate(`document.querySelector('[data-host-editor-tab=tasks]').click()`);
                    const feedback=await evaluate(`(() => { const d=document.querySelector('[data-host-editor-dialog]'),shell=d.querySelector('.maui-host-editor-shell'),status=d.querySelector('[data-host-editor-dirty-state]');const geometry=()=>[...shell.children].map(n=>n.getBoundingClientRect().toJSON());const before=geometry();MauiModalUI.showMessage('Exported 1 Host.','default',d);return {before,after:geometry(),message:status.textContent,banners:d.querySelectorAll('[data-maui-modal-feedback]').length,children:shell.children.length};})()`);
                    assert.deepEqual(feedback.after,feedback.before,name+' export notice preserves header/tabs/workspace/footer geometry');
                    assert.equal(feedback.message,'Exported 1 Host.');assert.equal(feedback.banners,0);assert.equal(feedback.children,4);checks+=4;
                    if (name === 'FTP-basic') {
                        const pageFeedback=await evaluate(`(()=>{const d=document.querySelector('[data-host-editor-dialog]'),footer=d.querySelector('.settings-footer'),actions=d.querySelector('.maui-host-editor-footer-actions'),height=footer.getBoundingClientRect().height;MauiModalUI.showMessage('Grid refresh failed. '+ 'Additional context. '.repeat(40),'error',d,'page');const notice=footer.querySelector('[data-feedback-origin="page"]');return {height,afterHeight:footer.getBoundingClientRect().height,noticeHeight:notice.getBoundingClientRect().height,noticeParent:notice.parentElement===footer,status:d.querySelector('[data-host-editor-dirty-state]').textContent,actionsVisible:actions.getBoundingClientRect().bottom<=innerHeight};})()`);
                        assert.equal(pageFeedback.afterHeight,pageFeedback.height,'Page notice preserves fixed editor footer height');
                        assert.ok(pageFeedback.noticeHeight<=38);assert.equal(pageFeedback.noticeParent,true);
                        assert.equal(pageFeedback.status,'Exported 1 Host.');assert.equal(pageFeedback.actionsVisible,true);checks+=5;
                        await evaluate(`document.querySelectorAll('[data-host-editor-dialog] [data-feedback-origin="page"]').forEach(node=>node.remove())`);
                    }

                    await evaluate(`MauiModalUI.clear(document.querySelector('[data-host-editor-dialog]'));document.querySelector('[data-host-editor-tab=information]').click()`);
                }
                if (name === 'FTP-basic') {
                    await evaluate(`(()=>{const n=document.querySelector('[data-host-editor-name]');window.originalFooterTestName=n.value;n.value+=' FOOTER';n.dispatchEvent(new Event('input',{bubbles:true}));})()`);
                    assert.deepEqual(await evaluate(`(()=>{const n=document.querySelector('[data-host-editor-dirty-state]'),d=document.querySelector('[data-host-editor-dialog]');return {message:n.textContent,dirty:d.classList.contains('has-unsaved-changes'),color:getComputedStyle(n).color};})()`),{message:'Unsaved changes.',dirty:true,color:await evaluate(`(()=>{const n=document.createElement('span');n.style.color='var(--warning)';document.body.append(n);const color=getComputedStyle(n).color;n.remove();return color;})()`)});
                    await evaluate(`(()=>{const n=document.querySelector('[data-host-editor-name]');n.value=originalFooterTestName;n.dispatchEvent(new Event('input',{bubbles:true}));})()`);
                    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dirty-state]').textContent`),'No unsaved changes.');checks+=2;
                    await evaluate(`MauiModalUI.showError('Unable to save the Host. Check the supplied values and try again.');`);
                    let footerVisible=false, footerGeometry;
                    for(let attempt=0;attempt<10&&!footerVisible;attempt++) {
                        footerGeometry=await evaluate(`(() => { const e=document.querySelector('[data-host-editor-error]'), f=e.closest('.maui-host-editor-footer'), r=e.getBoundingClientRect(), b=f.getBoundingClientRect(); return {hidden:e.hidden,error:r.toJSON(),footer:b.toJSON(),visible:!e.hidden&&r.width>0&&r.height>0&&r.left>=b.left&&r.right<=b.right&&r.top>=b.top&&r.bottom<=b.bottom}; })()`);
                        footerVisible=footerGeometry.visible;
                        if(!footerVisible)await pause(20);
                    }
                    if(!footerVisible)await screenshot(`FAILED-footer-${width}-${theme}`);
                    assert.equal(footerVisible,true, `Footer error remains inside the Host modal at ${width}/${theme}: ${JSON.stringify(footerGeometry)}`);
                    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dirty-state]').hidden`),true,'Error replaces normal Host footer status');checks++;
                    await evaluate(`MauiModalUI.clear(document.querySelector('[data-host-editor-dialog]'))`);
                    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dirty-state]').textContent`),'No unsaved changes.');checks++;
                }
                await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click()`);
                assert.equal(await evaluate(`document.querySelector('[data-host-editor-actions-menu] [data-host-editor-test]')?.textContent`),'Test Host');
                assert.equal(await evaluate(`document.querySelector('[data-host-editor-cancel]').textContent`),'Close');
                assert.equal(await evaluate(`[...document.querySelectorAll('[data-host-editor-config-tab]')].every(b=>b.querySelector('.icon svg circle')&&getComputedStyle(b.querySelector('.icon')).width==='18px')`),true,'Every Host navigation tab uses the shared Settings icon');
                assert.equal(await evaluate(`document.querySelector('.maui-host-editor-footer-left [data-host-editor-test]')===null`), true);
                if(name==='FTP-basic') {
                    const calls=await evaluate('hostTestCalls.length');
                    await evaluate(`document.querySelector('[data-host-editor-actions]').click()`);
                    assert.equal(await evaluate(`document.querySelector('[data-host-editor-actions]').getAttribute('aria-expanded')`),'true');
                    assert.equal(await evaluate(`new DOMMatrixReadOnly(getComputedStyle(document.querySelector('[data-host-editor-actions] .button-caret')).transform).a`),-1,'Host Actions arrow points up');
                    await evaluate(`document.querySelector('[data-host-editor-test]').click()`);
                    assert.equal(await evaluate('hostTestCalls.length'),calls+1);
                    assert.equal(await evaluate('hostTestCalls.at(-1).menuOpen'),false,'Test Host closes its Actions menu');
                    assert.equal(await evaluate(`document.querySelector('[data-host-editor-actions]').getAttribute('aria-expanded')`),'false');
                    assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-host-editor-actions] .button-caret')).transform`),'none');
                    await evaluate(`document.querySelector('[data-host-editor-actions]').click();document.querySelector('.maui-host-editor-header').click()`);
                    assert.equal(await evaluate(`document.querySelector('[data-host-editor-actions]').getAttribute('aria-expanded')`),'false','Outside click resets the Host menu and arrow');
                    checks+=7;
                }

                if(name==='FTP-basic') {
                    await evaluate(`document.querySelector('[data-host-editor-tab="information"]').click()`);
                    assert.equal(await evaluate(`inspectPropertyHeading(document.querySelector('[data-host-editor-panel="information"] .maui-object-properties-heading'))`),true,'Host Properties identity and source remain aligned at every width/theme');
                    assert.equal(await evaluate(`[...document.querySelectorAll('[data-host-editor-panel="information"] h3,[data-host-editor-panel="information"] h4')].every(n=>getComputedStyle(n).fontSize==='15px')`),true,'Host property and audit titles share 15px typography');
                    assert.equal(await evaluate(`[...document.querySelectorAll('[data-host-editor-panel="information"] .source-badge')].every(n=>getComputedStyle(n).fontSize==='10px'&&getComputedStyle(n).lineHeight==='12px'&&getComputedStyle(n).padding==='3px 8px'&&getComputedStyle(n).fontWeight==='700')`),true,'Host MOVEit/MAUI pills share font, line height and padding');
                    assert.equal(await evaluate(`[...document.querySelectorAll('[data-host-editor-information-fields] .maui-host-editor-field > span')].some(n=>['Host Type','Host ID'].includes(n.textContent))`),false,'Host identity appears only in its header');
                    if([1400,600].includes(width))await screenshot(`Host-properties-${width}-${theme}`);
                    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click()`); checks++;
                }
                const rail = await evaluate(`(() => { const n=document.querySelector('.maui-host-editor-rail').getBoundingClientRect(), p=document.querySelector('.maui-host-editor-properties').getBoundingClientRect(); return {width:n.width,right:n.right,top:n.top,bottom:n.bottom,properties:p.toJSON()}; })()`);
                if(width>700) { assert.equal(rail.width,width>940?285:240); assert.ok(rail.properties.left>=rail.right); }
                else assert.ok(rail.properties.top>=rail.bottom, 'Compact navigation sits above properties');
                checks+=3;
                if (name === 'FTP-basic' && width === 1400) {
                    const size = await evaluate(`(()=>{const d=document.querySelector('[data-host-editor-dialog]'),r=document.querySelector('.maui-host-editor-rail');return {height:d.getBoundingClientRect().height,railHeight:r.clientHeight,railContent:r.scrollHeight};})()`);
                    assert.equal(size.height,840,'Host modal gains 20px at desktop height');
                    assert.ok(size.railContent<=size.railHeight+1,'FTP left navigation fits at desktop height');checks+=2;
                }
                for (const section of model.host.configurationSections) {
                    await evaluate(`document.querySelector('[data-host-editor-config-tab="${section.key}"]').click()`);
                    await check(`${name}-${width}-${theme}-${section.key}`); checks++;
                    assert.equal(await evaluate(`document.querySelector('.maui-host-editor-config-card .source-badge')===null`),true,'Host configuration pages have no MOVEit pill');
                    if(name==='FTP-basic'&&section.key==='general'&&[1400,600].includes(width))await screenshot(`Host-general-${width}-${theme}`);
                    if (model.host.typeCode === 'S3' && section.key === 'general') {
                        const endpoint=await evaluate(`(()=>{const f=[...document.querySelectorAll('[data-host-editor-config-fields] .maui-host-editor-field')].find(n=>n.firstElementChild.textContent==='Alternative Provider URL'),n=f?.querySelector('input,.maui-host-editor-value');return n?{value:n.value??n.textContent,readonly:n.tagName!=='INPUT'||n.readOnly||n.disabled,type:n.type||'text'}:null;})()`);
                        const alternative=name.startsWith('S3-s3-alternative');
                        assert.equal(Boolean(endpoint),alternative,'Alternative provider URL follows the saved mode');
                        if(alternative){assert.equal(endpoint.value,'www.someurl.com');assert.equal(endpoint.type,'text');assert.equal(endpoint.readonly,!model.host.canEditConfiguration);}
                        if(name==='S3-s3-alternative'&&width===1400&&theme==='light')await screenshot('S3-alternative-provider');checks++;
                    }
                    assert.equal(await evaluate(`document.querySelector('[data-host-editor-config-tab="${section.key}"]').tabIndex`),0);
                    assert.equal(await evaluate(`document.querySelector('[role=tabpanel]').getAttribute('aria-labelledby')`), await evaluate(`document.querySelector('[data-host-editor-config-tab="${section.key}"]').id`));
                    const values = Object.fromEntries(model.host.configurationSections.flatMap(section => section.fields.map(field => [field.key, field.value])));
                    const active = field => !field.conditionKey || (field.conditionValue === 'active' ? Boolean(values[field.conditionKey]) && values[field.conditionKey].toLowerCase() !== 'none'
                        : ['true','false'].includes(field.conditionValue) ? /^(yes|true|1|on)$/i.test(values[field.conditionKey] || '') === (field.conditionValue === 'true') : String(values[field.conditionKey]) === field.conditionValue);
                    const flags = section.fields.filter(field => field.key !== 'UseDefStateCaching' && field.valueType.toLowerCase() === 'boolean' && active(field));
                    for (const flag of flags) {
                        const rendered = await evaluate(`(() => { const c = document.getElementById(${JSON.stringify('maui-host-editor-control-' + flag.key)}); return c ? { tag: c.tagName, disabled: c.disabled, value: c.value, options: [...c.options].map(o => o.text) } : null; })()`);
                        assert.ok(rendered, `${name}: Boolean ${flag.key} was not rendered`);
                        assert.equal(rendered.tag, 'SELECT', `${name}: Boolean ${flag.key} is not a dropdown`);
                        assert.equal(rendered.disabled, !flag.editable, `${name}: permission changed for ${flag.key}`);
                        assert.deepEqual(rendered.options.slice(0, 2), ['No', 'Yes']);
                        if (flag.value) assert.equal(rendered.value, /^(yes|true|1|on)$/i.test(flag.value) ? 'true' : 'false');
                    }
                    if (section.key === 'advanced' && section.fields.some(field => field.key === 'UseDefStateCaching')) {
                        const cache = section.fields.find(field => field.key === 'UseDefStateCaching');
                        const state = await evaluate(`(() => { const c = document.getElementById('maui-host-editor-control-UseDefStateCaching'); return { type:c.type, checked:c.checked, disabled:c.disabled, modes:[...document.querySelectorAll('[data-host-state-cache-mode]')].map(r => ({ value:r.value, checked:r.checked, disabled:r.matches(':disabled') })) }; })()`);
                        assert.equal(state.type, 'checkbox');
                        assert.equal(state.disabled, !cache.editable);
                        assert.equal(state.checked, cache.value === 'Yes');
                        assert.deepEqual(state.modes.map(r => r.value), ['always','task','for']);
                        assert.ok(state.modes.every(r => r.disabled === (!cache.editable || cache.value === 'Yes')));
                        if (name.endsWith('-cache-task')) assert.equal(state.modes[1].checked, true);
                        if (name.endsWith('-cache-timed')) assert.equal(state.modes[2].checked, true);
                        if (name.endsWith('-cache-always')) assert.equal(state.modes[0].checked, true);
                    }
                    if (name.startsWith('GoogleStorage-')) assert.ok(model.host.configurationSections.every(s => s.key !== 'security' && s.fields.every(f => f.key !== 'Secure')));
                    if (section.key === 'authentication') {
                        if (name.endsWith('-oauth')) assert.equal(await evaluate(`Boolean(document.getElementById('maui-host-editor-control-OAuthClientID'))`), true);
                        if (width === 1400 && theme === 'light' && name.endsWith('-basic')) await screenshot(name + '-authentication');
                        if (name === 'GoogleStorage-oauth' && width === 600) await screenshot(name + '-' + theme + '-narrow');
                    }
                    if (name === 'SSHFTP-basic' && section.key === 'ssh-algorithms' && width === 1400 && theme === 'light') await screenshot('SFTP-algorithms');
                    if (name === 'SharePoint-client-secret' && section.key === 'authentication' && width === 1400 && theme === 'light') await screenshot(name);
                    if (name === 'FTP-basic' && section.key === 'security' && width === 1400 && theme === 'light') await screenshot('FTPS-security');
                    if (name === 'FTP-ftp-custom' && section.key === 'directory-parsing' && [1400,600].includes(width) && theme === 'light') await screenshot('FTP-custom-parsing-'+width);
                    if (name === 'GoogleStorage-basic' && section.key === 'security' && width === 1400 && theme === 'light') await screenshot(name + '-security');
                }
                // Presentation-only changes cannot create a save patch.
                assert.equal(await evaluate('editor.save()'), false, `${name}: unchanged model attempted a save`);
            }
        }
    }
    await evaluate(`loadFixture(${JSON.stringify(fixtures['SSHFTP-basic'])})`);
    await evaluate(`document.querySelector('[data-host-editor-config-tab="ssh-algorithms"]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-undo]').disabled`),true,'Viewing algorithm lists is not an edit');checks++;
    assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-CmpL')`),null,'Compression appears only in Transfers');checks++;
    await evaluate(`window.applyHostSaveToFixture=true;document.querySelector('[data-host-editor-config-tab="transfers"]').click()`);
    assert.equal(await evaluate(`document.querySelector('label[for="maui-host-editor-control-CmpL"]').textContent`),'Disable Compression');
    assert.deepEqual(await evaluate(`[...document.getElementById('maui-host-editor-control-CmpL').options].map(option=>[option.value,option.textContent])`),[['0','Yes'],['6','No']]);
    assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-CmpL').value`),'0');checks+=3;
    for (const value of ['6','0']) {
        await evaluate(`const compression=document.getElementById('maui-host-editor-control-CmpL');compression.value='${value}';compression.dispatchEvent(new Event('change',{bubbles:true}));`);
        assert.equal(await evaluate('editor.save()'),true);
        assert.deepEqual(await evaluate('lastSaved.configuration'),{CmpL:value});
        await evaluate(`document.querySelector('[data-host-editor-config-tab="transfers"]').click()`);
        assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-CmpL').value`),value);checks+=3;
        if(value==='6')await screenshot('SSHFTP-disable-compression-No');
    }
    await evaluate(`window.applyHostSaveToFixture=false`);
    const undoHostFixture=structuredClone(fixtures['FTP-basic']);undoHostFixture.canUndo=true;undoHostFixture.host.resourceGroups='Operations, Production';
    await evaluate(`loadFixture(${JSON.stringify(undoHostFixture)})`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-undo]').disabled`),true,'Pre-open stored history cannot enable Editor Undo');checks++;
    assert.equal(await evaluate(`[...document.querySelectorAll('[data-host-editor-information-fields] label')].find(n=>n.querySelector('span')?.textContent==='Resource Groups').querySelector('input').value`),'Operations, Production');checks++;
    await evaluate(`window.nextHostUndo=true;document.querySelector('[data-host-editor-tab="information"]').click();const name=document.querySelector('[data-host-editor-name]');name.value+=' Changed';name.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-undo]').disabled`),false,'Unsaved edits are undoable immediately');checks++;
    assert.equal(await evaluate('editor.save()'),true);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-undo]').disabled`),false,'The local edit remains undoable after Save');checks++;
    await evaluate(`document.querySelector('[data-host-editor-undo]').click()`);await new Promise(resolve=>setTimeout(resolve,30));
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dialog]').open`),true,'Undo stays in the open editor');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-name]').value`),undoHostFixture.host.name);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-undo]').disabled`),true,'Undo is consumed');checks++;
    assert.deepEqual(await evaluate('undoHostCalls'),[],'No grid restore callback');checks++;
    await evaluate(`window.nextHostUndo=undefined;window.fixture.canUndo=false;`);
    await evaluate(`loadFixture(${JSON.stringify(undoHostFixture)})`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-undo]').disabled`),true,'Reopening discards the previous editor Undo scope');checks++;
    const metadataUndoFixture=structuredClone(undoHostFixture);metadataUndoFixture.canUndo=true;metadataUndoFixture.fields=[{name:'Owner',label:'Owner',type:'String'}];metadataUndoFixture.metadata={Owner:'Original'};
    await evaluate(`loadFixture(${JSON.stringify(metadataUndoFixture)})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="information"]').click()`);
    await evaluate(`const field=document.querySelector('[data-host-editor-metadata] input');field.value='Updated';field.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate('editor.save()'),true);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-undo]').disabled`),false,'Metadata changes support local Undo');checks++;
    await evaluate(`fixture.canUndo=false;const field=document.querySelector('[data-host-editor-metadata] input');field.value='Updated again';field.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate('editor.save()'),true);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-undo]').disabled`),false,'Last metadata change stays undoable');checks++;
    const partialReadonlyHost=structuredClone(undoHostFixture);
    partialReadonlyHost.host.canEditConfiguration=false;
    partialReadonlyHost.host.readOnlyReason='Protocol configuration is read-only. MAUI properties can be edited here.';
    await evaluate(`loadFixture(${JSON.stringify(partialReadonlyHost)})`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dirty-state]').textContent`),'Read-only: your MOVEit account does not permit editing this Host.');checks++;
    await evaluate(`const field=document.querySelector('[data-host-editor-name]');field.value+=' changed';field.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dialog]').classList.contains('has-unsaved-changes')`),false,'Read-only configuration cannot make Information or metadata dirty');
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-name]').readOnly`),true);checks++;
    const readonlyHost=structuredClone(undoHostFixture);readonlyHost.readOnly=true;
    await evaluate(`loadFixture(${JSON.stringify(readonlyHost)})`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-name]').readOnly`),true);checks++;
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('.maui-host-editor-tab-actions')).display`),'none');checks++;
    assert.equal(await evaluate('editor.save()'),false);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dirty-state]').textContent`),'Read-only: your MOVEit account does not permit editing this Host.');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-readonly-banner]')===null`),true);checks++;
    await evaluate(`window.readOnlyConfirms=0;window.savedConfirm=mauiConfirm;window.mauiConfirm=async()=>{readOnlyConfirms++;return false;}`);
    assert.equal(await evaluate('editor.close()'),true);assert.equal(await evaluate('readOnlyConfirms'),0);checks+=2;
    await evaluate(`window.mauiConfirm=savedConfirm`);
    async function loadCacheFixture(model) {
        assert.equal(await evaluate(`loadFixture(${JSON.stringify(model)})`), true);
        await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click(); document.querySelector('[data-host-editor-config-tab="advanced"]').click();`);
    }
    // All writable Host profiles use the same state-cache UI and complete sparse save group.
    for (const type of ['POP3','SSHFTP','S3','AzureBlob','GoogleStorage','FTP','Share','siLock','SharePoint']) {
        await loadCacheFixture(fixtures[type + '-basic']);
        await evaluate(`document.getElementById('maui-host-editor-control-UseDefStateCaching').click(); document.querySelector('[data-host-state-cache-mode="task"]').click();`);
        assert.equal(await evaluate('editor.save()'), true);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dirty-state]').textContent`),'Saved.');checks++;
        assert.deepEqual(await evaluate('lastSaved.configuration'), { UseDefStateCaching:false, StateCacheTime:'0' }, type + ' Until end of task run');
        await loadCacheFixture(fixtures[type + '-cache-timed']);
        await evaluate(`document.querySelector('[data-host-state-cache-mode="always"]').click();`);
        assert.equal(await evaluate('editor.save()'), true);
        assert.deepEqual(await evaluate('lastSaved.configuration'), { UseDefStateCaching:false, StateCacheTime:'-1' }, type + ' Always');
        await loadCacheFixture(fixtures[type + '-basic']);
        await evaluate(`document.getElementById('maui-host-editor-control-UseDefStateCaching').click(); document.querySelector('[data-host-state-cache-mode="for"]').click(); const t = document.getElementById('maui-host-editor-control-StateCacheTime'); t.value = '5'; t.dispatchEvent(new Event('input',{bubbles:true})); const u = document.getElementById('maui-host-editor-control-StateCacheUnit'); u.value='Hours'; u.dispatchEvent(new Event('change',{bubbles:true}));`);
        await check(type + '-timed-cache-edited');
        if (type === 'AzureBlob' || type === 'GoogleStorage') await screenshot(type + '-state-cache');
        assert.equal(await evaluate('editor.save()'), true);
        assert.deepEqual(await evaluate('lastSaved.configuration'), { UseDefStateCaching:false, StateCacheTime:'5', StateCacheUnit:'Hours' }, type + ' Timed cache');
        await loadCacheFixture(fixtures[type + '-cache-timed']);
        await evaluate(`document.getElementById('maui-host-editor-control-UseDefStateCaching').click();`);
        assert.equal(await evaluate('editor.save()'), true);
        assert.deepEqual(await evaluate('lastSaved.configuration'), { UseDefStateCaching:true }, type + ' Return to inherited settings');
    }
    // Exercise DOM input events and verify writable booleans remain JSON booleans.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['S3-basic'])})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click(); document.querySelector('[data-host-editor-config-tab="security"]').click(); const c = document.getElementById('maui-host-editor-control-Secure'); c.value = 'false'; c.dispatchEvent(new Event('change', { bubbles:true }));`);
    assert.equal(await evaluate('editor.save()'), true);
    assert.deepEqual(await evaluate('lastSaved.configuration'), { Secure: false });
    // Client certificate selection and clearing remain available with TLS disabled.
    for (const type of ['FTP','siLock']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures[type+'-tls-none'])})`);
        await evaluate(`window.applyHostSaveToFixture=true;document.querySelector('[data-host-editor-tab="configuration"]').click();document.querySelector('[data-host-editor-config-tab="security"]').click();`);
        assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-DefClientCertSHA1').value`),'AA BB');
        assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-host-editor-config-fields] .maui-host-editor-field > span')].map(label=>label.textContent).filter(label=>['Certificate Store','Certificate Subject'].includes(label))`),['Certificate Store','Certificate Subject']);
        for (const theme of ['light','dark']) {
            await evaluate(`document.documentElement.dataset.theme='${theme}'`);
            await screenshot(type+'-tls-none-'+theme);
        }
        await evaluate(`document.querySelector('[data-host-editor-lookup="certificate"]').click()`);
        await waitLookup();
        await evaluate(`document.querySelectorAll('.maui-host-picker-row')[1].click();document.querySelector('[data-host-picker-ok]').click();`);
        assert.equal(await evaluate('editor.save()'),true);
        assert.deepEqual(await evaluate('lastSaved.configuration'),{DefClientCertSHA1:'CC DD',DefClientCertStore:'My',DefClientCertSubject:'New Certificate'});
        await evaluate(`document.querySelector('[data-host-editor-config-tab="security"]').click();[...document.querySelectorAll('.maui-host-editor-field-actions button')].find(button=>button.textContent==='Clear Certificate').click();`);
        assert.equal(await evaluate('editor.save()'),true);
        assert.deepEqual(await evaluate('lastSaved.configuration'),{DefClientCertSHA1:'',DefClientCertStore:'',DefClientCertSubject:''});
        assert.equal(await evaluate(`fixture.host.configurationSections.flatMap(section=>section.fields).find(field=>field.key==='Secure').value`),type==='FTP'?'none':'No');
        await evaluate(`window.applyHostSaveToFixture=false`);checks+=7;
    }
    // A permission-limited OAuth Host must still display the configured mode.
    const readonlyOAuth = structuredClone(fixtures['GoogleStorage-oauth']);
    readonlyOAuth.host.canEditConfiguration = false;
    readonlyOAuth.host.configurationSections.forEach(section => section.fields.forEach(field => { field.editable = false; }));
    await evaluate(`loadFixture(${JSON.stringify(readonlyOAuth)})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click(); document.querySelector('[data-host-editor-config-tab="authentication"]').click();`);
    assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-OAuthClientID') === null && [...document.querySelectorAll('.maui-host-editor-field > span')].some(c => c.textContent === 'OAuth Client ID')`), true);
    assert.equal(await evaluate(`Boolean(document.getElementById('maui-host-editor-control-DefPassword'))`), false);
    await check('readonly-oauth');
    async function waitLookup() {
        for (let attempt = 0; attempt < 100 && await evaluate(`document.querySelector('[data-host-picker-loading]').hidden === false`); attempt++) await pause(10);
        assert.equal(await evaluate(`document.querySelector('[data-host-picker-loading]').hidden`), true);
    }
    await command('Emulation.setDeviceMetricsOverride', { width: 1400, height: 1000, deviceScaleFactor: 1, mobile: false });
    await evaluate(`document.documentElement.dataset.theme = 'light'; loadFixture(${JSON.stringify(fixtures['SharePoint-basic'])})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click(); document.querySelector('[data-host-editor-config-tab="authentication"]').click(); document.querySelector('[data-host-editor-lookup="certificate"]').click();`);
    await waitLookup(); await screenshot('SharePoint-certificate-picker');
    await evaluate(`document.querySelectorAll('.maui-host-picker-row')[1].click(); document.querySelector('[data-host-picker-ok]').click();`);
    assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-DefClientCertSHA1').value`), 'CC DD');
    assert.equal(await evaluate('editor.save()'), true);
    assert.deepEqual(await evaluate('lastSaved.configuration'), { DefClientCertSHA1:'CC DD', DefClientCertStore:'My', DefClientCertSubject:'New Certificate' });
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click(); document.querySelector('[data-host-editor-config-tab="general"]').click(); document.querySelector('[data-host-editor-lookup="documentlibrary"]').click();`);
    await waitLookup(); await screenshot('SharePoint-library-picker');
    const lookupsBeforeRefresh = await evaluate('lookupCalls.length');
    await evaluate(`document.querySelector('[data-host-picker-refresh]').click()`); await waitLookup();
    assert.equal(await evaluate('lookupCalls.length'), lookupsBeforeRefresh + 1, 'Tab changes cannot multiply picker Refresh requests');
    assert.equal(await evaluate(`document.querySelectorAll('.maui-host-picker-row').length`), 2);
    assert.equal(await evaluate(`document.querySelector('[data-host-picker-ok]').disabled`), true);
    await evaluate(`document.querySelectorAll('.maui-host-picker-row')[1].click(); document.querySelector('[data-host-picker-ok]').click();`);
    assert.equal(await evaluate('editor.save()'), true);
    assert.deepEqual(await evaluate('lastSaved.configuration'), { DefDocumentLibrary:'Library Two', DefDocumentLibraryGuid:'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee' });
    const lookup = await evaluate('lookupCalls.find(call => call.url.includes("DocumentLibraries"))');
    assert.ok(lookup.url.includes('/maui/HostEditor/DocumentLibraries')); assert.equal(lookup.method, 'POST'); assert.equal(lookup.headers.RequestVerificationToken, 'token');
    await evaluate(`loadFixture(${JSON.stringify(fixtures['SharePoint-client-secret'])})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click(); document.querySelector('[data-host-editor-config-tab="general"]').click();`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-lookup="documentlibrary"]').disabled`), false);
    await evaluate(`document.querySelector('[data-host-editor-config-tab="authentication"]').click(); const secret = document.getElementById('maui-host-editor-control-ClientSecret'); secret.value='  replacement  '; secret.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate('editor.save()'), true); assert.deepEqual(await evaluate('lastSaved.configuration'), { ClientSecret:'  replacement  ' });
    await evaluate(`loadFixture({...${JSON.stringify(fixtures['FTP-basic'])},checkoutObjectsForDurationOfEdit:true})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click(); document.querySelector('[data-host-editor-config-tab="security"]').click(); window.deferLookup=true; document.querySelector('[data-host-editor-lookup="certificate"]').click();`);
    for (let attempt = 0; attempt < 100 && !await evaluate('Boolean(window.releaseLookup)'); attempt++) await pause(10);
    assert.equal(await evaluate('editor.save()'), false, 'Picker must prevent parent saves');
    await evaluate(`document.querySelector('[data-host-picker-cancel]').click(); window.releaseLookup(); window.deferLookup=false;`);
    await pause(25);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-picker]').open`), false);
    assert.equal(await evaluate('editor.save()'), false, 'Cancelled lookup cannot change the Host');
    // FTP CCC is a boolean control over the captured Secure enum, not a fabricated native flag.
    for(const [name,checked,base] of [['FTP-basic',false,'implicit'],['FTP-ftp-explicit',false,'on'],['FTP-ftp-ccc',true,'on'],['FTP-ftp-control-only',false,'tls-c'],['FTP-ftp-control-ccc',true,'tls-c'],['FTP-ftp-implicit-ccc',true,'implicit']]) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures[name])})`);
        await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click();document.querySelector('[data-host-editor-config-tab="security"]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-host-editor-ftp-ccc]').value === 'true'`),checked);
        assert.deepEqual(await evaluate(`(()=>{const c=document.querySelector('[data-host-editor-ftp-ccc]');return {tag:c.tagName,options:[...c.options].map(o=>o.textContent)};})()`),{tag:'SELECT',options:['No','Yes']});
        assert.equal(await evaluate(`document.querySelector('[data-host-editor-ftp-ccc]').disabled`),false);
        assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-Secure').value`),base);
        await evaluate(`const c=document.querySelector('[data-host-editor-ftp-ccc]');c.value=${JSON.stringify(checked?'false':'true')};c.dispatchEvent(new Event('change',{bubbles:true}))`);
        if(checked)await screenshot('FTP-CCC-disabled');else await screenshot('FTP-CCC-enabled');
        assert.equal(await evaluate('editor.save()'),true);
        assert.deepEqual(await evaluate('lastSaved.configuration'),{Secure:checked?base:base+'-ccc'});checks+=4;
    }
    // Port changes accompany protocol changes in the native PUT, with custom-port memory across Save.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['FTP-ftp-explicit'])})`);
    await evaluate(`(()=>{window.applyHostSaveToFixture=true;const c=document.getElementById('maui-host-editor-control-Port');c.value='2121';c.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-host-editor-config-tab="security"]').click();const s=document.getElementById('maui-host-editor-control-Secure');s.value='implicit';s.dispatchEvent(new Event('change',{bubbles:true}));})()`);
    assert.equal(await evaluate('editor.save()'),true);
    assert.deepEqual(await evaluate('lastSaved.configuration'),{Secure:'implicit',Port:'990'});
    await evaluate(`(()=>{const c=document.getElementById('maui-host-editor-control-Secure');c.value='on';c.dispatchEvent(new Event('change',{bubbles:true}));document.querySelector('[data-host-editor-config-tab="general"]').click();})()`);
    assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-Port').value`),'2121');
    assert.equal(await evaluate('editor.save()'),true);
    assert.deepEqual(await evaluate('lastSaved.configuration'),{Secure:'on',Port:'2121'});
    await evaluate(`window.applyHostSaveToFixture=false`);
    await evaluate(`loadFixture({...${JSON.stringify(fixtures['FTP-basic'])},checkoutObjectsForDurationOfEdit:true})`);
    await evaluate(`document.querySelector('[data-host-editor-config-tab="security"]').click();const c=document.getElementById('maui-host-editor-control-Secure');c.value='none';c.dispatchEvent(new Event('change',{bubbles:true}));document.querySelector('[data-host-editor-config-tab="general"]').click();`);
    assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-Port').value`),'21','reopening a Host cannot reuse a previous editor session’s custom port');
    checks+=6;
    // Host 0 uses the captured four-section Local File System contract and the normal editor save lifecycle.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['FileSystem-basic'])})`);
    assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-host-editor-config-tab]')].map(c=>c.textContent.trim())`),['General','Uploads','Limits','Advanced']);
    await evaluate(`document.querySelector('[data-host-editor-tab="information"]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-name]').disabled`),true);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-description]').disabled`),false);
    await evaluate(`(()=>{const c=document.querySelector('[data-host-editor-description]');c.value='Local file processing';c.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-host-editor-tab="configuration"]').click();})()`);
    await evaluate(`(()=>{for(const [key,value]of Object.entries({DefRetryCount:'5',DefRetryTimeoutSecs:'45',DefRescanSecs:'1',UseNotif:'true'})){const c=document.getElementById('maui-host-editor-control-'+key);c.value=value;c.dispatchEvent(new Event(c.tagName==='SELECT'?'change':'input',{bubbles:true}));}})()`);
    await screenshot('FileSystem-General');
    await evaluate(`document.querySelector('[data-host-editor-config-tab="uploads"]').click()`);
    await evaluate(`(()=>{for(const [key,value]of Object.entries({RenameAfterUpload:'true',TempUploadName:'LOCAL[Rnd:4]',DefPreserveFileTimestamp:'true'})){const c=document.getElementById('maui-host-editor-control-'+key);c.value=value;c.dispatchEvent(new Event(c.tagName==='SELECT'?'change':'input',{bubbles:true}));}})()`);
    await screenshot('FileSystem-Uploads');
    await evaluate(`document.querySelector('[data-host-editor-config-tab="limits"]').click()`);
    await evaluate(`(()=>{for(const [key,value]of Object.entries({DefMxFi:'100',DefMxBy:'1024000'})){const c=document.getElementById('maui-host-editor-control-'+key);c.value=value;c.dispatchEvent(new Event('input',{bubbles:true}));}})()`);
    await screenshot('FileSystem-Limits');
    await evaluate(`document.querySelector('[data-host-editor-config-tab="advanced"]').click()`);
    await evaluate(`(()=>{for(const [key,value]of Object.entries({DelOldStateDays:'1',AdjDST:'false'})){const c=document.getElementById('maui-host-editor-control-'+key);c.value=value;c.dispatchEvent(new Event(c.tagName==='SELECT'?'change':'input',{bubbles:true}));}})()`);
    await screenshot('FileSystem-Advanced');
    assert.equal(await evaluate('editor.save()'),true);
    assert.deepEqual(await evaluate('lastSaved.configuration'),{DefRetryCount:'5',DefRetryTimeoutSecs:'45',DefRescanSecs:'1',UseNotif:true,RenameAfterUpload:true,TempUploadName:'LOCAL[Rnd:4]',DefPreserveFileTimestamp:true,DefMxFi:'100',DefMxBy:'1024000',DelOldStateDays:'1',AdjDST:false});
    assert.equal(await evaluate('lastSaved.name'),'(Default)'); assert.equal(await evaluate('lastSaved.description'),'Local file processing');
    checks+=7;
    // Native UNC authentication is represented by omission versus username/password, with no UI command in the native PUT.
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Share-run-as'])})`);
    await evaluate(`document.querySelector('[data-host-editor-config-tab="authentication"]').click()`);
    assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-DefUsername') === null`),true);
    await evaluate(`(()=>{const c=document.getElementById('maui-host-editor-control-UncAuthenticationMode');c.value='credentials';c.dispatchEvent(new Event('change',{bubbles:true}));for(const [key,value]of Object.entries({DefUsername:'domain\\\\user',DefPassword:'  secret  '})){const f=document.getElementById('maui-host-editor-control-'+key);f.value=value;f.dispatchEvent(new Event('input',{bubbles:true}));}})()`);
    await screenshot('UNC-specified-credentials');
    assert.equal(await evaluate('editor.save()'),true);
    assert.deepEqual(await evaluate('lastSaved.configuration'),{UncAuthenticationMode:'credentials',DefUsername:'domain\\user',DefPassword:'  secret  '});
    await evaluate(`loadFixture(${JSON.stringify(fixtures['Share-basic'])})`);
    const drive = await evaluate(`(()=>{const c=document.getElementById('maui-host-editor-control-DriveLetter');return{tag:c.tagName,values:[...c.options].map(o=>o.value)}})()`);
    assert.equal(drive.tag,'SELECT');assert.equal(drive.values.length,24);assert.deepEqual([drive.values[0],drive.values[1],drive.values.at(-1)],['','D:','Z:']);
    await evaluate(`document.querySelector('[data-host-editor-config-tab="authentication"]').click();const c=document.getElementById('maui-host-editor-control-UncAuthenticationMode');c.value='run-as';c.dispatchEvent(new Event('change',{bubbles:true}));`);
    assert.equal(await evaluate('editor.save()'),true);
    assert.deepEqual(await evaluate('lastSaved.configuration'),{UncAuthenticationMode:'run-as'});
    const transferLease = structuredClone(fixtures['siLock-basic']); transferLease.checkoutObjectsForDurationOfEdit = true;
    await evaluate(`loadFixture(${JSON.stringify(transferLease)})`);
    await evaluate(`document.querySelector('[data-host-editor-config-tab="authentication"]').click();const c=document.getElementById('maui-host-editor-control-DefPassword');c.value='  replacement  ';c.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate('editor.save()'),true);
    assert.deepEqual(await evaluate('lastSaved.configuration'),{DefPassword:'  replacement  '});
    await evaluate(`document.querySelector('[data-host-editor-config-tab="security"]').click();document.querySelector('[data-host-editor-lookup="certificate"]').click();`);
    await waitLookup();
    assert.equal(await evaluate(`lookupCalls.at(-1).headers['X-MAUI-Edit-Lease']`),'host-layout-lease');
    await evaluate(`document.querySelectorAll('.maui-host-picker-row')[1].click();document.querySelector('[data-host-picker-ok]').click();`);
    assert.equal(await evaluate('editor.save()'),true);
    assert.deepEqual(await evaluate('lastSaved.configuration'),{DefClientCertSHA1:'CC DD',DefClientCertStore:'My',DefClientCertSubject:'New Certificate'});
    for(const type of ['POP3','SMTP']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures[type+'-office365'])})`);
        await evaluate(`document.querySelector('[data-host-editor-config-tab="authentication"]').click();`);
        assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-OAuthProvider').value`),'microsoft365');
        assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-OAuthTenantID').value`),'tenantid1');
        assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-OAuthTokenURL') === null`),true);
        await evaluate(`(()=>{const c=document.getElementById('maui-host-editor-control-OAuthTenantID');c.value='tenantid2';c.dispatchEvent(new Event('input',{bubbles:true}));})()`);
        await screenshot(type+'-office365');
        assert.equal(await evaluate('editor.save()'),true);
        assert.deepEqual(await evaluate('lastSaved.configuration'),{OAuthProvider:'microsoft365',OAuthTenantID:'tenantid2'});
        await evaluate(`(()=>{const c=document.getElementById('maui-host-editor-control-OAuthProvider');c.value='custom';c.dispatchEvent(new Event('change',{bubbles:true}));})()`);
        assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-OAuthTenantID') === null`),true);
        assert.equal(await evaluate(`Boolean(document.getElementById('maui-host-editor-control-OAuthTokenURL'))`),true);
    }
    checks+=24;
    await evaluate(`loadFixture({...${JSON.stringify(fixtures['FTP-basic'])},checkoutObjectsForDurationOfEdit:true})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="information"]').click();const c=document.querySelector('[data-host-editor-name]');c.value+=' draft';c.dispatchEvent(new Event('input',{bubbles:true}));editor.close()`);
    assert.equal(await evaluate(`confirmCalls.at(-1).confirmText`),'Discard Changes');
    assert.equal(await evaluate(`confirmCalls.at(-1).danger`),true);checks+=2;
    // Three native parsing modes, active controls, Resource script labels, and sparse patches.
    for(const name of ['FTP-basic','FTP-ftp-custom','FTP-ftp-script','FTP-readonly','FTP-scripts-unavailable']) {
        await evaluate(`loadFixture(${JSON.stringify(fixtures[name])})`);
        await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click();document.querySelector('[data-host-editor-config-tab="directory-parsing"]').click()`);
        const mode=name==='FTP-ftp-custom'?'custom':name==='FTP-ftp-script'||name==='FTP-scripts-unavailable'?'script':'automatic';
        assert.equal(await evaluate(`document.querySelector('[data-host-editor-directory-mode]:checked').value`),mode);
        assert.equal(await evaluate(`document.querySelector('.maui-host-ftp-directory > legend')`),null,'Remove the redundant Directory list parsing legend');
        assert.equal(await evaluate(`(()=>{const a=getComputedStyle(document.querySelector('.maui-host-ftp-directory > label')),b=getComputedStyle(document.querySelector('.maui-host-ftp-directory-columns .maui-host-editor-control,.maui-host-ftp-directory-columns .maui-host-editor-value'));return a.fontFamily===b.fontFamily&&a.fontSize===b.fontSize&&a.fontSize==='12px';})()`),true,'Parsing fields use the shared font family and size');
        assert.equal(await evaluate(`document.querySelector('.maui-host-ftp-directory-columns').disabled`),mode!=='custom'||name==='FTP-readonly');
        assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-DirScriptID').matches(':disabled')`),mode!=='script'||name==='FTP-readonly'||name==='FTP-scripts-unavailable');
        assert.equal(await evaluate('editor.save()'),false,'Opening a parsing sheet must not change its mode');
        if(name==='FTP-readonly')assert.equal(await evaluate(`[...document.querySelectorAll('[data-host-editor-directory-mode]')].every(c=>c.disabled)`),true);
        if(name==='FTP-ftp-script')assert.equal(await evaluate(`document.getElementById('maui-host-editor-control-DirScriptID').selectedOptions[0].textContent`),'Directory Parser');
        await screenshot(name+'-directory-parsing');checks+=5;
    }
    await evaluate(`loadFixture({...${JSON.stringify(fixtures['FTP-basic'])},checkoutObjectsForDurationOfEdit:true})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click();document.querySelector('[data-host-editor-config-tab="directory-parsing"]').click();document.querySelector('[data-host-editor-directory-mode="script"]').click()`);
    assert.equal(await evaluate('editor.save()'),false,'A script mode requires a Script before writing');
    assert.match(await evaluate(`document.querySelector('[data-host-editor-error]').textContent`),/Select a directory parsing Script/);
    await evaluate(`const c=document.getElementById('maui-host-editor-control-DirScriptID');c.value='100401';c.dispatchEvent(new Event('change',{bubbles:true}))`);
    assert.equal(await evaluate('editor.save()'),true);
    assert.deepEqual(await evaluate('lastSaved.configuration'),{DirectoryParsingMode:'script',DirScriptID:'100401'});
    await evaluate(`loadFixture(${JSON.stringify(fixtures['FTP-ftp-script'])})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click();document.querySelector('[data-host-editor-config-tab="directory-parsing"]').click();document.querySelector('[data-host-editor-directory-mode="automatic"]').click()`);
    assert.equal(await evaluate('editor.save()'),true);assert.deepEqual(await evaluate('lastSaved.configuration'),{DirectoryParsingMode:'automatic'});
    await evaluate(`loadFixture({...${JSON.stringify(fixtures['FTP-basic'])},checkoutObjectsForDurationOfEdit:true})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click();document.querySelector('[data-host-editor-config-tab="directory-parsing"]').click();document.querySelector('[data-host-editor-directory-mode="custom"]').click();const c=document.getElementById('maui-host-editor-control-CustomDirSkipBottom');c.value='2';c.dispatchEvent(new Event('input',{bubbles:true}))`);
    assert.equal(await evaluate('editor.save()'),true);assert.deepEqual(await evaluate('lastSaved.configuration'),{DirectoryParsingMode:'custom',CustomDirSkipBottom:'2'});checks+=8;
    await evaluate(`loadFixture(${JSON.stringify(fixtures['SSHFTP-basic'])})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click(); document.querySelector('[data-host-editor-config-tab="ssh-algorithms"]').click(); document.querySelector('[data-host-editor-algorithms="EncList"] [data-algorithm-move="down"]').click();`);
    assert.equal(await evaluate('editor.save()'), true);
    assert.equal(await evaluate('lastSaved.configuration.EncList'), 'aes128-ctr,aes256-ctr');
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click(); document.querySelector('[data-host-editor-config-tab="ssh-algorithms"]').click(); document.querySelector('[data-host-editor-algorithms="EncList"] .maui-host-algorithm-toolbar button').click();`);
    assert.equal(await evaluate('editor.save()'), true);
    assert.equal(await evaluate('lastSaved.configuration.EncList'), 'aes256-ctr,aes192-ctr,aes128-ctr,aes256-gcm@openssh.com,aes128-gcm@openssh.com,chacha20-poly1305@openssh.com');
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-algorithms="EncList"] [data-algorithm="rijndael128-cbc"]')`),null,'Other-provider-only defaults are not selectable');
    const legacy=structuredClone(fixtures['SSHFTP-basic']);legacy.host.configurationSections.flatMap(section=>section.fields).find(field=>field.key==='EncList').value='retired-cipher,aes128-ctr';
    await evaluate(`loadFixture(${JSON.stringify(legacy)})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click();document.querySelector('[data-host-editor-config-tab="ssh-algorithms"]').click();`);
    assert.equal(await evaluate(`document.querySelector('[data-algorithm="retired-cipher"] input').checked`),true);
    assert.equal(await evaluate(`document.querySelector('[data-algorithm="retired-cipher"] input').disabled`),true,'An unsupported saved name is preserved but cannot be newly selected');
    assert.equal(await evaluate(`document.querySelector('[data-algorithm="aes128-ctr"] input').disabled`),false);
    assert.equal(await evaluate('editor.save()'),false,'Displaying saved unknown algorithms does not modify the Host');
    const emptyAlgorithms=structuredClone(fixtures['SSHFTP-basic']);
    emptyAlgorithms.host.configurationSections.flatMap(section=>section.fields).filter(field=>['EncList','KexAlgs'].includes(field.key)).forEach(field=>{field.value='';});
    await evaluate(`loadFixture(${JSON.stringify(emptyAlgorithms)})`);
    assert.equal(await evaluate('editor.save()'),false,'Default display alone must not dirty a Host');
    await evaluate(`document.querySelector('[data-host-editor-config-tab="general"]').click();const control=document.getElementById('maui-host-editor-control-Host');control.value='changed.test';control.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate('editor.save()'),true,'Editing a Host with empty native lists remains saveable');
    assert.equal(await evaluate('lastSaved.configuration.EncList'),sshDefaults.Encryption.Value,'Empty encryption fields persist configured defaults');
    assert.equal(await evaluate('lastSaved.configuration.KexAlgs'),sshDefaults.KeyExchange.Value,'Empty KEX fields persist configured defaults');
    checks+=4;
    await evaluate(`loadFixture(${JSON.stringify(fixtures['SSHFTP-catalog-unavailable'])})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click();document.querySelector('[data-host-editor-config-tab="ssh-algorithms"]').click();`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-algorithms="EncList"] button').disabled`),true);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-algorithms="EncList"]').parentElement.textContent.includes('Reload the Host')`),true);
    await evaluate(`document.querySelector('[data-host-editor-config-tab="general"]').click();const c=document.getElementById('maui-host-editor-control-Host');c.value='changed.test';c.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate('editor.save()'),true,'Unrelated Host edits remain usable when discovery fails');
    assert.equal(await evaluate(`'EncList' in lastSaved.configuration || 'KexAlgs' in lastSaved.configuration`),false);
    checks+=9;
    await evaluate(`loadFixture(${JSON.stringify(fixtures['S3-basic'])})`);
    await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').click(); document.querySelector('[data-host-editor-config-tab="security"]').click(); const control = document.getElementById('maui-host-editor-control-Secure'); control.value='false'; control.dispatchEvent(new Event('change',{bubbles:true})); window.deferSave=true; window.pendingSave=editor.save(); void 0;`);
    for (let attempt=0;attempt<100 && !await evaluate('Boolean(window.releaseSave)');attempt++) await pause(10);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-content]').inert`), true);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dirty-state]').textContent`),'Saving changes to MOVEit Automation…');checks++;
    assert.equal(await evaluate(`editor.open('99')`), false, 'Loading another Host cannot reset an in-flight save');
    await evaluate(`document.querySelector('[data-host-editor-tab="information"]').click(); document.getElementById('maui-host-editor-control-Secure').focus();`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-tab="configuration"]').getAttribute('aria-selected')`), 'true');
    assert.equal(await evaluate(`document.activeElement.id === 'maui-host-editor-control-Secure'`), false, 'In-flight controls cannot receive focus');
    await evaluate(`window.releaseSave(); window.deferSave=false;`);
    assert.equal(await evaluate('window.pendingSave'), true);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dirty-state]').textContent`),'Saved.');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-content]').inert`), false);
    // Render the real Host Auto Save toggle with the shared checkout client and production save.
    await evaluate(`loadFixture(${JSON.stringify({...fixtures['FTP-basic'],checkoutObjectsForDurationOfEdit:true,preferences:{autoSave:false}})})`);
    assert.equal(await evaluate(`leaseCalls.at(-1).url.endsWith('/Open')`),true);checks++;
    await evaluate(`const toggle=document.querySelector('[data-host-editor-auto-save]');toggle.checked=true;toggle.dispatchEvent(new Event('change',{bubbles:true}));`);
    for(let i=0;i<100 && !await evaluate(`document.querySelector('[data-host-editor-save]').textContent==='Close'`);i++)await pause(10);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-save]').textContent`),'Close');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-cancel]').hidden`),true);checks++;
    await evaluate(`window.priorHostSaves=hostSaveCount;const input=document.getElementById('maui-host-editor-control-Host');input.value='autosave.example.test';input.dispatchEvent(new Event('input',{bubbles:true}));input.blur();`);
    for(let i=0;i<150 && !await evaluate('hostSaveCount>priorHostSaves');i++)await pause(20);
    assert.equal(await evaluate('hostSaveCount-priorHostSaves'),1,'Host Auto Save submits one production save');checks++;
    assert.equal(await evaluate(`hostSaveHeaders.get('X-MAUI-Edit-Lease')`),'host-layout-lease');checks++;
    for(let i=0;i<100 && await evaluate(`document.querySelector('[data-host-editor-content]').inert`);i++)await pause(10);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-save]').textContent`),'Close');checks++;
    await evaluate('editor.close()');
    assert.equal(await evaluate(`leaseCalls.at(-1).url.endsWith('/Close')`),true);checks++;
    await evaluate(`loadFixture({...${JSON.stringify(fixtures['FTP-basic'])},checkoutObjectsForDurationOfEdit:true})`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dirty-state]').textContent`),'No unsaved changes.');checks++;
    await evaluate(`document.querySelector('[data-host-editor-clone]').click()`);
    for(let i=0;i<100 && !await evaluate('cloneCalls.length');i++) await pause(20);
    assert.equal(await evaluate('cloneCalls.length'),1);
    assert.equal(await evaluate('cloneCalls[0].id'),fixtures['FTP-basic'].host.id);
    assert.equal(await evaluate('cloneCalls[0].editorOpen'),true,'Clone cancellation keeps the source editor and checkout open');
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dialog]').open`),true);
    await evaluate(`(() => { const d=document.createElement('dialog'); d.innerHTML='<div><header>Save Notification Template</header><button>Cancel</button></div>'; document.body.append(d); d.showModal(); MauiModalUI.showError('Unable to save the notification template.'); window.feedbackDialog=d; })()`);
    assert.equal(await evaluate(`(() => { const b=feedbackDialog.querySelector('[data-maui-modal-feedback]'); return b.textContent==='Unable to save the notification template.' && b.getBoundingClientRect().height>0 && b.closest('dialog')===feedbackDialog; })()`),true,'Nested dialog displays its own error banner');
    await evaluate(`feedbackDialog.close(); feedbackDialog.showModal();`);
    assert.equal(await evaluate(`feedbackDialog.querySelector('[data-maui-modal-feedback]').hidden`),true,'Reopening clears stale error');
    await evaluate(`feedbackDialog.close(); feedbackDialog.remove();`);
    // Render actual Information partials in both themes; exercise each panel and long related-object lists.
    await evaluate(`editor.close()`);
    // Check shared grid selection/menu geometry, including disabled and absent checkboxes.
    await evaluate(`(() => {const d=document.createElement('dialog');d.className='settings-dialog';d.dataset.gridAlignmentFixture='';d.innerHTML='<div class="settings-card"><table class="data-grid-table"><thead><tr class="column-header-row"><th class="select-column has-row-info">${gridPageSelection}</th><th>Grid</th></tr></thead><tbody>'+['Tasks','Hosts','Scripts','Date Lists','Keys/Certificates','Global Parameters','Reports','Task Runs','File Activity','Audit','Upcoming Runs'].map((name,i)=>'<tr><td class="select-column has-row-info"><div class="row-select-tools">'+(i===6?'':'<input type="checkbox" '+(i===2?'disabled':'')+' aria-label="Select row" />')+'<button type="button" class="row-actions-button" aria-label="Row actions">…</button></div></td><td>'+name+'</td></tr>').join('')+'</tbody></table></div>';document.body.append(d);window.gridAlignmentFixture=d;d.showModal();})()`);
    for(const width of [1400,800,600]) {
        await command('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false});
        for(const theme of ['light','dark']) {
            await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
            assert.equal(await evaluate(`(() => {return [...gridAlignmentFixture.querySelectorAll('tbody tr')].every(row=>{const b=row.querySelector('button'),c=row.querySelector('input'),cell=row.querySelector('td').getBoundingClientRect(),box=b.getBoundingClientRect(),center=n=>n.top+n.height/2,p=getComputedStyle(b,'::after');return Math.abs(center(box)-center(cell))<1&&(!c||Math.abs(center(box)-center(c.getBoundingClientRect()))<1)&&getComputedStyle(b).fontSize==='0px'&&p.maskImage!=='none'&&p.width==='18px';});})()`),true,'Grid menu and checkbox centers align and visible dots use centered geometry');
            assert.equal(await evaluate(`(() => {const c=gridAlignmentFixture.querySelector('[data-page-select]');c.checked=${theme==='dark'};c.indeterminate=${width===800};const b=c.getBoundingClientRect(),cell=c.closest('th').getBoundingClientRect();return Math.abs((b.top+b.height/2)-(cell.top+cell.height/2))<1;})()`),true,'Grid header checkbox is vertically centered in its cell, including checked/indeterminate states');
            if([1400,600].includes(width))await screenshot(`Grid-row-controls-${width}-${theme}`); checks++;
        }
    }
    await evaluate(`gridAlignmentFixture.close();gridAlignmentFixture.remove()`);
    let informationChecks = 0;
    for(const width of [1400,800,600]) {
        await command('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false});
        for(const theme of ['light','dark']) {
            await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
            for(const name of ['task','host','script','date-list','key-certificate','global-parameter','report']) {
                const selector=name==='report'?'[data-report-edit-dialog]':'[data-'+name+'-details-dialog]';
                await evaluate(`(() => {
                    const d=document.querySelector('${selector}');window.informationDialog=d;
                    d.querySelectorAll('.details-loading').forEach(n=>n.hidden=true);
                    d.querySelectorAll('[data-${name}-details-content]').forEach(n=>n.hidden=false);
                    for(const g of d.querySelectorAll('.details-grid:empty'))g.innerHTML='<label class="field-block full-span"><span>Environment</span><input class="text-input" value="Production" /></label>';
                    for(const g of d.querySelectorAll('.audit-grid'))informationAuditRenderers[${JSON.stringify(name)}]?.(g);
                    d.querySelectorAll('.task-identity-item > strong').forEach(n=>n.textContent=/ID/.test(n.previousElementSibling.textContent)?'937919810':'Example object');
                    d.querySelectorAll('.field-block > strong').forEach(n=>n.textContent='Example value');
                    if(d.querySelector('[data-report-edit-time]'))d.querySelector('[data-report-edit-time]').append(new Option('5:00 PM','17:00'));
                    d.querySelectorAll('input:not([type=hidden]):not(.audit-item > input),textarea').forEach(n=>{if(!['checkbox','radio','date'].includes(n.type))n.value=n.tagName==='TEXTAREA'?'A description with representative information for layout review.':'Example value';});
                    d.showModal();
                })()`);
                const panels=await evaluate(`(() => {const panels=[...informationDialog.querySelectorAll('[data-${name}-details-panel]')];return panels.length?panels.map(p=>p.getAttribute('data-${name}-details-panel')).filter(key=>key!=='logs'):['information'];})()`);
                for(const panel of panels) {
                    await evaluate(`(() => {const d=informationDialog;informationTabRenderers[${JSON.stringify(name)}]?.(d,${JSON.stringify(panel)});d.querySelectorAll('[data-${name}-details-panel]').forEach(n=>n.hidden=n.getAttribute('data-${name}-details-panel')!==${JSON.stringify(panel)});d.querySelectorAll('[data-${name}-details-tab]').forEach(n=>{const active=n.getAttribute('data-${name}-details-tab')===${JSON.stringify(panel)};n.classList.toggle('active',active);n.setAttribute('aria-selected',String(active));});d.querySelectorAll('.host-tasks-empty').forEach(n=>n.hidden=true);d.querySelectorAll('.host-tasks-list').forEach(n=>{n.hidden=false;n.lastElementChild.innerHTML=Array.from({length:25},(_,i)=>'<div class="host-task-row"><div>'+i+'</div><div><button type="button" class="row-primary-link">Related object with a long descriptive name '+i+'</button></div></div>').join('');});d.querySelector('.task-details-body,.outbound-report-body').scrollTop=0;})()`);
                    const geometry=await evaluate(`(() => {const d=informationDialog,body=d.querySelector('.task-details-body,.outbound-report-body'),cards=[...d.querySelectorAll('.details-section,.maui-property-card')].filter(n=>!n.closest('[hidden]'));return{cards:cards.map(n=>{const c=getComputedStyle(n);return{padding:c.paddingTop,radius:c.borderRadius,border:c.borderBottomWidth,background:c.backgroundColor};}),background:getComputedStyle(document.querySelector('.maui-host-editor-card')).backgroundColor,overflow:body.scrollWidth>body.clientWidth+1,visible:cards.length};})()`);
                    assert.ok(geometry.visible>0,`${name}/${panel}: no visible property groups`);
                    if(panel==='information')assert.equal(await evaluate(`inspectPropertyHeading(informationDialog.querySelector('.maui-object-properties-heading'))`),true,`${name}: side-by-side identity columns with labels above left-aligned values`);
                    assert.equal(await evaluate(`[...informationDialog.querySelectorAll('.settings-footer button')].filter(n=>n.textContent.trim()==='Close').length`),1,'Information footer uses Close after production tab changes');
                    if(panel==='information') {
                        assert.equal(await evaluate(`informationDialog.querySelector('.field-block > strong,.audit-item > strong')===null`),true,'Read-only Information properties and audits use textboxes');
                        if(name==='date-list'||name==='script') {
                            const valueHook=name==='date-list'?'date-list-entry-count':'script-language',descriptionHook=name==='date-list'?'date-list-description':'script-description';
                            assert.equal(await evaluate(`(() => {const field=informationDialog.querySelector('[data-${valueHook}]').closest('.field-block'),description=informationDialog.querySelector('[data-${descriptionHook}]').closest('.field-block');return field.previousElementSibling===description&&field.getBoundingClientRect().top>=description.getBoundingClientRect().bottom;})()`),true,'Entries/Language appears directly below Description');
                        }
                        if(name!=='report')assert.deepEqual(await evaluate(`[...informationDialog.querySelectorAll('.audit-item > input')].map(n=>({value:n.value,readonly:n.readOnly,labeled:n.parentElement.tagName==='LABEL'}))`),['October 15, 2026','Marc','October 15, 2026','Reviewer'].map(value=>({value,readonly:true,labeled:true})),'Production audit values are bound to labeled readonly textboxes');
                    }
                    if(panel==='tasks'||panel==='dependencies')assert.equal(await evaluate(`informationDialog.querySelector('[data-${name}-details-panel="${panel}"]').querySelector('.source-badge')===null`),true,'Information related-Tasks sheets have no source pills');
                    assert.equal(await evaluate(`[...informationDialog.querySelectorAll('.source-badge')].every(n=>getComputedStyle(n).fontSize==='10px'&&getComputedStyle(n).lineHeight==='12px'&&getComputedStyle(n).padding==='3px 8px'&&getComputedStyle(n).fontWeight==='700')`),true,'Information MOVEit and MAUI pill sizes match');
                    assert.equal(await evaluate(`[...informationDialog.querySelectorAll('.details-section-heading h3')].every(n=>getComputedStyle(n).fontSize==='15px')`),true,'All Information property section titles use 15px');
                    for(const card of geometry.cards) {assert.deepEqual([card.padding,card.radius,card.border],['18px','10px','1px'],`${name}/${panel} matches Host cards`);assert.equal(card.background,geometry.background);}
                    assert.equal(geometry.overflow,false,`${name}/${panel}/${width}/${theme}: card content overflows`);
                    const footer=await evaluate(`informationDialog.querySelector('.settings-footer').getBoundingClientRect().toJSON()`);
                    assert.ok(footer.bottom<=1000&&footer.top>=0,'Information footer remains visible');
                    if([1400,600].includes(width))await screenshot(`Information-${name}-${panel}-${width}-${theme}`);
                    informationChecks++;
                }
                await evaluate('informationDialog.close()');
            }
        }
    }
    // Real production New Host markup, CSS and field-visibility callback.
    for(const theme of ['light','dark']) for(const width of [1400,1024,800,600]) {
        await command('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)};window.newHostTestDialog=document.querySelector('[data-new-host-dialog]');newHostTestDialog.showModal();`);
        for(const type of ['Share','siLock','FTP','SSHFTP','POP3','SMTP','S3','AzureBlob','SharePoint','GoogleStorage','OneDrive']) {
            const result=await evaluate(`(()=>{newHostTestDialog.querySelector('input[value="${type}"]').checked=true;syncNewHostConnectionField();const input=newHostTestDialog.querySelector('[data-new-host-unc]'),field=input.closest('label');return{visible:getComputedStyle(field).display!=='none',required:input.required,disabled:input.disabled,label:field.querySelector('span').textContent,overflow:newHostTestDialog.scrollWidth>newHostTestDialog.clientWidth+1};})()`);
            assert.deepEqual(result,{visible:type==='Share',required:type==='Share',disabled:type!=='Share',label:'Resource UNC',overflow:false});
            checks++;
        }
        await evaluate('newHostTestDialog.close()');
    }
    // Admin recovery retains values, changes revision and pauses Auto Save until manual Save.
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1000,deviceScaleFactor:1,mobile:false});
    const recoveryHost=structuredClone(fixtures['FTP-basic']);recoveryHost.preferences={autoSave:true};
    await evaluate(`document.documentElement.dataset.moveitAdmin='true';window.hostSaveConflict=true;window.hostSaveCount=0`);
    assert.equal(await evaluate(`loadFixture(${JSON.stringify(recoveryHost)})`),true);
    await evaluate(`(()=>{const c=document.querySelector('[data-host-editor-description]');c.value='Retained Host changes';c.dispatchEvent(new Event('input',{bubbles:true}));})()`);
    assert.equal(await evaluate('editor.save()'),false);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-override]').hidden`),false);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-reload]').hidden`),false);
    await screenshot('Host-checkout-conflict');
    await evaluate(`document.querySelector('[data-host-editor-override]').click()`);
    for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-host-editor-dirty-state]').textContent.includes('Checkout restored')`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-description]').value`),'Retained Host changes');
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-save]').textContent`),'Save');
    await pause(750);assert.equal(await evaluate('hostSaveCount'),1,'override cannot trigger Auto Save');
    await evaluate(`window.hostSaveConflict=false;document.querySelector('[data-host-editor-save]').click()`);
    for(let i=0;i<100&&await evaluate('hostSaveCount')<2;i++)await pause(20);
    assert.equal(await evaluate('lastSaved.editVersion'),'recovered-host-revision');
    assert.equal(await evaluate('lastSaved.description'),'Retained Host changes');
    await pause(50);
    await evaluate(`document.documentElement.dataset.moveitAdmin='false'`);
    await evaluate(`loadFixture({...${JSON.stringify(fixtures['FTP-basic'])},checkoutObjectsForDurationOfEdit:true})`);
    await evaluate(`window.hostSaveConflict=true;(()=>{const c=document.querySelector('[data-host-editor-description]');c.value='Discard on reload';c.dispatchEvent(new Event('input',{bubbles:true}));})()`);
    assert.equal(await evaluate('editor.save()'),false);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-override]').hidden`),true);
    await evaluate(`document.querySelector('[data-host-editor-reload]').click()`);
    for(let i=0;i<100&&!await evaluate(`document.querySelector('[data-host-editor-reload]').hidden`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-description]').value`),fixtures['FTP-basic'].host.description);
    await evaluate(`window.hostSaveConflict=false`);checks+=10;
    // Production Settings section, shared widget, adapter and unified Save Configuration code.
    const sshCatalog = Object.fromEntries([['encryption','EncList'],['keyExchange','KexAlgs']].map(([key,field]) => [key,
        fixtures['SSHFTP-basic'].host.configurationSections.flatMap(section=>section.fields).find(item=>item.key===field).options]));
    const requiredSettings = { keepAliveEnabled:true, keepAliveIntervalMinutes:5, requestTimeoutSeconds:30, importRowLimit:500, taskRunHistoryMaxCount:1000, recycleBinRetentionDays:30,
        logRetentionDays:30, jobHistoryRetentionDays:30, systemMessageType:'Information', backupFrequencyHours:24,
        backupRetentionDays:7, databaseConnectionString:'Data Source=Data/test.db' };
    const supportControls = '<div hidden>'+Object.entries(requiredSettings).map(([name,value])=>`<input data-setting="${name}" value="${value}">`).join('')
        +'<input data-report-smtp="port" value="25"><div data-server-list><div data-server-row>'
        +Object.entries({key:'test',environmentKey:'TEST',displayName:'Test',webAdminUrl:'https://moveit.test'}).map(([name,value])=>`<input data-server-field="${name}" value="${value}">`).join('')
        +'</div></div><div data-security-user-list><div data-security-user><input data-security-field="username" value="admin"><input data-security-field="role" value="Admin"></div></div></div>';
    await evaluate(`window.sshWorkspace=document.createElement('div');sshWorkspace.dataset.configurationWorkspace='';sshWorkspace.dataset.saveUrl='/SettingsSave';sshWorkspace.dataset.sshAlgorithmsUrl='/SshAlgorithms';sshWorkspace.dataset.configurationRevision='original';sshWorkspace.style.maxWidth='1000px';sshWorkspace.innerHTML=${JSON.stringify(sshSettingsMarkup+supportControls+'<div class="configuration-save-bar"><span data-configuration-save-state></span><button data-config-save>Save Changes</button><button data-config-discard>Discard</button></div><div data-configuration-toast></div>')};document.body.append(sshWorkspace);window.sshDiscoveryFailure=false;window.sshSaveFailure=false;window.sshRequests=[];window.sshDeferred=false;window.fetch=async(url,options={})=>{if(String(url)==='/SshAlgorithms')return new Response(JSON.stringify(sshDiscoveryFailure?{message:'Discovery unavailable; defaults preserved.'}:${JSON.stringify(sshCatalog)}),{status:sshDiscoveryFailure?503:200});if(String(url)==='/SettingsSave'){sshRequests.push(JSON.parse(options.body));if(sshDeferred)return await new Promise(resolve=>{window.completeSshSave=()=>resolve(new Response(JSON.stringify({configurationRevision:'saved',message:'Configuration saved.'})));});return new Response(JSON.stringify(sshSaveFailure?{message:'Save rejected'}:{configurationRevision:'saved',message:'Configuration saved.'}),{status:sshSaveFailure?409:200});}throw new Error('Unexpected Settings request '+url);};`);
    await evaluate(read('wwwroot/js/configuration-ssh-algorithms.js'));
    await evaluate(read('wwwroot/js/configuration.js'));
    for(let attempt=0;attempt<100&&!await evaluate(`Boolean(sshWorkspace.querySelector('[data-ssh-algorithm-list] input'))`);attempt++)await pause(20);
    assert.equal(await evaluate('MauiSshAlgorithmSettings.collect()'),null,'Discovery alone must not dirty Settings'); checks++;
    assert.equal(await evaluate(`sshWorkspace.querySelector('[data-config-save]').disabled`),true); checks++;
    for(const theme of ['light','dark'])for(const width of [1400,1024,800,600]){
        await command('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
        assert.equal(await evaluate(`(()=>{const section=sshWorkspace.querySelector('[data-config-section]');return section.scrollWidth>section.clientWidth+1||[...section.querySelectorAll('.maui-host-algorithm-row')].some(row=>row.scrollWidth>row.clientWidth+1);})()`),false,'Settings lists must fit their containers'); checks++;
        assert.deepEqual(await evaluate(`['encryption','keyExchange'].map(key=>sshWorkspace.querySelectorAll('[data-ssh-algorithm-list="'+key+'"] input:checked').length)`),[6,13]); checks++;
        if([1400,600].includes(width))await screenshot(`Settings-SSH-KEX-${width}-${theme}`);
    }
    await evaluate(`sshWorkspace.querySelector('[data-ssh-algorithm-list="encryption"] [data-algorithm-move="down"]').click()`);
    assert.equal(await evaluate(`sshWorkspace.querySelector('[data-config-save]').disabled`),false,'Algorithm ordering must dirty the real Settings form'); checks++;
    assert.equal(await evaluate(`MauiSshAlgorithmSettings.collect().encryption.split(',')[0]`),'aes192-ctr'); checks++;
    await evaluate(`window.sshDeferred=true;sshWorkspace.querySelector('[data-config-save]').click()`);
    assert.equal(await evaluate(`sshWorkspace.querySelectorAll('[data-ssh-algorithm-list] input:not(:disabled),[data-ssh-algorithm-list] button:not(:disabled)').length`),0,'Settings save locks both lists'); checks++;
    assert.equal(await evaluate(`Object.hasOwn(sshRequests.at(-1).sshAlgorithms,'keyExchange')`),false,'Unchanged defaults are not submitted'); checks++;
    await evaluate(`completeSshSave();window.sshDeferred=false`); await pause(50);
    assert.equal(await evaluate('MauiSshAlgorithmSettings.collect()'),null,'Successful save advances the defaults baseline'); checks++;
    assert.equal(await evaluate(`sshWorkspace.querySelector('[data-config-save]').disabled`),true); checks++;
    await evaluate(`sshWorkspace.querySelector('[data-ssh-algorithm-list="encryption"] .maui-host-algorithm-toolbar button').click();window.sshSaveFailure=true;sshWorkspace.querySelector('[data-config-save]').click()`); await pause(50);
    assert.equal(await evaluate(`MauiSshAlgorithmSettings.collect().encryption`),sshDefaults.Encryption.Value,'Failed save retains draft defaults'); checks++;
    assert.equal(await evaluate(`sshWorkspace.querySelector('[data-config-save]').disabled`),false); checks++;
    await evaluate(`window.sshSaveFailure=false;sshWorkspace.querySelector('[data-config-save]').click()`); await pause(50);
    await evaluate(`window.sshDiscoveryFailure=true;sshWorkspace.querySelector('[data-ssh-algorithms-reload]').click()`); await pause(50);
    assert.equal(await evaluate(`sshWorkspace.querySelector('[data-ssh-algorithms-status]').getAttribute('role')`),'alert','Discovery failure is visible inside Settings'); checks++;
    assert.equal(await evaluate('MauiSshAlgorithmSettings.collect()'),null,'Failed discovery preserves saved values'); checks++;
    await evaluate(`const input=sshWorkspace.querySelector('[data-setting="importRowLimit"]');input.value='501';input.dispatchEvent(new Event('input',{bubbles:true}));sshWorkspace.querySelector('[data-config-save]').click()`); await pause(50);
    assert.equal(await evaluate(`sshRequests.at(-1).sshAlgorithms`),null,'Unrelated Settings save can proceed without discovery'); checks++;
    assert.equal(await evaluate(`sshRequests.at(-1).importRowLimit`),501); checks++;
    await evaluate(`window.sshDiscoveryFailure=false;sshWorkspace.querySelector('[data-ssh-algorithms-reload]').click()`); await pause(50);
    assert.equal(await evaluate(`sshWorkspace.querySelectorAll('[data-ssh-algorithm-list] input:checked').length`),19,'Retry restores lists with saved selections'); checks++;
    assert.deepEqual(await evaluate('layoutErrors'),[],'Settings must not introduce browser errors'); checks++;
    // Exercise the same nested initializer used by Task editors with real shared dialogs.
    await command('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/?nested=1`});
    for(let i=0;i<100&&!await evaluate('Boolean(window.openNestedHost)');i++)await pause(20);
    await evaluate(`window.fixture={...${JSON.stringify(fixtures['FTP-basic'])},checkoutObjectsForDurationOfEdit:true};fixture.host.assignedCount=1;fixture.host.tasks=[];window.taskDraft='unchanged Task draft';openNestedHost(fixture.host.id,fixture.host.name,{onClosed:()=>nestedReturns++})`);
    for(const action of ['test','clone','export']) {
        assert.equal(await evaluate(`document.querySelector('[data-host-editor-${action}]').disabled`),false,action+' is wired for nested admin');checks++;
    }
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-delete]').disabled`),true,'Task references disable Delete');checks++;
    assert.match(await evaluate(`document.querySelector('[data-host-editor-delete]').title`),/used by Tasks/);checks++;
    await evaluate(`document.querySelector('[data-host-editor-test]').click()`);
    for(let i=0;i<100&&await evaluate(`document.querySelector('[data-host-test-run]').disabled`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-host-test-dialog]').open`),true);checks++;
    assert.equal(await evaluate(`actionRequests.at(-1).headers.get('X-MAUI-Edit-Lease')`),'host-layout-lease','Test borrows the Host editor checkout');checks++;
    await evaluate(`document.querySelector('[data-host-test-close]').click()`);
    assert.equal(await evaluate('nestedReturns'),0);checks++;
    await evaluate(`document.querySelector('[data-host-editor-export]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-host-portability-root] dialog').open`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-task-portability-root] dialog').open`),false,'Host export does not open the Task export dialog');checks++;
    await evaluate(`document.querySelector('[data-host-portability-root] [data-native-portability-export-form]').requestSubmit()`);
    for(let i=0;i<100&&await evaluate(`document.querySelector('[data-host-portability-root] [data-native-portability-export-error]').hidden`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-host-portability-root] [data-native-portability-export-error]').textContent`),'Export denied for fixture.');checks++;
    assert.deepEqual(await evaluate('actionRequests.at(-1).body.itemIds'),[fixtures['FTP-basic'].host.id]);checks++;
    assert.equal(await evaluate(`Boolean(document.querySelector('[data-host-portability-root] [data-native-portability-export-error]').closest('.settings-footer'))`),true,'Host export errors use the active Export footer');checks++;
    for(const [mode,message] of [['empty','The Host export was empty.'],['network','Unable to reach the export server.']]) {
        await evaluate(`window.hostExportMode=${JSON.stringify(mode)};document.querySelector('[data-host-portability-root] [data-native-portability-export-form]').requestSubmit()`);
        for(let i=0;i<100&&await evaluate(`document.querySelector('[data-host-portability-root] [data-native-portability-export-error]').textContent!==${JSON.stringify(message)}`);i++)await pause(20);
        assert.equal(await evaluate(`document.querySelector('[data-host-portability-root] [data-native-portability-export-error]').textContent`),message);
        assert.equal(await evaluate(`document.querySelector('[data-host-portability-root] dialog').open`),true);
        assert.equal(await evaluate(`document.querySelectorAll('[data-host-editor-dialog] [data-maui-modal-feedback]').length`),0);checks+=3;
    }
    await evaluate(`window.hostExportMode=''`);
    await evaluate(`window.hostExportSuccess=true;window.hostExportDownloads=[];document.addEventListener('click',event=>{if(event.target.matches?.('a[download]')){hostExportDownloads.push(event.target.download);event.preventDefault();}});window.hostBeforeExport=[...document.querySelector('.maui-host-editor-shell').children].map(n=>n.getBoundingClientRect().toJSON());document.querySelector('[data-host-portability-root] [data-native-portability-export-form]').requestSubmit()`);
    for(let i=0;i<100&&await evaluate(`document.querySelector('[data-host-portability-root] dialog').open`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-host-portability-root] dialog').open`),false,'Successful export closes the child dialog');
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dirty-state]').textContent`),'Exported 1 Host.');
    assert.equal(await evaluate(`document.querySelectorAll('[data-host-editor-dialog] [data-maui-modal-feedback]').length`),0,'Successful export adds no banner row');
    assert.deepEqual(await evaluate(`[...document.querySelector('.maui-host-editor-shell').children].map(n=>n.getBoundingClientRect().toJSON())`),await evaluate('hostBeforeExport'));
    assert.deepEqual(await evaluate('hostExportDownloads'),['Host.maui']);checks+=5;
    await evaluate(`document.querySelector('[data-host-editor-clone]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-clone-host-dialog]').open`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dialog]').open`),true);checks++;
    assert.equal(await evaluate('nestedReturns'),0);checks++;
    await evaluate(`document.querySelector('[data-clone-host-cancel]').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-dialog]').open`),true,'Cancel Clone returns to the Host editor');checks++;
    assert.equal(await evaluate(`leaseCalls.filter(c=>c.url.endsWith('/Close')).length`),0,'Cancelling actions retains the source checkout');checks++;
    await evaluate(`document.querySelector('[data-host-editor-clone]').click();window.cloneFailure=true;document.querySelector('[data-clone-host-form]').requestSubmit()`);
    for(let i=0;i<100&&await evaluate(`document.querySelector('[data-clone-host-error]').hidden`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-clone-host-error]').textContent`),'Name already exists.');checks++;
    await evaluate(`window.cloneFailure=false;document.querySelector('[data-clone-host-form]').requestSubmit()`);
    for(let i=0;i<100&&await evaluate(`editor.getCurrent()?.host?.id!=='900'`);i++)await pause(20);
    assert.equal(await evaluate(`editor.getCurrent().host.id`),'900');checks++;
    assert.equal(await evaluate('nestedReturns'),0,'Opening the clone preserves Task return context');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-host-editor-delete]').disabled`),false,'Unused clone can enter live Delete preflight');checks++;
    await evaluate(`document.querySelector('[data-host-editor-delete]').click()`);
    for(let i=0;i<100&&await evaluate(`document.querySelector('[data-delete-host-error]').hidden`);i++)await pause(20);
    assert.equal(await evaluate(`document.querySelector('[data-delete-host-dialog]').open`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-delete-host-yes]').disabled`),true,'Live dependency blocker cannot be confirmed');checks++;
    assert.match(await evaluate(`document.querySelector('[data-delete-host-error]').textContent`),/used by a Task/);checks++;
    await evaluate(`document.querySelector('[data-delete-host-no]').click()`);
    for(let i=0;i<100&&!await evaluate('nestedReturns');i++)await pause(20);
    assert.equal(await evaluate('nestedReturns'),1,'Closing the Host returns to Task exactly once');checks++;
    assert.equal(await evaluate('taskDraft'),'unchanged Task draft');checks++;
    assert.deepEqual(await evaluate('layoutErrors'),[],'Nested actions have no console errors');checks++;
    checks+=informationChecks;
    const report = { result: 'PASS', profiles: new Set(Object.values(fixtures).map(fixture => fixture.host.typeCode)).size, fixtures: Object.keys(fixtures).length, layoutChecks: checks, widths: [1400, 1024, 800, 600], themes: ['light', 'dark'] };
    fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
    console.log('Host Editor rendered layout regression: ' + JSON.stringify(report));
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => {
    socket?.close();
    if (browser?.pid) {
        if (process.platform === 'win32') spawnSync('taskkill', ['/pid', String(browser.pid), '/T', '/F'], { stdio: 'ignore' });
        else browser.kill('SIGTERM');
    }
    server?.close();
    for (const task of pending.values()) { clearTimeout(task.timer); task.reject(new Error('Browser closed')); }
    try { fs.rmSync(profile, { recursive: true, force: true }); } catch { /* Browser may still be releasing its temporary files. */ }
});
