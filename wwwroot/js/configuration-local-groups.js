(() => {
    'use strict';
    const settings = document.querySelector('[data-lg-settings]');
    const panel = document.querySelector('[data-lg-panel]');
    if (!settings || !panel) return;
    const field = name => panel.querySelector('[data-lg-' + name + ']');
    const addDialog = field('add-dialog');
    const addStatus = field('add-status');
    const removeDialog = field('remove-dialog');
    const resizeGroupList = limitRows(field('groups'), 14);
    const resizeMembers = limitRows(field('members'), 10);
    const checkedPrincipals = new Set();
    const toggle = settings.querySelector('[data-setting="moveItLocalGroupsEnabled"]');
    const endpoint = settings.querySelector('[data-lg-broker-url]');
    const token = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    let savedEnabled = panel.dataset.enabled === 'true', registered = false, busy = false, uncertain = false, generation = 0;
    let groups = [], selected = null, principals = [], memberToRemove = null;
    let opened = false, loadingInventory = false;
    const inventoryRetryDelays = [1000, 2000, 3000];
    const mutations = new Set(['create', 'describe', 'add-member', 'remove-member', 'delete']);
    const readSettings = () => ({ enabled: toggle.checked, agents: [...settings.querySelectorAll('[data-lg-registration]')].map(row => ({
        serverKey: row.dataset.serverKey, certificateSha256: row.querySelector('[data-lg-certificate]').value.trim()
    })) });
    function error(message = '') {
        for (const name of ['error', 'remove-error', 'add-error']) {
            const value = addDialog.open === (name === 'add-error') ? message : '';
            field(name).textContent = value;
            field(name).hidden = !value;
        }
        addStatus.hidden = !field('add-error').hidden;
    }
    function status(message) { field('status').textContent = message; }
    function dirtyDescription() { return selected && field('description').value !== selected.description; }
    function guardDraft() { if (!dirtyDescription()) return true; error('Save the description or reload groups before continuing.'); return false; }
    function controls() {
        const addBusy = busy && addDialog.open;
        const groupBusy = busy && !addBusy && !loadingInventory;
        panel.querySelectorAll('button,input,select,textarea').forEach(node => { node.disabled = busy; });
        field('load').disabled = busy || !savedEnabled || panel.dataset.canManage !== 'true';
        field('new').disabled = field('load').disabled || !registered || uncertain;
        for (const name of ['delete', 'description-save', 'add-open', 'find', 'add', 'description', 'query']) field(name).disabled = busy || !registered || !selected || uncertain;
        if (selected) {
            const assigned = groups.find(row => row.group.sid === selected.sid)?.resourceGroups || [];
            const hasMembers = selected.members.length > 0;
            field('delete').disabled ||= hasMembers || assigned.length > 0;
            field('delete').title = hasMembers ? 'Remove all members before deleting this group.' : assigned.length ? 'Remove MOVEit Resource Group assignments before deleting this group.' : '';
        }
        field('add').disabled ||= checkedPrincipals.size === 0;
        addDialog.classList.toggle('is-busy', addBusy);
        field('add-body').setAttribute('aria-busy', String(addBusy));
        if (!addBusy) addStatus.textContent = '';
        field('results').querySelectorAll('input').forEach(checkbox => {
            checkbox.disabled = busy || !registered || uncertain || checkbox.dataset.existing === 'true';
        });
        field('remove-apply').disabled = busy || !registered || uncertain || !memberToRemove;
        field('remove-apply').textContent = busy && removeDialog.open ? 'Removing…' : 'Remove Member';
        removeDialog.classList.toggle('is-busy', busy && removeDialog.open);
        field('confirm-apply').disabled = busy || !registered || uncertain;
        field('create-save').disabled = busy || uncertain || !registered || panel.dataset.canManage !== 'true';
        panel.querySelectorAll('.local-groups-group-button').forEach(button => { button.disabled = busy || !registered || uncertain; });
        panel.querySelectorAll('[data-lg-remove]').forEach(button => { button.disabled = busy || !registered || uncertain; });
        const workspace = field('workspace');
        if (loadingInventory || !registered) workspace.hidden = true;
        else if (workspace.hidden) {
            workspace.hidden = false;
            resizeGroupList();
        }
        field('inventory-loading').hidden = !loadingInventory;
        field('right').setAttribute('aria-busy', String(groupBusy));
        field('loading').hidden = !groupBusy;
        field('empty').hidden = groupBusy || field('groups').children.length > 0;
    }
    async function request(body) {
        const current = generation;
        const operation = body?.operation;
        const updating = mutations.has(operation);
        const controller = new AbortController(), timeout = window.setTimeout(() => controller.abort(), body ? 45000 : 15000);
        const options = { credentials: 'same-origin', cache: 'no-store', signal: controller.signal, headers: { Accept: 'application/json' } };
        if (body) {
            options.method = 'POST';
            options.headers['Content-Type'] = 'application/json';
            options.headers.RequestVerificationToken = token;
            options.body = JSON.stringify(body);
        }
        try {
            const response = await fetch(body ? panel.dataset.executeUrl : panel.dataset.stateUrl, options);
            let json = null;
            if ((response.headers.get('content-type') || '').includes('application/json')) {
                try { json = await response.json(); }
                catch (failure) {
                    if (failure.name === 'AbortError') throw failure;
                    const unreadable = new Error('The server response could not be read. Refresh the local groups before retrying.');
                    unreadable.uncertain = updating;
                    throw unreadable;
                }
            }
            if (current !== generation) { const stale = new Error('Settings changed while the request was running. Refresh the local groups.'); stale.uncertain = updating; throw stale; }
            if (!response.ok || !json) {
                const fallback = !body ? 'Service registration could not be checked.' : response.ok
                    ? 'The service acknowledgement could not be confirmed. Refresh before retrying.' : 'The local group request could not be completed.';
                const failure = new Error(json?.message || fallback);
                failure.uncertain = Boolean(json?.outcomeUncertain) || updating && (response.status >= 500 || response.ok);
                failure.serviceUnavailable = json?.retryable === true;
                failure.retryable = failure.serviceUnavailable || [502, 503, 504].includes(response.status);
                throw failure;
            }
            if (!body) return json;
            if (operation === 'list' ? !Array.isArray(json.groups) : json.success !== true || json.protocolVersion !== 1 || typeof json.computerName !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(json.id || '')) { const invalid = new Error('The service acknowledgement could not be confirmed. Refresh before retrying.'); invalid.uncertain = updating; throw invalid; }
            return json;
        } catch (failure) {
            if (failure.name === 'AbortError' || failure instanceof TypeError) {
                failure.uncertain = updating;
                failure.retryable = !updating;
            }
            throw failure;
        } finally { window.clearTimeout(timeout); }
    }
    async function run(operation, action) {
        if (busy) return;
        const updating = mutations.has(operation);
        if (updating && uncertain) { error('Refresh the local groups before another change.'); return; }
        const current = generation;
        const finding = operation === 'search';
        busy = true;
        error();
        if (addDialog.open) addStatus.textContent = finding ? 'Searching…' : 'Adding members…';
        else status(updating ? 'Updating…' : 'Loading…');
        controls();
        try {
            await action();
            if (current === generation && !finding) status(updating ? 'Updated.' : 'Loaded.');
        } catch (failure) {
            if (current === generation) {
                uncertain ||= Boolean(failure.uncertain);
                if (!finding || uncertain) status(uncertain ? 'Refresh required.' : 'Ready.');
                error((failure.message || 'The request failed.') + (uncertain ? ' Refresh the local groups before another change.' : ''));
            }
        }
        finally { if (current === generation) { busy = false; controls(); } }
    }
    function make(tag, text, cls = '') { const node = document.createElement(tag); if (text !== undefined) node.textContent = text; if (cls) node.className = cls; return node; }
    function button(text, action) { const node = make('button', text, 'secondary-button compact-button'); node.type = 'button'; node.addEventListener('click', action); return node; }
    function limitRows(body, limit) {
        const table = body.closest('table');
        const viewport = table.parentElement;
        const resize = () => {
            const lastVisibleRow = body.rows.length > limit ? body.rows[limit - 1] : null;
            // Use rendered heights so wrapped names still count as a single row.
            const height = lastVisibleRow ? lastVisibleRow.getBoundingClientRect().bottom - table.getBoundingClientRect().top : 0;
            const maxHeight = height > 0 ? Math.ceil(height + 2 * viewport.clientTop) + 'px' : '';
            if (viewport.style.maxHeight !== maxHeight) viewport.style.maxHeight = maxHeight;
        };
        // Resizing the scroll box can change the observed table width.
        new ResizeObserver(() => window.requestAnimationFrame(resize)).observe(table);
        return resize;
    }
    function renderGroups() {
        const body = field('groups'); body.replaceChildren(); const text = field('filter').value.trim().toLowerCase();
        let count = 0;
        for (const row of groups) {
            if (row.group.protected) continue;
            if (text && ![row.group.name, row.group.description, ...(row.resourceGroups || [])].join(' ').toLowerCase().includes(text)) continue;
            count++;
            const tr = make('tr'), name = make('td'), assigned = make('td', (row.resourceGroups || []).join(', ') || '—');
            tr.setAttribute('aria-selected', String(selected?.sid === row.group.sid));
            const open = button(row.group.name, () => { if (guardDraft()) void run('get', () => select(row.group.sid)); });
            open.className = 'local-groups-group-button'; open.disabled = busy || !registered || uncertain; open.title = row.group.description || 'View group';
            name.append(open);
            tr.append(name, assigned); body.append(tr);
        }
        field('empty').hidden = count !== 0;
        field('empty').textContent = groups.length ? 'No matching local groups.' : 'No local groups returned.';
        resizeGroupList();
    }
    function renderResults() {
        const body = field('results');
        const memberSids = new Set(selected?.members.map(member => member.sid));
        body.replaceChildren();
        for (const principal of principals) {
            const account = principal.account || principal.sid;
            const existing = memberSids.has(principal.sid);
            if (existing) checkedPrincipals.delete(principal.sid);
            const row = make('tr');
            const cell = make('td', undefined, 'local-groups-result-check');
            const checkbox = make('input');
            checkbox.type = 'checkbox';
            checkbox.dataset.existing = String(existing);
            checkbox.checked = checkedPrincipals.has(principal.sid);
            checkbox.disabled = existing || busy || !registered || uncertain;
            checkbox.setAttribute('aria-label', existing ? account + ' is already a member' : 'Select ' + account);
            if (existing) checkbox.title = 'Already a member';
            row.setAttribute('aria-selected', String(checkbox.checked));
            checkbox.addEventListener('change', () => {
                if (checkbox.checked) checkedPrincipals.add(principal.sid);
                else checkedPrincipals.delete(principal.sid);
                row.setAttribute('aria-selected', String(checkbox.checked));
                controls();
            });
            cell.append(checkbox);
            row.append(cell, make('td', account), make('td', principal.type || 'Unresolved', 'local-groups-account-type'));
            body.append(row);
        }
        field('results-empty').hidden = principals.length > 0;
    }
    function clearResults() {
        principals = [];
        checkedPrincipals.clear();
        renderResults();
    }
    function clearSelection() {
        selected = null;
        memberToRemove = null;
        if (addDialog.open) addDialog.close();
        if (removeDialog.open) removeDialog.close();
        field('details').hidden = true;
        field('create').hidden = true;
        field('select-note').hidden = false;
        field('confirm').hidden = true;
        clearResults();
    }
    function renderSelected(keepResults) {
        field('details').hidden = false;
        field('create').hidden = true;
        field('select-note').hidden = true;
        field('selected-name').textContent = selected.name;
        field('description').value = selected.description;
        field('confirm').hidden = true;
        if (keepResults) renderResults();
        else clearResults();
        const body = field('members');
        body.replaceChildren();
        for (const member of selected.members) {
            const row = make('tr');
            const actions = make('td', undefined, 'local-groups-member-actions');
            const remove = button('Remove', () => { if (guardDraft()) confirmRemoval(member); });
            remove.dataset.lgRemove = member.sid;
            remove.className = 'configuration-remove-button';
            actions.append(remove);
            row.append(make('td', member.account || member.sid), make('td', member.type || 'Unresolved', 'local-groups-account-type'), actions);
            body.append(row);
        }
        field('members-empty').hidden = selected.members.length !== 0;
        resizeMembers();
        renderGroups();
    }
    async function select(sid, keepResults = false) {
        const data = await request({ operation: 'get', groupSid: sid });
        if (data.groups?.length !== 1 || data.groups[0].sid !== sid || !Array.isArray(data.groups[0].members) || !data.groups[0].version || data.groups[0].protected) throw new Error('The service did not return an available group with complete current details.');
        selected = data.groups[0];
        uncertain = false;
        renderSelected(keepResults);
    }
    async function reload() {
        const data = await request({ operation: 'list' });
        if (!data.computerName || data.groups.some(row => !row?.group?.sid || typeof row.group.protected !== 'boolean' || !Array.isArray(row.resourceGroups))) throw new Error('The local groups inventory was incomplete.');
        groups = data.groups.filter(row => !row.group.protected); uncertain = false; clearSelection(); renderGroups();
        field('server').textContent = data.computerName;
    }
    const identity = () => ({ groupSid: selected.sid, groupName: selected.name, expectedVersion: selected.version });
    async function update(body, keepResults = false) {
        const current = generation;
        const oldSid = selected?.sid;
        await request(body);
        try {
            if (body.operation === 'create' || body.operation === 'delete') await reload();
            else await select(oldSid, keepResults);
            return current === generation;
        } catch (failure) {
            if (current === generation) {
                uncertain = true;
                error('Updated, but the current group details could not be loaded: ' + failure.message + ' Refresh the local groups.');
            }
            return false;
        }
    }
    function confirmRemoval(member) {
        if (busy || !registered || uncertain || !selected || removeDialog.open) return;
        field('confirm').hidden = true;
        memberToRemove = member;
        field('remove-account').textContent = member.account || member.sid;
        field('remove-group').textContent = selected.name;
        error();
        controls();
        removeDialog.showModal();
    }
    async function checkRegistration() {
        const data = await request();
        field('server').textContent = data.computerName || data.server || 'Current Automation Server';
        if (data.registered !== true) throw new Error(data.message || 'Register the Windows Service under Experimental.');
    }
    async function loadInventory() {
        const current = generation;
        try {
            for (let attempt = 0; current === generation; attempt++) {
                try {
                    await checkRegistration();
                    if (current !== generation) return;
                    await reload();
                    if (current === generation) registered = true;
                    return;
                } catch (failure) {
                    if (current !== generation) return;
                    if (!failure.retryable) throw failure;
                    if (attempt === inventoryRetryDelays.length) {
                        const message = failure.serviceUnavailable ? 'The Windows Service is not responding to requests.' : 'MAUI could not load the local groups.';
                        throw new Error(message + ' Click Reload Groups to try again.');
                    }
                    await new Promise(resolve => window.setTimeout(resolve, inventoryRetryDelays[attempt]));
                }
            }
        } finally {
            if (current === generation) loadingInventory = false;
        }
    }
    function loadGroups() {
        if (field('load').disabled) return;
        opened = true; field('load').textContent = 'Reload Groups';
        loadingInventory = true; registered = false;
        void run('list', loadInventory);
    }
    function open() { if (savedEnabled && !opened) loadGroups(); }
    window.MauiLocalGroupSettings = {
        read: readSettings,
        open,
        markSaved: () => {
            generation++; savedEnabled = toggle.checked; registered = false; opened = false; busy = false; loadingInventory = false;
            clearSelection(); groups = []; uncertain = false; panel.hidden = !savedEnabled;
            field('load').textContent = 'Load Groups'; renderGroups(); controls();
            if (panel.closest('[data-config-section]')?.classList.contains('active')) open();
        }
    };
    field('filter').addEventListener('input', renderGroups);
    field('load').addEventListener('click', loadGroups);
    field('new').addEventListener('click', () => { if (!guardDraft()) return; clearSelection(); renderGroups(); controls(); field('select-note').hidden = true; field('create').hidden = false; field('create-name').focus(); });
    field('create-cancel').addEventListener('click', () => { clearSelection(); renderGroups(); controls(); });
    field('create-save').addEventListener('click', () => void run('create', async () => {
        const name = field('create-name').value;
        if (!name.trim()) throw new Error('Group Name is required.');
        await update({ operation: 'create', groupName: name, description: field('create-description').value });
        if (!uncertain) { field('create-name').value = ''; field('create-description').value = ''; field('create').hidden = true; }
    }));
    field('description-save').addEventListener('click', () => { if (selected) void run('describe', () => update({ operation: 'describe', ...identity(), description: field('description').value })); });
    field('add-open').addEventListener('click', () => {
        if (field('add-open').disabled || addDialog.open || !guardDraft()) return;
        field('confirm').hidden = true;
        field('add-group').textContent = selected.name;
        field('query').value = '';
        clearResults();
        error();
        controls();
        addDialog.showModal();
    });
    field('find').addEventListener('click', () => {
        if (!addDialog.open) return;
        void run('search', async () => {
            clearResults();
            const query = field('query').value.trim();
            if (!query) throw new Error('Enter a user or group to find.');
            const separator = query.indexOf('\\');
            const authority = separator < 0 ? '' : query.slice(0, separator).toUpperCase();
            const exact = separator < 0
                ? query.includes('@') || /^S-1-/i.test(query)
                : authority === field('server').textContent.toUpperCase() || authority === 'BUILTIN';
            const data = await request(exact ? { operation: 'resolve', principal: query } : { operation: 'search', query });
            if (!Array.isArray(data.principals)) throw new Error('User/group results were incomplete.');
            principals = data.principals;
            renderResults();
        });
    });
    field('query').addEventListener('input', () => { clearResults(); controls(); });
    field('query').addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); field('find').click(); }
    });
    field('add').addEventListener('click', () => {
        if (!addDialog.open || !selected) return;
        const additions = principals.filter(principal => checkedPrincipals.has(principal.sid));
        if (!additions.length) return;
        void run('add-member', async () => {
            const current = generation;
            let completed = 0;
            for (const principal of additions) {
                try {
                    if (!await update({ operation: 'add-member', ...identity(), principal: principal.account }, true)) {
                        if (current === generation && completed + 1 < additions.length)
                            error(field('add-error').textContent + ' The remaining accounts were not submitted.');
                        return;
                    }
                } catch (failure) {
                    const progress = completed ? `${completed} of ${additions.length} selected members added. ` : '';
                    failure.message = `${progress}Could not add ${principal.account || principal.sid}. ${failure.message}`;
                    throw failure;
                }
                checkedPrincipals.delete(principal.sid);
                completed++;
            }
            addDialog.close();
        });
    });
    for (const name of ['add-close', 'add-cancel']) {
        field(name).addEventListener('click', () => { if (!busy) addDialog.close(); });
    }
    addDialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
    addDialog.addEventListener('close', () => {
        if (addDialog.open) return;
        const message = field('add-error').textContent;
        if (uncertain && message) error(message);
        if (!field('add-open').disabled) field('add-open').focus();
    });
    for (const name of ['remove-close', 'remove-cancel']) {
        field(name).addEventListener('click', () => { if (!busy) removeDialog.close(); });
    }
    removeDialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
    removeDialog.addEventListener('close', () => { if (!removeDialog.open) memberToRemove = null; });
    field('remove-apply').addEventListener('click', () => {
        if (!removeDialog.open || !memberToRemove || !selected) return;
        void run('remove-member', async () => {
            if (await update({ operation: 'remove-member', ...identity(), memberSid: memberToRemove.sid }, true))
                removeDialog.close();
        });
    });
    field('delete').addEventListener('click', () => {
        if (!selected || field('delete').disabled || !guardDraft()) return;
        field('confirm-text').textContent = 'Delete local group ' + selected.name + '? MOVEit references will be checked before deletion.';
        field('confirm-name').value = '';
        field('confirm').hidden = false;
    });
    field('confirm-cancel').addEventListener('click', () => { field('confirm').hidden = true; });
    field('confirm-apply').addEventListener('click', () => {
        if (field('confirm').hidden || !selected || field('delete').disabled) return;
        const name = field('confirm-name').value;
        if (name !== selected.name) { error('Type the exact group name to confirm deletion.'); return; }
        void run('delete', () => update({ operation: 'delete', ...identity(), confirmDelete: name }));
    });
    endpoint.textContent = window.location.origin + endpoint.dataset.lgAgentPath;
    panel.hidden = !savedEnabled; controls();
    if (panel.closest('[data-config-section]')?.classList.contains('active')) open();
})();

