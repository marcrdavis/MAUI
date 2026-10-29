/* Reusable MAUI Resource Group create/add workflow. */
(() => {
    function initialize(options = {}) {
        const setupUrl = String(options.setupUrl || '').trim();
        const createUrl = String(options.createUrl || '').trim();
        const addUrl = String(options.addUrl || '').trim();
        const token = String(options.antiForgeryToken || '');
        const loginUrl = String(options.loginUrl || window.mauiAppUrl('/Account/SessionExpired'));
        const status = typeof options.status === 'function' ? options.status : () => {};
        const changed = typeof options.changed === 'function' ? options.changed : () => {};

        const createDialog = document.querySelector('[data-resource-group-create-dialog]');
        const addDialog = document.querySelector('[data-resource-group-add-dialog]');
        if (!createDialog || !addDialog || !setupUrl || !createUrl || !addUrl) return null;

        const create = {
            dialog: createDialog,
            error: createDialog.querySelector('[data-resource-group-create-error]'),
            loading: createDialog.querySelector('[data-resource-group-create-loading]'),
            content: createDialog.querySelector('[data-resource-group-create-content]'),
            name: createDialog.querySelector('[data-resource-group-create-name]'),
            description: createDialog.querySelector('[data-resource-group-create-description]'),
            summary: createDialog.querySelector('[data-resource-group-create-summary]'),
            items: createDialog.querySelector('[data-resource-group-create-items]'),
            confirm: createDialog.querySelector('[data-resource-group-create-confirm]'),
            cancels: [...createDialog.querySelectorAll('[data-resource-group-create-cancel]')]
        };
        const add = {
            dialog: addDialog,
            error: addDialog.querySelector('[data-resource-group-add-error]'),
            loading: addDialog.querySelector('[data-resource-group-add-loading]'),
            content: addDialog.querySelector('[data-resource-group-add-content]'),
            name: addDialog.querySelector('[data-resource-group-add-name]'),
            summary: addDialog.querySelector('[data-resource-group-add-summary]'),
            items: addDialog.querySelector('[data-resource-group-add-items]'),
            confirm: addDialog.querySelector('[data-resource-group-add-confirm]'),
            cancels: [...addDialog.querySelectorAll('[data-resource-group-add-cancel]')]
        };

        let selectedTaskIds = [];
        let knownGroups = [];
        let busy = false;
        create.ready = add.ready = false;
        for (const view of [create, add]) view.update = window.MauiModalUI.watchAction(view.dialog, view.confirm,
            () => !busy && view.ready && selectedTaskIds.length > 0,
            () => Boolean(view.name?.value.trim()) && window.MauiModalUI.formValid(view.dialog));

        function normalizeIds(ids) {
            return [...new Set((ids || []).map(value => String(value || '').trim()).filter(Boolean))];
        }
        async function postJson(url, body) {
            const headers = new Headers({ Accept: 'application/json', 'Content-Type': 'application/json' });
            if (token) headers.set('RequestVerificationToken', token);
            const response = await fetch(url, { method: 'POST', headers, body: JSON.stringify(body) });
            if (response.status === 401) {
                window.location.replace(loginUrl);
                throw new Error('MOVEit session expired.');
            }
            const payload = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
            if (!response.ok) {
                const error = Object.assign(new Error(payload?.message || `Request failed (${response.status}).`), {payload});
                try {
                    if (await window.MauiObjectEditLease?.recoverConflict(error, 'Resource Groups', token))
                        error.message = window.MauiObjectEditLease.restoredFormMessage;
                } catch (recoveryError) { error.message = recoveryError.message; }
                throw error;
            }
            return payload;
        }
        function setError(view, message) {
            if (!view.error) return;
            view.error.textContent = message || '';
            view.error.hidden = !message;
        }
        function setLoading(view, value) {
            if (view.loading) view.loading.hidden = !value;
            if (view.content) view.content.hidden = value;
        }
        function renderItems(view, items) {
            if (!view.items) return;
            view.items.replaceChildren();
            const counts = new Map();
            (items || []).forEach(item => {
                counts.set(item.type, (counts.get(item.type) || 0) + 1);
                const row = document.createElement('tr');
                [item.type, item.name, item.id].forEach(value => {
                    const cell = document.createElement('td');
                    cell.textContent = String(value || '');
                    row.appendChild(cell);
                });
                view.items.appendChild(row);
            });
            const parts = [...counts.entries()].map(([type, count]) => `${count} ${type}${count === 1 ? '' : 's'}`);
            if (view.summary) view.summary.textContent = parts.length ? ` — ${parts.join(', ')}` : ' — No resources';
        }
        function setBusy(view, value, busyText, idleText) {
            busy = value;
            if (view.confirm) {
                view.confirm.textContent = value ? busyText : idleText;
            }
            view.cancels.forEach(button => button.disabled = value);
            if (view.name) view.name.disabled = value;
            if (view.description) view.description.disabled = value;
            view.update();
        }
        async function loadSetup(view) {
            setError(view, '');
            setLoading(view, true);
            view.ready = false;
            view.confirm.disabled = true;
            try {
                const result = await postJson(setupUrl, { taskIds: selectedTaskIds });
                knownGroups = result?.groups || [];
                renderItems(view, result?.items || []);
                if (view === add) {
                    view.name.replaceChildren();
                    knownGroups.forEach(group => {
                        const option = document.createElement('option');
                        option.value = group.name;
                        option.textContent = group.description ? `${group.name} — ${group.description}` : group.name;
                        view.name.appendChild(option);
                    });
                    if (!knownGroups.length) {
                        const option = document.createElement('option');
                        option.value = '';
                        option.textContent = 'No Resource Groups available';
                        view.name.appendChild(option);
                    }
                }
                setLoading(view, false);
                view.ready = true; view.update();
            } catch (error) {
                setLoading(view, false);
                setError(view, error?.message || 'Unable to load Resource Group information.');
            }
        }
        async function openCreate(ids) {
            selectedTaskIds = normalizeIds(ids);
            if (!selectedTaskIds.length) { status('Select at least one Task.', 5000, 'error'); return; }
            create.name.value = '';
            create.description.value = '';
            setError(create, '');
            create.dialog.showModal();
            await loadSetup(create);
            create.name.focus();
        }
        async function openAdd(ids) {
            selectedTaskIds = normalizeIds(ids);
            if (!selectedTaskIds.length) { status('Select at least one Task.', 5000, 'error'); return; }
            setError(add, '');
            add.dialog.showModal();
            await loadSetup(add);
        }
        async function submitCreate() {
            if (busy || !create.ready || create.confirm.disabled) return;
            const name = String(create.name?.value || '').trim();
            if (!name) { setError(create, 'Resource Group Name is required.'); create.name?.focus(); return; }
            if (knownGroups.some(group => String(group.name || '').trim().toLowerCase() === name.toLowerCase())) {
                setError(create, `A Resource Group named '${name}' already exists. Enter a unique Name.`); create.name?.focus(); return;
            }
            setError(create, '');
            setBusy(create, true, 'Creating...', 'Create Group');
            try {
                const result = await postJson(createUrl, { taskIds: selectedTaskIds, name, description: String(create.description?.value || '') });
                create.dialog.close();
                status(`Created Resource Group '${result.groupName}' with ${result.addedCount} resource${result.addedCount === 1 ? '' : 's'}.`, 6000, 'success');
                changed();
            } catch (error) { setError(create, error?.message || 'Unable to create Resource Group.'); }
            finally { setBusy(create, false, 'Creating...', 'Create Group'); }
        }
        async function submitAdd() {
            if (busy || !add.ready || add.confirm.disabled) return;
            const groupName = String(add.name?.value || '').trim();
            if (!groupName) { setError(add, 'Select a Resource Group.'); return; }
            setError(add, '');
            setBusy(add, true, 'Adding...', 'Add to Group');
            try {
                const result = await postJson(addUrl, { taskIds: selectedTaskIds, groupName });
                add.dialog.close();
                const already = Number(result.existingCount || 0);
                const added = Number(result.addedCount || 0);
                status(added > 0 ? `Added ${added} resource${added === 1 ? '' : 's'} to '${result.groupName}'.${already ? ` ${already} already belonged to the group.` : ''}` : `All selected resources already belong to '${result.groupName}'.`, 6000, 'success');
                changed();
            } catch (error) { setError(add, error?.message || 'Unable to update Resource Group.'); }
            finally { setBusy(add, false, 'Adding...', 'Add to Group'); }
        }

        create.confirm?.addEventListener('click', submitCreate);
        add.confirm?.addEventListener('click', submitAdd);

        create.cancels.forEach(button => button.addEventListener('click', () => { if (!busy) create.dialog.close(); }));
        add.cancels.forEach(button => button.addEventListener('click', () => { if (!busy) add.dialog.close(); }));
        [create.dialog, add.dialog].forEach(dialog => dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); }));

        return { openCreate, openAdd };
    }

    window.MauiResourceGroups = { initialize };
})();
