const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../wwwroot/js/native-portability.js'), 'utf8');
function extract(name) {
    const match = source.match(new RegExp(`        function ${name}\\([^]*?\\n        \\}`));
    assert.ok(match, `${name} exists`); return match[0];
}
class Element {
    constructor(tag) { this.tag = tag; this.children = []; this.dataset = {}; this.listeners = {}; this.textContent = ''; this.hidden = false; }
    append(...items) { this.children.push(...items); }
    appendChild(item) { this.append(item); return item; }
    addEventListener(name, handler) { this.listeners[name] = handler; }
    set value(value) { this.currentValue = value; }
    get value() { return this.currentValue ?? (this.tag === 'select' ? this.children[0]?.value : '') ?? ''; }
    querySelectorAll(selector) {
        const key = selector.slice(6, -1).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
        return this.children.flatMap(child => [...(Object.hasOwn(child.dataset, key) ? [child] : []), ...child.querySelectorAll(selector)]);
    }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
}
let checks = 0;
for (const objectType of ['Host', 'Script', 'DateList', 'SSHClientKey', 'SSLClientCert', 'PGPKey', 'Task']) {
    for (const canReuse of [true, false]) {
        const conflicts = new Element('div'); let changes = 0;
        const context = { document: { createElement: tag => new Element(tag) }, conflicts, updateImportSubmitState: () => changes++ };
        vm.createContext(context);
        vm.runInContext(['objectRoleLabel', 'friendlyConflictReasons', 'conflictKey', 'renderConflict', 'collectResolutions', 'conflictsValid'].map(extract).join('\n'), context);
        const item = { objectType, sourceId: '1', sourceName: '<img src=x onerror=bad()>', displayType: objectType,
            selected: objectType === 'Task', conflictTargetId: '20', reuseTargetName: 'Existing <script>', duplicateReasons: ['name'],
            canRename: ['Task', 'Host'].includes(objectType), suggestedName: 'New Name', canReuseExisting: canReuse };
        // The service controls eligibility. In particular, an ID conflict alone must never expose reuse.
        context.renderConflict(item);
        const select = conflicts.querySelector('[data-native-portability-resolution]');
        assert.deepEqual(select.children.map(option => option.value), [
            ...(canReuse ? ['useexisting'] : []), 'overwrite', ...(item.canRename ? ['rename'] : [])
        ]);
        assert.equal(select.value, canReuse ? 'useexisting' : item.canRename ? 'rename' : 'overwrite');
        assert.equal(context.collectResolutions()[0].targetId, '20');
        assert.equal(context.collectResolutions()[0].sourceId, '1');
        assert.equal(context.collectResolutions()[0].action, select.value);
        assert.equal(context.conflictsValid(), true);
        const rename = conflicts.querySelector('[data-native-portability-rename-row]');
        assert.equal(rename.hidden, select.value !== 'rename');
        assert.equal(conflicts.children[0].children[0].children[0].textContent, item.sourceName);
        assert.equal(conflicts.children[0].children[1].textContent.includes(item.reuseTargetName), canReuse);
        if (canReuse) assert.equal(select.children[0].textContent, 'Reuse Existing');
        select.value = 'overwrite'; select.listeners.change(); assert.equal(rename.hidden, true);
        assert.equal(context.collectResolutions()[0].action, 'overwrite');
        if (item.canRename) {
            select.value = 'rename'; select.listeners.change(); assert.equal(rename.hidden, false);
            const input = conflicts.querySelector('[data-native-portability-rename]');
            input.value = ''; input.listeners.input(); assert.equal(context.conflictsValid(), false);
            input.value = 'A'.repeat(255); assert.equal(context.conflictsValid(), false);
            input.value = 'Valid'; assert.equal(context.conflictsValid(), true);
        }
        assert.ok(changes > 0); checks++;
    }
}
// A key ID conflict without server compatibility confirmation has the original explicit overwrite choice.
{
    const conflicts = new Element('div');
    const context = { document: { createElement: tag => new Element(tag) }, conflicts, updateImportSubmitState() {} };
    vm.createContext(context);
    vm.runInContext(['objectRoleLabel', 'friendlyConflictReasons', 'conflictKey', 'renderConflict'].map(extract).join('\n'), context);
    context.renderConflict({ objectType: 'SSHClientKey', sourceId: '1', duplicateReasons: ['id'] });
    assert.deepEqual(conflicts.querySelector('[data-native-portability-resolution]').children.map(option => option.value), ['overwrite']); checks++;
}
console.log(`Native import dependency actions, defaults, target identity, rename validation and safe text: ${checks} scenarios passed.`);
