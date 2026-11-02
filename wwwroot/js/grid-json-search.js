/* On-demand, read-only Tasks/Hosts JSON search. Definitions never enter browser storage. */
(() => {
    'use strict';
    const cacheMilliseconds = 5 * 60 * 1000, maximumCacheBytes = 16 * 1024 * 1024;
    window.MauiGridJsonSearch = {
        create({ grid, search, getIds, changed }) {
            const endpoint = grid.dataset.jsonSearchUrl, model = window.MauiJsonSearch;
            if (!endpoint || !search || !model) return null;
            const root = grid.dataset.grid === 'hosts' ? 'Host' : 'Task';
            const summary = grid.querySelector('[data-pager-summary]'), toast = grid.querySelector('[data-prototype-toast]');
            const cache = new Map(); let cacheBytes = 0, timer = 0, generation = 0, controller = null;
            let disposed = false, signature = '', expiresAt = 0, state = '', message = '', completed = 0, total = 0;
            let matches = new Set();
            const active = () => /^json:/i.test(search.value);
            function cancel() {
                clearTimeout(timer); timer = 0; generation++;
                controller?.abort(); controller = null;
            }
            function clearCache() { cache.clear(); cacheBytes = 0; }
            function remember(id, values) {
                const bytes = JSON.stringify(values).length * 2;
                const previous = cache.get(id);
                if (previous) { cacheBytes -= previous.bytes; cache.delete(id); }
                if (bytes > maximumCacheBytes) return;
                while (cacheBytes + bytes > maximumCacheBytes && cache.size) {
                    const first = cache.keys().next().value;
                    cacheBytes -= cache.get(first).bytes; cache.delete(first);
                }
                cache.set(id, { values, bytes, at: Date.now() }); cacheBytes += bytes;
            }
            function renderStatus() {
                if (active() && summary && state !== 'ready') summary.textContent = message;
            }
            async function load(ids, predicate, current) {
                timer = 0;
                const request = new AbortController(); controller = request;
                const obsolete = () => disposed || current !== generation || request.signal.aborted;
                const found = new Set(); let next = 0;
                try {
                    async function worker() {
                        while (next < ids.length && !obsolete()) {
                            const id = ids[next++]; let values = cache.get(id);
                            if (!values || Date.now() - values.at >= cacheMilliseconds) {
                                const url = new URL(endpoint, window.location.href); url.searchParams.set('id', id);
                                const response = await (window.MauiMoveItGridConnection?.fetch || fetch)(url.toString(), {
                                    method: 'GET', credentials: 'same-origin', cache: 'no-store',
                                    headers: { Accept: 'application/json' }, signal: request.signal
                                });
                                if (obsolete()) return;
                                if (response.status === 401) window.mauiSessionExpired?.();
                                if (!response.ok) throw new Error('JSON search is unavailable.');
                                const data = await response.json();
                                if (obsolete()) return;
                                if (data?.success !== true || data.document?.id !== id || !Array.isArray(data.document.values))
                                    throw new Error('Incomplete JSON search response.');
                                const leaves = data.document.values.map(item => {
                                    if (!Array.isArray(item.path) || !item.path.length || !item.path.every(part => typeof part === 'string')
                                        || item.value !== null && !['string', 'boolean', 'number'].includes(typeof item.value)
                                        || typeof item.value === 'number' && (!Number.isFinite(item.value) || Math.abs(item.value) > Number.MAX_SAFE_INTEGER))
                                        throw new Error('Incomplete JSON search response.');
                                    return { name: item.path.at(-1), pathText: model.pathText(item.path, root), value: item.value, readOnly: item.value === null };
                                });
                                remember(id, leaves); values = { values: leaves };
                            }
                            if (obsolete()) return;
                            if (values.values.some(leaf => predicate(leaf, leaf.value))) found.add(id);
                            completed++; message = `Searching JSON (${completed} of ${total})…`; renderStatus();
                        }
                    }
                    // Bound upstream definition reads, regardless of the number of grid rows.
                    await Promise.all(Array.from({ length: Math.min(4, ids.length) }, worker));
                    if (obsolete()) return;
                    matches = found; state = 'ready'; expiresAt = Date.now() + cacheMilliseconds;
                } catch (error) {
                    if (obsolete()) return;
                    request.abort(); matches.clear(); state = 'failed';
                    message = 'JSON search could not be completed. Select Refresh Now to retry.';
                    window.mauiDisplayToast?.(toast, message, 'error', 5000, 'page');
                } finally {
                    if (!disposed && current === generation) { controller = null; changed(); }
                }
            }
            function ensure() {
                if (disposed) return;
                if (!active()) {
                    if (signature || state) { cancel(); signature = ''; state = ''; matches.clear(); search.setCustomValidity(''); }
                    return;
                }
                const ids = [...new Set(getIds().map(String))].sort(), query = search.value.slice(5).trim();
                const key = JSON.stringify([query, ids]);
                if (key === signature && !(state === 'ready' && Date.now() >= expiresAt)) return;
                cancel(); signature = key; matches.clear(); completed = 0; total = ids.length;
                let predicate;
                try {
                    if (!query) throw new Error('Enter a JSON element or value after JSON:.');
                    predicate = model.compileTreeSearch(query, root); search.setCustomValidity('');
                } catch (error) {
                    state = 'invalid'; message = error.message; search.setCustomValidity(message); renderStatus(); return;
                }
                state = 'loading'; message = `Searching JSON (0 of ${total})…`; renderStatus();
                const current = generation;
                timer = setTimeout(() => { void load(ids, predicate, current); }, 300);
            }
            function invalidate() {
                cancel(); clearCache(); signature = ''; state = ''; matches.clear(); ensure(); changed();
            }
            grid.addEventListener('maui:json-search-invalidated', invalidate);
            grid.addEventListener('maui:task-filename-search-invalidated', invalidate);
            window.addEventListener('pagehide', () => { disposed = true; cancel(); clearCache(); matches.clear(); });
            window.addEventListener('pageshow', event => { if (event.persisted) { disposed = false; invalidate(); } });
            return { active, ensure, renderStatus, ready: () => state === 'ready', matches: id => state === 'ready' && matches.has(String(id)) };
        }
    };
})();
