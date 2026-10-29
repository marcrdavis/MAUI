/* User Group selection/removal UI. The shared permissions service validates identities and owns v1 ACL mutations. */
(() => {
    'use strict';
    const grid = document.querySelector('[data-permissions-grid]');
    const dialog = document.querySelector('[data-user-group-dialog]');
    const removeDialog = document.querySelector('[data-user-group-remove-dialog]');
    if (!grid || !dialog || !removeDialog) return;
    const field = name => dialog.querySelector(`[data-user-group-${name}]`);
    const removeField = name => removeDialog.querySelector(`[data-user-group-remove-${name}]`);
    let group = '', rows = [], selected = new Map(), assigned = new Set(), loading = false, loaded = false, busy = false, blocked = false;
    let generation = 0, requestAbort = null, attempted = false, removeTarget = null, removing = false, removeBlocked = false, blockingMessage = '';
    const fold = value => String(value || '').toLocaleLowerCase();
    const make = (tag, cls, text) => { const node = document.createElement(tag); if (cls) node.className = cls; if (text !== undefined) node.textContent = text; return node; };
    const message = (target, text = '') => { target.textContent = text; target.hidden = !text; };
    const changed = () => grid.dispatchEvent(new CustomEvent('maui:resource-group-acls-changed'));
    async function request(url, body, signal) {
        const response = await fetch(url, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store', signal,
            headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json',
                RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]')?.value || '' }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const payload = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
        if (!response.ok || payload?.success !== true) {
            const error = new Error(payload?.message || (response.status === 403 ? 'Only MOVEit administrators can manage Groups and Permissions.'
                : response.status === 401 ? 'MOVEit session expired. Close this dialog and sign in again.' : 'The User Group operation could not be completed.'));
            error.status = response.status; error.payload = payload; throw error;
        }
        return payload;
    }
    const visibleRows = () => rows.filter(row => !field('search').value.trim() || [row.name, row.description].some(value => fold(value).includes(fold(field('search').value.trim()))));
    function controls() {
        dialog.querySelectorAll('input,select,button').forEach(control => { control.disabled = busy; });
        field('save').disabled = busy || loading || !loaded || blocked || selected.size === 0;
        field('save').textContent = busy ? 'Saving…' : 'Save';
        dialog.setAttribute('aria-busy', String(busy || loading));
        field('count').textContent = `${selected.size} selected`;
        field('all').disabled = busy || loading || !loaded || !visibleRows().some(row => !assigned.has(fold(row.name)));
        field('items').querySelectorAll('input').forEach(input => { input.disabled = busy || assigned.has(fold(input.value)); });
    }
    function render() {
        const body = field('items'); body.replaceChildren();
        const visible = visibleRows();
        for (const item of visible) {
            const row = make('tr'), cell = make('td'), checkbox = make('input'); checkbox.type = 'checkbox'; checkbox.value = item.name;
            const exists = assigned.has(fold(item.name)); checkbox.checked = exists || selected.has(fold(item.name));
            checkbox.setAttribute('aria-label', `${item.name}${exists ? ' — already assigned' : ''}`); checkbox.title = exists ? 'Already assigned' : item.name;
            checkbox.addEventListener('change', () => {
                if (checkbox.checked) selected.set(fold(item.name), { name: item.name, type: field('type').value }); else selected.delete(fold(item.name));
                render(); [...body.querySelectorAll('input')].find(input => input.value === item.name)?.focus({ preventScroll: true });
            });
            cell.append(checkbox); row.append(cell, make('td', '', item.name), make('td', '', item.description)); body.append(row);
        }
        if (!visible.length) {
            const row = make('tr'), cell = make('td', 'resource-group-empty'); cell.colSpan = 3;
            if (loading) {
                const status = make('div', 'details-loading'), spinner = make('span', 'inline-spinner');
                status.dataset.userGroupLoading = ''; status.setAttribute('role', 'status'); spinner.setAttribute('aria-hidden', 'true');
                status.append(spinner, make('span', '', 'Loading User Groups…')); cell.append(status);
            } else cell.textContent = loaded ? 'No User Groups match this search.' : 'Reload the User Groups list to continue.';
            row.append(cell); body.append(row);
        }
        const available = visible.filter(row => !assigned.has(fold(row.name)));
        field('all').checked = available.length > 0 && available.every(row => selected.has(fold(row.name)));
        field('all').indeterminate = available.some(row => selected.has(fold(row.name))) && !field('all').checked;
        controls();
    }
    async function load() {
        const current = ++generation; requestAbort?.abort(); requestAbort = new AbortController();
        loading = true; loaded = false; rows = []; message(field('error'), blockingMessage); render();
        try {
            const url = new URL(grid.dataset.userGroupsUrl, location.href); url.searchParams.set('resourceGroupName', group); url.searchParams.set('type', field('type').value);
            const data = await request(url, undefined, requestAbort.signal);
            if (current !== generation || !dialog.open) return;
            if (!Array.isArray(data.setup?.groups) || !Array.isArray(data.setup?.assignedGroups)) throw new Error('The User Groups list could not be confirmed. Reload the list.');
            rows = data.setup.groups; assigned = new Set(data.setup.assignedGroups.map(fold));
            for (const name of assigned) selected.delete(name);
            loaded = true;
        } catch (error) { if (current === generation && error.name !== 'AbortError') message(field('error'), error.message); }
        finally { if (current === generation) { loading = false; if (blockingMessage) message(field('error'), blockingMessage); render(); } }
    }
    async function open(name) {
        if (dialog.open || busy) return;
        group = name; rows = []; selected = new Map(); assigned = new Set(); blocked = false; attempted = false; blockingMessage = '';
        field('subtitle').textContent = 'Resource Group: ' + name; field('type').value = 'local'; field('search').value = '';
        field('results').replaceChildren(); field('results').hidden = true; message(field('error'));
        dialog.showModal(); await load(); if (dialog.open) field('search').focus();
    }
    field('search').addEventListener('input', render);
    field('type').addEventListener('change', () => { field('search').value = ''; void load(); });
    field('reload').addEventListener('click', () => void load());
    field('all').addEventListener('change', () => {
        for (const item of visibleRows().filter(row => !assigned.has(fold(row.name)))) {
            if (field('all').checked) selected.set(fold(item.name), { name: item.name, type: field('type').value }); else selected.delete(fold(item.name));
        }
        render();
    });
    dialog.querySelectorAll('[data-user-group-close]').forEach(button => button.addEventListener('click', () => { if (!busy) dialog.close(); }));
    dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
    dialog.addEventListener('close', () => { if (!dialog.open) { generation++; requestAbort?.abort(); if (attempted) changed(); } });
    field('form').addEventListener('submit', async event => {
        event.preventDefault(); if (field('save').disabled) return;
        busy = true; attempted = true; message(field('error')); controls();
        try {
            const data = await request(grid.dataset.addUserGroupsUrl, { resourceGroupName: group, userGroups: [...selected.values()] });
            const result = data.result;
            if (!Array.isArray(result?.items) || result.items.length !== selected.size
                || result.items.some(item => !selected.has(fold(item.name)) || !['added','alreadyAssigned','failed','unconfirmed','notAttempted'].includes(item.status))
                || new Set(result.items.map(item => fold(item.name))).size !== selected.size) throw new Error('The assignment results could not be confirmed.');
            blocked = result.saveUnconfirmed === true || result.items.some(item => ['unconfirmed','notAttempted'].includes(item.status));
            const list = field('results'); list.replaceChildren();
            for (const item of result.items) {
                list.append(make('li', '', item.name + ': ' + item.message));
                if (['added','alreadyAssigned'].includes(item.status)) { selected.delete(fold(item.name)); assigned.add(fold(item.name)); }
            }
            list.hidden = false; changed();
            if (!selected.size && !blocked) dialog.close();
            else message(field('error'), blocked ? 'Some assignment results could not be confirmed. Close and inspect the refreshed grid before retrying.' : 'Some User Groups could not be added. Review the results; only the remaining selections will be retried.');
        } catch (error) {
            blocked = !error.status || error.status >= 500 || [401,403,408].includes(error.status) || error.payload?.saveUnconfirmed === true;
            message(field('error'), blocked && ![401,403].includes(error.status) ? 'The assignment results could not be confirmed. Close and inspect the refreshed grid before retrying.' : error.message);
        }
        finally { if (blocked) blockingMessage = field('error').textContent; busy = false; render(); }
    });
    function removeControls() {
        removeDialog.querySelectorAll('button').forEach(button => { button.disabled = removing; });
        removeField('confirm').disabled = removing || removeBlocked;
        removeField('confirm').textContent = removing ? 'Removing…' : 'Remove User Group';
    }
    grid.addEventListener('maui:add-user-groups', event => void open(event.detail.name));
    grid.addEventListener('maui:remove-user-group', event => {
        if (removing || removeDialog.open || !event.detail.child) return;
        removeTarget = { resourceGroupName: event.detail.name, userGroupName: event.detail.child.name, aclId: event.detail.child.aclId, editVersion: event.detail.child.editVersion };
        removeBlocked = false; removeField('resource').value = removeTarget.resourceGroupName; removeField('name').value = removeTarget.userGroupName;
        message(removeField('error')); removeControls(); removeDialog.showModal();
    });
    removeDialog.querySelectorAll('[data-user-group-remove-close]').forEach(button => button.addEventListener('click', () => { if (!removing) removeDialog.close(); }));
    removeDialog.addEventListener('cancel', event => { if (removing) event.preventDefault(); });
    removeDialog.addEventListener('close', () => { if (!removeDialog.open && removeBlocked) changed(); });
    removeField('form').addEventListener('submit', async event => {
        event.preventDefault(); if (removing || removeBlocked || !removeTarget) return;
        removing = true; message(removeField('error')); removeControls();
        try {
            const data = await request(grid.dataset.removeUserGroupUrl, removeTarget);
            const result = data.result;
            if (result?.saveUnconfirmed || !Array.isArray(result?.items) || result.items.length !== 1
                || !['removed','alreadyRemoved'].includes(result.items[0].status)) throw new Error('The removal result could not be confirmed.');
            changed(); removeDialog.close();
        } catch (error) {
            removeBlocked = !error.status || error.status >= 500 || error.payload?.editConflict || [401,403,408].includes(error.status);
            message(removeField('error'), removeBlocked && ![401,403].includes(error.status) ? 'The assignment changed or its removal could not be confirmed. Close and inspect the refreshed grid before retrying.' : error.message);
        }
        finally { removing = false; removeControls(); }
    });
})();
