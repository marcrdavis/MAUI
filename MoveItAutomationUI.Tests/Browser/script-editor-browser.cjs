// Production Script modal, routing and checkout runtime exercised in an actual browser.
'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), os = require('node:os'), http = require('node:http');
const { spawn, spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..'), read = file => fs.readFileSync(path.join(root,file),'utf8');
const output = path.join(root,'TestResults/ScriptEditor'); fs.mkdirSync(output,{recursive:true});
const candidates = [process.env.MAUI_BROWSER_EXECUTABLE, process.env['ProgramFiles(x86)'] && path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'), process.env.ProgramFiles && path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium','/usr/bin/chromium-browser'];
const executable = candidates.find(file=>file&&fs.existsSync(file)); assert.ok(executable,'Edge or Chromium is required');
const groupField = read('Views/Shared/_ObjectCreationResourceGroup.cshtml').match(/<label[\s\S]*<\/label>/)[0].replace('@Model','Script');
const markup = read('Views/Shared/_ScriptEditor.cshtml').replace(/@\*[\s\S]*?\*@/g,'').replace(/@Url\.Action\("([^"\n]+)", "(ScriptEditor|Resources)"\)/g,'/maui/$2/$1').replace('<partial name="_ObjectCreationResourceGroup" model="@("Script")" />','<!--script-creation-group-->');
assert.ok(!/@Url/.test(markup));
// Render the production New Task/Host headers using the same shared icon partial.
const creationHeaders = ['Task','Host'].map(type => {
    const key=type.toLowerCase(), view=read(type==='Host'?'Views/Shared/_NewHostDialog.cshtml':'Views/Tasks/Index.cshtml');
    const start=view.indexOf('<dialog class="new-task-dialog" data-new-'+key+'-dialog>');
    const end=view.indexOf('<div class="new-task-body">',start);
    const iconKey=type==='Task'?'tasks':'host', iconSource=read('Views/Shared/_Icon.cshtml');
    const iconStart=iconSource.indexOf('icon == "'+iconKey+'"'), svgStart=iconSource.indexOf('<svg',iconStart), svgEnd=iconSource.indexOf('</svg>',svgStart)+6;
    const header=view.slice(start,end).replace(/<partial name="_Icon" model="@\("[^"]+"\)" \/>/,iconSource.slice(svgStart,svgEnd));
    assert.ok(start>=0&&end>start&&!header.includes('<partial'));
    return header+'<div class="new-task-body"><label class="field-block"><span>'+type+' Name</span><input class="text-input" /></label></div><div class="settings-footer"><button type="button" class="secondary-button">Close</button></div></form></dialog>';
}).join('');
// Use the actual Settings cards so header branding and Load controls cannot drift.
const settings = read('Views/Configuration/Settings.cshtml');
const settingsCards = ['script','notification'].map(kind => {
    const start = settings.indexOf('<div class="panel-card configuration-card" data-' + kind + '-template-settings>');
    const end = settings.indexOf('\n            </div>', start) + '\n            </div>'.length;
    assert.ok(start >= 0 && end > start);
    return settings.slice(start, end);
}).join('');
const inline = file => '<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const html = `<!doctype html><html data-can-save-templates="true" data-moveit-admin="true" data-use-maui-script-editor="true" data-object-edit-lease-open-url="/maui/ObjectEditLease/Open" data-object-edit-lease-close-url="/maui/ObjectEditLease/Close" data-object-edit-lease-override-url="/maui/ObjectEditLease/Override" data-object-edit-lease-heartbeat-url="/maui/ObjectEditLease/Heartbeat"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}\n${read('wwwroot/css/script-editor.css')}</style></head><body>
<form class="maui-editor-routing-antiforgery"><input name="__RequestVerificationToken" value="token"></form>
<dialog id="parent"><form><h2>Task editor retained</h2><input id="parent-draft" value="unsaved task"></form></dialog>
${settingsCards}${markup}${creationHeaders}
<script>
window.errors=[];window.addEventListener('error',event=>errors.push(event.message)); window.addEventListener('unhandledrejection',event=>errors.push(String(event.reason)));
window.mauiConfirm=async()=>true; window.requests=[]; window.confirmation='normal'; window.duration=true; window.deferSave=false;window.rejectSave=false;window.stale=false;window.acquire=0;window.release=0;
window.permissionReadOnly=false;window.builtIn=false;window.hasUndo=true;window.scriptPreferences={autoSave:false};window.savedMetadata={Owner:'Operations'};window.blockDelete=false;window.recycleFail=false;window.downloads=[];window.openedTask=null;window.saveOutage=false;
window.saved={id:'42',name:'Example',description:'Description',language:'PS1',source:'# Original',editVersion:'r1',resourceGroups:'Operations, Production'};
window.templates=[{id:'basic',name:'Download Basic Auth',source:'# Template\\nWrite-Output "[OrigName]"'}];
window.fetch=async (url,options={})=>{
 const target=new URL(url,location.href), body=options.body?JSON.parse(options.body):null; requests.push({path:target.pathname,body,headers:options.headers});
 const reply=(data,status=200)=>({ok:status===200,status,json:async()=>data});
 if(target.pathname.endsWith('/Templates'))return reply({success:true,templates});
 if(target.pathname.endsWith('/SaveTemplate')){ if(body.name==='Duplicate'&&!body.overwriteExisting)return reply({message:'Duplicate template name'},400); const item={id:body.templateId||'new-template',name:body.name,source:body.source};templates=templates.filter(x=>x.id!==item.id);templates.push(item);return reply({success:true,template:item}); }
 if(target.pathname.endsWith('/DeleteTemplate')){templates=templates.filter(x=>x.id!==body.templateId);return reply({success:true});}
 if(target.pathname.endsWith('/Shell'))return reply({success:true,script:saved,canUndo:!builtIn&&hasUndo,readOnly:builtIn,permissionReadOnly,checkoutObjectsForDurationOfEdit:duration,metadata:savedMetadata,fields:[{name:'Owner',label:'Owner',type:'Text'},{name:'Reviewed',label:'Reviewed',type:'Boolean'},{name:'Notes',label:'Notes',type:'MultilineText'}],audit:{createdBy:'Marc',modifiedBy:'Marc'},tasks:[{id:'123',name:'Script Task'}],preferences:scriptPreferences});
 if(target.pathname.endsWith('/SaveMetadata')){ if(rejectSave)return reply({message:'Required Owner',moveItSaved:false},400);savedMetadata=body.metadata;return reply({success:true,metadata:savedMetadata,audit:{modifiedBy:'Marc'}}); }
 if(target.pathname.endsWith('/SavePreferences')){scriptPreferences=body;return reply({success:true,preferences:scriptPreferences});}
 if(target.pathname.endsWith('/PreflightUndoScript'))return reply({success:true,snapshots:[{snapshotId:'before-open',snapshotCreatedUtc:'2025-01-01T12:00:00Z',snapshotCreatedBy:'Marc',canRestore:true},{snapshotId:'previous',snapshotCreatedUtc:'2026-10-17T12:00:00Z',snapshotCreatedBy:'Marc',canRestore:true}]});
 if(target.pathname.endsWith('/UndoScript')){saved={...saved,source:'# Restored',editVersion:'restored'};return reply({success:true});}
 if(target.pathname.endsWith('/PreflightDeleteScript'))return reply(blockDelete?{success:false,message:'Script is referenced by a Task'}:{success:true});
 if(target.pathname.endsWith('/DeleteScript'))return reply(recycleFail&&!body.deleteWithoutRecycle?{code:'RecycleCaptureFailed',message:'Recovery copy failed'}:{success:true},recycleFail&&!body.deleteWithoutRecycle?409:200);
 if(target.pathname.endsWith('/ExportScript')){
  if(window.exportMode==='http')return reply({message:'Export denied.'},403);
  if(window.exportMode==='plain')return {ok:false,status:502,json:async()=>{throw new Error('Not JSON');}};
  if(window.exportMode==='network')throw new Error('Unable to reach the export server.');
  return {ok:true,status:200,headers:new Headers({'content-disposition':'attachment; filename="Example.zip"'}),blob:async()=>new Blob(window.exportMode==='empty'?[]:['zip'])};
 }
 if(target.pathname.endsWith('/Open')){acquire++;return {ok:true,status:200,json:async()=>({required:true,leaseId:'lease-'+acquire})};}
 if(target.pathname.endsWith('/Close')){release++;return reply({success:true});}
 if(target.pathname.endsWith('/Heartbeat'))return reply({success:true});
 if(target.pathname.endsWith('/Override'))return reply({success:true,leaseId:'recovered',editVersion:'r2'});
 if(target.pathname.endsWith('/Save')){
  if(deferSave)await new Promise(resolve=>window.completeSave=resolve);
  if(saveOutage)return reply({message:'Unexpected server failure'},500);
  if(stale)return reply({message:'Concurrent changes',editConflict:true},409);
  if(rejectSave)return reply({message:'Inspect saved Script',scriptId:'42',moveItSaved:true},409);
  const nativeChanged=target.searchParams.has('id')&&['name','description','language','source'].some(key=>JSON.stringify(body[key])!==JSON.stringify(saved[key]));hasUndo=hasUndo||nativeChanged;
  savedMetadata=body.metadata;saved={...saved,...body,id:'42',editVersion:'r2'};
  return reply(confirmation==='incomplete'?{success:true}:{success:true,script:saved,canUndo:hasUndo,undoSnapshotId:nativeChanged?'previous':'',metadata:savedMetadata,audit:{modifiedBy:"Marc"},checkoutObjectsForDurationOfEdit:duration});
 }
 throw new Error('Unexpected endpoint '+target.pathname);
};
</script>${inline('wwwroot/js/modal-ui.js')}${inline('wwwroot/js/editors/object-edit-lease.js')}${inline('wwwroot/js/editors/editor-routing.js')}${inline('wwwroot/js/editors/script-editor.js')}
<script>window.MauiEditorRouting.openTask=async(id,name)=>{window.openedTask={id,name};};HTMLAnchorElement.prototype.click=function(){window.downloads.push(this.download);};window.pick=name=>document.querySelector('[data-script-editor-'+name+']');window.input=(name,value)=>{pick(name).value=value;pick(name).dispatchEvent(new Event('input',{bubbles:true}));};window.submit=()=>pick('form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));</script>
</body></html>`;
for (const [, script] of html.matchAll(/<script>([\s\S]*?)<\/script>/g)) new (require('node:vm').Script)(script);
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-script-layout-'));
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
    server = http.createServer((_request, response) => {
        const ordinary = _request.url.includes('templates=denied');
        let page = html.replace('<!--script-creation-group-->', ordinary ? groupField : '');
        if(ordinary) page=page.replace('data-can-save-templates="true"', 'data-can-save-templates="false"').replace('data-moveit-admin="true"', 'data-moveit-admin="false"');
        response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end(page);
    });
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

    for(let i=0;i<100&&!await evaluate('Boolean(window.MauiScriptEditor)');i++)await pause(50);
    assert.equal(await evaluate('Boolean(window.MauiScriptEditor)'),true);
    let checks=0;
    assert.equal(await evaluate(`document.querySelector('[data-script-template-settings] .configuration-card-heading.row-heading [data-script-template-load]').textContent`),'Load Template');checks++;
    assert.equal(await evaluate(`(()=>{const cards=['script','notification'].map(kind=>document.querySelector('[data-'+kind+'-template-settings] .configuration-card-heading'));return cards.every(n=>{const style=getComputedStyle(n);return style.paddingLeft==='20px'&&style.borderBottomWidth==='1px'&&getComputedStyle(n.querySelector('h3')).fontSize==='14px';});})()`),true);checks++;
    await evaluate(`window.loadTemplateFile=(name,contents)=>{const transfer=new DataTransfer();transfer.items.add(new File([contents],name));const input=document.querySelector('[data-script-template-file]');input.files=transfer.files;input.dispatchEvent(new Event('change',{bubbles:true}));};loadTemplateFile('Loaded Template.ps1','# Loaded\\nWrite-Output "[OrigName]"')`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-template-dialog]').open`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-template-title]').textContent`),'Add Script Template');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-template-name]').value`),'Loaded Template');checks++;
    assert.match(await evaluate(`document.querySelector('[data-script-template-source]').value`),/\[OrigName\]/);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-template-save]').textContent`),'Add Template');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-template-save]').disabled`),false,'Loaded data is ready to add without a dummy edit');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-template-overwrite-field]').hidden`),true);checks++;
    await screenshot('Settings-Add-Script-Template');
    await evaluate(`document.querySelector('[data-script-template-form]').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}))`);await pause(70);
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/SaveTemplate')).at(-1).body.templateId`),'');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-template-dialog]').open`),false);checks++;
    await evaluate(`[...document.querySelectorAll('[data-script-template-list] .configuration-notification-template-row')].find(row=>row.textContent.includes('Loaded Template')).querySelector('button').click()`);
    assert.equal(await evaluate(`document.querySelector('[data-script-template-save]').textContent`),'Update Template');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-template-save]').disabled`),true);checks++;
    await evaluate(`const name=document.querySelector('[data-script-template-name]');name.value='Renamed Loaded';name.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-script-template-form]').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}))`);await pause(70);
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/SaveTemplate')).at(-1).body.templateId`),'new-template');checks++;
    await evaluate(`[...document.querySelectorAll('[data-script-template-list] .configuration-notification-template-row')].find(row=>row.textContent.includes('Renamed Loaded')).querySelector('.danger-button').click()`);await pause(70);
    const templateWrites = await evaluate(`requests.filter(x=>x.path.endsWith('/SaveTemplate')).length`);
    await evaluate(`loadTemplateFile('Utf16.txt',new Uint8Array([255,254,...[..."# UTF16 Ω"].flatMap(c=>[c.charCodeAt(0)&255,c.charCodeAt(0)>>8])]))`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-template-source]').value`),'# UTF16 Ω');checks++;
    await evaluate(`document.querySelector('[data-script-template-close]').click();loadTemplateFile('NotScript.exe','text')`);await pause(70);
    assert.match(await evaluate(`document.querySelector('[data-script-template-settings-error]').textContent`),/\.txt or \.ps1/);checks++;
    await evaluate(`loadTemplateFile('Binary.txt',new Uint8Array([0,1,2]))`);await pause(70);
    assert.match(await evaluate(`document.querySelector('[data-script-template-settings-error]').textContent`),/text Script file/);checks++;
    await evaluate(`loadTemplateFile('Empty.ps1','')`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-template-save]').disabled`),true);checks++;
    await evaluate(`document.querySelector('[data-script-template-close]').click()`);
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/SaveTemplate')).length`),templateWrites,'Cancel, invalid files and empty sources do not write');checks++;

    await evaluate(`document.querySelector('#parent').showModal();await MauiEditorRouting.openScript('42','Example')`);
    assert.equal(await evaluate(`pick('name').value`),'Example');checks++;
    assert.equal(await evaluate(`pick('resource-groups').value`),'Operations, Production');checks++;
    assert.equal(await evaluate(`pick('resource-groups').readOnly`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-editor-action=undo]').disabled`),true,'Opening ignores prior grid history');checks++;
    assert.equal(await evaluate(`document.querySelectorAll('.script-editor-languages .new-task-type-card').length`),2);checks++;
    assert.equal(await evaluate(`(()=>{const cards=[...document.querySelectorAll('.script-editor-languages .new-task-type-card')].map(card=>card.getBoundingClientRect());return Math.abs(cards[0].top-cards[1].top)<2&&cards[1].left>cards[0].right;})()`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('.script-editor-languages .new-task-type-card:has(input:checked) strong').textContent`),'PowerShell');checks++;
    await screenshot('Script-Type-Cards-Edit');
    assert.equal(await evaluate(`pick('save').disabled`),true,'Unchanged editor has no Save');checks++;
    await evaluate(`input('name','');`);assert.equal(await evaluate(`pick('save').disabled`),true,'Empty name is not ready');checks++;
    await evaluate(`input('name','Changed')`);assert.equal(await evaluate(`pick('save').disabled`),false);checks++;
    await evaluate(`input('name','Example')`);assert.equal(await evaluate(`pick('save').disabled`),true,'Reverting restores unchanged state');checks++;
    assert.equal(await evaluate('acquire'),1);checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Shell')).length`),2);checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Shell')).at(-1).headers['X-MAUI-Edit-Lease']`),'lease-1');checks++;
    assert.equal(await evaluate(`pick('tabs').hidden`),false);checks++;
    assert.equal(await evaluate(`pick('name').closest('[data-script-editor-panel]').dataset.scriptEditorPanel`),'properties');checks++;
    assert.equal(await evaluate(`pick('task-count').textContent`),'1');checks++;
    assert.equal(await evaluate(`pick('metadata').querySelector('[data-script-editor-metadata-field="Reviewed"]').value`),'false');checks++;
    assert.equal(await evaluate(`pick('metadata').querySelector('[data-script-editor-metadata-field="Notes"]').classList.contains('textarea-input')`),true);checks++;
    await evaluate(`document.querySelector('[data-script-editor-tab="tasks"]').click();pick('tasks').querySelector('button').click()`);await pause(50);
    assert.deepEqual(await evaluate('openedTask'),{id:'123',name:'Script Task'});checks++;
    await evaluate(`document.querySelector('[data-script-editor-tab="properties"]').click();const control=pick('metadata').querySelector('input');control.value='Support';control.dispatchEvent(new Event('input',{bubbles:true}));submit()`);await pause(70);
    assert.equal(await evaluate('savedMetadata.Owner'),'Support');checks++;
    assert.equal(await evaluate(`[...document.querySelectorAll('[data-script-editor-panel="properties"] h3,[data-script-editor-panel="properties"] h4')].every(n=>getComputedStyle(n).fontSize==='15px')`),true);checks++;
    assert.equal(await evaluate(`[...document.querySelectorAll('[data-script-editor-panel="properties"] .source-badge')].every(n=>{const s=getComputedStyle(n);return s.fontSize==='10px'&&s.lineHeight==='12px'&&s.padding==='3px 8px'&&n.getBoundingClientRect().height===20;})`),true);checks++;
    await screenshot('Script-Properties');
    await evaluate(`document.querySelector('[data-script-editor-tab="script"]').click()`);
    await evaluate(`input('source',${JSON.stringify("$x='C:\\file'\n# [OrigName] literal\n")});deferSave=true;submit()`);await pause(50);
    assert.equal(await evaluate(`pick('save').textContent`),'Saving…');checks++;
    assert.equal(await evaluate(`pick('source').disabled`),true);checks++;
    await evaluate('completeSave();deferSave=false');await pause(80);
    assert.equal(await evaluate(`pick('save').textContent`),'Save');checks++;
    assert.equal(await evaluate(`pick('status').textContent`),'Saved');checks++;
    assert.match(await evaluate('saved.source'),/\[OrigName\]/);checks++;
    await evaluate(`input('name','Retained draft');stale=true;submit()`);await pause(70);
    assert.equal(await evaluate(`pick('save').disabled`),true);checks++;
    assert.equal(await evaluate(`pick('override').hidden`),false);checks++;
    await evaluate(`pick('override').click()`);await pause(70);
    assert.equal(await evaluate(`pick('save').disabled`),false);checks++;
    assert.equal(await evaluate(`pick('name').value`),'Retained draft');checks++;
    await evaluate(`stale=false;submit()`);await pause(70);
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).at(-1).body.editVersion`),'r2');checks++;
    await evaluate(`pick('save-template').click();document.querySelector('[data-script-template-name]').value='Duplicate';document.querySelector('[data-script-template-name]').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-script-template-form]').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}))`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-template-error]').textContent`),'Duplicate template name');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-template-dialog]').open`),true);checks++;
    await evaluate(`document.querySelector('[data-script-template-overwrite]').checked=true;document.querySelector('[data-script-template-overwrite]').dispatchEvent(new Event('change',{bubbles:true}));document.querySelector('[data-script-template-form]').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}))`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-template-dialog]').open`),false);checks++;
    await evaluate(`pick('template').value='basic';pick('template').dispatchEvent(new Event('change',{bubbles:true}))`);
    assert.match(await evaluate(`pick('source').value`),/Template/);checks++;
    await evaluate(`const file=new File(['# imported\\nWrite-Output "[OrigName]"'], 'example.ps1',{type:'text/plain'});const transfer=new DataTransfer();transfer.items.add(file);pick('file').files=transfer.files;pick('file').dispatchEvent(new Event('change',{bubbles:true}))`);await pause(70);
    assert.match(await evaluate(`pick('source').value`),/imported/);checks++;
    await evaluate(`const transfer=new DataTransfer();transfer.items.add(new File(['bad'], 'example.exe'));pick('file').files=transfer.files;pick('file').dispatchEvent(new Event('change',{bubbles:true}))`);await pause(70);
    assert.match(await evaluate(`pick('status').textContent`),/\.txt or \.ps1/);checks++;
    assert.match(await evaluate(`pick('source').value`),/imported/);checks++;
    await evaluate(`document.querySelector('input[name="mauiScriptLanguage"][value="VBScript"]').click()`);
    assert.equal(await evaluate(`pick('save-template').hidden`),true);checks++;
    assert.equal(await evaluate(`pick('template-field').hidden`),true);checks++;
    await evaluate(`pick('close').click()`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('#parent').open`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('#parent-draft').value`),'unsaved task');checks++;
    assert.equal(await evaluate('release'),1);checks++;
    await evaluate(`document.querySelector('#parent').close();await MauiScriptEditor.open();duration=false`);
    assert.equal(await evaluate(`pick('save').textContent`),'Create Script');checks++;
    assert.equal(await evaluate(`(()=>{const cards=[...document.querySelectorAll('.script-editor-languages .new-task-type-card')].map(card=>card.getBoundingClientRect());return Math.abs(cards[0].top-cards[1].top)<2&&cards[1].left>cards[0].right;})()`),true);checks++;
    await screenshot('Script-Type-Cards-New');
    await evaluate(`window.createdEvents=[];window.addEventListener('maui:script-editor-closed',event=>createdEvents.push(event.detail));window.creationShells=requests.filter(x=>x.path.endsWith('/Shell')).length;window.creationAcquires=acquire;input('name','New Script');input('source','# New');deferSave=true;submit()`);await pause(50);
    assert.equal(await evaluate(`pick('save').textContent`),'Creating…');checks++;
    await evaluate(`completeSave();deferSave=false`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-editor-action="undo"]').disabled`),true);checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).at(-1).body.name`),'New Script');checks++;
    assert.equal(await evaluate(`pick('title').textContent`),'New Script');checks++;
    assert.equal(await evaluate(`pick('kicker').hidden`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-editor]').open`),false);checks++;
    assert.deepEqual(await evaluate(`createdEvents`),[{id:'42',changed:true}]);checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Shell')).length`),await evaluate('creationShells'));checks++;
    assert.equal(await evaluate('acquire'),await evaluate('creationAcquires'));checks++;
    await evaluate(`await MauiScriptEditor.open('42')`);
    assert.equal(await evaluate(`document.querySelector('[data-script-editor-action="undo"]').disabled`),true);checks++;
    await evaluate(`const field=pick('metadata').querySelector('[data-script-editor-metadata-field="Owner"]');field.value='New owner';field.dispatchEvent(new Event('input',{bubbles:true}));submit()`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-editor-action="undo"]').disabled`),false,'Metadata is an editor change that can be undone');checks++;
    await evaluate(`input('source','# Second edit');submit()`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-editor-action="undo"]').disabled`),false);checks++;
    assert.equal(await evaluate(`pick('status').textContent`),'Saved');checks++;
    await evaluate(`input('source','# Server failure');saveOutage=true;submit()`);await pause(70);
    assert.equal(await evaluate(`pick('save').disabled`),true);checks++;
    assert.equal(await evaluate(`pick('override').hidden`),false);checks++;
    const uncertainSaveCalls=await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).length`);
    await evaluate(`saveOutage=false;pick('override').click()`);await pause(80);
    assert.equal(await evaluate(`pick('source').value`),'# Server failure');checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).length`),uncertainSaveCalls,'recovery retains the draft without repeating Save');checks++;
    await evaluate(`saveOutage=false;pick('reload').click()`);await pause(70);
    await evaluate(`input('source','# Not confirmed');confirmation='incomplete';submit()`);await pause(70);
    assert.equal(await evaluate(`pick('save').disabled`),true);checks++;
    assert.equal(await evaluate(`pick('override').hidden`),false);checks++;
    assert.equal(await evaluate(`pick('reload').hidden`),false);checks++;
    await evaluate(`confirmation='normal';pick('reload').click()`);await pause(70);
    await evaluate(`pick('actions').click()`);
    assert.equal(await evaluate(`pick('actions').getAttribute('aria-expanded')`),'true');checks++;
    await evaluate(`pick('actions').click()`);
    assert.equal(await evaluate(`pick('actions').getAttribute('aria-expanded')`),'false');checks++;
    await evaluate(`input('source','# Current session change');submit()`);await pause(70);
    await evaluate(`input('source','# Unsaved export draft');document.querySelector('[data-script-editor-action="export"]').click()`);await pause(70);
    assert.deepEqual(await evaluate('downloads'),['Example.zip']);checks++;
    assert.equal(await evaluate(`pick('status').textContent`),'Exported 1 Script.');
    assert.equal(await evaluate(`pick('status').classList.contains('error')`),false);
    assert.equal(await evaluate(`Boolean(pick('status').closest('.settings-footer'))`),true);checks+=3;
    for(const [mode,message] of [['http','Export denied.'],['plain','Unable to export Script (502).'],['empty','The Script export was empty.'],['network','Unable to reach the export server.']]) {
        await evaluate(`window.exportMode=${JSON.stringify(mode)};document.querySelector('[data-script-editor-action="export"]').click()`);await pause(70);
        assert.equal(await evaluate(`pick('status').textContent`),message);
        assert.equal(await evaluate(`pick('status').classList.contains('error')`),true);
        assert.equal(await evaluate(`document.querySelector('[data-script-editor]').open`),true);
        assert.equal(await evaluate(`document.querySelector('[data-script-editor] [data-maui-modal-feedback]')`),null);
        assert.equal(await evaluate('downloads.length'),1);checks+=5;
    }
    await evaluate(`window.exportMode='';document.querySelector('[data-script-editor-action="export"]').click()`);await pause(70);
    assert.equal(await evaluate(`pick('status').textContent`),'Exported 1 Script.');
    assert.equal(await evaluate(`pick('status').classList.contains('error')`),false);
    assert.equal(await evaluate('downloads.length'),2);checks+=3;
    assert.equal(await evaluate(`pick('source').value`),'# Unsaved export draft');checks++;
    await evaluate(`document.querySelector('[data-script-editor-action="undo"]').click()`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-action-dialog]').open`),false,'Undo stays in the editor');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-editor]').open`),true);checks++;
    assert.equal(await evaluate(`pick('source').value`),'# Current session change');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-editor-action="undo"]').disabled`),true,'Undo is consumed');checks++;
    assert.equal(await evaluate(`requests.some(x=>/PreflightUndo|UndoScript/.test(x.path))`),false,'No configuration restore call');checks++;
    await evaluate(`pick('auto-save').checked=true;pick('auto-save').dispatchEvent(new Event('change',{bubbles:true}))`);await pause(70);
    assert.equal(await evaluate('scriptPreferences.autoSave'),true);checks++;
    assert.equal(await evaluate(`pick('save').textContent`),'Close');checks++;
    assert.equal(await evaluate(`pick('save').disabled`),false,'Auto Save Close stays usable');checks++;
    await evaluate(`input('source','# Auto saved');pick('source').blur();pick('auto-save').focus()`);await pause(850);
    assert.equal(await evaluate('saved.source'),'# Auto saved');checks++;
    await evaluate(`stale=true;input('source','# Rejected auto save');pick('auto-save').focus()`);await pause(850);
    assert.equal(await evaluate(`pick('save').disabled`),true);checks++;
    const failedAutoSaves=await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).length`);
    await pause(850);
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).length`),failedAutoSaves);checks++;
    await evaluate(`stale=false;pick('override').click()`);await pause(70);
    assert.equal(await evaluate(`pick('save').textContent`),'Save');checks++;
    await evaluate(`document.querySelector('[data-script-editor-action="export"]').click()`);await pause(70);
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).length`),failedAutoSaves);checks++;
    await evaluate(`submit()`);await pause(70);
    assert.equal(await evaluate(`pick('save').textContent`),'Close');checks++;
    assert.equal(await evaluate(`pick('save').disabled`),false,'Auto Save Close stays usable');checks++;
    await evaluate(`input('source','# Flushed on Close');pick('save').click()`);await pause(100);
    assert.equal(await evaluate('saved.source'),'# Flushed on Close');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-editor]').open`),false);checks++;
    await evaluate(`await MauiScriptEditor.open('42');blockDelete=true;document.querySelector('[data-script-editor-action="delete"]').click()`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-action-confirm]').disabled`),true);checks++;
    assert.match(await evaluate(`document.querySelector('[data-script-action-error]').textContent`),/referenced/);checks++;
    await evaluate(`document.querySelector('[data-script-action-close]').click()`);await pause(70);
    await evaluate(`blockDelete=false;recycleFail=true;document.querySelector('[data-script-editor-action="delete"]').click()`);await pause(70);
    await evaluate(`document.querySelector('[data-script-action-confirm]').click()`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-action-confirm]').textContent`),'Delete Anyway');checks++;
    await evaluate(`document.querySelector('[data-script-action-confirm]').click()`);await pause(70);
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/DeleteScript')).at(-1).body.deleteWithoutRecycle`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-action-dialog]').open`),false);checks++;
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate(`await MauiScriptEditor.open()`);
    assert.equal(await evaluate(`pick('tabs').hidden&&pick('kicker').hidden`),true);checks++;
    assert.equal(await evaluate(`pick('name-label').textContent`),'Script Name');checks++;
    assert.equal(await evaluate(`(()=>{const button=pick('save-template').getBoundingClientRect(),body=document.querySelector('.script-editor-body');return button.bottom<=innerHeight&&button.top>0&&body.scrollHeight<=body.clientHeight+1;})()`),true);checks++;
    await screenshot('New-Script-1920');
    await evaluate(`pick('close').click()`);await pause(70);
    await evaluate(`await MauiScriptEditor.open('42')`);
    for(const theme of ['light','dark'])for(const width of [1400,800,600]){
      await command('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
      const expected=theme==='dark'?['#5aa9f4','#8bc4ff','rgb(17, 21, 34)','rgb(90, 169, 244)']:['#3e8ac7','#1e5a8a','rgb(255, 255, 255)','rgb(62, 138, 199)'];
      let colors;
      // The production button animates its background when the theme changes.
      for(let attempt=0;attempt<50;attempt++) {
        colors=await evaluate(`(()=>{const s=getComputedStyle(document.documentElement),b=getComputedStyle(pick('save'));return [s.getPropertyValue('--accent').trim(),s.getPropertyValue('--accent-strong').trim(),b.color,b.backgroundColor];})()`);
        if(colors.join('|')===expected.join('|'))break;
        await pause(20);
      }
      assert.deepEqual(colors,expected);checks++;
      assert.equal(await evaluate(`(()=>{const r=document.querySelector('[data-script-editor]').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;})()`),true);checks++;
      await screenshot('Script-'+theme+'-'+width);
    }
    assert.deepEqual(await evaluate('errors'),[]);checks++;
    await evaluate(`pick('close').click()`);await pause(70);
    for (const type of ['task','host']) {
        await evaluate(`document.querySelector('[data-new-${type}-dialog]').showModal()`);
        assert.equal(await evaluate(`document.querySelector('[data-new-${type}-dialog] h2').textContent`),'New '+type[0].toUpperCase()+type.slice(1));checks++;
        assert.equal(await evaluate(`document.querySelectorAll('[data-new-${type}-dialog] .script-editor-icon svg').length`),1);checks++;
        assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-new-${type}-dialog] h2')).fontSize`),'21px');checks++;
        await screenshot('New-'+type+'-Header');
        await evaluate(`document.querySelector('[data-new-${type}-dialog]').close()`);
    }
    const priorLeases=await evaluate('acquire'),priorSaves=await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).length`),priorTemplates=await evaluate(`requests.filter(x=>x.path.endsWith('/Templates')).length`);
    await command('Emulation.setDeviceMetricsOverride',{width:1400,height:1000,deviceScaleFactor:1,mobile:false});
    await evaluate(`builtIn=true;duration=true;scriptPreferences={autoSave:false};saved={...saved,name:'Built-In Script',language:'Unknown',editVersion:''};await MauiScriptEditor.open('42')`);
    assert.equal(await evaluate(`pick('name').readOnly&&pick('description').readOnly`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-editor-tab="script"]').hidden`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-editor-tab="properties"]').getAttribute('aria-selected')`),'true');checks++;
    assert.equal(await evaluate(`pick('save').hidden&&document.querySelector('.script-editor-actions-wrap').hidden`),true);checks++;
    assert.equal(await evaluate(`pick('type').textContent`),'Built-In');checks++;
    assert.equal(await evaluate(`pick('status').textContent`),'Read-only: your MOVEit account does not permit editing this Script.');checks++;
    assert.equal(await evaluate(`pick('metadata').querySelector('[data-script-editor-metadata-field="Owner"]').readOnly`),true);checks++;
    assert.equal(await evaluate('acquire'),priorLeases,'Built-in viewing never checks out');checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Templates')).length`),priorTemplates);checks++;
    await evaluate(`submit()`);await pause(70);
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).length`),priorSaves);checks++;
    const builtinMetadataBefore=await evaluate('savedMetadata.Owner');
    await evaluate(`const owner=pick('metadata').querySelector('[data-script-editor-metadata-field="Owner"]');owner.value='Blocked Owner';owner.dispatchEvent(new Event('input',{bubbles:true}));submit()`);await pause(70);
    assert.equal(await evaluate('savedMetadata.Owner'),builtinMetadataBefore,'Built-in metadata is read-only');checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/SaveScriptMetadata')).length`),0,'Read-only editor never attempts metadata save');checks++;
    assert.equal(await evaluate('acquire'),priorLeases);checks++;
    await screenshot('Built-In-Script-Properties');
    await evaluate(`document.querySelector('[data-script-editor-tab="tasks"]').click();pick('tasks').querySelector('button').click()`);await pause(50);
    assert.deepEqual(await evaluate('openedTask'),{id:'123',name:'Script Task'});checks++;
    await evaluate(`pick('close').click()`);await pause(70);
    await evaluate(`builtIn=false;duration=false;saved={...saved,name:'Custom Script',language:'PS1',editVersion:'custom'};await MauiScriptEditor.open('42')`);
    assert.equal(await evaluate(`!pick('name').readOnly&&!pick('description').readOnly&&!pick('save').hidden&&!pick('tab-actions').hidden&&!document.querySelector('[data-script-editor-tab="script"]').hidden`),true,'Read-only state does not bleed into custom Scripts');checks++;
    assert.deepEqual(await evaluate('errors'),[]);checks++;
    await evaluate(`document.querySelector('[data-script-editor]').close();permissionReadOnly=true;builtIn=false;window.readOnlyAcquires=acquire;await MauiScriptEditor.open('42')`);
    assert.equal(await evaluate(`pick('source').readOnly&&pick('name').readOnly&&pick('save').hidden`),true,'Visible custom Script opens read-only');checks++;
    assert.equal(await evaluate('acquire'),await evaluate('readOnlyAcquires'),'Read-only Script never checks out');checks++;
    assert.equal(await evaluate(`getComputedStyle(pick('actions')).display==='none'||getComputedStyle(pick('actions').parentElement.parentElement).display==='none'`),true);checks++;
    assert.equal(await evaluate(`pick('status').textContent`),'Read-only: your MOVEit account does not permit editing this Script.');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-editor-readonly]')===null`),true,'Read-only warning is footer-only');checks++;
    await evaluate(`window.readOnlyConfirms=0;window.mauiConfirm=async()=>{readOnlyConfirms++;return false;};document.querySelector('.script-editor-footer [data-script-editor-close]').click()`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-script-editor]').open`),false);assert.equal(await evaluate('readOnlyConfirms'),0);checks+=2;
    // A normal editor retains template loading and Script editing, but never writes shared templates.
    await command('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/?templates=denied` });
    for(let i=0;i<100&&!await evaluate('Boolean(window.MauiScriptEditor)');i++)await pause(50);
    await evaluate(`duration=false;await MauiScriptEditor.open('42')`);
    assert.equal(await evaluate(`document.querySelector('[data-script-template-load]').disabled`),true);checks++;
    assert.equal(await evaluate(`pick('save-template').disabled`),true);checks++;
    assert.equal(await evaluate(`getComputedStyle(pick('save-template')).opacity`),'0.48');checks++;
    assert.equal(await evaluate(`pick('template').disabled`),false);checks++;
    await evaluate(`pick('template').value='basic';pick('template').dispatchEvent(new Event('change',{bubbles:true}));pick('save-template').dispatchEvent(new Event('click'));document.querySelector('[data-script-template-form]').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))`);
    assert.match(await evaluate(`pick('source').value`),/Template/);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-script-template-dialog]').open`),false);checks++;
    assert.equal(await evaluate(`requests.some(x=>x.path.endsWith('/SaveTemplate'))`),false);checks++;
    assert.equal(await evaluate(`pick('save').disabled`),false,'Script edits remain savable');checks++;
    await evaluate(`pick('close').click()`);await pause(70);
    await evaluate(`await MauiScriptEditor.open()`);
    assert.equal(await evaluate(`pick('save-template').disabled`),true,'New Script template saving is also disabled');checks++;
    assert.equal(await evaluate(`pick('template').disabled`),false);checks++;
    assert.deepEqual(await evaluate('errors'),[]);checks++;
    // Render the real non-admin creation field, which adds a row absent from admin forms.
    await evaluate(`document.querySelector('[data-object-create-group="Script"]').hidden=false;const groups=document.querySelector('[data-object-resource-group]');groups.add(new Option('Operations','Operations'));groups.value='Operations';groups.required=true;`);
    for(const [width,height] of [[1920,1080],[1366,900],[600,1000]]) {
        await command('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
        for(const language of ['PS1','VBScript','PS1']) {
            await evaluate(`document.querySelector('input[name="mauiScriptLanguage"][value="${language}"]').click()`);
            const bounds=await evaluate(`(()=>{const rect=node=>{const r=node.getBoundingClientRect();return {top:r.top,bottom:r.bottom,left:r.left,right:r.right,width:r.width};};return {group:rect(document.querySelector('[data-object-create-group="Script"]')),name:rect(pick('name')),selector:rect(pick('template')),import:rect(pick('import')),save:rect(pick('save-template')),source:rect(pick('source').closest('.script-editor-source-field')),rowHidden:pick('template-row').hidden,toolsInRow:pick('tools').parentElement===pick('template-row'),disabled:pick('save-template').disabled,dialog:rect(document.querySelector('[data-script-editor]'))};})()`);
            assert.ok(bounds.group.width>0&&bounds.group.top<bounds.name.top,'Resource Group is visible above Name');checks++;
            assert.ok(bounds.dialog.top>=0&&bounds.dialog.bottom<=height+1,'Creation modal fits viewport');checks++;
            if(language==='PS1') {
                assert.equal(bounds.toolsInRow,true);assert.equal(bounds.rowHidden,false);checks+=2;
                assert.ok(bounds.import.bottom<=bounds.source.top&&bounds.save.bottom<=bounds.source.top,'PowerShell tools stay above Source');checks++;
                if(width>=1000) {
                    assert.ok(bounds.import.left>=bounds.selector.right+10,'Import sits right of the narrower selector');checks++;
                    assert.ok(Math.abs(bounds.import.bottom-bounds.selector.bottom)<2&&Math.abs(bounds.save.bottom-bounds.selector.bottom)<2,'Template selector and tools share one row');checks++;
                }
                assert.equal(bounds.disabled,true,'Moving the controls preserves template permissions');checks++;
            } else {
                assert.equal(bounds.rowHidden,true);assert.equal(bounds.toolsInRow,false);checks+=2;
                assert.ok(bounds.import.top>=bounds.source.bottom-1,'VBScript keeps Import below Source');checks++;
            }
            await screenshot('New-Script-Group-'+language+'-'+width+'x'+height);
        }
    }
    assert.deepEqual(await evaluate('errors'),[]);checks++;
    const result={result:'PASS',checks,themes:['light','dark'],widths:[1400,800,600]};
    fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Script editor rendered workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({status:pick("status").textContent,errors,requests})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});
