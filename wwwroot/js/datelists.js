/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Date List workspace behavior.
 */

(() => {
    'use strict';

    const config = document.querySelector('[data-date-list-page-config]');
    if (!config) return;

    const antiForgeryToken = document.querySelector('.task-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const grid = document.querySelector('[data-grid="datelists"]');
    const toast = grid?.querySelector('[data-prototype-toast]');
    const portability = window.MauiResourcePortability?.create({
        key: 'datelist', label: 'Date List', pluralLabel: 'Date Lists',
        exportUrl: String(config.dataset.exportDateListUrl || ''), exportIdParameter: 'dateListId',
        bulkExportUrl: String(config.dataset.exportDateListsUrl || ''), bulkRequestProperty: 'dateListIds',
        preflightUrl: String(config.dataset.preflightImportDateListUrl || ''), importUrl: String(config.dataset.importDateListUrl || ''),
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
        const returnUrl = window.MauiGridNavigationState?.createReturnUrl?.('datelists')
            || `${window.location.pathname}${window.location.search}${window.location.hash}`;
        url.searchParams.set('returnUrl', returnUrl);
        url.searchParams.set('returnLabel', 'Date Lists');
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

    // Purpose: Calls a Date List endpoint and normalizes JSON/error handling.
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

    // Opens a Date List through the configured editor with read-only access and separate saved history.
    async function launchNativeDateListEditor(dateListId, dateListName, popup = null, markEdited = true) {
        if (window.MauiEditorRouting?.useMauiDateListEditor) {
            closeReservedPopup(popup);
            return window.MauiEditorRouting.openDateList(dateListId,dateListName);
        }
        const normalizedDateListId = String(dateListId || '').trim();
        let normalizedDateListName = String(dateListName || '').trim();
        if (!normalizedDateListId) return false;
        let permissionReadOnly=false;
        try { permissionReadOnly=window.MauiObjectAccess ? !(await window.MauiObjectAccess.read('DateList',normalizedDateListId)).canEdit : false; }
        catch(error) { showStatus(error.message || 'The object is unavailable.',5000,'error'); closeReservedPopup(popup); return false; }

        if (!window.MauiNativeEditor?.open || !window.MauiNativeEditor?.reserve) {
            showStatus('The native MOVEit editor launcher is unavailable.', 5000, 'error');
            closeReservedPopup(popup);
            return false;
        }

        const reservedPopup = popup || window.MauiNativeEditor.reserve(`MAUI Date List Editor - ${normalizedDateListId}`);
        if (!reservedPopup || reservedPopup.closed) {
            showStatus('The MOVEit native editor popup was blocked by the browser.', 5000, 'error');
            return false;
        }

        if (!permissionReadOnly) {
            try {
                const prepared = await fetchJson(endpoint('prepareDateListEditUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ dateListId: normalizedDateListId })
                });
                normalizedDateListName = String(prepared?.dateListName || normalizedDateListName || 'DateList').trim();
                setDateListUndoAvailable(normalizedDateListId, true);
            } catch (error) {
                closeReservedPopup(reservedPopup);
                showStatus(`Date List Editor: ${error?.message || 'Unable to save the Date List undo snapshot.'}`, 5000, 'error');
                return false;
            }
        }

        const opened = await window.MauiNativeEditor.open({
            popup: reservedPopup,
            sessionUrl: endpoint('nativeEditorSessionUrl'),
            loginUrl: endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'),
            antiForgeryToken,
            returnHash: `#/resources/datelists/${encodeURIComponent(normalizedDateListId)}?HideNav`,
            windowTitle: `MAUI Date List Editor - ${normalizedDateListId}: ${normalizedDateListName || 'DateList'}`,
            skin: 'maui', disableUndo:permissionReadOnly,
            status: showStatus,
        });

        if (opened && markEdited && !permissionReadOnly) {
            try {
                await fetchJson(endpoint('markDateListEditedUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ dateListId: normalizedDateListId })
                });
            } catch (error) {
                console.warn('MAUI could not update the Date List modification audit information.');
            }
        }
        return opened;
    }

    // Purpose: Sets date list undo available.
    function setDateListUndoAvailable(dateListId, available) {
        const normalizedDateListId = String(dateListId || '').trim();
        if (!normalizedDateListId) return;
        document.querySelectorAll('[data-date-list-row-menu-button]').forEach(button => {
            if (String(button.dataset.rowId || '').trim() === normalizedDateListId) {
                button.dataset.dateListHasUndo = available ? 'true' : 'false';
            }
        });
    }

    // Opens a Task shown on the Date List Information dependency tab using the configured editor preference.
    async function launchTaskEditor(taskId, taskName) {
        try { return await window.MauiEditorRouting.openTask(taskId, taskName || ''); }
        catch (error) { showStatus(error?.message || 'Unable to open the Task Editor.', 5000, 'error'); return false; }
    }
    // ---------------------------------------------------------------------
    // New Date List
    // ---------------------------------------------------------------------
    const newDateListDialog = document.querySelector('[data-new-date-list-dialog]');
    const newDateListForm = newDateListDialog?.querySelector('[data-new-date-list-form]');
    const newDateListName = newDateListDialog?.querySelector('[data-new-date-list-name]');
    const newDateListSubmit = newDateListDialog?.querySelector('[data-new-date-list-submit]');
    const newDateListCancel = newDateListDialog?.querySelector('[data-new-date-list-cancel]');
    const newDateListClose = newDateListDialog?.querySelector('[data-new-date-list-close]');
    const newDateListError = newDateListDialog?.querySelector('[data-new-date-list-error]');
    const newDateListSpinner = newDateListDialog?.querySelector('[data-new-date-list-spinner]');
    const newDateListSubmitLabel = newDateListDialog?.querySelector('[data-new-date-list-submit-label]');
    let newDateListBusy = false, newDateListUncertain = false;
    const updateNewDateListAction = window.MauiModalUI.watchAction(newDateListDialog, newDateListSubmit,
        () => !newDateListBusy && !newDateListUncertain,
        () => Boolean(newDateListName?.value.trim()) && newDateListName.value.trim().length <= 255 && window.MauiModalUI.formValid(newDateListDialog));

    // Purpose: Opens new date list dialog.
    function openNewDateListDialog() {
        if (window.MauiEditorRouting?.useMauiDateListEditor) { void window.MauiDateListEditor.open(); return; }
        if (!newDateListDialog) return;
        resetNewDateListDialog();
        newDateListDialog.showModal();
        window.requestAnimationFrame(() => newDateListName?.focus());
    }

    // Purpose: Resets new date list dialog.
    function resetNewDateListDialog() {
        setNewDateListBusy(false);
        hideNewDateListError();
        newDateListUncertain = false; updateNewDateListAction();
        if (newDateListName) newDateListName.value = '';
        const description = newDateListDialog?.querySelector('[data-new-date-list-description]');
        if (description) description.value = '';
    }

    // Purpose: Sets new date list busy.
    function setNewDateListBusy(busy) {
        newDateListBusy = busy;
        if (newDateListCancel) newDateListCancel.disabled = busy;
        if (newDateListClose) newDateListClose.disabled = busy;
        if (newDateListName) newDateListName.disabled = busy;
        const description = newDateListDialog?.querySelector('[data-new-date-list-description]'); if (description) description.disabled = busy;
        if (newDateListSpinner) newDateListSpinner.hidden = !busy;
        if (newDateListSubmitLabel) newDateListSubmitLabel.textContent = busy ? 'Creating…' : 'Create Date List';
        updateNewDateListAction();
    }

    // Purpose: Displays new date list error.
    function showNewDateListError(message) {
        if (!newDateListError) return;
        newDateListError.textContent = message || 'Unable to create the Date List.';
        newDateListError.hidden = false;
    }

    // Purpose: Clears new date list error.
    function hideNewDateListError() {
        if (!newDateListError) return;
        newDateListError.textContent = '';
        newDateListError.hidden = true;
    }

    // Purpose: Creates a Date List and returns to the calling grid.
    async function createNewDateList() {
        if (newDateListBusy || newDateListUncertain) return;
        const name = String(newDateListName?.value || '').trim();
        const description = String(newDateListDialog?.querySelector('[data-new-date-list-description]')?.value || '').trim();
        if (!name) {
            showNewDateListError('Date List Name is required.');
            newDateListName?.focus();
            return;
        }
        hideNewDateListError();
        setNewDateListBusy(true);
        try {
            const data = await fetchJson(endpoint('createDateListUrl'), {
                method: 'POST',
                body: JSON.stringify({ name, description })
            });
            const dateListId = String(data?.dateList?.id || '').trim();
            const dateListName = String(data?.dateList?.name || name).trim();
            if (!dateListId) throw new Error('MOVEit created the Date List but did not return its ID.');

            newDateListDialog?.close();
            showStatus(`Created Date List ${dateListName}.`, 5000);
            await refreshDateListGrid(true);
        } catch (error) {
            newDateListUncertain = Boolean(error.payload?.moveItSaved || !error.status || error.status >= 500);
            showNewDateListError(error?.message || 'Unable to create the Date List.');
        } finally {
            setNewDateListBusy(false);
        }
    }

    // ---------------------------------------------------------------------
    // Contextual row menu
    // ---------------------------------------------------------------------
    const dateListRowActionMenu = document.querySelector('[data-date-list-row-action-menu]');
    const dateListRowUndoCommand = dateListRowActionMenu?.querySelector('[data-date-list-row-command="undo"]');
    const dateListRowDeleteCommand = dateListRowActionMenu?.querySelector('[data-date-list-row-command="delete"]');
    const dateListUndoDivider = dateListRowActionMenu?.querySelector('[data-date-list-undo-divider]');
    const dateListDeleteDivider = dateListRowActionMenu?.querySelector('[data-date-list-delete-divider]');
    let activeDateListMenuContext = null;

    // Purpose: Opens date list row action menu.
    function openDateListRowActionMenu(button) {
        if (!dateListRowActionMenu || !button) return;
        if (activeDateListMenuContext?.button === button && !dateListRowActionMenu.hidden) {
            closeDateListRowActionMenu();
            return;
        }

        closeDateListRowActionMenu();
        window.MauiObjectAccess?.reset(dateListRowActionMenu);
        activeDateListMenuContext = {
            button,
            id: String(button.dataset.rowId || '').trim(),
            name: String(button.dataset.dateListName || '').trim(),
            infoUrl: String(button.dataset.dateListInfoUrl || '').trim(),
            hasUndo: button.dataset.dateListHasUndo === 'true',
            canEditDelete: button.dataset.dateListCanEditDelete !== 'false'
        };
        if (dateListRowUndoCommand) {
            dateListRowUndoCommand.hidden = false;
            dateListRowUndoCommand.disabled = !activeDateListMenuContext.hasUndo;
            dateListRowUndoCommand.title = activeDateListMenuContext.hasUndo ? '' : 'Undo is not available until MAUI has saved a prior Date List configuration.';
        }
        if (dateListUndoDivider) dateListUndoDivider.hidden = false;
        if (dateListRowDeleteCommand) {
            // Built-in datelists are read-only. For custom datelists, always allow the user to request Delete;
            // the live server-side preflight is authoritative for permissions and dependencies.
            dateListRowDeleteCommand.hidden = false;
            dateListRowDeleteCommand.disabled = false;
            dateListRowDeleteCommand.title = '';
        }
        if (dateListDeleteDivider) dateListDeleteDivider.hidden = false;
        button.setAttribute('aria-expanded', 'true');
        void window.MauiObjectAccess?.menu(dateListRowActionMenu,'DateList',activeDateListMenuContext.id);
        dateListRowActionMenu.hidden = false;
        dateListRowActionMenu.style.visibility = 'hidden';

        const rect = button.getBoundingClientRect();
        const menuRect = dateListRowActionMenu.getBoundingClientRect();
        let left = rect.left;
        let top = rect.bottom + 4;
        if (left + menuRect.width > window.innerWidth - 8) left = Math.max(8, window.innerWidth - menuRect.width - 8);
        if (top + menuRect.height > window.innerHeight - 8) top = Math.max(8, rect.top - menuRect.height - 4);
        dateListRowActionMenu.style.left = `${Math.round(left)}px`;
        dateListRowActionMenu.style.top = `${Math.round(top)}px`;
        dateListRowActionMenu.style.visibility = '';
    }

    // Purpose: Closes date list row action menu.
    function closeDateListRowActionMenu() {
        activeDateListMenuContext?.button?.setAttribute('aria-expanded', 'false');
        activeDateListMenuContext = null;
        if (!dateListRowActionMenu) return;
        dateListRowActionMenu.hidden = true;
        dateListRowActionMenu.style.left = '';
        dateListRowActionMenu.style.top = '';
        dateListRowActionMenu.style.visibility = '';
    }

    // Purpose: Dispatches one command from the per-row Date List actions menu.
    function handleDateListRowCommand(command) {
        const context = activeDateListMenuContext;
        closeDateListRowActionMenu();
        if (!context) return;
        if (command === 'information') { openDateListDetails(context.id, context.infoUrl); return; }
        if (command === 'audit') { navigateToReport(endpoint('auditReportUrl'), { targetId: context.id, targetName: context.name }); return; }
        if (command === 'export') { portability?.exportItem(context.id, context.name); return; }
        if (command === 'edit') { launchNativeDateListEditor(context.id, context.name); return; }
        if (command === 'undo') { beginUndoDateList(context.id, context.name); return; }
        if (command === 'delete') beginDeleteDateLists([context.id], context.name);
    }

    // ---------------------------------------------------------------------
    // Undo Changes
    // ---------------------------------------------------------------------
    const undoDateListDialog = document.querySelector('[data-undo-date-list-dialog]');
    const undoDateListMessage = undoDateListDialog?.querySelector('[data-undo-date-list-message]');
    const undoDateListHistory = undoDateListDialog?.querySelector('[data-undo-date-list-history]');
    const undoDateListError = undoDateListDialog?.querySelector('[data-undo-date-list-error]');
    const undoDateListYes = undoDateListDialog?.querySelector('[data-undo-date-list-yes]');
    const undoDateListNoButtons = Array.from(undoDateListDialog?.querySelectorAll('[data-undo-date-list-no]') || []);
    let pendingUndoDateList = null;
    let undoDateListBusy = false;

    // Purpose: Formats undo snapshot date.
    function formatUndoSnapshotDate(value) {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? String(value || '') : date.toLocaleString();
    }

    // Purpose: Loads Date List undo history and opens the restore dialog.
    async function beginUndoDateList(dateListId, dateListName) {
        const id = String(dateListId || '').trim();
        if (!id || undoDateListBusy) return;
        undoDateListBusy = true;
        try {
            const preflight = await fetchJson(endpoint('preflightUndoDateListUrl'), {
                method: 'POST',
                body: JSON.stringify({ dateListId: id })
            });

            const snapshots = Array.isArray(preflight?.snapshots) ? preflight.snapshots : [];
            if (!preflight?.success || snapshots.length === 0) {
                showStatus(preflight?.message || 'Undo is not available for this Date List.', 5000);
                return;
            }

            const name = String(preflight?.dateListName || dateListName || id).trim();
            if (undoDateListHistory) {
                undoDateListHistory.replaceChildren();
                snapshots.forEach((snapshot, index) => {
                    const canRestore = snapshot?.canRestore !== false;
                    const row = document.createElement('label');
                    row.className = `undo-history-option${canRestore ? '' : ' is-disabled'}`;
                    const radio = document.createElement('input');
                    radio.type = 'radio'; radio.name = 'datelistUndoSnapshot'; radio.value = String(snapshot?.snapshotId || ''); radio.disabled = !canRestore;
                    const body = document.createElement('span'); body.className = 'undo-history-option-main';
                    const date = document.createElement('span'); date.className = 'undo-history-option-date'; date.textContent = formatUndoSnapshotDate(snapshot?.snapshotCreatedUtc);
                    const meta = document.createElement('span'); meta.className = 'undo-history-option-meta'; meta.textContent = `${index === 0 ? 'Most recent' : `Version ${index + 1}`} · Saved by ${String(snapshot?.snapshotCreatedBy || 'current user')}`;
                    body.append(date, meta);
                    const warningText = String(snapshot?.message || '').trim();
                    if (warningText) { const warning = document.createElement('span'); warning.className = 'undo-history-option-warning'; warning.textContent = warningText; body.append(warning); }
                    row.append(radio, body); undoDateListHistory.append(row);
                });
            }
            const first = undoDateListHistory?.querySelector('input[type="radio"]:not(:disabled)');
            if (first) first.checked = true;
            const snapshotId = first?.value || '';
            pendingUndoDateList = { dateListId: id, dateListName: name, snapshotId };
            undoDateListHistory?.querySelectorAll('input[type="radio"]').forEach(radio => radio.addEventListener('change', () => { pendingUndoDateList.snapshotId = radio.value; if (undoDateListYes) undoDateListYes.disabled = false; }));
            if (undoDateListYes) undoDateListYes.disabled = !snapshotId;
            if (undoDateListMessage) undoDateListMessage.textContent = `Select the version of Date List "${name}" to restore.`;
            if (undoDateListError) { undoDateListError.hidden = true; undoDateListError.textContent = ''; }
            undoDateListDialog?.showModal();
            window.requestAnimationFrame(() => undoDateListNoButtons.find(button => button.textContent.trim() === 'Cancel')?.focus());
        } catch (error) {
            showStatus(`Undo Changes: ${error?.message || 'Unable to validate the Date List undo snapshot.'}`, 5000, 'error');
        } finally {
            undoDateListBusy = false;
        }
    }

    // Purpose: Restores the selected Date List undo snapshot.
    async function confirmUndoDateList() {
        if (undoDateListBusy || !pendingUndoDateList?.dateListId || !pendingUndoDateList?.snapshotId) return;
        if (!window.MauiEditorRouting?.useMauiDateListEditor && !window.MauiNativeEditor?.reserve) {
            if (undoDateListError) {
                undoDateListError.textContent = 'The MAUI Date List Editor launcher is unavailable. Reload MAUI and try again.';
                undoDateListError.hidden = false;
            }
            return;
        }

        const popup = window.MauiEditorRouting?.useMauiDateListEditor ? null : window.MauiNativeEditor.reserve(`MAUI Date List Editor - ${pendingUndoDateList.dateListId}`);
        if (!window.MauiEditorRouting?.useMauiDateListEditor && !popup) {
            if (undoDateListError) {
                undoDateListError.textContent = 'The MOVEit native editor popup was blocked by the browser.';
                undoDateListError.hidden = false;
            }
            return;
        }

        undoDateListBusy = true;
        if (undoDateListYes) undoDateListYes.disabled = true;
        undoDateListNoButtons.forEach(button => { button.disabled = true; });
        try {
            const data = await fetchJson(endpoint('undoDateListUrl'), {
                method: 'POST',
                body: JSON.stringify({ dateListId: pendingUndoDateList.dateListId, snapshotId: pendingUndoDateList.snapshotId })
            });
            const restoredId = String(data?.dateListId || pendingUndoDateList.dateListId).trim();
            const restoredName = String(data?.dateListName || pendingUndoDateList.dateListName || restoredId).trim();
            const warning = String(data?.warning || '').trim();
            undoDateListDialog?.close();
            pendingUndoDateList = null;
            showStatus(warning ? `Undo Changes: ${warning}` : `Undo Changes: restored Date List ${restoredName}.`, 5000);

            const opened = await launchNativeDateListEditor(restoredId, restoredName, popup);
            if (!opened) closeReservedPopup(popup);
            await refreshDateListGrid(true);
        } catch (error) {
            closeReservedPopup(popup);
            if (undoDateListError) {
                undoDateListError.textContent = error?.message || 'Unable to restore the Date List undo snapshot.';
                undoDateListError.hidden = false;
            }
        } finally {
            undoDateListBusy = false;
            if (undoDateListYes) undoDateListYes.disabled = false;
            undoDateListNoButtons.forEach(button => { button.disabled = false; });
        }
    }

    // Purpose: Cancels undo date list.
    function cancelUndoDateList() {
        if (undoDateListBusy) return;
        pendingUndoDateList = null;
        undoDateListDialog?.close();
    }

    // ---------------------------------------------------------------------
    // Delete Date List / Delete Date Lists
    // ---------------------------------------------------------------------
    const deleteDateListDialog = document.querySelector('[data-delete-date-list-dialog]');
    const deleteDateListTitle = deleteDateListDialog?.querySelector('[data-delete-date-list-title]');
    const deleteDateListMessage = deleteDateListDialog?.querySelector('[data-delete-date-list-message]');
    const deleteDateListError = deleteDateListDialog?.querySelector('[data-delete-date-list-error]');
    const deleteDateListList = deleteDateListDialog?.querySelector('[data-delete-date-list-list]');
    const deleteDateListYes = deleteDateListDialog?.querySelector('[data-delete-date-list-yes]');
    const deleteDateListNoButtons = Array.from(deleteDateListDialog?.querySelectorAll('[data-delete-date-list-no]') || []);
    let pendingDeleteDateListIds = [];
    let deleteDateListsWithoutRecycle = false;
    let deleteDateListBusy = false;

    // Purpose: Normalizes selected date list ids.
    function normalizeSelectedDateListIds(ids) {
        return Array.from(new Set((ids || []).map(id => String(id || '').trim()).filter(Boolean)));
    }

    // Purpose: Formats delete date lists result.
    function formatDeleteDateListsResult(data) {
        const succeeded = Number(data?.succeeded || 0);
        const skipped = Number(data?.skipped || 0);
        const failed = Number(data?.failed || 0);
        const parts = [`Delete DateList: ${succeeded} succeeded`];
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

    // Purpose: Runs delete preflight and opens the Date List confirmation dialog.
    async function beginDeleteDateLists(dateListIds, singleDateListName = '') {
        const ids = normalizeSelectedDateListIds(dateListIds);
        if (!ids.length) {
            showStatus('Select at least one Date List.', 5000);
            return;
        }
        if (deleteDateListBusy || undoDateListBusy || newDateListBusy) {
            showStatus('Another Date List action is already in progress.', 5000);
            return;
        }

        deleteDateListBusy = true;
        try {
            const preflight = await fetchJson(endpoint('preflightDeleteDateListsUrl'), {
                method: 'POST',
                body: JSON.stringify({ dateListIds: ids })
            });

            if (!preflight?.success) {
                const blockers = Array.isArray(preflight?.blockers) ? preflight.blockers : [];
                const details = blockers.slice(0, 3).map(item => {
                    const tasks = Array.isArray(item?.taskReferences) ? item.taskReferences : [];
                    const taskText = tasks.length
                        ? ` Tasks: ${tasks.slice(0, 3).map(task => task.name || task.id).join(', ')}${tasks.length > 3 ? '…' : ''}.`
                        : '';
                    return `${item.dateListName || item.dateListId}: ${item.message || 'Cannot delete.'}${taskText}`;
                });
                showStatus([preflight?.message || 'The selected Date Lists cannot be deleted.', ...details].filter(Boolean).join(' | '), 5000);
                return;
            }

            const targets = Array.isArray(preflight?.items)
                ? preflight.items.map(item => ({
                    dateListId: String(item?.id || '').trim(),
                    dateListName: String(item?.name || item?.id || '').trim()
                })).filter(item => item.dateListId)
                : ids.map(id => ({ dateListId: id, dateListName: id }));

            pendingDeleteDateListIds = ids;
            deleteDateListsWithoutRecycle = false;
            if (deleteDateListYes) deleteDateListYes.textContent = 'Yes, Delete';
            if (deleteDateListTitle) deleteDateListTitle.textContent = ids.length === 1 ? 'Delete Date List' : 'Delete Date Lists';
            if (deleteDateListMessage) {
                deleteDateListMessage.textContent = ids.length === 1
                    ? 'The following Date List will be deleted from MOVEit Automation:'
                    : `The following ${ids.length} Date Lists will be deleted from MOVEit Automation:`;
            }
            if (deleteDateListList) {
                deleteDateListList.replaceChildren();
                targets.forEach(item => {
                    const row = document.createElement('div');
                    row.className = 'delete-object-list-item';
                    row.textContent = item.dateListName || item.dateListId;
                    deleteDateListList.appendChild(row);
                });
                deleteDateListList.hidden = targets.length === 0;
            }
            if (deleteDateListError) {
                deleteDateListError.hidden = true;
                deleteDateListError.textContent = '';
            }

            deleteDateListDialog?.showModal();
            window.requestAnimationFrame(() => deleteDateListNoButtons.find(button => button.textContent.trim() === 'Cancel')?.focus());
        } catch (error) {
            showStatus(`Delete DateList: ${error?.message || 'Dependency check failed.'}`, 5000, 'error');
        } finally {
            deleteDateListBusy = false;
        }
    }

    // Purpose: Deletes the confirmed Date List selection with Recycle Bin safeguards.
    async function confirmDeleteDateList() {
        if (deleteDateListBusy || !pendingDeleteDateListIds.length) return;
        deleteDateListBusy = true;
        if (deleteDateListYes) deleteDateListYes.disabled = true;
        deleteDateListNoButtons.forEach(button => { button.disabled = true; });

        try {
            const data = await fetchJson(endpoint('deleteDateListsUrl'), {
                method: 'POST',
                body: JSON.stringify({ dateListIds: pendingDeleteDateListIds, deleteWithoutRecycle: deleteDateListsWithoutRecycle })
            });

            deleteDateListDialog?.close();
            const deletedDateListIds = Array.isArray(data?.items)
                ? data.items.filter(item => item?.success === true).map(item => item?.id).filter(Boolean)
                : [];
            grid?.dispatchEvent(new CustomEvent('maui:grid-remove-rows', { detail: { rowIds: deletedDateListIds } }));
            pendingDeleteDateListIds = [];
            deleteDateListsWithoutRecycle = false;
            if (deleteDateListYes) deleteDateListYes.textContent = 'Yes, Delete';
            const warning = String(data?.warning || '').trim();
            const summary = formatDeleteDateListsResult(data);
            showStatus([summary, warning].filter(Boolean).join(' '), 5000);
            await refreshDateListGrid(true);
        } catch (error) {
            if (error?.status === 409 && error?.payload?.code === 'RecycleCaptureFailed') {
                deleteDateListsWithoutRecycle = true;
                if (deleteDateListError) { deleteDateListError.textContent = error?.message || 'MAUI could not create a recovery copy. Delete anyway?'; deleteDateListError.hidden = false; }
                if (deleteDateListYes) deleteDateListYes.textContent = 'Delete Anyway';
            } else if (deleteDateListError) {
                deleteDateListError.textContent = error?.message || 'Unable to delete the selected Date Lists.';
                deleteDateListError.hidden = false;
            }
        } finally {
            deleteDateListBusy = false;
            if (deleteDateListYes) deleteDateListYes.disabled = false;
            deleteDateListNoButtons.forEach(button => { button.disabled = false; });
        }
    }

    // Purpose: Cancels delete date list.
    function cancelDeleteDateList() {
        if (deleteDateListBusy) return;
        pendingDeleteDateListIds = [];
        deleteDateListsWithoutRecycle = false;
        if (deleteDateListYes) deleteDateListYes.textContent = 'Yes, Delete';
        deleteDateListDialog?.close();
    }

    // ---------------------------------------------------------------------
    // Date List Information
    // ---------------------------------------------------------------------
    const detailsDialog = document.querySelector('[data-date-list-details-dialog]');
    const detailsLoading = detailsDialog?.querySelector('[data-date-list-details-loading]');
    const detailsContent = detailsDialog?.querySelector('[data-date-list-details-content]');
    const detailsError = detailsDialog?.querySelector('[data-date-list-details-error]');
    const detailsState = window.mauiCreateInformationDialogState({
        loadingElement: detailsLoading,
        contentElement: detailsContent,
        errorElement: detailsError,
        defaultErrorMessage: 'Unable to load Date List information.'
    });
    const detailsSave = detailsDialog?.querySelector('[data-date-list-details-save]');
    const detailsCancel = detailsDialog?.querySelector('[data-date-list-details-cancel]');
    const detailsTabs = Array.from(detailsDialog?.querySelectorAll('[data-date-list-details-tab]') || []);
    const informationPanel = detailsDialog?.querySelector('[data-date-list-details-panel="information"]');
    const tasksPanel = detailsDialog?.querySelector('[data-date-list-details-panel="tasks"]');
    const metadataFields = detailsDialog?.querySelector('[data-date-list-metadata-fields]');
    const metadataAudit = detailsDialog?.querySelector('[data-date-list-metadata-audit]');
    const dateListTasksRows = detailsDialog?.querySelector('[data-date-list-tasks-rows]');
    const dateListTasksList = detailsDialog?.querySelector('[data-date-list-tasks-list]');
    const dateListTasksEmpty = detailsDialog?.querySelector('[data-date-list-tasks-empty]');
    let activeDateListId = '';
    let activeInfoUrl = '';
    const detailsReadiness = window.MauiModalUI.registerSaveAction(detailsDialog, detailsSave, () => {
        const name = detailsDialog.querySelector('[data-date-list-name]');
        return !name || name.disabled || name.readOnly || name.value.trim().length > 0 && name.value.trim().length <= 255;
    });


    // Purpose: Loads and displays Date List information and metadata.
    async function openDateListDetails(dateListId, infoUrl) {
        if (!detailsDialog || !dateListId || !infoUrl) return;
        activeDateListId = String(dateListId).trim();
        activeInfoUrl = infoUrl;
        resetDateListDetailsDialog();
        detailsDialog.showModal();
        try {
            const data = await fetchJson(infoUrl, { method: 'GET', cache: 'no-store' });
            populateDateListDetails(data);
            await window.MauiObjectAccess?.information(detailsDialog,'DateList',activeDateListId);
            window.MauiModalUI?.track(detailsDialog);
        } catch (error) {
            showDetailsError(error.message);
        }
    }

    // Purpose: Resets date list details dialog.
    function resetDateListDetailsDialog() {
        window.MauiObjectAccess?.clearInformation(detailsDialog);
        if (!detailsDialog) return;
        detailsReadiness.reset();
        detailsState.reset();
        detailsSave.disabled = true;
        metadataFields?.replaceChildren();
        metadataAudit?.replaceChildren();
        dateListTasksRows?.replaceChildren();
        if (dateListTasksEmpty) dateListTasksEmpty.hidden = true;
        if (dateListTasksList) dateListTasksList.hidden = true;
        setDateListDetailsTab('information');
    }

    // Purpose: Populates date list details.
    function populateDateListDetails(data) {
        detailsDialog.dataset.editVersion = String(data.editVersion || '');
        const dateList = data.dateList || {};
        detailsDialog.querySelector('[data-date-list-id]').textContent = dateList.id || activeDateListId;
        const entryCount = detailsDialog.querySelector('[data-date-list-entry-count]'); if (entryCount) entryCount.value = String(dateList.entryCount ?? 0);
        detailsDialog.querySelector('[data-date-list-name]').value = dateList.name || '';
        detailsDialog.querySelector('[data-date-list-description]').value = dateList.description || '';
        const resourceGroupsInput = detailsDialog.querySelector('[data-date-list-resource-groups]');
        if (resourceGroupsInput) resourceGroupsInput.value = dateList.resourceGroups || '—';
        populateMetadataFields(data.fields || [], data.metadata || {});
        populateAudit(data.audit || {});
        populateDateListTasks(data.tasks || []);
        detailsState.showContent();
        detailsSave.disabled = false;
    }

    // Purpose: Sets date list details tab.
    function setDateListDetailsTab(tabName) {
        const showTasks = tabName === 'tasks';
        detailsTabs.forEach(tab => {
            const selected = tab.dataset.dateListDetailsTab === tabName;
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
            control.dataset.dateListMetadataField = field.name;
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

    // Purpose: Populates date list tasks.
    function populateDateListTasks(tasks) {
        dateListTasksRows?.replaceChildren();
        if (!Array.isArray(tasks) || tasks.length === 0) {
            if (dateListTasksEmpty) dateListTasksEmpty.hidden = false;
            if (dateListTasksList) dateListTasksList.hidden = true;
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
            link.dataset.dateListTaskEditor = 'true';
            link.dataset.taskId = task.id || '';
            link.dataset.taskName = task.name || '';
            link.textContent = task.name || task.id || 'Task';
            name.appendChild(link);
            row.append(id, name);
            fragment.appendChild(row);
        });
        dateListTasksRows?.replaceChildren(fragment);
        if (dateListTasksEmpty) dateListTasksEmpty.hidden = true;
        if (dateListTasksList) dateListTasksList.hidden = false;
    }

    // Purpose: Saves editable Date List details and MAUI metadata.
    async function saveDateListDetails() {
        if (!activeDateListId || !metadataFields || detailsSave.disabled) return;
        hideDetailsError();
        if (window.mauiValidateMetadataControls && !window.mauiValidateMetadataControls(metadataFields)) return;
        detailsReadiness.busy(true);
        const metadata = {};
        metadataFields.querySelectorAll('[data-date-list-metadata-field]').forEach(control => {
            metadata[control.dataset.dateListMetadataField] = control.value ?? '';
        });

        try {
            await fetchJson(`${endpoint('saveDateListDetailsUrl')}?id=${encodeURIComponent(activeDateListId)}`, {
                method: 'POST',
                body: JSON.stringify({ editVersion: detailsDialog.dataset.editVersion || '', name: detailsDialog.querySelector('[data-date-list-name]')?.value || '', description: detailsDialog.querySelector('[data-date-list-description]')?.value || '', metadata })
            });
            detailsDialog.close();
            showStatus('Date List information saved.', 5000);
            await refreshDateListGrid(true);
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
    // Live Date List grid refresh
    // ---------------------------------------------------------------------
    const refreshControl = document.querySelector('[data-date-list-refresh-control]');
    const uiDateListRefreshSeconds = Math.max(0, Number(config.dataset.uiDateListRefreshSeconds || 0));
    const dateListRefreshController = window.mauiCreateResourceRefreshController({
        grid,
        refreshControl,
        lastRefreshElement: refreshControl?.querySelector('[data-date-list-last-refresh]'),
        refreshClock: refreshControl?.querySelector('[data-date-list-refresh-clock]'),
        refreshToggle: refreshControl?.querySelector('[data-date-list-refresh-toggle]'),
        refreshNow: refreshControl?.querySelector('[data-date-list-refresh-now]'),
        intervalSeconds: uiDateListRefreshSeconds,
        fetchJson,
        buildInitialUrl: () => {
            const url = new URL(endpoint('initialDateListGridUrl'), window.location.href);
            url.searchParams.set('preferCached', 'true');
            return url;
        },
        buildRefreshUrl: forceRefresh => {
            const url = new URL(endpoint('refreshDateListGridUrl'), window.location.href);
            url.searchParams.set('forceRefresh', forceRefresh ? 'true' : 'false');
            return url;
        },
        beforeRefresh: closeDateListRowActionMenu,
        onInitialError: error => {
            console.warn('Initial Date List grid load failed.');
            showStatus(`Date List load failed: ${error.message}`, 5000, 'error', 'page');
        },
        onRefreshError: error => {
            console.warn('Date List grid refresh failed.');
            showStatus(`Date List refresh failed: ${error.message}`, 5000, 'error', 'page');
        },
        pausedTitle: 'Date List refresh paused',
        activeTitle: 'Date List refresh options',
        persistLastRefresh: true,
        setAriaBusy: true
    });

    function refreshDateListGrid(forceRefresh) {
        return dateListRefreshController.refresh(forceRefresh);
    }

    // ---------------------------------------------------------------------
    // Event wiring
    // ---------------------------------------------------------------------
    document.addEventListener('maui:grid-action', event => {
        const gridId = String(event.detail?.gridId || '').trim().toLowerCase();
        const actionName = String(event.detail?.action || '').trim().toLowerCase();
        if (gridId !== 'datelists') return;
        if (actionName === 'new date list') {
            event.preventDefault();
            openNewDateListDialog();
            return;
        }
        if (actionName === 'import date lists' || actionName === 'import date list') {
            event.preventDefault();
            portability?.openImport();
            return;
        }
        if (actionName === 'export date lists' || actionName === 'export selected date lists') {
            event.preventDefault();
            portability?.exportSelected(event.detail?.selectedIds || []);
            return;
        }
        if (actionName === 'delete date lists') {
            event.preventDefault();
            beginDeleteDateLists(event.detail?.selectedIds || []);
        }
    });

    window.addEventListener('maui:date-list-editor-closed',event => { if (event.detail?.changed) void refreshDateListGrid(true); });

    document.addEventListener('maui:resource-imported', async event => {
        if (event.detail?.key !== 'datelist') return;
        await refreshDateListGrid(true);
        const id=String(event.detail?.id||'').trim(), name=String(event.detail?.name||'').trim();
        if (id) launchNativeDateListEditor(id,name,event.detail?.popup||null,true);
    });

    document.addEventListener('click', event => {
        const menuButton = event.target.closest('[data-date-list-row-menu-button]');
        if (menuButton) {
            event.preventDefault();
            event.stopPropagation();
            openDateListRowActionMenu(menuButton);
            return;
        }

        const menuCommand = event.target.closest('[data-date-list-row-command]');
        if (menuCommand && dateListRowActionMenu?.contains(menuCommand)) {
            event.preventDefault();
            if (!menuCommand.disabled) handleDateListRowCommand(String(menuCommand.dataset.dateListRowCommand || ''));
            return;
        }

        const dateListEditor = event.target.closest('[data-native-date-list-editor]');
        if (dateListEditor) {
            event.preventDefault();
            closeDateListRowActionMenu();
            launchNativeDateListEditor(dateListEditor.dataset.rowId, dateListEditor.dataset.dateListName || dateListEditor.textContent || '', null, true);
            return;
        }

        const taskEditor = event.target.closest('[data-date-list-task-editor]');
        if (taskEditor) {
            event.preventDefault();
            launchTaskEditor(taskEditor.dataset.taskId, taskEditor.dataset.taskName || taskEditor.textContent || '');
            return;
        }

        if (dateListRowActionMenu && !event.target.closest('[data-date-list-row-action-menu]')) closeDateListRowActionMenu();
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') closeDateListRowActionMenu();
    });
    window.addEventListener('resize', closeDateListRowActionMenu);
    window.addEventListener('scroll', closeDateListRowActionMenu, true);

    newDateListForm?.addEventListener('submit', event => { event.preventDefault(); createNewDateList(); });
    newDateListClose?.addEventListener('click', () => { if (!newDateListBusy) newDateListDialog?.close(); });
    newDateListCancel?.addEventListener('click', () => { if (!newDateListBusy) newDateListDialog?.close(); });
    newDateListDialog?.addEventListener('cancel', event => { if (newDateListBusy) event.preventDefault(); });
    newDateListDialog?.addEventListener('close', resetNewDateListDialog);

    detailsTabs.forEach(tab => tab.addEventListener('click', () => setDateListDetailsTab(tab.dataset.dateListDetailsTab || 'information')));
    detailsSave?.addEventListener('click', saveDateListDetails);
    detailsCancel?.addEventListener('click', () => detailsDialog?.close());
    detailsDialog?.addEventListener('close', () => { activeDateListId = ''; });

    undoDateListYes?.addEventListener('click', confirmUndoDateList);
    undoDateListNoButtons.forEach(button => button.addEventListener('click', cancelUndoDateList));
    undoDateListDialog?.addEventListener('cancel', event => {
        if (undoDateListBusy) event.preventDefault();
        else cancelUndoDateList();
    });

    deleteDateListYes?.addEventListener('click', confirmDeleteDateList);
    deleteDateListNoButtons.forEach(button => button.addEventListener('click', cancelDeleteDateList));
    deleteDateListDialog?.addEventListener('cancel', event => {
        if (deleteDateListBusy) event.preventDefault();
        else cancelDeleteDateList();
    });

    dateListRefreshController.loadInitial();
    window.MauiModalUI?.registerContext('date-list-information', detailsDialog, {
        capture: () => ({ id: activeDateListId, url: activeInfoUrl, tab: detailsTabs.find(tab => tab.classList.contains('active'))?.dataset.dateListDetailsTab }),
        restore: async state => { const url = window.MauiModalUI.safeUrl(state.url); if (url) { await openDateListDetails(state.id, url); setDateListDetailsTab(state.tab || 'information'); } }
    });
})();
