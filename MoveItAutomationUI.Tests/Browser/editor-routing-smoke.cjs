// Execute the production shared route and Calendar button adapter with captured transports.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const load = name => fs.readFileSync(path.join(root, name), 'utf8');
const layout = load('Views/Shared/_Layout.cshtml');
const tasks = load('wwwroot/js/tasks.js');
const hosts = load('wwwroot/js/hosts.js');
const routing = load('wwwroot/js/editors/editor-routing.js');
const calendar = load('wwwroot/js/task-calendar.js');
const helpLink = layout.match(/<a\b[^>]*>MOVEit Help<\/a>/)?.[0];
assert.ok(helpLink, 'MOVEit Help is available before About in the left navigation');
assert.match(helpLink, /href="https:\/\/docs\.progress\.com\/bundle\/moveit-automation-web-admin-help-2025"/);
assert.match(helpLink, /target="_blank"/);
assert.match(helpLink, /rel="noopener noreferrer"/);
assert.ok(layout.indexOf(helpLink) < layout.indexOf('asp-action="About"'));
// A collapsed sidebar must preserve an external Help link's new-tab behavior.
const navParent = load('wwwroot/js/site.js').match(/^    function activateNavParent\(button\) \{[\s\S]*?^    \}/m)?.[0];
assert.ok(navParent);
const navigation = [], externalClicks = [];
const firstLink = { href:'https://docs.progress.com/bundle/moveit-automation-web-admin-help-2025', target:'_blank', click:()=>externalClicks.push(true) };
const navContext = vm.createContext({ shell:{classList:{contains:()=>true}}, navigateFromSidebar:url=>navigation.push(url) });
vm.runInContext(navParent,navContext);
const navButton = { closest:()=>({ querySelector:()=>firstLink }) };
navContext.activateNavParent(navButton);
assert.deepEqual(externalClicks,[true]); assert.deepEqual(navigation,[]);
firstLink.target=''; firstLink.href='/maui/Help/About';
navContext.activateNavParent(navButton);
assert.deepEqual(navigation,['/maui/Help/About']);
for (const [, key] of routing.matchAll(/endpoint\(['"]([A-Za-z0-9]+)['"]\)/g)) {
    const kebab = key.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
    assert.ok(layout.includes(`data-${kebab}=`), `Missing shared endpoint data-${kebab}`);
}
assert.match(tasks, /searchParams\.get\('editorTaskId'\)/);
assert.match(hosts, /searchParams\.get\('editorHostId'\)/);
assert.match(tasks, /history\.replaceState/);
assert.match(hosts, /history\.replaceState/);
assert.match(tasks, /newTaskResourceGroup\.replaceChildren\(\)/);
assert.match(load('Views/Tasks/Index.cshtml'), /configured Task editor/);
assert.match(tasks, /onDeleteTask:\s*taskId\s*=>\s*beginDeleteTasks\(\[taskId\]\)/);
assert.doesNotMatch(load('Views/Shared/Editors/_TraditionalTaskEditorDialog.cshtml'), /placeholder-action="Delete"/);
assert.doesNotMatch(calendar, /MauiNativeEditor/);
const adapter = calendar.slice(calendar.indexOf('    async function openTaskEditor()'), calendar.indexOf('    function navigatePeriod('));
assert.ok(adapter.startsWith('    async function openTaskEditor()'));
const binding = calendar.match(/openEditorButton\?\.addEventListener\('click', openTaskEditor\);/);
assert.ok(binding, 'Calendar button must use the shared-route adapter');

function environment(preference, type, failDetails = false, hostPreference = true, scriptPreference = false, dateListPreference = false) {
    const navigations = [], native = [], requests = [], statuses = [], intervals = new Map();
    const popup = {closed:false};
    const element = { dataset: {
        useMauiTaskEditor: String(preference), useMauiHostEditor: String(hostPreference), useMauiScriptEditor: String(scriptPreference), useMauiDateListEditor: String(dateListPreference),
        editorPrepareDateListEditUrl: "/maui/Resources/PrepareDateListEdit", editorMarkDateListEditedUrl: "/maui/Resources/MarkDateListEdited",
        editorPrepareScriptEditUrl: "/maui/Resources/PrepareScriptEdit", editorMarkScriptEditedUrl: "/maui/Resources/MarkScriptEdited",
        editorTaskDetailsUrl: '/maui/Tasks/Details', editorTasksIndexUrl: '/maui/Tasks', editorHostsIndexUrl: '/maui/Hosts',
        editorPrepareTaskEditUrl: '/maui/Tasks/PrepareTaskEdit', editorMarkTaskEditedUrl: '/maui/Tasks/MarkTaskEdited',
        editorPrepareHostEditUrl: '/maui/Hosts/PrepareHostEdit', editorMarkHostEditedUrl: '/maui/Hosts/MarkHostEdited',
        editorNativeSessionUrl: '/maui/NativeEditor/Session', editorLoginUrl: '/maui/Account/SessionExpired'
    }};
    const window = {
        location: { href: 'https://maui.example/maui/Tasks/Calendar', assign: url => navigations.push(url) },
        setInterval: callback => { const key=intervals.size+1; intervals.set(key,callback); return key; }, clearInterval:key=>intervals.delete(key),
        MauiNativeEditor: {
            reserve: () => popup,
            open: async options => { native.push(options); return true; }
        }
    };
    const state = {selectedEvent:{taskId:'42',taskName:'Calendar Task'},taskDetailsCache:new Map([['42',{}]])};
    const button = {addEventListener: (name, handler) => { assert.equal(name,'click'); button.click=handler; }};
    const context = {
        document:{documentElement:element,querySelector:()=>null}, window, URL, console:{error:message=>statuses.push(message),info:message=>statuses.push(message)},
        Headers:class {set() {}}, state, openEditorButton:button, showStatus:message=>statuses.push(message),
        fetch:async (url,options={}) => {
            const pathname = new URL(url,window.location.href).pathname; requests.push({pathname,options});
            const details = pathname.endsWith('/Details');
            return {ok:!(details && failDetails),status:details && failDetails?400:200,
                headers:{get:()=> 'application/json'},json:async()=>details
                    ? {task:{name:'Resolved Task',type},message:'Task type unavailable'} : {taskName:'Resolved Task'}};
        }
    };
    vm.runInNewContext(routing,context,{filename:'editor-routing.js'});
    vm.runInNewContext(adapter+'\n'+binding[0],context,{filename:'task-calendar.js'});
    return {window,state,button,navigations,native,requests,statuses,intervals,popup};
}
(async()=>{
    for (const preference of [true,false]) {
        for (const type of ['Traditional','Synchronization','Advanced']) {
            const e=environment(preference,type);
            assert.equal(await e.button.click(),true);
            const maui=preference;
            assert.equal(e.native.length,maui?0:1,`${preference}/${type} native routing`);
            assert.equal(e.navigations.length,maui?1:0,`${preference}/${type} MAUI routing`);
            assert.equal(e.state.taskDetailsCache.has('42'),false);
            if(maui) {
                const url=new URL(e.navigations[0]);
                assert.equal(url.pathname,'/maui/Tasks');
                assert.equal(url.searchParams.get('editorTaskId'),'42');
                assert.equal(url.searchParams.get('editorTaskType'),type);
                assert.equal(url.searchParams.get('editorTaskName'),'Resolved Task');
            } else {
                assert.equal(e.native[0].returnHash,'#/tasks/details/42?HideNav');
                assert.equal(e.native[0].sessionUrl,'/maui/NativeEditor/Session');
            }
            assert.equal(e.requests.filter(r=>r.pathname.endsWith('/PrepareTaskEdit')).length,maui?0:1);
        }
    }
    const e=environment(true,'Traditional');
    assert.equal(await e.window.MauiEditorRouting.openHost('17','Example'),true);
    assert.equal(new URL(e.navigations[0]).pathname,'/maui/Hosts');
    let returned=0;
    const inPlace=environment(true,'Traditional');
    assert.equal(await inPlace.window.MauiEditorRouting.openHost('17','Example',{inPlace:true,onClosed:()=>returned++}),false);
    assert.equal(inPlace.navigations.length,0); assert.equal(returned,1);
    const nativeHost=environment(true,'Traditional',false,false);
    assert.equal(await nativeHost.window.MauiEditorRouting.openHost('17','Example',{inPlace:true,onClosed:()=>returned++}),true);
    assert.equal(nativeHost.navigations.length,0); assert.equal(returned,1);
    assert.equal(nativeHost.native[0].returnHash,'#/hosts/17?HideNav');
    assert.equal(nativeHost.requests[0].pathname,'/maui/Hosts/PrepareHostEdit');
    const monitor=[...nativeHost.intervals.values()][0]; monitor(); assert.equal(returned,1);
    nativeHost.popup.closed=true; monitor(); assert.equal(returned,2); assert.equal(nativeHost.intervals.size,0);
    e.state.selectedEvent=null;
    assert.equal(await e.button.click(),false);
    const failed=environment(true,'Traditional',true);
    assert.equal(await failed.button.click(),false);
    assert.equal(failed.native.length,0); assert.equal(failed.navigations.length,0);
    const missing=environment(true,'Traditional'); missing.window.MauiEditorRouting=null;
    assert.equal(await missing.button.click(),false); assert.equal(missing.statuses.length,1);
    for(const preference of [true,false])for(const isBuiltIn of [true,false]) {
        const scripts=environment(true,'Traditional',false,true,preference), opened=[];
        scripts.window.MauiScriptEditor={open:async id=>{opened.push(id);return true;}};
        assert.equal(await scripts.window.MauiEditorRouting.openScript('100001','Example',{isBuiltIn}),true);
        assert.equal(opened.length,preference?1:0,'All Scripts honor MAUI preference');
        assert.equal(scripts.native.length,preference?0:1);
        assert.equal(scripts.requests.filter(r=>r.pathname.endsWith('/PrepareScriptEdit')).length,!preference&&!isBuiltIn?1:0);
        assert.equal(scripts.requests.filter(r=>r.pathname.endsWith('/MarkScriptEdited')).length,!preference&&!isBuiltIn?1:0);
        if(!preference)assert.equal(scripts.native[0].disableUndo,isBuiltIn);
    }
    for (const preference of [true,false]) {
        const dateLists = environment(true,'Traditional',false,true,false,preference), opened = [];
        dateLists.window.MauiDateListEditor = {open:async id => { opened.push(id); return true; }};
        assert.equal(await dateLists.window.MauiEditorRouting.openDateList('42','Annual Dates'),true);
        assert.equal(opened.length,preference ? 1 : 0);
        assert.equal(dateLists.native.length,preference ? 0 : 1);
        assert.equal(dateLists.requests.filter(r => r.pathname.endsWith('/PrepareDateListEdit')).length,preference ? 0 : 1);
        if (!preference) {
            assert.equal(dateLists.native[0].returnHash,'#/resources/datelists/42?HideNav');
            assert.equal(dateLists.native[0].sessionUrl,'/maui/NativeEditor/Session');
            assert.deepEqual(JSON.parse(dateLists.requests[0].options.body),{dateListId:'42'});
        }
    }
    for(const type of ['Task','Host','Script','DateList']) {
        const readonly=environment(false,'Traditional',false,false,false,false);
        readonly.window.MauiObjectAccess={read:async()=>({canEdit:false,canRun:false,canDelete:false})};
        assert.equal(await readonly.window.MauiEditorRouting['open'+type]('42','Visible'),true);
        assert.equal(readonly.requests.length,0,type+' inspection does not capture history or mark modification');
        assert.equal(readonly.native[0].disableUndo,true);
    }
    for (const file of ['appsettings.custom.json','appsettings.template.json']) {
        const setting = JSON.parse(load(file)).Editors.UseMauiDateListEditor;
        assert.equal(setting.Value,false); assert.equal(setting.Experimental,true);
    }
    // Drive the production native handshake with an originating editor still open.
    // Legacy message options must never create a MAUI banner, even if a caller supplies them.
    const nativeSource=load('wwwroot/js/native-editor.js');
    function bridge({blocked=false,failed=false}={}) {
        const events={},notifications=[],posted=[];
        const popup={closed:false,document:{title:''},location:{replace:url=>popup.url=url},focus(){},
            close(){this.closed=true;},postMessage:(data,origin)=>posted.push({data,origin})};
        const window={location:{origin:'https://maui.example',replace(){}},crypto:{randomUUID:()=> 'launch-id'},
            addEventListener:(name,handler)=>events[name]=handler,setTimeout:()=>1,clearTimeout(){},clearInterval(){},
            mauiAppUrl:path=>'/maui'+path};
        const response={ok:true,status:200,headers:{get:()=> 'application/json'},
            json:async()=>({webAdminUrl:'https://moveit.example',username:'admin',password:'fixture-only'})};
        vm.runInNewContext(nativeSource,{window,URL,Headers,console:{info(){},warn(){}},document:{},
            fetch:async()=>{if(failed)throw new Error('Native session unavailable');return response;}});
        const options={popup:blocked?{closed:true}:popup,sessionUrl:'/maui/NativeEditor/Session',
            status:(...args)=>notifications.push(args),openingMessage:'Opening',submittingMessage:'Signing in',openedMessage:'Opened'};
        const post=(type,hash,origin='https://moveit.example')=>events.message({source:popup,origin,data:{type,launchId:'launch-id',hash,message:'Native login failed'}});
        return {window,popup,notifications,posted,options,post};
    }
    for(const hash of ['#/tasks/details/42?HideNav','#/hosts/17?HideNav','#/resources/scripts/100001?HideNav','#/resources/datelists/42?HideNav','#/resources/permissions']) {
        const b=bridge();
        assert.equal(await b.window.MauiNativeEditor.open({...b.options,returnHash:hash}),true);
        b.post('MOVEIT_READY',hash);b.post('MOVEIT_LOGIN_RECEIVED',hash);b.post('MOVEIT_LOGIN_SUBMITTING',hash);
        b.post('MOVEIT_ROUTE_RESTORED',hash);b.post('MAUI_EDITOR_READY',hash);
        assert.equal(b.notifications.length,0,'A successful native launch must leave the MAUI editor/page untouched: '+hash);
        assert.ok(b.posted.some(item=>item.data.type==='MOVEIT_LOGIN'));
        assert.ok(b.posted.some(item=>item.data.type==='MAUI_EDITOR_CONFIG'));
        assert.equal(b.popup.closed,false);
    }
    for(const failure of ['MOVEIT_LOGIN_ERROR','MOVEIT_SKIN_ERROR']) {
        const b=bridge(),hash='#/hosts/17?HideNav';
        await b.window.MauiNativeEditor.open({...b.options,returnHash:hash});
        b.post(failure,hash,'https://untrusted.example');assert.equal(b.notifications.length,0);
        b.post(failure,hash);assert.equal(b.notifications.length,1);assert.equal(b.notifications[0][2],'error');
    }
    for(const flags of [{blocked:true},{failed:true}]) {
        const b=bridge(flags);assert.equal(await b.window.MauiNativeEditor.open({...b.options,returnHash:'#/hosts/17?HideNav'}),false);
        assert.equal(b.notifications.length,1);assert.equal(b.notifications[0][2],'error');
    }
    const footerErrors=[];
    nativeHost.window.MauiModalUI={showMessage:(message,kind)=>{footerErrors.push({message,kind});return true;}};
    nativeHost.native[0].status('Native launch failed',5000,'error');
    assert.deepEqual(footerErrors,[{message:'Native launch failed',kind:'error'}],'Launch errors use the registered modal error surface');
    console.log('PASS Calendar/shared editor routing, retained Host return context, silent native handshakes and inline launch failures.');
})().catch(error=>{console.error(error);process.exitCode=1;});
