/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * This JavaScript file is part of the MAUI application.
 */

(() => {
    'use strict';

    const config = document.querySelector('[data-global-parameter-page-config]');
    if (!config) return;

    const grid = document.querySelector('[data-grid="globalparameters"]');
    const toast = grid?.querySelector('[data-prototype-toast]');
    const antiForgeryToken = document.querySelector('.task-antiforgery input[name="__RequestVerificationToken"]')?.value || '';

    // Purpose: Builds the URL for one controller endpoint.
    function endpoint(name) { return String(config.dataset[name] || ''); }
    // Purpose: Displays a transient workspace status message.
    function showStatus(message, duration = 5000, kind = 'default', origin = 'modal') {
        if (typeof window.mauiDisplayToast === 'function') { window.mauiDisplayToast(toast, message, kind, duration, origin); return; }
        if (!toast) return;
        toast.textContent = message || '';
        toast.classList.toggle('error', kind === 'error');
        toast.classList.add('visible');
        window.clearTimeout(showStatus.timer);
        showStatus.timer = window.setTimeout(() => toast.classList.remove('visible'), duration);
    }
    // Purpose: Calls a Global Parameter endpoint and normalizes JSON/error handling.
    async function fetchJson(url, options = {}) {
        const headers = new Headers(options.headers || {});
        headers.set('Accept', 'application/json');
        if (options.body != null) headers.set('Content-Type', 'application/json');
        if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
        const response = await (window.MauiMoveItGridConnection?.fetch || window.fetch.bind(window))(url, { ...options, headers });
        if (response.status === 401) {
            window.location.replace(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
            throw new Error('MOVEit session expired.');
        }
        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json') ? await response.json() : null;
        if (!response.ok) {
            const error = Object.assign(new Error(payload?.message || `Request failed (${response.status}).`), {status:response.status,payload});
            try {
                if (await window.MauiObjectEditLease?.recoverConflict(error, 'Global Parameters', antiForgeryToken))
                    error.message = window.MauiObjectEditLease.restoredFormMessage;
            } catch (recoveryError) { error.message = recoveryError.message; }
            throw error;
        }
        return payload || {};
    }
    // Purpose: Formats a date value for the current UI.
    function formatDate(value) {
        if (!value) return '—';
        const date = new Date(value);
        return Number.isNaN(date.valueOf()) ? '—' : date.toLocaleString();
    }

    // Creation stays separate; all existing Parameter edits live in Information.
    const newDialog = document.querySelector('[data-new-global-parameter-dialog]');
    const newForm = newDialog?.querySelector('[data-new-global-parameter-form]');
    const newName = newDialog?.querySelector('[data-new-global-parameter-name]');
    const newValue = newDialog?.querySelector('[data-new-global-parameter-value]');
    const newSecure = newDialog?.querySelector('[data-new-global-parameter-secure]');
    const newError = newDialog?.querySelector('[data-new-global-parameter-error]');
    const newSubmit = newDialog?.querySelector('[data-new-global-parameter-submit]');
    let newBusy = false, newUncertain = false;
    const updateNewAction = window.MauiModalUI.watchAction(newDialog, newSubmit,
        () => !newBusy && !newUncertain,
        () => Boolean(newName?.value.trim()) && newName.value.trim().length <= 254 && window.MauiModalUI.formValid(newDialog));
    function showNewError(message) { newError.textContent = message; newError.hidden = false; }
    function clearNewError() { newError.textContent = ''; newError.hidden = true; }
    function setNewBusy(busy) {
        newBusy = busy;
        newDialog?.querySelectorAll('input,button').forEach(control => control.disabled = busy);
        newSubmit.textContent = busy ? 'Creating…' : 'Create Parameter';
        updateNewAction();
    }
    function openNewParameter() {
        if (!newDialog || newDialog.open) return;
        newUncertain = false; newForm.reset(); newValue.type = 'text'; clearNewError(); setNewBusy(false);
        newDialog.showModal(); window.MauiModalUI?.track(newDialog); newName.focus();
    }
    async function closeNewParameter() {
        if (!newBusy && await window.MauiModalUI.confirmLeave(newDialog)) newDialog.close();
    }
    async function createParameter() {
        if (newBusy || newUncertain) return;
        const name = newName.value.trim();
        if (!name) { showNewError('Parameter Name is required.'); newName.focus(); return; }
        clearNewError(); setNewBusy(true);
        try {
            const result = await fetchJson(endpoint('createUrl'), { method: 'POST', body: JSON.stringify({ name, value: newValue.value, secure: newSecure.checked }) });
            if (result.success !== true) throw new Error(result.message || 'The Parameter creation could not be confirmed.');
            newDialog.close(); showStatus(`Global Parameter '${result.parameter?.name || name}' created.`);
            await refreshGrid(true);
        } catch (error) {
            newUncertain = Boolean(error.payload?.moveItSaved || !error.status);
            showNewError(error.message + (newUncertain ? ' Close and inspect Global Parameters before creating another copy.' : ''));
        } finally { setNewBusy(false); }
    }
    newSecure?.addEventListener('change', () => { newValue.type = newSecure.checked ? 'password' : 'text'; });
    newForm?.addEventListener('submit', event => { event.preventDefault(); void createParameter(); });
    newDialog?.querySelectorAll('[data-new-global-parameter-close]').forEach(button => button.addEventListener('click', closeNewParameter));
    newDialog?.addEventListener('cancel', event => { event.preventDefault(); void closeNewParameter(); });

    // ---------------------------------------------------------------------
    // Row action menu
    // ---------------------------------------------------------------------
    const rowMenu = document.querySelector('[data-global-parameter-row-action-menu]');
    let activeRowButton = null;

    // Purpose: Closes row menu.
    function closeRowMenu() {
        if (!rowMenu) return;
        rowMenu.hidden = true;
        activeRowButton?.setAttribute('aria-expanded', 'false');
        activeRowButton = null;
    }
    // Purpose: Opens row menu.
    function openRowMenu(button) {
        if (!rowMenu || !button) return;
        window.MauiObjectAccess?.reset(rowMenu);
        if (activeRowButton === button && !rowMenu.hidden) { closeRowMenu(); return; }
        activeRowButton?.setAttribute('aria-expanded', 'false');
        activeRowButton = button;
        button.setAttribute('aria-expanded', 'true');
        void window.MauiObjectAccess?.menu(rowMenu,'GlobalParameter',button.dataset.parameterName || button.dataset.rowId || '');
        rowMenu.hidden = false;
        const rect = button.getBoundingClientRect();
        const width = rowMenu.offsetWidth || 210;
        const height = rowMenu.offsetHeight || 100;
        rowMenu.style.left = `${Math.max(8, Math.min(window.innerWidth - width - 8, rect.right - width))}px`;
        rowMenu.style.top = `${Math.max(8, Math.min(window.innerHeight - height - 8, rect.bottom + 4))}px`;
    }
    document.addEventListener('click', event => {
        const button = event.target.closest('[data-global-parameter-row-menu-button]');
        if (button) { event.stopPropagation(); openRowMenu(button); return; }
        if (!event.target.closest('[data-global-parameter-row-action-menu]')) closeRowMenu();
    });
    window.addEventListener('scroll', closeRowMenu, true);
    window.addEventListener('resize', closeRowMenu);

    // ---------------------------------------------------------------------
    // Parameter Information owns Name, Value, security/reset and MAUI metadata edits.
    // ---------------------------------------------------------------------
    const detailsDialog = document.querySelector('[data-global-parameter-details-dialog]');
    const detailsLoading = detailsDialog?.querySelector('[data-global-parameter-details-loading]');
    const detailsContent = detailsDialog?.querySelector('[data-global-parameter-details-content]');
    const detailsError = detailsDialog?.querySelector('[data-global-parameter-details-error]');
    const detailsState = window.mauiCreateInformationDialogState({
        loadingElement: detailsLoading,
        contentElement: detailsContent,
        errorElement: detailsError,
        defaultErrorMessage: 'Unable to load Global Parameter information.'
    });
    const detailsName = detailsDialog?.querySelector('[data-global-parameter-name]');
    const detailsValue = detailsDialog?.querySelector('[data-global-parameter-value]');
    const detailsSecure = detailsDialog?.querySelector('[data-global-parameter-secure]');
    const detailsResourceGroups = detailsDialog?.querySelector('[data-global-parameter-resource-groups]');
    const metadataSection = detailsDialog?.querySelector('[data-global-parameter-metadata-section]');
    const metadataFields = detailsDialog?.querySelector('[data-global-parameter-metadata-fields]');
    const metadataAudit = detailsDialog?.querySelector('[data-global-parameter-metadata-audit]');
    const detailsSave = detailsDialog?.querySelector('[data-global-parameter-details-save]');
    const detailsCancel = detailsDialog?.querySelector('[data-global-parameter-details-cancel]');
    const detailsTabs = Array.from(detailsDialog?.querySelectorAll('[data-global-parameter-details-tab]') || []);
    let detailsBusy = false, detailsLoaded = false, detailsBlocked = false, detailsGeneration = 0;
    let detailsEditVersion = '', preserveExistingSecureValue = false;
    const detailsReadiness = window.MauiModalUI.registerSaveAction(detailsDialog, detailsSave,
        () => detailsName.value.trim().length > 0 && detailsName.value.trim().length <= 254);

    const detailsSecureNote = detailsDialog?.querySelector('[data-global-parameter-secure-existing-note]');
    const detailsResetValue = detailsDialog?.querySelector('[data-global-parameter-reset-value]');
    function applySecureFieldState() {
        detailsValue.type = detailsSecure.checked ? 'password' : 'text';
        detailsValue.disabled = detailsBusy || !detailsLoaded || preserveExistingSecureValue;
        detailsSecure.disabled = detailsBusy || !detailsLoaded || preserveExistingSecureValue;
        detailsSecureNote.hidden = !preserveExistingSecureValue;
        detailsResetValue.setAttribute('aria-disabled', String(detailsBusy));
    }
    function setDetailsBusy(busy) {
        detailsBusy = busy;
        detailsDialog.querySelectorAll('input,textarea,select,button').forEach(control => control.disabled = busy);
        detailsReadiness.block(detailsBlocked || !detailsLoaded);
        detailsSave.textContent = busy ? 'Saving…' : 'Save';
        applySecureFieldState();
        detailsReadiness.busy(busy);
    }
    detailsSecure?.addEventListener('change', applySecureFieldState);
    detailsResetValue?.addEventListener('click', event => {
        event.preventDefault();
        if (detailsBusy || !detailsLoaded || !preserveExistingSecureValue) return;
        preserveExistingSecureValue = false; detailsValue.value = ''; detailsSecure.checked = true;
        applySecureFieldState(); detailsReadiness.update(); detailsValue.focus();
    });
    function setDetailsTab(name) {
        detailsTabs.forEach(tab => { const selected = tab.dataset.globalParameterDetailsTab === name; tab.classList.toggle('active', selected); tab.setAttribute('aria-selected', String(selected)); });
        detailsDialog?.querySelectorAll('[data-global-parameter-details-panel]').forEach(panel => panel.hidden = panel.dataset.globalParameterDetailsPanel !== name);
        if (detailsSave) detailsSave.hidden = name === 'tasks';
    }
    detailsTabs.forEach(tab => tab.addEventListener('click', () => setDetailsTab(tab.dataset.globalParameterDetailsTab)));

    let activeDetailsParameterName = '';

    // Purpose: Sets details loading.
    function setDetailsLoading(loading) {
        detailsState.setLoading(loading);
    }
    // Purpose: Displays a details-dialog error.
    function showDetailsError(message) {
        detailsState.showError(message);
    }
    // Purpose: Clears the details-dialog error state.
    function hideDetailsError() {
        detailsState.hideError();
    }
    // Purpose: Creates metadata control.
    function createMetadataControl(field, value) {
        const type = String(field.type || 'String').toLowerCase();
        if (type === 'multilinestring' || type === 'multiline') {
            const control = document.createElement('textarea');
            control.className = 'text-input textarea-input';
            control.rows = 3;
            control.value = value;
            return control;
        }
        if (type === 'boolean' || type === 'list') {
            const select = document.createElement('select'); select.className = 'text-input';
            const options = type === 'boolean' ? ['false', 'true'] : field.possibleValues || [];
            options.forEach(option => select.appendChild(new Option(type === 'boolean' ? (option === 'true' ? 'True' : 'False') : option, option)));
            select.value = type === 'boolean' ? (String(value).toLowerCase() === 'true' ? 'true' : 'false') : value; return select;
        }
        const input = document.createElement('input');
        input.className = 'text-input';
        input.type = type === 'numeric' || type === 'number' ? 'number' : type === 'date' ? 'date' : 'text';
        input.value = value;
        return input;
    }
    // Purpose: Populates metadata.
    function populateMetadata(fields, values) {
        if (!metadataFields || !metadataSection) return;
        metadataFields.replaceChildren();
        const configured = Array.isArray(fields) ? fields : [];
        metadataSection.hidden = configured.length === 0;

        populateMetadataInto(metadataFields, configured, values);
    }
    function populateMetadataInto(target, fields, values) {
        if (!target) return;
        target.replaceChildren();
        const configured = Array.isArray(fields) ? fields : [];
        configured.forEach(field => {
            const name = String(field.name || '').trim();
            if (!name) return;
            const label = document.createElement('label');
            const type = String(field.type || '').toLowerCase();
            label.className = `field-block ${type.includes('multiline') ? 'full-span' : ''}`;
            const caption = document.createElement('span');
            caption.textContent = field.label || name;
            const control = createMetadataControl(field, String(values?.[name] ?? ''));
            window.mauiConfigureMetadataControl?.(control, field);
            control.dataset.globalParameterMetadataField = name;
            control.required = Boolean(field.required);
            label.append(caption, control);
            target.appendChild(label);
        });
    }
    // Information Task rows use the configured editor and retain their calling panel.
    function populateTasks(target, tasks) {
        if (!target) return;
        target.replaceChildren();
        const items = Array.isArray(tasks) ? tasks : [];
        if (!items.length) { const empty = document.createElement('div'); empty.className = 'host-tasks-empty'; empty.textContent = 'No visible Tasks currently reference this Global Parameter.'; target.append(empty); return; }
        const list = document.createElement('div'); list.className = 'host-tasks-list';
        const header = document.createElement('div'); header.className = 'host-tasks-header'; header.setAttribute('role', 'row');
        ['Task ID', 'Task Name'].forEach(text => { const cell = document.createElement('div'); cell.setAttribute('role', 'columnheader'); cell.textContent = text; header.append(cell); });
        list.append(header);
        items.forEach(task => {
            const row = document.createElement('div'); row.className = 'host-task-row'; row.setAttribute('role', 'row');
            const id = document.createElement('div'); id.textContent = task.id || ''; id.setAttribute('role', 'cell');
            const name = document.createElement('div'); name.setAttribute('role', 'cell');
            const link = document.createElement('button'); link.type = 'button'; link.className = 'row-primary-link'; link.textContent = task.name || task.id || 'Task';
            link.addEventListener('click', async () => { try { await window.MauiEditorRouting.openTask(task.id, task.name || ''); } catch (error) { window.MauiModalUI?.showError(error.message); } });
            name.append(link); row.append(id, name); list.append(row);
        });
        target.append(list);
    }

    // Purpose: Populates audit.
    function populateAudit(audit) {
        if (!metadataAudit) return;
        const rows = [
            ['Created On', formatDate(audit?.createdUtc)], ['Created By', audit?.createdBy || '—'],
            ['Modified On', formatDate(audit?.modifiedUtc)], ['Modified By', audit?.modifiedBy || '—']
        ];
        const fragment = document.createDocumentFragment();
        rows.forEach(([labelText, valueText]) => {
            const item = document.createElement('label'); item.className = 'audit-item';
            const label = document.createElement('span'); label.textContent = labelText;
            const value = document.createElement('input'); value.type = 'text'; value.className = 'text-input'; value.readOnly = true; value.value = valueText || '—';
            item.append(label, value); fragment.appendChild(item);
        });
        metadataAudit.replaceChildren(fragment);
    }
    // Purpose: Loads and displays Global Parameter details and metadata.
    async function openDetails(name, infoUrl = '') {
        if (document.documentElement.dataset.moveitAdmin !== 'true') return;
        window.MauiObjectAccess?.clearInformation(detailsDialog);
        const normalized = String(name || '').trim();
        if (!normalized || !detailsDialog) return;
        const generation = ++detailsGeneration;
        activeDetailsParameterName = normalized;
        detailsReadiness.reset();
        detailsLoaded = false; detailsBlocked = false; detailsEditVersion = ''; preserveExistingSecureValue = false;
        detailsSave.disabled = true;
        detailsTabs.forEach(tab => tab.disabled = true);
        setDetailsTab('information');
        hideDetailsError();
        setDetailsLoading(true);
        detailsDialog.showModal();
        try {
            const url = infoUrl || `${endpoint('detailsUrl')}?name=${encodeURIComponent(normalized)}`;
            const payload = await fetchJson(url, { method: 'GET', cache: 'no-store' });
            if (generation !== detailsGeneration || !detailsDialog.open) return;
            const parameter = payload.parameter || {};
            detailsEditVersion = String(payload.editVersion || '');
            if (!detailsEditVersion) throw new Error('The Parameter revision could not be loaded. Close and reopen Information before saving.');
            activeDetailsParameterName = String(parameter.name || normalized).trim() || normalized;
            if (detailsName) detailsName.value = activeDetailsParameterName;
            detailsDialog.querySelector('[data-global-parameter-identity]').textContent = activeDetailsParameterName;
            detailsSecure.checked = Boolean(parameter.secure);
            preserveExistingSecureValue = Boolean(parameter.secure);
            detailsValue.value = preserveExistingSecureValue ? '' : String(parameter.value ?? '');
            if (detailsResourceGroups) detailsResourceGroups.value = parameter.resourceGroups || '—';
            populateMetadata(payload.fields, payload.metadata);
            populateAudit(payload.audit);
            populateTasks(detailsDialog.querySelector('[data-global-parameter-details-task-content]'), payload.tasks);
            detailsLoaded = true;
            setDetailsBusy(false);
            detailsTabs.forEach(tab => tab.disabled = false);
            setDetailsLoading(false);
            window.MauiModalUI?.track(detailsDialog);
            await window.MauiObjectAccess?.information(detailsDialog,'GlobalParameter',detailsName.value);
        } catch (error) {
            if (generation === detailsGeneration && detailsDialog.open) showDetailsError(error.message);
        }
    }

    // Native fields and MAUI properties share the former editor's existing update endpoint.
    async function saveParameterDetails() {
        if (detailsBusy || !detailsLoaded || detailsBlocked || detailsSave.disabled || !activeDetailsParameterName) return;
        hideDetailsError();
        const name = detailsName.value.trim();
        if (!name) { showDetailsError('Parameter Name is required.'); detailsName.focus(); return; }
        if (window.mauiValidateMetadataControls && !window.mauiValidateMetadataControls(metadataFields)) return;
        const metadata = Object.fromEntries(Array.from(metadataFields.querySelectorAll('[data-global-parameter-metadata-field]')).map(control => [control.dataset.globalParameterMetadataField, control.value ?? '']));
        const payload = { editVersion: detailsEditVersion, originalName: activeDetailsParameterName, name,
            value: preserveExistingSecureValue ? '' : detailsValue.value, secure: detailsSecure.checked, preserveExistingSecureValue, metadata };
        setDetailsBusy(true);
        try {
            const result = await fetchJson(endpoint('saveDetailsUrl'), { method: 'POST', body: JSON.stringify(payload) });
            if (result.success !== true) throw new Error(result.message || 'The Parameter save could not be confirmed.');
            detailsDialog.close(); showStatus('Global Parameter information saved.'); await refreshGrid(true);
        } catch (error) {
            detailsBlocked = Boolean(error.payload?.editConflict || error.payload?.moveItSaved || !error.status || error.status >= 500);
            showDetailsError(error.message + (detailsBlocked ? ' Close and reopen Parameter Information before saving again.' : ''));
        } finally { setDetailsBusy(false); }
    }
    async function closeDetails() {
        if (!detailsBusy && (!detailsLoaded || await window.MauiModalUI.confirmLeave(detailsDialog))) detailsDialog.close();
    }
    detailsSave?.addEventListener('click', saveParameterDetails);
    detailsDialog?.querySelector('[data-global-parameter-details-form]')?.addEventListener('submit', event => { event.preventDefault(); void saveParameterDetails(); });
    detailsDialog?.querySelectorAll('[data-global-parameter-details-close], [data-global-parameter-details-cancel]').forEach(button => button.addEventListener('click', closeDetails));
    detailsDialog?.addEventListener('cancel', event => { event.preventDefault(); void closeDetails(); });
    detailsDialog?.addEventListener('close', () => { if (!detailsDialog.open) { detailsGeneration++; activeDetailsParameterName = ''; detailsLoaded = false; } });

    // ---------------------------------------------------------------------
    // Delete (individual + bulk)
    // ---------------------------------------------------------------------
    const deleteDialog = document.querySelector('[data-delete-global-parameter-dialog]');
    const deleteTitle = deleteDialog?.querySelector('[data-delete-global-parameter-title]');
    const deleteMessage = deleteDialog?.querySelector('[data-delete-global-parameter-message]');
    const deleteError = deleteDialog?.querySelector('[data-delete-global-parameter-error]');
    const deleteYes = deleteDialog?.querySelector('[data-delete-global-parameter-yes]');
    const deleteNoButtons = deleteDialog?.querySelectorAll('[data-delete-global-parameter-no]') || [];
    let pendingDeleteNames = [];
    let pendingDeleteIndividual = false;

    // Purpose: Displays delete error.
    function showDeleteError(message) {
        if (!deleteError) return;
        deleteError.textContent = message || 'Unable to delete the Global Parameter.';
        deleteError.hidden = false;
    }
    // Purpose: Clears delete error.
    function hideDeleteError() { if (deleteError) { deleteError.textContent = ''; deleteError.hidden = true; } }
    // Purpose: Runs delete preflight and opens the Global Parameter confirmation dialog.
    async function beginDelete(names, individual) {
        const normalized = [...new Set((names || []).map(name => String(name || '').trim()).filter(Boolean))];
        if (!normalized.length) { showStatus('Select at least one Global Parameter.'); return; }
        closeRowMenu();
        try {
            const url = individual ? endpoint('preflightDeleteUrl') : endpoint('preflightDeleteBulkUrl');
            const body = individual ? { name: normalized[0] } : { names: normalized };
            const result = await fetchJson(url, { method: 'POST', body: JSON.stringify(body) });
            if (!result.success) throw new Error(result.message || 'One or more parameters cannot be deleted.');
            pendingDeleteNames = normalized;
            pendingDeleteIndividual = individual;
            hideDeleteError();
            if (deleteTitle) deleteTitle.textContent = normalized.length === 1 ? 'Delete Parameter' : 'Delete Parameters';
            if (deleteMessage) deleteMessage.textContent = normalized.length === 1
                ? `Delete Global Parameter '${normalized[0]}'?`
                : `Delete ${normalized.length} selected Global Parameters?`;
            deleteDialog?.showModal();
            window.requestAnimationFrame(() => deleteNoButtons[0]?.focus());
        } catch (error) {
            showStatus(error.message, 5000, 'error');
        }
    }
    // Purpose: Deletes the confirmed Global Parameter selection.
    async function confirmDelete() {
        if (!pendingDeleteNames.length || !deleteYes) return;
        hideDeleteError();
        deleteYes.disabled = true;
        deleteNoButtons.forEach(button => button.disabled = true);
        try {
            const url = pendingDeleteIndividual ? endpoint('deleteUrl') : endpoint('deleteBulkUrl');
            const body = pendingDeleteIndividual ? { name: pendingDeleteNames[0] } : { names: pendingDeleteNames };
            const result = await fetchJson(url, { method: 'POST', body: JSON.stringify(body) });
            const requestedNames = [...pendingDeleteNames];
            const deletedNames = pendingDeleteIndividual
                ? (result?.success === true ? [String(result?.name || requestedNames[0] || '').trim()].filter(Boolean) : [])
                : (Array.isArray(result?.items) ? result.items.filter(item => item?.success === true).map(item => item?.name).filter(Boolean) : []);
            grid?.dispatchEvent(new CustomEvent('maui:grid-remove-rows', { detail: { rowIds: deletedNames } }));

            if (!result.success) {
                const deletedSet = new Set(deletedNames.map(name => String(name).toLowerCase()));
                pendingDeleteNames = pendingDeleteIndividual
                    ? requestedNames
                    : requestedNames.filter(name => !deletedSet.has(String(name).toLowerCase()));
                throw new Error(result.message || 'One or more parameters could not be deleted.');
            }

            const count = deletedNames.length;
            deleteDialog.close();
            showStatus(count === 1 ? 'Global Parameter deleted.' : `${count} Global Parameters deleted.`);
            pendingDeleteNames = [];
            await refreshGrid(true);
        } catch (error) {
            showDeleteError(error.message);
        } finally {
            deleteYes.disabled = false;
            deleteNoButtons.forEach(button => button.disabled = false);
        }
    }
    deleteDialog?.addEventListener('cancel', event => { if (deleteYes?.disabled) event.preventDefault(); else pendingDeleteNames = []; });
    deleteYes?.addEventListener('click', confirmDelete);
    deleteNoButtons.forEach(button => button.addEventListener('click', () => { pendingDeleteNames = []; deleteDialog?.close(); }));

    // ---------------------------------------------------------------------
    // Live grid refresh
    // ---------------------------------------------------------------------
    const refreshControl = document.querySelector('[data-global-parameter-refresh-control]');
    const uiRefreshSeconds = Math.max(0, Number(config.dataset.uiRefreshSeconds || 0));
    const globalParameterRefreshController = window.mauiCreateResourceRefreshController({
        grid,
        refreshControl,
        lastRefreshElement: refreshControl?.querySelector('[data-global-parameter-last-refresh]'),
        refreshClock: refreshControl?.querySelector('[data-global-parameter-refresh-clock]'),
        refreshToggle: refreshControl?.querySelector('[data-global-parameter-refresh-toggle]'),
        refreshNow: refreshControl?.querySelector('[data-global-parameter-refresh-now]'),
        intervalSeconds: uiRefreshSeconds,
        fetchJson,
        buildInitialUrl: () => {
            const url = new URL(endpoint('initialGridUrl'), window.location.href);
            url.searchParams.set('preferCached', 'true');
            return url;
        },
        buildRefreshUrl: forceRefresh => {
            const url = new URL(endpoint('refreshUrl'), window.location.href);
            url.searchParams.set('forceRefresh', forceRefresh ? 'true' : 'false');
            return url;
        },
        beforeRefresh: closeRowMenu,
        onInitialError: error => {
            console.warn('Initial Global Parameter grid load failed.');
            showStatus(`Global Parameter load failed: ${error.message}`, 5000, 'error', 'page');
        },
        onRefreshError: error => {
            console.warn('Global Parameter grid refresh failed.');
            showStatus(`Global Parameter refresh failed: ${error.message}`, 5000, 'error', 'page');
        }
    });

    function refreshGrid(forceRefresh) {
        return globalParameterRefreshController.refresh(forceRefresh);
    }

    // ---------------------------------------------------------------------
    // Event wiring
    // ---------------------------------------------------------------------
    document.addEventListener('maui:grid-action', event => {
        if (String(event.detail?.gridId || '').toLowerCase() !== 'globalparameters') return;
        const action = String(event.detail?.action || '').trim().toLowerCase();
        if (action === 'new parameter') { event.preventDefault(); openNewParameter(); return; }
        if (action === 'delete global parameters') { event.preventDefault(); beginDelete(event.detail?.selectedIds || [], false); }
    });

    document.addEventListener('click', event => {
        const editLink = event.target.closest('[data-global-parameter-information]');
        if (editLink) {
            event.preventDefault();
            openDetails(editLink.dataset.parameterName || editLink.dataset.rowId, editLink.dataset.parameterInfoUrl);
        }
    });

    rowMenu?.addEventListener('click', event => {
        const command = event.target.closest('[data-global-parameter-row-command]')?.dataset.globalParameterRowCommand;
        if (!command || !activeRowButton) return;
        const name = activeRowButton.dataset.parameterName || activeRowButton.dataset.rowId || '';
        const infoUrl = activeRowButton.dataset.parameterInfoUrl || '';
        closeRowMenu();
        if (command === 'information') openDetails(name, infoUrl);
        if (command === 'delete') beginDelete([name], true);
    });

    window.MauiModalUI?.register(newDialog, { error: showNewError, clear: clearNewError });
    window.MauiModalUI?.register(detailsDialog, { error: showDetailsError, clear: hideDetailsError });
    window.MauiModalUI?.registerContext('global-parameter-information', detailsDialog, {
        capture: () => ({ name: activeDetailsParameterName, tab: detailsTabs.find(tab => tab.classList.contains('active'))?.dataset.globalParameterDetailsTab }),
        prepare: () => !detailsBusy && window.MauiModalUI.confirmLeave(detailsDialog),
        restore: async state => { await openDetails(state.name); setDetailsTab(state.tab || 'information'); }
    });
    globalParameterRefreshController.loadInitial();
})();
