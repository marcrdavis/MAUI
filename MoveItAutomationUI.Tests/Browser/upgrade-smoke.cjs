'use strict';
// Exercise the production upgrade stream and restart action without restarting a real host.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../wwwroot/js/upgrade.js'), 'utf8');
function environment({ restartRequired = false, pendingRestart = false, rejectRestart = false, timeout = false } = {}) {
    const calls = [], navigations = [];
    const node = () => ({ hidden: false, disabled: false, textContent: '', listeners: {},
        addEventListener(type, fn) { this.listeners[type] = fn; },
        replaceChildren() {}, append() {} });
    const names = ['begin', 'retry', 'restart', 'return', 'ready', 'running', 'progress', 'success', 'success-version', 'summary', 'error', 'error-message'];
    const nodes = Object.fromEntries(names.map(name => [name, node()]));
    nodes.restart.hidden = !pendingRestart;
    nodes.return.hidden = true;
    const message = node();
    const page = {
        dataset: { runUrl: '/maui/Upgrade/Run', restartUrl: '/maui/Upgrade/Restart', restartStatusUrl: '/maui/Upgrade/RestartStatus', loginUrl: '/maui/Account/Login' },
        querySelector(selector) {
            if (selector.includes('__RequestVerificationToken')) return { value: 'token' };
            return nodes[selector.replace('[data-upgrade-', '').replace(']', '')] || null;
        },
        querySelectorAll() { return [message]; }
    };
    let restartAttempts = 0;
    const fetch = async (url, options) => {
        calls.push({ url, options });
        if (url === page.dataset.runUrl) {
            const payload = JSON.stringify({ type: 'success', restartRequired, toVersion: '0.17.387', fromVersion: '0.17.386', schemaVersion: 1, configurationVersion: 1 }) + '\n';
            let read = false;
            return { ok: true, body: { getReader: () => ({ read: async () => {
                if (read) return { done: true }; read = true;
                return { done: false, value: new TextEncoder().encode(payload) };
            } }) } };
        }
        if (url === page.dataset.restartUrl) {
            restartAttempts++;
            return rejectRestart && restartAttempts === 1
                ? { ok: false, status: 503, text: async () => JSON.stringify({ message: 'Could not schedule restart.' }) }
                : { ok: true, status: 200 };
        }
        if (url === page.dataset.restartStatusUrl) return { ok: true, json: async () => ({ ready: !timeout }) };
        throw new Error('Unexpected URL ' + url);
    };
    const window = { mauiAppUrl: value => '/maui' + value, location: { replace: url => navigations.push(url) }, setTimeout: fn => { fn(); } };
    vm.runInNewContext(source, { window, document: { querySelector: () => page, createElement: node, createTextNode: node }, fetch, TextDecoder, Uint8Array, console });
    // Click listeners return immediately for restart; allow its promise chain to settle.
    const click = async name => { await nodes[name].listeners.click(); await new Promise(setImmediate); };
    return { nodes, message, calls, navigations, click };
}
(async () => {
    const normal = environment();
    await normal.click('begin');
    assert.equal(normal.nodes.return.hidden, false);
    assert.equal(normal.nodes.restart.hidden, true);
    assert.equal(normal.calls.length, 1);
    await normal.click('return');
    assert.deepEqual(normal.navigations, ['/maui/Account/Login']);

    const required = environment({ restartRequired: true });
    await required.click('begin');
    assert.equal(required.nodes.return.hidden, true);
    assert.equal(required.nodes.restart.hidden, false);
    assert.equal(required.calls.length, 1, 'Completion must wait for the administrator to click Restart MAUI');
    await required.click('restart');
    assert.equal(required.calls[1].options.method, 'POST');
    assert.equal(required.calls[1].options.headers.RequestVerificationToken, 'token');
    assert.equal(required.calls[2].options.cache, 'no-store');
    assert.deepEqual(required.navigations, ['/maui/Account/Login']);

    const retry = environment({ pendingRestart: true, rejectRestart: true });
    await retry.click('restart');
    assert.equal(retry.message.textContent, 'Could not schedule restart.');
    assert.equal(retry.nodes.restart.disabled, false);
    assert.equal(retry.nodes.restart.hidden, false);
    assert.deepEqual(retry.navigations, []);
    await retry.click('restart');
    assert.deepEqual(retry.navigations, ['/maui/Account/Login']);

    const timedOut = environment({ pendingRestart: true, timeout: true });
    await timedOut.click('restart');
    assert.equal(timedOut.nodes.restart.hidden, false);
    assert.equal(timedOut.nodes.restart.disabled, false);
    assert.match(timedOut.message.textContent, /did not become reachable/);
    assert.deepEqual(timedOut.navigations, []);
    console.log('Upgrade completion and restart workflow passed (return, explicit restart, refreshed page, retry, timeout).');
})().catch(error => { console.error(error); process.exitCode = 1; });
