/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Configuration backup, Data Access Credential operations, and unattended-job readiness warnings.
 */

(() => {
    let workspace;
    let antiForgeryToken = '';
    let showToast = () => {};
    let checked = () => false;
    let numberValue = () => 0;
    let backupBaseline = '';
    let activeDataAccessRow = null;
    let initialized = false;

    let backupNowUrl = '';
    let backupsUrl = '';
    let backupDownloadUrl = '';
    let backupRemoveUrl = '';
    let backupRestoreUrl = '';
    let dataAccessReadinessUrl = '';
    let dataAccessSaveUrl = '';
    let dataAccessTestUrl = '';
    let dataAccessClearUrl = '';
    let backupNowButton;
    let backupRefreshButton;
    let backupList;
    let backupStatus;
    let backupReadiness;
    let backupDataAccessWarning;
    let backupDataAccessWarningText;
    let schedulerReadiness;
    let schedulerDataAccessWarning;
    let schedulerDataAccessWarningText;
    let dataAccessDialog;
    let dataAccessDialogTitle;
    let dataAccessServerLabel;
    let dataAccessServerKey;
    let dataAccessUsername;
    let dataAccessPassword;
    let dataAccessPasswordHelp;
    let dataAccessError;
    let dataAccessSaveButton;
    let dataAccessCancelButton;
    let dataAccessCloseButton;
    let backupRestoreDialog;
    let backupRestoreDate;
    let backupRestoreDatabase;
    let backupRestoreMoveItSummary;
    let backupRestoreSqlWarning;
    let backupRestoreMoveItOptions;
    let backupRestoreMultiWarning;
    let backupRestoreBusy = false;
    let backupRestoreAcknowledge;
    let backupRestoreConfirmation;
    let backupRestoreError;
    let backupRestoreConfirmButton;
    let backupRestoreCancelButton;
    let backupRestoreCloseButton;
    let activeBackup = null;
    let backupInventory = [];
    let backupLoadPromise = null;

    function backupSettingsSignature() {
        return JSON.stringify({
            enabled: checked('backupEnabled'),
            frequencyHours: numberValue('backupFrequencyHours'),
            retentionDays: numberValue('backupRetentionDays')
        });
    }

    function setBackupStatus(message, state = 'idle') {
        if (!backupStatus) return;
        backupStatus.textContent = message || '';
        backupStatus.dataset.state = state;
    }

    function ensureBackupSettingsSaved() {
        if (backupSettingsSignature() === backupBaseline) return true;
        showToast('Save the Backup settings before running Backup Now.');
        return false;
    }

    function renderDataAccessReadiness(state) {
        const missingEnvironments = Array.isArray(state?.missingDataAccessEnvironments)
            ? state.missingDataAccessEnvironments.filter(Boolean)
            : [];
        const missingServers = Array.isArray(state?.missingDataAccessServers)
            ? state.missingDataAccessServers.filter(Boolean)
            : [];
        const hasMissingEnvironments = missingEnvironments.length > 0;
        const hasMissingServers = missingServers.length > 0;
        const environmentList = missingEnvironments.join(', ');
        const serverList = missingServers.join(', ');

        if (backupDataAccessWarning) {
            backupDataAccessWarning.hidden = !hasMissingEnvironments;
            if (hasMissingEnvironments && backupDataAccessWarningText) {
                backupDataAccessWarningText.textContent = `Automatic backups cannot export MOVEit configuration for ${environmentList} until Data Access Credentials are configured under MOVEit Connections.`;
            }
        }
        if (backupReadiness) backupReadiness.hidden = !hasMissingEnvironments;

        if (schedulerDataAccessWarning) {
            schedulerDataAccessWarning.hidden = !hasMissingServers;
            if (hasMissingServers && schedulerDataAccessWarningText) {
                schedulerDataAccessWarningText.textContent = `Data Access Credentials are not configured for ${serverList}. Scheduled jobs that require those MOVEit connections may fail or complete with warnings.`;
            }
        }
        if (schedulerReadiness) schedulerReadiness.hidden = !hasMissingServers;
    }

    async function loadDataAccessReadiness() {
        if (!dataAccessReadinessUrl) return;
        try {
            const response = await fetch(dataAccessReadinessUrl, { method: 'GET', credentials: 'same-origin', cache: 'no-store' });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data?.message || `Data Access readiness check failed (${response.status}).`);
            renderDataAccessReadiness(data);
        } catch {
            if (backupReadiness) backupReadiness.hidden = true;
            if (backupDataAccessWarning) backupDataAccessWarning.hidden = true;
            if (schedulerReadiness) schedulerReadiness.hidden = true;
            if (schedulerDataAccessWarning) schedulerDataAccessWarning.hidden = true;
        }
    }

    async function postConfigurationJson(url, payload, defaultError = 'Configuration operation failed.') {
        if (!url) throw new Error(defaultError);
        const headers = new Headers({ 'Content-Type': 'application/json' });
        if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
        const response = await fetch(url, { method: 'POST', credentials: 'same-origin', headers, body: JSON.stringify(payload) });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data?.message || `${defaultError} (${response.status}).`);
        return data;
    }

    // Advance the open Settings page only after the independent write commits.
    function publishConfigurationRevision(data) {
        const revision = String(data?.configurationRevision || '');
        if (revision) window.dispatchEvent(new CustomEvent('maui:configuration-revision-updated', { detail: { revision } }));
    }

    function syncDataAccessRow(row, username, configured) {
        if (!row) return;
        row.dataset.configured = configured ? 'true' : 'false';
        const persisted = row.dataset.persisted === 'true';
        const usernameValue = row.querySelector('[data-data-access-username]');
        const passwordState = row.querySelector('[data-data-access-password-state]');
        const configure = row.querySelector('[data-data-access-configure]');
        const test = row.querySelector('[data-data-access-test]');
        const clear = row.querySelector('[data-data-access-clear]');
        if (usernameValue) usernameValue.textContent = configured && username ? username : '—';
        if (passwordState) passwordState.textContent = configured ? 'Configured' : 'Not Configured';
        if (configure) { configure.textContent = configured ? 'Edit' : 'Configure'; configure.disabled = !persisted; }
        if (test) test.disabled = !persisted || !configured;
        if (clear) clear.disabled = !persisted || !configured;
    }

    function closeDataAccessDialog() {
        if (dataAccessDialog?.open) dataAccessDialog.close();
        if (dataAccessPassword) dataAccessPassword.value = '';
        if (dataAccessError) { dataAccessError.hidden = true; dataAccessError.textContent = ''; }
        activeDataAccessRow = null;
    }

    function openDataAccessDialog(row) {
        if (!dataAccessDialog || !row || row.dataset.persisted !== 'true') return;
        activeDataAccessRow = row;
        const serverCard = row.closest('[data-server-row]');
        const serverKey = row.dataset.serverKey || serverCard?.querySelector('[data-server-field="key"]')?.value || '';
        const serverName = serverCard?.querySelector('[data-server-field="displayName"]')?.value?.trim() || serverKey;
        const configured = row.dataset.configured === 'true';
        const currentUsername = configured ? String(row.querySelector('[data-data-access-username]')?.textContent || '').trim() : '';
        if (dataAccessDialogTitle) dataAccessDialogTitle.textContent = configured ? 'Edit Data Access Credentials' : 'Configure Data Access Credentials';
        if (dataAccessServerLabel) dataAccessServerLabel.textContent = serverName;
        if (dataAccessServerKey) dataAccessServerKey.value = serverKey;
        if (dataAccessUsername) dataAccessUsername.value = currentUsername === '—' ? '' : currentUsername;
        if (dataAccessPassword) { dataAccessPassword.value = ''; dataAccessPassword.placeholder = configured ? 'Leave blank to keep current password' : 'Enter password'; }
        if (dataAccessPasswordHelp) dataAccessPasswordHelp.textContent = configured
            ? 'Leave blank to preserve the currently protected password, or enter a replacement password.'
            : 'Enter the password for this MOVEit Administrator account.';
        if (dataAccessError) { dataAccessError.hidden = true; dataAccessError.textContent = ''; }
        dataAccessDialog.showModal();
        dataAccessUsername?.focus();
    }

    async function saveDataAccessCredentials() {
        if (!dataAccessSaveUrl || !activeDataAccessRow || !dataAccessSaveButton) return;
        const serverKey = activeDataAccessRow.dataset.serverKey || '';
        const username = String(dataAccessUsername?.value || '').trim();
        const password = String(dataAccessPassword?.value || '');
        const configured = activeDataAccessRow.dataset.configured === 'true';
        if (!username) { if (dataAccessError) { dataAccessError.hidden = false; dataAccessError.textContent = 'Data Access Username is required.'; } return; }
        if (!configured && !password) { if (dataAccessError) { dataAccessError.hidden = false; dataAccessError.textContent = 'Data Access Password is required when configuring this server for the first time.'; } return; }

        dataAccessSaveButton.disabled = true;
        if (dataAccessCancelButton) dataAccessCancelButton.disabled = true;
        if (dataAccessCloseButton) dataAccessCloseButton.disabled = true;
        try {
            const data = await postConfigurationJson(dataAccessSaveUrl, { serverKey, username, password }, 'Data Access Credentials could not be saved');
            syncDataAccessRow(activeDataAccessRow, data?.username || username, true);
            publishConfigurationRevision(data);
            showToast(data?.message || 'Data Access Credentials saved.');
            window.dispatchEvent(new CustomEvent('maui:data-access-credentials-changed'));
            closeDataAccessDialog();
        } catch (error) {
            if (dataAccessError) { dataAccessError.hidden = false; dataAccessError.textContent = error?.message || 'Data Access Credentials could not be saved.'; }
        } finally {
            dataAccessSaveButton.disabled = false;
            if (dataAccessCancelButton) dataAccessCancelButton.disabled = false;
            if (dataAccessCloseButton) dataAccessCloseButton.disabled = false;
            if (dataAccessPassword) dataAccessPassword.value = '';
        }
    }

    async function testDataAccessCredentials(row) {
        if (!dataAccessTestUrl || !row) return;
        const serverKey = row.dataset.serverKey || '';
        const button = row.querySelector('[data-data-access-test]');
        if (!button || button.disabled) return;
        button.disabled = true;
        try {
            const data = await postConfigurationJson(dataAccessTestUrl, { serverKey }, 'Data Access credential test failed');
            showToast(data?.message || 'Data Access Credentials authenticated successfully.');
        } catch (error) {
            showToast(error?.message || `Data Access credential test failed for ${serverKey}.`, 'error');
        } finally {
            button.disabled = row.dataset.persisted !== 'true' || row.dataset.configured !== 'true';
        }
    }

    async function clearDataAccessCredentials(row) {
        if (!dataAccessClearUrl || !row) return;
        const serverKey = row.dataset.serverKey || '';
        const serverName = row.closest('[data-server-row]')?.querySelector('[data-server-field="displayName"]')?.value?.trim() || serverKey;
        if (!await window.mauiConfirm?.({
            title: 'Clear Data Access Credentials?',
            message: `Clear the Data Access Credentials for ${serverName}? Automatic backups and scheduled reports may be unavailable for its environment until another server has credentials configured.`,
            confirmText: 'Clear Credentials',
            danger: true
        })) return;
        const button = row.querySelector('[data-data-access-clear]');
        if (button) button.disabled = true;
        try {
            const data = await postConfigurationJson(dataAccessClearUrl, { serverKey }, 'Data Access Credentials could not be cleared');
            syncDataAccessRow(row, '', false);
            publishConfigurationRevision(data);
            showToast(data?.message || 'Data Access Credentials cleared.');
            window.dispatchEvent(new CustomEvent('maui:data-access-credentials-changed'));
        } catch (error) {
            showToast(error?.message || `Data Access Credentials could not be cleared for ${serverName}.`, 'error');
            if (button) button.disabled = false;
        }
    }

    function wireDataAccessRow(row) {
        if (!row || row.dataset.wired === 'true') return;
        row.dataset.wired = 'true';
        syncDataAccessRow(row, String(row.querySelector('[data-data-access-username]')?.textContent || '').trim().replace('—', ''), row.dataset.configured === 'true');
        row.querySelector('[data-data-access-configure]')?.addEventListener('click', () => openDataAccessDialog(row));
        row.querySelector('[data-data-access-test]')?.addEventListener('click', () => testDataAccessCredentials(row));
        row.querySelector('[data-data-access-clear]')?.addEventListener('click', () => clearDataAccessCredentials(row));
    }

    function formatBackupDate(value) {
        const date = new Date(value || '');
        return Number.isNaN(date.getTime()) ? 'Unknown' : date.toLocaleString();
    }

    function formatBytes(value) {
        let bytes = Number(value || 0);
        if (!Number.isFinite(bytes) || bytes < 0) bytes = 0;
        const units = ['B', 'KB', 'MB', 'GB', 'TB'];
        let unit = 0;
        while (bytes >= 1024 && unit < units.length - 1) { bytes /= 1024; unit += 1; }
        const digits = unit === 0 || bytes >= 100 ? 0 : bytes >= 10 ? 1 : 2;
        return `${bytes.toFixed(digits)} ${units[unit]}`;
    }

    function backupContentsText(item) {
        const parts = [];
        if (item?.hasAppSettings) parts.push('appsettings.json');
        if (item?.hasSqliteDatabase) parts.push('SQLite');
        else if (String(item?.databaseProvider || '').toLowerCase() === 'sqlserver') parts.push('SQL Server config');
        const moveItCount = Number(item?.moveItConfigurationCount || 0);
        if (moveItCount > 0) parts.push(`MOVEit config × ${moveItCount}`);
        return parts.length ? parts.join(' · ') : 'No recognized backup files';
    }

    function renderBackups() {
        if (!backupList) return;
        backupList.innerHTML = '';
        if (!backupInventory.length) {
            const row = document.createElement('tr');
            row.innerHTML = '<td colspan="5" class="backup-list-loading">No MAUI backups were found.</td>';
            backupList.appendChild(row);
            return;
        }

        backupInventory.forEach(item => {
            const row = document.createElement('tr');
            row.dataset.backupId = item.id || '';

            const dateCell = document.createElement('td');
            dateCell.textContent = formatBackupDate(item.backupUtc);

            const statusCell = document.createElement('td');
            const badge = document.createElement('span');
            badge.className = `backup-status-badge ${item.isComplete ? 'complete' : 'partial'}`;
            badge.textContent = item.isComplete ? 'Complete' : 'Partial';
            statusCell.appendChild(badge);

            const contentsCell = document.createElement('td');
            contentsCell.textContent = backupContentsText(item);
            if (Array.isArray(item.moveItEnvironments) && item.moveItEnvironments.length) {
                const env = document.createElement('small');
                env.className = 'backup-environment-list';
                env.textContent = item.moveItEnvironments.join(', ');
                contentsCell.appendChild(env);
            }

            const sizeCell = document.createElement('td');
            sizeCell.textContent = formatBytes(item.sizeBytes);

            const actionsCell = document.createElement('td');
            const actionsWrap = document.createElement('div');
            actionsWrap.className = 'backup-row-actions';
            const restore = document.createElement('button');
            restore.type = 'button';
            restore.className = 'secondary-button backup-action-button';
            restore.dataset.backupAction = 'restore';
            restore.textContent = 'Restore';
            restore.disabled = item.canRestore !== true;
            if (restore.disabled && item.restoreBlockReason) restore.title = item.restoreBlockReason;

            const download = document.createElement('button');
            download.type = 'button';
            download.className = 'secondary-button backup-action-button';
            download.dataset.backupAction = 'download';
            download.textContent = 'Download';

            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'danger-button backup-action-button';
            remove.dataset.backupAction = 'remove';
            remove.textContent = 'Remove';
            actionsWrap.append(restore, download, remove);
            actionsCell.appendChild(actionsWrap);

            row.append(dateCell, statusCell, contentsCell, sizeCell, actionsCell);
            backupList.appendChild(row);
        });
    }

    function loadBackups() {
        if (!backupsUrl || !backupList) return Promise.resolve();
        if (backupLoadPromise) return backupLoadPromise;

        backupLoadPromise = (async () => {
            if (backupRefreshButton) backupRefreshButton.disabled = true;
            backupList.innerHTML = '<tr><td colspan="5" class="backup-list-loading">Loading backups…</td></tr>';
            try {
                const response = await fetch(backupsUrl, { method: 'GET', credentials: 'same-origin', cache: 'no-store' });
                const data = await response.json().catch(() => ({}));
                if (!response.ok) throw new Error(data?.message || `Backup listing failed (${response.status}).`);
                backupInventory = Array.isArray(data?.backups) ? data.backups : [];
                renderBackups();
            } catch (error) {
                backupInventory = [];
                backupList.innerHTML = '';
                const row = document.createElement('tr');
                const cell = document.createElement('td');
                cell.colSpan = 5;
                cell.className = 'backup-list-error';
                cell.textContent = error?.message || 'Backups could not be loaded.';
                row.appendChild(cell);
                backupList.appendChild(row);
            } finally {
                if (backupRefreshButton) backupRefreshButton.disabled = false;
                backupLoadPromise = null;
            }
        })();

        return backupLoadPromise;
    }

    function findBackup(id) {
        return backupInventory.find(item => String(item?.id || '') === String(id || '')) || null;
    }

    function downloadBackup(item) {
        if (!item || !backupDownloadUrl) return;
        const anchor = document.createElement('a');
        anchor.href = `${backupDownloadUrl}?backupId=${encodeURIComponent(item.id || '')}`;
        anchor.style.display = 'none';
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
    }

    async function removeBackup(item) {
        if (!item || !backupRemoveUrl) return;
        const approved = await window.mauiConfirm?.({
            title: 'Remove Backup?',
            message: `Permanently remove the MAUI backup from ${formatBackupDate(item.backupUtc)}? This deletes the backup folder from this server and cannot be undone.`,
            confirmText: 'Remove Backup',
            danger: true
        });
        if (!approved) return;
        try {
            const data = await postConfigurationJson(backupRemoveUrl, { backupId: item.id }, 'Backup could not be removed');
            showToast(data?.message || 'Backup removed.');
            await loadBackups();
        } catch (error) {
            showToast(error?.message || 'Backup could not be removed.', 'error');
        }
    }

    function setRestoreError(message) {
        if (!backupRestoreError) return;
        backupRestoreError.textContent = message || '';
        backupRestoreError.hidden = !message;
    }

    function updateRestoreConfirmState() {
        if (!backupRestoreConfirmButton) return;
        backupRestoreConfirmButton.disabled = backupRestoreBusy || !activeBackup
            || activeBackup.canRestore !== true
            || !backupRestoreAcknowledge?.checked
            || String(backupRestoreConfirmation?.value || '').trim() !== 'RESTORE';
        if (backupRestoreMultiWarning) backupRestoreMultiWarning.hidden = (backupRestoreMoveItOptions?.querySelectorAll('input:checked').length || 0) < 2;
    }

    function closeBackupRestoreDialog() {
        if (backupRestoreBusy) return;
        if (backupRestoreDialog?.open) backupRestoreDialog.close();
        activeBackup = null;
        backupRestoreMoveItOptions?.replaceChildren();
        if (backupRestoreAcknowledge) backupRestoreAcknowledge.checked = false;
        if (backupRestoreConfirmation) backupRestoreConfirmation.value = '';
        setRestoreError('');
        updateRestoreConfirmState();
    }

    function openBackupRestoreDialog(item) {
        if (!backupRestoreDialog || !item || backupRestoreBusy) return;
        activeBackup = item;
        if (backupRestoreDate) backupRestoreDate.textContent = formatBackupDate(item.backupUtc);
        if (backupRestoreDatabase) {
            const provider = String(item.databaseProvider || 'Unknown');
            backupRestoreDatabase.textContent = item.hasSqliteDatabase ? `${provider} · maui.db included` : provider;
        }
        const moveItCount = Number(item.moveItConfigurationCount || 0);
        if (backupRestoreMoveItSummary) {
            backupRestoreMoveItSummary.textContent = moveItCount > 0 ? `${moveItCount} export${moveItCount === 1 ? '' : 's'}` : 'Not included';
        }
        if (backupRestoreSqlWarning) backupRestoreSqlWarning.hidden = String(item.databaseProvider || '').toLowerCase() !== 'sqlserver';
        if (backupRestoreMoveItOptions) {
            backupRestoreMoveItOptions.replaceChildren();
            const environments = Array.isArray(item.moveItEnvironments) ? item.moveItEnvironments : [];
            environments.forEach(environment => {
                const label = document.createElement('label'); label.className = 'backup-restore-option';
                const checkbox = document.createElement('input'); checkbox.type = 'checkbox'; checkbox.value = environment;
                checkbox.setAttribute('data-backup-restore-environment', '');
                const text = document.createElement('span'), name = document.createElement('strong'); name.textContent = environment;
                text.append(name); label.append(checkbox, text); backupRestoreMoveItOptions.append(label);
            });
            if (!environments.length) {
                const empty = document.createElement('p'); empty.className = 'field-help'; empty.textContent = 'No MOVEit Automation configurations are included in this backup.';
                backupRestoreMoveItOptions.append(empty);
            }
        }
        if (backupRestoreAcknowledge) backupRestoreAcknowledge.checked = false;
        if (backupRestoreConfirmation) backupRestoreConfirmation.value = '';
        setRestoreError(item.canRestore === true ? '' : (item.restoreBlockReason || 'This backup cannot be restored.'));
        updateRestoreConfirmState();
        backupRestoreDialog.showModal();
        backupRestoreAcknowledge?.focus();
    }

    async function restoreBackup() {
        if (!activeBackup || !backupRestoreUrl || !backupRestoreConfirmButton || backupRestoreConfirmButton.disabled) return;
        const backup = activeBackup;
        const originalLabel = backupRestoreConfirmButton.textContent;
        const moveItEnvironments = [...(backupRestoreMoveItOptions?.querySelectorAll('input:checked') || [])].map(input => input.value);
        let restartPending = false;
        backupRestoreBusy = true;
        backupRestoreMoveItOptions?.querySelectorAll('input').forEach(input => { input.disabled = true; });
        backupRestoreConfirmButton.disabled = true;
        if (backupRestoreCancelButton) backupRestoreCancelButton.disabled = true;
        if (backupRestoreCloseButton) backupRestoreCloseButton.disabled = true;
        setRestoreError('');
        backupRestoreConfirmButton.textContent = 'Restoring…';
        try {
            const data = await postConfigurationJson(backupRestoreUrl, {
                backupId: backup.id,
                moveItEnvironments,
                acknowledged: Boolean(backupRestoreAcknowledge?.checked),
                confirmationText: String(backupRestoreConfirmation?.value || '').trim()
            }, 'Backup restore failed');
            setBackupStatus(data?.message || 'Backup restored successfully.', 'success');
            showToast(data?.message || 'Backup restored successfully.');
            if (data?.restartScheduled) {
                restartPending = true;
                if (backupRestoreConfirmButton) backupRestoreConfirmButton.textContent = 'Restarting MAUI…';
                window.setTimeout(() => window.location.replace(window.mauiAppUrl('/Account/Login')), 3000);
                return;
            }
            backupRestoreBusy = false;
            closeBackupRestoreDialog();
            await loadBackups();
        } catch (error) {
            const message = error?.message || 'Backup restore failed.';
            const networkFailure = error instanceof TypeError || /failed to fetch|networkerror|network request/i.test(message);
            setRestoreError(networkFailure
                ? 'The connection ended before MAUI returned a definitive Backup Restore result. A destructive restore may still be completing or may have completed. Verify MAUI/MOVEit state and review Configuration > Logs before attempting another restore.'
                : message);
        } finally {
            if (restartPending) return;
            backupRestoreBusy = false;
            if (!backupRestoreDialog?.open) return;
            backupRestoreMoveItOptions?.querySelectorAll('input').forEach(input => { input.disabled = false; });
            backupRestoreConfirmButton.textContent = originalLabel || 'Restore Backup';
            if (backupRestoreCancelButton) backupRestoreCancelButton.disabled = false;
            if (backupRestoreCloseButton) backupRestoreCloseButton.disabled = false;
            updateRestoreConfirmState();
        }
    }

    async function handleBackupListClick(event) {
        const button = event.target.closest('[data-backup-action]');
        if (!button) return;
        const row = button.closest('[data-backup-id]');
        const item = findBackup(row?.dataset.backupId || '');
        if (!item) return;
        const action = button.dataset.backupAction || '';
        if (action === 'download') downloadBackup(item);
        else if (action === 'remove') await removeBackup(item);
        else if (action === 'restore') openBackupRestoreDialog(item);
    }

    async function runBackupNow() {
        if (!backupNowUrl || !backupNowButton || !ensureBackupSettingsSaved()) return;
        backupNowButton.disabled = true;
        setBackupStatus('Running backup…', 'testing');
        try {
            const headers = new Headers();
            if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
            const response = await fetch(backupNowUrl, { method: 'POST', headers, credentials: 'same-origin' });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data?.message || `Backup failed (${response.status}).`);
            const details = Array.isArray(data?.messages) && data.messages.length ? ` ${data.messages.join(' ')}` : '';
            const folder = data?.folderPath ? ` Folder: ${data.folderPath}` : '';
            setBackupStatus(`${data?.message || 'Backup completed.'}${folder}${details}`, 'success');
            showToast(data?.message || 'Backup completed.');
            await loadBackups();
        } catch (error) {
            setBackupStatus(error?.message || 'Backup failed.', 'error');
        } finally {
            backupNowButton.disabled = false;
        }
    }

    function initialize(context) {
        if (initialized) return;
        workspace = context?.workspace;
        if (!workspace) return;
        initialized = true;
        antiForgeryToken = context?.antiForgeryToken || '';
        showToast = typeof context?.showToast === 'function' ? context.showToast : () => {};
        checked = typeof context?.checked === 'function' ? context.checked : () => false;
        numberValue = typeof context?.numberValue === 'function' ? context.numberValue : () => 0;
        backupNowUrl = workspace.dataset.backupNowUrl || '';
        backupsUrl = workspace.dataset.backupsUrl || '';
        backupDownloadUrl = workspace.dataset.backupDownloadUrl || '';
        backupRemoveUrl = workspace.dataset.backupRemoveUrl || '';
        backupRestoreUrl = workspace.dataset.backupRestoreUrl || '';
        dataAccessReadinessUrl = workspace.dataset.dataAccessReadinessUrl || '';
        dataAccessSaveUrl = workspace.dataset.dataAccessSaveUrl || '';
        dataAccessTestUrl = workspace.dataset.dataAccessTestUrl || '';
        dataAccessClearUrl = workspace.dataset.dataAccessClearUrl || '';
        backupNowButton = workspace.querySelector('[data-backup-now]');
        backupRefreshButton = workspace.querySelector('[data-backup-refresh]');
        backupList = workspace.querySelector('[data-backup-list]');
        backupStatus = workspace.querySelector('[data-backup-status]');
        backupReadiness = workspace.querySelector('[data-backup-readiness]');
        backupDataAccessWarning = workspace.querySelector('[data-backup-data-access-warning]');
        backupDataAccessWarningText = workspace.querySelector('[data-backup-data-access-warning-text]');
        schedulerReadiness = workspace.querySelector('[data-scheduler-readiness]');
        schedulerDataAccessWarning = workspace.querySelector('[data-scheduler-data-access-warning]');
        schedulerDataAccessWarningText = workspace.querySelector('[data-scheduler-data-access-warning-text]');
        dataAccessDialog = document.querySelector('[data-data-access-dialog]');
        dataAccessDialogTitle = dataAccessDialog?.querySelector('[data-data-access-dialog-title]');
        dataAccessServerLabel = dataAccessDialog?.querySelector('[data-data-access-server]');
        dataAccessServerKey = dataAccessDialog?.querySelector('[data-data-access-server-key]');
        dataAccessUsername = dataAccessDialog?.querySelector('[data-data-access-username-input]');
        dataAccessPassword = dataAccessDialog?.querySelector('[data-data-access-password-input]');
        dataAccessPasswordHelp = dataAccessDialog?.querySelector('[data-data-access-password-help]');
        dataAccessError = dataAccessDialog?.querySelector('[data-data-access-error]');
        dataAccessSaveButton = dataAccessDialog?.querySelector('[data-data-access-save]');
        dataAccessCancelButton = dataAccessDialog?.querySelector('[data-data-access-cancel]');
        dataAccessCloseButton = dataAccessDialog?.querySelector('[data-data-access-close]');
        backupRestoreDialog = document.querySelector('[data-backup-restore-dialog]');
        backupRestoreDate = backupRestoreDialog?.querySelector('[data-backup-restore-date]');
        backupRestoreDatabase = backupRestoreDialog?.querySelector('[data-backup-restore-database]');
        backupRestoreMoveItSummary = backupRestoreDialog?.querySelector('[data-backup-restore-moveit-summary]');
        backupRestoreSqlWarning = backupRestoreDialog?.querySelector('[data-backup-restore-sql-warning]');
        backupRestoreMoveItOptions = backupRestoreDialog?.querySelector('[data-backup-restore-moveit-options]');
        backupRestoreMultiWarning = backupRestoreDialog?.querySelector('[data-backup-restore-multi-warning]');
        backupRestoreAcknowledge = backupRestoreDialog?.querySelector('[data-backup-restore-acknowledge]');
        backupRestoreConfirmation = backupRestoreDialog?.querySelector('[data-backup-restore-confirmation]');
        backupRestoreError = backupRestoreDialog?.querySelector('[data-backup-restore-error]');
        backupRestoreConfirmButton = backupRestoreDialog?.querySelector('[data-backup-restore-confirm]');
        backupRestoreCancelButton = backupRestoreDialog?.querySelector('[data-backup-restore-cancel]');
        backupRestoreCloseButton = backupRestoreDialog?.querySelector('[data-backup-restore-close]');

        backupBaseline = backupSettingsSignature();
        backupNowButton?.addEventListener('click', runBackupNow);
        backupRefreshButton?.addEventListener('click', loadBackups);
        backupList?.addEventListener('click', event => { void handleBackupListClick(event); });
        backupRestoreAcknowledge?.addEventListener('change', updateRestoreConfirmState);
        backupRestoreConfirmation?.addEventListener('input', updateRestoreConfirmState);
        backupRestoreMoveItOptions?.addEventListener('change', updateRestoreConfirmState);
        backupRestoreConfirmButton?.addEventListener('click', restoreBackup);
        backupRestoreCancelButton?.addEventListener('click', closeBackupRestoreDialog);
        backupRestoreCloseButton?.addEventListener('click', closeBackupRestoreDialog);
        backupRestoreDialog?.addEventListener('cancel', event => { event.preventDefault(); closeBackupRestoreDialog(); });
        window.addEventListener('maui:data-access-credentials-changed', () => { void loadDataAccessReadiness(); });
        dataAccessSaveButton?.addEventListener('click', saveDataAccessCredentials);
        dataAccessCancelButton?.addEventListener('click', closeDataAccessDialog);
        dataAccessCloseButton?.addEventListener('click', closeDataAccessDialog);
        dataAccessDialog?.addEventListener('cancel', event => { event.preventDefault(); closeDataAccessDialog(); });
        void loadDataAccessReadiness();
    }

    function markSettingsSaved() {
        if (initialized) backupBaseline = backupSettingsSignature();
    }

    window.MauiConfigurationOperations = Object.freeze({ initialize, wireDataAccessRow, syncDataAccessRow, markSettingsSaved, refreshBackups: loadBackups });
})();
