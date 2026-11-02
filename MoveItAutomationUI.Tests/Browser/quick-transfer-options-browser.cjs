// Production Quick Transfer option markup, CSS and conditional controls in Edge; no native transfer calls.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
const output=path.join(root,'TestResults/QuickTransferOptions');fs.mkdirSync(output,{recursive:true});
const candidates=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium'];
const executable=candidates.find(file=>file&&fs.existsSync(file));
const inline=file=>'<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const view=read('Views/QuickTransfers/Index.cshtml'),source=read('wwwroot/js/quick-transfer.js');
const start=view.indexOf('    <section class="quick-transfer-card quick-transfer-options-card"');
const options=view.slice(start,view.indexOf('    <section class="quick-transfer-status-card"',start))
 .replace(/title="@\(Model\.ConfirmationEmailAvailable[^\n]*\)"/g,'title="Send a transfer confirmation email."')
 .replace('@(Model.ConfirmationEmailAvailable ? null : "disabled")','');
assert.ok(!options.includes('@(')&&!options.includes('@Model'),'render SMTP-only Razor expressions');
const conditional=source.slice(source.indexOf('    function syncConditionalControls()'),source.indexOf('    function clearPreparedTransfer()'));
const busyControls=source.slice(source.indexOf('    function setBusy('),source.indexOf('    function syncTransferAction('));
const html=`<!doctype html><html data-theme="light"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}body{padding:16px;min-width:0}.quick-transfer-page{width:100%}</style></head><body><main class="quick-transfer-page" data-quick-transfer-config>${options}</main><script>
window.errors=[];window.requests=[];window.addEventListener('error',e=>errors.push(e.message));window.addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
const root=document.querySelector('[data-quick-transfer-config]'),cfg={confirmationEmailAvailable:'true'};
const confirmationCheckbox=root.querySelector('[data-quick-confirm-email]'),confirmationRecipients=root.querySelector('[data-quick-confirm-recipients]'),deleteTemp=root.querySelector('[data-quick-delete-temp]');
const sourceHost=null,destinationHost=null,pgpRow=null,preflightButton=null,transferButton=null;
let busy=false,transferInProgress=false;function syncPgpLicenseState(){}function currentMode(){return 'none';}function syncTransferAction(){}
${conditional}${busyControls}
confirmationCheckbox.addEventListener('change',syncConditionalControls);syncConditionalControls();window.ready=true;
window.toggleEmail=checked=>{confirmationCheckbox.checked=checked;confirmationCheckbox.dispatchEvent(new Event('change',{bubbles:true}));};
window.visible=()=>{const box=confirmationRecipients.getBoundingClientRect();return box.width>0&&box.height>0&&getComputedStyle(confirmationRecipients).visibility!=='hidden';};
window.geometry=()=>{const group=root.querySelector('.quick-transfer-email-option').getBoundingClientRect(),toggle=root.querySelector('.quick-transfer-confirm-toggle').getBoundingClientRect(),input=confirmationRecipients.getBoundingClientRect(),deletion=root.querySelector('[data-quick-delete-temp]').closest('.quick-transfer-switch-row').getBoundingClientRect();return {group:{x:group.x,y:group.y,width:group.width,height:group.height},toggle:{x:toggle.x,y:toggle.y,width:toggle.width,height:toggle.height},input:{x:input.x,y:input.y,width:input.width,height:input.height},deletion:{x:deletion.x,y:deletion.y,width:deletion.width,height:deletion.height}};};
</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
if(process.argv.includes('--syntax-only')){console.log('PASS Quick Transfer option browser fixture syntax');process.exit(0);}
assert.ok(executable,'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-qt-options-'));
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
    await until('window.ready','option controls ready');
    let checks=0;const check=async(expression,expected,label)=>{assert.deepEqual(await evaluate(expression),expected,label);checks++;};
    for(const theme of ['light','dark'])for(const width of [1593,1400,1024,800,600,400]){
        await command('Emulation.setDeviceMetricsOverride',{width,height:500,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme='${theme}';cfg.confirmationEmailAvailable='true';toggleEmail(false);setBusy(false)`);
        await check(`root.querySelectorAll('input[type="checkbox"]').length`,2,'only delete and confirmation options');
        await check('visible()&&confirmationRecipients.disabled',true,'recipients remain visible while unchecked');
        await check('confirmationCheckbox.disabled',false,'disabled recipients do not disable the email option');
        await check("getComputedStyle(root.querySelector('.quick-transfer-email-option')).opacity",'1','unchecked recipients do not dim the enabled email checkbox');
        await check('document.documentElement.scrollWidth<=innerWidth',true,'no horizontal overflow');
        if(width>720)await check('(()=>{const b=geometry();return Math.abs(b.toggle.y+b.toggle.height/2-b.input.y-b.input.height/2)<1;})()',true,'email checkbox and recipients share one line');
        if(width>1100)await check('(()=>{const b=geometry();return Math.abs(b.group.y-b.deletion.y)<1;})()',true,'task options share the desktop row');
        if([1593,800,400].includes(width))await screenshot(theme+'-'+width+'-unchecked');
        await evaluate(`toggleEmail(true);confirmationRecipients.value='user@example.com; team@example.com'`);
        await check('visible()&&!confirmationRecipients.disabled',true,'checked email enables recipients');
        await evaluate('deleteTemp.checked=false;setBusy(true)');await check('confirmationCheckbox.disabled&&confirmationRecipients.disabled&&deleteTemp.disabled',true,'transfer/preflight work locks options');
        await evaluate('setBusy(false)');await check('!confirmationCheckbox.disabled&&!confirmationRecipients.disabled&&!deleteTemp.disabled&&!deleteTemp.checked',true,'unlock restores enabled recipients and retention choice');
        await evaluate('toggleEmail(false)');await check(`confirmationRecipients.disabled&&confirmationRecipients.value==='user@example.com; team@example.com'`,true,'unchecking email retains draft text');
        await evaluate(`cfg.confirmationEmailAvailable='false';syncConditionalControls();setBusy(true);setBusy(false)`);await check('visible()&&confirmationCheckbox.disabled&&confirmationRecipients.disabled',true,'SMTP-unavailable controls remain visible and disabled after busy cycle');
        await check('errors',[],'no browser errors');
    }
    const result={result:'PASS',checks,themes:['light','dark'],widths:[1593,1400,1024,800,600,400]};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Quick Transfer option rendering: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({ready:window.ready,errors,requests})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});

