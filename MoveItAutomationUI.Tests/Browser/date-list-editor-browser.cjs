// Exercise the production Date List modal, routing and shared lease runtime in Edge.
'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), os = require('node:os'), http = require('node:http');
const { spawn, spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..'), read = file => fs.readFileSync(path.join(root,file),'utf8');
const output = path.join(root,'TestResults/DateListEditor'); fs.mkdirSync(output,{recursive:true});
const candidates = [process.env.MAUI_BROWSER_EXECUTABLE, process.env['ProgramFiles(x86)'] && path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'), process.env.ProgramFiles && path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium','/usr/bin/chromium-browser'];
const executable = candidates.find(file=>file&&fs.existsSync(file)); assert.ok(executable,'Edge or Chromium is required');
const markup = read('Views/Shared/_DateListEditor.cshtml').replace(/@\*[\s\S]*?\*@/g,'').replace(/@Url\.Action\("([^"\n]+)", "(DateListEditor|Resources)"\)/g,'/maui/$2/$1');
assert.ok(!/@Url/.test(markup));
const inline = file => '<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const html = `<!doctype html><html data-moveit-admin="true" data-use-maui-date-list-editor="true" data-object-edit-lease-open-url="/maui/ObjectEditLease/Open" data-object-edit-lease-close-url="/maui/ObjectEditLease/Close" data-object-edit-lease-override-url="/maui/ObjectEditLease/Override" data-object-edit-lease-heartbeat-url="/maui/ObjectEditLease/Heartbeat"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}\n${read('wwwroot/css/script-editor.css')}</style></head><body>
<form class="maui-editor-routing-antiforgery"><input name="__RequestVerificationToken" value="token"></form>${markup}
<script>
window.errors=[];window.addEventListener('error',event=>errors.push(event.message));window.addEventListener('unhandledrejection',event=>errors.push(String(event.reason)));
window.mauiConfirm=async()=>true;window.requests=[];window.duration=true;window.acquire=0;window.release=0;window.readOnly=false;window.hasUndo=true;window.preferences={autoSave:false};window.metadata={Owner:'Operations'};window.stale=false;window.uncertain=false;window.blockDelete=false;window.recycleFail=false;window.downloads=[];
window.saved={id:'42',name:'Annual Dates',description:'Description',entries:['2028-01-16','2040-*-31'],editVersion:'r1',resourceGroups:'Operations, Production'};
window.fetch=async(url,options={})=>{
 const target=new URL(url,location.href),body=options.body?JSON.parse(options.body):null;requests.push({path:target.pathname,body,headers:options.headers});
 const reply=(data,status=200)=>({ok:status===200,status,json:async()=>data});
 if(target.pathname.endsWith('/Shell'))return reply({success:true,dateList:saved,canUndo:hasUndo,readOnly,checkoutObjectsForDurationOfEdit:!readOnly&&duration,metadata,fields:[{name:'Owner',label:'Owner',type:'Text'}],audit:{createdBy:'Marc'},tasks:[{id:'123',name:'Related Task'}],preferences});
 if(target.pathname.endsWith('/SavePreferences')){preferences=body;return reply({success:true,preferences});}
 if(target.pathname.endsWith('/Open')){acquire++;return reply({required:true,leaseId:'lease-'+acquire});}
 if(target.pathname.endsWith('/Close')){release++;return reply({success:true});}
 if(target.pathname.endsWith('/Heartbeat'))return reply({success:true});
 if(target.pathname.endsWith('/Override'))return reply({success:true,leaseId:'recovered',editVersion:'r2'});
 if(target.pathname.endsWith('/Save')){
  if(stale)return reply({message:'Concurrent changes',editConflict:true},409);
  if(uncertain)return reply({message:'Inspect saved Date List',dateListId:'42',moveItSaved:true},409);
  const nativeChanged=target.searchParams.has('id')&&['name','description','entries'].some(key=>JSON.stringify(body[key])!==JSON.stringify(saved[key]));hasUndo=hasUndo||nativeChanged;
  metadata=body.metadata;saved={...saved,...body,id:'42',editVersion:'r2'};return reply({success:true,dateList:saved,canUndo:hasUndo,undoSnapshotId:nativeChanged?'prior':'',metadata,audit:{modifiedBy:'Marc'}});
 }
 if(target.pathname.endsWith('/PreflightUndoDateList'))return reply({success:true,snapshots:[{snapshotId:'before-open',snapshotCreatedUtc:'2025-01-01T12:00:00Z',snapshotCreatedBy:'Marc',canRestore:true},{snapshotId:'prior',snapshotCreatedUtc:'2026-10-17T12:00:00Z',snapshotCreatedBy:'Marc',canRestore:true}]});
 if(target.pathname.endsWith('/UndoDateList')){saved={...saved,entries:['2030-02-15'],editVersion:'restored'};return reply({success:true});}
 if(target.pathname.endsWith('/PreflightDeleteDateLists'))return reply(blockDelete?{success:false,message:'Referenced by a Task'}:{success:true});
 if(target.pathname.endsWith('/DeleteDateLists'))return reply(recycleFail&&!body.deleteWithoutRecycle?{code:'RecycleCaptureFailed',message:'Recovery copy failed'}:{success:true},recycleFail&&!body.deleteWithoutRecycle?409:200);
 if(target.pathname.endsWith('/ExportDateList')){
  if(window.exportMode==='http')return reply({message:'Export denied.'},403);
  if(window.exportMode==='plain')return {ok:false,status:502,json:async()=>{throw new Error('Not JSON');}};
  if(window.exportMode==='network')throw new Error('Unable to reach the export server.');
  return {ok:true,status:200,headers:new Headers({'content-disposition':'attachment; filename="Dates.zip"'}),blob:async()=>new Blob(window.exportMode==='empty'?[]:['zip'])};
 }
 throw new Error('Unexpected endpoint '+target.pathname);
};
</script>${inline('wwwroot/js/modal-ui.js')}${inline('wwwroot/js/editors/object-edit-lease.js')}${inline('wwwroot/js/editors/editor-routing.js')}${inline('wwwroot/js/editors/date-list-editor.js')}
<script>
window.MauiEditorRouting.openTask=async(id,name)=>{window.openedTask={id,name};};HTMLAnchorElement.prototype.click=function(){downloads.push(this.download);};
window.pick=name=>document.querySelector('[data-date-list-editor-'+name+']');window.input=(name,value)=>{pick(name).value=value;pick(name).dispatchEvent(new Event('input',{bubbles:true}));};window.submit=()=>pick('form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));
window.importText=text=>{const transfer=new DataTransfer();transfer.items.add(new File([text],'dates.txt',{type:'text/plain'}));pick('file').files=transfer.files;pick('file').dispatchEvent(new Event('change',{bubbles:true}));};
</script></body></html>`;
for(const [,script] of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-date-list-layout-'));
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


    for(let i=0;i<100&&!await evaluate('Boolean(window.MauiDateListEditor)');i++)await pause(50);
    assert.equal(await evaluate('Boolean(window.MauiDateListEditor)'),true);let checks=1;
    await evaluate(`await MauiEditorRouting.openDateList('42','Annual Dates')`);
    assert.equal(await evaluate(`pick('name').value`),'Annual Dates');checks++;
    assert.equal(await evaluate(`pick('save').disabled`),true,'Unchanged Date List has no Save');checks++;
    await evaluate(`input('name','')`);assert.equal(await evaluate(`pick('save').disabled`),true);checks++;
    await evaluate(`input('name','Changed')`);assert.equal(await evaluate(`pick('save').disabled`),false);checks++;
    await evaluate(`input('name','Annual Dates')`);assert.equal(await evaluate(`pick('save').disabled`),true);checks++;
    assert.equal(await evaluate(`pick('subtitle').textContent`),'Description · Date List ID: 42');checks++;
    assert.equal(await evaluate(`pick('name-label').textContent`),'Date List Name');checks++;
    assert.equal(await evaluate('acquire'),1);checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Shell')).length`),2);checks++;
    assert.equal(await evaluate(`pick('entries').children.length`),2);checks++;
    assert.equal(await evaluate(`pick('year').options[0].value`),String(new Date().getFullYear()));checks++;
    assert.equal(await evaluate(`pick('year').options[pick('year').options.length-1].value`),'2045');checks++;
    assert.equal(await evaluate(`pick('month').options.length`),13);checks++;
    assert.equal(await evaluate(`pick('day').options.length`),31);checks++;
    await evaluate(`pick('year').value='2028';pick('month').value='02';pick('day').value='30';pick('add').click()`);
    assert.equal(await evaluate(`pick('entries').children.length`),2);checks++;
    assert.match(await evaluate(`pick('status').textContent`),/valid date/);checks++;
    await evaluate(`pick('day').value='29';pick('add').click();pick('add').click()`);
    assert.equal(await evaluate(`pick('entries').children.length`),3);checks++;
    await evaluate('importText('+JSON.stringify('2030-*-15\n2040-*-31\n')+')');await pause(80);
    assert.equal(await evaluate(`pick('entries').children.length`),4);checks++;
    await evaluate('importText('+JSON.stringify('2030-12-15\n2030-02-30\n')+')');await pause(80);
    assert.equal(await evaluate(`pick('entries').children.length`),4,'Import is atomic');checks++;
    assert.match(await evaluate(`pick('status').textContent`),/line 2/);checks++;
    await evaluate(`submit()`);await pause(90);
    assert.deepEqual(await evaluate('saved.entries'),['2028-01-16','2040-*-31','2028-02-29','2030-*-15']);checks++;
    assert.equal(await evaluate(`pick('save').textContent`),'Save');checks++;
    await evaluate(`pick('entries').querySelector('button').click()`);
    assert.equal(await evaluate(`pick('entries').children.length`),3);checks++;
    await evaluate(`submit()`);await pause(80);
    await evaluate(`saved={...saved,description:'Long description '.repeat(80)};pick('close').click()`);await pause(60);
    await evaluate(`await MauiDateListEditor.open('42')`);
    assert.equal(await evaluate(`pick('subtitle').querySelector('.date-list-editor-subtitle-id').textContent`),'Date List ID: 42');checks++;
    assert.equal(await evaluate(`getComputedStyle(pick('subtitle').querySelector('.date-list-editor-subtitle-description')).textOverflow`),'ellipsis');checks++;
    for(const theme of ['light','dark'])for(const width of [1400,800,600]){
      await command('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.documentElement.dataset.theme='${theme}'`);await pause(40);
      assert.equal(await evaluate(`(()=>{const d=document.querySelector('[data-date-list-editor]');return d.getBoundingClientRect().width<=innerWidth&&d.scrollWidth<=d.clientWidth+1})()`),true,'No horizontal modal overflow');checks++;
      if(width===1400)await screenshot('Date-Entries-'+theme);
    }
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate(`document.querySelector('[data-date-list-editor-tab="properties"]').click()`);
    assert.equal(await evaluate(`pick('id').textContent`),'42');checks++;
    assert.equal(await evaluate(`pick('resource-groups').value`),'Operations, Production');checks++;
    assert.equal(await evaluate(`pick('resource-groups').readOnly`),true);checks++;
    await screenshot('Date-List-Properties');
    await evaluate(`document.querySelector('[data-date-list-editor-tab="tasks"]').click();pick('tasks').querySelector('button').click()`);await pause(40);
    assert.deepEqual(await evaluate('openedTask'),{id:'123',name:'Related Task'});checks++;
    await evaluate(`document.querySelector('[data-date-list-editor-tab="properties"]').click();stale=true;input('description','Concurrent draft');submit()`);await pause(80);
    assert.equal(await evaluate(`pick('save').disabled`),true);checks++;
    assert.equal(await evaluate(`pick('override').hidden`),false);checks++;
    await evaluate(`stale=false;pick('override').click()`);await pause(80);
    assert.equal(await evaluate(`pick('save').disabled`),false);checks++;
    await evaluate(`submit()`);await pause(80);
    await evaluate(`uncertain=true;input('description','Uncertain draft');submit()`);await pause(80);
    assert.equal(await evaluate(`pick('save').disabled&&!pick('override').hidden&&!pick('reload').hidden`),true);checks++;
    const uncertainSaveCalls=await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).length`);
    await evaluate(`uncertain=false;pick('override').click()`);await pause(80);
    assert.equal(await evaluate(`pick('description').value`),'Uncertain draft');checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Save')).length`),uncertainSaveCalls,'recovery must not repeat the Date List save');checks++;
    await evaluate(`uncertain=false;pick('reload').click()`);await pause(80);
    await evaluate(`pick('auto-save').checked=true;pick('auto-save').dispatchEvent(new Event('change'))`);await pause(50);
    await evaluate(`input('description','Auto-saved dates');pick('auto-save').focus()`);await pause(900);
    assert.equal(await evaluate('saved.description'),'Auto-saved dates');checks++;
    await evaluate(`pick('auto-save').checked=false;pick('auto-save').dispatchEvent(new Event('change'))`);await pause(50);
    const releasesBeforeClose = await evaluate('release');
    await evaluate(`pick('close').click()`);await pause(80);
    assert.equal(await evaluate('release'),releasesBeforeClose+1);checks++;
    await evaluate(`duration=false;await MauiDateListEditor.open()`);
    assert.equal(await evaluate(`pick('save').textContent`),'Create Date List');checks++;
    assert.equal(await evaluate(`pick('kicker').hidden`),true);checks++;
    assert.equal(await evaluate(`pick('name-label').textContent`),'Date List Name');checks++;
    await screenshot('New-Date-List');
    assert.equal(await evaluate(`(()=>{const r=pick('save').getBoundingClientRect();return r.bottom<=innerHeight})()`),true);checks++;
    await evaluate(`window.createdEvents=[];window.addEventListener('maui:date-list-editor-closed',event=>createdEvents.push(event.detail));window.creationShells=requests.filter(x=>x.path.endsWith('/Shell')).length;window.creationAcquires=acquire;input('name','New Dates');pick('year').value='2040';pick('month').value='01';pick('day').value='16';pick('add').click();submit()`);await pause(90);
    assert.equal(await evaluate(`document.querySelector('[data-date-list-editor-action="undo"]').disabled`),true,'New creation has no fabricated Undo');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-date-list-editor]').open`),false);checks++;
    assert.deepEqual(await evaluate('createdEvents'),[{id:'42',changed:true}]);checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/Shell')).length`),await evaluate('creationShells'));checks++;
    assert.equal(await evaluate('acquire'),await evaluate('creationAcquires'));checks++;
    await evaluate(`await MauiDateListEditor.open('42')`);
    await evaluate(`input('description','Native edit');submit()`);await pause(80);
    assert.equal(await evaluate(`document.querySelector('[data-date-list-editor-action="undo"]').disabled`),false);checks++;
    await evaluate(`document.querySelector('[data-date-list-editor-action="undo"]').click()`);await pause(80);
    assert.equal(await evaluate(`document.querySelector('[data-date-list-editor]').open`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-date-list-action-dialog]').open`),false,'Undo does not open history');checks++;
    assert.equal(await evaluate(`pick('description').value`),'','Only the last description edit is reversed');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-date-list-editor-action="undo"]').disabled`),true);checks++;
    assert.equal(await evaluate(`requests.some(x=>/PreflightUndo|UndoDateList/.test(x.path))`),false);checks++;
    await evaluate(`document.querySelector('[data-date-list-editor-action="export"]').click()`);await pause(60);
    assert.deepEqual(await evaluate('downloads'),['Dates.zip']);checks++;
    assert.equal(await evaluate(`pick('status').textContent`),'Exported 1 Date List.');
    assert.equal(await evaluate(`pick('status').classList.contains('error')`),false);
    assert.equal(await evaluate(`Boolean(pick('status').closest('.settings-footer'))`),true);checks+=3;
    for(const [mode,message] of [['http','Export denied.'],['plain','Unable to export Date List (502).'],['empty','The Date List export was empty.'],['network','Unable to reach the export server.']]) {
        await evaluate(`window.exportMode=${JSON.stringify(mode)};document.querySelector('[data-date-list-editor-action="export"]').click()`);await pause(70);
        assert.equal(await evaluate(`pick('status').textContent`),message);
        assert.equal(await evaluate(`pick('status').classList.contains('error')`),true);
        assert.equal(await evaluate(`document.querySelector('[data-date-list-editor]').open`),true);
        assert.equal(await evaluate(`document.querySelector('[data-date-list-editor] [data-maui-modal-feedback]')`),null);
        assert.equal(await evaluate('downloads.length'),1);checks+=5;
    }
    await evaluate(`window.exportMode='';document.querySelector('[data-date-list-editor-action="export"]').click()`);await pause(70);
    assert.equal(await evaluate(`pick('status').textContent`),'Exported 1 Date List.');
    assert.equal(await evaluate(`pick('status').classList.contains('error')`),false);
    assert.equal(await evaluate('downloads.length'),2);checks+=3;
    await evaluate(`blockDelete=true;document.querySelector('[data-date-list-editor-action="delete"]').click()`);await pause(70);
    assert.match(await evaluate(`document.querySelector('[data-date-list-action-error]').textContent`),/Referenced/);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-date-list-action-confirm]').disabled`),true);checks++;
    await evaluate(`document.querySelector('[data-date-list-action-close]').click()`);await pause(70);
    await evaluate(`blockDelete=false;recycleFail=true;document.querySelector('[data-date-list-editor-action="delete"]').click()`);await pause(70);
    await evaluate(`document.querySelector('[data-date-list-action-confirm]').click()`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-date-list-action-confirm]').textContent`),'Delete Anyway');checks++;
    await evaluate(`document.querySelector('[data-date-list-action-confirm]').click()`);await pause(70);
    assert.deepEqual(await evaluate(`requests.filter(x=>x.path.endsWith('/DeleteDateLists')).map(x=>x.body)`),[{dateListIds:['42'],deleteWithoutRecycle:false},{dateListIds:['42'],deleteWithoutRecycle:true}]);checks++;
    const leasesBeforeReadOnly = await evaluate('acquire');
    await evaluate(`readOnly=true;duration=true;await MauiDateListEditor.open('42')`);
    assert.equal(await evaluate(`pick('save').hidden&&pick('add').disabled&&pick('import').disabled`),true);checks++;
    assert.equal(await evaluate(`pick('entries').querySelector('button').hidden`),true);checks++;
    assert.equal(await evaluate('acquire'),leasesBeforeReadOnly,'Read-only access never checks out');checks++;
    assert.deepEqual(await evaluate('errors'),[]);checks++;
    assert.equal(await evaluate(`pick('status').textContent`),'Read-only: your MOVEit account does not permit editing this Date List.');checks++;
    assert.equal(await evaluate(`document.querySelector('[data-date-list-editor-readonly]')===null`),true,'Read-only warning is footer-only');checks++;
    await evaluate(`window.readOnlyConfirms=0;window.mauiConfirm=async()=>{readOnlyConfirms++;return false;};document.querySelector('.script-editor-footer [data-date-list-editor-close]').click()`);await pause(70);
    assert.equal(await evaluate(`document.querySelector('[data-date-list-editor]').open`),false);assert.equal(await evaluate('readOnlyConfirms'),0);checks+=2;
    const result={result:'PASS',checks,themes:['light','dark'],widths:[1920,1400,800,600]};
    fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Date List editor rendered workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({status:pick("status").textContent,errors,requests})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});
