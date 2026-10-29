/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * This JavaScript is part of the MAUI application.
 */
(() => {
    'use strict';
    const dialog = document.querySelector('[data-recycle-bin-dialog]');
    const openButton = document.querySelector('[data-recycle-bin-open]');
    if (!dialog || !openButton) return;

    const rows = dialog.querySelector('[data-recycle-bin-rows]');
    const emptyState = dialog.querySelector('[data-recycle-bin-empty]');
    const message = dialog.querySelector('[data-recycle-bin-message]');
    const selectAll = dialog.querySelector('[data-recycle-bin-select-all]');
    const restoreButton = dialog.querySelector('[data-recycle-bin-restore]');
    const emptyButton = dialog.querySelector('[data-recycle-bin-empty-button]');
    const closeButtons = dialog.querySelectorAll('[data-recycle-bin-close]');
    const confirmDialog = document.querySelector('[data-recycle-bin-confirm-dialog]');
    const confirmTitle = confirmDialog?.querySelector('[data-recycle-bin-confirm-title]');
    const confirmMessage = confirmDialog?.querySelector('[data-recycle-bin-confirm-message]');
    const confirmAccept = confirmDialog?.querySelector('[data-recycle-bin-confirm-accept]');
    const confirmCancel = confirmDialog?.querySelectorAll('[data-recycle-bin-confirm-cancel]') || [];
    const token = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    let busy = false;

    // Purpose: Sets the Recycle Bin status/error message.
    function setMessage(text, kind = '') {
        if (!message) return;
        message.textContent = text || '';
        message.hidden = !text;
        message.dataset.kind = kind;
    }

    // Purpose: Returns the currently selected Recycle Bin item IDs.
    function selectedIds() {
        return [...dialog.querySelectorAll('[data-recycle-bin-item]:checked')].map(input => input.value);
    }

    // Purpose: Updates Recycle Bin selection state and action availability.
    function updateSelection() {
        const all = [...dialog.querySelectorAll('[data-recycle-bin-item]')];
        const selected = all.filter(input => input.checked);
        all.forEach(input => { input.disabled = busy; });
        if (restoreButton) restoreButton.disabled = busy || selected.length === 0;
        if (emptyButton) emptyButton.disabled = busy || emptyButton.hidden;
        closeButtons.forEach(button => { button.disabled = busy; });
        if (selectAll) {
            selectAll.disabled = busy || all.length === 0;
            selectAll.checked = all.length > 0 && selected.length === all.length;
            selectAll.indeterminate = selected.length > 0 && selected.length < all.length;
        }
    }

    // Purpose: Updates controls while the current operation is running.
    function setBusy(value) {
        busy = Boolean(value);
        updateSelection();
    }

    // Purpose: Calls a Recycle Bin endpoint and normalizes JSON/error handling.
    async function requestJson(url, options = {}) {
        const headers = new Headers(options.headers || {});
        headers.set('Accept', 'application/json');
        if (options.body != null) headers.set('Content-Type', 'application/json');
        if (token && options.method && options.method.toUpperCase() !== 'GET') headers.set('RequestVerificationToken', token);
        const response = await fetch(url, { ...options, headers, cache: 'no-store' });
        if (response.status === 401) { window.location.replace(window.mauiAppUrl('/Account/SessionExpired')); throw new Error('MOVEit session expired.'); }
        const type = response.headers.get('content-type') || '';
        const payload = type.includes('application/json') ? await response.json() : null;
        if (!response.ok) throw new Error(payload?.message || `Request failed (${response.status}).`);
        return payload || {};
    }

    // Purpose: Returns the user-facing Recycle Bin resource type label.
    function friendlyType(value) {
        const type = String(value || '').trim();
        return type === 'DateList' ? 'Date List' : type;
    }

    // Purpose: Formats a date value for the current UI.
    function formatDate(value) {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? String(value || '') : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
    }

    // Purpose: Renders render.
    function render(data) {
        const items = Array.isArray(data?.items) ? data.items : [];
        rows.innerHTML = '';
        items.forEach(item => {
            const tr = document.createElement('tr');
            const values = [friendlyType(item.type), item.name, item.deletedBy, formatDate(item.deletedOn)];
            const selectCell = document.createElement('td');
            selectCell.className = 'recycle-bin-select';
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox'; checkbox.value = String(item.id || ''); checkbox.dataset.recycleBinItem = '';
            checkbox.addEventListener('change', updateSelection);
            selectCell.appendChild(checkbox); tr.appendChild(selectCell);
            values.forEach(value => { const td = document.createElement('td'); td.textContent = String(value || ''); tr.appendChild(td); });
            rows.appendChild(tr);
        });
        if (emptyState) emptyState.hidden = items.length > 0;
        if (emptyButton) emptyButton.hidden = !data?.isAdmin || items.length === 0;
        if (selectAll) selectAll.disabled = items.length === 0;
        updateSelection();
    }

    // Purpose: Loads and renders Recycle Bin items visible to the current user.
    async function loadItems(clearMessage = true) {
        if (clearMessage) setMessage('');
        if (restoreButton) restoreButton.disabled = true;
        if (selectAll) { selectAll.checked = false; selectAll.indeterminate = false; }
        try { render(await requestJson(dialog.dataset.itemsUrl, { method: 'GET' })); }
        catch (error) { setMessage(error?.message || 'Unable to load the Recycle Bin.', 'error'); }
    }

    // Purpose: Opens the shared Recycle Bin confirmation dialog for the requested action.
    function confirmAction(title, text, acceptLabel = 'Continue', danger = false) {
        if (!confirmDialog || !confirmAccept || !confirmTitle || !confirmMessage) return Promise.resolve(false);
        confirmTitle.textContent = title;
        confirmMessage.textContent = text;
        confirmAccept.textContent = acceptLabel;
        confirmAccept.classList.toggle('danger-button', danger);
        confirmAccept.classList.toggle('primary-button', !danger);
        return new Promise(resolve => {
            let settled = false;
            // Purpose: Closes the confirmation dialog and resolves its pending promise.
            const finish = value => {
                if (settled) return;
                settled = true;
                confirmAccept.removeEventListener('click', accept);
                confirmCancel.forEach(button => button.removeEventListener('click', cancel));
                confirmDialog.removeEventListener('cancel', onCancel);
                if (confirmDialog.open) confirmDialog.close();
                resolve(value);
            };
            // Purpose: Accepts the current confirmation action.
            const accept = () => finish(true);
            // Purpose: Cancels cancel.
            const cancel = () => finish(false);
            // Purpose: Cancels the current confirmation action.
            const onCancel = event => { event.preventDefault(); finish(false); };
            confirmAccept.addEventListener('click', accept);
            confirmCancel.forEach(button => button.addEventListener('click', cancel));
            confirmDialog.addEventListener('cancel', onCancel);
            confirmDialog.showModal();
        });
    }

    // Purpose: Restores the selected Recycle Bin items after conflict preflight.
    async function restoreSelected() {
        const ids = selectedIds();
        if (!ids.length || busy) return;
        setBusy(true); setMessage('Checking selected items…', 'info');
        try {
            const preflight = await requestJson(dialog.dataset.preflightUrl, { method: 'POST', body: JSON.stringify({ ids }) });
            const conflicts = Array.isArray(preflight?.conflicts) ? preflight.conflicts : [];
            let overwriteConflicts = false;
            if (conflicts.length) {
                const blocked = conflicts.filter(item => item?.canOverwrite === false);
                if (blocked.length) {
                    const details = blocked.slice(0, 5).map(item => `${friendlyType(item.type)}: ${item.name}${item.message ? ` — ${item.message}` : ''}`).join('\n');
                    const moreBlocked = blocked.length > 5 ? `\n…and ${blocked.length - 5} more.` : '';
                    setMessage(`Restore cannot continue because one or more selected items cannot be safely restored:\n${details}${moreBlocked}`, 'error');
                    return;
                }
                const names = conflicts.slice(0, 5).map(item => `${friendlyType(item.type)}: ${item.name}`).join('\n');
                const more = conflicts.length > 5 ? `\n…and ${conflicts.length - 5} more.` : '';
                overwriteConflicts = await confirmAction(
                    'Overwrite Existing Items?',
                    `The following restored item names already exist in MOVEit:\n\n${names}${more}\n\nMAUI will save an Undo version before each existing item is overwritten.`,
                    'Overwrite & Restore',
                    true);
                if (!overwriteConflicts) { setMessage('Restore cancelled. No existing items were overwritten.', 'info'); return; }
            }
            setMessage(`Restoring ${ids.length} item${ids.length === 1 ? '' : 's'}…`, 'info');
            const result = await requestJson(dialog.dataset.restoreUrl, { method: 'POST', body: JSON.stringify({ ids, overwriteConflicts }) });
            const itemResults = Array.isArray(result?.items) ? result.items : [];
            const resultLines = [
                `${result?.succeeded || 0} item(s) restored${result?.failed ? `; ${result.failed} failed` : ''}.`
            ];
            itemResults.forEach(item => {
                const type = friendlyType(item?.type || 'Item');
                const name = String(item?.name || item?.id || '').trim();
                const objectId = String(item?.newObjectId || '').trim();
                const note = String(item?.message || '').trim();
                if (item?.success) {
                    const action = item?.overwritten ? 'restored over the existing item' : 'restored';
                    resultLines.push(`${type}: ${name} — ${action}${objectId ? ` (ID ${objectId})` : ''}.${note ? ` ${note}` : ''}`);
                } else {
                    resultLines.push(`${type}: ${name} — restore failed.${note ? ` ${note}` : ''}`);
                }
            });
            setMessage(resultLines.join('\n'), result?.failed ? 'error' : 'success');
            await loadItems(false);
        } catch (error) { setMessage(error?.message || 'Unable to restore the selected items.', 'error'); }
        finally { setBusy(false); }
    }

    // Purpose: Permanently removes all Recycle Bin items when authorized.
    async function emptyRecycleBin() {
        if (busy) return;
        if (!await confirmAction('Empty Recycle Bin?', 'Permanently remove every item in the Recycle Bin for this MOVEit environment? This cannot be undone.', 'Empty Recycle Bin', true)) return;
        setBusy(true);
        try {
            const result = await requestJson(dialog.dataset.emptyUrl, { method: 'POST', body: '{}' });
            setMessage(result?.message || 'Recycle Bin emptied.', 'success');
            await loadItems(false);
        } catch (error) { setMessage(error?.message || 'Unable to empty the Recycle Bin.', 'error'); }
        finally { setBusy(false); }
    }

    openButton.addEventListener('click', async () => {
        if (restoreButton) restoreButton.disabled = true;
        dialog.showModal();
        await loadItems();
    });
    closeButtons.forEach(button => button.addEventListener('click', () => { if (!busy) dialog.close(); }));
    dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
    selectAll?.addEventListener('change', () => { dialog.querySelectorAll('[data-recycle-bin-item]').forEach(input => { input.checked = selectAll.checked; }); updateSelection(); });
    restoreButton?.addEventListener('click', restoreSelected);
    emptyButton?.addEventListener('click', emptyRecycleBin);
})();
