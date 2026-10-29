'use strict';
const assert=require('node:assert/strict');const fs=require('node:fs');const path=require('node:path');const vm=require('node:vm');
const root=path.resolve(__dirname,'../..');const read=p=>fs.readFileSync(path.join(root,p),'utf8').replace(/\r\n/g,'\n');const tasks=read('wwwroot/js/tasks.js');
// Run the production closure functions with captured DOM/transport boundaries.
function fn(source,name) {
 const match=new RegExp('    (?:async )?function '+name+'\\(').exec(source);assert.ok(match,name);
 const start=match.index;const rest=source.slice(start+match[0].length);const next=/\n    (?:async )?function /.exec(rest);
 return source.slice(start,next?start+match[0].length+next.index:source.length);
}
const context={};vm.createContext(context);vm.runInContext(fn(tasks,'formatTaskStateResult'),context);
for(const enabled of [true,false]) for(const count of [8,9,16,40]) {
 const items=Array.from({length:count},(_,i)=>({id:String(i),success:true,skipped:false}));
 let text=context.formatTaskStateResult('State',{items},enabled);assert.equal(text,`State: ${count} completed`);
 items[3].skipped=true;items[6]={id:'6',success:false,message:'Permission denied'};
 text=context.formatTaskStateResult('State',{items},enabled);
 assert.match(text,new RegExp(`${count-1} completed`));assert.match(text,new RegExp(`1 already ${enabled?'enabled':'disabled'}`));
 assert.match(text,/1 failed/);assert.match(text,/Permission denied/);assert.doesNotMatch(text,/skipped/);
}
const control=()=>({value:'',checked:false,disabled:false,hidden:true,textContent:'',focus(){}});
const modal={open:false,showModal(){this.open=true;},close(){this.open=false;}};
Object.assign(context,{
 canSaveTemplates:true,updateNotificationTemplateAction:()=>{},notificationTemplateSaveDialog:modal,notificationTemplateSaving:false,notificationBusy:false,editorNotificationTemplateContext:null,
 notificationTemplates:[],notificationSelectedTemplateId:'',notificationTemplateSaveName:control(),notificationTemplateOverwrite:control(),
 notificationTemplateOverwriteRow:control(),notificationTemplateOverwriteName:control(),notificationTemplateSaveError:control(),
 notificationTemplateSaveConfirm:control(),notificationTemplateSaveCancel:control(),notificationTemplateSaveClose:control(),
 notificationTemplateButton:control(),notificationError:control(),notificationSubject:{value:'Grid Subject'},notificationFormat:{value:'html'},
 selectedNotificationMode:()=> 'remove',currentNotificationMessage:()=>'<p>Grid message</p>',
 populateNotificationTemplates:items=>{context.notificationTemplates=Array.from(items);},
 showNativeEditorStatus:()=>{},endpoint:()=>'/maui/Tasks/SaveNotificationTemplate'
});
for(const name of ['selectedNotificationTemplate','notificationTemplateDraft','finishEditorNotificationTemplateSave','openEditorNotificationTemplateSave','updateNotificationTemplateSaveDialogState','closeNotificationTemplateSaveDialog','openNotificationTemplateSaveDialog','saveNotificationTemplate']) vm.runInContext(fn(tasks,name),context);
for (const kind of ['Task','Host']) {
 const view=read(kind==='Host'?'Views/Shared/_NewHostDialog.cshtml':'Views/Tasks/Index.cshtml');
 const name=view.indexOf('data-new-'+kind.toLowerCase()+'-name');
 const afterName=view.slice(view.indexOf('</label>',name)+8).trimStart();
 assert.ok(afterName.startsWith('<label class="new-task-auto-format"'),kind+' Auto Format must immediately follow Name');
}
(async()=>{
 let calls=[];context.fetchJson=async(url,options)=>{const payload=JSON.parse(options.body);calls.push({url,payload});return{template:{id:'saved',name:'Saved',subject:payload.subject,message:payload.message,format:payload.format}};};
 let result=context.openEditorNotificationTemplateSave({subject:'Editor',message:'Literal <not markup>',format:'text',templates:[],templateId:''});
 assert.equal(modal.open,true);context.notificationTemplateSaveName.value='Saved';await context.saveNotificationTemplate();
 const saved=await result;assert.equal(saved.format,'text');assert.equal(calls.length,1);assert.equal(calls[0].payload.message,'Literal <not markup>');assert.equal(calls[0].payload.overwriteExisting,false);
 result=context.openEditorNotificationTemplateSave({subject:'Updated',message:'<b>HTML</b>',format:'html',templates:[saved],templateId:saved.id});
 assert.equal(context.notificationTemplateOverwriteRow.hidden,false);context.notificationTemplateOverwrite.checked=true;await context.saveNotificationTemplate();
 assert.equal((await result).format,'html');assert.equal(calls[1].payload.templateId,'saved');assert.equal(calls[1].payload.overwriteExisting,true);
 result=context.openEditorNotificationTemplateSave({subject:'Cancel',message:'Body',format:'text',templates:[]});context.closeNotificationTemplateSaveDialog();assert.equal(await result,null);assert.equal(calls.length,2);
 result=context.openEditorNotificationTemplateSave({subject:'Fail',message:'Body',format:'text',templates:[]});context.notificationTemplateSaveName.value='Duplicate';context.fetchJson=async()=>{throw new Error('Name exists');};
 await context.saveNotificationTemplate();assert.equal(modal.open,true);assert.equal(context.notificationTemplateSaveError.textContent,'Name exists');context.closeNotificationTemplateSaveDialog();assert.equal(await result,null);
 context.canSaveTemplates=false;let deniedRequests=0;context.fetchJson=async()=>{deniedRequests++;throw new Error('Template write must not be requested');};
 assert.equal(await context.openEditorNotificationTemplateSave({subject:'Denied',message:'Body',format:'text',templates:[]}),null);
 assert.equal(context.openNotificationTemplateSaveDialog(),false);assert.equal(await context.saveNotificationTemplate(),false);
 assert.equal(modal.open,false);assert.equal(deniedRequests,0,'Ordinary users cannot open or submit template saving');
 context.canSaveTemplates=true;
 assert.match(tasks,/onCloneTask:.*openCloneTaskDialog/);assert.match(tasks,/onSaveNotificationTemplate:.*openEditorNotificationTemplateSave/);
 assert.doesNotMatch(read('wwwroot/js/editors/traditional-task-editor.js'),/showPlaceholderAction\('Save Notification Template'\)/);
 assert.match(read('wwwroot/js/hosts.js'),/onCloneHost:.*openCloneHostDialog/);
 const hosts=read('wwwroot/js/shared/host-actions.js').replace(/^    /gm,'');
 const hostFn=name=>{const start=hosts.indexOf(`    ${name==='submitCloneHost'?'async ':''}function ${name}(`);assert.ok(start>=0);return hosts.slice(start,hosts.indexOf('\n    }\n',start)+7);};
 for(const native of [false,true]) {
  const events=[], requests=[];const popup={closed:false};const dialog={open:false,showModal(){this.open=true;},close(){this.open=false;}};
  const h={cloneHostDialog:dialog,cloneHostName:control(),cloneHostSubmit:control(),cloneHostError:control(),cloneHostBusy:false,cloneHostUncertain:false,cloneHostSource:'',hostCloneLicenseLimited:false,useMauiHostEditor:!native,
   window:{MauiNativeEditor:{reserve:()=>{events.push('reserve');return popup;},open:()=>true}},
   showStatus:()=>{},closeReservedPopup:()=>{},endpoint:()=>'/maui/Hosts/CloneHost',
   fetchJson:async(url,options)=>{events.push('post');requests.push(JSON.parse(options.body));return{success:true,host:{id:'900',name:'Cloned'}};},
   launchHostEditor:async(id,name,reserved)=>{events.push('editor');assert.equal(id,'900');assert.equal(reserved,native?popup:null);},refreshHostGrid:async()=>{}
  };
  h.options={cloneLicenseLimited:()=>h.hostCloneLicenseLimited,openEditor:(...args)=>h.launchHostEditor(...args),refresh:()=>h.refreshHostGrid()};
  h.updateCloneHostAction=()=>{h.cloneHostSubmit.disabled=h.cloneHostBusy||h.cloneHostUncertain;};
  vm.createContext(h);vm.runInContext(hostFn('openCloneHostDialog')+'\n'+hostFn('submitCloneHost'),h);
  assert.equal(h.openCloneHostDialog('0','Default'),false);
  assert.equal(h.openCloneHostDialog('42','Source'),true);await h.submitCloneHost({preventDefault(){}});
  assert.equal(requests.length,1);assert.deepEqual(requests[0],{hostId:'42',newHostName:'Source Clone'});assert.equal(dialog.open,false);
  assert.equal(events[0],native?'reserve':'post');
  h.openCloneHostDialog('42','Source');let attempts=0;
  h.fetchJson=async()=>{attempts++;const error=new Error('Uncertain create');error.status=409;error.payload={moveItSaved:true};throw error;};
  await h.submitCloneHost({preventDefault(){}});assert.equal(h.cloneHostUncertain,true);assert.equal(h.cloneHostSubmit.disabled,true);
  await h.submitCloneHost({preventDefault(){}});assert.equal(attempts,1);
  dialog.close();h.openCloneHostDialog('42','Source');
  h.fetchJson=async()=>{const error=new Error('Name exists');error.status=409;error.payload={moveItSaved:false};throw error;};
  await h.submitCloneHost({preventDefault(){}});assert.equal(h.cloneHostUncertain,false);assert.equal(h.cloneHostSubmit.disabled,false);
 }
 // Execute the production New Task flow for both captured v0 kinds and editor preferences.
 const newTaskFn=name=>{const start=tasks.indexOf(`    ${name==='createNewTask'?'async ':''}function ${name}(`);assert.ok(start>=0);return tasks.slice(start,tasks.indexOf('\n    }\n',start)+7);};
 for(const type of ['Advanced','Synchronization']) for(const native of [false,true]) {
  let attempts=0;const events=[],requests=[],popup={closed:false};
  const dialog={open:true,querySelector:()=>({value:type}),querySelectorAll:()=>[],classList:{toggle(){}},close(){this.open=false;}};
  const c={newTaskDialog:dialog,newTaskBusy:false,newTaskCreationUncertain:false,newTaskGroupsLoading:false,newTaskGroupsLoadFailed:false,newTaskGroupsRequestId:0,
   newTaskName:{...control(),value:'NEW_TASK'},newTaskAutoFormat:{...control(),defaultChecked:false},newTaskResourceGroup:null,newTaskSubmit:control(),newTaskCancel:control(),newTaskClose:control(),newTaskSpinner:control(),newTaskSubmitLabel:control(),newTaskError:control(),
   window:{MauiNativeEditor:{reserve:()=>{events.push('reserve');return popup;},open:()=>true}},
   shouldUseMauiTaskEditor:()=>!native,endpoint:()=>'/maui/Tasks/CreateTask',applyNewTaskNameFormat:()=>{},closeReservedPopup:()=>events.push('close-popup'),
   fetchJson:async(url,options)=>{attempts++;events.push('POST');requests.push(JSON.parse(options.body));return{success:true,task:{id:'574205225',name:'NEW_TASK',type}};},
   launchTaskEditor:async(id,name,kind,reserved)=>{events.push('editor');assert.equal(id,'574205225');assert.equal(kind,type);assert.equal(reserved,native?popup:null);},
   refreshTaskGrid:async force=>events.push(force?'refresh-live':'refresh')
  };
  c.updateNewTaskAction=()=>{if(c.newTaskSubmit)c.newTaskSubmit.disabled=c.newTaskBusy||c.newTaskCreationUncertain||c.newTaskGroupsLoading||c.newTaskGroupsLoadFailed;};
  c.updateNewHostAction=()=>{if(c.newHostSubmit)c.newHostSubmit.disabled=c.newHostBusy||c.newHostCreationUncertain;};
  vm.createContext(c);
  for(const name of ['setNewTaskBusy','showNewTaskError','hideNewTaskError','resetNewTaskDialog','createNewTask'])vm.runInContext(newTaskFn(name),c);
  await c.createNewTask();assert.equal(attempts,1);assert.equal(dialog.open,false);
  assert.deepEqual(requests[0],{name:'NEW_TASK',taskType:type,resourceGroup:''});assert.equal(events[0],native?'reserve':'POST');
  for(const failure of ['rejected','unknown','network','bad-ack']) {
   c.resetNewTaskDialog();dialog.open=true;c.newTaskName.value='NEW_TASK';attempts=0;
   c.fetchJson=async()=>{attempts++;if(failure==='bad-ack')return{success:false,task:{id:'574205225'}};
    const error=new Error('Creation '+failure);if(failure!=='network')error.status=failure==='rejected'?400:502;
    error.payload={outcomeUnknown:failure==='unknown'};throw error;};
   await c.createNewTask();assert.equal(dialog.open,true);assert.equal(c.newTaskError.hidden,false);
   assert.equal(c.newTaskCreationUncertain,failure!=='rejected');assert.equal(c.newTaskSubmit.disabled,failure!=='rejected');
   await c.createNewTask();assert.equal(attempts,failure==='rejected'?2:1);
   if(failure!=='rejected')assert.match(c.newTaskError.textContent,/inspect Tasks/);
  }
 }
 // New Host uses the same guarded creation pattern, for all supplied types and both editor preferences.
 const hostSource=read('wwwroot/js/shared/new-host-dialog.js');
 const newHostFn=name=>{const match=new RegExp('^        (?:async )?function '+name+'\\(','m').exec(hostSource);assert.ok(match,name);return hostSource.slice(match.index,hostSource.indexOf('\n        }',match.index)+10);};
 for(const type of ['Share','siLock','FTP','SSHFTP','POP3','SMTP','S3','AzureBlob','SharePoint','GoogleStorage']) for(const native of [false,true]) {
  let attempts=0;const events=[],requests=[],popup={};
  let selectedType=type;const pathValue=String.raw`\\server\share`;
  const dialog={open:true,querySelector:selector=>selector.includes('data-object-resource-group')?null:selector.includes(':checked')?{value:selectedType}:{set checked(value){if(value)selectedType='Share';}},querySelectorAll:()=>[],classList:{toggle(){}},close(){this.open=false;}};
  const c={options:{},newHostDialog:dialog,newHostBusy:false,newHostCreationUncertain:false,useMauiHostEditor:!native,
   newHostName:{...control(),value:'NEW_HOST'},newHostUncField:control(),newHostUnc:{...control(),value:' '+pathValue+' '},newHostAutoFormat:{...control(),defaultChecked:false},newHostSubmit:control(),newHostCancel:control(),newHostClose:control(),newHostSpinner:control(),newHostSubmitLabel:control(),newHostError:control(),
   window:{MauiNativeEditor:{reserve:()=>{events.push('reserve');return popup;}}},endpoint:()=>'/maui/Hosts/CreateHost',
   closeReservedPopup:()=>events.push('close-popup'),refreshHostGrid:async force=>events.push(force?'refresh-live':'refresh'),
   fetchJson:async(url,options)=>{attempts++;requests.push(JSON.parse(options.body));events.push('POST');return{success:true,host:{id:'374281533',name:'NEW_HOST',typeCode:type}};},
   launchHostEditor:async(id,name,reserved)=>{assert.equal(id,'374281533');assert.equal(name,'NEW_HOST');assert.equal(reserved,native?popup:null);events.push('editor');return true;}
  };
  c.updateNewTaskAction=()=>{if(c.newTaskSubmit)c.newTaskSubmit.disabled=c.newTaskBusy||c.newTaskCreationUncertain||c.newTaskGroupsLoading||c.newTaskGroupsLoadFailed;};
  c.updateNewHostAction=()=>{if(c.newHostSubmit)c.newHostSubmit.disabled=c.newHostBusy||c.newHostCreationUncertain;};
  vm.createContext(c);
  for(const name of ['updateNewHostUncField','setNewHostBusy','showNewHostError','hideNewHostError','resetNewHostDialog','createNewHost'])vm.runInContext(newHostFn(name),c);
  c.updateNewHostUncField();assert.equal(c.newHostUncField.hidden,type!=='Share');assert.equal(c.newHostUnc.required,type==='Share');assert.equal(c.newHostUnc.disabled,type!=='Share');
  if(type==='Share') {
   for(const invalid of ['', 'server\\share']) {
    c.newHostUnc.value=invalid;await c.createNewHost();assert.equal(attempts,0);assert.equal(events.length,0);assert.match(c.newHostError.textContent,/Resource UNC/);
   }
   c.newHostUnc.value=pathValue;selectedType='FTP';c.updateNewHostUncField();assert.equal(c.newHostUncField.hidden,true);assert.equal(c.newHostUnc.required,false);
   selectedType='Share';c.updateNewHostUncField();assert.equal(c.newHostUnc.value,pathValue);assert.equal(c.newHostUncField.hidden,false);
  }
  c.setNewHostBusy(true);assert.equal(c.newHostUnc.readOnly,true);c.setNewHostBusy(false);
  await c.createNewHost();assert.equal(attempts,1);assert.equal(dialog.open,false);assert.deepEqual(requests[0],{name:'NEW_HOST',hostType:type,resourceGroup:'',...(type==='Share'?{unc:pathValue}:{})});assert.equal(events[0],native?'reserve':'POST');
  if(type==='Share'&&!native) {
   const originalQuery=dialog.querySelector,post=c.fetchJson;let groupSelect=null;
   dialog.querySelector=selector=>selector.includes('data-object-resource-group')?groupSelect:originalQuery(selector);
   c.resetNewHostDialog();dialog.open=true;c.newHostName.value='NEW_HOST';c.newHostUnc.value=pathValue;attempts=0;
   c.window.MauiObjectAccess={canCreate:()=>false,group:()=>''};await c.createNewHost();
   assert.equal(attempts,0);assert.match(c.newHostError.textContent,/permissions/);
   c.window.MauiObjectAccess.canCreate=()=>true;groupSelect={value:'',disabled:false,closest:()=>null,focus(){}};
   await c.createNewHost();assert.equal(attempts,0);assert.match(c.newHostError.textContent,/Resource Group/);
   groupSelect.value='Finance';c.window.MauiObjectAccess.group=()=>groupSelect.value;
   await c.createNewHost();assert.equal(attempts,1);assert.equal(requests.at(-1).resourceGroup,'Finance');
   delete c.window.MauiObjectAccess;dialog.querySelector=originalQuery;c.fetchJson=post;
  }
  if(type==='FTP'&&!native) {
   const launch=c.launchHostEditor,refresh=c.refreshHostGrid,messages=[];
   c.options.status=message=>messages.push(message);
   for(const failure of ['editor','refresh']) {
    c.resetNewHostDialog();dialog.open=true;c.newHostName.value='NEW_HOST';selectedType=type;attempts=0;
    c.launchHostEditor=failure==='editor'?async()=>{throw new Error('Editor unavailable');}:launch;
    c.refreshHostGrid=failure==='refresh'?async()=>{throw new Error('Refresh unavailable');}:refresh;
    await c.createNewHost();assert.equal(attempts,1);assert.equal(dialog.open,false);assert.equal(c.newHostCreationUncertain,false);
    assert.match(messages.at(-1),/ID 374281533.*was created/);
   }
   c.launchHostEditor=launch;c.refreshHostGrid=refresh;
  }
  for(const failure of ['rejected','unknown','network','bad-ack']) {
   c.resetNewHostDialog();assert.equal(c.newHostUnc.value,'');assert.equal(c.newHostUncField.hidden,false);assert.equal(c.newHostUnc.required,true);selectedType=type;c.updateNewHostUncField();c.newHostUnc.value=pathValue;dialog.open=true;c.newHostName.value='NEW_HOST';attempts=0;events.length=0;
   c.fetchJson=async()=>{attempts++;if(failure==='bad-ack')return{success:true,host:{name:'NEW_HOST'}};
    const error=new Error('Creation '+failure);if(failure!=='network')error.status=failure==='rejected'?400:502;
    error.payload={outcomeUnknown:failure==='unknown'};throw error;};
   await c.createNewHost();assert.equal(dialog.open,true);assert.equal(c.newHostError.hidden,false);
   assert.equal(c.newHostCreationUncertain,failure!=='rejected');assert.equal(c.newHostSubmit.disabled,failure!=='rejected');
   assert.equal(c.newHostClose.disabled,false);assert.equal(c.newHostCancel.disabled,false);
   await c.createNewHost();assert.equal(attempts,failure==='rejected'?2:1);
   if(failure!=='rejected'){assert.match(c.newHostError.textContent,/inspect Hosts/);assert.ok(events.includes('refresh-live'));}
  }
 }

 // Both pages render the same form and run the same creation controller.
 for (const page of ['Views/Hosts/Index.cshtml','Views/QuickTransfers/Index.cshtml']) {
  assert.match(read(page), /<partial name="_NewHostDialog" model="Model.AutoFormatNewTaskHostNames" \/>/);
  assert.doesNotMatch(read(page), /<dialog[^>]*data-new-host-dialog/);
 }
 const partial=read('Views/Shared/_NewHostDialog.cshtml');
 assert.ok(partial.indexOf('_ObjectCreationResourceGroup')<partial.indexOf('data-new-host-name'));
 assert.ok(partial.indexOf('data-new-host-auto-format')<partial.indexOf('data-new-host-unc-field'));
 assert.ok(partial.indexOf('data-new-host-unc-field')<partial.indexOf('<legend>Host Type'));
 for (const type of ['source','destination']) for (const maui of [false,true]) {
  const quick=read('wwwroot/js/quick-transfer.js'),start=quick.indexOf('    // Reuse the Hosts creation form/controller'),end=quick.indexOf('    // Event bindings.',start);
  const events=[],refreshes=[],native=[],routes=[];let options;
  const c={root:{dataset:{}},antiForgeryToken:'token',lastCreatedHostId:'',newHostTarget:'source',
   endpoint:key=>'/maui/'+key,fetchJson:async()=>{},showError:message=>events.push(message),
   refreshReferences:async(...args)=>refreshes.push(args),
   window:{MauiEditorRouting:{useMauiHostEditor:maui,openHost:async(...args)=>{routes.push(args);return true;}},
    MauiTaskHostEditor:{initialize:()=>events.push('initialize')},
    MauiNewHostDialog:{create:value=>{options=value;return{open:()=>events.push('open')};}},
    MauiNativeEditor:{open:async value=>{native.push(value);return true;}},mauiAppUrl:path=>'/maui'+path}};
  vm.createContext(c);vm.runInContext(quick.slice(start,end),c);c.openNewHost(type);
  options.created({id:'new-id'});const popup={};await options.openEditor('new-id','New Host',popup);await options.refresh(true);
  assert.deepEqual(refreshes[0],[true,'new-id',type]);assert.ok(events.includes('open'));
  assert.equal(routes.length,maui?1:0);assert.equal(native.length,maui?0:1);
  if(maui){assert.equal(routes[0][2].inPlace,true);await routes[0][2].onClosed();assert.deepEqual(refreshes[1],refreshes[0]);}
  else {assert.equal(native[0].popup,popup);assert.equal(native[0].returnHash,'#/hosts/new-id?HideNav');}
  await options.refresh(false);assert.deepEqual(refreshes.at(-1),[true,'new-id',type],'New Hosts must bypass the Quick Transfer reference cache');
  assert.equal(options.allowedTypes.includes('POP3'),false);assert.equal(options.allowedTypes.includes('SMTP'),false);
 }
 console.log('PASS large bulk-state counts, template create/overwrite/cancel/errors, Host clone routing/uncertainty, New Advanced/Sync and Host creation, recovery and editor integration.');
})().catch(error=>{console.error(error);process.exitCode=1;});
