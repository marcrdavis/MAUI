/* One Host browser. Callers adapt their verified transport and own the returned selection. */
(() => {
    'use strict';
    const dialog = document.querySelector('[data-host-browser]');
    if (!dialog) return;
    const find = key => dialog.querySelector('[data-host-browser-' + key + ']');
    const rows = find('rows'), location = find('location'), parent = find('parent'), ok = find('ok');
    const headers = [...dialog.querySelectorAll('[data-folder-browser-sort]')];
    const defaultHint = find('hint').textContent;
    let session = null, generation = 0;

    function close() {
        const previous = session;
        session = null; generation++;
        previous?.controller?.abort();
        if (dialog.open) dialog.close();
        previous?.options.onClose?.();
    }
    function busy(value) {
        if (!session) return;
        session.busy = value;
        find('loading').hidden = !value;
        location.disabled = value;
        find('refresh').disabled = value;
        parent.disabled = value;
        headers.forEach(button => { button.disabled = value; });
        ok.disabled = value || !session.loaded || (!session.selected && !session.canSelectCurrent);
    }
    function selectedPresentation() {
        rows.querySelectorAll('[data-browser-index]').forEach(row => {
            const selected = session?.items[Number(row.dataset.browserIndex)] === session?.selected;
            row.classList.toggle('selected', selected);
            row.setAttribute('aria-selected', String(selected));
        });
        busy(Boolean(session?.busy));
    }
    function render() {
        const state = session;
        if (!state) return;
        rows.replaceChildren();
        find('empty').hidden = !state.loaded || state.items.length > 0;
        parent.hidden = !state.hasParent;
        headers.forEach(button => {
            const direction = state.sort.column === button.dataset.folderBrowserSort ? state.sort.direction : '';
            button.setAttribute('aria-sort', direction === 'asc' ? 'ascending' : direction === 'desc' ? 'descending' : 'none');
            button.classList.toggle('sorted', Boolean(direction));
            button.querySelector('.sort-indicator').textContent = direction === 'asc' ? '↑' : direction === 'desc' ? '↓' : '';
        });
        window.MauiFolderBrowserSorting.apply(state.items, state.sort).forEach(item => {
            const row = document.createElement('button');
            row.type = 'button'; row.className = 'maui-host-browser-row ' + (item.isFolder ? 'folder' : 'file');
            row.dataset.browserIndex = String(state.items.indexOf(item));
            row.dataset.path = item.path || '';
            row.setAttribute('role', 'option');
            const name = document.createElement('div'); name.className = 'maui-host-browser-name';
            const icon = document.createElement('span'); icon.setAttribute('aria-hidden', 'true'); icon.textContent = item.isFolder ? '📁' : '📄';
            const text = document.createElement('span'); text.textContent = item.name || '—';
            const size = document.createElement('div'); size.textContent = item.size ?? '';
            const modified = document.createElement('div'); modified.textContent = item.lastModified || '';
            name.append(icon, text); row.append(name, size, modified); rows.append(row);
        });
        selectedPresentation();
    }
    async function load(path) {
        const state = session;
        if (!state || state.busy) return;
        const request = ++generation;
        state.controller = new AbortController(); state.loaded = false; state.selected = null; state.items = [];
        rows.replaceChildren(); find('empty').hidden = true; find('error').hidden = true; busy(true);
        try {
            const data = await state.options.load(path, state.controller.signal);
            if (session !== state || request !== generation || !dialog.open) return;
            state.path = String(data.currentPath ?? path ?? '');
            state.parentPath = data.parentPath ?? '';
            state.hasParent = data.hasParent ?? Boolean(state.parentPath);
            state.canSelectCurrent = data.canSelectCurrent ?? true;
            state.items = Array.isArray(data.items) ? data.items : [];
            state.loaded = true;
            location.value = state.path;
            if (data.hostName) find('host').textContent = data.hostName;
        } catch (error) {
            if (session !== state || request !== generation || error?.name === 'AbortError') return;
            find('error').textContent = error?.message || 'Unable to browse this location.'; find('error').hidden = false;
            state.hasParent = false;
        } finally {
            if (session === state && request === generation) { busy(false); render(); }
        }
    }
    function accept() {
        const state = session;
        if (!state || state.busy || !state.loaded || state.options.inspect || ok.disabled) return;
        const item = state.selected;
        // File names are literal native names. A folder-only selection never supplies a filename.
        const result = { path: item?.isFolder ? item.path : state.path, fileName: item && !item.isFolder ? item.name : null, item };
        state.options.onSelect?.(result);
        if (session === state) close();
    }
    async function activate(item) {
        const state = session;
        if (!state || state.busy) return;
        state.selected = item; selectedPresentation();
        if (!item.isFolder) { if (!state.options.inspect) accept(); return; }
        if (state.options.selector) { accept(); return; }
        const path = state.options.openFolder ? state.options.openFolder(item) : item.path;
        await load(path);
    }
    rows.setAttribute('role', 'listbox'); rows.setAttribute('aria-label', 'Folders and files');
    rows.addEventListener('click', event => {
        const row = event.target.closest('[data-browser-index]');
        if (!row || !session || session.busy) return;
        // Retain the row node between clicks so native dblclick recognition works.
        session.selected = session.items[Number(row.dataset.browserIndex)]; selectedPresentation();
    });
    rows.addEventListener('dblclick', event => {
        const row = event.target.closest('[data-browser-index]');
        if (row && session && !session.busy) { event.preventDefault(); void activate(session.items[Number(row.dataset.browserIndex)]); }
    });
    rows.addEventListener('keydown', event => {
        const row = event.target.closest('[data-browser-index]');
        if (event.key === 'Enter' && row && session && !session.busy) { event.preventDefault(); void activate(session.items[Number(row.dataset.browserIndex)]); }
    });
    headers.forEach(button => button.addEventListener('click', () => {
        if (!session || session.busy) return;
        session.sort = window.MauiFolderBrowserSorting.next(session.sort, button.dataset.folderBrowserSort); render();
    }));
    find('refresh').addEventListener('click', () => void load(location.value));
    location.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); void load(location.value); } });
    parent.addEventListener('click', () => {
        if (session && !session.busy) void load(session.options.parent ? session.options.parent(session.parentPath) : session.parentPath);
    });
    ok.addEventListener('click', accept);
    dialog.querySelectorAll('[data-host-browser-cancel]').forEach(button => button.addEventListener('click', close));
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
    dialog.addEventListener('close', () => { if (!dialog.open && session) close(); });
    window.MauiHostBrowser = {
        open(options) {
            if (session || dialog.open || typeof options?.load !== 'function') return null;
            const state = { options, items: [], selected: null, loaded: false, busy: false, sort: { column: '', direction: '' } };
            session = state;
            find('title').textContent = options.title || 'Browse Host'; find('host').textContent = options.hostName || '';
            find('hint').textContent = options.hint ?? defaultHint; find('hint').hidden = options.inspect || options.hint === '';
            ok.hidden = Boolean(options.inspect); location.readOnly = Boolean(options.selector);
            location.value = options.path ?? ''; parent.hidden = true; find('error').hidden = true;
            dialog.showModal(); void load(options.path ?? '');
            return { close: () => { if (session === state) close(); }, isOpen: () => session === state && dialog.open };
        }
    };
})();
