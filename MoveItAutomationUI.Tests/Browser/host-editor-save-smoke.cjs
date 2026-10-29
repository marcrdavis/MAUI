'use strict';
// Exercise the actual save function with controlled acknowledgements and network failures.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../wwwroot/js/editors/host-editor.js'), 'utf8');
const start = source.indexOf('async function performSave(');
const end = source.indexOf('function closeActions()', start);
assert.ok(start >= 0 && end > start);
function setup(response, shell = async () => ({ host:{ id:'42' } })) {
    let calls = 0, rendered = 0, dirty = true;
    const timers=new Map();let timerId=0;const classes=new Set();
    const context = vm.createContext({
        updateActionAvailability(){},rendering:false,undoEvent:null,localUndo:{record(){},rebase(){}},tabs:[], activeConfigurationSection:'', switchTab(){}, showConfigurationSection(){}, current:{host:{id:'42'}}, saveUrl:'/maui/HostEditor/Save', saving:false, partialSave:false,
        checkout:null, checkoutConflict:false, manualRecoverySave:false, reloadButton:null, overrideButton:null, autoSave:false, autoSaveTimer:0, autoSaveBlocked:'', activeSavePromise:null, autoSaveToggle:null, cancelButton:null, stablePayload:JSON.stringify, scheduleAutoSave(){},
        fingerprintBusy:false, picker:{open:false}, hostBrowser:null, content:{inert:false}, saveButton:{disabled:false},
        dialog:{open:true,classList:{toggle(name,value){value?classes.add(name):classes.delete(name);}}},
        error:{hidden:true,textContent:''},dirtyState:{hidden:false,textContent:'',classList:{toggle(){}}},loading:{hidden:true},loadSequence:1,savedStatusTimer:0,
        informationFields:null, metadata:null, configTabs:null, configFields:null, tasks:null, audit:null,
        antiForgeryToken:'token', loginUrl:'/maui/Account/SessionExpired',
        window:{MauiModalUI:{viewOnly(){}},location:{href:'https://maui.test/maui/Hosts',replace(){}},setTimeout(callback){timers.set(++timerId,callback);return timerId;},clearTimeout(id){timers.delete(id);}}, URL, Headers, console,
        isDirty:()=>dirty, validateBeforeSave:()=>true, collectPayload:()=>({name:'Updated',configuration:{}}),

        fetch:async (url, options)=>{
            calls++; assert.equal(new URL(url).searchParams.get('id'),'42');
            assert.equal(options.headers.get('RequestVerificationToken'),'token');
            return await response();
        },
        fetchShell:shell, renderModel(){rendered++;dirty=false;}, onSaved:async()=>{}
    });
    for(const name of ['clearSavedStatus','updateSaveButton','updateDirtyState','showError','hideError']) {
        const marker='        function '+name+'(',begin=source.indexOf(marker),finish=source.indexOf('\n        }',begin)+10;
        assert.ok(begin>=0&&finish>begin,name);vm.runInContext(source.slice(begin,finish),context);
    }
    vm.runInContext(source.slice(start,end),context);
    const wrapper=source.indexOf('        async function save(automatic'), wrapperEnd=source.indexOf('\n        }',wrapper)+10;
    vm.runInContext(source.slice(wrapper,wrapperEnd),context);
    context.updateDirtyState();
    return {context, calls:()=>calls, error:()=>context.error.textContent, rendered:()=>rendered, timers, classes, setDirty(value){dirty=value;context.updateDirtyState();}};
}
const reply = (body, ok=true) => async()=>({ok,status:ok?200:400,json:async()=>body});
async function main(){
    // Run the production shell reader as well as the save function: HTTP success
    // alone must not accept a missing or different Host during load/reload.
    const shellStart=source.indexOf('        async function fetchShell('),shellEnd=source.indexOf('        function updateActionAvailability()',shellStart);
    const shellContext=vm.createContext({checkout:null,shellUrl:'/maui/HostEditor/Shell',loginUrl:'/maui/Account/SessionExpired',URL,
        window:{MauiModalUI:{viewOnly(){}},location:{href:'https://maui.test/maui/Hosts',replace(){}}}});
    vm.runInContext(source.slice(shellStart,shellEnd),shellContext);
    for(const payload of [{},{host:{}},{host:{id:'99'}}]) {
        shellContext.fetch=async()=>({status:200,ok:true,json:async()=>payload});
        await assert.rejects(shellContext.fetchShell('42'),/requested Host/);
    }
    for(const id of ['42',42]) {
        const payload={host:{id}};shellContext.fetch=async()=>({status:200,ok:true,json:async()=>payload});
        assert.equal(await shellContext.fetchShell('42'),payload);
    }
    const confirmed=setup(reply({success:true,hostId:'42'}));
    assert.equal(confirmed.context.dirtyState.textContent,'Unsaved changes.');assert.equal(confirmed.classes.has('has-unsaved-changes'),true);
    assert.equal(await confirmed.context.save(),true);
    assert.equal(confirmed.context.dirtyState.textContent,'Saved.');assert.equal(confirmed.classes.has('has-unsaved-changes'),false);
    const savedTimer=[...confirmed.timers.values()][0];savedTimer();assert.equal(confirmed.context.dirtyState.textContent,'No unsaved changes.');
    confirmed.setDirty(true);assert.equal(confirmed.context.dirtyState.textContent,'Unsaved changes.');
    confirmed.setDirty(false);assert.equal(confirmed.context.dirtyState.textContent,'No unsaved changes.');
    assert.equal(confirmed.calls(),1); assert.equal(confirmed.rendered(),1);
    assert.equal(confirmed.context.content.inert,false);
    const unknownResponses=[
        async()=>{throw new TypeError('network failure with an untrusted detail');},
        async()=>({ok:true,status:200,json:async()=>{throw new SyntaxError('invalid JSON');}}),
        reply({}), reply({success:true,hostId:'99'}), reply({},false)
    ];
    for(const response of unknownResponses){
        const test=setup(response);
        assert.equal(await test.context.save(),false);
        assert.equal(test.context.partialSave,true);
        assert.match(test.error(),/could not be confirmed/);assert.equal(test.context.dirtyState.hidden,true,'errors replace normal footer status');
        assert.equal(await test.context.save(),false,'unknown outcomes cannot be resubmitted');
        assert.equal(test.calls(),1); assert.equal(test.rendered(),0);
        assert.equal(test.context.content.inert,false);
    }
    const rejected=setup(reply({moveItSaved:false,message:'Fix the supplied value.'},false));
    assert.equal(await rejected.context.save(),false); assert.equal(rejected.context.partialSave,false);
    assert.equal(rejected.error(),'Fix the supplied value.');assert.equal(rejected.context.dirtyState.hidden,true);
    rejected.setDirty(false);assert.equal(rejected.context.dirtyState.hidden,true,'editing does not erase an error');
    rejected.setDirty(true);await rejected.context.save(); assert.equal(rejected.calls(),2,'explicit safe rejection permits a corrected attempt');
    const conflict=setup(reply({moveItSaved:false,editConflict:true,message:'Reopen the Host before saving.'},false));
    assert.equal(await conflict.context.save(),false); assert.equal(conflict.context.partialSave,true);
    await conflict.context.save(); assert.equal(conflict.calls(),1,'stale drafts stay blocked until reopen');
    const partial=setup(reply({moveItSaved:true,message:'MOVEit saved; metadata failed.'},false));
    assert.equal(await partial.context.save(),false); assert.equal(partial.context.partialSave,true);
    assert.match(partial.error(),/^MOVEit saved; metadata failed\./);
    await partial.context.save(); assert.equal(partial.calls(),1);
    for(const shell of [async()=>null,async()=>{throw new Error('reload failed');}]){
        const test=setup(reply({success:true,hostId:'42'}),shell);
        assert.equal(await test.context.save(),false); assert.equal(test.context.partialSave,true);
        await test.context.save(); assert.equal(test.calls(),1);
    }
    let release;
    const busy=setup(()=>new Promise(resolve=>{release=resolve;}));
    const saving=busy.context.save();
    assert.equal(busy.context.content.inert,true);assert.equal(busy.context.dirtyState.textContent,'Saving changes to MOVEit Automation…');
    const joined=busy.context.save();
    assert.equal(busy.calls(),1,'an in-flight save is joined, not submitted twice');
    release({ok:true,status:200,json:async()=>({success:true,hostId:'42'})});
    assert.equal(await saving,true); assert.equal(await joined,true); assert.equal(busy.calls(),1);
    assert.equal(busy.context.content.inert,false);assert.equal(busy.context.dirtyState.textContent,'Saved.');
    const oldTimer=[...busy.timers.values()][0];busy.context.loadSequence++;busy.context.dirtyState.textContent='Loading Host…';oldTimer();assert.equal(busy.context.dirtyState.textContent,'Loading Host…','old save completion cannot change a newer editor');
    console.log('PASS Host save acknowledgement, uncertain-result retry lock, safe rejections, partial saves and in-flight edit lock.');
}
main().catch(error=>{console.error(error);process.exitCode=1;});
