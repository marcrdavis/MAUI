/* Shared MOVEit effective-permission UI. Server endpoints enforce the same policy independently. */
(() => {
    'use strict';
    const root = document.documentElement, data = document.querySelector('[data-object-creation-access]');
    if (!data) return;
    let creation;
    try { creation = JSON.parse(data.textContent); } catch { creation = { isAdmin: false, groups: {} }; }
    const deniedMessage = 'Your MOVEit account does not permit this operation.';
    const generations = new WeakMap();
    const canCreate = type => creation.isAdmin === true || (creation.groups?.[type]?.length || 0) > 0;
    const gridTypes = { tasks:'Task', hosts:'Host', scripts:'Script', datelists:'DateList', globalparameters:'GlobalParameter', keysandcerts:'KeyCertificate', keysandcertificates:'KeyCertificate' };
    const actionType = text => /import.*key.*cert/i.test(text) ? 'KeyCertificate' : /\b(tasks?|hosts?|scripts?)\b/i.test(text) ? ({task:'Task',host:'Host',script:'Script'}[text.match(/\b(tasks?|hosts?|scripts?)\b/i)[1].toLowerCase().replace(/s$/,'')])
        : /\btls\b|certificate/i.test(text) ? 'TLS' : /\bssh\b/i.test(text) ? 'SSH' : /\bpgp\b/i.test(text) ? 'PGP' : /date list/i.test(text) ? 'DateList' : /parameter/i.test(text) ? 'GlobalParameter' : '';
    const command = button => button.dataset.taskRowCommand || button.dataset.hostRowCommand || button.dataset.scriptRowCommand
        || button.dataset.dateListRowCommand || button.dataset.keyCertificateRowCommand || button.dataset.globalParameterRowCommand || button.dataset.gridAction || '';
    function operation(text) {
        const value = text.toLowerCase();
        // These change the referenced Tasks, not the Host; their preflight checks each Task.
        if (/-assigned-tasks$/.test(value)) return '';
        if (/^(create resource group|add to resource group|copy to|copy-to)$/.test(value)) return 'Admin';
        if (/^(new |import |clone|create advanced)/.test(value)) return 'Create';
        if (value === 'json editor') return 'Edit';
        if (/delete/.test(value)) return 'Delete';
        if (/^(edit|export|undo|toggle-enabled|enable|disable|notifications|test)/.test(value)) return 'Edit';
        if (/^(run(?!-history)|kill)/.test(value)) return 'Run';
        return '';
    }
    function remember(button) {
        if ('objectAccessBaseDisabled' in button.dataset) return;
        button.dataset.objectAccessBaseDisabled = String(button.disabled);
        button.dataset.objectAccessBaseTitle = button.title;
    }
    function lock(button, denied) {
        if (denied) { button.dataset.objectAccessDenied = 'true'; button.disabled = true; button.title = deniedMessage; }
        else { delete button.dataset.objectAccessDenied; if ('objectAccessBaseTitle' in button.dataset) button.title = button.dataset.objectAccessBaseTitle; }
    }
    function reset(menuNode) {
        menuNode?.querySelectorAll('[data-object-access-base-disabled]').forEach(button=>{
            button.disabled=button.dataset.objectAccessBaseDisabled==='true';
            button.title=button.dataset.objectAccessBaseTitle || '';
            delete button.dataset.objectAccessBaseTitle;delete button.dataset.objectAccessBaseDisabled;delete button.dataset.objectAccessDenied;
        });
    }
    async function request(endpoint, query = {}) {
        const url = new URL(endpoint, location.href);
        Object.entries(query).forEach(([key,value]) => (Array.isArray(value)?value:[value]).forEach(item=>url.searchParams.append(key,String(item))));
        const response = await fetch(url,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const payload = await response.json();
        if (!response.ok) throw new Error(payload?.message || 'MOVEit permissions could not be verified.');
        return payload;
    }
    let refreshing, refreshedAt = 0;
    async function refresh() {
        if (creation.isAdmin || !root.dataset.objectCreationAccessUrl) return;
        if (refreshing) return refreshing;
        if (Date.now()-refreshedAt < 2000) return;
        refreshing = request(root.dataset.objectCreationAccessUrl).then(value=>{
            creation=value; refreshedAt=Date.now();
            document.querySelectorAll('[data-object-create-group]').forEach(configureGroup);
            updateCreateButtons();
        }).finally(()=>{refreshing=null;});
        return refreshing;
    }
    async function readMany(type,ids) {
        if (creation.isAdmin) return ids.map(()=>({canEdit:true,canDelete:true,canRun:true}));
        const rights=[];
        for (let index=0;index<ids.length;index+=50) {
            const batch=ids.slice(index,index+50);
            const result=await request(root.dataset.objectAccessBatchUrl,{type,ids:batch});
            rights.push(...batch.map(id=>result[id] || {canEdit:false,canDelete:false,canRun:false}));
        }
        return rights;
    }
    function showError(message) {
        if (!window.MauiModalUI?.showMessage(message,'error')) window.mauiDisplayToast?.(message,'error');
    }
    async function read(type,id) {
        if (creation.isAdmin === true) return {canEdit:true,canDelete:true,canRun:true};
        return request(root.dataset.objectAccessUrl,{type,id});
    }
    async function mayEdit(type,id) {
        try {
            const rights = await read(type,id);
            if (rights.canEdit) return true;
            showError(deniedMessage);
        } catch (error) { showError(error.message); }
        return false;
    }
    async function menu(menuNode,type,id) {
        if (!menuNode) return;
        const token = {}; generations.set(menuNode,token);
        const buttons = [...menuNode.querySelectorAll('button')].map(button => ({button, op:operation(command(button)), blocked:button.disabled && button.dataset.objectAccessDenied !== 'true'})).filter(item=>item.op);
        buttons.forEach(item=>{remember(item.button);lock(item.button,true);});
        try {
            await refresh();
            const rights = await read(type,id);
            if(generations.get(menuNode)!==token)return;
            buttons.forEach(({button,op,blocked})=>{
                const allowed = op==='Admin' ? creation.isAdmin===true : op==='Create' ? rights.canEdit && canCreate(type) : op==='Delete' ? rights.canDelete : op==='Run' ? rights.canRun : rights.canEdit;
                lock(button,!allowed);button.disabled = blocked || !allowed;
            });
        } catch { /* Keep actions unavailable until their permissions can be read. */ }
    }
    async function bulk(grid,menuNode,ids) {
        const type = gridTypes[grid?.dataset.grid]; if(!type||!menuNode)return;
        const token={};generations.set(menuNode,token);
        const buttons=[...menuNode.querySelectorAll('[data-grid-action]')].map(button=>({button,op:operation(command(button)),blocked:button.disabled&&button.dataset.objectAccessDenied!=='true'})).filter(item=>item.op);
        const targets=(ids?.length?ids:[...grid.querySelectorAll('tbody tr[data-row-id]')].filter(row=>!row.hidden).map(row=>row.dataset.rowId));
        buttons.forEach(item=>{remember(item.button);lock(item.button,true);});
        try {
            await refresh();
            const needsObjects=buttons.some(item=>item.op!=='Create');
            const rights=needsObjects?await readMany(type,targets):[];
            if(generations.get(menuNode)!==token)return;
            buttons.forEach(({button,op,blocked})=>{
                const targetType=actionType(command(button))||type;
                const allowed=op==='Admin' ? creation.isAdmin===true : op==='Create' ? targetType==='KeyCertificate' ? ['SSH','TLS','PGP'].some(canCreate) : canCreate(targetType)
                    : rights.length>0&&rights.every(access=>op==='Delete'?access.canDelete:op==='Run'?access.canRun:access.canEdit);
                lock(button,!allowed);button.disabled=blocked||!allowed;
            });
        } catch { /* Mixed or unverifiable selections never enable a write action. */ }
    }
    // Selection exists only for actions actually offered by this grid. For example,
    // Run-only Task access does not enable selection when no Run bulk action exists.
    const selections = new WeakMap();
    function bulkOperations(grid) {
        const menu = grid.querySelector('[data-bulk-button]')?.closest('[data-menu]');
        return [...(menu?.querySelectorAll('[data-grid-action]') || [])]
            .filter(button => !button.hidden && button.dataset.objectAccessBaseDisabled !== 'true' && (!button.disabled || button.dataset.objectAccessDenied === 'true'))
            .map(button => operation(command(button))).filter(op => op && op !== 'Create');
    }
    function permits(op, access) {
        return op === 'Admin' ? creation.isAdmin === true : op === 'Delete' ? access?.canDelete === true
            : op === 'Run' ? access?.canRun === true : access?.canEdit === true;
    }
    function canBulk(grid, ids) {
        const state = selections.get(grid);
        if (!state) return !gridTypes[grid?.dataset.grid];
        return !state.pending && ids.length > 0 && state.ops.some(op => ids.every(id => permits(op,state.rights.get(id))));
    }
    async function selection(grid, rows, changed) {
        const type = gridTypes[grid?.dataset.grid]; if (!type) return;
        const state = {pending:true,ops:bulkOperations(grid),rights:new Map()}; selections.set(grid,state);
        const eligible = rows.filter(row => {
            const checkbox = row.querySelector('[data-row-select]');
            if (!checkbox) return false;
            checkbox.disabled = true;
            checkbox.dataset.objectSelectionPending = 'true';
            return checkbox.dataset.rowSelectionBlocked !== 'true';
        });
        changed();
        try {
            // No object lookups are needed for admin-only commands denied to this user.
            if (state.ops.some(op => op !== 'Admin' || creation.isAdmin === true)) {
                const rights = await readMany(type,eligible.map(row => row.dataset.rowId));
                eligible.forEach((row,index) => state.rights.set(row.dataset.rowId,rights[index]));
            }
        } catch { /* Unverifiable access leaves all selection controls unavailable. */ }
        if (selections.get(grid) !== state) return; // An in-flight grid refresh superseded this read.
        state.pending = false;
        rows.forEach(row => { const checkbox = row.querySelector('[data-row-select]'); if (checkbox) delete checkbox.dataset.objectSelectionPending; });
        eligible.forEach(row => {
            const checkbox = row.querySelector('[data-row-select]');
            checkbox.disabled = !state.ops.some(op => permits(op,state.rights.get(row.dataset.rowId)));
            checkbox.title = checkbox.disabled ? 'No permitted bulk actions are available for this row.' : '';
            checkbox.setAttribute('aria-label', checkbox.disabled ? 'This row cannot be selected for bulk actions' : 'Select row');
        });
        changed();
    }
    // Keep permission visibility separate from tab visibility and save readiness.
    // Tab handlers may show Save again; the shared observer retains a denial until reset.
    function informationSaveVisibility(control, denied) {
        if (!control.matches('button') || !/^(save|update)\b/i.test(control.textContent.trim())) return;
        if (denied) {
            if (!('objectAccessInfoSaveHidden' in control.dataset)) control.dataset.objectAccessInfoSaveHidden = String(control.hidden);
            control.dataset.objectAccessInfoSaveDenied = 'true';
            control.hidden = true;
        } else if ('objectAccessInfoSaveHidden' in control.dataset) {
            delete control.dataset.objectAccessInfoSaveDenied;
            control.hidden = control.dataset.objectAccessInfoSaveHidden === 'true';
            delete control.dataset.objectAccessInfoSaveHidden;
        }
    }
    function clearInformation(dialog) {
        if (!dialog) return;
        generations.set(dialog,{});
        dialog.querySelectorAll('[data-object-access-info-disabled]').forEach(control=>{
            if (control.dataset.objectAccessDenied==='true') control.disabled=control.dataset.objectAccessInfoDisabled==='true';
            delete control.dataset.objectAccessDenied;delete control.dataset.objectAccessInfoDisabled;
            informationSaveVisibility(control,false);
        });
    }
    async function information(dialog,type,id) {
        if (!dialog) return;
        const token={};generations.set(dialog,token);
        // Leave related-object links, log filters, tabs and Close usable in read-only Information.
        const panel=dialog.querySelector?.('[data-task-details-panel="information"],[data-host-details-panel="information"],[data-script-details-panel="information"],[data-date-list-details-panel="information"],[data-key-certificate-details-panel="information"],[data-global-parameter-details-panel="information"]');
        const controls=panel ? [...new Set([...panel.querySelectorAll('input,select,textarea,button'),...dialog.querySelectorAll('button')].filter(control=>panel.contains(control)||/^(save|update)\b/i.test(control.textContent.trim())))]
            : [...dialog.querySelectorAll('input,select,textarea,button')].filter(control=>!control.matches('button')||/^(save|update)\b/i.test(control.textContent.trim()));
        const previous=controls.map(control=>control.dataset.objectAccessDenied==='true'?control.dataset.objectAccessInfoDisabled==='true':control.disabled);
        controls.forEach((control,index)=>{control.dataset.objectAccessInfoDisabled=String(previous[index]);});
        controls.forEach(control=>{control.disabled=true;informationSaveVisibility(control,true);});
        try {
            const rights=await read(type,id);
            if(generations.get(dialog)!==token)return;
            controls.forEach((control,index)=>{control.disabled=rights.canEdit?previous[index]:true;informationSaveVisibility(control,!rights.canEdit);if(!rights.canEdit)control.dataset.objectAccessDenied='true';else delete control.dataset.objectAccessDenied;});
        } catch { if(generations.get(dialog)===token)controls.forEach(control=>{control.dataset.objectAccessDenied='true';control.disabled=true;}); }
    }
    function group(dialog) { return dialog?.querySelector('[data-object-resource-group]')?.value || ''; }
    function configureGroup(field) {
        const select=field.querySelector('[data-object-resource-group]');if(!select)return;
        const dialog=field.closest('dialog');
        const keyImport=field.dataset.objectCreateGroup==='KeyImport';
        const type=keyImport ? dialog?.querySelector('[data-import-key-certificate-type]')?.value?.toUpperCase()||'SSH' : field.dataset.objectCreateGroup;
        const editing=Boolean(field.closest('[data-script-editor-new-identity]')?.hidden);
        field.hidden=creation.isAdmin===true||editing;select.disabled=field.hidden;select.required=!field.hidden;
        if(field.hidden) { select.dispatchEvent(new Event('change',{bubbles:true})); return; }
        const chosen=select.value;select.replaceChildren(new Option('Select a Resource Group',''));
        (creation.groups?.[type]||[]).forEach(name=>select.add(new Option(name,name)));
        if([...select.options].some(option=>option.value===chosen))select.value=chosen;
        const message=field.querySelector('[data-object-group-message]');if(message)message.textContent=canCreate(type)?'':'No Resource Groups permit creating this object.';
        select.dispatchEvent(new Event('change',{bubbles:true}));
    }
    function updateCreateButtons() {
        document.querySelectorAll('[data-grid-action]').forEach(button=>{
            const action=command(button);if(operation(action)==='Admin'){remember(button);lock(button,creation.isAdmin!==true);button.disabled=button.dataset.objectAccessBaseDisabled==='true'||creation.isAdmin!==true;return;}if(operation(action)!=='Create')return;
            const type=actionType(action); if (!type) return;
            const allowed=type==='KeyCertificate'?['SSH','TLS','PGP'].some(canCreate):canCreate(type);
            remember(button); lock(button,!allowed);
            button.disabled=button.dataset.objectAccessBaseDisabled==='true'||!allowed;
        });
    }
    function initialize() {
        document.querySelectorAll('[data-object-create-group]').forEach(field=>{
            configureGroup(field);
            field.closest('dialog')?.addEventListener('change',event=>{if(event.target.matches('[data-import-key-certificate-type]'))configureGroup(field);});
        });
        updateCreateButtons();
        new MutationObserver(records=>{
            for(const record of records){
                if(record.attributeName==='disabled'&&record.target.dataset?.objectAccessDenied==='true'&&!record.target.disabled)record.target.disabled=true;
                if(record.attributeName==='hidden'&&record.target.dataset?.objectAccessInfoSaveDenied==='true'&&!record.target.hidden)record.target.hidden=true;
                if(record.attributeName==='open'&&record.target.open&&record.target.querySelector('[data-object-create-group]'))void refresh().catch(()=>{});
                if(record.attributeName==='hidden'&&record.target.matches?.('[data-script-editor-new-identity]'))record.target.querySelectorAll('[data-object-create-group]').forEach(configureGroup);
            }
        }).observe(document.body,{subtree:true,attributes:true,attributeFilter:['disabled','hidden','open']});
        document.addEventListener('click',event=>{if(event.target.closest('[data-object-access-denied="true"]')){event.preventDefault();event.stopImmediatePropagation();}},true);
    }
    window.MauiObjectAccess={canCreate,group,mayEdit,read,menu,bulk,selection,canBulk,information,clearInformation,reset};
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',initialize,{once:true});else initialize();
})();
