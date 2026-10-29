/* Scalar Task JSON operations shared by the tree, replacements and review. No I/O or stored browser drafts. */
(() => {
    'use strict';
    const changeCache = new WeakMap();
    const key = path => JSON.stringify(path);
    const clone = value => JSON.parse(JSON.stringify(value));
    const type = value => value === null ? 'Null' : typeof value === 'number' ? Number.isInteger(value) ? 'Integer' : 'Number' : typeof value === 'boolean' ? 'Boolean' : 'String';
    const text = value => JSON.stringify(value);
    const pathText = path => 'Task' + path.map(part => /^\d+$/.test(part) ? '[' + part + ']' : /^[A-Za-z_$][\w$]*$/.test(part) ? '.' + part : '[' + JSON.stringify(part) + ']').join('');
    function get(document, path) {
        let value = document;
        for (const part of path) {
            if (value === null || typeof value !== 'object' || !Object.hasOwn(value, part)) throw new Error('This element no longer exists. Reload this Task.');
            value = value[part];
        }
        return value;
    }
    function set(document, path, value) {
        changeCache.delete(document);
        const parent = get(document, path.slice(0, -1)), name = path.at(-1);
        if (!parent || !Object.hasOwn(parent, name)) throw new Error('This element no longer exists. Reload this Task.');
        Object.defineProperty(parent, name, { value: clone(value), writable: true, enumerable: true, configurable: true });
    }
    function parse(input, current) {
        if (typeof current === 'string') return input;
        if (typeof current === 'boolean') {
            if (input.trim() === 'true') return true;
            if (input.trim() === 'false') return false;
            throw new Error('Enter true or false for this Boolean value.');
        }
        if (typeof current !== 'number') throw new Error('This value is read-only.');
        if (!/^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(input.trim())) throw new Error('Enter a valid JSON number.');
        const value = Number(input);
        if (!Number.isFinite(value) || Math.abs(value) > Number.MAX_SAFE_INTEGER || Number.isInteger(current) && !Number.isSafeInteger(value))
            throw new Error('Enter a supported ' + (Number.isInteger(current) ? 'whole number.' : 'number.'));
        return value;
    }
    function leaves(document, readOnlyPaths = []) {
        const locked = new Set(readOnlyPaths.map(key)), result = []; let count = 0;
        function visit(value, path) {
            if (++count > 20000 || path.length > 48) throw new Error('This Task definition is too large for the JSON Editor.');
            if (value !== null && typeof value === 'object') {
                for (const [name, child] of Object.entries(value)) visit(child, [...path, name]);
            } else result.push({ path, key: key(path), name: path.at(-1), pathText: pathText(path), value, type: type(value), readOnly: locked.has(key(path)) });
        }
        visit(document, []); return result;
    }
    // All local mutations pass through set(). A saved/reloaded baseline has a new original reference.
    // Cache the diff so typing or paging does not repeatedly scan every loaded Task.
    function changes(task) {
        const cached=changeCache.get(task.document);
        if(cached?.original===task.original)return cached.value;
        const value=(task.leaves || []).map(leaf => ({path: leaf.path, value: get(task.document, leaf.path), before: get(task.original, leaf.path)}))
            .filter(change => text(change.before) !== text(change.value));
        changeCache.set(task.document,{original:task.original,value});return value;
    }
    function match(leaf, find, mode, filter) {
        const source = mode === 'path' ? leaf.pathText : leaf.name;
        let yes;
        if (mode === 'path') {
            // Only * is a wildcard; all regular-expression punctuation, including [*], is literal except its star.
            const pattern = find.split('*').map(part => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('.*');
            yes = new RegExp('^' + pattern + '$', 'i').test(source);
        } else yes = String(source).toLowerCase() === find.toLowerCase();
        return yes && (filter === '' || String(leaf.value) === filter || text(leaf.value) === filter);
    }
    // Explicit checkout recovery keeps only the intended leaf changes. An array element's native identity must still match.
    function rebase(previous, fresh) {
        const pending = changes(previous), locked = new Set(fresh.readOnlyPaths.map(key)), document = clone(fresh.definition);
        for (const change of pending) {
            if (locked.has(key(change.path))) throw new Error('An edited value is now read-only. Reload this Task before continuing.');
            for (let length = 1; length < change.path.length; length++) {
                const before = get(previous.original, change.path.slice(0,length)), after = get(document,change.path.slice(0,length));
                if (Array.isArray(before) && (!Array.isArray(after) || before.length!==after.length))
                    throw new Error('The Task structure changed. Reload this Task before continuing.');
                if (before && typeof before === 'object' && !Array.isArray(before) && Object.hasOwn(before,'ID') && text(before.ID) !== text(after?.ID))
                    throw new Error('The Task structure changed. Reload this Task before continuing.');
            }
            const current = get(document, change.path);
            if (type(current) !== type(change.value)) throw new Error('The edited value type changed. Reload this Task before continuing.');
            set(document, change.path, change.value);
        }
        return document;
    }
    window.MauiTaskJsonModel = { key, clone, type, text, pathText, get, set, parse, leaves, changes, match, rebase };
})();
