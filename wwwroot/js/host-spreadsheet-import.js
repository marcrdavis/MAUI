/* MAUI Host CSV/XLSX import — reuses Task Import modal/grid visual language. */
(() => {
    'use strict';
    function create(options) {
        // Hosts and Tasks share one Import Source dialog. Each spreadsheet feature owns
        // only its panel and actions; native portability owns the dialog lifecycle.
        const dialog = document.querySelector('[data-native-portability-import-dialog]');
        const panel = dialog?.querySelector('[data-host-spreadsheet-import-panel]');
        const nativePanel = dialog?.querySelector('[data-native-portability-import-panel]');
        const nativeAction = dialog?.querySelector('[data-native-portability-import-action]');
        const modeInputs = dialog?.querySelectorAll('input[name="hostImportMode"]') || [];
        if (!dialog || !panel || !modeInputs.length) return null;
        const get = attr => dialog.querySelector(`[data-host-spreadsheet-${attr}]`);
        const file = get('file'), prefix = get('prefix'), action = get('action'), updateExisting = get('update-existing');
        // Share the established error surface with Task Import and native portability.
        const error = dialog.querySelector('[data-native-portability-import-error]');
        const analysis = get('analysis'), summary = get('summary'), head = get('head'), body = get('body');
        const pagination = get('pagination'), pageLabel = get('page-label'), prev = get('prev'), next = get('next');
        const results = get('results'), resultsPanel = get('results-panel');
        const progress = get('progress'), progressText = get('progress-text'), progressFill = get('progress-fill');
        // Do not let an incomplete Import modal prevent the Hosts grid from loading.
        const required = {
            nativePanel, nativeAction, file, prefix, action, error, analysis, summary,
            head, body, pagination, pageLabel, prev, next, results, resultsPanel,
            progress, progressText, progressFill, updateExisting, testAfter: get('test-after')
        };
        const missing = Object.entries(required).filter(([, element]) => !element).map(([name]) => name);
        if (missing.length) {
            console.error('Host spreadsheet import disabled: missing dialog controls:', missing.join(', '));
            return null;
        }
        const requestedLimit = Number(options.rowLimit);
        const pageSize = Number.isInteger(requestedLimit) && requestedLimit >= 1 && requestedLimit <= 1000 ? requestedLimit : 500;
        let active = false, analyzed = null, preview = null, page = 0, busy = false;
        const outcomes = new Map();
        const token = String(options.antiForgeryToken || '');
        function errorMessage(message) { error.textContent = String(message || 'Host import failed.'); error.hidden = false; }
        function clearError() { error.hidden = true; error.textContent = ''; }
        function pendingRows() {
            return Array.isArray(analyzed?.rows)
                ? analyzed.rows.filter(row => row.status === 'Valid' && !outcomes.has(row.rowNumber))
                : [];
        }
        function updateActionState() {
            if (busy) { action.disabled = true; return; }
            if (analyzed) {
                const remaining = pendingRows().length;
                if (remaining > 0) {
                    const batch = Math.min(remaining, pageSize);
                    action.textContent = outcomes.size
                        ? `Import Next ${batch} Host${batch === 1 ? '' : 's'}`
                        : remaining > pageSize ? `Import First ${batch} Hosts` : 'Import Hosts';
                    action.disabled = false;
                    return;
                }
                if (analyzed.validRows > 0 && outcomes.size > 0) {
                    action.textContent = Array.from(outcomes.values()).includes('Failed')
                        ? 'Import Finished (with failures)'
                        : Array.from(outcomes.values()).includes('Needs Attention')
                            ? 'Import Finished (review needed)' : 'Import Complete';
                    action.disabled = true;
                    return;
                }
            }
            action.textContent = 'Analyze File';
            action.disabled = !preview || !file.files.length;
        }
        function setBusy(value, label) {
            busy = Boolean(value);
            [file, prefix, updateExisting, get('test-after')].forEach(control => { control.disabled = busy; });
            modeInputs.forEach(control => { control.disabled = busy; });
            dialog.querySelectorAll('[data-native-portability-import-cancel], [data-native-portability-import-close]')
                .forEach(control => { control.disabled = busy; });
            if (busy && label) action.textContent = label;
            prev.disabled = busy;
            next.disabled = busy;
            dialog.classList.toggle('is-busy', busy);
            updateActionState();
        }
        function selectedMode() {
            return dialog.querySelector('input[name="hostImportMode"]:checked')?.value || 'portability';
        }
        function finishAnalysis() {
            const id = analyzed?.analysisId;
            if (id) void post(options.finishUrl, { analysisId: id }, false).catch(() => {});
        }
        function setMode(mode) {
            if (busy) return;
            const spreadsheet = mode === 'spreadsheet';
            nativePanel.hidden = spreadsheet;
            nativeAction.hidden = spreadsheet;
            panel.hidden = !spreadsheet;
            action.hidden = !spreadsheet;
            if (!spreadsheet) {
                finishAnalysis();
                reset();
                file.value = '';
            }
            clearError();
        }
        async function post(url, bodyValue, multipart) {
            const headers = { 'RequestVerificationToken': token, 'Accept': 'application/json' };
            if (!multipart) headers['Content-Type'] = 'application/json';
            const response = await fetch(url, { method: 'POST', headers, body: multipart ? bodyValue : JSON.stringify(bodyValue), cache: 'no-store' });
            if (response.status === 401) { window.location.assign(options.loginUrl); throw new Error('MOVEit session expired.'); }
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data.message || `Host import request failed (${response.status}).`);
            return data.result || {};
        }
        function form() { const data = new FormData(); data.append('file', file.files[0]); data.append('prefix', prefix.value.trim()); data.append('updateExisting', String(updateExisting.checked)); data.append('testAfterCreation', String(get('test-after').checked)); return data; }
        function setProgress(done, total) {
            const percent = total ? Math.max(0, Math.min(100, Math.round(done / total * 100))) : 0;
            progressFill.style.width = `${percent}%`;
            progressText.textContent = total ? `Import Progress: ${done} of ${total}` : 'Import Progress';
        }
        function addSummaryPill(label, count, kind) {
            const pill = document.createElement('span');
            pill.className = `task-spreadsheet-summary-pill ${kind}`;
            pill.textContent = `${label}: ${count}`;
            summary.appendChild(pill);
        }
        function renderSummary() {
            summary.replaceChildren();
            if (!analyzed) return;
            const warnings = new Set((analyzed.issues || [])
                .filter(issue => String(issue.severity).toLowerCase() === 'warning')
                .map(issue => issue.rowNumber ?? 'File')).size;
            addSummaryPill('Valid', analyzed.validRows || 0, 'valid');
            addSummaryPill('Warnings', warnings, 'warning');
            addSummaryPill('Errors', analyzed.errorRows || 0, 'error');
            if (analyzed.skippedRows) addSummaryPill('Skipped', analyzed.skippedRows, 'warning');
            ['Created', 'Updated', 'Skipped', 'Failed', 'Needs Attention'].forEach(outcome => {
                const count = Array.from(outcomes.values()).filter(value => value === outcome).length;
                if (count) addSummaryPill(outcome, count,
                    outcome === 'Failed' ? 'error' : outcome === 'Needs Attention' || outcome === 'Skipped' ? 'warning' : 'valid');
            });
        }
        function renderPreview() {
            if (!preview) return;
            summary.replaceChildren();
            const note = document.createElement('span');
            note.className = 'task-spreadsheet-summary-note';
            const count = Array.isArray(preview.rows) ? preview.rows.length : 0;
            note.textContent = `File loaded: ${count} data row${count === 1 ? '' : 's'}. Select Analyze File to validate and normalize the import.`;
            summary.appendChild(note);
            draw();
        }
        function resultNeutral(message) {
            const row = document.createElement('div');
            row.className = 'task-spreadsheet-result-neutral';
            row.textContent = message;
            results.appendChild(row);
            resultsPanel.hidden = false;
        }
        function renderValidationResults() {
            results.replaceChildren();
            resultsPanel.hidden = false;
            const issues = Array.isArray(analyzed?.issues) ? analyzed.issues : [];
            if (!issues.length) {
                resultNeutral('No validation errors or warnings. The analyzed rows are ready to import.');
                return;
            }
            issues.forEach(issue => {
                const row = document.createElement('div');
                row.className = `task-spreadsheet-issue ${String(issue.severity || 'Warning').toLowerCase()}`;
                const location = document.createElement('strong');
                location.textContent = issue.rowNumber
                    ? `Row ${issue.rowNumber}${issue.column ? ` · ${issue.column}` : ''}`
                    : (issue.column || 'File');
                const message = document.createElement('span');
                message.className = 'task-spreadsheet-issue-message';
                message.textContent = issue.message || '';
                row.append(location, message);
                results.appendChild(row);
            });
        }
        function appendImportResult(result) {
            const outcome = String(result.outcome || 'Failed');
            const row = document.createElement('div');
            row.className = `task-spreadsheet-result ${outcome.toLowerCase().replace(/\s+/g, '-')}`;
            const pill = document.createElement('span');
            pill.className = `task-spreadsheet-row-pill ${outcome === 'Failed' ? 'error' : outcome === 'Skipped' || outcome === 'Needs Attention' ? 'warning' : 'valid'}`;
            pill.textContent = outcome;
            const message = document.createElement('span');
            message.textContent = `Row ${result.rowNumber}${result.name ? ` · ${result.name}` : ''} — ${result.message || ''}`;
            row.append(pill, message);
            results.appendChild(row);
            resultsPanel.hidden = false;
            results.scrollTop = results.scrollHeight;
        }
        function draw() {
            const data = analyzed || preview;
            if (!data) return;
            const rows = Array.isArray(data.rows) ? data.rows : [];
            const columns = Array.isArray(data.columns) ? data.columns : [];
            head.replaceChildren(); body.replaceChildren();
            const tr = document.createElement('tr');
            ['Row', 'Status', ...columns].forEach(column => {
                const th = document.createElement('th'); th.textContent = column; tr.appendChild(th);
            });
            head.appendChild(tr);
            const fragment = document.createDocumentFragment();
            rows.slice(page * pageSize, (page + 1) * pageSize).forEach(item => {
                const line = document.createElement('tr');
                line.dataset.spreadsheetRowNumber = String(item.rowNumber);
                const rowCell = document.createElement('td');
                rowCell.textContent = String(item.rowNumber ?? '');
                line.appendChild(rowCell);
                const status = analyzed ? (outcomes.get(item.rowNumber) || item.status || 'Valid') : 'Not analyzed';
                const statusCell = document.createElement('td');
                const pill = document.createElement('span');
                pill.className = `task-spreadsheet-row-pill ${!analyzed ? 'pending' :
                    status === 'Failed' || status === 'Error' ? 'error' :
                    status === 'Needs Attention' || status === 'Skipped' ? 'warning' :
                    status === 'Created' || status === 'Updated' ? 'created' : 'valid'}`;
                pill.textContent = status;
                statusCell.appendChild(pill);
                line.appendChild(statusCell);
                columns.forEach(column => {
                    const cell = document.createElement('td');
                    cell.textContent = item.values?.[column] ?? '';
                    line.appendChild(cell);
                });
                fragment.appendChild(line);
            });
            body.appendChild(fragment);
            window.MauiModalUI?.sizeTableViewport?.(panel.querySelector('.task-spreadsheet-grid-frame'), 5);
            pagination.hidden = rows.length <= pageSize;
            const first = rows.length ? page * pageSize + 1 : 0;
            pageLabel.textContent = `Rows ${first}–${Math.min(rows.length, (page + 1) * pageSize)} of ${rows.length} · ${pageSize} per page`;
            prev.disabled = busy || page === 0;
            next.disabled = busy || (page + 1) * pageSize >= rows.length;
            analysis.hidden = false;
        }
        function reset() {
            preview = null; analyzed = null; page = 0; outcomes.clear();
            summary.replaceChildren(); head.replaceChildren(); body.replaceChildren();
            pagination.hidden = true; analysis.hidden = true;
            results.replaceChildren(); resultsPanel.hidden = true;
            progress.hidden = true; setProgress(0, 0);
            clearError(); updateActionState();
        }
        async function selectFile() {
            finishAnalysis();
            reset();
            if (!file.files.length) return;
            setBusy(true, 'Loading...');
            try {
                preview = await post(options.previewUrl, form(), true);
                renderPreview();
            } catch (err) {
                errorMessage(err.message);
            } finally { setBusy(false); }
        }
        async function analyzeFile() {
            if (busy || !preview || !file.files.length) return;
            setBusy(true, 'Analyzing...'); clearError();
            try {
                if (analyzed?.analysisId) await post(options.finishUrl, { analysisId: analyzed.analysisId }, false);
                analyzed = null; outcomes.clear(); page = 0;
                results.replaceChildren(); resultsPanel.hidden = true;
                progress.hidden = true; setProgress(0, 0);
                analyzed = await post(options.analyzeUrl, form(), true);
                renderSummary();
                draw();
                renderValidationResults();
            } catch (err) {
                analyzed = null;
                renderPreview();
                errorMessage(err.message);
            } finally { setBusy(false); }
        }
        async function importRows() {
            if (busy || active || !analyzed?.analysisId) return;
            const pending = pendingRows().slice(0, pageSize);
            if (!pending.length) return;
            active = true; setBusy(true, 'Importing...'); clearError();
            progress.hidden = false;
            const total = analyzed.rows.filter(row => row.status === 'Valid').length;
            let created = 0, updated = 0, failed = 0, skipped = 0, attention = 0;
            setProgress(outcomes.size, total);
            try {
                for (const row of pending) {
                    let result;
                    try {
                        result = await post(options.importUrl, { analysisId: analyzed.analysisId, rowNumber: row.rowNumber }, false);
                        if (!result || result.rowNumber !== row.rowNumber ||
                            !['Created', 'Updated', 'Failed', 'Skipped', 'Needs Attention'].includes(result.outcome))
                            throw new Error('MOVEit returned an unexpected Host import result. Review the Host before retrying.');
                    } catch (err) {
                        result = { rowNumber: row.rowNumber, name: row.values?.NAME || '', outcome: 'Needs Attention', message: 'The Host import result could not be confirmed. Inspect the Host before retrying. ' + err.message };
                    }
                    outcomes.set(row.rowNumber, result.outcome);
                    if (result.outcome === 'Created') created++;
                    else if (result.outcome === 'Updated') updated++;
                    else if (result.outcome === 'Skipped') skipped++;
                    else if (result.outcome === 'Needs Attention') attention++;
                    else failed++;
                    appendImportResult(result);
                    setProgress(outcomes.size, total);
                    draw();
                    renderSummary();
                }
                resultNeutral(`Batch finished: ${created} created, ${updated} updated, ${skipped} skipped, ${failed} failed, ${attention} need attention.`);
                if (created || updated || attention) {
                    try { await options.refresh(); }
                    catch { /* A grid refresh failure does not undo a committed Host creation. */ }
                }
            } catch (err) { errorMessage(err.message); }
            finally {
                active = false;
                setBusy(false);
                // The analysis contains plaintext credentials only on the server; discard
                // it promptly after all planned rows were attempted.
                if (!pendingRows().length && analyzed?.analysisId) {
                    await post(options.finishUrl, { analysisId: analyzed.analysisId }, false).catch(() => {});
                    analyzed.analysisId = null;
                }
            }
        }
        function open() {
            options.nativePortability?.openImport?.();
            reset();
            file.value = '';
            prefix.value = '';
            updateExisting.checked = false;
            get('test-after').checked = false;
            const portability = dialog.querySelector('input[name="hostImportMode"][value="portability"]');
            if (portability) portability.checked = true;
            setMode('portability');
        }
        modeInputs.forEach(input => input.addEventListener('change', () => setMode(selectedMode())));
        dialog.addEventListener('close', () => {
            // Keep the active import state intact if a caller closes the dialog
            // programmatically while a request is still in progress.
            if (busy) return;
            // Analyzes retain plaintext passwords only in short-lived server memory.
            finishAnalysis();
            reset();
            file.value = '';
        });
        dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
        file.addEventListener('change', selectFile);
        function invalidateAnalysis() {
            if (!preview || busy) return;
            finishAnalysis();
            analyzed = null; page = 0; outcomes.clear();
            results.replaceChildren(); resultsPanel.hidden = true;
            progress.hidden = true; setProgress(0, 0);
            renderPreview();
            clearError(); updateActionState();
        }
        updateExisting.addEventListener('change', invalidateAnalysis);
        get('test-after').addEventListener('change', invalidateAnalysis);
        prefix.addEventListener('input', invalidateAnalysis);
        action.addEventListener('click', () => { if (active) return; if (analyzed?.analysisId) importRows(); else analyzeFile(); });
        prev.addEventListener('click', () => { if (busy) return; page = Math.max(0, page - 1); draw(); panel.querySelector('.task-spreadsheet-grid-frame').scrollTop = 0; });
        next.addEventListener('click', () => { if (busy) return; const count = (analyzed || preview)?.rows?.length || 0; page = Math.min(page + 1, Math.max(0, Math.ceil(count / pageSize) - 1)); draw(); panel.querySelector('.task-spreadsheet-grid-frame').scrollTop = 0; });
        setMode(selectedMode());
        return { open };
    }
    window.MauiHostSpreadsheetImport = { create };
})();

