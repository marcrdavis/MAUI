'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const plain = value => JSON.parse(JSON.stringify(value));
const sortingContext = { window:{} };
vm.runInNewContext(read('wwwroot/js/shared/folder-browser-sorting.js'), sortingContext);
const sorting = sortingContext.window.MauiFolderBrowserSorting;
const items = [
    { name:'file10',size:'1,024',lastModified:'2026-02-01T00:00:00Z' },
    { name:'file2',size:'99',lastModified:'2026-10-01T00:00:00Z' },
    { name:'folder',size:'',lastModified:'' },
    { name:'large',size:'2 MB',lastModified:'2025-12-31T23:00:00-05:00' },
    { name:'zero',size:'0',lastModified:'invalid' }
];
const original = structuredClone(items);
let sort = sorting.next({column:'',direction:''}, 'name');
assert.deepEqual(plain(sorting.apply(items,sort)).map(item=>item.name), ['file2','file10','folder','large','zero']);
sort = sorting.next(sort,'name'); assert.equal(sort.direction,'desc');
sort = sorting.next(sort,'name'); assert.deepEqual(plain(sorting.apply(items,sort)),items,'Third click restores API order');
assert.deepEqual(plain(sorting.apply(items,{column:'size',direction:'asc'})).map(item=>item.name),['zero','file2','file10','large','folder']);
assert.deepEqual(plain(sorting.apply(items,{column:'size',direction:'desc'})).map(item=>item.name),['large','file10','file2','zero','folder']);
assert.deepEqual(plain(sorting.apply(items,{column:'lastModified',direction:'asc'})).map(item=>item.name),['large','file10','file2','folder','zero']);
assert.deepEqual(items,original,'Sorting never mutates server order or selected objects');

// Exercise the production readiness helper without blocking preserved native steps.
const editorSource=read('wwwroot/js/editors/traditional-task-editor.js');
const readinessSource=editorSource.slice(editorSource.indexOf('        function incompleteHostStep()'),editorSource.indexOf('        function isSupportedHostSavePattern()'));
const readiness={taskSteps:[],canWriteHostStep:(_,type)=>type==='Share',isPreservedUnsupportedHostStep:step=>Boolean(step.preserved)};
vm.runInNewContext(readinessSource,readiness);
readiness.taskSteps=[{kind:'Destination',destination:{hostId:'',hostTypeCode:''}}];
assert.equal(readiness.incompleteHostStep().kind,'Destination');
readiness.taskSteps=[{kind:'Source',source:{hostId:'missing-host',hostTypeCode:''},preserved:true}];
assert.equal(readiness.incompleteHostStep(),undefined,'Unchanged unrecognized native steps remain preservable');
readiness.taskSteps[0].preserved=false;assert.equal(readiness.incompleteHostStep().kind,'Source');
readiness.taskSteps=[{kind:'Source',source:{hostId:'',hostTypeCode:'Share'},preserved:true}];
assert.equal(readiness.incompleteHostStep().kind,'Source','A supported step still requires an actual Host');

function hostScenario(admin) {
    let registered = null, callbacks = null, actionOptions = null, exportOptions = null;
    const actionCalls=[], exports=[];
    const dialog = {open:false};
    const window = { MauiEditorRouting:{ useMauiHostEditor:true,registerHostOpen:handler=>{registered=handler;} },
        MauiHostActions:{create:options=>{actionOptions=options;return {
            openHostTestDialog:(...args)=>actionCalls.push(['test',...args]),
            openCloneHostDialog:(...args)=>actionCalls.push(['clone',...args]),
            beginDeleteHost:(...args)=>actionCalls.push(['delete',...args])
        };}},
        MauiNativePortability:{create:options=>{exportOptions=options;return {openExport:(...args)=>exports.push(args)};}},
        MauiHostEditor:{initialize:options=>{callbacks=options;return {open:async()=>{dialog.open=true;return true;}};}} };
    vm.runInNewContext(read('wwwroot/js/editors/task-host-editor.js'),{window,document:{documentElement:{dataset:{moveitAdmin:String(admin)}},querySelector:()=>dialog}});
    window.MauiTaskHostEditor.initialize({dataset:{hostEditorShellUrl:'/maui/HostEditor/Shell',hostEditorSaveUrl:'/maui/HostEditor/Save',exportHostUrl:'/maui/Hosts/ExportHost'}},'token',{fetchJson:async()=>({})});
    return {dialog,actionCalls,exports,get actionOptions(){return actionOptions;},get exportOptions(){return exportOptions;},get registered(){return registered;},get callbacks(){return callbacks;}};
}

class Node {
    constructor(){this.listeners={};this.disabled=false;this.hidden=false;this.value='';this.textContent='';this.open=false;this.valid=true;}
    addEventListener(name,fn){this.listeners[name]=fn;}
    focus(){} checkValidity(){return this.valid;} showModal(){this.open=true;} close(){this.open=false;}
}
function runScenario() {
    const nodes = Object.fromEntries(['date','run','status','progress','error'].map(name=>[name,new Node()]));
    const close = new Node(),dialog = new Node();
    dialog.querySelector = selector=>nodes[selector.match(/data-run-as-of-(\w+)/)[1]];
    dialog.querySelectorAll = ()=>[close];
    let request = null,resolve = null, result = {completed:true,success:true,restored:true,message:'Task completed. Original settings restored.',progress:100};
    const calls={posts:0,reloads:0,recovery:0,busy:[],flush:true};
    const options={runUrl:()=>'/maui/TraditionalTaskEditor/RunAsOf',progressUrl:()=>'/maui/TraditionalTaskEditor/RunAsOfProgress',
        flush:async()=>calls.flush,context:()=>({id:'42',editVersion:'version'}),signature:()=>'signature',
        reload:async value=>{assert.equal(value,'signature');calls.reloads++;},busy:value=>calls.busy.push(value),recovery:()=>calls.recovery++,
        fetchJson:async(url,options={})=>{
            if(options.method==='POST'){calls.posts++;request={url,body:JSON.parse(options.body)};return await new Promise(yes=>{resolve=yes;});}
            return result;
        }};
    const window={crypto:{randomUUID:()=>'12345678-1234-1234-1234-123456789012'},setTimeout:()=>1,clearTimeout(){},location:{href:'https://maui.test/maui/Tasks'}};
    vm.runInNewContext(read('wwwroot/js/editors/task-run-as-of.js'),{window,document:{querySelector:()=>dialog},URL,Date,console});
    const modal=window.MauiTaskRunAsOf.create(options);
    return {window,modal,nodes,close,dialog,calls,get request(){return request;},complete:state=>{result=state||result;resolve(result);},click:()=>nodes.run.listeners.click()};
}

(async()=>{
    assert.equal(hostScenario(false).registered,null,'MAUI Admin alone cannot expose the MOVEit-admin Host action');
    const host=hostScenario(true);let returned=0;
    assert.equal(await host.registered('17','Host',{onClosed:()=>returned++}),true);
    assert.equal(host.callbacks.shellUrl,'/maui/HostEditor/Shell');
    const selected={id:'17',name:'Selected Host',testSupported:true};
    host.callbacks.onTestHost(selected,'host-lease');host.callbacks.onCloneHost(selected);host.callbacks.onExportHost(selected);host.callbacks.onDeleteHost(selected);
    assert.deepEqual(plain(host.actionCalls),[['test',[selected],'host-lease'],['clone','17','Selected Host'],['delete','17']]);
    assert.deepEqual(plain(host.exports),[[['17'],['Selected Host']]]);
    assert.equal(host.exportOptions.objectType,'Host');assert.equal(host.exportOptions.exportUrl,'/maui/Hosts/ExportHost');
    await host.actionOptions.openEditor('900','Cloned');assert.equal(returned,0,'Switching to a clone keeps the Task return callback');
    host.callbacks.onClosed();assert.equal(returned,0,'Queued close events cannot end a reopened modal');
    assert.equal(await host.registered('18','Another Host',{}),false,'An open Host draft cannot be replaced');
    host.dialog.open=false;host.callbacks.onClosed();host.callbacks.onClosed();assert.equal(returned,1,'Closing returns exactly once');
    const run=runScenario();
    assert.equal(run.window.MauiTaskRunAsOf.available([{kind:'Source',patch:{FileMask:'[YYYY]*.txt'}}]),true);
    assert.equal(run.window.MauiTaskRunAsOf.available([{kind:'Destination',patch:{Path:'/[DD-]'}}]),true);
    assert.equal(run.window.MauiTaskRunAsOf.available([{kind:'Source',patch:{FileMask:'*.*',Path:'/[OrigName]'}}]),false);
    run.modal.open();run.nodes.date.value='2026-10-01T23:00:05';
    const operation=run.click();await new Promise(setImmediate);
    assert.equal(run.calls.posts,1);assert.equal(run.nodes.run.textContent,'Running…');assert.equal(run.close.disabled,true);
    await run.click();assert.equal(run.calls.posts,1,'Repeated Run clicks cannot start duplicate jobs');
    assert.equal(new URL(run.request.url).pathname,'/maui/TraditionalTaskEditor/RunAsOf');
    assert.equal(run.request.body.dateTime,'2026-10-01T23:00:05','Selected wall-clock values do not shift through UTC');
    run.complete();await operation;
    assert.equal(run.calls.reloads,1);assert.equal(run.nodes.run.textContent,'Run');assert.equal(run.close.disabled,false);assert.equal(run.calls.recovery,0);
    const failed=runScenario();failed.modal.open();const failure=failed.click();await new Promise(setImmediate);
    failed.complete({completed:true,restored:false,success:false,message:'Restore from Undo.',progress:100});await failure;
    assert.equal(failed.nodes.run.disabled,true,'Unconfirmed restoration cannot be blindly retried');assert.equal(failed.calls.recovery,1);assert.equal(failed.nodes.error.hidden,false);
    const unsaved=runScenario();unsaved.calls.flush=false;unsaved.modal.open();await unsaved.click();assert.equal(unsaved.calls.posts,0);assert.equal(unsaved.nodes.error.hidden,false,'Save failures stay visible in the Run As Of modal');
    console.log('PASS folder sorting, admin-only nested Host returns, Run As Of macro eligibility, selected time, duplicate prevention, progress and restoration guardrails.');
})().catch(error=>{console.error(error);process.exitCode=1;});
