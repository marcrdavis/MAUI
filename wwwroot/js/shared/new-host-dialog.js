/* Shared New Host UI; Hosts/CreateHost and the Host CRUD layer remain authoritative. */
(() => {
    'use strict';
    function create(options) {
        const endpoint = () => options.createUrl;
        const fetchJson = options.fetchJson;
        const launchHostEditor = options.openEditor;
        const refreshHostGrid = options.refresh || (async () => {});
        const useMauiHostEditor = options.useMauiHostEditor;
        function closeReservedPopup(popup) { try { if (popup && !popup.closed) popup.close(); } catch { /* already closed */ } }
        const newHostDialog = document.querySelector('[data-new-host-dialog]');
        const newHostForm = newHostDialog?.querySelector('[data-new-host-form]');
        const newHostName = newHostDialog?.querySelector('[data-new-host-name]');
        const newHostUncField = newHostDialog?.querySelector('[data-new-host-unc-field]');
        const newHostUnc = newHostDialog?.querySelector('[data-new-host-unc]');
        const newHostAutoFormat = newHostDialog?.querySelector('[data-new-host-auto-format]');
        const newHostSubmit = newHostDialog?.querySelector('[data-new-host-submit]');
        const newHostCancel = newHostDialog?.querySelector('[data-new-host-cancel]');
        const newHostClose = newHostDialog?.querySelector('[data-new-host-close]');
        const newHostError = newHostDialog?.querySelector('[data-new-host-error]');
        const newHostSpinner = newHostDialog?.querySelector('[data-new-host-spinner]');
        const newHostSubmitLabel = newHostDialog?.querySelector('[data-new-host-submit-label]');
        let newHostBusy = false;
        let newHostCreationUncertain = false;
        const updateNewHostAction = window.MauiModalUI.watchAction(newHostDialog, newHostSubmit,
            () => !newHostBusy && !newHostCreationUncertain,
            () => Boolean(newHostName?.value.trim()) && newHostName.value.trim().length <= 254 && window.MauiModalUI.formValid(newHostDialog));

        // Opens the New Host dialog with its default type and naming behavior.
        function openNewHostDialog() {
            if (!newHostDialog || newHostBusy) return;
            resetNewHostDialog();
            newHostDialog.showModal();
            window.requestAnimationFrame(() => newHostName?.focus());
        }

        // Restores the New Host dialog to a clean UNC Share selection.
        function resetNewHostDialog() {
            newHostCreationUncertain = false;
            setNewHostBusy(false);
            hideNewHostError();
            if (newHostName) newHostName.value = '';
            if (newHostUnc) newHostUnc.value = '';
            if (newHostAutoFormat) newHostAutoFormat.checked = newHostAutoFormat.defaultChecked;
            const defaultType = newHostDialog?.querySelector('input[name="newHostType"][value="Share"]');
            if (defaultType) defaultType.checked = true;
            updateNewHostUncField();
        }

        // Only UNC Share creation supplies a required native UNC connection.
        function updateNewHostUncField() {
            const isShare = newHostDialog?.querySelector('input[name="newHostType"]:checked')?.value === 'Share';
            if (newHostUncField) newHostUncField.hidden = !isShare;
            if (newHostUnc) {
                newHostUnc.required = isShare;
                newHostUnc.disabled = !isShare;
            }
        }

        // Applies the same uppercase/space-to-underscore convention used by New Task.
        function applyNewHostNameFormat() {
            if (!newHostName || !newHostAutoFormat?.checked) return;
            const start = newHostName.selectionStart;
            const end = newHostName.selectionEnd;
            const formatted = newHostName.value.toUpperCase().replaceAll(' ', '_');
            if (formatted === newHostName.value) return;
            newHostName.value = formatted;
            if (typeof start === 'number' && typeof end === 'number') {
                try { newHostName.setSelectionRange(start, end); } catch { /* unsupported */ }
            }
        }

        // Enables/disables all New Host controls while New Host creation is in progress.
        function setNewHostBusy(busy) {
            newHostBusy = Boolean(busy);
            if (newHostName) newHostName.readOnly = newHostBusy;
            if (newHostUnc) newHostUnc.readOnly = newHostBusy;
            if (newHostAutoFormat) newHostAutoFormat.disabled = newHostBusy;
            const group = newHostDialog?.querySelector('[data-object-resource-group]');
            if (group) group.disabled = newHostBusy || Boolean(group.closest('[hidden]'));
            newHostDialog?.querySelectorAll('input[name="newHostType"]').forEach(input => {
                const permanentlyDisabled = Boolean(input.closest('.host-type-unavailable')) || Boolean(options.allowedTypes && !options.allowedTypes.includes(input.value));
                input.disabled = permanentlyDisabled || newHostBusy;
            });
            if (newHostCancel) newHostCancel.disabled = newHostBusy;
            if (newHostClose) newHostClose.disabled = newHostBusy;
            if (newHostSpinner) newHostSpinner.hidden = !newHostBusy;
            if (newHostSubmitLabel) newHostSubmitLabel.textContent = newHostBusy ? 'Creating…' : 'Create Host';
            newHostDialog?.classList.toggle('is-busy', newHostBusy);
            updateNewHostAction();
        }

        // Displays a server-side or validation error without closing the New Host dialog.
        function showNewHostError(message) {
            if (!newHostError) return;
            newHostError.textContent = message || 'Unable to create the Host.';
            newHostError.hidden = false;
        }

        // Clears the New Host error surface.
        function hideNewHostError() {
            if (!newHostError) return;
            newHostError.textContent = '';
            newHostError.hidden = true;
        }

        // Creates the Host once, then opens the preferred editor.
        async function createNewHost() {
            if (newHostBusy || newHostCreationUncertain) return;
            hideNewHostError();
            const name = String(newHostName?.value || '').trim();
            const hostType = String(newHostDialog?.querySelector('input[name="newHostType"]:checked')?.value || '').trim();
            if (!name || name.length > 254) {
                showNewHostError('Enter a Host Name from 1 to 254 characters.');
                newHostName?.focus();
                return;
            }
            if (!hostType) {
                showNewHostError('Select a Host Type.');
                return;
            }
            if (window.MauiObjectAccess?.canCreate?.('Host') === false) { showNewHostError('Your MOVEit permissions do not allow creating Hosts.'); return; }
            const groupSelect = newHostDialog?.querySelector('[data-object-resource-group]');
            if (groupSelect && !groupSelect.disabled && !groupSelect.value) { showNewHostError('Select a Resource Group.'); groupSelect.focus(); return; }
            const unc = hostType === 'Share' ? String(newHostUnc?.value || '').trim() : '';
            if (hostType === 'Share' && (!unc || !unc.startsWith('\\\\'))) {
                showNewHostError(!unc ? 'Resource UNC is required.' : 'Resource UNC must begin with \\\\.');
                newHostUnc?.focus();
                return;
            }
            if (!useMauiHostEditor && !window.MauiNativeEditor?.reserve) {
                showNewHostError('The native MOVEit editor launcher is unavailable.');
                return;
            }
            const popup = useMauiHostEditor ? null : window.MauiNativeEditor.reserve(`MAUI Host Editor - ${name}`);
            if (!useMauiHostEditor && !popup) {
                showNewHostError('The MOVEit native editor popup was blocked by the browser.');
                return;
            }

            setNewHostBusy(true);
            let host;
            try {
                const result = await fetchJson(endpoint('createHostUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ name, hostType, resourceGroup: window.MauiObjectAccess?.group(newHostDialog) || '', ...(hostType === 'Share' ? { unc } : {}) })
                });
                host = result.host || {};
                if (result.success !== true || !String(host.id || '').trim())
                    throw new Error('The Host creation acknowledgement was incomplete. Inspect Hosts before creating another copy.');
            } catch (error) {
                closeReservedPopup(popup);
                newHostCreationUncertain = Boolean(error?.payload?.outcomeUnknown || !error?.status || error.status >= 500);
                setNewHostBusy(false);
                showNewHostError((error?.message || 'Unable to create the Host.')
                    + (newHostCreationUncertain ? ' Close this dialog and inspect Hosts before creating another Host.' : ''));
                // Preserve the creation error if the best-effort inventory refresh also fails.
                if (newHostCreationUncertain) void Promise.resolve().then(() => refreshHostGrid(true)).catch(() => {});
                return;
            }
            // A confirmed create is never retried because a later editor/refresh failed.
            newHostDialog.close();
            try {
                options.created?.(host);
                const opened = await launchHostEditor(host.id, host.name || name, popup, false);
                if (!opened) closeReservedPopup(popup);
                await refreshHostGrid(false);
            } catch (error) {
                closeReservedPopup(popup);
                options.status?.(`Host '${host.name || name}' (ID ${host.id}) was created, but its editor or list could not be refreshed. ${error?.message || 'Refresh Hosts to inspect it.'}`, 5000, 'error');
            }
        }


        newHostName?.addEventListener('input', applyNewHostNameFormat);
        newHostAutoFormat?.addEventListener('change', () => { if (newHostAutoFormat.checked) applyNewHostNameFormat(); });
        newHostDialog?.querySelectorAll('input[name="newHostType"]').forEach(input => {
            input.addEventListener('change', updateNewHostUncField);
        });
        newHostForm?.addEventListener('submit', event => { event.preventDefault(); createNewHost(); });
        newHostClose?.addEventListener('click', () => { if (!newHostBusy) newHostDialog?.close(); });
        newHostCancel?.addEventListener('click', () => { if (!newHostBusy) newHostDialog?.close(); });
        newHostDialog?.addEventListener('cancel', event => { if (newHostBusy) event.preventDefault(); });
        newHostDialog?.addEventListener('close', resetNewHostDialog);


        if (options.allowedTypes) newHostDialog?.querySelectorAll('input[name="newHostType"]').forEach(input => {
            if (!options.allowedTypes.includes(input.value)) { input.disabled = true; input.closest('label').hidden = true; }
        });
        return { open: openNewHostDialog, get busy() { return newHostBusy; }, get isOpen() { return Boolean(newHostDialog?.open); } };
    }
    window.MauiNewHostDialog = { create };
})();
