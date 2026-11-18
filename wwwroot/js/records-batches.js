/*
 * MAUI Records history accumulates cursor-based batches and refreshes the loaded window.
 */
(() => {
    'use strict';

    function orderId(value) {
        const text = String(value ?? '');
        return /^\d+$/.test(text) ? BigInt(text) : 0n;
    }

    function distinctRows(rows) {
        const seen = new Set();
        return rows.filter(row => !seen.has(row.id) && seen.add(row.id));
    }

    function create({ requestBatch, cursorKey, batchSize, maximumEntries, onUpdate, onProgress }) {
        let loadedRows = [];
        let continuation = null;

        function recordCursor(row) {
            return String(row?.values?.[cursorKey] ?? '');
        }

        async function readPage({ cursor, offset }) {
            const payload = await requestBatch(cursor, offset);
            if (!Array.isArray(payload?.rows)) throw new Error('Records returned an invalid batch.');
            const end = offset + payload.rows.length;
            const nextCursor = String(payload.nextCursor ?? '');
            let next = null;
            if (payload.hasMore && end < maximumEntries) {
                const nextId = orderId(nextCursor);
                if (end <= offset || nextId <= 0n || (cursor && nextId >= orderId(cursor))) {
                    throw new Error('Records paging did not advance.');
                }
                next = { cursor: nextCursor, offset: end };
            }
            return { rows: payload.rows, next, refreshedUtc: payload.refreshedUtc };
        }

        function cursorAfter(rows) {
            if (rows.length >= maximumEntries) return null;
            const cursor = recordCursor(rows.at(-1));
            if (orderId(cursor) <= 0n) throw new Error('Records did not return a usable continuation cursor.');
            return { cursor, offset: rows.length };
        }

        function publish(rows, next, refreshedUtc) {
            loadedRows = rows.slice(0, maximumEntries);
            continuation = next;
            onUpdate({
                rows: loadedRows,
                offset: 0,
                end: loadedRows.length,
                totalCount: next ? maximumEntries : loadedRows.length,
                nextCursor: next?.cursor ?? '',
                hasMore: Boolean(next),
                nextCount: next ? Math.min(batchSize, maximumEntries - next.offset) : 0,
                refreshedUtc
            });
        }

        async function fetchOlder(all) {
            if (!continuation) return;
            let next = continuation;
            let lastPage;
            const olderRows = [];
            onProgress(loadedRows.length);
            try {
                do {
                    const page = await readPage(next);
                    olderRows.push(...page.rows);
                    lastPage = page;
                    next = page.next;
                    if (all) onProgress(loadedRows.length + olderRows.length);
                } while (all && next);
            } finally {
                // Preserve completed batches even if a later request fails.
                if (lastPage) publish(distinctRows([...loadedRows, ...olderRows]), next, lastPage.refreshedUtc);
            }
        }

        async function refreshAllLoaded() {
            const target = !loadedRows.length ? batchSize : continuation ? loadedRows.length : maximumEntries;
            let page = await readPage({ cursor: '', offset: 0 });
            const refreshed = [...page.rows];
            while (page.next && refreshed.length < target) {
                page = await readPage(page.next);
                refreshed.push(...page.rows);
            }
            const unique = distinctRows(refreshed);
            const rows = unique.slice(0, target);
            const next = unique.length > rows.length || page.next ? cursorAfter(rows) : null;
            publish(rows, next, page.refreshedUtc);
        }

        async function refreshNewest() {
            let page = await readPage({ cursor: '', offset: 0 });
            const latest = [...page.rows];
            if (!loadedRows.length || !page.next) {
                publish(distinctRows(latest), page.next, page.refreshedUtc);
                return;
            }

            const loadedIndex = new Map(loadedRows.map((row, index) => [row.id, index]));
            let lastOverlap = -1;
            while (true) {
                for (const row of page.rows) lastOverlap = Math.max(lastOverlap, loadedIndex.get(row.id) ?? -1);
                if (lastOverlap >= 0 || !page.next) break;
                // More than one batch may have arrived since the last refresh.
                page = await readPage(page.next);
                latest.push(...page.rows);
            }

            if (!page.next) {
                publish(distinctRows(latest), null, page.refreshedUtc);
                return;
            }

            const boundary = orderId(page.next.cursor);
            const older = loadedRows.slice(lastOverlap + 1).filter(row => {
                const id = orderId(recordCursor(row));
                return id === 0n || id < boundary;
            });
            const rows = distinctRows([...latest, ...older]);
            publish(rows, continuation ? cursorAfter(rows) : null, page.refreshedUtc);
        }

        return { fetchOlder, refreshAllLoaded, refreshNewest };
    }

    window.MauiRecordsBatches = { create };
})();
