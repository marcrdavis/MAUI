// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Quick Transfer workspace.
(() => {
    'use strict';

    const root = document.querySelector('[data-quick-transfer-config]');
    if (!root) return;

    const cfg = root.dataset;
    const antiForgeryToken = document.querySelector('.task-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const errorBox = root.querySelector('[data-quick-transfer-error]');
    const infoBox = root.querySelector('[data-quick-transfer-info]');
    const sourceHost = root.querySelector('[data-quick-source-host]');
    const sourcePath = root.querySelector('[data-quick-source-path]');
    const sourceFile = root.querySelector('[data-quick-source-file]');
    const deleteSourceAfterSuccess = root.querySelector('[data-quick-delete-source-after-success]');
    const sourceRootSummary = root.querySelector('[data-quick-source-root-summary]');
    const destinationHost = root.querySelector('[data-quick-destination-host]');
    const destinationPath = root.querySelector('[data-quick-destination-path]');
    const destinationFile = root.querySelector('[data-quick-destination-file]');
    const overwriteExisting = root.querySelector('[data-quick-overwrite-existing]');
    const destinationRootSummary = root.querySelector('[data-quick-destination-root-summary]');
    const pgpRow = root.querySelector('[data-quick-pgp-row]');
    const pgpKey = root.querySelector('[data-quick-pgp-key]');
    const pgpProcessingCards = Array.from(root.querySelectorAll('[data-quick-pgp-processing-option]'));
    const pgpProcessingInputs = pgpProcessingCards.map(card => card.querySelector('input[name="quickProcessingMode"]')).filter(Boolean);
    const persistTask = root.querySelector('[data-quick-persist-task]');
    const taskNameRow = root.querySelector('[data-quick-task-name-row]');
    const taskName = root.querySelector('[data-quick-task-name]');
    const taskAutoFormat = root.querySelector('[data-quick-task-auto-format]');
    const deleteTemp = root.querySelector('[data-quick-delete-temp]');
    const confirmationCheckbox = root.querySelector('[data-quick-confirm-email]');
    const confirmationRecipients = root.querySelector('[data-quick-confirm-recipients]');
    const confirmationRow = root.querySelector('[data-quick-confirm-recipients-row]');
    const preflightButton = root.querySelector('[data-quick-preflight]');
    const transferButton = root.querySelector('[data-quick-transfer]');
    const statusCard = root.querySelector('[data-quick-status-card]');
    const statusMessage = root.querySelector('[data-quick-status-message]');
    const statusState = root.querySelector('[data-quick-status-state]');
    const progress = root.querySelector('[data-quick-progress]');
    const progressBar = root.querySelector('[data-quick-progress-bar]');
    const statusTask = root.querySelector('[data-quick-status-task]');
    const statusFiles = root.querySelector('[data-quick-status-files]');
    const statusBytes = root.querySelector('[data-quick-status-bytes]');

    let pgpLicensed = cfg.pgpLicensed !== 'false';
    let transferToken = '';
    let preparedTaskId = '';
    let busy = false;
    let deleteTempPreference = Boolean(deleteTemp?.checked);
    let transferInProgress = false;
    let cancelRequested = false;
    let cancelRequestInFlight = false;
    let startRequestController = null;
    let statusRequestController = null;
    let pollTimer = 0;
    let lastCreatedHostId = '';
    let sourceRoot = { name: '', id: '', kind: '' };
    let destinationRoot = { name: '', id: '', kind: '' };

    const macroSelects = Array.from(root.querySelectorAll('[data-quick-macro-select]'));

    const preflightDialog = document.querySelector('[data-quick-preflight-dialog]');
    const preflightSubtitle = preflightDialog?.querySelector('[data-quick-preflight-subtitle]');
    const preflightSummary = preflightDialog?.querySelector('[data-quick-preflight-summary]');
    const preflightResults = preflightDialog?.querySelector('[data-quick-preflight-results]');
    const preflightRunAgain = preflightDialog?.querySelector('[data-quick-preflight-run-again]');
    const preflightCloseButtons = Array.from(preflightDialog?.querySelectorAll('[data-quick-preflight-close]') || []);
    let preflightBusy = false;
    let activePreflight = null;

    let newHostTarget = 'source';

    const preflightStageDefinitions = [
        ['task', 'Task'], ['hosts', 'Hosts'], ['resources', 'Resources'], ['certificates', 'Certificates / Keys'],
        ['notifications', 'Notifications'], ['scheduling', 'Scheduling'], ['configuration', 'Configuration'], ['runtime', 'Runtime']
    ];

    function endpoint(name) { return String(cfg[name] || '').trim(); }

    async function fetchJson(url, options = {}) {
        const init = { credentials: 'same-origin', cache: 'no-store', ...options };
        init.headers = { Accept: 'application/json', ...(options.headers || {}) };
        if (init.method && init.method.toUpperCase() !== 'GET') {
            init.headers['Content-Type'] = 'application/json';
            if (antiForgeryToken) init.headers.RequestVerificationToken = antiForgeryToken;
        }
        const response = await fetch(url, init);
        let data = null;
        try { data = await response.json(); } catch { data = null; }
        if (!response.ok) {
            if (response.status === 401 && endpoint('sessionExpiredUrl')) window.location.assign(endpoint('sessionExpiredUrl'));
            const error = new Error(data?.message || `Request failed (${response.status}).`);
            error.status = response.status; error.payload = data;
            throw error;
        }
        return data || {};
    }

    function showError(message) {
        if (!errorBox) return;
        errorBox.textContent = message || 'Quick Transfer could not complete the request.';
        errorBox.hidden = false;
        errorBox.scrollIntoView({ block: 'nearest' });
    }
    function hideError() { if (errorBox) { errorBox.hidden = true; errorBox.textContent = ''; } }
    function showInfo(message) { if (infoBox) { infoBox.textContent = message; infoBox.hidden = !message; } }

    function setBusy(value) {
        busy = Boolean(value);
        const locked = busy || transferInProgress;
        if (preflightButton) preflightButton.disabled = locked;
        root.querySelectorAll('input,select,button').forEach(control => {
            if (control.closest('[data-quick-status-card]') || control.closest('dialog')) return;
            if (control === preflightButton || control === transferButton) return;
            if (locked && control.dataset.quickWasDisabled === undefined) control.dataset.quickWasDisabled = control.disabled ? '1' : '0';
            if (locked) control.disabled = true;
            else if (control.dataset.quickWasDisabled !== '1') control.disabled = false;
            if (!locked) delete control.dataset.quickWasDisabled;
        });
        syncConditionalControls();
        syncTransferAction();
    }

    function syncTransferAction() {
        if (!transferButton) return;
        if (transferInProgress) {
            transferButton.classList.remove('primary-button');
            transferButton.classList.add('danger-button');
            transferButton.textContent = cancelRequested || cancelRequestInFlight ? 'Cancelling...' : 'Cancel';
            transferButton.disabled = cancelRequested || cancelRequestInFlight;
            transferButton.title = cancelRequested ? 'Cancellation has been requested. MAUI is waiting for MOVEit to confirm the outcome.' : 'Cancel this Quick Transfer.';
            return;
        }

        transferButton.classList.remove('danger-button');
        transferButton.classList.add('primary-button');
        if (!busy) transferButton.textContent = 'Transfer';
        transferButton.disabled = busy;
        transferButton.title = '';
    }

    const pgpLicenseTooltip = 'The connected MOVEit Automation license does not include PGP. PGP processing is disabled in Quick Transfer.';

    function syncPgpLicenseState(licensed = pgpLicensed) {
        pgpLicensed = Boolean(licensed);
        root.dataset.pgpLicensed = pgpLicensed ? 'true' : 'false';
        const locked = busy || transferInProgress;
        pgpProcessingCards.forEach(card => {
            card.classList.toggle('disabled-card', !pgpLicensed);
            card.setAttribute('aria-disabled', pgpLicensed ? 'false' : 'true');
            card.title = pgpLicensed ? '' : pgpLicenseTooltip;
        });
        pgpProcessingInputs.forEach(input => {
            input.disabled = locked || !pgpLicensed;
        });
        if (!pgpLicensed && ['encrypt', 'decrypt'].includes(currentMode())) {
            const none = root.querySelector('input[name="quickProcessingMode"][value="none"]');
            if (none) none.checked = true;
        }
    }

    function currentMode() { return root.querySelector('input[name="quickProcessingMode"]:checked')?.value || 'none'; }
    function formatTaskName() {
        if (!taskName || !taskAutoFormat?.checked) return;
        taskName.value = taskName.value.toUpperCase().replaceAll(' ', '_');
    }
    function syncConditionalControls() {
        syncPgpLicenseState();
        const mode = currentMode();
        const locked = busy || transferInProgress;
        if (pgpRow) pgpRow.hidden = mode !== 'encrypt';
        if (persistTask && taskNameRow) taskNameRow.hidden = !persistTask.checked;
        if (confirmationRow) confirmationRow.hidden = !confirmationCheckbox?.checked;
        if (deleteTemp && persistTask) {
            deleteTemp.disabled = locked || persistTask.checked;
            deleteTemp.checked = persistTask.checked ? false : deleteTempPreference;
        }
        [sourceHost, destinationHost].forEach((select, index) => {
            const button = root.querySelector(`[data-quick-browse="${index === 0 ? 'source' : 'destination'}"]`);
            const selected = select?.selectedOptions?.[0];
            const browseSupported = selected?.dataset?.browseSupported === 'true';
            if (button) {
                button.disabled = locked || !select?.value || !browseSupported;
                button.title = select?.value && !browseSupported ? 'This Host does not expose a verified Quick Transfer browse pattern. Enter the folder manually.' : '';
            }
        });
    }

    function clearPreparedTransfer() {
        if (busy || transferInProgress) return;
        // Keep the prepared token. The server uses it to replace an unstarted temporary task or update
        // an unstarted persistent task in place after the request changes.
        showInfo('');
    }

    function selectedRootKind(select) {
        return select?.selectedOptions?.[0]?.dataset?.rootKind || '';
    }

    function rootLabel(kind) {
        switch (kind) {
            case 'Bucket': return 'Bucket';
            case 'Container': return 'Container';
            case 'DocumentLibrary': return 'Document Library';
            default: return '';
        }
    }

    function isRootBasedHost(select) { return Boolean(selectedRootKind(select)); }

    function updateRootSummary(context) {
        const state = context === 'source' ? sourceRoot : destinationRoot;
        const summary = context === 'source' ? sourceRootSummary : destinationRootSummary;
        const host = context === 'source' ? sourceHost : destinationHost;
        if (!summary) return;
        const kind = state.kind || selectedRootKind(host);
        if (!kind) {
            summary.hidden = true;
            summary.textContent = '';
            return;
        }
        summary.textContent = `${rootLabel(kind) || 'Storage root'}: ${state.name || 'Host default'}`;
        summary.hidden = false;
    }

    function clearRootForContext(context) {
        if (context === 'source') sourceRoot = { name: '', id: '', kind: '' };
        else destinationRoot = { name: '', id: '', kind: '' };
        updateRootSummary(context);
    }

    function buildRequest() {
        return {
            sourceHostId: sourceHost?.value || '',
            sourcePath: sourcePath?.value || '',
            sourceFileSpec: sourceFile?.value || '',
            sourceRootName: sourceRoot.name || '',
            sourceRootId: sourceRoot.id || '',
            deleteSourceFileAfterSuccess: Boolean(deleteSourceAfterSuccess?.checked),
            processingMode: currentMode(),
            pgpKeyId: pgpKey?.value || '',
            destinationHostId: destinationHost?.value || '',
            destinationPath: destinationPath?.value || '',
            destinationFileName: destinationFile?.value || '',
            overwriteExistingFile: overwriteExisting ? Boolean(overwriteExisting.checked) : true,
            destinationRootName: destinationRoot.name || '',
            destinationRootId: destinationRoot.id || '',
            createTraditionalTask: Boolean(persistTask?.checked),
            taskName: taskName?.value || '',
            deleteTemporaryTaskAfterSuccess: Boolean(deleteTemp?.checked),
            sendTransferConfirmationEmail: Boolean(confirmationCheckbox?.checked),
            confirmationEmailRecipients: confirmationCheckbox?.checked ? (confirmationRecipients?.value || "") : "",
            transferToken
        };
    }

    function validateClient(request) {
        if (!request.sourceHostId) return 'Select a Source Host.';
        if (!request.sourcePath.trim() && !isRootBasedHost(sourceHost)) return 'Source Folder is required.';
        if (!request.sourceFileSpec.trim()) return 'Source Filename / Filespec is required.';
        if (request.processingMode === 'encrypt' && !request.pgpKeyId) return 'Select a PGP Key for encryption.';
        if (!request.destinationHostId) return 'Select a Destination Host.';
        if (!request.destinationPath.trim() && !isRootBasedHost(destinationHost)) return 'Destination Folder is required.';
        if (/[*?;]/.test(request.sourceFileSpec) && request.destinationFileName.trim() && !/\[OrigName\]/i.test(request.destinationFileName))
            return 'A wildcard or multi-file Source Filespec cannot use one fixed Destination Filename. Leave it blank or include [OrigName].';
        if (request.createTraditionalTask && !request.taskName.trim()) return 'Task Name is required when Create Traditional Task is selected.';
        if (request.sendTransferConfirmationEmail && !request.confirmationEmailRecipients.trim()) return 'Enter at least one confirmation email recipient.';
        return '';
    }

    async function refreshReferences(forceRefresh = true, selectHostId = '', target = '') {
        const previousSourceHostId = sourceHost?.value || '';
        const previousDestinationHostId = destinationHost?.value || '';
        const data = await fetchJson(`${endpoint('referencesUrl')}?forceRefresh=${forceRefresh ? 'true' : 'false'}`);
        if (typeof data.pgpLicensed === 'boolean') syncPgpLicenseState(data.pgpLicensed);
        populateHostSelect(sourceHost, data.sourceHosts || [], target === 'source' ? selectHostId : previousSourceHostId);
        populateHostSelect(destinationHost, data.destinationHosts || [], target === 'destination' ? selectHostId : previousDestinationHostId);
        if ((sourceHost?.value || '') !== previousSourceHostId || (sourceRoot.kind && sourceRoot.kind !== selectedRootKind(sourceHost))) clearRootForContext('source');
        if ((destinationHost?.value || '') !== previousDestinationHostId || (destinationRoot.kind && destinationRoot.kind !== selectedRootKind(destinationHost))) clearRootForContext('destination');
        if (pgpKey) {
            const current = pgpKey.value;
            pgpKey.replaceChildren(new Option('Select PGP Key', ''));
            (data.pgpKeys || []).forEach(key => pgpKey.add(new Option(`${key.name}${key.uid ? ` — ${key.uid}` : ''}${key.publicPrivate ? ` — ${key.publicPrivate}` : ''}`, key.id)));
            if ([...pgpKey.options].some(option => option.value === current)) pgpKey.value = current;
        }
        syncConditionalControls();
    }

    function populateHostSelect(select, hosts, preferred) {
        if (!select) return;
        select.replaceChildren(new Option(select === sourceHost ? 'Select Source Host' : 'Select Destination Host', ''));
        hosts.forEach(host => {
            const option = new Option(`${host.name} — ${host.type}`, host.id);
            option.dataset.hostType = host.typeCode || '';
            option.dataset.browseSupported = host.browseSupported ? 'true' : 'false';
            option.dataset.rootKind = host.rootKind || '';
            select.add(option);
        });
        if (preferred && [...select.options].some(option => option.value === String(preferred))) select.value = String(preferred);
    }

    // Shared browser; this adapter preserves Quick Transfer's saved root and prepared-task rules.
    function openBrowse(context) {
        if (busy || transferInProgress) return;
        hideError();
        const host = context === 'source' ? sourceHost : destinationHost;
        const pathInput = context === 'source' ? sourcePath : destinationPath;
        const fileInput = context === 'source' ? sourceFile : destinationFile;
        if (!host?.value) return showError(`Select a ${context === 'source' ? 'Source' : 'Destination'} Host first.`);
        if (host.selectedOptions[0]?.dataset?.browseSupported !== 'true') return showError('Folder browsing is not available for the selected Host type. Enter the folder manually.');
        const hostId = host.value;
        let browseRoot = { ...(context === 'source' ? sourceRoot : destinationRoot) };
        const rootItem = item => ['Bucket', 'Container', 'DocumentLibrary'].includes(item?.nativeType);
        window.MauiHostBrowser.open({
            title: context === 'source' ? 'Browse Source' : 'Browse Destination',
            hostName: host.selectedOptions[0].textContent, path: pathInput.value,
            load: async (directory, signal) => {
                const data = await fetchJson(endpoint('browseUrl'), { method: 'POST', signal,
                    body: JSON.stringify({ hostId, directory, fileMask: '', rootName: browseRoot.name || '', rootId: browseRoot.id || '', taskId: preparedTaskId || '', stepId: '' }) });
                browseRoot = { name: data.rootName ?? browseRoot.name, id: data.rootId ?? browseRoot.id, kind: data.rootKind || browseRoot.kind };
                return { currentPath: data.currentDirectory || '', parentPath: data.parentDirectory || '',
                    hasParent: Boolean(data.parentDirectory || browseRoot.name), canSelectCurrent: Boolean(data.currentDirectory || browseRoot.name),
                    hostName: data.hostName, items: (data.items || []).map(item => ({ ...item, isFolder: item.isDirectory })) };
            },
            openFolder: item => {
                if (!rootItem(item)) return item.path;
                browseRoot = { name: item.name, id: item.nativeId || '', kind: item.nativeType };
                return item.nativeType === 'DocumentLibrary' ? '' : '/';
            },
            parent: path => { if (path) return path; browseRoot = { name: '', id: '', kind: selectedRootKind(host) }; return ''; },
            onSelect: selection => {
                if (host.value !== hostId || busy || transferInProgress) return;
                if (rootItem(selection.item)) {
                    browseRoot = { name: selection.item.name, id: selection.item.nativeId || '', kind: selection.item.nativeType };
                    pathInput.value = selection.item.nativeType === 'DocumentLibrary' ? '' : '/';
                } else pathInput.value = selection.path;
                if (selection.fileName !== null) fileInput.value = selection.fileName;
                if (context === 'source') sourceRoot = { ...browseRoot }; else destinationRoot = { ...browseRoot };
                updateRootSummary(context); clearPreparedTransfer();
            }
        });
    }

    // ---- Macros ----
    // Match the Traditional Task Editor Source File(s) and Destination File Name dropdowns.
    function buildMacroOptions(select) {
        const context = select?.dataset?.quickMacroSelect === 'destination' ? 'runtime-file' : 'source-file';
        window.MauiTaskFieldMacros.populateSelect(select, context);
    }

    function insertMacroFromSelect(select) {
        const value = String(select?.value || '');
        if (!value) return;
        const target = select.dataset.quickMacroSelect === 'destination' ? destinationFile : sourceFile;
        if (!target) { select.value = ''; return; }
        const start = typeof target.selectionStart === 'number' ? target.selectionStart : target.value.length;
        const end = typeof target.selectionEnd === 'number' ? target.selectionEnd : start;
        target.value = `${target.value.slice(0, start)}${value}${target.value.slice(end)}`;
        target.focus();
        try { target.setSelectionRange(start + value.length, start + value.length); } catch { /* unsupported */ }
        target.dispatchEvent(new Event('input', { bubbles: true }));
        select.value = '';
    }
    macroSelects.forEach(select => {
        buildMacroOptions(select);
        select.addEventListener('change', () => insertMacroFromSelect(select));
    });

    // ---- Preflight ----
    // This intentionally mirrors the established Tasks page Preflight modal/pattern.
    function preflightStageElement(stage) {
        return preflightDialog?.querySelector(`[data-quick-preflight-stage="${stage}"]`);
    }

    function setPreflightStageState(stage, state) {
        const element = preflightStageElement(stage);
        if (!element) return;
        element.classList.remove('is-active', 'is-pass', 'is-warning', 'is-fail');
        if (state) element.classList.add(`is-${state}`);
        const label = preflightStageDefinitions.find(([key]) => key === stage)?.[1] || stage;
        element.setAttribute('aria-label', `${label}: ${state || 'pending'}`);
    }

    function resetPreflightUi(taskNameValue) {
        preflightStageDefinitions.forEach(([stage]) => setPreflightStageState(stage, ''));
        preflightResults?.replaceChildren();
        if (preflightSummary) preflightSummary.textContent = 'Running preflight checks…';
        if (preflightSubtitle) preflightSubtitle.textContent = taskNameValue
            ? `Read-only validation for ${taskNameValue}. No Task or file-transfer changes will be made.`
            : 'Validate the Quick Transfer Task configuration and dependencies without running the Task.';
    }

    function appendPreflightCheck(stage, check) {
        if (!preflightResults) return;
        const row = document.createElement('tr');
        const status = String(check?.status || 'unknown').toLowerCase();
        const statusCell = document.createElement('td');
        const badge = document.createElement('span');
        badge.className = `task-preflight-result-badge is-${status}`;
        badge.textContent = status === 'pass' ? 'Pass' : status === 'warning' ? 'Warning' : status === 'fail' ? 'Error' : status === 'info' ? 'Info' : 'Unknown';
        statusCell.appendChild(badge);
        const titleCell = document.createElement('td');
        const stageName = preflightStageDefinitions.find(([key]) => key === stage)?.[1] || stage;
        titleCell.textContent = `${stageName}: ${String(check?.title || 'Check')}`;
        const messageCell = document.createElement('td');
        messageCell.textContent = String(check?.message || '');
        row.append(statusCell, titleCell, messageCell);
        preflightResults.appendChild(row);
    }

    function preflightHostDetailIsError(message) {
        const text = String(message || '').trim();
        if (!text) return false;
        if (/\b(failed|failure|error)\b/i.test(text)) return true;
        const unquoted = text.replace(/(['"])(?:(?!\1).)*\1/g, '');
        return /\(\s*-?\d{1,10}\s*\)/.test(unquoted);
    }

    function updatePreflightSummary() {
        const rows = Array.from(preflightResults?.querySelectorAll('.task-preflight-result-badge') || []);
        const count = value => rows.filter(row => row.classList.contains(`is-${value}`)).length;
        const failures = count('fail');
        const warnings = count('warning');
        const unknown = count('unknown');
        const passed = count('pass');
        const info = count('info');
        const parts = [`${passed} passed`];
        if (warnings) parts.push(`${warnings} warning${warnings === 1 ? '' : 's'}`);
        if (failures) parts.push(`${failures} error${failures === 1 ? '' : 's'}`);
        if (unknown) parts.push(`${unknown} unknown`);
        if (info) parts.push(`${info} informational`);
        if (preflightSummary) preflightSummary.textContent = `Completed: ${parts.join(' · ')}.`;
    }

    async function runPreparedPreflight() {
        if (preflightBusy || !activePreflight?.taskId) return;
        preflightBusy = true;
        resetPreflightUi(activePreflight.taskName);
        if (preflightRunAgain) {
            preflightRunAgain.disabled = true;
            preflightRunAgain.textContent = 'Checking...';
        }
        preflightCloseButtons.forEach(button => { button.disabled = true; });

        try {
            for (const [stage] of preflightStageDefinitions) {
                setPreflightStageState(stage, 'active');
                try {
                    const data = await fetchJson(endpoint('preflightStageUrl'), {
                        method: 'POST',
                        body: JSON.stringify({ taskId: activePreflight.taskId, stage })
                    });
                    const returnedName = String(data?.taskName || '').trim();
                    if (returnedName) {
                        activePreflight.taskName = returnedName;
                        if (preflightSubtitle) preflightSubtitle.textContent = `Read-only validation for ${returnedName}. No Task or file-transfer changes will be made.`;
                    }
                    const status = ['pass', 'warning', 'fail'].includes(String(data?.status || '').toLowerCase())
                        ? String(data.status).toLowerCase() : 'warning';
                    setPreflightStageState(stage, status);
                    const checks = Array.isArray(data?.checks) ? data.checks : [];
                    if (checks.length === 0) appendPreflightCheck(stage, { status: 'info', title: 'Stage', message: 'No applicable checks were returned.' });
                    else checks.forEach(check => appendPreflightCheck(stage, check));
                } catch (error) {
                    const message = error?.message || 'This preflight stage could not be completed.';
                    const hostError = stage === 'hosts' && preflightHostDetailIsError(message);
                    setPreflightStageState(stage, hostError ? 'fail' : 'warning');
                    appendPreflightCheck(stage, { status: hostError ? 'fail' : 'unknown', title: 'Stage', message });
                }
            }
            updatePreflightSummary();
        } finally {
            preflightBusy = false;
            if (preflightRunAgain) {
                preflightRunAgain.disabled = false;
                preflightRunAgain.textContent = 'Run Preflight Again';
            }
            preflightCloseButtons.forEach(button => { button.disabled = false; });
        }
    }

    async function runPreflight() {
        if (busy || preflightBusy) return;
        hideError();
        const request = buildRequest();
        const validation = validateClient(request);
        if (validation) { showError(validation); return; }
        setBusy(true);
        if (preflightButton) preflightButton.textContent = 'Preparing...';
        try {
            const prepared = await fetchJson(endpoint('prepareUrl'), { method: 'POST', body: JSON.stringify(request) });
            transferToken = prepared.transferToken || '';
            preparedTaskId = prepared.taskId || '';
            if (!preparedTaskId) throw new Error('MOVEit did not return the prepared Task ID.');
            activePreflight = { taskId: preparedTaskId, taskName: String(prepared.taskName || preparedTaskId).trim() };
            showStatusCard({
                state: 'Prepared',
                message: 'Quick Transfer Task prepared. Transfer has not started.',
                taskName: activePreflight.taskName,
                completed: false,
                percentComplete: 0
            });
            resetPreflightUi(activePreflight.taskName);
            if (preflightDialog && !preflightDialog.open) preflightDialog.showModal();
            await runPreparedPreflight();
        } catch (error) {
            showError(error.message);
        } finally {
            setBusy(false);
            if (preflightButton) preflightButton.textContent = 'Preflight';
        }
    }

    function closePreflight() {
        if (preflightBusy) return;
        activePreflight = null;
        preflightDialog?.close();
    }

    // ---- Transfer ----
    async function startTransfer() {
        if (transferInProgress) {
            await cancelTransfer();
            return;
        }
        if (busy) return;
        hideError(); showInfo('');
        cancelRequested = false;
        const request = buildRequest();
        const validation = validateClient(request);
        if (validation) return showError(validation);
        setBusy(true);
        if (transferButton) transferButton.textContent = 'Preparing...';
        try {
            if (!transferToken) {
                const prepared = await fetchJson(endpoint('prepareUrl'), { method: 'POST', body: JSON.stringify(request) });
                transferToken = prepared.transferToken || '';
                preparedTaskId = prepared.taskId || '';
                if (!transferToken || !preparedTaskId) throw new Error('MOVEit did not return the prepared Quick Transfer task.');
                request.transferToken = transferToken;
            }

            transferInProgress = true;
            syncTransferAction();
            showStatusCard({
                state: 'Starting',
                message: 'Submitting the prepared Task to MOVEit. You can cancel while MAUI waits for the start response.',
                taskName: preparedTaskId || 'Prepared Task',
                completed: false
            });

            startRequestController = new AbortController();
            const result = await fetchJson(endpoint('transferUrl'), {
                method: 'POST',
                body: JSON.stringify({ ...request, transferToken }),
                signal: startRequestController.signal
            });
            transferToken = result.transferToken || transferToken;
            preparedTaskId = result.taskId || preparedTaskId;
            if (!cancelRequested) {
                showStatusCard({ state: 'Starting', message: 'MOVEit accepted the transfer request.', taskName: result.taskName, completed: false });
                schedulePoll(500);
            }
        } catch (error) {
            if (cancelRequested || error?.name === 'AbortError') {
                if (transferToken) schedulePoll(500);
            } else {
                showError(error.message);
                if (transferToken) {
                    transferInProgress = true;
                    showStatusCard({ state: 'Verifying', message: 'The start response could not be confirmed. MAUI will check MOVEit status without resubmitting the transfer.', taskName: preparedTaskId || 'Prepared Task', completed: false });
                    schedulePoll(750);
                } else {
                    transferInProgress = false;
                }
            }
        } finally {
            startRequestController = null;
            setBusy(false);
        }
    }

    async function cancelTransfer() {
        if (!transferInProgress || !transferToken || cancelRequested || cancelRequestInFlight) return;
        hideError();
        cancelRequestInFlight = true;
        cancelRequested = true;
        window.clearTimeout(pollTimer);
        statusRequestController?.abort();
        startRequestController?.abort();
        syncTransferAction();
        showStatusCard({
            state: 'Cancelling',
            message: 'Cancellation requested. MAUI is locating the exact MOVEit run instance so it can be stopped safely.',
            taskName: statusTask?.textContent || preparedTaskId || 'Prepared Task',
            completed: false
        });

        try {
            const data = await fetchJson(endpoint('cancelUrl'), {
                method: 'POST',
                body: JSON.stringify({ transferToken })
            });
            showStatusCard(data);
            if (!data.completed) schedulePoll(500);
        } catch (error) {
            cancelRequested = false;
            showError(error.message);
            if (transferInProgress) schedulePoll(750);
        } finally {
            cancelRequestInFlight = false;
            syncTransferAction();
        }
    }

    function schedulePoll(delay = 1500) {
        window.clearTimeout(pollTimer);
        if (!transferToken) return;
        pollTimer = window.setTimeout(pollStatus, delay);
    }

    async function pollStatus() {
        if (!transferToken) return;
        const controller = new AbortController();
        statusRequestController = controller;
        try {
            const data = await fetchJson(`${endpoint('statusUrl')}?transferToken=${encodeURIComponent(transferToken)}`, { signal: controller.signal });
            // A status request can already be in flight when Cancel is clicked. Do not let an older
            // Running/Starting response overwrite the newer Cancelling state. Completed responses remain
            // authoritative because MOVEit may finish naturally just before the kill reaches it.
            const responseState = String(data.state || '').toLowerCase();
            if (cancelRequested && !data.completed && responseState !== 'cancelling') {
                schedulePoll(500);
                return;
            }
            showStatusCard(data);
            if (!data.completed) schedulePoll(1500);
        } catch (error) {
            if (error?.name === 'AbortError') return;
            showStatusCard({ state: 'Unknown', message: `Unable to refresh transfer status: ${error.message}`, taskName: statusTask?.textContent || '—', completed: false });
            schedulePoll(4000);
        } finally {
            if (statusRequestController === controller) statusRequestController = null;
        }
    }

    function showStatusCard(data) {
        if (!statusCard) return;
        statusCard.hidden = false;
        const state = String(data.state || 'Working');
        const normalizedState = state.toLowerCase().replaceAll(' ', '-');
        if (['starting', 'running', 'finalizing', 'verifying', 'cancelling'].includes(normalizedState)) transferInProgress = true;
        if (normalizedState === 'cancelling') cancelRequested = true;
        if (statusState) { statusState.textContent = state; statusState.dataset.state = normalizedState; }
        if (statusMessage) statusMessage.textContent = data.message || '';
        if (statusTask) {
            statusTask.textContent = data.taskName || data.taskId || statusTask.textContent || '—';
            statusTask.title = statusTask.textContent === '—' ? '' : statusTask.textContent;
        }
        if (statusFiles) statusFiles.textContent = Number.isFinite(data.filesTransferred) ? String(data.filesTransferred) : '—';
        if (statusBytes) statusBytes.textContent = Number.isFinite(data.totalBytesTransferred) ? formatBytes(data.totalBytesTransferred) : (Number.isFinite(data.currentFileBytes) ? `${formatBytes(data.currentFileBytes)}${Number.isFinite(data.totalFileBytes) ? ` / ${formatBytes(data.totalFileBytes)}` : ''}` : '—');
        const percent = Number.isFinite(data.percentComplete) ? Math.max(0, Math.min(100, data.percentComplete)) : (data.completed && data.transferSucceeded ? 100 : null);
        if (progress) {
            progress.setAttribute('aria-valuenow', percent === null ? '0' : String(percent));
            progress.classList.toggle('indeterminate', percent === null && !data.completed);
        }
        if (progressBar) progressBar.style.width = `${percent === null ? 20 : percent}%`;
        if (data.completed) {
            transferInProgress = false;
            cancelRequested = false;
            cancelRequestInFlight = false;
            transferToken = '';
            preparedTaskId = '';
            setBusy(false);
        } else {
            syncTransferAction();
        }
    }

    function formatBytes(value) {
        const bytes = Number(value); if (!Number.isFinite(bytes)) return '—';
        const units = ['B', 'KB', 'MB', 'GB', 'TB']; let amount = bytes; let unit = 0;
        while (Math.abs(amount) >= 1024 && unit < units.length - 1) { amount /= 1024; unit += 1; }
        return `${amount >= 100 || unit === 0 ? amount.toFixed(0) : amount.toFixed(1)} ${units[unit]}`;
    }

    // Reuse the Hosts creation form/controller and preferred editor without leaving this transfer draft.
    const useMauiHostEditor = window.MauiEditorRouting?.useMauiHostEditor === true;
    window.MauiTaskHostEditor?.initialize(root, antiForgeryToken, { fetchJson });
    const refreshCreatedHost = () => refreshReferences(true, lastCreatedHostId, newHostTarget);
    const newHost = window.MauiNewHostDialog.create({
        createUrl: endpoint('createHostUrl'), fetchJson, useMauiHostEditor,
        status: (message, _duration, kind) => { if (!window.MauiModalUI.showMessage(message, kind)) showError(message); },
        allowedTypes: ['Share','siLock','FTP','SSHFTP','S3','AzureBlob','SharePoint','GoogleStorage'],
        created: host => { lastCreatedHostId = String(host.id); },
        refresh: refreshCreatedHost,
        openEditor: async (id, name, popup) => {
            if (useMauiHostEditor) return window.MauiEditorRouting.openHost(id, name, {
                inPlace: true, onClosed: () => refreshCreatedHost(true).catch(error => showError(error.message))
            });
            return window.MauiNativeEditor.open({
                popup, sessionUrl: endpoint('nativeEditorSessionUrl'),
                loginUrl: endpoint('sessionExpiredUrl') || window.mauiAppUrl('/Account/SessionExpired'), antiForgeryToken,
                returnHash: `#/hosts/${encodeURIComponent(id)}?HideNav`, windowTitle: `MAUI Host Editor - ${id}: ${name}`,
                skin: 'maui', status: message => showError(message)
            });
        }
    });
    function openNewHost(target) { newHostTarget = target || 'source'; newHost.open(); }

    // Event bindings.
    sourceHost?.addEventListener('change', () => clearRootForContext('source'));
    destinationHost?.addEventListener('change', () => clearRootForContext('destination'));
    root.querySelectorAll('[data-quick-new-host]').forEach(button => button.addEventListener('click', () => openNewHost(button.dataset.quickNewHost)));
    root.querySelectorAll('[data-quick-browse]').forEach(button => button.addEventListener('click', () => openBrowse(button.dataset.quickBrowse)));
    deleteTemp?.addEventListener('change', () => { if (!persistTask?.checked) deleteTempPreference = deleteTemp.checked; });
    root.querySelectorAll('input,select').forEach(control => control.addEventListener('change', () => { clearPreparedTransfer(); syncConditionalControls(); }));
    root.querySelectorAll('input[type="text"]').forEach(control => control.addEventListener('input', clearPreparedTransfer));
    root.querySelectorAll('input[name="quickProcessingMode"]').forEach(control => control.addEventListener('change', syncConditionalControls));
    persistTask?.addEventListener('change', syncConditionalControls);
    confirmationCheckbox?.addEventListener('change', syncConditionalControls);
    taskName?.addEventListener('input', formatTaskName);
    preflightButton?.addEventListener('click', runPreflight);
    transferButton?.addEventListener('click', startTransfer);

    preflightRunAgain?.addEventListener('click', runPreparedPreflight);
    preflightCloseButtons.forEach(button => button.addEventListener('click', closePreflight));
    preflightDialog?.addEventListener('cancel', event => { if (preflightBusy) event.preventDefault(); else activePreflight = null; });
    preflightDialog?.addEventListener('close', () => { if (!preflightBusy) activePreflight = null; });

    window.addEventListener('focus', () => { if (lastCreatedHostId && !newHost.isOpen) refreshReferences(true, lastCreatedHostId, newHostTarget).catch(() => {}); });
    updateRootSummary('source');
    updateRootSummary('destination');
    syncConditionalControls();
    syncTransferAction();
})();
