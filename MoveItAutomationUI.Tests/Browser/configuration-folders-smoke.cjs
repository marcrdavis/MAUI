'use strict';
// Run the production Configuration action without touching a live MOVEit server or database.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../wwwroot/js/configuration.js'), 'utf8');
const start = source.indexOf('    async function createResourceGroupFolders() {');
const end = source.indexOf('    // Update this already-rendered link', start);
assert.ok(start > 0 && end > start);
function fixture({ disabled = false, reply } = {}) {
    const calls = [], toasts = [], attributes = new Map();
    const button = { disabled, textContent: 'Create Folders',
        setAttribute: (key, value) => attributes.set(key, value), removeAttribute: key => attributes.delete(key) };
    const action = vm.runInNewContext(source.slice(start, end) + '\ncreateResourceGroupFolders', {
        workspace: { dataset: { createResourceGroupFoldersUrl: '/maui/Configuration/CreateResourceGroupFolders' },
            querySelector: () => button }, antiForgeryToken: 'csrf-token', Headers,
        fetch: async (url, options) => { calls.push({ url, options }); return reply(); },
        showToast: (message, kind = 'default') => toasts.push({ message, kind })
    });
    return { action, calls, toasts, button, attributes };
}
(async () => {
    let resolve;
    const pending = new Promise(done => { resolve = done; });
    const success = fixture({ reply: () => pending });
    const first = success.action();
    assert.equal(success.button.disabled, true);
    assert.equal(success.button.textContent, 'Creating…');
    assert.equal(success.attributes.get('aria-busy'), 'true');
    await success.action();
    assert.equal(success.calls.length, 1, 'Busy clicks cannot enqueue another operation');
    assert.equal(success.calls[0].url, '/maui/Configuration/CreateResourceGroupFolders');
    assert.equal(success.calls[0].options.method, 'POST');
    assert.equal(success.calls[0].options.credentials, 'same-origin');
    assert.equal(success.calls[0].options.headers.get('RequestVerificationToken'), 'csrf-token');
    assert.equal(success.calls[0].options.body, '{}');
    resolve({ ok: true, json: async () => ({ success: true, message: 'Created 2 Task folders.' }) });
    await first;
    assert.deepEqual(success.toasts, [{ message: 'Created 2 Task folders.', kind: 'default' }]);
    assert.equal(success.button.disabled, false);
    assert.equal(success.button.textContent, 'Create Folders');
    assert.equal(success.attributes.has('aria-busy'), false);
    const denied = fixture({ disabled: true, reply: () => { throw new Error('No request expected'); } });
    await denied.action();
    assert.equal(denied.calls.length, 0);
    assert.equal(denied.button.disabled, true);
    for (const reply of [
        () => ({ ok: false, json: async () => ({ message: 'Folder creation failed.' }) }),
        () => ({ ok: true, json: async () => { throw new Error('invalid JSON'); } }),
        () => { throw new Error('Network failure.'); }
    ]) {
        const failed = fixture({ reply });
        await failed.action();
        assert.equal(failed.toasts.length, 1);
        assert.equal(failed.toasts[0].kind, 'error');
        assert.equal(failed.button.disabled, false);
        assert.equal(failed.button.textContent, 'Create Folders');
        await failed.action();
        assert.equal(failed.calls.length, 2, 'Failures leave the action available for retry');
    }
    console.log('PASS Configuration Resource Group folder action: antiforgery/virtual path, busy/permission guards, toast outcomes and retry.');
})().catch(error => { console.error(error); process.exitCode = 1; });
