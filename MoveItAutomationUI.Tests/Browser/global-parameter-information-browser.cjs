// Production Parameter Information editing and resource creation headers in Edge.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
const output=path.join(root,'TestResults/ResourceInformation');fs.mkdirSync(output,{recursive:true});
const candidates=[process.env.MAUI_BROWSER_EXECUTABLE,process.env['ProgramFiles(x86)']&&path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Microsoft/Edge/Application/msedge.exe'),'/usr/bin/chromium','/usr/bin/chromium-browser'];
const executable=candidates.find(file=>file&&fs.existsSync(file));
const source=read('Views/Resources/GlobalParameters.cshtml'),keys=read('Views/Resources/KeysAndCerts.cshtml'),renderIcons=require('./render-razor-icons.cjs');
assert.ok(!source.includes('data-global-parameter-editor'),'No separate Parameter editor');
assert.ok(!keys.includes('Edit Key/Certificate'),'Keys expose Information rather than Edit');
assert.ok(!read('wwwroot/js/keys-certificates.js').includes('launchNativeKeyCertificateEditor'));
assert.ok(!read('Views/Shared/_DataGrid.cshtml').includes('data-native-key-certificate-editor'));
const newKeys=['ssh-key','tls-certificate','pgp-key'].map(type=>keys.match(new RegExp('<dialog[^>]*data-new-'+type+'-dialog[^>]*>[\\s\\S]*?<\\/dialog>'))[0]).join('');
const dialogs=require('./render-creation-groups.cjs')(renderIcons([...source.matchAll(/<dialog\b[\s\S]*?<\/dialog>/g)].map(match=>match[0]).join('')+newKeys+keys.match(/<dialog[^>]*data-key-certificate-details-dialog[^>]*>[\s\S]*?<\/dialog>/)[0]+keys.match(/<dialog[^>]*data-export-key-certificate-dialog[^>]*>[\s\S]*?<\/dialog>/)[0],read('Views/Shared/_Icon.cshtml')),read('Views/Shared/_ObjectCreationResourceGroup.cshtml'));
assert.ok(!dialogs.includes('@'),'No unresolved Razor');
const menu=source.slice(source.indexOf('<div class="task-row-action-menu"'),source.indexOf('<dialog'));
const endpoints=['create','details','save-details','preflight-delete','delete','preflight-delete-bulk','delete-bulk','initial-grid','refresh'];
const site=read('wwwroot/js/site.js');
const toastFunction=site.slice(site.indexOf('    function displayToast('),site.indexOf('    window.mauiDisplayToast = displayToast;'));
const inline=file=>'<script>'+read(file).replace(/<\/script/gi,'<\\/script')+'</script>';
const html=`<!doctype html><html data-moveit-admin="true" data-app-base-url="/maui/" data-object-access-url="/maui/object-access" data-object-access-batch-url="/maui/object-access-batch" data-object-creation-access-url="/maui/creation-access"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}\n${read('wwwroot/css/script-editor.css')}\n${read('wwwroot/css/editors/host-editor.css')}\n${read('wwwroot/css/editors/traditional-task-editor.css')}</style></head><body>
<script type="application/json" data-object-creation-access>{"isAdmin":false,"groups":{"SSH":["Keys"],"TLS":[],"PGP":["Keys"]}}</script><div data-key-certificate-page-config data-key-certificate-export-options-url="/maui/key-export-options" data-export-key-certificate-url="/maui/key-export"></div><button id="key-information" data-key-certificate-row-menu-button data-row-id="pgp|42" data-key-certificate-info-url="/maui/key-details" data-key-certificate-type="PGP Key">Key menu</button>${keys.slice(keys.indexOf('<div class="task-row-action-menu"'),keys.indexOf('<dialog'))}<div data-global-parameter-page-config ${endpoints.map(name=>'data-'+name+'-url="/maui/'+name+'"').join(' ')} data-ui-refresh-seconds="0"></div><div data-grid="globalparameters"><div data-prototype-toast></div></div>
<form class="task-antiforgery"><input name="__RequestVerificationToken" value="token"></form><button data-global-parameter-information data-parameter-name="Endpoint" id="information">Information</button><button data-global-parameter-row-menu-button data-parameter-name="Endpoint" id="row-menu">Menu</button>${dialogs}${menu}
<script>
window.failExport=false;window.downloadedName="";window.downloadedSize=0;URL.createObjectURL=blob=>{downloadedSize=blob.size;return "blob:fixture";};URL.revokeObjectURL=()=>{};const nativeAnchorClick=HTMLAnchorElement.prototype.click;HTMLAnchorElement.prototype.click=function(){if(this.hasAttribute('download'))downloadedName=this.download;else nativeAnchorClick.call(this);};window.errors=[];window.requests=[];window.addEventListener('error',event=>errors.push(event.message));window.addEventListener('unhandledrejection',event=>errors.push(String(event.reason)));
window.objectCanEdit=true;window.keyType='PGP Key';window.keyPublicPrivate='Private';window.allowDiscard=true;window.mauiConfirm=async()=>allowDiscard;window.saved={name:'Endpoint',value:'old value',secure:false,resourceGroups:'Operations'};window.metadata={Owner:'Operations',Flag:'True'};window.failSave=false;window.failLoad=false;window.conflict=false;window.delaySave=false;window.finishSave=null;window.noFields=false;
window.missingKeyDetails=false;window.keyExpired=false;window.keyNative={createdDate:'2025-01-02',expirationDate:'2030-01-02',symmetricAlgorithm:'AES256',keyType:'RSA',keySize:'4096',fingerprint:'SHA256:fixture-fingerprint',publicKeySsh:'---- BEGIN SSH2 PUBLIC KEY ----\\nfixture-public-key\\n---- END SSH2 PUBLIC KEY ----',publicKeyOpenSsh:'ssh-ed25519 fixture-public-key example@host',validFromDate:'2025-01-01',sha1Thumbprint:'AA BB CC DD',serialNumber:'00 12 34'};
window.mauiConfigureMetadataControl=(control,field)=>control.required=Boolean(field.required);window.mauiValidateMetadataControls=container=>[...container.querySelectorAll('input,textarea,select')].every(control=>control.checkValidity());
window.MauiEditorRouting={openTask:async(id,name)=>{window.openedTask={id,name};}};
window.fetch=async(url,options={})=>{
 const target=new URL(url,location.href),body=options.body?JSON.parse(options.body):null;requests.push({path:target.pathname,body,token:new Headers(options.headers||{}).get('RequestVerificationToken')});
 const reply=(data,status=200)=>({ok:status===200,status,headers:new Headers({'content-type':'application/json'}),json:async()=>data});
 if(target.pathname.endsWith('/object-access'))return reply({canEdit:objectCanEdit,canDelete:objectCanEdit,canRun:true});
 if(target.pathname.endsWith('/creation-access'))return reply({isAdmin:false,groups:{SSH:['Keys'],TLS:[],PGP:['Keys']}});
 if(target.pathname.endsWith('/key-export-options'))return reply({rowId:target.searchParams.get('id'),name:'Example Key',isTls:keyType==='TLS Certificate',publicPrivate:keyPublicPrivate,canIncludePrivateKey:keyType==='TLS Certificate'||['Private','pair'].includes(keyPublicPrivate)});
 if(target.pathname.endsWith('/key-export'))return failExport?reply({message:'Export denied'},403):new Response(new Uint8Array([48,130,255]),{headers:{'content-type':'application/octet-stream','content-disposition':'attachment; filename="fixture.pfx"'}});
 if(target.pathname.endsWith('/key-details'))return reply({item:{id:'42',name:'Example Key',type:keyType,publicPrivate:keyPublicPrivate,...(missingKeyDetails?{}:keyNative),isExpired:missingKeyDetails?undefined:keyExpired},metadata:{},fields:[],tasks:[],hosts:[]});
 if(target.pathname.endsWith('/details'))return failLoad?reply({message:'Read unavailable'},500):reply({editVersion:'revision-1',parameter:{...saved,value:saved.secure?'':saved.value,displayValue:saved.secure?'(suppressed)':saved.value},metadata,fields:noFields?[]:[{name:'Owner',label:'Owner',type:'Text',required:true},{name:'Flag',label:'Flag',type:'Boolean'}],audit:{createdBy:'Marc'},tasks:[{id:'123',name:'Related Task'}]});
 if(target.pathname.endsWith('/save-details')||target.pathname.endsWith('/create')){
  if(delaySave)await new Promise(resolve=>finishSave=resolve);
  if(conflict)return reply({message:'Parameter changed',editConflict:true},409);
  if(failSave)return reply({message:'Save rejected'},400);
  saved={...saved,...body,value:body.preserveExistingSecureValue?saved.value:body.value};if(body.metadata)metadata=body.metadata;return reply({success:true,parameter:saved});
 }
 if(target.pathname.includes('preflight-delete'))return reply({success:true});
 if(target.pathname.endsWith('/delete'))return reply({success:true,name:body.name});
 if(target.pathname.endsWith('/initial-grid')||target.pathname.endsWith('/refresh'))return reply({success:true,rows:[]});
 throw new Error('Unexpected endpoint '+target.pathname);
};
</script>${inline('wwwroot/js/object-access.js')}${inline('wwwroot/js/resource-ui.js')}${inline('wwwroot/js/modal-ui.js')}<script>let sessionExpiredRedirectStarted=false;${toastFunction}window.mauiDisplayToast=displayToast;</script>${inline('wwwroot/js/grid-row-renderer.js')}${inline('wwwroot/js/globalparameters.js')}${inline('wwwroot/js/keys-certificates.js')}
<script>window.pick=name=>document.querySelector('[data-global-parameter-'+name+']');window.newPick=name=>document.querySelector('[data-new-global-parameter-'+name+']');window.input=(name,value)=>{pick(name).value=value;pick(name).dispatchEvent(new Event('input',{bubbles:true}));};window.submit=()=>pick('details-form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));window.openInfo=()=>document.querySelector('#information').click();window.newParameter=()=>document.dispatchEvent(new CustomEvent('maui:grid-action',{detail:{gridId:'globalparameters',action:'New Parameter'},cancelable:true}));</script></body></html>`;
for(const [,script]of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new(require('node:vm').Script)(script);
if(process.env.MAUI_VALIDATE_BROWSER_FIXTURE_ONLY==='1'){console.log('PASS resource Information fixture scripts compile');process.exit(0);}
assert.ok(executable,'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-parameter-layout-'));
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
    await command('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/maui/Resources/GlobalParameters` });




    for(let i=0;i<100&&!await evaluate('Boolean(window.openInfo)');i++)await pause(50);
    let checks=0;
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    for (const [type,value] of [['PGP Key','Private'],['PGP Key','Public'],['SSH Client Key',''],['TLS Certificate',''],['PGP Key','']]) {
      await evaluate(`keyType=${JSON.stringify(type)};keyPublicPrivate=${JSON.stringify(value)};missingKeyDetails=keyType==='PGP Key'&&!keyPublicPrivate;keyExpired=keyPublicPrivate==='Public';document.querySelector('#key-information').click();document.querySelector('[data-key-certificate-row-command="information"]').click()`);await pause(50);
      const identity=await evaluate(`(()=>{const field=document.querySelector('[data-key-certificate-key-type-field]');return {visible:getComputedStyle(field).display!=='none',value:field.querySelector('strong').textContent,columns:getComputedStyle(field.parentElement).gridTemplateColumns.split(' ').length};})()`);
      assert.equal(identity.visible,type==='PGP Key');assert.equal(identity.value,type==='PGP Key'?(value||'—'):'');checks+=2;
      if(type==='PGP Key'){assert.equal(identity.columns,3);checks++;}
      const native=await evaluate(`(()=>{const section=document.querySelector('[data-key-certificate-native-details]');return {title:section.querySelector('h3').textContent,next:section.nextElementSibling.querySelector('h3').textContent,badge:section.querySelector('.source-badge').textContent,fields:[...section.querySelectorAll('[data-key-certificate-native-field]')].map(control=>({name:control.dataset.keyCertificateNativeField,label:control.previousElementSibling.textContent,value:control.value,readOnly:control.readOnly,tag:control.tagName})),saveHidden:document.querySelector('[data-key-certificate-details-save]').hidden};})()`);
      const labels=type==='PGP Key'?['Creation Date','Expiration Date','Is Expired','Preferred Encryption Algorithm','Key Type','Key Length']:type==='SSH Client Key'?['Key (fingerprint)','Public Key','Public Key (Open SSH Format)']:['Valid From','Expiration Date','Thumbprint','Serial Number'];
      assert.deepEqual(native.fields.map(field=>field.label),labels);checks++;
      assert.equal(native.title,type==='TLS Certificate'?'Certificate Details':'Key Details');assert.equal(native.next,'Additional Information');assert.equal(native.badge,'MOVEit');checks+=3;
      assert.equal(native.fields.every(field=>field.readOnly),true,'Native values remain read-only');assert.equal(native.saveHidden,true,'Native details do not enable metadata Save');checks+=2;
      const expected=type==='PGP Key'?(value?['2025-01-02','2030-01-02',value==='Public'?'Yes':'No','AES256','RSA','4096']:['—','—','—','—','—','—']):type==='SSH Client Key'?['SHA256:fixture-fingerprint','---- BEGIN SSH2 PUBLIC KEY ----\nfixture-public-key\n---- END SSH2 PUBLIC KEY ----','ssh-ed25519 fixture-public-key example@host']:['2025-01-01','2030-01-02','AA BB CC DD','00 12 34'];
      assert.deepEqual(native.fields.map(field=>field.value),expected,'Show exact public values and honest unavailable values');checks++;
      if(type==='SSH Client Key'){assert.deepEqual(native.fields.map(field=>field.tag),['INPUT','TEXTAREA','TEXTAREA']);checks++;}
      if(type!=='PGP Key'||value==='Private') {
        for(const theme of ['light','dark'])for(const width of [1920,800,600]) {
          await command('Emulation.setDeviceMetricsOverride',{width,height:1080,deviceScaleFactor:1,mobile:false});
          await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
          assert.equal(await evaluate(`document.querySelector('[data-key-certificate-details-dialog]').getBoundingClientRect().width<=innerWidth`),true);checks++;
          assert.equal(await evaluate(`document.querySelector('[data-key-certificate-details-cancel]').getBoundingClientRect().bottom<=innerHeight`),true);checks++;
          await screenshot('Native-Details-'+type.replaceAll(' ','-')+'-'+theme+'-'+width);
        }
        await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
        await evaluate(`document.documentElement.dataset.theme='light'`);
      }
      if(type==='PGP Key'&&value==='Private') {
        await evaluate(`mauiDisplayToast(null,'Key status <example>');`);
        assert.equal(await evaluate(`document.querySelector('[data-key-certificate-details-dialog] [data-maui-modal-feedback][data-feedback-origin="modal"]').textContent`),'Key status <example>');checks++;
        assert.equal(await evaluate(`document.querySelector('[data-key-certificate-details-dialog] [data-maui-modal-feedback][data-feedback-origin="modal"]').getBoundingClientRect().height>0`),true);checks++;
        await screenshot('PGP-Key-Information');
        await evaluate(`document.querySelector('[data-new-pgp-key-dialog]').showModal();mauiDisplayToast(null,'Child request failed.','error');`);
        assert.equal(await evaluate(`document.querySelector('[data-new-pgp-key-dialog] [data-maui-modal-feedback]').textContent`),'Child request failed.');checks++;
        assert.equal(await evaluate(`document.querySelector('[data-key-certificate-details-dialog] [data-maui-modal-feedback][data-feedback-origin="modal"]').textContent`),'Key status <example>');checks++;
        await evaluate(`document.querySelector('[data-new-pgp-key-dialog]').close();const pageToast=document.querySelector('[data-prototype-toast]');mauiDisplayToast(pageToast,'Grid unavailable.','error',5000,'page');`);
        assert.equal(await evaluate(`(()=>{const notice=document.querySelector('[data-key-certificate-details-dialog] [data-feedback-origin="page"]');return notice.textContent==='Page update: Grid unavailable.'&&Boolean(notice.closest('.settings-footer'))&&!document.querySelector('[data-prototype-toast]').classList.contains('visible');})()`),true,'Page feedback stays in the active footer, never behind its backdrop');checks++;

        assert.equal(await evaluate(`document.querySelector('[data-key-certificate-details-dialog] [data-maui-modal-feedback][data-feedback-origin="modal"]').textContent`),'Key status <example>');checks++;
        await evaluate(`mauiDisplayToast(null,'Key request failed.','error');mauiDisplayToast(null,'Status update');`);
        assert.equal(await evaluate(`document.querySelector('[data-key-certificate-details-dialog] [data-maui-modal-feedback][data-feedback-origin="modal"]:not([hidden])').textContent`),'Key request failed.');checks++;
      }
      await evaluate(`document.querySelector('[data-key-certificate-details-cancel]').click()`);
    }
    for (const theme of ['light','dark']) {
    await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
    for (const [kind,type,privateValue] of [['tls','TLS Certificate',''],['pgp','PGP Key','pair'],['pgp','PGP Key','Private'],['pgp','PGP Key','Public'],['pgp','PGP Key','pub'],['ssh','SSH Client Key','']]) {
      await evaluate(`keyType=${JSON.stringify(type)};keyPublicPrivate=${JSON.stringify(privateValue)};const button=document.querySelector('#key-information');button.dataset.rowId=${JSON.stringify(kind+'|42')};button.click();`);await pause(30);
      assert.equal(await evaluate(`document.querySelector('[data-key-certificate-row-command="export"]').hidden`),kind==='ssh');checks++;
      if(kind==='ssh') {await evaluate(`document.body.click()`);continue;}
      await evaluate(`document.querySelector('[data-key-certificate-row-command="export"]').click()`);await pause(40);
      assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-dialog]').open`),true);checks++;
      assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-submit]').disabled`),false);checks++;
      const canIncludePrivate=kind==='tls'||['pair','Private'].includes(privateValue);
      assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-public-only]').getClientRects().length>0`),kind==='pgp'&&['Public','pub'].includes(privateValue),'Public key help recognizes the documented native pub value');checks++;
      assert.deepEqual(await evaluate(`(()=>{const checkbox=document.querySelector('[data-export-key-certificate-private]'),password=document.querySelector('[data-export-key-certificate-password]');return {checked:checkbox.checked,disabled:checkbox.disabled,passwordVisible:password.getClientRects().length>0,passwordRequired:password.required,passwordDisabled:password.disabled};})()`),
        {checked:false,disabled:!canIncludePrivate,passwordVisible:false,passwordRequired:false,passwordDisabled:true},'Initial public export state, including actual password visibility');checks++;
      if(canIncludePrivate) {
        await evaluate(`document.querySelector('[data-export-key-certificate-private]').click()`);
        assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-password-fields]').hidden`),kind!=='tls');checks++;
        assert.deepEqual(await evaluate(`['password','confirm'].map(name=>{const input=document.querySelector('[data-export-key-certificate-'+name+']');return {visible:input.getClientRects().length>0,required:input.required,disabled:input.disabled};})`),
          [0,1].map(()=>({visible:kind==='tls',required:kind==='tls',disabled:kind!=='tls'})),'PGP never displays or requires passwords; TLS private export does');checks++;
        if(kind==='tls') {
          assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-submit]').disabled`),true);checks++;
          assert.deepEqual(await evaluate(`(()=>{const option=document.querySelector('[data-export-key-certificate-private]').closest('label').getBoundingClientRect(),labels=[...document.querySelectorAll('[data-export-key-certificate-password-fields] label')].map(label=>label.getBoundingClientRect());return [Math.round(labels[0].top-option.bottom),Math.round(labels[1].top-labels[0].bottom)];})()`),[16,16],'Checkbox and password rows use normal form spacing');checks++;
          await evaluate(`const input=document.querySelector('[data-export-key-certificate-password]');input.value='fixture-password';input.dispatchEvent(new Event('input',{bubbles:true}));`);
          assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-submit]').disabled`),true,'Both password values are required');checks++;
          await evaluate(`const input=document.querySelector('[data-export-key-certificate-confirm]');input.value='different';input.dispatchEvent(new Event('input',{bubbles:true}));`);
          assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-submit]').disabled`),true,'Mismatched passwords block export');checks++;
          await evaluate(`for(const name of ['password','confirm']){const input=document.querySelector('[data-export-key-certificate-'+name+']');input.value='fixture-password';input.dispatchEvent(new Event('input',{bubbles:true}));}`);
        }
      } else {assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-private]').disabled`),true);checks++;}
      assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-submit]').disabled`),false);checks++;
      await screenshot('Export-'+kind+'-'+(privateValue||'private')+'-'+theme);
      await evaluate(`failExport=true;document.querySelector('[data-export-key-certificate-form]').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));`);await pause(50);
      assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-error]').textContent`),'Export denied');checks++;
      assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-dialog]').open`),true);checks++;
      assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-password]').value`),'');checks++;
      await evaluate(`failExport=false;if(keyType==='TLS Certificate'){for(const name of ['password','confirm']){const input=document.querySelector('[data-export-key-certificate-'+name+']');input.value='fixture-password';input.dispatchEvent(new Event('input',{bubbles:true}));}}document.querySelector('[data-export-key-certificate-form]').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));`);await pause(50);
      assert.equal(await evaluate(`document.querySelector('[data-export-key-certificate-dialog]').open`),false);checks++;
      assert.equal(await evaluate('downloadedName'),'fixture.pfx');assert.equal(await evaluate('downloadedSize'),3);checks+=2;
      assert.equal(await evaluate(`requests.filter(request=>request.path.endsWith('/key-export')).at(-1).token`),'token');checks++;
      assert.deepEqual(await evaluate(`requests.filter(request=>request.path.endsWith('/key-export')).at(-1).body`),{rowId:kind+'|42',includePrivateKey:canIncludePrivate,password:kind==='tls'?'fixture-password':'',confirmPassword:kind==='tls'?'fixture-password':''},'PGP private export sends no password values');checks++;
    }
    }
    await evaluate(`document.documentElement.dataset.theme='light'`);
    for(const type of ['ssh-key','tls-certificate','pgp-key']){
      await evaluate(`document.dispatchEvent(new CustomEvent('maui:grid-action',{detail:{gridId:'keysandcertificates',action:${JSON.stringify('New '+({'ssh-key':'SSH Key','tls-certificate':'TLS Certificate','pgp-key':'PGP Key'}[type]))}}}))`);
      await pause(20);
      assert.equal(await evaluate(`document.querySelector('[data-new-${type}-submit]').disabled`),true,'Blank creation is disabled');checks++;
      await evaluate(`const name=document.querySelector('[data-new-${type}-name]');name.value='Ready';name.dispatchEvent(new Event('input',{bubbles:true}));`);
      assert.equal(await evaluate(`document.querySelector('[data-new-${type}-submit]').disabled`),true,'A non-admin also needs an eligible Resource Group');checks++;
      assert.equal(await evaluate(`document.querySelector('[data-new-${type}-dialog] [data-object-create-group]').nextElementSibling.querySelector('[data-new-${type}-name]')!==null`),true,'Resource Group precedes Name');checks++;
      if(type!=='tls-certificate'){
        await evaluate(`const group=document.querySelector('[data-new-${type}-dialog] [data-object-resource-group]');group.value='Keys';group.dispatchEvent(new Event('change',{bubbles:true}));`);
        assert.equal(await evaluate(`document.querySelector('[data-new-${type}-submit]').disabled`),false,'An eligible group enables creation without optional credentials');checks++;
      }else{
        assert.equal(await evaluate(`document.querySelector('[data-new-${type}-dialog] [data-object-resource-group]').options.length`),1,'No unauthorized groups are offered');checks++;
      }
      if(type==='pgp-key'){
        await evaluate(`const pass=document.querySelector('[data-new-pgp-key-password]');pass.value='one';pass.dispatchEvent(new Event('input',{bubbles:true}));`);
        assert.equal(await evaluate(`document.querySelector('[data-new-pgp-key-submit]').disabled`),true,'Mismatched passwords are not ready');checks++;
        await evaluate(`const pass=document.querySelector('[data-new-pgp-key-confirm-password]');pass.value='one';pass.dispatchEvent(new Event('input',{bubbles:true}));`);
        assert.equal(await evaluate(`document.querySelector('[data-new-pgp-key-submit]').disabled`),false);checks++;
      }
      assert.equal(await evaluate(`document.querySelector('[data-new-${type}-dialog] .script-editor-icon svg')!==null`),true);checks++;
      assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-new-${type}-dialog] h2')).fontSize`),'21px');checks++;
      assert.equal(await evaluate(`document.querySelector('[data-new-${type}-cancel]').textContent`),'Close');checks++;
      await screenshot('New-'+type);
      await evaluate(`document.querySelector('[data-new-${type}-dialog]').close()`);
    }
    await evaluate(`window.fixture=document.createElement('dialog');fixture.innerHTML='<form novalidate><input name="name" required value="Original"><input type="hidden" data-maui-metadata-tags="true" name="tags" value="Operations"><input type="hidden" data-modal-value name="folder" value="1"><input name="email" data-maui-metadata-email="true"><input type="number" name="number" value="1.5"><button type="button" class="primary-button" id="fixture-save">Save</button><button type="button" class="secondary-button" id="fixture-close">Close</button><button type="button" disabled class="danger-button" title="Unavailable until ready">Delete</button><button type="button" disabled class="toolbar-button">Choose</button></form>';document.body.append(fixture);window.fields=fixture.querySelector('form').elements;window.fixtureSave=fixture.querySelector('#fixture-save');window.readiness=MauiModalUI.registerSaveAction(fixture,fixtureSave);fixture.showModal();MauiModalUI.track(fixture);window.editField=(name,value)=>{fields.namedItem(name).value=value;fields.namedItem(name).dispatchEvent(new Event('input',{bubbles:true}));};`);
    assert.equal(await evaluate('fixtureSave.disabled'),true);checks++;
    await evaluate(`editField('name','Revised')`);assert.equal(await evaluate('fixtureSave.disabled'),false);checks++;
    await evaluate(`editField('name','Original')`);assert.equal(await evaluate('fixtureSave.disabled'),true);checks++;
    await evaluate(`editField('tags','Operations;Reviewed')`);assert.equal(await evaluate('fixtureSave.disabled'),false,'Tag-only changes enable Save');checks++;
    await evaluate(`readiness.busy(true);editField('name','Typing during save')`);assert.equal(await evaluate('fixtureSave.disabled'),true,'Typing cannot unlock an in-flight Save');checks++;
    await evaluate(`readiness.busy(false);readiness.block(true);editField('name','Typing after conflict')`);assert.equal(await evaluate('fixtureSave.disabled'),true,'Typing cannot unlock a conflict');checks++;
    await evaluate(`readiness.reset();MauiModalUI.track(fixture);fields.namedItem('folder').value='2';readiness.update()`);assert.equal(await evaluate('fixtureSave.disabled'),false,'Folder-only change enables Save');checks++;
    await evaluate(`editField('email','invalid')`);assert.equal(await evaluate('fixtureSave.disabled'),true,'Invalid optional email blocks Save');checks++;
    await evaluate(`editField('email','one@example.com;two@example.com')`);assert.equal(await evaluate('fixtureSave.disabled'),false,'Metadata email lists and decimal values retain support');checks++;
    await evaluate(`editField('name','   ')`);assert.equal(await evaluate('fixtureSave.disabled'),true);checks++;
    await evaluate(`editField('name','Ready');MauiModalUI.track(fixture)`);assert.equal(await evaluate('fixtureSave.disabled'),true);checks++;
    for(const theme of ['light','dark']){
      await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
      assert.equal(await evaluate(`[...fixture.querySelectorAll('button:disabled')].every(button=>getComputedStyle(button).opacity==='0.48'&&getComputedStyle(button).cursor==='default')`),true,'All disabled button classes use the same treatment');checks++;
      assert.equal(await evaluate(`fixture.querySelector('#fixture-close').disabled`),false,'Close remains usable');checks++;
      // Theme changes animate the same background property as hover. Compare settled colors.
      await evaluate(`getComputedStyle(fixtureSave).backgroundColor;await Promise.all(fixtureSave.getAnimations().map(animation=>animation.finished.catch(()=>{})))`);
      const before=await evaluate(`getComputedStyle(fixtureSave).backgroundColor`);
      const target=await evaluate(`const r=fixtureSave.getBoundingClientRect();({x:r.left+r.width/2,y:r.top+r.height/2})`);
      await command('Input.dispatchMouseEvent',{type:'mouseMoved',...target});
      await evaluate(`getComputedStyle(fixtureSave).backgroundColor;await Promise.all(fixtureSave.getAnimations().map(animation=>animation.finished.catch(()=>{})))`);
      assert.equal(await evaluate(`getComputedStyle(fixtureSave).backgroundColor`),before,'Disabled primary has no enabled hover treatment');checks++;
      await screenshot('Modal-Buttons-'+theme);
    }
    await evaluate(`fixture.querySelector('#fixture-close').focus();fixtureSave.focus()`);assert.equal(await evaluate(`document.activeElement===fixture.querySelector('#fixture-close')`),true,'Disabled Save cannot steal keyboard focus');checks++;
    await evaluate(`window.clicks=0;fixtureSave.addEventListener('click',()=>clicks++);fixtureSave.click()`);assert.equal(await evaluate('clicks'),0,'Native disabled click semantics remain intact');checks++;
    await evaluate(`fixture.close();fixture.showModal()`);await pause(20);assert.equal(await evaluate('fixtureSave.disabled'),true,'Reopening waits for a fresh baseline');checks++;
    await evaluate(`fixture.close();fixture.remove();document.documentElement.dataset.theme='light'`);
    for(const type of ['SSH Client Key','TLS Certificate','PGP Key']){
      const result=await evaluate(`const grid=document.createElement('div');grid.dataset.grid='keysandcertificates';const table=document.createElement('table');table.innerHTML='<thead><tr class="column-header-row"><th data-column="Name"></th></tr></thead>';const row=MauiGridRows.createRenderer({grid,table,originalColumnOrder:['Name'],rowSelectionEnabled:true}).buildRowFromModel({id:'42',values:{Name:'Example',Type:${JSON.stringify(type)}},rawValues:{NativeEditorId:'42'},infoActionUrl:'/maui/Information'});({text:row.querySelector('[data-column=Name]').textContent,links:row.querySelectorAll('[data-column=Name] a,[data-column=Name] button').length,menu:!!row.querySelector('[data-key-certificate-row-menu-button]')})`);
      assert.deepEqual(result,{text:'Example',links:0,menu:true});checks++;
    }
    await evaluate('newParameter()');
    assert.equal(await evaluate(`newPick('submit').textContent`),'Create Parameter');checks++;
    await screenshot('New-Parameter');
    await evaluate(`newPick('name').value='Created';newPick('name').dispatchEvent(new Event('input',{bubbles:true}));newPick('value').value='first';newPick('form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))`);await pause(80);
    assert.deepEqual(await evaluate(`requests.find(x=>x.path.endsWith('/create')).body`),{name:'Created',value:'first',secure:false});checks++;
    assert.equal(await evaluate(`newPick('dialog').open`),false);checks++;
    await evaluate(`openInfo()`);await pause(80);
    assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-global-parameter-details-tab]')].map(x=>x.textContent)`),['Information','Tasks']);checks++;
    assert.equal(await evaluate(`pick('value').readOnly`),false);checks++;
    assert.equal(await evaluate(`pick('details-save').disabled`),true,'Loaded Information is unchanged');checks++;
    await evaluate(`input('name','')`);assert.equal(await evaluate(`pick('details-save').disabled`),true);checks++;
    await evaluate(`input('name','Changed')`);assert.equal(await evaluate(`pick('details-save').disabled`),false);checks++;
    await evaluate(`input('name','Created')`);assert.equal(await evaluate(`pick('details-save').disabled`),true,'Reverting disables Save');checks++;
    assert.equal(await evaluate(`pick('value').previousElementSibling.textContent`),'Parameter Value');checks++;
    assert.equal(await evaluate(`pick('name').closest('label').nextElementSibling.contains(pick('value'))`),true);checks++;
    assert.equal(await evaluate(`pick('metadata-fields').querySelector('[data-global-parameter-metadata-field="Flag"]').value`),'true');checks++;
    for(const theme of ['light','dark'])for(const width of [1920,1400,800,600]){
      await command('Emulation.setDeviceMetricsOverride',{width,height:1080,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
      assert.equal(await evaluate(`pick('details-dialog').getBoundingClientRect().width<=innerWidth`),true);checks++;
      assert.equal(await evaluate(`pick('details-cancel').getBoundingClientRect().bottom<=innerHeight`),true);checks++;
      await screenshot('Information-'+theme+'-'+width);
    }
    await command('Emulation.setDeviceMetricsOverride',{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    await evaluate(`document.documentElement.dataset.theme='light';input('name','Renamed');input('value','Changed value');pick('metadata-fields').querySelector('[data-global-parameter-metadata-field="Owner"]').value='Changed owner';allowDiscard=false;pick('details-cancel').click()`);await pause(50);
    assert.equal(await evaluate(`pick('details-dialog').open`),true);checks++;
    await evaluate(`failSave=true;submit()`);await pause(70);
    assert.match(await evaluate(`pick('details-error').textContent`),/Save rejected/);checks++;
    assert.equal(await evaluate(`pick('details-dialog').open`),true);checks++;
    await evaluate(`failSave=false;delaySave=true;submit()`);await pause(40);
    assert.equal(await evaluate(`pick('details-save').textContent`),'Saving…');checks++;
    assert.equal(await evaluate(`pick('details-cancel').disabled`),true);checks++;
    await evaluate(`finishSave();delaySave=false;allowDiscard=true`);await pause(90);
    assert.deepEqual(await evaluate(`requests.filter(x=>x.path.endsWith('/save-details')).at(-1).body`),{editVersion:'revision-1',originalName:'Created',name:'Renamed',value:'Changed value',secure:false,preserveExistingSecureValue:false,metadata:{Owner:'Changed owner',Flag:'true'}});checks++;
    assert.equal(await evaluate(`pick('details-dialog').open`),false);checks++;
    await evaluate(`saved={name:'Endpoint',value:'protected secret',secure:true};openInfo()`);await pause(70);
    assert.equal(await evaluate(`pick('value').disabled&&pick('secure').disabled`),true);checks++;
    assert.equal(await evaluate(`pick('value').value`),'');checks++;
    await evaluate(`input('name','Secure renamed');submit()`);await pause(80);
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/save-details')).at(-1).body.preserveExistingSecureValue`),true);checks++;
    assert.equal(await evaluate(`saved.value`),'protected secret');checks++;
    await evaluate(`openInfo()`);await pause(70);
    await evaluate(`pick('reset-value').click()`);
    assert.equal(await evaluate(`pick('details-save').disabled`),false,'Resetting a protected value is a real change even while blank');checks++;
    await evaluate(`input('value','replacement')`);
    assert.equal(await evaluate(`pick('value').type`),'password');checks++;
    assert.equal(await evaluate(`pick('value').disabled`),false);checks++;
    await evaluate(`submit()`);await pause(80);
    assert.equal(await evaluate(`saved.value`),'replacement');checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.path.endsWith('/save-details')).at(-1).body.preserveExistingSecureValue`),false);checks++;
    await evaluate(`openInfo()`);await pause(70);
    await evaluate(`pick('reset-value').click();pick('secure').checked=false;pick('secure').dispatchEvent(new Event('change'));input('value','visible');submit()`);await pause(80);
    assert.equal(await evaluate(`saved.secure`),false);checks++;
    await evaluate(`noFields=true;openInfo()`);await pause(70);
    assert.equal(await evaluate(`pick('details-save').hidden`),false);checks++;
    await evaluate(`document.querySelector('[data-global-parameter-details-tab="tasks"]').click();pick('details-task-content').querySelector('button').click()`);
    assert.equal(await evaluate(`pick('details-save').hidden`),true);checks++;
    assert.deepEqual(await evaluate('openedTask'),{id:'123',name:'Related Task'});checks++;
    await screenshot('Parameter-Tasks');
    const returnPath=await evaluate('return await MauiModalUI.prepareReturn()');
    const returnUrl=new URL(returnPath,`http://127.0.0.1:${server.address().port}`);
    assert.deepEqual(JSON.parse(returnUrl.searchParams.get('editorResume')),{key:'global-parameter-information',state:{name:'Secure renamed',tab:'tasks'}});checks++;
    assert.deepEqual(await evaluate('errors'),[]);checks++;
    await command('Page.navigate',{url:returnUrl.toString()});await pause(200);
    assert.equal(await evaluate(`pick('details-dialog').open`),true);checks++;
    assert.equal(await evaluate(`document.querySelector('[data-global-parameter-details-panel="tasks"]').hidden`),false);checks++;
    await evaluate(`document.querySelector('[data-global-parameter-details-tab="information"]').click();conflict=true;input('value','Concurrent draft');submit()`);await pause(70);
    assert.equal(await evaluate(`pick('details-save').disabled`),true);checks++;
    assert.match(await evaluate(`pick('details-error').textContent`),/Close and reopen/);checks++;
    await evaluate(`pick('details-cancel').click()`);await pause(40);
    await evaluate(`conflict=false;failLoad=true;openInfo()`);await pause(70);
    assert.equal(await evaluate(`pick('details-save').disabled`),true);checks++;
    assert.equal(await evaluate(`pick('details-content').hidden`),true);checks++;
    assert.match(await evaluate(`pick('details-error').textContent`),/Read unavailable/);checks++;
    assert.equal(await evaluate(`requests.filter(x=>x.body).every(x=>x.token==='token')`),true);checks++;
    assert.deepEqual(await evaluate('errors'),[]);checks++;
    // Reusing an Information modal must restore normal fields after a read-only object.
    await evaluate(`objectCanEdit=false;conflict=false;failLoad=false;openInfo()`);await pause(90);
    assert.equal(await evaluate(`pick('value').disabled&&pick('details-save').disabled`),true,'Read-only metadata and values cannot be edited');checks++;
    assert.equal(await evaluate(`pick('details-cancel').disabled`),false,'Read-only Information still closes');checks++;
    assert.equal(await evaluate(`pick('details-save').hidden`),true,'Read-only Information hides Save');checks++;
    await evaluate(`document.querySelector('[data-global-parameter-details-tab="tasks"]').click();document.querySelector('[data-global-parameter-details-tab="information"]').click()`);await pause(40);
    assert.equal(await evaluate(`pick('details-save').hidden`),true,'Returning from Tasks cannot expose a denied Save');checks++;

    await evaluate(`objectCanEdit=true;openInfo()`);await pause(90);
    assert.equal(await evaluate(`pick('value').disabled`),false,'The next editable object does not inherit a prior denial');checks++;
    assert.equal(await evaluate(`pick('details-save').hidden`),false,'Editable Information restores Save');checks++;

    // Global Parameters stay visible; native non-admins have no management affordances.
    await evaluate(`pick('details-dialog').close();document.documentElement.dataset.moveitAdmin='false';window.beforeDenied=requests.length;openInfo()`);await pause(40);
    assert.equal(await evaluate(`pick('details-dialog').open`),false);checks++;
    assert.equal(await evaluate('requests.length===beforeDenied'),true,'Blocked Information makes no details request');checks++;
    const parameterRow=await evaluate(`const grid=document.createElement('div');grid.dataset.grid='globalparameters';const table=document.createElement('table');table.innerHTML='<thead><tr class="column-header-row"><th data-column="Name"></th></tr></thead>';const row=MauiGridRows.createRenderer({grid,table,originalColumnOrder:['Name'],rowSelectionEnabled:true}).buildRowFromModel({id:'Visible',values:{Name:'Visible'},rawValues:{},infoActionUrl:'/maui/details?name=Visible'});({name:row.querySelector('[data-column=Name]').textContent,links:row.querySelectorAll('[data-column=Name] button,[data-column=Name] a').length,menus:row.querySelectorAll('[data-global-parameter-row-menu-button],[data-row-info-url]').length})`);
    assert.deepEqual(parameterRow,{name:'Visible',links:0,menus:0});checks++;
    for(const type of ['tasks','hosts','scripts','datelists','keysandcertificates']) {
      const state=await evaluate(`const grid=document.createElement('div');grid.dataset.grid=${JSON.stringify(type)};const table=document.createElement('table');table.innerHTML='<thead><tr class="column-header-row"><th data-column="Name"></th></tr></thead>';const row=MauiGridRows.createRenderer({grid,table,originalColumnOrder:['Name'],rowSelectionEnabled:true}).buildRowFromModel({id:'42',values:{Name:'Visible'},rawValues:{CanEdit:'0',CanRun:'0',CanEditDelete:'0'},infoActionUrl:'/maui/details'});({selectionDisabled:row.querySelector('[data-row-select]').disabled,nameDisabled:row.querySelector('[data-column=Name] button')?.disabled||false})`);
      assert.equal(state.selectionDisabled,true,type+' read-only row cannot be selected');checks++;
      assert.equal(state.nameDisabled,false,type+' name allows inspection');checks++;
    }
    await evaluate(`document.documentElement.dataset.moveitAdmin='true'`);
    for(const theme of ['light','dark']){
      await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)}`);
      for(const menuClass of ['dropdown-menu','task-row-action-menu','script-editor-actions-menu','maui-host-editor-actions-menu','traditional-task-editor-actions-menu','traditional-task-editor-add-menu']){
        const style=await evaluate(`const menu=document.createElement('div');menu.id='disabled-menu-fixture';menu.className=${JSON.stringify(menuClass)};menu.style.cssText='display:block;position:fixed;left:10px;top:10px;z-index:99999';menu.innerHTML='<button class="menu-danger-action danger-menu-item" disabled>Unavailable</button><button>Available</button>';document.body.append(menu);const disabled=getComputedStyle(menu.firstElementChild),enabled=getComputedStyle(menu.lastElementChild),rect=menu.firstElementChild.getBoundingClientRect();({cursor:disabled.cursor,opacity:Number(disabled.opacity),enabledCursor:enabled.cursor,x:rect.x+rect.width/2,y:rect.y+rect.height/2})`);
        assert.equal(style.cursor,'default',menuClass+'/'+theme);assert.ok(style.opacity<=.5,menuClass+'/'+theme+' visibly dimmed');assert.equal(style.enabledCursor,'pointer',menuClass+'/'+theme+' enabled cursor');checks+=3;
        await command('Input.dispatchMouseEvent',{type:'mouseMoved',x:style.x,y:style.y});
        assert.equal(await evaluate(`getComputedStyle(document.querySelector('#disabled-menu-fixture button')).backgroundColor`),'rgba(0, 0, 0, 0)',menuClass+'/'+theme+' disabled danger item has no hover highlight');checks++;
        await evaluate(`document.getElementById('disabled-menu-fixture').remove()`);
      }
    }
    const result={result:'PASS',checks,themes:['light','dark'],widths:[1920,1400,800,600]};fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(result,null,2));console.log('Resource Information workflows: '+JSON.stringify(result));
})().catch(async error=>{console.error(error);try{if(socket){console.error(await evaluate('({status:pick("details-error").textContent,errors,requests})'));await screenshot('FAILED');}}catch{}process.exitCode=1;}).finally(()=>{
 socket?.close();if(browser?.pid){if(process.platform==='win32')spawnSync('taskkill',['/pid',String(browser.pid),'/T','/F'],{stdio:'ignore'});else browser.kill('SIGTERM');}
 server?.close();for(const task of pending.values()){clearTimeout(task.timer);task.reject(new Error('Browser closed'));}try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
});
