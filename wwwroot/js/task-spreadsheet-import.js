/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Task CSV/XLSX import workflow. Intentionally separate from native .maui portability code.
 */
(() => {
    'use strict';

    function create(options) {
        const previewUrl = String(options?.previewUrl || '').trim();
        const analyzeUrl = String(options?.analyzeUrl || '').trim();
        const importRowUrl = String(options?.importRowUrl || '').trim();
        const antiForgeryToken = String(options?.antiForgeryToken || '');
        const loginUrl = String(options?.loginUrl || window.mauiAppUrl('/Account/SessionExpired'));
        const nativePortability = options?.nativePortability || null;
        const configuredLimit = Number(options?.importRowLimit);
        const rowLimit = Number.isInteger(configuredLimit) && configuredLimit >= 1 && configuredLimit <= 1000 ? configuredLimit : 500;
        const refresh = typeof options?.refresh === 'function' ? options.refresh : async () => {};
        const status = typeof options?.status === 'function' ? options.status : () => {};
        if (!previewUrl || !analyzeUrl || !importRowUrl) return null;

        const dialog = document.querySelector('[data-native-portability-import-dialog]');
        const nativePanel = dialog?.querySelector('[data-native-portability-import-panel]');
        const nativeAction = dialog?.querySelector('[data-native-portability-import-action]');
        const panel = dialog?.querySelector('[data-task-spreadsheet-import-panel]');
        const file = dialog?.querySelector('[data-task-spreadsheet-file]');
        const prefix = dialog?.querySelector('[data-task-spreadsheet-prefix]');
        const updateExisting = dialog?.querySelector('[data-task-spreadsheet-update-existing]');
        const leaveDisabled = dialog?.querySelector('[data-task-spreadsheet-leave-disabled]');
        const action = dialog?.querySelector('[data-task-spreadsheet-action]');
        const error = dialog?.querySelector('[data-native-portability-import-error]');
        const analysisPanel = dialog?.querySelector('[data-task-spreadsheet-analysis]');
        const summary = dialog?.querySelector('[data-task-spreadsheet-summary]');
        const gridHead = dialog?.querySelector('[data-task-spreadsheet-grid-head]');
        const gridBody = dialog?.querySelector('[data-task-spreadsheet-grid-body]');
        const pagination = dialog?.querySelector('[data-task-spreadsheet-grid-pagination]');
        const pageLabel = dialog?.querySelector('[data-task-spreadsheet-page-label]');
        const prevPage = dialog?.querySelector('[data-task-spreadsheet-page-prev]');
        const nextPage = dialog?.querySelector('[data-task-spreadsheet-page-next]');
        const resultsPanel = dialog?.querySelector('[data-task-spreadsheet-results-panel]');
        const results = dialog?.querySelector('[data-task-spreadsheet-results]');
        const progress = dialog?.querySelector('[data-task-spreadsheet-progress]');
        const progressFill = dialog?.querySelector('[data-task-spreadsheet-progress-fill]');
        const progressText = dialog?.querySelector('[data-task-spreadsheet-progress-text]');
        const modeInputs = dialog?.querySelectorAll('input[name="taskImportMode"]') || [];
        const cancelButtons = Array.from(dialog?.querySelectorAll('[data-native-portability-import-cancel]') || []);
        if (!dialog || !panel || !file || !action) return null;

        let preview = null;
        let analysis = null;
        let busy = false;
        let pageIndex = 0;
        // Outcomes are maintained separately from validation so switching preview pages is lossless.
        const rowOutcomes = new Map();

        function selectedMode() {
            return dialog.querySelector('input[name="taskImportMode"]:checked')?.value || 'portability';
        }

        function showError(message) {
            if (!error) return;
            error.textContent = String(message || 'The operation could not be completed.');
            error.hidden = false;
        }

        function clearError() {
            if (!error) return;
            error.textContent = '';
            error.hidden = true;
        }

        function clearGrid() {
            if (gridHead) gridHead.replaceChildren();
            if (gridBody) gridBody.replaceChildren();
            if (pagination) pagination.hidden = true;
        }

        function importRows() {
            return Array.isArray(analysis?.rows) ? analysis.rows : [];
        }

        function pendingRows() {
            return importRows().filter(row => !rowOutcomes.has(row.rowNumber));
        }

        function updatePaging(totalRows) {
            const pages = Math.max(1, Math.ceil(totalRows / rowLimit));
            pageIndex = Math.max(0, Math.min(pageIndex, pages - 1));
            if (pagination) pagination.hidden = totalRows <= rowLimit;
            if (pageLabel) {
                const first = totalRows ? pageIndex * rowLimit + 1 : 0;
                const last = Math.min(totalRows, (pageIndex + 1) * rowLimit);
                pageLabel.textContent = `Rows ${first}–${last} of ${totalRows} · ${rowLimit} per page`;
            }
            if (prevPage) prevPage.disabled = busy || pageIndex === 0;
            if (nextPage) nextPage.disabled = busy || pageIndex >= pages - 1;
        }

        function changePage(delta) {
            if (busy) return;
            pageIndex += delta;
            if (analysis) renderGrid(analysis.columns, analysis.rows, true);
            else if (preview) renderGrid(preview.columns, preview.rows, false);
            const gridFrame = dialog.querySelector('.task-spreadsheet-grid-frame');
            if (gridFrame) gridFrame.scrollTop = 0;
        }

        function clearResults() {
            if (results) results.replaceChildren();
            if (resultsPanel) resultsPanel.hidden = true;
        }

        function resetProgress() {
            if (progress) progress.hidden = true;
            setProgress(0, 0);
        }

        function resetAnalysis(preservePreview = true) {
            analysis = null;
            rowOutcomes.clear();
            pageIndex = 0;
            if (summary) summary.replaceChildren();
            clearResults();
            resetProgress();
            if (preservePreview && preview) {
                renderPreview();
            } else {
                if (analysisPanel) analysisPanel.hidden = true;
                clearGrid();
            }
            updateActionState();
        }

        function resetSpreadsheet() {
            preview = null;
            if (file) file.value = '';
            if (prefix) prefix.value = '';
            if (updateExisting) updateExisting.checked = false;
            if (leaveDisabled) leaveDisabled.checked = true;
            clearError();
            resetAnalysis(false);
        }

        function setMode(mode) {
            const spreadsheet = mode === 'spreadsheet';
            if (nativePanel) nativePanel.hidden = spreadsheet;
            if (nativeAction) nativeAction.hidden = spreadsheet;
            panel.hidden = !spreadsheet;
            action.hidden = !spreadsheet;
            if (progress) progress.hidden = !(spreadsheet && rowOutcomes.size > 0);
            clearError();
            updateActionState();
        }

        function updateActionState() {
            if (!action) return;
            // Only fully attempted imports are complete, including failed/unverified rows.
            // Derive the label on every state update so paging/refresh cannot reset it.
            const complete = selectedMode() === 'spreadsheet' && analysis?.canImport && pendingRows().length === 0;
            cancelButtons.forEach(button => { button.textContent = complete ? 'Close' : 'Cancel'; });
            if (busy) {
                action.disabled = true;
                return;
            }
            if (analysis?.canImport) {
                const remaining = pendingRows().length;
                const nextBatch = Math.min(remaining, rowLimit);
                action.textContent = remaining === 0
                    ? (Array.from(rowOutcomes.values()).some(outcome => outcome === 'Failed') ? 'Import Finished (with failures)'
                        : (Array.from(rowOutcomes.values()).some(outcome => outcome === 'Unverified') ? 'Import Finished (review unverified)' : 'Import Complete'))
                    : rowOutcomes.size ? `Import Next ${nextBatch} Task${nextBatch === 1 ? '' : 's'}`
                        : (remaining > rowLimit ? `Import First ${nextBatch} Tasks` : 'Import Tasks');
                action.disabled = remaining === 0;
            } else {
                action.textContent = 'Analyze File';
                action.disabled = !(file?.files?.length > 0 && preview);
            }
        }

        function setBusy(value, label) {
            busy = Boolean(value);
            [file, prefix, updateExisting, leaveDisabled].forEach(control => { if (control) control.disabled = busy; });
            modeInputs.forEach(control => { control.disabled = busy; });
            dialog.querySelectorAll('[data-native-portability-import-cancel], [data-native-portability-import-close]').forEach(control => { control.disabled = busy; });
            if (action) {
                action.disabled = busy;
                if (busy && label) action.textContent = label;
            }
            updateActionState();
            const visibleCount = Array.isArray(analysis?.rows) ? analysis.rows.length : Array.isArray(preview?.rows) ? preview.rows.length : 0;
            updatePaging(visibleCount);
        }

        async function requestJson(url, init) {
            const response = await fetch(url, init);
            if (response.status === 401) {
                window.location.assign(loginUrl);
                throw new Error('Your session has expired.');
            }
            const contentType = response.headers.get('content-type') || '';
            const payload = contentType.includes('application/json') ? await response.json() : null;
            if (!response.ok) throw new Error(payload?.message || `Request failed (${response.status}).`);
            return payload;
        }

        async function loadPreview() {
            if (busy) return;
            clearError();
            preview = null;
            analysis = null;
            pageIndex = 0;
            rowOutcomes.clear();
            if (summary) summary.replaceChildren();
            clearGrid();
            clearResults();
            resetProgress();
            if (analysisPanel) analysisPanel.hidden = true;
            updateActionState();
            if (!file?.files?.length) return;

            setBusy(true, 'Loading...');
            try {
                const formData = new FormData();
                formData.append('File', file.files[0]);
                const payload = await requestJson(previewUrl, {
                    method: 'POST',
                    headers: { 'Accept': 'application/json', 'RequestVerificationToken': antiForgeryToken },
                    body: formData
                });
                preview = payload?.result || null;
                if (!preview) throw new Error('MAUI did not return Task spreadsheet preview data.');
                renderPreview();
            } catch (err) {
                preview = null;
                if (analysisPanel) analysisPanel.hidden = true;
                clearGrid();
                showError(err?.message || 'Unable to load the Task spreadsheet preview.');
            } finally {
                setBusy(false);
            }
        }

        async function analyzeFile() {
            if (busy || !file?.files?.length || !preview) return;
            clearError();
            resetAnalysis(true);
            setBusy(true, 'Analyzing...');
            try {
                const formData = new FormData();
                formData.append('File', file.files[0]);
                formData.append('TaskNamePrefix', prefix?.value || '');
                formData.append('UpdateExisting', updateExisting?.checked ? 'true' : 'false');
                formData.append('LeaveTasksDisabled', leaveDisabled?.checked ? 'true' : 'false');
                const payload = await requestJson(analyzeUrl, {
                    method: 'POST',
                    headers: { 'Accept': 'application/json', 'RequestVerificationToken': antiForgeryToken },
                    body: formData
                });
                analysis = payload?.result || null;
                if (!analysis) throw new Error('MAUI did not return Task spreadsheet analysis results.');
                renderAnalysis();
            } catch (err) {
                analysis = null;
                renderPreview();
                showError(err?.message || 'Unable to analyze the Task spreadsheet.');
            } finally {
                setBusy(false);
            }
        }

        function renderPreview() {
            if (!analysisPanel || !preview) return;
            analysisPanel.hidden = false;
            if (summary) {
                summary.replaceChildren();
                const note = document.createElement('span');
                note.className = 'task-spreadsheet-summary-note';
                const rowCount = Array.isArray(preview.rows) ? preview.rows.length : 0;
                note.textContent = `File loaded: ${rowCount} data row${rowCount === 1 ? '' : 's'}. Select Analyze File to validate and normalize the import.`;
                summary.appendChild(note);
                if ((preview.skippedBlankRows || 0) > 0) {
                    const skipped = document.createElement('span');
                    skipped.className = 'task-spreadsheet-summary-note';
                    skipped.textContent = `${preview.skippedBlankRows} blank row${preview.skippedBlankRows === 1 ? '' : 's'} skipped.`;
                    summary.appendChild(skipped);
                }
            }
            renderGrid(preview.columns, preview.rows, false);
            clearResults();
        }

        function renderAnalysis() {
            if (!analysisPanel || !analysis) return;
            analysisPanel.hidden = false;
            renderSummary();
            renderGrid(analysis.columns, analysis.rows, true);
            renderValidationResults();
            updateActionState();
        }

        function renderSummary() {
            if (!summary) return;
            summary.replaceChildren();
            const counts = [
                ['Valid', analysis.validRows || 0, 'valid'],
                ['Warnings', analysis.warningRows || 0, 'warning'],
                ['Errors', analysis.errorRows || 0, 'error']
            ];
            counts.forEach(([label, count, kind]) => {
                const pill = document.createElement('span');
                pill.className = `task-spreadsheet-summary-pill ${kind}`;
                pill.textContent = `${label}: ${count}`;
                summary.appendChild(pill);
            });
            if (rowOutcomes.size > 0) {
                ['Created', 'Updated', 'Failed', 'Unverified'].forEach(outcome => {
                    const count = Array.from(rowOutcomes.values()).filter(value => value === outcome).length;
                    if (!count) return;
                    const pill = document.createElement('span');
                    pill.className = `task-spreadsheet-summary-pill ${outcome === 'Failed' ? 'error' : outcome === 'Unverified' ? 'warning' : 'valid'}`;
                    pill.textContent = `${outcome}: ${count}`;
                    summary.appendChild(pill);
                });
            }
            if ((analysis.ignoredColumns || 0) > 0) {
                const text = document.createElement('span');
                text.className = 'task-spreadsheet-summary-note';
                text.textContent = `${analysis.ignoredColumns} unmatched column${analysis.ignoredColumns === 1 ? '' : 's'} ignored.`;
                summary.appendChild(text);
            }
        }

        function renderGrid(columnsValue, rowsValue, analyzed) {
            if (!gridHead || !gridBody) return;
            gridHead.replaceChildren();
            gridBody.replaceChildren();
            const columns = Array.isArray(columnsValue) ? columnsValue : [];
            const rows = Array.isArray(rowsValue) ? rowsValue : [];
            updatePaging(rows.length);
            const headerRow = document.createElement('tr');
            ['Row', 'Status', ...columns].forEach(name => {
                const th = document.createElement('th');
                th.textContent = name;
                headerRow.appendChild(th);
            });
            gridHead.appendChild(headerRow);
            // The full preview/analysis stays in memory; only the current page has DOM nodes.
            const first = pageIndex * rowLimit;
            const fragment = document.createDocumentFragment();
            rows.slice(first, first + rowLimit).forEach(row => {
                const tr = document.createElement('tr');
                tr.dataset.spreadsheetRowNumber = String(row.rowNumber);
                const rowCell = document.createElement('td');
                rowCell.textContent = String(row.rowNumber || '');
                tr.appendChild(rowCell);
                const statusCell = document.createElement('td');
                const pill = document.createElement('span');
                const rowStatus = analyzed ? rowOutcomes.get(row.rowNumber) || String(row.status || 'Valid') : 'Not analyzed';
                pill.className = `task-spreadsheet-row-pill ${analyzed ? (rowStatus === 'Unverified' ? 'warning' : rowStatus.toLowerCase()) : 'pending'}`;
                pill.textContent = rowStatus;
                statusCell.appendChild(pill);
                tr.appendChild(statusCell);
                columns.forEach(column => {
                    const td = document.createElement('td');
                    td.textContent = row.values?.[column] ?? '';
                    tr.appendChild(td);
                });
                fragment.appendChild(tr);
            });
            gridBody.appendChild(fragment);
            window.MauiModalUI?.sizeTableViewport?.(dialog.querySelector('.task-spreadsheet-grid-frame'), 5);
        }

        function updateGridOutcome(rowNumber, outcome) {
            if (outcome !== 'Importing') rowOutcomes.set(rowNumber, outcome);
            // Update only the currently visible row; other pages read rowOutcomes on render.
            const visibleRow = [...(gridBody?.querySelectorAll('tr') || [])]
                .find(row => row.dataset.spreadsheetRowNumber === String(rowNumber));
            const pill = visibleRow?.querySelector('.task-spreadsheet-row-pill');
            if (pill) {
                pill.className = `task-spreadsheet-row-pill ${outcome.toLowerCase()}`;
                pill.textContent = outcome;
            }
        }

        function renderValidationResults() {
            if (!results || !resultsPanel) return;
            results.replaceChildren();
            resultsPanel.hidden = false;
            const list = Array.isArray(analysis?.issues) ? analysis.issues : [];
            if (list.length === 0) {
                const row = document.createElement('div');
                row.className = 'task-spreadsheet-result-neutral';
                row.textContent = 'No validation errors or warnings. The analyzed rows are ready to import.';
                results.appendChild(row);
                return;
            }
            list.forEach(issue => {
                const row = document.createElement('div');
                const severity = String(issue.severity || 'Warning').toLowerCase();
                row.className = `task-spreadsheet-issue ${severity}`;
                const location = issue.rowNumber ? `Row ${issue.rowNumber}${issue.column ? ` · ${issue.column}` : ''}` : (issue.column || 'File');
                const strong = document.createElement('strong');
                strong.textContent = location;
                row.appendChild(strong);
                const message = document.createElement('span');
                message.className = 'task-spreadsheet-issue-message';
                message.textContent = issue.message || '';
                row.appendChild(message);
                results.appendChild(row);
            });
        }

        async function importTasks() {
            if (busy || !analysis?.canImport) return;
            const batch = pendingRows().slice(0, rowLimit);
            if (!batch.length) return;
            clearError();
            setBusy(true, 'Importing...');
            if (resultsPanel) resultsPanel.hidden = false;
            // Keep prior batch results visible so operators can review every operation.
            if (progress) progress.hidden = false;
            let succeeded = 0;
            let failed = 0;
            let unverified = 0;
            setProgress(rowOutcomes.size, importRows().length);
            try {
                for (const row of batch) {
                    let item;
                    updateGridOutcome(row.rowNumber, 'Importing');
                    try {
                        const payload = await requestJson(importRowUrl, {
                            method: 'POST',
                            headers: {
                                'Accept': 'application/json',
                                'Content-Type': 'application/json',
                                'RequestVerificationToken': antiForgeryToken
                            },
                            body: JSON.stringify({ analysisId: analysis.analysisId, rowNumber: row.rowNumber })
                        });
                        item = payload?.result;
                        if (!item || !['Created', 'Updated', 'Unverified'].includes(item.outcome) || item.rowNumber !== row.rowNumber)
                            throw new Error('MOVEit returned an unexpected import result. Review the Task before retrying.');
                        if (item.outcome === 'Unverified') unverified++;
                        else succeeded++;
                    } catch (err) {
                        item = { rowNumber: row.rowNumber, taskName: row.values?.TASK_NAME || '', outcome: 'Failed', message: err?.message || 'Import failed.' };
                        failed++;
                    }
                    updateGridOutcome(row.rowNumber, item.outcome);
                    appendResult(item);
                    setProgress(rowOutcomes.size, importRows().length);
                }
                if (succeeded > 0) {
                    try { await refresh(true); } catch { /* grid refresh failure does not change committed import result */ }
                }
                renderSummary();
                const remaining = pendingRows().length;
                const summaryText = `Task spreadsheet batch completed. ${succeeded} succeeded${failed ? `; ${failed} failed` : ''}${unverified ? `; ${unverified} unverified (review MOVEit before retrying)` : ''}.`;
                status(remaining ? `${summaryText} ${remaining} row${remaining === 1 ? '' : 's'} remain.` : summaryText, 7000, failed || unverified ? 'warning' : 'success');
                // Do not discard the analysis: subsequent actions import only the unattempted rows.
            } finally {
                setBusy(false);
            }
        }

        function appendResult(item) {
            if (!results) return;
            const row = document.createElement('div');
            const outcome = String(item?.outcome || 'Failed');
            row.className = `task-spreadsheet-result ${outcome.toLowerCase()}`;
            const pill = document.createElement('span');
            pill.className = `task-spreadsheet-row-pill ${outcome.toLowerCase() === 'failed' ? 'error' : outcome.toLowerCase() === 'unverified' ? 'warning' : 'valid'}`;
            pill.textContent = outcome;
            row.appendChild(pill);
            const text = document.createElement('span');
            text.textContent = `Row ${item?.rowNumber || ''}${item?.taskName ? ` · ${item.taskName}` : ''} — ${item?.message || ''}`;
            row.appendChild(text);
            results.appendChild(row);
            (Array.isArray(item?.warnings) ? item.warnings : []).forEach(warning => {
                const warningRow = document.createElement('div');
                warningRow.className = 'task-spreadsheet-result-warning';
                warningRow.textContent = `Row ${item?.rowNumber || ''} · ${warning}`;
                results.appendChild(warningRow);
            });
        }

        function setProgress(done, total) {
            const percent = total > 0 ? Math.max(0, Math.min(100, Math.round((done / total) * 100))) : 0;
            if (progressFill) progressFill.style.width = `${percent}%`;
            if (progressText) progressText.textContent = total > 0 ? `Import Progress: ${done} of ${total}` : 'Import Progress';
        }

        function invalidateOptions() {
            if (busy) return;
            clearError();
            resetAnalysis(true);
        }

        function open() {
            nativePortability?.openImport?.();
            resetSpreadsheet();
            const portability = dialog.querySelector('input[name="taskImportMode"][value="portability"]');
            if (portability) portability.checked = true;
            setMode('portability');
        }

        modeInputs.forEach(input => input.addEventListener('change', () => setMode(selectedMode())));
        file.addEventListener('change', () => { void loadPreview(); });
        prefix?.addEventListener('input', invalidateOptions);
        prefix?.addEventListener('change', invalidateOptions);
        updateExisting?.addEventListener('change', invalidateOptions);
        leaveDisabled?.addEventListener('change', invalidateOptions);
        action.addEventListener('click', () => { if (analysis?.canImport) void importTasks(); else void analyzeFile(); });
        prevPage?.addEventListener('click', () => changePage(-1));
        nextPage?.addEventListener('click', () => changePage(1));
        dialog.addEventListener('close', () => { if (!busy) resetSpreadsheet(); });
        setMode(selectedMode());
        return { open };
    }

    window.MauiTaskSpreadsheetImport = { create };
})();

