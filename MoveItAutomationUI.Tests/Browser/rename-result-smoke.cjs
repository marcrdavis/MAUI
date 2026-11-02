'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const context = { window: {} }; vm.createContext(context);
vm.runInContext(fs.readFileSync(path.resolve(__dirname, '../../wwwroot/js/shared/rename-result.js'), 'utf8'), context);
const policy = context.window.MauiRenameResult;
let checks = 0;
for (const kind of ['Task', 'Host']) {
    const key = kind.toLowerCase(), valid = { success: true, [key + 'Id']: '42', [key + 'Name']: 'NEW_NAME' };
    policy.requireConfirmation(valid, kind, '42', 'NEW_NAME'); checks++;
    for (const result of [null, {}, { success: true }, { ...valid, success: false }, { ...valid, [key + 'Id']: '99' },
        { ...valid, [key + 'Id']: 42 }, { ...valid, [key + 'Name']: 'OTHER_NAME' }]) {
        assert.throws(() => policy.requireConfirmation(result, kind, '42', 'NEW_NAME'), error => error.payload.outcomeUnknown === true);
        checks++;
    }
    assert.ok(policy.failureMessage(new TypeError('Lost response'), kind).includes('Close this dialog and refresh the ' + kind + 's grid'));
    checks++;
}
for (const error of [new TypeError('Lost response'), new SyntaxError('Invalid JSON'), new Error('Lost acknowledgement'),
    { status: 0 }, { status: 200 }, { status: 408 }, { status: 500 }, { status: 502 }, { status: 503 }, { status: 409, payload: { editConflict: true } },
    { status: 500, payload: { moveItSaved: true } }, { status: 409, payload: { outcomeUnknown: true } }]) {
    assert.equal(policy.requiresReload(error), true); checks++;
}
for (const error of [{ status: 400 }, { status: 401 }, { status: 403 }, { status: 409 },
    { status: 500, payload: { moveItSaved: false, outcomeUnknown: false } }]) {
    assert.equal(policy.requiresReload(error), false); checks++;
}
console.log('PASS shared rename acknowledgement and failure policy: ' + checks + ' checks');
