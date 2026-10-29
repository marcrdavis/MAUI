/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Configuration workspace behavior.
 */

(() => {
    const workspace = document.querySelector('[data-configuration-workspace]');
    if (!workspace) return;

    const saveUrl = workspace.dataset.saveUrl || '';
    let configurationRevision = workspace.dataset.configurationRevision || '';
    // Independent credential writes update the concurrency token without changing user edits.
    window.addEventListener('maui:configuration-revision-updated', event => {
        const revision = String(event.detail?.revision || '');
        if (!revision) return;
        configurationRevision = revision;
        workspace.dataset.configurationRevision = revision;
    });
    const databaseTestUrl = workspace.dataset.databaseTestUrl || '';
    const antiForgeryToken = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    let categoryButtons = [...workspace.querySelectorAll('[data-config-section-target]')];
    const sections = [...workspace.querySelectorAll('[data-config-section]')];
    const searchInput = workspace.querySelector('[data-configuration-search]');
    const saveButton = workspace.querySelector('[data-config-save]');
    const discardButton = workspace.querySelector('[data-config-discard]');
    const saveBar = workspace.querySelector('.configuration-save-bar');
    const saveState = workspace.querySelector('[data-configuration-save-state]');
    const toast = document.querySelector('[data-configuration-toast]');
    const serverList = workspace.querySelector('[data-server-list]');
    const serverTemplate = document.querySelector('[data-server-template]');
    const metadataTemplate = document.querySelector('[data-metadata-field-template]');
    const metadataDeleteDialog = document.querySelector('[data-metadata-field-delete-dialog]');
    const metadataDeleteClose = metadataDeleteDialog?.querySelector('[data-metadata-field-delete-close]');
    const metadataDeleteCancel = metadataDeleteDialog?.querySelector('[data-metadata-field-delete-cancel]');
    const metadataDeleteConfirm = metadataDeleteDialog?.querySelector('[data-metadata-field-delete-confirm]');
    const metadataDeleteName = metadataDeleteDialog?.querySelector('[data-metadata-field-delete-name]');
    const metadataDeleteMessage = metadataDeleteDialog?.querySelector('[data-metadata-field-delete-message]');
    const securityList = workspace.querySelector('[data-security-user-list]');
    const securityTemplate = document.querySelector('[data-security-user-template]');
    const restartBanner = document.querySelector('[data-restart-banner]');
    const restartReasons = restartBanner?.querySelector('[data-restart-reasons]');
    const databaseTestButton = workspace.querySelector('[data-test-database-configuration]');
    const databaseTestStatus = workspace.querySelector('[data-database-test-status]');

    let baseline = '';
    let securityOriginalUsernames = [...workspace.querySelectorAll('[data-security-user][data-security-original="true"]')]
        .map(row => String(row.dataset.securityUsername || row.querySelector('[data-security-field="username"]')?.value || '').trim())
        .filter(Boolean);
    let saving = false;
    let pendingMetadataDeleteRow = null;
    let toastTimer = 0;

    // Purpose: Displays a transient configuration status message.
    function showToast(message, kind = 'default') {
        if (!toast) return;
        if (typeof window.mauiDisplayToast === 'function') {
            window.mauiDisplayToast(toast, message, kind, 5000);
            return;
        }
        toast.textContent = message || '';
        toast.classList.toggle('error', kind === 'error');
        toast.classList.add('visible');
        window.clearTimeout(toastTimer);
        toastTimer = window.setTimeout(() => toast.classList.remove('visible'), 5000);
    }

    // Purpose: Sets active section.
    function setActiveSection(key) {
        const openingBackup = key === 'backup' && !sections.some(section =>
            section.dataset.configSection === 'backup' && section.classList.contains('active'));
        categoryButtons.forEach(button => button.classList.toggle('active', button.dataset.configSectionTarget === key));
        sections.forEach(section => section.classList.toggle('active', section.dataset.configSection === key));
        if (openingBackup) void window.MauiConfigurationOperations?.refreshBackups?.();
    }

    // Purpose: Initializes category navigation and removes categories that have no rendered settings section.
    function initializeCategoryNavigation() {
        categoryButtons = categoryButtons.filter(button => {
            const key = button.dataset.configSectionTarget || '';
            const hasSection = sections.some(section => section.dataset.configSection === key);
            if (!hasSection) button.remove();
            return hasSection;
        });
        categoryButtons.forEach(button => button.addEventListener('click', () => setActiveSection(button.dataset.configSectionTarget || 'general')));
        searchInput?.addEventListener('input', () => {
            const query = String(searchInput.value || '').trim().toLowerCase();
            let firstMatch = null;
            categoryButtons.forEach(button => {
                const key = button.dataset.configSectionTarget || '';
                const section = sections.find(item => item.dataset.configSection === key);
                const haystack = `${button.textContent || ''} ${section?.dataset.searchTerms || ''} ${section?.textContent || ''}`.toLowerCase();
                const matches = !query || haystack.includes(query);
                button.classList.toggle('search-hidden', !matches);
                if (matches && !firstMatch) firstMatch = key;
            });
            const activeButton = categoryButtons.find(button => button.classList.contains('active') && !button.classList.contains('search-hidden'));
            if (!activeButton && firstMatch) setActiveSection(firstMatch);
        });
    }

    // Purpose: Shows the configuration resource panel matching the selected resource type.
    function activateResourcePanels(select, selector, attribute) {
        if (!select) return;
        // Purpose: Refreshes visibility and state for the active configuration resource panel.
        const update = () => {
            workspace.querySelectorAll(selector).forEach(panel => {
                panel.classList.toggle('active', panel.getAttribute(attribute) === select.value);
            });
        };
        select.addEventListener('change', update);
        update();
    }

    // Purpose: Generates a stable internal key for a newly configured MOVEit server.
    function generateServerKey() {
        let suffix = '';
        if (window.crypto?.randomUUID) {
            suffix = window.crypto.randomUUID().replaceAll('-', '').slice(0, 12);
        } else if (window.crypto?.getRandomValues) {
            const bytes = new Uint8Array(6);
            window.crypto.getRandomValues(bytes);
            suffix = [...bytes].map(value => value.toString(16).padStart(2, '0')).join('');
        } else {
            suffix = `${Date.now().toString(16)}${Math.random().toString(16).slice(2)}`.replace('.', '').slice(0, 12);
        }
        return `srv-${suffix}`;
    }

    // Purpose: Wires server row.
    function wireServerRow(row) {
        row.querySelector('[data-remove-server]')?.addEventListener('click', () => {
            const rows = serverList?.querySelectorAll('[data-server-row]') || [];
            if (rows.length <= 1) {
                showToast('At least one MOVEit server must remain configured.');
                return;
            }
            row.remove();
            refreshDirtyState();
        });
        const displayName = row.querySelector('[data-server-field="displayName"]');
        displayName?.addEventListener('input', () => {
            const title = row.querySelector('[data-server-title]');
            if (title) title.textContent = displayName.value.trim() || 'New MOVEit Server';
        });
        window.MauiConfigurationOperations?.wireDataAccessRow(row.querySelector('[data-data-access-row]'));
    }

    // Purpose: Initializes servers.
    function initializeServers() {
        serverList?.querySelectorAll('[data-server-row]').forEach(wireServerRow);
        workspace.querySelector('[data-add-server]')?.addEventListener('click', () => {
            if (!serverTemplate || !serverList) return;
            const fragment = serverTemplate.content.cloneNode(true);
            const row = fragment.querySelector('[data-server-row]');
            if (!row) return;
            const keyInput = row.querySelector('[data-server-field="key"]');
            if (keyInput) keyInput.value = generateServerKey();
            const dataAccessRow = row.querySelector('[data-data-access-row]');
            if (dataAccessRow) dataAccessRow.dataset.serverKey = keyInput?.value || '';
            wireServerRow(row);
            serverList.appendChild(fragment);
            row.querySelector('[data-server-field="displayName"]')?.focus();
            refreshDirtyState();
        });
    }

    // Purpose: Wires metadata row.
    function wireMetadataRow(row) {
        const resourcePanel = row.closest('[data-metadata-resource]');
        const allowedTypes = new Set(String(resourcePanel?.dataset.metadataAllowedTypes || '')
            .split(',')
            .map(value => value.trim())
            .filter(Boolean));
        const typeControl = row.querySelector('[data-metadata-field="type"]');
        const systemManaged = row.dataset.metadataSystemManaged === 'true';
        if (systemManaged) {
            row.querySelectorAll('[data-metadata-field], [data-remove-metadata-field]').forEach(control => {
                control.disabled = true;
            });
            return;
        }

        if (typeControl && allowedTypes.size > 0) {
            [...typeControl.options].forEach(option => {
                if (!allowedTypes.has(option.value)) option.remove();
            });
        }

        row.querySelector('[data-remove-metadata-field]')?.addEventListener('click', () => requestMetadataFieldRemoval(row));
        const type = row.querySelector('[data-metadata-field="type"]');
        const possibleValues = row.querySelector('[data-metadata-field="possibleValues"]');
        // Purpose: Updates possible values state.
        const updatePossibleValuesState = () => {
            if (!possibleValues || !type) return;
            const isList = String(type.value).toLowerCase() === 'list';
            possibleValues.disabled = !isList;
            possibleValues.placeholder = isList ? 'Comma-separated values' : 'Only used for List';
            if (!isList) possibleValues.value = '';
        };
        type?.addEventListener('change', () => {
            updatePossibleValuesState();
            refreshDirtyState();
        });
        updatePossibleValuesState();
    }

    // Purpose: Closes the destructive metadata-field confirmation without modifying the pending Settings payload.
    function closeMetadataFieldDeleteDialog() {
        pendingMetadataDeleteRow = null;
        if (metadataDeleteDialog?.open) metadataDeleteDialog.close();
    }

    // Purpose: Removes a new unsaved field immediately, but requires explicit confirmation for persisted fields.
    function requestMetadataFieldRemoval(row) {
        if (row.dataset.metadataPersisted !== 'true') {
            row.remove();
            refreshDirtyState();
            return;
        }

        pendingMetadataDeleteRow = row;
        const fieldName = String(row.querySelector('[data-metadata-field="name"]')?.value || '').trim();
        const fieldLabel = String(row.querySelector('[data-metadata-field="label"]')?.value || '').trim() || fieldName;
        const resourceKey = row.closest('[data-metadata-resource]')?.dataset.metadataResource || '';
        const resourceSelect = workspace.querySelector('[data-metadata-resource-select]');
        const resourceLabel = [...(resourceSelect?.options || [])].find(option => option.value === resourceKey)?.textContent?.trim() || resourceKey;
        if (metadataDeleteName) metadataDeleteName.textContent = fieldLabel === fieldName ? fieldName : `${fieldLabel} (${fieldName})`;
        if (metadataDeleteMessage) metadataDeleteMessage.textContent = `Remove this field from ${resourceLabel}? The deletion will take effect only when you click Save Changes.`;
        metadataDeleteDialog?.showModal();
    }

    // Purpose: Accepts the destructive field removal into the pending configuration; the server performs data cleanup on Save Changes.
    function confirmMetadataFieldRemoval() {
        const row = pendingMetadataDeleteRow;
        pendingMetadataDeleteRow = null;
        if (metadataDeleteDialog?.open) metadataDeleteDialog.close();
        if (!row) return;
        row.remove();
        refreshDirtyState();
    }

    // Purpose: Initializes metadata editor.
    function initializeMetadataEditor() {
        const select = workspace.querySelector('[data-metadata-resource-select]');
        activateResourcePanels(select, '[data-metadata-resource]', 'data-metadata-resource');
        workspace.querySelectorAll('[data-metadata-row]').forEach(wireMetadataRow);
        workspace.querySelectorAll('[data-add-metadata-field]').forEach(button => {
            button.addEventListener('click', () => {
                const panel = button.closest('[data-metadata-resource]');
                const container = panel?.querySelector('[data-metadata-fields]');
                if (!container || !metadataTemplate) return;
                const fragment = metadataTemplate.content.cloneNode(true);
                const row = fragment.querySelector('[data-metadata-row]');
                if (!row) return;
                container.appendChild(fragment);
                wireMetadataRow(row);
                row.querySelector('[data-metadata-field="name"]')?.focus();
                refreshDirtyState();
            });
        });
        metadataDeleteClose?.addEventListener('click', closeMetadataFieldDeleteDialog);
        metadataDeleteCancel?.addEventListener('click', closeMetadataFieldDeleteDialog);
        metadataDeleteConfirm?.addEventListener('click', confirmMetadataFieldRemoval);
        metadataDeleteDialog?.addEventListener('cancel', event => {
            event.preventDefault();
            closeMetadataFieldDeleteDialog();
        });
    }

    // Purpose: Formats security last login.
    function formatSecurityLastLogin(row) {
        const display = row.querySelector('[data-security-last-login]');
        const utc = String(display?.dataset.utc || '').trim();
        if (!display || !utc) return;
        const value = new Date(utc);
        if (Number.isNaN(value.getTime())) return;
        display.textContent = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(value);
        display.title = `${value.toISOString()} (UTC)`;
    }

    // Purpose: Returns security admins.
    function countSecurityAdmins() {
        return [...(securityList?.querySelectorAll('[data-security-user]') || [])]
            .filter(row => String(row.querySelector('[data-security-field="role"]')?.value || '').toLowerCase() === 'admin')
            .length;
    }

    // Purpose: Wires security row.
    function wireSecurityRow(row) {
        formatSecurityLastLogin(row);
        row.querySelector('[data-remove-security-user]')?.addEventListener('click', () => {
            const role = String(row.querySelector('[data-security-field="role"]')?.value || 'User');
            if (role.toLowerCase() === 'admin' && countSecurityAdmins() <= 1) {
                showToast('At least one MAUI Admin must remain configured.');
                return;
            }
            row.remove();
            refreshDirtyState();
        });
    }

    // Purpose: Initializes security editor.
    function initializeSecurityEditor() {
        securityList?.querySelectorAll('[data-security-user]').forEach(wireSecurityRow);
        workspace.querySelector('[data-add-security-user]')?.addEventListener('click', () => {
            if (!securityList || !securityTemplate) return;
            const fragment = securityTemplate.content.cloneNode(true);
            const row = fragment.querySelector('[data-security-user]');
            if (!row) return;
            wireSecurityRow(row);
            securityList.appendChild(fragment);
            row.querySelector('[data-security-field="username"]')?.focus();
            refreshDirtyState();
        });
    }

    // Purpose: Rebuilds the available-column choices for the selected grid definition.
    function rebuildDefaultColumnAddOptions(panel) {
        const select = panel.querySelector('[data-default-column-add-select]');
        if (!select) return;
        const existing = new Set([...panel.querySelectorAll('[data-default-column]')].map(item => item.dataset.defaultColumn));
        [...select.options].forEach(option => {
            if (!option.value) return;
            option.hidden = existing.has(option.value);
            option.disabled = existing.has(option.value);
        });
        select.value = '';
    }

    // Purpose: Creates default column row.
    function createDefaultColumnRow(column) {
        const row = document.createElement('div');
        row.className = 'configuration-default-column';
        row.dataset.defaultColumn = column;
        const label = document.createElement('span');
        label.textContent = column;
        const actions = document.createElement('div');
        actions.innerHTML = '<button type="button" data-column-up title="Move up">↑</button><button type="button" data-column-down title="Move down">↓</button><button type="button" data-column-remove title="Remove">×</button>';
        row.append(label, actions);
        return row;
    }

    // Purpose: Wires default column row.
    function wireDefaultColumnRow(row) {
        row.querySelector('[data-column-up]')?.addEventListener('click', () => {
            const previous = row.previousElementSibling;
            if (previous) row.parentElement?.insertBefore(row, previous);
            refreshDirtyState();
        });
        row.querySelector('[data-column-down]')?.addEventListener('click', () => {
            const next = row.nextElementSibling;
            if (next) row.parentElement?.insertBefore(next, row);
            refreshDirtyState();
        });
        row.querySelector('[data-column-remove]')?.addEventListener('click', () => {
            const panel = row.closest('[data-grid-resource]');
            if (row.parentElement?.children.length <= 1) {
                showToast('A grid must have at least one default column.');
                return;
            }
            row.remove();
            if (panel) rebuildDefaultColumnAddOptions(panel);
            refreshDirtyState();
        });
    }

    // Purpose: Initializes grid definitions.
    function initializeGridDefinitions() {
        const select = workspace.querySelector('[data-grid-resource-select]');
        activateResourcePanels(select, '[data-grid-resource]', 'data-grid-resource');
        workspace.querySelectorAll('[data-grid-resource]').forEach(panel => {
            panel.querySelectorAll('[data-default-column]').forEach(wireDefaultColumnRow);
            panel.querySelector('[data-default-column-add]')?.addEventListener('click', () => {
                const selectControl = panel.querySelector('[data-default-column-add-select]');
                const column = String(selectControl?.value || '').trim();
                if (!column) return;
                const list = panel.querySelector('[data-default-column-list]');
                if (!list || list.querySelector(`[data-default-column="${CSS.escape(column)}"]`)) return;
                const row = createDefaultColumnRow(column);
                wireDefaultColumnRow(row);
                list.appendChild(row);
                rebuildDefaultColumnAddOptions(panel);
                refreshDirtyState();
            });
            rebuildDefaultColumnAddOptions(panel);
        });
    }

    // Purpose: Reads a string value from a configuration control.
    function value(name) {
        return workspace.querySelector(`[data-setting="${name}"]`)?.value ?? '';
    }

    // Purpose: Reads a Boolean value from a configuration checkbox.
    function checked(name) {
        const input = workspace.querySelector(`[data-setting="${name}"]`);
        if (!input) return false;
        if (input.type === 'checkbox') return Boolean(input.checked);
        return String(input.value || '').trim().toLowerCase() === 'true';
    }

    // Purpose: Reads and normalizes a numeric value from a configuration control.
    function numberValue(name) {
        return Number(value(name));
    }

    // Purpose: Collects servers.
    function collectServers() {
        return [...(serverList?.querySelectorAll('[data-server-row]') || [])].map(row => ({
            key: row.querySelector('[data-server-field="key"]')?.value.trim() || '',
            environmentKey: row.querySelector('[data-server-field="environmentKey"]')?.value.trim() || '',
            displayName: row.querySelector('[data-server-field="displayName"]')?.value.trim() || '',
            webAdminUrl: row.querySelector('[data-server-field="webAdminUrl"]')?.value.trim() || '',
            serverHost: row.querySelector('[data-server-field="serverHost"]')?.value.trim() || ''
        }));
    }

    // Purpose: Collects security users.
    function collectSecurityUsers() {
        return [...(securityList?.querySelectorAll('[data-security-user]') || [])].map(row => ({
            username: row.querySelector('[data-security-field="username"]')?.value.trim() || '',
            role: row.querySelector('[data-security-field="role"]')?.value || 'User'
        }));
    }

    // Purpose: Collects refresh settings.
    function collectRefreshSettings() {
        return [...workspace.querySelectorAll('[data-refresh-row]')].map(row => ({
            key: row.dataset.refreshKey || '',
            cacheRefreshSeconds: Number(row.querySelector('[data-refresh-field="cache"]')?.value || 0),
            uiRefreshIntervalSeconds: Number(row.querySelector('[data-refresh-field="ui"]')?.value || 0)
        }));
    }

    // Purpose: Collects report settings.
    function collectReportSettings() {
        return [...workspace.querySelectorAll('[data-report-setting-row]')].map(row => ({
            key: row.dataset.reportSettingKey || '',
            batchResultLimit: Number(row.querySelector('[data-report-setting-field="batch"]')?.value || 500),
            uiRefreshIntervalSeconds: Number(row.querySelector('[data-report-setting-field="ui"]')?.value || 0)
        }));
    }

    // Purpose: Collects metadata resources.
    function collectMetadataResources() {
        return [...workspace.querySelectorAll('[data-metadata-resource]')].map(panel => ({
            key: panel.dataset.metadataResource || '',
            fields: [...panel.querySelectorAll('[data-metadata-row]')].map(row => ({
                name: row.querySelector('[data-metadata-field="name"]')?.value.trim() || '',
                label: row.querySelector('[data-metadata-field="label"]')?.value.trim() || '',
                description: row.querySelector('[data-metadata-field="description"]')?.value.trim() || '',
                type: row.querySelector('[data-metadata-field="type"]')?.value || 'String',
                possibleValues: String(row.querySelector('[data-metadata-field="possibleValues"]')?.value || '').split(',').map(item => item.trim()).filter(Boolean),
                required: Boolean(row.querySelector('[data-metadata-field="required"]')?.checked)
            }))
        }));
    }

    // Purpose: Collects grid definitions.
    function collectGridDefinitions() {
        return [...workspace.querySelectorAll('[data-grid-resource]')].map(panel => ({
            key: panel.dataset.gridResource || '',
            defaultColumns: [...panel.querySelectorAll('[data-default-column]')].map(row => row.dataset.defaultColumn || '').filter(Boolean),
            searchColumns: [...panel.querySelectorAll('[data-search-column-list] input[type="checkbox"]:checked')].map(input => input.value)
        }));
    }

    // Purpose: Collects SMTP fields that participate in the common Configuration save.
    // Test Recipient is intentionally excluded because it is an operation-only value.
    function collectSmtpSettings() {
        const get = key => workspace.querySelector(`[data-report-smtp="${key}"]`);
        return {
            host: String(get('host')?.value || '').trim(),
            port: Number(get('port')?.value || 25),
            enableSsl: Boolean(get('enableSsl')?.checked),
            ignoreCertificateErrors: Boolean(get('ignoreCertificateErrors')?.checked),
            username: String(get('username')?.value || '').trim(),
            password: get('password')?.value || '',
            fromAddress: String(get('fromAddress')?.value || '').trim(),
            fromName: String(get('fromName')?.value || '').trim()
        };
    }

    // Purpose: Collects the complete editable Configuration Settings payload.
    function collectPayload() {
        return {
            configurationRevision,
            sshAlgorithms: window.MauiSshAlgorithmSettings?.collect() ?? null,
            idleSessionTimeoutMinutes: numberValue('idleSessionTimeoutMinutes'),
            checkoutObjectsForDurationOfEdit: checked('checkoutObjectsForDurationOfEdit'),
            autoFormatNewTaskHostNames: checked('autoFormatNewTaskHostNames'),
            importRowLimit: numberValue('importRowLimit'),
            enableQuickTransfer: checked('enableQuickTransfer'),
            taskJsonEditor: checked('taskJsonEditor'),
            copyBetweenEnvironments: checked('copyBetweenEnvironments'),
            copyRecoveryRetentionDays: Number(value('copyRecoveryRetentionDays') || 30),
            keepAliveEnabled: checked('keepAliveEnabled'),
            keepAliveIntervalMinutes: numberValue('keepAliveIntervalMinutes'),
            useMauiTaskEditor: checked('useMauiTaskEditor'),
            useMauiHostEditor: checked('useMauiHostEditor'),
            useMauiScriptEditor: checked('useMauiScriptEditor'),
            useMauiDateListEditor: checked('useMauiDateListEditor'),
            useMauiSystemSettingsEditor: checked('useMauiSystemSettingsEditor'),
            useMauiGroupsAndPermissionsEditor: checked('useMauiGroupsAndPermissionsEditor'),
            taskFailureAlertsEnabled: checked('taskFailureAlertsEnabled'),
            recycleBinEnabled: checked('recycleBinEnabled'),
            recycleBinRetentionDays: numberValue('recycleBinRetentionDays'),
            logRetentionDays: numberValue('logRetentionDays'),
            jobHistoryRetentionDays: numberValue('jobHistoryRetentionDays'),
            systemMessageEnabled: checked('systemMessageEnabled'),
            systemMessageType: value('systemMessageType'),
            systemMessageBody: value('systemMessageBody'),
            backupEnabled: checked('backupEnabled'),
            backupFrequencyHours: numberValue('backupFrequencyHours'),
            backupRetentionDays: numberValue('backupRetentionDays'),
            taskRunHistoryMaxCount: numberValue('taskRunHistoryMaxCount'),
            requestTimeoutSeconds: numberValue('requestTimeoutSeconds'),
            allowInvalidServerCertificates: checked('allowInvalidServerCertificates'),
            servers: collectServers(),
            databaseProvider: value('databaseProvider'),
            databaseConnectionString: String(value('databaseConnectionString')).trim(),
            fitColumnsToGrid: checked('fitColumnsToGrid'),
            highlightFailedTasksInGrid: checked('highlightFailedTasksInGrid'),
            defaultRowSize: numberValue('defaultRowSize'),
            selectRowsBeyondGridSize: checked('selectRowsBeyondGridSize'),
            refreshSettings: collectRefreshSettings(),
            reportSettings: collectReportSettings(),
            smtp: collectSmtpSettings(),
            metadataResources: collectMetadataResources(),
            gridDefinitions: collectGridDefinitions(),
            securityUsers: collectSecurityUsers(),
            securityOriginalUsernames: [...securityOriginalUsernames]
        };
    }

    // Purpose: Validates the Configuration Settings payload before it is saved.
    function validatePayload(payload) {
        if (!Number.isInteger(payload.keepAliveIntervalMinutes) || payload.keepAliveIntervalMinutes < 1 || payload.keepAliveIntervalMinutes > 20) throw new Error('Ping Interval must be between 1 and 20 minutes.');
        if (!Number.isInteger(payload.requestTimeoutSeconds) || payload.requestTimeoutSeconds < 5 || payload.requestTimeoutSeconds > 300) throw new Error('REST Request Timeout must be between 5 and 300 seconds.');
        if (!Number.isInteger(payload.importRowLimit) || payload.importRowLimit < 1 || payload.importRowLimit > 1000) throw new Error('Import Row Limit must be between 1 and 1,000.');
        if (!Number.isInteger(payload.taskRunHistoryMaxCount) || payload.taskRunHistoryMaxCount < 1 || payload.taskRunHistoryMaxCount > 100000) throw new Error('Task Run History Maximum must be between 1 and 100,000.');
        if (!Number.isInteger(payload.recycleBinRetentionDays) || payload.recycleBinRetentionDays < 1 || payload.recycleBinRetentionDays > 365) throw new Error('Recycle Bin Retention must be between 1 and 365 days.');
        if (!Number.isInteger(payload.logRetentionDays) || payload.logRetentionDays < 1 || payload.logRetentionDays > 365) throw new Error('Log Retention must be between 1 and 365 days.');
        if (![7, 14, 30, 60, 90, 180, 365].includes(payload.jobHistoryRetentionDays)) throw new Error('Job History Retention must be 7, 14, 30, 60, 90, 180, or 365 days.');
        if (!['Information', 'Important', 'Critical'].includes(payload.systemMessageType)) throw new Error('System Message Type must be Information, Important, or Critical.');
        if (![4, 8, 12, 24].includes(payload.backupFrequencyHours)) throw new Error('Backup Frequency must be 4, 8, 12, or 24 hours.');
        if (!Number.isInteger(payload.backupRetentionDays) || payload.backupRetentionDays < 1 || payload.backupRetentionDays > 30) throw new Error('Backup Retention must be between 1 and 30 days.');
        if (!payload.databaseConnectionString) throw new Error('Database Connection String is required.');
        if (!payload.servers.length) throw new Error('At least one MOVEit server must be configured.');

        const serverKeys = new Set();
        for (const server of payload.servers) {
            if (!server.key || !server.environmentKey || !server.displayName || !server.webAdminUrl) throw new Error('Every MOVEit server requires a Key, Environment Key, Display Name, and Web Admin URL.');
            const normalized = server.key.toLowerCase();
            if (serverKeys.has(normalized)) throw new Error(`MOVEit server Key "${server.key}" is duplicated.`);
            serverKeys.add(normalized);
            try {
                const url = new URL(server.webAdminUrl);
                if (!['http:', 'https:'].includes(url.protocol)) throw new Error();
            } catch {
                throw new Error(`MOVEit server "${server.key}" requires a valid HTTP or HTTPS Web Admin URL.`);
            }
        }

        for (const refresh of payload.refreshSettings) {
            if (!Number.isInteger(refresh.cacheRefreshSeconds) || refresh.cacheRefreshSeconds < 0 || !Number.isInteger(refresh.uiRefreshIntervalSeconds) || refresh.uiRefreshIntervalSeconds < 0) {
                throw new Error(`Refresh values for ${refresh.key} must be zero or positive whole numbers.`);
            }
        }

        const allowedReportBatchSizes = new Set([100, 200, 500, 1000, 2000, 5000, 10000]);
        for (const report of payload.reportSettings) {
            if (!allowedReportBatchSizes.has(report.batchResultLimit)) throw new Error(`Report batch limit for ${report.key} is not supported.`);
            if (!Number.isInteger(report.uiRefreshIntervalSeconds) || report.uiRefreshIntervalSeconds < 0 || report.uiRefreshIntervalSeconds > 86400) {
                throw new Error(`UI refresh interval for ${report.key} must be between 0 and 86,400 seconds.`);
            }
        }

        const smtp = payload.smtp || {};
        if (!Number.isInteger(smtp.port) || smtp.port < 1 || smtp.port > 65535) throw new Error('SMTP Port must be between 1 and 65535.');
        const smtpFromAddress = workspace.querySelector('[data-report-smtp="fromAddress"]');
        if (smtp.fromAddress && smtpFromAddress && !smtpFromAddress.checkValidity()) throw new Error('SMTP From Address is not a valid email address.');

        for (const resource of payload.metadataResources) {
            const names = new Set();
            for (const field of resource.fields) {
                if (!field.name) throw new Error(`Every ${resource.key} metadata field requires a Name.`);
                const normalized = field.name.toLowerCase();
                if (names.has(normalized)) throw new Error(`Metadata field "${field.name}" is duplicated for ${resource.key}.`);
                names.add(normalized);
                if (!field.label) field.label = field.name;
            }
        }

        const securityNames = new Set();
        let adminCount = 0;
        for (const user of payload.securityUsers) {
            if (!user.username) throw new Error('Every Security entry requires a MOVEit Username.');
            if (user.username.length > 128) throw new Error('MOVEit usernames used by MAUI Security cannot exceed 128 characters.');
            const normalized = user.username.toLowerCase();
            if (securityNames.has(normalized)) throw new Error(`MOVEit username "${user.username}" is duplicated in Security.`);
            securityNames.add(normalized);
            if (!['user', 'admin'].includes(String(user.role).toLowerCase())) throw new Error(`Role "${user.role}" is not valid.`);
            if (String(user.role).toLowerCase() === 'admin') adminCount += 1;
        }
        if (adminCount < 1) throw new Error('At least one MAUI Admin must remain configured.');

        for (const grid of payload.gridDefinitions) {
            if (!grid.defaultColumns.length) throw new Error(`Grid ${grid.key} must have at least one default column.`);
        }
    }


    // Purpose: Builds a stable signature used for dirty-state comparisons.
    function payloadSignature() {
        // A document revision is concurrency metadata, not a user-editable field.
        const payload = collectPayload();
        delete payload.configurationRevision;
        return JSON.stringify(payload);
    }

    // Connection edits affect availability immediately; re-adding an environment never opts in automatically.
    function syncCopyAvailability() {
        const environments = new Set(collectServers().map(server => (server.environmentKey || server.key).trim().toUpperCase()).filter(Boolean));
        const available = environments.size >= 2;
        const toggle = workspace.querySelector('[data-setting="copyBetweenEnvironments"]');
        if (toggle && !available) toggle.checked = false;
        for (const setting of ['copyBetweenEnvironments', 'copyRecoveryRetentionDays']) {
            const control = workspace.querySelector(`[data-setting="${setting}"]`);
            if (control) control.disabled = !available;
        }
        workspace.querySelectorAll('[data-copy-availability-hint]').forEach(hint => {
            hint.title = available ? '' : 'Requires two or more MOVEit environments configured in MAUI.';
        });
    }

    // Purpose: Updates the Configuration page dirty/restart state after edits.
    function refreshDirtyState() {
        syncCopyAvailability();
        const keepAliveInterval = workspace.querySelector('[data-setting="keepAliveIntervalMinutes"]');
        if (keepAliveInterval) keepAliveInterval.disabled = !checked('keepAliveEnabled');
        if (!baseline || saving) return;
        const dirty = payloadSignature() !== baseline;
        saveBar?.classList.toggle('is-dirty', dirty);
        if (saveButton) saveButton.disabled = !dirty;
        if (discardButton) discardButton.disabled = !dirty;
        if (saveState) saveState.textContent = dirty ? 'Unsaved changes' : 'No unsaved changes';
    }

    // Purpose: Initializes dirty tracking.
    function initializeDirtyTracking() {
        workspace.addEventListener('input', event => {
            if (event.target === searchInput || event.target.closest('[data-config-section="moveit-configuration"]') || event.target.closest('[data-config-section="scheduler"]')) return;
            refreshDirtyState();
        });
        workspace.addEventListener('change', event => {
            if (event.target === searchInput || event.target.closest('[data-config-section="moveit-configuration"]') || event.target.closest('[data-config-section="scheduler"]') || event.target.matches('[data-metadata-resource-select],[data-grid-resource-select]')) return;
            refreshDirtyState();
        });
        discardButton?.addEventListener('click', () => window.location.reload());
        syncCopyAvailability();
        baseline = payloadSignature();
        refreshDirtyState();
    }

    // Purpose: Locks identifiers that become immutable after configuration has been saved.
    function lockSavedIdentifiers(payload) {
        workspace.querySelectorAll('[data-metadata-field="name"]').forEach(input => {
            if (!input.value.trim()) return;
            input.readOnly = true;
            input.classList.add('configuration-locked-input');
            const row = input.closest('[data-metadata-row]');
            if (row) row.dataset.metadataPersisted = 'true';
        });
        workspace.querySelectorAll('[data-security-user]').forEach(row => {
            const input = row.querySelector('[data-security-field="username"]');
            const username = String(input?.value || '').trim();
            if (!username || !input) return;
            input.readOnly = true;
            input.classList.add('configuration-locked-input');
            row.dataset.securityOriginal = 'true';
            row.dataset.securityUsername = username;
        });
        securityOriginalUsernames = payload.securityUsers.map(user => user.username).filter(Boolean);
        workspace.querySelectorAll('[data-server-row]').forEach(row => {
            const key = String(row.querySelector('[data-server-field="key"]')?.value || '').trim();
            const dataAccessRow = row.querySelector('[data-data-access-row]');
            if (!key || !dataAccessRow) return;
            dataAccessRow.dataset.serverKey = key;
            dataAccessRow.dataset.persisted = 'true';
            window.MauiConfigurationOperations?.syncDataAccessRow(
                dataAccessRow,
                String(dataAccessRow.querySelector('[data-data-access-username]')?.textContent || '').trim().replace('—', ''),
                dataAccessRow.dataset.configured === 'true');
        });
    }

    // Purpose: Updates the restart-required banner from the current configuration state.
    function updateRestartBanner(required, reasons) {
        if (!restartBanner) return;
        restartBanner.classList.toggle('visible', Boolean(required));
        if (restartReasons) {
            restartReasons.innerHTML = '';
            (reasons || []).forEach(reason => {
                const item = document.createElement('li');
                item.textContent = reason;
                restartReasons.appendChild(item);
            });
        }
    }

    // Purpose: Updates the database-test result shown in Configuration Settings.
    function setDatabaseTestStatus(message, state = 'idle') {
        if (!databaseTestStatus) return;
        databaseTestStatus.textContent = message || '';
        databaseTestStatus.dataset.state = state;
    }

    // Purpose: Tests the currently entered metadata database configuration.
    async function testDatabaseConfiguration() {
        if (!databaseTestUrl || !databaseTestButton) return;
        const provider = String(value('databaseProvider')).trim();
        const connectionString = String(value('databaseConnectionString')).trim();
        if (!connectionString) {
            setDatabaseTestStatus('Database Connection String is required.', 'error');
            return;
        }

        databaseTestButton.disabled = true;
        setDatabaseTestStatus('Testing…', 'testing');
        try {
            const headers = new Headers({ 'Content-Type': 'application/json' });
            if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
            const response = await fetch(databaseTestUrl, {
                method: 'POST',
                headers,
                body: JSON.stringify({ provider, connectionString }),
                credentials: 'same-origin'
            });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data?.message || `Database test failed (${response.status}).`);
            const safe = data.safeForMauiInitialization !== false;
            setDatabaseTestStatus(
                safe
                    ? (data.message || 'Database connection successful.')
                    : 'Connection successful, but this database is not empty and does not appear to be a MAUI database.',
                safe ? 'success' : 'warning');
        } catch (error) {
            setDatabaseTestStatus(error?.message || 'Database test failed.', 'error');
        } finally {
            databaseTestButton.disabled = false;
        }
    }

    // This immediate operation does not change or save Configuration form values.
    async function createResourceGroupFolders() {
        const button = workspace.querySelector('[data-create-resource-group-folders]');
        const url = workspace.dataset.createResourceGroupFoldersUrl;
        if (!url || !button || button.disabled) return;
        const label = button.textContent;
        button.disabled = true;
        button.textContent = 'Creating…';
        button.setAttribute('aria-busy', 'true');
        try {
            const headers = new Headers({ 'Content-Type': 'application/json' });
            if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
            const response = await fetch(url, { method: 'POST', headers, body: '{}', credentials: 'same-origin' });
            const data = await response.json().catch(() => ({}));
            if (!response.ok || data.success !== true) throw new Error(data.message || 'Task folders could not be created.');
            showToast(data.message || 'Task folders created.');
        } catch (error) {
            showToast(error?.message || 'Task folders could not be created.', 'error');
        } finally {
            button.disabled = false;
            button.textContent = label;
            button.removeAttribute('aria-busy');
        }
    }

    // Update this already-rendered link only after the preference was persisted successfully.
    function syncSavedEditorNavigation(payload) {
        document.documentElement.dataset.useMauiSystemSettingsEditor = payload.useMauiSystemSettingsEditor ? 'true' : 'false';
        document.querySelectorAll('[data-manage-lockouts-native]').forEach(link => { link.hidden = Boolean(payload.useMauiSystemSettingsEditor); });
        document.querySelectorAll('[data-system-settings-open]').forEach(link => {
            link.dataset.nativeEditorRoute = payload.useMauiSystemSettingsEditor ? '' : '#/system/settings';
        });
        document.querySelectorAll('[data-groups-and-permissions-native-route]').forEach(link => {
            link.dataset.nativeEditorRoute = payload.useMauiGroupsAndPermissionsEditor
                ? '' : link.dataset.groupsAndPermissionsNativeRoute;
        });
    }

    // Purpose: Validates and saves the complete Configuration Settings payload.
    async function saveConfiguration() {
        if (saving || !saveUrl) return;
        let payload;
        try {
            payload = collectPayload();
            validatePayload(payload);
        } catch (error) {
            showToast(error?.message || 'Review the configuration values and try again.', 'error');
            return;
        }

        saving = true;
        window.MauiSshAlgorithmSettings?.setBusy(true);
        saveBar?.classList.add('is-saving');
        if (saveButton) saveButton.disabled = true;
        if (discardButton) discardButton.disabled = true;
        if (saveState) saveState.textContent = 'Saving configuration…';

        try {
            const headers = new Headers({ 'Content-Type': 'application/json' });
            if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
            const response = await fetch(saveUrl, {
                method: 'POST',
                headers,
                body: JSON.stringify(payload),
                credentials: 'same-origin'
            });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data?.message || `Configuration save failed (${response.status}).`);

            // The server returns the revision actually persisted, not the stale page revision.
            configurationRevision = String(data.configurationRevision || configurationRevision);
            lockSavedIdentifiers(payload);
            syncSavedEditorNavigation(payload);

            // An endpoint or environment change must not silently redirect existing unattended
            // administrator credentials to another MOVEit server. Reflect server-side revocation.
            const clearedDataAccessKeys = Array.isArray(data.clearedDataAccessServerKeys)
                ? data.clearedDataAccessServerKeys : [];
            if (clearedDataAccessKeys.length) {
                const cleared = new Set(clearedDataAccessKeys.map(key => String(key).toLowerCase()));
                workspace.querySelectorAll('[data-server-row]').forEach(row => {
                    const key = String(row.querySelector('[data-server-field="key"]')?.value || '').trim().toLowerCase();
                    if (!cleared.has(key)) return;
                    const dataAccessRow = row.querySelector('[data-data-access-row]');
                    window.MauiConfigurationOperations?.syncDataAccessRow(dataAccessRow, '', false);
                });
                window.dispatchEvent(new CustomEvent('maui:data-access-credentials-changed'));
            }

            // SMTP passwords are write-only. Once the unified save succeeds, clear the browser value before
            // establishing the new dirty-state baseline so blank correctly means "retain saved password".
            const smtpPassword = workspace.querySelector('[data-report-smtp="password"]');
            if (smtpPassword) {
                smtpPassword.value = '';
                smtpPassword.placeholder = data.smtpPasswordConfigured ? 'Configured — leave blank to keep' : '';
            }

            window.MauiConfigurationOperations?.markSettingsSaved();
            window.MauiSshAlgorithmSettings?.markSaved();
            baseline = payloadSignature();
            updateRestartBanner(Boolean(data.restartRequired), data.restartReasons || []);
            if (payload.smtp) payload.smtp.password = '';
            window.dispatchEvent(new Event('maui:configuration-saved'));
            showToast(clearedDataAccessKeys.length
                ? `Configuration saved. Re-enter Data Access Credentials for ${clearedDataAccessKeys.length} server(s) whose connection identity changed.`
                : (data.message || 'Configuration saved.'));
            if (saveState) saveState.textContent = data.restartRequired ? 'Saved — restart pending' : 'Configuration saved';
        } catch (error) {
            showToast(error?.message || 'The configuration could not be saved.', 'error');
            if (saveState) saveState.textContent = 'Save failed';
        } finally {
            saving = false;
            window.MauiSshAlgorithmSettings?.setBusy(false);
            saveBar?.classList.remove('is-saving');
            window.setTimeout(refreshDirtyState, 0);
        }
    }

    workspace.querySelector('[data-create-resource-group-folders]')?.addEventListener('click', createResourceGroupFolders);
    saveButton?.addEventListener('click', saveConfiguration);
    databaseTestButton?.addEventListener('click', testDatabaseConfiguration);
    workspace.querySelectorAll('[data-setting="databaseProvider"], [data-setting="databaseConnectionString"]').forEach(control => {
        control.addEventListener('input', () => setDatabaseTestStatus('Database values changed — test again before restarting.', 'idle'));
        control.addEventListener('change', () => setDatabaseTestStatus('Database values changed — test again before restarting.', 'idle'));
    });

    window.MauiConfigurationOperations?.initialize({ workspace, antiForgeryToken, showToast, checked, numberValue });
    window.MauiConfigurationMoveIt?.initialize({ workspace, antiForgeryToken, showToast });
    initializeCategoryNavigation();
    initializeServers();
    initializeMetadataEditor();
    initializeSecurityEditor();
    initializeGridDefinitions();
    initializeDirtyTracking();
})();
