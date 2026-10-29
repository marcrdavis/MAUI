/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * This JavaScript is part of the MAUI application.
 */

(() => {
    const pageConfig = document.querySelector('[data-task-page-config]');
    if (!pageConfig) return;

    const antiForgeryToken = document.querySelector('.task-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const canSaveTemplates = document.documentElement.dataset.canSaveTemplates === 'true';

    // Returns one Razor-generated endpoint URL and preserves virtual-directory deployments.
    function endpoint(name) {
        return pageConfig.dataset[name] || '';
    }

    // Purpose: Navigates to a report while preserving the relevant return context and transient grid state.
    function navigateToReport(baseUrl, parameters) {
        const target = String(baseUrl || '').trim();
        if (!target) return;
        const url = new URL(target, window.location.href);
        Object.entries(parameters || {}).forEach(([key, value]) => {
            const text = String(value ?? '').trim();
            if (text) url.searchParams.set(key, text);
        });

        const returnUrl = window.MauiGridNavigationState?.createReturnUrl?.('tasks')
            || `${window.location.pathname}${window.location.search}${window.location.hash}`;
        url.searchParams.set('returnUrl', returnUrl);
        url.searchParams.set('returnLabel', 'Tasks');
        window.location.assign(url.toString());
    }

    // Sends JSON with the ASP.NET antiforgery header and convert API failures to readable browser errors.
    async function fetchJson(url, options = {}) {
        const headers = new Headers(options.headers || {});
        headers.set('Accept', 'application/json');
        if (options.body) headers.set('Content-Type', 'application/json');
        if (antiForgeryToken && options.method && options.method.toUpperCase() !== 'GET') {
            headers.set('RequestVerificationToken', antiForgeryToken);
        }

        const response = await (window.MauiMoveItGridConnection?.fetch || window.fetch.bind(window))(url, { ...options, headers });
        if (response.status === 401) {
            window.location.replace(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
            throw new Error('MOVEit session expired.');
        }

        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json') ? await response.json() : null;
        if (!response.ok) { const error = new Error(payload?.message || `Request failed (${response.status}).`); error.status = response.status; error.payload = payload; throw error; }
        return payload;
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
        return payload;
    }

    // ---------------------------------------------------------------------
    // Native MOVEit Task Editor launcher
    // ---------------------------------------------------------------------
    const nativeEditorToast = document.querySelector('[data-grid="tasks"] [data-prototype-toast]');

    const useMauiTaskEditor = pageConfig.dataset.useMauiTaskEditor === 'true';
    window.MauiTaskHostEditor?.initialize(pageConfig, antiForgeryToken, { fetchJson });
    const traditionalTaskEditor = useMauiTaskEditor ? (window.MauiTaskEditor || window.MauiTraditionalTaskEditor)?.initialize({
        configElement: pageConfig,
        antiForgeryToken,
        onNativeEditor: (taskId, taskName) => launchNativeTaskEditor(taskId, taskName),
        onPreflight: (taskId, taskName) => openTaskPreflight(taskId, taskName),
        onRunNow: (taskId, taskName) => runTaskNow(taskId, taskName),
        onExport: (taskId, taskName) => nativePortability?.openExport([taskId], [taskName]),
        onDeleteTask: taskId => beginDeleteTasks([taskId]),
        onCloneTask: (id, name, type) => openCloneTaskDialog(id, name, type),
        onSaveNotificationTemplate: draft => openEditorNotificationTemplateSave(draft),
        onClosed: () => {
            taskGrid?.dispatchEvent(new CustomEvent('maui:task-filename-search-invalidated'));
            if (!window.MauiEditorRouting?.returnToOrigin('task')) return refreshTaskGrid(false);
        }
    }) : null;

    function shouldUseMauiTaskEditor(taskType) {
        return useMauiTaskEditor && ['traditional','synchronization','sync','sy','advanced','cos'].includes(String(taskType || '').trim().toLowerCase());
    }

    async function launchTaskEditor(taskId, taskName, taskType = '', popup = null) {
        if (shouldUseMauiTaskEditor(taskType)) {
            closeReservedPopup(popup);
            if (!traditionalTaskEditor?.open) {
                showNativeEditorStatus('The MAUI Task Editor is unavailable. Reload MAUI and try again.', 5000, 'error');
                return false;
            }
            return traditionalTaskEditor.open(taskId, taskName, taskType);
        }
        return launchNativeTaskEditor(taskId, taskName, popup);
    }

    window.MauiEditorRouting?.registerTaskOpen((taskId, taskName, taskType, popup) => {
        if (document.querySelector('[data-traditional-task-editor-dialog][open]') && document.querySelector('[data-host-editor-dialog][open]')) {
            window.MauiModalUI?.showError('Close the Host editor to return to your current Task draft before opening another Task.');
            return false;
        }
        return launchTaskEditor(taskId, taskName, taskType, popup);
    });


    // Shows a short MAUI status/error message without blocking the Task grid.
    function showNativeEditorStatus(message, durationMs = 5000, kind = 'default', origin = 'modal') {
        if (typeof window.mauiDisplayToast === 'function') { window.mauiDisplayToast(nativeEditorToast, message, kind, durationMs, origin); return; }
        if (!nativeEditorToast) return;
        nativeEditorToast.textContent = message || '';
        nativeEditorToast.classList.toggle('error', kind === 'error');
        nativeEditorToast.classList.toggle('warning', kind === 'warning');
        nativeEditorToast.classList.add('visible');
        window.clearTimeout(nativeEditorToast._mauiNativeTimer);
        nativeEditorToast._mauiNativeTimer = window.setTimeout(
            () => nativeEditorToast.classList.remove('visible'),
            durationMs);
    }

    // Launches a Task-specific native editor route only after MAUI has saved the current task XML as the user's undo point.
    // A pre-reserved popup can be supplied when asynchronous work has already started from the user's click.
    async function launchNativeTaskEditor(taskId, taskName, popup = null, prepareUndo = true, markEdited = true) {
        const normalizedTaskId = String(taskId || '').trim();
        let normalizedTaskName = String(taskName || '').trim();
        if (!normalizedTaskId) return false;
        let permissionReadOnly=false;
        try { permissionReadOnly=window.MauiObjectAccess ? !(await window.MauiObjectAccess.read('Task',normalizedTaskId)).canEdit : false; }
        catch(error) { showNativeEditorStatus(error.message || 'The object is unavailable.',5000,'error'); closeReservedPopup(popup); return false; }


        if (!window.MauiNativeEditor?.open || !window.MauiNativeEditor?.reserve) {
            showNativeEditorStatus('The MAUI native editor launcher is unavailable.', 5000, 'error');
            try { popup?.close(); } catch { /* ignored */ }
            return false;
        }

        // Reserve the popup before awaiting the server-side snapshot so normal editor clicks remain browser-popup safe.
        const reservedPopup = popup || window.MauiNativeEditor.reserve(`MAUI Task Editor - ${normalizedTaskId}`);
        if (!reservedPopup || reservedPopup.closed) {
            showNativeEditorStatus('The MOVEit native editor popup was blocked by the browser.', 5000, 'error');
            return false;
        }

        if (prepareUndo && !permissionReadOnly) {
            try {
                const prepared = await fetchJson(endpoint('prepareTaskEditUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ taskId: normalizedTaskId })
                });
                normalizedTaskName = String(prepared?.taskName || normalizedTaskName || 'Task').trim();
                setTaskUndoAvailable(normalizedTaskId, true);
            } catch (error) {
                closeReservedPopup(reservedPopup);
                showNativeEditorStatus(`Task Editor: ${error?.message || 'Unable to save the undo snapshot.'}`, 5000, 'error');
                return false;
            }
        }

        const opened = await window.MauiNativeEditor.open({
            popup: reservedPopup,
            sessionUrl: endpoint('nativeEditorSessionUrl'),
            loginUrl: endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'),
            antiForgeryToken,
            returnHash: `#/tasks/details/${encodeURIComponent(normalizedTaskId)}?HideNav`,
            windowTitle: `MAUI Task Editor - ${normalizedTaskId}: ${normalizedTaskName || 'Task'}`,
            skin: 'maui', disableUndo:permissionReadOnly,
            status: showNativeEditorStatus,
        });

        if (opened && markEdited && !permissionReadOnly) {
            // Records the MAUI edit audit after the editor navigation has successfully started.
            try {
                await fetchJson(endpoint('markTaskEditedUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ taskId: normalizedTaskId })
                });
            } catch (error) {
                console.warn('MAUI could not update the task modification audit information.');
            }
        }

        return opened;
    }

    // Supplies Task-specific route/title information from an existing grid row.
    async function openNativeTaskEditor(button) {
        const taskId = String(button.dataset.rowId || '').trim();
        const taskName = String(button.dataset.taskName || button.textContent || '').trim();
        const rowTaskType = button.dataset.taskType
            || button.closest('tr')?.querySelector('[data-task-row-menu-button]')?.dataset.taskType
            || '';
        return await launchTaskEditor(taskId, taskName, rowTaskType);
    }

    // ---------------------------------------------------------------------
    // New Task modal
    // ---------------------------------------------------------------------
    const newTaskDialog = document.querySelector('[data-new-task-dialog]');
    const newTaskForm = newTaskDialog?.querySelector('[data-new-task-form]');
    const newTaskName = newTaskDialog?.querySelector('[data-new-task-name]');
    const newTaskAutoFormat = newTaskDialog?.querySelector('[data-new-task-auto-format]');
    const newTaskResourceGroup = newTaskDialog?.querySelector('[data-new-task-resource-group]');
    const newTaskSubmit = newTaskDialog?.querySelector('[data-new-task-submit]');
    const newTaskCancel = newTaskDialog?.querySelector('[data-new-task-cancel]');
    const newTaskClose = newTaskDialog?.querySelector('[data-new-task-close]');
    const newTaskError = newTaskDialog?.querySelector('[data-new-task-error]');
    const newTaskSpinner = newTaskDialog?.querySelector('[data-new-task-spinner]');
    const newTaskSubmitLabel = newTaskDialog?.querySelector('[data-new-task-submit-label]');
    let newTaskBusy = false;
    let newTaskCreationUncertain = false;
    let newTaskGroupsLoading = false;
    let newTaskGroupsLoadFailed = false;
    let newTaskGroupsRequestId = 0;
    const updateNewTaskAction = window.MauiModalUI.watchAction(newTaskDialog, newTaskSubmit,
        () => !newTaskBusy && !newTaskCreationUncertain && !newTaskGroupsLoading && !newTaskGroupsLoadFailed,
        () => Boolean(newTaskName?.value.trim()) && newTaskName.value.trim().length <= 254 && window.MauiModalUI.formValid(newTaskDialog));

    // Loads only Resource Groups that permit Task creation for this MOVEit non-admin session.
    // Admins have no group control and do not make this request.
    async function loadNewTaskResourceGroups() {
        if (!newTaskResourceGroup) return;
        const requestId = ++newTaskGroupsRequestId;
        newTaskGroupsLoading = true;
        newTaskGroupsLoadFailed = false;
        newTaskResourceGroup.disabled = true;
        if (newTaskSubmit) newTaskSubmit.disabled = true;
        try {
            const response = await fetchJson(endpoint('newTaskGroupsUrl'));
            if (requestId !== newTaskGroupsRequestId || !newTaskDialog?.open) return;
            const groups = Array.isArray(response?.groups) ? response.groups : [];
            newTaskResourceGroup.replaceChildren();
            const placeholder = document.createElement('option');
            placeholder.value = '';
            placeholder.textContent = 'Select a Resource Group';
            newTaskResourceGroup.appendChild(placeholder);
            groups.forEach(name => {
                const option = document.createElement('option');
                option.value = String(name);
                option.textContent = String(name);
                newTaskResourceGroup.appendChild(option);
            });
            if (groups.length === 0) {
                newTaskGroupsLoadFailed = true;
                showNewTaskError('No Resource Groups are available to this MOVEit account.');
            }
        } catch (error) {
            if (requestId !== newTaskGroupsRequestId || !newTaskDialog?.open) return;
            newTaskGroupsLoadFailed = true;
            showNewTaskError(error?.message || 'Unable to load MOVEit Resource Groups. Close and reopen New Task to retry.');
        } finally {
            if (requestId === newTaskGroupsRequestId && newTaskDialog?.open) {
                newTaskGroupsLoading = false;
                newTaskResourceGroup.disabled = newTaskBusy || newTaskGroupsLoadFailed;
                updateNewTaskAction();
            }
        }
    }

    // Purpose: Open new task dialog.
    function openNewTaskDialog() {
        if (!newTaskDialog) return;
        resetNewTaskDialog();
        newTaskDialog.showModal();
        void loadNewTaskResourceGroups();
        window.requestAnimationFrame(() => newTaskName?.focus());
    }

    // Purpose: Reset the New Task dialog to its default state.
    function resetNewTaskDialog() {
        if (!newTaskDialog) return;
        ++newTaskGroupsRequestId; // Ignore any pending response after close/reopen.
        newTaskGroupsLoading = false;
        newTaskGroupsLoadFailed = false;
        newTaskCreationUncertain = false;
        setNewTaskBusy(false);
        hideNewTaskError();
        if (newTaskName) newTaskName.value = '';
        if (newTaskResourceGroup) {
            newTaskResourceGroup.replaceChildren();
            const placeholder = document.createElement('option');
            placeholder.value = '';
            placeholder.textContent = 'Select a Resource Group';
            newTaskResourceGroup.appendChild(placeholder);
            newTaskResourceGroup.disabled = false;
        }
        if (newTaskAutoFormat) newTaskAutoFormat.checked = newTaskAutoFormat.defaultChecked;
        const traditional = newTaskDialog.querySelector('input[name="newTaskType"][value="Traditional"]');
        if (traditional) traditional.checked = true;
    }

    // Keeps Auto Format limited to only the two requested transformations: uppercase and spaces to underscores.
    function applyNewTaskNameFormat() {
        if (!newTaskName || !newTaskAutoFormat?.checked) return;
        const start = newTaskName.selectionStart;
        const end = newTaskName.selectionEnd;
        const formatted = newTaskName.value.toUpperCase().replaceAll(' ', '_');
        if (formatted === newTaskName.value) return;
        newTaskName.value = formatted;
        if (typeof start === 'number' && typeof end === 'number') {
            try { newTaskName.setSelectionRange(start, end); } catch { /* unsupported input selection */ }
        }
    }

    // Purpose: Set new task busy.
    function setNewTaskBusy(busy) {
        newTaskBusy = Boolean(busy);
        if (newTaskName) newTaskName.readOnly = newTaskBusy;
        if (newTaskAutoFormat) newTaskAutoFormat.disabled = newTaskBusy;
        newTaskDialog?.querySelectorAll('input[name="newTaskType"]').forEach(input => { input.disabled = newTaskBusy; });
        if (newTaskResourceGroup) newTaskResourceGroup.disabled = newTaskBusy || newTaskGroupsLoading || newTaskGroupsLoadFailed;
        if (newTaskCancel) newTaskCancel.disabled = newTaskBusy;
        if (newTaskClose) newTaskClose.disabled = newTaskBusy;
        if (newTaskSpinner) newTaskSpinner.hidden = !newTaskBusy;
        if (newTaskSubmitLabel) newTaskSubmitLabel.textContent = newTaskBusy ? 'Creating…' : 'Create Task';
        newTaskDialog?.classList.toggle('is-busy', newTaskBusy);
        updateNewTaskAction();
    }

    // Purpose: Show new task error.
    function showNewTaskError(message) {
        if (!newTaskError) return;
        newTaskError.textContent = message || 'Unable to create the task.';
        newTaskError.hidden = false;
    }

    // Purpose: Hide new task error.
    function hideNewTaskError() {
        if (!newTaskError) return;
        newTaskError.textContent = '';
        newTaskError.hidden = true;
    }

    // Purpose: Close reserved popup.
    function closeReservedPopup(popup) {
        try {
            if (popup && !popup.closed) popup.close();
        } catch { /* ignored */ }
    }

    const nativePortability = window.MauiNativePortability?.create({
        objectType: 'Task',
        exportUrl: endpoint('exportTaskUrl'),
        preflightUrl: endpoint('preflightImportTaskUrl'),
        importUrl: endpoint('importTaskUrl'),
        antiForgeryToken,
        loginUrl: endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'),
        status: showNativeEditorStatus,
        refresh: async () => { await refreshTaskGrid(true); }
    });

    const taskSpreadsheetImport = window.MauiTaskSpreadsheetImport?.create({
        previewUrl: endpoint('previewTaskSpreadsheetUrl'),
        analyzeUrl: endpoint('analyzeTaskSpreadsheetUrl'),
        importRowUrl: endpoint('importTaskSpreadsheetRowUrl'),
        importRowLimit: Number(pageConfig.dataset.taskSpreadsheetImportRowLimit),
        antiForgeryToken,
        loginUrl: endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'),
        nativePortability,
        status: showNativeEditorStatus,
        refresh: async () => { await refreshTaskGrid(true); }
    });

    // Creates a disabled MOVEit Task and opens the preferred editor.
    async function createNewTask() {
        if (newTaskBusy || newTaskCreationUncertain || !newTaskDialog) return;
        hideNewTaskError();
        applyNewTaskNameFormat();

        const taskName = String(newTaskName?.value || '').trim();
        const taskType = String(newTaskDialog.querySelector('input[name="newTaskType"]:checked')?.value || 'Traditional');
        if (!taskName) {
            showNewTaskError('Task Name is required.');
            newTaskName?.focus();
            return;
        }
        if (taskName.length > 254) {
            showNewTaskError('Task Name cannot exceed 254 characters.');
            newTaskName?.focus();
            return;
        }
        if (newTaskGroupsLoading || newTaskGroupsLoadFailed) return;
        if (newTaskResourceGroup && !newTaskResourceGroup.value) {
            showNewTaskError('Select a Resource Group.');
            newTaskResourceGroup.focus();
            return;
        }

        let reservedPopup = null;
        if (!shouldUseMauiTaskEditor(taskType)) {
            if (!window.MauiNativeEditor?.reserve || !window.MauiNativeEditor?.open) {
                showNewTaskError('The native Task Editor launcher is unavailable. Reload MAUI and try again.');
                return;
            }
            reservedPopup = window.MauiNativeEditor.reserve('MAUI Task Editor - New Task');
            if (!reservedPopup) {
                showNewTaskError('The native MOVEit editor popup was blocked. Allow popups for MAUI and try again.');
                return;
            }
        }

        setNewTaskBusy(true);

        try {
            const data = await fetchJson(endpoint('createTaskUrl'), {
                method: 'POST',
                body: JSON.stringify({ name: taskName, taskType, resourceGroup: newTaskResourceGroup?.value || '' })
            });

            const created = data?.task || {};
            const createdId = String(created.id || '').trim();
            const createdName = String(created.name || taskName).trim();
            if (data?.success !== true || !createdId) throw new Error('The Task creation acknowledgement was incomplete. Inspect Tasks before creating another copy.');

            newTaskDialog.close();

            // Capture the initial undo point and begin editor navigation before refreshing the grid so
            // the newly created row sees the per-user undo snapshot on its first refresh.
            await launchTaskEditor(createdId, createdName, taskType, reservedPopup);
            refreshTaskGrid(false);
        } catch (error) {
            closeReservedPopup(reservedPopup);
            newTaskCreationUncertain = Boolean(error?.payload?.outcomeUnknown || !error?.status || error.status >= 500);
            setNewTaskBusy(false);
            showNewTaskError((error?.message || 'Unable to create the task.')
                + (newTaskCreationUncertain ? ' Close this dialog and inspect Tasks before creating another Task.' : ''));
            if (newTaskCreationUncertain) void refreshTaskGrid(true);
        }
    }

    // ---------------------------------------------------------------------
    // Clone Task modal
    // ---------------------------------------------------------------------
    const cloneTaskDialog = document.querySelector('[data-clone-task-dialog]');
    const cloneTaskForm = cloneTaskDialog?.querySelector('[data-clone-task-form]');
    const cloneTaskName = cloneTaskDialog?.querySelector('[data-clone-task-name]');
    const cloneTaskError = cloneTaskDialog?.querySelector('[data-clone-task-error]');
    const cloneTaskSubmit = cloneTaskDialog?.querySelector('[data-clone-task-submit]');
    const cloneTaskCancel = cloneTaskDialog?.querySelector('[data-clone-task-cancel]');
    const cloneTaskClose = cloneTaskDialog?.querySelector('[data-clone-task-close]');
    const cloneTaskSpinner = cloneTaskDialog?.querySelector('[data-clone-task-spinner]');
    const cloneTaskSubmitLabel = cloneTaskDialog?.querySelector('[data-clone-task-submit-label]');
    let cloneTaskBusy = false;
    let cloneTaskSource = null;
    const updateCloneTaskAction = window.MauiModalUI.watchAction(cloneTaskDialog, cloneTaskSubmit,
        () => !cloneTaskBusy && Boolean(cloneTaskSource),
        () => Boolean(cloneTaskName?.value.trim()) && cloneTaskName.value.trim().length <= 254);

    // Purpose: Opens Clone Task with the requested default name.
    function openCloneTaskDialog(taskId, taskName, taskType = '') {
        if (!cloneTaskDialog) return;
        resetCloneTaskDialog();
        const sourceId = String(taskId || '').trim();
        const sourceName = String(taskName || sourceId).trim();
        if (!sourceId) return;
        cloneTaskSource = { taskId: sourceId, taskName: sourceName, taskType };
        if (cloneTaskName) cloneTaskName.value = `${sourceName} Clone`;
        cloneTaskDialog.showModal();
        window.requestAnimationFrame(() => {
            cloneTaskName?.focus();
            const length = cloneTaskName?.value?.length || 0;
            try { cloneTaskName?.setSelectionRange(length, length); } catch { /* unsupported input selection */ }
        });
    }

    // Purpose: Resets Clone Task state after the dialog closes.
    function resetCloneTaskDialog() {
        setCloneTaskBusy(false);
        cloneTaskSource = null;
        if (cloneTaskName) cloneTaskName.value = '';
        hideCloneTaskError();
    }

    // Purpose: Applies the standard MAUI busy treatment to Clone Task.
    function setCloneTaskBusy(busy) {
        cloneTaskBusy = Boolean(busy);
        if (cloneTaskName) cloneTaskName.readOnly = cloneTaskBusy;
        if (cloneTaskCancel) cloneTaskCancel.disabled = cloneTaskBusy;
        if (cloneTaskClose) cloneTaskClose.disabled = cloneTaskBusy;
        if (cloneTaskSpinner) cloneTaskSpinner.hidden = !cloneTaskBusy;
        if (cloneTaskSubmitLabel) cloneTaskSubmitLabel.textContent = cloneTaskBusy ? 'Creating…' : 'Create Task';
        cloneTaskDialog?.classList.toggle('is-busy', cloneTaskBusy);
        updateCloneTaskAction();
    }

    // Purpose: Shows a Clone Task validation/server error.
    function showCloneTaskError(message) {
        if (!cloneTaskError) return;
        cloneTaskError.textContent = message || 'Unable to clone the Task.';
        cloneTaskError.hidden = false;
    }

    // Purpose: Clears the Clone Task error surface.
    function hideCloneTaskError() {
        if (!cloneTaskError) return;
        cloneTaskError.textContent = '';
        cloneTaskError.hidden = true;
    }

    // Purpose: Clones the selected Task through MAUI's export/import pipeline and opens the new Task in the native editor.
    async function submitCloneTask() {
        if (cloneTaskBusy || !cloneTaskSource) return;
        hideCloneTaskError();
        const newTaskName = String(cloneTaskName?.value || '').trim();
        if (!newTaskName) {
            showCloneTaskError('Task Name is required.');
            cloneTaskName?.focus();
            return;
        }
        if (newTaskName.length > 254) {
            showCloneTaskError('Task Name cannot exceed 254 characters.');
            cloneTaskName?.focus();
            return;
        }
        const source = cloneTaskSource;
        const requiresNativePopup = !shouldUseMauiTaskEditor(source.taskType);
        if (requiresNativePopup && (!window.MauiNativeEditor?.reserve || !window.MauiNativeEditor?.open)) {
            showCloneTaskError('The native MOVEit editor launcher is unavailable. Reload MAUI and try again.');
            return;
        }
        const reservedPopup = requiresNativePopup ? window.MauiNativeEditor.reserve('MAUI Task Editor - Cloned Task') : null;
        if (requiresNativePopup && !reservedPopup) {
            showCloneTaskError('The native MOVEit editor popup was blocked. Allow popups for MAUI and try again.');
            return;
        }

        setCloneTaskBusy(true);
        try {
            const result = await fetchJson(endpoint('cloneTaskUrl'), {
                method: 'POST',
                body: JSON.stringify({ taskId: source.taskId, newTaskName })
            });
            const taskId = String(result?.taskId || '').trim();
            const taskName = String(result?.taskName || newTaskName).trim();
            if (!taskId) throw new Error('MOVEit created the cloned Task but did not return a Task ID.');

            const warning = String(result?.warning || '').trim();
            cloneTaskDialog?.close();
            if (warning) showNativeEditorStatus(`Clone Task: ${warning}`, 5000);
            else showNativeEditorStatus(`Created cloned Task ${taskId}.`, 5000);
            const opened = await launchTaskEditor(taskId, taskName, source.taskType || '', reservedPopup);
            if (!opened) closeReservedPopup(reservedPopup);
            await refreshTaskGrid(true);
        } catch (error) {
            closeReservedPopup(reservedPopup);
            setCloneTaskBusy(false);
            showCloneTaskError(error?.message || 'Unable to clone the Task.');
        }
    }

    // Updates the current grid row immediately after a snapshot is created so Undo Changes is available without waiting for refresh.
    function setTaskUndoAvailable(taskId, available) {
        const normalizedTaskId = String(taskId || '').trim();
        if (!normalizedTaskId) return;
        document.querySelectorAll('[data-task-row-menu-button]').forEach(button => {
            if (String(button.dataset.rowId || '').trim() === normalizedTaskId) {
                button.dataset.taskHasUndo = available ? 'true' : 'false';
            }
        });
    }

    // ---------------------------------------------------------------------
    // Per-row Task actions
    // ---------------------------------------------------------------------
    const taskRowActionMenu = document.querySelector('[data-task-row-action-menu]');
    const taskRowUndoCommand = taskRowActionMenu?.querySelector('[data-task-row-command="undo"]');
    const taskRowNotificationsCommand = taskRowActionMenu?.querySelector('[data-task-row-command="notifications"]');
    const taskRowCloneCommand = taskRowActionMenu?.querySelector('[data-task-row-command="clone"]');
    const taskRowRunCommand = taskRowActionMenu?.querySelector('[data-task-row-command="run"]');
    const taskRowKillCommand = taskRowActionMenu?.querySelector('[data-task-row-command="kill"]');
    const taskRowToggleEnabledCommand = taskRowActionMenu?.querySelector('[data-task-row-command="toggle-enabled"]');
    let activeTaskRowAction = null;
    let taskLicenseLimitReached = false;

    // Purpose: Close task row action menu.
    function closeTaskRowActionMenu() {
        if (activeTaskRowAction?.trigger) activeTaskRowAction.trigger.setAttribute('aria-expanded', 'false');
        activeTaskRowAction = null;
        if (!taskRowActionMenu) return;
        taskRowActionMenu.hidden = true;
        taskRowActionMenu.style.left = '';
        taskRowActionMenu.style.top = '';
    }

    // Purpose: Position task row action menu.
    function positionTaskRowActionMenu(trigger) {
        if (!taskRowActionMenu || taskRowActionMenu.hidden) return;
        const triggerRect = trigger.getBoundingClientRect();
        const menuRect = taskRowActionMenu.getBoundingClientRect();
        const margin = 8;
        const gap = 5;

        let left = triggerRect.right - menuRect.width;
        left = Math.max(margin, Math.min(left, window.innerWidth - menuRect.width - margin));

        let top = triggerRect.bottom + gap;
        if (top + menuRect.height > window.innerHeight - margin) {
            top = Math.max(margin, triggerRect.top - menuRect.height - gap);
        }

        taskRowActionMenu.style.left = `${Math.round(left)}px`;
        taskRowActionMenu.style.top = `${Math.round(top)}px`;
    }

    // Purpose: Open task row action menu.
    function openTaskRowActionMenu(trigger) {
        if (!taskRowActionMenu || !trigger) return;

        if (activeTaskRowAction?.trigger === trigger && !taskRowActionMenu.hidden) {
            closeTaskRowActionMenu();
            return;
        }

        closeTaskRowActionMenu();
        window.MauiObjectAccess?.reset(taskRowActionMenu);
        const taskId = String(trigger.dataset.rowId || '').trim();
        if (!taskId) return;

        const enabled = trigger.dataset.taskEnabled === 'true';
        const runnable = trigger.dataset.taskRunnable === 'true';
        const running = trigger.dataset.taskRunning === 'true';
        const hasUndo = trigger.dataset.taskHasUndo === 'true';
        activeTaskRowAction = {
            trigger,
            taskId,
            taskName: String(trigger.dataset.taskName || taskId).trim(),
            infoUrl: String(trigger.dataset.taskInfoUrl || '').trim(),
            enabled,
            hasUndo,
            taskType: String(trigger.dataset.taskType || '').trim()
        };

        if (taskRowUndoCommand) { taskRowUndoCommand.hidden = false; taskRowUndoCommand.disabled = !hasUndo; taskRowUndoCommand.title = hasUndo ? '' : 'Undo is not available until MAUI has saved a prior Task configuration.'; }
        if (taskRowNotificationsCommand) { const supported = activeTaskRowAction.taskType.toLowerCase() === 'traditional'; taskRowNotificationsCommand.disabled = !supported; taskRowNotificationsCommand.title = supported ? '' : 'Edit Notifications are only supported for Traditional tasks.'; }
        if (taskRowCloneCommand) { taskRowCloneCommand.disabled = taskLicenseLimitReached; taskRowCloneCommand.title = taskLicenseLimitReached ? 'The MOVEit Task license limit has been reached.' : ''; }
        if (taskRowRunCommand) { taskRowRunCommand.hidden = false; taskRowRunCommand.disabled = !runnable; taskRowRunCommand.title = runnable ? '' : 'Incomplete Tasks cannot be run.'; }
        if (taskRowKillCommand) { taskRowKillCommand.hidden = false; taskRowKillCommand.disabled = !running; taskRowKillCommand.title = running ? '' : 'Kill Task is available only while the Task is running.'; }
        if (taskRowToggleEnabledCommand) taskRowToggleEnabledCommand.textContent = enabled ? 'Disable Task' : 'Enable Task';

        void window.MauiObjectAccess?.menu(taskRowActionMenu,'Task',taskId);
        taskRowActionMenu.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
        positionTaskRowActionMenu(trigger);
    }

    // Purpose: Copies text to the system clipboard with a legacy fallback for browsers where Clipboard API access is unavailable.
    async function copyTaskNameToClipboard(taskName) {
        const value = String(taskName || '').trim();
        if (!value) return;

        let clipboardError = null;
        if (navigator.clipboard?.writeText) {
            try {
                await navigator.clipboard.writeText(value);
                showNativeEditorStatus('Copied to clipboard.', 5000);
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
            showNativeEditorStatus('Copied to clipboard.', 5000);
        } catch (error) {
            console.warn('MAUI could not copy the Task name to the clipboard.');
            showNativeEditorStatus('Unable to copy Task name to clipboard.', 5000, 'error');
        } finally {
            textarea.remove();
        }
    }

    // Purpose: Dispatch one command from the per-row Task actions menu.
    function handleTaskRowCommand(command) {
        const action = activeTaskRowAction;
        if (!action) return;
        closeTaskRowActionMenu();

        if (command === 'information') {
            taskInformation?.open(action.taskId, action.infoUrl);
            return;
        }
        if (command === 'preflight') {
            openTaskPreflight(action.taskId, action.taskName);
            return;
        }
        if (command === 'run-history') {
            navigateToReport(endpoint('taskRunReportUrl'), { taskId: action.taskId, taskName: action.taskName });
            return;
        }
        if (command === 'file-activity') {
            navigateToReport(endpoint('fileActivityReportUrl'), { taskId: action.taskId, taskName: action.taskName });
            return;
        }
        if (command === 'audit') {
            navigateToReport(endpoint('auditReportUrl'), { targetType: 'task', targetId: action.taskId, targetName: action.taskName });
            return;
        }
        if (command === 'copy-name') {
            copyTaskNameToClipboard(action.taskName);
            return;
        }
        if (command === 'clone') {
            openCloneTaskDialog(action.taskId, action.taskName, action.taskType);
            return;
        }
        if (command === 'export') {
            nativePortability?.openExport([action.taskId], [action.taskName]);
            return;
        }
        if (command === 'edit') {
            launchTaskEditor(action.taskId, action.taskName, action.taskType || '');
            return;
        }
        if (command === 'undo') {
            beginUndoTask(action.taskId, action.taskName, action.taskType);
            return;
        }
        if (command === 'notifications') {
            openTaskNotifications([action.taskId]);
            return;
        }
        if (command === 'run') {
            runTaskNow(action.taskId, action.taskName);
            return;
        }
        if (command === 'kill') {
            beginKillTask(action.taskId, action.taskName);
            return;
        }
        if (command === 'toggle-enabled') {
            runTaskEnableDisable([action.taskId], !action.enabled);
            return;
        }
        if (command === 'delete') beginDeleteTasks([action.taskId]);
    }

    // ---------------------------------------------------------------------
    // Per-user three-version Undo
    // ---------------------------------------------------------------------
    const undoTaskDialog = document.querySelector('[data-undo-task-dialog]');
    const undoTaskMessage = undoTaskDialog?.querySelector('[data-undo-task-message]');
    const undoTaskHistory = undoTaskDialog?.querySelector('[data-undo-task-history]');
    const undoTaskError = undoTaskDialog?.querySelector('[data-undo-task-error]');
    const undoTaskYes = undoTaskDialog?.querySelector('[data-undo-task-yes]');
    const undoTaskNoButtons = Array.from(undoTaskDialog?.querySelectorAll('[data-undo-task-no]') || []);
    let pendingUndoTask = null;
    let undoTaskBusy = false;

    // Purpose: Formats undo snapshot date.
    function formatUndoSnapshotDate(value) {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? String(value || '') : date.toLocaleString();
    }

    // Purpose: Renders task undo history.
    function renderTaskUndoHistory(snapshots) {
        if (!undoTaskHistory) return '';
        undoTaskHistory.replaceChildren();
        let selectedId = '';
        snapshots.forEach((snapshot, index) => {
            const canRestore = snapshot?.canRestore !== false;
            const id = String(snapshot?.snapshotId || '').trim();
            const label = document.createElement('label');
            label.className = `undo-history-option${canRestore ? '' : ' is-disabled'}`;
            const radio = document.createElement('input');
            radio.type = 'radio'; radio.name = 'taskUndoSnapshot'; radio.value = id; radio.disabled = !canRestore;
            if (!selectedId && canRestore) { radio.checked = true; selectedId = id; }
            const body = document.createElement('span'); body.className = 'undo-history-option-main';
            const date = document.createElement('span'); date.className = 'undo-history-option-date'; date.textContent = formatUndoSnapshotDate(snapshot?.snapshotCreatedUtc);
            const meta = document.createElement('span'); meta.className = 'undo-history-option-meta'; meta.textContent = `${index === 0 ? 'Most recent' : `Version ${index + 1}`} · Saved by ${String(snapshot?.snapshotCreatedBy || 'current user')}`;
            body.append(date, meta);
            const warningText = String(snapshot?.message || '').trim();
            if (warningText) { const warning = document.createElement('span'); warning.className = 'undo-history-option-warning'; warning.textContent = warningText; body.append(warning); }
            label.append(radio, body); undoTaskHistory.append(label);
        });
        return selectedId;
    }

    // Purpose: Loads Task undo history and opens the restore dialog.
    async function beginUndoTask(taskId, taskName, taskType = '') {
        const id = String(taskId || '').trim();
        if (!id) return;
        if (bulkTaskActionBusy || deleteTasksBusy || killTaskBusy || undoTaskBusy) { showNativeEditorStatus('Another task action is already in progress.', 5000); return; }
        undoTaskBusy = true;
        try {
            const preflight = await fetchJson(endpoint('preflightUndoTaskUrl'), { method: 'POST', body: JSON.stringify({ taskId: id }) });
            const snapshots = Array.isArray(preflight?.snapshots) ? preflight.snapshots : [];
            if (!preflight?.success || snapshots.length === 0) { showNativeEditorStatus(preflight?.message || 'Undo is not available for this task.', 5000); return; }
            const name = String(preflight?.taskName || taskName || id).trim();
            const selectedSnapshotId = renderTaskUndoHistory(snapshots);
            pendingUndoTask = { taskId: id, taskName: name, taskType, snapshots, selectedSnapshotId };
            if (undoTaskMessage) undoTaskMessage.textContent = `Select the version of task "${name}" to restore.`;
            if (undoTaskError) { undoTaskError.hidden = true; undoTaskError.textContent = ''; }
            if (undoTaskYes) undoTaskYes.disabled = !selectedSnapshotId;
            undoTaskHistory?.querySelectorAll('input[type="radio"]').forEach(radio => radio.addEventListener('change', () => {
                pendingUndoTask.selectedSnapshotId = radio.value;
                if (undoTaskYes) undoTaskYes.disabled = false;
            }));
            undoTaskDialog?.showModal();
            window.requestAnimationFrame(() => undoTaskHistory?.querySelector('input[type="radio"]:checked')?.focus());
        } catch (error) { showNativeEditorStatus(`Undo Changes: ${error?.message || 'Unable to validate the undo history.'}`, 5000, 'error'); }
        finally { undoTaskBusy = false; }
    }

    // Purpose: Restores the selected Task undo snapshot.
    async function confirmUndoTask() {
        if (undoTaskBusy || !pendingUndoTask?.taskId || !pendingUndoTask?.selectedSnapshotId) return;
        const requiresNativePopup = !shouldUseMauiTaskEditor(pendingUndoTask.taskType);
        if (requiresNativePopup && !window.MauiNativeEditor?.reserve) { if (undoTaskError) { undoTaskError.textContent = 'The native MOVEit editor launcher is unavailable.'; undoTaskError.hidden = false; } return; }
        const reservedPopup = requiresNativePopup ? window.MauiNativeEditor.reserve(`MAUI Task Editor - ${pendingUndoTask.taskId}`) : null;
        if (requiresNativePopup && !reservedPopup) { if (undoTaskError) { undoTaskError.textContent = 'The MOVEit native editor popup was blocked by the browser.'; undoTaskError.hidden = false; } return; }
        undoTaskBusy = true; if (undoTaskYes) undoTaskYes.disabled = true; undoTaskNoButtons.forEach(button => { button.disabled = true; });
        try {
            const result = await fetchJson(endpoint('undoTaskUrl'), { method: 'POST', body: JSON.stringify({ taskId: pendingUndoTask.taskId, snapshotId: pendingUndoTask.selectedSnapshotId }) });
            const restoredTaskId = String(result?.taskId || pendingUndoTask.taskId).trim();
            const restoredTaskName = String(result?.taskName || pendingUndoTask.taskName || restoredTaskId).trim();
            const warning = String(result?.warning || '').trim();
            const restoredTaskType = pendingUndoTask.taskType;
            undoTaskDialog?.close(); pendingUndoTask = null;
            if (warning) showNativeEditorStatus(`Undo Changes: ${warning}`, 5000); else showNativeEditorStatus(`Undo Changes: restored Task ${restoredTaskId}.`, 5000);
            const opened = await launchTaskEditor(restoredTaskId, restoredTaskName, restoredTaskType || '', reservedPopup); if (!opened) closeReservedPopup(reservedPopup);
            await refreshTaskGrid(true);
        } catch (error) { closeReservedPopup(reservedPopup); if (undoTaskError) { undoTaskError.textContent = error?.message || 'Unable to restore the selected task version.'; undoTaskError.hidden = false; } }
        finally { undoTaskBusy = false; if (undoTaskYes) undoTaskYes.disabled = false; undoTaskNoButtons.forEach(button => { button.disabled = false; }); }
    }

    // Purpose: Cancels undo task.
    function cancelUndoTask() { if (undoTaskBusy) return; pendingUndoTask = null; undoTaskDialog?.close(); }

    // ---------------------------------------------------------------------
    // Task bulk actions / shared single-task action implementations
    // ---------------------------------------------------------------------
    const deleteTasksDialog = document.querySelector('[data-delete-tasks-dialog]');
    const deleteTasksTitle = deleteTasksDialog?.querySelector('[data-delete-tasks-title]');
    const deleteTasksMessage = deleteTasksDialog?.querySelector('[data-delete-tasks-message]');
    const deleteTasksList = deleteTasksDialog?.querySelector('[data-delete-tasks-list]');
    const deleteTasksError = deleteTasksDialog?.querySelector('[data-delete-tasks-error]');
    const deleteTasksYes = deleteTasksDialog?.querySelector('[data-delete-tasks-yes]');
    const deleteTasksNoButtons = Array.from(deleteTasksDialog?.querySelectorAll('[data-delete-tasks-no]') || []);
    const killTaskDialog = document.querySelector('[data-kill-task-dialog]');
    const killTaskMessage = killTaskDialog?.querySelector('[data-kill-task-message]');
    const killTaskError = killTaskDialog?.querySelector('[data-kill-task-error]');
    const killTaskYes = killTaskDialog?.querySelector('[data-kill-task-yes]');
    const killTaskNoButtons = Array.from(killTaskDialog?.querySelectorAll('[data-kill-task-no]') || []);
    let pendingDeleteTaskIds = [];
    let deleteTasksWithoutRecycle = false;
    let pendingKillTask = null;
    let deleteTasksBusy = false;
    let killTaskBusy = false;
    let bulkTaskActionBusy = false;

    // Purpose: Normalize the selected Task IDs before a bulk action.
    function normalizeSelectedTaskIds(ids) {
        return Array.from(new Set((ids || []).map(id => String(id || '').trim()).filter(Boolean)));
    }


    // ---------------------------------------------------------------------
    // Task Preflight
    // ---------------------------------------------------------------------
    const taskPreflightDialog = document.querySelector('[data-task-preflight-dialog]');
    const taskPreflightSubtitle = taskPreflightDialog?.querySelector('[data-task-preflight-subtitle]');
    const taskPreflightSummary = taskPreflightDialog?.querySelector('[data-task-preflight-summary]');
    const taskPreflightResults = taskPreflightDialog?.querySelector('[data-task-preflight-results]');
    const taskPreflightRunAgain = taskPreflightDialog?.querySelector('[data-task-preflight-run-again]');
    const taskPreflightCloseButtons = Array.from(taskPreflightDialog?.querySelectorAll('[data-task-preflight-close]') || []);
    const taskPreflightStageOrder = ['task', 'hosts', 'resources', 'certificates', 'notifications', 'scheduling', 'configuration', 'runtime'];
    const taskPreflightStageNames = {
        task: 'Task', hosts: 'Hosts', resources: 'Resources', certificates: 'Certificates / Keys',
        notifications: 'Notifications', scheduling: 'Scheduling', configuration: 'Configuration', runtime: 'Runtime'
    };
    let taskPreflightBusy = false;
    let activeTaskPreflight = null;

    function taskPreflightStageElement(stage) {
        return taskPreflightDialog?.querySelector(`[data-preflight-stage="${stage}"]`);
    }

    function setTaskPreflightStageState(stage, state) {
        const element = taskPreflightStageElement(stage);
        if (!element) return;
        element.classList.remove('is-active', 'is-pass', 'is-warning', 'is-fail');
        if (state) element.classList.add(`is-${state}`);
        element.setAttribute('aria-label', `${taskPreflightStageNames[stage] || stage}: ${state || 'pending'}`);
    }

    function resetTaskPreflightUi(taskName) {
        window.MauiModalUI?.clear(taskPreflightDialog);
        taskPreflightStageOrder.forEach(stage => setTaskPreflightStageState(stage, ''));
        if (taskPreflightResults) taskPreflightResults.replaceChildren();
        if (taskPreflightSummary) taskPreflightSummary.textContent = 'Running preflight checks…';
        if (taskPreflightSubtitle) taskPreflightSubtitle.textContent = taskName
            ? `Read-only validation for ${taskName}. No Task or file-transfer changes will be made.`
            : 'Validate the Task configuration and dependencies without running the Task.';
    }

    function appendTaskPreflightCheck(stage, check) {
        if (!taskPreflightResults) return;
        const row = document.createElement('tr');
        const status = String(check?.status || 'unknown').toLowerCase();
        const statusCell = document.createElement('td');
        const badge = document.createElement('span');
        badge.className = `task-preflight-result-badge is-${status}`;
        badge.textContent = status === 'pass' ? 'Pass' : status === 'warning' ? 'Warning' : status === 'fail' ? 'Error' : status === 'info' ? 'Info' : 'Unknown';
        statusCell.appendChild(badge);
        const titleCell = document.createElement('td');
        titleCell.textContent = `${taskPreflightStageNames[stage] || stage}: ${String(check?.title || 'Check')}`;
        const messageCell = document.createElement('td');
        messageCell.textContent = String(check?.message || '');
        row.append(statusCell, titleCell, messageCell);
        taskPreflightResults.appendChild(row);
        if (['fail', 'warning', 'unknown'].includes(status)) {
            const kind = status === 'fail' ? 'error' : 'warning';
            const text = `${titleCell.textContent}: ${messageCell.textContent}`;
            const summary = status === 'fail' ? 'Preflight found an error. See MAUI logs for details.'
                : status === 'warning' ? 'Preflight found a warning. See MAUI logs for details.'
                : 'Preflight could not complete a check. See MAUI logs for details.';
            if (!window.MauiModalUI?.showMessage(text, kind, taskPreflightDialog, 'modal', summary)) window.mauiLogNotification?.(text, kind, 'modal');
        }
    }

    function updateTaskPreflightSummary() {
        const rows = Array.from(taskPreflightResults?.querySelectorAll('.task-preflight-result-badge') || []);
        const count = value => rows.filter(row => row.classList.contains(`is-${value}`)).length;
        const failures = count('fail');
        const warnings = count('warning');
        const unknown = count('unknown');
        const passed = count('pass');
        const info = count('info');
        const parts = [`${passed} passed`];
        if (warnings) parts.push(`${warnings} warning${warnings === 1 ? '' : 's'}`);
        if (failures) parts.push(`${failures} error${failures === 1 ? '' : 's'}`);
        if (unknown) parts.push(`${unknown} unknown`);
        if (info) parts.push(`${info} informational`);
        if (taskPreflightSummary) taskPreflightSummary.textContent = `Completed: ${parts.join(' · ')}.`;
    }

    async function runTaskPreflight() {
        if (taskPreflightBusy || !activeTaskPreflight?.taskId) return;
        taskPreflightBusy = true;
        resetTaskPreflightUi(activeTaskPreflight.taskName);
        if (taskPreflightRunAgain) {
            taskPreflightRunAgain.disabled = true;
            taskPreflightRunAgain.textContent = 'Checking...';
        }
        taskPreflightCloseButtons.forEach(button => { button.disabled = true; });

        try {
            for (const stage of taskPreflightStageOrder) {
                setTaskPreflightStageState(stage, 'active');
                try {
                    const data = await fetchJson(endpoint('taskPreflightUrl'), {
                        method: 'POST',
                        body: JSON.stringify({ taskId: activeTaskPreflight.taskId, stage })
                    });
                    const returnedName = String(data?.taskName || '').trim();
                    if (returnedName) {
                        activeTaskPreflight.taskName = returnedName;
                        if (taskPreflightSubtitle) taskPreflightSubtitle.textContent = `Read-only validation for ${returnedName}. No Task or file-transfer changes will be made.`;
                    }
                    const status = ['pass', 'warning', 'fail'].includes(String(data?.status || '').toLowerCase())
                        ? String(data.status).toLowerCase() : 'warning';
                    setTaskPreflightStageState(stage, status);
                    const checks = Array.isArray(data?.checks) ? data.checks : [];
                    if (checks.length === 0) appendTaskPreflightCheck(stage, { status: 'info', title: 'Stage', message: 'No applicable checks were returned.' });
                    else checks.forEach(check => appendTaskPreflightCheck(stage, check));
                } catch (error) {
                    const message = error?.message || 'This preflight stage could not be completed.';
                    const hostError = stage === 'hosts';
                    // Keep transport/timeout failures in the open results table. A failed Host-stage
                    // request must not look like a completed Host test or be reported behind its backdrop.
                    setTaskPreflightStageState(stage, hostError ? 'fail' : 'warning');
                    appendTaskPreflightCheck(stage, { status: hostError ? 'fail' : 'unknown', title: 'Stage', message });
                }
            }
            updateTaskPreflightSummary();
        } finally {
            taskPreflightBusy = false;
            if (taskPreflightRunAgain) {
                taskPreflightRunAgain.disabled = false;
                taskPreflightRunAgain.textContent = 'Run Preflight Again';
            }
            taskPreflightCloseButtons.forEach(button => { button.disabled = false; });
        }
    }

    function openTaskPreflight(taskId, taskName) {
        const id = String(taskId || '').trim();
        if (!id || !taskPreflightDialog) return;
        activeTaskPreflight = { taskId: id, taskName: String(taskName || id).trim() };
        resetTaskPreflightUi(activeTaskPreflight.taskName);
        taskPreflightDialog.showModal();
        runTaskPreflight();
    }

    function closeTaskPreflight() {
        if (taskPreflightBusy) return;
        activeTaskPreflight = null;
        taskPreflightDialog?.close();
    }

    taskPreflightRunAgain?.addEventListener('click', runTaskPreflight);
    taskPreflightCloseButtons.forEach(button => button.addEventListener('click', closeTaskPreflight));
    taskPreflightDialog?.addEventListener('cancel', event => { if (taskPreflightBusy) event.preventDefault(); else activeTaskPreflight = null; });
    taskPreflightDialog?.addEventListener('close', () => { if (!taskPreflightBusy) activeTaskPreflight = null; });

    // ---------------------------------------------------------------------
    // Traditional Task email Notifications
    // ---------------------------------------------------------------------
    const notificationDialog = document.querySelector('[data-task-notification-dialog]');
    const notificationSubtitle = notificationDialog?.querySelector('[data-task-notification-subtitle]');
    const notificationError = notificationDialog?.querySelector('[data-task-notification-error]');
    const notificationWarning = notificationDialog?.querySelector('[data-task-notification-warning]');
    const notificationMultiWarning = notificationDialog?.querySelector('[data-task-notification-multi-warning]');
    const notificationConfig = notificationDialog?.querySelector('[data-task-notification-config]');
    const notificationMessageFields = notificationDialog?.querySelector('[data-task-notification-message-fields]');
    const notificationResultsFieldset = notificationDialog?.querySelector('[data-notification-results-fieldset]');
    const notificationExistingRow = notificationDialog?.querySelector('[data-notification-existing-row]');
    const notificationExistingLoading = notificationDialog?.querySelector('[data-notification-existing-loading]');
    const notificationExistingSelect = notificationDialog?.querySelector('[data-notification-existing]');
    const notificationFailure = notificationDialog?.querySelector('[data-notify-failure]');
    const notificationNoAction = notificationDialog?.querySelector('[data-notify-no-action]');
    const notificationSuccess = notificationDialog?.querySelector('[data-notify-success]');
    const notificationFormat = notificationDialog?.querySelector('[data-notification-format]');
    const notificationHost = notificationDialog?.querySelector('[data-notification-host]');
    const notificationRecipients = notificationDialog?.querySelector('[data-notification-recipients]');
    const notificationPreserveRecipients = notificationDialog?.querySelector('[data-notification-preserve-recipients]');
    const notificationSubject = notificationDialog?.querySelector('[data-notification-subject]');
    const notificationHtmlToolbar = notificationDialog?.querySelector('[data-notification-html-toolbar]');
    const notificationHtmlEditor = notificationDialog?.querySelector('[data-notification-html-editor]');
    const notificationHtmlSource = notificationDialog?.querySelector('[data-notification-html-source]');
    const notificationHtmlToggle = notificationDialog?.querySelector('[data-notification-html-toggle]');
    const notificationTextEditor = notificationDialog?.querySelector('[data-notification-text-editor]');
    const notificationTemplateButton = notificationDialog?.querySelector('[data-notification-template-button]');
    const notificationTemplateMenu = notificationDialog?.querySelector('[data-notification-template-menu]');
    const notificationTemplateSave = notificationDialog?.querySelector('[data-notification-template-save]');
    const notificationTemplateSaveDialog = document.querySelector('[data-notification-template-save-dialog]');
    const notificationTemplateSaveName = notificationTemplateSaveDialog?.querySelector('[data-notification-template-save-name]');
    const notificationTemplateOverwriteRow = notificationTemplateSaveDialog?.querySelector('[data-notification-template-overwrite-row]');
    const notificationTemplateOverwrite = notificationTemplateSaveDialog?.querySelector('[data-notification-template-overwrite]');
    const notificationTemplateOverwriteName = notificationTemplateSaveDialog?.querySelector('[data-notification-template-overwrite-name]');
    const notificationTemplateSaveError = notificationTemplateSaveDialog?.querySelector('[data-notification-template-save-error]');
    const notificationTemplateSaveConfirm = notificationTemplateSaveDialog?.querySelector('[data-notification-template-save-confirm]');
    const notificationTemplateSaveCancel = notificationTemplateSaveDialog?.querySelector('[data-notification-template-save-cancel]');
    const notificationTemplateSaveClose = notificationTemplateSaveDialog?.querySelector('[data-notification-template-save-close]');
    const notificationConfirmDialog = document.querySelector('[data-task-notification-confirm-dialog]');
    const notificationConfirmMessage = notificationConfirmDialog?.querySelector('[data-task-notification-confirm-message]');
    const notificationConfirmContinue = notificationConfirmDialog?.querySelector('[data-task-notification-confirm-continue]');
    const notificationConfirmCancelButtons = Array.from(notificationConfirmDialog?.querySelectorAll('[data-task-notification-confirm-cancel]') || []);
    const notificationLinkDialog = document.querySelector('[data-notification-link-dialog]');
    const notificationLinkUrl = notificationLinkDialog?.querySelector('[data-notification-link-url]');
    const notificationLinkError = notificationLinkDialog?.querySelector('[data-notification-link-error]');
    const notificationLinkConfirm = notificationLinkDialog?.querySelector('[data-notification-link-confirm]');
    const notificationLinkCancelButtons = Array.from(notificationLinkDialog?.querySelectorAll('[data-notification-link-cancel]') || []);
    const notificationApply = notificationDialog?.querySelector('[data-task-notification-apply]');
    const notificationCancel = notificationDialog?.querySelector('[data-task-notification-cancel]');
    const notificationClose = notificationDialog?.querySelector('[data-task-notification-close]');
    const notificationProgress = notificationDialog?.querySelector('[data-task-notification-progress]');
    const notificationProgressTitle = notificationDialog?.querySelector('[data-notification-progress-title]');
    const notificationProgressCount = notificationDialog?.querySelector('[data-notification-progress-count]');
    const notificationProgressBar = notificationDialog?.querySelector('[data-notification-progress-bar]');
    const notificationCurrent = notificationDialog?.querySelector('[data-notification-current]');
    const notificationSummary = notificationDialog?.querySelector('[data-notification-summary]');
    const notificationResults = notificationDialog?.querySelector('[data-notification-results]');
    const notificationModeInputs = Array.from(notificationDialog?.querySelectorAll('input[name="taskNotificationMode"]') || []);
    const notificationRemoveMode = notificationModeInputs.find(input => input.value === 'remove');
    const notificationMacroSelects = Array.from(notificationDialog?.querySelectorAll('.task-notification-macro') || []);
    const notificationResultInputs = [notificationFailure, notificationNoAction, notificationSuccess].filter(Boolean);
    const notificationVisualHtmlControls = Array.from(notificationDialog?.querySelectorAll('[data-notification-command], [data-notification-link]') || []);
    const notificationHtmlOnlyControls = [...notificationVisualHtmlControls, notificationHtmlToggle].filter(Boolean);
    let notificationTargets = [];
    let notificationInitialSkipped = [];
    let notificationExisting = [];
    let notificationTemplates = [];
    let notificationSelectedTemplateId = '';
    let notificationSingleTask = false;
    let notificationBusy = false;
    let notificationTemplateSaving = false;
    const updateNotificationTemplateAction = window.MauiModalUI.watchAction(notificationTemplateSaveDialog, notificationTemplateSaveConfirm,
        () => canSaveTemplates && !notificationBusy && !notificationTemplateSaving,
        () => Boolean(selectedNotificationTemplate() && notificationTemplateOverwrite?.checked || notificationTemplateSaveName?.value.trim()));
    let editorNotificationTemplateContext = null;
    let pendingNotificationBulkApply = null;
    let notificationLastFormat = 'html';
    let notificationHtmlSourceMode = false;
    let notificationDefaultHostId = '';
    let notificationHtmlRange = null;

    const notificationMacros = [
        ['Task', '[TaskID]'], ['Task', '[TaskName]'], ['Task', '[TaskStatus]'],
        ['Errors', '[ErrorCodeTask]'], ['Errors', '[ErrorDescriptionTask]'], ['Errors', '[ErrorCodeFile]'], ['Errors', '[ErrorDescriptionFile]'],
        ['File', '[OrigName]'], ['File', '[OnlyName]'], ['File', '[OnlyExt]'], ['File', '[FullPath]'], ['File', '[DestFileName]'], ['File', '[DestFolderPath]'], ['File', '[FileSize]'], ['File', '[FileDateStamp]'],
        ['Run', '[NominalStart]'],
        ['Date/Time', '[yyyy]'], ['Date/Time', '[mm]'], ['Date/Time', '[dd]'], ['Date/Time', '[hh]'], ['Date/Time', '[tt]'], ['Date/Time', '[ss]']
    ];

    function buildNotificationMacroOptions(select) {
        if (!select) return;
        select.replaceChildren();
        const first = document.createElement('option'); first.value = ''; first.textContent = 'Macros…'; select.appendChild(first);
        let currentGroup = null; let group = null;
        notificationMacros.forEach(([category, macro]) => {
            if (category !== currentGroup) { group = document.createElement('optgroup'); group.label = category; select.appendChild(group); currentGroup = category; }
            const option = document.createElement('option'); option.value = macro; option.textContent = macro; group.appendChild(option);
        });
    }
    notificationMacroSelects.forEach(buildNotificationMacroOptions);

    function selectedNotificationMode() { return notificationModeInputs.find(input => input.checked)?.value || 'upsert'; }
    function setNotificationExistingLoading(loading) {
        const isLoading = Boolean(loading);
        if (notificationExistingLoading) notificationExistingLoading.hidden = !isLoading;
        if (notificationExistingSelect) notificationExistingSelect.hidden = isLoading;
    }
    function selectedExistingNotificationIndex() {
        if (!notificationSingleTask) return null;
        const raw = String(notificationExistingSelect?.value || '').trim();
        if (!raw) return null;
        const value = Number.parseInt(raw, 10);
        return Number.isInteger(value) && value >= 0 ? value : null;
    }
    function updateNotificationModeUi() {
        const hasExistingSelection = selectedExistingNotificationIndex() !== null;
        if (notificationRemoveMode) notificationRemoveMode.disabled = notificationSingleTask && !hasExistingSelection;
        if (notificationSingleTask && !hasExistingSelection && selectedNotificationMode() === 'remove') {
            const upsert = notificationModeInputs.find(input => input.value === 'upsert');
            if (upsert) upsert.checked = true;
        }
        const remove = selectedNotificationMode() === 'remove';
        const disableResults = remove && notificationSingleTask;
        notificationResultInputs.forEach(input => input.disabled = disableResults);
        [notificationFormat, notificationHost, notificationRecipients, notificationSubject, notificationTextEditor, notificationHtmlSource]
            .filter(Boolean)
            .forEach(control => control.disabled = remove);
        if (notificationPreserveRecipients) {
            const createNew = notificationSingleTask && !hasExistingSelection;
            if (createNew) notificationPreserveRecipients.checked = false;
            notificationPreserveRecipients.disabled = remove || createNew;
        }
        notificationMacroSelects.forEach(control => control.disabled = remove);
        notificationVisualHtmlControls.forEach(control => control.disabled = remove || notificationHtmlSourceMode);
        if (notificationHtmlToggle) notificationHtmlToggle.disabled = remove;
        if (notificationHtmlEditor) {
            notificationHtmlEditor.contentEditable = remove || notificationHtmlSourceMode ? 'false' : 'true';
            notificationHtmlEditor.setAttribute('aria-disabled', remove ? 'true' : 'false');
        }
        notificationResultsFieldset?.classList.toggle('is-disabled', disableResults);
        notificationMessageFields?.classList.toggle('is-disabled', remove);
        if (notificationApply) notificationApply.textContent = remove ? 'Remove' : 'Add/Update';
        if (notificationTemplateButton) notificationTemplateButton.disabled = remove;
        if (notificationTemplateSave) notificationTemplateSave.disabled = remove || !canSaveTemplates;
        if (remove && notificationTemplateMenu) notificationTemplateMenu.hidden = true;
    }

    function sanitizeNotificationStyle(styleText) {
        const allowedProperties = new Set([
            'color', 'background-color', 'font-family', 'font-size', 'font-weight', 'font-style',
            'text-decoration', 'text-align', 'vertical-align', 'line-height', 'white-space',
            'width', 'min-width', 'max-width', 'height',
            'border', 'border-width', 'border-style', 'border-color', 'border-collapse', 'border-spacing',
            'padding', 'padding-top', 'padding-right', 'padding-bottom', 'padding-left',
            'margin', 'margin-top', 'margin-right', 'margin-bottom', 'margin-left'
        ]);
        const probe = document.createElement('span');
        probe.style.cssText = String(styleText || '');
        const safe = [];
        Array.from(probe.style).forEach(property => {
            const normalized = String(property || '').toLowerCase();
            if (!allowedProperties.has(normalized)) return;
            const value = String(probe.style.getPropertyValue(property) || '').trim();
            if (!value || /(?:url\s*\(|expression\s*\(|javascript:|@import|behavior\s*:)/i.test(value)) return;
            safe.push(`${normalized}: ${value}`);
        });
        return safe.join('; ');
    }

    function parseNotificationHtml(html) {
        // Parse into a detached template-context fragment so incomplete HTML email fragments
        // (including table rows/cells) retain their structure without touching the live DOM.
        const context = document.createElement('template');
        const range = document.createRange();
        range.selectNodeContents(context);
        return range.createContextualFragment(String(html || ''));
    }

    function sanitizeNotificationHtml(html) {
        const root = parseNotificationHtml(html);
        const allowed = new Set(['P','DIV','SPAN','BR','STRONG','B','EM','I','U','UL','OL','LI','A','TABLE','THEAD','TBODY','TFOOT','TR','TH','TD']);
        const numericAttributes = new Set(['colspan', 'rowspan', 'cellpadding', 'cellspacing', 'border']);
        const dimensionAttributes = new Set(['width', 'height']);
        const clean = node => {
            Array.from(node.children).forEach(child => {
                clean(child);
                if (!allowed.has(child.tagName)) { child.replaceWith(...Array.from(child.childNodes)); return; }
                Array.from(child.attributes).forEach(attr => {
                    const name = attr.name.toLowerCase();
                    const value = String(attr.value || '').trim();
                    if (child.tagName === 'A' && name === 'href') {
                        if (!/^(https?:|mailto:)/i.test(value)) child.removeAttribute(attr.name);
                        return;
                    }
                    if (name === 'style') {
                        const safeStyle = sanitizeNotificationStyle(value);
                        if (safeStyle) child.setAttribute('style', safeStyle); else child.removeAttribute(attr.name);
                        return;
                    }
                    if (numericAttributes.has(name) && ['TABLE','TH','TD'].includes(child.tagName)) {
                        if (!/^\d{1,4}$/.test(value)) child.removeAttribute(attr.name);
                        return;
                    }
                    if (dimensionAttributes.has(name) && ['TABLE','TH','TD'].includes(child.tagName)) {
                        if (!/^\d{1,4}(?:px|%)?$/i.test(value)) child.removeAttribute(attr.name);
                        return;
                    }
                    if (name === 'align' && ['TABLE','TR','TH','TD','P','DIV'].includes(child.tagName)) {
                        if (!/^(left|center|right|justify)$/i.test(value)) child.removeAttribute(attr.name);
                        return;
                    }
                    if (name === 'valign' && ['TR','TH','TD'].includes(child.tagName)) {
                        if (!/^(top|middle|bottom|baseline)$/i.test(value)) child.removeAttribute(attr.name);
                        return;
                    }
                    child.removeAttribute(attr.name);
                });
            });
        };
        clean(root);

        const serialized = document.createElement('div');
        Array.from(root.childNodes).forEach(node => serialized.appendChild(document.importNode(node, true)));
        return serialized.innerHTML;
    }

    function replaceNotificationHtmlEditorContent(html) {
        if (!notificationHtmlEditor) return;
        const root = parseNotificationHtml(html);
        const fragment = document.createDocumentFragment();
        Array.from(root.childNodes).forEach(node => fragment.appendChild(document.importNode(node, true)));
        notificationHtmlEditor.replaceChildren(fragment);
    }

    function setNotificationHtmlSourceMode(sourceMode, focus = true) {
        if (!notificationHtmlEditor || !notificationHtmlSource) return;
        if (sourceMode) {
            notificationHtmlSource.value = sanitizeNotificationHtml(notificationHtmlEditor.innerHTML || '');
        } else {
            const sanitized = sanitizeNotificationHtml(notificationHtmlSource.value || '');
            notificationHtmlSource.value = sanitized;
            replaceNotificationHtmlEditorContent(sanitized);
        }
        notificationHtmlSourceMode = Boolean(sourceMode);
        notificationHtmlEditor.hidden = notificationLastFormat !== 'html' || notificationHtmlSourceMode;
        notificationHtmlSource.hidden = notificationLastFormat !== 'html' || !notificationHtmlSourceMode;
        if (notificationHtmlToggle) {
            notificationHtmlToggle.textContent = notificationHtmlSourceMode ? 'Visual' : 'Source';
            notificationHtmlToggle.title = notificationHtmlSourceMode ? 'Return to visual editor' : 'Edit HTML source';
            notificationHtmlToggle.classList.toggle('is-active', notificationHtmlSourceMode);
        }
        updateNotificationModeUi();
        if (focus) {
            if (notificationHtmlSourceMode) notificationHtmlSource.focus();
            else { notificationHtmlEditor.focus(); rememberNotificationHtmlRange(); }
        }
    }

    function setNotificationHtmlContent(html) {
        const sanitized = sanitizeNotificationHtml(html);
        replaceNotificationHtmlEditorContent(sanitized);
        if (notificationHtmlSource) notificationHtmlSource.value = sanitized;
    }

    function currentNotificationHtml() {
        const sanitized = sanitizeNotificationHtml(notificationHtmlSourceMode ? notificationHtmlSource?.value || '' : notificationHtmlEditor?.innerHTML || '');
        if (notificationHtmlSourceMode && notificationHtmlSource) notificationHtmlSource.value = sanitized;
        return sanitized;
    }

    function setNotificationFormatUi(convert = false) {
        const html = notificationFormat?.value !== 'text';
        if (convert && notificationLastFormat === 'html' && !html && notificationTextEditor && notificationHtmlEditor) {
            if (notificationHtmlSourceMode) setNotificationHtmlSourceMode(false, false);
            notificationTextEditor.value = notificationHtmlEditor.innerText || '';
        }
        if (convert && notificationLastFormat === 'text' && html && notificationHtmlEditor && notificationTextEditor) {
            notificationHtmlEditor.replaceChildren();
            String(notificationTextEditor.value || '').split(/\r?\n/).forEach((line, index) => { if (index) notificationHtmlEditor.appendChild(document.createElement('br')); notificationHtmlEditor.appendChild(document.createTextNode(line)); });
            if (notificationHtmlSource) notificationHtmlSource.value = sanitizeNotificationHtml(notificationHtmlEditor.innerHTML || '');
        }
        notificationLastFormat = html ? 'html' : 'text';
        if (notificationHtmlToolbar) notificationHtmlToolbar.hidden = false;
        notificationHtmlOnlyControls.forEach(control => control.hidden = !html);
        if (notificationHtmlEditor) notificationHtmlEditor.hidden = !html || notificationHtmlSourceMode;
        if (notificationHtmlSource) notificationHtmlSource.hidden = !html || !notificationHtmlSourceMode;
        if (notificationTextEditor) notificationTextEditor.hidden = html;
        if (notificationHtmlToggle) {
            notificationHtmlToggle.textContent = notificationHtmlSourceMode ? 'Visual' : 'Source';
            notificationHtmlToggle.title = notificationHtmlSourceMode ? 'Return to visual editor' : 'Edit HTML source';
            notificationHtmlToggle.classList.toggle('is-active', notificationHtmlSourceMode);
        }
        updateNotificationModeUi();
    }

    function insertTextAtCaret(input, text) {
        if (!input) return;
        const start = Number.isFinite(input.selectionStart) ? input.selectionStart : input.value.length;
        const end = Number.isFinite(input.selectionEnd) ? input.selectionEnd : start;
        input.setRangeText(text, start, end, 'end'); input.focus();
    }

    function rememberNotificationHtmlRange() {
        const selection = window.getSelection();
        if (selection?.rangeCount && notificationHtmlEditor?.contains(selection.anchorNode)) notificationHtmlRange = selection.getRangeAt(0).cloneRange();
    }

    function restoreNotificationHtmlRange() {
        if (!notificationHtmlEditor) return false;
        notificationHtmlEditor.focus();
        const selection = window.getSelection();
        if (!selection || !notificationHtmlRange) return false;
        try { selection.removeAllRanges(); selection.addRange(notificationHtmlRange); return true; } catch { return false; }
    }

    function insertHtmlMacro(text) {
        if (!notificationHtmlEditor) return;
        restoreNotificationHtmlRange();
        const selection = window.getSelection();
        if (!selection?.rangeCount || !notificationHtmlEditor.contains(selection.anchorNode)) { notificationHtmlEditor.append(document.createTextNode(text)); rememberNotificationHtmlRange(); return; }
        const range = selection.getRangeAt(0); range.deleteContents(); const node = document.createTextNode(text); range.insertNode(node); range.setStartAfter(node); range.collapse(true); selection.removeAllRanges(); selection.addRange(range); rememberNotificationHtmlRange();
    }

    function resetNotificationProgress() {
        if (notificationProgress) notificationProgress.hidden = true;
        if (notificationProgressTitle) notificationProgressTitle.textContent = 'Updating Notifications';
        if (notificationProgressCount) notificationProgressCount.textContent = '';
        if (notificationProgressBar) notificationProgressBar.value = 0;
        if (notificationCurrent) notificationCurrent.textContent = '';
        if (notificationSummary) notificationSummary.textContent = '';
        if (notificationResults) notificationResults.replaceChildren();
    }

    function setDefaultNotificationResults() {
        if (notificationFailure) notificationFailure.checked = true;
        if (notificationNoAction) notificationNoAction.checked = false;
        if (notificationSuccess) notificationSuccess.checked = false;
    }

    function selectedNotificationTemplate() {
        const selectedId = String(editorNotificationTemplateContext?.draft?.templateId ?? notificationSelectedTemplateId ?? '').trim();
        return selectedId ? notificationTemplates.find(item => String(item?.id || '') === selectedId) || null : null;
    }

    // Templates saved before Format metadata existed remain compatible. For those templates,
    // infer HTML only from fragment tags that the MAUI notification editor itself supports.
    // Unknown angle-bracket tokens such as <filename> therefore remain plain text.
    function resolveNotificationTemplateFormat(item) {
        const explicit = String(item?.format || '').trim().toLowerCase();
        if (explicit === 'html' || explicit === 'text') return explicit;
        const message = String(item?.message || '');
        const supportedHtmlTag = /<\/?(?:p|div|span|br|strong|b|em|i|u|ul|ol|li|a|table|thead|tbody|tfoot|tr|th|td)\b[^>]*>/i;
        return supportedHtmlTag.test(message) ? 'html' : 'text';
    }

    function applyNotificationTemplate(item) {
        const subject = String(item?.subject || '');
        const message = String(item?.message || '');
        const format = resolveNotificationTemplateFormat(item);

        // A template replaces the body, so switch modes without converting the previous body.
        // This avoids encoding raw HTML as text before it reaches the HTML editor.
        if (notificationFormat) notificationFormat.value = format;
        notificationLastFormat = format;
        notificationHtmlSourceMode = false;
        setNotificationFormatUi(false);

        if (notificationSubject) notificationSubject.value = subject;
        if (format === 'html') {
            if (notificationTextEditor) notificationTextEditor.value = '';
            setNotificationHtmlContent(message);
            rememberNotificationHtmlRange();
        } else {
            if (notificationTextEditor) notificationTextEditor.value = message;
            setNotificationHtmlContent('');
        }
    }

    function clearSelectedNotificationTemplate() {
        notificationSelectedTemplateId = '';
        if (notificationTemplateButton) notificationTemplateButton.title = 'Load a saved Subject and Message Body template';
    }

    function clearUnavailableNotificationHostWarning() {
        if (!notificationWarning || notificationWarning.hidden) return;
        if (String(notificationWarning.textContent || '').startsWith('The selected notification references an SMTP Host')) {
            notificationWarning.hidden = true;
            notificationWarning.textContent = '';
        }
    }

    function currentNotificationMessage() {
        return notificationFormat?.value === 'text'
            ? String(notificationTextEditor?.value || '')
            : currentNotificationHtml();
    }

    function clearNotificationMessageFields() {
        clearSelectedNotificationTemplate();
        notificationLastFormat = 'html';
        notificationHtmlSourceMode = false;
        if (notificationFormat) notificationFormat.value = 'html';
        if (notificationHost) notificationHost.value = notificationDefaultHostId;
        if (notificationRecipients) notificationRecipients.value = '';
        if (notificationPreserveRecipients) notificationPreserveRecipients.checked = false;
        if (notificationSubject) notificationSubject.value = '';
        if (notificationHtmlEditor) notificationHtmlEditor.replaceChildren();
        if (notificationHtmlSource) notificationHtmlSource.value = '';
        if (notificationTextEditor) notificationTextEditor.value = '';
        setNotificationFormatUi();
    }

    function applyNotificationValues(item, setResults = true) {
        if (!item) { clearNotificationMessageFields(); return; }
        clearSelectedNotificationTemplate();
        if (setResults) {
            if (notificationFailure) notificationFailure.checked = Boolean(item.doIfFailure);
            if (notificationNoAction) notificationNoAction.checked = Boolean(item.doIfNoAction);
            if (notificationSuccess) notificationSuccess.checked = Boolean(item.doIfSuccess);
        }
        if (notificationFormat) notificationFormat.value = item.isHtml ? 'html' : 'text';
        if (notificationHost) notificationHost.value = String(item.hostId || '');
        if (notificationRecipients) notificationRecipients.value = String(item.addressTo || '');
        if (notificationSubject) notificationSubject.value = String(item.subject || '');
        notificationHtmlSourceMode = false;
        setNotificationHtmlContent(item.isHtml ? item.message || '' : '');
        if (notificationTextEditor) notificationTextEditor.value = item.isHtml ? '' : String(item.message || '');
        notificationLastFormat = item.isHtml ? 'html' : 'text';
        setNotificationFormatUi();
    }

    function selectedNotificationResults() {
        return {
            failure: Boolean(notificationFailure?.checked),
            noAction: Boolean(notificationNoAction?.checked),
            success: Boolean(notificationSuccess?.checked)
        };
    }

    function notificationResultLabel(item) {
        const labels = [];
        if (item?.doIfFailure) labels.push('Failure');
        if (item?.doIfNoAction) labels.push('No Transfer');
        if (item?.doIfSuccess) labels.push('Success');
        return labels.length ? labels.join(' + ') : 'No result selected';
    }

    function populateExistingNotifications(items) {
        notificationExisting = Array.isArray(items) ? items.slice() : [];
        if (!notificationExistingSelect) return;
        notificationExistingSelect.replaceChildren();
        const createNew = document.createElement('option'); createNew.value = ''; createNew.textContent = 'Create New'; notificationExistingSelect.appendChild(createNew);
        notificationExisting.forEach((item, ordinal) => {
            const option = document.createElement('option');
            option.value = String(item.actionIndex);
            const subject = String(item.subject || '').trim();
            option.textContent = `Notification ${ordinal + 1}: ${notificationResultLabel(item)}${subject ? ` — ${subject}` : ''}`;
            notificationExistingSelect.appendChild(option);
        });
        notificationExistingSelect.value = '';
    }

    function populateNotificationTemplates(items) {
        notificationTemplates = Array.isArray(items) ? items.slice().sort((a, b) => String(a?.name || '').localeCompare(String(b?.name || ''), undefined, { sensitivity: 'base' })) : [];
        if (notificationSelectedTemplateId && !notificationTemplates.some(item => String(item?.id || '') === notificationSelectedTemplateId)) clearSelectedNotificationTemplate();
        if (!notificationTemplateMenu) return;
        notificationTemplateMenu.replaceChildren();
        if (!notificationTemplates.length) {
            const empty = document.createElement('div'); empty.className = 'task-notification-template-empty'; empty.textContent = 'No saved templates'; notificationTemplateMenu.appendChild(empty); return;
        }
        notificationTemplates.forEach(item => {
            const button = document.createElement('button'); button.type = 'button'; button.setAttribute('role', 'menuitem'); button.textContent = String(item.name || item.id || 'Template');
            button.addEventListener('click', () => {
                notificationSelectedTemplateId = String(item.id || '');
                if (notificationTemplateButton) notificationTemplateButton.title = `Loaded template: ${String(item.name || 'Template')}`;
                applyNotificationTemplate(item);
                notificationTemplateMenu.hidden = true;
                if (notificationError) notificationError.hidden = true;
            });
            notificationTemplateMenu.appendChild(button);
        });
    }

    function resetNotificationDialog() {
        notificationTargets = []; notificationInitialSkipped = []; notificationExisting = []; notificationTemplates = []; notificationSelectedTemplateId = ''; notificationSingleTask = false; notificationBusy = false; notificationTemplateSaving = false; notificationDefaultHostId = ''; notificationHtmlSourceMode = false; notificationHtmlRange = null;
        if (notificationError) { notificationError.hidden = true; notificationError.textContent = ''; }
        if (notificationWarning) { notificationWarning.hidden = true; notificationWarning.textContent = ''; }
        if (notificationMultiWarning) { notificationMultiWarning.hidden = true; notificationMultiWarning.textContent = ''; }
        setDefaultNotificationResults();
        notificationModeInputs.forEach(input => { input.checked = input.value === 'upsert'; input.disabled = false; });
        if (notificationExistingRow) notificationExistingRow.hidden = true;
        setNotificationExistingLoading(false);
        populateExistingNotifications([]);
        populateNotificationTemplates([]);
        if (notificationTemplateMenu) notificationTemplateMenu.hidden = true;
        if (notificationTemplateSaveDialog?.open) notificationTemplateSaveDialog.close();
        if (notificationConfirmDialog?.open) notificationConfirmDialog.close();
        if (notificationLinkDialog?.open) notificationLinkDialog.close();
        pendingNotificationBulkApply = null;
        clearNotificationMessageFields();
        if (notificationConfig) notificationConfig.hidden = false; resetNotificationProgress();
        if (notificationApply) { notificationApply.hidden = false; notificationApply.disabled = false; notificationApply.textContent = 'Add/Update'; }
        if (notificationCancel) { notificationCancel.disabled = false; notificationCancel.textContent = 'Close'; } if (notificationClose) notificationClose.disabled = false;
        updateNotificationModeUi();
    }

    async function openTaskNotifications(ids) {
        const selectedIds = normalizeSelectedTaskIds(ids); if (!selectedIds.length || notificationBusy) return;
        resetNotificationDialog();
        notificationSingleTask = selectedIds.length === 1;
        updateNotificationModeUi();
        if (notificationSubtitle) notificationSubtitle.textContent = notificationSingleTask ? 'Manage task-level email notifications for this Traditional Task.' : `Apply one task-level email notification configuration to ${selectedIds.length} selected Tasks.`;
        if (notificationExistingRow) notificationExistingRow.hidden = !notificationSingleTask;
        setNotificationExistingLoading(notificationSingleTask);
        notificationDialog?.showModal();
        if (notificationApply) notificationApply.disabled = true;
        try {
            const setup = await fetchJson(endpoint('notificationSetupUrl'), { method: 'POST', body: JSON.stringify({ taskIds: selectedIds }) });
            notificationTargets = Array.isArray(setup?.eligible) ? setup.eligible : []; notificationInitialSkipped = Array.isArray(setup?.skipped) ? setup.skipped : [];
            const smtpHosts = Array.isArray(setup?.smtpHosts) ? setup.smtpHosts : [];
            notificationDefaultHostId = smtpHosts.length === 1 ? String(smtpHosts[0]?.id || '') : '';
            if (notificationHost) {
                notificationHost.replaceChildren();
                const blank = document.createElement('option'); blank.value=''; blank.textContent='Select SMTP Host'; notificationHost.appendChild(blank);
                smtpHosts.forEach(host => { const option=document.createElement('option'); option.value=String(host.id||''); option.textContent=String(host.name||host.id||''); notificationHost.appendChild(option); });
                if (notificationDefaultHostId) notificationHost.value = notificationDefaultHostId;
            }
            populateNotificationTemplates(setup?.templates || []);
            if (!notificationTargets.length) {
                setNotificationExistingLoading(false);
                if (notificationExistingRow) notificationExistingRow.hidden = true;
                if (notificationError) { notificationError.textContent = notificationInitialSkipped[0]?.reason || 'No selected Traditional Tasks can be updated.'; notificationError.hidden = false; }
                return;
            }

            if (notificationSingleTask) {
                populateExistingNotifications(Array.isArray(setup?.existing?.notifications) ? setup.existing.notifications : []);
                setNotificationExistingLoading(false);
                setDefaultNotificationResults();
                clearNotificationMessageFields();
            } else {
                if (notificationExistingRow) notificationExistingRow.hidden = true;
                if (notificationMultiWarning) {
                    notificationMultiWarning.textContent = `This operation will affect ${notificationTargets.length} Task${notificationTargets.length === 1 ? '' : 's'}. Each Task will be evaluated independently. Matching notification types will be added, updated, or removed across the selected Tasks according to each Task's current notification configuration and the operation selected.`;
                    notificationMultiWarning.hidden = false;
                }
            }
            if (notificationInitialSkipped.length && notificationWarning) { notificationWarning.textContent = `${notificationInitialSkipped.length} selected Task(s) are not Traditional or are unavailable and will be skipped.`; notificationWarning.hidden = false; }
            if (!smtpHosts.length && notificationWarning) { notificationWarning.textContent = 'No SMTP Hosts are currently available in MOVEit. Add/Update requires an SMTP Host.'; notificationWarning.hidden = false; }
            updateNotificationModeUi();
            if (notificationApply) notificationApply.disabled = false;
        } catch (error) {
            setNotificationExistingLoading(false);
            if (notificationExistingRow) notificationExistingRow.hidden = true;
            if (notificationError) { notificationError.textContent = error?.message || 'Unable to load notification settings.'; notificationError.hidden = false; }
        }
    }

    function currentNotificationPayload(mode = selectedNotificationMode()) {
        const selected = selectedNotificationResults();
        const isHtml = notificationFormat?.value !== 'text';
        return {
            mode,
            actionIndex: selectedExistingNotificationIndex(),
            matchExactResults: !notificationSingleTask,
            doIfFailure: selected.failure,
            doIfNoAction: selected.noAction,
            doIfSuccess: selected.success,
            isHtml,
            hostId: String(notificationHost?.value || ''),
            addressTo: String(notificationRecipients?.value || ''),
            preserveExistingRecipients: Boolean(notificationPreserveRecipients?.checked),
            subject: String(notificationSubject?.value || ''),
            message: isHtml ? currentNotificationHtml() : String(notificationTextEditor?.value || '')
        };
    }

    function notificationTemplateDraft() {
        return editorNotificationTemplateContext?.draft || {
            subject: String(notificationSubject?.value || ''), message: currentNotificationMessage(),
            format: notificationFormat?.value === 'text' ? 'text' : 'html'
        };
    }

    function finishEditorNotificationTemplateSave(result) {
        const context = editorNotificationTemplateContext;
        editorNotificationTemplateContext = null;
        context?.resolve(result);
    }

    function openEditorNotificationTemplateSave(draft) {
        if (!canSaveTemplates || !notificationTemplateSaveDialog || notificationTemplateSaving || notificationBusy || editorNotificationTemplateContext) return Promise.resolve(null);
        populateNotificationTemplates(draft.templates || []);
        return new Promise(resolve => {
            editorNotificationTemplateContext = { draft: { ...draft }, resolve };
            if (!openNotificationTemplateSaveDialog()) finishEditorNotificationTemplateSave(null);
        });
    }

    function updateNotificationTemplateSaveDialogState() {
        const selected = selectedNotificationTemplate();
        const overwrite = Boolean(selected && notificationTemplateOverwrite?.checked);
        if (notificationTemplateSaveName) {
            notificationTemplateSaveName.disabled = overwrite;
            notificationTemplateSaveName.placeholder = overwrite ? String(selected?.name || '') : 'Template name';
        }
        updateNotificationTemplateAction();
    }

    function closeNotificationTemplateSaveDialog() {
        if (notificationTemplateSaving) return;
        if (notificationTemplateSaveDialog?.open) notificationTemplateSaveDialog.close();
        finishEditorNotificationTemplateSave(null);
    }

    function openNotificationTemplateSaveDialog() {
        if (!canSaveTemplates || notificationBusy || notificationTemplateSaving || (!editorNotificationTemplateContext && selectedNotificationMode() === 'remove')) return false;
        const draft = notificationTemplateDraft();
        const subject = String(draft.subject || '').trim();
        const message = String(draft.message || '');
        if (!subject || !message.trim()) {
            showNativeEditorStatus('Subject and Message Body are required before saving a template.', 5000, 'error');
            return;
        }
        const selected = selectedNotificationTemplate();
        if (notificationTemplateSaveError) { notificationTemplateSaveError.hidden = true; notificationTemplateSaveError.textContent = ''; }
        if (notificationTemplateSaveName) { notificationTemplateSaveName.value = ''; notificationTemplateSaveName.disabled = false; }
        if (notificationTemplateOverwrite) notificationTemplateOverwrite.checked = false;
        if (notificationTemplateOverwriteRow) notificationTemplateOverwriteRow.hidden = !selected;
        if (notificationTemplateOverwriteName) notificationTemplateOverwriteName.textContent = selected ? `“${selected.name}”` : '';
        updateNotificationTemplateSaveDialogState();
        notificationTemplateSaveDialog?.showModal();
        notificationTemplateSaveName?.focus();
        return true;
    }

    async function saveNotificationTemplate() {
        if (!canSaveTemplates || notificationBusy || notificationTemplateSaving || (!editorNotificationTemplateContext && selectedNotificationMode() === 'remove')) return false;
        const draft = notificationTemplateDraft();
        const subject = String(draft.subject || '').trim();
        const message = String(draft.message || '');
        const selected = selectedNotificationTemplate();
        const overwriteExisting = Boolean(selected && notificationTemplateOverwrite?.checked);
        const name = overwriteExisting ? '' : String(notificationTemplateSaveName?.value || '').trim();
        if (!subject || !message.trim()) { if (notificationTemplateSaveError) { notificationTemplateSaveError.textContent='Subject and Message Body are required before saving a template.'; notificationTemplateSaveError.hidden=false; } return; }
        if (!overwriteExisting && !name) { if (notificationTemplateSaveError) { notificationTemplateSaveError.textContent='Template Name is required.'; notificationTemplateSaveError.hidden=false; } return; }

        notificationTemplateSaving = true;
        if (notificationTemplateSaveConfirm) notificationTemplateSaveConfirm.disabled = true;
        if (notificationTemplateSaveCancel) notificationTemplateSaveCancel.disabled = true;
        if (notificationTemplateSaveClose) notificationTemplateSaveClose.disabled = true;
        try {
            const data = await fetchJson(endpoint('saveNotificationTemplateUrl'), {
                method:'POST',
                body:JSON.stringify({ name, subject, message, format: draft.format === 'text' ? 'text' : 'html', templateId: selected?.id || '', overwriteExisting })
            });
            const saved = data?.template;
            if (!saved?.id || !saved?.name) throw new Error('Notification template save returned an invalid response.');
            {
                const existingIndex = notificationTemplates.findIndex(item => String(item?.id || '') === String(saved.id || ''));
                if (existingIndex >= 0) notificationTemplates[existingIndex] = saved;
                else notificationTemplates.push(saved);
                notificationSelectedTemplateId = String(saved.id || '');
                populateNotificationTemplates(notificationTemplates);
                if (notificationTemplateButton) notificationTemplateButton.title = `Loaded template: ${String(saved.name || 'Template')}`;
                if (notificationError) notificationError.hidden=true;
                finishEditorNotificationTemplateSave(saved);
                if (notificationTemplateSaveDialog?.open) notificationTemplateSaveDialog.close();
                showNativeEditorStatus(`${overwriteExisting ? 'Updated' : 'Saved'} notification template ${saved.name}.`, 5000);
            }
        } catch (error) {
            if (notificationTemplateSaveError) { notificationTemplateSaveError.textContent=error?.message||'Unable to save notification template.'; notificationTemplateSaveError.hidden=false; }
        } finally {
            notificationTemplateSaving = false;
            updateNotificationTemplateAction();
            if (notificationTemplateSaveCancel) notificationTemplateSaveCancel.disabled = false;
            if (notificationTemplateSaveClose) notificationTemplateSaveClose.disabled = false;
        }
    }

    async function executeTaskNotifications(mode, payloadBase) {
        notificationBusy=true; if (notificationError) notificationError.hidden=true; resetNotificationProgress(); if (notificationConfig) notificationConfig.hidden=true; if (notificationProgress) notificationProgress.hidden=false; if (notificationApply) notificationApply.hidden=true; if (notificationCancel) notificationCancel.disabled=true; if (notificationClose) notificationClose.disabled=true;
        const results=[]; let succeeded=0, skipped=notificationInitialSkipped.length, failed=0; notificationInitialSkipped.forEach(item => results.push({name:item.name||item.id, status:'Skipped', message:item.reason||'Not eligible'}));
        const total=notificationTargets.length + notificationInitialSkipped.length; if (notificationProgressTitle) notificationProgressTitle.textContent = mode === 'remove' ? 'Removing Notifications' : 'Updating Notifications';
        for (let i=0; i<notificationTargets.length; i++) {
            const target=notificationTargets[i]; const processedBefore=notificationInitialSkipped.length+i; if (notificationProgressCount) notificationProgressCount.textContent=`${processedBefore} of ${total} tasks processed`; if (notificationProgressBar) notificationProgressBar.value=Math.round((processedBefore/total)*100); if (notificationCurrent) notificationCurrent.textContent=`Processing: ${target.name || target.id}`;
            try { const data=await fetchJson(endpoint('updateNotificationUrl'), {method:'POST', body:JSON.stringify({...payloadBase, taskId:target.id})}); if (data?.skipped) { skipped++; results.push({name:target.name,status:'Skipped',message:data?.message||'No change'}); } else { succeeded++; results.push({name:target.name,status:data?.warning ? 'Warning' : 'Updated',message:[data?.message,data?.warning].filter(Boolean).join(' ')}); setTaskUndoAvailable(target.id,true); } }
            catch(error) { failed++; results.push({name:target.name,status:'Failed',message:error?.message||'Update failed'}); }
        }
        if (notificationProgressCount) notificationProgressCount.textContent=`${total} of ${total} tasks processed`; if (notificationProgressBar) notificationProgressBar.value=100; if (notificationCurrent) notificationCurrent.textContent='Complete';
        if (notificationSummary) notificationSummary.textContent=`${succeeded} updated · ${skipped} skipped · ${failed} failed`;
        if (notificationResults) { notificationResults.replaceChildren(); results.filter(item=>item.status!=='Updated').forEach(item=>{ const row=document.createElement('div'); row.className='task-notification-result-row'; row.textContent=`${item.status}: ${item.name}${item.message ? ` — ${item.message}` : ''}`; notificationResults.appendChild(row); }); }
        notificationBusy=false; if (notificationCancel) { notificationCancel.disabled=false; notificationCancel.textContent='Close'; } if (notificationClose) notificationClose.disabled=false; await refreshTaskGrid(true);
    }

    function closeNotificationBulkConfirm() {
        pendingNotificationBulkApply = null;
        if (notificationConfirmDialog?.open) notificationConfirmDialog.close();
    }

    async function confirmNotificationBulkApply() {
        const pending = pendingNotificationBulkApply;
        if (!pending || notificationBusy) return;
        pendingNotificationBulkApply = null;
        if (notificationConfirmDialog?.open) notificationConfirmDialog.close();
        await executeTaskNotifications(pending.mode, pending.payloadBase);
    }

    async function applyTaskNotifications() {
        if (notificationBusy || !notificationTargets.length) return;
        const mode = selectedNotificationMode(); const payloadBase = currentNotificationPayload(mode);
        if (mode === 'remove' && notificationSingleTask && payloadBase.actionIndex === null) { if (notificationError) { notificationError.textContent='Select an existing notification to remove.'; notificationError.hidden=false; } return; }
        if (!(mode === 'remove' && payloadBase.actionIndex !== null) && !payloadBase.doIfFailure && !payloadBase.doIfNoAction && !payloadBase.doIfSuccess) { if (notificationError) { notificationError.textContent='Select at least one Task result.'; notificationError.hidden=false; } return; }
        const preservingSingleExistingRecipients = mode === 'upsert' && notificationSingleTask && payloadBase.actionIndex !== null && payloadBase.preserveExistingRecipients;
        const recipientsRequired = mode === 'upsert' && !preservingSingleExistingRecipients;
        if (mode === 'upsert' && (!payloadBase.hostId || (recipientsRequired && !payloadBase.addressTo.trim()) || !payloadBase.subject.trim() || !payloadBase.message.trim())) {
            if (notificationError) {
                notificationError.textContent = !payloadBase.addressTo.trim() && recipientsRequired
                    ? (notificationSingleTask ? 'Recipients are required when creating a notification.' : 'Recipients are required because bulk Add/Update may need to create a notification.')
                    : 'SMTP Host, Subject, and Message Body are required for Add/Update.';
                notificationError.hidden=false;
            }
            return;
        }
        if (!notificationSingleTask) {
            const verb = mode === 'remove' ? 'Remove matching notification types from' : 'Add or update matching notification types on';
            pendingNotificationBulkApply = { mode, payloadBase };
            if (notificationConfirmMessage) notificationConfirmMessage.textContent = `${verb} ${notificationTargets.length} Task${notificationTargets.length === 1 ? '' : 's'}. Each Task will be evaluated independently according to its current notification configuration.`;
            notificationConfirmDialog?.showModal();
            return;
        }
        await executeTaskNotifications(mode, payloadBase);
    }

    notificationModeInputs.forEach(input => input.addEventListener('change', updateNotificationModeUi));
    notificationExistingSelect?.addEventListener('change', () => {
        clearUnavailableNotificationHostWarning();
        const actionIndex = selectedExistingNotificationIndex();
        if (actionIndex === null) {
            notificationModeInputs.forEach(input => input.checked = input.value === 'upsert');
            setDefaultNotificationResults();
            clearNotificationMessageFields();
            updateNotificationModeUi();
            return;
        }
        const item = notificationExisting.find(notification => Number(notification?.actionIndex) === actionIndex);
        if (!item) return;
        applyNotificationValues(item, true);
        if (notificationHost && item.hostId && notificationHost.value !== String(item.hostId)) {
            notificationHost.value='';
            if (notificationWarning) { notificationWarning.textContent='The selected notification references an SMTP Host that is not currently available. Select an SMTP Host before applying changes.'; notificationWarning.hidden=false; }
        }
        updateNotificationModeUi();
    });
    notificationTemplateButton?.addEventListener('click', event => {
        event.stopPropagation();
        if (selectedNotificationMode() === 'remove' || !notificationTemplateMenu) return;
        notificationTemplateMenu.hidden = !notificationTemplateMenu.hidden;
    });
    notificationTemplateMenu?.addEventListener('click', event => event.stopPropagation());
    document.addEventListener('click', () => { if (notificationTemplateMenu) notificationTemplateMenu.hidden = true; });
    notificationTemplateSave?.addEventListener('click', () => { editorNotificationTemplateContext = null; openNotificationTemplateSaveDialog(); });
    notificationTemplateOverwrite?.addEventListener('change', updateNotificationTemplateSaveDialogState);
    notificationTemplateSaveConfirm?.addEventListener('click', saveNotificationTemplate);
    [notificationTemplateSaveCancel, notificationTemplateSaveClose].forEach(button => button?.addEventListener('click', closeNotificationTemplateSaveDialog));
    notificationTemplateSaveDialog?.addEventListener('cancel', event => { event.preventDefault(); closeNotificationTemplateSaveDialog(); });
    notificationTemplateSaveDialog?.addEventListener('close', () => finishEditorNotificationTemplateSave(null));
    notificationConfirmContinue?.addEventListener('click', confirmNotificationBulkApply);
    notificationConfirmCancelButtons.forEach(button => button.addEventListener('click', closeNotificationBulkConfirm));
    notificationConfirmDialog?.addEventListener('cancel', event => { event.preventDefault(); closeNotificationBulkConfirm(); });
    notificationFormat?.addEventListener('change', () => setNotificationFormatUi(true));
    notificationHtmlToggle?.addEventListener('click', () => {
        if (selectedNotificationMode() === 'remove' || notificationFormat?.value === 'text') return;
        setNotificationHtmlSourceMode(!notificationHtmlSourceMode);
    });
    ['keyup','mouseup','input','focus'].forEach(name => notificationHtmlEditor?.addEventListener(name, rememberNotificationHtmlRange));
    notificationDialog?.querySelectorAll('[data-notification-command]').forEach(button => {
        button.addEventListener('mousedown', event => event.preventDefault());
        button.addEventListener('click', () => { restoreNotificationHtmlRange(); document.execCommand(button.dataset.notificationCommand, false, null); rememberNotificationHtmlRange(); });
    });
    const notificationLinkButton = notificationDialog?.querySelector('[data-notification-link]');
    function closeNotificationLinkDialog() {
        if (notificationLinkDialog?.open) notificationLinkDialog.close();
    }
    function openNotificationLinkDialog() {
        if (selectedNotificationMode() === 'remove' || !notificationLinkDialog) return;
        rememberNotificationHtmlRange();
        if (notificationLinkError) { notificationLinkError.hidden = true; notificationLinkError.textContent = ''; }
        if (notificationLinkUrl) notificationLinkUrl.value = '';
        notificationLinkDialog.showModal();
        window.setTimeout(() => notificationLinkUrl?.focus(), 0);
    }
    function addNotificationLink() {
        const url = String(notificationLinkUrl?.value || '').trim();
        if (!/^(https?:|mailto:)/i.test(url)) {
            if (notificationLinkError) { notificationLinkError.textContent = 'Enter a URL beginning with http://, https://, or mailto:.'; notificationLinkError.hidden = false; }
            notificationLinkUrl?.focus();
            return;
        }
        closeNotificationLinkDialog();
        restoreNotificationHtmlRange();
        document.execCommand('createLink', false, url);
        rememberNotificationHtmlRange();
    }
    notificationLinkButton?.addEventListener('mousedown', event => event.preventDefault());
    notificationLinkButton?.addEventListener('click', openNotificationLinkDialog);
    notificationLinkConfirm?.addEventListener('click', addNotificationLink);
    notificationLinkCancelButtons.forEach(button => button.addEventListener('click', closeNotificationLinkDialog));
    notificationLinkDialog?.addEventListener('cancel', event => { event.preventDefault(); closeNotificationLinkDialog(); });
    notificationLinkUrl?.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); addNotificationLink(); } });
    notificationDialog?.querySelector('[data-notification-subject-macro]')?.addEventListener('change', event => { if (event.target.value) insertTextAtCaret(notificationSubject, event.target.value); event.target.value=''; });
    notificationDialog?.querySelector('[data-notification-body-macro]')?.addEventListener('change', event => { const macro=event.target.value; if (macro) { if (notificationFormat?.value === 'text') insertTextAtCaret(notificationTextEditor, macro); else if (notificationHtmlSourceMode) insertTextAtCaret(notificationHtmlSource, macro); else insertHtmlMacro(macro); } event.target.value=''; });
    notificationApply?.addEventListener('click', applyTaskNotifications);
    [notificationCancel, notificationClose].forEach(button => button?.addEventListener('click', () => { if (!notificationBusy) notificationDialog?.close(); }));
    notificationDialog?.addEventListener('cancel', event => { if (notificationBusy) event.preventDefault(); });
    notificationDialog?.addEventListener('close', resetNotificationDialog);

    // Purpose: Format a bulk-action result message for the MAUI status surface.
    function formatBulkResult(action, data) {
        const succeeded = Number(data?.succeeded || 0);
        const skipped = Number(data?.skipped || 0);
        const failed = Number(data?.failed || 0);
        const parts = [`${action}: ${succeeded} succeeded`];
        if (skipped) parts.push(`${skipped} skipped`);
        if (failed) parts.push(`${failed} failed`);
        const failures = Array.isArray(data?.items) ? data.items.filter(item => item && item.success === false).slice(0, 3) : [];
        if (failures.length) parts.push(failures.map(item => `${item.name || item.id}: ${item.message || 'Failed'}`).join(' | '));
        return parts.join(', ');
    }

    // An idempotent state request completes for both changed and already-matching Tasks.
    // Count item outcomes once; never describe an already-enabled/disabled Task as skipped.
    function formatTaskStateResult(action, data, enabled) {
        const items = Array.isArray(data?.items) ? data.items : [];
        const completed = items.filter(item => item?.success === true).length;
        const already = items.filter(item => item?.success === true && item.skipped === true).length;
        const failed = items.filter(item => item?.success === false);
        const parts = [`${action}: ${completed} completed`];
        if (already) parts.push(`${completed - already} changed`, `${already} already ${enabled ? 'enabled' : 'disabled'}`);
        if (failed.length) parts.push(`${failed.length} failed`, failed.slice(0,3).map(item => `${item.name || item.id}: ${item.message || 'Failed'}`).join(' | '));
        return parts.join(', ');
    }

    // Starts one runnable MOVEit task immediately from its row action.
    async function runTaskNow(taskId, taskName) {
        const id = String(taskId || '').trim();
        if (!id) return;
        if (bulkTaskActionBusy || deleteTasksBusy || killTaskBusy || undoTaskBusy) {
            showNativeEditorStatus('Another task action is already in progress.', 5000);
            return;
        }

        bulkTaskActionBusy = true;
        try {
            const data = await fetchJson(endpoint('runTaskNowUrl'), {
                method: 'POST',
                body: JSON.stringify({ taskId: id })
            });
            const name = String(data?.taskName || taskName || id).trim();
            showNativeEditorStatus(`Started task ${name}.`, 5000);
            await refreshTaskGrid(true);
        } catch (error) {
            showNativeEditorStatus(`Run Task Now: ${error?.message || 'Unable to start the task.'}`, 5000, 'error');
        } finally {
            bulkTaskActionBusy = false;
        }
    }

    // Preflights a running task and open the confirmation dialog for its current run instances.
    async function beginKillTask(taskId, taskName) {
        const id = String(taskId || '').trim();
        if (!id) return;
        if (bulkTaskActionBusy || deleteTasksBusy || killTaskBusy || undoTaskBusy) {
            showNativeEditorStatus('Another task action is already in progress.', 5000);
            return;
        }

        bulkTaskActionBusy = true;
        try {
            const preflight = await fetchJson(endpoint('preflightKillTaskUrl'), {
                method: 'POST',
                body: JSON.stringify({ taskId: id })
            });
            const nominalStarts = Array.isArray(preflight?.nominalStarts)
                ? preflight.nominalStarts.map(value => String(value || '').trim()).filter(Boolean)
                : [];
            if (!preflight?.success || nominalStarts.length === 0) {
                showNativeEditorStatus(preflight?.message || 'The task is no longer running.', 5000);
                await refreshTaskGrid(true);
                return;
            }

            const name = String(preflight?.taskName || taskName || id).trim();
            pendingKillTask = { taskId: id, taskName: name, nominalStarts };
            if (killTaskMessage) {
                killTaskMessage.textContent = nominalStarts.length === 1
                    ? `Task "${name}" currently has 1 running instance. This will terminate that run immediately.`
                    : `Task "${name}" currently has ${nominalStarts.length} running instances. This will terminate all ${nominalStarts.length} currently running instances.`;
            }
            if (killTaskError) {
                killTaskError.hidden = true;
                killTaskError.textContent = '';
            }
            killTaskDialog?.showModal();
            window.requestAnimationFrame(() => killTaskNoButtons.find(button => button.textContent.trim() === 'No')?.focus());
        } catch (error) {
            showNativeEditorStatus(`Kill Task: ${error?.message || 'Unable to verify the running task.'}`, 5000, 'error');
        } finally {
            bulkTaskActionBusy = false;
        }
    }

    // Confirms and submit the kill request for the running instances captured by the preflight.
    async function confirmKillTask() {
        if (killTaskBusy || !pendingKillTask?.taskId || !pendingKillTask.nominalStarts?.length) return;
        killTaskBusy = true;
        if (killTaskYes) killTaskYes.disabled = true;
        killTaskNoButtons.forEach(button => { button.disabled = true; });

        try {
            const data = await fetchJson(endpoint('killTaskUrl'), {
                method: 'POST',
                body: JSON.stringify({
                    taskId: pendingKillTask.taskId,
                    nominalStarts: pendingKillTask.nominalStarts
                })
            });
            killTaskDialog?.close();
            const killed = Number(data?.killed || 0);
            const skipped = Number(data?.skipped || 0);
            const failed = Number(data?.failed || 0);
            const parts = [`Kill Task: ${killed} run${killed === 1 ? '' : 's'} killed`];
            if (skipped) parts.push(`${skipped} already ended`);
            if (failed) parts.push(`${failed} failed`);
            const failures = Array.isArray(data?.items) ? data.items.filter(item => item && item.success === false).slice(0, 3) : [];
            if (failures.length) parts.push(failures.map(item => item.message || 'Kill failed').join(' | '));
            showNativeEditorStatus(parts.join(', '), 5000);
            pendingKillTask = null;
            await refreshTaskGrid(true);
        } catch (error) {
            if (killTaskError) {
                killTaskError.textContent = error?.message || 'Unable to kill the running task.';
                killTaskError.hidden = false;
            }
        } finally {
            killTaskBusy = false;
            if (killTaskYes) killTaskYes.disabled = false;
            killTaskNoButtons.forEach(button => { button.disabled = false; });
        }
    }

    // Purpose: Cancel kill task.
    function cancelKillTask() {
        if (killTaskBusy) return;
        pendingKillTask = null;
        killTaskDialog?.close();
    }

    // Enables or disable the supplied task IDs by using the shared bulk-action endpoint.
    async function runTaskEnableDisable(taskIds, enabled) {
        const ids = normalizeSelectedTaskIds(taskIds);
        if (!ids.length) {
            showNativeEditorStatus('Select at least one task.', 5000);
            return;
        }
        if (bulkTaskActionBusy || deleteTasksBusy || killTaskBusy || undoTaskBusy) {
            showNativeEditorStatus('Another task action is already in progress.', 5000);
            return;
        }

        const action = enabled ? 'Enable Task' : 'Disable Task';
        bulkTaskActionBusy = true;
        const actionVerb = enabled ? 'Enabling' : 'Disabling';
        showNativeEditorStatus(`${actionVerb} ${ids.length} task${ids.length === 1 ? '' : 's'}...`, 5000);
        try {
            const data = await fetchJson(endpoint(enabled ? 'enableTasksUrl' : 'disableTasksUrl'), {
                method: 'POST',
                body: JSON.stringify({ taskIds: ids })
            });
            showNativeEditorStatus(formatTaskStateResult(action, data, enabled), 5000);
            await refreshTaskGrid(true);
        } catch (error) {
            showNativeEditorStatus(`${action}: ${error?.message || 'Operation failed.'}`, 5000, 'error');
        } finally {
            bulkTaskActionBusy = false;
        }
    }

    // Preflights the supplied task IDs for dependencies before Offers destructive deletion.
    async function beginDeleteTasks(taskIds) {
        const ids = normalizeSelectedTaskIds(taskIds);
        if (!ids.length) {
            showNativeEditorStatus('Select at least one task.', 5000);
            return;
        }
        if (bulkTaskActionBusy || deleteTasksBusy || killTaskBusy || undoTaskBusy) {
            showNativeEditorStatus('Another task action is already in progress.', 5000);
            return;
        }

        bulkTaskActionBusy = true;
        try {
            const preflight = await fetchJson(endpoint('preflightDeleteTasksUrl'), {
                method: 'POST',
                body: JSON.stringify({ taskIds: ids })
            });
            if (!preflight?.success) {
                const blockers = Array.isArray(preflight?.blockers) ? preflight.blockers : [];
                const detail = blockers.length
                    ? blockers.slice(0, 3).map(item => `${item.taskName} is referenced by ${item.referencedByTaskName}`).join(' | ')
                    : '';
                showNativeEditorStatus([preflight?.message || 'The selected tasks cannot be deleted.', detail].filter(Boolean).join(' '), 5000);
                return;
            }

            const targets = Array.isArray(preflight?.targets)
                ? preflight.targets.map(item => ({
                    taskId: String(item?.taskId || '').trim(),
                    taskName: String(item?.taskName || item?.taskId || '').trim()
                })).filter(item => item.taskId)
                : ids.map(id => ({ taskId: id, taskName: id }));

            pendingDeleteTaskIds = ids;
            deleteTasksWithoutRecycle = false;
            if (deleteTasksYes) deleteTasksYes.textContent = 'Yes, Delete';
            if (deleteTasksTitle) deleteTasksTitle.textContent = ids.length === 1 ? 'Delete Task' : 'Delete Tasks';
            if (deleteTasksMessage) {
                deleteTasksMessage.textContent = ids.length === 1
                    ? 'The following task will be deleted from MOVEit Automation:'
                    : `The following ${ids.length} tasks will be deleted from MOVEit Automation:`;
            }
            if (deleteTasksList) {
                deleteTasksList.replaceChildren();
                targets.forEach(item => {
                    const row = document.createElement('div');
                    row.className = 'delete-object-list-item';
                    row.textContent = item.taskName || item.taskId;
                    deleteTasksList.appendChild(row);
                });
                deleteTasksList.hidden = targets.length === 0;
            }
            if (deleteTasksError) {
                deleteTasksError.hidden = true;
                deleteTasksError.textContent = '';
            }
            deleteTasksDialog?.showModal();
            window.requestAnimationFrame(() => deleteTasksNoButtons.find(button => button.textContent.trim() === 'Cancel')?.focus());
        } catch (error) {
            showNativeEditorStatus(`Delete Task: ${error?.message || 'Dependency check failed.'}`, 5000, 'error');
        } finally {
            bulkTaskActionBusy = false;
        }
    }

    // Submits deletion only after the user explicitly confirms the preflighted task set.
    async function confirmDeleteTasks() {
        if (deleteTasksBusy || !pendingDeleteTaskIds.length) return;
        deleteTasksBusy = true;
        if (deleteTasksYes) deleteTasksYes.disabled = true;
        deleteTasksNoButtons.forEach(button => { button.disabled = true; });
        showNativeEditorStatus(`Deleting ${pendingDeleteTaskIds.length} task${pendingDeleteTaskIds.length === 1 ? '' : 's'}...`, 5000);

        try {
            const data = await fetchJson(endpoint('deleteTasksUrl'), {
                method: 'POST',
                body: JSON.stringify({ taskIds: pendingDeleteTaskIds, deleteWithoutRecycle: deleteTasksWithoutRecycle })
            });
            deleteTasksDialog?.close();
            const deletedTaskIds = Array.isArray(data?.items)
                ? data.items.filter(item => item?.success === true).map(item => item?.id).filter(Boolean)
                : [];
            taskGrid?.dispatchEvent(new CustomEvent('maui:grid-remove-rows', { detail: { rowIds: deletedTaskIds } }));
            const deleteSummary = formatBulkResult('Delete Task', data);
            const deleteWarning = String(data?.warning || '').trim();
            showNativeEditorStatus([deleteSummary, deleteWarning].filter(Boolean).join(' '), 5000);
            pendingDeleteTaskIds = [];
            deleteTasksWithoutRecycle = false;
            if (deleteTasksYes) deleteTasksYes.textContent = 'Yes, Delete';
            await refreshTaskGrid(true);
        } catch (error) {
            if (error?.status === 409 && error?.payload?.code === 'RecycleCaptureFailed') {
                deleteTasksWithoutRecycle = true;
                if (deleteTasksError) {
                    deleteTasksError.textContent = error?.message || 'MAUI could not create a recovery copy. Delete anyway?';
                    deleteTasksError.hidden = false;
                }
                if (deleteTasksYes) deleteTasksYes.textContent = 'Delete Anyway';
            } else if (deleteTasksError) {
                deleteTasksError.textContent = error?.message || 'Unable to delete the selected tasks.';
                deleteTasksError.hidden = false;
            }
        } finally {
            deleteTasksBusy = false;
            if (deleteTasksYes) deleteTasksYes.disabled = false;
            deleteTasksNoButtons.forEach(button => { button.disabled = false; });
        }
    }

    // Purpose: Cancel delete tasks.
    function cancelDeleteTasks() {
        if (deleteTasksBusy) return;
        pendingDeleteTaskIds = [];
        deleteTasksWithoutRecycle = false;
        if (deleteTasksYes) deleteTasksYes.textContent = 'Yes, Delete';
        if (deleteTasksList) { deleteTasksList.replaceChildren(); deleteTasksList.hidden = true; }
        deleteTasksDialog?.close();
    }

    // ---------------------------------------------------------------------
    // Shared Task Information modal
    // ---------------------------------------------------------------------
    const taskInformation = window.MauiTaskInformation?.initialize({
        configElement: pageConfig,
        antiForgeryToken,
        onStatus: showNativeEditorStatus,
        onSaved: async () => { await refreshTaskGrid(true); }
    });

    // ---------------------------------------------------------------------
    // Single-pane task/folder tree manager
    // ---------------------------------------------------------------------
    const folderDialog = document.querySelector('[data-folder-manager-dialog]');
    const treeContainer = folderDialog?.querySelector('[data-folder-tree-container]');
    const treeDataNode = document.querySelector('[data-folder-tree-data]');
    const folderError = folderDialog?.querySelector('[data-folder-error]');
    const contextMenu = folderDialog?.querySelector('[data-folder-context-menu]');
    let folderTreeData = { folders: [], tasks: [] };
    let contextFolderId = null;
    let draggedItem = null;
    let folderEditorId = null;
    let folderEditorOriginalName = '';
    let folderEditSaving = false;
    let folderManagerDirty = false;
    const selectedTaskIds = new Set();
    let taskSelectionAnchorId = null;

    try {
        folderTreeData = JSON.parse(treeDataNode?.textContent || '{"folders":[],"tasks":[]}');
    } catch {
        folderTreeData = { folders: [], tasks: [] };
    }

    // Purpose: Open folder manager.
    function openFolderManager() {
        if (document.documentElement.dataset.moveitAdmin !== 'true') return;
        hideFolderError();
        hideContextMenu();
        cancelFolderRename(false);
        folderManagerDirty = false;
        renderFolderTree();
        folderDialog.showModal();
    }

    // Purpose: Close folder manager.
    function closeFolderManager() {
        hideContextMenu();
        cancelFolderRename(false);
        folderDialog.close();
        if (folderManagerDirty) window.location.reload();
    }

    // Purpose: Render folder tree.
    function renderFolderTree() {
        if (!treeContainer) return;
        treeContainer.replaceChildren();
        treeContainer.appendChild(buildFolderBranch(null, 'All Tasks', true));
        focusActiveFolderEditor();
    }

    // Purpose: Build folder branch.
    function buildFolderBranch(folderId, folderName, isRoot = false) {
        const branch = document.createElement('div');
        branch.className = `folder-tree-branch ${isRoot ? 'root-branch' : ''}`;
        branch.dataset.folderId = folderId == null ? '' : String(folderId);

        const row = document.createElement('div');
        row.className = `folder-tree-row folder-node ${isRoot ? 'root-node' : ''}`;
        row.dataset.folderId = folderId == null ? '' : String(folderId);
        row.draggable = !isRoot && Number(folderEditorId) !== Number(folderId);
        row.tabIndex = 0;

        const children = document.createElement('div');
        children.className = 'folder-tree-children';

        const childFolders = folderTreeData.folders
            .filter(folder => normalizeFolderId(folder.parentId) === normalizeFolderId(folderId))
            .sort((a, b) => String(a.name).localeCompare(String(b.name), undefined, { sensitivity: 'base', numeric: true }));
        const childTasks = folderTreeData.tasks
            .filter(task => normalizeFolderId(task.folderId) === normalizeFolderId(folderId))
            .sort((a, b) => String(a.name).localeCompare(String(b.name), undefined, { sensitivity: 'base', numeric: true }));
        const hasChildren = childFolders.length > 0 || childTasks.length > 0;

        const expander = document.createElement('button');
        expander.type = 'button';
        expander.className = 'tree-expander';
        expander.textContent = hasChildren ? '▾' : '';
        expander.disabled = !hasChildren;
        expander.setAttribute('aria-label', hasChildren ? 'Collapse folder' : 'Empty folder');
        expander.addEventListener('click', event => {
            event.stopPropagation();
            if (!hasChildren) return;
            const collapsed = branch.classList.toggle('collapsed');
            expander.textContent = collapsed ? '▸' : '▾';
            expander.setAttribute('aria-label', collapsed ? 'Expand folder' : 'Collapse folder');
        });

        const icon = document.createElement('span');
        icon.className = 'folder-tree-glyph';
        icon.textContent = isRoot ? '◫' : '▰';

        row.append(expander, icon);
        if (!isRoot && Number(folderEditorId) === Number(folderId)) {
            row.appendChild(buildFolderNameEditor(folderId, folderName));
        } else {
            const label = document.createElement('span');
            label.className = 'folder-tree-label';
            label.textContent = folderName;
            row.appendChild(label);
        }

        const count = document.createElement('span');
        count.className = 'folder-tree-count';
        count.textContent = String(childTasks.length);
        count.title = `${childTasks.length} task${childTasks.length === 1 ? '' : 's'} directly in this folder`;
        row.appendChild(count);
        branch.append(row, children);

        childFolders.forEach(folder => children.appendChild(buildFolderBranch(Number(folder.id), folder.name, false)));
        childTasks.forEach(task => children.appendChild(buildTaskRow(task)));

        wireFolderDropTarget(row, folderId, folderName);

        if (!isRoot && Number(folderEditorId) !== Number(folderId)) {
            row.addEventListener('dragstart', event => {
                draggedItem = { type: 'folder', id: Number(folderId), name: folderName };
                event.dataTransfer.effectAllowed = 'move';
                event.dataTransfer.setData('text/plain', `folder:${folderId}`);
                row.classList.add('dragging');
            });
            row.addEventListener('dragend', () => {
                row.classList.remove('dragging');
                clearDropTargets();
                draggedItem = null;
            });
        }

        row.addEventListener('contextmenu', event => {
            if (folderEditorId != null && folderId != null && Number(folderEditorId) === Number(folderId)) return;
            event.preventDefault();
            event.stopPropagation();
            showContextMenu(event.clientX, event.clientY, folderId, isRoot);
        });

        return branch;
    }

    // Purpose: Build folder name editor.
    function buildFolderNameEditor(folderId, folderName) {
        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'folder-tree-name-input';
        input.value = folderName || '';
        input.maxLength = 200;
        input.autocomplete = 'off';
        input.dataset.folderNameEditor = String(folderId);
        input.setAttribute('aria-label', 'Folder name');
        input.addEventListener('click', event => event.stopPropagation());
        input.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                commitFolderRename();
            } else if (event.key === 'Escape') {
                event.preventDefault();
                cancelFolderRename(true);
            }
        });
        input.addEventListener('blur', () => {
            window.setTimeout(() => {
                if (Number(folderEditorId) === Number(folderId) && !folderEditSaving) commitFolderRename();
            }, 0);
        });
        return input;
    }

    // Purpose: Focus active folder editor.
    function focusActiveFolderEditor() {
        if (folderEditorId == null) return;
        window.requestAnimationFrame(() => {
            const input = treeContainer?.querySelector(`[data-folder-name-editor="${folderEditorId}"]`);
            if (!input) return;
            input.focus();
            input.select();
        });
    }

    // Purpose: Begin folder rename.
    function beginFolderRename(folderId) {
        const folder = folderTreeData.folders.find(item => Number(item.id) === Number(folderId));
        if (!folder) return;
        folderEditorId = Number(folderId);
        folderEditorOriginalName = folder.name || '';
        hideContextMenu();
        hideFolderError();
        renderFolderTree();
    }

    // Validates and persist the current folder rename operation.
    async function commitFolderRename() {
        if (folderEditorId == null || folderEditSaving) return;
        const editorId = Number(folderEditorId);
        const input = treeContainer?.querySelector(`[data-folder-name-editor="${editorId}"]`);
        const name = String(input?.value || '').trim();
        if (!name) {
            showFolderError('Folder name is required.');
            input?.focus();
            return;
        }

        if (name === folderEditorOriginalName) {
            folderEditorId = null;
            folderEditorOriginalName = '';
            renderFolderTree();
            return;
        }

        folderEditSaving = true;
        hideFolderError();
        try {
            await fetchJson(endpoint('renameFolderUrl'), {
                method: 'POST',
                body: JSON.stringify({ folderId: editorId, name })
            });
            const folder = folderTreeData.folders.find(item => Number(item.id) === editorId);
            if (folder) folder.name = name;
            folderManagerDirty = true;
            folderEditorId = null;
            folderEditorOriginalName = '';
            renderFolderTree();
        } catch (error) {
            showFolderError(error.message);
            focusActiveFolderEditor();
        } finally {
            folderEditSaving = false;
        }
    }

    // Purpose: Cancel folder rename.
    function cancelFolderRename(render = true) {
        folderEditorId = null;
        folderEditorOriginalName = '';
        folderEditSaving = false;
        if (render) renderFolderTree();
    }

    // Creates a new MAUI task folder beneath the optional parent folder.
    async function createNewFolder(parentId) {
        hideFolderError();
        hideContextMenu();
        const normalizedParent = parentId == null ? null : Number(parentId);
        const placeholderName = getUniqueNewFolderName(normalizedParent);

        try {
            const response = await fetchJson(endpoint('createFolderUrl'), {
                method: 'POST',
                body: JSON.stringify({ name: placeholderName, parentId: normalizedParent })
            });
            const created = response?.result || {};
            const id = Number(created.id ?? created.Id);
            if (!Number.isFinite(id)) throw new Error('The folder was created, but the server did not return its ID.');

            folderTreeData.folders.push({
                id,
                parentId: normalizeFolderId(created.parentId ?? created.ParentId ?? normalizedParent),
                name: String(created.name ?? created.Name ?? placeholderName)
            });
            folderManagerDirty = true;
            folderEditorId = id;
            folderEditorOriginalName = String(created.name ?? created.Name ?? placeholderName);
            renderFolderTree();
        } catch (error) {
            showFolderError(error.message);
        }
    }

    // Purpose: Generate a non-conflicting default name for a new folder.
    function getUniqueNewFolderName(parentId) {
        const siblingNames = new Set(folderTreeData.folders
            .filter(folder => normalizeFolderId(folder.parentId) === normalizeFolderId(parentId))
            .map(folder => String(folder.name || '').toLowerCase()));
        if (!siblingNames.has('new folder')) return 'New Folder';

        let suffix = 2;
        while (siblingNames.has(`new folder (${suffix})`)) suffix += 1;
        return `New Folder (${suffix})`;
    }

    // Purpose: Build task row.
    function buildTaskRow(task) {
        const row = document.createElement('div');
        row.className = 'folder-tree-row task-node';
        row.draggable = true;
        row.dataset.taskId = task.id;
        row.tabIndex = 0;
        row.setAttribute('role', 'treeitem');
        row.setAttribute('aria-selected', selectedTaskIds.has(String(task.id)) ? 'true' : 'false');
        if (selectedTaskIds.has(String(task.id))) row.classList.add('selected');

        const spacer = document.createElement('span');
        spacer.className = 'tree-expander task-expander-spacer';
        const icon = document.createElement('span');
        icon.className = 'folder-tree-glyph task-glyph';
        icon.textContent = '≡';
        const label = document.createElement('span');
        label.className = 'folder-tree-label';
        label.textContent = task.name;
        const id = document.createElement('span');
        id.className = 'folder-tree-task-id';
        id.textContent = task.id;

        row.append(spacer, icon, label, id);

        // Implements file-explorer style selection: plain click selects one task, Shift-click selects a range,
        // and Ctrl/Cmd-click toggles individual tasks without clearing the rest of the selection.
        row.addEventListener('click', event => {
            event.stopPropagation();
            selectTaskRow(String(task.id), event.shiftKey, event.ctrlKey || event.metaKey);
        });

        row.addEventListener('keydown', event => {
            if (event.key !== ' ' && event.key !== 'Enter') return;
            event.preventDefault();
            selectTaskRow(String(task.id), event.shiftKey, event.ctrlKey || event.metaKey);
        });

        row.addEventListener('dragstart', event => {
            const taskId = String(task.id);
            // Makes dragging an unselected task implicitly makes it the only selected task, matching
            // common desktop tree/file-manager behavior without forcing an extra click first.
            if (!selectedTaskIds.has(taskId)) {
                selectedTaskIds.clear();
                selectedTaskIds.add(taskId);
                taskSelectionAnchorId = taskId;
                updateTaskSelectionStyles();
            }

            const ids = Array.from(selectedTaskIds);
            draggedItem = {
                type: 'tasks',
                ids,
                name: ids.length === 1 ? task.name : `${ids.length} selected tasks`
            };
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', ids.map(value => `task:${value}`).join(','));
            row.classList.add('dragging');
        });
        row.addEventListener('dragend', () => {
            row.classList.remove('dragging');
            clearDropTargets();
            draggedItem = null;
        });
        return row;
    }

    // Purpose: Select task row.
    function selectTaskRow(taskId, extendRange, toggle) {
        const id = String(taskId);
        if (extendRange && taskSelectionAnchorId) {
            const visibleIds = Array.from(treeContainer?.querySelectorAll('.task-node[data-task-id]') || [])
                .filter(row => row.getClientRects().length > 0)
                .map(row => String(row.dataset.taskId || ''))
                .filter(Boolean);
            const anchorIndex = visibleIds.indexOf(String(taskSelectionAnchorId));
            const targetIndex = visibleIds.indexOf(id);
            if (anchorIndex >= 0 && targetIndex >= 0) {
                if (!toggle) selectedTaskIds.clear();
                const start = Math.min(anchorIndex, targetIndex);
                const finish = Math.max(anchorIndex, targetIndex);
                visibleIds.slice(start, finish + 1).forEach(value => selectedTaskIds.add(value));
            } else {
                selectedTaskIds.clear();
                selectedTaskIds.add(id);
            }
        } else if (toggle) {
            if (selectedTaskIds.has(id)) selectedTaskIds.delete(id);
            else selectedTaskIds.add(id);
            taskSelectionAnchorId = id;
        } else {
            selectedTaskIds.clear();
            selectedTaskIds.add(id);
            taskSelectionAnchorId = id;
        }

        if (!selectedTaskIds.size) taskSelectionAnchorId = null;
        updateTaskSelectionStyles();
    }

    // Purpose: Update task selection styles.
    function updateTaskSelectionStyles() {
        treeContainer?.querySelectorAll('.task-node[data-task-id]').forEach(row => {
            const selected = selectedTaskIds.has(String(row.dataset.taskId || ''));
            row.classList.toggle('selected', selected);
            row.setAttribute('aria-selected', selected ? 'true' : 'false');
        });
    }

    // Purpose: Wire folder drop target.
    function wireFolderDropTarget(row, folderId, folderName) {
        row.addEventListener('dragover', event => {
            if (!draggedItem || folderEditorId != null) return;
            if (draggedItem.type === 'folder' && Number(draggedItem.id) === Number(folderId)) return;
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
            clearDropTargets();
            row.classList.add('drop-target');
        });
        row.addEventListener('dragleave', event => {
            if (!row.contains(event.relatedTarget)) row.classList.remove('drop-target');
        });
        row.addEventListener('drop', async event => {
            event.preventDefault();
            event.stopPropagation();
            row.classList.remove('drop-target');
            if (!draggedItem) return;

            // Captures the drag payload before awaiting network work; dragend may clear the shared state meanwhile.
            const movedItem = { ...draggedItem };
            const targetId = folderId == null ? null : Number(folderId);
            const targetName = folderName || 'All Tasks';

            try {
                if (movedItem.type === 'tasks') {
                    const movedIds = Array.from(new Set((movedItem.ids || []).map(value => String(value)).filter(Boolean)));
                    await fetchJson(endpoint('moveTasksUrl') || endpoint('moveTaskUrl'), {
                        method: 'POST',
                        body: endpoint('moveTasksUrl')
                            ? JSON.stringify({ taskIds: movedIds, folderId: targetId })
                            : JSON.stringify({ taskId: movedIds[0], folderId: targetId })
                    });
                    folderTreeData.tasks.forEach(task => {
                        if (movedIds.includes(String(task.id))) task.folderId = targetId;
                    });
                } else {
                    if (!await window.mauiConfirm?.({
                        title: 'Move Folder?',
                        message: `Move folder "${movedItem.name}" to "${targetName}"?`,
                        confirmText: 'Move Folder'
                    })) return;
                    await fetchJson(endpoint('moveFolderUrl'), {
                        method: 'POST',
                        body: JSON.stringify({ folderId: Number(movedItem.id), parentId: targetId })
                    });
                    const folder = folderTreeData.folders.find(item => Number(item.id) === Number(movedItem.id));
                    if (folder) folder.parentId = targetId;
                }

                folderManagerDirty = true;
                draggedItem = null;
                renderFolderTree();
            } catch (error) {
                showFolderError(error.message);
            }
        });
    }

    // Purpose: Normalize folder id.
    function normalizeFolderId(value) {
        if (value === null || value === undefined || value === '') return null;
        const numeric = Number(value);
        return Number.isFinite(numeric) ? numeric : null;
    }

    // Purpose: Clear drop targets.
    function clearDropTargets() {
        folderDialog?.querySelectorAll('.drop-target').forEach(item => item.classList.remove('drop-target'));
    }

    // Purpose: Show context menu.
    function showContextMenu(clientX, clientY, folderId, isRoot) {
        contextFolderId = folderId == null ? null : Number(folderId);
        const rename = contextMenu.querySelector('[data-folder-context-action="rename"]');
        const remove = contextMenu.querySelector('[data-folder-context-action="delete"]');

        // Treats All Tasks as a virtual root and can never be renamed or deleted.
        rename.hidden = isRoot;
        remove.hidden = isRoot;

        contextMenu.hidden = false;
        contextMenu.style.left = `${clientX}px`;
        contextMenu.style.top = `${clientY}px`;

        requestAnimationFrame(() => {
            const rect = contextMenu.getBoundingClientRect();
            const left = Math.max(8, Math.min(clientX, window.innerWidth - rect.width - 8));
            const top = Math.max(8, Math.min(clientY, window.innerHeight - rect.height - 8));
            contextMenu.style.left = `${left}px`;
            contextMenu.style.top = `${top}px`;
        });
    }

    // Purpose: Hide context menu.
    function hideContextMenu() {
        if (contextMenu) contextMenu.hidden = true;
    }

    // Deletes the currently selected empty MAUI task folder after confirmation.
    async function deleteContextFolder() {
        // Null represents All Tasks. There is intentionally no server operation capable of deleting this virtual root.
        if (contextFolderId == null) return;
        const folder = folderTreeData.folders.find(item => Number(item.id) === Number(contextFolderId));
        if (!await window.mauiConfirm?.({
            title: 'Delete Folder?',
            message: `Delete folder "${folder?.name || contextFolderId}"? Only folders with no subfolders and no MAUI task assignments can be deleted.`,
            confirmText: 'Delete Folder',
            danger: true
        })) return;

        try {
            await fetchJson(endpoint('deleteFolderUrl'), {
                method: 'POST',
                body: JSON.stringify({ folderId: contextFolderId })
            });
            folderTreeData.folders = folderTreeData.folders.filter(item => Number(item.id) !== Number(contextFolderId));
            folderManagerDirty = true;
            contextFolderId = null;
            renderFolderTree();
        } catch (error) {
            showFolderError(error.message);
        }
    }

    // Purpose: Show folder error.
    function showFolderError(message) {
        folderError.textContent = message;
        folderError.hidden = false;
    }

    // Purpose: Hide folder error.
    function hideFolderError() {
        folderError.hidden = true;
        folderError.textContent = '';
    }

    // ---------------------------------------------------------------------
    // Live Task grid refresh
    // ---------------------------------------------------------------------
    const taskGrid = document.querySelector('[data-grid="tasks"]');
    const taskLicenseAlert = document.querySelector('[data-task-license-alert]');
    const taskLicenseTitle = document.querySelector('[data-task-license-title]');
    const taskLicenseMessage = document.querySelector('[data-task-license-message]');
    const refreshControl = document.querySelector('[data-task-refresh-control]');
    const lastRefreshText = refreshControl?.querySelector('[data-task-last-refresh]');
    const refreshClock = refreshControl?.querySelector('[data-task-refresh-clock]');
    const refreshToggle = refreshControl?.querySelector('[data-task-refresh-toggle]');
    const refreshNow = refreshControl?.querySelector('[data-task-refresh-now]');
    const uiTaskRefreshSeconds = Math.max(0, Number(pageConfig.dataset.uiTaskRefreshSeconds || 0));
    let taskRefreshPaused = false;
    let taskRefreshInFlight = false;
    let taskRefreshTimer = 0;

    // Formats the server's UTC refresh timestamp in the browser's local time zone.
    function formatLastRefresh(value) {
        if (!value) return '--:--:--';
        const date = new Date(value);
        if (Number.isNaN(date.valueOf())) return '--:--:--';
        return date.toLocaleTimeString([], {
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: true
        });
    }

    // Updates the visible timestamp only after data has been successfully received and applied.
    function setLastRefresh(value) {
        if (!lastRefreshText) return;
        lastRefreshText.textContent = `Last Refresh: ${formatLastRefresh(value)}`;
        if (refreshControl && value) refreshControl.dataset.lastRefreshUtc = value;
    }

    // Purpose: Clear task refresh timer.
    function clearTaskRefreshTimer() {
        if (!taskRefreshTimer) return;
        window.clearTimeout(taskRefreshTimer);
        taskRefreshTimer = 0;
    }

    // Schedules the next refresh from the completion of the prior request so slow network calls never overlap.
    function scheduleTaskGridRefresh(delayMs = uiTaskRefreshSeconds * 1000) {
        clearTaskRefreshTimer();
        if (uiTaskRefreshSeconds <= 0 || taskRefreshPaused || document.hidden) return;
        taskRefreshTimer = window.setTimeout(() => {
            if (taskPreflightBusy || document.querySelector('[data-host-test-dialog][aria-busy="true"]')) {
                scheduleTaskGridRefresh(1000);
                return;
            }
            if (window.mauiIsRowActionMenuOpen?.()) {
                scheduleTaskGridRefresh(750);
                return;
            }
            refreshTaskGrid(false);
        }, Math.max(250, delayMs));
    }

    // Purpose: Set refresh busy.
    function setRefreshBusy(isBusy) {
        taskRefreshInFlight = isBusy;
        refreshClock?.classList.toggle('is-refreshing', isBusy);
        refreshClock?.setAttribute('aria-busy', isBusy ? 'true' : 'false');
        if (refreshNow) refreshNow.disabled = isBusy;
    }

    // Purpose: Update refresh pause ui.
    function updateRefreshPauseUi() {
        if (!refreshToggle) return;
        if (uiTaskRefreshSeconds <= 0) {
            refreshToggle.textContent = 'Automatic Refresh Disabled';
            refreshToggle.disabled = true;
            refreshClock?.classList.add('is-paused');
            return;
        }

        refreshToggle.disabled = false;
        refreshToggle.textContent = taskRefreshPaused ? 'Resume Refresh' : 'Pause Refresh';
        refreshClock?.classList.toggle('is-paused', taskRefreshPaused);
        refreshClock?.setAttribute('title', taskRefreshPaused ? 'Task refresh paused' : 'Task refresh options');
    }

    // Purpose: Builds task grid request url.
    function buildTaskGridRequestUrl(endpointName) {
        const url = new URL(endpoint(endpointName), window.location.href);
        url.searchParams.set('filter', pageConfig.dataset.activeFilter || 'all');
        const selectedFolderId = String(pageConfig.dataset.selectedFolderId || '').trim();
        if (selectedFolderId) url.searchParams.set('folderId', selectedFolderId);
        return url;
    }

    // Purpose: Renders task license capacity.
    function renderTaskLicenseCapacity(advisory) {
        const current = Number(advisory?.current ?? -1);
        const maximum = Number(advisory?.maximum ?? -1);
        taskLicenseLimitReached = Number.isFinite(current) && Number.isFinite(maximum) && maximum > 0 && current >= maximum;
        document.querySelectorAll('[data-grid-action="New Task"]').forEach(button => {
            button.disabled = taskLicenseLimitReached;
            button.title = taskLicenseLimitReached ? 'The MOVEit Task license limit has been reached.' : '';
        });
        if (!taskLicenseAlert) return;
        taskLicenseAlert.classList.remove('is-warning', 'is-danger');
        if (!advisory || !advisory.message) {
            taskLicenseAlert.hidden = true;
            return;
        }
        const severity = String(advisory.severity || '').toLowerCase();
        taskLicenseAlert.classList.add(severity === 'danger' ? 'is-danger' : 'is-warning');
        if (taskLicenseTitle) taskLicenseTitle.textContent = severity === 'danger' ? 'License Warning:' : 'Capacity Warning:';
        if (taskLicenseMessage) taskLicenseMessage.textContent = advisory.message;
        taskLicenseAlert.hidden = false;
    }

    // Applies one complete server row set through the established reusable-grid replacement contract.
    function applyTaskGridPayload(payload) {
        taskGrid?.dispatchEvent(new CustomEvent('maui:grid-replace-rows', {
            detail: { rows: Array.isArray(payload?.rows) ? payload.rows : [] }
        }));

        if (Array.isArray(payload?.treeTasks)) {
            folderTreeData.tasks = payload.treeTasks;
        }

        renderTaskLicenseCapacity(payload?.licenseCapacity);
        setLastRefresh(payload?.refreshedUtc || new Date().toISOString());
    }

    // Loads the first complete Task dataset while the shared grid shell remains in its initial loading state.
    async function loadInitialTaskGrid() {
        if (taskRefreshInFlight || !taskGrid) return;
        setRefreshBusy(true);
        clearTaskRefreshTimer();
        let refreshRecommended = false;
        try {
            const initialUrl = buildTaskGridRequestUrl('initialTaskGridUrl');
            initialUrl.searchParams.set('preferCached', 'true');
            const payload = await fetchJson(initialUrl.toString(), {
                method: 'GET',
                cache: 'no-store'
            });
            applyTaskGridPayload(payload);
            refreshRecommended = payload?.refreshRecommended === true;
        } catch (error) {
            console.warn('Initial Task grid load failed.');
            if (!window.MauiMoveItGridConnection?.isHandled(error)) showNativeEditorStatus(`Task load failed: ${error.message}`, 5000, 'error', 'page');
        } finally {
            taskGrid.dispatchEvent(new CustomEvent('maui:grid-initial-data-ready'));
            setRefreshBusy(false);
            if (refreshRecommended) {
                window.setTimeout(() => refreshTaskGrid(false), 0);
            } else {
                scheduleTaskGridRefresh();
            }
        }
    }

    // Automatic refresh polls MAUI through the shared cache contract; only Refresh Now forces a live MOVEit read.
    async function refreshTaskGrid(forceRefresh) {
        if (taskRefreshInFlight || !taskGrid) return;
        closeTaskRowActionMenu();
        setRefreshBusy(true);
        clearTaskRefreshTimer();

        try {
            const refreshUrl = buildTaskGridRequestUrl('refreshTaskGridUrl');
            refreshUrl.searchParams.set('forceRefresh', forceRefresh ? 'true' : 'false');
            const payload = await fetchJson(refreshUrl.toString(), {
                method: 'GET',
                cache: 'no-store'
            });
            if (forceRefresh) taskGrid.dispatchEvent(new CustomEvent('maui:task-filename-search-invalidated'));
            applyTaskGridPayload(payload);
        } catch (error) {
            console.warn('Task grid refresh failed.');
            if (!window.MauiMoveItGridConnection?.isHandled(error)) showNativeEditorStatus(`Task refresh failed: ${error.message}`, 5000, 'error', 'page');
        } finally {
            setRefreshBusy(false);
            scheduleTaskGridRefresh();
        }
    }

    // Purpose: Toggle task refresh.
    function toggleTaskRefresh() {
        if (uiTaskRefreshSeconds <= 0) return;
        taskRefreshPaused = !taskRefreshPaused;
        updateRefreshPauseUi();
        refreshToggle?.closest('[data-menu]')?.classList.remove('open');

        if (taskRefreshPaused) {
            clearTaskRefreshTimer();
        } else {
            refreshTaskGrid(false);
        }
    }

    // Purpose: Refresh tasks now.
    function refreshTasksNow() {
        refreshNow?.closest('[data-menu]')?.classList.remove('open');
        refreshTaskGrid(true);
    }

    setLastRefresh(refreshControl?.dataset.lastRefreshUtc || '');
    updateRefreshPauseUi();
    loadInitialTaskGrid();

    // Stops polling MOVEit while the browser tab is hidden. Returning to the Tasks tab performs an immediate catch-up refresh.
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) {
            clearTaskRefreshTimer();
        } else if (!taskRefreshPaused && uiTaskRefreshSeconds > 0) {
            refreshTaskGrid(false);
        }
    });

    refreshToggle?.addEventListener('click', toggleTaskRefresh);
    refreshNow?.addEventListener('click', refreshTasksNow);

    const resourceGroupManager = window.MauiResourceGroups?.initialize?.({
        setupUrl: endpoint('resourceGroupSetupUrl'),
        createUrl: endpoint('createResourceGroupUrl'),
        addUrl: endpoint('addToResourceGroupUrl'),
        antiForgeryToken,
        loginUrl: endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'),
        status: showNativeEditorStatus,
        changed: () => refreshTaskGrid(true)
    });

    const taskJsonEditor = window.MauiTaskJsonEditor?.initialize({ fetchJson, antiForgeryToken,
        changed: () => refreshTaskGrid(true) });

    // ---------------------------------------------------------------------
    // Event wiring
    // ---------------------------------------------------------------------
    // Handles the reusable grid's cancelable event before it falls back to the shared grid toast.
    // Keeps the Tasks action named "New Task" in the Actions dropdown.
    document.addEventListener('maui:grid-action', event => {
        const gridId = String(event.detail?.gridId || '').trim().toLowerCase();
        const actionName = String(event.detail?.action || '').trim().toLowerCase();
        if (gridId !== 'tasks') return;

        if (actionName === 'json editor' && taskJsonEditor) {
            event.preventDefault();
            void taskJsonEditor.open(event.detail?.selectedIds || []);
            return;
        }

        if (actionName === 'new task') {
            event.preventDefault();
            openNewTaskDialog();
            return;
        }

        if (actionName === 'import tasks' || actionName === 'import task') {
            event.preventDefault();
            if (taskSpreadsheetImport) taskSpreadsheetImport.open();
            else nativePortability?.openImport();
            return;
        }

        if (actionName === 'edit notifications' || actionName === 'notifications') {
            event.preventDefault();
            openTaskNotifications(event.detail?.selectedIds || []);
            return;
        }

        if (actionName === 'create resource group') {
            event.preventDefault();
            if (!resourceGroupManager) showNativeEditorStatus('Resource Group management is unavailable.', 5000, 'error');
            else resourceGroupManager.openCreate(event.detail?.selectedIds || []);
            return;
        }

        if (actionName === 'add to resource group') {
            event.preventDefault();
            if (!resourceGroupManager) showNativeEditorStatus('Resource Group management is unavailable.', 5000, 'error');
            else resourceGroupManager.openAdd(event.detail?.selectedIds || []);
            return;
        }

        if (actionName === 'export tasks' || actionName === 'export selected tasks') {
            event.preventDefault();
            nativePortability?.openExport(event.detail?.selectedIds || [], []);
            return;
        }

        if (actionName === 'enable tasks' || actionName === 'disable tasks' || actionName === 'delete tasks') {
            event.preventDefault();
            const selectedIds = normalizeSelectedTaskIds(event.detail?.selectedIds);
            if (actionName === 'enable tasks') runTaskEnableDisable(selectedIds, true);
            if (actionName === 'disable tasks') runTaskEnableDisable(selectedIds, false);
            if (actionName === 'delete tasks') beginDeleteTasks(selectedIds);
        }
    });

    document.addEventListener('click', event => {
        const taskRowMenuButton = event.target.closest('[data-task-row-menu-button]');
        if (taskRowMenuButton) {
            event.preventDefault();
            event.stopPropagation();
            openTaskRowActionMenu(taskRowMenuButton);
            return;
        }

        const taskRowCommand = event.target.closest('[data-task-row-command]');
        if (taskRowCommand && taskRowActionMenu?.contains(taskRowCommand)) {
            event.preventDefault();
            handleTaskRowCommand(String(taskRowCommand.dataset.taskRowCommand || ''));
            return;
        }

        const nativeEditorButton = event.target.closest('[data-native-task-editor]');
        if (nativeEditorButton) {
            event.preventDefault();
            closeTaskRowActionMenu();
            openNativeTaskEditor(nativeEditorButton);
            return;
        }

        const infoButton = event.target.closest('[data-row-info-url]');
        if (infoButton) {
            event.preventDefault();
            closeTaskRowActionMenu();
            taskInformation?.open(infoButton?.dataset?.rowId, infoButton?.dataset?.rowInfoUrl);
            return;
        }

        if (taskRowActionMenu && !event.target.closest('[data-task-row-action-menu]')) closeTaskRowActionMenu();
        if (contextMenu && !event.target.closest('[data-folder-context-menu]')) hideContextMenu();
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            hideContextMenu();
            closeTaskRowActionMenu();
        }
    });
    window.addEventListener('resize', closeTaskRowActionMenu);
    window.addEventListener('scroll', closeTaskRowActionMenu, true);

    newTaskName?.addEventListener('input', applyNewTaskNameFormat);
    newTaskAutoFormat?.addEventListener('change', () => {
        if (newTaskAutoFormat.checked) applyNewTaskNameFormat();
    });
    newTaskForm?.addEventListener('submit', event => {
        event.preventDefault();
        createNewTask();
    });
    newTaskClose?.addEventListener('click', () => {
        if (!newTaskBusy) newTaskDialog?.close();
    });
    newTaskCancel?.addEventListener('click', () => {
        if (!newTaskBusy) newTaskDialog?.close();
    });
    newTaskDialog?.addEventListener('cancel', event => {
        if (newTaskBusy) event.preventDefault();
    });
    newTaskDialog?.addEventListener('close', resetNewTaskDialog);

    cloneTaskForm?.addEventListener('submit', event => {
        event.preventDefault();
        submitCloneTask();
    });
    cloneTaskClose?.addEventListener('click', () => {
        if (!cloneTaskBusy) cloneTaskDialog?.close();
    });
    cloneTaskCancel?.addEventListener('click', () => {
        if (!cloneTaskBusy) cloneTaskDialog?.close();
    });
    cloneTaskDialog?.addEventListener('cancel', event => {
        if (cloneTaskBusy) event.preventDefault();
    });
    cloneTaskDialog?.addEventListener('close', resetCloneTaskDialog);


    undoTaskYes?.addEventListener('click', confirmUndoTask);
    undoTaskNoButtons.forEach(button => button.addEventListener('click', cancelUndoTask));
    undoTaskDialog?.addEventListener('cancel', event => {
        event.preventDefault();
        cancelUndoTask();
    });

    deleteTasksYes?.addEventListener('click', confirmDeleteTasks);
    deleteTasksNoButtons.forEach(button => button.addEventListener('click', cancelDeleteTasks));
    deleteTasksDialog?.addEventListener('cancel', event => {
        event.preventDefault();
        cancelDeleteTasks();
    });

    killTaskYes?.addEventListener('click', confirmKillTask);
    killTaskNoButtons.forEach(button => button.addEventListener('click', cancelKillTask));
    killTaskDialog?.addEventListener('cancel', event => {
        event.preventDefault();
        cancelKillTask();
    });

    document.querySelector('[data-folder-manager-open]')?.addEventListener('click', openFolderManager);
    folderDialog?.querySelectorAll('[data-folder-manager-close]').forEach(button => button.addEventListener('click', closeFolderManager));

    // Keeps Escape consistent with the other dialogs while still routing through closeFolderManager
    // so any hierarchy changes are reconciled with the main grid before the dialog disappears.
    folderDialog?.addEventListener('cancel', event => {
        event.preventDefault();
        closeFolderManager();
    });

    contextMenu?.querySelectorAll('[data-folder-context-action]').forEach(button => {
        button.addEventListener('click', () => {
            const action = button.dataset.folderContextAction;
            const targetFolderId = contextFolderId;
            hideContextMenu();
            if (action === 'new') createNewFolder(targetFolderId);
            if (action === 'rename' && targetFolderId != null) beginFolderRename(targetFolderId);
            if (action === 'delete' && targetFolderId != null) deleteContextFolder();
        });
    });
    // Cross-page editor links from Host dependencies and other MAUI views return here.
    // Consume the target once; otherwise a grid refresh or browser reload would reopen it.
    const taskEditorLandingUrl = new URL(window.location.href);
    const taskEditorLandingId = String(taskEditorLandingUrl.searchParams.get('editorTaskId') || '').trim();
    if (taskEditorLandingId) {
        const taskEditorLandingName = taskEditorLandingUrl.searchParams.get('editorTaskName') || '';
        const taskEditorLandingType = taskEditorLandingUrl.searchParams.get('editorTaskType') || '';
        ['editorTaskId', 'editorTaskName', 'editorTaskType'].forEach(key => taskEditorLandingUrl.searchParams.delete(key));
        window.history.replaceState(window.history.state, '', taskEditorLandingUrl.toString());
        void window.MauiEditorRouting.openTask(taskEditorLandingId, taskEditorLandingName, taskEditorLandingType)
            .catch(error => showNativeEditorStatus(`Task Editor: ${error?.message || 'Unable to open the Task.'}`, 5000, 'error'));
    }

})();

