'use strict';
// Exercise production Configuration callbacks without a live MOVEit session.
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), assert = require('node:assert/strict');
const read = p => fs.readFileSync(path.resolve(__dirname, '../..', p), 'utf8');
class Node {
    constructor() { this.value = ''; this.innerHTML = ''; this.hidden = false; this.disabled = false; this.children = []; this.events = {}; this.dataset = {}; this.open = false; }
    addEventListener(type, handler) { this.events[type] = handler; }
    async fire(type) { await this.events[type]?.({ preventDefault() {} }); await new Promise(r => setImmediate(r)); }
    append(...nodes) { this.children.push(...nodes); }
    replaceChildren() { this.children = []; }
    focus() {}
    showModal() { this.open = true; }
    close() { this.open = false; }
    setRangeText(text) { this.value += text; }
}
const fields = {}, deletes = {}, workspace = new Node(), list = new Node(), dialog = new Node(), del = new Node(), file = new Node(), load = new Node(), loadError = new Node();
for (const key of ['name','subject','format','text','source-text','visual','title','save','error','source','subject-macro','body-macro']) fields[key] = new Node();
for (const key of ['name','error','confirm','close','cancel']) deletes[key] = new Node();
const close = new Node();
dialog.querySelector = s => fields[s.match(/data-template-(.+)\]/)[1]];
dialog.querySelectorAll = s => s === '[data-template-close]' ? [close] : s.startsWith('[data-template-command]') ? [] : [...Object.values(fields), close];
del.querySelector = s => deletes[s.match(/data-notification-template-delete-(.+)\]/)[1]];
workspace.querySelector = s => ({ '[data-notification-template-list]': list, '[data-notification-template-file]': file, '[data-notification-template-load]': load, '[data-notification-template-load-error]': loadError })[s];
workspace.dataset = { notificationTemplatesUrl: '/maui/Configuration/NotificationTemplates', saveNotificationTemplateUrl: '/maui/Configuration/SaveNotificationTemplate', deleteNotificationTemplateUrl: '/maui/Configuration/DeleteNotificationTemplate' };
const document = { querySelector: s => ({ '[data-configuration-workspace]': workspace, '[data-notification-template-edit-dialog]': dialog, '[data-notification-template-delete-dialog]': del })[s], createElement: () => new Node() };
const requests = []; let stored = [], fail = false;
const context = { document, console, window: { getSelection: () => null, MauiModalUI: { formValid: () => true,
    watchAction: (d,b,allowed,valid) => () => { b.disabled = !(allowed() && valid()); } } },
    fetch: async (url, options) => {
        if (!options.method) return { ok: true, json: async () => ({ success: true, templates: stored }) };
        const payload = JSON.parse(options.body); requests.push({ url, payload });
        if (fail) return { ok: false, status: 400, json: async () => ({ message: 'Name already exists.' }) };
        if (url.includes('Delete')) { stored = []; return { ok: true, json: async () => ({ success: true }) }; }
        const template = { ...payload, id: payload.templateId || 'template-1' }; stored = [template];
        return { ok: true, json: async () => ({ success: true, template }) };
    } };
vm.createContext(context);
vm.runInContext(read('wwwroot/js/shared/notification-message.js'), context);
const detect = context.window.MauiNotificationMessage.detectFormat;
assert.equal(detect('Filename [OrigName] < 10'), 'text');
assert.equal(detect('<!doctype html><html><body>Hello</body></html>'), 'html');
assert.equal(detect('<table><tr><td>Body</td></tr></table>'), 'html');
// Browser DOM sanitizer is exercised by the existing notification layout/runtime tests;
// this harness substitutes only that DOM-dependent primitive.
context.window.MauiNotificationMessage = { ...context.window.MauiNotificationMessage, sanitize: text => text, populateMacros() {} };
vm.runInContext(read('wwwroot/js/configuration-notification-templates.js'), context);
(async () => {
    await new Promise(r => setImmediate(r));
    file.files = [{ name: 'Task Failure.txt', size: 20, text: async () => 'Task [TaskName] failed.' }];
    await file.fire('change');
    assert.equal(dialog.open, true); assert.equal(fields.name.value, 'Task Failure'); assert.equal(fields.format.value, 'text');
    assert.equal(fields.save.disabled, true, 'Subject required before Add');
    fields.subject.value = 'Failure'; fields.save.disabled = false;
    fail = true; await fields.save.fire('click');
    assert.equal(dialog.open, true); assert.equal(fields.error.textContent, 'Name already exists.');
    assert.equal(fields.error.hidden, false, 'Error stays inside modal');
    fail = false; await fields.save.fire('click');
    assert.equal(dialog.open, false); assert.equal(requests.at(-1).payload.overwriteExisting, false);
    const row = list.children[0]; assert.equal(row.children[1].children[0].textContent, 'Edit / Rename');
    await row.children[1].children[0].fire('click');
    assert.equal(fields.save.textContent, 'Update Template'); assert.equal(fields.save.disabled, true, 'Unchanged edit disabled');
    fields.name.value = 'Renamed'; fields.subject.value = 'New subject'; fields.save.disabled = false;
    await fields.save.fire('click');
    assert.equal(requests.at(-1).payload.templateId, 'template-1'); assert.equal(requests.at(-1).payload.name, 'Renamed');
    assert.equal(requests.at(-1).payload.overwriteExisting, true);
    await list.children[0].children[1].children[1].fire('click');
    assert.equal(del.open, true); await deletes.confirm.fire('click'); assert.equal(del.open, false);
    const count = requests.length;
    file.files = [{ name: 'bad.exe', size: 5 }]; await file.fire('change'); assert.match(loadError.textContent, /Select a/); assert.equal(requests.length, count);
    file.files = [{ name: 'large.html', size: 3 * 1024 * 1024 }]; await file.fire('change'); assert.match(loadError.textContent, /2 MB/);
    file.files = [{ name: 'markup.txt', size: 10, text: async () => '<p>HTML in a text file</p>' }]; await file.fire('change'); assert.equal(fields.format.value, 'html');
    await close.fire('click'); assert.equal(dialog.open, false); assert.equal(requests.length, count, 'Close never writes');
    console.log('PASS Notification Templates file loading, content-based format, create/edit/rename/delete, modal errors and validation.');
})().catch(error => { console.error(error); process.exitCode = 1; });
