/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Shared Script and Date List portable import/export workflow.
 */
(() => {
    'use strict';

    function token() {
        return document.querySelector('.task-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    }

    async function errorFrom(response) {
        const ct = response.headers.get('content-type') || '';
        const payload = ct.includes('application/json') ? await response.json() : null;
        return new Error(payload?.message || `Request failed (${response.status}).`);
    }


    function validationProgressRow() {
        const div = document.createElement('div');
        div.className = 'task-import-validation-progress';
        const spinner = document.createElement('span');
        spinner.className = 'inline-spinner';
        spinner.setAttribute('aria-hidden', 'true');
        div.setAttribute('role', 'status');
        div.setAttribute('aria-live', 'polite');
        const message = document.createElement('span');
        message.textContent = 'Validation in progress...';
        div.append(spinner, message);
        return div;
    }

    function validationRow(kind, text) {
        const div = document.createElement('div');
        div.className = `task-import-validation-row ${kind}`;
        const icon = document.createElement('span');
        icon.className = 'task-import-validation-icon';
        icon.textContent = kind === 'error' ? '×' : kind === 'warning' ? '!' : '✓';
        const copy = document.createElement('span');
        copy.textContent = text;
        div.append(icon, copy);
        return div;
    }

    function downloadResponse(response, fallbackName) {
        return response.blob().then(blob => {
            const disposition = response.headers.get('content-disposition') || '';
            const utf8Name = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
            const quotedName = disposition.match(/filename="([^"]+)"/i)?.[1];
            const filename = utf8Name ? decodeURIComponent(utf8Name) : (quotedName || fallbackName);
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = objectUrl;
            link.download = filename;
            link.hidden = true;
            document.body.appendChild(link);
            link.click();
            link.remove();
            window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
            return filename;
        });
    }

    // Purpose: Builds the package validation summary with a collapsed list of exported item names.
    function packageSummaryRow(kind, itemNames, itemCount, blockerCount, importMode) {
        const names = Array.isArray(itemNames) ? itemNames.filter(name => String(name || '').trim()) : [];
        const count = Math.max(Number(itemCount || 0), names.length, 1);
        const blockers = Math.max(Number(blockerCount || 0), 0);
        const summary = document.createElement('div');
        summary.className = `task-import-validation-summary ${kind}`;

        const line = document.createElement('div');
        line.className = 'task-import-validation-summary-line';
        const icon = document.createElement('span');
        icon.className = 'task-import-validation-icon';
        icon.textContent = kind === 'error' ? '×' : kind === 'warning' ? '!' : '✓';
        const message = document.createElement('span');
        const modeLabel = importMode === 'overwrite' ? 'Update existing' : 'Create new';
        message.textContent = blockers > 0
            ? `Package is valid. ${count} ${count === 1 ? 'item' : 'items'} found. ${blockers} ${blockers === 1 ? 'item cannot' : 'items cannot'} be imported using ${modeLabel}. Review the import details or choose the appropriate Import As mode.`
            : `Package is valid. ${count} ${count === 1 ? 'item' : 'items'} found. No import-mode conflicts detected.`;
        line.append(icon, message);

        let details = null;
        if (names.length) {
            const plural = names.length !== 1;
            const toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'task-import-validation-toggle';
            toggle.textContent = plural ? 'Show items' : 'Show item';
            details = document.createElement('div');
            details.className = 'task-import-validation-items';
            details.hidden = true;
            const list = document.createElement('ul');
            names.forEach(name => {
                const item = document.createElement('li');
                item.textContent = name;
                list.appendChild(item);
            });
            details.appendChild(list);
            toggle.addEventListener('click', () => {
                const show = details.hidden;
                details.hidden = !show;
                toggle.textContent = show ? (plural ? 'Hide items' : 'Hide item') : (plural ? 'Show items' : 'Show item');
            });
            line.appendChild(toggle);
        }

        summary.appendChild(line);
        if (details) summary.appendChild(details);
        return summary;
    }

    // Purpose: Creates and wires one shared portable resource import/export controller.
    function create(options) {
        const dialog = document.querySelector(`[data-resource-import-dialog="${options.key}"]`);
        if (!dialog) return null;

        const pluralLabel = String(options.pluralLabel || `${options.label}s`);
        const form = dialog.querySelector('[data-resource-import-form]');
        const file = dialog.querySelector('[data-resource-import-file]');
        const validation = dialog.querySelector('[data-resource-import-validation]');
        const validationList = dialog.querySelector('[data-resource-import-validation-list]');
        const metadataAckRow = dialog.querySelector('[data-resource-import-metadata-ack-row]');
        const metadataAck = dialog.querySelector('[data-resource-import-metadata-ack]');
        const overwriteInfo = dialog.querySelector('[data-resource-import-overwrite-info]');
        const error = dialog.querySelector('[data-resource-import-error]');
        const submit = dialog.querySelector('[data-resource-import-submit]');
        const spinner = dialog.querySelector('[data-resource-import-spinner]');
        const submitLabel = dialog.querySelector('[data-resource-import-submit-label]');
        const cancel = dialog.querySelector('[data-resource-import-cancel]');
        const close = dialog.querySelector('[data-resource-import-close]');
        let busy = false;
        let validating = false;
        let validationSequence = 0;
        let preflight = null;

        const modes = () => Array.from(dialog.querySelectorAll('[data-resource-import-mode]'));
        const mode = () => modes().find(item => item.checked)?.value || 'create';
        const isMulti = () => Boolean(preflight?.isMultiItem || Number(preflight?.itemCount || 0) > 1);

        function redirectIfUnauthorized(response) {
            if (response?.status !== 401) return false;
            window.location.replace(String(options.loginUrl || window.mauiAppUrl('/Account/SessionExpired')));
            return true;
        }

        function showError(message) {
            if (!error) return;
            error.textContent = message || '';
            error.hidden = !message;
        }

        function setBusy(value) {
            busy = Boolean(value);
            [file, metadataAck, cancel, close, ...modes()].forEach(control => {
                if (control) control.disabled = busy;
            });
            if (submit) submit.disabled = busy || !canSubmit();
            if (spinner) spinner.hidden = !busy;
            if (submitLabel) submitLabel.textContent = busy ? (mode() === 'overwrite' ? 'Updating…' : 'Importing…') : (mode() === 'overwrite' ? `Update ${pluralLabel}` : `Import ${pluralLabel}`);
        }

        function canSubmit() {
            if (validating || !preflight || !file?.files?.length || preflight.message) return false;
            if (preflight.metadata?.requiresAcknowledgement && !metadataAck?.checked) return false;
            return true;
        }

        function updateSubmit() {
            if (submit) submit.disabled = busy || !canSubmit();
        }

        function reset() {
            validating = false;
            validationSequence += 1;
            preflight = null;
            showError('');
            if (file) file.value = '';
            if (validation) validation.hidden = true;
            if (validationList) validationList.innerHTML = '';
            if (metadataAckRow) metadataAckRow.hidden = true;
            if (metadataAck) metadataAck.checked = false;
            if (overwriteInfo) overwriteInfo.hidden = true;
            modes().forEach((item, index) => item.checked = index === 0);
            setBusy(false);
            updateSubmit();
        }

        function renderPreflight() {
            if (!preflight) return;
            const multi = isMulti();
            const items = Array.isArray(preflight.items) ? preflight.items : [];
            if (overwriteInfo) overwriteInfo.hidden = mode() !== 'overwrite';

            if (validationList) {
                validationList.replaceChildren();
                const itemNames = items.length
                    ? items.map(item => item?.sourceName || item?.suggestedName || item?.sourceId || options.label)
                    : [preflight.sourceName || preflight.sourceId || options.label];
                const itemErrors = items.filter(item => String(item?.message || '').trim());
                const errorCount = itemErrors.length || (preflight.message ? 1 : 0);
                validationList.appendChild(packageSummaryRow(errorCount ? 'warning' : 'ok', itemNames, preflight.itemCount || itemNames.length, errorCount, mode()));
                if (multi) {
                    validationList.appendChild(validationRow('ok', `Plan: ${preflight.createCount || 0} create; ${preflight.overwriteCount || 0} update.`));
                }
                itemErrors.forEach(item => validationList.appendChild(validationRow('error', item.message)));
                if (!itemErrors.length && preflight.message) validationList.appendChild(validationRow('error', preflight.message));
                if (!multi) {
                    const item = items[0];
                    if (mode() === 'overwrite') {
                        if (item?.targetId && !item?.message) validationList.appendChild(validationRow('ok', `Same-name ${options.label} '${preflight.sourceName}' matched (ID ${item.targetId})`));
                    } else if (!preflight.message) {
                        validationList.appendChild(validationRow('ok', `${options.label} name '${preflight.sourceName}' will be used`));
                    }
                }
                const metadata = preflight.metadata || {};
                const issues = Array.isArray(metadata.issues) ? metadata.issues : [];
                validationList.appendChild(validationRow(issues.length ? 'warning' : 'ok',
                    `${metadata.compatibleFields || 0} of ${metadata.exportedFields || 0} MAUI custom metadata fields are compatible.`));
                issues.forEach(issue => validationList.appendChild(validationRow('warning', issue)));
            }
            if (validation) validation.hidden = false;
            const metadata = preflight.metadata || {};
            if (metadataAckRow) metadataAckRow.hidden = !metadata.requiresAcknowledgement;
            if (!metadata.requiresAcknowledgement && metadataAck) metadataAck.checked = false;
            updateSubmit();
        }

        async function runPreflight() {
            const selectedFile = file?.files?.[0];
            const currentValidation = ++validationSequence;
            preflight = null;
            validating = Boolean(selectedFile);
            updateSubmit();
            if (!selectedFile) {
                validating = false;
                if (validation) validation.hidden = true;
                return;
            }
            showError('');
            if (metadataAckRow) metadataAckRow.hidden = true;
            if (metadataAck) metadataAck.checked = false;
            if (validationList) {
                validationList.replaceChildren(validationProgressRow());
            }
            if (validation) validation.hidden = false;
            const data = new FormData();
            data.append('File', selectedFile);
            data.append('Mode', mode());
            try {
                const response = await fetch(options.preflightUrl, { method: 'POST', headers: { RequestVerificationToken: token() }, body: data });
                if (redirectIfUnauthorized(response)) return;
                if (!response.ok) throw await errorFrom(response);
                const payload = await response.json();
                if (currentValidation !== validationSequence) return;
                preflight = payload.result;
                renderPreflight();
            } catch (e) {
                if (currentValidation !== validationSequence) return;
                preflight = null;
                if (validation) validation.hidden = true;
                if (metadataAckRow) metadataAckRow.hidden = true;
                if (metadataAck) metadataAck.checked = false;
                showError(e.message || `Unable to validate the ${options.label} export.`);
            } finally {
                if (currentValidation === validationSequence) {
                    validating = false;
                    updateSubmit();
                }
            }
        }

        function updateMode() {
            preflight = null;
            if (overwriteInfo) overwriteInfo.hidden = mode() !== 'overwrite';
            if (submitLabel && !busy) submitLabel.textContent = mode() === 'overwrite' ? `Update ${pluralLabel}` : `Import ${pluralLabel}`;
            updateSubmit();
            if (file?.files?.length) runPreflight();
        }

        function multiFailureMessage(data) {
            const failures = (data?.items || []).filter(item => !item.success);
            const details = failures.slice(0, 5).map(item => `${item.sourceName || 'Item'}: ${item.error || 'Import failed.'}`);
            const suffix = failures.length > 5 ? ` (+${failures.length - 5} more)` : '';
            return `Imported ${data?.importedCount || 0} of ${data?.itemCount || 0} ${pluralLabel}. ${details.join(' | ')}${suffix}`;
        }

        async function submitImport() {
            if (busy || !canSubmit()) return;
            const multi = isMulti();
            const popup = multi ? null : (window.MauiNativeEditor?.reserve?.(`MAUI ${options.label} Editor - Imported ${options.label}`) || null);
            if (!multi && !popup) {
                showError('The MOVEit native editor popup was blocked by the browser.');
                return;
            }
            setBusy(true);
            showError('');
            const data = new FormData();
            data.append('File', file.files[0]);
            data.append('Mode', mode());
            data.append('AcknowledgeUnsupportedMetadata', metadataAck?.checked ? 'true' : 'false');
            try {
                const response = await fetch(options.importUrl, { method: 'POST', headers: { RequestVerificationToken: token() }, body: data });
                if (redirectIfUnauthorized(response)) return;
                if (!response.ok) throw await errorFrom(response);
                const result = await response.json();
                if (multi) {
                    document.dispatchEvent(new CustomEvent('maui:resource-imported', { detail: { key: options.key, bulk: true } }));
                    if (Number(result.failedCount || 0) > 0) {
                        showError(multiFailureMessage(result));
                    } else {
                        dialog.close();
                        options.status?.(`Imported ${result.importedCount || result.itemCount || 0} ${pluralLabel}: ${result.createdCount || 0} created, ${result.overwrittenCount || 0} updated.`, 5000);
                    }
                    return;
                }

                dialog.close();
                options.status?.(`${options.label} ${result.name || preflight?.sourceName || ''} ${result.created ? 'imported' : 'updated'}.${result.warning ? ` ${result.warning}` : ''}`, 5000);
                document.dispatchEvent(new CustomEvent('maui:resource-imported', {
                    detail: { key: options.key, id: result.id, name: result.name, created: result.created, warning: result.warning, popup }
                }));
            } catch (e) {
                try { popup?.close(); } catch { /* ignored */ }
                showError(e.message || `Unable to import the ${options.label}.`);
            } finally {
                setBusy(false);
                updateSubmit();
            }
        }

        file?.addEventListener('change', runPreflight);
        modes().forEach(item => item.addEventListener('change', updateMode));
        metadataAck?.addEventListener('change', updateSubmit);
        form?.addEventListener('submit', event => { event.preventDefault(); submitImport(); });
        cancel?.addEventListener('click', () => { if (!busy) dialog.close(); });
        close?.addEventListener('click', () => { if (!busy) dialog.close(); });
        dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
        dialog.addEventListener('close', reset);

        async function exportItem(id, displayName) {
            try {
                const url = new URL(options.exportUrl, window.location.href);
                url.searchParams.set(options.exportIdParameter, id);
                const response = await fetch(url, { headers: { Accept: 'application/zip, application/octet-stream' }, cache: 'no-store' });
                if (redirectIfUnauthorized(response)) return;
                if (!response.ok) throw await errorFrom(response);
                await downloadResponse(response, `${displayName || options.label}.maui`);
                options.status?.(`Export ${options.label}: ${displayName || id} downloaded.`, 5000);
            } catch (e) {
                options.status?.(`Export ${options.label}: ${e.message || 'Unable to export.'}`, 5000);
            }
        }

        async function exportSelected(ids) {
            const selectedIds = Array.from(new Set((ids || []).map(id => String(id || '').trim()).filter(Boolean)));
            if (!selectedIds.length) return;
            try {
                const body = {};
                body[options.bulkRequestProperty] = selectedIds;
                const response = await fetch(options.bulkExportUrl, {
                    method: 'POST',
                    headers: { Accept: 'application/zip, application/octet-stream', 'Content-Type': 'application/json', RequestVerificationToken: token() },
                    body: JSON.stringify(body)
                });
                if (redirectIfUnauthorized(response)) return;
                if (!response.ok) throw await errorFrom(response);
                await downloadResponse(response, `${pluralLabel.replace(/\s+/g, '_')}_${selectedIds.length}.maui`);
                options.status?.(`Exported ${selectedIds.length} ${pluralLabel}.`, 5000);
            } catch (e) {
                options.status?.(`Export ${pluralLabel}: ${e.message || 'Unable to export selected items.'}`, 5000);
            }
        }

        return {
            openImport() { reset(); dialog.showModal(); },
            exportItem,
            exportSelected
        };
    }

    window.MauiResourcePortability = { create };
})();
