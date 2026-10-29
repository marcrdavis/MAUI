/* Date List CRUD modal: compact creation form, tabbed editing, shared checkout and metadata. */
(() => {
    'use strict';
    const dialog = document.querySelector('[data-date-list-editor]');
    if (!dialog) return;
    const find = key => dialog.querySelector('[data-date-list-editor-' + key + ']');
    const form = find('form'), name = find('name'), description = find('description');
    const status = find('status'), save = find('save'), reload = find('reload'), override = find('override');
    const token = document.querySelector('.maui-editor-routing-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const admin = document.documentElement.dataset.moveitAdmin === 'true';
    let id = '', revision = '', baseline = '', busy = false, blocked = false, uncertain = false, changed = false, sequence = 0;
    let entries = [], importing = false, editing = false, activeTab = 'date-list', metadataFields = [];
    let autoSave = false, autoTimer = 0, autoBlocked = '', activeSave = null, manualRecovery = false, preferencesBusy = false;
    let canUndo = false, readOnly = false;
    const localUndo = window.MauiModalUI.createLocalUndo({
        capture: () => JSON.parse(snapshot()),
        restore: value => {
            name.value=value.name; description.value=value.description; entries=value.entries.slice();
            renderEntries(); renderMetadata(value.metadata);
        },
        changed: () => { canUndo=localUndo.available(); draftStatus(); controls(); queueAutoSave(); }
    });
    function metadataValues() {
        const values = {};
        find('metadata').querySelectorAll('[data-date-list-editor-metadata-field]').forEach(control => { values[control.dataset.dateListEditorMetadataField] = control.value; });
        return values;
    }
    const draft = () => ({ name: name.value.trim(), description: description.value, entries: entries.slice(), metadata: editing ? metadataValues() : {} });
    const snapshot = () => JSON.stringify(draft());
    const dirty = () => !readOnly && snapshot() !== baseline;
    function message(text = '', error = false) { status.textContent = text; status.classList.toggle('error', error); }
    function draftStatus() { if (!blocked) message(dirty() ? (autoSave && editing && !manualRecovery ? 'Changes pending Auto Save…' : 'Unsaved changes') : readOnly ? window.MauiModalUI.readOnlyMessage('Date List') : editing ? 'Saved' : 'New Date List'); }
    function autoCloseMode() { return editing && autoSave && !manualRecovery && !blocked; }
    function closeActions() { find('actions-menu').hidden = true; find('actions').setAttribute('aria-expanded', 'false'); }
    function controls() {
        const waiting = busy || importing || preferencesBusy;
        dialog.querySelectorAll('input,textarea,select,button').forEach(control => { control.disabled = waiting; });
        dialog.querySelectorAll('input,textarea,select').forEach(control => {
            if (readOnly) { if (control.tagName === 'SELECT' || ['radio','checkbox','file'].includes(control.type)) control.disabled = true; else control.readOnly = true; }
        });
        save.hidden = readOnly; save.disabled = waiting || blocked || readOnly || !autoCloseMode() && ((!readOnly && !name.value.trim()) || editing && !dirty() || !window.MauiModalUI.formValid(dialog));
        save.textContent = busy ? (editing ? 'Saving…' : 'Creating…') : autoCloseMode() ? 'Close' : editing ? 'Save' : 'Create Date List';
        dialog.querySelector('.script-editor-footer [data-date-list-editor-close]').hidden = autoCloseMode();
        reload.hidden = readOnly || !blocked || !id;
        override.hidden = readOnly || !blocked || !admin || !id;
        for (const key of ['year','month','day','add','import']) find(key).disabled = waiting || readOnly;
        find('auto-save').disabled = waiting || !editing || readOnly;
        find('actions').disabled = waiting || !editing || blocked;
        dialog.querySelector('[data-date-list-editor-action="undo"]').disabled = waiting || !editing || blocked || !canUndo;
        window.MauiModalUI.viewOnly(dialog,readOnly);
    }
    function layout(isEditing) {
        editing = isEditing; dialog.classList.toggle('is-editing', editing); find('tabs').hidden = !editing;
        const holder = editing ? find('native-fields') : find('new-identity');
        holder.append(find('name-field'), find('description-field'));
        if (editing) holder.append(find('resource-groups').closest('label'));
        find('new-identity').hidden = editing;
        find('tab-actions').hidden = readOnly;
        dialog.querySelector('[data-date-list-editor-tab="date-list"]').hidden = false;
        name.readOnly = description.readOnly = readOnly;
        find('name-label').textContent = 'Date List Name';
        find('kicker').hidden = !editing;
        find('kicker').textContent = editing ? 'DATE LIST EDITOR' : 'NEW DATE LIST';
    }
    function switchTab(key) {
        const allowed = ['date-list','properties','tasks'];
        activeTab = editing && allowed.includes(key) ? key : allowed[0]; closeActions();
        dialog.querySelectorAll('[data-date-list-editor-tab]').forEach(button => { const selected = button.dataset.dateListEditorTab === activeTab; button.classList.toggle('active', selected); button.setAttribute('aria-selected', String(selected)); button.tabIndex = selected ? 0 : -1; });
        dialog.querySelectorAll('[data-date-list-editor-panel]').forEach(panel => { panel.hidden = panel.dataset.dateListEditorPanel !== activeTab; });
    }
    const lease = window.MauiObjectEditLease.create({ type: 'DateList', token, busy: () => busy || importing || preferencesBusy,
        onLost: text => { blocked = true; clearTimeout(autoTimer); message(text, true); controls(); } });
    async function request(target, body, withLease = false) {
        const headers = { Accept: 'application/json', RequestVerificationToken: token };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (withLease && lease.id) headers['X-MAUI-Edit-Lease'] = lease.id;
        const response = await fetch(target, { method: body === undefined ? 'GET' : 'POST', headers, credentials: 'same-origin', cache: 'no-store', body: body === undefined ? undefined : JSON.stringify(body) });
        const data = await response.json().catch(() => null);
        if (response.status === 401) window.mauiSessionExpired?.();
        if (!response.ok || data?.success !== true) {
            const error = new Error(data?.message || (response.status === 401 ? 'Your session expired. Close this editor and sign in again.' : 'The Date List response could not be confirmed.'));
            error.status = response.status; error.payload = data; error.ambiguous = response.ok && !data; throw error;
        }
        return data;
    }
    function url(key, objectId = '') { const target = new URL(dialog.dataset[key], window.location.href); if (objectId) target.searchParams.set('id', objectId); return target.toString(); }
    function validateModel(model) {
        if (!model || !/^[1-9]\d*$/.test(model.id) || !model.editVersion || !Array.isArray(model.entries) || model.entries.some(value => typeof value !== 'string') || ['name','description'].some(key => typeof model[key] !== 'string')) throw new Error('The complete saved Date List could not be confirmed. Reload the Date List before saving again.');
        return model;
    }
    function apply(model) {
        find('resource-groups').value = model.resourceGroups || '—';
        validateModel(model); id = model.id; revision = model.editVersion; layout(true);
        name.value = model.name; description.value = model.description; entries = model.entries.slice(); renderEntries();
        find('title').textContent = model.name; find('id').textContent = id; find('type').textContent = 'Date List';
        const subtitle = find('subtitle'), descriptionText = model.description.trim();
        const copy = document.createElement('span'); copy.className = 'date-list-editor-subtitle-description'; copy.textContent = descriptionText ? descriptionText + ' · ' : '';
        const identity = document.createElement('span'); identity.className = 'date-list-editor-subtitle-id'; identity.textContent = 'Date List ID: ' + id;
        subtitle.replaceChildren(copy, identity); subtitle.title = descriptionText ? descriptionText + ' · Date List ID: ' + id : identity.textContent;
    }
    function field(label, control, full = false) {
        const wrapper = document.createElement('label'); wrapper.className = 'field-block' + (full ? ' full-span' : '');
        const caption = document.createElement('span'); caption.textContent = label; wrapper.append(caption, control); return wrapper;
    }
    function renderMetadata(values = {}) {
        const container = find('metadata'); container.replaceChildren();
        for (const definition of metadataFields) {
            const type = String(definition.type || '').toLowerCase(); let control;
            if (['boolean','list'].includes(type)) {
                control = document.createElement('select');
                (type === 'boolean' ? ['false','true'] : definition.possibleValues || []).forEach(value => control.add(new Option(type === 'boolean' ? value === 'true' ? 'True' : 'False' : value, value)));
            } else if (type.includes('multiline')) { control = document.createElement('textarea'); control.rows = 3; }
            else { control = document.createElement('input'); control.type = ['number','numeric'].includes(type) ? 'number' : type === 'date' ? 'date' : 'text'; }
            control.className = 'text-input' + (type.includes('multiline') ? ' textarea-input' : ''); control.value = type === 'boolean' ? String(values[definition.name]).toLowerCase() === 'true' ? 'true' : 'false' : values[definition.name] ?? ''; control.required = !readOnly && Boolean(definition.required);
            control.dataset.dateListEditorMetadataField = definition.name;
            if (!readOnly) window.mauiConfigureMetadataControl?.(control, definition);
            const wrapper = field(definition.label || definition.name, control, type.includes('multiline')); if (definition.description) wrapper.title = definition.description;
            container.append(wrapper);
        }
    }
    function renderAudit(audit = {}) {
        const container = find('audit'); container.replaceChildren();
        for (const [label, value] of [['Created On',audit.createdUtc],['Created By',audit.createdBy],['Modified On',audit.modifiedUtc],['Modified By',audit.modifiedBy]]) {
            const control = document.createElement('input'); control.className = 'text-input'; control.readOnly = true;
            control.value = value ? label.endsWith(' On') ? new Date(value).toLocaleString() : value : '—'; container.append(field(label, control));
        }
    }
    function renderTasks(tasks = []) {
        const container = find('tasks'); container.replaceChildren(); find('task-count').textContent = String(tasks.length);
        container.hidden = tasks.length === 0; find('tasks-empty').hidden = tasks.length > 0;
        for (const task of tasks) {
            const row = document.createElement('div'); row.className = 'script-editor-task-row';
            const number = document.createElement('div'); number.className = 'script-editor-task-id'; number.textContent = task.id;
            const link = document.createElement('button'); link.type = 'button'; link.className = 'row-primary-link'; link.textContent = task.name || task.id;
            link.addEventListener('click', async () => { try { await window.MauiEditorRouting.openTask(task.id, task.name || ''); } catch (error) { message(error.message, true); } });
            row.append(number, link); container.append(row);
        }
    }
    async function read() {
        let data = await request(url('shellUrl', id), undefined, true); readOnly = data.readOnly === true; validateModel(data.dateList);
        if (!readOnly && !lease.id && data.checkoutObjectsForDurationOfEdit && await lease.open(id, true, true)) data = await request(url('shellUrl', id), undefined, true);
        apply(data.dateList); localUndo.rebase(true); canUndo = false; metadataFields = data.fields || []; renderMetadata(data.metadata); renderAudit(data.audit); renderTasks(data.tasks);
        autoSave = !readOnly && Boolean(data.preferences?.autoSave); find('auto-save').checked = autoSave; baseline = snapshot(); localUndo.rebase(true); canUndo=false; switchTab(activeTab);
    }
    async function open(objectId = '') {
        if (dialog.open) return false;
        const current = ++sequence; await lease.close(); if (current !== sequence) return false;
        clearTimeout(autoTimer); window.MauiModalUI.viewOnly(dialog,false); readOnly = false; canUndo = false; id = String(objectId || '').trim(); revision = ''; blocked = false; uncertain = false; changed = false; manualRecovery = false; autoBlocked = '';
        autoSave = false; find('auto-save').checked = false; activeTab = 'date-list'; metadataFields = []; renderMetadata(); renderAudit(); renderTasks();
        name.value = ''; description.value = ''; entries = []; renderEntries(); layout(Boolean(id)); switchTab('date-list');
        find('title').textContent = id ? 'Date List' : 'New Date List'; find('subtitle').textContent = id ? 'Date List ID: ' + id : 'Scheduled dates and recurring monthly entries'; find('subtitle').title = '';
        baseline = snapshot(); localUndo.rebase(true); canUndo=false; busy = true; controls(); save.textContent = editing ? 'Save' : 'Create Date List'; message(id ? 'Loading Date List…' : 'New Date List'); dialog.showModal();
        try { if (id) await read(); draftStatus(); }
        catch (error) { blocked = true; message(error.message, true); }
        finally { busy = false; controls(); if (!editing) name.focus(); }
        return true;
    }
    function queueAutoSave() {
        clearTimeout(autoTimer);
        if (!dialog.open || !editing || !autoSave || !dirty() || manualRecovery || blocked || busy || importing || preferencesBusy || autoBlocked === snapshot()) return;
        autoTimer = setTimeout(() => {
            const focused = document.activeElement;
            if (focused && dialog.contains(focused) && (focused instanceof HTMLTextAreaElement || focused instanceof HTMLInputElement && !['checkbox','radio','button'].includes(focused.type))
                || document.querySelector('[data-date-list-action-dialog]').open) { queueAutoSave(); return; }
            void saveDraft(true);
        },650);
    }
    async function performSave(automatic) {
        if (busy || importing || blocked || preferencesBusy || readOnly) return false;
        if (!dirty() && editing) return true;
        const input = draft();
        if (input.entries.some(value => !validDate(value))) { message('Review the date entries before saving.',true); autoBlocked = snapshot(); return false; }
        if (!input.name) { if (editing) switchTab('properties'); name.focus(); message('Date List Name is required.',true); autoBlocked = snapshot(); return false; }
        if (editing && window.mauiValidateMetadataControls && !window.mauiValidateMetadataControls(find('metadata'))) { switchTab('properties'); message('Review the Date List metadata fields before saving.',true); autoBlocked = snapshot(); return false; }
        clearTimeout(autoTimer); busy = true; closeActions(); controls(); message('Saving…');
        try {
            lease.assert(); const creating = !id;
            const data = await request(url('saveUrl',id), { ...input, editVersion: revision },true);
            if (creating && /^\d+$/.test(data.dateList?.id)) id = data.dateList.id;
            if (creating) {
                validateModel(data.dateList); changed = true; canUndo = false; baseline = snapshot();
                // A confirmed creation returns to its caller without loading or checking out an editor.
                await finishClose(); return true;
            }
            apply(data.dateList);
            canUndo = localUndo.available(); changed = true;
            if (data.metadata) renderMetadata(data.metadata);
            if (data.audit) renderAudit(data.audit);
            baseline = snapshot(); localUndo.rebase(); manualRecovery = false; autoBlocked = ''; message(data.warning || 'Saved',Boolean(data.warning)); return true;
        } catch (error) {
            if (error.payload?.dateListId && /^\d+$/.test(error.payload.dateListId)) id = error.payload.dateListId;
            uncertain = Boolean(error.payload?.moveItSaved || error.ambiguous || !error.status || error.status >= 500 && error.payload?.moveItSaved !== false); changed = changed || uncertain;
            blocked = Boolean(uncertain || error.payload?.editConflict || error.payload?.leaseLost || error.payload?.checkoutConflict);
            autoBlocked = snapshot();
            message((error.message || 'Date List could not be saved.') + (blocked ? ' ' + window.MauiObjectEditLease.blockedMessage('DateList') : ''),true);
            if (automatic) manualRecovery = true;
            return false;
        } finally { busy = false; controls(); }
    }
    function saveDraft(automatic = false) {
        if (activeSave) return activeSave;
        activeSave = performSave(automatic).finally(() => { activeSave = null; }); return activeSave;
    }
    async function close() {
        if (importing || preferencesBusy || busy && !activeSave) return false;
        if (activeSave && (!autoSave || !await activeSave)) return false;
        if (autoSave && editing && !manualRecovery && !blocked && dirty() && !await saveDraft()) return false;
        if (dirty() && !await window.mauiConfirm?.({ title:'Discard Date List Changes?',message:'Close this editor and discard unsaved Date List changes?',confirmText:'Discard Changes',danger:true })) return false;
        return await finishClose();
    }
    async function finishClose() {
        clearTimeout(autoTimer); closeActions(); busy = true; controls(); await lease.close(); sequence++; dialog.close(); busy = false;
        window.dispatchEvent(new CustomEvent('maui:date-list-editor-closed',{detail:{id,changed}})); return true;
    }
    form.addEventListener('submit',event => { event.preventDefault(); if (autoCloseMode()) void close(); else void saveDraft(); });
    dialog.querySelectorAll('[data-date-list-editor-close]').forEach(button => button.addEventListener('click',() => { void close(); }));
    dialog.addEventListener('cancel',event => { event.preventDefault(); if (!find('actions-menu').hidden) closeActions(); else void close(); });
    form.addEventListener('input',event => { if (!busy && !blocked && !readOnly) localUndo.record(event.target,'Date List field',true); draftStatus(); controls(); queueAutoSave(); });
    form.addEventListener('change',event => { if (!busy && !blocked && !readOnly) localUndo.record(event.target,'Date List field',true); controls(); queueAutoSave(); });
    form.addEventListener('focusout',() => { localUndo.end(); queueAutoSave(); });
    dialog.querySelectorAll('[data-date-list-editor-tab]').forEach(button => {
        button.addEventListener('click',() => switchTab(button.dataset.dateListEditorTab));
        button.addEventListener('keydown',event => { const keys = ['date-list','properties','tasks']; if (!['ArrowRight','ArrowLeft','Home','End'].includes(event.key)) return; event.preventDefault(); const index = keys.indexOf(activeTab); switchTab(event.key === 'Home' ? keys[0] : event.key === 'End' ? keys[2] : keys[(index + (event.key === 'ArrowRight' ? 1 : keys.length - 1)) % keys.length]); dialog.querySelector('[data-date-list-editor-tab="'+activeTab+'"]').focus(); });
    });
    const year = find('year'), month = find('month'), day = find('day');
    const currentYear = new Date().getFullYear();
    for (let value = currentYear; value <= Math.max(currentYear, 2045); value++) year.add(new Option(String(value), String(value)));
    month.add(new Option('All (*)', '*'));
    ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'].forEach((label,index) => month.add(new Option(label,String(index+1).padStart(2,'0'))));
    for (let value = 1; value <= 31; value++) day.add(new Option(String(value),String(value).padStart(2,'0')));
    function validDate(value) {
        const match = /^(\d{4})-(\*|\d{2})-(\d{2})$/.exec(value);
        if (!match || Number(match[1]) < 1 || Number(match[3]) < 1 || Number(match[3]) > 31) return false;
        if (match[2] === '*') return true;
        const date = new Date(0); date.setUTCFullYear(Number(match[1]),Number(match[2])-1,Number(match[3]));
        return date.getUTCFullYear() === Number(match[1]) && date.getUTCMonth() === Number(match[2])-1 && date.getUTCDate() === Number(match[3]);
    }
    function renderEntries() {
        const container = find('entries'); container.replaceChildren(); find('entries-empty').hidden = entries.length > 0;
        entries.forEach((value,index) => {
            const row = document.createElement('div'); row.className = 'date-list-entry-row';
            const text = document.createElement('span'); text.textContent = value;
            const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'danger-button'; remove.textContent = 'Delete'; remove.hidden = readOnly;
            remove.setAttribute('aria-label','Delete date ' + value);
            remove.addEventListener('click',() => { if (busy || importing || readOnly) return; entries.splice(index,1); renderEntries(); localUndo.record(null,'Delete Date Entry'); draftStatus(); controls(); queueAutoSave(); });
            row.append(text,remove); container.append(row);
        });
    }
    function addEntries(values) {
        const unique = entries.slice(), known = new Set(entries);
        for (const value of values) if (!known.has(value)) { unique.push(value); known.add(value); }
        if (unique.length > 10000) throw new Error('A Date List may contain at most 10000 entries in this editor.');
        const added = unique.length - entries.length; entries = unique; renderEntries(); localUndo.record(null,'Add Date Entries'); draftStatus(); controls(); queueAutoSave(); return added;
    }
    find('add').addEventListener('click',() => {
        const value = year.value + '-' + month.value + '-' + day.value;
        if (!validDate(value)) { message('Select a valid date before adding the entry.',true); return; }
        try { if (!addEntries([value])) message('This date is already in the list.',true); } catch (error) { message(error.message,true); }
    });
    find('import').addEventListener('click',() => find('file').click());
    find('file').addEventListener('change',async event => {
        const file = event.target.files?.[0]; event.target.value = ''; if (!file || busy || importing || readOnly) return;
        if (!/\.txt$/i.test(file.name) || file.size > 2 * 1024 * 1024) { message('Select a .txt file no larger than 2 MiB.',true); return; }
        importing = true; controls(); const current = sequence;
        try {
            const bytes = new Uint8Array(await file.arrayBuffer()); const encoding = bytes[0] === 255 && bytes[1] === 254 ? 'utf-16le' : bytes[0] === 254 && bytes[1] === 255 ? 'utf-16be' : 'utf-8';
            const text = new TextDecoder(encoding,{fatal:true}).decode(bytes); const values = [];
            for (const [index,line] of text.replace(/^\uFEFF/,'').split(/\r\n?|\n/).entries()) {
                if (!line.trim()) continue;
                if (!validDate(line)) throw new Error('Invalid date on line ' + (index+1) + '. Use YYYY-MM-DD or YYYY-*-DD. No entries were imported.');
                values.push(line);
            }
            if (!values.length) throw new Error('The text file contains no date entries.');
            if (current !== sequence || !dialog.open) return;
            const added = addEntries(values); message('Imported ' + added + ' date entries. Duplicate dates were skipped.');
        } catch (error) { message(error.message || 'The date entries could not be read.',true); }
        finally { importing = false; controls(); queueAutoSave(); }
    });
    reload.addEventListener('click',async () => {
        if (busy || !id) return;
        if (dirty() && !await window.mauiConfirm?.({title:'Reload Date List?',message:'Discard unsaved changes and load the current Date List from MOVEit?',confirmText:'Reload Date List',danger:true})) return;
        clearTimeout(autoTimer); busy = true; controls();
        try { await lease.close(); await read(); blocked = false; uncertain = false; manualRecovery = false; autoBlocked = ''; message('Saved'); }
        catch (error) { blocked = true; message(error.message,true); }
        finally { busy = false; controls(); }
    });
    override.addEventListener('click',async () => {
        if (busy || !blocked || !id || !admin) return;
        busy = true; controls();
        try { const version = await lease.override(id); if (version) { if (!revision) await read(); revision = version; blocked = false; uncertain = false; manualRecovery = true; autoBlocked = ''; message(window.MauiObjectEditLease.restoredMessage); } }
        catch (error) { message(error.message,true); }
        finally { busy = false; controls(); }
    });
    find('auto-save').addEventListener('change',async () => {
        const requested = find('auto-save').checked; preferencesBusy = true; controls(); clearTimeout(autoTimer);
        try { const data = await request(url('preferencesUrl'),{autoSave:requested}); if (typeof data.preferences?.autoSave !== 'boolean') throw new Error('The Auto Save preference could not be confirmed.'); autoSave = data.preferences.autoSave; autoBlocked = ''; draftStatus(); }
        catch (error) { message(error.message,true); }
        finally { preferencesBusy = false; find('auto-save').checked = autoSave; controls(); queueAutoSave(); }
    });
    find('actions').addEventListener('click',event => { event.stopPropagation(); if (busy) return; const menu = find('actions-menu'); menu.hidden = !menu.hidden; find('actions').setAttribute('aria-expanded',String(!menu.hidden)); });
    document.addEventListener('click',event => { if (!event.target.closest('[data-date-list-editor-actions]') && !event.target.closest('[data-date-list-editor-actions-menu]')) closeActions(); });
    window.addEventListener('beforeunload',event => { if (dialog.open && dirty()) { event.preventDefault(); event.returnValue = ''; } });
    window.MauiModalUI?.register(dialog,{error:text => message(text,true),clear:() => draftStatus(),footerStatus:status});
    window.MauiModalUI?.registerContext('date-list-editor',dialog,{
        capture:() => ({id,tab:activeTab}), prepare:async () => !busy && !blocked && (!dirty() || await saveDraft()),
        restore:async state => { if (await open(state.id)) switchTab(state.tab || 'date-list'); }
    });
    window.MauiDateListEditor = {open};
    // Delete/export reuse the Resources CRUD guardrails and Recycle Bin recovery.
    // Only Delete hands off checkout; Undo reverses the local draft in this editor.
    const actionDialog = document.querySelector('[data-date-list-action-dialog]');
    const af = key => actionDialog.querySelector('[data-date-list-action-' + key + ']');
    let actionKind = '', actionId = '', actionBusy = false, deleteWithoutRecycle = false;
    function actionError(text = '') { af('error').textContent = text; af('error').hidden = !text; }
    function actionControls() {
        actionDialog.querySelectorAll('button,input').forEach(control => { control.disabled = actionBusy || control.dataset.unavailable === 'true'; });
        af('confirm').disabled = actionBusy || af('confirm').dataset.unavailable === 'true';
    }
    async function exportDateList() {
        if (busy || importing || !editing || readOnly) return;
        closeActions();
        if (dirty() && autoSave && !manualRecovery && !await saveDraft()) return;
        if (dirty() && !await window.mauiConfirm?.({title:'Export Saved Date List?',message:'Export the saved MOVEit Date List? Unsaved changes in this editor will remain here.',confirmText:'Export Date List'})) return;
        importing = true; controls(); message('Exporting Date List…');
        try {
            const target = new URL(dialog.dataset.exportUrl,window.location.href); target.searchParams.set('dateListId',id);
            const response = await fetch(target.toString(),{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/zip'}});
            if (response.status === 401) window.mauiSessionExpired?.();
            if (!response.ok) { const result = await response.json().catch(() => null); throw new Error(result?.message || `Unable to export Date List (${response.status}).`); }
            const blob = await response.blob(); if (!blob.size) throw new Error('The Date List export was empty.');
            const disposition = response.headers.get('content-disposition') || '';
            const utf8 = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1], quoted = disposition.match(/filename="([^"]+)"/i)?.[1];
            const filename = utf8 ? decodeURIComponent(utf8) : quoted || 'Date List_' + id + '.zip';
            const objectUrl = URL.createObjectURL(blob), link = document.createElement('a'); link.href = objectUrl; link.download = filename; link.hidden = true;
            document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(objectUrl),1000); message('Exported 1 Date List.');
        } catch (error) { message(error?.message || 'Unable to export Date List.',true); }
        finally { importing = false; controls(); queueAutoSave(); }
    }
    async function beginAction(kind) {
        closeActions();
        if (busy || importing || !id || blocked || readOnly) return;
        if (kind === 'undo') { if (canUndo) await localUndo.undo(); return; }
        if (kind === 'export') return exportDateList();
        if (kind !== 'delete' ) return;
        const selectedId=id, selectedName=find('title').textContent;
        if (!await close()) return;
        actionKind='delete'; actionId=selectedId; actionBusy=true; deleteWithoutRecycle=false;
        af('history').replaceChildren(); af('history').hidden=true; actionError();
        af('title').textContent='Delete Date List'; af('confirm').textContent='Delete Date List'; af('confirm').className='danger-button';
        af('message').textContent='Checking Date List “'+selectedName+'”…';
        af('confirm').dataset.unavailable='true'; actionControls(); actionDialog.showModal();
        try {
            const preflight=await request(url('deletePreflightUrl'),{dateListId:actionId});
            af('message').textContent='Delete Date List “'+selectedName+'”? MAUI will create a Recycle Bin recovery copy when available.';
            af('confirm').dataset.unavailable=String(preflight.success !== true);
            if(preflight.success !== true) actionError(preflight.message || 'This Date List cannot be deleted.');
        } catch(error) { actionError(error.message); }
        finally { actionBusy=false; actionControls(); }
    }
    async function closeAction() {
        if (actionBusy) return;
        actionDialog.close(); if (actionId) await open(actionId);
    }
    actionDialog.querySelectorAll('[data-date-list-action-close]').forEach(button => button.addEventListener('click',() => { void closeAction(); }));
    actionDialog.addEventListener('cancel',event => { event.preventDefault(); void closeAction(); });
    af('form').addEventListener('submit',async event => {
        event.preventDefault(); if (actionBusy || af('confirm').disabled) return;
        actionBusy = true; actionControls(); actionError();
        try {
            const result = await request(url('deleteUrl'),{dateListIds:[actionId],deleteWithoutRecycle});
            actionDialog.close(); window.dispatchEvent(new CustomEvent('maui:date-list-editor-closed',{detail:{id:actionId,changed:true}}));
            if (result.warning && !window.MauiModalUI?.showError(result.warning)) window.mauiShowToast?.(result.warning,'error');
        } catch (error) {
            actionError(error.message);
            if (error.payload?.code === 'RecycleCaptureFailed') { deleteWithoutRecycle = true; af('confirm').textContent = 'Delete Anyway'; }
            else if (!error.status || error.status >= 500 || error.ambiguous || error.payload?.moveItSaved) { af('confirm').dataset.unavailable = 'true'; actionError(error.message + ' Close this action and inspect the current Date List before retrying.'); }
        } finally { actionBusy = false; actionControls(); }
    });
    dialog.querySelectorAll('[data-date-list-editor-action]').forEach(button => button.addEventListener('click',() => { void beginAction(button.dataset.dateListEditorAction); }));

})();
