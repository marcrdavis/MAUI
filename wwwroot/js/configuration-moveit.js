/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Configuration > MOVEit Configuration import/export operations.
 */

(() => {
    function initialize(context) {
        const { workspace, antiForgeryToken, showToast } = context || {};
        if (!workspace) return;
        const exportUrl = workspace.dataset.moveitConfigExportUrl || '';
        const importUrl = workspace.dataset.moveitConfigImportUrl || '';

        const exportModes = Array.from(workspace.querySelectorAll('[data-moveit-config-export-mode]'));
        const exportPassphraseFields = workspace.querySelector('[data-moveit-config-export-passphrase-fields]');
        const exportPassphrase = workspace.querySelector('[data-moveit-config-export-passphrase]');
        const exportConfirmPassphrase = workspace.querySelector('[data-moveit-config-export-confirm-passphrase]');
        const sanitizedWarning = workspace.querySelector('[data-moveit-config-export-sanitized-warning]');
        const exportButton = workspace.querySelector('[data-moveit-config-export]');
        const importModes = Array.from(workspace.querySelectorAll('[data-moveit-config-import-mode]'));
        const importFile = workspace.querySelector('[data-moveit-config-import-file]');
        const importPassphraseField = workspace.querySelector('[data-moveit-config-import-passphrase-field]');
        const importPassphrase = workspace.querySelector('[data-moveit-config-import-passphrase]');
        const importAcknowledge = workspace.querySelector('[data-moveit-config-import-acknowledge]');
        const importButton = workspace.querySelector('[data-moveit-config-import]');
        const importDialog = document.querySelector('[data-moveit-config-import-dialog]');
        const importDialogClose = importDialog?.querySelector('[data-moveit-config-import-dialog-close]');
        const importCancel = importDialog?.querySelector('[data-moveit-config-import-cancel]');
        const importConfirm = importDialog?.querySelector('[data-moveit-config-import-confirm]');
        const importConfirmFile = importDialog?.querySelector('[data-moveit-config-import-confirm-file]');
        const importError = importDialog?.querySelector('[data-moveit-config-import-error]');

        function setOperationError(element, message) {
            if (!element) return;
            element.textContent = message || '';
            element.hidden = !message;
        }

        function selectedMode(inputs, fallback) {
            return inputs.find(input => input.checked)?.value || fallback;
        }

        function passphraseValidationMessage(value) {
            const phrase = String(value || '');
            if (phrase.length < 16) return 'Passphrase must be at least 16 characters long.';
            if (!/[A-Z]/.test(phrase)) return 'Passphrase must contain at least 1 uppercase letter.';
            if (!/[a-z]/.test(phrase)) return 'Passphrase must contain at least 1 lowercase letter.';
            if (!/[0-9]/.test(phrase)) return 'Passphrase must contain at least 1 digit.';
            if (!/[^A-Za-z0-9\s]/.test(phrase)) return 'Passphrase must contain at least 1 special character.';
            return '';
        }

        function secureRandomIndex(length) {
            if (!Number.isInteger(length) || length < 1) throw new Error('Unable to generate an export passphrase.');
            if (!window.crypto?.getRandomValues) throw new Error('Secure browser random generation is unavailable. Enter an export passphrase manually.');
            const value = new Uint32Array(1);
            const limit = 0x100000000 - (0x100000000 % length);
            do { window.crypto.getRandomValues(value); } while (value[0] >= limit);
            return value[0] % length;
        }

        function generateExportPassphrase() {
            const upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
            const lower = 'abcdefghijkmnopqrstuvwxyz';
            const digits = '23456789';
            const special = '!@#$%^&*()-_=+[]{};:,.?';
            const all = upper + lower + digits + special;
            const characters = [
                upper[secureRandomIndex(upper.length)],
                lower[secureRandomIndex(lower.length)],
                digits[secureRandomIndex(digits.length)],
                special[secureRandomIndex(special.length)]
            ];
            while (characters.length < 16) characters.push(all[secureRandomIndex(all.length)]);
            for (let index = characters.length - 1; index > 0; index -= 1) {
                const swap = secureRandomIndex(index + 1);
                [characters[index], characters[swap]] = [characters[swap], characters[index]];
            }
            return characters.join('');
        }

        function populateExportPassphrase(force = false) {
            if (!exportPassphrase || !exportConfirmPassphrase) return;
            if (!force && exportPassphrase.value && exportConfirmPassphrase.value) return;
            const generated = generateExportPassphrase();
            exportPassphrase.value = generated;
            exportConfirmPassphrase.value = generated;
        }

        function updateExportMode() {
            const mode = selectedMode(exportModes, 'Passphrase');
            const portable = mode === 'Passphrase';
            const sanitized = mode === 'Sanitized';
            if (exportPassphraseFields) exportPassphraseFields.hidden = !portable;
            if (sanitizedWarning) sanitizedWarning.hidden = !sanitized;
            if (portable) {
                try { populateExportPassphrase(); }
                catch (error) { showToast?.(error?.message || 'Unable to generate an export passphrase. Enter one manually.', 'error'); }
            } else {
                if (exportPassphrase) exportPassphrase.value = '';
                if (exportConfirmPassphrase) exportConfirmPassphrase.value = '';
            }
        }

        function getDownloadFileName(response) {
            const disposition = response.headers.get('Content-Disposition') || '';
            const utfMatch = disposition.match(/filename\*=UTF-8''([^;]+)/i);
            if (utfMatch?.[1]) {
                try { return decodeURIComponent(utfMatch[1]); } catch { return utfMatch[1]; }
            }
            const normalMatch = disposition.match(/filename="?([^";]+)"?/i);
            return normalMatch?.[1] || 'MOVEitAutomation_Config.xml';
        }

        async function exportConfiguration() {
            if (!exportUrl || !exportButton || exportButton.disabled) return;
            const mode = selectedMode(exportModes, 'Passphrase');
            const passphrase = exportPassphrase?.value || '';
            const confirmPassphrase = exportConfirmPassphrase?.value || '';
            if (mode === 'Passphrase') {
                if (!passphrase) { showToast?.('Enter a passphrase for the portable configuration export.'); exportPassphrase?.focus(); return; }
                const passphraseError = passphraseValidationMessage(passphrase);
                if (passphraseError) { showToast?.(passphraseError, 'error'); exportPassphrase?.focus(); return; }
                if (passphrase !== confirmPassphrase) { showToast?.('The export passphrase and confirmation do not match.', 'error'); exportConfirmPassphrase?.focus(); return; }
            }

            const originalLabel = exportButton.textContent;
            exportButton.disabled = true;
            exportButton.textContent = 'Exporting…';
            try {
                const headers = new Headers({ 'Content-Type': 'application/json' });
                if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
                const response = await fetch(exportUrl, { method: 'POST', credentials: 'same-origin', headers, body: JSON.stringify({ mode, passphrase, confirmPassphrase }) });
                if (!response.ok) {
                    const data = await response.json().catch(() => ({}));
                    throw new Error(data?.message || `MOVEit configuration export failed (${response.status}).`);
                }
                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const anchor = document.createElement('a');
                anchor.href = url;
                anchor.download = getDownloadFileName(response);
                document.body.appendChild(anchor);
                anchor.click();
                anchor.remove();
                window.setTimeout(() => URL.revokeObjectURL(url), 1000);
                if (mode === 'Passphrase') {
                    try { populateExportPassphrase(true); }
                    catch { if (exportPassphrase) exportPassphrase.value = ''; if (exportConfirmPassphrase) exportConfirmPassphrase.value = ''; }
                } else {
                    if (exportPassphrase) exportPassphrase.value = '';
                    if (exportConfirmPassphrase) exportConfirmPassphrase.value = '';
                }
                showToast?.('MOVEit configuration exported.');
            } catch (error) {
                showToast?.(error?.message || 'The MOVEit configuration could not be exported.', 'error');
            } finally {
                exportButton.disabled = false;
                exportButton.textContent = originalLabel || 'Export Configuration';
            }
        }

        function updateImportMode() {
            const portable = selectedMode(importModes, 'Passphrase') === 'Passphrase';
            if (importPassphraseField) importPassphraseField.hidden = !portable;
            if (!portable && importPassphrase) importPassphrase.value = '';
            updateImportButton();
        }

        function updateImportButton() {
            if (!importButton) return;
            const hasFile = Boolean(importFile?.files?.length);
            const acknowledged = Boolean(importAcknowledge?.checked);
            const portable = selectedMode(importModes, 'Passphrase') === 'Passphrase';
            const hasPassphrase = Boolean(importPassphrase?.value);
            const adminControlsEnabled = !importFile?.disabled;
            importButton.disabled = !adminControlsEnabled || !hasFile || !acknowledged || (portable && !hasPassphrase);
        }

        function closeImportDialog() {
            if (importDialog?.open) importDialog.close();
            // Whole-configuration import passphrases are sensitive package credentials, not account credentials.
            // Clear them whenever the confirmation workflow closes so Cancel/Close never leaves the value in the DOM.
            if (importPassphrase) importPassphrase.value = '';
            setOperationError(importError, '');
            updateImportButton();
        }

        function openImportConfirmation() {
            const file = importFile?.files?.[0];
            if (!file || !importAcknowledge?.checked) return;
            if (!file.name.toLowerCase().endsWith('.xml')) { showToast?.('Select a MOVEit Automation configuration .xml file.'); return; }
            if (importConfirmFile) importConfirmFile.textContent = file.name;
            setOperationError(importError, '');
            importDialog?.showModal();
        }

        async function importConfiguration() {
            const file = importFile?.files?.[0];
            if (!file || !importUrl || !importConfirm) return;
            const mode = selectedMode(importModes, 'Passphrase');
            const passphrase = importPassphrase?.value || '';
            if (mode === 'Passphrase' && !passphrase) { setOperationError(importError, 'Enter the passphrase that was used to export this configuration.'); return; }

            const formData = new FormData();
            formData.append('file', file, file.name);
            formData.append('mode', mode);
            formData.append('passphrase', passphrase);
            formData.append('acknowledged', 'true');
            const originalLabel = importConfirm.textContent;
            importConfirm.disabled = true;
            if (importCancel) importCancel.disabled = true;
            if (importDialogClose) importDialogClose.disabled = true;
            importConfirm.textContent = 'Importing…';
            setOperationError(importError, '');
            let response = null;
            try {
                const headers = new Headers();
                if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
                response = await fetch(importUrl, { method: 'POST', credentials: 'same-origin', headers, body: formData });
                const data = await response.json().catch(() => ({}));
                if (!response.ok) {
                    if (data?.outcomeUnknown === true) {
                        closeImportDialog();
                        if (importAcknowledge) importAcknowledge.checked = false;
                        updateImportButton();
                        showToast?.(data?.message || 'MOVEit did not return a definitive import result. Verify the active MOVEit configuration before retrying.', 'error');
                        return;
                    }
                    throw new Error(data?.message || `MOVEit configuration import failed (${response.status}).`);
                }
                closeImportDialog();
                if (importFile) importFile.value = '';
                if (importPassphrase) importPassphrase.value = '';
                if (importAcknowledge) importAcknowledge.checked = false;
                updateImportButton();
                showToast?.(data?.message || 'MOVEit configuration imported successfully.');
            } catch (error) {
                if (response === null) {
                    closeImportDialog();
                    if (importAcknowledge) importAcknowledge.checked = false;
                    updateImportButton();
                    showToast?.('The connection ended before MAUI received a definitive MOVEit configuration-import result. The import may have completed. Verify the active MOVEit Automation configuration before attempting another import.', 'error');
                    return;
                }
                setOperationError(importError, error?.message || 'The MOVEit configuration could not be imported.');
            } finally {
                importConfirm.disabled = false;
                if (importCancel) importCancel.disabled = false;
                if (importDialogClose) importDialogClose.disabled = false;
                importConfirm.textContent = originalLabel || 'Import Configuration';
            }
        }

        exportModes.forEach(input => input.addEventListener('change', updateExportMode));
        exportButton?.addEventListener('click', exportConfiguration);
        importModes.forEach(input => input.addEventListener('change', updateImportMode));
        importFile?.addEventListener('change', updateImportButton);
        importPassphrase?.addEventListener('input', updateImportButton);
        importAcknowledge?.addEventListener('change', updateImportButton);
        importButton?.addEventListener('click', openImportConfirmation);
        importDialogClose?.addEventListener('click', closeImportDialog);
        importCancel?.addEventListener('click', closeImportDialog);
        importConfirm?.addEventListener('click', importConfiguration);
        updateExportMode();
        updateImportMode();
        updateImportButton();
    }

    window.MauiConfigurationMoveIt = Object.freeze({ initialize });
})();
