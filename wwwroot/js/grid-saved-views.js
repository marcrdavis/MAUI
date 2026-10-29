/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Shared Saved View dialog and persistence support for MAUI data grids.
 */

(() => {
    // Owns the shared Save/Delete View dialogs so every grid uses one consistent persistence workflow.
    // Individual grids only provide capture/apply callbacks and their stable GridId.
    function createManager() {
        const root = document.documentElement;
        const saveDialog = document.querySelector('[data-saved-view-save-dialog]');
        const saveForm = saveDialog?.querySelector('[data-saved-view-save-form]');
        const saveTitle = saveDialog?.querySelector('[data-saved-view-dialog-title]');
        const saveInstructions = saveDialog?.querySelector('[data-saved-view-dialog-instructions]');
        const nameInput = saveDialog?.querySelector('[data-saved-view-name]');
        const descriptionInput = saveDialog?.querySelector('[data-saved-view-description]');
        const overwriteMessage = saveDialog?.querySelector('[data-saved-view-overwrite-message]');
        const saveError = saveDialog?.querySelector('[data-saved-view-save-error]');
        const saveButton = saveDialog?.querySelector('[data-saved-view-save-button]');
        const saveCancelButtons = Array.from(saveDialog?.querySelectorAll('[data-saved-view-save-cancel]') || []);
        const deleteDialog = document.querySelector('[data-saved-view-delete-dialog]');
        const deleteName = deleteDialog?.querySelector('[data-saved-view-delete-name]');
        const deleteError = deleteDialog?.querySelector('[data-saved-view-delete-error]');
        const deleteConfirm = deleteDialog?.querySelector('[data-saved-view-delete-confirm]');
        const deleteCancelButtons = Array.from(deleteDialog?.querySelectorAll('[data-saved-view-delete-cancel]') || []);
        let activeGrid = null;
        let overwritePending = false;
        let saveMode = 'create';
        let saveBusy = false;
        // Update View may capture grid changes outside this modal; a populated name remains ready.
        const updateSaveAction = window.MauiModalUI.watchAction(saveDialog, saveButton, () => !saveBusy && Boolean(activeGrid),
            () => Boolean(nameInput?.value.trim()) && nameInput.value.trim().length <= 100
                && (descriptionInput?.value.trim().length || 0) <= 500);
        const pendingUpdateToastKey = 'maui-saved-view-update-toast';

        // Purpose: Returns the current MAUI antiforgery token.
        function antiForgeryToken() {
            return document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
        }

        // Purpose: Posts JSON with antiforgery protection and normalized error handling.
        async function postJson(url, payload) {
            const headers = new Headers({ 'Content-Type': 'application/json' });
            const token = antiForgeryToken();
            if (token) headers.set('RequestVerificationToken', token);
            const response = await fetch(url, { method: 'POST', credentials: 'same-origin', headers, body: JSON.stringify(payload) });
            const data = await response.json().catch(() => ({}));
            return { response, data };
        }

        // Purpose: Sets save busy.
        function setSaveBusy(busy) {
            saveBusy = busy;
            if (nameInput) nameInput.disabled = busy;
            if (descriptionInput) descriptionInput.disabled = busy;
            updateSaveAction();
            saveCancelButtons.forEach(button => button.disabled = busy);
        }

        // Purpose: Sets delete busy.
        function setDeleteBusy(busy) {
            if (deleteConfirm) deleteConfirm.disabled = busy;
            deleteCancelButtons.forEach(button => button.disabled = busy);
        }

        // Purpose: Sets save mode.
        function setSaveMode(mode) {
            saveMode = mode === 'update' ? 'update' : 'create';
            const isUpdate = saveMode === 'update';
            if (saveTitle) saveTitle.textContent = isUpdate ? 'Update View' : 'Create View';
            if (saveInstructions) {
                saveInstructions.textContent = isUpdate
                    ? 'Update this saved view using the current grid layout, sorting, column filters, name, and optional description.'
                    : 'Create a reusable view from the current grid layout, sorting, and column filters.';
            }
            if (saveButton) saveButton.textContent = isUpdate ? 'Update' : 'Create View';
        }

        // Purpose: Resets save messages.
        function resetSaveMessages() {
            overwritePending = false;
            if (overwriteMessage) { overwriteMessage.hidden = true; overwriteMessage.textContent = ''; }
            if (saveError) { saveError.hidden = true; saveError.textContent = ''; }
            if (saveButton) delete saveButton.dataset.replaceConfirmed;
            setSaveMode(saveMode);
        }

        // Update View can run directly from the grid; an open Save View dialog owns its errors.
        function showSaveError(message) {
            const text = message || 'The view could not be saved.';
            if (saveError) {
                saveError.textContent = text;
                saveError.hidden = false;
            }
            if (saveMode === 'update' && !saveDialog?.open) window.mauiShowToast?.(text, 'error', 'page');
        }

        // Purpose: Carries the Update View success notification across the required saved-view redirect.
        function queueUpdateSuccessToast(viewId) {
            if (saveMode !== 'update' || !viewId) return;
            try {
                window.sessionStorage.setItem(pendingUpdateToastKey, JSON.stringify({ viewId: String(viewId), message: 'View updated.' }));
            } catch {
                // Storage can be unavailable under restrictive browser policies. The update itself still succeeded.
            }
        }

        // Purpose: Displays delete error.
        function showDeleteError(message) {
            if (!deleteError) return;
            deleteError.textContent = message || 'The saved view could not be deleted.';
            deleteError.hidden = false;
        }

        // Purpose: Opens save.
        function openSave(gridApi, mode = 'create') {
            if (!saveDialog || !nameInput) return;
            activeGrid = gridApi;
            const activeView = gridApi.getActiveView();
            const isUpdate = mode === 'update' && Boolean(activeView?.id);
            setSaveMode(isUpdate ? 'update' : 'create');
            resetSaveMessages();

            // Creating from an active saved view intentionally starts with a new name while
            // captureState() preserves the current view's layout, sorting, and filters.
            nameInput.value = isUpdate ? (activeView?.viewName || '') : '';
            if (descriptionInput) descriptionInput.value = isUpdate ? (activeView?.description || '') : '';
            saveDialog.showModal();
            requestAnimationFrame(() => { nameInput.focus(); nameInput.select(); });
        }

        // Purpose: Opens delete.
        function openDelete(gridApi) {
            const view = gridApi.getActiveView();
            if (!deleteDialog || !view?.id) return;
            activeGrid = gridApi;
            if (deleteName) deleteName.textContent = view.viewName;
            if (deleteError) { deleteError.hidden = true; deleteError.textContent = ''; }
            deleteDialog.showModal();
        }

        nameInput?.addEventListener('input', resetSaveMessages);
        descriptionInput?.addEventListener('input', () => {
            if (saveError) { saveError.hidden = true; saveError.textContent = ''; }
        });
        saveCancelButtons.forEach(button => button.addEventListener('click', () => { if (!button.disabled) saveDialog?.close(); }));
        deleteCancelButtons.forEach(button => button.addEventListener('click', () => { if (!button.disabled) deleteDialog?.close(); }));

        saveForm?.addEventListener('submit', async event => {
            event.preventDefault();
            if (saveBusy || !activeGrid || !nameInput) return;
            if (saveError) { saveError.hidden = true; saveError.textContent = ''; }
            const viewName = nameInput.value.trim();
            if (!viewName) { showSaveError('View Name is required.'); nameInput.focus(); return; }
            if (viewName.length > 100) { showSaveError('View Name cannot exceed 100 characters.'); nameInput.focus(); return; }
            const description = descriptionInput?.value.trim() || '';
            if (description.length > 500) { showSaveError('Description cannot exceed 500 characters.'); descriptionInput?.focus(); return; }

            // If the previous submit discovered a same-grid collision, the next explicit submit is the replace confirmation.
            const shouldOverwrite = saveMode === 'create' && (overwritePending || saveButton?.dataset.replaceConfirmed === 'true');
            if (saveButton) delete saveButton.dataset.replaceConfirmed;
            setSaveBusy(true);
            try {
                const activeView = activeGrid.getActiveView();
                const { response, data } = await postJson(root.dataset.savedViewSaveUrl, {
                    id: saveMode === 'update' ? (activeView?.id || '') : '',
                    gridKey: activeGrid.gridKey,
                    viewName,
                    description,
                    overwrite: shouldOverwrite,
                    state: activeGrid.captureState()
                });
                if (response.status === 409) {
                    if (saveMode === 'create' && data?.canOverwrite) {
                        overwritePending = true;
                        if (overwriteMessage) { overwriteMessage.textContent = data?.message || 'A view with this name already exists. Creating this view will replace it.'; overwriteMessage.hidden = false; }
                        if (saveButton) { saveButton.textContent = 'Replace View'; saveButton.dataset.replaceConfirmed = 'true'; }
                    } else {
                        showSaveError(data?.message || 'A saved view with this name already exists. Choose another name.');
                    }
                    return;
                }
                if (!response.ok) { showSaveError(data?.message || `Request failed (${response.status}).`); return; }
                const url = data?.view?.url;
                if (url) {
                    if (saveMode === 'update') queueUpdateSuccessToast(data?.view?.id);
                    window.location.assign(url);
                    return;
                }
                saveDialog.close();
                window.mauiShowToast?.(saveMode === 'update' ? 'View updated.' : 'View created.');
            } catch (error) {
                showSaveError(error?.message || 'The view could not be saved.');
            } finally {
                setSaveBusy(false);
            }
        });

        deleteConfirm?.addEventListener('click', async () => {
            const view = activeGrid?.getActiveView();
            if (!view?.id) return;
            if (deleteError) { deleteError.hidden = true; deleteError.textContent = ''; }
            setDeleteBusy(true);
            try {
                const { response, data } = await postJson(root.dataset.savedViewDeleteUrl, { id: view.id });
                if (!response.ok) { showDeleteError(data?.message || `Request failed (${response.status}).`); return; }
                const url = new URL(window.location.href);
                url.searchParams.delete('savedView');
                window.location.assign(url.toString());
            } catch (error) {
                showDeleteError(error?.message || 'The saved view could not be deleted.');
            } finally {
                setDeleteBusy(false);
            }
        });

        return { openSave, openDelete };
    }

    window.MauiGridSavedViews = Object.freeze({ createManager });
})();
