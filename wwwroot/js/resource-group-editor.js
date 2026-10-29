/* Resource Group New/Edit modal. Explicit selections are drafts; the shared service owns dependency expansion and v0 writes. */
(() => {
    'use strict';
    const grid = document.querySelector('[data-permissions-grid]');
    const dialog = document.querySelector('[data-group-editor]');
    if (!grid || !dialog) return;
    const picker = document.querySelector('[data-group-picker]');
    // Keep editor controls isolated from grid row/menu attributes and from other open resource dialogs.
    const field = name => name === 'editor' ? dialog : name === 'picker' ? picker
        : (name.startsWith('picker-') ? picker : dialog).querySelector(`[data-group-${name}]`);
    const labels = { task: 'Tasks', host: 'Hosts', script: 'Scripts', ssh: 'SSH Keys', ssl: 'TLS Certs', pgp: 'PGP Keys' };
    const singularLabels = { task: 'Task', host: 'Host', script: 'Script', ssh: 'SSH Key', ssl: 'TLS Cert', pgp: 'PGP Key' };
    const key = item => item.memberType + '|' + item.id;
    let originalName = '', editVersion = '', selected = [], items = [], catalog = [], tab = 'task', baseline = '', busy = false, busyText = '', uncertain = false, blockingMessage = '';
    let pickerSelected = new Set();
    const make = (tag, cls, text) => {
        const element = document.createElement(tag); if (cls) element.className = cls;
        if (text !== undefined) element.textContent = text; return element;
    };
    const message = (name, text) => { const target = field(name); target.textContent = text || ''; target.hidden = !text; };
    const signature = () => JSON.stringify([field('name').value, field('description').value, selected.map(key).sort()]);
    function updateStatus() {
        field('save').disabled = busy || uncertain || !field('name').value.trim() || (originalName !== '' && signature() === baseline);
        field('status').textContent = busy ? busyText : signature() !== baseline ? 'Unsaved changes' : originalName ? 'Saved' : '';
    }
    function setBusy(value, text = 'Loading...') {
        busy = value; busyText = text;
        dialog.querySelectorAll('input, textarea, button').forEach(control => { control.disabled = busy; });
        picker.querySelectorAll('input, button').forEach(control => { control.disabled = busy; });
        field('loading').hidden = !busy; field('loading').querySelector('span:last-child').textContent = text; dialog.setAttribute('aria-busy', String(busy));
        field('save').textContent = busy && text === 'Saving…' ? 'Saving…' : originalName ? 'Save' : 'Create Group';
        if (!busy) { renderMembers(); renderPicker(); } updateStatus();
    }
    async function request(url, body) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        const response = await fetch(url, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json', RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const data = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
        if (!response.ok || data?.success !== true) {
            const error = new Error(data?.message || (response.status === 401 ? 'MOVEit session expired. Close this dialog and sign in again.' : 'The Resource Group operation could not be completed.'));
            error.payload = data; error.status = response.status;
            try {
                if (await window.MauiObjectEditLease?.recoverConflict(error, 'Resource Groups', token))
                    error.message = window.MauiObjectEditLease.restoredFormMessage;
            } catch (recoveryError) { error.message = recoveryError.message; }
            throw error;
        }
        return data;
    }
    function renderMembers() {
        const list = field('items'); list.replaceChildren();
        const visible = items.filter(item => item.memberType === tab);
        if (!visible.length) list.append(make('p', 'resource-group-empty', `No ${labels[tab]} in this Resource Group.`));
        for (const item of visible) {
            const row = make('div', 'resource-group-member'); const copy = make('div', 'resource-group-member-copy');
            copy.append(make('strong', '', item.name), make('span', '', `ID: ${item.id}${item.description ? ' · ' + item.description : ''}`));
            const required = item.requiredBy || [];
            if (required.length) copy.append(make('span', 'resource-group-required', 'Required by ' + required.join('; ')));
            const remove = make('button', 'danger-button', 'Remove'); remove.type = 'button';
            remove.disabled = busy || required.length > 0;
            remove.title = required.length ? 'Remove the referencing Task or Host first.' : `Remove ${item.name}`;
            remove.addEventListener('click', () => void changeMembers(selected.filter(member => key(member) !== key(item))));
            row.append(copy, remove); list.append(row);
        }
        field('member-count').textContent = `${visible.length} ${visible.length === 1 ? singularLabels[tab] : labels[tab]}`;
        field('add').textContent = 'Add ' + labels[tab];
        field('add').disabled = busy;
        dialog.querySelectorAll('[data-group-tab]').forEach(button => {
            const active = button.dataset.groupTab === tab; button.classList.toggle('active', active);
            button.setAttribute('aria-selected', String(active)); button.tabIndex = active ? 0 : -1;
        });
        document.getElementById('group-member-panel').setAttribute('aria-labelledby', `group-${tab}-tab`);
    }
    async function changeMembers(next, fromPicker = false) {
        if (busy) return;
        message(fromPicker ? 'picker-error' : 'error', fromPicker ? '' : blockingMessage); setBusy(true, 'Updating members...');
        try {
            const response = await request(grid.dataset.resolveUrl, { members: next });
            items = response.items;
            // Native membership has no separate provenance flag. Use the same independent roots as load/save,
            // including when a previously independent Host/key becomes a required Task dependency.
            const required = new Set(items.filter(item => item.requiredBy.length > 0).map(key));
            selected = next.filter(member => !required.has(key(member)));
            if (fromPicker) picker.close();
        } catch (error) { message(fromPicker ? 'picker-error' : 'error', fromPicker ? error.message : [blockingMessage, error.message].filter(Boolean).join(' ')); }
        finally { setBusy(false); }
    }
    function pickerRows() {
        const current = new Set(items.map(key)); const term = field('picker-search').value.trim().toLocaleLowerCase();
        return catalog.filter(item => item.memberType === tab && !current.has(key(item))
            && (!term || [item.id, item.name, item.description].some(value => String(value || '').toLocaleLowerCase().includes(term))));
    }
    function renderPicker() {
        const body = field('picker-items'); body.replaceChildren(); const rows = pickerRows();
        for (const item of rows) {
            const row = make('tr'); const cell = make('td'); const checkbox = make('input'); checkbox.type = 'checkbox';
            checkbox.checked = pickerSelected.has(key(item)); checkbox.disabled = busy; checkbox.setAttribute('aria-label', `Add ${item.name} (ID ${item.id})`);
            checkbox.dataset.groupPickerKey = key(item);
            checkbox.addEventListener('change', () => {
                if (checkbox.checked) pickerSelected.add(key(item)); else pickerSelected.delete(key(item)); renderPicker();
                [...body.querySelectorAll('input')].find(input => input.dataset.groupPickerKey === key(item))?.focus({ preventScroll: true });
            });
            cell.append(checkbox); row.append(cell, make('td', '', item.id), make('td', '', item.name), make('td', '', item.description)); body.append(row);
        }
        if (!rows.length) { const row = make('tr'); const cell = make('td', 'resource-group-empty', 'No available resources match this search.'); cell.colSpan = 4; row.append(cell); body.append(row); }
        field('picker-all').checked = rows.length > 0 && rows.every(item => pickerSelected.has(key(item)));
        field('picker-all').indeterminate = rows.some(item => pickerSelected.has(key(item))) && !field('picker-all').checked;
        field('picker-all').disabled = busy || rows.length === 0;
        field('picker-count').textContent = `${pickerSelected.size} selected`;
        field('picker-add').disabled = busy || pickerSelected.size === 0;
    }
    async function open(name = '') {
        if (dialog.open || busy) return;
        originalName = name; selected = []; items = []; catalog = []; tab = 'task'; uncertain = false; blockingMessage = '';
        field('name').value = ''; field('description').value = ''; editVersion = ''; baseline = signature();
        field('title').textContent = name ? 'Edit Resource Group' : 'New Resource Group';
        field('subtitle').textContent = name || 'Choose the resources to include in this Resource Group.';
        dialog.querySelectorAll('[data-group-close]').forEach(button => { if (!button.classList.contains('dialog-close')) button.textContent = name ? 'Close' : 'Cancel'; });
        message('error', ''); field('content').hidden = true; dialog.showModal(); setBusy(true);
        try {
            const url = new URL(grid.dataset.editorUrl, location.href); if (name) url.searchParams.set('name', name);
            const response = await request(url); const editor = response.editor;
            selected = editor.members; items = editor.items; catalog = response.availableItems; editVersion = editor.editVersion;
            field('name').value = editor.name; field('description').value = editor.description;
            baseline = signature(); field('content').hidden = false;
        } catch (error) { uncertain = true; blockingMessage = error.message; message('error', blockingMessage); }
        finally { setBusy(false); if (!uncertain) field('name').focus(); }
    }
    function close() { dialog.close(); }
    async function tryClose() {
        if (busy) return;
        if (signature() !== baseline && !await window.mauiConfirm({ title: 'Discard Resource Group Changes?', message: 'Close this editor and discard unsaved Resource Group changes?', confirmText: 'Discard Changes', danger: true })) return;
        close();
    }
    dialog.addEventListener('close', () => grid.dispatchEvent(new CustomEvent('maui:resource-group-closed')));
    dialog.addEventListener('cancel', event => { event.preventDefault(); void tryClose(); });
    dialog.querySelectorAll('[data-group-close]').forEach(button => button.addEventListener('click', () => void tryClose()));
    field('name').addEventListener('input', updateStatus); field('description').addEventListener('input', updateStatus);
    dialog.querySelectorAll('[data-group-tab]').forEach(button => button.addEventListener('click', () => { if (!busy) { tab = button.dataset.groupTab; renderMembers(); } }));
    dialog.querySelector('[role="tablist"]').addEventListener('keydown', event => {
        if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key) || busy) return;
        event.preventDefault(); const tabs = [...dialog.querySelectorAll('[data-group-tab]')]; let index = tabs.findIndex(button => button.dataset.groupTab === tab);
        index = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
        tabs[index].click(); tabs[index].focus();
    });
    field('add').addEventListener('click', () => {
        pickerSelected = new Set(); field('picker-title').textContent = 'Add ' + labels[tab];
        field('picker-search').value = ''; message('picker-error', ''); renderPicker(); picker.showModal(); field('picker-search').focus();
    });
    picker.querySelectorAll('[data-group-picker-close]').forEach(button => button.addEventListener('click', () => { if (!busy) picker.close(); }));
    picker.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
    field('picker-search').addEventListener('input', renderPicker);
    field('picker-all').addEventListener('change', () => { for (const item of pickerRows()) { if (field('picker-all').checked) pickerSelected.add(key(item)); else pickerSelected.delete(key(item)); } renderPicker(); });
    field('picker-form').addEventListener('submit', event => {
        event.preventDefault(); if (!pickerSelected.size || busy) return;
        const additions = catalog.filter(item => pickerSelected.has(key(item))).map(item => ({ memberType: item.memberType, id: item.id }));
        void changeMembers([...selected, ...additions], true);
    });
    field('form').addEventListener('submit', async event => {
        event.preventDefault(); if (busy || uncertain || !field('name').value.trim()) return;
        message('error', ''); setBusy(true, 'Saving…');
        try {
            await request(grid.dataset.saveUrl, { originalName, editVersion, name: field('name').value, description: field('description').value, members: selected });
            baseline = signature(); close();
        } catch (error) {
            uncertain = error.payload?.saveUnconfirmed === true || error.payload?.editConflict === true || !error.payload
                || error.status >= 500 || [401,403,408,409].includes(error.status);
            blockingMessage = uncertain ? (!error.payload || error.status >= 500 || error.status === 408
                ? 'The Resource Group save result could not be confirmed. Close this dialog and inspect the refreshed Group before retrying.' : error.message) : '';
            message('error', blockingMessage || error.message);
        }
        finally { setBusy(false); }
    });
    grid.querySelector('[data-permissions-new]').addEventListener('click', () => { grid.querySelectorAll('[data-menu].open').forEach(menu => { menu.classList.remove('open'); menu.querySelector('[data-menu-button]')?.setAttribute('aria-expanded', 'false'); }); void open(); });
    grid.addEventListener('maui:edit-resource-group', event => void open(event.detail.name));
    const deleteDialog = document.querySelector('[data-group-delete-dialog]');
    const deleteField = name => deleteDialog.querySelector(`[data-group-delete-${name}]`);
    let deleteTarget = null, deleting = false, deleteLoading = false, deleteBlocked = false, deleteAttempted = false, deleteGeneration = 0;
    const deleteMessage = text => { deleteField('error').textContent = text || ''; deleteField('error').hidden = !text; };
    function deleteControls() {
        deleteDialog.querySelectorAll('button').forEach(button => { button.disabled = deleting; });
        deleteField('confirm').disabled = deleting || deleteLoading || deleteBlocked || !deleteTarget;
        deleteField('confirm').textContent = deleting ? 'Deleting…' : 'Delete Resource Group';
        deleteField('loading').hidden = !deleteLoading;
        deleteDialog.setAttribute('aria-busy', String(deleting || deleteLoading));
    }
    grid.addEventListener('maui:delete-resource-group', async event => {
        if (!deleteDialog || deleteDialog.open || dialog.open || deleting) return;
        const current = ++deleteGeneration;
        deleteTarget = null; deleteBlocked = false; deleteAttempted = false; deleteLoading = true;
        deleteField('name').value = event.detail.name; deleteField('description').value = ''; deleteMessage('');
        deleteControls(); deleteDialog.showModal();
        try {
            const url = new URL(grid.dataset.deletePreflightUrl, location.href); url.searchParams.set('name', event.detail.name);
            const response = await request(url);
            if (current !== deleteGeneration || !deleteDialog.open) return;
            if (!response.group?.name || !response.group?.editVersion) throw new Error('The Resource Group could not be confirmed. Close and refresh the grid.');
            deleteTarget = { name: response.group.name, editVersion: response.group.editVersion };
            deleteField('name').value = response.group.name; deleteField('description').value = response.group.description || '';
        } catch (error) { if (current === deleteGeneration && deleteDialog.open) { deleteBlocked = true; deleteMessage(error.message); } }
        finally { if (current === deleteGeneration) { deleteLoading = false; deleteControls(); } }
    });
    deleteDialog.querySelectorAll('[data-group-delete-close]').forEach(button => button.addEventListener('click', () => { if (!deleting) deleteDialog.close(); }));
    deleteDialog.addEventListener('cancel', event => { if (deleting) event.preventDefault(); });
    deleteDialog.addEventListener('close', () => {
        deleteGeneration++;
        if (deleteAttempted) grid.dispatchEvent(new CustomEvent('maui:resource-group-closed'));
    });
    deleteField('form').addEventListener('submit', async event => {
        event.preventDefault(); if (deleteField('confirm').disabled) return;
        deleting = true; deleteAttempted = true; deleteMessage(''); deleteControls();
        try { await request(grid.dataset.deleteUrl, deleteTarget); deleteDialog.close(); }
        catch (error) {
            deleteBlocked = error.payload?.saveUnconfirmed === true || error.payload?.editConflict === true || !error.payload
                || error.status >= 500 || [401,403,408,409].includes(error.status);
            deleteMessage(deleteBlocked && ![401,403].includes(error.status)
                ? 'The Resource Group changed or its deletion could not be confirmed. Close this dialog and inspect the refreshed Groups before retrying.' : error.message);
        }
        finally { deleting = false; deleteControls(); }
    });
})();
