/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * First-class administration workspace for MAUI-created/scheduled Reports.
 */
(() => {
    'use strict';

    const config = document.querySelector('[data-reports-page-config]');
    const grid = document.querySelector('[data-grid="reports"]');
    if (!config || !grid) return;

    const urls = {
        list: config.dataset.listUrl || '',
        history: config.dataset.historyUrl || '',
        update: config.dataset.updateUrl || '',
        editView: config.dataset.editViewUrl || '',
        enabled: config.dataset.enabledUrl || '',
        run: config.dataset.runUrl || '',
        export: config.dataset.exportUrl || '',
        importPreflight: config.dataset.importPreflightUrl || '',
        import: config.dataset.importUrl || '',
        bulkDelete: config.dataset.bulkDeleteUrl || ''
    };
    const token = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const readiness = document.querySelector('[data-outbound-report-readiness]');
    const smtpWarning = document.querySelector('[data-outbound-report-smtp-warning]');
    const smtpWarningText = document.querySelector('[data-outbound-report-smtp-warning-text]');
    const dataAccessWarning = document.querySelector('[data-outbound-report-data-access-warning]');
    const dataAccessWarningText = document.querySelector('[data-outbound-report-data-access-warning-text]');
    const editDialog = document.querySelector('[data-report-edit-dialog]');
    const historyDialog = document.querySelector('[data-report-history-dialog]');
    const runDialog = document.querySelector('[data-report-run-dialog]');
    const rowMenu = document.querySelector('[data-report-management-row-action-menu]');
    const toggleCommand = rowMenu?.querySelector('[data-report-management-command="toggle-enabled"]');
    const importDialog = document.querySelector('[data-report-import-dialog]');
    const deleteDialog = document.querySelector('[data-delete-reports-dialog]');

    let reportMap = new Map();
    let activeReport = null;
    let runQueue = [];
    let runQueueActive = false;
    let runCancelRequested = false;
    let activeMenuContext = null;
    let editSelectedDates = [];
    let initialLoadComplete = false;
    let importPreflight = null;
    let importBusy = false;
    let pendingDeleteIds = [];

    function toast(message, kind = 'default', origin = 'modal') { window.mauiShowToast?.(message, kind, origin); }

    async function request(url, payload, method = 'POST') {
        const headers = new Headers({ Accept: 'application/json' });
        if (method !== 'GET') {
            headers.set('Content-Type', 'application/json');
            if (token) headers.set('RequestVerificationToken', token);
        }
        const response = await fetch(url, {
            method,
            credentials: 'same-origin',
            cache: 'no-store',
            headers,
            body: method === 'GET' ? undefined : JSON.stringify(payload || {})
        });
        if (response.status === 401) {
            window.mauiSessionExpired?.();
            throw new Error('Your session has expired.');
        }
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
        return data;
    }

    async function errorFrom(response) {
        const contentType = response.headers.get('content-type') || '';
        const data = contentType.includes('application/json') ? await response.json().catch(() => ({})) : {};
        return new Error(data?.message || `Request failed (${response.status}).`);
    }

    function formatDate(value) {
        if (!value) return '';
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
    }

    function describeSchedule(schedule) {
        if (!schedule) return '—';
        const time = schedule.timeOfDay || '';
        if (schedule.deliveryMode === 'Once') return `Once: ${(schedule.dates || []).join(', ')} ${time}`.trim();
        if (schedule.frequency === 'Daily') return `Daily ${time}`.trim();
        if (schedule.frequency === 'Weekly') {
            const names = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
            return `Weekly (${(schedule.daysOfWeek || []).map(day => names[day] || day).join(', ')}) ${time}`.trim();
        }
        if (schedule.frequency === 'Monthly') return `Monthly (${(schedule.daysOfMonth || []).join(', ')}) ${time}`.trim();
        return schedule.deliveryMode || '—';
    }

    function reportToGridRow(report) {
        const recipients = (report.recipients || []).join('; ');
        const nextRun = formatDate(report.nextRunUtc);
        const lastRun = formatDate(report.lastRunUtc);
        const created = formatDate(report.createdUtc);
        const modified = formatDate(report.modifiedUtc);
        const enabled = Boolean(report.enabled);
        return {
            id: String(report.id || ''),
            values: {
                Id: report.id || '',
                ReportName: report.title || '',
                Description: report.description || '',
                Source: report.gridLabel || report.gridKey || '',
                Environment: report.environmentKey || '',
                Recipients: recipients,
                Schedule: describeSchedule(report.schedule),
                Enabled: enabled ? 'Yes' : 'No',
                NextRun: nextRun,
                LastRun: lastRun,
                LastStatus: report.lastStatus || '—',
                LastMessage: report.lastMessage || '',
                Owner: report.ownerUsername || '',
                Created: created,
                Modified: modified
            },
            rawValues: {
                Id: report.id || '',
                ReportName: report.title || '',
                Description: report.description || '',
                Source: report.gridLabel || report.gridKey || '',
                Environment: report.environmentKey || '',
                Recipients: recipients,
                Schedule: describeSchedule(report.schedule),
                Enabled: enabled ? '1' : '0',
                NextRun: report.nextRunUtc || '',
                LastRun: report.lastRunUtc || '',
                LastStatus: report.lastStatus || '',
                LastMessage: report.lastMessage || '',
                Owner: report.ownerUsername || '',
                Created: report.createdUtc || '',
                Modified: report.modifiedUtc || '',
                ReportEditViewUrl: urls.editView
                    ? `${urls.editView}?id=${encodeURIComponent(report.id || '')}&returnScope=${encodeURIComponent(config.dataset.reportScope || 'all')}`
                    : ''
            },
            infoActionUrl: ''
        };
    }

    function renderReadiness(state) {
        const smtpConfigured = Boolean(state?.smtpConfigured);
        const missing = Array.isArray(state?.missingDataAccessEnvironments) ? state.missingDataAccessEnvironments.filter(Boolean) : [];
        if (smtpWarning) {
            smtpWarning.hidden = smtpConfigured;
            if (!smtpConfigured && smtpWarningText) smtpWarningText.textContent = String(state?.smtpMessage || '').trim() || 'Saved Reports cannot be delivered until SMTP Configuration is completed.';
        }
        if (dataAccessWarning) {
            dataAccessWarning.hidden = missing.length === 0;
            if (missing.length && dataAccessWarningText) dataAccessWarningText.textContent = `Scheduled Reports for ${missing.join(', ')} cannot run until Data Access Credentials are configured under Configuration > MOVEit Connections.`;
        }
        if (readiness) readiness.hidden = smtpConfigured && missing.length === 0;
    }

    async function loadReports({ initial = false } = {}) {
        try {
            const data = await request(urls.list, null, 'GET');
            const reports = Array.isArray(data.reports) ? data.reports : [];
            reportMap = new Map(reports.map(report => [String(report.id || ''), report]));
            renderReadiness(data.readiness || {});
            grid.dispatchEvent(new CustomEvent('maui:grid-replace-rows', { detail: { rows: reports.map(reportToGridRow) } }));
        } catch (error) {
            toast(`Reports: ${error.message}`, 'error', 'page');
            if (initial) grid.dispatchEvent(new CustomEvent('maui:grid-replace-rows', { detail: { rows: [] } }));
        } finally {
            if (initial && !initialLoadComplete) {
                initialLoadComplete = true;
                grid.dispatchEvent(new CustomEvent('maui:grid-initial-data-ready'));
            }
        }
    }

    function reportById(id) { return reportMap.get(String(id || '').trim()) || null; }

    // Preserves the current Reports grid search/filter/sort state when opening a Report's source-grid editor.
    grid.addEventListener('click', event => {
        const link = event.target.closest('a.row-primary-link');
        if (!link || !grid.contains(link) || !String(link.href || '').trim()) return;
        const target = new URL(link.href, window.location.href);
        if (!urls.editView || target.pathname !== new URL(urls.editView, window.location.href).pathname) return;
        const returnUrl = window.MauiGridNavigationState?.createReturnUrl?.('reports');
        if (returnUrl) target.searchParams.set('returnUrl', returnUrl);
        link.href = target.toString();
    });

    // ------------------------------------------------------------------
    // Row actions
    // ------------------------------------------------------------------
    function closeRowMenu() {
        activeMenuContext?.button?.setAttribute('aria-expanded', 'false');
        activeMenuContext = null;
        if (!rowMenu) return;
        rowMenu.hidden = true;
        rowMenu.style.left = '';
        rowMenu.style.top = '';
        rowMenu.style.visibility = '';
    }

    function openRowMenu(button) {
        if (!rowMenu || !button) return;
        if (activeMenuContext?.button === button && !rowMenu.hidden) { closeRowMenu(); return; }
        closeRowMenu();
        const id = String(button.dataset.rowId || '').trim();
        const report = reportById(id);
        if (!report) return;
        activeMenuContext = { button, id, report };
        if (toggleCommand) toggleCommand.textContent = report.enabled ? 'Disable Report' : 'Enable Report';
        button.setAttribute('aria-expanded', 'true');
        rowMenu.hidden = false;
        rowMenu.style.visibility = 'hidden';
        const rect = button.getBoundingClientRect();
        const menuRect = rowMenu.getBoundingClientRect();
        let left = rect.left;
        let top = rect.bottom + 4;
        if (left + menuRect.width > window.innerWidth - 8) left = Math.max(8, window.innerWidth - menuRect.width - 8);
        if (top + menuRect.height > window.innerHeight - 8) top = Math.max(8, rect.top - menuRect.height - 4);
        rowMenu.style.left = `${Math.round(left)}px`;
        rowMenu.style.top = `${Math.round(top)}px`;
        rowMenu.style.visibility = '';
    }

    async function toggleReport(report) {
        try {
            await request(urls.enabled, { id: report.id, enabled: !report.enabled });
            toast(report.enabled ? 'Report disabled.' : 'Report enabled.');
            await loadReports();
        } catch (error) { toast(error.message, 'error'); }
    }

    // ------------------------------------------------------------------
    // Run Now / History / Edit
    // ------------------------------------------------------------------
    function selectedReportsInGridOrder(ids) {
        const selected = new Set((ids || []).map(id => String(id || '').trim()).filter(Boolean));
        const reports = [];
        grid.querySelectorAll('tbody tr[data-row-id]').forEach(row => {
            const id = String(row.dataset.rowId || '').trim();
            if (!selected.has(id)) return;
            const report = reportById(id);
            if (report) reports.push(report);
            selected.delete(id);
        });
        selected.forEach(id => {
            const report = reportById(id);
            if (report) reports.push(report);
        });
        return reports;
    }

    function renderRunQueue() {
        const list = runDialog?.querySelector('[data-report-run-list]');
        if (!list) return;
        list.replaceChildren();
        runQueue.forEach(item => {
            const row = document.createElement('tr');
            row.dataset.reportRunItemId = item.id;

            const name = document.createElement('td');
            name.textContent = item.title;
            row.appendChild(name);

            const status = document.createElement('td');
            const statusPill = document.createElement('span');
            statusPill.dataset.reportRunStatus = '';
            statusPill.className = `outbound-report-run-status ${item.status.toLowerCase()}`;
            statusPill.textContent = item.status;
            status.appendChild(statusPill);
            row.appendChild(status);

            const message = document.createElement('td');
            message.dataset.reportRunMessage = '';
            message.className = 'outbound-report-run-message';
            message.textContent = item.message || '';
            row.appendChild(message);

            item.row = row;
            list.appendChild(row);
        });
    }

    function setRunQueueItem(item, status, message = '') {
        item.status = status;
        item.message = message || '';
        const statusCell = item.row?.querySelector('[data-report-run-status]');
        const messageCell = item.row?.querySelector('[data-report-run-message]');
        if (statusCell) {
            statusCell.className = `outbound-report-run-status ${status.toLowerCase()}`;
            statusCell.textContent = status;
        }
        if (messageCell) messageCell.textContent = item.message;
    }

    function resetRunDialogState() {
        runQueue = [];
        runQueueActive = false;
        runCancelRequested = false;
        const list = runDialog?.querySelector('[data-report-run-list]');
        list?.replaceChildren();
    }

    function openRun(reports) {
        if (!runDialog) return;
        const source = Array.isArray(reports) ? reports : [reports];
        const seen = new Set();
        runQueue = source
            .filter(Boolean)
            .map(report => ({
                id: String(report.id || '').trim(),
                title: String(report.title || report.id || 'Report').trim(),
                status: 'Waiting',
                message: '',
                row: null
            }))
            .filter(item => item.id && !seen.has(item.id) && seen.add(item.id));
        if (!runQueue.length) return;

        runQueueActive = false;
        runCancelRequested = false;
        const multiple = runQueue.length > 1;
        const heading = runDialog.querySelector('[data-report-run-heading]');
        const description = runDialog.querySelector('[data-report-run-description]');
        const error = runDialog.querySelector('[data-report-run-error]');
        const confirm = runDialog.querySelector('[data-report-run-confirm]');
        const cancel = runDialog.querySelector('[data-report-run-cancel]');
        const close = runDialog.querySelector('[data-report-run-close]');
        if (heading) heading.textContent = multiple ? 'Run Reports' : 'Run Report';
        if (description) description.textContent = multiple
            ? `Run ${runQueue.length} selected Reports using current data and email each result immediately.`
            : 'Generate the saved Report using current data and email it immediately.';
        if (error) { error.hidden = true; error.textContent = ''; }
        if (confirm) { confirm.hidden = false; confirm.disabled = false; confirm.textContent = 'Run Now'; }
        if (cancel) { cancel.hidden = false; cancel.disabled = false; cancel.textContent = 'Cancel'; }
        if (close) close.disabled = false;
        renderRunQueue();
        runDialog.showModal();
    }

    function cancelPendingRuns() {
        if (!runDialog) return;
        if (!runQueueActive) {
            runDialog.close();
            return;
        }
        if (runCancelRequested) return;

        runCancelRequested = true;
        runQueue.filter(item => item.status === 'Waiting')
            .forEach(item => setRunQueueItem(item, 'Cancelled', 'Cancelled before execution.'));
        const description = runDialog.querySelector('[data-report-run-description]');
        if (description) description.textContent = 'Cancellation requested. The Report currently running will finish; un-run Reports have been cancelled.';
    }

    function summarizeRunQueue() {
        const counts = runQueue.reduce((result, item) => {
            result[item.status] = (result[item.status] || 0) + 1;
            return result;
        }, {});
        const parts = [];
        if (counts.Successful) parts.push(`${counts.Successful} successful`);
        if (counts.Failed) parts.push(`${counts.Failed} failed`);
        if (counts.Cancelled) parts.push(`${counts.Cancelled} cancelled`);
        return parts.length ? `Run complete: ${parts.join(', ')}.` : 'Run complete.';
    }

    async function confirmRun() {
        if (!runDialog || !runQueue.length || runQueueActive) return;

        runQueueActive = true;
        runCancelRequested = false;
        runQueue.forEach(item => setRunQueueItem(item, 'Waiting', ''));

        const confirm = runDialog.querySelector('[data-report-run-confirm]');
        const cancel = runDialog.querySelector('[data-report-run-cancel]');
        const close = runDialog.querySelector('[data-report-run-close]');
        const description = runDialog.querySelector('[data-report-run-description]');
        const error = runDialog.querySelector('[data-report-run-error]');
        if (error) { error.hidden = true; error.textContent = ''; }
        if (confirm) { confirm.hidden = false; confirm.disabled = true; confirm.textContent = 'Running'; }
        if (cancel) { cancel.hidden = false; cancel.disabled = false; cancel.textContent = 'Cancel'; }
        if (close) close.disabled = true;

        for (const item of runQueue) {
            if (runCancelRequested || item.status === 'Cancelled') break;
            setRunQueueItem(item, 'Running', '');
            try {
                const data = await request(urls.run, { id: item.id });
                setRunQueueItem(item, 'Successful', data?.result?.message || 'Report sent successfully.');
            } catch (ex) {
                setRunQueueItem(item, 'Failed', ex.message || 'Report execution failed.');
            }
        }

        if (runCancelRequested) {
            runQueue.filter(item => item.status === 'Waiting')
                .forEach(item => setRunQueueItem(item, 'Cancelled', 'Cancelled before execution.'));
        }

        runQueueActive = false;
        if (confirm) { confirm.hidden = false; confirm.disabled = false; confirm.textContent = 'Run Now'; }
        if (cancel) { cancel.hidden = false; cancel.disabled = false; cancel.textContent = 'Cancel'; }
        if (close) close.disabled = false;
        if (description) description.textContent = summarizeRunQueue();

        await loadReports();
    }

    async function showHistory(report) {
        if (!historyDialog || !report) return;
        historyDialog.querySelector('[data-report-history-title]').textContent = report.title || '';
        const body = historyDialog.querySelector('[data-report-history-list]');
        body.innerHTML = '<tr><td colspan="5">Loading...</td></tr>';
        historyDialog.showModal();
        try {
            const url = new URL(urls.history, window.location.origin);
            url.searchParams.set('id', report.id);
            const data = await request(url.toString(), null, 'GET');
            const history = Array.isArray(data.history) ? data.history : [];
            body.replaceChildren();
            if (!history.length) { body.innerHTML = '<tr><td colspan="5">No Report history.</td></tr>'; return; }
            history.forEach(item => {
                const row = document.createElement('tr');
                [formatDate(item.startedUtc), item.status || '—', String(item.rowCount ?? 0), String(item.recipientCount ?? 0), item.message || ''].forEach(value => {
                    const cell = document.createElement('td'); cell.textContent = value; row.appendChild(cell);
                });
                body.appendChild(row);
            });
        } catch (error) {
            body.innerHTML = '<tr><td colspan="5"></td></tr>';
            body.querySelector('td').textContent = error.message;
        }
    }

    function renderEditDates() {
        if (!editDialog) return;
        const holder = editDialog.querySelector('[data-report-edit-date-chips]');
        holder?.replaceChildren();
        editSelectedDates.sort().forEach(date => {
            const chip = document.createElement('button');
            chip.type = 'button'; chip.className = 'outbound-report-date-chip'; chip.textContent = `${date} ×`; chip.title = `Remove ${date}`;
            chip.addEventListener('click', () => { editSelectedDates = editSelectedDates.filter(item => item !== date); renderEditDates(); });
            holder?.appendChild(chip);
        });
    }

    function populateFiveMinuteTimes(select, selectedValue = '08:00') {
        if (!select || select.options.length) return;
        for (let hour = 0; hour < 24; hour += 1) {
            for (let minute = 0; minute < 60; minute += 5) {
                const value = `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
                const option = document.createElement('option');
                option.value = value;
                option.textContent = value;
                select.appendChild(option);
            }
        }
        select.value = selectedValue;
    }

    function initializeEditPickers() {
        if (!editDialog || editDialog.dataset.pickersReady === 'true') return;
        populateFiveMinuteTimes(editDialog.querySelector('[data-report-edit-time]'));
        editDialog.dataset.pickersReady = 'true';
        const weekdays = editDialog.querySelector('[data-report-edit-weekdays]');
        ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].forEach((name, index) => {
            const label = document.createElement('label');
            label.className = 'outbound-report-choice';
            const input = document.createElement('input');
            input.type = 'checkbox';
            input.value = String(index);
            input.dataset.reportEditWeekday = '';
            const text = document.createElement('span');
            text.textContent = name;
            label.append(input, text);
            weekdays?.appendChild(label);
        });
        const monthdays = editDialog.querySelector('[data-report-edit-monthdays]');
        for (let day = 1; day <= 31; day += 1) {
            const label = document.createElement('label');
            label.className = 'outbound-report-choice compact';
            const input = document.createElement('input');
            input.type = 'checkbox';
            input.value = String(day);
            input.dataset.reportEditMonthday = '';
            const text = document.createElement('span');
            text.textContent = String(day);
            label.append(input, text);
            monthdays?.appendChild(label);
        }
        editDialog.querySelector('[data-report-edit-add-date]')?.addEventListener('click', () => {
            const input = editDialog.querySelector('[data-report-edit-date]');
            const value = String(input?.value || '');
            if (value && !editSelectedDates.includes(value)) editSelectedDates.push(value);
            if (input) input.value = '';
            renderEditDates();
        });
    }

    function syncEditSchedule() {
        const delivery = editDialog?.querySelector('[data-report-edit-delivery]')?.value || 'Recurring';
        const frequency = editDialog?.querySelector('[data-report-edit-frequency]')?.value || 'Daily';
        const frequencyField = editDialog?.querySelector('[data-report-edit-frequency-field]');
        const once = editDialog?.querySelector('[data-report-edit-once]');
        const weekly = editDialog?.querySelector('[data-report-edit-weekly]');
        const monthly = editDialog?.querySelector('[data-report-edit-monthly]');
        if (frequencyField) frequencyField.hidden = delivery !== 'Recurring';
        if (once) once.hidden = delivery !== 'Once';
        if (weekly) weekly.hidden = delivery !== 'Recurring' || frequency !== 'Weekly';
        if (monthly) monthly.hidden = delivery !== 'Recurring' || frequency !== 'Monthly';
    }

    function openEdit(report) {
        if (!editDialog || !report) return;
        initializeEditPickers();
        activeReport = report;
        const identity = editDialog.querySelector('[data-report-edit-id]'); if (identity) identity.textContent = String(report.id || '—');
        const schedule = report.schedule || {};
        editDialog.querySelector('[data-report-edit-title]').value = report.title || '';
        editDialog.querySelector('[data-report-edit-description]').value = report.description || '';
        editDialog.querySelector('[data-report-edit-recipients]').value = (report.recipients || []).join('; ');
        editDialog.querySelector('[data-report-edit-delivery]').value = schedule.deliveryMode === 'Once' ? 'Once' : 'Recurring';
        editDialog.querySelector('[data-report-edit-time]').value = schedule.timeOfDay || '08:00';
        editDialog.querySelector('[data-report-edit-frequency]').value = schedule.frequency || 'Daily';
        editSelectedDates = [...(schedule.dates || [])];
        const dateInput = editDialog.querySelector('[data-report-edit-date]'); if (dateInput) dateInput.value = '';
        renderEditDates();
        editDialog.querySelector('[data-report-edit-enabled]').checked = Boolean(report.enabled);
        editDialog.querySelector('[data-report-edit-owner]').value = report.ownerUsername || '';
        editDialog.querySelector('[data-report-edit-created]').value = formatDate(report.createdUtc);
        editDialog.querySelector('[data-report-edit-modified]').value = formatDate(report.modifiedUtc);
        editDialog.querySelectorAll('[data-report-edit-weekday]').forEach(item => { item.checked = (schedule.daysOfWeek || []).includes(Number(item.value)); });
        editDialog.querySelectorAll('[data-report-edit-monthday]').forEach(item => { item.checked = (schedule.daysOfMonth || []).includes(Number(item.value)); });
        const error = editDialog.querySelector('[data-report-edit-error]'); if (error) { error.hidden = true; error.textContent = ''; }
        syncEditSchedule();
        editDialog.showModal();
    }

    function isFiveMinuteTime(value) {
        const match = /^(\d{2}):(\d{2})$/.exec(String(value || ''));
        if (!match) return false;
        const hours = Number(match[1]);
        const minutes = Number(match[2]);
        return hours >= 0 && hours <= 23 && minutes >= 0 && minutes <= 59 && minutes % 5 === 0;
    }

    async function saveEdit(event) {
        if (!activeReport || !editDialog) return;
        const deliveryMode = editDialog.querySelector('[data-report-edit-delivery]').value;
        const frequency = editDialog.querySelector('[data-report-edit-frequency]').value;
        if (deliveryMode === 'Once') {
            const pendingDate = String(editDialog.querySelector('[data-report-edit-date]')?.value || '');
            if (pendingDate && !editSelectedDates.includes(pendingDate)) editSelectedDates.push(pendingDate);
        }
        const timeOfDay = editDialog.querySelector('[data-report-edit-time]').value;
        const error = editDialog.querySelector('[data-report-edit-error]');
        if (!isFiveMinuteTime(timeOfDay)) {
            if (error) { error.textContent = 'Report delivery times must use 5-minute increments (00, 05, 10, ... 55).'; error.hidden = false; }
            return;
        }
        const schedule = {
            deliveryMode,
            frequency: deliveryMode === 'Recurring' ? frequency : '',
            timeOfDay,
            timeZoneId: activeReport.schedule?.timeZoneId || '',
            dates: deliveryMode === 'Once' ? [...editSelectedDates] : [],
            daysOfWeek: deliveryMode === 'Recurring' && frequency === 'Weekly' ? [...editDialog.querySelectorAll('[data-report-edit-weekday]:checked')].map(item => Number(item.value)) : [],
            daysOfMonth: deliveryMode === 'Recurring' && frequency === 'Monthly' ? [...editDialog.querySelectorAll('[data-report-edit-monthday]:checked')].map(item => Number(item.value)) : []
        };
        if (error) error.hidden = true;
        event.currentTarget.disabled = true;
        try {
            await request(urls.update, {
                id: activeReport.id,
                title: editDialog.querySelector('[data-report-edit-title]').value,
                description: editDialog.querySelector('[data-report-edit-description]').value,
                recipients: editDialog.querySelector('[data-report-edit-recipients]').value,
                enabled: editDialog.querySelector('[data-report-edit-enabled]').checked,
                schedule
            });
            editDialog.close(); toast('Report updated.'); await loadReports();
        } catch (ex) {
            if (error) { error.textContent = ex.message; error.hidden = false; }
        } finally { event.currentTarget.disabled = false; }
    }

    // ------------------------------------------------------------------
    // Portable export/import
    // ------------------------------------------------------------------
    function downloadResponse(response, fallbackName) {
        return response.blob().then(blob => {
            const disposition = response.headers.get('content-disposition') || '';
            const utf8Name = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
            const quotedName = disposition.match(/filename="([^"]+)"/i)?.[1];
            const filename = utf8Name ? decodeURIComponent(utf8Name) : (quotedName || fallbackName);
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement('a'); link.href = objectUrl; link.download = filename; link.hidden = true;
            document.body.appendChild(link); link.click(); link.remove();
            window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
        });
    }

    async function exportReports(ids) {
        const reportIds = [...new Set((ids || []).map(id => String(id || '').trim()).filter(Boolean))];
        if (!reportIds.length) return;
        try {
            const response = await fetch(urls.export, {
                method: 'POST', credentials: 'same-origin',
                headers: { Accept: 'application/zip, application/octet-stream, application/json', 'Content-Type': 'application/json', RequestVerificationToken: token },
                body: JSON.stringify({ reportIds })
            });
            if (response.status === 401) { window.mauiSessionExpired?.(); return; }
            if (!response.ok) throw await errorFrom(response);
            await downloadResponse(response, `Reports_${reportIds.length}.maui`);
            toast(`Exported ${reportIds.length} Report${reportIds.length === 1 ? '' : 's'}.`);
        } catch (error) { toast(`Export Reports: ${error.message}`, 'error'); }
    }

    function validationRow(kind, text) {
        const div = document.createElement('div'); div.className = `task-import-validation-row ${kind}`;
        const icon = document.createElement('span'); icon.className = 'task-import-validation-icon'; icon.textContent = kind === 'error' ? '×' : kind === 'warning' ? '!' : '✓';
        const copy = document.createElement('span'); copy.textContent = text; div.append(icon, copy); return div;
    }

    function resetImportDialog() {
        if (!importDialog) return;
        importPreflight = null; importBusy = false;
        const file = importDialog.querySelector('[data-report-import-file]'); if (file) file.value = '';
        const validation = importDialog.querySelector('[data-report-import-validation]'); if (validation) validation.hidden = true;
        importDialog.querySelector('[data-report-import-validation-list]')?.replaceChildren();
        const error = importDialog.querySelector('[data-report-import-error]'); if (error) { error.hidden = true; error.textContent = ''; }
        const submit = importDialog.querySelector('[data-report-import-submit]'); if (submit) submit.disabled = true;
        const spinner = importDialog.querySelector('[data-report-import-spinner]'); if (spinner) spinner.hidden = true;
    }

    async function preflightImport() {
        const file = importDialog?.querySelector('[data-report-import-file]')?.files?.[0];
        const validation = importDialog?.querySelector('[data-report-import-validation]');
        const list = importDialog?.querySelector('[data-report-import-validation-list]');
        const submit = importDialog?.querySelector('[data-report-import-submit]');
        const error = importDialog?.querySelector('[data-report-import-error]');
        importPreflight = null;
        if (submit) submit.disabled = true;
        if (!file) { if (validation) validation.hidden = true; return; }
        if (error) { error.hidden = true; error.textContent = ''; }
        if (list) { list.replaceChildren(); list.appendChild(validationRow('ok', 'Validating package...')); }
        if (validation) validation.hidden = false;
        const form = new FormData(); form.append('File', file);
        try {
            const response = await fetch(urls.importPreflight, { method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: token }, body: form });
            if (response.status === 401) { window.mauiSessionExpired?.(); return; }
            if (!response.ok) throw await errorFrom(response);
            const data = await response.json(); importPreflight = data.result || null;
            if (list) {
                list.replaceChildren();
                const items = Array.isArray(importPreflight?.items) ? importPreflight.items : [];
                list.appendChild(validationRow(Number(importPreflight?.errorCount || 0) > 0 ? 'warning' : 'ok', importPreflight?.message || 'Validation complete.'));
                items.forEach(item => list.appendChild(validationRow(item.valid ? 'ok' : 'error', `${item.title || 'Report'} (${item.environmentKey || 'environment'}): ${item.valid ? 'Ready to import' : item.message}`)));
            }
            if (submit) submit.disabled = Number(importPreflight?.errorCount || 0) > 0 || Number(importPreflight?.itemCount || 0) < 1;
        } catch (ex) {
            if (validation) validation.hidden = true;
            if (error) { error.textContent = ex.message; error.hidden = false; }
        }
    }

    async function submitImport(event) {
        event.preventDefault();
        if (importBusy || !importPreflight || Number(importPreflight.errorCount || 0) > 0) return;
        const file = importDialog?.querySelector('[data-report-import-file]')?.files?.[0]; if (!file) return;
        const submit = importDialog.querySelector('[data-report-import-submit]');
        const spinner = importDialog.querySelector('[data-report-import-spinner]');
        const error = importDialog.querySelector('[data-report-import-error]');
        importBusy = true; if (submit) submit.disabled = true; if (spinner) spinner.hidden = false; if (error) error.hidden = true;
        const form = new FormData(); form.append('File', file);
        try {
            const response = await fetch(urls.import, { method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: token }, body: form });
            if (response.status === 401) { window.mauiSessionExpired?.(); return; }
            if (!response.ok) throw await errorFrom(response);
            const data = await response.json();
            importDialog.close();
            toast(`Imported ${data?.result?.importedCount || 0} Report${Number(data?.result?.importedCount || 0) === 1 ? '' : 's'} disabled for review.`);
            await loadReports();
        } catch (ex) {
            if (error) { error.textContent = ex.message; error.hidden = false; }
        } finally {
            importBusy = false; if (spinner) spinner.hidden = true;
            if (submit) submit.disabled = !importPreflight || Number(importPreflight.errorCount || 0) > 0;
        }
    }

    // ------------------------------------------------------------------
    // Delete single/bulk
    // ------------------------------------------------------------------
    function openDelete(ids) {
        if (!deleteDialog) return;
        pendingDeleteIds = [...new Set((ids || []).map(id => String(id || '').trim()).filter(Boolean))];
        if (!pendingDeleteIds.length) return;
        const reports = pendingDeleteIds.map(reportById).filter(Boolean);
        deleteDialog.querySelector('[data-delete-reports-title]').textContent = pendingDeleteIds.length === 1 ? 'Delete Report' : 'Delete Reports';
        deleteDialog.querySelector('[data-delete-reports-message]').textContent = pendingDeleteIds.length === 1
            ? `Delete Report "${reports[0]?.title || pendingDeleteIds[0]}"?`
            : `Delete ${pendingDeleteIds.length} selected Reports?`;
        const list = deleteDialog.querySelector('[data-delete-reports-list]');
        if (list) {
            list.replaceChildren(); list.hidden = pendingDeleteIds.length <= 1;
            reports.forEach(report => { const div = document.createElement('div'); div.textContent = report.title || report.id; list.appendChild(div); });
        }
        const error = deleteDialog.querySelector('[data-delete-reports-error]'); if (error) { error.hidden = true; error.textContent = ''; }
        deleteDialog.showModal();
    }

    async function confirmDelete() {
        if (!pendingDeleteIds.length || !deleteDialog) return;
        const confirm = deleteDialog.querySelector('[data-delete-reports-confirm]');
        const cancels = Array.from(deleteDialog.querySelectorAll('[data-delete-reports-cancel]'));
        const error = deleteDialog.querySelector('[data-delete-reports-error]');
        if (confirm) confirm.disabled = true; cancels.forEach(button => button.disabled = true);
        try {
            await request(urls.bulkDelete, { reportIds: pendingDeleteIds });
            const count = pendingDeleteIds.length; deleteDialog.close(); toast(`Deleted ${count} Report${count === 1 ? '' : 's'}.`); await loadReports();
        } catch (ex) {
            if (error) { error.textContent = ex.message; error.hidden = false; }
        } finally {
            if (confirm) confirm.disabled = false; cancels.forEach(button => button.disabled = false);
        }
    }

    // ------------------------------------------------------------------
    // Event wiring
    // ------------------------------------------------------------------
    document.addEventListener('maui:grid-action', event => {
        if (String(event.detail?.gridId || '').toLowerCase() !== 'reports') return;
        const action = String(event.detail?.action || '').trim().toLowerCase();
        if (action === 'import reports') { event.preventDefault(); resetImportDialog(); importDialog?.showModal(); return; }
        if (action === 'run reports') {
            event.preventDefault();
            openRun(selectedReportsInGridOrder(event.detail?.selectedIds || []));
            return;
        }
        if (action === 'export reports') { event.preventDefault(); void exportReports(event.detail?.selectedIds || []); return; }
        if (action === 'delete reports') { event.preventDefault(); openDelete(event.detail?.selectedIds || []); }
    });

    document.addEventListener('click', event => {
        const menuButton = event.target.closest('[data-report-management-row-menu-button]');
        if (menuButton) { event.preventDefault(); event.stopPropagation(); openRowMenu(menuButton); return; }
        const command = event.target.closest('[data-report-management-command]');
        if (command && rowMenu?.contains(command)) {
            event.preventDefault();
            const context = activeMenuContext; const name = String(command.dataset.reportManagementCommand || ''); closeRowMenu();
            if (!context) return;
            if (name === 'edit-information') openEdit(context.report);
            else if (name === 'run') openRun(context.report);
            else if (name === 'history') void showHistory(context.report);
            else if (name === 'toggle-enabled') void toggleReport(context.report);
            else if (name === 'delete') openDelete([context.id]);
            return;
        }
        if (rowMenu && !rowMenu.hidden && !event.target.closest('[data-report-management-row-action-menu]')) closeRowMenu();
    });

    window.addEventListener('resize', closeRowMenu);
    window.addEventListener('scroll', closeRowMenu, true);
    runDialog?.querySelector('[data-report-run-cancel]')?.addEventListener('click', cancelPendingRuns);
    runDialog?.querySelector('[data-report-run-close]')?.addEventListener('click', () => { if (!runQueueActive) runDialog.close(); });
    runDialog?.querySelector('[data-report-run-confirm]')?.addEventListener('click', confirmRun);
    runDialog?.addEventListener('cancel', event => { if (runQueueActive) event.preventDefault(); });
    runDialog?.addEventListener('close', resetRunDialogState);
    historyDialog?.querySelectorAll('[data-report-history-close]').forEach(button => button.addEventListener('click', () => historyDialog.close()));
    editDialog?.querySelectorAll('[data-report-edit-close]').forEach(button => button.addEventListener('click', () => editDialog.close()));
    editDialog?.querySelector('[data-report-edit-delivery]')?.addEventListener('change', syncEditSchedule);
    editDialog?.querySelector('[data-report-edit-frequency]')?.addEventListener('change', syncEditSchedule);
    editDialog?.querySelector('[data-report-edit-save]')?.addEventListener('click', saveEdit);
    importDialog?.querySelector('[data-report-import-file]')?.addEventListener('change', preflightImport);
    importDialog?.querySelector('[data-report-import-form]')?.addEventListener('submit', submitImport);
    importDialog?.querySelectorAll('[data-report-import-close],[data-report-import-cancel]').forEach(button => button.addEventListener('click', () => { if (!importBusy) importDialog.close(); }));
    importDialog?.addEventListener('close', resetImportDialog);
    deleteDialog?.querySelectorAll('[data-delete-reports-cancel]').forEach(button => button.addEventListener('click', () => deleteDialog.close()));
    deleteDialog?.querySelector('[data-delete-reports-confirm]')?.addEventListener('click', confirmDelete);
    deleteDialog?.addEventListener('close', () => { pendingDeleteIds = []; });

    void loadReports({ initial: true });
})();
