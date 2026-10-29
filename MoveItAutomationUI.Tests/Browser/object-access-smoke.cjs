// Exercise production permission UI with authenticated read responses; no MOVEit writes.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const source=fs.readFileSync(path.resolve(__dirname,'../../wwwroot/js/object-access.js'),'utf8');
const control=(action,disabled=false)=>({dataset:{taskRowCommand:action},disabled,title:'original',textContent:action,matches:()=>false});
(async()=>{
 let reads=[],allow=false,messages=[];
 const creation={isAdmin:false,groups:{Task:['Task Group'],Host:[]}};
 const document={documentElement:{dataset:{objectAccessUrl:'/ObjectAccess/Object',objectAccessBatchUrl:'/ObjectAccess/Objects',objectCreationAccessUrl:'/ObjectAccess/Creation'}},readyState:'loading',
  querySelector:selector=>selector==='[data-object-creation-access]'?{textContent:JSON.stringify(creation)}:null,addEventListener(){},querySelectorAll:()=>[]};
 const window={MauiModalUI:{showMessage:message=>messages.push(message)}};
 const context={document,window,URL,location:{href:'https://maui.test/'},console,fetch:async url=>{
  reads.push(url.pathname);
  const payload=url.pathname.endsWith('Creation')?creation:url.pathname.endsWith('Objects')?Object.fromEntries(url.searchParams.getAll('ids').map(id=>[id,{canEdit:id!=='denied',canDelete:false,canRun:true}])):{canEdit:allow,canDelete:false,canRun:true};
  return {ok:true,json:async()=>payload};
 }};
 vm.createContext(context);vm.runInContext(source,context);const api=window.MauiObjectAccess;
 assert.equal(api.canCreate('Task'),true);assert.equal(api.canCreate('Host'),false);
 assert.equal(await api.mayEdit('Task','visible'),false);assert.equal(messages.length,1);
 const edit=control('edit'),del=control('delete'),clone=control('clone'),undo=control('undo',true),history=control('run-history');
 const buttons=[edit,del,clone,undo,history];const menu={querySelectorAll:selector=>selector.includes('base-disabled')?buttons.filter(button=>'objectAccessBaseDisabled'in button.dataset):buttons};
 await api.menu(menu,'Task','1');assert.equal(edit.disabled,true);assert.equal(history.disabled,false);
 api.reset(menu);assert.equal(edit.disabled,false);assert.equal(edit.title,'original');
 allow=true;await api.menu(menu,'Task','2');assert.equal(edit.disabled,false);assert.equal(clone.disabled,false);assert.equal(del.disabled,true);assert.equal(undo.disabled,true);
 // Edit Existing remains sufficient for all edits; Delete and Clone/New stay independent.
 api.reset(menu);assert.equal(del.disabled,false);assert.equal(undo.disabled,true);
 const exp=control('');exp.dataset.gridAction='Export Tasks';const bulkMenu={querySelectorAll:()=>[exp]};
 await api.bulk({dataset:{grid:'tasks'}},bulkMenu,['1','denied']);assert.equal(exp.disabled,true);
 assert.ok(reads.includes('/ObjectAccess/Objects'));api.reset(bulkMenu);
 await api.bulk({dataset:{grid:'tasks'}},bulkMenu,['1','2']);assert.equal(exp.disabled,false);
 const json=control('');json.dataset.gridAction='JSON Editor';
 await api.bulk({dataset:{grid:'tasks'}},{querySelectorAll:()=>[json]},['1']);assert.equal(json.disabled,false,'Edit Existing can use JSON Editor');
 await api.bulk({dataset:{grid:'tasks'}},{querySelectorAll:()=>[json]},['denied']);assert.equal(json.disabled,true,'Read-only access cannot use JSON Editor');
 const createGroup=control('');createGroup.dataset.gridAction='Create Resource Group';
 const addGroup=control('');addGroup.dataset.gridAction='Add To Resource Group';
 await api.bulk({dataset:{grid:'tasks'}},{querySelectorAll:()=>[createGroup,addGroup]},['1']);
 assert.equal(createGroup.disabled,true);assert.equal(addGroup.disabled,true);
 // Exercise the real shared selection manager, not just disabled menu commands.
 vm.runInContext(fs.readFileSync(path.resolve(__dirname,'../../wwwroot/js/grid-view-state.js'),'utf8'),context);
 function gridCase(type,actions,ids) {
  const buttons=actions.map(action=>({...control(''),dataset:{gridAction:action}}));
  const menu={querySelectorAll:()=>buttons};
  const grid={dataset:{grid:type},querySelector:()=>({closest:()=>menu})};
  // No inventory permission flags: eligibility must come from effective access.
  const rows=ids.map(id=>{const checkbox={dataset:{rowSelectionBlocked:'false'},disabled:false,checked:false,setAttribute(name,value){this[name]=value;}};return {dataset:{rowId:id},querySelector:()=>checkbox,classList:{toggle(){}},hidden:false};});
  const pageSelect={},bulkButton={},selectionCount={};
  const manager=window.MauiGridViewState.createManager({grid,pageSelect,bulkButton,selectionCount,initialPageSize:10,defaultPageSize:10,
   getRows:()=>rows,isRowSelectable:row=>!row.querySelector().disabled,canUseBulkActions:ids=>api.canBulk(grid,ids)});
  const changed=()=>{manager.reconcileRows();manager.render(rows);};
  return {grid,rows,pageSelect,bulkButton,manager,changed};
 }
 const taskGrid=gridCase('tasks',['Enable Tasks','Delete Tasks','Export Tasks','Create Resource Group','Add to Resource Group'],['denied']);
 const pending=api.selection(taskGrid.grid,taskGrid.rows,taskGrid.changed);
 assert.equal(taskGrid.pageSelect.disabled,true,'Selection waits for effective access');
 await pending;
 assert.equal(taskGrid.rows[0].querySelector().disabled,true,'Run-only access grants no action in this Task bulk menu');
 taskGrid.manager.selectRowsForHeaderCheckbox(true,taskGrid.rows);
 taskGrid.manager.setRowSelected('denied',true,taskGrid.rows);
 assert.equal(taskGrid.manager.getSelectedIds().length,0);assert.equal(taskGrid.bulkButton.disabled,true);
 for(const type of ['datelists','scripts','hosts','keysandcertificates','globalparameters']) {
  const item=gridCase(type,['Export '+type,'Delete '+type],['denied']);await api.selection(item.grid,item.rows,item.changed);
  assert.equal(item.pageSelect.disabled,true,type+' Select All disabled');assert.equal(item.rows[0].querySelector().disabled,true,type+' row disabled');assert.equal(item.bulkButton.disabled,true);
 }
 const allowedGrid=gridCase('tasks',['Enable Tasks','Export Tasks','Delete Tasks'],['1','denied']);
 await api.selection(allowedGrid.grid,allowedGrid.rows,allowedGrid.changed);
 assert.equal(allowedGrid.rows[0].querySelector().disabled,false);assert.equal(allowedGrid.rows[0].querySelector()['aria-label'],'Select row');assert.equal(allowedGrid.rows[1].querySelector().disabled,true);
 allowedGrid.manager.selectRowsForHeaderCheckbox(true,allowedGrid.rows);
 assert.deepEqual(Array.from(allowedGrid.manager.getSelectedIds()),['1']);assert.equal(allowedGrid.bulkButton.disabled,false);
 const blockedGrid=gridCase('scripts',['Export Scripts'],['1']);blockedGrid.rows[0].querySelector().dataset.rowSelectionBlocked='true';
 await api.selection(blockedGrid.grid,blockedGrid.rows,blockedGrid.changed);assert.equal(blockedGrid.pageSelect.disabled,true,'Built-ins stay structurally excluded');
 const emptyGrid=gridCase('tasks',[],['1']);await api.selection(emptyGrid.grid,emptyGrid.rows,emptyGrid.changed);assert.equal(emptyGrid.pageSelect.disabled,true,'No configured actions means no selection');
 // A permission refresh preserves a selection only after its access is re-confirmed.
 await api.selection(allowedGrid.grid,allowedGrid.rows,allowedGrid.changed);assert.deepEqual(Array.from(allowedGrid.manager.getSelectedIds()),['1']);
 const input=control('');const save={...control('Save'),hidden:false,matches:selector=>selector==='button'};const dialog={querySelectorAll:()=>[input,save]};
 for(const type of ['Task','Host','Script','DateList','KeyCertificate','GlobalParameter']) {
  allow=false;await api.information(dialog,type,'1');assert.equal(input.disabled,true);assert.equal(save.hidden,true,type+' read-only Save is hidden');
  api.clearInformation(dialog);assert.equal(save.hidden,false,'Opening another object clears the visibility denial');
  allow=true;await api.information(dialog,type,'2');assert.equal(input.disabled,false);assert.equal(save.hidden,false,type+' editable Save is restored');
 }
 save.hidden=true;allow=true;await api.information(dialog,'KeyCertificate','2');assert.equal(save.hidden,true,'Permission checks preserve no-fields or Tasks-tab hiding');

 // Failed reads must not leave write affordances enabled, and late grid responses
 // must not undo a newer denial after refresh replaces the permission generation.
 const originalFetch=context.fetch;
 context.fetch=async()=>{throw new Error('Permissions unavailable');};
 const unavailable=gridCase('tasks',['Export Tasks','Delete Tasks'],['1']);
 await api.selection(unavailable.grid,unavailable.rows,unavailable.changed);
 assert.equal(unavailable.rows[0].querySelector().disabled,true);assert.equal(unavailable.bulkButton.disabled,true);
 await api.information(dialog,'Task','1');assert.equal(save.hidden,true);assert.equal(input.disabled,true);
 const waiters=[];
 context.fetch=async()=>await new Promise(resolve=>waiters.push(resolve));
 const refreshed=gridCase('tasks',['Export Tasks'],['1']);
 const oldRead=api.selection(refreshed.grid,refreshed.rows,refreshed.changed);
 const newRead=api.selection(refreshed.grid,refreshed.rows,refreshed.changed);
 const response=canEdit=>({ok:true,json:async()=>({'1':{canEdit,canDelete:false,canRun:false}})});
 waiters[1](response(false));await newRead;
 waiters[0](response(true));await oldRead;
 assert.equal(refreshed.rows[0].querySelector().disabled,true,'A stale grant cannot replace the latest denial');
 assert.equal(refreshed.pageSelect.disabled,true);assert.equal(refreshed.bulkButton.disabled,true);
 context.fetch=originalFetch;

 console.log('PASS effective creation grants, denied routing, mixed bulk access, menu reset, read-only Information and Edit Existing behavior.');
})().catch(error=>{console.error(error);process.exitCode=1;});
