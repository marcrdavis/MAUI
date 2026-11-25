'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../wwwroot/js/report-api-access.js'), 'utf8');
function fixture(appBase = '/MAUI/', createMode = false) {
    const controls = new Map();
    const element = selector => {
        if (!controls.has(selector)) controls.set(selector, {
            hidden: false, disabled: false, checked: false, value: '', textContent: '',
            addEventListener(event, listener) { this[event] = listener; },
            focus() {}, select() {}
        });
        return controls.get(selector);
    };
    const section = { querySelector: element };
    const dialog = { querySelector: selector => selector === '[data-report-api-section]' ? section : element(selector),
        addEventListener(event, listener) { this[event] = listener; } };
    const config = { dataset: { appBase, apiAccessUrl: appBase + 'Reports/ApiAccess',
        apiPrepareUrl: appBase + (createMode ? 'OutboundReports/PrepareApiLink' : 'Reports/PrepareApiLink'), apiRegenerateUrl: appBase + 'Reports/RegenerateApiLink' } };
    const requests = [], copied = [];
    const window = { location: { origin: 'https://maui.example.test' }, mauiConfirm: async () => true };
    const document = { querySelector: selector => selector === '[data-reports-page-config]' ? (createMode ? null : config)
        : selector === '[data-report-edit-dialog]' ? (createMode ? null : dialog) : { value: 'antiforgery-token' } };
    vm.runInNewContext(source, { window, document, URL, navigator: { clipboard: { writeText: async value => copied.push(value) } },
        fetch: (url, options) => new Promise(resolve => requests.push({ url, options, finish(state, ok = true) {
            resolve({ ok, headers: { get: () => 'application/json' }, json: async () => ok ? { apiAccess: state } : { message: state } });
        } })) });
    return { api: createMode ? window.MauiReportApiAccess.create(dialog, config, true) : window.MauiReportApiAccess, element, dialog, window, requests, copied, config };
}
const inactive = (revision = '') => ({ enabled: false, revision, path: null });
const prepared = (name, revision = '') => ({ enabled: false, revision, path: '/api/reports/' + name + '/csv', linkDraft: 'protected-' + name });
const active = (name, revision) => ({ enabled: true, revision, path: '/api/reports/' + name + '/csv' });
const flush = () => new Promise(resolve => setImmediate(resolve));

// Run the production save handler with a dispatched-event lifetime and a deferred response.
// The browser clears currentTarget after dispatch, before an awaited save can finish.
function reportSaveFixture(lineEnding) {
    const reportsSource = fs.readFileSync(path.join(__dirname, '../../wwwroot/js/reports.js'), 'utf8')
        .replace(/\r?\n/g, lineEnding).replace(/\r\n/g, '\n');
    const start = reportsSource.indexOf('    function isFiveMinuteTime(');
    const end = reportsSource.indexOf('    // ------------------------------------------------------------------\n    // Portable export/import', start);
    assert.ok(start > 0 && end > start);
    const controls = new Map();
    const control = key => {
        if (!controls.has(key)) controls.set(key, { disabled: false, value: '', checked: false, hidden: true, textContent: '' });
        return controls.get(key);
    };
    for (const [key, value] of Object.entries({ delivery: 'Recurring', frequency: 'Daily', time: '08:00', title: 'Original report', description: 'Description', recipients: 'team@example.test' }))
        control('[data-report-edit-' + key + ']').value = value;
    control('[data-report-edit-enabled]').checked = false;
    const save = control('[data-report-edit-save]');
    const close = control('[data-report-edit-close]');
    const apiToggle = control('[data-report-api-enabled]');
    const copy = control('[data-report-api-copy]'); copy.disabled = true;
    const error = control('[data-report-edit-error]');
    const inputs = [...controls.values()].filter(element => element !== error);
    const listeners = new Map(), pending = [], notices = [];
    let closes = 0, refreshes = 0;
    const dialog = {
        querySelector: control,
        querySelectorAll: selector => selector === 'input, select, textarea, button' ? inputs : [],
        addEventListener(name, listener) { listeners.set(name, listener); },
        removeEventListener(name, listener) { if (listeners.get(name) === listener) listeners.delete(name); },
        close() { closes++; }
    };
    const window = { MauiReportApiAccess: { ready: () => true, state: () => ({ enabled: true, revision: 'sharing-revision' }) } };
    vm.runInNewContext(`let editSaving = false; const activeReport = { id: 'report-id', schedule: { timeZoneId: 'UTC' } }; const editSelectedDates = [];
        ${reportsSource.slice(start, end)}
        window.save = saveEdit;`, { window, editDialog: dialog, urls: { update: '/MAUI/Reports/Update' },
        request: (url, body) => new Promise((resolve, reject) => pending.push({ url, body, resolve, reject })),
        toast: message => notices.push(message), loadReports: async () => { refreshes++; } });
    return { save, close, apiToggle, copy, error, inputs, pending, window, control,
        counts: () => ({ closes, refreshes }),
        cancel() { let prevented = false; listeners.get('cancel')?.({ preventDefault() { prevented = true; } }); return prevented; },
        send() { const event = { currentTarget: save }; const result = window.save(event); event.currentTarget = null; return result; } };
}

// Execute the production readiness renderer, including stale-state clearing on reload.
function checkEnvironmentReadiness() {
    const reports = fs.readFileSync(path.join(__dirname, '../../wwwroot/js/reports.js'), 'utf8').replace(/\r\n/g, '\n');
    const start = reports.indexOf('    function renderReadiness('), end = reports.indexOf('    async function loadReports(', start);
    assert.ok(start > 0 && end > start);
    const node = () => ({ hidden: true, textContent: '' });
    const context = { smtpWarning: node(), smtpWarningText: node(), dataAccessWarning: node(),
        dataAccessWarningTitle: node(), dataAccessWarningText: node(), readiness: node() };
    vm.runInNewContext(reports.slice(start, end), context);
    const render = state => context.renderReadiness({ smtpConfigured: true, ...state });
    render({ unconfiguredEnvironments: ['PROD'] });
    assert.equal(context.dataAccessWarning.hidden, false);
    assert.equal(context.dataAccessWarningTitle.textContent, 'Report environment no longer configured.');
    assert.match(context.dataAccessWarningText.textContent, /No MOVEit connections are configured for PROD/);
    assert.doesNotMatch(context.dataAccessWarningText.textContent, /Data Access Credentials/);
    assert.match(context.dataAccessWarningText.textContent, /reports and history are retained/);
    render({ missingDataAccessEnvironments: ['QA'], unconfiguredEnvironments: ['PROD'] });
    assert.equal(context.dataAccessWarningTitle.textContent, 'Report environment configuration needs attention.');
    assert.match(context.dataAccessWarningText.textContent, /Reports for QA/);
    assert.match(context.dataAccessWarningText.textContent, /configured for PROD/);
    render({ missingDataAccessEnvironments: ['QA'] });
    assert.equal(context.dataAccessWarningTitle.textContent, 'Data Access Credentials are missing.');
    assert.doesNotMatch(context.dataAccessWarningText.textContent, /PROD/);
    render({});
    assert.equal(context.dataAccessWarning.hidden, true);
    assert.equal(context.dataAccessWarningText.textContent, '');
    assert.equal(context.readiness.hidden, true);
    render({ smtpConfigured: false });
    assert.equal(context.dataAccessWarning.hidden, true);
    assert.equal(context.smtpWarning.hidden, false);
    assert.equal(context.readiness.hidden, false);
    const markup = fs.readFileSync(path.join(__dirname, '../../Views/Reports/Index.cshtml'), 'utf8');
    assert.match(markup, /<strong data-outbound-report-data-access-warning-title>/);
}
checkEnvironmentReadiness();

(async () => {
    // Reopening before the browser dispatches an earlier Close event must preserve
    // the new dialog's API state, including an in-flight link preparation.
    {
        const f = fixture('/MAUI/', true);
        await f.api.open();
        await f.api.open();
        f.dialog.open = true;
        f.element('[data-report-api-enabled]').checked = true;
        const preparing = f.element('[data-report-api-enabled]').change();
        f.dialog.close(); // Dispatch the queued event from the previous opening.
        assert.equal(f.api.ready(), false);
        f.requests[0].finish(prepared('reopened-report'));
        await preparing;
        assert.equal(f.api.ready(), true);
        assert.equal(f.api.state().linkDraft, 'protected-reopened-report');
        assert.equal(f.element('[data-report-submit]').disabled, false);
        f.dialog.open = false;
        f.dialog.close();
        assert.equal(f.api.state(), undefined, 'An actual close still clears API state');
    }
    for (const appBase of ['/', '/MAUI/']) {
        const f = fixture(appBase, true), { api, element, requests } = f;
        await api.open();
        assert.equal(requests.length, 0, 'Create does not require an existing Report');
        assert.equal(api.ready(), true);
        const enabled = element('[data-report-api-enabled]');
        enabled.checked = true;
        const preparing = enabled.change();
        assert.equal(api.ready(), false);
        assert.equal(element('[data-report-submit]').disabled, true);
        assert.equal(requests[0].url, appBase + 'OutboundReports/PrepareApiLink');
        assert.deepEqual(JSON.parse(requests[0].options.body), {});
        requests[0].finish(prepared('new-report'));
        await preparing;
        assert.equal(api.ready(), true);
        assert.match(element('[data-report-api-url]').value, /api\/reports\/new-report\/csv$/);
        await element('[data-report-api-copy]').click();
        assert.equal(f.copied.length, 1);
        const replacing = element('[data-report-api-regenerate]').click();
        requests[1].finish('Preparation unavailable', false);
        await replacing;
        assert.equal(api.state().linkDraft, 'protected-new-report', 'A failed replacement preserves the prior prepared URL');
        const retry = element('[data-report-api-regenerate]').click();
        requests[2].finish(prepared('replacement'));
        await retry;
        assert.equal(api.state().linkDraft, 'protected-replacement');
        f.dialog.close();
        await api.open();
        assert.equal(api.state().enabled, false);
        assert.equal(element('[data-report-api-url]').value, '');
    }
    {
        const f = reportSaveFixture('\n');
        f.control('[data-report-edit-delivery]').value = 'None';
        f.control('[data-report-edit-time]').value = 'invalid';
        f.control('[data-report-edit-recipients]').value = 'unused invalid address';
        const saving = f.send();
        assert.equal(f.pending.length, 1, 'Hidden schedule controls do not prevent a None save');
        assert.deepEqual(JSON.parse(JSON.stringify(f.pending[0].body.schedule)), { deliveryMode: 'None' });
        assert.equal(f.pending[0].body.recipients, '');
        assert.equal(f.pending[0].body.apiAccess.enabled, true);
        f.pending[0].resolve({ success: true });
        await saving;
    }
    for (const appBase of ['/', '/MAUI/']) {
        const f = fixture(appBase), { api, element, requests, copied } = f;
        const enabled = element('[data-report-api-enabled]'), save = element('[data-report-edit-save]');
        const copy = element('[data-report-api-copy]'), regenerate = element('[data-report-api-regenerate]');
        const status = element('[data-report-api-status]'), url = element('[data-report-api-url]');
        const opening = api.open({ id: 'report-id' });
        assert.equal(save.disabled, true);
        assert.equal(api.ready(), false);
        requests[0].finish(inactive());
        await opening;
        assert.equal(save.disabled, false);
        enabled.checked = true;
        const enabling = enabled.change();
        assert.equal(element('[data-report-api-link-fields]').hidden, false);
        assert.equal(save.disabled, true);
        assert.equal(element('[data-report-api-spinner]').hidden, false);
        assert.equal(requests[1].url, appBase + 'Reports/PrepareApiLink');
        assert.equal(requests[1].options.headers.RequestVerificationToken, 'antiforgery-token');
        assert.deepEqual(JSON.parse(requests[1].options.body), { id: 'report-id', revision: '' });
        requests[1].finish(prepared('preview-one'));
        await enabling;
        assert.equal(save.disabled, false);
        assert.equal(copy.disabled, false);
        assert.equal(regenerate.disabled, false);
        assert.equal(api.ready(), true);
        assert.equal(url.value, 'https://maui.example.test' + appBase + 'api/reports/preview-one/csv');
        assert.match(status.textContent, /Save Report to activate/);
        await copy.click();
        assert.equal(copied[0], url.value);
        assert.match(status.textContent, /Save Report/);
        assert.deepEqual(JSON.parse(JSON.stringify(api.state())), { enabled: true, revision: '', linkDraft: 'protected-preview-one' });
        const replacing = regenerate.click();
        assert.equal(save.disabled, true);
        assert.equal(requests[2].url, appBase + 'Reports/PrepareApiLink', 'unsaved replacement prepares a draft without rotating active state');
        requests[2].finish(prepared('preview-two'));
        await replacing;
        assert.match(url.value, /preview-two/);
        assert.equal(api.state().linkDraft, 'protected-preview-two', 'Save carries the exact URL already copied');
        enabled.checked = false;
        await enabled.change();
        assert.equal(element('[data-report-api-link-fields]').hidden, true);
        assert.equal(api.state().linkDraft, undefined, 'disabled saves discard the draft');
        enabled.checked = true;
        await enabled.change();
        assert.match(url.value, /preview-two/, 'off/on preserves the staged URL');
        assert.equal(requests.length, 3);
        f.dialog.close();
        assert.equal(api.state(), undefined, 'closing discards the draft');
        const reopening = api.open({ id: 'report-id' });
        requests[3].finish(inactive());
        await reopening;
        assert.equal(enabled.checked, false);
        assert.equal(url.value, '');
    }

    {
        const f = fixture(), { api, element, requests, copied } = f;
        const opening = api.open({ id: 'saved-report' });
        requests[0].finish(active('link-one', 'r1'));
        await opening;
        await element('[data-report-api-copy]').click();
        assert.equal(copied[0], 'https://maui.example.test/MAUI/api/reports/link-one/csv');
        assert.equal(element('[data-report-api-status]').textContent, 'Link copied.');
        f.window.mauiConfirm = async () => false;
        await element('[data-report-api-regenerate]').click();
        assert.equal(requests.length, 1, 'cancelled regeneration leaves the link alone');
        f.window.mauiConfirm = async () => true;
        const regenerating = element('[data-report-api-regenerate]').click();
        await flush();
        assert.equal(element('[data-report-edit-save]').disabled, true);
        assert.equal(requests[1].url, '/MAUI/Reports/RegenerateApiLink');
        assert.equal(requests[1].options.headers.RequestVerificationToken, 'antiforgery-token');
        assert.deepEqual(JSON.parse(requests[1].options.body), { id: 'saved-report', revision: 'r1' });
        requests[1].finish(active('link-two', 'r2'));
        await regenerating;
        assert.equal(api.state().revision, 'r2');
        assert.equal(api.state().linkDraft, undefined);
        assert.match(element('[data-report-api-url]').value, /link-two/);
    }

    {
        const f = fixture(), { api, element, requests } = f;
        const opening = api.open({ id: 'disabled-report' });
        requests[0].finish(inactive('disabled-revision'));
        await opening;
        const enabled = element('[data-report-api-enabled]');
        enabled.checked = true;
        const enabling = enabled.change();
        assert.equal(JSON.parse(requests[1].options.body).revision, 'disabled-revision');
        requests[1].finish('Unable to prepare link.', false);
        await enabling;
        assert.equal(api.ready(), false);
        assert.equal(element('[data-report-edit-save]').disabled, true);
        assert.equal(element('[data-report-api-regenerate]').disabled, false, 'retry remains available before Save');
        assert.equal(element('[data-report-api-status]').textContent, 'Unable to prepare link.');
        const retry = element('[data-report-api-regenerate]').click();
        requests[2].finish(prepared('retry', 'disabled-revision'));
        await retry;
        assert.equal(api.ready(), true);
        assert.equal(api.state().linkDraft, 'protected-retry');
        const failedReplacement = element('[data-report-api-regenerate]').click();
        requests[3].finish('Unable to replace link.', false);
        await failedReplacement;
        assert.equal(api.state().linkDraft, 'protected-retry', 'failed replacement preserves the valid prepared URL');
        assert.equal(element('[data-report-api-status]').textContent, 'Unable to replace link.');
    }

    {
        const f = fixture(), { api, element, requests } = f;
        let opening = api.open({ id: 'old' });
        requests[0].finish(inactive());
        await opening;
        element('[data-report-api-enabled]').checked = true;
        const obsoletePrepare = element('[data-report-api-enabled]').change();
        f.dialog.close();
        opening = api.open({ id: 'new' });
        requests[2].finish(active('new-link', 'new-revision'));
        await opening;
        requests[1].finish(prepared('obsolete-link'));
        await obsoletePrepare;
        assert.equal(api.state().revision, 'new-revision');
        assert.equal(api.state().linkDraft, undefined);
        assert.match(element('[data-report-api-url]').value, /new-link/);
        const obsolete = api.open({ id: 'obsolete' });
        opening = api.open({ id: 'current' });
        requests[4].finish(inactive('current'));
        await opening;
        requests[3].finish(active('wrong', 'obsolete'));
        await obsolete;
        assert.equal(api.state().revision, 'current');
        opening = api.open({ id: 'failure' });
        requests[5].finish('Unable to load sharing.', false);
        await opening;
        assert.equal(element('[data-report-edit-save]').disabled, true);
        assert.equal(api.ready(), false);
        assert.equal(element('[data-report-api-status]').textContent, 'Unable to load sharing.');
    }
    for (const lineEnding of ['\n', '\r\n']) {
        const f = reportSaveFixture(lineEnding);
        const first = f.send();
        f.pending[0].reject(new Error('Report validation failed.'));
        await assert.doesNotReject(first, 'The save handler must not dereference a cleared event.currentTarget');
        assert.equal(f.save.disabled, false, 'Failed Save can be retried');
        assert.equal(f.error.hidden, false);
        assert.equal(f.error.textContent, 'Report validation failed.');
        assert.equal(f.control('[data-report-edit-title]').value, 'Original report');
        assert.equal(f.copy.disabled, true, 'Previously disabled API controls stay disabled');
        assert.equal(f.cancel(), false, 'The save-only close guard is removed after failure');

        const retry = f.send();
        assert.ok(f.inputs.every(input => input.disabled), 'Report fields, API controls and Close are locked while saving');
        assert.equal(f.cancel(), true, 'Escape cannot dismiss an outstanding save');
        await f.send();
        assert.equal(f.pending.length, 2, 'A second click cannot submit a duplicate save');
        assert.equal(f.pending[1].url, '/MAUI/Reports/Update');
        assert.equal(f.pending[1].body.apiAccess.revision, 'sharing-revision');
        f.pending[1].resolve({ success: true });
        await assert.doesNotReject(retry);
        assert.deepEqual(f.counts(), { closes: 1, refreshes: 1 });
        assert.equal(f.save.disabled, false);
        assert.equal(f.close.disabled, false);
        assert.equal(f.apiToggle.disabled, false);
        assert.equal(f.copy.disabled, true);
        assert.equal(f.cancel(), false);
    }
    console.log('PASS Report environment readiness, API link preview, draft activation, active-link rotation, stale-response safety and Report Save failure/retry, dispatched-event lifetime, pending-control locks and duplicate-submit protection.');
})().catch(error => { console.error(error); process.exitCode = 1; });
