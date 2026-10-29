(() => {
    'use strict';

    // Shared lifecycle for resource grids that use MAUI's cached initial-load + live refresh contract.
    // Resource-specific endpoints, payload rendering, menus, and status messages remain owned by each page.
    window.mauiCreateResourceRefreshController = function createResourceRefreshController(options = {}) {
        const grid = options.grid || null;
        const refreshControl = options.refreshControl || null;
        const lastRefreshElement = options.lastRefreshElement || null;
        const refreshClock = options.refreshClock || null;
        const refreshToggle = options.refreshToggle || null;
        const refreshNow = options.refreshNow || null;
        const fetchJson = options.fetchJson;
        const intervalSeconds = Math.max(0, Number(options.intervalSeconds || 0));
        const persistLastRefresh = options.persistLastRefresh === true;
        const setAriaBusy = options.setAriaBusy === true;
        const rowMenuDelayMs = Math.max(250, Number(options.rowMenuDelayMs || 750));
        const minimumDelayMs = Math.max(250, Number(options.minimumDelayMs || 250));

        if (typeof fetchJson !== 'function') {
            throw new Error('mauiCreateResourceRefreshController requires fetchJson.');
        }

        let paused = false;
        let inFlight = false;
        let timer = 0;
        let disposed = false;
        let initialized = false;

        function formatLastRefresh(value) {
            if (!value) return '--:--:--';
            const date = new Date(value);
            if (Number.isNaN(date.valueOf())) return '--:--:--';
            return date.toLocaleTimeString([], {
                hour: '2-digit',
                minute: '2-digit',
                second: '2-digit',
                hour12: true
            });
        }

        function setLastRefresh(value) {
            if (lastRefreshElement) {
                lastRefreshElement.textContent = `Last Refresh: ${formatLastRefresh(value)}`;
            }
            if (persistLastRefresh && refreshControl && value) {
                refreshControl.dataset.lastRefreshUtc = value;
            }
        }

        function clearTimer() {
            if (!timer) return;
            window.clearTimeout(timer);
            timer = 0;
        }

        function setBusy(busy) {
            inFlight = Boolean(busy);
            refreshClock?.classList.toggle('is-refreshing', inFlight);
            if (setAriaBusy) {
                refreshClock?.setAttribute('aria-busy', inFlight ? 'true' : 'false');
            }
            if (refreshNow) refreshNow.disabled = inFlight;
            options.onBusyChange?.(inFlight);
        }

        function updatePauseUi() {
            if (!refreshToggle) return;
            if (intervalSeconds <= 0) {
                refreshToggle.textContent = 'Automatic Refresh Disabled';
                refreshToggle.disabled = true;
                refreshClock?.classList.add('is-paused');
                if (options.disabledTitle) refreshClock?.setAttribute('title', options.disabledTitle);
                return;
            }

            refreshToggle.disabled = false;
            refreshToggle.textContent = paused ? 'Resume Refresh' : 'Pause Refresh';
            refreshClock?.classList.toggle('is-paused', paused);
            const title = paused ? options.pausedTitle : options.activeTitle;
            if (title) refreshClock?.setAttribute('title', title);
        }

        function applyPayload(payload) {
            if (typeof options.applyPayload === 'function') {
                options.applyPayload(payload, { setLastRefresh });
            } else {
                grid?.dispatchEvent(new CustomEvent('maui:grid-replace-rows', {
                    detail: { rows: Array.isArray(payload?.rows) ? payload.rows : [] }
                }));
                setLastRefresh(payload?.refreshedUtc || new Date().toISOString());
            }
            options.onPayload?.(payload);
        }

        function schedule(delayMs = intervalSeconds * 1000) {
            clearTimer();
            if (disposed || intervalSeconds <= 0 || paused || document.hidden) return;
            timer = window.setTimeout(() => {
                // Host diagnostics can hold native resources longer than a normal grid read.
                // Defer automatic reads without changing the user's Pause/Resume preference.
                if (options.deferAutomaticRefresh?.()) { schedule(1000); return; }
                if (window.mauiIsRowActionMenuOpen?.()) {
                    schedule(rowMenuDelayMs);
                    return;
                }
                void refresh(false);
            }, Math.max(minimumDelayMs, Number(delayMs) || 0));
        }

        async function loadInitial() {
            if (disposed || inFlight || !grid) return;
            initialized = true;
            setBusy(true);
            clearTimer();
            let refreshRecommended = false;
            try {
                const url = options.buildInitialUrl?.();
                if (!url) throw new Error('Initial resource-grid URL is not configured.');
                const payload = await fetchJson(url.toString(), { method: 'GET', cache: 'no-store' });
                applyPayload(payload);
                refreshRecommended = payload?.refreshRecommended === true;
            } catch (error) {
                if (window.MauiMoveItGridConnection?.isHandled(error)) options.onConnectionFailure?.(error, { initial: true });
                else if (typeof options.onInitialError === 'function') options.onInitialError(error);
                else console.warn('Initial resource grid load failed.');
            } finally {
                grid.dispatchEvent(new CustomEvent(options.initialReadyEvent || 'maui:grid-initial-data-ready'));
                setBusy(false);
                if (refreshRecommended) window.setTimeout(() => void refresh(false), 0);
                else schedule();
            }
        }

        async function refresh(forceRefresh = false) {
            if (disposed || inFlight || !grid) return;
            options.beforeRefresh?.(Boolean(forceRefresh));
            setBusy(true);
            clearTimer();
            try {
                const url = options.buildRefreshUrl?.(Boolean(forceRefresh));
                if (!url) throw new Error('Resource-grid refresh URL is not configured.');
                const payload = await fetchJson(url.toString(), { method: 'GET', cache: 'no-store' });
                applyPayload(payload);
            } catch (error) {
                if (window.MauiMoveItGridConnection?.isHandled(error)) options.onConnectionFailure?.(error, { initial: false });
                else if (typeof options.onRefreshError === 'function') options.onRefreshError(error);
                else console.warn('Resource grid refresh failed.');
            } finally {
                setBusy(false);
                schedule();
            }
        }

        function refreshWhenIdle(forceRefresh = false, settings = {}) {
            const maxAttempts = Math.max(0, Number(settings.maxAttempts ?? 20));
            const delayMs = Math.max(50, Number(settings.delayMs ?? 250));
            let attempt = 0;

            const tryRefresh = () => {
                if (disposed) return;
                if (!inFlight) {
                    void refresh(forceRefresh);
                    return;
                }
                if (attempt >= maxAttempts) return;
                attempt += 1;
                window.setTimeout(tryRefresh, delayMs);
            };

            tryRefresh();
        }

        function togglePause() {
            if (intervalSeconds <= 0) return;
            paused = !paused;
            updatePauseUi();
            refreshToggle?.closest('[data-menu]')?.classList.remove('open');
            if (paused) clearTimer();
            else void refresh(false);
        }

        function refreshNowHandler() {
            refreshNow?.closest('[data-menu]')?.classList.remove('open');
            void refresh(true);
        }

        function visibilityHandler() {
            if (!initialized) return;
            if (document.hidden) clearTimer();
            else if (!paused && intervalSeconds > 0) void refresh(false);
        }

        refreshToggle?.addEventListener('click', togglePause);
        refreshNow?.addEventListener('click', refreshNowHandler);
        document.addEventListener('visibilitychange', visibilityHandler);
        setLastRefresh(refreshControl?.dataset.lastRefreshUtc || '');
        updatePauseUi();

        return {
            loadInitial,
            refresh,
            refreshWhenIdle,
            schedule,
            clearTimer,
            setLastRefresh,
            get isInFlight() { return inFlight; },
            get isPaused() { return paused; },
            destroy() {
                disposed = true;
                clearTimer();
                refreshToggle?.removeEventListener('click', togglePause);
                refreshNow?.removeEventListener('click', refreshNowHandler);
                document.removeEventListener('visibilitychange', visibilityHandler);
            }
        };
    };

    // Shared Loading/content/error state for MAUI Information dialogs.
    // Resource-specific fields, tabs, metadata, and save behavior remain local to each resource.
    window.mauiCreateInformationDialogState = function createInformationDialogState(options = {}) {
        const loadingElement = options.loadingElement || null;
        const contentElement = options.contentElement || null;
        const errorElement = options.errorElement || null;
        const defaultErrorMessage = String(options.defaultErrorMessage || 'Unable to load information.');

        function hideError() {
            if (!errorElement) return;
            errorElement.textContent = '';
            errorElement.hidden = true;
        }

        function setLoading(loading) {
            if (loadingElement) loadingElement.hidden = !loading;
            if (contentElement) contentElement.hidden = Boolean(loading);
        }

        function reset() {
            hideError();
            setLoading(true);
        }

        function showContent() {
            setLoading(false);
            hideError();
        }

        function showError(message) {
            if (loadingElement) loadingElement.hidden = true;
            if (!errorElement) return;
            errorElement.textContent = message || defaultErrorMessage;
            errorElement.hidden = false;
        }

        return { reset, setLoading, showContent, showError, hideError };
    };
})();
