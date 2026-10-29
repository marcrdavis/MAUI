/* Optional Tasks-only search enrichment. No storage, checkout or normal grid-load requests. */
(() => {
    'use strict';
    const cacheMilliseconds = 5 * 60 * 1000;
    window.MauiTaskFilenameSearch = {
        create({ grid, search, changed }) {
            const url = grid.dataset.taskFilenameSearchUrl;
            if (!url || !search) return null;
            const toast = grid.querySelector('[data-prototype-toast]');
            let index = new Map(), loadedAt = 0, timer = 0, pending = null, sequence = 0;
            let lastQuery = '', failed = false, disposed = false;
            const query = () => search.value.trim();
            function cancel() {
                clearTimeout(timer); timer = 0; sequence++;
                pending?.abort(); pending = null;
            }
            async function load() {
                timer = 0;
                if (disposed || !query()) return;
                const controller = new AbortController(), current = ++sequence;
                const obsolete = () => disposed || current !== sequence || controller.signal.aborted;
                pending = controller;
                try {
                    const response = await fetch(url, { method: 'GET', credentials: 'same-origin', cache: 'no-store',
                        headers: { Accept: 'application/json' }, signal: controller.signal });
                    // Ignore obsolete reads before session handling or parsing a potentially large index.
                    if (obsolete()) return;
                    if (response.status === 401) window.mauiSessionExpired?.();
                    if (!response.ok) throw new Error('Filename search failed.');
                    const data = await response.json();
                    if (obsolete()) return;
                    if (data?.success !== true || !Array.isArray(data.items)) throw new Error('Incomplete filename search response.');
                    const next = new Map();
                    for (const item of data.items) {
                        if (!item || typeof item.taskId !== 'string' || !item.taskId.trim() || typeof item.text !== 'string' || next.has(item.taskId))
                            throw new Error('Incomplete filename search response.');
                        next.set(item.taskId, item.text.toLowerCase());
                    }
                    index = next; loadedAt = Date.now(); failed = false;
                } catch (error) {
                    if (disposed || current !== sequence || controller.signal.aborted) return;
                    index.clear(); loadedAt = 0; failed = true;
                    // Notify once per failed read. Background grid failures stay on the page, even with a modal open.
                    window.mauiDisplayToast?.(toast, 'Filename search is unavailable. Results include task details only. Change the search or select Refresh Now to retry.', 'error', 5000, 'page');
                } finally {
                    if (!disposed && current === sequence) { pending = null; changed(); }
                }
            }
            function ensure() {
                if (disposed) return;
                const value = query();
                if (!value) { lastQuery = ''; failed = false; cancel(); return; }
                if (lastQuery !== value) { lastQuery = value; failed = false; clearTimeout(timer); timer = 0; }
                if (pending) return;
                if (loadedAt && Date.now() - loadedAt < cacheMilliseconds) return;
                if (failed) return;
                if (!timer) timer = setTimeout(load, 300);
            }
            function invalidate() {
                cancel(); index.clear(); loadedAt = 0; failed = false; ensure(); changed();
            }
            grid.addEventListener('maui:task-filename-search-invalidated', invalidate);
            // Cache belongs only to this open page/connection/login and is never persisted to browser storage.
            window.addEventListener('pagehide', () => { disposed = true; cancel(); index.clear(); });
            // A restored page needs a fresh index and must not retain a previous failed-read latch.
            window.addEventListener('pageshow', event => { if (event.persisted) { disposed = false; invalidate(); } });
            return { ensure, text: id => index.get(String(id)) || '' };
        }
    };
})();
