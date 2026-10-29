/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * This JavaScript is part of the MAUI application.
 */

(() => {
    'use strict';

    const config = document.querySelector('[data-host-page-config]');
    if (!config) return;

    const antiForgeryToken = document.querySelector('.task-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const grid = document.querySelector('[data-grid="hosts"]');
    const toast = grid?.querySelector('[data-prototype-toast]');
    const hostLicenseAlert = document.querySelector('[data-host-license-alert]');
    const hostLicenseTitle = document.querySelector('[data-host-license-title]');
    const hostLicenseMessage = document.querySelector('[data-host-license-message]');

    // Returns one endpoint value from the server-rendered Host page configuration.
    function endpoint(name) {
        return String(config.dataset[name] || '');
    }

    // Purpose: Navigates to a report while preserving the relevant return context.
    function navigateToReport(baseUrl, parameters) {
        const target = String(baseUrl || '').trim();
        if (!target) return;
        const url = new URL(target, window.location.href);
        Object.entries(parameters || {}).forEach(([key, value]) => {
            const text = String(value ?? '').trim();
            if (text) url.searchParams.set(key, text);
        });
        const returnUrl = window.MauiGridNavigationState?.createReturnUrl?.('hosts')
            || `${window.location.pathname}${window.location.search}${window.location.hash}`;
        url.searchParams.set('returnUrl', returnUrl);
        url.searchParams.set('returnLabel', 'Hosts');
        window.location.assign(url.toString());
    }

    // Displays transient Host-page feedback using the reusable grid toast surface.
    function showStatus(message, duration = 5000, kind = 'default', origin = 'modal') {
        if (typeof window.mauiDisplayToast === 'function') { window.mauiDisplayToast(toast, message, kind, duration, origin); return; }
        if (!toast) return;
        toast.textContent = message || '';
        toast.classList.toggle('error', kind === 'error');
        toast.classList.toggle('warning', kind === 'warning');
        toast.classList.add('visible');
        window.clearTimeout(showStatus.timer);
        showStatus.timer = window.setTimeout(() => toast.classList.remove('visible'), duration);
    }

    // Sends a JSON request with the page anti-forgery token and normalizes API errors.
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
        if (!response.ok) { const error = new Error(payload?.message || `Request failed (${response.status}).`); error.status = response.status; error.payload = payload; throw error; }
        return payload || {};
    }

    // Sends multipart form data with the ASP.NET antiforgery header without overriding the browser-generated boundary.
    async function fetchFormJson(url, formData) {
        const headers = new Headers({ 'Accept': 'application/json' });
        if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
        const response = await fetch(url, { method: 'POST', headers, body: formData });
        if (response.status === 401) {
            window.location.replace(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
            throw new Error('MOVEit session expired.');
        }
        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json') ? await response.json() : null;
        if (!response.ok) { const error = new Error(payload?.message || `Request failed (${response.status}).`); error.status = response.status; error.payload = payload; throw error; }
        return payload || {};
    }

    // Safely closes a popup reserved before asynchronous server work begins.
    function closeReservedPopup(popup) {
        if (!popup) return;
        try { popup.close(); } catch { /* ignored */ }
    }

    const nativePortability = window.MauiNativePortability?.create({
        objectType: 'Host',
        exportUrl: endpoint('exportHostUrl'),
        preflightUrl: endpoint('preflightImportHostUrl'),
        importUrl: endpoint('importHostUrl'),
        antiForgeryToken,
        loginUrl: endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'),
        status: showStatus,
        refresh: async () => { await refreshHostGrid(true); }
    });

    const hostSpreadsheetImport = window.MauiHostSpreadsheetImport?.create({
        nativePortability,
        rowLimit: Number(config.dataset.hostSheetRowLimit),
        previewUrl: endpoint('hostSheetPreviewUrl'),
        analyzeUrl: endpoint('hostSheetAnalyzeUrl'),
        importUrl: endpoint('hostSheetImportUrl'),
        finishUrl: endpoint('hostSheetFinishUrl'),
        antiForgeryToken,
        loginUrl: endpoint('loginUrl'),
        refresh: async () => refreshHostGrid(true)
    });

    const useMauiHostEditor = config.dataset.useMauiHostEditor === 'true';
    const hostActions = window.MauiHostActions.create({
        endpoint, fetchJson, status: showStatus, grid,
        useMauiHostEditor,
        openEditor: (...args) => launchHostEditor(...args),
        refresh: () => refreshHostGrid(true),
        afterTests: () => refreshHostGridAfterTests(),
        cloneLicenseLimited: () => hostCloneLicenseLimited,
        isBlocked: () => undoHostBusy || newHost.busy
    });
    const { openCloneHostDialog, openHostTestDialog, beginDeleteHost, beginDeleteHosts } = hostActions;

    const hostEditor = useMauiHostEditor && window.MauiHostEditor?.initialize
        ? window.MauiHostEditor.initialize({
            shellUrl: endpoint('hostEditorShellUrl'),
            saveUrl: endpoint('hostEditorSaveUrl'),
            fingerprintUrl: endpoint('hostEditorFingerprintUrl'),
            certificatesUrl: endpoint('hostEditorCertificatesUrl'),
            documentLibrariesUrl: endpoint('hostEditorDocumentLibrariesUrl'),
            loginUrl: endpoint('loginUrl'),
            antiForgeryToken,
            onTestHost: (host, leaseId) => openHostTestDialog([host], leaseId),
            onExportHost: host => nativePortability?.openExport([host?.id], [host?.name]),
            onDeleteHost: host => beginDeleteHost(host?.id),
            onCloneHost: host => openCloneHostDialog(host?.id, host?.name),
            onClosed: () => window.MauiEditorRouting?.returnToOrigin('host'),
            onSaved: () => refreshHostGrid(true)
        }) : null;

    async function launchHostEditor(hostId, hostName, popup = null, markEdited = true) {
        if (useMauiHostEditor) {
            closeReservedPopup(popup);
            if (!hostEditor?.open) { showStatus('The MAUI Host Editor is unavailable. Reload MAUI and try again.', 5000, 'error'); return false; }
            return hostEditor.open(hostId, hostName);
        }
        return launchNativeHostEditor(hostId, hostName, popup, markEdited);
    }
    window.MauiEditorRouting?.registerHostOpen((hostId, hostName) => launchHostEditor(hostId, hostName));

    // Native editor entry honors permission checks and maintains its separate saved-history workflow.
    async function launchNativeHostEditor(hostId, hostName, popup = null, markEdited = true, prepareUndo = true) {
        const normalizedHostId = String(hostId || '').trim();
        let normalizedHostName = String(hostName || '').trim();
        if (!normalizedHostId) return false;
        let permissionReadOnly=false;
        try { permissionReadOnly=window.MauiObjectAccess ? !(await window.MauiObjectAccess.read('Host',normalizedHostId)).canEdit : false; }
        catch(error) { showStatus(error.message || 'The object is unavailable.',5000,'error'); closeReservedPopup(popup); return false; }

        if (!window.MauiNativeEditor?.open || !window.MauiNativeEditor?.reserve) {
            showStatus('The native MOVEit editor launcher is unavailable.', 5000, 'error');
            closeReservedPopup(popup);
            return false;
        }

        // Reserve the popup before the server-side COM snapshot so the browser still treats the editor as user initiated.
        const reservedPopup = popup || window.MauiNativeEditor.reserve(`MAUI Host Editor - ${normalizedHostId}`);
        if (!reservedPopup || reservedPopup.closed) {
            showStatus('The MOVEit native editor popup was blocked by the browser.', 5000, 'error');
            return false;
        }

        if (prepareUndo && !permissionReadOnly) {
            try {
                const prepared = await fetchJson(endpoint('prepareHostEditUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ hostId: normalizedHostId })
                });
                normalizedHostName = String(prepared?.hostName || normalizedHostName || 'Host').trim();
                setHostUndoAvailable(normalizedHostId, true);
            } catch (error) {
                closeReservedPopup(reservedPopup);
                showStatus(`Host Editor: ${error?.message || 'Unable to save the Host undo snapshot.'}`, 5000, 'error');
                return false;
            }
        }

        const opened = await window.MauiNativeEditor.open({
            popup: reservedPopup,
            sessionUrl: endpoint('nativeEditorSessionUrl'),
            loginUrl: endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'),
            antiForgeryToken,
            returnHash: `#/hosts/${encodeURIComponent(normalizedHostId)}?HideNav`,
            windowTitle: `MAUI Host Editor - ${normalizedHostId}: ${normalizedHostName || 'Host'}`,
            skin: 'maui', disableUndo:permissionReadOnly,
            status: showStatus,
        });

        if (opened && markEdited && !permissionReadOnly) {
            try {
                await fetchJson(endpoint('markHostEditedUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ hostId: normalizedHostId })
                });
            } catch (error) {
                console.warn('MAUI could not update the Host modification audit information.');
            }
        }
        return opened;
    }

    // Updates the current Host row immediately after a snapshot is created so Undo Changes is available without waiting for refresh.
    function setHostUndoAvailable(hostId, available) {
        const normalizedHostId = String(hostId || '').trim();
        if (!normalizedHostId) return;
        document.querySelectorAll('[data-host-row-menu-button]').forEach(button => {
            if (String(button.dataset.rowId || '').trim() === normalizedHostId) {
                button.dataset.hostHasUndo = available ? 'true' : 'false';
            }
        });
    }

    // ---------------------------------------------------------------------
    // New Host
    // ---------------------------------------------------------------------
    const newHost = window.MauiNewHostDialog.create({
        createUrl: endpoint('createHostUrl'), fetchJson, useMauiHostEditor,
        status: showStatus,
        openEditor: (...args) => launchHostEditor(...args), refresh: force => refreshHostGrid(force)
    });
    function openNewHostDialog() { newHost.open(); }

    // ---------------------------------------------------------------------
    // Host row menu
    // ---------------------------------------------------------------------
    const hostRowActionMenu = document.querySelector('[data-host-row-action-menu]');
    const hostRowExportCommand = hostRowActionMenu?.querySelector('[data-host-row-command="export"]');
    const hostRowTestCommand = hostRowActionMenu?.querySelector('[data-host-row-command="test"]');
    const hostRowUndoCommand = hostRowActionMenu?.querySelector('[data-host-row-command="undo"]');
    const hostRowCloneCommand = hostRowActionMenu?.querySelector('[data-host-row-command="clone"]');
    let hostCloneLicenseLimited = false;
    let activeHostMenuContext = null;

    // Positions and opens the contextual Host row action menu.
    function openHostRowActionMenu(button) {
        if (!hostRowActionMenu || !button) return;
        if (activeHostMenuContext?.button === button && !hostRowActionMenu.hidden) {
            closeHostRowActionMenu();
            return;
        }

        closeHostRowActionMenu();
        window.MauiObjectAccess?.reset(hostRowActionMenu);
        activeHostMenuContext = {
            button,
            id: String(button.dataset.rowId || '').trim(),
            name: String(button.dataset.hostName || '').trim(),
            type: String(button.dataset.hostType || '').trim(),
            typeCode: String(button.dataset.hostTypeCode || '').trim(),
            destination: String(button.dataset.hostDestination || '').trim(),
            testSupported: button.dataset.hostTestSupported === 'true',
            infoUrl: String(button.dataset.hostInfoUrl || '').trim(),
            hasUndo: button.dataset.hostHasUndo === 'true'
        };
        const isLocalFileSystem = activeHostMenuContext.typeCode.toLowerCase() === 'filesystem'
            || activeHostMenuContext.type.toLowerCase() === 'local file system';
        if (hostRowCloneCommand) hostRowCloneCommand.disabled = isLocalFileSystem || hostCloneLicenseLimited;
        if (hostRowTestCommand) {
            hostRowTestCommand.hidden = !activeHostMenuContext.testSupported;
            hostRowTestCommand.disabled = !activeHostMenuContext.testSupported;
        }
        if (hostRowExportCommand) {
            hostRowExportCommand.hidden = isLocalFileSystem;
            hostRowExportCommand.disabled = isLocalFileSystem;
            hostRowExportCommand.title = isLocalFileSystem ? 'The Local File System Host is managed by MOVEit Automation and cannot be exported.' : '';
        }
        if (hostRowUndoCommand) {
            hostRowUndoCommand.hidden = false;
            hostRowUndoCommand.disabled = !activeHostMenuContext.hasUndo;
            hostRowUndoCommand.title = activeHostMenuContext.hasUndo ? '' : 'Undo is not available until MAUI has saved a prior Host configuration.';
        }
        button.setAttribute('aria-expanded', 'true');
        void window.MauiObjectAccess?.menu(hostRowActionMenu,'Host',activeHostMenuContext.id);
        hostRowActionMenu.hidden = false;
        hostRowActionMenu.style.visibility = 'hidden';

        const rect = button.getBoundingClientRect();
        const menuRect = hostRowActionMenu.getBoundingClientRect();
        let left = rect.left;
        let top = rect.bottom + 4;
        if (left + menuRect.width > window.innerWidth - 8) left = Math.max(8, window.innerWidth - menuRect.width - 8);
        if (top + menuRect.height > window.innerHeight - 8) top = Math.max(8, rect.top - menuRect.height - 4);
        hostRowActionMenu.style.left = `${Math.round(left)}px`;
        hostRowActionMenu.style.top = `${Math.round(top)}px`;
        hostRowActionMenu.style.visibility = '';
    }

    // Closes the Host row action menu and clears its active row context.
    function closeHostRowActionMenu() {
        activeHostMenuContext?.button?.setAttribute('aria-expanded', 'false');
        activeHostMenuContext = null;
        if (!hostRowActionMenu) return;
        hostRowActionMenu.hidden = true;
        hostRowActionMenu.style.left = '';
        hostRowActionMenu.style.top = '';
        hostRowActionMenu.style.visibility = '';
    }

    // Purpose: Copies a Host name to the system clipboard using the same Clipboard API/fallback pattern as Tasks.
    async function copyHostNameToClipboard(hostName) {
        const value = String(hostName || '').trim();
        if (!value) return;

        let clipboardError = null;
        if (navigator.clipboard?.writeText) {
            try {
                await navigator.clipboard.writeText(value);
                showStatus('Copied to clipboard.', 5000);
                return;
            } catch (error) {
                clipboardError = error;
            }
        }

        const textarea = document.createElement('textarea');
        textarea.value = value;
        textarea.setAttribute('readonly', '');
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        try {
            textarea.select();
            const copied = document.execCommand('copy');
            if (!copied) throw clipboardError || new Error('Clipboard copy was not accepted by the browser.');
            showStatus('Copied to clipboard.', 5000);
        } catch (error) {
            console.warn('MAUI could not copy the Host name to the clipboard.');
            showStatus('Unable to copy Host name to clipboard.', 5000, 'error');
        } finally {
            textarea.remove();
        }
    }

    // Dispatches implemented Host row commands; disabled placeholders never reach this function.
    function handleHostRowCommand(command) {
        const context = activeHostMenuContext;
        closeHostRowActionMenu();
        if (!context) return;
        if (command === 'information') { openHostDetailsById(context.id, context.infoUrl, context.type); return; }
        if (command === 'audit') { navigateToReport(endpoint('auditReportUrl'), { targetType: 'host', targetId: context.id, targetName: context.name }); return; }
        if (command === 'test') { openHostTestDialog([context]); return; }
        if (command === 'enable-assigned-tasks') { beginAssignedTaskStateChange(context.id, context.name, true); return; }
        if (command === 'disable-assigned-tasks') { beginAssignedTaskStateChange(context.id, context.name, false); return; }
        if (command === 'clone') { openCloneHostDialog(context.id, context.name); return; }
        if (command === 'copy-name') { copyHostNameToClipboard(context.name); return; }
        if (command === 'export') { nativePortability?.openExport([context.id], [context.name]); return; }
        if (command === 'edit') { launchHostEditor(context.id, context.name); return; }
        if (command === 'undo') { beginUndoHost(context.id, context.name); return; }
        if (command === 'delete') beginDeleteHost(context.id);
    }

    // ---------------------------------------------------------------------
    // Host connection testing
    // ---------------------------------------------------------------------
    // Reads the current grid's Host row metadata so single and bulk testing share one modal/workflow.
    function getHostTestContexts(hostIds) {
        const requested = new Set((hostIds || []).map(value => String(value || '').trim()).filter(Boolean));
        const contexts = [];
        document.querySelectorAll('[data-host-row-menu-button]').forEach(button => {
            const id = String(button.dataset.rowId || '').trim();
            if (!requested.has(id)) return;
            contexts.push({
                id,
                name: String(button.dataset.hostName || id).trim(),
                type: String(button.dataset.hostType || '').trim(),
                typeCode: String(button.dataset.hostTypeCode || '').trim(),
                destination: String(button.dataset.hostDestination || '').trim(),
                testSupported: button.dataset.hostTestSupported === 'true'
            });
        });
        return (hostIds || []).map(id => contexts.find(item => item.id === String(id || '').trim())).filter(Boolean);
    }

    // ---------------------------------------------------------------------
    // Enable / Disable Tasks assigned to one Host
    // ---------------------------------------------------------------------
    const assignedTaskStateDialog = document.querySelector('[data-host-assigned-task-state-dialog]');
    const assignedTaskStateTitle = assignedTaskStateDialog?.querySelector('[data-host-assigned-task-state-title]');
    const assignedTaskStateSubtitle = assignedTaskStateDialog?.querySelector('[data-host-assigned-task-state-subtitle]');
    const assignedTaskStateSummary = assignedTaskStateDialog?.querySelector('[data-host-assigned-task-state-summary]');
    const assignedTaskStateError = assignedTaskStateDialog?.querySelector('[data-host-assigned-task-state-error]');
    const assignedTaskStateList = assignedTaskStateDialog?.querySelector('[data-host-assigned-task-state-list]');
    const assignedTaskStateRows = assignedTaskStateDialog?.querySelector('[data-host-assigned-task-state-rows]');
    const assignedTaskStateConfirm = assignedTaskStateDialog?.querySelector('[data-host-assigned-task-state-confirm]');
    const assignedTaskStateCancelButtons = Array.from(assignedTaskStateDialog?.querySelectorAll('[data-host-assigned-task-state-cancel]') || []);
    let pendingAssignedTaskState = null;
    let assignedTaskStateBusy = false;

    // Loads the current Host assignment impact and opens the confirmation modal.
    async function beginAssignedTaskStateChange(hostId, hostName, enabled) {
        const id = String(hostId || '').trim();
        if (!id || assignedTaskStateBusy) return;

        assignedTaskStateBusy = true;
        try {
            const preview = await fetchJson(endpoint('preflightAssignedTaskStateUrl'), {
                method: 'POST',
                body: JSON.stringify({ hostId: id, enabled: Boolean(enabled) })
            });

            const tasks = Array.isArray(preview?.tasks) ? preview.tasks : [];
            const resolvedHostName = String(preview?.hostName || hostName || id).trim();
            const assigned = Number(preview?.assigned ?? tasks.length);
            const willChange = Number(preview?.willChange || 0);
            const alreadyInState = Number(preview?.alreadyInState || 0);
            const blocked = Number(preview?.blocked || 0);
            const verb = enabled ? 'Enable' : 'Disable';
            const stateWord = enabled ? 'enabled' : 'disabled';
            const oppositeWord = enabled ? 'disabled' : 'enabled';

            pendingAssignedTaskState = { hostId: id, hostName: resolvedHostName, enabled: Boolean(enabled), willChange };
            if (assignedTaskStateTitle) assignedTaskStateTitle.textContent = `${verb} Assigned Tasks`;
            if (assignedTaskStateSubtitle) assignedTaskStateSubtitle.textContent = `Review the Tasks assigned to Host "${resolvedHostName}" before continuing.`;
            if (assignedTaskStateError) { assignedTaskStateError.hidden = true; assignedTaskStateError.textContent = ''; }

            const summaryParts = [];
            if (assigned === 0) {
                summaryParts.push(`No Tasks are assigned to Host "${resolvedHostName}".`);
            } else {
                summaryParts.push(`${assigned} Task${assigned === 1 ? ' is' : 's are'} assigned to this Host.`);
                if (willChange) summaryParts.push(`${willChange} ${oppositeWord} Task${willChange === 1 ? '' : 's'} will be ${stateWord}.`);
                if (alreadyInState) summaryParts.push(`${alreadyInState} Task${alreadyInState === 1 ? ' is' : 's are'} already ${stateWord}.`);
                if (blocked) summaryParts.push(`${blocked} Task${blocked === 1 ? ' cannot' : 's cannot'} be ${stateWord}.`);
            }
            if (assignedTaskStateSummary) assignedTaskStateSummary.textContent = summaryParts.join(' ');

            renderAssignedTaskStateRows(tasks);
            if (assignedTaskStateList) assignedTaskStateList.hidden = assigned === 0;
            if (assignedTaskStateConfirm) {
                assignedTaskStateConfirm.hidden = assigned === 0;
                assignedTaskStateConfirm.disabled = willChange === 0;
                assignedTaskStateConfirm.textContent = `${verb} ${willChange} Task${willChange === 1 ? '' : 's'}`;
            }

            assignedTaskStateDialog?.showModal();
            window.requestAnimationFrame(() => assignedTaskStateCancelButtons.find(button => button.textContent.trim() === 'Cancel')?.focus());
        } catch (error) {
            showStatus(`${enabled ? 'Enable' : 'Disable'} Assigned Tasks: ${error?.message || 'Unable to load assigned Tasks.'}`, 5000, 'error');
        } finally {
            assignedTaskStateBusy = false;
        }
    }

    // Renders all assigned Tasks so the impact of the requested Host action is explicit.
    function renderAssignedTaskStateRows(tasks) {
        if (!assignedTaskStateRows) return;
        const fragment = document.createDocumentFragment();
        tasks.forEach(task => {
            const row = document.createElement('div');
            row.className = 'host-assigned-task-state-row';
            if (task?.willChange) row.classList.add('will-change');
            else if (task?.blocked) row.classList.add('is-blocked');
            else row.classList.add('is-unchanged');
            row.setAttribute('role', 'row');
            row.title = String(task?.message || '');

            const name = document.createElement('div');
            name.setAttribute('role', 'cell');
            name.textContent = String(task?.name || task?.id || 'Task');

            const description = document.createElement('div');
            description.setAttribute('role', 'cell');
            description.textContent = String(task?.description || '—');

            const status = document.createElement('div');
            status.setAttribute('role', 'cell');
            const statusValue = document.createElement('strong');
            statusValue.textContent = String(task?.status || 'Unknown');
            const note = document.createElement('span');
            note.className = 'host-assigned-task-state-note';
            note.textContent = String(task?.message || '');
            status.append(statusValue, note);

            row.append(name, description, status);
            fragment.appendChild(row);
        });
        assignedTaskStateRows.replaceChildren(fragment);
    }

    // Applies the requested state after the server re-resolves the Host's current Task assignments and states.
    async function confirmAssignedTaskStateChange() {
        if (assignedTaskStateBusy || !pendingAssignedTaskState?.hostId || pendingAssignedTaskState.willChange <= 0) return;
        assignedTaskStateBusy = true;
        if (assignedTaskStateConfirm) assignedTaskStateConfirm.disabled = true;
        assignedTaskStateCancelButtons.forEach(button => { button.disabled = true; });

        try {
            const result = await fetchJson(endpoint('setAssignedTasksEnabledUrl'), {
                method: 'POST',
                body: JSON.stringify({ hostId: pendingAssignedTaskState.hostId, enabled: pendingAssignedTaskState.enabled })
            });
            const verb = pendingAssignedTaskState.enabled ? 'Enable' : 'Disable';
            const succeeded = Number(result?.succeeded || 0);
            const newlyAlreadyInState = Number(result?.skipped || 0);
            const failed = Number(result?.failed || 0);
            const alreadyInState = Number(result?.alreadyInState || 0) + newlyAlreadyInState;
            const blocked = Number(result?.blocked || 0);
            const parts = [`${verb} Assigned Tasks: ${succeeded} changed`];
            if (alreadyInState) parts.push(`${alreadyInState} already ${pendingAssignedTaskState.enabled ? 'enabled' : 'disabled'}`);
            if (blocked) parts.push(`${blocked} blocked`);

            if (failed) parts.push(`${failed} failed`);
            const failures = Array.isArray(result?.items)
                ? result.items.filter(item => item && item.success === false).slice(0, 3)
                : [];
            if (failures.length) parts.push(failures.map(item => `${item.name || item.id}: ${item.message || 'Failed'}`).join(' | '));

            assignedTaskStateDialog?.close();
            pendingAssignedTaskState = null;
            showStatus(parts.join(', '), 5000);
            await refreshHostGrid(true);
        } catch (error) {
            if (assignedTaskStateError) {
                assignedTaskStateError.textContent = error?.message || 'Unable to update the assigned Tasks.';
                assignedTaskStateError.hidden = false;
            }
        } finally {
            assignedTaskStateBusy = false;
            if (assignedTaskStateConfirm) assignedTaskStateConfirm.disabled = !pendingAssignedTaskState || pendingAssignedTaskState.willChange <= 0;
            assignedTaskStateCancelButtons.forEach(button => { button.disabled = false; });
        }
    }

    function cancelAssignedTaskStateChange() {
        if (assignedTaskStateBusy) return;
        pendingAssignedTaskState = null;
        assignedTaskStateDialog?.close();
    }

    // ---------------------------------------------------------------------
    // Per-user three-version Host Undo
    // ---------------------------------------------------------------------
    const undoHostDialog = document.querySelector('[data-undo-host-dialog]');
    const undoHostMessage = undoHostDialog?.querySelector('[data-undo-host-message]');
    const undoHostHistory = undoHostDialog?.querySelector('[data-undo-host-history]');
    const undoHostError = undoHostDialog?.querySelector('[data-undo-host-error]');
    const undoHostYes = undoHostDialog?.querySelector('[data-undo-host-yes]');
    const undoHostNoButtons = Array.from(undoHostDialog?.querySelectorAll('[data-undo-host-no]') || []);
    let pendingUndoHost = null;
    let undoHostBusy = false;

    // Purpose: Formats undo snapshot date.
    function formatUndoSnapshotDate(value) {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? String(value || '') : date.toLocaleString();
    }

    // Purpose: Loads Host undo history and opens the restore dialog.
    async function beginUndoHost(hostId, hostName) {
        const id = String(hostId || '').trim();
        if (!id || undoHostBusy) return;
        undoHostBusy = true;
        try {
            const preflight = await fetchJson(endpoint('preflightUndoHostUrl'), {
                method: 'POST',
                body: JSON.stringify({ hostId: id })
            });

            const snapshots = Array.isArray(preflight?.snapshots) ? preflight.snapshots : [];
            if (!preflight?.success || snapshots.length === 0) {
                showStatus(preflight?.message || 'Undo is not available for this Host.', 5000);
                return;
            }

            const name = String(preflight?.hostName || hostName || id).trim();
            if (undoHostHistory) {
                undoHostHistory.replaceChildren();
                snapshots.forEach((snapshot, index) => {
                    const canRestore = snapshot?.canRestore !== false;
                    const row = document.createElement('label');
                    row.className = `undo-history-option${canRestore ? '' : ' is-disabled'}`;
                    const radio = document.createElement('input');
                    radio.type = 'radio'; radio.name = 'hostUndoSnapshot'; radio.value = String(snapshot?.snapshotId || ''); radio.disabled = !canRestore;
                    const body = document.createElement('span'); body.className = 'undo-history-option-main';
                    const date = document.createElement('span'); date.className = 'undo-history-option-date'; date.textContent = formatUndoSnapshotDate(snapshot?.snapshotCreatedUtc);
                    const meta = document.createElement('span'); meta.className = 'undo-history-option-meta'; meta.textContent = `${index === 0 ? 'Most recent' : `Version ${index + 1}`} · Saved by ${String(snapshot?.snapshotCreatedBy || 'current user')}`;
                    body.append(date, meta);
                    const warningText = String(snapshot?.message || '').trim();
                    if (warningText) { const warning = document.createElement('span'); warning.className = 'undo-history-option-warning'; warning.textContent = warningText; body.append(warning); }
                    row.append(radio, body); undoHostHistory.append(row);
                });
            }
            const first = undoHostHistory?.querySelector('input[type="radio"]:not(:disabled)');
            if (first) first.checked = true;
            const snapshotId = first?.value || '';
            pendingUndoHost = { hostId: id, hostName: name, snapshotId };
            undoHostHistory?.querySelectorAll('input[type="radio"]').forEach(radio => radio.addEventListener('change', () => { pendingUndoHost.snapshotId = radio.value; if (undoHostYes) undoHostYes.disabled = false; }));
            if (undoHostYes) undoHostYes.disabled = !snapshotId;
            if (undoHostMessage) undoHostMessage.textContent = `Select the version of Host "${name}" to restore.`;
            if (undoHostError) {
                undoHostError.hidden = true;
                undoHostError.textContent = '';
            }
            undoHostDialog?.showModal();
            window.requestAnimationFrame(() => undoHostNoButtons.find(button => button.textContent.trim() === 'Cancel')?.focus());
        } catch (error) {
            showStatus(`Undo Changes: ${error?.message || 'Unable to validate the Host undo snapshot.'}`, 5000, 'error');
        } finally {
            undoHostBusy = false;
        }
    }

    // Purpose: Restores the selected Host undo snapshot.
    async function confirmUndoHost() {
        if (undoHostBusy || !pendingUndoHost?.hostId || !pendingUndoHost?.snapshotId) return;
        if (!useMauiHostEditor && !window.MauiNativeEditor?.reserve) {
            if (undoHostError) {
                undoHostError.textContent = 'The native MOVEit editor launcher is unavailable.';
                undoHostError.hidden = false;
            }
            return;
        }
        const reservedPopup = useMauiHostEditor ? null : window.MauiNativeEditor.reserve(`MAUI Host Editor - ${pendingUndoHost.hostId}`);
        if (!useMauiHostEditor && !reservedPopup) {
            if (undoHostError) {
                undoHostError.textContent = 'The MOVEit native editor popup was blocked by the browser.';
                undoHostError.hidden = false;
            }
            return;
        }

        undoHostBusy = true;
        if (undoHostYes) undoHostYes.disabled = true;
        undoHostNoButtons.forEach(button => { button.disabled = true; });
        try {
            const result = await fetchJson(endpoint('undoHostUrl'), {
                method: 'POST',
                body: JSON.stringify({ hostId: pendingUndoHost.hostId, snapshotId: pendingUndoHost.snapshotId })
            });
            const restoredHostId = String(result?.hostId || pendingUndoHost.hostId).trim();
            const restoredHostName = String(result?.hostName || pendingUndoHost.hostName || restoredHostId).trim();
            const warning = String(result?.warning || '').trim();
            undoHostDialog?.close();
            pendingUndoHost = null;

            if (warning) showStatus(`Undo Changes: ${warning}`, 5000);
            else showStatus(`Undo Changes: restored Host ${restoredHostId}.`, 5000);

            const opened = await launchHostEditor(restoredHostId, restoredHostName, reservedPopup);
            if (!opened) closeReservedPopup(reservedPopup);
            await refreshHostGrid(true);
        } catch (error) {
            closeReservedPopup(reservedPopup);
            if (undoHostError) {
                undoHostError.textContent = error?.message || 'Unable to restore the Host undo snapshot.';
                undoHostError.hidden = false;
            }
        } finally {
            undoHostBusy = false;
            if (undoHostYes) undoHostYes.disabled = false;
            undoHostNoButtons.forEach(button => { button.disabled = false; });
        }
    }

    // Purpose: Cancels undo host.
    function cancelUndoHost() {
        if (undoHostBusy) return;
        pendingUndoHost = null;
        undoHostDialog?.close();
    }

    // ---------------------------------------------------------------------
    // Delete Host
    // ---------------------------------------------------------------------
    // ---------------------------------------------------------------------
    // Host Information
    // ---------------------------------------------------------------------
    const detailsDialog = document.querySelector('[data-host-details-dialog]');
    const detailsLoading = detailsDialog?.querySelector('[data-host-details-loading]');
    const detailsContent = detailsDialog?.querySelector('[data-host-details-content]');
    const detailsError = detailsDialog?.querySelector('[data-host-details-error]');
    const detailsState = window.mauiCreateInformationDialogState({
        loadingElement: detailsLoading,
        contentElement: detailsContent,
        errorElement: detailsError,
        defaultErrorMessage: 'Unable to load Host information.'
    });
    const detailsSave = detailsDialog?.querySelector('[data-host-details-save]');
    const detailsCancel = detailsDialog?.querySelector('[data-host-details-cancel]');
    const detailsTabs = Array.from(detailsDialog?.querySelectorAll('[data-host-details-tab]') || []);
    const informationPanel = detailsDialog?.querySelector('[data-host-details-panel="information"]');
    const tasksPanel = detailsDialog?.querySelector('[data-host-details-panel="tasks"]');
    const metadataFields = detailsDialog?.querySelector('[data-host-metadata-fields]');
    const metadataAudit = detailsDialog?.querySelector('[data-host-metadata-audit]');
    const hostTasksRows = detailsDialog?.querySelector('[data-host-tasks-rows]');
    const hostTasksList = detailsDialog?.querySelector('[data-host-tasks-list]');
    const hostTasksEmpty = detailsDialog?.querySelector('[data-host-tasks-empty]');
    let activeHostId = '';
    let activeInfoUrl = '';
    let canEditMoveItFields = false;
    let hostInformationSaveUncertain = false;
    const detailsReadiness = window.MauiModalUI.registerSaveAction(detailsDialog, detailsSave, () => {
        const name = detailsDialog.querySelector('[data-host-name]');
        return !name || name.disabled || name.readOnly || name.value.trim().length > 0 && name.value.trim().length <= 254;
    });

    // Loads Host Information from the server and opens the modal.
    async function openHostDetailsById(hostId, infoUrl, knownHostType = '') {
        if (!detailsDialog || !hostId || !infoUrl) return;
        activeHostId = String(hostId).trim();
        activeInfoUrl = infoUrl;
        resetHostDetailsDialog();
        detailsDialog.showModal();
        try {
            const data = await fetchJson(infoUrl);
            populateHostDetails(data, knownHostType);
            await window.MauiObjectAccess?.information(detailsDialog,'Host',activeHostId);
            window.MauiModalUI?.track(detailsDialog);
        } catch (error) {
            showDetailsError(error.message);
        }
    }

    // Resets Host Information state before another Host is loaded.
    function resetHostDetailsDialog() {
        window.MauiObjectAccess?.clearInformation(detailsDialog);
        if (!detailsDialog) return;
        detailsReadiness.reset();
        hostInformationSaveUncertain = false;
        detailsState.reset();
        detailsSave.disabled = true;
        canEditMoveItFields = false;
        metadataFields?.replaceChildren();
        metadataAudit?.replaceChildren();
        hostTasksRows?.replaceChildren();
        if (hostTasksEmpty) hostTasksEmpty.hidden = true;
        if (hostTasksList) hostTasksList.hidden = true;
        setHostDetailsTab('information');
    }

    // Populates common MOVEit Host information plus MAUI metadata and visible Task dependencies.
    function populateHostDetails(data, knownHostType = '') {
        detailsDialog.dataset.editVersion = String(data.editVersion || '');
        const host = data.host || {};
        // The Hosts inventory carries MOVEit's polymorphic Host type wrapper, while the direct
        // single-Host response may omit it. Prefer the already-normalized type from the selected
        // grid row so Host Information stays consistent with the Hosts view.
        const displayHostType = String(knownHostType || '').trim() || String(host.type || '').trim();
        detailsDialog.querySelector('[data-host-type]').textContent = displayHostType || '—';
        detailsDialog.querySelector('[data-host-id]').textContent = host.id || activeHostId;
        canEditMoveItFields = Boolean(host.canEditMoveItFields);
        const hostNameInput = detailsDialog.querySelector('[data-host-name]');
        const hostDescriptionInput = detailsDialog.querySelector('[data-host-description]');
        if (hostNameInput) { hostNameInput.value = host.name || ''; hostNameInput.disabled = !canEditMoveItFields || host.canEditName === false; }
        if (hostDescriptionInput) { hostDescriptionInput.value = host.description || ''; hostDescriptionInput.disabled = !canEditMoveItFields; }
        const resourceGroupsInput = detailsDialog.querySelector('[data-host-resource-groups]');
        if (resourceGroupsInput) resourceGroupsInput.value = host.resourceGroups || '—';
        const editNote = detailsDialog.querySelector('[data-host-edit-note]');
        if (editNote) {
            const readOnlyReason = String(host.moveItFieldsReadOnlyReason || '').trim();
            editNote.hidden = canEditMoveItFields || !readOnlyReason;
            editNote.textContent = canEditMoveItFields ? '' : readOnlyReason;
        }
        populateMetadataFields(data.fields || [], data.metadata || {});
        populateAudit(data.audit || {});
        populateHostTasks(data.tasks || []);
        detailsState.showContent();
        detailsSave.disabled = false;
    }

    // Switches the Host Information modal between Information and Tasks tabs.
    function setHostDetailsTab(tabName) {
        const showTasks = tabName === 'tasks';
        detailsTabs.forEach(tab => {
            const selected = tab.dataset.hostDetailsTab === tabName;
            tab.classList.toggle('active', selected);
            tab.setAttribute('aria-selected', selected ? 'true' : 'false');
        });
        if (informationPanel) informationPanel.hidden = showTasks;
        if (tasksPanel) tasksPanel.hidden = !showTasks;
        if (detailsSave) detailsSave.hidden = showTasks;
    }

    // Builds controls for the configured Host metadata fields.
    function populateMetadataFields(fields, values) {
        if (!metadataFields) return;
        const fragment = document.createDocumentFragment();
        fields.forEach(field => {
            const label = document.createElement('label');
            label.className = `field-block ${String(field.type || '').toLowerCase().includes('multiline') ? 'full-span' : ''}`;
            const caption = document.createElement('span');
            caption.textContent = field.label || field.name;
            if (field.description) caption.title = field.description;
            label.appendChild(caption);
            const control = createMetadataControl(field, values[field.name] ?? '');
            window.mauiConfigureMetadataControl?.(control, field);
            control.dataset.hostMetadataField = field.name;
            control.required = Boolean(field.required);
            label.appendChild(control);
            fragment.appendChild(label);
        });
        metadataFields.replaceChildren(fragment);
    }

    // Creates one metadata editor using the configured metadata field type.
    function createMetadataControl(field, value) {
        const type = String(field.type || 'String').toLowerCase();
        if (type === 'multilinestring' || type === 'multiline') {
            const textarea = document.createElement('textarea');
            textarea.className = 'text-input textarea-input';
            textarea.rows = 4;
            textarea.value = value;
            return textarea;
        }
        if (type === 'boolean') {
            const select = document.createElement('select');
            select.className = 'text-input';
            select.append(new Option('False', 'false'), new Option('True', 'true'));
            select.value = String(value).toLowerCase() === 'true' ? 'true' : 'false';
            return select;
        }
        if (type === 'list') {
            const select = document.createElement('select');
            select.className = 'text-input';
            (field.possibleValues || []).forEach(optionValue => select.appendChild(new Option(optionValue, optionValue)));
            select.value = value;
            return select;
        }
        const input = document.createElement('input');
        input.className = 'text-input';
        input.type = type === 'numeric' || type === 'number' ? 'number' : type === 'date' ? 'date' : 'text';
        input.value = value;
        return input;
    }

    // Renders the Host metadata Created/Modified audit values.
    function populateAudit(audit) {
        if (!metadataAudit) return;
        const rows = [
            ['Created On', formatDate(audit.createdUtc)],
            ['Created By', audit.createdBy || '—'],
            ['Modified On', formatDate(audit.modifiedUtc)],
            ['Modified By', audit.modifiedBy || '—']
        ];
        const fragment = document.createDocumentFragment();
        rows.forEach(([labelText, valueText]) => {
            const item = document.createElement('label');
            item.className = 'audit-item';
            const label = document.createElement('span');
            label.textContent = labelText;
            const value = document.createElement('input'); value.type = 'text'; value.className = 'text-input'; value.readOnly = true;
            value.value = valueText || '—';
            item.append(label, value);
            fragment.appendChild(item);
        });
        metadataAudit.replaceChildren(fragment);
    }

    // Renders visible Task dependencies and makes each Task Name a native editor link.
    function populateHostTasks(tasks) {
        hostTasksRows?.replaceChildren();
        if (!Array.isArray(tasks) || tasks.length === 0) {
            if (hostTasksEmpty) hostTasksEmpty.hidden = false;
            if (hostTasksList) hostTasksList.hidden = true;
            return;
        }

        const fragment = document.createDocumentFragment();
        tasks.forEach(task => {
            const row = document.createElement('div');
            row.className = 'host-task-row';
            row.setAttribute('role', 'row');
            const id = document.createElement('div');
            id.setAttribute('role', 'cell');
            id.textContent = task.id || '';
            const name = document.createElement('div');
            name.setAttribute('role', 'cell');
            const link = document.createElement('button');
            link.type = 'button';
            link.className = 'row-primary-link';
            link.dataset.hostTaskEditor = 'true';
            link.dataset.taskId = task.id || '';
            link.dataset.taskName = task.name || '';
            link.textContent = task.name || task.id || 'Task';
            name.appendChild(link);
            row.append(id, name);
            fragment.appendChild(row);
        });
        hostTasksRows?.replaceChildren(fragment);
        if (hostTasksEmpty) hostTasksEmpty.hidden = true;
        if (hostTasksList) hostTasksList.hidden = false;
    }

    // Saves editable common MOVEit Host fields plus MAUI metadata without resetting the grid view.
    async function saveHostDetails() {
        if (!activeHostId || !metadataFields || hostInformationSaveUncertain || detailsSave.disabled) return;
        hideDetailsError();

        const hostNameInput = detailsDialog.querySelector('[data-host-name]');
        const hostName = String(hostNameInput?.value || '').trim();
        if (canEditMoveItFields && !hostName) {
            showDetailsError('Host Name is required.');
            hostNameInput?.focus();
            return;
        }
        if (canEditMoveItFields && hostName.length > 254) {
            showDetailsError('Host Name cannot exceed 254 characters.');
            hostNameInput?.focus();
            return;
        }

        if (window.mauiValidateMetadataControls && !window.mauiValidateMetadataControls(metadataFields)) return;
        detailsReadiness.busy(true);
        const metadata = {};
        metadataFields.querySelectorAll('[data-host-metadata-field]').forEach(control => {
            metadata[control.dataset.hostMetadataField] = control.value ?? '';
        });

        try {
            await fetchJson(`${endpoint('saveDetailsUrl')}?id=${encodeURIComponent(activeHostId)}`, {
                method: 'POST',
                body: JSON.stringify({
                    editVersion: detailsDialog.dataset.editVersion || '',
                    name: hostName,
                    description: detailsDialog.querySelector('[data-host-description]')?.value || '',
                    updateMoveItFields: canEditMoveItFields,
                    metadata
                })
            });
            detailsDialog.close();
            showStatus('Host information saved.', 5000);
            await refreshHostGrid(true);
        } catch (error) {
            hostInformationSaveUncertain = Boolean(error?.payload?.editConflict || error?.payload?.moveItSaved || error?.payload?.outcomeUnknown || !error?.status || error.status >= 500);
            showDetailsError((error?.message || 'Unable to save Host Information.')
                + (hostInformationSaveUncertain ? ' Close and reopen Host Information before saving again.' : ''));
            detailsReadiness.block(hostInformationSaveUncertain); detailsReadiness.busy(false);
        }
    }

    // Shows a Host Information error while keeping the dialog open.
    function showDetailsError(message) {
        detailsState.showError(message);
    }

    // Clears the Host Information error surface.
    function hideDetailsError() {
        detailsState.hideError();
    }

    // Formats an ISO audit timestamp using the user's local browser settings.
    function formatDate(value) {
        if (!value) return '—';
        const date = new Date(value);
        return Number.isNaN(date.valueOf()) ? '—' : date.toLocaleString();
    }

    // ---------------------------------------------------------------------
    // Live Host grid refresh
    // ---------------------------------------------------------------------
    const refreshControl = document.querySelector('[data-host-refresh-control]');
    const uiHostRefreshSeconds = Math.max(0, Number(config.dataset.uiHostRefreshSeconds || 0));

    // Builds the Host grid request URL, preserving the active Host type filter.
    function buildHostGridRequestUrl(endpointName) {
        const url = new URL(endpoint(endpointName), window.location.href);
        url.searchParams.set('filter', config.dataset.activeFilter || 'all');
        return url;
    }

    // Renders Host license capacity alongside the shared grid refresh lifecycle.
    function renderHostLicenseCapacity(advisory) {
        const current = Number(advisory?.current ?? -1);
        const maximum = Number(advisory?.maximum ?? -1);
        const hostLicenseLimitReached = Number.isFinite(current) && Number.isFinite(maximum) && maximum > 0 && current >= maximum;
        hostCloneLicenseLimited = hostLicenseLimitReached;
        document.querySelectorAll('[data-grid-action="New Host"]').forEach(button => {
            button.disabled = hostLicenseLimitReached;
            button.title = hostLicenseLimitReached ? 'The MOVEit Host license limit has been reached.' : '';
        });
        if (!hostLicenseAlert) return;
        hostLicenseAlert.classList.remove('is-warning', 'is-danger');
        if (!advisory || !advisory.message) {
            hostLicenseAlert.hidden = true;
            return;
        }
        const severity = String(advisory.severity || '').toLowerCase();
        hostLicenseAlert.classList.add(severity === 'danger' ? 'is-danger' : 'is-warning');
        if (hostLicenseTitle) hostLicenseTitle.textContent = severity === 'danger' ? 'License Warning:' : 'Capacity Warning:';
        if (hostLicenseMessage) hostLicenseMessage.textContent = advisory.message;
        hostLicenseAlert.hidden = false;
    }

    const hostRefreshController = window.mauiCreateResourceRefreshController({
        grid,
        refreshControl,
        lastRefreshElement: refreshControl?.querySelector('[data-host-last-refresh]'),
        refreshClock: refreshControl?.querySelector('[data-host-refresh-clock]'),
        refreshToggle: refreshControl?.querySelector('[data-host-refresh-toggle]'),
        refreshNow: refreshControl?.querySelector('[data-host-refresh-now]'),
        intervalSeconds: uiHostRefreshSeconds,
        fetchJson,
        buildInitialUrl: () => {
            const url = buildHostGridRequestUrl('initialHostGridUrl');
            url.searchParams.set('preferCached', 'true');
            return url;
        },
        buildRefreshUrl: forceRefresh => {
            const url = buildHostGridRequestUrl('refreshHostGridUrl');
            url.searchParams.set('forceRefresh', forceRefresh ? 'true' : 'false');
            return url;
        },
        beforeRefresh: closeHostRowActionMenu,
        deferAutomaticRefresh: () => Boolean(document.querySelector('[data-host-test-dialog][aria-busy="true"]')),
        onPayload: payload => renderHostLicenseCapacity(payload?.licenseCapacity),
        onInitialError: error => {
            console.warn('Initial Host grid load failed.');
            showStatus(`Host load failed: ${error.message}`, 5000, 'error', 'page');
        },
        onRefreshError: error => {
            console.warn('Host grid refresh failed.');
            showStatus(`Host refresh failed: ${error.message}`, 5000, 'error', 'page');
        },
        pausedTitle: 'Host refresh paused',
        activeTitle: 'Host refresh options',
        persistLastRefresh: true,
        setAriaBusy: true
    });

    // Preserves the established Host refresh function used by create/import/delete/metadata flows.
    function refreshHostGrid(forceRefresh) {
        return hostRefreshController.refresh(forceRefresh);
    }

    // Refreshes persisted Health values after interactive tests without racing an in-progress grid refresh.
    function refreshHostGridAfterTests() {
        hostRefreshController.refreshWhenIdle(false, { maxAttempts: 20, delayMs: 250 });
    }

    hostRefreshController.loadInitial();

    // ---------------------------------------------------------------------
    // Event wiring
    // ---------------------------------------------------------------------
    document.addEventListener('maui:grid-action', event => {
        const gridId = String(event.detail?.gridId || '').trim().toLowerCase();
        const actionName = String(event.detail?.action || '').trim().toLowerCase();
        if (gridId !== 'hosts') return;
        if (actionName === 'new host') {
            event.preventDefault();
            openNewHostDialog();
            return;
        }
        if (actionName === 'import hosts' || actionName === 'import host') {
            event.preventDefault();
            hostSpreadsheetImport?.open();
            return;
        }
        if (actionName === 'test hosts') {
            event.preventDefault();
            openHostTestDialog(getHostTestContexts(event.detail?.selectedIds || []));
            return;
        }
        if (actionName === 'export hosts' || actionName === 'export selected hosts') {
            event.preventDefault();
            nativePortability?.openExport(event.detail?.selectedIds || [], []);
            return;
        }
        if (actionName === 'delete hosts') {
            event.preventDefault();
            beginDeleteHosts(event.detail?.selectedIds || []);
        }
    });

    document.addEventListener('click', event => {
        const menuButton = event.target.closest('[data-host-row-menu-button]');
        if (menuButton) {
            event.preventDefault();
            event.stopPropagation();
            openHostRowActionMenu(menuButton);
            return;
        }

        const menuCommand = event.target.closest('[data-host-row-command]');
        if (menuCommand && hostRowActionMenu?.contains(menuCommand)) {
            event.preventDefault();
            handleHostRowCommand(String(menuCommand.dataset.hostRowCommand || ''));
            return;
        }

        const hostEditor = event.target.closest('[data-native-host-editor]');
        if (hostEditor) {
            event.preventDefault();
            closeHostRowActionMenu();
            launchHostEditor(hostEditor.dataset.rowId, hostEditor.dataset.hostName || hostEditor.textContent || '');
            return;
        }

        const taskEditor = event.target.closest('[data-host-task-editor]');
        if (taskEditor) {
            event.preventDefault();
            if (window.MauiEditorRouting?.openTask) window.MauiEditorRouting.openTask(taskEditor.dataset.taskId, taskEditor.dataset.taskName || taskEditor.textContent || '', taskEditor.dataset.taskType || '');
            else showStatus('The Task Editor launcher is unavailable.', 5000, 'error');
            return;
        }

        if (hostRowActionMenu && !event.target.closest('[data-host-row-action-menu]')) closeHostRowActionMenu();
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') closeHostRowActionMenu();
    });
    window.addEventListener('resize', closeHostRowActionMenu);
    window.addEventListener('scroll', closeHostRowActionMenu, true);

    assignedTaskStateConfirm?.addEventListener('click', confirmAssignedTaskStateChange);
    assignedTaskStateCancelButtons.forEach(button => button.addEventListener('click', cancelAssignedTaskStateChange));
    assignedTaskStateDialog?.addEventListener('cancel', event => {
        if (assignedTaskStateBusy) event.preventDefault();
        else pendingAssignedTaskState = null;
    });
    assignedTaskStateDialog?.addEventListener('close', () => {
        if (!assignedTaskStateBusy) pendingAssignedTaskState = null;
    });

    detailsTabs.forEach(tab => tab.addEventListener('click', () => setHostDetailsTab(tab.dataset.hostDetailsTab || 'information')));
    detailsSave?.addEventListener('click', saveHostDetails);
    detailsCancel?.addEventListener('click', () => detailsDialog?.close());
    detailsDialog?.addEventListener('close', () => { activeHostId = ''; });

    undoHostYes?.addEventListener('click', confirmUndoHost);
    undoHostNoButtons.forEach(button => button.addEventListener('click', cancelUndoHost));
    undoHostDialog?.addEventListener('cancel', event => {
        if (undoHostBusy) event.preventDefault();
        else cancelUndoHost();
    });

    // Cross-page Host-editor navigation lands on this page without opening a native popup.
    const hostEditorLandingUrl = new URL(window.location.href);
    const hostEditorLandingId = String(hostEditorLandingUrl.searchParams.get('editorHostId') || '').trim();
    if (hostEditorLandingId) {
        const hostEditorLandingName = hostEditorLandingUrl.searchParams.get('editorHostName') || '';
        ['editorHostId', 'editorHostName'].forEach(key => hostEditorLandingUrl.searchParams.delete(key));
        window.history.replaceState(window.history.state, '', hostEditorLandingUrl.toString());
        void window.MauiEditorRouting.openHost(hostEditorLandingId, hostEditorLandingName)
            .catch(error => showStatus(`Host Editor: ${error?.message || 'Unable to open the Host.'}`, 5000, 'error'));
    }

    window.MauiModalUI?.registerContext('host-information', detailsDialog, {
        capture: () => ({ id: activeHostId, url: activeInfoUrl, tab: detailsTabs.find(tab => tab.classList.contains('active'))?.dataset.hostDetailsTab }),
        restore: async state => { const url = window.MauiModalUI.safeUrl(state.url); if (url) { await openHostDetailsById(state.id, url); setHostDetailsTab(state.tab || 'information'); } }
    });
})();

