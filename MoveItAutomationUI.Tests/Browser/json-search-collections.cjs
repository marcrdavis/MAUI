'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const test = require('node:test');
const root = path.resolve(__dirname, '../..'), context = { window: {} };
vm.runInNewContext(fs.readFileSync(path.join(root, 'wwwroot/js/shared/json-search.js'), 'utf8'), context);
const model = context.window.MauiJsonSearch;
function project(value, segments = [], output = []) {
    if (value !== null && typeof value === 'object') {
        for (const [name, child] of Object.entries(value)) project(child, [...segments, name], output);
    } else if (segments.length) output.push({ path: segments, pathText: model.pathText(segments), name: segments.at(-1), value, readOnly: value === null });
    return output;
}
const email = flag => ({ Type: 'Email', DoIfFailure: flag, DoIfSuccess: 1 });
const runTask = flag => ({ Type: 'Task', DoIfFailure: flag });
const task = actions => ({ Active: 1, NextActions: { NextAction: actions } });
const missingEmail = 'NONE(NextActions.NextAction[*], Type=Email AND DoIfFailure=1)';
const hasEmail = 'ANY(NextActions.NextAction[*], Type=Email AND DoIfFailure=1)';
const matches = (query, definition) => model.compileTaskSearch(query)(project(definition));

for (const [label, definition, expected] of [
    ['no NextActions property', { Active: 1 }, true],
    ['empty NextActions object', { NextActions: {} }, true],
    ['empty next-action array', task([]), true],
    ['success email only', task([email(0)]), true],
    ['success and failure emails', task([email(0), email(1)]), false],
    ['failure email only', task([email(1)]), false],
    ['success email and failure Run Task', task([email(0), runTask(1)]), true],
    ['failure Run Task only', task([runTask(1)]), true],
    ['Boolean failure flag', task([email(true)]), false],
    ['string failure flag', task([email('1')]), false],
    ['single-object success email', task(email(0)), true],
    ['single-object failure email', task(email(1)), false],
    ['email without a failure flag', task([{ Type: 'Email' }]), true],
    ['null failure flag', task([email(null)]), true]
]) test(label, () => {
    assert.equal(matches(missingEmail, definition), expected);
    assert.equal(matches(hasEmail, definition), !expected);
});

test('existing comparisons retain existential and cross-action AND behavior', () => {
    const mixed = task([email(0), email(1)]);
    assert.equal(matches('DoIfFailure=0', mixed), true);
    assert.equal(matches('DoIfFailure!=1', mixed), true);
    assert.equal(matches('NextActions.NextAction[*].Type=Email AND NextActions.NextAction[*].DoIfFailure=1', task([email(0), runTask(1)])), true);
    assert.equal(matches(hasEmail, task([email(0), runTask(1)])), false);
    assert.equal(matches('DoIfFailure!=1', task([])), false);
});
test('absence of any failure action includes empty tasks and excludes failure Run Task actions', () => {
    const query = 'NONE(NextActions.NextAction[*], DoIfFailure=1)';
    assert.equal(matches(query, task([])), true);
    assert.equal(matches(query, task([email(0)])), true);
    assert.equal(matches(query, task([runTask(1)])), false);
});
test('root AND/OR retain precedence when combined with collection conditions', () => {
    const enabled = task([email(0)]), disabled = { ...enabled, Active: 0 };
    assert.equal(matches('Active=1 AND ' + missingEmail, enabled), true);
    assert.equal(matches('Active=1 AND ' + missingEmail, disabled), false);
    assert.equal(matches(hasEmail + ' OR Active=0 AND ' + missingEmail, disabled), true);
    assert.equal(matches(hasEmail + ' OR Active=0 AND ' + missingEmail, enabled), false);
    assert.equal(matches(hasEmail + ' OR Active=0 AND ' + missingEmail, task([email(1)])), true);
    assert.equal(matches(missingEmail + ' AND NONE(NextActions.NextAction[*], Type=Task)', task([email(0)])), true);
    assert.equal(matches(missingEmail + ' AND NONE(NextActions.NextAction[*], Type=Task)', task([email(0), runTask(0)])), false);
});
test('item-local OR and AND use the same action and preserve precedence', () => {
    const query = 'ANY(NextActions.NextAction[*], Type=Email AND DoIfFailure=1 OR Type=Task AND DoIfFailure=0)';
    assert.equal(matches(query, task([email(0), runTask(1)])), false);
    assert.equal(matches(query, task([email(1), runTask(1)])), true);
    assert.equal(matches(query, task([email(0), runTask(0)])), true);
});
test('case, explicit Task root, exact indexes, implicit wildcard, and singleton normalization', () => {
    assert.equal(matches('nOnE( task.nextactions.nextaction[*] , type=email and doiffailure=1 )', task([email(0), email(1)])), false);
    assert.equal(matches('ANY(NextActions.NextAction[1], Type=Email AND DoIfFailure=1)', task([email(0), email(1)])), true);
    assert.equal(matches('ANY(NextActions.NextAction[0], Type=Email AND DoIfFailure=1)', task([email(0), email(1)])), false);
    assert.equal(matches('NONE(NextActions.NextAction, Type=Email AND DoIfFailure=1)', task(email(1))), false);
});
test('quoted values preserve commas, joins, parentheses, escaped quotes, and function names', () => {
    const subject = 'Failure, AND OR (retry) "quoted" ANY(a,b)';
    const definition = task([{ ...email(1), Subject: subject }]);
    assert.equal(matches('ANY(NextActions.NextAction[*], Subject=' + JSON.stringify(subject) + ' AND DoIfFailure=1)', definition), true);
    assert.equal(matches('Name contains "ANY(a,b) AND NONE(c,d)"', { Name: 'ANY(a,b) AND NONE(c,d)' }), true);
    assert.equal(matches('ANY(NextActions.NextAction[*], Subject contains retry AND Type=Email)', definition), true);
});
test('collection and relative field paths respect segment boundaries', () => {
    const definition = task([{ ...email(0), Options: { Enabled: true } }, runTask(1)]);
    definition.DoIfFailure = 1;
    definition.NextActions.NextActionBackup = [email(1)];
    assert.equal(matches(missingEmail, definition), true);
    assert.equal(matches('ANY(NextActions.NextAction[*], Type=Email AND Options.Enabled=1)', definition), true);
    assert.equal(matches('ANY(NextActions.NextAction[*], Type=Task AND Options.Enabled=1)', definition), false);
    assert.equal(matches('ANY(["Next,Actions"]["Next.Action"][*], Type=Email AND DoIfFailure=1)', { 'Next,Actions': { 'Next.Action': [email(1)] } }), true);
});
test('intermediate collection wildcards retain separate item identities', () => {
    const definition = { Groups: [{ NextActions: { NextAction: [email(0)] } }, { NextActions: { NextAction: [runTask(1)] } }] };
    assert.equal(matches('NONE(Groups[*].NextActions.NextAction[*], Type=Email AND DoIfFailure=1)', definition), true);
    definition.Groups[1].NextActions.NextAction.push(email(1));
    assert.equal(matches('ANY(Groups[*].NextActions.NextAction[*], Type=Email AND DoIfFailure=1)', definition), true);
});
test('collection searches do not mutate their input or infer matches from masked nulls', () => {
    const leaves = project(task([{ ...email(0), Subject: null }])), before = JSON.stringify(leaves);
    assert.equal(model.compileTaskSearch('ANY(NextActions.NextAction[*], Subject=null)')(leaves), false);
    assert.equal(model.compileTaskSearch(missingEmail)(leaves), true);
    assert.equal(JSON.stringify(leaves), before);
});
for (const query of [
    'ANY(NextActions.NextAction[*], Type=Email',
    'NONE(NextActions.NextAction[*] Type=Email)',
    'NONE(, Type=Email)',
    'ANY(NextActions..NextAction[*], Type=Email)',
    'NONE(Task, Type=Email)',
    'NONE(NextActions.NextAction[*],)',
    'ANY(NextActions.NextAction[*], Type=)',
    'ANY(NextActions.NextAction[*], Type=Email AND)',
    'NONE(NextActions.NextAction[*], Type=Email OR)',
    'ANY(NextActions.NextAction[*], Type="unfinished)',
    'NONE(NextActions.NextAction[*], Email)',
    'NONE(NextActions.NextAction[*], ANY(Parameters[*], Name=value))',
    'NONE(NextActions.NextAction[*], Type=Email) garbage'
]) test('reject malformed collection query: ' + query, () => assert.throws(() => model.compileTaskSearch(query)));
