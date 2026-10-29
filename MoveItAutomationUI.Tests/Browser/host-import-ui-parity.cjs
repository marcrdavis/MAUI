'use strict';
// Browser-flow regression for Host CSV/XLSX import and parity with Task Import.
// Run: node MoveItAutomationUI.Tests/Browser/host-import-ui-parity.cjs
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'wwwroot/js/host-spreadsheet-import.js'), 'utf8');
const modal = fs.readFileSync(path.join(root, 'Views/Shared/_NativePortabilityDialogs.cshtml'), 'utf8');
const panelMarkup = fs.readFileSync(path.join(root, 'Views/Shared/_HostSpreadsheetImportPanel.cshtml'), 'utf8');

assert.match(modal, /data-native-portability-import-error/);
assert.match(modal, /data-host-spreadsheet-action/);
assert.match(panelMarkup, /task-spreadsheet-grid/);
assert.match(panelMarkup, /task-spreadsheet-summary/);
assert.match(panelMarkup, /task-spreadsheet-results-panel/);
assert.match(panelMarkup, /data-host-spreadsheet-update-existing/);
assert.match(js, /data.append\('updateExisting', String\(updateExisting.checked\)\)/);
assert.match(js, /task-spreadsheet-summary-pill/);
assert.match(js, /\['Row', 'Status', \.\.\.columns\]/);
assert.match(js, /Import Complete/);
assert.match(js, /Import Progress:/);

class Element {
    constructor(tag = 'div') {
        this.tag = tag; this.children = []; this.dataset = {}; this.listeners = {};
        this.hidden = false; this.disabled = false; this.checked = false;
        this.value = ''; this.files = []; this.textContent = ''; this.style = {};
        this.scrollTop = 0; this.scrollHeight = 0;
        this.classList = { toggle() {} };
    }
    appendChild(child) {
        if (child.tag === '#fragment') this.children.push(...child.children);
        else this.children.push(child);
        return child;
    }
    append(...children) { children.forEach(child => this.appendChild(child)); }
    replaceChildren(...children) {
        this.children = []; this.textContent = ''; this.append(...children);
    }
    addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); }
    emit(type) { return Promise.all((this.listeners[type] || []).map(fn => fn({ preventDefault() {} }))); }
    querySelector() { return null; }
    querySelectorAll() { return []; }
}
class FormDataStub {
    constructor() { this.fields = new Map(); }
    append(key, value) { this.fields.set(key, value); }
}

async function run() {
    const dialog = new Element('dialog');
    const panel = new Element();
    const nativePanel = new Element();
    const nativeAction = new Element();
    const error = new Element();
    const portability = new Element('input');
    const spreadsheet = new Element('input');
    portability.value = 'portability'; portability.checked = true;
    spreadsheet.value = 'spreadsheet';

    const fields = {};
    for (const name of [
        'file', 'prefix', 'action', 'analysis', 'summary', 'head', 'body',
        'pagination', 'page-label', 'prev', 'next', 'results', 'results-panel',
        'progress', 'progress-text', 'progress-fill', 'test-after', 'update-existing'
    ]) fields[name] = new Element();

    dialog.querySelector = selector => {
        if (selector === '[data-native-portability-import-panel]') return nativePanel;
        if (selector === '[data-native-portability-import-action]') return nativeAction;
        if (selector === '[data-native-portability-import-error]') return error;
        if (selector === '[data-host-spreadsheet-import-panel]') return panel;
        if (selector === 'input[name="hostImportMode"]:checked')
            return spreadsheet.checked ? spreadsheet : portability;
        if (selector === 'input[name="hostImportMode"][value="portability"]') return portability;
        const match = selector.match(/^\[data-host-spreadsheet-(.+)\]$/);
        return match ? fields[match[1]] : null;
    };
    dialog.querySelectorAll = selector => selector === 'input[name="hostImportMode"]'
        ? [portability, spreadsheet] : [];
    panel.querySelector = selector => selector === '.task-spreadsheet-grid-frame' ? new Element() : null;

    const rows = [
        { rowNumber: 2, status: 'Valid', values: { NAME: 'NewSFTP', PASSWORD: '******' } },
        { rowNumber: 3, status: 'Skipped', values: { NAME: 'Existing', PASSWORD: '' } },
        { rowNumber: 4, status: 'Error', values: { NAME: 'Invalid', PASSWORD: '' } }
    ];
    const analyzed = {
        analysisId: 'analysis-1', columns: ['NAME', 'PASSWORD'], rows,
        validRows: 1, skippedRows: 1, errorRows: 1,
        issues: [
            { rowNumber: 3, column: 'NAME', severity: 'Warning', message: 'Already exists' },
            { rowNumber: 4, column: 'HOST', severity: 'Error', message: 'Invalid Host' }
        ]
    };
    const preview = { columns: analyzed.columns, rows: rows.map(row => ({ ...row, status: 'Pending' })) };
    let nextOutcome = 'Created';
    let lastAnalysisForm = null;
    let refreshes = 0;
    let importRequests = 0;
    const fetchStub = async (url, options) => {
        if (url === '/analyze') lastAnalysisForm = options.body;
        if (url === '/import') {
            importRequests++;
            if (nextOutcome === 'LostResponse') throw new Error('Response lost after server commit');
            if (nextOutcome === 'MalformedResponse') return { ok: true, status: 200, json: async () => ({ result: { rowNumber: 99 } }) };
        }
        return ({ 
        ok: true, status: 200,
        json: async () => ({
            result: url === '/preview' ? preview
                : url === '/analyze' ? JSON.parse(JSON.stringify(analyzed))
                : url === '/import' ? {
                    rowNumber: 2, name: 'NewSFTP', outcome: nextOutcome, message: nextOutcome
                } : {}
        })
    });
    };
    const window = {};
    const document = {
        querySelector: selector => selector === '[data-native-portability-import-dialog]' ? dialog : null,
        createElement: tag => new Element(tag),
        createDocumentFragment: () => new Element('#fragment')
    };
    const options = {
        nativePortability: { openImport() {} }, rowLimit: 500,
        previewUrl: '/preview', analyzeUrl: '/analyze', importUrl: '/import',
        finishUrl: '/finish', antiForgeryToken: 'token', loginUrl: '/login',
        refresh: async () => { refreshes++; }
    };
    vm.runInNewContext(js, { document, window, fetch: fetchStub, FormData: FormDataStub, console: { error() {} } });
    const instance = window.MauiHostSpreadsheetImport.create(options);
    assert.ok(instance, 'Host Import initializes when shared Task-style markup is complete');
    assert.equal(panel.hidden, true);

    portability.checked = false;
    spreadsheet.checked = true;
    await spreadsheet.emit('change');
    fields.file.files = [{ name: 'hosts.xlsx' }];
    await fields.file.emit('change');
    assert.match(fields.summary.children[0].textContent, /File loaded: 3 data rows/);
    assert.equal(fields.head.children[0].children[0].textContent, 'Row');
    assert.equal(fields.head.children[0].children[1].textContent, 'Status');
    assert.equal(fields.body.children[0].children[0].textContent, '2');
    assert.equal(fields.body.children[0].children[1].children[0].textContent, 'Not analyzed');

    await fields.action.emit('click');
    for (let i = 0; i < 12; i++) await Promise.resolve();
    const counters = () => fields.summary.children.map(child => child.textContent);
    assert.deepEqual(counters(), ['Valid: 1', 'Warnings: 1', 'Errors: 1', 'Skipped: 1']);
    assert.equal(fields.action.textContent, 'Import Hosts');
    assert.equal(lastAnalysisForm.fields.get('updateExisting'), 'false');
    assert.equal(fields.results.children[0].children[0].textContent, 'Row 3 · NAME');

    await fields.action.emit('click');
    for (let i = 0; i < 20; i++) await Promise.resolve();
    assert.equal(fields.action.textContent, 'Import Complete');
    assert.equal(fields.action.disabled, true);
    assert.equal(fields['progress-text'].textContent, 'Import Progress: 1 of 1');
    assert.ok(counters().includes('Created: 1'));
    assert.equal(fields.body.children[0].children[1].children[0].textContent, 'Created');
    assert.equal(fields.results.children[2].children[0].textContent, 'Created');

    portability.checked = true;
    spreadsheet.checked = false;
    await portability.emit('change');
    assert.equal(panel.hidden, true);
    assert.equal(nativePanel.hidden, false);
    assert.equal(fields.action.hidden, true);

    // Starting a new import must reset the completed state without carrying old results.
    spreadsheet.checked = true;
    portability.checked = false;
    await spreadsheet.emit('change');
    fields.file.files = [{ name: 'hosts-recheck.xlsx' }];
    await fields.file.emit('change');
    await fields.action.emit('click');
    for (let i = 0; i < 12; i++) await Promise.resolve();
    nextOutcome = 'Needs Attention';
    await fields.action.emit('click');
    for (let i = 0; i < 20; i++) await Promise.resolve();
    assert.equal(fields.action.textContent, 'Import Finished (review needed)');
    assert.ok(counters().includes('Needs Attention: 1'));
    assert.equal(fields.action.disabled, true);

    await fields.file.emit('change');
    await fields.action.emit('click');
    for (let i = 0; i < 12; i++) await Promise.resolve();
    nextOutcome = 'Failed';
    await fields.action.emit('click');
    for (let i = 0; i < 20; i++) await Promise.resolve();
    assert.equal(fields.action.textContent, 'Import Finished (with failures)');
    assert.ok(counters().includes('Failed: 1'));
    assert.equal(fields.action.disabled, true);

    // Task-style update checkbox: analysis must honor it and an Updated result
    // must update the same pills, outcome state, progress and grid refresh.
    await fields.file.emit('change');
    fields['update-existing'].checked = true;
    await fields['update-existing'].emit('change');
    await fields.action.emit('click');
    for (let i = 0; i < 12; i++) await Promise.resolve();
    assert.equal(lastAnalysisForm.fields.get('updateExisting'), 'true');
    nextOutcome = 'Updated';
    await fields.action.emit('click');
    for (let i = 0; i < 20; i++) await Promise.resolve();
    assert.equal(fields.action.textContent, 'Import Complete');
    assert.ok(counters().includes('Updated: 1'));
    assert.equal(fields.body.children[0].children[1].children[0].textContent, 'Updated');
    assert.equal(fields.results.children[2].children[0].textContent, 'Updated');

    // A response can be lost after COM commits. Do not label that as a definite failure,
    // retry the write, or leave the Hosts grid stale. Invalid response identities are uncertain too.
    for (const failure of ['LostResponse', 'MalformedResponse']) {
        await fields.file.emit('change');
        await fields.action.emit('click');
        for (let i = 0; i < 12; i++) await Promise.resolve();
        const oldRefreshes = refreshes, oldRequests = importRequests;
        nextOutcome = failure;
        await fields.action.emit('click');
        for (let i = 0; i < 20; i++) await Promise.resolve();
        assert.ok(counters().includes('Needs Attention: 1'));
        assert.ok(!counters().includes('Failed: 1'));
        assert.equal(fields.action.textContent, 'Import Finished (review needed)');
        assert.equal(fields.action.disabled, true);
        assert.equal(importRequests, oldRequests + 1, 'An uncertain write is never automatically retried');
        assert.equal(refreshes, oldRefreshes + 1, 'Refresh inventory after an uncertain Host mutation');
    }

    // A removed/misnamed control disables only Host Import, not the All Hosts page.
    const query = dialog.querySelector;
    dialog.querySelector = selector => selector === '[data-native-portability-import-error]'
        ? null : query(selector);
    assert.equal(window.MauiHostSpreadsheetImport.create(options), null);
}
run().then(() => console.log('Host Import UI parity browser regression passed.'))
    .catch(err => { console.error(err); process.exitCode = 1; });

