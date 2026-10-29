/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * This JavaScript is part of the MAUI application.
 */

(() => {
    const root = document.documentElement;
    const shell = document.querySelector('[data-shell]');
    const sessionExpiredUrl = String(root.dataset.sessionExpiredUrl || window.mauiAppUrl('/Account/SessionExpired')).trim() || window.mauiAppUrl('/Account/SessionExpired');
    const systemMessageBanner = document.querySelector('[data-system-message-banner]');
    const systemMessageDismissUrl = String(root.dataset.systemMessageDismissUrl || '').trim();
    const taskFailureAlertBanner = document.querySelector('[data-task-failure-alert]');
    const taskFailureAlertTitle = taskFailureAlertBanner?.querySelector('[data-task-failure-alert-title]');
    const taskFailureAlertList = taskFailureAlertBanner?.querySelector('[data-task-failure-alert-list]');
    const taskFailureAlertPollUrl = String(root.dataset.taskFailureAlertPollUrl || '').trim();
    const taskFailureAlertItems = new Map();
    let taskFailureAlertDismissTimer = 0;
    let taskFailureAlertPollInFlight = false;
    let taskFailureAlertAudioContext = null;
    let taskFailureAlertAudioUnlockInitialized = false;
    let systemMessageDismissed = systemMessageBanner?.dataset.dismissed === 'true';
    let sessionExpiredRedirectStarted = false;

    // Ends the stale browser session through the dedicated server cleanup endpoint and replaces
    // the current history entry so Back cannot return to a known-expired application page.
    function redirectForSessionExpiration() {
        if (sessionExpiredRedirectStarted) return;
        sessionExpiredRedirectStarted = true;
        root.dataset.sessionRedirectPending = 'true';
        document.querySelectorAll('.toast.visible').forEach(toast => toast.classList.remove('visible'));
        window.location.replace(sessionExpiredUrl);
    }
    window.mauiSessionExpired = redirectForSessionExpiration;

    // Applies the selected theme to the current page. Persistence is handled by the
    // server-side User Preferences service so the choice follows the signed-in user.
    function setTheme(theme) {
        const value = theme === 'dark' ? 'dark' : 'light';
        root.dataset.theme = value;
        document.querySelectorAll('[data-theme-choice]').forEach(button => {
            button.classList.toggle('selected', button.dataset.themeChoice === value);
        });
        return value;
    }

    // Purpose: Returns the current MAUI antiforgery token.
    function getAntiForgeryToken() {
        return document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    }

    // Purpose: Posts JSON with antiforgery protection and normalized error handling.
    async function postJson(url, payload) {
        if (!url) throw new Error('The MAUI preference endpoint is not available.');
        const headers = new Headers({ 'Content-Type': 'application/json' });
        const token = getAntiForgeryToken();
        if (token) headers.set('RequestVerificationToken', token);
        const response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers,
            body: JSON.stringify(payload)
        });
        if (response.status === 401) {
            redirectForSessionExpiration();
            throw new Error('Your session has expired.');
        }
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
        return data;
    }

    // All toast feedback is logged once. An open dialog always receives the message;
    // page-origin notices are labelled and kept separate from editor save errors.
    function displayToast(toast, message, kind = 'default', duration = 5000, origin = 'modal') {
        if (sessionExpiredRedirectStarted) { window.mauiLogNotification?.(message, kind, origin); return; }
        if (window.MauiModalUI?.showMessage(message, kind, undefined, origin)) {
            if (toast) { window.clearTimeout(toast._mauiToastTimer); toast.classList.remove('visible'); }
            return;
        }
        window.mauiLogNotification?.(message, kind, origin);
        if (!toast) return;
        const isError = kind === 'error';
        const isWarning = kind === 'warning';
        toast.textContent = message || '';
        toast.classList.toggle('error', isError);
        toast.classList.toggle('warning', isWarning);
        toast.setAttribute('role', isError ? 'alert' : 'status');
        toast.setAttribute('aria-live', isError ? 'assertive' : 'polite');
        toast.classList.add('visible');
        window.clearTimeout(toast._mauiToastTimer);
        toast._mauiToastTimer = window.setTimeout(() => toast.classList.remove('visible'), duration);
    }
    window.mauiDisplayToast = displayToast;

    // Purpose: Displays global toast. Callers identify error paths explicitly instead of classifying message text.
    function showGlobalToast(message, kind = 'default', origin = 'modal') {
        let toast = document.querySelector('[data-user-preferences-toast]');
        if (!toast) {
            toast = document.createElement('div');
            toast.className = 'toast';
            toast.dataset.userPreferencesToast = '';
            document.body.appendChild(toast);
        }
        displayToast(toast, message, kind, 5000, origin);
    }
    window.mauiShowToast = showGlobalToast;

    // Toggles between the two built-in themes and persists the choice for the current MAUI user.
    async function toggleTheme() {
        const previous = root.dataset.theme === 'dark' ? 'dark' : 'light';
        const next = previous === 'dark' ? 'light' : 'dark';
        setTheme(next);
        try {
            const data = await postJson(root.dataset.preferencesThemeUrl, { value: next });
            const saved = setTheme(data?.theme || next);
            root.dataset.defaultTheme = saved;
        } catch (error) {
            setTheme(previous);
            showGlobalToast(error?.message || 'The theme preference could not be saved.', 'error');
        }
    }

    // Restores the saved collapsed-sidebar preference for the desktop application shell.
    function restoreSidebarState() {
        if (!shell) return;
        const collapsed = localStorage.getItem('automation-ui-sidebar-collapsed') === 'true';
        shell.classList.toggle('sidebar-collapsed', collapsed);
    }

    // Toggles and persists the desktop sidebar width preference.
    function toggleSidebar() {
        if (!shell) return;
        const collapsed = !shell.classList.contains('sidebar-collapsed');
        shell.classList.toggle('sidebar-collapsed', collapsed);
        localStorage.setItem('automation-ui-sidebar-collapsed', String(collapsed));
    }

    // Collapses expanded navigation groups before a route change so the sidebar responds immediately
    // even when the destination view needs additional server-side work before it renders. When navigation
    // stays inside the currently expanded group (for example Tasks -> Running), preserve that group to avoid
    // a close/reopen flash while the destination page is loading.
    function collapseNavGroupsForNavigation(preserveGroup = null) {
        let changed = false;
        document.querySelectorAll('[data-nav-group].expanded').forEach(group => {
            if (group === preserveGroup) return;
            group.classList.remove('expanded');
            changed = true;
        });
        return changed;
    }

    // If another navigation group was collapsed, give the browser a paint opportunity before starting the
    // request. Navigation within the active group does not need that delay and begins immediately.
    function navigateFromSidebar(url, preserveGroup = null) {
        if (!url) return;

        const navigationChanged = collapseNavGroupsForNavigation(preserveGroup);
        if (!navigationChanged) {
            window.location.assign(url);
            return;
        }

        document.querySelector('[data-sidebar]')?.getBoundingClientRect();
        window.requestAnimationFrame(() => {
            window.requestAnimationFrame(() => window.location.assign(url));
        });
    }

    // Handles normal left-navigation links while preserving modifier-click/new-tab browser behavior.
    // Resource links that declare a native MOVEit route open that route directly in the native-editor popup
    // instead of navigating MAUI to a placeholder/grid page.
    function activateNavLink(event) {
        if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const link = event.currentTarget;
        if (!link?.href || link.target === '_blank') return;

        const nativeRoute = String(link.dataset.nativeEditorRoute || '').trim();
        if (nativeRoute) {
            event.preventDefault();

            const nativeEditor = window.MauiNativeEditor;
            if (!nativeEditor) {
                window.location.assign(link.href);
                return;
            }

            const windowTitle = String(link.dataset.nativeEditorTitle || 'MAUI Native Editor');
            const popup = nativeEditor.reserve(windowTitle);
            if (!popup) return;

            const antiForgeryToken = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
            void nativeEditor.open({
                popup,
                sessionUrl: String(link.dataset.nativeEditorSessionUrl || ''),
                antiForgeryToken,
                returnHash: nativeRoute,
                windowTitle,
            });
            return;
        }

        event.preventDefault();
        const group = link.closest('[data-nav-group]');
        navigateFromSidebar(link.href, group);
    }

    // Handles every expandable navigation parent consistently. Clicking a parent toggles its own
    // submenu and immediately collapses any other expanded top-level group. Parent clicks never
    // start a page request; the child links own navigation. When the sidebar itself is collapsed,
    // the first child remains the usable destination.
    // Purpose: Toggle one sidebar group while keeping the navigation accordion mutually exclusive.
    function activateNavParent(button) {
        const group = button.closest('[data-nav-group]');
        if (!group) return;

        if (shell?.classList.contains('sidebar-collapsed')) {
            const firstChild = group.querySelector('.nav-children a[href]');
            if (firstChild?.href) {
                if (firstChild.target === '_blank') firstChild.click();
                else navigateFromSidebar(firstChild.href);
                return;
            }
        }

        const wasExpanded = group.classList.contains('expanded');
        collapseNavGroupsForNavigation(group);
        group.classList.toggle('expanded', !wasExpanded);
    }

    // Purpose: Initializes user preferences.
    function initializeUserPreferences() {
        const menu = document.querySelector('[data-user-menu]');
        const menuButton = menu?.querySelector('[data-user-menu-button]');
        const menuPanel = menu?.querySelector('[data-user-menu-panel]');
        const openButton = menu?.querySelector('[data-user-preferences-open]');
        const dialog = document.querySelector('[data-user-preferences-dialog]');
        if (!dialog) return;

        const themeSelect = dialog.querySelector('[data-user-preferences-theme]');
        const startPageSelect = dialog.querySelector('[data-user-preferences-start-page]');
        const pageSizeSelect = dialog.querySelector('[data-user-preferences-grid-page-size]');
        const saveButton = dialog.querySelector('[data-user-preferences-save]');
        const closeButtons = Array.from(dialog.querySelectorAll('[data-user-preferences-close]'));
        const message = dialog.querySelector('[data-user-preferences-message]');
        let busy = false;

        // Purpose: Sets menu open.
        function setMenuOpen(open) {
            if (!menuPanel || !menuButton) return;
            if (open) {
                const serverMenu = document.querySelector('[data-server-menu]');
                const serverPanel = serverMenu?.querySelector('[data-server-menu-panel]');
                const serverButton = serverMenu?.querySelector('[data-server-menu-button]');
                if (serverPanel) serverPanel.hidden = true;
                if (serverButton) serverButton.setAttribute('aria-expanded', 'false');
                serverMenu?.classList.remove('open');
            }
            menuPanel.hidden = !open;
            menuButton.setAttribute('aria-expanded', String(open));
            menu.classList.toggle('open', open);
        }

        // Purpose: Updates controls while the current operation is running.
        function setBusy(value) {
            busy = value;
            [themeSelect, startPageSelect, pageSizeSelect, saveButton, ...closeButtons].forEach(control => {
                if (control) control.disabled = value;
            });
            dialog.setAttribute('aria-busy', String(value));
        }

        // Purpose: Sets the Recycle Bin status/error message.
        function setMessage(text, isError = false) {
            if (!message) return;
            message.hidden = !text;
            message.textContent = text || '';
            message.classList.toggle('error', Boolean(text && isError));
        }

        // Purpose: Populates page sizes.
        function populatePageSizes(values, selected) {
            if (!pageSizeSelect) return;
            pageSizeSelect.replaceChildren();
            (Array.isArray(values) ? values : []).forEach(value => {
                const option = document.createElement('option');
                option.value = String(value);
                option.textContent = String(value);
                option.selected = Number(value) === Number(selected);
                pageSizeSelect.appendChild(option);
            });
        }

        // Purpose: Loads current user preferences and opens the preferences dialog.
        async function loadAndOpen() {
            if (busy) return;
            setMenuOpen(false);
            setMessage('');
            setBusy(true);
            try {
                const response = await fetch(root.dataset.preferencesUrl, { credentials: 'same-origin' });
                if (response.status === 401) {
                    redirectForSessionExpiration();
                    return;
                }
                const data = await response.json().catch(() => ({}));
                if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
                const preferences = data?.preferences || {};
                if (themeSelect) themeSelect.value = preferences.theme || root.dataset.theme || 'light';
                if (startPageSelect) startPageSelect.value = preferences.startPage || 'Dashboard';
                populatePageSizes(data?.choices?.gridPageSizes, preferences.gridPageSize || root.dataset.userGridPageSize);
                dialog.showModal();
            } catch (error) {
                showGlobalToast(error?.message || 'The user preferences could not be loaded.', 'error');
            } finally {
                setBusy(false);
            }
        }

        // Purpose: Validates and saves the current user preferences.
        async function savePreferences() {
            if (busy) return;
            setMessage('');
            setBusy(true);
            try {
                const payload = {
                    theme: themeSelect?.value || 'light',
                    startPage: startPageSelect?.value || 'Dashboard',
                    gridPageSize: Number(pageSizeSelect?.value || 50)
                };
                const data = await postJson(root.dataset.preferencesSaveUrl, payload);
                const preferences = data?.preferences || payload;
                const theme = setTheme(preferences.theme || payload.theme);
                root.dataset.defaultTheme = theme;
                root.dataset.userGridPageSize = String(preferences.gridPageSize || payload.gridPageSize);
                window.dispatchEvent(new CustomEvent('maui:user-grid-page-size-changed', {
                    detail: { value: Number(root.dataset.userGridPageSize) }
                }));
                dialog.close();
                showGlobalToast('Preferences saved.');
            } catch (error) {
                setMessage(error?.message || 'The user preferences could not be saved.', true);
            } finally {
                setBusy(false);
            }
        }

        menuButton?.addEventListener('click', event => {
            event.stopPropagation();
            setMenuOpen(menuPanel?.hidden ?? true);
        });
        openButton?.addEventListener('click', loadAndOpen);
        document.addEventListener('click', event => {
            if (menu && !menu.contains(event.target)) setMenuOpen(false);
        });
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape') setMenuOpen(false);
        });
        closeButtons.forEach(button => button.addEventListener('click', () => {
            if (!busy) dialog.close();
        }));
        saveButton?.addEventListener('click', savePreferences);

        window.mauiSaveGridPageSizePreference = async value => {
            const numericValue = Number(value);
            const data = await postJson(root.dataset.preferencesGridPageSizeUrl, { value: numericValue });
            const saved = Number(data?.gridPageSize || numericValue);
            root.dataset.userGridPageSize = String(saved);
            return saved;
        };

        // Persists one grid's column visibility, order, and widths in MAUI user preferences.
        window.mauiSaveGridLayoutPreference = async (gridKey, layout) => {
            const data = await postJson(root.dataset.preferencesGridLayoutUrl, { gridKey, layout });
            return data?.layout || layout;
        };

        // Removes one persisted grid layout so configuration-defined defaults apply on future loads.
        window.mauiResetGridLayoutPreference = async gridKey => {
            await postJson(root.dataset.preferencesGridLayoutResetUrl, { gridKey });
        };
    }

    // Keeps the global footer Server display synchronized with MOVEit's authoritative task-scheduler state.
    // MAUI Admins additionally receive the Start/Stop control; status monitoring remains available to all users.
    function initializeServerControl() {
        if (!shell) return;

        const menu = document.querySelector('[data-server-menu]');
        const menuButton = menu?.querySelector('[data-server-menu-button]');
        const menuPanel = menu?.querySelector('[data-server-menu-panel]');
        const actionButton = menu?.querySelector('[data-scheduler-action]');
        const statusTarget = menuButton || menu?.querySelector('[data-server-name]');
        const stoppedIndicator = document.querySelector('[data-scheduler-stopped-indicator]');
        const stopDialog = document.querySelector('[data-scheduler-stop-dialog]');
        const stopConfirmButton = stopDialog?.querySelector('[data-scheduler-stop-confirm]');
        const stopCancelButtons = Array.from(stopDialog?.querySelectorAll('[data-scheduler-stop-cancel]') || []);
        const stopError = stopDialog?.querySelector('[data-scheduler-stop-error]');
        const tamperResetButton = menu?.querySelector('[data-tamper-reset-open]');
        const tamperResetDialog = document.querySelector('[data-tamper-reset-dialog]');
        const tamperResetConfirmButton = tamperResetDialog?.querySelector('[data-tamper-reset-yes]');
        const tamperResetCancelButtons = Array.from(tamperResetDialog?.querySelectorAll('[data-tamper-reset-no]') || []);
        const tamperResetError = tamperResetDialog?.querySelector('[data-tamper-reset-error]');
        const failoverStatusButton = menu?.querySelector('[data-failover-status-open]');
        const failoverStatusDialog = document.querySelector('[data-failover-status-dialog]');
        const failoverStatusCloseButtons = Array.from(failoverStatusDialog?.querySelectorAll('[data-failover-status-close]') || []);
        const failoverStatusError = failoverStatusDialog?.querySelector('[data-failover-status-error]');
        const failoverStatusLoading = failoverStatusDialog?.querySelector('[data-failover-status-loading]');
        const failoverNodeGrid = failoverStatusDialog?.querySelector('[data-failover-node-grid]');
        const failoverNodeTemplate = failoverStatusDialog?.querySelector('[data-failover-node-template]');
        const statusUrl = String(root.dataset.schedulerStatusUrl || '').trim();
        const startUrl = String(root.dataset.schedulerStartUrl || '').trim();
        const stopUrl = String(root.dataset.schedulerStopUrl || '').trim();
        const tamperResetUrl = String(root.dataset.tamperResetUrl || '').trim();
        const failoverStatusUrl = String(root.dataset.failoverStatusUrl || '').trim();
        if (!menu || !statusTarget || !statusUrl) return;

        const hasServerMenuControl = Boolean(menuButton && menuPanel);
        const hasSchedulerControl = Boolean(hasServerMenuControl && actionButton && startUrl && stopUrl);
        const hasTamperResetControl = Boolean(hasServerMenuControl && tamperResetButton && tamperResetDialog && tamperResetConfirmButton && tamperResetUrl);
        const hasFailoverStatusControl = Boolean(hasServerMenuControl && failoverStatusButton && failoverStatusDialog && failoverStatusUrl);
        let schedulerState = null;
        let commandBusy = false;
        let statusBusy = false;

        // Opens/closes the admin-only Server popup independently from the User menu.
        function setMenuOpen(open) {
            if (!hasServerMenuControl || !menuButton || !menuPanel) return;
            if (open) {
                const userMenu = document.querySelector('[data-user-menu]');
                const userPanel = userMenu?.querySelector('[data-user-menu-panel]');
                const userButton = userMenu?.querySelector('[data-user-menu-button]');
                if (userPanel) userPanel.hidden = true;
                if (userButton) userButton.setAttribute('aria-expanded', 'false');
                userMenu?.classList.remove('open');
            }
            menuPanel.hidden = !open;
            menuButton.setAttribute('aria-expanded', String(open));
            menu.classList.toggle('open', open);
        }

        // Applies one authoritative/known scheduler state to the global footer and warning indicator.
        function applySchedulerState(data) {
            const isEnabled = data?.isEnabled === true;
            const isDisabled = data?.isDisabled === true;
            const displayStatus = String(data?.displayStatus || (isEnabled ? 'Running' : isDisabled ? 'Not running' : 'Status unknown'));
            schedulerState = { isEnabled, isDisabled, displayStatus, status: String(data?.status || '') };

            menu.classList.toggle('scheduler-stopped', isDisabled);
            statusTarget.title = `MOVEit Scheduler: ${displayStatus}`;
            if (stoppedIndicator) {
                stoppedIndicator.hidden = !isDisabled;
                stoppedIndicator.title = isDisabled ? `MOVEit Scheduler: ${displayStatus}` : '';
            }

            if (actionButton) {
                actionButton.disabled = commandBusy || (!isEnabled && !isDisabled);
                actionButton.dataset.schedulerCommand = isEnabled ? 'stop' : isDisabled ? 'start' : '';
                actionButton.textContent = isEnabled ? 'Stop Scheduler' : isDisabled ? 'Start Scheduler' : 'Scheduler Status Unknown';
            }
        }

        // Leaves the last known visual state intact when a transient status check fails, but prevents a stale admin action.
        function markStatusUnavailable() {
            if (actionButton) {
                actionButton.disabled = true;
                actionButton.dataset.schedulerCommand = '';
                actionButton.textContent = 'Scheduler Status Unavailable';
            }
            statusTarget.title = schedulerState
                ? `MOVEit Scheduler: ${schedulerState.displayStatus} (last known; current status unavailable)`
                : 'MOVEit Scheduler status unavailable';
        }

        async function refreshStatus(showError = false) {
            if (statusBusy || commandBusy) return;
            statusBusy = true;
            try {
                const response = await fetch(statusUrl, { credentials: 'same-origin', cache: 'no-store' });
                if (response.status === 401) {
                    redirectForSessionExpiration();
                    return;
                }
                const data = await response.json().catch(() => ({}));
                if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
                applySchedulerState(data);
            } catch (error) {
                markStatusUnavailable();
                if (showError) showGlobalToast(error?.message || 'The scheduler status could not be retrieved.', 'error', 'page');
            } finally {
                statusBusy = false;
            }
        }

        function setStopDialogBusy(value) {
            commandBusy = value;
            stopConfirmButton && (stopConfirmButton.disabled = value);
            stopCancelButtons.forEach(button => { button.disabled = value; });
            stopDialog?.setAttribute('aria-busy', String(value));
            if (actionButton) {
                if (value) {
                    actionButton.disabled = true;
                } else if (schedulerState) {
                    applySchedulerState(schedulerState);
                }
            }
        }

        function setStopError(message) {
            if (!stopError) return;
            stopError.hidden = !message;
            stopError.textContent = message || '';
        }

        async function startScheduler() {
            if (!hasSchedulerControl || !actionButton || commandBusy) return;
            commandBusy = true;
            setMenuOpen(false);
            actionButton.disabled = true;
            actionButton.textContent = 'Starting Scheduler...';
            try {
                await postJson(startUrl, {});
                applySchedulerState({ status: 'SCHEDULER_ENABLED', isEnabled: true, isDisabled: false, displayStatus: 'Running' });
                showGlobalToast('Scheduler started.');
            } catch (error) {
                showGlobalToast(error?.message || 'The scheduler could not be started.', 'error');
            } finally {
                commandBusy = false;
                if (schedulerState) applySchedulerState(schedulerState);
                void refreshStatus(false);
            }
        }

        async function stopScheduler() {
            if (!hasSchedulerControl || commandBusy || !stopDialog) return;
            setStopError('');
            setStopDialogBusy(true);
            try {
                await postJson(stopUrl, {});
                applySchedulerState({ status: 'SCHEDULER_DISABLED_BY_OPERATOR', isEnabled: false, isDisabled: true, displayStatus: 'Stopped by operator' });
                stopDialog.close();
                showGlobalToast('Scheduler stopped.');
            } catch (error) {
                setStopError(error?.message || 'The scheduler could not be stopped.');
            } finally {
                setStopDialogBusy(false);
                void refreshStatus(false);
            }
        }

        function setTamperResetBusy(value) {
            commandBusy = value;
            tamperResetConfirmButton && (tamperResetConfirmButton.disabled = value);
            tamperResetCancelButtons.forEach(button => { button.disabled = value; });
            tamperResetDialog?.setAttribute('aria-busy', String(value));
            if (actionButton) {
                if (value) actionButton.disabled = true;
                else if (schedulerState) applySchedulerState(schedulerState);
            }
            if (tamperResetButton) tamperResetButton.disabled = value;
        }

        function setTamperResetError(message) {
            if (!tamperResetError) return;
            tamperResetError.hidden = !message;
            tamperResetError.textContent = message || '';
        }

        async function resetTamperDetection() {
            if (!hasTamperResetControl || commandBusy || !tamperResetDialog) return;
            setTamperResetError('');
            setTamperResetBusy(true);
            try {
                await postJson(tamperResetUrl, {});
                tamperResetDialog.close();
                showGlobalToast('Tamper detection reset.');
            } catch (error) {
                setTamperResetError(error?.message || 'Tamper detection could not be reset.');
            } finally {
                setTamperResetBusy(false);
            }
        }

        function setFailoverStatusError(message) {
            if (!failoverStatusError) return;
            failoverStatusError.hidden = !message;
            failoverStatusError.textContent = message || '';
        }

        function setFailoverStatusLoading(value) {
            if (failoverStatusLoading) failoverStatusLoading.hidden = !value;
            failoverStatusDialog?.setAttribute('aria-busy', String(value));
        }

        function renderFailoverStatus(data) {
            if (!failoverNodeGrid || !failoverNodeTemplate) return;
            failoverNodeGrid.replaceChildren();

            const nodes = Array.isArray(data?.nodes) ? data.nodes : [];
            if (nodes.length === 0) {
                failoverNodeGrid.hidden = true;
                setFailoverStatusError('MOVEit reports failover is enabled, but no failover node definitions were returned.');
                return;
            }

            nodes.forEach(node => {
                const fragment = failoverNodeTemplate.content.cloneNode(true);
                const card = fragment.querySelector('.failover-node-card');
                const iconWrap = fragment.querySelector('[data-failover-node-icon-wrap]');
                const healthMark = fragment.querySelector('[data-failover-node-health-mark]');
                const hostname = fragment.querySelector('[data-failover-node-hostname]');
                const roleBadge = fragment.querySelector('[data-failover-node-role]');
                const status = fragment.querySelector('[data-failover-node-status]');
                const stamp = fragment.querySelector('[data-failover-node-stamp]');

                const nodeHasError = node?.nodeHasError === true;
                const role = String(node?.role || '').trim();
                const normalizedRole = role.toLowerCase();
                const roleLabel = normalizedRole === 'primary' ? 'Primary' : normalizedRole === 'secondary' ? 'Secondary' : (role || 'Unknown role');

                if (hostname) hostname.textContent = String(node?.hostname || `Node ${node?.nodeNum || ''}`).trim() || 'Unnamed node';
                if (roleBadge) {
                    roleBadge.textContent = roleLabel;
                    roleBadge.classList.toggle('is-primary', normalizedRole === 'primary');
                }
                if (status) status.textContent = String(node?.status || '').trim() || 'No status text returned.';
                if (stamp) {
                    const statusStamp = String(node?.statusStamp || '').trim();
                    stamp.textContent = statusStamp ? `Last updated: ${statusStamp}` : 'Status timestamp unavailable';
                }
                if (iconWrap) {
                    iconWrap.classList.toggle('is-error', nodeHasError);
                    iconWrap.classList.toggle('is-healthy', !nodeHasError);
                    iconWrap.setAttribute('role', 'img');
                    iconWrap.setAttribute('aria-label', nodeHasError ? 'Node has an error' : 'Node healthy');
                    iconWrap.title = nodeHasError ? 'Node has an error' : 'Node healthy';
                }
                if (healthMark) healthMark.textContent = nodeHasError ? '×' : '✓';
                card?.classList.toggle('has-error', nodeHasError);

                failoverNodeGrid.appendChild(fragment);
            });

            failoverNodeGrid.hidden = false;
        }

        async function requestFailoverStatus() {
            const response = await fetch(failoverStatusUrl, { credentials: 'same-origin', cache: 'no-store' });
            if (response.status === 401) {
                redirectForSessionExpiration();
                return null;
            }
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
            return data;
        }

        // The native endpoint itself is the capability check. A failure leaves the option hidden rather than
        // advertising a failover screen MAUI cannot verify for the current session/server.
        async function refreshFailoverAvailability() {
            if (!hasFailoverStatusControl || !failoverStatusButton) return;
            try {
                const data = await requestFailoverStatus();
                failoverStatusButton.hidden = data?.resilEnabled !== true;
            } catch {
                failoverStatusButton.hidden = true;
            }
        }

        async function openFailoverStatus() {
            if (!hasFailoverStatusControl || !failoverStatusDialog || !failoverStatusButton) return;
            setMenuOpen(false);
            setFailoverStatusError('');
            if (failoverNodeGrid) {
                failoverNodeGrid.replaceChildren();
                failoverNodeGrid.hidden = true;
            }
            setFailoverStatusLoading(true);
            failoverStatusDialog.showModal();

            try {
                const data = await requestFailoverStatus();
                if (!data) return;
                if (data.resilEnabled !== true) {
                    failoverStatusButton.hidden = true;
                    setFailoverStatusError('Failover is not enabled on this MOVEit Automation environment.');
                    return;
                }
                renderFailoverStatus(data);
            } catch (error) {
                setFailoverStatusError(error?.message || 'MOVEit failover status could not be retrieved.');
            } finally {
                setFailoverStatusLoading(false);
            }
        }

        if (hasServerMenuControl && menuButton && menuPanel) {
            menuButton.addEventListener('click', event => {
                event.stopPropagation();
                setMenuOpen(menuPanel.hidden);
            });

            actionButton?.addEventListener('click', () => {
                if (!hasSchedulerControl || actionButton.disabled || commandBusy) return;
                const command = actionButton.dataset.schedulerCommand;
                if (command === 'start') {
                    void startScheduler();
                    return;
                }
                if (command === 'stop' && stopDialog) {
                    setMenuOpen(false);
                    setStopError('');
                    stopDialog.showModal();
                }
            });

            stopConfirmButton?.addEventListener('click', () => void stopScheduler());
            stopCancelButtons.forEach(button => button.addEventListener('click', () => {
                if (!commandBusy) stopDialog?.close();
            }));
            stopDialog?.addEventListener('cancel', event => {
                if (commandBusy) event.preventDefault();
            });

            tamperResetButton?.addEventListener('click', () => {
                if (!hasTamperResetControl || tamperResetButton.disabled || commandBusy || !tamperResetDialog) return;
                setMenuOpen(false);
                setTamperResetError('');
                tamperResetDialog.showModal();
            });
            tamperResetConfirmButton?.addEventListener('click', () => void resetTamperDetection());
            tamperResetCancelButtons.forEach(button => button.addEventListener('click', () => {
                if (!commandBusy) tamperResetDialog?.close();
            }));
            tamperResetDialog?.addEventListener('cancel', event => {
                if (commandBusy) event.preventDefault();
            });

            failoverStatusButton?.addEventListener('click', () => void openFailoverStatus());
            failoverStatusCloseButtons.forEach(button => button.addEventListener('click', () => failoverStatusDialog?.close()));

            document.addEventListener('click', event => {
                if (!menu.contains(event.target)) setMenuOpen(false);
            });
            document.addEventListener('keydown', event => {
                if (event.key === 'Escape') setMenuOpen(false);
            });
        }

        // Scheduler state is system-wide and may be changed outside MAUI. Refresh while the page is visible
        // without treating this background request as browser/user activity for idle-session purposes.
        window.setInterval(() => {
            if (document.visibilityState === 'visible') void refreshStatus(false);
        }, 30000);
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') void refreshStatus(false);
        });

        void refreshStatus(false);
        void refreshFailoverAvailability();
    }

    // Purpose: Restores the shared System Message when it is configured and has not been dismissed in this login session.
    function restoreSystemMessage() {
        if (!systemMessageBanner || systemMessageDismissed || taskFailureAlertItems.size > 0) return;
        systemMessageBanner.hidden = false;
    }

    // Purpose: Temporarily hides the shared System Message while a higher-priority shell banner is active.
    function hideSystemMessage() {
        if (systemMessageBanner) systemMessageBanner.hidden = true;
    }

    // Purpose: Persists System Message dismissal in the current server-side MAUI session.
    function initializeSystemMessage() {
        if (!systemMessageBanner) return;
        systemMessageBanner.querySelector('[data-system-message-dismiss]')?.addEventListener('click', async () => {
            if (systemMessageDismissed) return;
            const button = systemMessageBanner.querySelector('[data-system-message-dismiss]');
            if (button) button.disabled = true;
            const headers = new Headers();
            const token = getAntiForgeryToken();
            if (token) headers.set('RequestVerificationToken', token);
            try {
                const response = await fetch(systemMessageDismissUrl, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers
                });
                if (response.status === 401 || response.status === 403) {
                    redirectForSessionExpiration();
                    return;
                }
                if (!response.ok) throw new Error('Unable to dismiss the System Message.');
                systemMessageDismissed = true;
                systemMessageBanner.dataset.dismissed = 'true';
                systemMessageBanner.hidden = true;
            } catch (error) {
                if (button) button.disabled = false;
                window.mauiShowToast?.(error?.message || 'Unable to dismiss the System Message.', 'error');
            }
        });
    }

    function isSessionExpirationWarningVisible() {
        const warning = document.querySelector('[data-session-expiration-warning]');
        return Boolean(warning && !warning.hidden);
    }

    // Restores whichever shared shell message currently has priority: a transient Task failure alert first,
    // otherwise the administrator-configured System Message. The idle-session warning always takes precedence.
    function restoreShellMessage() {
        if (isSessionExpirationWarningVisible()) return;
        if (taskFailureAlertBanner && taskFailureAlertItems.size > 0) {
            hideSystemMessage();
            const wasHidden = taskFailureAlertBanner.hidden;
            taskFailureAlertBanner.hidden = false;
            if (wasHidden) {
                void playTaskFailureAlertSound();
                scheduleTaskFailureAlertDismiss();
            }
            return;
        }
        restoreSystemMessage();
    }

    function hideTaskFailureAlertForPriority() {
        if (!taskFailureAlertBanner || taskFailureAlertBanner.hidden) return;
        taskFailureAlertBanner.hidden = true;
        window.clearTimeout(taskFailureAlertDismissTimer);
        taskFailureAlertDismissTimer = 0;
    }

    // Clears the one transient failure banner without changing the configured System Message dismissal state.
    function dismissTaskFailureAlert(restoreMessage = true) {
        window.clearTimeout(taskFailureAlertDismissTimer);
        taskFailureAlertDismissTimer = 0;
        taskFailureAlertItems.clear();
        if (taskFailureAlertBanner) taskFailureAlertBanner.hidden = true;
        if (taskFailureAlertList) taskFailureAlertList.replaceChildren();
        if (restoreMessage && !isSessionExpirationWarningVisible()) restoreSystemMessage();
    }

    function renderTaskFailureAlert() {
        if (!taskFailureAlertBanner || !taskFailureAlertList || taskFailureAlertItems.size === 0) return;
        const failures = [...taskFailureAlertItems.values()];
        if (taskFailureAlertTitle) taskFailureAlertTitle.textContent = failures.length === 1 ? 'Task Failure' : `${failures.length} Task Failures`;
        taskFailureAlertList.replaceChildren();
        failures.forEach(item => {
            const row = document.createElement('li');
            const name = String(item?.taskName || item?.taskId || 'Task').trim() || 'Task';
            const message = String(item?.message || '').trim();
            row.textContent = message ? `${name} — ${message}` : `${name} failed.`;
            taskFailureAlertList.appendChild(row);
        });
    }

    function getTaskFailureAudioContext() {
        if (taskFailureAlertAudioContext) return taskFailureAlertAudioContext;
        const AudioContextType = window.AudioContext || window.webkitAudioContext;
        if (!AudioContextType) return null;
        try {
            taskFailureAlertAudioContext = new AudioContextType();
            return taskFailureAlertAudioContext;
        } catch {
            return null;
        }
    }

    // Plays one brief two-tone alert for a newly detected failure batch. Browsers may block Web Audio
    // until the user has interacted with the page; the visual alert remains authoritative in that case.
    async function playTaskFailureAlertSound() {
        const context = getTaskFailureAudioContext();
        if (!context) return;
        try {
            if (context.state === 'suspended') await context.resume();
            const now = context.currentTime;
            const gain = context.createGain();
            gain.gain.setValueAtTime(0.0001, now);
            gain.gain.exponentialRampToValueAtTime(0.16, now + 0.015);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.48);
            gain.connect(context.destination);

            const first = context.createOscillator();
            first.type = 'sine';
            first.frequency.setValueAtTime(740, now);
            first.connect(gain);
            first.start(now);
            first.stop(now + 0.18);

            const second = context.createOscillator();
            second.type = 'sine';
            second.frequency.setValueAtTime(940, now + 0.22);
            second.connect(gain);
            second.start(now + 0.22);
            second.stop(now + 0.46);
        } catch {
            // Audio availability is browser-controlled; never make a visual failure alert depend on sound playback.
        }
    }

    function scheduleTaskFailureAlertDismiss() {
        window.clearTimeout(taskFailureAlertDismissTimer);
        taskFailureAlertDismissTimer = window.setTimeout(() => dismissTaskFailureAlert(true), 30000);
    }

    function showTaskFailureAlert(failures) {
        if (!taskFailureAlertBanner || !Array.isArray(failures) || failures.length === 0) return;
        let added = false;
        failures.forEach(item => {
            const runId = String(item?.runId || '').trim();
            if (!runId || taskFailureAlertItems.has(runId)) return;
            taskFailureAlertItems.set(runId, item);
            added = true;
        });
        if (!added) return;

        renderTaskFailureAlert();
        hideSystemMessage();
        if (!isSessionExpirationWarningVisible()) {
            taskFailureAlertBanner.hidden = false;
            void playTaskFailureAlertSound();
            // New failures arriving while the banner is already active are merged into the same alert and
            // restart the 30-second visibility window instead of creating another stacked failure message.
            scheduleTaskFailureAlertDismiss();
        }
    }

    function initializeTaskFailureAlertAudioUnlock() {
        if (taskFailureAlertAudioUnlockInitialized) return;
        taskFailureAlertAudioUnlockInitialized = true;
        const unlockAudio = () => {
            const context = getTaskFailureAudioContext();
            if (context?.state === 'suspended') void context.resume().catch(() => {});
        };
        document.addEventListener('pointerdown', unlockAudio, { once: true, passive: true });
        document.addEventListener('keydown', unlockAudio, { once: true });
    }

    async function pollTaskFailureAlerts() {
        if (!taskFailureAlertPollUrl || taskFailureAlertPollInFlight || sessionExpiredRedirectStarted || document.visibilityState !== 'visible') return;
        taskFailureAlertPollInFlight = true;
        try {
            const response = await fetch(taskFailureAlertPollUrl, {
                method: 'GET',
                credentials: 'same-origin',
                cache: 'no-store',
                headers: { 'Accept': 'application/json' }
            });
            if (response.status === 401 || response.status === 403) {
                redirectForSessionExpiration();
                return;
            }
            if (!response.ok) return;
            const data = await response.json().catch(() => null);
            if (!data) return;
            if (data.enabled === false) {
                if (taskFailureAlertItems.size > 0) dismissTaskFailureAlert(true);
                return;
            }
            initializeTaskFailureAlertAudioUnlock();
            showTaskFailureAlert(Array.isArray(data.failures) ? data.failures : []);
        } catch {
            // Failure-alert polling is advisory. A transient network/MOVEit error must not disrupt the page.
        } finally {
            taskFailureAlertPollInFlight = false;
        }
    }

    function initializeTaskFailureAlerts() {
        if (!taskFailureAlertBanner || !taskFailureAlertPollUrl) return;
        taskFailureAlertBanner.querySelector('[data-task-failure-alert-dismiss]')?.addEventListener('click', () => dismissTaskFailureAlert(true));

        window.setInterval(() => void pollTaskFailureAlerts(), 15000);
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') void pollTaskFailureAlerts();
        });
        void pollTaskFailureAlerts();
    }

    // Enforces the configured idle timeout from deliberate browser activity and surfaces the final
    // two minutes as a visible countdown without creating a second session/timeout mechanism.
    function initializeIdleSessionTimeout() {
        if (!shell) return;

        const timeoutMinutes = Number(root.dataset.idleSessionTimeoutMinutes || 0);
        const activityUrl = String(root.dataset.sessionActivityUrl || '').trim();
        if (!Number.isFinite(timeoutMinutes) || timeoutMinutes <= 0 || !activityUrl) return;

        const timeoutMs = timeoutMinutes * 60 * 1000;
        const warningLeadMs = Math.min(2 * 60 * 1000, Math.max(0, timeoutMs - 1000));
        const heartbeatIntervalMs = Math.min(60 * 1000, Math.max(15 * 1000, Math.floor(timeoutMs / 4)));
        const warningBanner = document.querySelector('[data-session-expiration-warning]');
        const warningCountdown = warningBanner?.querySelector('[data-session-expiration-countdown]');
        const staySignedInButton = warningBanner?.querySelector('[data-session-expiration-stay]');
        let lastUserActivity = Date.now();
        let lastHeartbeat = 0;
        let idleTimer = 0;
        let warningCountdownTimer = 0;
        let logoutStarted = false;

        function remainingIdleMs() {
            return Math.max(0, timeoutMs - (Date.now() - lastUserActivity));
        }

        function formatCountdown(milliseconds) {
            const totalSeconds = Math.max(0, Math.ceil(milliseconds / 1000));
            const minutes = Math.floor(totalSeconds / 60);
            const seconds = totalSeconds % 60;
            return `${minutes}:${String(seconds).padStart(2, '0')}`;
        }

        function hideExpirationWarning(restoreMessage = true) {
            window.clearInterval(warningCountdownTimer);
            warningCountdownTimer = 0;
            if (warningBanner) warningBanner.hidden = true;
            if (restoreMessage) restoreShellMessage();
        }

        function updateExpirationWarning() {
            if (logoutStarted) return;
            const remaining = remainingIdleMs();
            if (remaining <= 0) {
                signOutForIdleTimeout();
                return;
            }
            if (warningCountdown) warningCountdown.textContent = formatCountdown(remaining);
        }

        function showExpirationWarning() {
            if (!warningBanner || logoutStarted) return;
            hideSystemMessage();
            hideTaskFailureAlertForPriority();
            warningBanner.hidden = false;
            updateExpirationWarning();
            if (!warningCountdownTimer) {
                warningCountdownTimer = window.setInterval(updateExpirationWarning, 1000);
            }
        }

        // Uses the anonymous session-expired cleanup route because the authentication cookie may already
        // have expired by the time the idle timer fires. The endpoint still revokes the MOVEit token when
        // a live server-side session remains available.
        function signOutForIdleTimeout() {
            if (logoutStarted) return;
            logoutStarted = true;
            window.clearTimeout(idleTimer);
            hideExpirationWarning(false);
            redirectForSessionExpiration();
        }

        // Returns true after starting sign-out when the configured idle window has elapsed.
        function signOutIfExpired() {
            if (remainingIdleMs() > 0) return false;
            signOutForIdleTimeout();
            return true;
        }

        // Records the authoritative server-side idle timestamp only after deliberate user activity.
        // Background grid/status refresh requests never call this endpoint.
        async function sendActivityHeartbeat(force = false) {
            if (logoutStarted || signOutIfExpired()) return;
            const now = Date.now();
            if (!force && (now - lastHeartbeat) < heartbeatIntervalMs) return;
            lastHeartbeat = now;

            const headers = new Headers();
            const token = getAntiForgeryToken();
            if (token) headers.set('RequestVerificationToken', token);

            try {
                const response = await fetch(activityUrl, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers,
                    keepalive: true
                });
                if (response.status === 401 || response.status === 403) signOutForIdleTimeout();
            } catch {
                // A temporary heartbeat failure is non-fatal; the next activity will retry.
            }
        }

        // Uses one timer for both phases of the same idle lifecycle: enter the two-minute warning window,
        // then expire at the configured timeout. The countdown interval is display-only.
        function scheduleIdleCheck() {
            window.clearTimeout(idleTimer);
            if (logoutStarted) return;

            const remaining = remainingIdleMs();
            if (remaining <= 0) {
                signOutForIdleTimeout();
                return;
            }

            if (warningLeadMs > 0 && remaining <= warningLeadMs) {
                showExpirationWarning();
                idleTimer = window.setTimeout(() => {
                    if (!signOutIfExpired()) scheduleIdleCheck();
                }, remaining + 100);
                return;
            }

            hideExpirationWarning();
            const untilWarning = warningLeadMs > 0 ? remaining - warningLeadMs : remaining;
            idleTimer = window.setTimeout(scheduleIdleCheck, Math.max(100, untilWarning + 100));
        }

        // Records deliberate interaction with MAUI and periodically renews the authenticated server session.
        // Passive/ambient events such as pointer movement, raw scroll notifications, tab visibility changes,
        // and keep-awake function keys must not extend the idle session.
        function recordUserActivity(event) {
            if (event && event.isTrusted === false) return;
            const activityTarget = event?.target instanceof Element ? event.target : null;
            if (activityTarget?.closest('[data-session-expiration-stay]')) return;
            if (logoutStarted || signOutIfExpired()) return;
            lastUserActivity = Date.now();
            hideExpirationWarning();
            scheduleIdleCheck();
            void sendActivityHeartbeat();
        }

        const ignoredActivityKeys = new Set([
            'Shift', 'Control', 'Alt', 'Meta',
            'CapsLock', 'NumLock', 'ScrollLock',
            'Pause', 'PrintScreen', 'ContextMenu',
            'AudioVolumeUp', 'AudioVolumeDown', 'AudioVolumeMute',
            'MediaTrackNext', 'MediaTrackPrevious', 'MediaStop', 'MediaPlayPause'
        ]);

        function recordKeyboardActivity(event) {
            if (!event || event.isTrusted === false) return;

            const key = String(event.key || '').trim();
            if (/^F(?:[1-9]|1\d|2[0-4])$/i.test(key)) return;
            if (ignoredActivityKeys.has(key)) return;

            recordUserActivity(event);
        }

        // Pointer/touch presses and wheel movement represent deliberate interaction with the application.
        // Keyboard input is also supported for accessibility, but function/system keys are intentionally ignored
        // so utilities that periodically emit keys such as F15 cannot keep the MAUI session alive.
        document.addEventListener('pointerdown', recordUserActivity, { passive: true });
        document.addEventListener('touchstart', recordUserActivity, { passive: true });
        document.addEventListener('wheel', recordUserActivity, { passive: true });
        document.addEventListener('keydown', recordKeyboardActivity);

        // The explicit button uses the same activity timestamp and heartbeat as any other deliberate interaction,
        // but forces the server heartbeat immediately so the acknowledgement is not delayed by throttling.
        staySignedInButton?.addEventListener('click', event => {
            if (event.isTrusted === false || logoutStarted) return;
            lastUserActivity = Date.now();
            hideExpirationWarning();
            scheduleIdleCheck();
            void sendActivityHeartbeat(true);
        });

        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState !== 'visible') return;
            if (!signOutIfExpired()) scheduleIdleCheck();
        });

        window.addEventListener('pageshow', () => {
            if (!signOutIfExpired()) scheduleIdleCheck();
        });

        scheduleIdleCheck();
    }

    let mauiTagCache = null;
    let mauiTagLoadPromise = null;

    function parseAssignedTags(value) {
        return String(value || '').split(';').map(item => item.trim()).filter(Boolean);
    }

    function tagTextColor(color) {
        const hex = String(color || '').replace('#', '');
        if (!/^[0-9a-f]{6}$/i.test(hex)) return '#ffffff';
        const r = parseInt(hex.slice(0, 2), 16);
        const g = parseInt(hex.slice(2, 4), 16);
        const b = parseInt(hex.slice(4, 6), 16);
        return ((r * 299 + g * 587 + b * 114) / 1000) >= 155 ? '#111111' : '#ffffff';
    }

    function buildTagPill(name, color, removable = false, onRemove = null) {
        const pill = document.createElement('span');
        pill.className = 'metadata-tag-pill';
        pill.style.backgroundColor = color || '#808080';
        pill.style.color = tagTextColor(color);
        const text = document.createElement('span');
        text.textContent = name;
        pill.appendChild(text);
        if (removable) {
            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'metadata-tag-remove';
            remove.textContent = '×';
            remove.setAttribute('aria-label', `Remove ${name}`);
            remove.addEventListener('click', event => {
                event.preventDefault();
                event.stopPropagation();
                onRemove?.();
            });
            pill.appendChild(remove);
        }
        return pill;
    }

    async function loadMauiTags(force = false) {
        if (force) {
            mauiTagCache = null;
            mauiTagLoadPromise = null;
        }
        if (mauiTagCache) return mauiTagCache;
        if (mauiTagLoadPromise) return mauiTagLoadPromise;
        const url = String(root.dataset.tagsListUrl || '').trim();
        if (!url) return [];
        mauiTagLoadPromise = fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
            .then(async response => {
                if (response.status === 401) {
                    redirectForSessionExpiration();
                    return [];
                }
                const data = await response.json().catch(() => ({}));
                if (!response.ok) throw new Error(data?.message || `Unable to load Tags (${response.status}).`);
                mauiTagCache = Array.isArray(data?.tags) ? data.tags : [];
                return mauiTagCache;
            })
            .catch(error => {
                mauiTagLoadPromise = null;
                throw error;
            });
        return mauiTagLoadPromise;
    }
    window.mauiGetTags = loadMauiTags;
    window.mauiInvalidateTags = () => { mauiTagCache = null; mauiTagLoadPromise = null; };

    function closeMetadataTagPicker(picker) {
        if (!(picker instanceof HTMLElement)) return;
        picker.hidden = true;
        picker._mauiTagButton?.setAttribute('aria-expanded', 'false');
    }

    // One shared set of handlers closes floating Tag pickers without registering listeners
    // each time an Information modal rebuilds its metadata controls. The picker is fixed
    // to the viewport so it is not clipped by the modal's scrolling body.
    document.addEventListener('pointerdown', event => {
        document.querySelectorAll('.metadata-tags-picker:not([hidden])').forEach(picker => {
            const editor = picker.closest('.metadata-tags-editor');
            if (editor && !editor.contains(event.target)) closeMetadataTagPicker(picker);
        });
    });
    document.addEventListener('scroll', event => {
        document.querySelectorAll('.metadata-tags-picker:not([hidden])').forEach(picker => {
            if (event.target !== picker) closeMetadataTagPicker(picker);
        });
    }, true);
    window.addEventListener('resize', () => {
        document.querySelectorAll('.metadata-tags-picker:not([hidden])').forEach(closeMetadataTagPicker);
    });

    window.mauiRenderTagPills = async (container, value) => {
        if (!(container instanceof Element)) return;
        const names = parseAssignedTags(value);
        container.replaceChildren();
        if (names.length === 0) return;
        let tags = [];
        try { tags = await loadMauiTags(); } catch { /* Gray fallback pills remain useful. */ }
        const byName = new Map(tags.map(tag => [String(tag.name || '').toLowerCase(), tag]));
        names.forEach(name => {
            const tag = byName.get(name.toLowerCase());
            container.appendChild(buildTagPill(tag?.name || name, tag?.color || '#808080'));
        });
    };

    function attachTagEditor(control, field) {
        if (!(control instanceof HTMLInputElement)) return;
        control.type = 'hidden';
        control.dataset.mauiMetadataTags = 'true';
        control.dataset.mauiMetadataLabel = String(field?.label || field?.name || 'Tags').trim() || 'Tags';

        const editor = document.createElement('div');
        editor.className = 'metadata-tags-editor';
        const pills = document.createElement('div');
        pills.className = 'metadata-tags-selected';
        const actions = document.createElement('div');
        actions.className = 'metadata-tags-actions';
        const addButton = document.createElement('button');
        addButton.type = 'button';
        addButton.className = 'secondary-button metadata-tags-add';
        addButton.textContent = '+ Add Tag';
        addButton.setAttribute('aria-haspopup', 'listbox');
        addButton.setAttribute('aria-expanded', 'false');
        const picker = document.createElement('div');
        picker.className = 'metadata-tags-picker';
        picker.hidden = true;
        picker.setAttribute('role', 'listbox');
        picker.setAttribute('aria-label', 'Available Tags');
        picker._mauiTagButton = addButton;
        actions.append(addButton, picker);
        editor.append(pills, actions);

        let registry = [];
        const selected = () => parseAssignedTags(control.value);
        const setSelected = values => {
            control.value = values.join('; ');
            control.dispatchEvent(new Event('input', { bubbles: true }));
            control.dispatchEvent(new Event('change', { bubbles: true }));
            render();
        };
        const render = () => {
            const values = selected();
            const byName = new Map(registry.map(tag => [String(tag.name || '').toLowerCase(), tag]));
            pills.replaceChildren();
            values.forEach(name => {
                const tag = byName.get(name.toLowerCase());
                pills.appendChild(buildTagPill(tag?.name || name, tag?.color || '#808080', true, () => {
                    setSelected(values.filter(value => value.toLowerCase() !== name.toLowerCase()));
                }));
            });
            // The filter above is intentionally simple and case-insensitive.
            picker.replaceChildren();
            const selectedKeys = new Set(values.map(value => value.toLowerCase()));
            const available = registry.filter(tag => !selectedKeys.has(String(tag.name || '').toLowerCase()));
            if (available.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'metadata-tags-picker-empty';
                empty.textContent = registry.length === 0
                    ? 'No Tags have been created for this MOVEit environment.'
                    : 'All available Tags are assigned.';
                picker.appendChild(empty);
            } else {
                available.forEach(tag => {
                    const option = document.createElement('button');
                    option.type = 'button';
                    option.className = 'metadata-tags-picker-option';
                    option.setAttribute('role', 'option');
                    option.appendChild(buildTagPill(tag.name, tag.color));
                    option.addEventListener('click', () => {
                        setSelected([...values, tag.name]);
                        closeMetadataTagPicker(picker);
                        addButton.focus();
                    });
                    picker.appendChild(option);
                });
            }
        };

        const positionPicker = () => {
            const rect = addButton.getBoundingClientRect();
            const viewportPadding = 12;
            const preferredWidth = Math.min(320, Math.max(240, rect.width + 120));
            const width = Math.min(preferredWidth, Math.max(220, window.innerWidth - (viewportPadding * 2)));
            picker.style.width = `${width}px`;
            picker.style.left = `${Math.min(Math.max(viewportPadding, rect.left), Math.max(viewportPadding, window.innerWidth - width - viewportPadding))}px`;
            picker.style.top = `${rect.bottom + 6}px`;
            picker.style.visibility = 'hidden';
            picker.hidden = false;

            const pickerHeight = Math.min(picker.scrollHeight, Math.max(0, window.innerHeight - (viewportPadding * 2)));
            const roomBelow = window.innerHeight - rect.bottom - viewportPadding;
            const roomAbove = rect.top - viewportPadding;
            if (roomBelow < Math.min(pickerHeight, 180) && roomAbove > roomBelow) {
                picker.style.top = `${Math.max(viewportPadding, rect.top - picker.offsetHeight - 6)}px`;
            }
            picker.style.visibility = '';
            addButton.setAttribute('aria-expanded', 'true');
        };

        addButton.addEventListener('click', async event => {
            event.preventDefault();
            if (!picker.hidden) {
                closeMetadataTagPicker(picker);
                return;
            }
            try {
                registry = await loadMauiTags();
                render();
                positionPicker();
            } catch (error) {
                closeMetadataTagPicker(picker);
                showGlobalToast(error?.message || 'Tags could not be loaded.', 'error');
            }
        });
        render();
        void loadMauiTags().then(tags => { registry = tags; render(); }).catch(() => {});
        queueMicrotask(() => {
            if (control.parentElement && !editor.isConnected) control.insertAdjacentElement('afterend', editor);
        });
    }

    // Configures common client-side behavior for one MAUI metadata editor control.
    window.mauiConfigureMetadataControl = (control, field) => {
        if (!(control instanceof HTMLInputElement || control instanceof HTMLTextAreaElement || control instanceof HTMLSelectElement)) return control;
        const type = String(field?.type || 'String').trim().toLowerCase();
        if (type === 'tags') {
            attachTagEditor(control, field);
            return control;
        }
        if (type !== 'email') return control;

        control.dataset.mauiMetadataEmail = 'true';
        control.dataset.mauiMetadataLabel = String(field?.label || field?.name || 'Email').trim() || 'Email';
        if (control instanceof HTMLInputElement) {
            control.type = 'text';
            control.inputMode = 'email';
            control.autocomplete = 'off';
            control.spellcheck = false;
        }
        control.addEventListener('input', () => control.setCustomValidity(''));
        return control;
    };

    // Validates all Email metadata controls in a container and normalizes accepted lists to semicolon-separated addresses.
    window.mauiValidateMetadataControls = container => {
        if (!(container instanceof Element)) return true;
        const tagControls = [...container.querySelectorAll('[data-maui-metadata-tags="true"]')];
        for (const control of tagControls) {
            if (control.required && !String(control.value || '').trim()) {
                showGlobalToast(`${control.dataset.mauiMetadataLabel || 'Tags'} is required.`, 'error');
                control.nextElementSibling?.querySelector('button')?.focus();
                return false;
            }
        }

        const emailControls = [...container.querySelectorAll('[data-maui-metadata-email="true"]')];
        const validator = document.createElement('input');
        validator.type = 'email';

        for (const control of emailControls) {
            control.setCustomValidity('');
            const raw = String(control.value || '').trim();
            const label = control.dataset.mauiMetadataLabel || 'Email';
            if (!raw) {
                if (control.required) {
                    control.setCustomValidity(`${label} is required.`);
                    control.reportValidity();
                    control.focus();
                    return false;
                }
                continue;
            }

            const addresses = raw.split(/[;,]/).map(value => value.trim()).filter(Boolean);
            const invalid = addresses.length === 0 || addresses.some(address => {
                validator.value = address;
                return !validator.checkValidity();
            });
            if (invalid) {
                control.setCustomValidity(`${label} must contain valid email address(es) separated by commas or semicolons.`);
                control.reportValidity();
                control.focus();
                return false;
            }

            control.value = addresses.join('; ');
        }
        return true;
    };

    // Project-wide MAUI confirmation surface. Browser-native confirm/alert controls are intentionally not used.
    const mauiConfirmDialog = document.querySelector('[data-maui-confirm-dialog]');
    const mauiConfirmTitle = mauiConfirmDialog?.querySelector('[data-maui-confirm-title]');
    const mauiConfirmMessage = mauiConfirmDialog?.querySelector('[data-maui-confirm-message]');
    const mauiConfirmAccept = mauiConfirmDialog?.querySelector('[data-maui-confirm-accept]');
    const mauiConfirmCancelButtons = [...(mauiConfirmDialog?.querySelectorAll('[data-maui-confirm-cancel]') || [])];
    let mauiConfirmResolve = null;

    function finishMauiConfirm(result) {
        const resolve = mauiConfirmResolve;
        mauiConfirmResolve = null;
        if (mauiConfirmDialog?.open) mauiConfirmDialog.close();
        resolve?.(Boolean(result));
    }

    window.mauiConfirm = (options = {}) => new Promise(resolve => {
        if (!mauiConfirmDialog || !mauiConfirmAccept) { resolve(false); return; }
        if (mauiConfirmResolve) { resolve(false); return; }
        mauiConfirmResolve = resolve;
        if (mauiConfirmTitle) mauiConfirmTitle.textContent = String(options.title || 'Confirm Action');
        if (mauiConfirmMessage) mauiConfirmMessage.textContent = String(options.message || 'Are you sure you want to continue?');
        mauiConfirmAccept.textContent = String(options.confirmText || 'Continue');
        mauiConfirmAccept.classList.toggle('danger-button', options.danger === true);
        mauiConfirmAccept.classList.toggle('primary-button', options.danger !== true);
        const cancelText = String(options.cancelText || 'Cancel');
        mauiConfirmCancelButtons.forEach(button => {
            if (!button.classList.contains('dialog-close')) button.textContent = cancelText;
        });
        mauiConfirmDialog.showModal();
        window.setTimeout(() => mauiConfirmAccept.focus(), 0);
    });

    mauiConfirmAccept?.addEventListener('click', () => finishMauiConfirm(true));
    mauiConfirmCancelButtons.forEach(button => button.addEventListener('click', () => finishMauiConfirm(false)));
    mauiConfirmDialog?.addEventListener('cancel', event => { event.preventDefault(); finishMauiConfirm(false); });
    mauiConfirmDialog?.addEventListener('close', () => {
        if (!mauiConfirmResolve) return;
        const resolve = mauiConfirmResolve;
        mauiConfirmResolve = null;
        resolve(false);
    });

    // Returns true while any page-owned row ellipsis/action menu is open. Auto-refreshing
    // grids use this to avoid replacing row DOM while the user is choosing an action.
    // Manual refresh remains unaffected.
    window.mauiIsRowActionMenuOpen = () => Boolean(document.querySelector('.task-row-action-menu:not([hidden])'));

    initializeUserPreferences();
    initializeServerControl();
    initializeSystemMessage();
    initializeTaskFailureAlerts();
    initializeIdleSessionTimeout();

    document.querySelector('[data-sidebar-toggle]')?.addEventListener('click', toggleSidebar);
    document.querySelector('[data-theme-quick-toggle]')?.addEventListener('click', toggleTheme);
    document.querySelectorAll('[data-nav-parent]').forEach(button => button.addEventListener('click', () => activateNavParent(button)));
    document.querySelectorAll('.nav-tree a[href]').forEach(link => link.addEventListener('click', activateNavLink));
    document.querySelectorAll('[data-theme-choice]').forEach(button => button.addEventListener('click', () => setTheme(button.dataset.themeChoice)));

    restoreSidebarState();
    setTheme(root.dataset.theme || root.dataset.defaultTheme || 'light');
})();
