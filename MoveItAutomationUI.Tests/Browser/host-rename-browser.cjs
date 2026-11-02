// Production Rename Host modal, shared name formatting, footer feedback and request flow in Edge; no native transfer calls.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn}=require('node:child_process');
const stopBrowser=require('./rename-browser-cleanup.cjs');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8').replace(/\r\n/g,'\n');
const output=path.join(root,'TestResults/HostRename');fs.mkdirSync(output,{recursive:true});
const candidates=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium'];
const executable=candidates.find(file=>file&&fs.existsSync(file));
const inline=file=>'<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const view=read('Views/Hosts/Index.cshtml'),source=read('wwwroot/js/hosts.js');
const modalStart=view.indexOf('<dialog class="new-task-dialog" data-rename-host-dialog');
const modal=view.slice(modalStart,view.indexOf('</dialog>',modalStart)+9)
 .replace('@(Model.AutoFormatNewTaskHostNames ? "checked" : null)','checked');
assert.ok(modalStart>0&&!modal.includes('@('),'production Rename markup rendered');
assert.match(view,/data-host-row-command="export"[\s\S]*?<\/button>\s*<button[^>]+data-host-row-command="rename"/,'Rename immediately follows Export');
const part=(start,end)=>{const i=source.indexOf(start),j=source.indexOf(end,i);assert.ok(i>=0&&j>i,start);return source.slice(i,j);};
const menu=view.slice(view.indexOf('<div class="task-row-action-menu" data-host-row-action-menu'),modalStart);
const code=[part('    function endpoint(', '    // Purpose: Navigates'),part('    async function fetchJson(', '    // Sends multipart'),
 part('    function applyHostNameFormat(', '    // Rename Host:'),part('    // Rename Host:','    // End Rename Host.'),
 part('    const hostRowActionMenu =', '    // Purpose: Copies a Host name'),
 part('    function handleHostRowCommand(', '    // ---------------------------------------------------------------------\n    // Host connection testing'),
 part('    // Rename Host events.', '    // End Rename Host events.')].join('\n');
const html=`<!doctype html><html data-theme="light"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}body{min-width:0}</style></head><body><button id="row" data-row-id="0" data-host-name="Local File System" data-host-type-code="FileSystem" data-host-type="Local File System">Row</button>${menu}${modal}
${inline('wwwroot/js/modal-ui.js')}${inline('wwwroot/js/shared/rename-result.js')}<script>
window.errors=[];window.requests=[];window.refreshes=[];window.undo=[];window.notices=[];window.mode='success';window.hold=false;window.held=[];
window.addEventListener('error',e=>errors.push(e.message));window.addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
const config={dataset:{renameHostUrl:'/maui/Hosts/RenameHost'}},antiForgeryToken='test-token';
function setHostUndoAvailable(id,value){undo.push({id,value});}
async function refreshHostGrid(force){refreshes.push(force);if(window.refreshFails)throw new Error('Refresh failed');}
function showStatus(message){notices.push(message);}
window.nativeNames={};
window.fetch=async(url,options)=>{const body=JSON.parse(options.body);requests.push({url,body,method:options.method,token:options.headers.get('RequestVerificationToken')});if(hold)await new Promise(resolve=>held.push(resolve));
 const ack={success:true,hostId:body.hostId,hostName:body.name};
 if(mode==='network-after-save'){nativeNames[body.hostId]=body.name;throw new TypeError('Simulated lost response');}
 const payload=mode==='success'?ack:mode==='missing-ack'?{success:true}:mode==='wrong-id'?{...ack,hostId:'999'}:mode==='wrong-name'?{...ack,hostName:'Different'}:mode==='success-false'?{...ack,success:false}:mode==='duplicate'?{message:'A Host named Taken already exists.'}:mode==='partial'?{message:'Inspect the Host before retrying.',outcomeUnknown:true,moveItSaved:true}:mode==='conflict'?{message:'The Host name changed in MOVEit.',editConflict:true}:mode==='audit'?{message:'Host was renamed; audit update failed.',moveItSaved:true}:mode==='prewrite'?{message:'Undo capture failed.',moveItSaved:false,outcomeUnknown:false}:mode==='server'?{message:'Gateway unavailable.'}:{message:'x'.repeat(500)};
 const success=['success','missing-ack','wrong-id','wrong-name','success-false','non-json','bad-json'].includes(mode);
 return{ok:success,status:success?200:mode==='prewrite'?500:mode==='timeout'?408:mode==='server'?502:409,headers:new Headers({'content-type':mode==='non-json'?'text/html':'application/json'}),json:async()=>{if(mode==='bad-json')throw new SyntaxError('Simulated invalid JSON');return payload;}};};
${code}
window.openHost=(id='1',name='Current name',type='FTP')=>{activeHostMenuContext={id,name,type};handleHostRowCommand('rename');};
window.typeName=value=>{renameHostName.value=value;renameHostName.dispatchEvent(new Event('input',{bubbles:true}));};
window.toggleFormat=value=>{renameHostAutoFormat.checked=value;renameHostAutoFormat.dispatchEvent(new Event('change',{bubbles:true}));};
window.send=()=>renameHostForm.dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}));window.ready=true;
</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
if(process.argv.includes('--syntax-only')){console.log('PASS Rename Host browser fixture syntax');process.exit(0);}
assert.ok(executable,'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-host-rename-'));
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
        const message = JSON.parse(event.data), host = pending.get(message.id);
        if (!host) return;
        pending.delete(message.id); clearTimeout(host.timer);
        if (message.error) host.reject(new Error(JSON.stringify(message.error))); else host.resolve(message.result);
    });
    await command('Page.enable');
    await command('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/` });



    async function until(expression,label){for(let i=0;i<120;i++){if(await evaluate(expression))return;await pause(30);}throw new Error('Timed out: '+label+'; '+JSON.stringify(await evaluate('({errors,requests})')));}
    await until('window.ready','Rename controls ready');
    let checks=0;const check=async(expression,expected,label)=>{assert.deepEqual(await evaluate(expression),expected,label);checks++;};
    await evaluate(`openHostRowActionMenu(document.getElementById('row'))`);
    await check('hostRowRenameCommand.disabled',true,'Local File System rename disabled');
    await evaluate(`closeHostRowActionMenu();document.getElementById('row').dataset.rowId='42';document.getElementById('row').dataset.hostTypeCode='FTP';document.getElementById('row').dataset.hostType='FTP';openHostRowActionMenu(document.getElementById('row'))`);
    await check('hostRowRenameCommand.disabled',false,'ordinary Host rename enabled after menu reset');
    await evaluate('closeHostRowActionMenu()');
    for(const type of ['Share','siLock','FTP','SSHFTP','POP3','SMTP','S3','AzureBlob','SharePoint','GoogleStorage','OneDrive','AS2']){
        await evaluate(`mode='success';openHost('1','Current name','${type}')`);
        await check('renameHostDialog.open&&renameHostName.value===\'CURRENT_NAME\'&&renameHostAutoFormat.checked&&!renameHostSubmit.disabled',true,'configured formatting applies immediately and enables Rename');
        await evaluate(`typeName('new host-name')`);await check('renameHostName.value','NEW_HOST-NAME','same formatting as New Host');
        await evaluate(`toggleFormat(false);typeName('New host');hold=true;send();send()`);
        await until('renameHostBusy','rename pending');
        await check('renameHostSubmit.disabled&&renameHostCancel.disabled&&renameHostClose.disabled&&renameHostName.readOnly&&renameHostAutoFormat.disabled&&!renameHostSpinner.hidden',true,'all write and close controls locked');
        await check(`(()=>{const e=new Event('cancel',{cancelable:true});renameHostDialog.dispatchEvent(e);return e.defaultPrevented;})()`,true,'Escape cannot close pending write');
        await check('requests.at(-1)',{url:'/maui/Hosts/RenameHost',body:{hostId:'1',name:'New host',originalName:'Current name'},method:'POST',token:'test-token'},'selected host, original name and antiforgery');
        await evaluate('hold=false;held.splice(0).forEach(resolve=>resolve())');
        await until('!renameHostDialog.open&&!renameHostBusy','rename completes');
    }
    await check('requests.length',12,'double submits produce one request per rename');
    await check('refreshes',Array(12).fill(true),'success invalidates grid search data');
    await check('undo',Array.from({length:12},()=>({id:'1',value:true})),'verified renames enable Undo');
    await evaluate(`openHost();renameHostCancel.click()`);await until('!renameHostDialog.open','cancel');await check('requests.length',12,'cancel never writes');
    await evaluate(`openHost();toggleFormat(false);typeName('   ');send()`);await check('requests.length',12,'blank name rejected locally');
    await check(`!document.querySelector('[data-rename-host-error]').hidden`,true,'validation error visible');
    await evaluate(`typeName('x'.repeat(255));send()`);await check('requests.length',12,'long name rejected locally');
    await evaluate(`typeName('Taken');mode='duplicate';send()`);await until('!renameHostBusy','duplicate response');
    await check(`renameHostDialog.open&&renameHostName.value==='Taken'&&!renameHostSubmit.disabled&&document.querySelector('[data-rename-host-error]').textContent.includes('already exists')`,true,'duplicate stays in modal for correction');
    await check(`document.querySelector('[data-rename-host-error]').parentElement.classList.contains('settings-footer')`,true,'error in footer');
    await evaluate(`mode='success';typeName('Corrected');send()`);await until('!renameHostDialog.open&&!renameHostBusy','correction saved');
    for(const mode of ['partial','conflict','audit']){
        await evaluate(`openHost();toggleFormat(false);typeName('Changed');window.mode='${mode}';send()`);await until('!renameHostBusy','blocked result');
        await check('renameHostDialog.open&&renameHostRequiresReload&&renameHostSubmit.disabled&&!renameHostCancel.disabled',true,'uncertain/saved/conflicting outcome requires reload');
        const count=await evaluate('requests.length');await evaluate(`typeName('Another');send()`);await check('requests.length',count,'blocked outcome cannot repeat rename');
        await evaluate('renameHostCancel.click()');await until('!renameHostDialog.open','close outcome');
    }
    // A late request belongs to its opening, even if code closes the dialog and opens another Host.
    await evaluate(`mode='success';openHost();typeName('Late');hold=true;send();renameHostDialog.close()`);await until('!renameHostBusy','forced close reset');
    await evaluate(`openHost('2','Another Host');hold=false;held.splice(0).forEach(resolve=>resolve())`);await until('refreshes.length===17','late request refresh');
    await check(`renameHostDialog.open&&renameHostName.value==='ANOTHER_HOST'`,true,'late response leaves new opening alone');
    await evaluate('renameHostCancel.click()');await until('!renameHostDialog.open','close late session');
    await evaluate(`openHost();typeName('Refresh Failure');mode='success';refreshFails=true;send()`);await until('!renameHostDialog.open&&notices.length===1','refresh failure after rename');
    await check('renameHostRequiresReload',false,'grid failure cannot create a retryable save error');
    await evaluate('refreshFails=false');
    for(const theme of ['light','dark'])for(const width of [1400,800,400]){
        await command('Emulation.setDeviceMetricsOverride',{width,height:700,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme='${theme}';openHost();toggleFormat(false);typeName('Taken');mode='long';send()`);await until('!renameHostBusy','render error');
        await check(`(()=>{const box=renameHostDialog.getBoundingClientRect();return box.left>=0&&box.right<=innerWidth&&renameHostDialog.scrollWidth<=renameHostDialog.clientWidth;})()`,true,'modal fits viewport');
        await check(`(()=>{const error=document.querySelector('[data-rename-host-error]').getBoundingClientRect(),cancel=renameHostCancel.getBoundingClientRect();return error.bottom<=cancel.top+1||error.right<=cancel.left;})()`,true,'footer feedback does not cover actions');
        await check(`document.querySelector('[data-rename-host-error]').textContent.length<=200`,true,'long footer diagnostics use common summary');
        await screenshot(theme+'-'+width);await evaluate('renameHostCancel.click()');await until('!renameHostDialog.open','layout close');
    }
    // The rendered checkbox's defaultChecked state comes from Auto Format New Task/Host Names.
    for(const type of ['Share','siLock','FTP','SSHFTP','POP3','SMTP','S3','AzureBlob','SharePoint','GoogleStorage','OneDrive','AS2']){
        const count=await evaluate('requests.length');
        await evaluate(`renameHostAutoFormat.defaultChecked=false;openHost('2','Another Host','${type}')`);
        await check(`renameHostName.value==='Another Host'&&!renameHostAutoFormat.checked&&renameHostSubmit.disabled`,true,'disabled configuration preserves existing name on open');
        await evaluate(`toggleFormat(true)`);await check('renameHostName.value','ANOTHER_HOST','manual opt-in still formats');
        await evaluate('renameHostCancel.click()');await until('!renameHostDialog.open','close manual formatting');
        await evaluate(`openHost('2','Another Host','${type}')`);
        await check(`renameHostName.value==='Another Host'&&!renameHostAutoFormat.checked`,true,'reopening restores configured default');
        await evaluate('renameHostCancel.click()');await until('!renameHostDialog.open','close disabled setting');
        await check('requests.length',count,'opening and canceling never rename');
        await evaluate(`renameHostAutoFormat.defaultChecked=true;mode='success';openHost('2','Another Host','${type}')`);
        await check(`renameHostName.value==='ANOTHER_HOST'&&renameHostAutoFormat.checked&&!renameHostSubmit.disabled`,true,'enabled configuration formats without toggling');
        await evaluate('send()');await until('!renameHostDialog.open&&!renameHostBusy','save initially formatted name');
        await check('requests.at(-1).body',{hostId:'2',name:'ANOTHER_HOST',originalName:'Another Host'},'save retains original native name for concurrency validation');
    }
    await evaluate(`openHost('3','ALREADY_FORMATTED')`);
    await check(`renameHostName.value==='ALREADY_FORMATTED'&&renameHostSubmit.disabled`,true,'already formatted name remains unchanged');
    await evaluate('renameHostCancel.click()');await until('!renameHostDialog.open','close unchanged formatted name');
    for(const mode of ['missing-ack','wrong-id','wrong-name','success-false','non-json','bad-json','timeout','server','network-after-save']){
        await evaluate(`window.mode='${mode}';openHost('42','Before');toggleFormat(false);typeName('After');window.qualityRequestCount=requests.length;window.qualityUndoCount=undo.length;send()`);
        await until('!renameHostBusy','ambiguous response handled');
        await check('renameHostDialog.open&&renameHostRequiresReload&&renameHostSubmit.disabled&&!renameHostCancel.disabled',true,'ambiguous result stays open and blocks repeat');
        await check(`document.querySelector('[data-rename-host-error]').textContent.includes('Close this dialog')`,true,'ambiguous result gives reload guidance in footer');
        await check('undo.length===qualityUndoCount',true,'unconfirmed result cannot enable Undo');
        await evaluate(`typeName('Another');send()`);await check('requests.length===qualityRequestCount+1',true,'ambiguous result never resubmits');
        if(mode==='network-after-save')await check(`nativeNames['42']`,'After','response loss may follow a successful native rename');
        await evaluate('renameHostCancel.click()');await until('!renameHostDialog.open','close ambiguous outcome');
    }
    await evaluate(`mode='prewrite';openHost('42','Before');toggleFormat(false);typeName('After');send()`);await until('!renameHostBusy','known prewrite failure');
    await check('renameHostDialog.open&&!renameHostRequiresReload&&!renameHostSubmit.disabled',true,'confirmed failure before write permits correction');
    await evaluate(`mode='success';send()`);await until('!renameHostDialog.open&&!renameHostBusy','retry known prewrite failure succeeds');
    await check('errors',[],'no browser errors');
    const result={result:'PASS',checks};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Rename Host modal: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({ready:window.ready,errors,requests})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();stopBrowser(browser);
 server?.close();for(const host of pending.values()){clearTimeout(host.timer);host.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});

