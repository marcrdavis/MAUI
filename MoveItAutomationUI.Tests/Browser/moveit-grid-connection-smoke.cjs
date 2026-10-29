'use strict';
// Exercise the production connection helper and resource lifecycle with controlled HTTP
// responses. Browser->MAUI failures must never be mislabeled as MOVEit transport failures.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const read = file => fs.readFileSync(path.resolve(__dirname, '../..', file), 'utf8');
let checks = 0;
function check(actual, expected) { assert.equal(actual, expected); checks++; }
function environment(scope = 'environment-a', storage = new Map()) {
    const banner = { hidden: true }, queue = [];
    const document = { documentElement: { dataset: { moveitGridScope: scope } }, hidden: false,
        querySelector: () => banner, addEventListener() {}, removeEventListener() {} };
    const window = { sessionStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) },
        fetch: async () => { const response = queue.shift(); if (response instanceof Error) throw response; return response; },
        setTimeout: () => 1, clearTimeout() {}, dispatchEvent() {} };
    const context = { window, document, console, Event: class {}, CustomEvent: class { constructor(type) { this.type = type; } } };
    vm.runInNewContext(read('wwwroot/js/shared/moveit-grid-connection.js'), context);
    return { banner, queue, window, context, storage,
        async request(outcome, status = 503) {
            queue.push({ ok: status >= 200 && status < 300, status, headers: { get: () => outcome } });
            try { return await window.MauiMoveItGridConnection.fetch('/MAUI/Tasks/RefreshGrid'); }
            catch (error) { return error; }
        }
    };
}
(async () => {
    const env = environment();
    check(env.banner.hidden, true);
    let error = await env.request('timeout');
    check(env.banner.hidden, true);
    check(env.window.MauiMoveItGridConnection.isHandled(error), false);
    await env.request('none', 200); // cache reuse is not a successful network attempt
    error = await env.request('timeout');
    check(env.banner.hidden, false);
    check(env.window.MauiMoveItGridConnection.isHandled(error), true);
    await env.request('none', 200);
    check(env.banner.hidden, false);
    await env.request('responded', 500);
    check(env.banner.hidden, false);
    await env.request('live', 200);
    check(env.banner.hidden, true);
    await env.request('timeout');
    check(env.banner.hidden, true); // success reset the streak
    await env.request('responded', 500);
    await env.request('timeout');
    check(env.banner.hidden, true); // a real HTTP error breaks consecutive timeouts
    error = await env.request('unreachable');
    check(env.banner.hidden, false);
    check(env.window.MauiMoveItGridConnection.isHandled(error), true);
    check(error.message, 'MAUI cannot connect to MOVEit.');
    const revisit = environment('environment-a', env.storage);
    check(revisit.banner.hidden, false); // navigation retains the warning
    const other = environment('environment-b', env.storage);
    check(other.banner.hidden, true); // another environment/login cannot inherit it
    const auth = await other.request('unreachable', 401);
    check(auth.status, 401);
    check(other.banner.hidden, true);
    await other.request('unreachable', 403);
    check(other.banner.hidden, true);
    const ordinary = await other.request(null, 500);
    check(ordinary.status, 500);
    check(other.banner.hidden, true);
    const browserError = new TypeError('Failed to fetch');
    other.queue.push(browserError);
    await assert.rejects(other.window.MauiMoveItGridConnection.fetch('/Tasks/RefreshGrid'), error => error === browserError);
    checks++;
    check(other.banner.hidden, true);
    check(other.window.MauiMoveItGridConnection.isHandled(browserError), false);
    // These JSON adapters also serve editor/actions requests. An unrelated authorization
    // response has no grid marker and cannot reset the consecutive grid-timeout count.
    for (const status of [401, 403]) {
        const isolated = environment(`nongrid-auth-${status}`);
        await isolated.request('timeout');
        const unrelated = await isolated.request(null, status);
        check(unrelated.status, status);
        check(isolated.banner.hidden, true);
        await isolated.request('timeout');
        check(isolated.banner.hidden, false);
    }
    const publicPage = environment('');
    await publicPage.request('unreachable');
    check(publicPage.banner.hidden, true);
    const unavailableStorage = environment('x');
    unavailableStorage.window.sessionStorage.setItem = () => { throw new Error('Blocked storage'); };
    await unavailableStorage.request('unreachable');
    check(unavailableStorage.banner.hidden, false);

    // The real resource lifecycle must retain rows, finish initial loading, suppress repeated
    // toasts, and schedule retries. An unrelated failure still reaches the usual callback.
    const lifecycle = environment();
    vm.runInNewContext(read('wwwroot/js/resource-ui.js'), lifecycle.context);
    const events = [];
    let errors = 0, connectionFailures = 0, applied = 0;
    const controller = lifecycle.window.mauiCreateResourceRefreshController({
        grid: { dispatchEvent: event => events.push(event.type) }, intervalSeconds: 60,
        buildInitialUrl: () => '/Tasks/InitialGrid', buildRefreshUrl: () => '/Tasks/RefreshGrid',
        fetchJson: async url => { await lifecycle.window.MauiMoveItGridConnection.fetch(url); return { rows: [] }; },
        onInitialError: () => errors++, onRefreshError: () => errors++,
        onConnectionFailure: () => connectionFailures++, applyPayload: () => applied++
    });
    lifecycle.queue.push({ ok: false, status: 503, headers: { get: () => 'unreachable' } });
    await controller.loadInitial();
    check(events.includes('maui:grid-initial-data-ready'), true);
    check(controller.isInFlight, false);
    check(applied, 0); check(errors, 0); check(connectionFailures, 1);
    lifecycle.queue.push({ ok: false, status: 503, headers: { get: () => 'unreachable' } });
    await controller.refresh();
    check(applied, 0); check(errors, 0); check(connectionFailures, 2);
    lifecycle.queue.push(new Error('ordinary failure'));
    await controller.refresh();
    check(errors, 1);
    lifecycle.queue.push({ ok: true, status: 200, headers: { get: () => 'live' } });
    await controller.refresh();
    check(applied, 1); check(lifecycle.banner.hidden, true);
    controller.destroy();

    for (const name of ['tasks', 'hosts', 'scripts', 'datelists', 'globalparameters', 'keys-certificates', 'groups-and-permissions', 'records', 'upcoming-runs']) {
        check(read(`wwwroot/js/${name}.js`).includes('window.MauiMoveItGridConnection?.fetch'), true);
    }
    // Run every production page JSON adapter through the shared lifecycle. The previous
    // presence-only checks could miss an adapter dropping the handled-error marker.
    function productionFunction(source, name) {
        const start = source.indexOf(`async function ${name}(`);
        assert.ok(start >= 0, name);
        return source.slice(start, source.indexOf('\n    }', start) + 6);
    }
    const names = ['tasks', 'hosts', 'scripts', 'datelists', 'globalparameters', 'keys-certificates', 'groups-and-permissions', 'records', 'upcoming-runs'];
    const response = (outcome, status = 200) => ({ status, ok: status >= 200 && status < 300,
        headers: { get: key => key.toLowerCase() === 'content-type' ? 'application/json' : outcome },
        json: async () => ({ rows: [], groups: [], success: true }) });
    for (const name of names) {
        const page = environment(`adapter-${name}`);
        const source = read(`wwwroot/js/${name}.js`);
        Object.assign(page.context, { Headers, FormData, URL, antiForgeryToken: '', editGeneration: 0, editing: false,
            root: { querySelector: () => null, dataset: {} }, endpoint: () => '/MAUI/Tasks/RefreshGrid' });
        page.window.location = { replace() {} };
        vm.runInNewContext(productionFunction(source, 'fetchJson'), page.context);
        vm.runInNewContext(read('wwwroot/js/resource-ui.js'), page.context);
        let errors = 0, applies = 0;
        const controller = page.window.mauiCreateResourceRefreshController({
            grid: { dispatchEvent() {} }, intervalSeconds: 60,
            buildInitialUrl: () => '/MAUI/InitialGrid', buildRefreshUrl: () => '/MAUI/RefreshGrid',
            fetchJson: page.context.fetchJson, applyPayload: () => applies++,
            onInitialError: () => errors++, onRefreshError: () => errors++
        });
        page.queue.push(response('unreachable', 503)); await controller.loadInitial();
        check(page.banner.hidden, false); check(errors, 0); check(applies, 0);
        page.queue.push(response('unreachable', 503)); await controller.refresh();
        check(errors, 0); check(applies, 0);
        page.queue.push(response('none')); await controller.refresh();
        check(page.banner.hidden, false);
        page.queue.push(response('live')); await controller.refresh();
        check(page.banner.hidden, true);
        page.queue.push(response('timeout', 503)); await controller.refresh();
        check(page.banner.hidden, true); check(errors, 1);
        page.queue.push(response('timeout', 503)); await controller.refresh();
        check(page.banner.hidden, false); check(errors, 1);
        page.queue.push(response('live')); await controller.refresh();
        check(page.banner.hidden, true);
        page.queue.push(response('responded', 500)); await controller.refresh();
        check(page.banner.hidden, true); check(errors, 2);
        controller.destroy();

        // Also execute the custom refresh catches owned by Tasks, Records and Upcoming Runs.
        if (['tasks', 'records', 'upcoming-runs'].includes(name)) {
            let toasts = 0, renders = 0;
            const grid = { dispatchEvent() {} };
            Object.assign(page.context, { taskRefreshInFlight: false, refreshInFlight: false,
                taskGrid: grid, grid, batchUrl: '/MAUI/Records/TaskRunBatch',
                refreshQueued: false, refreshQueuedManual: false,
                errorBanner: { hidden: false, textContent: 'Earlier timeout' },
                closeTaskRowActionMenu() {}, closeReportRowActionMenu() {}, closeActionMenu() {},
                setRefreshBusy() {}, clearTaskRefreshTimer() {}, clearRefreshTimer() {},
                scheduleTaskGridRefresh() {}, scheduleRefresh() {}, updateBatchUi() {}, setLastRefresh() {},
                buildTaskGridRequestUrl: () => new URL('https://maui.test/Tasks/RefreshGrid'),
                buildBatchUrl: () => '/MAUI/Records/TaskRunBatch',
                applyTaskGridPayload: () => renders++, applyUpcomingRunsPayload: () => renders++,
                showStatus: () => toasts++, showNativeEditorStatus: () => toasts++,
                console: { warn() {} }
            });
            const functionName = name === 'tasks' ? 'refreshTaskGrid' : name === 'records' ? 'loadBatch' : 'refreshUpcomingRuns';
            vm.runInNewContext(productionFunction(source, functionName), page.context);
            const call = () => name === 'records' ? page.context.loadBatch('', 0, true)
                : page.context[functionName](true);
            page.queue.push(response('unreachable', 503)); await call();
            check(page.banner.hidden, false); check(toasts, 0); check(renders, 0);
            if (name === 'upcoming-runs') check(page.context.errorBanner.hidden, true);
            page.queue.push(response('unreachable', 503)); await call(); check(toasts, 0);
            page.queue.push(response('live')); await call(); check(page.banner.hidden, true);
        }
    }
    const layout = read('Views/Shared/_Layout.cshtml');
    check(layout.includes('system-message-banner system-message-important" data-moveit-unreachable-banner'), true);
    check(layout.indexOf('~/js/shared/moveit-grid-connection.js') < layout.indexOf('~/js/site.js'), true);
    console.log(`${checks} MOVEit grid connection checks passed.`);
})().catch(error => { console.error(error); process.exitCode = 1; });
