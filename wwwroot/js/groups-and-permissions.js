/* Author: Marc R. Davis (marcdavis@comcast.net)
 * Resource Group hierarchy. Parent paging and ACL summaries intentionally stay outside the flat-grid controller. */
(() => {
    'use strict';
    const keys = ['TaskRun', 'TaskAddEditDelete', 'TaskEditExisting', 'HostAddEditDelete', 'ScriptAddEditDelete', 'SSLAddEditDelete', 'SSHAddEditDelete', 'PGPAddEditDelete'];
    const fold = value => String(value || '').toLocaleLowerCase();
    // Search narrows children only when the parent name does not match. Summaries always use the complete ACL set.
    function filterGroups(groups, search, filters, descending) {
        const term = fold(search.trim());
        return groups.map(group => ({ group, children: !term || fold(group.name).includes(term)
            ? group.userGroups : group.userGroups.filter(child => fold(child.name).includes(term)) }))
            .filter(item => (!term || fold(item.group.name).includes(term) || item.children.length)
                && keys.every(key => !filters[key] || item.group.permissions[key].state === filters[key]))
            .sort((a, b) => a.group.name.localeCompare(b.group.name, undefined, { sensitivity: 'base', numeric: true }) * (descending ? -1 : 1));
    }
    function flattenRows(matches, expanded, searching) {
        return matches.flatMap(({ group, children }) => [
            { name: group.name, permissions: Object.fromEntries(keys.map(key => [key, group.permissions[key].state])), parent: true },
            ...((searching || expanded.has(group.name)) ? children.map(child => ({ name: `${group.name} / ${child.name}`,
                permissions: Object.fromEntries(keys.map(key => [key, child.permissions[key] ? 'Granted' : 'Not granted'])), parent: false })) : [])
        ]);
    }
    window.MauiResourcePermissions = { keys, filterGroups, flattenRows };
    const root = document.querySelector('[data-permissions-grid]');
    if (!root) return;
    const pick = name => root.querySelector(`[data-permissions-${name}]`);
    const table = root.querySelector('table');
    const body = pick('body');
    const search = pick('search');
    const error = pick('error');
    const dialog = pick('settings-dialog');
    const pageSizes = JSON.parse(root.dataset.pageSizes);
    const normalizeSize = value => pageSizes.includes(Number(value)) ? Number(value) : Number(root.dataset.defaultPageSize);
    let pageSize = normalizeSize(document.documentElement.dataset.userGridPageSize);
    let groups = [], page = 1, descending = false, loaded = false, savingSettings = false;
    let editing = null, editGeneration = 0;
    const heldControls = new Map();
    const expanded = new Set();
    const searchCollapsed = new Set();
    const filters = {};
    const matches = () => filterGroups(groups, search.value, filters, descending);
    const setError = message => { error.textContent = message; error.hidden = !message; };
    const make = (tag, className, text) => {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (text !== undefined) element.textContent = text;
        return element;
    };
    const cell = (row, text, className) => { const td = make('td', className, text); row.append(td); return td; };
    const empty = message => { const row = make('tr'); cell(row, message, 'permissions-empty').colSpan = 9; body.replaceChildren(row); };
    // Parent and assigned-user rows share a floating menu so horizontal scrolling cannot clip it.
    const rowMenu = make('div', 'task-row-action-menu permissions-row-actions');
    rowMenu.hidden = true; rowMenu.setAttribute('role', 'menu');
    const menuTargets = new WeakMap();
    let menuAnchor = null;
    const actions = [
        { label: 'Edit Resource Group', action: 'edit-resource-group' },
        { label: 'Add User Groups', action: 'add-user-groups' },
        { label: 'Edit Permissions', action: 'edit-permissions' },
        { label: 'Delete Resource Group', action: 'delete-resource-group', danger: true },
        { label: 'Remove User Group', action: 'remove-user-group', child: true, danger: true }
    ];
    for (const action of actions) {
        const divider = action.label === 'Delete Resource Group' ? make('div', 'task-row-action-divider') : null;
        if (divider) { divider.setAttribute('role', 'separator'); rowMenu.append(divider); }
        const button = make('button', action.danger ? 'task-row-danger-action' : '', action.label); button.type = 'button';
        button.disabled = action.disabled === true; button.setAttribute('role', 'menuitem');
        button.dataset.permissionsCommand = action.action || ''; button.dataset.permissionsChildAction = String(Boolean(action.child));
        button.addEventListener('click', () => {
            const target = menuTargets.get(menuAnchor); closeRowMenu();
            if (target && action.action) root.dispatchEvent(new CustomEvent('maui:' + action.action, { detail: target }));
        });
        rowMenu.append(button);
    }
    document.body.append(rowMenu);
    function closeRowMenu() { if (menuAnchor) menuAnchor.setAttribute('aria-expanded', 'false'); menuAnchor = null; rowMenu.hidden = true; }
    function openRowMenu(button) {
        if (editing) return;
        root.querySelectorAll('[data-menu].open').forEach(menu => toolbar.closeMenu(menu));
        const reopen = menuAnchor !== button || rowMenu.hidden;
        closeRowMenu(); if (!reopen) return;
        const target = menuTargets.get(button);
        const child = Boolean(target?.child);
        const assigned = groups.find(group => group.name === target?.name)?.userGroups.length || 0;
        rowMenu.querySelector('[data-permissions-command="edit-permissions"]').disabled = assigned === 0;
        rowMenu.setAttribute('aria-label', child ? 'User Group assignment actions' : 'Resource Group actions');
        rowMenu.querySelectorAll('button').forEach(action => { action.hidden = (action.dataset.permissionsChildAction === 'true') !== child; });
        rowMenu.querySelector('[role="separator"]').hidden = child;
        menuAnchor = button; button.setAttribute('aria-expanded', 'true'); rowMenu.hidden = false;
        const box = button.getBoundingClientRect();
        rowMenu.style.left = `${Math.max(8, Math.min(box.left, innerWidth - rowMenu.offsetWidth - 8))}px`;
        rowMenu.style.top = `${Math.max(8, Math.min(box.bottom + 4, innerHeight - rowMenu.offsetHeight - 8))}px`;
    }
    function menuButton(name, child = null) {
        const button = make('button', 'row-actions-button permissions-row-menu', '…'); button.type = 'button';
        button.setAttribute('aria-label', `Actions for ${child ? child.name + ' in ' : ''}${name}`);
        button.setAttribute('aria-haspopup', 'menu'); button.setAttribute('aria-expanded', 'false');
        button.dataset.permissionsGroupName = name;
        button.disabled = Boolean(editing);
        if (child) button.dataset.permissionsUserGroupName = child.name;
        menuTargets.set(button, { name, child });
        button.addEventListener('click', event => { event.stopPropagation(); openRowMenu(button); }); return button;
    }
    function render() {
        closeRowMenu();
        const result = matches();
        const pages = Math.max(1, Math.ceil(result.length / pageSize)); page = Math.min(page, pages);
        const slice = result.slice((page - 1) * pageSize, page * pageSize);
        const fragment = document.createDocumentFragment();
        for (const { group, children } of slice) {
            const active = editing?.name === group.name;
            const open = active || (search.value.trim() ? !searchCollapsed.has(group.name) : expanded.has(group.name));
            const row = make('tr', 'permissions-parent');
            const name = cell(row); const nameLine = make('div', 'permissions-group-name');
            const expand = make('button', 'permissions-expand'); expand.type = 'button'; expand.setAttribute('aria-expanded', String(open));
            expand.disabled = Boolean(editing);
            expand.setAttribute('aria-label', `${open ? 'Collapse' : 'Expand'} ${group.name}`); expand.title = group.name;
            expand.append(make('span', 'permissions-expand-symbol', open ? '−' : '+'), make('span', '', group.name));
            expand.addEventListener('click', () => {
                // Search expansion is temporary; clearing search restores the normal expansion choices.
                if (search.value.trim()) { if (open) searchCollapsed.add(group.name); else searchCollapsed.delete(group.name); }
                else if (expanded.has(group.name)) expanded.delete(group.name); else expanded.add(group.name);
                render();
                [...body.querySelectorAll('.permissions-expand')].find(button => button.textContent.slice(1) === group.name)?.focus({ preventScroll: true });
            });
            nameLine.append(expand, active ? permissionActions() : menuButton(group.name)); name.append(nameLine, make('span', 'permissions-assigned-count', `${group.userGroups.length} assigned user ${group.userGroups.length === 1 ? 'group' : 'groups'}`));
            if (active) {
                const status = make('span', 'permissions-edit-status' + (editing.message ? ' is-error' : ''), editing.message || 'Editing permissions');
                status.setAttribute('role', editing.message ? 'alert' : 'status'); name.append(status);
            }
            keys.forEach(key => {
                const summary = active ? permissionSummary([...editing.rows.values()].map(item => item.values), key) : group.permissions[key];
                const td = cell(row); const badge = make('span', `permissions-summary permissions-${summary.state.toLowerCase()}`, summary.state);
                badge.title = `${summary.granted} of ${summary.total} assigned user groups`;
                badge.setAttribute('aria-label', `${summary.state}: ${badge.title}`); td.append(badge);
            }); fragment.append(row);
            if (open) {
                const visibleChildren = active ? group.userGroups : children;
                if (!visibleChildren.length) { const emptyRow = make('tr'); cell(emptyRow, 'No assigned user groups.', 'permissions-empty-child').colSpan = 9; fragment.append(emptyRow); }
                for (const child of visibleChildren) {
                    const childRow = make('tr', 'permissions-child' + (active ? ' permissions-editing' : '')); const childName = cell(childRow);
                    const childLine = make('div', 'permissions-group-name'); childLine.append(make('span', 'permissions-user-name', child.name));
                    if (!active) childLine.append(menuButton(group.name, child)); childName.append(childLine); childName.title = child.name;
                    keys.forEach(key => {
                        if (active) { const td = cell(childRow); td.append(permissionCheckbox(child, key)); return; }
                        const granted = child.permissions[key]; const td = cell(childRow, granted ? '✓' : '—', granted ? 'permissions-granted' : 'permissions-denied'); td.setAttribute('aria-label', granted ? 'Granted' : 'Not granted'); td.title = granted ? 'Granted' : 'Not granted';
                    });
                    fragment.append(childRow);
                }
            }
        }
        body.replaceChildren(fragment);
        root.querySelector('.permissions-name-column').classList.toggle('is-editing', Boolean(editing));
        if (!result.length) empty(groups.length ? 'No Resource Groups match these filters.' : 'No visible Resource Groups.');
        pick('count').replaceChildren(make('strong', '', String(result.length)), document.createTextNode(' '), make('span', '', 'of'), document.createTextNode(' '), make('strong', '', String(groups.length)), document.createTextNode(' '), make('span', '', 'resource groups'));
        pick('page').textContent = `${page} / ${pages}`;
        pick('summary').textContent = `Showing ${result.length ? (page - 1) * pageSize + 1 : 0}–${Math.min(page * pageSize, result.length)} of ${result.length}`;
        pick('rows').value = String(pageSize);
        pick('previous').disabled = page <= 1; pick('next').disabled = page >= pages;
        pick('sort-indicator').textContent = descending ? '↓' : '↑'; pick('name-header').setAttribute('aria-sort', descending ? 'descending' : 'ascending');
        holdGridControls();
    }
    function permissionSummary(values, key) {
        const granted = values.filter(item => item[key]).length, total = values.length;
        return { state: granted === 0 ? 'None' : granted === total ? 'All' : 'Some', granted, total };
    }
    function permissionChanges() {
        return editing ? [...editing.rows.values()].filter(item => !item.completed && keys.some(key => item.values[key] !== item.original[key])) : [];
    }
    function holdGridControls() {
        if (!editing) return;
        root.querySelectorAll('.sticky-grid-tools button,.sticky-grid-tools input,.sticky-grid-tools select,thead button,thead select,.pager-controls button,.pager-controls select').forEach(control => {
            if (!heldControls.has(control)) heldControls.set(control, control.disabled);
            control.disabled = true;
        });
    }
    function permissionActions() {
        const actions = make('span', 'permissions-edit-actions');
        const cancel = make('button', 'secondary-button', 'Cancel'), save = make('button', 'primary-button', editing.busy ? 'Saving…' : 'Save');
        cancel.type = save.type = 'button'; cancel.dataset.permissionsEditCancel = ''; save.dataset.permissionsEditSave = '';
        cancel.disabled = editing.busy; save.disabled = editing.busy || editing.blocked || !permissionChanges().length;
        cancel.addEventListener('click', finishPermissionEdit); save.addEventListener('click', () => void savePermissionEdit());
        actions.append(cancel, save); return actions;
    }
    function permissionCheckbox(child, key) {
        const draft = editing.rows.get(child.aclId), input = make('input', 'permissions-edit-checkbox'); input.type = 'checkbox';
        input.dataset.permissionsAcl = child.aclId; input.dataset.permissionsKey = key;
        input.checked = draft.values[key]; input.setAttribute('aria-label', `${child.name}: ${columns.find(column => column.key === key).label}`);
        const required = key === 'TaskEditExisting' && draft.values.TaskAddEditDelete;
        input.disabled = editing.busy || editing.blocked || draft.completed || required;
        if (required) input.title = 'Required while Tasks Add/Edit/Delete is selected.';
        input.addEventListener('change', () => {
            draft.values[key] = input.checked;
            if (key === 'TaskAddEditDelete' && input.checked) draft.values.TaskEditExisting = true;
            render(); body.querySelector(`[data-permissions-acl="${CSS.escape(child.aclId)}"][data-permissions-key="${key}"]`)?.focus({ preventScroll: true });
        });
        return input;
    }
    root.addEventListener('maui:edit-permissions', event => {
        if (editing || savingRows || savingSettings) return;
        const group = groups.find(item => item.name === event.detail.name); if (!group?.userGroups.length) return;
        if (new Set(group.userGroups.map(child => child.aclId)).size !== group.userGroups.length) { setError('Reload this Resource Group before editing its permissions.'); return; }
        editing = { name: group.name, rows: new Map(group.userGroups.map(child => [child.aclId, { child, original: { ...child.permissions },
            values: { ...child.permissions, TaskEditExisting: child.permissions.TaskEditExisting || child.permissions.TaskAddEditDelete }, completed: false }])), busy: false, blocked: false, message: '' };
        editGeneration++;
        expanded.add(group.name); searchCollapsed.delete(group.name); refresh.clearTimer(); setError(''); render();
        body.querySelector('[data-permissions-edit-cancel]')?.focus({ preventScroll: true });
    });
    function finishPermissionEdit() {
        if (!editing || editing.busy) return;
        editing = null; editGeneration++; heldControls.forEach((disabled, control) => { control.disabled = disabled; }); heldControls.clear();
        pick('refresh').disabled = refresh.isInFlight; render(); refresh.refreshWhenIdle(true);
    }
    async function savePermissionEdit() {
        if (!editing || editing.busy || editing.blocked) return;
        const changes = permissionChanges(); if (!changes.length) return;
        const state = editing; state.busy = true; state.message = ''; render();
        try {
            const response = await fetch(root.dataset.savePermissionsUrl, { method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]')?.value || '' },
                body: JSON.stringify({ resourceGroupName: state.name, updates: changes.map(item => ({ aclId: item.child.aclId, userGroupName: item.child.name, editVersion: item.child.editVersion, permissions: item.values })) }) });
            const payload = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
            if (!response.ok || payload?.success !== true) {
                state.blocked = response.status >= 500 || [401,403,408,409].includes(response.status) || payload?.editConflict || payload?.saveUnconfirmed;
                throw new Error(payload?.message || 'Permissions could not be saved.');
            }
            const result = payload.result, requested = new Set(changes.map(item => item.child.aclId));
            if (!Array.isArray(result?.items) || result.items.length !== changes.length || new Set(result.items.map(item => item.aclId)).size !== changes.length
                || result.items.some(item => !requested.has(item.aclId) || fold(state.rows.get(item.aclId).child.name) !== fold(item.name)
                    || !['saved','unchanged','failed','unconfirmed','conflict','notAttempted'].includes(item.status)))
            { state.blocked = true; throw new Error('The permission save results could not be confirmed.'); }
            state.blocked = result.saveUnconfirmed === true || result.editConflict === true || result.items.some(item => ['unconfirmed','conflict','notAttempted'].includes(item.status));
            for (const item of result.items) if (['saved','unchanged'].includes(item.status)) {
                const row = state.rows.get(item.aclId); row.completed = true; row.child.permissions = { ...row.values };
            }
            const group = groups.find(item => item.name === state.name);
            for (const key of keys) group.permissions[key] = permissionSummary(group.userGroups.map(child => child.permissions), key);
            state.message = result.items.map(item => `${item.name}: ${item.message}`).join(' ');
            if (state.blocked) state.message += ' Cancel editing to reload the current permissions before making further changes.';
            if (!state.blocked && !permissionChanges().length) { state.busy = false; finishPermissionEdit(); }
        } catch (failure) {
            // Network/JSON failures have no safe per-row acknowledgement; never replay this batch automatically.
            if (failure instanceof TypeError || failure instanceof SyntaxError) state.blocked = true;
            state.message = failure.message + (state.blocked ? ' Cancel editing to reload the current permissions before making further changes.' : '');
        } finally { state.busy = false; if (editing === state) render(); }
    }
    window.addEventListener('beforeunload', event => {
        if (editing && (editing.busy || permissionChanges().length)) { event.preventDefault(); event.returnValue = ''; }
    });
    // Adapt the full filtered hierarchy to the existing CSV and Print managers. No changes to flat-grid behavior.
    const exportTable = document.createElement('table'); const header = make('tr', 'column-header-row');
    const columns = [{ key: 'Name', label: 'Resource Group / User Group' }, ...keys.map(key => ({ key, label: root.querySelector(`[data-permission-column="${key}"]`).dataset.exportLabel }))];
    columns.forEach(column => { const th = make('th'); th.dataset.column = column.key; const label = make('div', 'sort-button'); label.append(make('span', '', column.label)); th.append(label); header.append(th); }); exportTable.append(header);
    const exporter = window.MauiGridExport.createManager({ grid: root, table: exportTable, cssEscape: CSS.escape,
        layout: { getColumnOrder: () => columns.map(column => column.key), isColumnVisible: () => true },
        getOrderedRows: () => flattenRows(matches(), search.value.trim() ? new Set(matches().map(item => item.group.name).filter(name => !searchCollapsed.has(name))) : expanded, false).map(item => {
            const row = make('tr'); row.dataset.rowId = item.name;
            columns.forEach(column => { const td = cell(row, column.key === 'Name' ? item.name : item.permissions[column.key]); td.dataset.column = column.key; }); return row;
        }) });
    const printer = window.MauiGridPrint.createManager({ grid: root, exporter });
    const toolbar = window.MauiGridActions.createManager({ grid: root, exporter, printer });
    toolbar.wire();
    root.querySelectorAll('[data-menu-button]').forEach(button => button.addEventListener('click', closeRowMenu));
    ['refresh', 'refresh-toggle'].forEach(name => pick(name).addEventListener('click', () => toolbar.closeMenu(pick('refresh-menu'))));
    search.addEventListener('input', () => { searchCollapsed.clear(); page = 1; if (loaded) render(); });
    root.querySelectorAll('[data-permissions-filter]').forEach(select => select.addEventListener('change', () => { filters[select.dataset.permissionsFilter] = select.value; page = 1; if (loaded) render(); }));
    pick('reset').addEventListener('click', () => { search.value = ''; searchCollapsed.clear(); Object.keys(filters).forEach(key => delete filters[key]); root.querySelectorAll('[data-permissions-filter]').forEach(select => { select.value = ''; }); page = 1; if (loaded) render(); });
    pick('sort').addEventListener('click', () => { descending = !descending; if (loaded) render(); });
    pick('previous').addEventListener('click', () => { page--; render(); }); pick('next').addEventListener('click', () => { page++; render(); });
    let savingRows = false;
    pick('rows').addEventListener('change', async () => {
        if (savingRows || savingSettings) { pick('rows').value = String(pageSize); return; }
        savingRows = true; pick('rows').disabled = true; pick('settings').disabled = true;
        const previous = pageSize;
        try {
            const saved = await window.mauiSaveGridPageSizePreference(normalizeSize(pick('rows').value));
            pageSize = normalizeSize(saved); document.documentElement.dataset.userGridPageSize = String(pageSize); page = 1;
            setError(''); if (loaded) render();
        } catch (failure) { pick('rows').value = String(previous); setError(failure.message || 'The grid preference could not be saved.'); }
        finally { savingRows = false; pick('rows').disabled = savingSettings; pick('settings').disabled = savingSettings; }
    });
    const updateSettingsAction = window.MauiModalUI.watchAction(dialog, pick('settings-save'), () => !savingSettings && !savingRows,
        () => normalizeSize(pick('page-size').value) !== pageSize);
    pick('settings').addEventListener('click', () => { pick('page-size').value = String(pageSize); pick('settings-error').hidden = true; dialog.showModal(); });
    root.querySelectorAll('[data-permissions-settings-close]').forEach(button => button.addEventListener('click', () => { if (!savingSettings) dialog.close(); }));
    dialog.addEventListener('cancel', event => { if (savingSettings) event.preventDefault(); });
    pick('settings-form').addEventListener('submit', async event => {
        event.preventDefault(); if (savingSettings || savingRows) return;
        savingSettings = true; dialog.querySelectorAll('button,select').forEach(control => { control.disabled = true; });
        pick('rows').disabled = true; pick('settings').disabled = true; pick('settings-error').hidden = true;
        try {
            const size = normalizeSize(pick('page-size').value);
            if (size !== pageSize) {
                const saved = await window.mauiSaveGridPageSizePreference(size);
                pageSize = normalizeSize(saved); document.documentElement.dataset.userGridPageSize = String(pageSize); page = 1;
            }
            if (loaded) render(); dialog.close();
        } catch (failure) { pick('settings-error').textContent = failure.message || 'The grid preference could not be saved.'; pick('settings-error').hidden = false; }
        finally {
            savingSettings = false; dialog.querySelectorAll('button,select').forEach(control => { control.disabled = false; });
            pick('rows').disabled = savingRows; pick('settings').disabled = savingRows; updateSettingsAction();
        }
    });
    document.addEventListener('click', event => { if (!rowMenu.contains(event.target)) closeRowMenu(); });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') { const anchor = menuAnchor; closeRowMenu(); anchor?.focus(); root.querySelectorAll('[data-menu].open').forEach(menu => toolbar.closeMenu(menu)); } });
    root.querySelector('.permissions-scroll').addEventListener('scroll', closeRowMenu); window.addEventListener('resize', closeRowMenu);
    // Reuse the shared CSS-owned grid viewport and its short-window fallback. This hierarchy deliberately
    // does not initialize the flat-grid controller, so it applies that controller's sizing decision here.
    function sizeViewport() {
        const footerHeight = document.querySelector('.app-footer')?.getBoundingClientRect().height || 0;
        const gap = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--grid-footer-gap')) || 14;
        const fixed = innerHeight - root.getBoundingClientRect().top - footerHeight - gap >= 280;
        document.documentElement.classList.toggle('grid-fixed-page-root', fixed);
        document.body.classList.toggle('grid-fixed-page', fixed);
        document.body.classList.toggle('grid-page-scroll-fallback', !fixed);
    }
    sizeViewport(); window.addEventListener('resize', sizeViewport);
    async function fetchJson(url, options) {
        const generation = editGeneration;
        const response = await (window.MauiMoveItGridConnection?.fetch || window.fetch.bind(window))(url, { ...options, headers: { Accept: 'application/json' } });
        if (response.status === 401) {
            if (!editing && !root.querySelector('dialog[open]') && generation === editGeneration) location.replace(root.dataset.loginUrl);
            throw new Error('MOVEit session expired.');
        }
        const payload = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
        if (!response.ok || payload?.success !== true || !Array.isArray(payload.groups)) throw new Error(payload?.message || 'Resource Group permissions could not be loaded.');
        return { ...payload, editGeneration: generation };
    }
    const refresh = window.mauiCreateResourceRefreshController({ grid: root, intervalSeconds: 60,
        fetchJson, refreshControl: pick('refresh-control'), refreshClock: pick('refresh-clock'),
        refreshToggle: pick('refresh-toggle'), refreshNow: pick('refresh'), lastRefreshElement: pick('refreshed'),
        pausedTitle: 'Resource Group refresh paused', activeTitle: 'Resource Group refresh options',
        persistLastRefresh: true, setAriaBusy: true,
        buildInitialUrl: () => root.dataset.inventoryUrl,
        buildRefreshUrl: force => { const url = new URL(root.dataset.inventoryUrl, location.href); url.searchParams.set('forceRefresh', String(force)); return url; },
        applyPayload: (payload, context) => {
            // A refresh started before edit mode may still complete. It must not replace the user's draft/revisions.
            if (editing || payload.editGeneration !== editGeneration) return;
            groups = payload.groups; loaded = true; setError(''); render(); table.setAttribute('aria-busy', 'false'); context.setLastRefresh(payload.refreshedUtc);
            pick('export').disabled = false; pick('print').disabled = false;
        },
        onConnectionFailure: (_failure, state) => {
            setError('');
            if (state.initial) {
                empty('Resource Groups could not be loaded. Use Refresh Now to retry.');
                pick('count').textContent = 'Unable to load resource groups';
                table.setAttribute('aria-busy', 'false');
            }
        },
        onInitialError: failure => { empty('Resource Group permissions could not be loaded. Use Refresh Now to retry.'); pick('count').textContent = 'Unable to load Resource Groups'; table.setAttribute('aria-busy', 'false'); setError(failure.message); },
        onRefreshError: failure => { if (!editing) setError(`${failure.message} ${loaded ? 'The last successful view remains displayed.' : 'Use Refresh Now to retry.'}`); },
        onBusyChange: () => holdGridControls()
    });
    root.addEventListener('maui:resource-group-closed', () => refresh.refreshWhenIdle(true));
    root.addEventListener('maui:resource-group-acls-changed', () => refresh.refreshWhenIdle(true));
    window.addEventListener('pagehide', () => { refresh.destroy(); closeRowMenu(); });
    void refresh.loadInitial();
})();
