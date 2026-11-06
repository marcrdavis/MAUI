/* Built-in Script guidance shared by Traditional/Advanced Process panels.
 * Definition reads and virtual required rows never mutate Task drafts or serialize placeholder values. */
(() => {
    'use strict';
    const fold = value => String(value || '').toLowerCase();
    const input = definition => fold(definition.direction) !== 'out';
    const configured = parameter => Boolean(parameter && (parameter.secure && !parameter.valueChanged || String(parameter.value ?? '').length));
    const find = (parameters, name) => parameters?.find(item => fold(item.name) === fold(name));
    function effective(definition, process, task, advanced, global) {
        // Respect an existing Process entry on every Task type. Editing guidance must
        // reflect the stored definition without relocating values or changing native scope.
        const local = find(process?.parameters, definition.name), inherited = find(task, definition.name);
        const scope = local ? 'process' : inherited ? 'task' : global?.value ? 'global' : '';
        const parameter = local || inherited || global?.value;
        return { scope, parameter, configured: configured(parameter), inherited: advanced && scope === 'task',
            present: Boolean(parameter), hasDefault: !parameter && definition.defaultValue != null && definition.defaultValue !== '',
            globalPending: !parameter && Boolean(global?.pending), globalUnknown: !parameter && Boolean(global?.error) };
    }
    window.MauiTaskScriptGuidance = { effective, create };
    function create(options) {
        const root = options.root.querySelector('[data-script-guidance]');
        if (!root) return null;
        const field = name => root.querySelector('[data-script-guidance-' + name + ']');
        const cache = new Map(), globals = new Map();
        let generation = 0, abort = new AbortController(), pool = { active: 0, queue: [] };
        const metadata = process => cache.get(String(process?.scriptId))?.value;
        const scriptOption = process => options.scripts().find(item => item.id === String(process?.scriptId));
        const builtIn = process => (scriptOption(process)?.type || process?.scriptType) === 'Standard Script';
        const make = (tag, cls, text) => { const node = document.createElement(tag); if (cls) node.className = cls; if (text !== undefined) node.textContent = text; return node; };
        const stateFor = (item, process) => effective(item, process, options.taskParameters(), options.advanced(), globals.get(fold(item.name)));
        function notify() { options.changed(); show(options.process()); }
        function queueGlobal(item, process) {
            if (!input(item) || !options.globalUrl?.() || find(process?.parameters, item.name) || find(options.taskParameters(), item.name) || globals.has(fold(item.name))) return;
            const current = generation, currentPool = pool, signal = abort.signal;
            const entry = { pending: true }; globals.set(fold(item.name), entry);
            currentPool.queue.push(async () => {
                try {
                    const url = new URL(options.globalUrl(), location.href); url.searchParams.set('name', item.name);
                    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal, headers: { Accept: 'application/json' } });
                    const payload = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
                    if (!response.ok || payload?.success !== true || typeof payload.present !== 'boolean') throw new Error('Global value could not be verified.');
                    if (payload.present && (fold(payload.parameter?.name) !== fold(item.name) || typeof payload.parameter.secure !== 'boolean' || typeof payload.parameter.value !== 'string')) throw new Error('Global value could not be verified.');
                    if (current !== generation) return;
                    // Never retain a secret returned by a malformed/older endpoint.
                    entry.value = payload.present ? { name: payload.parameter.name, secure: payload.parameter.secure, value: payload.parameter.secure ? '' : payload.parameter.value } : null;
                } catch (error) { if (current === generation && error.name !== 'AbortError') entry.error = true; }
                finally { if (current === generation) { entry.pending = false; notify(); } }
            });
            drain(currentPool);
        }
        function drain(currentPool) {
            while (currentPool.active < 4 && currentPool.queue.length) {
                currentPool.active++;
                void currentPool.queue.shift()().finally(() => { currentPool.active--; drain(currentPool); });
            }
        }
        async function load(process) {
            const id = String(process?.scriptId || '');
            if (!id || !builtIn(process)) return;
            const current = generation;
            if (cache.has(id)) { await cache.get(id).promise; if (current === generation) for (const item of definitions(process)) queueGlobal(item, process); return; }
            const entry = {}; cache.set(id, entry);
            entry.promise = (async () => {
                try {
                    const url = new URL(options.url(), location.href); url.searchParams.set('id', id);
                    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal: abort.signal, headers: { Accept: 'application/json' } });
                    const payload = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
                    if (!response.ok || payload?.success !== true) throw new Error(payload?.message || 'Script details could not be loaded. Use Reload Script Details to retry.');
                    if (payload.script?.id !== id || !Array.isArray(payload.script.parameters)) throw new Error('MOVEit did not return complete Script details.');
                    if (current !== generation) return;
                    entry.value = payload.script;
                    for (const item of definitions(process)) queueGlobal(item, process);
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
        const displayedValue = parameter => parameter.secure ? '(suppressed)' : String(parameter.value ?? '') || '(empty)';
        function sourceNote(state) {
            if (state.scope) return (state.scope === 'process' ? 'Process value' : state.scope === 'task' ? state.inherited ? 'Inherited from Task' : 'Task value' : 'Global value') + ': ' + displayedValue(state.parameter);
            if (state.globalPending) return 'Checking Global value…';
            if (state.globalUnknown) return 'Global value could not be verified.';
            return '';
        }
        function parameterHelp(target, item, scope) {
            if (!target) return;
            target.hidden = !item;
            if (!item) { target.textContent = ''; return; }
            const state = stateFor(item, scope === 'process' ? options.process() : null);
            target.textContent = [item.description, item.required ? 'Required parameter.' : 'Optional parameter.',
                item.type ? 'Type: ' + item.type : '', item.defaultValue != null ? 'Default: ' + item.defaultValue : '',
                item.choices.length ? 'Choices: ' + item.choices.join(', ') : '', sourceNote(state),
                scope === 'process' ? 'Changes apply only to this Process step.' : 'Changes apply at Task scope. Process entries remain separate.'
            ].filter(Boolean).join('\n');
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
            field('retry').hidden = !builtIn(process) || !entry?.error && data?.parametersAvailable !== false && !definitions(process).some(item => globals.get(fold(item.name))?.error);
            field('reference').hidden = !data?.parametersAvailable || !data.parameters.length;
            const rows = field('rows'); rows.replaceChildren(); let missing = 0, pending = 0, unknown = 0;
            for (const item of data?.parameters || []) {
                queueGlobal(item, process);
                const row = make('tr'), name = make('td', '', item.name), direction = make('td', '', item.direction || '—');
                const detail = make('td'), actions = make('td');
                detail.append(make('div', '', item.description || '—'));
                const notes = [item.required && input(item) ? 'Required' : '', item.type ? 'Type: ' + item.type : '',
                    item.defaultValue != null ? 'Default: ' + item.defaultValue : '', item.choices.length ? 'Choices: ' + item.choices.join(', ') : ''].filter(Boolean);
                const state = stateFor(item, process), note = sourceNote(state);
                if (input(item) && note) notes.push(note);
                if (notes.length) detail.append(make('small', 'task-script-parameter-notes', notes.join(' · ')));
                if (input(item)) {
                    const defaultScope = options.advanced() ? 'process' : 'task';
                    const scope = state.scope === 'process' || state.scope === 'task' ? state.scope : defaultScope;
                    const button = make('button', 'secondary-button compact-button', state.scope === 'global' ? 'Override' : state.present ? 'Edit' : 'Configure'); button.type = 'button';
                    button.disabled = !options.canEdit();
                    button.setAttribute('aria-label', 'Configure ' + item.name);
                    button.addEventListener('click', () => configure(item.name, scope)); actions.append(button);
                    if (options.advanced() && state.scope === 'task') {
                        const override = make('button', 'secondary-button compact-button', 'Override'); override.type = 'button'; override.disabled = !options.canEdit();
                        override.title = 'Create a Process override';
                        override.setAttribute('aria-label', 'Override ' + item.name + ' for Process');
                        override.addEventListener('click', () => configure(item.name, 'process')); actions.append(override);
                    }
                    if (item.required && !state.configured) {
                        if (state.globalPending) pending++;
                        else if (state.globalUnknown) unknown++;
                        else if (!state.hasDefault) missing++;
                    }
                }
                row.append(name, direction, detail, actions); rows.append(row);
            }
            field('required').textContent = [missing ? 'Required parameters not configured.' : '', pending ? 'Checking Global Parameters…' : '',
                unknown ? 'Required parameters have no local value. Global values could not be verified.' : ''].filter(Boolean).join(' ');
            field('edit').disabled = !options.canEdit();
            field('edit').textContent = options.advanced() ? 'Edit Process Parameters' : 'Edit Task Parameters';
            if (builtIn(process) && !entry) void load(process);
        }
        function appendRequiredRows(scope, process, rows, table, empty) {
            if (!rows) return;
            const list = scope === 'task' ? requirements() : definitions(process).filter(item => item.required && input(item));
            for (const item of list) {
                // Task Parameters remains Task-scoped. Do not invent a missing Task value
                // when every relevant Process already has its own stored entry.
                if (scope === 'task' && !find(options.taskParameters(), item.name)) {
                    const consumers = options.processes().filter(candidate => definitions(candidate).some(def => input(def) && def.required && fold(def.name) === fold(item.name)));
                    if (consumers.length && consumers.every(candidate => find(candidate.parameters, item.name))) continue;
                }
                const state = stateFor(item, scope === 'process' ? process : null);
                if (state.present) {
                    if (!state.configured && !state.hasDefault) {
                        const row = [...rows.children].find(row => fold(row.firstElementChild?.textContent) === fold(item.name));
                        if (row?.children[1]) { row.children[1].textContent = 'Required but not configured'; row.children[1].classList.add('task-script-required'); }
                    }
                    // Inherited entries are guidance only, never inserted into local collections.
                    continue;
                }
                const row = make('div', 'traditional-task-editor-parameter-row task-script-required-row'); row.dataset.scriptRequiredName = item.name;
                const value = sourceNote(state) || (state.hasDefault ? 'Default: ' + item.defaultValue : 'Required but not configured');
                const missing = !state.hasDefault && !state.globalPending && !state.globalUnknown;
                row.append(make('div', missing ? 'task-script-required' : '', item.name), make('div', missing ? 'task-script-required' : '', value));
                const actions = make('div', 'traditional-task-editor-parameter-row-actions');
                const button = make('button', 'secondary-button compact-button', 'Configure'); button.type = 'button'; button.disabled = !options.canEdit(); button.setAttribute('aria-label', 'Configure required parameter ' + item.name);
                button.addEventListener('click', () => configure(item.name, scope)); actions.append(button); row.append(actions); rows.append(row);
                if (table) table.hidden = false; if (empty) empty.hidden = true;
            }
        }
        field('edit').addEventListener('click', () => { if (!options.canEdit()) return; if (options.advanced()) options.processTab(); else options.taskDialog(); });
        field('retry').addEventListener('click', () => { const process = options.process(); if (!process) return; cache.delete(String(process.scriptId)); globals.clear(); show(process); });
        return { show, definition, appendRequiredRows, parameterHelp,
            draft: (item, scope) => {
                if (!item) return { value: '', secure: false };
                const state = stateFor(item, scope === 'process' ? options.process() : null);
                return state.parameter ? { value: state.parameter.secure ? '' : String(state.parameter.value ?? ''), secure: Boolean(state.parameter.secure) }
                    : { value: item.defaultValue ?? '', secure: false };
            },
            prime: processes => {
                const current = generation, queue = [...processes];
                const worker = async () => { while (queue.length && current === generation) await load(queue.shift()); };
                const count = Math.min(4, queue.length);
                for (let i = 0; i < count; i++) void worker();
            },
            reset: () => { generation++; abort.abort(); abort = new AbortController(); cache.clear(); globals.clear(); pool.queue.length = 0; pool = { active: 0, queue: [] }; root.hidden = true; } };
    }
})();
