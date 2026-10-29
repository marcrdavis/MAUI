/* Isolated Copy Between Environments wizard. Server plans own identities, authorization and write decisions. */
(() => {
    'use strict';
    const dialog = document.querySelector('[data-environment-copy]');
    if (!dialog) return;
    const find = name => dialog.querySelector('[data-copy-' + name + ']');
    const difference = document.querySelector('[data-copy-differences]');
    const diff = name => difference.querySelector('[data-copy-' + name + ']');
    const types = { tasks:'Task', hosts:'Host', scripts:'Script', datelists:'DateList', keysandcertificates:'KeyCertificate', keysandcerts:'KeyCertificate' };
    const selected = new Set(), dependencies = new Set();
    const reuseOnly = item => item.reuseOnly === true || item.builtIn === true || item.type === 'Host' && item.id === '0';
    let selectionReady = false;
    let catalogReady = false, connectionLoading = false, connectionVersion = 0, connectionController = null, connectionSelections = [], sourceCache = null;
    let objects = [], plan = null, stage = 'objects', busy = false, dirty = false, reviewDirty = false;
    let sourceReady = false, destinationReady = false, operation = '', timer = null, generation = 0, opener = null, differenceOpener = null;
    let storageKey = '', latestReport = null, previousOperation = '';
    let statusRequest = null, pollFailures = 0, phaseClock = null, phaseEpoch = 0, resultRowsVersion = '', stopRequested = false;
    const disabledBeforeBusy = new Map();
    const node = (tag, text, css) => { const element = document.createElement(tag); if (text != null) element.textContent = text; if (css) element.className = css; return element; };
    const url = action => { const result = new URL(dialog.dataset.endpoint, location.href); result.pathname = result.pathname.replace(/\/Catalog\/?$/i, '/' + action); result.search = ''; return result; };
    async function request(action, body, query, signal) {
        const requestGeneration = generation;
        const target = url(action); Object.entries(query || {}).forEach(([key,value]) => target.searchParams.set(key,value));
        const token = document.querySelector('.maui-editor-routing-antiforgery input[name="__RequestVerificationToken"]')?.value
            || document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        const timeoutMs = {Catalog:15000,Connection:30000,Objects:60000,Selection:60000,Resolve:30000,Placement:15000,Status:15000}[action];
        const controller = timeoutMs ? new AbortController() : null;
        let timedOut = false;
        const abort = () => controller?.abort();
        if (signal?.aborted) throw new DOMException('Connection changed','AbortError');
        signal?.addEventListener('abort',abort,{once:true});
        const timeout = timeoutMs ? setTimeout(()=>{timedOut=true;controller.abort();},timeoutMs) : null;
        try {
            const response = await fetch(target, { method:body ? 'POST':'GET', credentials:'same-origin', cache:'no-store',
                ...(controller ? {signal:controller.signal} : {}),
                headers:{ Accept:'application/json', ...(body ? { 'Content-Type':'application/json', RequestVerificationToken:token } : {}) },
                ...(body ? { body:JSON.stringify(body) } : {}) });
            let payload; try { payload = await response.json(); } catch {
                const error = new Error('MAUI could not read the server response. If a copy has started, check its report before trying again.');
                // HTML access-denied/error responses must retain their HTTP status so polling
                // stops for authorization failures instead of treating them as network outages.
                if (!response.ok) error.status = response.status;
                throw error;
            }
            if (requestGeneration !== generation || signal?.aborted) { if(action === 'Analyze' && payload?.planId) discard(payload.planId); throw new DOMException('Copy dialog changed','AbortError'); }
            if (!response.ok) { const error=new Error(payload?.message || 'The copy request could not be completed.');error.status=response.status;throw error; }
            return payload;
        } catch(error) {
            if(timedOut)throw new Error('The request timed out. Retry, choose another destination or close this modal.');
            throw error;
        } finally { if(timeout)clearTimeout(timeout);signal?.removeEventListener('abort',abort); }
    }
    function message(text, error = false) { find('feedback').textContent = text || ''; find('feedback').classList.toggle('is-error', error); find('feedback').setAttribute('role',error?'alert':'status'); }
    function working(value) {
        busy = value; dialog.setAttribute('aria-busy', String(value));
        if (value) dialog.querySelectorAll('input,select,button').forEach(control => {
            if (control.hasAttribute('data-copy-close') || control.hasAttribute('data-copy-stop')
                || connectionLoading && (control === find('destination') || control === find('refresh'))) return;
            disabledBeforeBusy.set(control, control.disabled); control.disabled = true;
        });
        else { disabledBeforeBusy.forEach((disabled,control) => { if (control.isConnected) control.disabled = disabled; }); disabledBeforeBusy.clear(); }
        if (!value && stage === 'objects') renderObjects(); else update();
    }
    function resetPosition() {
        const panel = dialog.querySelector('[data-copy-panel="' + stage + '"]');
        panel?.setAttribute('tabindex','-1'); panel?.focus({preventScroll:true});
        dialog.querySelector('.environment-copy-body').scrollTop = 0;
    }
    async function action(callback, resetAfter = true) {
        if (busy) return; const run = generation, focusKey = document.activeElement?.closest('[data-copy-object]')?.dataset.copyObject,
            resolveKey = document.activeElement?.closest('[data-copy-row]')?.dataset.copyRow,
            resolveField = ['action','target','name'].find(key=>document.activeElement?.hasAttribute('data-row-'+key)); message(''); working(true);
        try { await callback(); } catch (error) { if(run === generation && error?.name !== 'AbortError')message(error?.message || 'Unable to complete the copy request.',true); }
        finally { if(run === generation) { working(false); if(resetAfter && dialog.open && !difference.open)resetPosition();
            else if(resolveKey && resolveField && dialog.open && !difference.open)[...find('plan').children].find(row=>row.dataset.copyRow === resolveKey)?.querySelector('[data-row-'+resolveField+']')?.focus({preventScroll:true});
            else if(focusKey && dialog.open && !difference.open)[...find('objects').children].find(row=>row.dataset.copyObject === focusKey)?.querySelector('input:not(:disabled)')?.focus({preventScroll:true}); } }
    }
    function update() {
        find('source').disabled = true;
        find('destination').disabled = !catalogReady || Boolean(operation) || busy && !connectionLoading;
        const resolved = plan && !dirty && plan.unresolved === 0;
        dialog.querySelectorAll('[data-copy-tab]').forEach(tab => {
            const key = tab.dataset.copyTab;
            tab.disabled = busy || Boolean(operation) && key !== 'results' || key === 'resolve' && !plan || key === 'review' && !resolved || key === 'results' && !operation;
            tab.classList.toggle('active',key === stage); tab.setAttribute('aria-selected',String(key === stage));
        });
        find('back').hidden = stage === 'objects'; find('back').textContent = stage === 'results' ? 'New Copy' : 'Back';
        find('back').disabled = busy || stage === 'results' && !latestReport?.completedUtc; find('next').hidden = stage === 'results';
        find('next').textContent = busy ? 'Working…' : stage === 'objects' ? 'Analyze' : stage === 'resolve' ? 'Review Copy'
            : reviewDirty ? 'Update Review' : 'Copy ' + (plan?.items.length || 0) + ' Objects';
        find('next').disabled = busy || stage === 'objects' && (!sourceReady || !destinationReady || !selected.size || !selectionReady || find('source').value === find('destination').value)
            || stage === 'resolve' && !dirty && !resolved || stage === 'review' && !reviewDirty && plan?.items.some(x=>x.choice.action === 'overwrite') && !find('confirm-overwrite').checked;
    }
    function showStage(name) {
        if (name === 'review' && (!plan || plan.unresolved || dirty)) return;
        if (name === 'resolve' && !plan || name === 'results' && !operation) return;
        stage = name; dialog.querySelectorAll('[data-copy-panel]').forEach(panel=>panel.hidden = panel.dataset.copyPanel !== name);
        update();
    }
    function discard(id) { if(id)void request('Discard',{planId:id,reviewToken:''}).catch(()=>{}); }
    function invalidate() { if(plan && operation !== plan.planId)discard(plan.planId); plan = null; dirty = false; reviewDirty = false; find('confirm-overwrite').checked = false; showStage('objects'); }
    function filtered() { const type = find('filter').value, search = find('search').value.trim().toLowerCase(); return objects.filter(x=>(!type || x.type === type) && (!search || (x.name + ' ' + x.id + ' ' + x.subtype).toLowerCase().includes(search))); }
    function renderObjects() {
        const body = find('objects'); body.replaceChildren(); const visible = filtered();
        visible.forEach(item => {
            const required = dependencies.has(item.key), protectedObject = reuseOnly(item);
            const row = node('tr'); row.dataset.copyObject = item.key;
            row.classList.toggle('is-copy-dependency',required); row.classList.toggle('is-copy-reuse-only',protectedObject);
            const cell = node('td',null,'environment-copy-check'); const checkbox = node('input');
            checkbox.type = 'checkbox'; checkbox.checked = selected.has(item.key) || required; checkbox.disabled = busy || required || protectedObject;
            checkbox.setAttribute('aria-label',(required ? 'Required dependency: ' : 'Select ') + item.name);
            checkbox.title = required ? 'Required by the selected objects' : protectedObject ? 'Reused when required; cannot be copied' : 'Select ' + item.name;
            checkbox.addEventListener('change',()=>{ if(busy || required || protectedObject)return; checkbox.checked ? selected.add(item.key) : selected.delete(item.key);
                void action(async()=>{invalidate();await refreshSelection();},false); });
            cell.append(checkbox); row.append(cell);
            const inclusion = required ? 'Required dependency' : protectedObject ? 'Reuse only: ' + (item.builtIn ? 'built-in Script' : 'Local File System') : selected.has(item.key) ? 'Selected' : '';
            [item.name, item.type === 'KeyCertificate' ? item.subtype + ' key/certificate' : item.subtype + ' ' + item.type, item.id, inclusion].forEach(value=>row.append(node('td',value)));
            body.append(row);
        });
        const selectable = visible.filter(item=>!reuseOnly(item) && !dependencies.has(item.key));
        find('empty').hidden = visible.length > 0; find('selection-count').textContent = selected.size + ' selected' + (dependencies.size ? ' · ' + dependencies.size + ' dependencies' : '');
        find('select-all').disabled = busy || !selectable.length; find('select-all').checked = selectable.length > 0 && selectable.every(x=>selected.has(x.key));
        find('select-all').indeterminate = selectable.some(x=>selected.has(x.key)) && !find('select-all').checked; update();
    }
    async function refreshSelection(signal) {
        dependencies.clear(); selectionReady = !find('dependencies').checked || !selected.size; renderObjects();
        if (selectionReady) return;
        message('Reading dependencies…');
        const result = await request('Selection',{source:find('source').value,objects:objects.filter(x=>selected.has(x.key)).map(x=>({type:x.type,id:x.id}))},null,signal);
        result.dependencies.forEach(key=>dependencies.add(key)); selectionReady = true; renderObjects(); message('');
    }
    async function loadConnections(initialSelections = [], signal, version) {
        sourceReady = false; destinationReady = false; selectionReady = false; selected.clear(); dependencies.clear(); objects = []; renderObjects();
        const run = generation, source = find('source').value, destination = find('destination').value;
        find('source-status').textContent = source ? 'Connecting with Data Access account…' : 'Choose a source environment';
        find('destination-status').textContent = destination ? 'Connecting with Data Access account…' : 'Choose a destination environment';
        const results = await Promise.allSettled([
            sourceCache?.key === source ? Promise.resolve(sourceCache.payload) : source ? request('Objects',null,{environment:source},signal) : Promise.reject(new Error('Choose a source environment.')),
            destination && destination !== source ? request('Connection',null,{environment:destination},signal) : Promise.reject(new Error('Choose a different destination environment.'))
        ]);
        if (run !== generation || version !== connectionVersion || !dialog.open) return;
        const errors = [];
        results.forEach((result,index)=>{
            const field = find(index ? 'destination-status':'source-status');
            if (result.status === 'fulfilled') {
                field.textContent = 'Data Access: ' + result.value.account + ' · Connected';
                if (index) destinationReady = true;
                else { sourceCache={key:source,payload:result.value};sourceReady = true; objects = result.value.objects; initialSelections.forEach(key=>{if(objects.some(x=>x.key === key && !reuseOnly(x)))selected.add(key);}); }
            } else { field.textContent = result.reason.message; errors.push(result.reason.message); }
        });
        find('route').textContent = source + ' → ' + destination;
        if (sourceReady) await refreshSelection(signal); else renderObjects();
        if (errors.length) message(errors.join(' '),true); else message('');
    }
    async function connect(initialSelections, refreshSource = false, resetAfter = false) {
        if(busy && !connectionLoading)return;
        const run=generation, version=++connectionVersion;
        connectionController?.abort(); if(busy)working(false);
        connectionController=new AbortController(); const signal=connectionController.signal;
        connectionSelections=[...initialSelections]; if(refreshSource)sourceCache=null;
        connectionLoading=true; invalidate(); message('Connecting with Data Access accounts…');working(true);
        try { await loadConnections(connectionSelections,signal,version); }
        catch(error) { if(run===generation && version===connectionVersion && error?.name!=='AbortError')message(error.message,true); }
        finally { if(run===generation && version===connectionVersion) {working(false);connectionLoading=false;update();if(resetAfter)resetPosition();} }
    }
    function choices() { return [...find('plan').querySelectorAll('[data-copy-row]')].map(row=>({key:row.dataset.copyRow,action:row.querySelector('[data-row-action]').value,
        targetId:row.querySelector('[data-row-target]').value,name:row.querySelector('[data-row-name]').value})); }
    function acceptPlan(value) {
        plan = value; dirty = false; reviewDirty = false; find('confirm-overwrite').checked = false; renderPlan(); renderReview(); update();
    }
    async function resolve(applyNaming = false) {
        const selectedChoices = choices();
        if (applyNaming) selectedChoices.forEach(choice=>{ if(choice.action === 'create') choice.name = plan.items.find(x=>x.key === choice.key).sourceName; });
        const value = await request('Resolve',{planId:plan.planId,choices:selectedChoices,prefix:applyNaming?find('prefix').value:'',suffix:applyNaming?find('suffix').value:'',
            nameScope:find('name-scope').value,uniqueNames:find('unique').checked});
        acceptPlan(value);
    }
    function renderPlan() {
        const scroll = dialog.querySelector('.environment-copy-body').scrollTop;
        const body = find('plan'); body.replaceChildren(); if (!plan) return;
        find('plan-count').textContent = plan.items.length + ' objects · ' + plan.unresolved + ' need attention';
        plan.items.forEach(item=>{
            const row = node('div',null,'environment-copy-plan-row'); row.dataset.copyRow = item.key; row.classList.toggle('needs-attention',item.issues.length > 0);
            const main = node('div',null,'environment-copy-plan-main');
            const source = node('div'); source.append(node('strong',item.sourceName),node('small',item.type + ' · ID ' + item.sourceId + ' · ' + item.reason));
            source.querySelector('strong').title = item.sourceName;
            const target = node('div'); const match = node('select',null,'text-input'); match.dataset.rowTarget = ''; match.setAttribute('aria-label','Destination match for ' + item.sourceName);
            match.add(new Option(item.targets.length ? 'Select destination object':'No compatible destination object',''));
            item.targets.forEach(value=>match.add(new Option(value.name + ' · ID ' + value.id,value.id)));
            match.value = item.choice.targetId;
            if (!match.value) { const exact = item.matches.filter(x=>item.targets.some(t=>t.id === x.id)); if(exact.length === 1) match.value = exact[0].id; }
            target.append(match,node('small',item.matches.length ? 'Name / ID matches: ' + item.matches.map(x=>x.name + ' (' + x.id + ')').join(', ') : 'No matching name or source ID'));
            const actionCell = node('div'); const select = node('select',null,'text-input'); select.dataset.rowAction = ''; select.setAttribute('aria-label','Action for ' + item.sourceName);
            [['','Choose action'],['create','Create New'],['reuse','Reuse Existing'],['overwrite','Overwrite']].forEach(([key,label])=>{
                const option = new Option(label,key); option.disabled = key === 'create' && !item.canCreate || key === 'overwrite' && !item.canOverwrite || (key === 'reuse' || key === 'overwrite') && !item.targets.length; select.add(option);
            }); select.value = item.choice.action;
            actionCell.append(select,node('small',item.choice.action === 'reuse'?'Destination unchanged':item.choice.action === 'overwrite'?'Retain destination identity':'ID assigned on create'));
            const nameCell = node('div'); const name = node('input',null,'text-input'); name.dataset.rowName = ''; name.value = item.choice.name; name.maxLength = 255; name.setAttribute('aria-label','Destination name for ' + item.sourceName);
            name.readOnly = item.choice.action !== 'create' || !item.canRename; match.disabled = item.choice.action === 'create';
            nameCell.append(name,node('small',item.canRename?'New names can be adjusted':'Native resource name retained'));
            main.append(source,target,actionCell,nameCell); row.append(main);
            const status = node('div',null,'environment-copy-plan-status'); const explanation = node('p', item.issues.length ? 'Action required: ' + item.issues.map(value=>value.replace(/^(?:Action required:\s*)+/i,'')).join(' ') : item.choice.action === 'reuse' ? 'Reuse the existing object without changing it.' : item.credentialStatus + '. ' + (item.type === 'Task'?'Task remains disabled.':''));
            status.append(explanation);
            const compare = node('button','Review differences','secondary-button'); compare.type = 'button'; compare.disabled = !match.value;
            compare.addEventListener('click',()=>action(async()=>{
                if (dirty) await resolve();
                await openDifferences(item.key,match.value,compare);
            },false)); status.append(compare); row.append(status); body.append(row);
            function change(validateNow = false) {
                dirty = true; plan.reviewToken = ''; find('confirm-overwrite').checked = false;
                name.readOnly = select.value !== 'create' || !item.canRename; match.disabled = select.value === 'create';
                if (select.value !== 'create' && match.value) name.value = item.targets.find(x=>x.id === match.value)?.name || item.sourceName;
                compare.disabled = !match.value;
                const issue = !select.value ? 'Choose Create New, Reuse Existing or Overwrite.'
                    : select.value === 'create' && !name.value.trim() ? 'Enter a destination name.'
                    : select.value !== 'create' && !match.value ? 'Select a compatible destination object.' : '';
                row.classList.toggle('needs-attention',Boolean(issue));
                explanation.textContent = issue ? 'Action required: ' + issue : validateNow ? 'Checking resolution…' : 'Changes will be validated before Review.';
                update();
                // Select changes are validated immediately. Typing stays local until an explicit action.
                if(validateNow)void action(()=>resolve(),false);
            }
            select.addEventListener('change',()=>change(true)); match.addEventListener('change',()=>change(true)); name.addEventListener('input',()=>change(false));
        });
        dialog.querySelector('.environment-copy-body').scrollTop = scroll;
    }
    function folderPaths(folders) {
        const path = (folder,seen = new Set()) => { if(seen.has(folder.id))return folder.name;seen.add(folder.id);const parent=folders.find(x=>x.id === folder.parentId);return parent?path(parent,seen)+'/'+folder.name:folder.name; };
        return folders.map(folder=>({id:folder.id,name:path(folder)})).sort((a,b)=>a.name.localeCompare(b.name));
    }
    function renderReview() {
        if (!plan) return;
        const counts = ['create','overwrite','reuse'].map(action=>plan.items.filter(x=>x.choice.action === action).length);
        find('summary').textContent = counts[0] + ' new · ' + counts[1] + ' overwritten · ' + counts[2] + ' reused · ' + plan.source + ' → ' + plan.destination;
        const group = find('group'), previousGroup = group.value; group.replaceChildren(new Option('Select Resource Group','')); plan.groups.forEach(value=>group.add(new Option(value,value))); group.value = previousGroup;
        const folder = find('folder-id'), previousFolder = folder.value; folder.replaceChildren(new Option('Select destination folder','')); folderPaths(plan.folders).forEach(value=>folder.add(new Option(value.name,String(value.id)))); folder.value = previousFolder;
        find('folder-controls').hidden = !plan.items.some(x=>x.type === 'Task' && x.choice.action === 'create');
        find('overwrite-field').hidden = !counts[1];
        find('skips').textContent = plan.items.flatMap(item=>[...item.skipped,...(item.metadataNotes || [])].map(value=>item.sourceName + ': ' + value)).join('\n'); placementVisibility();
        renderPlacement(plan);
    }
    function renderPlacement(value) {
        find('group-creation').textContent = (value.groupsToCreate?.length ? 'Groups to create: ' + value.groupsToCreate.join(', ') + '. ' : '') + 'Missing groups use source names. User-group permissions are not copied.';
        const placement = find('placement'); placement.replaceChildren();
        (value.placement || []).forEach(item=>{
            const row=node('div',null,'environment-copy-result');row.classList.toggle('access-change',item.accessChanged);
            row.append(node('strong',item.name),node('p','Source Resource Groups: '+(item.sourceGroups?.length?item.sourceGroups.join(', '):'None')),
                node('p',(item.accessChanged?'Destination Resource Group access will change: ':'Destination Resource Groups: ')+(item.beforeGroups.length?item.beforeGroups.join(', '):'None')+' → '+(item.afterGroups.length?item.afterGroups.join(', '):'None')));
            if(item.destinationGroupsPreserved)row.append(node('p','Existing destination Resource Groups will be retained.'));
            if(item.folder)row.append(node('p','Task folder: '+item.folder));placement.append(row);
        });
    }
    async function previewPlacement() {
        const run = generation, options = reviewRequest();
        find('skips').textContent = ''; // Prior Review warnings no longer describe the current placement.
        find('placement').replaceChildren(node('p','Updating placement…','field-help'));
        find('group-creation').textContent = 'Checking source Resource Groups…';
        if (options.groupMode === 'one' && !options.group) {
            find('placement').replaceChildren(node('p','Select a destination Resource Group.','field-help'));
            return;
        }
        try {
            // The server reuses its placement rules over the owned plan; no exports or MOVEit writes.
            const value = await request('Placement',options);
            plan.placement = value.placement; plan.groupsToCreate = value.groupsToCreate; renderPlacement(value);
        } catch(error) {
            if (run !== generation || !dialog.open || error?.name === 'AbortError') throw error;
            find('placement').replaceChildren(node('p','Placement could not be previewed. Use Update Review to retry.','field-help'));
            find('group-creation').textContent = 'Source group placement has not been confirmed.';
            throw error;
        }
    }
    function placementVisibility() {
        find('group-field').hidden = find('group-mode').value !== 'one';
        find('group-creation').hidden = find('group-mode').value !== 'create-source';
        find('folder-existing').hidden = find('folder-mode').value !== 'existing'; find('folder-new').hidden = find('folder-mode').value !== 'new';
    }
    function reviewRequest() { return { planId:plan.planId,groupMode:find('group-mode').value,group:find('group').value,
        folderMode:find('folder-mode').value,folderId:find('folder-id').value?Number(find('folder-id').value):null,folderPath:find('folder-path').value,
        createFolder:find('create-folder').checked,copyMetadata:find('metadata').checked,preserveDestinationMetadata:find('preserve-metadata').checked,
        preserveDestinationGroups:find('preserve-groups').checked,preserveDestinationCredentials:find('preserve-credentials').checked,recoveryCopies:find('recovery').checked,
        includeTlsPrivateKeys:find('tls-private').checked,allowIncompleteCredentials:find('incomplete').checked }; }
    async function review() { const value = await request('Review',reviewRequest()); acceptPlan(value); showStage('review'); }
    async function openDifferences(key,targetId,trigger) {
        const run = generation;
        differenceOpener = {key,trigger}; diff('comparison').replaceChildren(); diff('difference-error').hidden = true; diff('difference-subtitle').textContent = 'Loading comparison…';
        diff('difference-identity').textContent = ''; diff('difference-impact').textContent = ''; difference.showModal();
        try {
            const result = await request('Differences',null,{planId:plan.planId,key,targetId});
            if (!difference.open) return;
            diff('difference-subtitle').textContent = result.name + ' · ' + result.type;
            diff('difference-identity').textContent = result.source + ' / ' + result.sourceId + ' → ' + result.destination + ' / ' + result.destinationId;
            diff('difference-impact').textContent = result.impactMessage + (result.impact?.length ? ' ' + result.impact.join(', ') : '');
            const table = diff('comparison'); const header = node('div',null,'environment-copy-comparison-row'); ['Property / status','Source','Destination'].forEach(text=>header.append(node('div',text))); table.append(header);
            result.values.forEach(value=>{
                const row = node('div',null,'environment-copy-comparison-row'); row.classList.toggle('changed',value.status !== 'Same'); row.classList.toggle('code',/(^|\.)source$/i.test(value.property));
                [value.property+'\n'+value.status,value.source,value.destination].forEach(text=>row.append(node('div',text))); table.append(row);
            });
            plan = result.plan; dirty = false; renderPlan(); update();
        } catch(error) {
            if (run !== generation || !difference.open || error?.name === 'AbortError') return;
            diff('difference-error').textContent = error.message; diff('difference-error').hidden = false;
        }
    }
    function closeDifference() { if(difference.open)difference.close(); const row = [...find('plan').children].find(x=>x.dataset.copyRow === differenceOpener?.key); if(differenceOpener?.trigger?.isConnected)differenceOpener.trigger.focus();else row?.querySelector('button')?.focus(); }
    function rememberOperation(id) { operation = id; try { localStorage.setItem(storageKey,id); } catch { /* Operation report remains server-side. */ } }
    function phaseAge() {
        if(!latestReport?.completedUtc && dialog.open && stage==='results')
            find('phase-age').textContent = Math.max(0,Math.floor((performance.now()-phaseEpoch)/1000))+'s in this phase';
    }
    function renderResults(report) {
        latestReport=report;
        const active=!report.completedUtc, done=report.completedObjects ?? report.items.filter(item=>['Created','Overwritten','Reused'].includes(item.status)).length;
        const total=Math.max(0,report.totalObjects || plan?.items.length || report.items.length);
        const type=({KeyCertificate:'Key / Certificate',DateList:'Date List'})[report.currentObjectType] || report.currentObjectType;
        const current=report.currentObjectName ? ' · '+type+' · '+report.currentObjectName : '';
        const phase=(report.cancelRequested || stopRequested) && active ? 'Stop requested; finishing current work'
            : report.phase || (active?'Preparing copy':report.status);
        find('result-title').textContent=active?'Copy Progress':report.status;
        // Routine progress has one status line. Keep only distinct report details below it.
        const details=[report.message,
            report.groupsCreated?.length?'Resource Groups created: '+report.groupsCreated.join(', ')+'.':'',
            report.groupCreationStatus==='Not verified'?'Resource Group creation is not verified. Inspect: '+(report.groupsPlanned||[]).join(', ')+'.':''
        ].filter(Boolean).join(' ');
        find('result-message').textContent=details;find('result-message').hidden=!details;
        find('phase').textContent=phase+current;find('phase').title=phase+current;
        find('activity').hidden=!active;find('phase-age').hidden=!active;
        phaseEpoch=performance.now()-Math.max(0,Number(report.phaseElapsedSeconds)||0)*1000;
        clearInterval(phaseClock);phaseClock=null;
        if(active){phaseAge();phaseClock=setInterval(phaseAge,1000);}else find('phase-age').textContent='';
        find('counts').textContent=done+' of '+total+' objects verified';
        const progress=find('progress');progress.max=Math.max(1,total);
        // A genuine indeterminate state communicates preparation/first-object activity.
        // Never fill a stopped/cancelled operation merely because it has a completion timestamp.
        if(active && done===0)progress.removeAttribute('value');else progress.value=Math.min(done,total);
        progress.setAttribute('aria-valuetext',done+' of '+total+' objects verified. '+phase+current);
        const rowsVersion=JSON.stringify([report.operationId,Boolean(report.completedUtc),report.currentObjectKey,report.items]);
        if(rowsVersion!==resultRowsVersion){
            resultRowsVersion=rowsVersion;
            const body=find('results');body.replaceChildren();
            report.items.forEach(item=>{
                const inProgress=active&&item.status==='Not verified'&&item.key===report.currentObjectKey;
                const row=node('div',null,'environment-copy-result');
                const severity=inProgress?'none':item.severity || 'none';
                row.classList.toggle('is-warning',severity==='warning');row.classList.toggle('is-error',severity==='error');row.classList.toggle('has-metadata-notes',severity==='metadata');
                const label=inProgress?'In progress':severity==='warning'&&['Created','Overwritten','Reused'].includes(item.status)?item.status+' with warnings':item.status;
                row.append(node('strong',label+' · '+item.destinationName),
                    node('p',item.type+' · Source '+item.sourceId+' → Destination '+(item.destinationId||'Not confirmed')),
                    node('p',inProgress?'Copy and verification are in progress.':item.message));
                if(item.skipped.length)row.append(node('p','Warnings: '+item.skipped.join('\n')));
                if(item.metadataNotes?.length)row.append(node('p','Metadata differences: '+item.metadataNotes.join('\n')));
                if(item.recoveryCaptured){const recovery=node('a','Download Recovery Definition','text-link'),link=url('Recovery');link.searchParams.set('operationId',operation);link.searchParams.set('key',item.key);recovery.href=link;row.append(recovery,node('p','Recovery definitions require a separately reviewed restore; completed writes are not automatically rolled back.'));}
                body.append(row);
            });
        }
        const reportUrl=url('Report');reportUrl.searchParams.set('operationId',operation);find('report').href=reportUrl;find('report').hidden=false;
        find('stop').hidden=!active;find('stop').disabled=Boolean(report.cancelRequested||stopRequested);
        message('');
        update();
    }
    async function poll() {
        clearTimeout(timer);if(!operation||!dialog.open)return;
        const run=generation,id=operation;
        if(statusRequest?.run===run&&statusRequest.id===id)return statusRequest.promise;
        statusRequest?.controller.abort();
        const entry={run,id,controller:new AbortController(),promise:null};statusRequest=entry;
        entry.promise=(async()=>{
            try{
                const report=await request('Status',null,{operationId:id},entry.controller.signal);
                if(run!==generation||id!==operation||!dialog.open)return;
                pollFailures=0;renderResults(report);if(!report.completedUtc)timer=setTimeout(poll,1000);
            }catch(error){
                if(run===generation&&id===operation&&dialog.open&&error?.name!=='AbortError'){
                    const retry=!error.status||error.status>=500||error.status===408||error.status===429;
                    message((retry?'Unable to refresh progress. The copy may still be running; retrying the status read. ':'Progress updates are unavailable. Reopen the operation report after restoring access. ')+error.message,true);
                    if(retry){pollFailures++;timer=setTimeout(poll,Math.min(5000,1000*pollFailures));}
                }
            }finally{if(statusRequest===entry)statusRequest=null;}
        })();
        return entry.promise;
    }
    async function open(gridType,ids,trigger) {
        statusRequest?.controller.abort();statusRequest=null;clearInterval(phaseClock);phaseClock=null;pollFailures=0;resultRowsVersion='';stopRequested=false;
        opener=trigger || document.activeElement; generation++; connectionController?.abort();connectionVersion++;connectionLoading=false;catalogReady=false;sourceCache=null;selected.clear(); dependencies.clear(); selectionReady=false; plan=null;operation='';latestReport=null;stage='objects';dirty=false;reviewDirty=false;
        const run = generation;
        previousOperation='';find('previous').hidden=true;find('prefix').value='';find('suffix').value='';find('name-scope').value='tasks';find('group-mode').value='none';find('folder-mode').value='root';find('folder-path').value='';
        ['dependencies','unique','metadata','preserve-metadata','preserve-groups','preserve-credentials','recovery','create-folder','tls-private'].forEach(name=>find(name).checked=true);find('incomplete').checked=false;find('confirm-overwrite').checked=false;
        dialog.showModal();showStage('objects');message('Loading configured environments…');working(true);
        try {
            const catalog=await request('Catalog'); storageKey='MAUI.EnvironmentCopy.'+(dialog.dataset.owner||'')+'.'+catalog.currentEnvironment;
            [find('source'),find('destination')].forEach(select=>{select.replaceChildren(new Option('Select environment',''));catalog.environments.forEach(environment=>select.add(new Option(environment.name+(environment.credentialsConfigured?'':' (Data Access required)'),environment.key)));});
            catalogReady=true;find('source').value=catalog.currentEnvironment;find('destination').value=catalog.environments.find(x=>x.key!==catalog.currentEnvironment)?.key||'';find('filter').value=gridType;find('search').value='';
            let previous='';try{previous=localStorage.getItem(storageKey)||'';}catch{}
            if(/^[a-f0-9]{32}$/.test(previous)){
                try{const report=await request('Status',null,{operationId:previous});previousOperation=previous;find('previous').hidden=false;if(!report.completedUtc){operation=previous;showStage('results');renderResults(report);timer=setTimeout(poll,1000);}}
                catch{/* A missing/expired previous report does not prevent a new analysis. */}
            }
            if(run!==generation || !dialog.open)return;
            working(false);if(!operation){void connect(ids.map(id=>gridType+'|'+id));return;}
        }catch(error){if(run===generation&&error?.name!=='AbortError')message(error.message,true);}finally{if(run===generation && !connectionLoading){working(false);resetPosition();}}
    }
    function close(){statusRequest?.controller.abort();statusRequest=null;clearInterval(phaseClock);phaseClock=null;if(plan&&operation!==plan.planId)discard(plan.planId);generation++;connectionVersion++;connectionController?.abort();connectionLoading=false;clearTimeout(timer);working(false);if(difference.open)difference.close();if(dialog.open)dialog.close();opener?.focus?.();}
    find('next').addEventListener('click',()=>action(async()=>{
        if(stage==='objects'){
            const value=await request('Analyze',{source:find('source').value,destination:find('destination').value,objects:objects.filter(x=>selected.has(x.key)).map(x=>({type:x.type,id:x.id})),includeDependencies:find('dependencies').checked});
            acceptPlan(value);showStage('resolve');message(value.unresolved?value.unresolved+' rows need attention.':'Destination plan is ready for review.');
        } else if(stage==='resolve'){
            if(dirty)await resolve();if(plan.unresolved){message('Resolve all highlighted rows before reviewing the copy.',true);return;}await review();
        } else if(stage==='review'){
            if(reviewDirty){await review();message('Review updated. Check the summary and confirm any overwrites before copying.');return;}
            rememberOperation(plan.planId);const started=await request('Start',{planId:plan.planId,reviewToken:plan.reviewToken,confirmOverwrite:find('confirm-overwrite').checked});rememberOperation(started.operationId);showStage('results');await poll();
        }
    }));
    find('apply-names').addEventListener('click',()=>action(()=>resolve(true)));
    find('back').addEventListener('click',()=>{message('');if(stage==='results'){operation='';plan=null;latestReport=null;showStage('objects');resetPosition();return;}showStage(stage==='review'?'resolve':'objects');resetPosition();});
    find('previous').addEventListener('click',()=>action(async()=>{operation=previousOperation;showStage('results');await poll();}));
    dialog.querySelectorAll('[data-copy-tab]').forEach(tab=>tab.addEventListener('click',()=>{
        const target=tab.dataset.copyTab;if(target==='review'&&stage!=='review'){void action(review);return;}showStage(target);resetPosition();if(target==='results')void poll();
    }));
    find('destination').addEventListener('change',()=>void connect(connectionLoading?connectionSelections:[...selected]));
    find('refresh').addEventListener('click',()=>void connect(connectionLoading?connectionSelections:[...selected],true,true));
    ['filter','search'].forEach(name=>find(name).addEventListener('input',renderObjects));
    find('select-all').addEventListener('change',()=>{if(busy)return;const checked=find('select-all').checked;
        filtered().filter(x=>!reuseOnly(x) && !dependencies.has(x.key)).forEach(x=>checked?selected.add(x.key):selected.delete(x.key));
        void action(async()=>{invalidate();await refreshSelection();},false);});
    find('dependencies').addEventListener('change',()=>action(async()=>{invalidate();await refreshSelection();},false));
    dialog.querySelector('[data-copy-panel="review"]').addEventListener('change',event=>{
        if(event.target===find('confirm-overwrite')){update();return;}
        if(busy)return;
        reviewDirty=true;if(plan)plan.reviewToken='';find('confirm-overwrite').checked=false;placementVisibility();update();
        if(['group-mode','group','preserve-groups','folder-mode','folder-id','create-folder'].some(name=>event.target===find(name))){
            const control=event.target,run=generation;
            void action(previewPlacement,false).then(()=>{if(run===generation&&dialog.open&&stage==='review')control.focus({preventScroll:true});});
        }
    });
    find('folder-path').addEventListener('input',()=>{reviewDirty=true;if(plan)plan.reviewToken='';update();});
    find('stop').addEventListener('click',()=>action(async()=>{
        stopRequested=true;find('stop').disabled=true;
        try{await request('Cancel',{planId:operation,reviewToken:''});if(latestReport)renderResults(latestReport);}
        catch(error){stopRequested=false;find('stop').disabled=false;throw error;}
    },false));
    dialog.querySelectorAll('[data-copy-close]').forEach(button=>button.addEventListener('click',close));dialog.addEventListener('cancel',event=>{event.preventDefault();close();});
    difference.querySelectorAll('[data-copy-difference-close]').forEach(button=>button.addEventListener('click',closeDifference));difference.addEventListener('cancel',event=>{event.preventDefault();closeDifference();});
    // Row menus remain owned by their page scripts. This optional module adds one entry and
    // delegates closing to the existing row opener, without changing grid checkbox selection.
    const rowMenus = [
        {kind:'task',type:'Task'},{kind:'host',type:'Host'},{kind:'script',type:'Script'},
        {kind:'date-list',type:'DateList'},{kind:'key-certificate',type:'KeyCertificate'}
    ].map(item=>({...item,menu:document.querySelector('[data-'+item.kind+'-row-action-menu]'),context:null}));
    rowMenus.forEach(item=>{
        if(!item.menu)return;
        const button=node('button','Copy To');button.type='button';button.setAttribute('role','menuitem');
        button.setAttribute('data-'+item.kind+'-row-command','copy-to');button.dataset.environmentCopyRow=item.kind;
        const after=item.menu.querySelector('[data-'+item.kind+'-row-command="export"]');
        item.menu.insertBefore(button,after?after.nextSibling:item.menu.firstChild);item.copy=button;
    });
    document.addEventListener('click',event=>{
        const command=event.target.closest('[data-environment-copy-row]');
        if(command){
            event.preventDefault();event.stopPropagation();
            const item=rowMenus.find(item=>item.kind===command.dataset.environmentCopyRow),context=item?.context;
            if(!context || command.disabled || command.hidden)return;
            if(context.button.isConnected)context.button.click();else item.menu.hidden=true;
            void open(item.type,[context.id],context.button);return;
        }
        for(const item of rowMenus){
            const button=event.target.closest('[data-'+item.kind+'-row-menu-button]');if(!button || !item.menu)continue;
            item.context={id:button.dataset.rowId,button};
            item.copy.hidden=item.kind==='host' && button.dataset.rowId==='0' || item.kind==='script' && button.dataset.scriptBuiltIn==='true';
        }
    },true);
    document.addEventListener('maui:grid-action',event=>{
        if(String(event.detail?.action).toLowerCase()!=='copy to')return;
        const type=types[String(event.detail?.gridId).toLowerCase()];if(!type)return;
        event.preventDefault();event.stopPropagation();void open(type,event.detail.selectedIds||[],document.activeElement);
    },true);
})();
