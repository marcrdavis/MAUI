// Production Rename Task modal, shared name formatting, footer feedback and request flow in Edge; no native transfer calls.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn}=require('node:child_process');
const stopBrowser=require('./rename-browser-cleanup.cjs');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8').replace(/\r\n/g,'\n');
const output=path.join(root,'TestResults/TaskRename');fs.mkdirSync(output,{recursive:true});
const candidates=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium'];
const executable=candidates.find(file=>file&&fs.existsSync(file));
const inline=file=>'<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const view=read('Views/Tasks/Index.cshtml'),source=read('wwwroot/js/tasks.js');
const modalStart=view.indexOf('<dialog class="new-task-dialog" data-rename-task-dialog');
const modal=view.slice(modalStart,view.indexOf('</dialog>',modalStart)+9)
 .replace('@(Model.AutoFormatNewTaskHostNames ? "checked" : null)','checked');
assert.ok(modalStart>0&&!modal.includes('@('),'production Rename markup rendered');
assert.match(view,/data-task-row-command="export"[\s\S]*?<\/button>\s*<button[^>]+data-task-row-command="rename"/,'Rename immediately follows Export');
const part=(start,end)=>{const i=source.indexOf(start),j=source.indexOf(end,i);assert.ok(i>=0&&j>i,start);return source.slice(i,j);};
const code=[part('    function endpoint(', '    // Purpose: Navigates'),part('    async function fetchJson(', '    // Sends multipart'),
 part('    function applyTaskNameFormat(', '    function applyNewTaskNameFormat('),part('    // Rename Task:','    // End Rename Task.'),
 part('    function handleTaskRowCommand(', '    // ---------------------------------------------------------------------\n    // Per-user'),
 part('    // Rename Task events.', '    // End Rename Task events.')].join('\n');
const html=`<!doctype html><html data-theme="light"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}body{min-width:0}</style></head><body>${modal}
${inline('wwwroot/js/modal-ui.js')}${inline('wwwroot/js/shared/rename-result.js')}<script>
window.errors=[];window.requests=[];window.refreshes=[];window.undo=[];window.notices=[];window.mode='success';window.hold=false;window.held=[];
window.addEventListener('error',e=>errors.push(e.message));window.addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
const pageConfig={dataset:{renameTaskUrl:'/maui/Tasks/RenameTask'}},antiForgeryToken='test-token';let activeTaskRowAction=null;
function closeTaskRowActionMenu(){}function setTaskUndoAvailable(id,value){undo.push({id,value});}
async function refreshTaskGrid(force){refreshes.push(force);if(window.refreshFails)throw new Error('Refresh failed');}
function showNativeEditorStatus(message){notices.push(message);}
window.nativeNames={};
window.fetch=async(url,options)=>{const body=JSON.parse(options.body);requests.push({url,body,method:options.method,token:options.headers.get('RequestVerificationToken')});if(hold)await new Promise(resolve=>held.push(resolve));
 const ack={success:true,taskId:body.taskId,taskName:body.name};
 if(mode==='network-after-save'){nativeNames[body.taskId]=body.name;throw new TypeError('Simulated lost response');}
 const payload=mode==='success'?ack:mode==='missing-ack'?{success:true}:mode==='wrong-id'?{...ack,taskId:'999'}:mode==='wrong-name'?{...ack,taskName:'Different'}:mode==='success-false'?{...ack,success:false}:mode==='duplicate'?{message:'A Task named Taken already exists.'}:mode==='partial'?{message:'Inspect the Task before retrying.',outcomeUnknown:true,moveItSaved:true}:mode==='conflict'?{message:'The Task name changed in MOVEit.',editConflict:true}:mode==='audit'?{message:'Task was renamed; audit update failed.',moveItSaved:true}:mode==='prewrite'?{message:'Undo capture failed.',moveItSaved:false,outcomeUnknown:false}:mode==='server'?{message:'Gateway unavailable.'}:{message:'x'.repeat(500)};
 const success=['success','missing-ack','wrong-id','wrong-name','success-false','non-json','bad-json'].includes(mode);
 return{ok:success,status:success?200:mode==='prewrite'?500:mode==='timeout'?408:mode==='server'?502:409,headers:new Headers({'content-type':mode==='non-json'?'text/html':'application/json'}),json:async()=>{if(mode==='bad-json')throw new SyntaxError('Simulated invalid JSON');return payload;}};};
${code}
window.openTask=(id='1',name='Current name',type='Traditional')=>{activeTaskRowAction={taskId:id,taskName:name,taskType:type};handleTaskRowCommand('rename');};
window.typeName=value=>{renameTaskName.value=value;renameTaskName.dispatchEvent(new Event('input',{bubbles:true}));};
window.toggleFormat=value=>{renameTaskAutoFormat.checked=value;renameTaskAutoFormat.dispatchEvent(new Event('change',{bubbles:true}));};
window.send=()=>renameTaskForm.dispatchEvent(new Event('submit',{cancelable:true,bubbles:true}));window.ready=true;
</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
if(process.argv.includes('--syntax-only')){console.log('PASS Rename Task browser fixture syntax');process.exit(0);}
assert.ok(executable,'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-task-rename-'));
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
    await until('window.ready','Rename controls ready');
    let checks=0;const check=async(expression,expected,label)=>{assert.deepEqual(await evaluate(expression),expected,label);checks++;};
    for(const type of ['Traditional','Advanced','Sync']){
        await evaluate(`mode='success';openTask('1','Current name','${type}')`);
        await check('renameTaskDialog.open&&renameTaskName.value===\'CURRENT_NAME\'&&renameTaskAutoFormat.checked&&!renameTaskSubmit.disabled',true,'configured formatting applies immediately and enables Rename');
        await evaluate(`typeName('new task-name')`);await check('renameTaskName.value','NEW_TASK-NAME','same formatting as New Task');
        await evaluate(`toggleFormat(false);typeName('New task');hold=true;send();send()`);
        await until('renameTaskBusy','rename pending');
        await check('renameTaskSubmit.disabled&&renameTaskCancel.disabled&&renameTaskClose.disabled&&renameTaskName.readOnly&&renameTaskAutoFormat.disabled&&!renameTaskSpinner.hidden',true,'all write and close controls locked');
        await check(`(()=>{const e=new Event('cancel',{cancelable:true});renameTaskDialog.dispatchEvent(e);return e.defaultPrevented;})()`,true,'Escape cannot close pending write');
        await check('requests.at(-1)',{url:'/maui/Tasks/RenameTask',body:{taskId:'1',name:'New task',originalName:'Current name'},method:'POST',token:'test-token'},'selected task, original name and antiforgery');
        await evaluate('hold=false;held.splice(0).forEach(resolve=>resolve())');
        await until('!renameTaskDialog.open&&!renameTaskBusy','rename completes');
    }
    await check('requests.length',3,'double submits produce one request per rename');
    await check('refreshes',[true,true,true],'success invalidates grid search data');
    await check('undo',[{id:'1',value:true},{id:'1',value:true},{id:'1',value:true}],'verified renames enable Undo');
    await evaluate(`openTask();renameTaskCancel.click()`);await until('!renameTaskDialog.open','cancel');await check('requests.length',3,'cancel never writes');
    await evaluate(`openTask();toggleFormat(false);typeName('   ');send()`);await check('requests.length',3,'blank name rejected locally');
    await check(`!document.querySelector('[data-rename-task-error]').hidden`,true,'validation error visible');
    await evaluate(`typeName('x'.repeat(255));send()`);await check('requests.length',3,'long name rejected locally');
    await evaluate(`typeName('Taken');mode='duplicate';send()`);await until('!renameTaskBusy','duplicate response');
    await check(`renameTaskDialog.open&&renameTaskName.value==='Taken'&&!renameTaskSubmit.disabled&&document.querySelector('[data-rename-task-error]').textContent.includes('already exists')`,true,'duplicate stays in modal for correction');
    await check(`document.querySelector('[data-rename-task-error]').parentElement.classList.contains('settings-footer')`,true,'error in footer');
    await evaluate(`mode='success';typeName('Corrected');send()`);await until('!renameTaskDialog.open&&!renameTaskBusy','correction saved');
    for(const mode of ['partial','conflict','audit']){
        await evaluate(`openTask();toggleFormat(false);typeName('Changed');window.mode='${mode}';send()`);await until('!renameTaskBusy','blocked result');
        await check('renameTaskDialog.open&&renameTaskRequiresReload&&renameTaskSubmit.disabled&&!renameTaskCancel.disabled',true,'uncertain/saved/conflicting outcome requires reload');
        const count=await evaluate('requests.length');await evaluate(`typeName('Another');send()`);await check('requests.length',count,'blocked outcome cannot repeat rename');
        await evaluate('renameTaskCancel.click()');await until('!renameTaskDialog.open','close outcome');
    }
    // A late request belongs to its opening, even if code closes the dialog and opens another Task.
    await evaluate(`mode='success';openTask();typeName('Late');hold=true;send();renameTaskDialog.close()`);await until('!renameTaskBusy','forced close reset');
    await evaluate(`openTask('2','Another Task');hold=false;held.splice(0).forEach(resolve=>resolve())`);await until('refreshes.length===8','late request refresh');
    await check(`renameTaskDialog.open&&renameTaskName.value==='ANOTHER_TASK'`,true,'late response leaves new opening alone');
    await evaluate('renameTaskCancel.click()');await until('!renameTaskDialog.open','close late session');
    await evaluate(`openTask();typeName('Refresh Failure');mode='success';refreshFails=true;send()`);await until('!renameTaskDialog.open&&notices.length===1','refresh failure after rename');
    await check('renameTaskRequiresReload',false,'grid failure cannot create a retryable save error');
    await evaluate('refreshFails=false');
    for(const theme of ['light','dark'])for(const width of [1400,800,400]){
        await command('Emulation.setDeviceMetricsOverride',{width,height:700,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme='${theme}';openTask();toggleFormat(false);typeName('Taken');mode='long';send()`);await until('!renameTaskBusy','render error');
        await check(`(()=>{const box=renameTaskDialog.getBoundingClientRect();return box.left>=0&&box.right<=innerWidth&&renameTaskDialog.scrollWidth<=renameTaskDialog.clientWidth;})()`,true,'modal fits viewport');
        await check(`(()=>{const error=document.querySelector('[data-rename-task-error]').getBoundingClientRect(),cancel=renameTaskCancel.getBoundingClientRect();return error.bottom<=cancel.top+1||error.right<=cancel.left;})()`,true,'footer feedback does not cover actions');
        await check(`document.querySelector('[data-rename-task-error]').textContent.length<=200`,true,'long footer diagnostics use common summary');
        await screenshot(theme+'-'+width);await evaluate('renameTaskCancel.click()');await until('!renameTaskDialog.open','layout close');
    }
    // The rendered checkbox's defaultChecked state comes from Auto Format New Task/Host Names.
    for(const type of ['Traditional','Advanced','Sync']){
        const count=await evaluate('requests.length');
        await evaluate(`renameTaskAutoFormat.defaultChecked=false;openTask('2','Another Task','${type}')`);
        await check(`renameTaskName.value==='Another Task'&&!renameTaskAutoFormat.checked&&renameTaskSubmit.disabled`,true,'disabled configuration preserves existing name on open');
        await evaluate(`toggleFormat(true)`);await check('renameTaskName.value','ANOTHER_TASK','manual opt-in still formats');
        await evaluate('renameTaskCancel.click()');await until('!renameTaskDialog.open','close manual formatting');
        await evaluate(`openTask('2','Another Task','${type}')`);
        await check(`renameTaskName.value==='Another Task'&&!renameTaskAutoFormat.checked`,true,'reopening restores configured default');
        await evaluate('renameTaskCancel.click()');await until('!renameTaskDialog.open','close disabled setting');
        await check('requests.length',count,'opening and canceling never rename');
        await evaluate(`renameTaskAutoFormat.defaultChecked=true;mode='success';openTask('2','Another Task','${type}')`);
        await check(`renameTaskName.value==='ANOTHER_TASK'&&renameTaskAutoFormat.checked&&!renameTaskSubmit.disabled`,true,'enabled configuration formats without toggling');
        await evaluate('send()');await until('!renameTaskDialog.open&&!renameTaskBusy','save initially formatted name');
        await check('requests.at(-1).body',{taskId:'2',name:'ANOTHER_TASK',originalName:'Another Task'},'save retains original native name for concurrency validation');
    }
    await evaluate(`openTask('3','ALREADY_FORMATTED')`);
    await check(`renameTaskName.value==='ALREADY_FORMATTED'&&renameTaskSubmit.disabled`,true,'already formatted name remains unchanged');
    await evaluate('renameTaskCancel.click()');await until('!renameTaskDialog.open','close unchanged formatted name');
    for(const mode of ['missing-ack','wrong-id','wrong-name','success-false','non-json','bad-json','timeout','server','network-after-save']){
        await evaluate(`window.mode='${mode}';openTask('42','Before');toggleFormat(false);typeName('After');window.qualityRequestCount=requests.length;window.qualityUndoCount=undo.length;send()`);
        await until('!renameTaskBusy','ambiguous response handled');
        await check('renameTaskDialog.open&&renameTaskRequiresReload&&renameTaskSubmit.disabled&&!renameTaskCancel.disabled',true,'ambiguous result stays open and blocks repeat');
        await check(`document.querySelector('[data-rename-task-error]').textContent.includes('Close this dialog')`,true,'ambiguous result gives reload guidance in footer');
        await check('undo.length===qualityUndoCount',true,'unconfirmed result cannot enable Undo');
        await evaluate(`typeName('Another');send()`);await check('requests.length===qualityRequestCount+1',true,'ambiguous result never resubmits');
        if(mode==='network-after-save')await check(`nativeNames['42']`,'After','response loss may follow a successful native rename');
        await evaluate('renameTaskCancel.click()');await until('!renameTaskDialog.open','close ambiguous outcome');
    }
    await evaluate(`mode='prewrite';openTask('42','Before');toggleFormat(false);typeName('After');send()`);await until('!renameTaskBusy','known prewrite failure');
    await check('renameTaskDialog.open&&!renameTaskRequiresReload&&!renameTaskSubmit.disabled',true,'confirmed failure before write permits correction');
    await evaluate(`mode='success';send()`);await until('!renameTaskDialog.open&&!renameTaskBusy','retry known prewrite failure succeeds');
    await check('errors',[],'no browser errors');
    const result={result:'PASS',checks};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Rename Task modal: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({ready:window.ready,errors,requests})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();stopBrowser(browser);
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});

