/* Advanced selection/topology adapter around the existing Task editor panels. No network/CRUD transport here. */
(() => {
 'use strict';
 const clone=value=>JSON.parse(JSON.stringify(value));
 const labels={Source:'Source',Process:'Process',Destination:'Destination',For:'File Loop',If:'Conditional (If)',Email:'Send Email',RunTask:'Run Task',UpdOrig:'Update Original',Comment:'Comment'};
 function create(options){
  const {dialog}=options;let nodes=[],selected='',sequence=0,pending=null,tree=null,baseline='',loading=false;
  const pane=dialog.querySelector('[data-advanced-flow-pane]'),host=pane?.querySelector('[data-advanced-flow-tree]'),inspector=dialog.querySelector('[data-advanced-inspector]');
  if(!pane||!host||!inspector)return null;
  const key=()=>`new-advanced-${++sequence}`;
  const all=()=>{const result=[];function visit(list,parent=null){for(const node of list){result.push({node,list,parent});if(node.kind==='For')visit(node.steps||[],node);if(node.kind==='If'){for(const branch of node.when||[]){result.push({node:branch,list:node.when,parent:node,branch:true});visit(branch.steps||[],branch);}if(node.otherwise){result.push({node:node.otherwise,parent:node,otherwise:true,branch:true});visit(node.otherwise.steps||[],node.otherwise);}}}}visit(nodes);return result;};
  const entry=target=>all().find(e=>e.node.key===target);
  function el(tag,cls,text){const n=document.createElement(tag);if(cls)n.className=cls;if(text!==undefined)n.textContent=text;return n;}
  function button(text,handler){const b=el('button','secondary-button',text);b.type='button';b.addEventListener('click',handler);return b;}
  function remember(){options.remember(clone(nodes),selected);}
  function change(){options.changed();refresh();}
  function model(node){return options.steps().find(s=>s.key===node.key)||options.actions().find(a=>a.key===node.key);}
  const itemLabels={'[OrigName]':'File Name','[FileSize]':'File Size','[FileDateStamp]':'File Modified Timestamp','[ErrorCodeFile]':'File Error Code','[ErrorCodeTask]':'Task Error Code'};
  const comparisons={MASK:'matches',NOTMASK:'not matches',NEQ:'=',NNE:'≠',NGT:'>',NGE:'≥',NLT:'<',NLE:'≤',DGT:'newer than',DLT:'older than'};
  function criteriaSummary(criteria){const parts=criteria?.comp?[criteria.comp]:(criteria?.And||criteria?.Or)?.comp;return Array.isArray(parts)?parts.map(p=>`${itemLabels[p.a]||p.a} ${comparisons[p.test]||p.test} ${p.b}`).join(criteria?.Or?' or ':' and '):'Condition';}
  // If and its alternate branches share one visual level, matching the native execution flow.
  // The editable draft still retains the authoritative If/When/Otherwise structure.
  function displayList(list){return list.flatMap(node=>{
   if(node.kind==='If')return [...(node.when||[]).map((branch,index)=>({key:index?branch.key:node.key,kind:index?'When':'If',label:index?'Else If':'If',summary:criteriaSummary(branch.criteria),disabled:node.disabled,insertParentKey:branch.key,insertionEligible:index===0,children:displayList(branch.steps||[])})),...(node.otherwise?[{key:node.otherwise.key,kind:'Otherwise',label:'Otherwise',disabled:node.disabled,insertParentKey:node.otherwise.key,insertionEligible:false,children:displayList(node.otherwise.steps||[])}]:[])];
   const value=model(node);
   return [{key:node.key,id:node.id,kind:node.kind,label:labels[node.kind]||'Step',disabled:node.disabled,
    summary:value?(value.source||value.process||value.destination?options.summary(value):options.actionSummary(value)):node.kind==='For'?'For each file':node.fields?.Text||node.fields?.RenameTo||(node.kind==='UpdOrig'?'Delete original file':''),children:displayList(node.steps||[])}];
  });}
  function refresh(){
   if(!options.enabled())return;
   resizeFlow(flowRatio);
   if(!tree)tree=window.MauiAdvancedTaskFlow.mount(host,{nodes:displayList(nodes),onSelect:n=>select(n.key),canDrag:item=>options.canEdit()&&!entry(item.key)?.branch,getInsertActions:target=>Object.entries(labels).map(([kind,label])=>({id:'add-'+kind,kind,label,disabled:!options.canEdit(),run:()=>{if(!options.canEdit())return;pending=target;add(kind);}})),getActions:actionsFor});
   else tree.update(displayList(nodes));
   if(selected)tree.select(selected,false,false);
   updateInspectorHeading();
   inspector.inert=!options.canEdit()&&!options.readOnly();
   pane.querySelectorAll('[data-advanced-insert]').forEach(b=>b.disabled=!options.canEdit());
  }
  function branchFor(current){return current.node.kind==='If'?current.node.when?.[0]:current.branch?current.node:null;}
  function addBranch(owner,otherwise){
   if(!options.canEdit()||!owner||otherwise&&owner.otherwise)return;
   remember();const branch={key:key(),steps:[]};
   if(otherwise)owner.otherwise=branch;else{branch.criteria={comp:{a:'[ErrorCodeFile]',b:0,test:'NEQ'}};owner.when.push(branch);}
   change();select(branch.key);
  }
  function childrenState(branch,disabled){
   remember();for(const child of descendants(branch).slice(1))if(child.kind&&child.kind!=='Comment')child.disabled=disabled;change();
  }
  function actionsFor(item){
   const current=entry(item.key);if(!current)return[];
   const branch=branchFor(current),owner=current.node.kind==='If'?current.node:current.branch?current.parent:null;
   const editable=options.canEdit(),formActions=options.formActions?.(item.key)||[];
   const command=(id,label,run,disabled=false,danger=false)=>({id,label,disabled:!editable||disabled,danger,run:()=>{if(!options.canEdit())return;select(item.key);run();}});
   const actions=[];
   if(owner){
    actions.push(command('add-elseif','Add Conditional (Else If) Branch',()=>addBranch(owner,false)),command('add-otherwise','Add Default (Otherwise) Branch',()=>addBranch(owner,true),Boolean(owner.otherwise)));
    const children=descendants(branch).slice(1).filter(n=>n.kind&&n.kind!=='Comment');
    actions.push(command('enable-children','Enable Children Under This Branch',()=>childrenState(branch,false),!children.some(n=>n.disabled)),command('disable-children','Disable Children Under This Branch',()=>childrenState(branch,true),!children.some(n=>!n.disabled)));
   }
   if(!current.branch&&current.node.kind!=='Comment'&&Object.hasOwn(labels,current.node.kind))actions.push(command('toggle',current.node.disabled?'Enable Entire Step':'Disable Entire Step',()=>{remember();current.node.disabled=!current.node.disabled;change();}));
   for(const action of formActions.filter(action=>action.id==='source-timestamps'))actions.push(command(action.id,action.label,action.run,action.disabled));
   if(!current.branch)actions.push(command('clone','Clone Step',copy,!Object.hasOwn(labels,current.node.kind)||Boolean(formActions.find(a=>a.id==='clone')?.disabled)));
   if(branch)actions.push(command('delete-branch','Delete Branch',()=>remove(branch.key),!current.otherwise&&owner.when.length===1,true));
   if(!current.branch)actions.push(command('delete','Delete Entire Step',remove,false,true));
   return actions;
  }
  function load(flow){loading=true;nodes=clone(flow||[]);selected='';pending=null;
   function bind(list){for(const node of list){const m=(['Email','RunTask'].includes(node.kind)?options.actions():options.steps()).find(x=>x.id&&x.id===node.id);if(m)node.key=m.key;
    if(node.steps)bind(node.steps);for(const b of node.when||[])bind(b.steps||[]);if(node.otherwise)bind(node.otherwise.steps||[]);}}
   bind(nodes);baseline=JSON.stringify(nodes);loading=false;if(!nodes.length)inspector.replaceChildren(el('p','field-help','Choose a (+) insertion point to add a step.'));refresh();
  }
  function select(target){
   if(!options.enabled())return;const requested=entry(target);if(requested?.branch&&!requested.otherwise&&requested.parent.when[0]===requested.node)target=requested.parent.key;selected=target;const item=entry(target);if(!item)return;const n=item.node;
   if(['Source','Process','Destination'].includes(n.kind)){options.workspace('steps');options.selectStep(n.key);}
   else if(['Email','RunTask'].includes(n.kind)){options.workspace('next-actions');options.selectAction(n.key);}
   else{options.workspace('advanced-step');renderInspector(item);}
   tree?.select(target,false,false);if(innerWidth<=1200)dialog.dataset.advancedView='properties';refresh();
  }
  function container(target){if(!target?.parentKey)return nodes;const e=entry(target.parentKey);return e&&(e.node.kind==='For'||e.branch)?(e.node.steps??=[]):null;}
  function insertion(){
   if(pending){const list=container(pending);if(list){const index=pending.beforeKey?list.findIndex(n=>n.key===pending.beforeKey):list.length;return{list,index:index<0?list.length:index};}}
   const current=entry(selected);const selectedBranch=current&&branchFor(current);if(selectedBranch)return{list:selectedBranch.steps??=[],index:(selectedBranch.steps||[]).length};if(current?.node.kind==='For')return{list:current.node.steps??=[],index:(current.node.steps||[]).length};
   if(current?.list&&!current.branch)return{list:current.list,index:current.list.indexOf(current.node)+1};
   return{list:nodes,index:nodes.length};
  }
  function attach(m,kind){if(!options.enabled()||loading)return;const place=insertion();const node={key:m.key,id:m.id||'',order:++sequence,kind,disabled:false,fields:{}};place.list.splice(place.index,0,node);selected=node.key;pending=null;change();}
  function add(kind){if(!options.canEdit())return;remember();const place=insertion();
   if(['Source','Process','Destination','Email','RunTask'].includes(kind)){options.addForm(kind);select(selected);return;}
   const node={key:key(),id:'',order:sequence,kind,disabled:false,fields:{}};
   if(kind==='For')node.steps=[];
   if(kind==='If')node.when=[{key:key(),criteria:{comp:{a:'[ErrorCodeFile]',b:0,test:'NEQ'}},steps:[]}];
   if(kind==='Comment')node.fields.Text='';if(kind==='UpdOrig')node.fields={Action:'d',RenameTo:'',DelRename:0};
   place.list.splice(place.index,0,node);selected=node.key;pending=null;change();select(node.key);
  }
  function descendants(node){const list=[];function visit(n){list.push(n);for(const child of n.steps||[])visit(child);for(const b of n.when||[])visit(b);if(n.otherwise)visit(n.otherwise);}visit(node);return list;}
  async function remove(target=selected){const current=entry(target);if(!current||!options.canEdit())return;
   const count=descendants(current.node).filter(n=>n.kind).length-(current.branch?0:1);
   const signature=JSON.stringify(nodes);
   if(!await window.mauiConfirm({title:current.branch?'Delete Branch':'Delete Step',message:`Delete this ${current.branch?'branch':'step'}${count>0?` and its ${count} child step(s)`:''}?`,confirmText:'Delete',danger:true}))return;
   if(signature!==JSON.stringify(nodes)||!options.canEdit()){options.error('The flow changed while the confirmation was open. Select the step again.');return;}
   if(current.branch&&!current.otherwise&&current.list.length===1){options.error('A Conditional step must retain its If branch. Delete the entire Conditional step instead.');return;}
   remember();options.removeForms(descendants(current.node).map(n=>n.key));
   if(current.otherwise)delete current.parent.otherwise;else current.list.splice(current.list.indexOf(current.node),1);
   selected=current.parent?.key||nodes[0]?.key||'';change();if(selected)select(selected);else options.workspace('advanced-step'),inspector.replaceChildren(el('p','field-help','Add a step to begin.'));
  }
  function copy(){const current=entry(selected);if(!current||current.branch||!options.canEdit())return;
   remember();const copied=clone(current.node);
   function reset(n){const originalKey=n.key;n.cloneFromId=n.id||n.cloneFromId||'';n.id='';n.key=key();n.order=sequence;
    const value=options.cloneForm(originalKey,n.key);if(value)n.key=value.key;
    for(const child of n.steps||[])reset(child);for(const b of n.when||[]){b.key=key();for(const child of b.steps||[])reset(child);}if(n.otherwise){n.otherwise.key=key();for(const child of n.otherwise.steps||[])reset(child);}}
   reset(copied);current.list.splice(current.list.indexOf(current.node)+1,0,copied);selected=copied.key;change();select(selected);
  }
  async function move(targetKey,beforeKey='',sourceKey=selected){
   const current=entry(sourceKey);if(!current||current.branch||!options.canEdit())return;
   const list=container({parentKey:targetKey});if(!list||beforeKey===sourceKey)return;
   const signature=JSON.stringify(nodes);
   if(targetKey&&descendants(current.node).some(n=>n.key===targetKey)||targetKey===sourceKey){options.error('A step cannot be moved inside itself.');return;}
   if(!await window.mauiConfirm({title:'Move Step',message:'Move this step and its children to the selected position?',confirmText:'Move'}))return;
   if(signature!==JSON.stringify(nodes)||!options.canEdit()){options.error('The flow changed while the confirmation was open. Select the step again.');return;}
   remember();selected=current.node.key;current.list.splice(current.list.indexOf(current.node),1);const index=beforeKey?list.findIndex(n=>n.key===beforeKey):-1;list.splice(index<0?list.length:index,0,current.node);change();select(selected);
  }
  function field(title,control){const label=el('label','field-block');label.append(el('span','',title),control);return label;}
  function input(value,onchange,type='text'){const c=el('input','text-input');c.type=type;c.value=value??'';c.addEventListener('input',()=>{remember();onchange(c.value);options.changed();refresh();});return c;}
  function choice(value,items,onchange){const c=el('select','text-input');for(const [v,t] of items)c.add(new Option(t,v));c.value=value;c.addEventListener('change',()=>{remember();onchange(c.value);change();});return c;}
  function inspectorHeading(current){
   if(current.node.kind==='If')return 'If '+criteriaSummary(current.node.when?.[0]?.criteria);
   if(current.otherwise)return 'Otherwise';
   if(current.branch)return 'Else If '+criteriaSummary(current.node.criteria);
   return labels[current.node.kind]||'Step';
  }
  function updateInspectorHeading(){const current=entry(selected),heading=inspector.querySelector('h3');if(current&&heading)heading.textContent=inspectorHeading(current);}
  function renderInspector(current){
   const n=current.node;inspector.replaceChildren(el('h3','',inspectorHeading(current)));
   const body=el('div','traditional-task-editor-form-card');inspector.append(body);
   if(n.kind==='Comment'){const c=el('textarea','text-input');c.rows=6;c.value=n.fields.Text||'';c.addEventListener('input',()=>{remember();n.fields.Text=c.value;options.changed();refresh();});body.append(field('Comment',c));}
   else if(n.kind==='UpdOrig'){
    body.append(field('Update original file',choice(n.fields.Action||'d',[['d','Delete'],['r','Rename']],value=>{n.fields.Action=value;renderInspector(current);})));
    if(n.fields.Action==='r'){body.append(field('Rename to',input(n.fields.RenameTo,value=>n.fields.RenameTo=value)));const c=el('input');c.type='checkbox';c.checked=Boolean(Number(n.fields.DelRename));c.addEventListener('change',()=>{remember();n.fields.DelRename=c.checked?1:0;change();});const label=el('label','traditional-task-editor-inline-check');label.append(c,el('span','','Renamed files may overwrite existing files'));body.append(label);}
   }else if(n.kind==='For')body.append(el('p','field-help','Steps inside this loop run for each file. Use the insert controls to add steps, or drag a step to an insertion point in the loop.'));
   else if(n.kind==='If'||current.branch){
    const branch=branchFor(current);
    if(current.otherwise)body.append(el('p','field-help','Runs when no preceding branch matches.'));
    else if(branch)renderCriteria(body,branch);
   }else body.append(el('p','field-help','This step is preserved and can be moved or removed. Its properties remain read-only.'));
   if(options.readOnly())inspector.querySelectorAll('input,select,textarea,button').forEach(c=>c.disabled=true);
   inspector.inert=!options.canEdit()&&!options.readOnly();
  }
  function renderCriteria(body,branch){
   const criteria=branch.criteria||{};let match=criteria.Or?'Or':'And';const parts=criteria.comp?[criteria.comp]:(criteria.And||criteria.Or)?.comp;
   if(!Array.isArray(parts)){body.append(el('p','field-help','These criteria are preserved. Use MOVEit to edit this condition format.'));return;}
   const commit=()=>{branch.criteria=parts.length===1?{comp:clone(parts[0])}:{[match]:{comp:clone(parts)}};options.changed();refresh();};
   body.append(field('Match',choice(match,[['And','All criteria'],['Or','Any criterion']],value=>{match=value;commit();})));
   parts.forEach((part,index)=>{
    const row=el('div','advanced-criterion');
    const items=[['[OrigName]','File Name'],['[FileSize]','File Size'],['[FileDateStamp]','File Timestamp'],['[ErrorCodeFile]','File Error Code'],['[ErrorCodeTask]','Task Error Code'],['custom','Custom Macro']];
    const selectedItem=items.some(([v])=>v===part.a)?part.a:'custom';
    const item=field('Item to evaluate',choice(selectedItem,items,value=>{part.a=value==='custom'?'[macro]':value;part.test=value==='[OrigName]'?'MASK':value==='[FileDateStamp]'?'DGT':'NEQ';part.b=value==='[OrigName]'?'*.*':value==='[FileDateStamp]'?'[DateSubtract([Now],1H)]':value==='custom'?'':0;commit();renderInspector(entry(selected));}));
    if(selectedItem==='custom'){const macro=input(part.a,value=>{part.a=value;commit();});macro.setAttribute('aria-label','Custom Macro');item.append(macro);}row.append(item);
    const numeric=[['NEQ','Equals'],['NNE','Does not equal'],['NGT','Greater than'],['NGE','Greater than or equal'],['NLT','Less than'],['NLE','Less than or equal']];
    const text=[['MASK','Matches'],['NOTMASK','Not Matches']];
    const operators=part.a==='[OrigName]'?text:part.a==='[FileDateStamp]'?[['DGT','Newer than'],['DLT','Older than']]:selectedItem==='custom'?[...text,...numeric,['DGT','Newer than'],['DLT','Older than']]:numeric;
    if(!operators.some(([code])=>code===part.test))operators.push([part.test,part.test]);
    row.append(field('Comparison',choice(part.test,operators,value=>{part.test=value;commit();})));
    row.append(field(part.a==='[FileSize]'?'Value (bytes)':'Value',input(part.b,value=>{part.b=['[FileSize]','[ErrorCodeFile]','[ErrorCodeTask]'].includes(part.a)&&value.trim()!==''&&Number.isFinite(Number(value))?Number(value):value;commit();})));
    if(part.a==='[FileDateStamp]')row.append(el('p','field-help','Use a timestamp macro, for example [DateSubtract([Now],1H)] for one hour ago.'));
    const remove=button('×',()=>{remember();parts.splice(index,1);commit();renderInspector(entry(selected));});remove.classList.add('advanced-criterion-remove');remove.title='Remove Criterion';remove.setAttribute('aria-label','Remove Criterion');remove.disabled=parts.length<=1;row.append(remove);body.append(row);
   });
   const addCriterion=button('Add Criterion',()=>{remember();parts.push({a:'[OrigName]',b:'*.*',test:'MASK'});commit();renderInspector(entry(selected));});addCriterion.classList.add('advanced-criterion-add');body.append(addCriterion);
  }
  dialog.querySelectorAll('[data-advanced-view]').forEach(b=>b.addEventListener('click',()=>dialog.dataset.advancedView=b.dataset.advancedView));
  host.addEventListener('dragstart',event=>{const row=event.target.closest('.advanced-task-flow-row'),item=row?.closest('[data-flow-key]');if(event.target!==row||!row?.draggable||!item||entry(item.dataset.flowKey)?.branch||!options.canEdit()){event.preventDefault();return;}event.stopPropagation();event.dataTransfer.setData('application/x-maui-advanced-step',item.dataset.flowKey);});
  host.addEventListener('dragover',event=>{if(options.canEdit()){event.preventDefault();event.dataTransfer.dropEffect='move';}});
  host.addEventListener('drop',event=>{event.preventDefault();event.stopPropagation();const target=event.target.closest('[data-advanced-insert]');if(!target){options.error('Drop on an insert control to choose the exact step position.');return;}const sourceKey=event.dataTransfer.getData('application/x-maui-advanced-step');if(entry(sourceKey))move(target.dataset.parentKey,target.dataset.beforeKey,sourceKey);});
  const divider=dialog.querySelector('[data-advanced-divider]'),panel=pane.parentElement;
  let flowRatio=.4,resizing=false;
  function resizeFlow(ratio){
   flowRatio=Math.max(.25,Math.min(.7,ratio));
   const available=panel.clientWidth-panel.querySelector('.traditional-task-editor-rail').offsetWidth-8;
   if(available<700)return;
   const size=Math.max(250,Math.min(available-450,available*flowRatio));
   panel.style.setProperty('--advanced-flow-size',`${size}px`);divider.setAttribute('aria-valuenow',String(Math.round(size/available*100)));divider.setAttribute('aria-valuemin',String(Math.ceil(Math.max(.25,250/available)*100)));divider.setAttribute('aria-valuemax',String(Math.floor(Math.min(.7,(available-450)/available)*100)));
  }
  new ResizeObserver(()=>resizeFlow(flowRatio)).observe(panel);
  divider.addEventListener('pointerdown',event=>{if(event.button!==0||!options.enabled())return;event.preventDefault();resizing=true;divider.setPointerCapture(event.pointerId);});
  divider.addEventListener('pointermove',event=>{if(!resizing)return;const bounds=panel.getBoundingClientRect(),rail=panel.querySelector('.traditional-task-editor-rail').getBoundingClientRect();resizeFlow((event.clientX-rail.right)/(bounds.right-rail.right-8));});
  const finishResize=()=>resizing=false;divider.addEventListener('pointerup',finishResize);divider.addEventListener('pointercancel',finishResize);divider.addEventListener('lostpointercapture',finishResize);
  divider.addEventListener('keydown',event=>{if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;event.preventDefault();resizeFlow(event.key==='Home' ? .25 : event.key==='End' ? .7 : Number(divider.getAttribute('aria-valuenow'))/100+(event.key==='ArrowRight' ? .03 : -.03));});
  return{load,refresh,select,attach,add,remove,copy,exportImage:metadata=>window.MauiAdvancedTaskFlow.renderImage(displayList(nodes),metadata),restoreDraft:(value,selection)=>{nodes=clone(value);selected=selection;refresh();if(selected)select(selected);},getDraft:()=>clone(nodes),dirty:()=>JSON.stringify(nodes)!==baseline,capture:()=>selected,
   restore:selection=>{if(selection)select(selection);},first:()=>nodes[0]?.key||'',setWorkspace:name=>{pane.hidden=!options.enabled()||!['steps','next-actions','advanced-step'].includes(name);dialog.classList.toggle('advanced-flow-visible',!pane.hidden);if(!pane.hidden)resizeFlow(flowRatio);},clear:()=>{nodes=[];selected='';baseline='[]';pane.hidden=true;}};
 }
 window.MauiAdvancedTaskEditor={create};
})();
