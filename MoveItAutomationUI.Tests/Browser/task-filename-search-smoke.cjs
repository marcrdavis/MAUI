// Execute the production on-demand module with deterministic timers and interrupted reads.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const root = path.resolve(__dirname, '../..'), read = p => fs.readFileSync(path.join(root,p),'utf8');
const source = read('wwwroot/js/task-filename-search.js');
function fixture() {
    let clock=1000, sequence=0, changes=0, expired=0, manager;
    const timers=new Map(), requests=[], events={}, windowEvents={};
    const toast={}, toasts=[];
    const search={value:''}, grid={dataset:{taskFilenameSearchUrl:'/maui/TaskFilenameSearch'},querySelector:selector=>{assert.equal(selector,'[data-prototype-toast]');return toast;},addEventListener:(name,fn)=>events[name]=fn};
    const window={addEventListener:(name,fn)=>windowEvents[name]=fn,mauiSessionExpired:()=>expired++,mauiDisplayToast:(surface,message,kind,duration,origin)=>toasts.push({surface,message,kind,duration,origin})};
    vm.runInNewContext(source,{window,AbortController,Map,Date:{now:()=>clock},setTimeout:(fn,delay)=>{const id=++sequence;timers.set(id,{fn,at:clock+delay});return id;},clearTimeout:id=>timers.delete(id),fetch:(url,options)=>new Promise((resolve,reject)=>requests.push({url,options,resolve,reject}))});
    manager=window.MauiTaskFilenameSearch.create({grid,search,changed:()=>{changes++;manager.ensure();}});
    const flush=async()=>{for(let i=0;i<8;i++)await Promise.resolve();};
    return {manager,search,toasts,toast,requests,events,windowEvents,grid,window,flush,get changes(){return changes;},get expired(){return expired;},
        type(value){search.value=value;manager.ensure();},
        async tick(ms){clock+=ms;for(const [id,timer]of [...timers])if(timer.at<=clock){timers.delete(id);timer.fn();}await flush();},
        async reply(index,items,extra={}){requests[index].resolve({status:200,ok:true,json:async()=>({success:true,items}),...extra});await flush();}};
}
(async()=>{
 const f=fixture();f.manager.ensure();await f.tick(10000);assert.equal(f.requests.length,0,'empty grid load/refresh must not read filenames');
 f.type('in');await f.tick(100);f.type('invoice');await f.tick(299);assert.equal(f.requests.length,0,'debounce typing');
 assert.equal(f.toasts.length,0,'loading stays silent');await f.tick(1);assert.equal(f.requests.length,1);
 assert.equal(f.requests[0].url,'/maui/TaskFilenameSearch');assert.equal(f.requests[0].options.method,'GET');assert.equal(f.requests[0].options.cache,'no-store');assert.equal(f.requests[0].options.credentials,'same-origin');
 for(let i=0;i<20;i++)f.manager.ensure();assert.equal(f.toasts.length,0,'normal loading renders stay silent');
 f.type('*.txt');await f.tick(1000);assert.equal(f.requests.length,1,'query-independent bulk read stays single-flight');
 await f.reply(0,[{taskId:'1',text:'*.TXT\n[OrigName].pgp'},{taskId:'2',text:'invoice_*.csv'}]);
 assert.equal(f.manager.text('1'),'*.txt\n[origname].pgp');assert.equal(f.toasts.length,0,'successful search stays silent');
 f.type('invoice');for(let i=0;i<20;i++)f.manager.ensure();await f.tick(1000);assert.equal(f.requests.length,1,'cache survives ordinary grid refreshes');
 for(let i=0;i<20;i++)f.manager.ensure();assert.equal(f.toasts.length,0,'cached renders stay silent');
 f.type('');assert.equal(f.toasts.length,0);assert.equal(f.manager.text('1').includes('*.txt'),true);
 f.type('*.txt');await f.tick(300);assert.equal(f.requests.length,1,'reuse on retyping');
 await f.tick(5*60*1000);f.manager.ensure();await f.tick(300);assert.equal(f.requests.length,2,'expired cache refreshes only during active search');
 f.type('');assert.equal(f.requests[1].options.signal.aborted,true);await f.reply(1,[{taskId:'1',text:'stale'}]);assert.notEqual(f.manager.text('1'),'stale');assert.equal(f.toasts.length,0,'cancelled responses must not notify');
 f.type('next');await f.tick(300);assert.equal(f.requests.length,3);f.events['maui:task-filename-search-invalidated']();await f.tick(300);assert.equal(f.requests.length,4);assert.equal(f.requests[2].options.signal.aborted,true);
 await f.reply(3,[{taskId:'1',text:'new.csv'}]);await f.reply(2,[{taskId:'1',text:'old.csv'}]);assert.equal(f.manager.text('1'),'new.csv','invalidated read cannot replace newer result');
 f.windowEvents.pagehide();assert.equal(f.manager.text('1'),'');f.windowEvents.pageshow({persisted:true});await f.tick(300);assert.equal(f.requests.length,5,'BFCache return can search again');await f.reply(4,[]);assert.equal(f.toasts.length,0);
 // Restore after failure as well as after success: a previous failed-read latch must not strand the page.
 const back=fixture();back.type('*.txt');await back.tick(300);await back.reply(0,[],{ok:false,status:500});
 assert.equal(back.toasts.length,1);back.windowEvents.pagehide();back.windowEvents.pageshow({persisted:true});await back.tick(300);
 assert.equal(back.requests.length,2,'BFCache return retries an active search after a previous failure');
 await back.reply(1,[{taskId:'1',text:'restored.txt'}]);assert.equal(back.manager.text('1'),'restored.txt');assert.equal(back.toasts.length,1,'successful restoration stays silent');
 back.type('');back.windowEvents.pagehide();back.windowEvents.pageshow({persisted:true});await back.tick(300);assert.equal(back.requests.length,2,'restored empty search does not start a read');
 // Failure recovery also supports changing the query and clearing/retyping it, without Refresh Now.
 for(const retryMode of ['changed','cleared']){
  const recovery=fixture();recovery.type('*.txt');await recovery.tick(300);await recovery.reply(0,[],{ok:false,status:500});
  if(retryMode==='cleared')recovery.type('');recovery.type(retryMode==='changed'?'invoice':'*.txt');await recovery.tick(300);
  assert.equal(recovery.requests.length,2,retryMode);await recovery.reply(1,[{taskId:'1',text:'recovered.txt'}]);assert.equal(recovery.manager.text('1'),'recovered.txt');assert.equal(recovery.toasts.length,1);
 }
 // A transport may finish after abort. Obsolete responses must not trigger sign-in or parse large bodies.
 const stale=fixture();stale.type('old');await stale.tick(300);stale.type('');let staleBodies=0;
 await stale.reply(0,[],{status:401,ok:false,json:async()=>{staleBodies++;return {};}});
 assert.equal(stale.expired,0,'obsolete unauthorized response must not expire the current session');assert.equal(staleBodies,0);
 stale.type('next');await stale.tick(300);let finishJson;
 stale.requests[1].resolve({ok:true,status:200,json:()=>new Promise(resolve=>{finishJson=resolve;})});await stale.flush();
 stale.events['maui:task-filename-search-invalidated']();await stale.tick(300);await stale.reply(2,[{taskId:'1',text:'current.txt'}]);
 finishJson({success:true,get items(){staleBodies++;return [{taskId:'1',text:'obsolete.txt'}];}});await stale.flush();
 assert.equal(staleBodies,0,'invalidated body must not be indexed after JSON completes');assert.equal(stale.manager.text('1'),'current.txt');
 for(const failure of ['http','json','network','missing','duplicate','badType','session']){
  const t=fixture();t.type('*.txt');await t.tick(300);
  if(failure==='network')t.requests[0].reject(new Error('Disconnected'));
  else if(failure==='http'||failure==='session')await t.reply(0,[],{ok:false,status:failure==='session'?401:500});
  else if(failure==='json')await t.reply(0,[],{json:async()=>{throw new Error('Malformed JSON');}});
  else await t.reply(0,failure==='missing'?null:failure==='duplicate'?[{taskId:'1',text:'a'},{taskId:'1',text:'b'}]:[{taskId:'1',text:null}]);
  await t.flush();assert.equal(t.toasts.length,1,failure);assert.equal(t.manager.text('1'),'');
  assert.equal(t.toasts[0].surface,t.toast);assert.equal(t.toasts[0].kind,'error');assert.equal(t.toasts[0].origin,'page');assert.equal(t.toasts[0].duration,5000);assert.match(t.toasts[0].message,/task details only/);assert.match(t.toasts[0].message,/Refresh Now to retry/);
  t.manager.ensure();await t.tick(5000);assert.equal(t.requests.length,1,'failures must not spin/retry on normal refresh');assert.equal(t.toasts.length,1,'ordinary renders must not repeat the error toast');
  if(failure==='session')assert.equal(t.expired,1);
  t.events['maui:task-filename-search-invalidated']();await t.tick(300);await t.reply(1,[{taskId:'1',text:'retry.txt'}]);assert.equal(t.toasts.length,1);assert.equal(t.manager.text('1'),'retry.txt');
 }
 // Use the real grid's search text and expression parsing: masks remain literal and OR/NOT retain semantics.
 const core=read('wwwroot/js/grid.js');
 const extraction=core.slice(core.indexOf('        function getRowSearchText('),core.indexOf('        function resolveDateMacro('));
 const textFilters=core.slice(core.indexOf('        function splitFilterAlternatives('),core.indexOf('        // Parses numeric comparison filters'));
 const context={searchColumns:['Name'],layout:{getColumnOrder:()=>['Name']},table:{querySelector:()=>null},cssEscape:x=>x,filenameSearch:{text:id=>id==='1'?'*.txt\n[yyyy]file.csv':''}};
 vm.createContext(context);vm.runInContext(extraction+textFilters+'\nthis.match=(name,id,q)=>matchesTextFilter(getRowSearchText({dataset:{rowId:id},querySelector:()=>({textContent:name})}),parseFilterExpression(q,parseTextFilter));',context);
 assert.equal(context.match('Other task','1','*.TXT'),true);assert.equal(context.match('Other task','1','invoice.txt'),false);assert.equal(context.match('Other task','1','!*.txt'),false);assert.equal(context.match('Other task','1','absent, *.txt'),true);assert.equal(context.match('Name match','2','Name'),true);
 const view=read('Views/Shared/_DataGrid.cshtml');assert.match(view,/Model.GridId == \"tasks\" && !isReportEditRequest/);assert.doesNotMatch(view,/data-task-filename-(?:spinner|message|status|error|retry)|task-filename-search-(?:box|row)/);assert.match(view,/Url.Action\("Index", "TaskFilenameSearch"\)/);
 assert.doesNotMatch(read('wwwroot/css/site.css'),/task-filename-search-/);assert.doesNotMatch(source,/Searching filenames|feedbackKind|data-task-filename-spinner/);
 assert.ok(read('Views/Shared/_Layout.cshtml').indexOf('task-filename-search.js')<read('Views/Shared/_Layout.cshtml').indexOf('~/js/grid.js'));
 console.log('PASS Task filename search: no eager reads, debounce, cache, expiry, abort/stale body/session races, silent loading, one page-error toast per failed read, BFCache, failures/retry, literal masks and grid OR/NOT semantics.');
})().catch(error=>{console.error(error);process.exitCode=1;});
