'use strict';
// Execute production modal feedback and return routing with nested dialogs and captured navigation.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const read = file => fs.readFileSync(path.resolve(__dirname, '../..', file), 'utf8');
function environment(href = 'https://maui.test/maui/Hosts') {
    const listeners = {}, dialogs = [], navigations = [], restores = [];
    class Node {
        constructor(tag = 'DIV') { this.tagName = tag; this.dataset = {}; this.hidden = false; this.children = []; this.attributes = {}; this.textContent = ''; }
        querySelectorAll(selector) { return this.children.filter(n => selector === '[data-maui-modal-feedback]' ? 'mauiModalFeedback' in n.dataset : selector === '.inline-alert.error-alert' ? n.className?.includes('error-alert') : false); }
        querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
        prepend(n) { n.parentElement = this; this.children.unshift(n); }
        append(...nodes) { for (const node of nodes) { node.parentElement=this; this.children.push(node); } }
        appendChild(node) { this.append(node); }
        closest(selector) { return selector.startsWith('dialog') ? (this.tagName === 'DIALOG' ? this : this.parentElement?.closest(selector)) : null; }
        getClientRects() { return [1]; }
        setAttribute(k,v) { this.attributes[k] = v; }
    }
    const document = { documentElement:{dataset:{appBaseUrl:'/maui/',useMauiTaskEditor:'true',useMauiHostEditor:'true',editorTasksIndexUrl:'/maui/Tasks',editorHostsIndexUrl:'/maui/Hosts'}},
        activeElement:null, querySelectorAll:selector=>selector === 'dialog[open]' ? dialogs.filter(d=>d.open) : [], querySelector:()=>dialogs.find(d=>d.open)||null,
        createElement:tag=>new Node(tag.toUpperCase()), addEventListener:(type,fn)=>listeners[type]=fn };
    const window = { location:{href,assign:url=>navigations.push(url),replace:url=>restores.push(url)},history:{replaceState(){}},mauiConfirm:async()=>true,clearTimeout(){},setTimeout:()=>1 };
    const context = {document,window,URL,queueMicrotask,console,Headers};
    vm.runInNewContext(read('wwwroot/js/modal-ui.js'), context);
    vm.runInNewContext(read('wwwroot/js/editors/editor-routing.js'), context);
    const site = read('wwwroot/js/site.js');
    const start = site.indexOf('    function displayToast('), end = site.indexOf('    window.mauiDisplayToast = displayToast;',start);
    vm.runInNewContext('let sessionExpiredRedirectStarted=false;'+site.slice(start,end)+'window.mauiDisplayToast=displayToast;',context);
    function dialog() { const node = new Node('DIALOG'); node.open = false; dialogs.push(node); return node; }
    function open(node) { listeners.beforetoggle({target:node,newState:'open'}); node.open=true; }
    function close(node) { node.open=false; listeners.close({target:node}); }
    return {Node,window,document,dialog,open,close,navigations,restores,staleClose:node=>listeners.close({target:node})};
}
// Exercise production callbacks that previously rewrote Close after the markup rendered.
function productionFunction(file, name) {
    const source=read(file), match=new RegExp('^( +)(?:async )?function '+name+'\([^\n]*\) \{','m').exec(source);
    assert.ok(match,`Missing ${name} in ${file}`);
    const end=source.indexOf('\n'+match[1]+'}',match.index+match[0].length);
    assert.ok(end>match.index,`Unterminated ${name}`);
    return source.slice(match.index,end+match[1].length+2);
}
function checkTaskFolderPermissions() {
    for (const admin of [false,true]) {
        const node=()=>({dataset:{},children:[],style:{setProperty(){}},setAttribute(){},addEventListener(){},
            append(...items){this.children.push(...items);},appendChild(item){this.children.push(item);},replaceChildren(...items){this.children=items;}});
        let selected,opened=false,managerOpened=false;
        const folderSelect={},detailsFolderMenu=node(),detailsFolderButton=node();
        const context={document:{documentElement:{dataset:{moveitAdmin:String(admin)}},createElement:node,createDocumentFragment:node},
            folderSelect,detailsFolderMenu,detailsFolderButton,detailsFolderTree:{classList:{contains:()=>false,toggle:()=>opened=true}},
            createFolderIcon:node,setFolderSelection:(id,label)=>selected={id,label},
            hideFolderError(){},hideContextMenu(){},cancelFolderRename(){},renderFolderTree(){},folderManagerDirty:false,
            folderDialog:{showModal(){managerOpened=true;}}};
        vm.createContext(context);
        for(const name of ['normalizeFolderId','createFolderOption','populateFolderSelect','toggleFolderSelector'])
            vm.runInContext(productionFunction('wwwroot/js/task-information.js',name),context);
        vm.runInContext("populateFolderSelect([{id:7,name:'Operations',depth:0}],7);toggleFolderSelector()",context);
        assert.deepEqual(selected,{id:7,label:'Operations'},'Folder remains visible');
        assert.equal(detailsFolderButton.disabled,!admin);assert.equal(folderSelect.disabled,!admin);assert.equal(opened,admin);
        assert.ok(detailsFolderMenu.children[0].children.every(option=>option.disabled===!admin));
        vm.runInContext(productionFunction('wwwroot/js/tasks.js','openFolderManager'),context);
        vm.runInContext('openFolderManager()',context);assert.equal(managerOpened,admin,'Non-admins cannot open folder management');
    }
}
checkTaskFolderPermissions();
async function checkTaskInformationLogs() {
    const pending=[],rendered=[],node=()=>({hidden:false,textContent:'',replaceChildren(){}});
    const context={activeTaskId:'42',taskLogsRequest:null,taskLogsGeneration:0,AbortController,
        dialog:{open:true},taskLogsLoading:node(),taskLogsError:node(),taskLogsEmpty:node(),taskLogsList:node(),taskLogsRows:node(),
        endpoint:()=>'/Tasks/Logs',fetchJson:(url,options)=>new Promise((resolve,reject)=>pending.push({url,options,resolve,reject})),
        renderLogs:rows=>rendered.push(rows)};
    vm.createContext(context);vm.runInContext(productionFunction('wwwroot/js/task-information.js','loadLogs'),context);
    const first=vm.runInContext('loadLogs()',context);assert.match(pending[0].url,/id=42/);assert.equal(pending[0].options.cache,'no-store');
    pending.shift().resolve({logs:[{id:1}]});await first;assert.equal(rendered.at(-1)[0].id,1);
    const older=vm.runInContext('loadLogs()',context),old=pending.shift();
    const latest=vm.runInContext('loadLogs()',context),fresh=pending.shift();
    assert.equal(old.options.signal.aborted,true);
    fresh.resolve({logs:[{id:3}]});await latest;old.resolve({logs:[{id:2}]});await older;
    assert.equal(rendered.at(-1)[0].id,3,'An older Information log response cannot replace a newer refresh');
    const failed=vm.runInContext('loadLogs()',context);pending.shift().reject(new Error('Logs unavailable'));await failed;
    assert.equal(context.taskLogsError.hidden,false);
    const recovered=vm.runInContext('loadLogs()',context);pending.shift().resolve({logs:[]});await recovered;
    assert.equal(context.taskLogsError.hidden,true);
    const stale=vm.runInContext('loadLogs()',context),staleRequest=pending.shift();
    context.activeTaskId='99';context.taskLogsGeneration++;staleRequest.resolve({logs:[{id:999}]});await stale;
    assert.deepEqual(rendered.at(-1),[],'Logs from a different Task are discarded');
    const closed=vm.runInContext('loadLogs()',context),closedRequest=pending.shift();context.dialog.open=false;
    closedRequest.resolve({logs:[{id:999}]});await closed;assert.deepEqual(rendered.at(-1),[]);
    let visits=0;Object.assign(context,{detailsTabs:[],informationPanel:node(),tasksPanel:node(),logsPanel:node(),detailsSave:node(),taskDependenciesLoaded:true,loadLogs:()=>visits++});
    vm.runInContext(productionFunction('wwwroot/js/task-information.js','setTab'),context);
    vm.runInContext("setTab('logs');setTab('information');setTab('logs');setTab('logs')",context);
    assert.equal(visits,3,'Information logs refresh on every visit and repeated click');
}

function checkInformationFooterRuntime() {
    const node=()=>({hidden:false,disabled:false,textContent:'',dataset:{},children:[],classList:{toggle(){}},setAttribute(){},replaceChildren(...items){this.children=items;},append(...items){this.children.push(...items);},appendChild(item){this.children.push(item);}});
    const cases=[['hosts','setHostDetailsTab','hostDetailsTab'],['scripts','setScriptDetailsTab','scriptDetailsTab'],['datelists','setDateListDetailsTab','dateListDetailsTab'],['task-information','setTab','taskDetailsTab'],['keys-certificates','setDetailsTab','keyCertificateDetailsTab']];
    for(const [file,fn,tabKey] of cases) {
        const detailsCancel={textContent:'Close'},informationPanel=node(),tasksPanel=node(),logsPanel=node(),depsPanel=node(),detailsSave=node();
        const tabs=file==='task-information'?['information','tasks','logs']:file==='keys-certificates'?['information','dependencies']:['information','tasks'];
        const detailsTabs=tabs.map(value=>({...node(),dataset:{[tabKey]:value}}));
        const context={window:{},detailsReadiness:{reset(){}},detailsCancel,detailsTabs,informationPanel,tasksPanel,logsPanel,infoPanel:informationPanel,depsPanel,detailsSave,taskDependenciesLoaded:true,taskLogsLoaded:true};
        vm.createContext(context);
        vm.runInContext(productionFunction('wwwroot/js/'+file+'.js',fn),context);
        for(const tab of [...tabs,...tabs.slice().reverse()]) {
            vm.runInContext(`${fn}(${JSON.stringify(tab)},false)`,context);
            assert.equal(detailsCancel.textContent,'Close',`${file}/${tab}: runtime must retain Close`);
            assert.equal(detailsSave.hidden,tab!=='information',`${file}/${tab}: existing Save visibility remains`);
        }
        if(file==='hosts') {
            Object.assign(context,{detailsDialog:{},detailsState:{reset(){}},hostInformationSaveUncertain:true,metadataFields:node(),metadataAudit:node(),hostTasksRows:node(),hostTasksEmpty:node(),hostTasksList:node()});
            vm.runInContext(productionFunction('wwwroot/js/hosts.js','resetHostDetailsDialog')+';resetHostDetailsDialog()',context);
            assert.equal(context.hostInformationSaveUncertain,false,'Host reopen clears the uncertain-save guard');
            assert.equal(detailsSave.disabled,true,'Host waits for authoritative Information read before another save');
        }
        if(file==='task-information') {
            for(const key of ['detailsLoading','detailsContent','detailsError','metadataFields','metadataAudit','taskLogsRows','taskCalledByRows','taskCallsRows','taskDependenciesLoading','taskDependenciesError','taskCalledByEmpty','taskCalledByList','taskCallsEmpty','taskCallsList','taskLogsLoading','taskLogsError','taskLogsEmpty','taskLogsList'])context[key]=node();
            Object.assign(context,{activeTaskName:'Previous Task',saveOutcomeUncertain:true,taskLogsRequest:null,taskLogsGeneration:0,resetFolderSelector:()=>{}});
            vm.runInContext(productionFunction('wwwroot/js/task-information.js','reset')+';reset()',context);
            assert.equal(detailsCancel.textContent,'Close','Task initial/reopen reset retains Close');
            assert.equal(context.detailsLoading.hidden,false);
            assert.equal(context.saveOutcomeUncertain,false,'Reopen clears the uncertain-save guard');
            assert.ok(detailsTabs.every(tab=>tab.disabled),'Task tabs still disable during loading');
        }
    }
    const context={metadataFields:node(),metadataSection:node(),detailsSave:node(),detailsCancel:{textContent:'Close'},document:{createElement:node},window:{},createMetadataControl:(_field,value)=>({...node(),value})};
    vm.createContext(context);
    vm.runInContext(productionFunction('wwwroot/js/globalparameters.js','populateMetadataInto')+productionFunction('wwwroot/js/globalparameters.js','populateMetadata'),context);
    for(const fields of [[],[{name:'Environment',label:'Environment',type:'text'}],[]]) {
        context.fields=fields;
        vm.runInContext('populateMetadata(fields,{Environment:"Production"})',context);
        assert.equal(context.detailsCancel.textContent,'Close','Global Parameter metadata loading retains Close');
        assert.equal(context.detailsSave.hidden,false,'Parameter native fields keep Information Save available without metadata');
    }
}
checkInformationFooterRuntime();

// Execute each production navigator: the Back label names its source page, while
// saved grid state, context filters and IIS PathBase survive the round trip.
function checkReportReturnNavigation() {
    const cases = [
        ['tasks', 'tasks', 'Tasks'], ['hosts', 'hosts', 'Hosts'],
        ['scripts', 'scripts', 'Scripts'], ['datelists', 'datelists', 'Date Lists'],
        ['keys-certificates', 'keysandcertificates', 'Keys & Certificates'],
        ['upcoming-runs', 'upcomingruns', 'Upcoming Runs'],
        ['records', 'taskruns', 'Task Runs'], ['records', 'fileactivity', 'File Activity'],
        ['records', 'audit', 'Audit'], ['records', '', 'Previous Page']
    ];
    for (const [file, gridName, label] of cases) for (const savedState of [false, true]) {
        const target = [], source = new URL('https://maui.test/maui/Tasks?view=success#selected');
        const saved = '/maui/Tasks?mauiGridState=retained#selected';
        const window = { location: { href: source.href, pathname: source.pathname, search: source.search, hash: source.hash, assign: value => target.push(value) } };
        if (savedState) window.MauiGridNavigationState = { createReturnUrl: name => { assert.equal(name, gridName); return saved; } };
        const context = { URL, window, grid: { dataset: { grid: gridName } } };
        vm.createContext(context);
        vm.runInContext(productionFunction('wwwroot/js/' + file + '.js', 'navigateToReport'), context);
        vm.runInContext("navigateToReport('/maui/Records/TaskRun?preset=recent', {taskId:'42',taskName:'Example',unused:''})", context);
        assert.equal(target.length, 1);
        const destination = new URL(target[0]);
        assert.equal(destination.pathname, '/maui/Records/TaskRun');
        assert.equal(destination.searchParams.get('returnLabel'), label, file + '/' + gridName);
        assert.equal(destination.searchParams.get('returnUrl'), savedState && gridName ? saved : source.pathname + source.search + source.hash);
        assert.equal(destination.searchParams.get('taskId'), '42');
        assert.equal(destination.searchParams.get('taskName'), 'Example');
        assert.equal(destination.searchParams.get('preset'), 'recent');
        assert.equal(destination.searchParams.has('unused'), false);
    }
}
checkReportReturnNavigation();


async function checkTaskInformationSaveRecovery() {
    for (const failure of ['validation','conflict','partial','network','success']) {
        const posts=[], messages=[]; let closed=false;
        const inputs={'[data-task-name]':{value:' Renamed '},'[data-task-description]':{value:'Description'},'[data-task-operator-notes]':{value:'Notes'}};
        const context={activeTaskId:'1',canEditMoveItFields:true,saveOutcomeUncertain:false,detailsSave:{disabled:false},
            dialog:{dataset:{editVersion:'revision'},querySelector:selector=>inputs[selector],close:()=>{closed=true;}},
            metadataFields:{querySelectorAll:()=>[]},folderSelect:{value:''},window:{mauiValidateMetadataControls:()=>true},
            endpoint:()=>'/maui/Tasks/SaveDetails',encodeURIComponent,hideError:()=>{},showError:message=>messages.push(message),onStatus:()=>{},onSaved:async()=>{},
            fetchJson:async(url,request)=>{posts.push({url,payload:JSON.parse(request.body)});if(failure==='success')return {};
                const error=new Error('Save rejected');
                if(failure!=='network'){error.status=failure==='validation'?400:failure==='conflict'?409:502;error.payload=failure==='conflict'?{editConflict:true}:failure==='partial'?{moveItSaved:true,outcomeUnknown:true}:{};}
                throw error;
            }};
        let blocked=false;context.detailsReadiness={busy(value){context.detailsSave.disabled=value||blocked;},block(value){blocked=value;}};
        vm.createContext(context);
        vm.runInContext(productionFunction('wwwroot/js/task-information.js','save'),context);
        await vm.runInContext('save()',context);
        assert.equal(posts.length,1);
        assert.deepEqual(posts[0].payload,{editVersion:'revision',name:'Renamed',description:'Description',operatorNotes:'Notes',updateMoveItFields:true,folderId:null,metadata:{}});
        assert.equal(closed,failure==='success');
        if(failure==='success')continue;
        const uncertain=failure!=='validation';
        assert.equal(context.detailsSave.disabled,uncertain);
        assert.equal(messages[0].includes('Close and reopen'),uncertain,'Recovery instruction reflects whether retry is safe');
        await vm.runInContext('save()',context);
        assert.equal(posts.length,uncertain?1:2,'Uncertain writes never repeat; validation failures remain editable');
    }
    const context={Headers,antiForgeryToken:'token',window:{},fetch:async()=>({status:409,ok:false,headers:new Headers({'content-type':'application/json'}),json:async()=>({message:'Concurrent edit',editConflict:true})})};
    vm.createContext(context);
    vm.runInContext(productionFunction('wwwroot/js/task-information.js','fetchJson'),context);
    await assert.rejects(vm.runInContext("fetchJson('/save',{method:'POST',body:'{}'})",context),error=>error.status===409&&error.payload.editConflict&&error.message==='Concurrent edit');
}

async function checkHostInformationSaveRecovery() {
    for (const failure of ['validation','duplicate','conflict','partial','local','network','success']) {
        const posts=[],messages=[];let closed=false;
        const inputs={'[data-host-name]':{value:' Renamed '},'[data-host-description]':{value:'Description'}};
        const context={activeHostId:'42',canEditMoveItFields:true,hostInformationSaveUncertain:false,detailsSave:{disabled:false},
            detailsDialog:{dataset:{editVersion:'revision'},querySelector:selector=>inputs[selector],close:()=>{closed=true;}},
            metadataFields:{querySelectorAll:()=>[]},window:{mauiValidateMetadataControls:()=>true},endpoint:()=>'/maui/Hosts/SaveDetails',encodeURIComponent,
            hideDetailsError:()=>{},showDetailsError:message=>messages.push(message),showStatus:()=>{},refreshHostGrid:async()=>{},
            fetchJson:async(url,request)=>{posts.push(JSON.parse(request.body));if(failure==='success')return{};
                const error=new Error('Save rejected');
                if(failure!=='network'){
                    error.status=failure==='validation'?400:failure==='duplicate'||failure==='conflict'?409:failure==='local'?500:502;
                    error.payload=failure==='conflict'?{editConflict:true}:failure==='partial'?{moveItSaved:true,outcomeUnknown:true}:failure==='local'?{moveItSaved:true}:{};
                }throw error;
            }};
        let blocked=false;context.detailsReadiness={busy(value){context.detailsSave.disabled=value||blocked;},block(value){blocked=value;}};
        vm.createContext(context);vm.runInContext(productionFunction('wwwroot/js/hosts.js','saveHostDetails'),context);
        await vm.runInContext('saveHostDetails()',context);
        assert.equal(posts.length,1);assert.deepEqual(posts[0],{editVersion:'revision',name:'Renamed',description:'Description',updateMoveItFields:true,metadata:{}});
        assert.equal(closed,failure==='success');if(failure==='success')continue;
        const uncertain=!['validation','duplicate'].includes(failure);
        assert.equal(context.hostInformationSaveUncertain,uncertain);assert.equal(context.detailsSave.disabled,uncertain);
        assert.equal(messages[0].includes('Close and reopen'),uncertain);
        await vm.runInContext('saveHostDetails()',context);assert.equal(posts.length,uncertain?1:2);
    }
}

// Run each production registration against shared feedback, including the return from
// an export child dialog. Success/warning messages must not add a fifth editor row.
function checkEditorFooterFeedback() {
    for (const file of ['host-editor','traditional-task-editor','script-editor','date-list-editor','system-settings-editor']) {
        const e=environment(), editor=e.dialog(), child=e.dialog(), status=new e.Node(), classes=new Set();
        status.classList={contains:name=>classes.has(name),toggle:(name,on)=>on?classes.add(name):classes.delete(name)};
        editor.prepend(status);
        const write=(text,error=false)=>{status.hidden=false;status.textContent=text;status.classList.toggle('error',error);};
        const reset=()=>write('No unsaved changes.');
        const registration=read('wwwroot/js/editors/'+file+'.js').split('\n').find(line=>line.includes('register(dialog,')||line.includes('register?.(dialog,'));
        assert.ok(registration?.includes('footerStatus:'),file+' must register its existing footer');
        vm.runInNewContext(registration,{window:e.window,dialog:editor,dirtyState:status,status,find:()=>status,
            showError:text=>write(text,true),hideError:reset,setFooterStatus:(text,kind)=>write(text,kind==='error'),
            updateDirtyState:reset,message:write,draftStatus:reset,feedbackError:true,changed:reset});
        e.open(editor);
        e.open(child); e.close(child);
        for (const kind of ['default','success','warning']) {
            e.window.mauiDisplayToast(null,'Export completed.',kind);
            assert.equal(status.textContent,'Export completed.',file+' routes '+kind+' to its footer');
            assert.equal(editor.children.length,1,file+' must not insert a banner');
        }
        e.window.MauiModalUI.showError('Save needs review.');
        e.window.mauiDisplayToast(null,'Another action completed.');
        e.window.mauiDisplayToast(null,'Another action warning.','warning');
        assert.equal(status.textContent,'Save needs review.',file+' preserves the current footer error');
        e.window.MauiModalUI.clear(editor); status.hidden=true;
        e.window.mauiDisplayToast(null,'Another action warning.','warning');
        assert.equal(status.textContent,'No unsaved changes.',file+' preserves a hidden status next to a separate error');
        status.hidden=false;
        e.open(child);e.window.MauiModalUI.showError('Export failed.');
        assert.equal(status.textContent,'No unsaved changes.',file+' child failures stay in the child');
        e.close(child);e.window.mauiDisplayToast(null,'Export completed.');
        assert.equal(status.textContent,'Export completed.',file+' parent regains feedback after the child closes');
        assert.equal(editor.children.length,1);
        const pageToast={classList:{toggle(){},add(){},remove(){}},setAttribute(){}};
        e.window.mauiDisplayToast(pageToast,'Grid refresh failed.','error',5000,'page');
        assert.equal(pageToast.textContent,undefined,'No page toast is shown behind an editor');assert.equal(status.textContent,'Export completed.');
        assert.equal(editor.children.find(node=>node.dataset.feedbackOrigin==='page').textContent,'Page update: Grid refresh failed.');
    }
}
checkEditorFooterFeedback();
// Unregistered diagnostic dialogs place operation and labelled page feedback in their footer.
{
    const e=environment(), modal=e.dialog(), footer=new e.Node(); footer.parentElement=modal;
    const originalQuery=modal.querySelector.bind(modal);
    modal.querySelector=selector=>selector.startsWith('.settings-footer')?footer:originalQuery(selector);
    modal.querySelectorAll=selector=>selector==='[data-maui-modal-feedback]'?footer.children:selector.startsWith('.settings-footer')?[footer]:[];
    e.open(modal);e.window.mauiDisplayToast(null,'The Host operation timed out.','error');
    assert.equal(footer.children[0].textContent,'The Host operation timed out.');
    assert.equal(modal.children.length,0,'No body/header banner changes modal rows');
    e.window.mauiDisplayToast(null,'Refresh failed.','error',5000,'page');
    assert.equal(footer.children.find(node=>node.dataset.feedbackOrigin==='page').textContent,'Page update: Refresh failed.');
    assert.equal(footer.children.find(node=>node.dataset.feedbackOrigin==='modal').textContent,'The Host operation timed out.');
    const logged=[];e.window.mauiLogNotification=(text,kind,origin)=>logged.push({text,kind,origin});
    const diagnostic='A long MOVEit diagnostic.\n'+'Additional context. '.repeat(50);
    e.window.MauiModalUI.clear(modal);
    e.window.MauiModalUI.showMessage(diagnostic,'error',modal,'modal','Host test failed. See MAUI logs for details.');
    const visible=()=>footer.children.find(node=>!node.hidden);
    assert.equal(visible().textContent,'Host test failed. See MAUI logs for details.');
    assert.ok(!/inline-alert|error-alert/.test(visible().className),'Footer uses text styling, not banner classes');
    assert.equal(logged.length,1);assert.equal(logged[0].text,diagnostic,'Log retains diagnostic details, not just summary');
    for(const kind of ['warning','default']){
        e.window.MauiModalUI.clear(modal);e.window.MauiModalUI.showMessage(diagnostic,kind,modal);
        assert.ok(visible().textContent.length<=200);assert.ok(!visible().textContent.includes('\n'));
        assert.match(visible().textContent,/See MAUI logs for details/);assert.equal(visible().dataset.feedbackKind,kind);
        assert.equal(logged.at(-1).text,diagnostic);
    }
}

async function checkDiagnosticFooterSummaries() {
    const e=environment(),dialog=e.dialog(),footer=new e.Node(),logged=[];footer.parentElement=dialog;
    dialog.querySelectorAll=selector=>selector==='[data-maui-modal-feedback]'?footer.children:selector.startsWith('.settings-footer')?[footer]:[];
    e.window.mauiLogNotification=(text,kind)=>logged.push({text,kind});e.open(dialog);
    const detail='Native timeout details. '+ 'Extended diagnostic context. '.repeat(20);
    const context=vm.createContext({window:e.window,document:e.document,taskPreflightDialog:dialog,taskPreflightResults:new e.Node(),taskPreflightStageNames:{hosts:'Hosts'},
        hostTestDialog:dialog,hostTestLeaseId:'',hostTestRunning:false,hostTestCancelRequested:false,hostTestItems:[{id:'12',name:'Slow Host',supported:true,status:'Waiting'}],endpoint:()=>'/test',options:{},
        resetHostTestQueueForRun(){},updateHostTestControls:running=>{context.hostTestRunning=running;},updateHostTestRow(){},updateHostTestSummary(){},fetchJson:async()=>({status:'Failed',message:detail})});
    vm.runInContext(productionFunction('wwwroot/js/shared/host-actions.js','runHostTests'),context);
    await vm.runInContext('runHostTests()',context);
    assert.equal(footer.children.find(node=>!node.hidden).textContent,'Host test failed. See MAUI logs for details.');
    assert.equal(logged.at(-1).text,'Slow Host: '+detail);assert.equal(logged.length,1);
    e.window.MauiModalUI.clear(dialog);context.check={status:'warning',title:'Connection',message:detail};
    vm.runInContext(productionFunction('wwwroot/js/tasks.js','appendTaskPreflightCheck')+";appendTaskPreflightCheck('hosts',check)",context);
    assert.equal(footer.children.find(node=>!node.hidden).textContent,'Preflight found a warning. See MAUI logs for details.');
    assert.equal(logged.at(-1).text,'Hosts: Connection: '+detail);assert.equal(logged.length,2);
}


async function checkSlowHostDiagnostics() {
    const feedback=environment(), diagnostic=feedback.dialog(), logged=[];let diagnosticError='';
    feedback.window.mauiLogNotification=(text,kind)=>logged.push({text,kind});
    feedback.window.MauiModalUI.register(diagnostic,{error:text=>diagnosticError=text});feedback.open(diagnostic);
    const resultRows=new feedback.Node();
    const appendContext=vm.createContext({window:feedback.window,document:feedback.document,
        taskPreflightDialog:diagnostic,taskPreflightResults:resultRows,taskPreflightStageNames:{hosts:'Hosts'}});
    vm.runInContext(productionFunction('wwwroot/js/tasks.js','appendTaskPreflightCheck'),appendContext);
    vm.runInContext("appendTaskPreflightCheck('hosts',{status:'fail',title:'Connection',message:'The Host operation timed out.'})",appendContext);
    assert.match(diagnosticError,/timed out/);assert.equal(logged.at(-1).kind,'error');assert.equal(resultRows.children.length,1);
    for (const transportFailure of [false,true]) {
        const hostContext=vm.createContext({window:feedback.window,hostTestDialog:diagnostic,hostTestLeaseId:'',hostTestRunning:false,hostTestCancelRequested:false,
            hostTestItems:[{id:'12',name:'Slow Host',supported:true,status:'Waiting'}],endpoint:()=>'/test',options:{},
            resetHostTestQueueForRun(){},updateHostTestControls:running=>{hostContext.hostTestRunning=running;},updateHostTestRow(){},updateHostTestSummary(){},
            fetchJson:async()=>{if(transportFailure)throw new Error('MOVEit connection failed.');return {status:'Failed',message:'The Host operation timed out.'};}});
        vm.runInContext(productionFunction('wwwroot/js/shared/host-actions.js','runHostTests'),hostContext);
        const count=logged.length;await vm.runInContext('runHostTests()',hostContext);
        assert.match(diagnosticError,transportFailure?/connection failed/:/timed out/);
        assert.equal(logged.length,count+1);assert.equal(logged.at(-1).kind,'error');assert.equal(hostContext.hostTestRunning,false);
    }

    const checks=[],stages=[],buttons=[{disabled:false}],runButton={disabled:false,textContent:''};
    const context=vm.createContext({taskPreflightBusy:false,activeTaskPreflight:{taskId:'42',taskName:'Task'},
        taskPreflightStageOrder:['task','hosts','runtime'],taskPreflightRunAgain:runButton,taskPreflightCloseButtons:buttons,
        resetTaskPreflightUi(){},setTaskPreflightStageState:(stage,status)=>stages.push([stage,status]),
        appendTaskPreflightCheck:(stage,check)=>checks.push({stage,...check}),updateTaskPreflightSummary(){},endpoint:()=>'/preflight',
        fetchJson:async(_url,options)=>{const {stage}=JSON.parse(options.body);if(stage==='hosts')throw new Error('The Host operation timed out.');return {status:'pass',checks:[]};}});
    vm.runInContext(productionFunction('wwwroot/js/tasks.js','runTaskPreflight'),context);
    await vm.runInContext('runTaskPreflight()',context);
    assert.ok(checks.some(check=>check.stage==='hosts'&&check.status==='fail'&&check.message.includes('timed out')),
        'Host transport failure is readable in the open Preflight results, without a toast');
    assert.ok(stages.some(([stage,status])=>stage==='runtime'&&status==='pass'),'later stages still run');
    assert.equal(context.taskPreflightBusy,false);assert.equal(buttons[0].disabled,false);assert.equal(runButton.disabled,false);

    let nextTimer,reads=0,hostRunning=false;
    Object.assign(context,{taskRefreshTimer:0,uiTaskRefreshSeconds:10,taskRefreshPaused:false,
        document:{hidden:false,querySelector:()=>hostRunning?{}:null},clearTaskRefreshTimer(){},
        window:{setTimeout:callback=>{nextTimer=callback;return 1;}},refreshTaskGrid:()=>reads++});
    vm.runInContext(productionFunction('wwwroot/js/tasks.js','scheduleTaskGridRefresh'),context);
    context.taskPreflightBusy=true;vm.runInContext('scheduleTaskGridRefresh()',context);nextTimer();
    assert.equal(reads,0,'Preflight defers automatic grid requests');
    context.taskPreflightBusy=false;hostRunning=true;nextTimer();assert.equal(reads,0,'nested Host tests also defer refresh');
    hostRunning=false;nextTimer();assert.equal(reads,1,'refresh resumes without changing Pause state');

    const timers=[];let deferred=true,resourceReads=0;
    const resource=vm.createContext({console,CustomEvent:class{},document:{hidden:false,addEventListener(){},removeEventListener(){}},
        window:{setTimeout:callback=>{timers.push(callback);return timers.length;},clearTimeout(){}}});
    vm.runInContext(read('wwwroot/js/resource-ui.js'),resource);
    const controller=resource.window.mauiCreateResourceRefreshController({grid:{dispatchEvent(){}},intervalSeconds:10,
        fetchJson:async()=>{resourceReads++;return {rows:[]};},buildInitialUrl:()=>'/initial',buildRefreshUrl:()=>'/refresh',
        applyPayload(){},deferAutomaticRefresh:()=>deferred});
    await controller.loadInitial();timers.shift()();assert.equal(resourceReads,1,'Host diagnostics defer automatic resource grid refresh');
    deferred=false;timers.shift()();await Promise.resolve();await Promise.resolve();assert.equal(resourceReads,2);
    assert.equal(controller.isPaused,false);controller.destroy();
}

(async()=>{
    await checkDiagnosticFooterSummaries();
    await checkSlowHostDiagnostics();
    await checkTaskInformationLogs();
    await checkTaskInformationSaveRecovery();
    await checkHostInformationSaveRecovery();
    const e = environment(), parent = e.dialog(), child = e.dialog(), ui = e.window.MauiModalUI;
    let footer = '', cleared = 0;
    ui.register(parent,{error:message=>footer=message,clear:()=>{footer='';cleared++;}});
    e.open(parent);
    e.window.mauiDisplayToast(null,'Template validation failed.','error');
    assert.equal(footer,'Template validation failed.','parent editor receives footer error even without a page toast');
    e.staleClose(parent);
    assert.equal(footer,'Template validation failed.','a queued close from an earlier session cannot clear a reopened modal error');
    e.open(child);
    e.window.mauiDisplayToast(null,'Template saved.');
    assert.equal(child.children[0].textContent,'Template saved.');
    assert.equal(child.children[0].attributes.role,'status');
    assert.match(child.children[0].className,/info-alert/);
    e.window.mauiDisplayToast(null,'Verify the imported configuration.','warning');
    assert.match(child.children[0].className,/warning-alert/);
    const pageToast = {textContent:'',classList:{toggle(){},add(){},remove(){}},setAttribute(){}};
    e.window.mauiDisplayToast(pageToast,'Parent grid refresh failed.','error',5000,'page');
    assert.equal(pageToast.textContent,'','No toast appears behind a child modal');
    assert.equal(child.children.find(node=>node.dataset.feedbackOrigin==='page').textContent,'Page update: Parent grid refresh failed.');
    assert.equal(child.children.find(node=>node.dataset.feedbackOrigin==='modal').textContent,'Verify the imported configuration.','A page failure does not overwrite operation feedback');
    e.window.mauiDisplayToast(null,'Template save failed.','error');
    assert.equal(child.children[0].textContent,'Template save failed.');
    assert.equal(child.children[0].attributes.role,'alert');
    e.window.mauiDisplayToast(null,'Background status');
    assert.equal(child.children[0].textContent,'Template save failed.','Status does not erase an unresolved banner error');
    assert.equal(footer,'Template validation failed.','child failure stays in active child');
    e.close(child); e.open(child);
    assert.equal(child.children[0].hidden,true); assert.equal(child.children[0].textContent,'');
    const errorDialog=e.dialog();e.open(errorDialog);
    const nativeError = new e.Node();nativeError.className='inline-alert error-alert full-span';nativeError.hidden=true;nativeError.parentElement=errorDialog;errorDialog.children.push(nativeError);
    ui.showError('Native error',errorDialog);
    assert.equal(nativeError.textContent,'Native error');assert.match(nativeError.className,/full-span/,'Reused error surfaces retain their layout classes');
    nativeError.hidden=true;ui.showMessage('Operation complete','default',errorDialog);
    assert.match(nativeError.className,/error-alert/,'Status never repurposes a native error surface');
    assert.notEqual(errorDialog.children.find(node=>!node.hidden),nativeError);e.close(errorDialog);

    e.close(child); e.close(parent); e.open(child); e.open(parent);
    assert.equal(ui.activeDialog(),parent,'last-opened dialog wins over DOM order');
    assert.equal(ui.safeUrl(''),''); assert.equal(ui.safeUrl('https://evil.test/maui/Hosts'),'');
    assert.equal(ui.safeUrl('/outside/Hosts'),''); assert.equal(ui.safeUrl('/maui/Hosts?x=1'),'/maui/Hosts?x=1');
    let prepare = true;
    ui.registerContext('host-editor',parent,{capture:()=>({id:'17',tab:'tasks',section:'general'}),prepare:async()=>prepare,restore:async()=>{}});
    prepare = false;
    assert.equal(await e.window.MauiEditorRouting.openTask('42','Related','Traditional'),false);
    assert.equal(e.navigations.length,0,'failed parent save blocks navigation');
    prepare = true;
    assert.equal(await e.window.MauiEditorRouting.openTask('42','Related','Traditional'),true);
    const target = new URL(e.navigations[0]), origin = target.searchParams.get('editorReturnUrl');
    assert.equal(target.searchParams.get('editorReturnKind'),'task');
    assert.deepEqual(JSON.parse(new URL(origin,'https://maui.test').searchParams.get('editorResume')),{key:'host-editor',state:{id:'17',tab:'tasks',section:'general'}});
    const childPage = environment(target.toString()), action = childPage.dialog();
    childPage.open(action);
    assert.equal(childPage.window.MauiEditorRouting.returnToOrigin('task'),false,'Clone/Delete handoff is preserved');
    childPage.close(action);
    assert.equal(childPage.window.MauiEditorRouting.returnToOrigin('host'),false);
    assert.equal(childPage.window.MauiEditorRouting.returnToOrigin('task'),true);
    assert.equal(childPage.restores[0],origin);
    const sourcePage = environment(new URL(origin,'https://maui.test').toString());
    let restored;
    sourcePage.window.MauiModalUI.registerContext('host-editor',sourcePage.dialog(),{restore:async state=>{restored=state;}});
    await Promise.resolve();
    assert.deepEqual(JSON.parse(JSON.stringify(restored)),{id:'17',tab:'tasks',section:'general'});
    e.close(parent); e.open(parent); ui.track(parent);
    parent.querySelectorAll = () => [{name:'description',type:'text',value:'changed'}];
    let discardOptions; e.window.mauiConfirm=async options=>{discardOptions=options;return false;};
    assert.equal(await ui.confirmLeave(parent),false,'dirty Information edits require discard confirmation');
    assert.equal(discardOptions.confirmText,'Discard Changes');assert.equal(discardOptions.danger,true,'Information discard uses the same danger style as delete confirmations');
    assert.ok(cleared>0);
    for (const file of ['scripts','datelists','keys-certificates','records','upcoming-runs','task-information']) {
        const source = read('wwwroot/js/'+file+'.js');
        assert.ok(source.includes('MauiEditorRouting.openTask'),file+' related Tasks use common preference route');
    }
    let draft={name:'Original',description:'Before'};
    e.window.mauiConfirm=async()=>true;
    const undo=ui.createLocalUndo({capture:()=>draft,restore:value=>draft=value});undo.rebase(true);
    draft.name='First edit';undo.record('name','Name',true);
    draft.description='Last edit';undo.record('description','Description',true);
    assert.equal(await undo.undo(),true);assert.equal(draft.name,'First edit');assert.equal(draft.description,'Before');
    assert.equal(undo.available(),false);assert.equal(await undo.undo(),false,'Only one reversal is retained');
    draft.name='T';undo.record('name','Name',true);draft.name='Typing';undo.record('name','Name',true);
    undo.rebase();assert.equal(undo.available(),true,'Save reconciliation retains only the local last edit');
    await undo.undo();assert.equal(draft.name,'First edit','Typing in one control is one change');
    draft.name='New edit';undo.record('name','Name',true);undo.rebase(true);
    assert.equal(undo.available(),false,'A new opening/reload clears Undo');
    console.log('PASS modal footer/banner errors, nested dialog ordering, stale-message reset, safe return URLs, dirty guards, related editor routing and Host→Task→Host context.');
})().catch(error=>{console.error(error);process.exitCode=1;});
