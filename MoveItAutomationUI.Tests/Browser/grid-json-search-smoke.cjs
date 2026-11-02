'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const root=path.resolve(__dirname,'../..'),read=file=>fs.readFileSync(path.join(root,file),'utf8');
function fixture(kind='tasks',ids=['1','2']) {
 let clock=1000,serial=0,changes=0,expired=0,manager;
 const timers=new Map(),requests=[],events={},windowEvents={},toasts=[],summary={textContent:''};
 const search={value:'',validity:'',setCustomValidity(value){this.validity=value;}};
 const grid={dataset:{grid:kind,jsonSearchUrl:'/maui/ObjectJsonSearch?type='+(kind==='tasks'?'Task':'Host')},querySelector:s=>s==='[data-pager-summary]'?summary:{},addEventListener:(name,fn)=>events[name]=fn};
 const window={location:{href:'https://maui.test/maui/Tasks'},addEventListener:(name,fn)=>windowEvents[name]=fn,mauiSessionExpired:()=>expired++,mauiDisplayToast:(...args)=>toasts.push(args)};
 const context={window,AbortController,URL,Map,Set,Date:{now:()=>clock},setTimeout:(fn,delay)=>{const id=++serial;timers.set(id,{fn,at:clock+delay});return id;},clearTimeout:id=>timers.delete(id),fetch:(url,options)=>new Promise((resolve,reject)=>requests.push({url,options,resolve,reject}))};
 vm.runInNewContext(read('wwwroot/js/shared/json-search.js'),context);vm.runInNewContext(read('wwwroot/js/grid-json-search.js'),context);
 manager=window.MauiGridJsonSearch.create({grid,search,getIds:()=>ids,changed:()=>{changes++;manager.ensure();manager.renderStatus();}});
 const flush=async()=>{for(let i=0;i<40;i++)await Promise.resolve();};
 return {manager,search,requests,events,windowEvents,toasts,summary,ids,window,flush,get changes(){return changes;},get expired(){return expired;},
  type(value){search.value=value;manager.ensure();},
  async tick(ms){clock+=ms;for(const[id,t]of [...timers])if(t.at<=clock){timers.delete(id);t.fn();}await flush();},
  async reply(index,values,extra={}){const id=new URL(requests[index].url).searchParams.get('id');requests[index].resolve({ok:true,status:200,json:async()=>({success:true,document:{id,values}}),...extra});await flush();}};
}
const leaf=(path,value)=>({path:Array.isArray(path)?path:[path],value});
(async()=>{
 const f=fixture();for(const q of ['', 'DoIfFailure=1','json value',' JSON:Port=22']){f.type(q);await f.tick(1000);assert.equal(f.manager.active(),false);}
 assert.equal(f.requests.length,0,'ordinary searches never request definitions');
 f.type('JsOn:DoIfFailure=1');await f.tick(299);assert.equal(f.requests.length,0);await f.tick(1);assert.equal(f.requests.length,2);
 assert.match(f.requests[0].url,/\/maui\/ObjectJsonSearch\?type=Task&id=1/);assert.equal(f.requests[0].options.cache,'no-store');assert.equal(f.requests[0].options.credentials,'same-origin');
 await f.reply(0,[leaf(['steps','0','If','DoIfFailure'],1)]);assert.equal(f.manager.matches('1'),false,'do not report partial search results');assert.match(f.summary.textContent,/1 of 2/);
 await f.reply(1,[leaf('DoIfFailure',0)]);assert.equal(f.manager.matches('1'),true);assert.equal(f.manager.matches('2'),false);
 for(const [q,one,two]of [['DoIfFailure!=1',false,true],['DoIfFailure<=0',false,true],['DoIfFailure<1',false,true],['DoIfFailure>=1',true,false],['DoIfFailure>0',true,false],['steps[*].If.DoIfFailure=1',true,false],['Missing!=1',false,false],['DoIfFailure',true,true]]){
  f.type('JSON:'+q);await f.tick(300);assert.equal(f.manager.matches('1'),one,q);assert.equal(f.manager.matches('2'),two,q);
 }
 assert.equal(f.requests.length,2,'changed expressions reuse redacted values');
 for(const q of ['JSON:','JSON:DoIfFailure=','JSON:=1','JSON:Name contains ']){f.type(q);await f.tick(500);assert.ok(f.search.validity,q);assert.equal(f.manager.matches('1'),false);}
 assert.equal(f.requests.length,2);f.type('normal');assert.equal(f.search.validity,'');
 f.type('JSON:DoIfFailure=1');await f.tick(300);f.events['maui:task-filename-search-invalidated']();await f.tick(300);assert.equal(f.requests.length,4);
 await f.reply(2,[leaf('DoIfFailure',0)]);await f.reply(3,[leaf('DoIfFailure',1)]);assert.equal(f.manager.matches('1'),false);assert.equal(f.manager.matches('2'),true,'save/Refresh invalidates cached matches');
 await f.tick(5*60*1000);f.manager.ensure();await f.tick(300);assert.equal(f.requests.length,6,'cache expiry refreshes active JSON search');
 f.type('Alpha');assert.equal(f.requests[4].options.signal.aborted,true);let parsed=0;
 await f.reply(4,[],{status:401,ok:false,json:async()=>{parsed++;return {};}});await f.reply(5,[]);assert.equal(f.expired,0);assert.equal(parsed,0,'obsolete session responses cannot expire a current session');
 const nulls=fixture('tasks',['1']);nulls.type('JSON:Empty');await nulls.tick(300);await nulls.reply(0,[leaf('Empty',null)]);assert.equal(nulls.manager.matches('1'),true,'null element names remain searchable');nulls.type('JSON:Empty=null');await nulls.tick(300);assert.equal(nulls.manager.matches('1'),false,'null placeholders do not satisfy comparisons');
 const h=fixture('hosts');h.type('json:Host.SSHFTP.Port>=22');await h.tick(300);await h.reply(0,[leaf(['SSHFTP','Port'],22)]);await h.reply(1,[leaf(['FTP','Port'],21)]);assert.equal(h.manager.matches('1'),true);assert.equal(h.manager.matches('2'),false);
 h.type('JSON:Host contains EXAMPLE.COM');await h.tick(300);assert.equal(h.manager.matches('1'),false);
 h.events['maui:json-search-invalidated']();await h.tick(300);await h.reply(2,[leaf(['SSHFTP','Host'],'sftp.example.com')]);await h.reply(3,[leaf(['FTP','Host'],'other.test')]);assert.equal(h.manager.matches('1'),true);
 h.type('JSON:Host contains "a=b,or c"');await h.tick(300);assert.equal(h.manager.matches('1'),false,'JSON values are never split by ordinary OR/comma parsing');
 h.windowEvents.pagehide();h.windowEvents.pageshow({persisted:true});await h.tick(300);assert.equal(h.requests.length,6);await h.reply(4,[]);await h.reply(5,[]);
 const queue=fixture('tasks',Array.from({length:9},(_,i)=>String(i+1)));queue.type('JSON:Active=1');await queue.tick(300);assert.equal(queue.requests.length,4,'bound concurrent definition reads to four');
 await queue.reply(0,[leaf('Active',1)]);assert.equal(queue.requests.length,5);queue.type('');assert.ok(queue.requests.slice(1).every(r=>r.options.signal.aborted));
 for(const failure of ['http','network','malformed','identity','session']){
  const t=fixture('tasks',['1']);t.type('JSON:Active=1');await t.tick(300);
  if(failure==='network')t.requests[0].reject(new Error('Offline'));
  else if(failure==='identity')await t.reply(0,[],{json:async()=>({success:true,document:{id:'wrong',values:[]}})});
  else if(failure==='malformed')await t.reply(0,[leaf('Active',{})]);
  else await t.reply(0,[],{ok:false,status:failure==='session'?401:500});
  await t.flush();assert.equal(t.manager.matches('1'),false);assert.equal(t.toasts.length,1);assert.equal(t.toasts[0][4],'page');assert.match(t.summary.textContent,/could not be completed/);
  for(let i=0;i<10;i++)t.manager.ensure();await t.tick(1000);assert.equal(t.requests.length,1,'failure must not trigger a request loop');assert.equal(t.expired,failure==='session'?1:0);
  t.events['maui:json-search-invalidated']();await t.tick(300);await t.reply(1,[leaf('Active',true)]);assert.equal(t.manager.matches('1'),true,'explicit retry recovers');
 }
 // Exercise the production grid filter function, including intersections and ordinary-mode fallback.
 const source=read('wwwroot/js/grid.js'),filter=source.slice(source.indexOf('        function getFilteredRows()'),source.indexOf('        // Sorts a row collection'));
 const rows=['1','2'].map(id=>({dataset:{rowId:id},querySelector:()=>({dataset:{value:id},textContent:id})}));
 const c={rows,cssEscape:x=>x,search:{value:'JSON:Active=1'},jsonSearch:{active:()=>true,matches:id=>id==='2'},scopeFilters:[],filters:[],getRowSearchText:()=>{throw new Error('JSON mode must not search ordinary row text');},parseFilterExpression:()=>{throw new Error('JSON mode must not parse ordinary search');}};
 vm.runInNewContext(filter+'this.result=getFilteredRows();',c);assert.deepEqual(Array.from(c.result,row=>row.dataset.rowId),['2']);
 c.scopeFilters=[{dataset:{filterColumn:'Name',filterValue:'1'}}];vm.runInNewContext('this.result=getFilteredRows();',c);assert.equal(c.result.length,0,'scope filters intersect JSON matches');
 c.scopeFilters=[];c.filters=[{dataset:{columnFilter:'Name'},value:'1'}];c.parseFilterExpression=()=>true;c.matchesColumnFilter=()=>false;c.table={querySelector:()=>({dataset:{type:'text'}})};c.updateDateMacroHint=()=>{};c.splitFilterAlternatives=()=>[];c.parseTextFilter=()=>true;c.parseNumericFilter=()=>true;c.parseDateFilter=()=>true;
 vm.runInNewContext('this.result=getFilteredRows();',c);assert.equal(c.result.length,0,'column filters intersect JSON matches');
 c.filters=[];c.jsonSearch.active=()=>false;c.getRowSearchText=row=>row.dataset.rowId;c.matchesTextFilter=text=>text==='1';vm.runInNewContext('this.result=getFilteredRows();',c);assert.deepEqual(Array.from(c.result,row=>row.dataset.rowId),['1']);
 const view=read('Views/Shared/_DataGrid.cshtml');assert.match(view,/\(Model.GridId is "tasks" or "hosts"\) && !isReportEditRequest/);assert.match(view,/placeholder="@searchPlaceholder"/);
 const layout=read('Views/Shared/_Layout.cshtml');assert.ok(layout.indexOf('shared/json-search.js')<layout.indexOf('grid-json-search.js'));assert.ok(layout.indexOf('grid-json-search.js')<layout.indexOf('~/js/grid.js'));
 console.log('PASS grid JSON search: opt-in prefix, all operators, nested paths, Tasks/Hosts, numeric/Boolean/text parity, filters, cache/expiry/invalidation, bounded reads, debounce, invalid expressions, cancellation, stale sessions, failures/retry and ordinary-search fallback.');
})().catch(error=>{console.error(error);process.exitCode=1;});
