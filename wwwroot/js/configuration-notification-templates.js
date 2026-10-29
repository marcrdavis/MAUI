/* MAUI Configuration > General > Notification Templates. Saves independently of Save Configuration. */
(() => {
    'use strict';
    const workspace = document.querySelector('[data-configuration-workspace]');
    const list = workspace?.querySelector('[data-notification-template-list]');
    if (!list) return;
    const dialog = document.querySelector('[data-notification-template-edit-dialog]');
    const field = key => dialog.querySelector(`[data-template-${key}]`);
    const message = window.MauiNotificationMessage;
    const file = workspace.querySelector('[data-notification-template-file]');
    const loadError = workspace.querySelector('[data-notification-template-load-error]');
    const deleteDialog = document.querySelector('[data-notification-template-delete-dialog]');
    const df = key => deleteDialog.querySelector(`[data-notification-template-delete-${key}]`);
    const token = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    let templates = [], editing = null, pendingDelete = null, busy = false, reading = false;
    let format = 'text', sourceMode = false, baseline = '', htmlRange = null;
    function error(element, text = '') { element.textContent = text; element.hidden = !text; }
    async function request(url, payload) {
        const options = { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } };
        if (payload !== undefined) {
            options.method = 'POST'; options.headers['Content-Type'] = 'application/json';
            options.headers.RequestVerificationToken = token; options.body = JSON.stringify(payload);
        }
        const response = await fetch(url, options);
        if (response.status === 401) { window.mauiSessionExpired?.(); throw new Error('Your session has expired.'); }
        const data = await response.json().catch(() => ({}));
        if (!response.ok || data.success !== true) throw new Error(data.message || `Request failed (${response.status}).`);
        return data;
    }
    function body() { return format === 'text' ? field('text').value : message.sanitize(sourceMode ? field('source-text').value : field('visual').innerHTML); }
    function draft() { return { name: field('name').value.trim(), subject: field('subject').value.trim(), message: body(), format }; }
    const snapshot = () => JSON.stringify(draft());
    const updateAction = window.MauiModalUI.watchAction(dialog, field('save'), () => !busy,
        () => Boolean(field('name').value.trim() && field('subject').value.trim() && body().trim())
            && (!editing || snapshot() !== baseline) && window.MauiModalUI.formValid(dialog));
    function controls() {
        dialog.querySelectorAll('input,select,textarea,button').forEach(control => { control.disabled = busy; });
        field('visual').contentEditable = String(!busy);
        field('save').textContent = busy ? 'Saving…' : editing ? 'Update Template' : 'Add Template';
        updateAction();
    }
    function display() {
        const html = format === 'html';
        field('visual').hidden = !html || sourceMode;
        field('source-text').hidden = !html || !sourceMode;
        field('text').hidden = html;
        dialog.querySelectorAll('[data-template-command],[data-template-source]').forEach(control => { control.hidden = !html; });
        field('source').textContent = sourceMode ? 'Visual' : 'Source';
        updateAction();
    }
    function open(item) {
        editing = item.id ? item : null; htmlRange = null; sourceMode = false;
        field('title').textContent = editing ? 'Edit Notification Template' : 'Add Notification Template';
        field('name').value = item.name || ''; field('subject').value = item.subject || '';
        format = item.format === 'html' || !item.format && message.detectFormat(item.message) === 'html' ? 'html' : 'text';
        field('format').value = format;
        field('text').value = item.message || '';
        field('visual').innerHTML = message.sanitize(item.message || '');
        field('source-text').value = field('visual').innerHTML;
        baseline = snapshot(); error(field('error')); display(); controls(); dialog.showModal(); field('name').focus();
    }
    function close() { if (!busy) dialog.close(); }
    dialog.querySelectorAll('[data-template-close]').forEach(button => button.addEventListener('click', close));
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
    field('format').addEventListener('change', () => {
        if (format === 'html') {
            field('visual').innerHTML = body(); field('text').value = field('visual').innerText;
        } else {
            field('visual').replaceChildren();
            field('text').value.split(/\r?\n/).forEach((line, index) => { if (index) field('visual').append(document.createElement('br')); field('visual').append(document.createTextNode(line)); });
        }
        format = field('format').value; sourceMode = false; htmlRange = null; display();
    });
    field('source').addEventListener('click', () => {
        const html = body(); sourceMode = !sourceMode;
        field('source-text').value = html; field('visual').innerHTML = html; htmlRange = null; display();
    });
    function rememberRange() {
        const selection = window.getSelection();
        if (selection?.rangeCount && field('visual').contains(selection.anchorNode)) htmlRange = selection.getRangeAt(0).cloneRange();
    }
    ['keyup','mouseup','input','blur'].forEach(type => field('visual').addEventListener(type, rememberRange));
    function restoreRange() {
        field('visual').focus(); const selection = window.getSelection();
        if (selection && htmlRange) { selection.removeAllRanges(); selection.addRange(htmlRange); }
    }
    // Prevent pasted markup from introducing executable HTML before save-time sanitization.
    field('visual').addEventListener('paste', event => {
        event.preventDefault(); restoreRange();
        const html = event.clipboardData?.getData('text/html');
        if (html) document.execCommand('insertHTML', false, message.sanitize(html));
        else document.execCommand('insertText', false, event.clipboardData?.getData('text/plain') || '');
        rememberRange(); updateAction();
    });
    field('visual').addEventListener('drop', event => event.preventDefault());
    dialog.querySelectorAll('[data-template-command]').forEach(button => {
        button.addEventListener('mousedown', event => event.preventDefault());
        button.addEventListener('click', () => {
            if (sourceMode) return;
            restoreRange(); document.execCommand(button.dataset.templateCommand, false); rememberRange(); updateAction();
        });
    });
    for (const key of ['subject','body']) {
        const select = field(`${key}-macro`); message.populateMacros(select);
        select.addEventListener('change', () => {
            if (!select.value) return;
            if (key === 'body' && format === 'html' && !sourceMode) {
                restoreRange(); document.execCommand('insertText', false, select.value); rememberRange();
            } else {
                const input = key === 'subject' ? field('subject') : format === 'text' ? field('text') : field('source-text');
                input.setRangeText(select.value, input.selectionStart ?? input.value.length, input.selectionEnd ?? input.value.length, 'end'); input.focus();
            }
            select.value = ''; updateAction();
        });
    }
    function render() {
        list.replaceChildren(); templates.sort((a,b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }));
        if (!templates.length) { const empty = document.createElement('div'); empty.className = 'configuration-notification-template-empty'; empty.textContent = 'No notification templates are currently saved.'; list.append(empty); }
        for (const item of templates) {
            const row = document.createElement('div'); row.className = 'configuration-notification-template-row';
            const name = document.createElement('strong'); name.className = 'configuration-notification-template-name'; name.textContent = item.name;
            const actions = document.createElement('div'); actions.className = 'configuration-notification-template-actions';
            const edit = document.createElement('button'); edit.type = 'button'; edit.className = 'secondary-button'; edit.textContent = 'Edit / Rename'; edit.addEventListener('click', () => { if (!busy && !reading) open(item); });
            const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'danger-button'; remove.textContent = 'Delete';
            remove.addEventListener('click', () => { if (busy || reading) return; pendingDelete = item; df('name').textContent = item.name; error(df('error')); deleteDialog.showModal(); });
            actions.append(edit, remove); row.append(name, actions); list.append(row);
        }
    }
    workspace.querySelector('[data-notification-template-load]').addEventListener('click', () => { if (!busy && !reading) { file.value = ''; file.click(); } });
    file.addEventListener('change', async () => {
        const selected = file.files?.[0]; if (!selected || reading || busy) return;
        error(loadError); reading = true;
        try {
            if (!/\.(txt|html?)$/i.test(selected.name)) throw new Error('Select a .txt, .htm or .html file.');
            if (selected.size > 2 * 1024 * 1024) throw new Error('Select a template smaller than 2 MB.');
            const contents = (await selected.text()).replace(/^\uFEFF/, '');
            open({ name: selected.name.replace(/\.[^.]+$/, ''), message: contents, format: message.detectFormat(contents) });
        } catch (ex) { error(loadError, ex.message || 'The template file could not be loaded.'); }
        finally { reading = false; }
    });
    field('save').addEventListener('click', async () => {
        if (busy || field('save').disabled) return;
        const payload = { ...draft(), templateId: editing?.id || '', overwriteExisting: Boolean(editing) };
        busy = true; error(field('error')); controls();
        try {
            const data = await request(workspace.dataset.saveNotificationTemplateUrl, payload);
            if (!data.template?.id) throw new Error('The template save could not be confirmed.');
            templates = templates.filter(item => item.id !== data.template.id); templates.push(data.template); render(); dialog.close();
        } catch (ex) { error(field('error'), ex.message || 'The template could not be saved.'); }
        finally { busy = false; controls(); }
    });
    const closeDelete = () => { if (!busy) { deleteDialog.close(); pendingDelete = null; } };
    ['cancel','close'].forEach(key => df(key).addEventListener('click', closeDelete));
    deleteDialog.addEventListener('cancel', event => { event.preventDefault(); closeDelete(); });
    df('confirm').addEventListener('click', async () => {
        if (busy || !pendingDelete) return;
        busy = true; df('confirm').disabled = true; error(df('error'));
        try {
            await request(workspace.dataset.deleteNotificationTemplateUrl, { templateId: pendingDelete.id });
            templates = templates.filter(item => item.id !== pendingDelete.id); render(); deleteDialog.close(); pendingDelete = null;
        } catch (ex) { error(df('error'), ex.message || 'The template could not be deleted.'); }
        finally { busy = false; df('confirm').disabled = false; }
    });
    request(workspace.dataset.notificationTemplatesUrl).then(data => { templates = Array.isArray(data.templates) ? data.templates : []; render(); }).catch(ex => error(loadError, ex.message));
})();
