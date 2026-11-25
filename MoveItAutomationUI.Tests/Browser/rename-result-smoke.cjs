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
const cleanupSource = fs.readFileSync(path.join(__dirname, 'rename-browser-cleanup.cjs'), 'utf8');
function cleanupCase(platform, result, exists, killResult = true, exitCode = null) {
    const errors = [], processState = { platform, kill() { if (!exists) throw Object.assign(new Error('Process exited'), { code: 'ESRCH' }); } };
    let stops = 0, unreferenced = false;
    const module = { exports: {} };
    vm.runInNewContext(cleanupSource, { module, require: () => ({ spawnSync() { stops++; return result; } }),
        process: processState, console: { error: (...args) => errors.push(args.join(' ')) } });
    module.exports({ pid: 12345, exitCode, kill() { return killResult; }, unref() { unreferenced = true; } });
    return { exitCode: processState.exitCode, errors, stops, unreferenced };
}
for (const platform of ['win32', 'linux']) {
    const exited = cleanupCase(platform, { status: 128 }, false, false);
    assert.equal(exited.exitCode, undefined); assert.equal(exited.errors.length, 0); checks++;
    const alive = cleanupCase(platform, { status: 1, stderr: 'Fixture stop failure' }, true, false);
    assert.equal(alive.exitCode, 1); assert.equal(alive.errors.length, 1); checks++;
    const success = cleanupCase(platform, { status: 0 }, true);
    assert.equal(success.exitCode, undefined); assert.equal(success.unreferenced, true); checks++;
}
const timeout = cleanupCase('win32', { status: null, error: { code: 'ETIMEDOUT' } }, true);
assert.equal(timeout.exitCode, 1); assert.match(timeout.errors[0], /ETIMEDOUT/); checks++;
const alreadyStopped = cleanupCase('win32', { status: 1 }, false, false, 0);
assert.equal(alreadyStopped.stops, 0); assert.equal(alreadyStopped.exitCode, undefined); checks++;
console.log('PASS shared rename acknowledgement, failure policy and verified browser cleanup: ' + checks + ' checks');
