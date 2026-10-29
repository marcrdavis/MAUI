// Dependency-free Settings initialization and dirty-state regression test.
// Exercises the actual configuration.js implementation without requiring a .NET server.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../wwwroot/js/configuration.js'), 'utf8');

function classList() {
    const values = new Set();
    return { toggle(name, value) { value ? values.add(name) : values.delete(name); },
        add(name) { values.add(name); }, remove(name) { values.delete(name); },
        contains(name) { return values.has(name); } };
}

function runSettingsScenario(hiddenEditorControls) {
    const controls = new Map();
    function add(name, type, value, checked) {
        const control = { type, value: String(value), checked: Boolean(checked),
            listeners: {}, addEventListener(event, callback) { this.listeners[event] = callback; } };
        controls.set(name, control);
        return control;
    }
    add('enableQuickTransfer', 'checkbox', '', false);
    add('autoFormatNewTaskHostNames', 'checkbox', '', true);
    add('useMauiTaskEditor', hiddenEditorControls ? 'hidden' : 'checkbox', 'true', true);
    add('useMauiHostEditor', hiddenEditorControls ? 'hidden' : 'checkbox', 'false', false);
    add('importRowLimit', 'number', '500');
    add('requestTimeoutSeconds', 'number', '30');
    const saveButton = { disabled: true, listeners: {}, addEventListener(name, fn) { this.listeners[name] = fn; } };
    const discardButton = { disabled: true, listeners: {}, addEventListener(name, fn) { this.listeners[name] = fn; } };
    const saveBar = { classList: classList() };
    const saveState = { textContent: 'No unsaved changes' };
    const handlers = {};
    const workspace = {
        dataset: { saveUrl: '/Configuration/SaveSettings' },
        addEventListener(name, callback) { handlers[name] = callback; },
        querySelector(selector) {
            const settingName = /^\[data-setting="([^"]+)"\]$/.exec(selector)?.[1];
            if (settingName) return controls.get(settingName) || null;
            return { '[data-config-save]': saveButton, '[data-config-discard]': discardButton,
                '.configuration-save-bar': saveBar, '[data-configuration-save-state]': saveState }[selector] || null;
        },
        querySelectorAll() { return []; }
    };
    const document = {
        querySelector(selector) { return selector === '[data-configuration-workspace]' ? workspace : null; },
        querySelectorAll() { return []; }
    };
    const windowHandlers = {};
    const window = { setTimeout() { return 1; }, clearTimeout() {}, location: { reload() {} },
        addEventListener(name, listener) { windowHandlers[name] = listener; } };
    assert.doesNotThrow(() => vm.runInNewContext(source, { window, document, URL, console }),
        'Settings must initialize without undefined helpers or missing-control failures');
    assert.equal(saveButton.disabled, true, 'Save must initially be disabled');
    assert.equal(discardButton.disabled, true, 'Discard must initially be disabled');
    assert.ok(handlers.input && handlers.change, 'Dirty-state listeners must be registered');
    assert.equal(typeof saveButton.listeners.click, 'function', 'Save click listener must exist');
    const revisionListener = windowHandlers['maui:configuration-revision-updated'];
    assert.equal(typeof revisionListener, 'function', 'Settings should accept independent persisted revision updates');
    revisionListener({ detail: { revision: 'persisted-after-credentials' } });
    assert.equal(workspace.dataset.configurationRevision, 'persisted-after-credentials');
    assert.equal(saveButton.disabled, true, 'Credential-only saves must not mark an untouched Settings form dirty');

    const target = { closest() { return null; }, matches() { return false; } };
    function change(control, property, value, event) {
        control[property] = value;
        handlers[event]({ target });
    }
    const quick = controls.get('enableQuickTransfer');
    change(quick, 'checked', true, 'change');
    assert.equal(saveButton.disabled, false, 'Changing a toggle must enable Save');
    assert.equal(saveState.textContent, 'Unsaved changes');
    revisionListener({ detail: { revision: 'persisted-while-form-dirty' } });
    assert.equal(workspace.dataset.configurationRevision, 'persisted-while-form-dirty');
    assert.equal(saveButton.disabled, false, 'Independent revisions must not discard unsaved user edits');
    change(quick, 'checked', false, 'change');
    assert.equal(saveButton.disabled, true, 'Reverting a toggle must clear dirty state');
    change(controls.get('importRowLimit'), 'value', '501', 'input');
    assert.equal(saveButton.disabled, false, 'Changing a numeric value must enable Save');
    change(controls.get('importRowLimit'), 'value', '500', 'input');
    assert.equal(saveButton.disabled, true, 'Reverting a numeric value must clear dirty state');
    const editor = controls.get('useMauiTaskEditor');
    if (hiddenEditorControls) change(editor, 'value', 'false', 'change');
    else change(editor, 'checked', false, 'change');
    assert.equal(saveButton.disabled, false, 'Editor Boolean controls must participate in the signature');
}

runSettingsScenario(false);
runSettingsScenario(true);
assert.doesNotMatch(source, /\bcheckboxValue\(/, 'Undefined checkboxValue helper must not return');
console.log('PASS Settings initialization, revision sync, preserve-dirty-state, visible/hidden editor values, toggle/numeric dirty tracking and reversion.');
