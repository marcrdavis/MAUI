/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * MOVEit Automation Records workspace batching, live refresh, and Task editor integration.
 * MOVEit calls these data sets Reports; MAUI calls the workspace Records to distinguish it from MAUI-generated Reports.
 */
(() => {
    'use strict';

    const config = document.querySelector('[data-report-page-config]');
    if (!config) return;

    const grid = document.querySelector('.data-workspace[data-grid]');
    if (!grid) return;

    const refreshControl = document.querySelector('[data-report-refresh-control]');
    const refreshText = document.querySelector('[data-report-last-refresh]');
    const refreshClock = document.querySelector('[data-report-refresh-clock]');
    const refreshToggle = document.querySelector('[data-report-refresh-toggle]');
    const refreshNow = document.querySelector('[data-report-refresh-now]');
    const previousBatchAction = grid.querySelector('[data-report-fetch-previous]');
    const rangeStart = grid.querySelector('[data-report-range-start]');
    const rangeEnd = grid.querySelector('[data-report-range-end]');
    const reportTotalCount = grid.querySelector('[data-report-total-count]');
    const toast = grid.querySelector('[data-prototype-toast]');
    const reportRowActionMenu = document.querySelector('[data-report-row-action-menu]');
    const reportTaskInformationCommand = reportRowActionMenu?.querySelector('[data-report-row-command="task-information"]');
    const reportRowDetailsCommand = reportRowActionMenu?.querySelector('[data-report-row-command="details"]');
    const reportRowChangesCommand = reportRowActionMenu?.querySelector('[data-report-row-command="changes"]');
    const reportRowRunHistoryCommand = reportRowActionMenu?.querySelector('[data-report-row-command="run-history"]');
    const reportRowFileActivityCommand = reportRowActionMenu?.querySelector('[data-report-row-command="file-activity"]');
    const reportRowAuditCommand = reportRowActionMenu?.querySelector('[data-report-row-command="audit"]');
    const reportCopyTaskNameCommand = reportRowActionMenu?.querySelector('[data-report-row-command="copy-task-name"]');
    const reportEditTaskCommand = reportRowActionMenu?.querySelector('[data-report-row-command="edit-task"]');
    const reportToggleTaskEnabledCommand = reportRowActionMenu?.querySelector('[data-report-row-command="toggle-task-enabled"]');
    const reportRunTaskCommand = reportRowActionMenu?.querySelector('[data-report-row-command="run-task"]');
    const reportDetailsDialog = document.querySelector('[data-report-details-dialog]');
    const reportDetailsTitle = reportDetailsDialog?.querySelector('[data-report-details-title]');
    const reportDetailsBody = reportDetailsDialog?.querySelector('[data-report-details-body]');
    const antiForgeryToken = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';

    let activeReportRowAction = null;
    let reportMenuOpenSequence = 0;

    const batchUrl = String(config.dataset.batchUrl || '');
    const batchSize = Math.max(1, Number(config.dataset.batchSize || 500));
    const uiRefreshSeconds = Math.max(0, Number(config.dataset.uiRefreshSeconds || 0));
    const maximumEntries = Math.max(batchSize, Number(config.dataset.maximumEntries || 10000));

    let currentOffset = Math.max(0, Number(config.dataset.currentOffset || 0));
    let currentCursor = String(config.dataset.currentCursor || '');
    let nextCursor = String(config.dataset.nextCursor || '');
    let hasMore = String(config.dataset.hasMore || '').toLowerCase() === 'true';
    let nextCount = Math.max(0, Number(config.dataset.nextCount || batchSize));
    let refreshPaused = false;
    let refreshInFlight = false;
    let refreshTimer = 0;
    let batchHistory = [{ cursor: currentCursor, offset: currentOffset }];
    let batchHistoryIndex = 0;

    // Purpose: Builds the URL for one controller endpoint.
    function endpoint(name) {
        return String(config.dataset[name] || '');
    }

    // Purpose: Displays a transient workspace status message.
    function showStatus(message, durationMs = 5000, kind = 'default', origin = 'modal') {
        if (typeof window.mauiDisplayToast === 'function') { window.mauiDisplayToast(toast, message, kind, durationMs, origin); return; }
        if (!toast) return;
        toast.textContent = message || '';
        toast.classList.toggle('error', kind === 'error');
        toast.classList.add('visible');
        window.clearTimeout(toast._mauiReportTimer);
        toast._mauiReportTimer = window.setTimeout(() => toast.classList.remove('visible'), durationMs);
    }

    // Purpose: Calls a report endpoint and normalizes JSON/error handling.
    async function fetchJson(url, options = {}) {
        const headers = new Headers(options.headers || {});
        headers.set('Accept', 'application/json');
        if (options.method && options.method.toUpperCase() !== 'GET' && antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
        const response = await (window.MauiMoveItGridConnection?.fetch || window.fetch.bind(window))(url, { credentials: 'same-origin', cache: 'no-store', ...options, headers });
        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json') ? await response.json() : null;
        if (response.status === 401) {
            window.location.replace(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
            throw new Error('MOVEit session expired.');
        }
        if (!response.ok) throw new Error(payload?.message || `Request failed (${response.status}).`);
        return payload || {};
    }

    // Purpose: Builds batch url.
    function buildBatchUrl(cursor, offset) {
        const url = new URL(batchUrl, window.location.href);
        if (cursor) url.searchParams.set('cursor', cursor);
        if (offset > 0) url.searchParams.set('offset', String(offset));
        const parameters = [
            ['taskId', config.dataset.taskId],
            ['taskName', config.dataset.taskName],
            ['runId', config.dataset.runId],
            ['nominalStart', config.dataset.runNominalStart],
            ['dashboardView', config.dataset.reportPreset],
            ['targetType', config.dataset.targetType],
            ['targetId', config.dataset.targetId],
            ['targetName', config.dataset.targetName]
        ];
        parameters.forEach(([name, value]) => {
            const text = String(value || '').trim();
            if (text) url.searchParams.set(name, text);
        });
        return url.toString();
    }

    // Purpose: Formats the last-refresh timestamp.
    function formatLastRefresh(value) {
        const date = value ? new Date(value) : null;
        if (!date || Number.isNaN(date.getTime())) return '--:--:--';
        return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' });
    }

    // Purpose: Updates the visible last-refresh timestamp.
    function setLastRefresh(value) {
        if (!refreshText) return;
        refreshText.textContent = `Last Refresh: ${formatLastRefresh(value)}`;
        if (refreshControl && value) refreshControl.dataset.lastRefreshUtc = value;
    }

    // Purpose: Returns the current report batch action for the requested direction.
    function countAction() {
        return grid.querySelector('[data-grid-count-action]');
    }

    // Purpose: Updates batch ui.
    function updateBatchUi(payload) {
        currentOffset = Math.max(0, Number(payload?.offset ?? currentOffset));
        currentCursor = String(payload?.currentCursor ?? currentCursor);
        nextCursor = String(payload?.nextCursor ?? '');
        hasMore = Boolean(payload?.hasMore);
        nextCount = Math.max(0, Number(payload?.nextCount || 0));
        const totalCount = Math.max(0, Number(payload?.totalCount ?? maximumEntries));
        const end = Math.max(currentOffset, Number(payload?.end ?? currentOffset));
        const currentRowCount = Math.max(0, end - currentOffset);
        const start = currentRowCount > 0 ? currentOffset + 1 : 0;
        grid.dataset.totalCountOverride = String(totalCount);
        grid.dataset.displayedCountOverride = String(currentRowCount);

        if (rangeStart) rangeStart.textContent = String(start);
        if (rangeEnd) rangeEnd.textContent = String(end);
        if (reportTotalCount) reportTotalCount.textContent = String(totalCount);

        if (previousBatchAction) {
            const previousCount = Math.min(batchSize, currentOffset);
            previousBatchAction.textContent = `Fetch Previous ${previousCount}`;
            previousBatchAction.hidden = batchHistoryIndex <= 0 || previousCount <= 0;
            previousBatchAction.disabled = false;
        }

        let action = countAction();
        if (hasMore && nextCount > 0) {
            if (!action) {
                action = document.createElement('button');
                action.type = 'button';
                action.className = 'grid-count-link';
                action.dataset.gridCountAction = '';
                grid.querySelector('.report-batch-navigation, .grid-count')?.appendChild(action);
            }
            action.textContent = `Fetch Next ${nextCount}`;
            action.hidden = false;
            action.disabled = false;
        } else if (action) {
            action.hidden = true;
        }
    }

    // Purpose: Clears the active automatic-refresh timer.
    function clearRefreshTimer() {
        if (!refreshTimer) return;
        window.clearTimeout(refreshTimer);
        refreshTimer = 0;
    }

    // Purpose: Schedules the next automatic refresh.
    function scheduleRefresh(delayMs = uiRefreshSeconds * 1000) {
        clearRefreshTimer();
        if (uiRefreshSeconds <= 0 || refreshPaused || document.hidden) return;
        refreshTimer = window.setTimeout(() => {
            if (window.mauiIsRowActionMenuOpen?.()) {
                scheduleRefresh(750);
                return;
            }
            loadBatch(currentCursor, currentOffset, false);
        }, Math.max(250, delayMs));
    }

    // Purpose: Updates refresh controls while a refresh request is active.
    function setRefreshBusy(value) {
        refreshInFlight = value;
        refreshClock?.classList.toggle('is-refreshing', value);
        refreshClock?.setAttribute('aria-busy', value ? 'true' : 'false');
        if (refreshNow) refreshNow.disabled = value;
        const action = countAction();
        if (action) action.disabled = value;
        if (previousBatchAction) previousBatchAction.disabled = value;
    }

    // Purpose: Updates refresh controls to match the current paused/running state.
    function updateRefreshUi() {
        if (!refreshToggle) return;
        if (uiRefreshSeconds <= 0) {
            refreshToggle.textContent = 'Automatic Refresh Disabled';
            refreshToggle.disabled = true;
            refreshClock?.classList.add('is-paused');
            return;
        }
        refreshToggle.disabled = false;
        refreshToggle.textContent = refreshPaused ? 'Resume Refresh' : 'Pause Refresh';
        refreshClock?.classList.toggle('is-paused', refreshPaused);
    }

    // Purpose: Loads one report batch and updates paging/grid state.
    async function loadBatch(cursor, offset, manual) {
        if (refreshInFlight || !batchUrl) return false;
        let succeeded = false;
        setRefreshBusy(true);
        clearRefreshTimer();
        try {
            const payload = await fetchJson(buildBatchUrl(cursor, offset), { method: 'GET' });
            closeReportRowActionMenu();
            // Update total override before row replacement so the shared grid renders the report-wide count.
            updateBatchUi(payload);
            grid.dispatchEvent(new CustomEvent('maui:grid-replace-rows', {
                detail: { rows: Array.isArray(payload?.rows) ? payload.rows : [] }
            }));
            setLastRefresh(payload?.refreshedUtc || new Date().toISOString());
            succeeded = true;
        } catch (error) {
            console.warn('Report refresh failed.');
            if (!window.MauiMoveItGridConnection?.isHandled(error)) showStatus(`Report refresh failed: ${error.message}`, 5000, 'error', 'page');
        } finally {
            setRefreshBusy(false);
            scheduleRefresh();
        }
        return succeeded;
    }

    // Purpose: Loads the next available report batch.
    async function fetchNextBatch() {
        if (!hasMore || !nextCursor || refreshInFlight) return;
        const currentRows = grid.querySelectorAll('[data-grid-body] tr[data-row-id]').length;
        const nextOffset = Math.min(maximumEntries, currentOffset + currentRows);
        const nextState = { cursor: nextCursor, offset: nextOffset };
        const previousHistory = batchHistory;
        const previousIndex = batchHistoryIndex;
        batchHistory = batchHistory.slice(0, batchHistoryIndex + 1);
        batchHistory.push(nextState);
        batchHistoryIndex += 1;
        const succeeded = await loadBatch(nextState.cursor, nextState.offset, true);
        if (!succeeded) {
            batchHistory = previousHistory;
            batchHistoryIndex = previousIndex;
            updateBatchUi({
                offset: currentOffset,
                end: currentOffset + grid.querySelectorAll('[data-grid-body] tr[data-row-id]').length,
                totalCount: Number(grid.dataset.totalCountOverride || maximumEntries),
                hasMore, currentCursor, nextCursor, nextCount
            });
        }
    }

    // Purpose: Loads the previous report batch from retained cursor history.
    async function fetchPreviousBatch() {
        if (batchHistoryIndex <= 0 || refreshInFlight) return;
        const previousIndex = batchHistoryIndex;
        const previousState = batchHistory[batchHistoryIndex - 1];
        if (!previousState) return;
        batchHistoryIndex -= 1;
        const succeeded = await loadBatch(previousState.cursor, previousState.offset, true);
        if (!succeeded) {
            batchHistoryIndex = previousIndex;
            updateBatchUi({
                offset: currentOffset,
                end: currentOffset + grid.querySelectorAll('[data-grid-body] tr[data-row-id]').length,
                totalCount: Number(grid.dataset.totalCountOverride || maximumEntries),
                hasMore, currentCursor, nextCursor, nextCount
            });
        }
    }

    // Purpose: Returns the field labels used by the active report-details dialog.
    function reportDetailLabels() {
        switch (String(grid.dataset.grid || '').toLowerCase()) {
            case 'taskruns':
                return { action: 'View Run Details', title: 'Run Details' };
            case 'fileactivity':
                return { action: 'View Activity Details', title: 'Activity Details' };
            default:
                return { action: 'View Audit Details', title: 'Audit Details' };
        }
    }

    // Purpose: Closes report row action menu.
    function closeReportRowActionMenu() {
        reportMenuOpenSequence += 1;
        if (activeReportRowAction?.trigger) activeReportRowAction.trigger.setAttribute('aria-expanded', 'false');
        activeReportRowAction = null;
        if (!reportRowActionMenu) return;
        reportRowActionMenu.hidden = true;
        reportRowActionMenu.style.left = '';
        reportRowActionMenu.style.top = '';
    }

    // Purpose: Positions the report row action menu within the current viewport.
    function positionReportRowActionMenu(trigger) {
        if (!reportRowActionMenu || reportRowActionMenu.hidden || !trigger) return;
        const triggerRect = trigger.getBoundingClientRect();
        const menuRect = reportRowActionMenu.getBoundingClientRect();
        const margin = 8;
        const gap = 5;

        let left = triggerRect.right - menuRect.width;
        left = Math.max(margin, Math.min(left, window.innerWidth - menuRect.width - margin));

        let top = triggerRect.bottom + gap;
        if (top + menuRect.height > window.innerHeight - margin) {
            top = Math.max(margin, triggerRect.top - menuRect.height - gap);
        }

        reportRowActionMenu.style.left = `${Math.round(left)}px`;
        reportRowActionMenu.style.top = `${Math.round(top)}px`;
    }

    // Purpose: Navigates to a report while preserving the relevant return context.
    function navigateToReport(baseUrl, parameters) {
        const destination = String(baseUrl || '').trim();
        if (!destination) return;
        const url = new URL(destination, window.location.href);
        Object.entries(parameters || {}).forEach(([name, value]) => {
            const text = String(value ?? '').trim();
            if (text) url.searchParams.set(name, text);
        });
        const sourceGrid = String(grid?.dataset?.grid || '').toLowerCase();
        const returnLabel = sourceGrid === 'taskruns' ? 'Task Runs' : sourceGrid === 'fileactivity' ? 'File Activity' : sourceGrid === 'audit' ? 'Audit' : 'Previous Page';
        const returnUrl = sourceGrid
            ? window.MauiGridNavigationState?.createReturnUrl?.(sourceGrid)
            : '';
        url.searchParams.set('returnUrl', returnUrl || `${window.location.pathname}${window.location.search}${window.location.hash}`);
        url.searchParams.set('returnLabel', returnLabel);
        window.location.assign(url.toString());
    }

    // Purpose: Returns the Task context represented by the active report row.
    function activeTaskContext() {
        const values = getReportRowValues(activeReportRowAction?.row);
        const taskState = activeReportRowAction?.taskState || null;
        return {
            taskId: String(taskState?.id ?? values.TaskID ?? values.TaskId ?? '').trim(),
            taskName: String(taskState?.name ?? values.TaskName ?? '').trim(),
            enabled: typeof taskState?.enabled === 'boolean' ? taskState.enabled : null,
            runnable: typeof taskState?.runnable === 'boolean' ? taskState.runnable : null
        };
    }

    // Purpose: Loads the current Task state needed by Task Run row actions without loading Task metadata.
    async function loadTaskActionState(taskId) {
        const baseUrl = endpoint('taskActionStateUrl');
        const id = String(taskId || '').trim();
        if (!baseUrl || !id) return null;
        const url = new URL(baseUrl, window.location.href);
        url.searchParams.set('id', id);
        const payload = await fetchJson(url.toString());
        const task = payload?.task || null;
        if (!task) return null;
        const runnable = typeof task.runnable === 'boolean'
            ? task.runnable
            : (typeof task.canRun === 'boolean' ? task.canRun : null);
        return {
            id: String(task.id || id).trim(),
            name: String(task.name || id).trim(),
            enabled: typeof task.enabled === 'boolean' ? task.enabled : null,
            runnable
        };
    }

    // Purpose: Copies a Task name using the same browser-compatible clipboard behavior as the Tasks workspace.
    async function copyTaskNameToClipboard(taskName) {
        const value = String(taskName || '').trim();
        if (!value) return;

        let clipboardError = null;
        if (navigator.clipboard?.writeText) {
            try {
                await navigator.clipboard.writeText(value);
                showStatus('Copied to clipboard.', 5000);
                return;
            } catch (error) {
                clipboardError = error;
            }
        }

        const textarea = document.createElement('textarea');
        textarea.value = value;
        textarea.setAttribute('readonly', '');
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        try {
            textarea.select();
            const copied = document.execCommand('copy');
            if (!copied) throw clipboardError || new Error('Clipboard copy was not accepted by the browser.');
            showStatus('Copied to clipboard.', 5000);
        } catch (error) {
            console.warn('MAUI could not copy the Task name to the clipboard.');
            showStatus('Unable to copy Task name to clipboard.', 5000, 'error');
        } finally {
            textarea.remove();
        }
    }

    // Purpose: Enables or disables the selected Task through the existing Tasks action endpoint.
    async function setTaskEnabled(task, enabled) {
        const id = String(task?.taskId || '').trim();
        if (!id) return;
        const url = endpoint(enabled ? 'enableTasksUrl' : 'disableTasksUrl');
        if (!url) return;

        try {
            const payload = await fetchJson(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ taskIds: [id] })
            });
            const item = Array.isArray(payload?.items) ? payload.items[0] : null;
            if (!item?.success) throw new Error(item?.message || `Unable to ${enabled ? 'enable' : 'disable'} the Task.`);
            const name = String(item?.name || task.taskName || id).trim();
            showStatus(item?.skipped ? (item?.message || `${name} is already ${enabled ? 'enabled' : 'disabled'}.`) : `${enabled ? 'Enabled' : 'Disabled'} Task ${name}.`, 5000);
        } catch (error) {
            showStatus(`${enabled ? 'Enable' : 'Disable'} Task: ${error?.message || 'Unable to update the Task.'}`, 5000, 'error');
        }
    }

    // Purpose: Starts the selected Task through the existing Tasks action endpoint.
    async function runTaskNow(task) {
        const id = String(task?.taskId || '').trim();
        if (!id) return;
        const url = endpoint('runTaskNowUrl');
        if (!url) return;

        try {
            const payload = await fetchJson(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ taskId: id })
            });
            const name = String(payload?.taskName || task.taskName || id).trim();
            showStatus(`Started task ${name}.`, 5000);
            await loadBatch(currentCursor, currentOffset, true);
        } catch (error) {
            showStatus(`Run Task Now: ${error?.message || 'Unable to start the task.'}`, 5000, 'error');
        }
    }

    // Purpose: Opens report row action menu immediately, then hydrates only the actions that require live Task state.
    async function openReportRowActionMenu(trigger) {
        if (!reportRowActionMenu || !trigger) return;
        if (activeReportRowAction?.trigger === trigger && !reportRowActionMenu.hidden) {
            closeReportRowActionMenu();
            return;
        }

        closeReportRowActionMenu();
        const requestSequence = ++reportMenuOpenSequence;
        const row = trigger.closest('tr[data-row-id]');
        if (!row) return;

        const labels = reportDetailLabels();
        if (reportRowDetailsCommand) reportRowDetailsCommand.textContent = labels.action;
        const reportType = String(grid.dataset.grid || '').toLowerCase();
        const isTaskRun = reportType === 'taskruns';
        const isFileActivity = reportType === 'fileactivity';
        const isAudit = reportType === 'audit';
        const rowValues = getReportRowValues(row);
        if (reportRowChangesCommand) {
            const available = isAudit && String(rowValues.HasAuditDiff ?? '') === '1'
                && /^[1-9]\d*$/.test(String(rowValues.LogID ?? '')) && Boolean(window.MauiAuditChanges);
            reportRowChangesCommand.hidden = !available;
            reportRowChangesCommand.disabled = !available;
        }
        const taskId = String(rowValues.TaskID ?? rowValues.TaskId ?? '').trim();
        const hasTask = Boolean(taskId);
        const hasRunContext = Boolean(String(rowValues.NominalStart ?? '').trim());

        const actionContext = { trigger, row, taskState: null };
        activeReportRowAction = actionContext;

        // The menu itself must never wait on MOVEit. Historical/detail actions are immediately available from the row.
        if (reportTaskInformationCommand) {
            reportTaskInformationCommand.disabled = !isTaskRun || !hasTask;
            reportTaskInformationCommand.title = hasTask ? '' : 'Task Information is not available for this report row.';
        }
        if (reportCopyTaskNameCommand) reportCopyTaskNameCommand.disabled = !isTaskRun || !hasTask;
        if (reportEditTaskCommand) {
            reportEditTaskCommand.disabled = !isTaskRun || !hasTask;
            reportEditTaskCommand.title = hasTask ? '' : 'The current Task is unavailable.';
        }
        if (reportToggleTaskEnabledCommand) {
            reportToggleTaskEnabledCommand.textContent = 'Enable Task';
            reportToggleTaskEnabledCommand.disabled = true;
            reportToggleTaskEnabledCommand.title = hasTask ? 'Loading current Task state…' : 'The current Task state is unavailable.';
        }
        if (reportRunTaskCommand) {
            reportRunTaskCommand.disabled = true;
            reportRunTaskCommand.title = hasTask ? 'Loading current Task state…' : 'The current Task state is unavailable.';
        }
        if (reportRowRunHistoryCommand) {
            const show = isFileActivity;
            const available = show && hasTask;
            reportRowRunHistoryCommand.hidden = !show;
            reportRowRunHistoryCommand.disabled = !available;
            reportRowRunHistoryCommand.title = available ? '' : (show ? 'Run History is not available for this report row.' : '');
        }
        if (reportRowFileActivityCommand) {
            const show = isTaskRun;
            const available = show && hasTask && hasRunContext;
            reportRowFileActivityCommand.hidden = !show;
            reportRowFileActivityCommand.disabled = !available;
            reportRowFileActivityCommand.title = available ? '' : (show ? 'File Activity for this run is not available for this report row.' : '');
            reportRowFileActivityCommand.textContent = 'View File Activity (This Run)';
        }
        if (reportRowAuditCommand) {
            const show = !isAudit && (isTaskRun || isFileActivity);
            const available = show && hasTask;
            reportRowAuditCommand.hidden = !show;
            reportRowAuditCommand.disabled = !available;
            reportRowAuditCommand.title = available ? '' : (show ? 'Audit Records are not available for this report row.' : '');
        }

        reportRowActionMenu.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
        positionReportRowActionMenu(trigger);

        // Only Enable/Disable and Run require the Task's current live state. Hydrate them after the menu is visible.
        if (!isTaskRun || !hasTask) return;
        try {
            const taskState = await loadTaskActionState(taskId);
            if (requestSequence !== reportMenuOpenSequence || activeReportRowAction !== actionContext || reportRowActionMenu.hidden) return;
            actionContext.taskState = taskState;
            const stateKnown = Boolean(taskState);
            if (reportToggleTaskEnabledCommand) {
                reportToggleTaskEnabledCommand.textContent = stateKnown && taskState.enabled ? 'Disable Task' : 'Enable Task';
                reportToggleTaskEnabledCommand.disabled = !stateKnown || typeof taskState.enabled !== 'boolean';
                reportToggleTaskEnabledCommand.title = stateKnown ? '' : 'The current Task state is unavailable.';
            }
            if (reportRunTaskCommand) {
                const runnableKnown = stateKnown && typeof taskState.runnable === 'boolean';
                reportRunTaskCommand.disabled = !runnableKnown || !taskState.runnable;
                reportRunTaskCommand.title = runnableKnown && !taskState.runnable
                    ? 'Incomplete Tasks cannot be run.'
                    : (runnableKnown ? '' : 'The current Task state is unavailable.');
            }
            positionReportRowActionMenu(trigger);
        } catch (error) {
            if (requestSequence !== reportMenuOpenSequence || activeReportRowAction !== actionContext || reportRowActionMenu.hidden) return;
            if (reportToggleTaskEnabledCommand) {
                reportToggleTaskEnabledCommand.disabled = true;
                reportToggleTaskEnabledCommand.title = 'The current Task state is unavailable.';
            }
            if (reportRunTaskCommand) {
                reportRunTaskCommand.disabled = true;
                reportRunTaskCommand.title = 'The current Task state is unavailable.';
            }
            showStatus(`Task actions: ${error?.message || 'Unable to load the current Task state.'}`, 5000, 'error');
        }
    }

    // Purpose: Returns report row values.
    function getReportRowValues(row) {
        try {
            const parsed = JSON.parse(row?.dataset?.rowValues || '{}');
            return parsed && typeof parsed === 'object' ? parsed : {};
        } catch {
            return {};
        }
    }

    // Purpose: Opens report details.
    function openReportDetails() {
        const row = activeReportRowAction?.row;
        if (!row || !reportDetailsDialog || !reportDetailsBody) return;

        const values = getReportRowValues(row);
        const labels = reportDetailLabels();
        if (reportDetailsTitle) reportDetailsTitle.textContent = labels.title;

        const fragment = document.createDocumentFragment();
        grid.querySelectorAll('.column-header-row th[data-column]').forEach(header => {
            const key = String(header.dataset.column || '').trim();
            if (!key) return;

            const label = header.querySelector('.sort-button span')?.textContent?.trim() || key;
            const tableRow = document.createElement('tr');
            const fieldCell = document.createElement('th');
            fieldCell.scope = 'row';
            fieldCell.textContent = label;
            const valueCell = document.createElement('td');
            const value = String(values[key] ?? '');
            valueCell.textContent = value || '—';
            tableRow.append(fieldCell, valueCell);
            fragment.appendChild(tableRow);
        });

        reportDetailsBody.replaceChildren(fragment);
        closeReportRowActionMenu();
        reportDetailsDialog.showModal();
    }

    // Purpose: Toggles automatic report refresh.
    function toggleRefresh() {
        if (uiRefreshSeconds <= 0) return;
        refreshPaused = !refreshPaused;
        updateRefreshUi();
        refreshToggle?.closest('[data-menu]')?.classList.remove('open');
        if (refreshPaused) clearRefreshTimer();
        else loadBatch(currentCursor, currentOffset, false);
    }

    // Purpose: Opens the referenced Task using the configured editor preference.
    async function launchTaskEditor(taskOrButton, taskNameArgument = '') {
        const isElement = taskOrButton instanceof Element;
        const taskId = String(isElement ? taskOrButton.dataset.rowId : taskOrButton || '').trim();
        const taskName = String(isElement ? (taskOrButton.dataset.taskName || taskOrButton.textContent || '') : taskNameArgument || '').trim();
        try { return await window.MauiEditorRouting.openTask(taskId, taskName || ''); }
        catch (error) { showStatus(error?.message || 'Unable to open the Task Editor.', 5000, 'error'); return false; }
    }
    const taskInformation = String(grid.dataset.grid || '').toLowerCase() === 'taskruns'
        ? window.MauiTaskInformation?.initialize({
            configElement: config,
            antiForgeryToken,
            onStatus: showStatus,
        })
        : null;

    document.addEventListener('click', event => {
        const rowMenuButton = event.target.closest('[data-report-row-menu-button]');
        if (rowMenuButton && grid.contains(rowMenuButton)) {
            event.preventDefault();
            event.stopPropagation();
            openReportRowActionMenu(rowMenuButton);
            return;
        }

        if (reportRowActionMenu && !reportRowActionMenu.hidden && !event.target.closest('[data-report-row-action-menu]')) {
            closeReportRowActionMenu();
        }

        const previousAction = event.target.closest('[data-report-fetch-previous]');
        if (previousAction && grid.contains(previousAction)) {
            event.preventDefault();
            fetchPreviousBatch();
            return;
        }

        const action = event.target.closest('[data-grid-count-action]');
        if (action && grid.contains(action)) {
            event.preventDefault();
            fetchNextBatch();
            return;
        }

        const taskLink = event.target.closest('[data-native-task-editor]');
        if (taskLink && grid.contains(taskLink)) {
            event.preventDefault();
            launchTaskEditor(taskLink);
        }
    });

    reportTaskInformationCommand?.addEventListener('click', event => {
        event.preventDefault();
        const task = activeTaskContext();
        closeReportRowActionMenu();
        if (!task.taskId) return;
        taskInformation?.open(task.taskId);
    });

    reportCopyTaskNameCommand?.addEventListener('click', event => {
        event.preventDefault();
        const task = activeTaskContext();
        closeReportRowActionMenu();
        if (!task.taskId) return;
        copyTaskNameToClipboard(task.taskName || task.taskId);
    });

    reportEditTaskCommand?.addEventListener('click', event => {
        event.preventDefault();
        const task = activeTaskContext();
        closeReportRowActionMenu();
        if (!task.taskId) return;
        launchTaskEditor(task.taskId, task.taskName);
    });

    reportToggleTaskEnabledCommand?.addEventListener('click', event => {
        event.preventDefault();
        const task = activeTaskContext();
        closeReportRowActionMenu();
        if (!task.taskId || typeof task.enabled !== 'boolean') return;
        setTaskEnabled(task, !task.enabled);
    });

    reportRunTaskCommand?.addEventListener('click', event => {
        event.preventDefault();
        const task = activeTaskContext();
        closeReportRowActionMenu();
        if (!task.taskId || task.runnable !== true) return;
        runTaskNow(task);
    });

    reportRowDetailsCommand?.addEventListener('click', event => {
        event.preventDefault();
        openReportDetails();
    });

    reportRowChangesCommand?.addEventListener('click', event => {
        event.preventDefault();
        const values = getReportRowValues(activeReportRowAction?.row);
        closeReportRowActionMenu();
        if (String(values.HasAuditDiff ?? '') !== '1') return;
        window.MauiAuditChanges?.open(String(values.LogID ?? ''), String(values.TargetName || values.Action || ''));
    });

    reportRowRunHistoryCommand?.addEventListener('click', event => {
        event.preventDefault();
        const task = activeTaskContext();
        closeReportRowActionMenu();
        if (!task.taskId) return;
        navigateToReport(endpoint('taskRunReportUrl'), { taskId: task.taskId, taskName: task.taskName });
    });

    reportRowFileActivityCommand?.addEventListener('click', event => {
        event.preventDefault();
        const task = activeTaskContext();
        const values = getReportRowValues(activeReportRowAction?.row);
        const runId = String(values.RunID ?? values.RunId ?? '').trim();
        const nominalStart = String(values.NominalStart ?? '').trim();
        closeReportRowActionMenu();
        if (!task.taskId || !nominalStart) {
            showStatus('The selected task run does not include a run identifier for File Activity.', 5000);
            return;
        }
        navigateToReport(endpoint('fileActivityReportUrl'), {
            taskId: task.taskId,
            taskName: task.taskName,
            runId,
            nominalStart
        });
    });

    reportRowAuditCommand?.addEventListener('click', event => {
        event.preventDefault();
        const task = activeTaskContext();
        closeReportRowActionMenu();
        if (!task.taskId) return;
        navigateToReport(endpoint('auditReportUrl'), { targetType: 'task', targetId: task.taskId, targetName: task.taskName });
    });

    refreshToggle?.addEventListener('click', toggleRefresh);
    refreshNow?.addEventListener('click', () => {
        refreshNow.closest('[data-menu]')?.classList.remove('open');
        loadBatch(currentCursor, currentOffset, true);
    });

    document.addEventListener('visibilitychange', () => {
        if (document.hidden) clearRefreshTimer();
        else if (!refreshPaused && uiRefreshSeconds > 0) loadBatch(currentCursor, currentOffset, false);
    });

    window.addEventListener('resize', () => {
        if (activeReportRowAction?.trigger && reportRowActionMenu && !reportRowActionMenu.hidden) {
            positionReportRowActionMenu(activeReportRowAction.trigger);
        }
    });
    window.addEventListener('scroll', closeReportRowActionMenu, true);

    setLastRefresh(refreshControl?.dataset.lastRefreshUtc || '');
    updateRefreshUi();
    updateBatchUi({
        offset: currentOffset,
        end: currentOffset,
        totalCount: 0,
        hasMore: false,
        currentCursor,
        nextCursor: '',
        nextCount: 0
    });

    // Loads the first native MOVEit Records batch while the shared Loading shell is visible.
    // The page HTML contains no report rows, matching the proven Tasks/Hosts/Upcoming Runs pattern.
    async function loadInitialRecordsGrid() {
        try {
            await loadBatch('', 0, false);
        } finally {
            grid.dispatchEvent(new CustomEvent('maui:grid-initial-data-ready'));
        }
    }

    loadInitialRecordsGrid();
})();
