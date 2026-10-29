/* Author: Marc R. Davis (marcdavis@comcast.net)
 * Shared grid-only MOVEit connection feedback. Server transport evidence, not HTTP status
 * guesses or browser connectivity, determines whether MOVEit is unreachable.
 */
(() => {
    'use strict';
    const nativeFetch = window.fetch.bind(window);
    const banner = document.querySelector('[data-moveit-unreachable-banner]');
    const scope = document.documentElement?.dataset.moveitGridScope || '';
    const storageKey = `maui:moveit-grid-connection:${scope}`;
    let timeouts = 0;
    let unreachable = false;
    try {
        const saved = scope ? JSON.parse(window.sessionStorage.getItem(storageKey) || 'null') : null;
        timeouts = Math.min(2, Math.max(0, Number(saved?.timeouts) || 0));
        unreachable = saved?.unreachable === true;
    } catch (_) { /* Storage can be unavailable; the current page still handles failures. */ }

    function render() {
        if (banner && banner.hidden === unreachable) {
            banner.hidden = !unreachable;
            // Existing grids size their viewport from the shell offset on resize.
            window.dispatchEvent(new Event('resize'));
        }
        try {
            if (scope && (timeouts || unreachable)) window.sessionStorage.setItem(storageKey, JSON.stringify({ timeouts, unreachable }));
            else if (scope) window.sessionStorage.removeItem(storageKey);
        } catch (_) { /* Connection feedback must never depend on browser storage. */ }
    }

    window.MauiMoveItGridConnection = {
        // Page JSON helpers opt in here. Only grid responses carry this header; editor calls,
        // unrelated HTTP errors, and failures between the browser and MAUI pass through unchanged.
        async fetch(url, options) {
            const response = await nativeFetch(url, options);
            if (!scope) return response;
            const outcome = response.headers.get('X-Maui-MoveIt-Connection');
            if (!outcome) return response;
            if (response.status === 401 || response.status === 403) { timeouts = 0; render(); return response; }
            if (outcome === 'unreachable') {
                timeouts = 0;
                unreachable = true;
            } else if (outcome === 'timeout') {
                timeouts = Math.min(2, timeouts + 1);
                if (timeouts >= 2) unreachable = true;
            } else if (outcome === 'live' && response.ok) {
                timeouts = 0;
                unreachable = false;
            } else if (outcome === 'responded' || outcome === 'live') {
                // A real HTTP response breaks a timeout streak, but only a successful live
                // grid read clears an existing warning. Cached responses have outcome 'none'.
                timeouts = 0;
            }
            render();
            if (!response.ok && (outcome === 'timeout' || outcome === 'unreachable')) {
                const error = new Error(outcome === 'timeout'
                    ? 'MOVEit did not respond before the request timed out.'
                    : 'MAUI cannot connect to MOVEit.');
                error.moveItGridHandled = unreachable;
                throw error;
            }
            return response;
        },
        isHandled: error => error?.moveItGridHandled === true
    };
    render();
})();
