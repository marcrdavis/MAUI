const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

const script = name => fs.readFileSync(path.join(__dirname, '../../wwwroot/js', name), 'utf8');
const window = {};
vm.runInNewContext(script('records-batches.js'), { window });

function run(id, status = 'Success', cursorKey = 'RunID') {
    return { id: `run-${id}`, values: { [cursorKey]: String(id), Status: status } };
}

function history(newest, oldest = 1, cursorKey = 'RunID') {
    return Array.from({ length: newest - oldest + 1 }, (_, index) => run(newest - index, 'Success', cursorKey));
}

function fixture(initial = history(12), maximumEntries = 10, cursorKey = 'RunID') {
    const batchSize = 2;
    const state = { source: initial, requests: [], updates: [], progress: [], failAtCursor: null };
    const batches = window.MauiRecordsBatches.create({
        cursorKey,
        batchSize,
        maximumEntries,
        onUpdate: payload => state.updates.push(payload),
        onProgress: count => state.progress.push(count),
        requestBatch: async (cursor, offset) => {
            state.requests.push([cursor, offset]);
            if (cursor === state.failAtCursor) throw new Error('Simulated request failure');
            const available = state.source.filter(row => !cursor || BigInt(row.values[cursorKey]) < BigInt(cursor));
            const rows = available.slice(0, Math.min(batchSize, maximumEntries - offset));
            return {
                rows,
                hasMore: available.length > rows.length && offset + rows.length < maximumEntries,
                nextCursor: rows.at(-1)?.values[cursorKey]
            };
        }
    });
    return { state, batches, latest: () => state.updates.at(-1), ids: () => Array.from(state.updates.at(-1).rows, row => row.values[cursorKey]) };
}

test('Next appends and All renders once with progress until the cap', async () => {
    const { state, batches, latest, ids } = fixture(history(12), 7);
    await batches.refreshNewest();
    assert.deepEqual(ids(), ['12', '11']);
    await batches.fetchOlder(false);
    assert.deepEqual(ids(), ['12', '11', '10', '9']);
    await batches.fetchOlder(true);
    assert.deepEqual(ids(), ['12', '11', '10', '9', '8', '7', '6']);
    assert.deepEqual(state.requests, [['', 0], ['11', 2], ['9', 4], ['7', 6]]);
    assert.deepEqual(state.progress, [2, 4, 6, 7]);
    assert.equal(state.updates.length, 3);
    assert.equal(latest().hasMore, false);
});

test('All uses the actual count at the end of a short source', async () => {
    const { state, batches, latest, ids } = fixture(history(5));
    await batches.refreshNewest();
    assert.equal(latest().totalCount, 10);
    await batches.fetchOlder(true);
    assert.deepEqual(state.progress, [2, 4, 5]);
    assert.deepEqual(ids(), ['5', '4', '3', '2', '1']);
    assert.equal(latest().totalCount, 5);
    assert.equal(latest().hasMore, false);
});

test('Task tags survive append and Fetch All, and refresh uses the newly loaded values', async () => {
    const source = history(6).map(row => ({ ...row, values: { ...row.values, Tags: 'Operations; Finance' } }));
    const { state, batches, latest } = fixture(source);
    await batches.refreshNewest();
    await batches.fetchOlder(false);
    await batches.fetchOlder(true);
    assert.equal(latest().rows.length, 6);
    assert.ok(latest().rows.every(row => row.values.Tags === 'Operations; Finance'));
    state.source = source.map(row => ({ ...row, values: { ...row.values, Tags: 'Updated' } }));
    await batches.refreshAllLoaded();
    assert.ok(latest().rows.every(row => row.values.Tags === 'Updated'));
});

test('All keeps completed pages and resumes from the failed cursor', async () => {
    const { state, batches, latest, ids } = fixture(history(7));
    await batches.refreshNewest();
    state.failAtCursor = '4';
    await assert.rejects(batches.fetchOlder(true), /Simulated request failure/);
    assert.deepEqual(ids(), ['7', '6', '5', '4']);
    assert.equal(latest().nextCursor, '4');
    state.failAtCursor = null;
    await batches.fetchOlder(true);
    assert.deepEqual(ids(), ['7', '6', '5', '4', '3', '2', '1']);
});

test('Automatic refresh bridges a gap and merges by ID without rebuilding older models', async () => {
    const { state, batches, latest, ids } = fixture();
    await batches.refreshNewest();
    await batches.fetchOlder(false);
    const older = latest().rows.at(-1);
    state.source = history(17);
    state.source[5] = run(12, 'Failed');
    state.requests.length = 0;
    await batches.refreshNewest();
    assert.deepEqual(state.requests, [['', 0], ['16', 2], ['14', 4]]);
    assert.deepEqual(ids(), ['17', '16', '15', '14', '13', '12', '11', '10', '9']);
    assert.equal(latest().rows.find(row => row.values.RunID === '12').values.Status, 'Failed');
    assert.equal(latest().rows.at(-1), older);
    await batches.fetchOlder(false);
    assert.deepEqual(state.requests.at(-1), ['9', 9]);
    assert.equal(latest().hasMore, false);
});

test('Automatic refresh keeps the newest rows when the grid is already at the cap', async () => {
    const { state, batches, latest, ids } = fixture(history(7), 5);
    await batches.refreshNewest();
    await batches.fetchOlder(true);
    state.source = history(8);
    await batches.refreshNewest();
    assert.deepEqual(ids(), ['8', '7', '6', '5', '4']);
    assert.equal(latest().hasMore, false);
});

test('Manual refresh preserves the loaded count and rebuilds the continuation', async () => {
    const { state, batches, latest, ids } = fixture();
    await batches.refreshNewest();
    await batches.fetchOlder(false);
    state.source = history(13).filter(row => row.values.RunID !== '10');
    await batches.refreshNewest();
    assert.deepEqual(ids(), ['13', '12', '11', '10', '9']);
    state.failAtCursor = '9';
    const beforeFailure = latest();
    await assert.rejects(batches.refreshAllLoaded(), /Simulated request failure/);
    assert.equal(latest(), beforeFailure);
    state.failAtCursor = null;
    await batches.refreshAllLoaded();
    assert.deepEqual(ids(), ['13', '12', '11', '9', '8']);
    assert.equal(latest().nextCursor, '8');
    await batches.fetchOlder(false);
    assert.deepEqual(state.requests.at(-1), ['8', 5]);
    assert.deepEqual(ids(), ['13', '12', '11', '9', '8', '7', '6']);
});

test('A repeated continuation cursor cannot loop Fetch All', async () => {
    const updates = [];
    const batches = window.MauiRecordsBatches.create({
        cursorKey: 'RunID',
        batchSize: 2,
        maximumEntries: 10,
        onUpdate: payload => updates.push(payload),
        onProgress: () => {},
        requestBatch: async () => ({ rows: [run(2), run(1)], nextCursor: '1', hasMore: true })
    });
    await batches.refreshNewest();
    await assert.rejects(batches.fetchOlder(true), /paging did not advance/);
    assert.equal(updates.length, 1);
});

function runDialogFixture(request) {
    const elements = new Map();
    const state = { closes: 0 };
    const dialog = {
        querySelector(selector) {
            if (!elements.has(selector)) elements.set(selector, {});
            return elements.get(selector);
        },
        close() { state.closes++; },
        showModal() {}
    };
    const reports = script('reports.js');
    const start = reports.indexOf('    function openRun(');
    const end = reports.indexOf('    async function showHistory(', start);
    assert.ok(start >= 0 && end > start);
    const context = {
        runDialog: dialog,
        renderRunQueue() {},
        setRunQueueItem(item, status, message) { Object.assign(item, { status, message }); },
        request,
        loadReports: async () => {},
        urls: { run: '/Reports/Run' }
    };
    const actions = vm.runInNewContext(`let runQueue = [], runQueueActive = false, runCancelRequested = false;
        ${reports.slice(start, end)}
        ({ openRun, confirmRun, cancelPendingRuns })`, context);
    return { actions, state, button: dialog.querySelector('[data-report-run-cancel]'), notes: dialog.querySelector('[data-report-run-description]') };
}

test('Run Report switches to Close after success or failure and resets when opened again', async () => {
    let fail = false;
    const { actions, state, button, notes } = runDialogFixture(async () => {
        if (fail) throw new Error('Report failed');
    });
    actions.openRun({ id: 'one' });
    assert.equal(button.textContent, 'Cancel');
    await actions.confirmRun();
    assert.equal(button.textContent, 'Close');
    actions.cancelPendingRuns();
    assert.equal(state.closes, 1);

    actions.openRun({ id: 'one' });
    assert.equal(button.textContent, 'Cancel');
    fail = true;
    await actions.confirmRun();
    assert.equal(button.textContent, 'Close');
    assert.match(notes.textContent, /1 failed/);
});

test('Cancel still stops pending reports while the active report finishes', async () => {
    let complete;
    let requests = 0;
    const { actions, button, notes } = runDialogFixture(() => {
        requests++;
        return new Promise(resolve => { complete = resolve; });
    });
    actions.openRun([{ id: 'one' }, { id: 'two' }]);
    const running = actions.confirmRun();
    assert.equal(button.textContent, 'Cancel');
    actions.cancelPendingRuns();
    assert.equal(button.textContent, 'Cancel');
    complete();
    await running;
    assert.equal(requests, 1);
    assert.equal(button.textContent, 'Close');
    assert.match(notes.textContent, /1 successful, 1 cancelled/);
});

test('Log ID drives both the merged and manually refreshed continuation', async () => {
    const { state, batches, latest, ids } = fixture(history(12, 1, 'LogID'), 10, 'LogID');
    await batches.refreshNewest();
    await batches.fetchOlder(false);
    state.source = history(13, 1, 'LogID');
    await batches.refreshNewest();
    assert.deepEqual(ids(), ['13', '12', '11', '10', '9']);
    assert.equal(latest().nextCursor, '9');

    await batches.refreshAllLoaded();
    assert.deepEqual(ids(), ['13', '12', '11', '10', '9']);
    assert.equal(latest().nextCursor, '9');
    await batches.fetchOlder(false);
    assert.deepEqual(state.requests.at(-1), ['9', 5]);
    assert.deepEqual(ids(), ['13', '12', '11', '10', '9', '8', '7']);
    assert.ok(latest().rows.every(row => !Object.hasOwn(row.values, 'RunID')));
});

function recordsPage(gridId, endpoint, scope = {}, append = true) {
    const controls = new Map();
    const requests = [];
    const updates = [];
    const messages = [];
    const flush = () => new Promise(resolve => setImmediate(resolve));
    const nextSelector = '[data-report-fetch-next]';
    const allSelector = '[data-report-fetch-all]';
    const control = () => ({
        addEventListener(type, listener) { this[type] = listener; },
        setAttribute(name, value) { this[name] = value; },
        closest() { return null; }
    });
    if (append) {
        for (const selector of [nextSelector, allSelector, '[data-report-fetch-group]', '[data-report-fetch-next-count]', '[data-report-loaded-count]', '[data-report-fetch-progress]']) {
            controls.set(selector, control());
        }
        controls.get(nextSelector).querySelector = selector => controls.get(selector) ?? null;
    }
    controls.set('[data-report-batch-range]', control());
    controls.set('[data-report-range-end]', control());
    controls.set('[data-report-total-count]', control());
    controls.set('[data-pager-summary]', control());
    controls.set('[data-report-refresh-now]', control());

    const grid = {
        dataset: { grid: gridId },
        querySelector: selector => controls.get(selector) ?? null,
        dispatchEvent: event => { if (event.type === 'maui:grid-replace-rows') updates.push(event.detail); }
    };
    const config = {
        dataset: {
            ...scope, batchUrl: `/MAUI/Records/${endpoint}Batch`, batchSize: '2', maximumEntries: '7', uiRefreshSeconds: '0'
        }
    };
    const browser = {
        MauiRecordsBatches: append ? window.MauiRecordsBatches : undefined,
        location: { href: `https://example.test/MAUI/Records/${endpoint}` },
        clearTimeout() {},
        addEventListener() {},
        mauiDisplayToast: (_toast, message) => messages.push(message),
        fetch: url => new Promise(resolve => requests.push({
            url: new URL(url),
            complete: payload => resolve({
                ok: true,
                headers: { get: () => 'application/json' },
                json: async () => payload
            })
        }))
    };
    const document = {
        querySelector: selector => selector === '[data-report-page-config]' ? config
            : selector === '.data-workspace[data-grid]' ? grid
                : controls.get(selector) ?? null,
        addEventListener() {}
    };
    const CustomEvent = function (type, { detail } = {}) { Object.assign(this, { type, detail }); };
    vm.runInNewContext(script('records.js'), { window: browser, document, URL, Headers, CustomEvent, console: { warn() {} } });
    return { controls, requests, updates, messages, flush, next: controls.get(nextSelector), all: controls.get(allSelector) };
}

for (const [gridId, endpoint, cursorKey, scope] of [
    ['taskruns', 'TaskRun', 'RunID', {}],
    ['fileactivity', 'FileActivity', 'LogID', { taskId: '17', runId: '85', runNominalStart: '2026-11-16T09:00:00' }],
    ['audit', 'Audit', 'LogID', { targetType: 'task', targetId: '17' }]
]) {
    test(`${endpoint} appends, keeps its context, and hides fetch controls after Fetch All`, async () => {
        const { controls, requests, updates, flush, next, all } = recordsPage(gridId, endpoint, scope);
        const progress = controls.get('[data-report-fetch-progress]');
        const summary = controls.get('[data-pager-summary]');
        const fetchGroup = controls.get('[data-report-fetch-group]');
        assert.equal(fetchGroup.hidden, true);
        requests[0].complete({ rows: history(9, 8, cursorKey), hasMore: true, nextCursor: '8' });
        await flush();
        assert.equal(controls.get('[data-report-fetch-next-count]').textContent, '2');
        assert.equal(next.title, 'Fetch next 2 rows');
        assert.equal(next['aria-label'], next.title);
        assert.equal(next.textContent, undefined, 'Updating the count leaves the icon intact');
        assert.equal(fetchGroup.hidden, false);
        assert.equal(controls.get('[data-report-range-end]').textContent, '2');
        assert.equal(controls.get('[data-report-total-count]').textContent, '7');

        next.click();
        assert.equal(next.disabled, true);
        assert.equal(all.disabled, true);
        assert.equal(summary.hidden, true);
        assert.equal(progress.hidden, false);
        assert.match(progress.textContent, /Loading 2 rows/);
        assert.equal(requests[1].url.searchParams.get('cursor'), '8');
        requests[1].complete({ rows: history(7, 6, cursorKey), hasMore: true, nextCursor: '6' });
        await flush();
        assert.equal(updates.at(-1).rows.length, 4);
        assert.equal(controls.get('[data-report-range-end]').textContent, '4');
        assert.equal(summary.hidden, false);
        assert.equal(progress.hidden, true);
        assert.equal(next.disabled, false);

        all.click();
        assert.equal(all.disabled, true);
        assert.equal(summary.hidden, true);
        assert.match(progress.textContent, /Loading 4 rows/);
        requests[2].complete({ rows: history(5, 4, cursorKey), hasMore: true, nextCursor: '4' });
        await flush();
        assert.match(progress.textContent, /Loading 6 rows/);
        requests[3].complete({ rows: history(3, 3, cursorKey), hasMore: false });
        await flush();

        assert.equal(updates.length, 3);
        assert.equal(updates.at(-1).reuseRows, true);
        assert.equal(updates.at(-1).rows.length, 7);
        assert.equal(summary.hidden, false);
        assert.equal(progress.hidden, true);
        assert.equal(fetchGroup.hidden, true);
        assert.equal(controls.get('[data-report-batch-range]').hidden, true);
        assert.equal(controls.get('[data-report-loaded-count]').hidden, false);
        for (const { url } of requests) {
            assert.equal(url.pathname, `/MAUI/Records/${endpoint}Batch`);
            for (const [key, value] of Object.entries(scope)) {
                assert.equal(url.searchParams.get(key === 'runNominalStart' ? 'nominalStart' : key), value);
            }
        }
    });
}

test('A failed Fetch Next restores the footer and lets the user retry the same batch', async () => {
    const { controls, requests, updates, messages, flush, next, all } = recordsPage('taskruns', 'TaskRun');
    const progress = controls.get('[data-report-fetch-progress]');
    const summary = controls.get('[data-pager-summary]');
    const fetchGroup = controls.get('[data-report-fetch-group]');
    requests[0].complete({ rows: history(9, 8), hasMore: true, nextCursor: '8' });
    await flush();

    next.click();
    assert.equal(summary.hidden, true);
    requests[1].complete({});
    await flush();
    assert.match(messages.at(-1), /Unable to fetch more rows/);
    assert.equal(updates.length, 1);
    assert.equal(summary.hidden, false);
    assert.equal(progress.hidden, true);
    assert.equal(next.disabled, false);
    assert.equal(all.disabled, false);
    assert.equal(fetchGroup.hidden, false);

    next.click();
    assert.equal(requests[2].url.href, requests[1].url.href);
    requests[2].complete({ rows: history(7, 6), hasMore: false });
    await flush();
    assert.equal(updates.at(-1).rows.length, 4);
    assert.equal(fetchGroup.hidden, true);
});

test('Dashboard File Activity continues to refresh its single preset result', async () => {
    const { controls, requests, updates, flush, next, all } =
        recordsPage('fileactivity', 'FileActivity', { reportPreset: 'failed' }, false);
    const payload = { rows: history(5, 1, 'LogID'), end: 5, totalCount: 5, hasMore: false };
    requests[0].complete(payload);
    await flush();
    assert.equal(updates[0].reuseRows, false);
    assert.equal(next, undefined);
    assert.equal(all, undefined);
    assert.equal(controls.has('[data-report-fetch-group]'), false);

    controls.get('[data-report-refresh-now]').click();
    assert.equal(requests[1].url.searchParams.get('dashboardView'), 'failed');
    assert.equal(requests[1].url.searchParams.has('cursor'), false);
    requests[1].complete(payload);
    await flush();
    assert.equal(updates.length, 2);
    assert.equal(updates[1].rows.length, 5);
});
