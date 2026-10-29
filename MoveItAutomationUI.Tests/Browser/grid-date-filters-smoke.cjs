'use strict';
// Production Date/Time helper plus the real grid expression/matching path. Shared
// cases also run in .NET against scheduled Reports. No DOM or browser-date parsing mocks.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
let checks = 0;
function check(actual, expected, message) { assert.deepEqual(actual, expected, message); checks++; }
const context = vm.createContext({ window: {}, Intl, Date, console });
vm.runInContext(read('wwwroot/js/grid-date-macros.js'), context);
const helper = context.window.MauiGridDateMacros;
const clock = helper.createClock('2026-10-21T12:00:00', 'UTC', 'en-US');
const cases = JSON.parse(read('MoveItAutomationUI.Tests/Reporting/Fixtures/GridDateFilterCases.json'));
for (const item of cases.cases) {
    const filter = helper.parseFilter(item.filter, clock);
    assert.ok(filter, item.filter);
    check(helper.matches({ dataset: { value: item.raw }, textContent: item.display || '' }, filter, clock), item.matches, JSON.stringify(item));
}
for (const [today, macro, start, end] of [
    ['2026-12-31', '[nextmonth]', '1/1/2027', '2/1/2027'],
    ['2026-03-31', '[nextquarter]', '4/1/2026', '7/1/2026'],
    ['2026-04-01', '[nextquarter]', '7/1/2026', '10/1/2026'],
    ['2027-01-01', '[nextquarter]', '4/1/2027', '7/1/2027'],
    ['2027-01-01', '[nextyear]', '1/1/2028', '1/1/2029'],
    ['2024-01-31', '[nextmonth]', '2/1/2024', '3/1/2024']
]) {
    const c = helper.createClock(`${today}T12:00:00`);
    const f = helper.parseFilter(macro, c);
    check(f.start, helper.parseFilter(start, c).start);
    check(f.end, helper.parseFilter(end, c).start);
}
for (const value of ['2026-02-30', '2026-13-01', '>not a date', '10/21', 'October 2026', '10/2026', '2026', '!', '[nextdecade]', '10/21/2026 13:00 PM', '10/21/2026 00:30 PM', '2026-10-21T25:00:00', '2026-10-21T01:60:00', '2026-10-21T01:00:00+15:00']) {
    check(helper.parseFilter(value, clock), null, value);
}
for (const [value, expected] of [
    ['Oct 21, 2026', ['Oct 21, 2026']],
    ['>=Oct 21, 2026 2:30 PM, <Nov 1, 2026', ['>=Oct 21, 2026 2:30 PM', '<Nov 1, 2026']],
    ['Wed, Oct 21, 2026 or [nextmonth]', ['Wed, Oct 21, 2026', '[nextmonth]']],
    ['2026-10-21,', ['2026-10-21,']],
    ['[today] or', ['[today] or']]
]) check(Array.from(helper.splitAlternatives(value)), expected);
const nyClock = helper.createClock('2026-10-21T12:00:00', 'America/New_York');
function match(raw, filter, c = nyClock, display = '') { return helper.matches({ dataset: { value: raw }, textContent: display }, helper.parseFilter(filter, c), c); }
check(match('2026-10-22T02:00:00Z', '2026-10-21'), true, 'UTC date differs from application date');
check(match('2026-03-08T07:00:00Z', '2026-03-08 03:00'), true, 'DST start');
check(match('2026-03-08T06:59:59Z', '<2026-03-08 03:00'), true, 'before DST start');
check(match('2026-10-21T14:30:00+02:00', '2026-10-21 08:30'), true);
const unknownZone = helper.createClock('2026-10-21T12:00:00', 'Unrecognized/ServerZone');
check(match('2026-10-22T02:00:00Z', '2026-10-21', unknownZone, 'Oct 21, 2026 10:00 PM'), true);
const french = helper.createClock('2026-10-21T12:00:00', 'UTC', 'fr-FR');
check(match('févr. 21, 2026  2:30 PM', '2026-02-21', french), true);
const spanish = helper.createClock('2026-10-21T12:00:00', 'UTC', 'es-ES');
check(match('oct 21, 2026  2:30 p. m.', '2026-10-21 14:30', spanish), true);
const japanese = helper.createClock('2026-10-21T12:00:00', 'UTC', 'ja-JP');
check(match('10月 21, 2026  14:30', '2026-10-21 14:30', japanese), true);
check(match('2026-10-21T14:30:00.1234567Z', '2026-10-21 14:30:00.123', clock), true);

// Run getFilteredRows from grid.js, including its datetime-vs-text/number routing.
const grid = read('wwwroot/js/grid.js');
const functions = grid.slice(grid.indexOf('        function splitFilterAlternatives('), grid.indexOf('        // Sorts a row collection'));
Object.assign(context, { dateMacros: helper, dateMacroClock: clock,
    resolveDateMacro: value => helper.parseFilter(value, clock), matchesDateMacro: (cell, f) => helper.matches(cell, f, clock),
    updateDateMacroHint() {}, cssEscape: s => s, getRowSearchText: row => row.text,
    search: { value: '' }, scopeFilters: [], filters: [], rows: [],
    table: { querySelector: selector => ({ dataset: { type: selector.includes('Date') ? 'datetime' : selector.includes('Count') ? 'number' : 'text' } }) }
});
vm.runInContext(functions, context);
context.rows = ['2026-10-20T23:59:59', '2026-10-21T14:30:00', '2026-11-10T12:00:00', '2027-01-05T00:00:00'].map((date, i) => ({ id: String(i), text: ['Failed', 'Success', 'Disabled', 'Failed'][i],
    querySelector: selector => ({ dataset: { value: selector.includes('Date') ? date : String(i) }, textContent: selector.includes('Date') ? ['Oct 20, 2026', '10/21/2026', 'Nov 10, 2026', 'Jan 5, 2027'][i] : ['Failed', 'Success', 'Disabled', 'Failed'][i] }) }));
function ids(key, value) { context.filters = [{ dataset: { columnFilter: key }, value }]; return Array.from(vm.runInContext('getFilteredRows().map(row => row.id)', context)); }
check(ids('Date', '<[today], [nextmonth]'), ['0', '2']);
check(ids('Date', 'Oct 21, 2026, Nov 10, 2026'), ['1', '2']);
check(ids('Date', '![nextmonth]'), ['0', '1', '3']);
check(ids('Date', '2026'), ['0', '1', '2']);
check(ids('Date', 'Oct'), ['0']); // Partial text behavior is unchanged.
check(ids('Count', '>=2, <1'), ['0', '2', '3']);
check(ids('Name', '!Failed'), ['1', '2']);
context.search.value = 'Failed or Disabled';
check(ids('Date', '>[today]'), ['2', '3']);
context.search.value = '';
context.filters = [{ dataset: { columnFilter: 'Date' }, value: '>=[today]' }, { dataset: { columnFilter: 'Name' }, value: 'Failed' }];
check(Array.from(vm.runInContext('getFilteredRows().map(row => row.id)', context)), ['3']);

const view = read('Views/Shared/_DataGrid.cshtml');
for (const macro of ['[nextmonth]', '[nextquarter]', '[nextyear]']) {
    assert.ok(view.includes(`data-date-macro-option="${macro}"`)); checks++;
}
assert.ok(view.includes('Date &amp; Time comparisons')); checks++;
if (!process.env.MAUI_DATE_FILTER_TZ_CHILD) {
    for (const TZ of ['UTC', 'America/Los_Angeles', 'Asia/Tokyo']) {
        const run = spawnSync(process.execPath, [__filename], { env: { ...process.env, TZ, MAUI_DATE_FILTER_TZ_CHILD: '1' }, encoding: 'utf8' });
        assert.equal(run.status, 0, run.stdout + run.stderr); checks++;
    }
}
console.log(`${checks} grid Date/Time checks passed${process.env.TZ ? ` (browser TZ ${process.env.TZ})` : ''}.`);
