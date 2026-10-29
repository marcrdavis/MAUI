/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Configuration > Metadata Excel import/export workflow.
 */

(() => {
    const workspace = document.querySelector('[data-configuration-workspace]');
    if (!workspace) return;

    const exportUrl = workspace.dataset.metadataExportUrl || '';
    const previewUrl = workspace.dataset.metadataImportPreviewUrl || '';
    const applyUrl = workspace.dataset.metadataImportApplyUrl || '';
    if (!exportUrl || !previewUrl || !applyUrl) return;

    const antiForgeryToken = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const saveBar = workspace.querySelector('.configuration-save-bar');
    const exportType = workspace.querySelector('[data-metadata-export-type]');
    const exportButton = workspace.querySelector('[data-metadata-export-button]');
    const exportStatus = workspace.querySelector('[data-metadata-export-status]');
    const importFile = workspace.querySelector('[data-metadata-import-file]');
    const previewButton = workspace.querySelector('[data-metadata-import-preview]');
    const importStatus = workspace.querySelector('[data-metadata-import-status]');

    const dialog = document.querySelector('[data-metadata-import-preview-dialog]');
    const closeButton = dialog?.querySelector('[data-metadata-import-preview-close]');
    const cancelButton = dialog?.querySelector('[data-metadata-import-preview-cancel]');
    const saveButton = dialog?.querySelector('[data-metadata-import-save]');
    const dialogError = dialog?.querySelector('[data-metadata-import-preview-error]');
    const changeRows = dialog?.querySelector('[data-metadata-preview-change-rows]');
    const noChanges = dialog?.querySelector('[data-metadata-preview-no-changes]');
    const truncated = dialog?.querySelector('[data-metadata-preview-truncated]');
    const errorSection = dialog?.querySelector('[data-metadata-preview-errors]');
    const warningSection = dialog?.querySelector('[data-metadata-preview-warnings]');
    const errorList = dialog?.querySelector('[data-metadata-preview-error-list]');
    const warningList = dialog?.querySelector('[data-metadata-preview-warning-list]');

    let selectedFile = null;
    let currentPreview = null;
    let busy = false;

    function hasUnsavedConfigurationChanges() {
        return Boolean(saveBar?.classList.contains('is-dirty'));
    }

    function setStatus(element, message, isError = false) {
        if (!element) return;
        element.textContent = message || '';
        element.classList.toggle('error', Boolean(message && isError));
    }

    function showToast(message, kind = 'default') {
        const toast = document.querySelector('[data-configuration-toast]');
        if (typeof window.mauiDisplayToast === 'function' && toast) {
            window.mauiDisplayToast(toast, message, kind, 5000);
        }
    }

    function requireSavedSettings(statusElement) {
        if (!hasUnsavedConfigurationChanges()) return true;
        setStatus(statusElement, 'Save or discard pending Settings changes before importing or exporting metadata.', true);
        return false;
    }

    async function responseMessage(response) {
        try {
            const payload = await response.json();
            return payload?.message || `Request failed (${response.status}).`;
        } catch {
            return `Request failed (${response.status}).`;
        }
    }

    function parseDownloadFileName(header, fallback) {
        const value = String(header || '');
        const utf = value.match(/filename\*=UTF-8''([^;]+)/i);
        if (utf?.[1]) {
            try { return decodeURIComponent(utf[1].replace(/^"|"$/g, '')); } catch { /* use fallback parser */ }
        }
        const basic = value.match(/filename="?([^";]+)"?/i);
        return basic?.[1]?.trim() || fallback;
    }

    function downloadBlob(blob, fileName) {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        window.setTimeout(() => URL.revokeObjectURL(url), 0);
    }

    async function exportMetadata() {
        if (busy || !requireSavedSettings(exportStatus)) return;
        busy = true;
        if (exportButton) exportButton.disabled = true;
        if (exportType) exportType.disabled = true;
        setStatus(exportStatus, 'Preparing workbook…');
        try {
            const objectType = String(exportType?.value || 'Task');
            const response = await fetch(`${exportUrl}?objectType=${encodeURIComponent(objectType)}`, {
                method: 'GET',
                credentials: 'same-origin',
                cache: 'no-store'
            });
            if (!response.ok) throw new Error(await responseMessage(response));
            const blob = await response.blob();
            const typeLabel = String(exportType?.selectedOptions?.[0]?.textContent || objectType).trim().replace(/[^A-Za-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
            const fallback = `MAUI_${typeLabel || 'Metadata'}_Metadata.xlsx`;
            downloadBlob(blob, parseDownloadFileName(response.headers.get('Content-Disposition'), fallback));
            setStatus(exportStatus, 'Workbook exported.');
        } catch (error) {
            setStatus(exportStatus, error?.message || 'Unable to export metadata.', true);
        } finally {
            busy = false;
            if (exportButton) exportButton.disabled = false;
            if (exportType) exportType.disabled = false;
        }
    }

    function uploadForm(file) {
        const form = new FormData();
        form.append('file', file, file.name);
        return form;
    }

    async function postWorkbook(url, file) {
        const response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {},
            body: uploadForm(file)
        });
        if (!response.ok) throw new Error(await responseMessage(response));
        return await response.json();
    }

    function setText(selector, value) {
        const element = dialog?.querySelector(selector);
        if (element) element.textContent = String(value ?? '');
    }

    function displayValue(value, clearsValue = false) {
        if (clearsValue) return '(clear)';
        const text = String(value ?? '');
        return text.length ? text : '—';
    }

    function renderIssues(section, list, issues) {
        if (!section || !list) return;
        const values = Array.isArray(issues) ? issues : [];
        section.hidden = values.length === 0;
        const fragment = document.createDocumentFragment();
        values.forEach(issue => {
            const row = document.createElement('div');
            row.className = 'metadata-import-issue-row';
            const rowPrefix = issue?.rowNumber ? `Row ${issue.rowNumber}: ` : '';
            row.textContent = `${rowPrefix}${issue?.message || 'Validation issue.'}`;
            fragment.appendChild(row);
        });
        list.replaceChildren(fragment);
    }

    function renderPreview(preview) {
        currentPreview = preview;
        if (dialogError) {
            dialogError.textContent = '';
            dialogError.hidden = true;
        }

        setText('[data-metadata-preview-file]', preview?.fileName || selectedFile?.name || '—');
        setText('[data-metadata-preview-type]', preview?.objectTypeLabel || preview?.objectType || '—');
        setText('[data-metadata-preview-environment]', preview?.environmentKey || '—');
        setText('[data-metadata-preview-rows]', preview?.rowsRead ?? 0);
        setText('[data-metadata-preview-matched]', preview?.matchedRows ?? 0);
        setText('[data-metadata-preview-objects]', preview?.objectsWithChanges ?? 0);
        setText('[data-metadata-preview-changes]', preview?.changeCount ?? 0);
        setText('[data-metadata-preview-unchanged]', preview?.unchangedRows ?? 0);

        renderIssues(errorSection, errorList, preview?.errors);
        renderIssues(warningSection, warningList, preview?.warnings);

        const changes = Array.isArray(preview?.changes) ? preview.changes : [];
        const fragment = document.createDocumentFragment();
        changes.forEach(change => {
            const row = document.createElement('tr');
            const object = document.createElement('td');
            const objectName = document.createElement('strong');
            objectName.textContent = change?.objectName || change?.objectId || '—';
            object.appendChild(objectName);
            if (change?.objectId) {
                const objectId = document.createElement('small');
                objectId.textContent = `ID ${change.objectId}`;
                object.appendChild(objectId);
            }
            const field = document.createElement('td');
            field.textContent = change?.fieldLabel || change?.fieldName || '—';
            const current = document.createElement('td');
            current.textContent = displayValue(change?.currentValue);
            const next = document.createElement('td');
            next.textContent = displayValue(change?.newValue, Boolean(change?.clearsValue));
            if (change?.clearsValue) next.classList.add('metadata-import-clear-value');
            row.append(object, field, current, next);
            fragment.appendChild(row);
        });
        changeRows?.replaceChildren(fragment);

        if (noChanges) noChanges.hidden = changes.length > 0 || Number(preview?.changeCount || 0) > 0;
        if (truncated) {
            truncated.hidden = !preview?.changesTruncated;
            truncated.textContent = preview?.changesTruncated ? `Showing the first ${changes.length.toLocaleString()} changes.` : '';
        }

        const valid = preview?.success === true && (!Array.isArray(preview?.errors) || preview.errors.length === 0);
        if (saveButton) saveButton.disabled = !valid || Number(preview?.changeCount || 0) === 0;
        dialog?.showModal();
    }

    async function previewImport() {
        if (busy || !selectedFile || !requireSavedSettings(importStatus)) return;
        busy = true;
        if (previewButton) previewButton.disabled = true;
        if (importFile) importFile.disabled = true;
        setStatus(importStatus, 'Validating workbook…');
        try {
            const preview = await postWorkbook(previewUrl, selectedFile);
            renderPreview(preview);
            const errors = Array.isArray(preview?.errors) ? preview.errors.length : 0;
            setStatus(importStatus,
                errors > 0 ? `${errors} validation error${errors === 1 ? '' : 's'} found.` : 'Workbook validated. Review the preview.',
                errors > 0);
        } catch (error) {
            setStatus(importStatus, error?.message || 'Unable to validate metadata workbook.', true);
        } finally {
            busy = false;
            if (importFile) importFile.disabled = false;
            if (previewButton) previewButton.disabled = !selectedFile;
        }
    }

    function showDialogError(message) {
        if (!dialogError) return;
        dialogError.textContent = message || 'Unable to save metadata import.';
        dialogError.hidden = false;
    }

    async function applyImport() {
        if (busy || !selectedFile || !currentPreview || saveButton?.disabled) return;
        if (!requireSavedSettings(importStatus)) {
            dialog?.close();
            return;
        }

        busy = true;
        if (saveButton) saveButton.disabled = true;
        if (cancelButton) cancelButton.disabled = true;
        if (closeButton) closeButton.disabled = true;
        if (dialogError) dialogError.hidden = true;

        try {
            const result = await postWorkbook(applyUrl, selectedFile);
            dialog?.close();
            const message = result?.message || 'Metadata import saved.';
            setStatus(importStatus, message);
            showToast(message);
            selectedFile = null;
            currentPreview = null;
            if (importFile) importFile.value = '';
            if (previewButton) previewButton.disabled = true;
        } catch (error) {
            showDialogError(error?.message || 'Unable to save metadata import.');
            if (saveButton) saveButton.disabled = false;
        } finally {
            busy = false;
            if (cancelButton) cancelButton.disabled = false;
            if (closeButton) closeButton.disabled = false;
        }
    }

    function closePreview() {
        if (busy) return;
        dialog?.close();
    }

    exportButton?.addEventListener('click', exportMetadata);
    importFile?.addEventListener('change', () => {
        selectedFile = importFile.files?.[0] || null;
        currentPreview = null;
        if (previewButton) previewButton.disabled = !selectedFile;
        setStatus(importStatus, selectedFile ? selectedFile.name : '');
    });
    previewButton?.addEventListener('click', previewImport);
    saveButton?.addEventListener('click', applyImport);
    closeButton?.addEventListener('click', closePreview);
    cancelButton?.addEventListener('click', closePreview);
    dialog?.addEventListener('cancel', event => {
        if (busy) event.preventDefault();
    });
})();
