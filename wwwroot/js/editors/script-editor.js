/* Script CRUD modal: compact creation form, tabbed editing, shared checkout and metadata. */
(() => {
    'use strict';
    const dialog = document.querySelector('[data-script-editor]');
    if (!dialog) return;
    const find = key => dialog.querySelector('[data-script-editor-' + key + ']');
    const form = find('form'), name = find('name'), description = find('description'), source = find('source');
    const template = find('template'), status = find('status'), save = find('save'), reload = find('reload'), override = find('override');
    const token = document.querySelector('.maui-editor-routing-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const admin = document.documentElement.dataset.moveitAdmin === 'true';
    const canSaveTemplates = document.documentElement.dataset.canSaveTemplates === 'true';
    let id = '', revision = '', baseline = '', busy = false, blocked = false, uncertain = false, changed = false, sequence = 0;
    let templates = [], importing = false, editing = false, activeTab = 'script', metadataFields = [];
    let autoSave = false, autoTimer = 0, autoBlocked = '', activeSave = null, manualRecovery = false, preferencesBusy = false;
    let canUndo = false, readOnly = false, permissionReadOnly = false;
    const localUndo = window.MauiModalUI.createLocalUndo({
        capture: () => JSON.parse(snapshot()),
        restore: value => {
            if (!readOnly) {
                name.value=value.name; description.value=value.description; source.value=value.source;
                dialog.querySelectorAll('input[name="mauiScriptLanguage"]').forEach(radio=>radio.checked=radio.value===value.language);
                template.value=''; updateLines();
            }
            renderMetadata(value.metadata);
        },
        changed: () => { canUndo=localUndo.available(); draftStatus(); controls(); queueAutoSave(); }
    });
    const language = () => dialog.querySelector('input[name="mauiScriptLanguage"]:checked')?.value || 'PS1';
    function metadataValues() {
        const values = {};
        find('metadata').querySelectorAll('[data-script-editor-metadata-field]').forEach(control => { values[control.dataset.scriptEditorMetadataField] = control.value; });
        return values;
    }
    const draft = () => ({ resourceGroup: editing ? '' : window.MauiObjectAccess?.group(dialog) || '', name: name.value.trim(), description: description.value, language: language(), source: source.value, metadata: editing ? metadataValues() : {} });
    const snapshot = () => JSON.stringify(readOnly ? {metadata: metadataValues()} : draft());
    const dirty = () => !permissionReadOnly && snapshot() !== baseline;
    function message(text = '', error = false) { status.textContent = text; status.classList.toggle('error', error); }
    function draftStatus() { if (!blocked) message(permissionReadOnly ? window.MauiModalUI.readOnlyMessage('Script') : dirty() ? (autoSave && editing && !manualRecovery ? 'Changes pending Auto Save…' : 'Unsaved changes') : editing ? 'Saved' : 'New Script'); }
    function updateLines() { find('lines').textContent = Array.from({ length: source.value.split('\n').length }, (_, i) => i + 1).join('\n'); find('lines').scrollTop = source.scrollTop; }
    function autoCloseMode() { return editing && autoSave && !manualRecovery && !blocked; }
    function closeActions() { find('actions-menu').hidden = true; find('actions').setAttribute('aria-expanded', 'false'); }
    function controls() {
        const waiting = busy || importing || preferencesBusy;
        dialog.querySelectorAll('input,textarea,select,button').forEach(control => { control.disabled = waiting; });
        // Read-only configuration locks native fields, MAUI metadata and Auto Save.
        for (const control of [name, description, source]) control.readOnly = readOnly;
        if (readOnly) dialog.querySelectorAll('input[name="mauiScriptLanguage"]').forEach(control => { control.disabled = true; });
        save.hidden = permissionReadOnly; save.disabled = waiting || blocked || !autoCloseMode() && ((!readOnly && !name.value.trim()) || editing && !dirty() || !window.MauiModalUI.formValid(dialog));
        save.textContent = busy ? (editing ? 'Saving…' : 'Creating…') : autoCloseMode() ? 'Close' : editing ? 'Save' : 'Create Script';
        dialog.querySelector('.script-editor-footer [data-script-editor-close]').hidden = autoCloseMode();
        reload.hidden = readOnly || !blocked || !id;
        override.hidden = readOnly || !blocked || !admin || !id;
        const powerShell = language() === 'PS1', templateRow = find('template-row'), tools = find('tools');
        // Keep one set of controls and their keyboard order: PowerShell tools sit
        // beside its selector; VBScript retains the original tools below Source.
        const toolsParent = powerShell ? templateRow : templateRow.parentElement;
        if (tools.parentElement !== toolsParent) toolsParent.append(tools);
        templateRow.hidden = !powerShell;
        find('template-field').hidden = !powerShell; find('save-template').hidden = !powerShell;
        find('save-template').disabled = waiting || !canSaveTemplates || readOnly;
        find('auto-save').disabled = waiting || !editing;
        find('actions').disabled = waiting || !editing || blocked || readOnly;
        dialog.querySelector('[data-script-editor-action="undo"]').disabled = waiting || !editing || blocked || !canUndo;
        find('template').disabled = waiting || language() !== 'PS1';
        window.MauiModalUI.viewOnly(dialog,permissionReadOnly);
    }
    function layout(isEditing) {
        editing = isEditing; dialog.classList.toggle('is-editing', editing); find('tabs').hidden = !editing;
        const holder = editing ? find('native-fields') : find('new-identity');
        holder.append(find('name-field'), find('description-field'));
        if (editing) holder.append(find('resource-groups').closest('label'));
        find('new-identity').hidden = editing;
        find('tab-actions').hidden = false;
        dialog.querySelector('.script-editor-actions-wrap').hidden = readOnly;
        dialog.querySelector('[data-script-editor-tab="script"]').hidden = readOnly;
        name.readOnly = description.readOnly = source.readOnly = readOnly;
        find('name-label').textContent = 'Script Name';
        find('kicker').hidden = !editing;
        find('kicker').textContent = editing ? 'SCRIPT EDITOR' : 'NEW SCRIPT';
    }
    function switchTab(key) {
        const allowed = readOnly ? ['properties','tasks'] : ['script','properties','tasks'];
        activeTab = editing && allowed.includes(key) ? key : allowed[0]; closeActions();
        dialog.querySelectorAll('[data-script-editor-tab]').forEach(button => { const selected = button.dataset.scriptEditorTab === activeTab; button.classList.toggle('active', selected); button.setAttribute('aria-selected', String(selected)); button.tabIndex = selected ? 0 : -1; });
        dialog.querySelectorAll('[data-script-editor-panel]').forEach(panel => { panel.hidden = panel.dataset.scriptEditorPanel !== activeTab; });
    }
    const lease = window.MauiObjectEditLease.create({ type: 'Script', token, busy: () => busy || importing || preferencesBusy,
        onLost: text => { blocked = true; clearTimeout(autoTimer); message(text, true); controls(); } });
    async function request(target, body, withLease = false) {
        const headers = { Accept: 'application/json', RequestVerificationToken: token };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (withLease && lease.id) headers['X-MAUI-Edit-Lease'] = lease.id;
        const response = await fetch(target, { method: body === undefined ? 'GET' : 'POST', headers, credentials: 'same-origin', cache: 'no-store', body: body === undefined ? undefined : JSON.stringify(body) });
        const data = await response.json().catch(() => null);
        if (response.status === 401) window.mauiSessionExpired?.();
        if (!response.ok || data?.success !== true) {
            const error = new Error(data?.message || (response.status === 401 ? 'Your session expired. Close this editor and sign in again.' : 'The Script response could not be confirmed.'));
            error.status = response.status; error.payload = data; error.ambiguous = response.ok && !data; throw error;
        }
        return data;
    }
    function url(key, objectId = '') { const target = new URL(dialog.dataset[key], window.location.href); if (objectId) target.searchParams.set('id', objectId); return target.toString(); }
    function validateModel(model) {
        if (!model || !/^\d+$/.test(model.id) || !readOnly && (!model.editVersion || !['PS1','VBScript'].includes(model.language)) || ['name','description','source'].some(key => typeof model[key] !== 'string')) throw new Error('The complete saved Script could not be confirmed. Reload the Script before saving again.');
        return model;
    }
    function apply(model) {
        find('resource-groups').value = model.resourceGroups || '—';
        validateModel(model); id = model.id; revision = model.editVersion; layout(true);
        name.value = model.name; description.value = model.description; source.value = model.source;
        dialog.querySelectorAll('input[name="mauiScriptLanguage"]').forEach(radio => { radio.checked = radio.value === model.language; });
        template.value = ''; updateLines(); find('title').textContent = model.name; find('id').textContent = id; find('type').textContent = readOnly ? 'Built-In' : 'Custom';
        find('subtitle').textContent = readOnly ? 'Built-In Script ID: ' + id : (model.language === 'PS1' ? 'PowerShell' : 'VBScript') + ' · Script ID: ' + id;
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
            control.className = 'text-input' + (type.includes('multiline') ? ' textarea-input' : ''); control.value = type === 'boolean' ? String(values[definition.name]).toLowerCase() === 'true' ? 'true' : 'false' : values[definition.name] ?? ''; control.required = Boolean(definition.required);
            control.dataset.scriptEditorMetadataField = definition.name;
            window.mauiConfigureMetadataControl?.(control, definition);
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
    function renderTemplates() {
        const selected = template.value; template.replaceChildren(new Option('None',''));
        templates.forEach(item => template.add(new Option(item.name,item.id))); template.value = templates.some(item => item.id === selected) ? selected : '';
    }
    async function loadTemplates() {
        const data = await request(url('templatesUrl')); if (!Array.isArray(data.templates)) throw new Error('Script templates could not be loaded.');
        templates = data.templates; renderTemplates(); renderSettings();
    }
    async function read() {
        let data = await request(url('shellUrl', id), undefined, true); readOnly = data.readOnly === true; permissionReadOnly = readOnly || data.permissionReadOnly === true; validateModel(data.script);
        if (!readOnly && !permissionReadOnly && !lease.id && data.checkoutObjectsForDurationOfEdit && await lease.open(id, true, true)) data = await request(url('shellUrl', id), undefined, true);
        apply(data.script); localUndo.rebase(true); canUndo = false; metadataFields = data.fields || []; renderMetadata(data.metadata); renderAudit(data.audit); renderTasks(data.tasks);
        autoSave = !permissionReadOnly && Boolean(data.preferences?.autoSave); find('auto-save').checked = autoSave; baseline = snapshot(); localUndo.rebase(true); canUndo=false; switchTab(activeTab);
    }
    async function open(objectId = '') {
        if (dialog.open) return false;
        const current = ++sequence; await lease.close(); if (current !== sequence) return false;
        clearTimeout(autoTimer); window.MauiModalUI.viewOnly(dialog,false); readOnly = false; permissionReadOnly=false; canUndo = false; id = String(objectId || '').trim(); revision = ''; blocked = false; uncertain = false; changed = false; manualRecovery = false; autoBlocked = '';
        autoSave = false; find('auto-save').checked = false; activeTab = 'script'; metadataFields = []; renderMetadata(); renderAudit(); renderTasks();
        name.value = ''; description.value = ''; source.value = ''; template.value = ''; layout(Boolean(id)); switchTab('script');
        dialog.querySelectorAll('input[name="mauiScriptLanguage"]').forEach(radio => { radio.checked = radio.value === 'PS1'; });
        find('title').textContent = id ? 'Script' : 'New Script'; find('subtitle').textContent = id ? 'Script ID: ' + id : 'PowerShell and VBScript';
        baseline = snapshot(); localUndo.rebase(true); canUndo=false; updateLines(); busy = true; controls(); save.textContent = editing ? 'Save' : 'Create Script'; message(id ? 'Loading Script…' : 'New Script'); dialog.showModal();
        try { if (id) await read(); draftStatus(); }
        catch (error) { blocked = true; message(error.message, true); }
        finally { busy = false; controls(); if (!editing) name.focus(); }
        if (!readOnly && !permissionReadOnly) void loadTemplates().then(() => { find('template-error').hidden = true; }).catch(error => { find('template-error').textContent = error.message; find('template-error').hidden = false; });
        return true;
    }
    function queueAutoSave() {
        clearTimeout(autoTimer);
        if (!dialog.open || !editing || !autoSave || !dirty() || manualRecovery || blocked || busy || importing || preferencesBusy || autoBlocked === snapshot()) return;
        autoTimer = setTimeout(() => {
            const focused = document.activeElement;
            if (focused && dialog.contains(focused) && (focused instanceof HTMLTextAreaElement || focused instanceof HTMLInputElement && !['checkbox','radio','button'].includes(focused.type))
                || document.querySelector('[data-script-template-dialog]').open || document.querySelector('[data-script-action-dialog]').open) { queueAutoSave(); return; }
            void saveDraft(true);
        },650);
    }
    async function performSave(automatic) {
        if (busy || importing || blocked || permissionReadOnly || preferencesBusy) return false;
        if (!dirty() && editing) return true;
        const input = draft();
        if (!readOnly && !input.name) { if (editing) switchTab('properties'); name.focus(); message('Script Name is required.',true); autoBlocked = snapshot(); return false; }
        if (!readOnly && input.source.length > 2 * 1024 * 1024) { message('Script Source must be no larger than 2 MiB of text.',true); autoBlocked = snapshot(); return false; }
        if (editing && window.mauiValidateMetadataControls && !window.mauiValidateMetadataControls(find('metadata'))) { switchTab('properties'); message('Review the Script metadata fields before saving.',true); autoBlocked = snapshot(); return false; }
        clearTimeout(autoTimer); busy = true; closeActions(); controls(); message('Saving…');
        try {
            // permissionReadOnly above includes built-ins; this path only saves editable custom Scripts.
            let data;
            lease.assert(); const creating = !id;
            data = await request(url('saveUrl',id), { ...input, editVersion: revision },true);
            if (creating && /^\d+$/.test(data.script?.id)) id = data.script.id;
            if (creating) {
                validateModel(data.script); changed = true; canUndo = false; baseline = snapshot();
                // A confirmed creation returns to its caller without loading or checking out an editor.
                await finishClose(); return true;
            }
            apply(data.script);
            canUndo = localUndo.available();
            if (data.metadata) renderMetadata(data.metadata);
            if (data.audit) renderAudit(data.audit);
            changed = true;
            baseline = snapshot(); localUndo.rebase(); manualRecovery = false; autoBlocked = ''; message(data.warning || 'Saved',Boolean(data.warning)); return true;
        } catch (error) {
            if (error.payload?.scriptId && /^\d+$/.test(error.payload.scriptId)) id = error.payload.scriptId;
            uncertain = !readOnly && Boolean(error.payload?.moveItSaved || error.ambiguous || !error.status || error.status >= 500 && error.payload?.moveItSaved !== false); changed = changed || uncertain;
            blocked = !readOnly && Boolean(uncertain || error.payload?.editConflict || error.payload?.leaseLost || error.payload?.checkoutConflict);
            autoBlocked = snapshot();
            message((error.message || 'Script could not be saved.') + (blocked ? ' ' + window.MauiObjectEditLease.blockedMessage('Script') : ''),true);
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
        if (dirty() && !await window.mauiConfirm?.({ title:'Discard Script Changes?',message:'Close this editor and discard unsaved Script changes?',confirmText:'Discard Changes',danger:true })) return false;
        return await finishClose();
    }
    async function finishClose() {
        clearTimeout(autoTimer); closeActions(); busy = true; controls(); await lease.close(); sequence++; dialog.close(); busy = false;
        window.dispatchEvent(new CustomEvent('maui:script-editor-closed',{detail:{id,changed}})); return true;
    }
    form.addEventListener('submit',event => { event.preventDefault(); if (autoCloseMode()) void close(); else void saveDraft(); });
    dialog.querySelectorAll('[data-script-editor-close]').forEach(button => button.addEventListener('click',() => { void close(); }));
    dialog.addEventListener('cancel',event => { event.preventDefault(); if (!find('actions-menu').hidden) closeActions(); else void close(); });
    form.addEventListener('input',event => { if (event.target === source) updateLines(); if (!busy && !blocked && !permissionReadOnly) localUndo.record(event.target,'Script field',true); draftStatus(); controls(); queueAutoSave(); });
    form.addEventListener('change',event => { if (!busy && !blocked && !permissionReadOnly) localUndo.record(event.target,'Script field',true); controls(); queueAutoSave(); });
    form.addEventListener('focusout',() => { localUndo.end(); queueAutoSave(); });
    source.addEventListener('scroll',() => { find('lines').scrollTop = source.scrollTop; });
    dialog.querySelectorAll('[data-script-editor-tab]').forEach(button => {
        button.addEventListener('click',() => switchTab(button.dataset.scriptEditorTab));
        button.addEventListener('keydown',event => { const keys = readOnly ? ['properties','tasks'] : ['script','properties','tasks']; if (!['ArrowRight','ArrowLeft','Home','End'].includes(event.key)) return; event.preventDefault(); const index = keys.indexOf(activeTab); switchTab(event.key === 'Home' ? keys[0] : event.key === 'End' ? keys[keys.length-1] : keys[(index + (event.key === 'ArrowRight' ? 1 : keys.length - 1)) % keys.length]); dialog.querySelector('[data-script-editor-tab="'+activeTab+'"]').focus(); });
    });
    dialog.querySelectorAll('input[name="mauiScriptLanguage"]').forEach(radio => radio.addEventListener('change',controls));
    template.addEventListener('change',() => { const item = templates.find(item => item.id === template.value); if (item) { source.value = item.source; updateLines(); draftStatus(); controls(); queueAutoSave(); } });
    // Share decoding with Settings template loading: UTF-8 and BOM-marked UTF-16,
    // plain text only. Contents are assigned to textarea.value, never evaluated.
    async function readScriptTextFile(file) {
        const bytes = new Uint8Array(await file.arrayBuffer());
        const encoding = bytes[0] === 255 && bytes[1] === 254 ? 'utf-16le' : bytes[0] === 254 && bytes[1] === 255 ? 'utf-16be' : 'utf-8';
        const text = new TextDecoder(encoding, { fatal: true }).decode(bytes);
        if (text.includes('\0')) throw new Error('Select a text Script file.');
        return text;
    }
    find('import').addEventListener('click',() => find('file').click());
    find('file').addEventListener('change',async event => {
        const file = event.target.files?.[0]; event.target.value = ''; if (!file || busy || importing) return;
        if (!/\.(txt|ps1)$/i.test(file.name)) { message('Select a .txt or .ps1 Script file.',true); return; }
        if (file.size > 2 * 1024 * 1024) { message('The Script file must be no larger than 2 MiB.',true); return; }
        importing = true; controls(); const current = sequence;
        try {
            const text = await readScriptTextFile(file);
            if (current !== sequence || !dialog.open) return;
            source.value = text; template.value = ''; if (/\.ps1$/i.test(file.name)) dialog.querySelector('input[value="PS1"]').checked = true;
            updateLines(); localUndo.record(null,'Import Script'); draftStatus();
        } catch (error) { message(error.message || 'The Script file could not be read.',true); }
        finally { importing = false; controls(); queueAutoSave(); }
    });
    reload.addEventListener('click',async () => {
        if (busy || !id) return;
        if (dirty() && !await window.mauiConfirm?.({title:'Reload Script?',message:'Discard unsaved changes and load the current Script from MOVEit?',confirmText:'Reload Script',danger:true})) return;
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
    document.addEventListener('click',event => { if (!event.target.closest('[data-script-editor-actions]') && !event.target.closest('[data-script-editor-actions-menu]')) closeActions(); });
    window.addEventListener('beforeunload',event => { if (dialog.open && dirty()) { event.preventDefault(); event.returnValue = ''; } });
    window.MauiModalUI?.register(dialog,{error:text => message(text,true),clear:() => draftStatus(),footerStatus:status});
    window.MauiModalUI?.registerContext('script-editor',dialog,{
        capture:() => ({id,tab:activeTab}), prepare:async () => !busy && !blocked && (!dirty() || await saveDraft()),
        restore:async state => { if (await open(state.id)) switchTab(state.tab || 'script'); }
    });
    window.MauiScriptEditor = {open};
    // Delete/export reuse the Resources CRUD guardrails and Recycle Bin recovery.
    // Only Delete hands off checkout; Undo reverses the local draft in this editor.
    const actionDialog = document.querySelector('[data-script-action-dialog]');
    const af = key => actionDialog.querySelector('[data-script-action-' + key + ']');
    let actionKind = '', actionId = '', actionBusy = false, deleteWithoutRecycle = false;
    function actionError(text = '') { af('error').textContent = text; af('error').hidden = !text; }
    function actionControls() {
        actionDialog.querySelectorAll('button,input').forEach(control => { control.disabled = actionBusy || control.dataset.unavailable === 'true'; });
        af('confirm').disabled = actionBusy || af('confirm').dataset.unavailable === 'true';
    }
    async function exportScript() {
        if (busy || importing || !editing || readOnly) return;
        closeActions();
        if (dirty() && autoSave && !manualRecovery && !await saveDraft()) return;
        if (dirty() && !await window.mauiConfirm?.({title:'Export Saved Script?',message:'Export the saved MOVEit Script? Unsaved changes in this editor will remain here.',confirmText:'Export Script'})) return;
        importing = true; controls(); message('Exporting Script…');
        try {
            const target = new URL(dialog.dataset.exportUrl,window.location.href); target.searchParams.set('scriptId',id);
            const response = await fetch(target.toString(),{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/zip'}});
            if (response.status === 401) window.mauiSessionExpired?.();
            if (!response.ok) { const result = await response.json().catch(() => null); throw new Error(result?.message || `Unable to export Script (${response.status}).`); }
            const blob = await response.blob(); if (!blob.size) throw new Error('The Script export was empty.');
            const disposition = response.headers.get('content-disposition') || '';
            const utf8 = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1], quoted = disposition.match(/filename="([^"]+)"/i)?.[1];
            const filename = utf8 ? decodeURIComponent(utf8) : quoted || 'Script_' + id + '.zip';
            const objectUrl = URL.createObjectURL(blob), link = document.createElement('a'); link.href = objectUrl; link.download = filename; link.hidden = true;
            document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(objectUrl),1000); message('Exported 1 Script.');
        } catch (error) { message(error?.message || 'Unable to export Script.',true); }
        finally { importing = false; controls(); queueAutoSave(); }
    }
    async function beginAction(kind) {
        closeActions();
        if (busy || importing || !id || blocked || permissionReadOnly) return;
        if (kind === 'undo') { if (canUndo) await localUndo.undo(); return; }
        if (kind === 'export') return exportScript();
        if (kind !== 'delete' || readOnly) return;
        const selectedId=id, selectedName=find('title').textContent;
        if (!await close()) return;
        actionKind='delete'; actionId=selectedId; actionBusy=true; deleteWithoutRecycle=false;
        af('history').replaceChildren(); af('history').hidden=true; actionError();
        af('title').textContent='Delete Script'; af('confirm').textContent='Delete Script'; af('confirm').className='danger-button';
        af('message').textContent='Checking Script “'+selectedName+'”…';
        af('confirm').dataset.unavailable='true'; actionControls(); actionDialog.showModal();
        try {
            const preflight=await request(url('deletePreflightUrl'),{scriptId:actionId});
            af('message').textContent='Delete Script “'+selectedName+'”? MAUI will create a Recycle Bin recovery copy when available.';
            af('confirm').dataset.unavailable=String(preflight.success !== true);
            if(preflight.success !== true) actionError(preflight.message || 'This Script cannot be deleted.');
        } catch(error) { actionError(error.message); }
        finally { actionBusy=false; actionControls(); }
    }
    async function closeAction() {
        if (actionBusy) return;
        actionDialog.close(); if (actionId) await open(actionId);
    }
    actionDialog.querySelectorAll('[data-script-action-close]').forEach(button => button.addEventListener('click',() => { void closeAction(); }));
    actionDialog.addEventListener('cancel',event => { event.preventDefault(); void closeAction(); });
    af('form').addEventListener('submit',async event => {
        event.preventDefault(); if (actionBusy || af('confirm').disabled) return;
        actionBusy = true; actionControls(); actionError();
        try {
            const result = await request(url('deleteUrl'),{scriptId:actionId,deleteWithoutRecycle});
            actionDialog.close(); window.dispatchEvent(new CustomEvent('maui:script-editor-closed',{detail:{id:actionId,changed:true}}));
            if (result.warning && !window.MauiModalUI?.showError(result.warning)) window.mauiShowToast?.(result.warning,'error');
        } catch (error) {
            actionError(error.message);
            if (error.payload?.code === 'RecycleCaptureFailed') { deleteWithoutRecycle = true; af('confirm').textContent = 'Delete Anyway'; }
            else if (!error.status || error.status >= 500 || error.ambiguous || error.payload?.moveItSaved) { af('confirm').dataset.unavailable = 'true'; actionError(error.message + ' Close this action and inspect the current Script before retrying.'); }
        } finally { actionBusy = false; actionControls(); }
    });
    dialog.querySelectorAll('[data-script-editor-action]').forEach(button => button.addEventListener('click',() => { void beginAction(button.dataset.scriptEditorAction); }));

    // Template management reuses the same environment-scoped store from the editor
    // and Configuration. Source is plain text throughout; it is never evaluated.
    const templateDialog = document.querySelector('[data-script-template-dialog]');
    const tf = key => templateDialog.querySelector('[data-script-template-' + key + ']');
    const settingsList = document.querySelector('[data-script-template-list]');
    const settingsError = document.querySelector('[data-script-template-settings-error]');
    const templateLoad = document.querySelector('[data-script-template-load]');
    const templateFile = document.querySelector('[data-script-template-file]');
    let templateReading = false;
    let templateBusy = false, templateId = '', templateSource = '', templateMode = 'script';
    let templateBaseline = '';
    const templateSnapshot = () => JSON.stringify([tf('name').value, tf('source').value]);
    const updateTemplateAction = window.MauiModalUI.watchAction(templateDialog, templateDialog?.querySelector('[data-script-template-save]'),
        () => canSaveTemplates && !templateBusy, () => Boolean(tf('name').value.trim()) && Boolean((templateMode === 'script' ? templateSource : tf('source').value).trim()) && window.MauiModalUI.formValid(templateDialog)
            && (templateMode !== 'configuration' || templateSnapshot() !== templateBaseline));
    function templateControls() { templateDialog.querySelectorAll('input,textarea,button').forEach(control => { control.disabled = templateBusy; }); tf('save').textContent = templateBusy ? 'Saving…' : templateMode === 'load' ? 'Add Template' : templateMode === 'configuration' ? 'Update Template' : 'Save'; updateTemplateAction(); }
    function templateError(text = '') { tf('error').textContent = text; tf('error').hidden = !text; }
    function openTemplate(mode, item = null) {
        if (!canSaveTemplates || templateBusy) return;
        templateMode = mode; templateId = item?.id || ''; templateSource = mode === 'script' ? source.value : item?.source || '';
        tf('name').value = item?.name || ''; tf('source').value = templateSource; tf('overwrite').checked = false;
        tf('title').textContent = mode === 'script' ? 'Save as Template' : mode === 'load' ? 'Add Script Template' : 'Edit Script Template';
        tf('overwrite-field').hidden = mode !== 'script'; tf('source-field').hidden = mode === 'script';
        templateBaseline = templateSnapshot();
        templateBusy = false; templateError(); templateControls(); templateDialog.showModal(); tf('name').focus();
    }
    if (templateLoad) templateLoad.disabled = !canSaveTemplates;
    templateLoad?.addEventListener('click', () => {
        if (!canSaveTemplates || templateBusy || templateReading || !templateFile) return;
        templateFile.value = ''; templateFile.click();
    });
    templateFile?.addEventListener('change', async event => {
        const file = event.target.files?.[0]; event.target.value = '';
        if (!file || !canSaveTemplates || templateBusy || templateReading) return;
        templateReading = true; templateLoad.disabled = true; showSettingsError();
        try {
            if (!/\.(txt|ps1)$/i.test(file.name)) throw new Error('Select a .txt or .ps1 Script template file.');
            if (file.size > 2 * 1024 * 1024) throw new Error('The Script template file must be no larger than 2 MiB.');
            const text = await readScriptTextFile(file);
            openTemplate('load', { name: file.name.replace(/\.[^.]+$/, ''), source: text });
        } catch (error) { showSettingsError(error.message || 'The Script template file could not be read.'); }
        finally { templateReading = false; templateLoad.disabled = !canSaveTemplates; }
    });
    find('save-template').addEventListener('click', () => { if (language() === 'PS1') openTemplate('script'); });
    templateDialog.querySelectorAll('[data-script-template-close]').forEach(button => button.addEventListener('click', () => { if (!templateBusy) templateDialog.close(); }));
    templateDialog.addEventListener('cancel', event => { if (templateBusy) event.preventDefault(); });
    templateDialog.addEventListener('close', () => queueAutoSave());
    tf('form').addEventListener('submit', async event => {
        event.preventDefault(); if (!canSaveTemplates || templateBusy || tf('save').disabled) return;
        templateBusy = true; templateControls(); templateError();
        try {
            const data = await request(url('saveTemplateUrl'), { name: tf('name').value.trim(), source: templateMode === 'script' ? templateSource : tf('source').value,
                templateId, overwriteExisting: tf('overwrite').checked, language: 'PS1' });
            if (!data.template?.id) throw new Error('The Script template save could not be confirmed.');
            templateDialog.close();
            try { await loadTemplates(); } catch (error) { if (dialog.open) { find('template-error').textContent = 'Template saved. ' + error.message; find('template-error').hidden = false; } else showSettingsError('Template saved. ' + error.message); }
        } catch (error) { templateError(error.message); }
        finally { templateBusy = false; templateControls(); }
    });
    function showSettingsError(text = '') { if (settingsError) { settingsError.textContent = text; settingsError.hidden = !text; } }
    function renderSettings() {
        if (!settingsList) return;
        settingsList.replaceChildren();
        if (!templates.length) { const empty = document.createElement('div'); empty.className = 'configuration-notification-template-empty'; empty.textContent = 'No Script templates are currently saved.'; settingsList.append(empty); }
        for (const item of templates) {
            const row = document.createElement('div'); row.className = 'configuration-notification-template-row';
            const label = document.createElement('strong'); label.className = 'configuration-notification-template-name'; label.textContent = item.name;
            const actions = document.createElement('div'); actions.className = 'configuration-notification-template-actions';
            const edit = document.createElement('button'); edit.type = 'button'; edit.className = 'secondary-button'; edit.textContent = 'Edit / Rename'; edit.addEventListener('click', () => openTemplate('configuration', item));
            const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'danger-button'; remove.textContent = 'Delete';
            remove.addEventListener('click', async () => {
                if (!await window.mauiConfirm?.({ title: 'Delete Script Template?', message: 'Remove the Script template “' + item.name + '”? Saved MOVEit Scripts are not changed.', confirmText: 'Delete Template', danger: true })) return;
                remove.disabled = true; showSettingsError();
                try { await request(url('deleteTemplateUrl'), { templateId: item.id }); await loadTemplates(); }
                catch (error) { showSettingsError(error.message); remove.disabled = false; }
            });
            actions.append(edit, remove); row.append(label, actions); settingsList.append(row);
        }
    }
    if (settingsList) void loadTemplates().catch(error => showSettingsError(error.message));
})();
