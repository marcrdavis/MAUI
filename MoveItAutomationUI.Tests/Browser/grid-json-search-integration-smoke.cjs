'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const root = path.resolve(__dirname, '../..'), read = file => fs.readFileSync(path.join(root, file), 'utf8');
const event = class { constructor(type, options) { this.type = type; this.detail = options?.detail; } };

(async () => {
    // Execute the production Task refresh while an earlier request owns the row-refresh lock.
    const taskSource = read('wwwroot/js/tasks.js');
    const taskFunction = taskSource.slice(taskSource.indexOf('    async function refreshTaskGrid('), taskSource.indexOf('    // Purpose: Toggle task refresh.'));
    const events = [], task = { taskRefreshInFlight: true, taskGrid: { dispatchEvent: e => events.push(e.type) }, CustomEvent: event };
    vm.runInNewContext(taskFunction, task);
    await task.refreshTaskGrid(true);
    assert.deepEqual(events, ['maui:task-filename-search-invalidated'], 'a saved Task invalidates JSON/filename caches even when row refresh is busy');
    await task.refreshTaskGrid(false);
    assert.equal(events.length, 1, 'ordinary refresh must not invalidate definitions');

    // Execute the Host hook through the real shared lifecycle, including a skipped in-flight refresh.
    const hostSource = read('wwwroot/js/hosts.js');
    const hook = hostSource.match(/onRefreshRequested: forceRefresh => \{([\s\S]*?)\n        \},/)[1];
    const hostEvents = [], grid = { dispatchEvent: e => hostEvents.push(e.type), querySelector: () => null };
    const host = { grid, CustomEvent: event, window: { clearTimeout() {}, setTimeout() {} }, document: { addEventListener() {}, removeEventListener() {}, hidden: false } };
    vm.runInNewContext(read('wwwroot/js/resource-ui.js') + '\nthis.hook = forceRefresh => {' + hook + '};', host);
    let finish, reads = 0;
    const controller = host.window.mauiCreateResourceRefreshController({ grid, onRefreshRequested: host.hook,
        buildRefreshUrl: () => '/maui/Hosts/RefreshGrid', fetchJson: () => { reads++; return new Promise(resolve => finish = resolve); } });
    const pending = controller.refresh(false);
    await controller.refresh(true);
    assert.equal(reads, 1, 'forced refresh does not duplicate an in-flight grid read');
    assert.deepEqual(hostEvents, ['maui:json-search-invalidated']);
    finish({ rows: [] }); await pending;
    controller.destroy();
    await controller.refresh(true);
    assert.equal(hostEvents.filter(name => name === 'maui:json-search-invalidated').length, 1, 'closed pages do not restart searches');

    // Use the actual grid Report policy, then the actual Report dialog submit/open functions.
    const core = read('wwwroot/js/grid.js');
    const rule = core.slice(core.indexOf('        function getReportCreationIssue('), core.indexOf('        const reportApi ='));
    const state = { jsonSearch: { active: () => true, ensure() {}, ready: () => false } };
    vm.runInNewContext(rule, state);
    assert.match(state.getReportCreationIssue('Now'), /Complete a valid JSON search/);
    state.jsonSearch.ready = () => true;
    assert.equal(state.getReportCreationIssue('Now'), '');
    for (const mode of ['Once', 'Recurring']) assert.match(state.getReportCreationIssue(mode), /only be sent now/);
    state.jsonSearch.active = () => false;
    assert.equal(state.getReportCreationIssue('Recurring'), '', 'ordinary scheduled Reports are unchanged');

    const reportSource = read('wwwroot/js/outbound-reports.js');
    const submit = reportSource.slice(reportSource.indexOf('    async function submit()'), reportSource.indexOf('    function openCreate('));
    const open = reportSource.slice(reportSource.indexOf('    function openCreate('), reportSource.indexOf('    async function updateView('));
    const error = { hidden: true }, button = { disabled: false }, posts = [], notices = [];
    let mode = 'Recurring', issue = 'JSON searches can only be sent now.', opened = 0, captured = 0;
    const snapshot = { columns: [{ key: 'Name' }], rows: [{ id: '1', values: { Name: 'Matched' } }] };
    const api = { gridKey: 'tasks', getCreateIssue: () => issue, captureSnapshot: () => { captured++; return snapshot; },
        captureViewState: () => ({ searchText: 'JSON:Active=1' }), captureSourceContext: () => ({ path: '/Tasks' }) };
    const dialog = { close() {}, showModal: () => opened++, querySelector: selector => selector === '[data-report-error]' ? error : selector === '[data-report-submit]' ? button : { value: 'Example' } };
    const report = { dialog, currentApi: api, createUrl: '/maui/OutboundReports/Create', ensureDialog() {}, reset() {}, refreshEmailStatus() {}, requestAnimationFrame() {},
        buildSchedule: () => ({ deliveryMode: mode, timeOfDay: '08:00' }), isFiveMinuteTime: () => true,
        postJson: async (url, payload) => { posts.push(payload); return {}; }, window: { mauiShowToast: message => notices.push(message) } };
    vm.runInNewContext(submit + open, report);
    await report.submit();
    assert.equal(posts.length, 0); assert.equal(captured, 0); assert.equal(error.hidden, false); assert.match(error.textContent, /only be sent now/);
    mode = 'Now'; issue = 'Complete a valid JSON search before sending a Report.';
    await report.submit(); assert.equal(posts.length, 0, 'pending/invalid/failed JSON searches cannot send empty snapshots');
    report.openCreate(api); assert.equal(opened, 0); assert.match(notices.at(-1), /Complete a valid JSON search/);
    issue = ''; report.openCreate(api); assert.equal(opened, 1);
    await report.submit(); assert.equal(posts.length, 1); assert.equal(posts[0].snapshot, snapshot); assert.equal(button.disabled, false);
    mode = 'Recurring'; await report.submit(); assert.equal(posts.length, 2); assert.equal(posts[1].snapshot, null, 'ordinary schedules retain normal server regeneration');
    console.log('PASS JSON search integration: forced-refresh/edit races, shared Host lifecycle, disposed pages, completed Send Now snapshots, explicit unsupported-schedule errors and unchanged ordinary Report submission.');
})().catch(error => { console.error(error); process.exitCode = 1; });
