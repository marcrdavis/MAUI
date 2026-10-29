/* Built-in Script guidance shared by Traditional/Advanced Process panels.
 * Definition reads and virtual required rows never mutate Task drafts or serialize placeholder values. */
(() => {
    'use strict';
    const fold = value => String(value || '').toLowerCase();
    const input = definition => fold(definition.direction) !== 'out';
    const configured = parameter => Boolean(parameter && (parameter.secure && !parameter.valueChanged || String(parameter.value || '').length));
    function effective(definition, process, task, advanced) {
        const local = (advanced ? process?.parameters : task)?.find(item => fold(item.name) === fold(definition.name));
        const inherited = advanced && !local ? task.find(item => fold(item.name) === fold(definition.name)) : null;
        return { configured: configured(local || inherited), inherited: Boolean(inherited), present: Boolean(local || inherited),
            hasDefault: !local && !inherited && definition.defaultValue != null && definition.defaultValue !== '' };
    }
    window.MauiTaskScriptGuidance = { effective, create };
    function create(options) {
        const root = options.root.querySelector('[data-script-guidance]');
        if (!root) return null;
        const field = name => root.querySelector(`[data-script-guidance-${name}]`);
        const cache = new Map(); let generation = 0, abort = new AbortController();
        const metadata = process => cache.get(String(process?.scriptId))?.value;
        const scriptOption = process => options.scripts().find(item => item.id === String(process?.scriptId));
        const builtIn = process => (scriptOption(process)?.type || process?.scriptType) === 'Standard Script';
        const make = (tag, cls, text) => { const node = document.createElement(tag); if (cls) node.className = cls; if (text !== undefined) node.textContent = text; return node; };
        function notify() { options.changed(); show(options.process()); }
        async function load(process) {
            const id = String(process?.scriptId || '');
            if (!id || !builtIn(process)) return;
            if (cache.has(id)) return cache.get(id).promise;
            const current = generation, entry = {}; cache.set(id, entry);
            entry.promise = (async () => {
                try {
                    const url = new URL(options.url(), location.href); url.searchParams.set('id', id);
                    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal: abort.signal, headers: { Accept: 'application/json' } });
                    const payload = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
                    if (!response.ok || payload?.success !== true) throw new Error(payload?.message || 'Script details could not be loaded. Use Reload Script Details to retry.');
                    if (payload.script?.id !== id || !Array.isArray(payload.script.parameters)) throw new Error('MOVEit did not return complete Script details.');
                    if (current !== generation) return;
                    entry.value = payload.script;
                } catch (error) { if (current === generation && error.name !== 'AbortError') entry.error = error.message; }
                finally { if (current === generation) notify(); }
            })();
            return entry.promise;
        }
        function definitions(process) { return metadata(process)?.parameters || []; }
        function definition(name, scope) {
            const candidates = scope === 'task' ? [...definitions(options.process()), ...options.processes().flatMap(definitions)] : definitions(options.process());
            return candidates.find(item => fold(item.name) === fold(name));
        }
        function requirements() {
            if (options.advanced()) return [];
            const result = new Map();
            for (const process of options.processes()) for (const item of definitions(process))
                if (input(item) && item.required && !result.has(fold(item.name))) result.set(fold(item.name), item);
            return [...result.values()];
        }
        function parameterHelp(target, item) {
            if (!target) return;
            target.hidden = !item;
            target.textContent = item ? [item.description, item.required ? 'Required parameter.' : 'Optional parameter.',
                item.type ? 'Type: ' + item.type : '', item.defaultValue != null ? 'Default: ' + item.defaultValue : '',
                item.choices.length ? 'Choices: ' + item.choices.join(', ') : ''].filter(Boolean).join('\n') : '';
        }
        function configure(name, scope) {
            if (!options.canEdit()) return;
            if (scope === 'task') options.editTask(name); else options.editProcess(name);
        }
        function show(process) {
            root.hidden = !process?.scriptId;
            if (root.hidden) return;
            const entry = cache.get(String(process.scriptId)), data = entry?.value, option = scriptOption(process);
            field('description').textContent = data?.description || data?.processDescription || option?.description || '';
            field('status').textContent = entry?.error || (builtIn(process) ? data ? data.parametersAvailable ? '' : 'MOVEit did not return parameter definitions for this Script.' : 'Loading Script details…' : 'Custom Script parameters are configured manually.');
            field('status').classList.toggle('error-text', Boolean(entry?.error));
            field('retry').hidden = !builtIn(process) || !entry?.error && data?.parametersAvailable !== false;
            field('reference').hidden = !data?.parametersAvailable || !data.parameters.length;
            const rows = field('rows'); rows.replaceChildren(); let missing = 0;
            for (const item of data?.parameters || []) {
                const row = make('tr'), name = make('td', '', item.name), direction = make('td', '', item.direction || '—');
                const detail = make('td'), actions = make('td');
                detail.append(make('div', '', item.description || '—'));
                const notes = [item.required && input(item) ? 'Required' : '', item.type ? 'Type: ' + item.type : '',
                    item.defaultValue != null ? 'Default: ' + item.defaultValue : '', item.choices.length ? 'Choices: ' + item.choices.join(', ') : ''].filter(Boolean);
                const state = effective(item, process, options.taskParameters(), options.advanced());
                if (state.inherited && state.configured) notes.push('Inherited from Task');
                if (notes.length) detail.append(make('small', 'task-script-parameter-notes', notes.join(' · ')));
                if (input(item)) {
                    const button = make('button', 'secondary-button compact-button', state.configured ? 'Edit' : 'Configure'); button.type = 'button';
                    button.disabled = !options.canEdit();
                    button.setAttribute('aria-label', 'Configure ' + item.name);
                    button.addEventListener('click', () => configure(item.name, options.advanced() ? 'process' : 'task')); actions.append(button);
                    if (item.required && !state.configured && !state.hasDefault) missing++;
                }
                row.append(name, direction, detail, actions); rows.append(row);
            }
            field('required').textContent = missing ? 'Required parameters not configured. A Global Parameter may supply a value.' : '';
            field('edit').disabled = !options.canEdit();
            field('edit').textContent = options.advanced() ? 'Edit Process Parameters' : 'Edit Task Parameters';
            if (builtIn(process) && !entry) void load(process);
        }
        function appendRequiredRows(scope, process, rows, table, empty) {
            if (!rows) return;
            const list = scope === 'task' ? requirements() : definitions(process).filter(item => item.required && input(item));
            for (const item of list) {
                const state = effective(item, process, options.taskParameters(), scope === 'process');
                if (state.present) {
                    if (!state.configured && !state.hasDefault) {
                        const row = [...rows.children].find(row => fold(row.firstElementChild?.textContent) === fold(item.name));
                        if (row?.children[1]) { row.children[1].textContent = 'Required but not configured'; row.children[1].classList.add('task-script-required'); }
                    }
                    continue;
                }
                const row = make('div', 'traditional-task-editor-parameter-row task-script-required-row'); row.dataset.scriptRequiredName = item.name;
                const value = state.hasDefault ? 'Default: ' + item.defaultValue : 'Required but not configured';
                row.append(make('div', state.hasDefault ? '' : 'task-script-required', item.name), make('div', state.hasDefault ? '' : 'task-script-required', value));
                const actions = make('div', 'traditional-task-editor-parameter-row-actions');
                const button = make('button', 'secondary-button compact-button', 'Configure'); button.type = 'button'; button.disabled = !options.canEdit(); button.setAttribute('aria-label', 'Configure required parameter ' + item.name);
                button.addEventListener('click', () => configure(item.name, scope)); actions.append(button); row.append(actions); rows.append(row);
                if (table) table.hidden = false; if (empty) empty.hidden = true;
            }
        }
        field('edit').addEventListener('click', () => { if (!options.canEdit()) return; if (options.advanced()) options.processTab(); else options.taskDialog(); });
        field('retry').addEventListener('click', () => { const process = options.process(); if (!process) return; cache.delete(String(process.scriptId)); show(process); });
        return { show, definition, appendRequiredRows, parameterHelp,
            prime: processes => {
                const current = generation, queue = [...processes];
                // Bound inventory guidance reads on Tasks with many Process steps; duplicate Scripts share one promise.
                const worker = async () => { while (queue.length && current === generation) await load(queue.shift()); };
                const count = Math.min(4, queue.length);
                for (let i = 0; i < count; i++) void worker();
            },
            reset: () => { generation++; abort.abort(); abort = new AbortController(); cache.clear(); root.hidden = true; } };
    }
})();
