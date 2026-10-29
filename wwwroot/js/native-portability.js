/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Shared Task/Host native portability dialogs.
 */
(() => {
    'use strict';

    function create(options) {
        const objectType = String(options?.objectType || '').trim();
        const exportUrl = String(options?.exportUrl || '').trim();
        const preflightUrl = String(options?.preflightUrl || '').trim();
        const importUrl = String(options?.importUrl || '').trim();
        const antiForgeryToken = String(options?.antiForgeryToken || '');
        const loginUrl = String(options?.loginUrl || window.mauiAppUrl('/Account/SessionExpired'));
        const status = typeof options?.status === 'function' ? options.status : () => {};
        const refresh = typeof options?.refresh === 'function' ? options.refresh : async () => {};
        if (!objectType || !exportUrl) return null;
        const root = options.root || document;

        const exportDialog = root.querySelector('[data-native-portability-export-dialog]');
        const exportForm = exportDialog?.querySelector('[data-native-portability-export-form]');
        const exportError = exportDialog?.querySelector('[data-native-portability-export-error]');
        const exportPassFields = exportDialog?.querySelector('[data-native-portability-passphrase-fields]');
        const exportPassphrase = exportDialog?.querySelector('[data-native-portability-export-passphrase]');
        const exportConfirm = exportDialog?.querySelector('[data-native-portability-export-confirm]');
        const includeKeys = exportDialog?.querySelector('[data-native-portability-include-keys]');
        const exportSubmit = exportDialog?.querySelector('[data-native-portability-export-submit]');
        const exportSubmitLabel = exportDialog?.querySelector('[data-native-portability-export-submit-label]');

        const importDialog = root.querySelector('[data-native-portability-import-dialog]');
        const importForm = importDialog?.querySelector('[data-native-portability-import-form]');
        const importError = importDialog?.querySelector('[data-native-portability-import-error]');
        const importFile = importDialog?.querySelector('[data-native-portability-import-file]');
        const importPassphrase = importDialog?.querySelector('[data-native-portability-import-passphrase]');
        const leaveDisabled = importDialog?.querySelector('[data-native-portability-leave-disabled]');
        const validation = importDialog?.querySelector('[data-native-portability-validation]');
        const summary = importDialog?.querySelector('[data-native-portability-summary]');
        const conflicts = importDialog?.querySelector('[data-native-portability-conflicts]');
        const metadataAckRow = importDialog?.querySelector('[data-native-portability-metadata-ack-row]');
        const metadataAck = importDialog?.querySelector('[data-native-portability-metadata-ack]');
        const importSubmit = importDialog?.querySelector('[data-native-portability-import-submit]');
        const importSubmitLabel = importDialog?.querySelector('[data-native-portability-import-submit-label]');

        // Errors belong to the Export dialog while its options are open. Completion
        // closes it and uses the calling editor's existing footer feedback route.
        window.MauiModalUI?.register(exportDialog, { error: text => showError(exportError, text), clear: () => clearError(exportError) });

        let exportIds = [];
        let exportNames = [];
        let busy = false;
        let preflight = null;

        function protectionMode() {
            return exportDialog?.querySelector('input[name="nativePortabilityProtection"]:checked')?.value === 'passphrase' ? 'passphrase' : 'server-key';
        }

        function showError(target, message) {
            if (!target) return;
            target.textContent = String(message || 'The operation could not be completed.');
            target.hidden = false;
        }

        function clearError(target) {
            if (!target) return;
            target.textContent = '';
            target.hidden = true;
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
            if (!exportPassphrase || !exportConfirm) return;
            if (!force && exportPassphrase.value && exportConfirm.value) return;
            const generated = generateExportPassphrase();
            exportPassphrase.value = generated;
            exportConfirm.value = generated;
        }

        function setBusy(value, mode) {
            busy = Boolean(value);
            const dialog = mode === 'export' ? exportDialog : importDialog;
            dialog?.querySelectorAll('input, select, button').forEach(control => {
                if (mode === 'export' && control === exportSubmit) return;
                if (mode === 'import' && control === importSubmit) return;
                control.disabled = busy;
            });
            if (mode === 'export') {
                if (exportSubmit) exportSubmit.disabled = busy;
                if (exportSubmitLabel) exportSubmitLabel.textContent = busy ? 'Exporting…' : 'Export';
            } else {
                if (importSubmitLabel && busy) importSubmitLabel.textContent = preflight ? 'Importing…' : 'Analyzing…';
                updateImportSubmitState();
            }
            dialog?.classList.toggle('is-busy', busy);
            if (dialog) dialog.setAttribute('aria-busy', busy ? 'true' : 'false');
        }

        function updateProtectionUi() {
            const portable = protectionMode() === 'passphrase';
            if (exportPassFields) exportPassFields.hidden = !portable;
            if (portable) {
                try { populateExportPassphrase(); }
                catch (error) { showError(exportError, error?.message || 'Unable to generate an export passphrase. Enter one manually.'); }
            } else {
                if (exportPassphrase) exportPassphrase.value = '';
                if (exportConfirm) exportConfirm.value = '';
            }
        }

        function resetExport() {
            exportIds = [];
            exportNames = [];
            clearError(exportError);
            const serverKey = exportDialog?.querySelector('input[name="nativePortabilityProtection"][value="server-key"]');
            if (serverKey) serverKey.checked = true;
            if (includeKeys) includeKeys.checked = true;
            if (exportPassphrase) exportPassphrase.value = '';
            if (exportConfirm) exportConfirm.value = '';
            updateProtectionUi();
            setBusy(false, 'export');
        }

        function openExport(ids, names) {
            if (!exportDialog) return;
            resetExport();
            exportIds = Array.from(new Set((ids || []).map(value => String(value || '').trim()).filter(Boolean)));
            exportNames = (names || []).map(value => String(value || '').trim()).filter(Boolean);
            if (!exportIds.length) return;
            exportDialog.showModal();
        }

        async function exportPackage() {
            clearError(exportError);
            const mode = protectionMode();
            const phrase = String(exportPassphrase?.value || '');
            if (mode === 'passphrase') {
                if (!phrase) { showError(exportError, 'Enter a passphrase for a Portable export.'); exportPassphrase?.focus(); return; }
                const passphraseError = passphraseValidationMessage(phrase);
                if (passphraseError) { showError(exportError, passphraseError); exportPassphrase?.focus(); return; }
                if (phrase !== String(exportConfirm?.value || '')) { showError(exportError, 'Passphrase and confirmation do not match.'); exportConfirm?.focus(); return; }
            }

            setBusy(true, 'export');
            try {
                const response = await fetch(exportUrl, {
                    method: 'POST',
                    headers: {
                        'Accept': 'application/zip, application/octet-stream, application/json',
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': antiForgeryToken
                    },
                    body: JSON.stringify({
                        itemIds: exportIds,
                        protectionMode: mode,
                        passphrase: mode === 'passphrase' ? phrase : '',
                        includeKeys: Boolean(includeKeys?.checked)
                    })
                });
                if (response.status === 401) { window.location.replace(loginUrl); return; }
                if (!response.ok) {
                    const contentType = response.headers.get('content-type') || '';
                    const payload = contentType.includes('application/json') ? await response.json().catch(() => null) : null;
                    throw new Error(payload?.message || `Unable to export ${objectType} (${response.status}).`);
                }
                const blob = await response.blob();
                if (!blob.size) throw new Error(`The ${objectType} export was empty.`);
                const disposition = response.headers.get('content-disposition') || '';
                const utf8Name = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
                const quotedName = disposition.match(/filename="([^"]+)"/i)?.[1];
                const fallback = exportIds.length === 1
                    ? `${exportNames[0] || `${objectType}_${exportIds[0]}`}.maui`
                    : `${objectType}s_${exportIds.length}.maui`;
                const filename = utf8Name ? decodeURIComponent(utf8Name) : (quotedName || fallback);
                const objectUrl = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = objectUrl;
                link.download = filename;
                link.hidden = true;
                document.body.appendChild(link);
                link.click();
                link.remove();
                window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
                exportDialog?.close();
                status(`Exported ${exportIds.length} ${objectType}${exportIds.length === 1 ? '' : 's'}.`, 5000);
            } catch (error) {
                showError(exportError, error?.message || `Unable to export ${objectType}.`);
            } finally {
                // Keep the generated/user-entered value available when an export fails so the administrator can
                // correct/retry without losing it. The dialog close handler still clears both fields on exit.
                setBusy(false, 'export');
            }
        }

        function resetImport() {
            preflight = null;
            clearError(importError);
            if (importFile) importFile.value = '';
            if (importPassphrase) importPassphrase.value = '';
            if (leaveDisabled) leaveDisabled.checked = true;
            if (metadataAck) metadataAck.checked = false;
            if (metadataAckRow) metadataAckRow.hidden = true;
            if (validation) validation.hidden = true;
            summary?.replaceChildren();
            conflicts?.replaceChildren();
            setBusy(false, 'import');
            updateImportSubmitState();
        }

        function openImport() {
            if (!importDialog || !preflightUrl || !importUrl) return;
            resetImport();
            importDialog.showModal();
            window.requestAnimationFrame(() => importFile?.focus());
        }

        function appendSummary(kind, message) {
            if (!summary) return;
            const row = document.createElement('div');
            row.className = `task-import-validation-row ${kind}`;
            const icon = document.createElement('span');
            icon.className = 'task-import-validation-icon';
            icon.textContent = kind === 'error' ? '×' : kind === 'warning' ? '!' : '✓';
            const text = document.createElement('span');
            text.textContent = message;
            row.append(icon, text);
            summary.appendChild(row);
        }

        function objectRoleLabel(item) {
            const displayType = String(item?.displayType || item?.objectType || 'Object').trim() || 'Object';
            return item?.selected ? `Selected ${displayType}` : `${displayType} Dependency`;
        }

        function friendlyConflictReasons(reasons) {
            return (Array.isArray(reasons) ? reasons : []).map(reason => {
                const normalized = String(reason || '').trim();
                if (normalized.toLowerCase() === 'id') return 'ID';
                if (normalized.toLowerCase() === 'name') return 'Name';
                return normalized ? normalized.charAt(0).toUpperCase() + normalized.slice(1) : '';
            }).filter(Boolean);
        }

        function conflictKey(item) {
            return `${String(item?.objectType || '').toLowerCase()}|${String(item?.sourceId || '').toLowerCase()}`;
        }

        function renderConflict(item) {
            if (!conflicts) return;
            const card = document.createElement('div');
            card.className = 'native-portability-conflict';
            card.dataset.portabilityConflictKey = conflictKey(item);
            card.dataset.objectType = item.objectType || '';
            card.dataset.sourceId = item.sourceId || '';
            card.dataset.targetId = item.conflictTargetId || '';

            const title = document.createElement('div');
            title.className = 'native-portability-conflict-title';
            const strong = document.createElement('strong');
            strong.textContent = item.sourceName || item.sourceId || item.objectType;
            const badge = document.createElement('span');
            badge.className = 'source-badge';
            badge.textContent = objectRoleLabel(item);
            title.append(strong, badge);

            const reasons = document.createElement('div');
            reasons.className = 'field-help';
            const reasonList = friendlyConflictReasons(item.duplicateReasons);
            reasons.textContent = `Conflict: ${reasonList.length ? reasonList.join(', ') : 'Existing destination object'}.`;

            const controls = document.createElement('div');
            controls.className = 'native-portability-conflict-controls';
            const selectLabel = document.createElement('label');
            selectLabel.className = 'field-block';
            const selectCaption = document.createElement('span');
            selectCaption.textContent = 'Action';
            const select = document.createElement('select');
            select.className = 'text-input';
            select.dataset.nativePortabilityResolution = '';
            const keyType = ['sshclientkey', 'sslclientcert', 'pgpkey'].includes(String(item.objectType || '').toLowerCase());
            const idConflict = Array.isArray(item.duplicateReasons)
                && item.duplicateReasons.some(reason => String(reason || '').toLowerCase() === 'id');
            if (keyType && idConflict) {
                const useExisting = document.createElement('option');
                useExisting.value = 'useexisting';
                useExisting.textContent = 'Use Existing';
                select.appendChild(useExisting);
            }
            const overwrite = document.createElement('option');
            overwrite.value = 'overwrite';
            overwrite.textContent = 'Overwrite Existing';
            select.appendChild(overwrite);
            if (item.canRename) {
                const rename = document.createElement('option');
                rename.value = 'rename';
                rename.textContent = 'Import as New / Rename';
                select.appendChild(rename);
                select.value = 'rename';
            } else if (keyType && idConflict) {
                select.value = 'useexisting';
            }
            selectLabel.append(selectCaption, select);

            const nameLabel = document.createElement('label');
            nameLabel.className = 'field-block';
            nameLabel.dataset.nativePortabilityRenameRow = '';
            const nameCaption = document.createElement('span');
            nameCaption.textContent = 'New Name';
            const nameInput = document.createElement('input');
            nameInput.className = 'text-input';
            nameInput.type = 'text';
            nameInput.maxLength = 254;
            nameInput.value = item.suggestedName || '';
            nameInput.dataset.nativePortabilityRename = '';
            nameLabel.append(nameCaption, nameInput);
            if (!item.canRename || select.value !== 'rename') nameLabel.hidden = true;

            select.addEventListener('change', () => {
                nameLabel.hidden = select.value !== 'rename';
                updateImportSubmitState();
            });
            nameInput.addEventListener('input', updateImportSubmitState);
            controls.append(selectLabel, nameLabel);
            card.append(title, reasons, controls);
            conflicts.appendChild(card);
        }

        function renderPreflight(result) {
            preflight = result || null;
            summary?.replaceChildren();
            conflicts?.replaceChildren();
            if (!preflight) { updateImportSubmitState(); return; }
            if (validation) validation.hidden = false;
            appendSummary('ok', `${preflight.packageType} package decoded successfully. Protection: ${preflight.protectionMode === 'passphrase' ? 'Portable / Passphrase' : 'Server Key'}.`);
            const itemCount = Number(preflight.itemCount || 0);
            const conflictCount = Number(preflight.conflictCount || 0);
            appendSummary('ok', `${itemCount} native ${itemCount === 1 ? 'object' : 'objects'} found; ${conflictCount} ${conflictCount === 1 ? 'conflict' : 'conflicts'}. Referenced keys/certificates: ${preflight.includesKeys ? 'included' : 'not included'}.`);
            const items = Array.isArray(preflight.items) ? preflight.items : [];
            items.forEach(item => {
                if (item.conflict) renderConflict(item);
                else appendSummary('ok', `${objectRoleLabel(item)}: ${item.sourceName || item.sourceId} — import as new.`);
                (item.metadataIssues || []).forEach(issue => appendSummary('warning', `${item.sourceName || item.sourceId}: ${issue}`));
            });
            if (metadataAckRow) metadataAckRow.hidden = !preflight.requiresMetadataAcknowledgement;
            if (metadataAck && !preflight.requiresMetadataAcknowledgement) metadataAck.checked = false;
            if (importSubmitLabel) importSubmitLabel.textContent = 'Import';
            updateImportSubmitState();
        }

        function invalidateImportPreflight() {
            preflight = null;
            if (validation) validation.hidden = true;
            summary?.replaceChildren();
            conflicts?.replaceChildren();
            if (metadataAck) metadataAck.checked = false;
            if (metadataAckRow) metadataAckRow.hidden = true;
            clearError(importError);
            updateImportSubmitState();
        }

        function collectResolutions() {
            const rows = Array.from(conflicts?.querySelectorAll('[data-portability-conflict-key]') || []);
            return rows.map(row => ({
                objectType: row.dataset.objectType || '',
                sourceId: row.dataset.sourceId || '',
                action: row.querySelector('[data-native-portability-resolution]')?.value || 'overwrite',
                newName: row.querySelector('[data-native-portability-rename]')?.value?.trim() || '',
                targetId: row.dataset.targetId || ''
            }));
        }

        function conflictsValid() {
            return collectResolutions().every(item => item.action !== 'rename' || (Boolean(item.newName) && item.newName.length <= 254));
        }

        function updateImportSubmitState() {
            if (!importSubmit) return;
            const hasFile = Boolean(importFile?.files?.[0]);
            const ackOk = !preflight?.requiresMetadataAcknowledgement || Boolean(metadataAck?.checked);
            const canImport = Boolean(preflight) && conflictsValid() && ackOk;
            importSubmit.disabled = busy || !hasFile || (preflight ? !canImport : false);
            if (importSubmitLabel && !busy) importSubmitLabel.textContent = preflight ? 'Import' : 'Analyze Package';
        }

        async function analyzeImport() {
            const file = importFile?.files?.[0];
            if (!file) { showError(importError, 'Select a MAUI portability package.'); return; }
            clearError(importError);
            setBusy(true, 'import');
            try {
                const form = new FormData();
                form.append('File', file);
                form.append('Passphrase', String(importPassphrase?.value || ''));
                const headers = new Headers({ 'Accept': 'application/json', 'RequestVerificationToken': antiForgeryToken });
                const response = await fetch(preflightUrl, { method: 'POST', headers, body: form });
                if (response.status === 401) { window.location.replace(loginUrl); return; }
                const payload = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
                if (!response.ok) throw new Error(payload?.message || `Package analysis failed (${response.status}).`);
                renderPreflight(payload?.result);
            } catch (error) {
                preflight = null;
                if (validation) validation.hidden = true;
                showError(importError, error?.message || 'Unable to analyze the package.');
            } finally {
                setBusy(false, 'import');
            }
        }

        async function importPackage() {
            const file = importFile?.files?.[0];
            if (!file) return;
            clearError(importError);
            let response = null;
            setBusy(true, 'import');
            try {
                const form = new FormData();
                form.append('File', file);
                form.append('Passphrase', String(importPassphrase?.value || ''));
                form.append('ResolutionsJson', JSON.stringify(collectResolutions()));
                form.append('LeaveTasksDisabled', String(Boolean(leaveDisabled?.checked)));
                form.append('AcknowledgeUnsupportedMetadata', String(Boolean(metadataAck?.checked)));
                const headers = new Headers({ 'Accept': 'application/json', 'RequestVerificationToken': antiForgeryToken });
                response = await fetch(importUrl, { method: 'POST', headers, body: form });
                if (response.status === 401) {
                    window.location.replace(loginUrl);
                    return;
                }

                let payload = null;
                if ((response.headers.get('content-type') || '').includes('application/json')) {
                    try { payload = await response.json(); }
                    catch { /* status handling below still decides whether an unreadable response is ambiguous */ }
                }
                if (!response.ok) {
                    const outcomeUnknown = payload?.outcomeUnknown === true || response.status === 502 || response.status === 504;
                    if (outcomeUnknown) {
                        importDialog?.close();
                        status(payload?.message || 'MAUI did not receive a definitive import result from MOVEit. The import may have completed. Refresh the grid and verify the destination objects before attempting any retry.', 10000, 'warning');
                        try { await refresh(true); } catch { /* verification remains manual if refresh also fails */ }
                        return;
                    }
                    throw new Error(payload?.message || `Import failed (${response.status}).`);
                }

                // A successful HTTP response confirms that the server-side import path completed. If the response body
                // is missing/truncated, do not present that as a failed import or invite a retry; refresh and verify instead.
                if (!payload || payload.success !== true) {
                    importDialog?.close();
                    status('MOVEit import completed, but MAUI could not read the import result. Refresh the grid and verify the imported objects before attempting any retry.', 10000, 'warning');
                    try { await refresh(true); } catch { /* the warning already tells the user to verify manually */ }
                    return;
                }

                importDialog?.close();
                const count = Number(payload?.importedCount || 0);
                const warning = String(payload?.warning || '').trim();
                status(warning || `Imported ${count} native object${count === 1 ? '' : 's'} successfully.`, 6000, warning ? 'warning' : 'default');

                // The server has already committed the native MOVEit import once an OK response reaches this point.
                // Refresh the corresponding MAUI grid; do not automatically open the imported object editor.
                try {
                    await refresh(true);
                } catch {
                    status('MOVEit import completed, but MAUI could not refresh the grid. Refresh manually and verify the imported objects before retrying.', 8000, 'warning');
                }
            } catch (error) {
                if (response === null) {
                    // No HTTP response reached the browser. The server may still have completed MOVEit's native commit,
                    // so close the workflow and require destination verification rather than offering an immediate retry.
                    importDialog?.close();
                    status('MAUI did not receive a definitive import response. MOVEit may have completed the import. Refresh the grid and verify the destination objects before attempting any retry.', 10000, 'warning');
                    try { await refresh(true); } catch { /* verification remains manual if refresh also fails */ }
                } else {
                    // A definitive non-success response means this preview is stale or invalid. Force a fresh Analyze
                    // pass before another import attempt so conflicts and destination state are recalculated.
                    invalidateImportPreflight();
                    showError(importError, error?.message || `Unable to import ${objectType}.`);
                }
            } finally {
                if (importPassphrase) importPassphrase.value = '';
                setBusy(false, 'import');
            }
        }

        exportDialog?.querySelectorAll('input[name="nativePortabilityProtection"]').forEach(input => input.addEventListener('change', updateProtectionUi));
        exportForm?.addEventListener('submit', event => { event.preventDefault(); void exportPackage(); });
        exportDialog?.querySelectorAll('[data-native-portability-export-cancel], [data-native-portability-export-close]').forEach(button => button.addEventListener('click', () => exportDialog?.close()));

        importForm?.addEventListener('submit', event => {
            event.preventDefault();
            const selectedSource = importDialog?.querySelector('input[name="taskImportMode"]:checked, input[name="hostImportMode"]:checked')?.value;
            if (selectedSource && selectedSource !== 'portability') return;
            if (preflight) void importPackage();
            else void analyzeImport();
        });
        importFile?.addEventListener('change', invalidateImportPreflight);
        importPassphrase?.addEventListener('input', () => {
            if (preflight) invalidateImportPreflight();
            else updateImportSubmitState();
        });
        metadataAck?.addEventListener('change', updateImportSubmitState);
        importDialog?.querySelectorAll('[data-native-portability-import-cancel], [data-native-portability-import-close]').forEach(button => button.addEventListener('click', () => importDialog?.close()));
        [exportDialog, importDialog].forEach(dialog => dialog?.addEventListener('cancel', event => {
            if (busy) event.preventDefault();
        }));
        exportDialog?.addEventListener('close', () => {
            if (exportPassphrase) exportPassphrase.value = '';
            if (exportConfirm) exportConfirm.value = '';
        });
        importDialog?.addEventListener('close', () => {
            // Package passphrases are intentionally ephemeral even when the administrator closes without submitting.
            if (importPassphrase) importPassphrase.value = '';
        });

        updateProtectionUi();
        updateImportSubmitState();
        return Object.freeze({ openExport, openImport });
    }

    window.MauiNativePortability = Object.freeze({ create });
})();
