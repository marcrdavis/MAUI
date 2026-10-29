/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Upcoming Runs page behavior: 60-second forecast refresh and Task row actions.
 */

(() => {
    const config = document.querySelector('[data-upcoming-runs-config]');
    const grid = document.querySelector('[data-grid="upcomingruns"]');
    if (!config || !grid) return;

    const antiForgeryToken = document.querySelector('.upcoming-runs-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const toast = grid.querySelector('[data-prototype-toast]');
    const errorBanner = document.querySelector('[data-upcoming-runs-error]');
    const warning = document.querySelector('[data-upcoming-runs-warning]');
    const warningText = document.querySelector('[data-upcoming-runs-warning-text]');
    const actionMenu = document.querySelector('[data-upcoming-task-row-action-menu]');
    const taskInformationCommand = actionMenu?.querySelector('[data-upcoming-task-row-command="information"]');
    const taskDetailsCommand = actionMenu?.querySelector('[data-upcoming-task-row-command="details"]');
    const copyTaskNameCommand = actionMenu?.querySelector('[data-upcoming-task-row-command="copy-name"]');
    const editTaskCommand = actionMenu?.querySelector('[data-upcoming-task-row-command="edit"]');
    const toggleTaskEnabledCommand = actionMenu?.querySelector('[data-upcoming-task-row-command="toggle-enabled"]');
    const runTaskCommand = actionMenu?.querySelector('[data-upcoming-task-row-command="run"]');
    const runDetailsDialog = document.querySelector('[data-upcoming-run-details-dialog]');
    const runDetailsBody = runDetailsDialog?.querySelector('[data-upcoming-run-details-body]');
    const REFRESH_MS = 60_000;

    let refreshTimer = null;
    let refreshInFlight = false;
    let refreshQueued = false;
    let refreshQueuedManual = false;
    let actionInFlight = false;
    let activeAction = null;
    let actionMenuOpenSequence = 0;

    function endpoint(name) {
        return config.dataset[name] || '';
    }

    function showStatus(message, durationMs = 5000, kind = 'default', origin = 'modal') {
        if (typeof window.mauiDisplayToast === 'function') { window.mauiDisplayToast(toast, message, kind, durationMs, origin); return; }
        if (!toast) return;
        toast.textContent = message || '';
        toast.classList.toggle('error', kind === 'error');
        toast.classList.add('visible');
        window.clearTimeout(toast._mauiUpcomingRunsTimer);
        toast._mauiUpcomingRunsTimer = window.setTimeout(() => toast.classList.remove('visible'), durationMs);
    }

    async function fetchJson(url, options = {}) {
        const headers = new Headers(options.headers || {});
        headers.set('Accept', 'application/json');
        if (options.body) headers.set('Content-Type', 'application/json');
        if (antiForgeryToken && options.method && options.method.toUpperCase() !== 'GET') {
            headers.set('RequestVerificationToken', antiForgeryToken);
        }

        const response = await (window.MauiMoveItGridConnection?.fetch || window.fetch.bind(window))(url, { ...options, headers, cache: options.cache || 'no-store' });
        if (response.status === 401) {
            window.location.replace(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
            throw new Error('MOVEit session expired.');
        }

        if (response.redirected && /\/Account\/(Login|SessionExpired)/i.test(new URL(response.url, window.location.href).pathname)) {
            window.location.replace(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
            throw new Error('MOVEit session expired.');
        }

        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json') ? await response.json() : null;
        if (!response.ok) {
            throw new Error(payload?.message || `Request failed (${response.status}).`);
        }
        return payload;
    }

    function updateWarning(count) {
        const unsupported = Number(count || 0);
        if (!warning || !warningText) return;
        warning.hidden = unsupported <= 0;
        if (unsupported > 0) {
            warningText.textContent = `${unsupported} schedule(s) contained definitions MAUI could not project safely; affected occurrences were omitted. Details were written to MAUI Logs.`;
        }
    }

    function clearRefreshTimer() {
        if (refreshTimer) window.clearTimeout(refreshTimer);
        refreshTimer = null;
    }

    function scheduleRefresh() {
        clearRefreshTimer();
        if (document.hidden) return;
        refreshTimer = window.setTimeout(() => refreshUpcomingRuns(false), REFRESH_MS);
    }

    // Applies a complete forecast row set through the reusable-grid replacement contract.
    function applyUpcomingRunsPayload(data) {
        grid.dispatchEvent(new CustomEvent('maui:grid-replace-rows', {
            detail: { rows: Array.isArray(data?.rows) ? data.rows : [] }
        }));
        updateWarning(data?.unsupportedScheduleCount);
        if (errorBanner) {
            errorBanner.hidden = true;
            errorBanner.textContent = '';
        }
    }

    // Loads the first forecast while the shared reusable-grid loading shell remains visible. The initial HTML
    // contains no forecast rows, so a large table cannot expand the page before the fixed-height grid viewport exists.
    async function loadInitialUpcomingRuns() {
        if (refreshInFlight) return;
        refreshInFlight = true;
        clearRefreshTimer();
        try {
            const data = await fetchJson(endpoint('refreshUrl'));
            applyUpcomingRunsPayload(data);
        } catch (error) {
            console.warn('Initial Upcoming Runs load failed.');
            if (errorBanner && !window.MauiMoveItGridConnection?.isHandled(error)) {
                errorBanner.textContent = `Upcoming Runs could not be loaded: ${error.message}`;
                errorBanner.hidden = false;
                requestAnimationFrame(() => window.dispatchEvent(new Event('resize')));
            }
        } finally {
            grid.dispatchEvent(new CustomEvent('maui:grid-initial-data-ready'));
            refreshInFlight = false;
            scheduleRefresh();
        }
    }

    async function refreshUpcomingRuns(manual, queueIfBusy = false) {
        if (document.hidden) return;
        if (refreshInFlight) {
            if (queueIfBusy) {
                refreshQueued = true;
                refreshQueuedManual = refreshQueuedManual || manual;
            }
            return;
        }
        refreshInFlight = true;
        clearRefreshTimer();
        closeActionMenu();
        try {
            const data = await fetchJson(endpoint('refreshUrl'));
            applyUpcomingRunsPayload(data);
        } catch (error) {
            console.warn('Upcoming Runs refresh failed.');
            if (window.MauiMoveItGridConnection?.isHandled(error) && errorBanner) { errorBanner.hidden = true; errorBanner.textContent = ''; }
            if (manual && !window.MauiMoveItGridConnection?.isHandled(error)) showStatus(`Upcoming Runs refresh failed: ${error.message}`, 5000, 'error', 'page');
        } finally {
            refreshInFlight = false;
            if (refreshQueued && !document.hidden) {
                const queuedManual = refreshQueuedManual;
                refreshQueued = false;
                refreshQueuedManual = false;
                window.setTimeout(() => refreshUpcomingRuns(queuedManual, false), 0);
            } else {
                refreshQueued = false;
                refreshQueuedManual = false;
                scheduleRefresh();
            }
        }
    }

    function closeActionMenu() {
        actionMenuOpenSequence += 1;
        if (activeAction?.trigger) activeAction.trigger.setAttribute('aria-expanded', 'false');
        activeAction = null;
        if (!actionMenu) return;
        actionMenu.hidden = true;
        actionMenu.style.left = '';
        actionMenu.style.top = '';
    }

    function positionActionMenu(trigger) {
        if (!actionMenu || actionMenu.hidden) return;
        const triggerRect = trigger.getBoundingClientRect();
        const menuRect = actionMenu.getBoundingClientRect();
        const margin = 8;
        const gap = 5;
        let left = triggerRect.right - menuRect.width;
        left = Math.max(margin, Math.min(left, window.innerWidth - menuRect.width - margin));
        let top = triggerRect.bottom + gap;
        if (top + menuRect.height > window.innerHeight - margin) {
            top = Math.max(margin, triggerRect.top - menuRect.height - gap);
        }
        actionMenu.style.left = `${Math.round(left)}px`;
        actionMenu.style.top = `${Math.round(top)}px`;
    }

    function getUpcomingRowValues(row) {
        try {
            const parsed = JSON.parse(row?.dataset?.rowValues || '{}');
            return parsed && typeof parsed === 'object' ? parsed : {};
        } catch {
            return {};
        }
    }

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

    async function openActionMenu(trigger) {
        if (!actionMenu || !trigger) return;
        if (activeAction?.trigger === trigger && !actionMenu.hidden) {
            closeActionMenu();
            return;
        }

        closeActionMenu();
        const requestSequence = ++actionMenuOpenSequence;
        const row = trigger.closest('tr[data-row-id]');
        const taskId = String(trigger.dataset.taskId || '').trim();
        if (!row || !taskId) return;

        const actionContext = {
            trigger,
            row,
            taskId,
            taskName: String(trigger.dataset.taskName || taskId).trim(),
            infoUrl: String(trigger.dataset.taskInfoUrl || '').trim(),
            taskState: null
        };
        activeAction = actionContext;

        // Open immediately. Only state-dependent commands wait for the current Task state.
        if (taskInformationCommand) {
            taskInformationCommand.disabled = false;
            taskInformationCommand.title = '';
        }
        if (taskDetailsCommand) taskDetailsCommand.disabled = false;
        if (copyTaskNameCommand) copyTaskNameCommand.disabled = !actionContext.taskName;
        if (editTaskCommand) {
            editTaskCommand.disabled = false;
            editTaskCommand.title = '';
        }
        if (toggleTaskEnabledCommand) {
            toggleTaskEnabledCommand.textContent = 'Enable Task';
            toggleTaskEnabledCommand.disabled = true;
            toggleTaskEnabledCommand.title = 'Loading current Task state…';
        }
        if (runTaskCommand) {
            runTaskCommand.disabled = true;
            runTaskCommand.title = 'Loading current Task state…';
        }

        actionMenu.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
        positionActionMenu(trigger);

        try {
            const taskState = await loadTaskActionState(taskId);
            if (requestSequence !== actionMenuOpenSequence || activeAction !== actionContext || actionMenu.hidden) return;
            actionContext.taskState = taskState;
            if (taskState?.name) actionContext.taskName = taskState.name;

            const stateKnown = Boolean(taskState);
            if (toggleTaskEnabledCommand) {
                const enabledKnown = stateKnown && typeof taskState.enabled === 'boolean';
                toggleTaskEnabledCommand.textContent = enabledKnown && taskState.enabled ? 'Disable Task' : 'Enable Task';
                toggleTaskEnabledCommand.disabled = !enabledKnown;
                toggleTaskEnabledCommand.title = enabledKnown ? '' : 'The current Task state is unavailable.';
            }
            if (runTaskCommand) {
                const runnableKnown = stateKnown && typeof taskState.runnable === 'boolean';
                runTaskCommand.disabled = !runnableKnown || !taskState.runnable;
                runTaskCommand.title = runnableKnown && !taskState.runnable
                    ? 'Incomplete Tasks cannot be run.'
                    : (runnableKnown ? '' : 'The current Task state is unavailable.');
            }
            positionActionMenu(trigger);
        } catch (error) {
            if (requestSequence !== actionMenuOpenSequence || activeAction !== actionContext || actionMenu.hidden) return;
            if (toggleTaskEnabledCommand) {
                toggleTaskEnabledCommand.disabled = true;
                toggleTaskEnabledCommand.title = 'The current Task state is unavailable.';
            }
            if (runTaskCommand) {
                runTaskCommand.disabled = true;
                runTaskCommand.title = 'The current Task state is unavailable.';
            }
            showStatus(`Task actions: ${error?.message || 'Unable to load the current Task state.'}`, 5000, 'error');
        }
    }

    function navigateToReport(baseUrl, parameters) {
        const target = String(baseUrl || '').trim();
        if (!target) return;
        const url = new URL(target, window.location.href);
        Object.entries(parameters || {}).forEach(([key, value]) => {
            const text = String(value ?? '').trim();
            if (text) url.searchParams.set(key, text);
        });
        const returnUrl = window.MauiGridNavigationState?.createReturnUrl?.('upcomingruns')
            || `${window.location.pathname}${window.location.search}${window.location.hash}`;
        url.searchParams.set('returnUrl', returnUrl);
        url.searchParams.set('returnLabel', 'Upcoming Runs');
        window.location.assign(url.toString());
    }

    const taskInformation = window.MauiTaskInformation?.initialize({
        configElement: config,
        antiForgeryToken,
        onStatus: showStatus,
        onSaved: async () => { await refreshUpcomingRuns(true, true); }
    });

    function openRunDetails(action) {
        const row = action?.row;
        if (!row || !runDetailsDialog || !runDetailsBody) return;

        const values = getUpcomingRowValues(row);
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

        runDetailsBody.replaceChildren(fragment);
        runDetailsDialog.showModal();
    }

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

    function closeReservedPopup(popup) {
        try { if (popup && !popup.closed) popup.close(); } catch { /* ignored */ }
    }

    async function launchTaskEditor(taskId, taskName) {
        try { return await window.MauiEditorRouting.openTask(taskId, taskName || ''); }
        catch (error) { showStatus(error?.message || 'Unable to open the Task Editor.', 5000, 'error'); return false; }
    }
    async function runTaskNow(action) {
        if (actionInFlight || !action?.taskId) return;
        actionInFlight = true;
        try {
            const data = await fetchJson(endpoint('runTaskNowUrl'), {
                method: 'POST',
                body: JSON.stringify({ taskId: action.taskId })
            });
            showStatus(`Started task ${data?.taskName || action.taskName || action.taskId}.`, 5000);
        } catch (error) {
            showStatus(`Run Task Now: ${error.message || 'Unable to start the task.'}`, 5000, 'error');
        } finally {
            actionInFlight = false;
        }
    }

    async function setTaskEnabled(action, enabled) {
        if (actionInFlight || !action?.taskId) return;
        actionInFlight = true;
        const verb = enabled ? 'Enable' : 'Disable';
        try {
            const data = await fetchJson(endpoint(enabled ? 'enableTasksUrl' : 'disableTasksUrl'), {
                method: 'POST',
                body: JSON.stringify({ taskIds: [action.taskId] })
            });
            const failed = Number(data?.failed || 0);
            const skipped = Number(data?.skipped || 0);
            if (failed > 0) {
                const failure = Array.isArray(data?.items) ? data.items.find(item => item?.success === false) : null;
                showStatus(`${verb} Task: ${failure?.message || `Unable to ${enabled ? 'enable' : 'disable'} the task.`}`, 5000, 'error');
            } else if (skipped > 0) {
                showStatus(`Task ${action.taskName || action.taskId} is already ${enabled ? 'enabled' : 'disabled'}.`, 5000);
            } else {
                showStatus(`${enabled ? 'Enabled' : 'Disabled'} task ${action.taskName || action.taskId}.`, 5000);
            }
            await refreshUpcomingRuns(true, true);
        } catch (error) {
            showStatus(`${verb} Task: ${error.message || `Unable to ${enabled ? 'enable' : 'disable'} the task.`}`, 5000, 'error');
        } finally {
            actionInFlight = false;
        }
    }

    function handleActionCommand(command) {
        const action = activeAction;
        if (!action) return;
        closeActionMenu();

        if (command === 'information') { taskInformation?.open(action.taskId, action.infoUrl); return; }
        if (command === 'details') { openRunDetails(action); return; }
        if (command === 'file-activity') { navigateToReport(endpoint('fileActivityReportUrl'), { taskId: action.taskId, taskName: action.taskName }); return; }
        if (command === 'audit') { navigateToReport(endpoint('auditReportUrl'), { targetType: 'task', targetId: action.taskId, targetName: action.taskName }); return; }
        if (command === 'copy-name') { copyTaskNameToClipboard(action.taskName); return; }
        if (command === 'edit') { launchTaskEditor(action.taskId, action.taskName); return; }
        if (command === 'toggle-enabled') {
            const enabled = action.taskState?.enabled;
            if (typeof enabled === 'boolean') setTaskEnabled(action, !enabled);
            return;
        }
        if (command === 'run' && action.taskState?.runnable === true) runTaskNow(action);
    }

    document.addEventListener('click', event => {
        const menuButton = event.target.closest('[data-upcoming-task-row-menu-button]');
        if (menuButton && grid.contains(menuButton)) {
            event.preventDefault();
            event.stopPropagation();
            openActionMenu(menuButton);
            return;
        }

        const command = event.target.closest('[data-upcoming-task-row-command]');
        if (command && actionMenu?.contains(command)) {
            event.preventDefault();
            handleActionCommand(String(command.dataset.upcomingTaskRowCommand || ''));
            return;
        }

        const taskLink = event.target.closest('[data-native-task-editor]');
        if (taskLink && grid.contains(taskLink)) {
            event.preventDefault();
            closeActionMenu();
            launchTaskEditor(taskLink.dataset.rowId, taskLink.dataset.taskName || taskLink.textContent || '');
            return;
        }

        if (actionMenu && !event.target.closest('[data-upcoming-task-row-action-menu]')) closeActionMenu();
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') closeActionMenu();
    });
    window.addEventListener('resize', closeActionMenu);
    window.addEventListener('scroll', closeActionMenu, true);

    document.addEventListener('visibilitychange', () => {
        if (document.hidden) clearRefreshTimer();
        else refreshUpcomingRuns(false);
    });

    loadInitialUpcomingRuns();
})();
