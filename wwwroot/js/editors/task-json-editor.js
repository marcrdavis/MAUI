/* Experimental Task JSON workflow. Local value patches only; all writes use the shared Task CRUD endpoints. */
(() => {
    'use strict';
    const model = window.MauiTaskJsonModel;
    function initialize({ fetchJson, antiForgeryToken, changed }) {
        const dialog = document.querySelector('[data-task-json-dialog]');
        if (!dialog || !model || !window.MauiObjectEditLease) return null;
        const q = selector => dialog.querySelector(selector), all = selector => [...dialog.querySelectorAll(selector)];
        const tree = q('[data-task-json-tree]'), status = q('[data-task-json-status]'), submit = q('[data-task-json-submit]');
        const maxTasks = 100, maxBytes = 16 * 1024 * 1024;
        // Reuse the calling grid's Rows choices and current preference. Changes here are local to this modal.
        const sourceGrid=document.querySelector('[data-grid="tasks"]');
        const pageSizes=(()=>{try{const sizes=[...new Set(JSON.parse(sourceGrid?.dataset.pageSizes||'[]'))].map(Number).filter(n=>Number.isInteger(n)&&n>0&&n<=1000);return sizes.length?sizes:[50];}catch{return [50];}})();
        const preferredPageSize=()=>{const value=Number(document.documentElement.dataset.userGridPageSize||sourceGrid?.dataset.defaultPageSize||50);return pageSizes.includes(value)?value:pageSizes.includes(50)?50:pageSizes[0];};
        let pageSize=preferredPageSize(), findComplete=false;
        let items = [], selected = null, view = 'edit', phase = '', generation = 0, findGeneration = 0, matches = [], matchPage = 0, reviewPage = 0;
        let controller = null, loadedBytes = 0, inputDirty = false;
        const busy = () => Boolean(phase), dirty = () => items.some(item => item.document && model.changes(item).length);
        const pending = () => items.filter(item => item.document && model.changes(item).length);
        const node = (tag, text, className) => { const e = document.createElement(tag); if (text !== undefined) e.textContent = text; if (className) e.className = className; return e; };
        function notice(message, kind = 'default', detail = message) {
            window.MauiModalUI?.clear(dialog);
            if (kind === 'error' || kind === 'warning') {
                if (window.MauiModalUI?.showMessage(detail, kind, dialog, 'modal', message)) return;
                window.mauiLogNotification?.(detail, kind, 'modal');
            }
            status.textContent = message; status.hidden = false; status.dataset.feedbackKind = kind;
        }
        function counts() {
            const tasks = pending(), total = tasks.reduce((sum,item) => sum + model.changes(item).length,0);
            return total ? `${total} unsaved ${total === 1 ? 'value' : 'values'} in ${tasks.length} ${tasks.length === 1 ? 'Task' : 'Tasks'}.` : 'No unsaved changes.';
        }
        function updateControls() {
            status.dataset.taskJsonSaving=String(phase==='saving');
            submit.textContent = phase === 'saving' ? 'Saving...' : phase === 'validating' ? 'Validating...' : view === 'review' ? 'Save' : 'Review Changes';
            submit.disabled = busy() || !dirty() || inputDirty || items.some(item => !item.document || item.blocked);
            all('[data-task-json-close]').forEach(button => { button.disabled = phase === 'saving'; });
            all('[data-task-json-tab]').forEach(button => { button.disabled = phase === 'saving'; });
            const editing = busy() || !selected || selected.leaf.readOnly;
            q('[data-task-json-new]').disabled = editing;
            q('[data-task-json-apply]').disabled = editing || !inputDirty;
            q('[data-task-json-find-button]').disabled = busy();
            q('[data-task-json-replace]').disabled = busy() || !matches.some(m => m.checked && m.editable);
            all('[data-task-json-match]').forEach(box => { box.disabled = busy() || box.dataset.editable !== 'true'; });
            const eligible = matches.filter(m => m.editable), checked = eligible.filter(m => m.checked).length;
            const selectAll = q('[data-task-json-select-all]');
            selectAll.disabled = busy() || !eligible.length; selectAll.checked = eligible.length > 0 && eligible.length === checked; selectAll.indeterminate = checked > 0 && checked < eligible.length;
            const applied=matches.filter(m=>m.applied).length, count=q('[data-task-json-match-count]');
            count.hidden=!matches.length;count.textContent=matches.length?(checked||!applied?`${checked} selected changes${applied?` · ${applied} applied`:''}`:`${applied} applied`):'';
            pageControls('match',matchPage,matches.length);pageControls('review',reviewPage,reviewRows().length);
            dialog.setAttribute('aria-busy',String(busy()));
        }
        async function request(item, action, body) {
            const url = new URL(dialog.dataset[action + 'Url'], location.href); url.searchParams.set('id',item.id);
            item.requestBusy=true;
            let result;
            try { result = await fetchJson(url.toString(), {method: body === undefined ? 'GET' : 'POST', cache: 'no-store',
                headers: item.lease.id ? {'X-MAUI-Edit-Lease':item.lease.id} : {}, signal: phase === 'saving' ? undefined : controller?.signal,
                ...(body === undefined ? {} : {body: JSON.stringify(body)})}); }
            finally { item.requestBusy=false; }
            if (!result || body !== undefined && result.success !== true) throw new Error('MOVEit did not return a valid editor response.');
            return result;
        }
        const payload = item => ({editVersion:item.editVersion,changes:model.changes(item).map(({path,value})=>({path,value}))});
        function failure(item,error,saving = false) {
            const p = error.payload || {};
            item.message = error.message || 'The operation did not complete.';
            item.blocked = Boolean(p.requiresReload || p.editConflict || p.leaseLost || p.checkoutConflict || p.moveItSaved || saving && !Object.hasOwn(p,'moveItSaved'));
            item.status = item.blocked ? 'Reload required' : 'Error'; item.validation = false;
            if (p.leaseLost || p.checkoutConflict) item.checkoutConflict = true;
            const summary=p.checkoutConflict?item.message:item.blocked?'Reload or recover the affected Task before saving.':'A Task could not be updated. Review its error and try again.';
            notice(summary, 'error', item.name + ': ' + item.message);
        }
        async function load(item, sequence, retain = false) {
            try {
                if (!retain) await item.lease.open(item.id,true,dialog.dataset.checkoutEnabled === 'true');
                if (sequence !== generation) { void item.lease.close(); return; }
                let result = await request(item,'shell');
                if (result.checkoutObjectsForDurationOfEdit && !item.lease.id) {
                    await item.lease.open(item.id,true,true); result = await request(item,'shell');
                }
                if (sequence !== generation) { void item.lease.close(); return; }
                installDocument(item,result,retain);
            } catch(error) {
                if (sequence !== generation) return;
                failure(item,error); item.blocked = true;
                if (!retain) await item.lease.close();
            }
        }
        function installDocument(item,result,retain=false) {
            if (String(result.id) !== item.id || !result.definition || !Array.isArray(result.readOnlyPaths) || !result.editVersion)
                throw new Error('MOVEit did not return a complete Task definition.');
            const bytes = new TextEncoder().encode(JSON.stringify(result.definition)).length;
            if (loadedBytes - (item.bytes || 0) + bytes > maxBytes) throw new Error('The selected Task definitions are too large. Select fewer Tasks.');
            const document = retain && item.document ? model.rebase(item,result) : model.clone(result.definition);
            const leaves = model.leaves(document,result.readOnlyPaths);
            loadedBytes += bytes - (item.bytes || 0);
            Object.assign(item,{name:result.name || item.id,kind:result.kind,editVersion:result.editVersion,original:model.clone(result.definition),document,leaves,
                bytes,blocked:false,message:'',status:'Loaded',validation:false,checkoutConflict:false,savedRows:[]});
            item.leafMap=new Map(leaves.map(leaf=>[leaf.key,leaf]));
            if(selected?.item===item){const leaf=item.leafMap.get(selected.leaf.key);selected=leaf?{item,leaf}:null;}
        }
        function taskTools(item,parent) {
            const tools = node('div',undefined,'task-json-task-tools');
            const reload = node('button','Reload Task','secondary-button'); reload.type='button'; reload.disabled=busy();
            reload.addEventListener('click',()=>{void reloadTask(item,false);}); tools.append(reload);
            if (document.documentElement.dataset.moveitAdmin === 'true' && item.blocked) {
                const override = node('button','Override Checkout','secondary-button'); override.type='button'; override.disabled=busy();
                override.addEventListener('click',()=>{void reloadTask(item,true);}); tools.append(override);
            }
            parent.append(tools);
            if(item.message) parent.append(node('p',item.message,'task-json-task-message'));
        }
        function renderTree() {
            const scroll=tree.scrollTop, expanded=new Set([...tree.querySelectorAll('details[open]')].map(d=>d.dataset.path));
            tree.replaceChildren(); const search=q('[data-task-json-tree-search]').value.trim().toLowerCase();
            for(const item of items) {
                const details=node('details'); details.dataset.path=item.id; const heading=node('summary',item.name);
                heading.append(node('span',[item.kind,item.status].filter(Boolean).join(' · ') || 'Loading')); details.append(heading);
                const children=node('div',undefined,'task-json-branch'); let populated=false;
                function populate() {
                    if(populated)return; populated=true; taskTools(item,children);
                    if(!item.document)return;
                    if(search) {
                        const found=item.leaves.filter(leaf=>leaf.pathText.toLowerCase().includes(search) || !leaf.readOnly && String(model.get(item.document,leaf.path)).toLowerCase().includes(search));
                        for(const leaf of found.slice(0,100)) children.append(leafButton(item,leaf,leaf.pathText));
                        if(found.length>100)children.append(node('p',`${found.length} matches. Refine the search to see all values.`,'settings-help'));
                    } else appendChildren(item,item.document,[],children,expanded);
                }
                details.addEventListener('toggle',()=>{if(details.open)populate();});
                details.open=expanded.has(item.id) || Boolean(search); if(details.open)populate();
                details.append(children);tree.append(details);
            }
            tree.scrollTop=scroll;
        }
        function appendChildren(item,value,path,parent,expanded) {
            for(const [name,child] of Object.entries(value)) {
                const full=[...path,name], identity=item.id+model.key(full);
                if(child!==null&&typeof child==='object') {
                    const details=node('details'), heading=node('summary',name+' '+(Array.isArray(child)?`[${child.length}]`:'{…}'));
                    details.dataset.path=identity; details.append(heading); const branch=node('div',undefined,'task-json-branch'); let populated=false;
                    function populate(){if(!populated){populated=true;appendChildren(item,child,full,branch,expanded);}}
                    details.addEventListener('toggle',()=>{if(details.open)populate();});details.open=expanded.has(identity);if(details.open)populate();details.append(branch);parent.append(details);
                } else {
                    const leaf=item.leafMap?.get(model.key(full)) || item.leaves.find(l=>l.key===model.key(full));
                    parent.append(leafButton(item,leaf,name));
                }
            }
        }
        function leafButton(item,leaf,label) {
            const value=model.get(item.document,leaf.path), button=node('button',label+' = '+model.text(value),'task-json-leaf task-json-code');
            button.type='button';button.dataset.readonly=String(leaf.readOnly);button.setAttribute('aria-pressed',String(selected?.item===item&&selected.leaf.key===leaf.key));
            button.title=leaf.pathText+(leaf.readOnly?' (read-only)':'');
            button.addEventListener('click',()=>{void selectValue(item,leaf);});return button;
        }
        async function discardInput() {
            if(!inputDirty)return true;
            const yes=await window.mauiConfirm?.({title:'Discard Unapplied Value?',message:'Discard the value entered here and continue?',confirmText:'Discard Changes',danger:true});
            if(yes)inputDirty=false;return Boolean(yes);
        }
        async function selectValue(item,leaf) {
            if(!await discardInput())return;
            selected={item,leaf};renderSelected();renderTree();
        }
        function renderSelected() {
            q('[data-task-json-pick]').hidden=Boolean(selected);q('[data-task-json-fields]').hidden=!selected;
            if(!selected){updateControls();return;}
            const {item,leaf}=selected,value=model.get(item.document,leaf.path);
            q('[data-task-json-name]').value=item.name;q('[data-task-json-element]').value=leaf.name;q('[data-task-json-path]').value=leaf.pathText;
            q('[data-task-json-type]').textContent=leaf.type;q('[data-task-json-current]').value=typeof value==='string'?value:model.text(value);
            q('[data-task-json-new]').value=typeof value==='string'?value:model.text(value);q('[data-task-json-protected]').hidden=!leaf.readOnly;
            sizeValueField(q('[data-task-json-current]'));sizeValueField(q('[data-task-json-new]'));
            q('[data-task-json-json]').textContent=JSON.stringify(model.get(item.document,leaf.path.slice(0,-1)),null,2);
            inputDirty=false;updateControls();
        }
        function sizeValueField(field) {field.classList.toggle('is-multiline',/[\r\n]/.test(field.value));}
        function markEdited(changedItems,retainMatches=false) {
            for(const entry of items) { entry.savedRows=[];entry.validation=false; }
            for(const item of Array.isArray(changedItems)?changedItems:[changedItems]) { item.status=item.blocked?'Reload required':'Modified';if(!item.blocked)item.message=''; }
            if(!retainMatches){matches=[];findComplete=false;}findGeneration++;renderMatches();notice(counts());updateControls();
        }
        function applyValue() {
            if(busy()||!selected||selected.leaf.readOnly)return;
            try {
                const {item,leaf}=selected,value=model.parse(q('[data-task-json-new]').value,model.get(item.document,leaf.path));
                model.set(item.document,leaf.path,value);markEdited(item);renderSelected();renderTree();
            } catch(error) {notice(error.message,'error');}
        }
        async function findMatches() {
            if(busy())return;
            if(!await discardInput())return;
            const find=q('[data-task-json-find]').value.trim(),mode=q('[data-task-json-mode]').value,filter=q('[data-task-json-filter]').value,replacement=q('[data-task-json-replacement]').value;
            if(!find){notice('Enter an element name or path.','error');return;}
            const sequence=++findGeneration, owner=generation, found=[];matches=[];findComplete=false;phase='finding';updateControls();
            try {
                for(const item of items.filter(item=>item.document)) {
                    for(const leaf of item.leaves) {
                        const value=model.get(item.document,leaf.path);if(!model.match({...leaf,value},find,mode,filter))continue;
                        let after=null,message='',editable=false;
                        try{if(leaf.readOnly)message='Read-only';else{after=model.parse(replacement,value);editable=model.text(after)!==model.text(value);message=editable?'Change':'Unchanged';}}
                        catch(error){message=error.message;}
                        found.push({item,leaf,before:value,after,message,editable,checked:editable});
                        if(found.length>20000)throw new Error('Too many matches. Narrow the element name or path.');
                    }
                    await new Promise(resolve=>window.requestAnimationFrame(resolve));
                    if(sequence!==findGeneration||owner!==generation)return;
                }
                matches=found;findComplete=true;matchPage=0;renderMatches();notice(matches.length?'Choose the changes to apply.':counts());
            } catch(error){matches=[];renderMatches();notice(error.message,'error');}
            finally{if(owner===generation&&phase==='finding'){phase='';updateControls();}}
        }
        function pageControls(kind,page,total) {
            const start=page*pageSize;q(`[data-task-json-pager="${kind}"]`).hidden=!total;
            q(`[data-task-json-${kind}-page]`).textContent=total?`Showing ${start+1}–${Math.min(start+pageSize,total)} of ${total}`:'';
            q(`[data-task-json-${kind}-indicator]`).textContent=total?`${page+1} / ${Math.ceil(total/pageSize)}`:'';
            q(`[data-task-json-page-size="${kind}"]`).disabled=busy();
            q(`[data-task-json-${kind}-prev]`).disabled=page===0||busy();q(`[data-task-json-${kind}-next]`).disabled=start+pageSize>=total||busy();
        }
        function valueCell(value) { const cell=node('td',model.text(value));cell.title=model.text(value);return cell; }
        function labelCell(item,path) { const cell=node('td',item.name);cell.append(node('span',model.pathText(path),'task-json-path task-json-code'));return cell; }
        function renderMatches() {
            matchPage=Math.min(matchPage,Math.max(0,Math.ceil(matches.length/pageSize)-1));
            const body=q('[data-task-json-matches]');body.replaceChildren();
            matches.slice(matchPage*pageSize,(matchPage+1)*pageSize).forEach(match=>{
                const row=node('tr'),cell=node('td',undefined,'task-json-select-cell'),box=node('input');box.type='checkbox';box.checked=match.checked;box.dataset.taskJsonMatch='';box.dataset.editable=String(match.editable);box.disabled=!match.editable||busy();box.setAttribute('aria-label','Replace '+match.item.name+' '+match.leaf.pathText);
                box.addEventListener('change',()=>{match.checked=box.checked;updateControls();});cell.append(box);row.append(cell,labelCell(match.item,match.leaf.path),valueCell(match.before),valueCell(match.leaf.readOnly?match.before:match.after),node('td',match.message));
                if(!match.editable)row.className='task-json-row-unchanged';body.append(row);
            });
            if(!matches.length){const row=node('tr'),cell=node('td',findComplete?'No matching values.':'Find matching elements to preview changes.','settings-help');cell.colSpan=5;row.append(cell);body.append(row);}
            pageControls('match',matchPage,matches.length);updateControls();
        }
        function replaceValues() {
            if(busy()||!matches.some(m=>m.checked&&m.editable))return;
            const selectedMatches=matches.filter(m=>m.checked&&m.editable);
            for(const match of selectedMatches){model.set(match.item.document,match.leaf.path,match.after);match.applied=true;match.checked=false;match.editable=false;match.message='Applied';}
            markEdited([...new Set(selectedMatches.map(m=>m.item))],true);
            renderSelected();renderTree();renderReview();
        }
        function reviewRows() {
            return items.flatMap(item=>[...(item.savedRows||[]),...(item.document?model.changes(item).map(change=>({item,...change})):[])]);
        }
        function renderReview() {
            const rows=reviewRows(),body=q('[data-task-json-review]');body.replaceChildren();
            reviewPage=Math.min(reviewPage,Math.max(0,Math.ceil(rows.length/pageSize)-1));
            rows.slice(reviewPage*pageSize,(reviewPage+1)*pageSize).forEach(change=>{
                const row=node('tr'),item=change.item;row.append(labelCell(item,change.path),valueCell(change.before),valueCell(change.value));
                const cell=node('td',change.saved?(change.warning?'Saved with warning':'Saved'):item.status || 'Not validated');
                if(item.message&&!change.saved)cell.append(node('span',item.message,'task-json-path'));
                if(!change.saved&&(item.blocked||item.status==='Error'))row.className='task-json-row-error';else if(change.warning)row.className='task-json-row-warning';row.append(cell);body.append(row);
            });
            if(!rows.length){const row=node('tr'),cell=node('td','No pending changes.','settings-help');cell.colSpan=4;row.append(cell);body.append(row);}
            pageControls('review',reviewPage,rows.length);
        }
        async function switchTab(next) {
            if(phase==='saving'||!await discardInput())return false;
            view=next;all('[data-task-json-tab]').forEach(button=>{const active=button.dataset.taskJsonTab===next;button.classList.toggle('active',active);button.setAttribute('aria-selected',String(active));});
            all('[data-task-json-panel]').forEach(panel=>panel.hidden=panel.dataset.taskJsonPanel!==next);
            if(next==='review'){reviewPage=0;renderReview();}updateControls();return true;
        }
        async function validateAll() {
            if(busy()||items.some(item=>!item.document||item.blocked))return false;
            const targets=pending();if(!targets.length)return false;
            const names=new Map();
            for(const item of items){const name=String(item.document.Name||'').trim().toLowerCase();
                if(names.has(name)){const error=new Error('Selected Tasks must have unique names. Change the proposed Task Name before saving.');failure(item,error);failure(names.get(name),error);renderTree();renderReview();return false;}names.set(name,item);}
            const sequence=generation;phase='validating';updateControls();renderTree();let valid=true;
            try {
                for(const item of targets) {
                    try {item.lease.assert();await request(item,'validate',payload(item));if(sequence!==generation)return false;item.validation=true;item.status='Ready';item.message='';}
                    catch(error){if(sequence!==generation)return false;failure(item,error);valid=false;}
                    if(!dialog.open)return false;
                }
                if(valid)notice(counts()+' Ready to save.');
                return valid&&dialog.open;
            } finally {
                if(sequence===generation){phase='';renderTree();renderReview();updateControls();}
            }
        }
        async function save() {
            if(busy()||!dirty())return;
            if(view!=='review'){if(await switchTab('review'))await validateAll();return;}
            const owner=generation;phase='confirming';updateControls();renderTree();
            let proceed=false;
            try{proceed=await window.mauiConfirm?.({title:'Save Task JSON?',
                message:'WARNING: Improperly editing task JSON data can corrupt your configuration, trigger application-wide failures, or completely prevent MOVEit from starting. Proceed with extreme caution.',
                cancelText:'Cancel',confirmText:'Proceed',danger:true});}
            finally{if(owner===generation){phase='';updateControls();renderTree();}}
            if(!proceed||owner!==generation||!dialog.open)return;
            if(!await validateAll())return;
            const targets=pending();phase='saving';updateControls();renderTree();let saved=0,failed=0,warnings=0;
            const progress=q('[data-task-json-progress]'),progressWrap=q('[data-task-json-save-progress]');
            progress.max=targets.length;progress.removeAttribute('value');progress.setAttribute('aria-valuetext',`0 of ${targets.length} Tasks processed.`);progressWrap.hidden=false;
            for(let index=0;index<targets.length;index++) {
                const item=targets[index];item.status='Saving';notice(`Saving Task ${index+1} of ${targets.length}...`);renderReview();
                try {
                    item.lease.assert();const before=model.changes(item),result=await request(item,'save',payload(item));
                    if(!result.editVersion)throw new Error('The saved revision was not returned. Reload this Task.');
                    let warning=result.warning||'';
                    item.original=model.clone(item.document);item.editVersion=result.editVersion;
                    if(result.document){try{installDocument(item,result.document);}catch{item.blocked=true;warning='Task saved; reload it before making more changes.';}}
                    if(result.requiresReload)item.blocked=true;
                    item.savedRows=before.map(c=>({item,...c,saved:true,warning}));item.name=String(item.document.Name||item.id);item.status=warning?'Saved with warning':'Saved';item.message=warning;item.validation=false;saved++;if(warning)warnings++;
                } catch(error){failed++;failure(item,error,true);if(error.status===401)break;}
                finally{progress.value=index+1;progress.setAttribute('aria-valuetext',`${index+1} of ${targets.length} Tasks processed.`);}
                renderTree();renderReview();
            }
            progressWrap.hidden=true;phase='';matches=[];findComplete=false;findGeneration++;renderMatches();renderTree();renderReview();renderSelected();updateControls();
            const untouched=pending().length;
            const checkout=targets.find(item=>item.checkoutConflict&&item.message);
            if(failed&&checkout)notice(checkout.message,'error');
            else if(failed)notice(`${saved} saved; ${untouched} ${untouched===1?'Task still has':'Tasks still have'} unsaved changes.`,'error');
            else if(warnings)notice(`${saved} Tasks saved with warnings.`,'warning');
            else notice(`${saved} ${saved===1?'Task':'Tasks'} saved.`);
            if(saved||failed)changed?.();
        }
        async function reloadTask(item,override) {
            if(busy())return;
            if(!override&&item.document&&model.changes(item).length&&!await window.mauiConfirm?.({title:'Reload Task?',message:'Discard this Task’s unsaved changes and load its current MOVEit configuration?',confirmText:'Discard Changes',danger:true}))return;
            phase='loading';updateControls();const sequence=generation;
            try {
                if(override){if(!await item.lease.override(item.id))return;await load(item,sequence,true);}
                else {await item.lease.close();await load(item,sequence);}
                if(sequence!==generation)return;
                if(selected?.item===item){selected=null;renderSelected();}matches=[];findComplete=false;findGeneration++;renderMatches();renderTree();renderReview();
                if(!item.blocked){resetFindReplace();notice(override?'Checkout restored. Review your retained changes before saving.':counts());}
            } catch(error){if(sequence===generation)failure(item,error);}
            finally{if(sequence===generation){phase='';updateControls();renderTree();}}
        }
        function clearEditor() {
            if(!items.length&&!controller)return;
            generation++;findGeneration++;controller?.abort();controller=null;phase='';
            q('[data-task-json-save-progress]').hidden=true;q('[data-task-json-progress]').value=0;
            for(const item of items)void (item.lease.dispose?.() || item.lease.close());
            items=[];selected=null;matches=[];findComplete=false;inputDirty=false;loadedBytes=0;tree.replaceChildren();
            q('[data-task-json-matches]').replaceChildren();q('[data-task-json-review]').replaceChildren();q('[data-task-json-json]').textContent='';
            for(const field of ['name','element','path','current','new'])q('[data-task-json-'+field+']').value='';
            q('[data-task-json-fields]').hidden=true;q('[data-task-json-pick]').hidden=false;
        }
        async function close() {
            if(phase==='saving')return;
            if((dirty()||inputDirty)&&!await window.mauiConfirm?.({title:'Discard Task JSON Changes?',message:'Close without saving the changes in this editor?',confirmText:'Discard Changes',danger:true}))return;
            clearEditor();dialog.close();
        }
        async function open(ids) {
            if(dialog.open||busy())return;
            const selectedIds=[...new Set((ids||[]).map(id=>String(id).trim()).filter(id=>/^\d{1,32}$/.test(id)))];
            if(!selectedIds.length)return;
            resetFindReplace();window.MauiModalUI?.clear(dialog);dialog.showModal();
            if(selectedIds.length>maxTasks){notice(`Select up to ${maxTasks} Tasks for the JSON Editor.`,'error');return;}
            const sequence=++generation;controller=new AbortController();loadedBytes=0;phase='loading';view='edit';selected=null;inputDirty=false;matches=[];findComplete=false;pageSize=preferredPageSize();matchPage=reviewPage=0;
            all('[data-task-json-page-size]').forEach(select=>{select.value=String(pageSize);});
            items=selectedIds.map(id=>{
                const item={id,name:'Task '+id,status:'Loading',blocked:false,savedRows:[]};
                item.lease=window.MauiObjectEditLease.create({type:'Task',token:antiForgeryToken,busy:()=>Boolean(item.requestBusy),
                    onLost:message=>{if(sequence!==generation)return;item.blocked=true;item.checkoutConflict=true;item.message=message;item.status='Checkout lost';notice('A Task checkout was lost. Reload or recover it before saving.','error',message);renderTree();renderReview();updateControls();}});return item;
            });
            await switchTab('edit');renderSelected();renderTree();
            for(let index=0;index<items.length;index++) {
                if(sequence!==generation)return;
                const item=items[index];notice(`Loading Task ${index+1} of ${items.length}...`);await load(item,sequence);
                if(sequence!==generation)return;
                renderTree();
            }
            phase='';renderTree();renderMatches();renderReview();updateControls();
            const failed=items.filter(item=>!item.document).length;
            if(failed)notice(`${failed} ${failed===1?'Task could':'Tasks could'} not be loaded. Reload the affected Tasks before editing.`,'error');
            else notice(`${items.length} ${items.length===1?'Task':'Tasks'} loaded. Nothing has been changed.`);
        }
        function resetFindReplace(){q('[data-task-json-find]').value='';q('[data-task-json-replacement]').value='';}
        all('[data-task-json-tab]').forEach(button=>button.addEventListener('click',()=>{void switchTab(button.dataset.taskJsonTab);}));
        all('[data-task-json-close]').forEach(button=>button.addEventListener('click',()=>{void close();}));
        dialog.addEventListener('cancel',event=>{event.preventDefault();void close();});
        dialog.addEventListener('close',()=>{if(!dialog.open)clearEditor();});
        window.addEventListener('beforeunload',event=>{if(dialog.open&&(dirty()||inputDirty||phase==='saving')){event.preventDefault();event.returnValue='';}});
        q('[data-task-json-new]').addEventListener('input',()=>{sizeValueField(q('[data-task-json-new]'));if(selected){const value=model.get(selected.item.document,selected.leaf.path);inputDirty=q('[data-task-json-new]').value!==(typeof value==='string'?value:model.text(value));}updateControls();});
        q('[data-task-json-apply]').addEventListener('click',applyValue);q('[data-task-json-find-button]').addEventListener('click',()=>{void findMatches();});
        q('[data-task-json-replace]').addEventListener('click',()=>{void replaceValues();});submit.addEventListener('click',()=>{void save();});
        q('[data-task-json-tree-search]').addEventListener('input',renderTree);
        q('[data-task-json-expand]').title='Expand all selected Task roots';q('[data-task-json-expand]').addEventListener('click',()=>{[...tree.children].forEach(d=>{d.open=true;});});
        q('[data-task-json-collapse]').addEventListener('click',()=>tree.querySelectorAll('details').forEach(d=>{d.open=false;}));
        q('[data-task-json-select-all]').addEventListener('change',event=>{for(const match of matches)match.checked=match.editable&&event.target.checked;renderMatches();});
        for(const field of ['mode','find','filter','replacement'])q('[data-task-json-'+field+']').addEventListener('input',()=>{findGeneration++;matches=[];findComplete=false;renderMatches();});
        all('[data-task-json-page-size]').forEach(select=>{
            for(const size of pageSizes){const option=node('option',String(size));option.value=String(size);select.append(option);}select.value=String(pageSize);
            select.addEventListener('change',()=>{const size=Number(select.value);if(busy()||!pageSizes.includes(size))return;pageSize=size;matchPage=reviewPage=0;all('[data-task-json-page-size]').forEach(control=>{control.value=String(size);});renderMatches();renderReview();all('.task-json-results > .table-scroll').forEach(scroll=>{scroll.scrollTop=0;});});
        });
        for(const kind of ['match','review'])for(const direction of ['prev','next'])q(`[data-task-json-${kind}-${direction}]`).addEventListener('click',()=>{
            if(busy())return;const step=direction==='next'?1:-1;if(kind==='match'){matchPage=Math.max(0,matchPage+step);renderMatches();}else{reviewPage=Math.max(0,reviewPage+step);renderReview();}q(`[data-task-json-pager="${kind}"]`).previousElementSibling.scrollTop=0;
        });
        return {open};
    }
    window.MauiTaskJsonEditor={initialize};
})();
