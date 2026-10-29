/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * This JavaScript is part of the MAUI application.
 */

(() => {
    'use strict';

    window.addEventListener('maui:script-editor-closed', event => { if (event.detail?.changed) void refreshScriptGrid(true); });
    const config = document.querySelector('[data-script-page-config]');
    if (!config) return;

    const antiForgeryToken = document.querySelector('.task-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const grid = document.querySelector('[data-grid="scripts"]');
    const toast = grid?.querySelector('[data-prototype-toast]');
    const portability = window.MauiResourcePortability?.create({
        key: 'script', label: 'Script', pluralLabel: 'Scripts',
        exportUrl: String(config.dataset.exportScriptUrl || ''), exportIdParameter: 'scriptId',
        bulkExportUrl: String(config.dataset.exportScriptsUrl || ''), bulkRequestProperty: 'scriptIds',
        preflightUrl: String(config.dataset.preflightImportScriptUrl || ''), importUrl: String(config.dataset.importScriptUrl || ''),
        loginUrl: String(config.dataset.loginUrl || window.mauiAppUrl('/Account/SessionExpired')),
        status: showStatus
    });

    // Purpose: Builds the URL for one controller endpoint.
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
        const returnUrl = window.MauiGridNavigationState?.createReturnUrl?.('scripts')
            || `${window.location.pathname}${window.location.search}${window.location.hash}`;
        url.searchParams.set('returnUrl', returnUrl);
        url.searchParams.set('returnLabel', 'Scripts');
        window.location.assign(url.toString());
    }

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

    // Purpose: Calls a Script endpoint and normalizes JSON/error handling.
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

    // Purpose: Closes reserved popup.
    function closeReservedPopup(popup) {
        if (!popup) return;
        try { popup.close(); } catch { /* ignored */ }
    }

    // Honor the shared Script preference for custom edits and read-only built-in viewing. Native custom opens capture Undo first.
    async function launchNativeScriptEditor(scriptId, scriptName, popup = null, markEdited = true, isBuiltIn = false) {
        if (window.MauiEditorRouting?.useMauiScriptEditor) {
            closeReservedPopup(popup);
            return window.MauiEditorRouting.openScript(scriptId, scriptName, { isBuiltIn });
        }
        const normalizedScriptId = String(scriptId || '').trim();
        let normalizedScriptName = String(scriptName || '').trim();
        if (!normalizedScriptId) return false;
        let permissionReadOnly=false;
        try { permissionReadOnly=window.MauiObjectAccess ? !(await window.MauiObjectAccess.read('Script',normalizedScriptId)).canEdit : false; }
        catch(error) { showStatus(error.message || 'The object is unavailable.',5000,'error'); closeReservedPopup(popup); return false; }

        if (!window.MauiNativeEditor?.open || !window.MauiNativeEditor?.reserve) {
            showStatus('The native MOVEit editor launcher is unavailable.', 5000, 'error');
            closeReservedPopup(popup);
            return false;
        }

        const reservedPopup = popup || window.MauiNativeEditor.reserve(`MAUI Script Editor - ${normalizedScriptId}`);
        if (!reservedPopup || reservedPopup.closed) {
            showStatus('The MOVEit native editor popup was blocked by the browser.', 5000, 'error');
            return false;
        }

        if (!isBuiltIn && !permissionReadOnly) {
            try {
                const prepared = await fetchJson(endpoint('prepareScriptEditUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ scriptId: normalizedScriptId })
                });
                normalizedScriptName = String(prepared?.scriptName || normalizedScriptName || 'Script').trim();
                setScriptUndoAvailable(normalizedScriptId, true);
            } catch (error) {
                closeReservedPopup(reservedPopup);
                showStatus(`Script Editor: ${error?.message || 'Unable to save the Script undo snapshot.'}`, 5000, 'error');
                return false;
            }
        }

        const opened = await window.MauiNativeEditor.open({
            popup: reservedPopup,
            sessionUrl: endpoint('nativeEditorSessionUrl'),
            loginUrl: endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'),
            antiForgeryToken,
            returnHash: `#/resources/scripts/${encodeURIComponent(normalizedScriptId)}?HideNav`,
            windowTitle: `MAUI Script Editor - ${normalizedScriptId}: ${normalizedScriptName || 'Script'}`,
            skin: 'maui',
            status: showStatus,
            disableUndo: isBuiltIn || permissionReadOnly,
        });

        if (opened && markEdited && !isBuiltIn && !permissionReadOnly) {
            try {
                await fetchJson(endpoint('markScriptEditedUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ scriptId: normalizedScriptId })
                });
            } catch (error) {
                console.warn('MAUI could not update the Script modification audit information.');
            }
        }
        return opened;
    }

    // Purpose: Sets script undo available.
    function setScriptUndoAvailable(scriptId, available) {
        const normalizedScriptId = String(scriptId || '').trim();
        if (!normalizedScriptId) return;
        document.querySelectorAll('[data-script-row-menu-button]').forEach(button => {
            if (String(button.dataset.rowId || '').trim() === normalizedScriptId) {
                button.dataset.scriptHasUndo = available ? 'true' : 'false';
            }
        });
    }

    // Opens a Task shown on the Script Information dependency tab using the configured editor preference.
    async function launchTaskEditor(taskId, taskName) {
        try { return await window.MauiEditorRouting.openTask(taskId, taskName || ''); }
        catch (error) { showStatus(error?.message || 'Unable to open the Task Editor.', 5000, 'error'); return false; }
    }
    // ---------------------------------------------------------------------
    // New Script
    // ---------------------------------------------------------------------
    const newScriptDialog = document.querySelector('[data-new-script-dialog]');
    const newScriptForm = newScriptDialog?.querySelector('[data-new-script-form]');
    const newScriptName = newScriptDialog?.querySelector('[data-new-script-name]');
    const newScriptSubmit = newScriptDialog?.querySelector('[data-new-script-submit]');
    const newScriptCancel = newScriptDialog?.querySelector('[data-new-script-cancel]');
    const newScriptClose = newScriptDialog?.querySelector('[data-new-script-close]');
    const newScriptError = newScriptDialog?.querySelector('[data-new-script-error]');
    const newScriptSpinner = newScriptDialog?.querySelector('[data-new-script-spinner]');
    const newScriptSubmitLabel = newScriptDialog?.querySelector('[data-new-script-submit-label]');
    let newScriptBusy = false, newScriptUncertain = false;
    const updateNewScriptAction = window.MauiModalUI.watchAction(newScriptDialog, newScriptSubmit,
        () => !newScriptBusy && !newScriptUncertain,
        () => Boolean(newScriptName?.value.trim()) && newScriptName.value.trim().length <= 255 && window.MauiModalUI.formValid(newScriptDialog));

    // Purpose: Opens new script dialog.
    function openNewScriptDialog() {
        if (window.MauiEditorRouting?.useMauiScriptEditor) { void window.MauiScriptEditor.open(); return; }
        if (!newScriptDialog) return;
        resetNewScriptDialog();
        newScriptDialog.showModal();
        window.requestAnimationFrame(() => newScriptName?.focus());
    }

    // Purpose: Resets new script dialog.
    function resetNewScriptDialog() {
        newScriptUncertain = false;
        setNewScriptBusy(false);
        hideNewScriptError();
        if (newScriptName) newScriptName.value = '';
        const powerShell = newScriptDialog?.querySelector('input[name="newScriptLanguage"][value="PS1"]');
        if (powerShell) powerShell.checked = true;
    }

    // Purpose: Sets new script busy.
    function setNewScriptBusy(busy) {
        newScriptBusy = busy;
        if (newScriptCancel) newScriptCancel.disabled = busy;
        if (newScriptClose) newScriptClose.disabled = busy;
        if (newScriptName) newScriptName.disabled = busy;
        newScriptDialog?.querySelectorAll('input[name="newScriptLanguage"]').forEach(input => { input.disabled = busy; });
        if (newScriptSpinner) newScriptSpinner.hidden = !busy;
        if (newScriptSubmitLabel) newScriptSubmitLabel.textContent = busy ? 'Creating…' : 'Create Script';
        updateNewScriptAction();
    }

    // Purpose: Displays new script error.
    function showNewScriptError(message) {
        if (!newScriptError) return;
        newScriptError.textContent = message || 'Unable to create the Script.';
        newScriptError.hidden = false;
    }

    // Purpose: Clears new script error.
    function hideNewScriptError() {
        if (!newScriptError) return;
        newScriptError.textContent = '';
        newScriptError.hidden = true;
    }

    // Purpose: Creates a Script and returns to the calling grid.
    async function createNewScript() {
        if (newScriptBusy || newScriptUncertain) return;
        const name = String(newScriptName?.value || '').trim();
        const language = String(newScriptDialog?.querySelector('input[name="newScriptLanguage"]:checked')?.value || 'PS1').trim();
        if (!name) {
            showNewScriptError('Script Name is required.');
            newScriptName?.focus();
            return;
        }
        hideNewScriptError();
        setNewScriptBusy(true);
        try {
            const data = await fetchJson(endpoint('createScriptUrl'), {
                method: 'POST',
                body: JSON.stringify({ name, language, resourceGroup: window.MauiObjectAccess?.group(newScriptDialog) || '' })
            });
            const scriptId = String(data?.script?.id || '').trim();
            const scriptName = String(data?.script?.name || name).trim();
            if (!scriptId) throw new Error('MOVEit created the Script but did not return its ID.');

            newScriptDialog?.close();
            showStatus(`Created Script ${scriptName}.`, 5000);
            await refreshScriptGrid(true);
        } catch (error) {
            newScriptUncertain = Boolean(error.payload?.moveItSaved || !error.status);
            showNewScriptError((error?.message || 'Unable to create the Script.') + (newScriptUncertain ? ' Close and inspect Scripts before creating another Script.' : ''));
        } finally {
            setNewScriptBusy(false);
        }
    }

    // ---------------------------------------------------------------------
    // Contextual row menu
    // ---------------------------------------------------------------------
    const scriptRowActionMenu = document.querySelector('[data-script-row-action-menu]');
    const scriptRowExportCommand = scriptRowActionMenu?.querySelector('[data-script-row-command="export"]');
    const scriptRowUndoCommand = scriptRowActionMenu?.querySelector('[data-script-row-command="undo"]');
    const scriptRowDeleteCommand = scriptRowActionMenu?.querySelector('[data-script-row-command="delete"]');
    const scriptUndoDivider = scriptRowActionMenu?.querySelector('[data-script-undo-divider]');
    const scriptDeleteDivider = scriptRowActionMenu?.querySelector('[data-script-delete-divider]');
    let activeScriptMenuContext = null;

    // Purpose: Opens script row action menu.
    function openScriptRowActionMenu(button) {
        if (!scriptRowActionMenu || !button) return;
        if (activeScriptMenuContext?.button === button && !scriptRowActionMenu.hidden) {
            closeScriptRowActionMenu();
            return;
        }

        closeScriptRowActionMenu();
        window.MauiObjectAccess?.reset(scriptRowActionMenu);
        activeScriptMenuContext = {
            button,
            id: String(button.dataset.rowId || '').trim(),
            name: String(button.dataset.scriptName || '').trim(),
            infoUrl: String(button.dataset.scriptInfoUrl || '').trim(),
            hasUndo: button.dataset.scriptHasUndo === 'true',
            canEditDelete: button.dataset.scriptCanEditDelete !== 'false',
            isBuiltIn: button.dataset.scriptBuiltIn === 'true'
        };
        if (scriptRowExportCommand) {
            scriptRowExportCommand.hidden = false;
            scriptRowExportCommand.disabled = activeScriptMenuContext.isBuiltIn;
            scriptRowExportCommand.title = activeScriptMenuContext.isBuiltIn ? 'Built-in MOVEit Scripts are read-only and are not portable MAUI custom Scripts.' : '';
        }
        if (scriptRowUndoCommand) {
            const undoUnavailable = activeScriptMenuContext.isBuiltIn || !activeScriptMenuContext.hasUndo;
            scriptRowUndoCommand.hidden = false;
            scriptRowUndoCommand.disabled = undoUnavailable;
            scriptRowUndoCommand.title = activeScriptMenuContext.isBuiltIn
                ? 'Built-in MOVEit Scripts do not support MAUI Undo.'
                : (activeScriptMenuContext.hasUndo ? '' : 'Undo is not available until MAUI has saved a prior Script configuration.');
        }
        if (scriptUndoDivider) scriptUndoDivider.hidden = false;
        if (scriptRowDeleteCommand) {
            // Built-in scripts are read-only. Keep Delete visible but disabled so menu structure stays consistent.
            scriptRowDeleteCommand.hidden = false;
            scriptRowDeleteCommand.disabled = activeScriptMenuContext.isBuiltIn;
            scriptRowDeleteCommand.title = activeScriptMenuContext.isBuiltIn ? 'Built-in MOVEit Scripts are read-only.' : '';
        }
        if (scriptDeleteDivider) scriptDeleteDivider.hidden = false;
        button.setAttribute('aria-expanded', 'true');
        void window.MauiObjectAccess?.menu(scriptRowActionMenu,'Script',activeScriptMenuContext.id);
        scriptRowActionMenu.hidden = false;
        scriptRowActionMenu.style.visibility = 'hidden';

        const rect = button.getBoundingClientRect();
        const menuRect = scriptRowActionMenu.getBoundingClientRect();
        let left = rect.left;
        let top = rect.bottom + 4;
        if (left + menuRect.width > window.innerWidth - 8) left = Math.max(8, window.innerWidth - menuRect.width - 8);
        if (top + menuRect.height > window.innerHeight - 8) top = Math.max(8, rect.top - menuRect.height - 4);
        scriptRowActionMenu.style.left = `${Math.round(left)}px`;
        scriptRowActionMenu.style.top = `${Math.round(top)}px`;
        scriptRowActionMenu.style.visibility = '';
    }

    // Purpose: Closes script row action menu.
    function closeScriptRowActionMenu() {
        activeScriptMenuContext?.button?.setAttribute('aria-expanded', 'false');
        activeScriptMenuContext = null;
        if (!scriptRowActionMenu) return;
        scriptRowActionMenu.hidden = true;
        scriptRowActionMenu.style.left = '';
        scriptRowActionMenu.style.top = '';
        scriptRowActionMenu.style.visibility = '';
    }

    // Purpose: Dispatches one command from the per-row Script actions menu.
    function handleScriptRowCommand(command) {
        const context = activeScriptMenuContext;
        closeScriptRowActionMenu();
        if (!context) return;
        if (command === 'information') { openScriptDetails(context.id, context.infoUrl); return; }
        if (command === 'audit') { navigateToReport(endpoint('auditReportUrl'), { targetType: 'script', targetId: context.id, targetName: context.name }); return; }
        if (command === 'export') { portability?.exportItem(context.id, context.name); return; }
        if (command === 'edit') { launchNativeScriptEditor(context.id, context.name, null, true, context.isBuiltIn); return; }
        if (command === 'undo') { beginUndoScript(context.id, context.name); return; }
        if (command === 'delete') beginDeleteScripts([context.id], context.name);
    }

    // ---------------------------------------------------------------------
    // Undo Changes
    // ---------------------------------------------------------------------
    const undoScriptDialog = document.querySelector('[data-undo-script-dialog]');
    const undoScriptMessage = undoScriptDialog?.querySelector('[data-undo-script-message]');
    const undoScriptHistory = undoScriptDialog?.querySelector('[data-undo-script-history]');
    const undoScriptError = undoScriptDialog?.querySelector('[data-undo-script-error]');
    const undoScriptYes = undoScriptDialog?.querySelector('[data-undo-script-yes]');
    const undoScriptNoButtons = Array.from(undoScriptDialog?.querySelectorAll('[data-undo-script-no]') || []);
    let pendingUndoScript = null;
    let undoScriptBusy = false;

    // Purpose: Formats undo snapshot date.
    function formatUndoSnapshotDate(value) {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? String(value || '') : date.toLocaleString();
    }

    // Purpose: Loads Script undo history and opens the restore dialog.
    async function beginUndoScript(scriptId, scriptName) {
        const id = String(scriptId || '').trim();
        if (!id || undoScriptBusy) return;
        undoScriptBusy = true;
        try {
            const preflight = await fetchJson(endpoint('preflightUndoScriptUrl'), {
                method: 'POST',
                body: JSON.stringify({ scriptId: id })
            });

            const snapshots = Array.isArray(preflight?.snapshots) ? preflight.snapshots : [];
            if (!preflight?.success || snapshots.length === 0) {
                showStatus(preflight?.message || 'Undo is not available for this Script.', 5000);
                return;
            }

            const name = String(preflight?.scriptName || scriptName || id).trim();
            if (undoScriptHistory) {
                undoScriptHistory.replaceChildren();
                snapshots.forEach((snapshot, index) => {
                    const canRestore = snapshot?.canRestore !== false;
                    const row = document.createElement('label');
                    row.className = `undo-history-option${canRestore ? '' : ' is-disabled'}`;
                    const radio = document.createElement('input');
                    radio.type = 'radio'; radio.name = 'scriptUndoSnapshot'; radio.value = String(snapshot?.snapshotId || ''); radio.disabled = !canRestore;
                    const body = document.createElement('span'); body.className = 'undo-history-option-main';
                    const date = document.createElement('span'); date.className = 'undo-history-option-date'; date.textContent = formatUndoSnapshotDate(snapshot?.snapshotCreatedUtc);
                    const meta = document.createElement('span'); meta.className = 'undo-history-option-meta'; meta.textContent = `${index === 0 ? 'Most recent' : `Version ${index + 1}`} · Saved by ${String(snapshot?.snapshotCreatedBy || 'current user')}`;
                    body.append(date, meta);
                    const warningText = String(snapshot?.message || '').trim();
                    if (warningText) { const warning = document.createElement('span'); warning.className = 'undo-history-option-warning'; warning.textContent = warningText; body.append(warning); }
                    row.append(radio, body); undoScriptHistory.append(row);
                });
            }
            const first = undoScriptHistory?.querySelector('input[type="radio"]:not(:disabled)');
            if (first) first.checked = true;
            const snapshotId = first?.value || '';
            pendingUndoScript = { scriptId: id, scriptName: name, snapshotId };
            undoScriptHistory?.querySelectorAll('input[type="radio"]').forEach(radio => radio.addEventListener('change', () => { pendingUndoScript.snapshotId = radio.value; if (undoScriptYes) undoScriptYes.disabled = false; }));
            if (undoScriptYes) undoScriptYes.disabled = !snapshotId;
            if (undoScriptMessage) undoScriptMessage.textContent = `Select the version of Script "${name}" to restore.`;
            if (undoScriptError) { undoScriptError.hidden = true; undoScriptError.textContent = ''; }
            undoScriptDialog?.showModal();
            window.requestAnimationFrame(() => undoScriptNoButtons.find(button => button.textContent.trim() === 'Cancel')?.focus());
        } catch (error) {
            showStatus(`Undo Changes: ${error?.message || 'Unable to validate the Script undo snapshot.'}`, 5000, 'error');
        } finally {
            undoScriptBusy = false;
        }
    }

    // Purpose: Restores the selected Script undo snapshot.
    async function confirmUndoScript() {
        if (undoScriptBusy || !pendingUndoScript?.scriptId || !pendingUndoScript?.snapshotId) return;
        if (!window.MauiEditorRouting?.useMauiScriptEditor && !window.MauiNativeEditor?.reserve) {
            if (undoScriptError) {
                undoScriptError.textContent = 'The MAUI Script Editor launcher is unavailable. Reload MAUI and try again.';
                undoScriptError.hidden = false;
            }
            return;
        }

        const popup = window.MauiEditorRouting?.useMauiScriptEditor ? null : window.MauiNativeEditor.reserve(`MAUI Script Editor - ${pendingUndoScript.scriptId}`);
        if (!popup && !window.MauiEditorRouting?.useMauiScriptEditor) {
            if (undoScriptError) {
                undoScriptError.textContent = 'The MOVEit native editor popup was blocked by the browser.';
                undoScriptError.hidden = false;
            }
            return;
        }

        undoScriptBusy = true;
        if (undoScriptYes) undoScriptYes.disabled = true;
        undoScriptNoButtons.forEach(button => { button.disabled = true; });
        try {
            const data = await fetchJson(endpoint('undoScriptUrl'), {
                method: 'POST',
                body: JSON.stringify({ scriptId: pendingUndoScript.scriptId, snapshotId: pendingUndoScript.snapshotId })
            });
            const restoredId = String(data?.scriptId || pendingUndoScript.scriptId).trim();
            const restoredName = String(data?.scriptName || pendingUndoScript.scriptName || restoredId).trim();
            const warning = String(data?.warning || '').trim();
            undoScriptDialog?.close();
            pendingUndoScript = null;
            showStatus(warning ? `Undo Changes: ${warning}` : `Undo Changes: restored Script ${restoredName}.`, 5000);

            const opened = await launchNativeScriptEditor(restoredId, restoredName, popup);
            if (!opened) closeReservedPopup(popup);
            await refreshScriptGrid(true);
        } catch (error) {
            closeReservedPopup(popup);
            if (undoScriptError) {
                undoScriptError.textContent = error?.message || 'Unable to restore the Script undo snapshot.';
                undoScriptError.hidden = false;
            }
        } finally {
            undoScriptBusy = false;
            if (undoScriptYes) undoScriptYes.disabled = false;
            undoScriptNoButtons.forEach(button => { button.disabled = false; });
        }
    }

    // Purpose: Cancels undo script.
    function cancelUndoScript() {
        if (undoScriptBusy) return;
        pendingUndoScript = null;
        undoScriptDialog?.close();
    }

    // ---------------------------------------------------------------------
    // Delete Script / Delete Scripts
    // ---------------------------------------------------------------------
    const deleteScriptDialog = document.querySelector('[data-delete-script-dialog]');
    const deleteScriptTitle = deleteScriptDialog?.querySelector('[data-delete-script-title]');
    const deleteScriptMessage = deleteScriptDialog?.querySelector('[data-delete-script-message]');
    const deleteScriptError = deleteScriptDialog?.querySelector('[data-delete-script-error]');
    const deleteScriptList = deleteScriptDialog?.querySelector('[data-delete-script-list]');
    const deleteScriptYes = deleteScriptDialog?.querySelector('[data-delete-script-yes]');
    const deleteScriptNoButtons = Array.from(deleteScriptDialog?.querySelectorAll('[data-delete-script-no]') || []);
    let pendingDeleteScriptIds = [];
    let deleteScriptsWithoutRecycle = false;
    let deleteScriptBusy = false;

    // Purpose: Normalizes selected script ids.
    function normalizeSelectedScriptIds(ids) {
        return Array.from(new Set((ids || []).map(id => String(id || '').trim()).filter(Boolean)));
    }

    // Purpose: Formats delete scripts result.
    function formatDeleteScriptsResult(data) {
        const succeeded = Number(data?.succeeded || 0);
        const skipped = Number(data?.skipped || 0);
        const failed = Number(data?.failed || 0);
        const parts = [`Delete Script: ${succeeded} succeeded`];
        if (skipped) parts.push(`${skipped} skipped`);
        if (failed) parts.push(`${failed} failed`);

        const failures = Array.isArray(data?.items)
            ? data.items.filter(item => item && item.success === false).slice(0, 3)
            : [];
        if (failures.length) {
            parts.push(failures.map(item => `${item.name || item.id}: ${item.message || (item.skipped ? 'Skipped' : 'Failed')}`).join(' | '));
        }
        return parts.join(', ');
    }

    // Purpose: Runs delete preflight and opens the Script confirmation dialog.
    async function beginDeleteScripts(scriptIds, singleScriptName = '') {
        const ids = normalizeSelectedScriptIds(scriptIds);
        if (!ids.length) {
            showStatus('Select at least one custom Script.', 5000);
            return;
        }
        if (deleteScriptBusy || undoScriptBusy || newScriptBusy) {
            showStatus('Another Script action is already in progress.', 5000);
            return;
        }

        deleteScriptBusy = true;
        try {
            const preflight = await fetchJson(endpoint('preflightDeleteScriptsUrl'), {
                method: 'POST',
                body: JSON.stringify({ scriptIds: ids })
            });

            if (!preflight?.success) {
                const blockers = Array.isArray(preflight?.blockers) ? preflight.blockers : [];
                const details = blockers.slice(0, 3).map(item => {
                    const tasks = Array.isArray(item?.taskReferences) ? item.taskReferences : [];
                    const taskText = tasks.length
                        ? ` Tasks: ${tasks.slice(0, 3).map(task => task.name || task.id).join(', ')}${tasks.length > 3 ? '…' : ''}.`
                        : '';
                    return `${item.scriptName || item.scriptId}: ${item.message || 'Cannot delete.'}${taskText}`;
                });
                showStatus([preflight?.message || 'The selected Scripts cannot be deleted.', ...details].filter(Boolean).join(' | '), 5000);
                return;
            }

            const targets = Array.isArray(preflight?.targets)
                ? preflight.targets.map(item => ({
                    scriptId: String(item?.scriptId || '').trim(),
                    scriptName: String(item?.scriptName || item?.scriptId || '').trim()
                })).filter(item => item.scriptId)
                : ids.map(id => ({ scriptId: id, scriptName: id }));

            pendingDeleteScriptIds = ids;
            deleteScriptsWithoutRecycle = false;
            if (deleteScriptYes) deleteScriptYes.textContent = 'Yes, Delete';
            if (deleteScriptTitle) deleteScriptTitle.textContent = ids.length === 1 ? 'Delete Script' : 'Delete Scripts';
            if (deleteScriptMessage) {
                deleteScriptMessage.textContent = ids.length === 1
                    ? 'The following Script will be deleted from MOVEit Automation:'
                    : `The following ${ids.length} Scripts will be deleted from MOVEit Automation:`;
            }
            if (deleteScriptList) {
                deleteScriptList.replaceChildren();
                targets.forEach(item => {
                    const row = document.createElement('div');
                    row.className = 'delete-object-list-item';
                    row.textContent = item.scriptName || item.scriptId;
                    deleteScriptList.appendChild(row);
                });
                deleteScriptList.hidden = targets.length === 0;
            }
            if (deleteScriptError) {
                deleteScriptError.hidden = true;
                deleteScriptError.textContent = '';
            }

            deleteScriptDialog?.showModal();
            window.requestAnimationFrame(() => deleteScriptNoButtons.find(button => button.textContent.trim() === 'Cancel')?.focus());
        } catch (error) {
            showStatus(`Delete Script: ${error?.message || 'Dependency check failed.'}`, 5000, 'error');
        } finally {
            deleteScriptBusy = false;
        }
    }

    // Purpose: Deletes the confirmed Script selection with Recycle Bin safeguards.
    async function confirmDeleteScript() {
        if (deleteScriptBusy || !pendingDeleteScriptIds.length) return;
        deleteScriptBusy = true;
        if (deleteScriptYes) deleteScriptYes.disabled = true;
        deleteScriptNoButtons.forEach(button => { button.disabled = true; });

        try {
            const data = await fetchJson(endpoint('deleteScriptsUrl'), {
                method: 'POST',
                body: JSON.stringify({ scriptIds: pendingDeleteScriptIds, deleteWithoutRecycle: deleteScriptsWithoutRecycle })
            });

            deleteScriptDialog?.close();
            const deletedScriptIds = Array.isArray(data?.items)
                ? data.items.filter(item => item?.success === true).map(item => item?.id).filter(Boolean)
                : [];
            grid?.dispatchEvent(new CustomEvent('maui:grid-remove-rows', { detail: { rowIds: deletedScriptIds } }));
            pendingDeleteScriptIds = [];
            deleteScriptsWithoutRecycle = false;
            if (deleteScriptYes) deleteScriptYes.textContent = 'Yes, Delete';
            const warning = String(data?.warning || '').trim();
            const summary = formatDeleteScriptsResult(data);
            showStatus([summary, warning].filter(Boolean).join(' '), 5000);
            await refreshScriptGrid(true);
        } catch (error) {
            if (error?.status === 409 && error?.payload?.code === 'RecycleCaptureFailed') {
                deleteScriptsWithoutRecycle = true;
                if (deleteScriptError) { deleteScriptError.textContent = error?.message || 'MAUI could not create a recovery copy. Delete anyway?'; deleteScriptError.hidden = false; }
                if (deleteScriptYes) deleteScriptYes.textContent = 'Delete Anyway';
            } else if (deleteScriptError) {
                deleteScriptError.textContent = error?.message || 'Unable to delete the selected Scripts.';
                deleteScriptError.hidden = false;
            }
        } finally {
            deleteScriptBusy = false;
            if (deleteScriptYes) deleteScriptYes.disabled = false;
            deleteScriptNoButtons.forEach(button => { button.disabled = false; });
        }
    }

    // Purpose: Cancels delete script.
    function cancelDeleteScript() {
        if (deleteScriptBusy) return;
        pendingDeleteScriptIds = [];
        deleteScriptsWithoutRecycle = false;
        if (deleteScriptYes) deleteScriptYes.textContent = 'Yes, Delete';
        if (deleteScriptList) { deleteScriptList.replaceChildren(); deleteScriptList.hidden = true; }
        deleteScriptDialog?.close();
    }

    // ---------------------------------------------------------------------
    // Script Information
    // ---------------------------------------------------------------------
    const detailsDialog = document.querySelector('[data-script-details-dialog]');
    const detailsLoading = detailsDialog?.querySelector('[data-script-details-loading]');
    const detailsContent = detailsDialog?.querySelector('[data-script-details-content]');
    const detailsError = detailsDialog?.querySelector('[data-script-details-error]');
    const detailsState = window.mauiCreateInformationDialogState({
        loadingElement: detailsLoading,
        contentElement: detailsContent,
        errorElement: detailsError,
        defaultErrorMessage: 'Unable to load Script information.'
    });
    const detailsSave = detailsDialog?.querySelector('[data-script-details-save]');
    const detailsCancel = detailsDialog?.querySelector('[data-script-details-cancel]');
    const detailsTabs = Array.from(detailsDialog?.querySelectorAll('[data-script-details-tab]') || []);
    const informationPanel = detailsDialog?.querySelector('[data-script-details-panel="information"]');
    const tasksPanel = detailsDialog?.querySelector('[data-script-details-panel="tasks"]');
    const metadataFields = detailsDialog?.querySelector('[data-script-metadata-fields]');
    const metadataAudit = detailsDialog?.querySelector('[data-script-metadata-audit]');
    const scriptTasksRows = detailsDialog?.querySelector('[data-script-tasks-rows]');
    const scriptTasksList = detailsDialog?.querySelector('[data-script-tasks-list]');
    const scriptTasksEmpty = detailsDialog?.querySelector('[data-script-tasks-empty]');
    let activeScriptId = '';
    let activeInfoUrl = '';
    const detailsReadiness = window.MauiModalUI.registerSaveAction(detailsDialog, detailsSave, () => {
        const name = detailsDialog.querySelector('[data-script-name]');
        return !name || name.disabled || name.readOnly || name.value.trim().length > 0 && name.value.trim().length <= 255;
    });


    // Purpose: Loads and displays Script information, metadata, and dependencies.
    async function openScriptDetails(scriptId, infoUrl) {
        if (!detailsDialog || !scriptId || !infoUrl) return;
        activeScriptId = String(scriptId).trim();
        activeInfoUrl = infoUrl;
        resetScriptDetailsDialog();
        detailsDialog.showModal();
        try {
            const data = await fetchJson(infoUrl, { method: 'GET', cache: 'no-store' });
            populateScriptDetails(data);
            await window.MauiObjectAccess?.information(detailsDialog,'Script',activeScriptId);
            window.MauiModalUI?.track(detailsDialog);
        } catch (error) {
            showDetailsError(error.message);
        }
    }

    // Purpose: Resets script details dialog.
    function resetScriptDetailsDialog() {
        window.MauiObjectAccess?.clearInformation(detailsDialog);
        if (!detailsDialog) return;
        detailsReadiness.reset();
        detailsState.reset();
        detailsSave.disabled = true;
        metadataFields?.replaceChildren();
        metadataAudit?.replaceChildren();
        scriptTasksRows?.replaceChildren();
        if (scriptTasksEmpty) scriptTasksEmpty.hidden = true;
        if (scriptTasksList) scriptTasksList.hidden = true;
        setScriptDetailsTab('information');
    }

    // Purpose: Populates script details.
    function populateScriptDetails(data) {
        detailsDialog.dataset.editVersion = String(data.editVersion || '');
        const script = data.script || {};
        detailsDialog.querySelector('[data-script-type]').textContent = script.type || (script.isBuiltIn ? 'Built-In' : 'Custom');
        detailsDialog.querySelector('[data-script-language]').value = script.language || '—';
        detailsDialog.querySelector('[data-script-id]').textContent = script.id || activeScriptId;
        const scriptNameInput = detailsDialog.querySelector('[data-script-name]');
        const scriptDescriptionInput = detailsDialog.querySelector('[data-script-description]');
        if (scriptNameInput) { scriptNameInput.value = script.name || ''; scriptNameInput.readOnly = Boolean(script.isBuiltIn); }
        if (scriptDescriptionInput) { scriptDescriptionInput.value = script.description || ''; scriptDescriptionInput.readOnly = Boolean(script.isBuiltIn); }
        const resourceGroupsInput = detailsDialog.querySelector('[data-script-resource-groups]');
        if (resourceGroupsInput) resourceGroupsInput.value = script.resourceGroups || '—';
        populateMetadataFields(data.fields || [], data.metadata || {});
        populateAudit(data.audit || {});
        populateScriptTasks(data.tasks || []);
        detailsState.showContent();
        detailsSave.disabled = false;
    }

    // Purpose: Sets script details tab.
    function setScriptDetailsTab(tabName) {
        const showTasks = tabName === 'tasks';
        detailsTabs.forEach(tab => {
            const selected = tab.dataset.scriptDetailsTab === tabName;
            tab.classList.toggle('active', selected);
            tab.setAttribute('aria-selected', selected ? 'true' : 'false');
        });
        if (informationPanel) informationPanel.hidden = showTasks;
        if (tasksPanel) tasksPanel.hidden = !showTasks;
        if (detailsSave) detailsSave.hidden = showTasks;
    }

    // Purpose: Populates metadata fields.
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
            control.dataset.scriptMetadataField = field.name;
            control.required = Boolean(field.required);
            label.appendChild(control);
            fragment.appendChild(label);
        });
        metadataFields.replaceChildren(fragment);
    }

    // Purpose: Creates metadata control.
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

    // Purpose: Populates audit.
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

    // Purpose: Populates script tasks.
    function populateScriptTasks(tasks) {
        scriptTasksRows?.replaceChildren();
        if (!Array.isArray(tasks) || tasks.length === 0) {
            if (scriptTasksEmpty) scriptTasksEmpty.hidden = false;
            if (scriptTasksList) scriptTasksList.hidden = true;
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
            link.dataset.scriptTaskEditor = 'true';
            link.dataset.taskId = task.id || '';
            link.dataset.taskName = task.name || '';
            link.textContent = task.name || task.id || 'Task';
            name.appendChild(link);
            row.append(id, name);
            fragment.appendChild(row);
        });
        scriptTasksRows?.replaceChildren(fragment);
        if (scriptTasksEmpty) scriptTasksEmpty.hidden = true;
        if (scriptTasksList) scriptTasksList.hidden = false;
    }

    // Purpose: Saves editable Script details and MAUI metadata.
    async function saveScriptDetails() {
        if (!activeScriptId || !metadataFields || detailsSave.disabled) return;
        hideDetailsError();
        if (window.mauiValidateMetadataControls && !window.mauiValidateMetadataControls(metadataFields)) return;
        detailsReadiness.busy(true);
        const metadata = {};
        metadataFields.querySelectorAll('[data-script-metadata-field]').forEach(control => {
            metadata[control.dataset.scriptMetadataField] = control.value ?? '';
        });

        try {
            await fetchJson(`${endpoint('saveScriptDetailsUrl')}?id=${encodeURIComponent(activeScriptId)}`, {
                method: 'POST',
                body: JSON.stringify({ editVersion: detailsDialog.dataset.editVersion || '', name: detailsDialog.querySelector('[data-script-name]')?.value || '', description: detailsDialog.querySelector('[data-script-description]')?.value || '', metadata })
            });
            detailsDialog.close();
            showStatus('Script information saved.', 5000);
            await refreshScriptGrid(true);
        } catch (error) {
            showDetailsError(error.message);
            detailsReadiness.block(Boolean(error.payload?.moveItSaved || error.payload?.editConflict || !error.status)); detailsReadiness.busy(false);
        }
    }

    // Purpose: Displays a details-dialog error.
    function showDetailsError(message) {
        detailsState.showError(message);
    }

    // Purpose: Clears the details-dialog error state.
    function hideDetailsError() {
        detailsState.hideError();
    }

    // Purpose: Formats a date value for the current UI.
    function formatDate(value) {
        if (!value) return '—';
        const date = new Date(value);
        return Number.isNaN(date.valueOf()) ? '—' : date.toLocaleString();
    }

    // ---------------------------------------------------------------------
    // Live Script grid refresh
    // ---------------------------------------------------------------------
    const refreshControl = document.querySelector('[data-script-refresh-control]');
    const uiScriptRefreshSeconds = Math.max(0, Number(config.dataset.uiScriptRefreshSeconds || 0));
    const scriptRefreshController = window.mauiCreateResourceRefreshController({
        grid,
        refreshControl,
        lastRefreshElement: refreshControl?.querySelector('[data-script-last-refresh]'),
        refreshClock: refreshControl?.querySelector('[data-script-refresh-clock]'),
        refreshToggle: refreshControl?.querySelector('[data-script-refresh-toggle]'),
        refreshNow: refreshControl?.querySelector('[data-script-refresh-now]'),
        intervalSeconds: uiScriptRefreshSeconds,
        fetchJson,
        buildInitialUrl: () => {
            const url = new URL(endpoint('initialScriptGridUrl'), window.location.href);
            url.searchParams.set('preferCached', 'true');
            return url;
        },
        buildRefreshUrl: forceRefresh => {
            const url = new URL(endpoint('refreshScriptGridUrl'), window.location.href);
            url.searchParams.set('forceRefresh', forceRefresh ? 'true' : 'false');
            return url;
        },
        beforeRefresh: closeScriptRowActionMenu,
        onInitialError: error => {
            console.warn('Initial Script grid load failed.');
            showStatus(`Script load failed: ${error.message}`, 5000, 'error', 'page');
        },
        onRefreshError: error => {
            console.warn('Script grid refresh failed.');
            showStatus(`Script refresh failed: ${error.message}`, 5000, 'error', 'page');
        },
        pausedTitle: 'Script refresh paused',
        activeTitle: 'Script refresh options',
        persistLastRefresh: true,
        setAriaBusy: true
    });

    function refreshScriptGrid(forceRefresh) {
        return scriptRefreshController.refresh(forceRefresh);
    }

    // ---------------------------------------------------------------------
    // Event wiring
    // ---------------------------------------------------------------------
    document.addEventListener('maui:grid-action', event => {
        const gridId = String(event.detail?.gridId || '').trim().toLowerCase();
        const actionName = String(event.detail?.action || '').trim().toLowerCase();
        if (gridId !== 'scripts') return;
        if (actionName === 'new script') {
            event.preventDefault();
            openNewScriptDialog();
            return;
        }
        if (actionName === 'import scripts' || actionName === 'import script') {
            event.preventDefault();
            portability?.openImport();
            return;
        }
        if (actionName === 'export scripts' || actionName === 'export selected scripts') {
            event.preventDefault();
            portability?.exportSelected(event.detail?.selectedIds || []);
            return;
        }
        if (actionName === 'delete scripts') {
            event.preventDefault();
            beginDeleteScripts(event.detail?.selectedIds || []);
        }
    });

    document.addEventListener('maui:resource-imported', async event => {
        if (event.detail?.key !== 'script') return;
        await refreshScriptGrid(true);
        const id=String(event.detail?.id||'').trim(), name=String(event.detail?.name||'').trim();
        if (id) launchNativeScriptEditor(id,name,event.detail?.popup||null,true,false);
    });

    document.addEventListener('click', event => {
        const menuButton = event.target.closest('[data-script-row-menu-button]');
        if (menuButton) {
            event.preventDefault();
            event.stopPropagation();
            openScriptRowActionMenu(menuButton);
            return;
        }

        const menuCommand = event.target.closest('[data-script-row-command]');
        if (menuCommand && scriptRowActionMenu?.contains(menuCommand)) {
            event.preventDefault();
            if (!menuCommand.disabled) handleScriptRowCommand(String(menuCommand.dataset.scriptRowCommand || ''));
            return;
        }

        const scriptEditor = event.target.closest('[data-native-script-editor]');
        if (scriptEditor) {
            event.preventDefault();
            closeScriptRowActionMenu();
            launchNativeScriptEditor(scriptEditor.dataset.rowId, scriptEditor.dataset.scriptName || scriptEditor.textContent || '', null, true, scriptEditor.dataset.scriptBuiltIn === 'true');
            return;
        }

        const taskEditor = event.target.closest('[data-script-task-editor]');
        if (taskEditor) {
            event.preventDefault();
            launchTaskEditor(taskEditor.dataset.taskId, taskEditor.dataset.taskName || taskEditor.textContent || '');
            return;
        }

        if (scriptRowActionMenu && !event.target.closest('[data-script-row-action-menu]')) closeScriptRowActionMenu();
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') closeScriptRowActionMenu();
    });
    window.addEventListener('resize', closeScriptRowActionMenu);
    window.addEventListener('scroll', closeScriptRowActionMenu, true);

    newScriptForm?.addEventListener('submit', event => { event.preventDefault(); createNewScript(); });
    newScriptClose?.addEventListener('click', () => { if (!newScriptBusy) newScriptDialog?.close(); });
    newScriptCancel?.addEventListener('click', () => { if (!newScriptBusy) newScriptDialog?.close(); });
    newScriptDialog?.addEventListener('cancel', event => { if (newScriptBusy) event.preventDefault(); });
    newScriptDialog?.addEventListener('close', resetNewScriptDialog);

    detailsTabs.forEach(tab => tab.addEventListener('click', () => setScriptDetailsTab(tab.dataset.scriptDetailsTab || 'information')));
    detailsSave?.addEventListener('click', saveScriptDetails);
    detailsCancel?.addEventListener('click', () => detailsDialog?.close());
    detailsDialog?.addEventListener('close', () => { activeScriptId = ''; });

    undoScriptYes?.addEventListener('click', confirmUndoScript);
    undoScriptNoButtons.forEach(button => button.addEventListener('click', cancelUndoScript));
    undoScriptDialog?.addEventListener('cancel', event => {
        if (undoScriptBusy) event.preventDefault();
        else cancelUndoScript();
    });

    deleteScriptYes?.addEventListener('click', confirmDeleteScript);
    deleteScriptNoButtons.forEach(button => button.addEventListener('click', cancelDeleteScript));
    deleteScriptDialog?.addEventListener('cancel', event => {
        if (deleteScriptBusy) event.preventDefault();
        else cancelDeleteScript();
    });

    scriptRefreshController.loadInitial();
    window.MauiModalUI?.registerContext('script-information', detailsDialog, {
        capture: () => ({ id: activeScriptId, url: activeInfoUrl, tab: detailsTabs.find(tab => tab.classList.contains('active'))?.dataset.scriptDetailsTab }),
        restore: async state => { const url = window.MauiModalUI.safeUrl(state.url); if (url) { await openScriptDetails(state.id, url); setScriptDetailsTab(state.tab || 'information'); } }
    });
})();
