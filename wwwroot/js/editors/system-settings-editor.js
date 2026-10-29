/* Native definitions stay server-side. Only typed sections and opaque revisions cross the browser boundary. */
(() => {
    'use strict';
    const dialog = document.querySelector('[data-system-settings-editor]');
    if (!dialog) return;
    const find = name => dialog.querySelector('[data-system-' + name + ']');
    const fields = [...dialog.querySelectorAll('[data-system-field]')];
    const tabs = [...dialog.querySelectorAll('[data-system-tab]')];
    const field = name => fields.find(input => input.dataset.systemField === name);
    const groups = ['logging', 'eventLog', 'tasks', 'stateFile', 'tamper', 'lockout', 'trustedIps'];
    const numberSelects = ['debugLevel', 'eventLogLevel', 'eventLogAuditLevel'];
    const maximumBytes = 2147483648;
    let baseline = {}, editVersion = '', lockoutVersion = '', trustedVersion = '', busy = false, blocked = false;
    let returnFocus = null, browser = null, feedbackError = false, saving = false, activeTab = 'logging', lockouts = null, blockedMessage = '';

    function message(text, error = false) {
        feedbackError = error;
        find('status').textContent = text;
        find('status').classList.toggle('error', error);
    }
    const cacheMode = () => dialog.querySelector('[data-system-cache-mode]:checked')?.value;
    const expiry = prefix => dialog.querySelector('[data-system-auto="' + prefix + '"]:checked')?.value;
    const trustedInputs = () => [...dialog.querySelectorAll('[data-system-trusted-address]')];
    const groupFields = name => fields.filter(input => input.dataset.systemGroup === name);
    function values(name) {
        if (name === 'trustedIps') return trustedInputs().map(input => input.value.trim());
        if (name === 'stateFile') return {
            defStateCacheTime: cacheMode() === 'always' ? -1 : cacheMode() === 'task' ? 0 : find('cache-time').value === '' ? NaN : Number(find('cache-time').value),
            defStateCacheUnit: cacheMode() === 'timed' ? find('cache-unit').value : null
        };
        const value = {};
        groupFields(name).forEach(input => {
            value[input.dataset.systemField] = input.type === 'checkbox' ? input.checked
                : input.type === 'number' || numberSelects.includes(input.dataset.systemField) ? input.value === '' ? NaN : Number(input.value) : input.value;
        });
        if (name === 'logging') value.maxLogFileSize = find('size').value === '' ? NaN : Number(find('size').value) * Number(find('unit').value);
        if (name === 'lockout') { value.ipAutoUnlockEnabled = expiry('ip') === 'timed'; value.userAutoUnlockEnabled = expiry('user') === 'timed'; }
        return value;
    }
    function draft(name) {
        // Raw numeric input distinguishes blank/invalid text from zero; dormant cache inputs
        // do not create an extra change when an Always/Until end radio is selected.
        if (name === 'trustedIps') return JSON.stringify(trustedInputs().map(input => input.value));
        if (name === 'stateFile') return JSON.stringify([cacheMode(), ...(cacheMode() === 'timed' ? [find('cache-time').value, find('cache-unit').value] : [])]);
        const raw = groupFields(name).map(input => input.type === 'checkbox' ? input.checked : input.value);
        if (name === 'logging') raw.push(find('size').value, find('unit').value);
        if (name === 'lockout') raw.push(expiry('ip'), expiry('user'));
        return JSON.stringify(raw);
    }
    const changedGroups = () => groups.filter(name => baseline[name] !== undefined && draft(name) !== baseline[name]);
    const dirty = () => changedGroups().length > 0;
    const integer = (number, min = 0) => Number.isInteger(number) && number >= min && number <= 2147483647;
    function ipKey(text) {
        if (/^(?:\d{1,3}\.){3}\d{1,3}$/.test(text)) return text.split('.').every(part => Number(part) <= 255 && (part === '0' || !part.startsWith('0'))) ? text : '';
        if (!text.includes(':') || /[\s\[\]%]/.test(text)) return '';
        try { return new URL('http://[' + text + ']/').hostname.toLowerCase(); } catch { return ''; }
    }
    function validation(name, value) {
        if (name === 'logging') {
            if (![0,10,20,30,40,50,60,70].includes(value.debugLevel)) return 'Select a supported System Log Level.';
            if (!Number.isSafeInteger(value.maxLogFileSize) || value.maxLogFileSize < 0 || value.maxLogFileSize > maximumBytes || value.logToDisk && value.maxLogFileSize === 0)
                return 'Maximum System Log File Size must be greater than zero when logging to disk and cannot exceed 2 GB.';
            if (value.logToDisk && !value.logFile.trim()) return 'Enter a System Log File Name.';
            if (!integer(value.numTaskLogsToKeep)) return 'Enter a valid Default Number of Task Logs Saved.';
            if ([value.logFile, value.taskLogDir, value.as2LogDirectory].some(text => /[\u0000-\u001f\u007f]/.test(text))) return 'Log file names and directories cannot contain control characters.';
        }
        if (name === 'eventLog' && (!['Application','MOVEit'].includes(value.eventLogName) || ![0,10,20,30,40].includes(value.eventLogLevel) || ![0,10,30].includes(value.eventLogAuditLevel))) return 'Select supported Windows Event Log settings.';
        if (name === 'tasks' && (!integer(value.maxSimulTasks, 1) || !integer(value.pollIntervalSecs) || !integer(value.as2PollIntervalSecs))) return 'Enter a positive Maximum Running Tasks and nonnegative polling intervals.';
        if (name === 'stateFile' && (!integer(value.defStateCacheTime, -1) || cacheMode() === 'timed' && (!integer(value.defStateCacheTime, 1) || !['Minutes','Hours'].includes(value.defStateCacheUnit)))) return 'Enter a positive State File cache duration and select Minutes or Hours.';
        if (name === 'lockout') {
            for (const prefix of ['ip','user']) {
                const enabled = value[prefix + 'LockoutEnabled'], auto = value[prefix + 'AutoUnlockEnabled'];
                if (!integer(value[prefix + 'LockoutTries'], enabled ? 1 : 0) || !integer(value[prefix + 'LockoutTime'], enabled ? 1 : 0) || !integer(value[prefix + 'UnlockTime'], enabled && auto ? 1 : 0)) return 'Enter positive attempts and durations for enabled lockout settings.';
                if (!['hours','minutes','days'].includes(value[prefix + 'LockoutUnits']) || !['hours','minutes','days'].includes(value[prefix + 'UnlockUnits'])) return 'Select Hours, Minutes or Days for lockout settings.';
            }
        }
        if (name === 'trustedIps') {
            const keys = value.map(ipKey);
            if (keys.some(key => !key)) return 'Enter a valid IPv4 or IPv6 address for each Trusted IP Address.';
            if (new Set(keys).size !== keys.length) return 'Each Trusted IP Address must be unique.';
        }
        return '';
    }
    const errors = () => changedGroups().map(name => validation(name, values(name))).find(Boolean) || '';
    function controls() {
        dialog.querySelectorAll('[data-system-fields]').forEach(input => { input.disabled = busy || !editVersion || input.dataset.systemFields === 'lockout' && !lockoutVersion; });
        field('logFile').disabled = !field('logToDisk').checked;
        find('size').disabled = find('unit').disabled = !field('logToDisk').checked;
        field('taskLogDir').disabled = field('numTaskLogsToKeep').disabled = !field('logToTaskLog').checked;
        dialog.querySelector('[data-system-browse="taskLogDir"]').disabled = !field('logToTaskLog').checked;
        find('cache-time').disabled = find('cache-unit').disabled = cacheMode() !== 'timed';
        for (const prefix of ['ip','user']) {
            const enabled = field(prefix + 'LockoutEnabled').checked;
            ['LockoutTries','LockoutTime','LockoutUnits'].forEach(key => { field(prefix + key).disabled = !enabled; });
            dialog.querySelectorAll('[data-system-auto="' + prefix + '"]').forEach(input => { input.disabled = !enabled; });
            ['UnlockTime','UnlockUnits'].forEach(key => { field(prefix + key).disabled = !enabled || expiry(prefix) !== 'timed'; });
            dialog.querySelector('[data-system-lockout-status="' + prefix + '"]').textContent = enabled ? 'Enabled' : 'Disabled';
        }
        find('save').disabled = busy || blocked || !dirty() || Boolean(errors());
        find('save').textContent = saving ? 'Saving...' : 'Save';
        find('reload').hidden = !blocked && Boolean(editVersion);
        find('reload').disabled = busy;
        dialog.querySelectorAll('[data-system-close]').forEach(button => { button.disabled = busy; });
        lockouts?.updateControls();
    }
    function changed() {
        if (!editVersion || busy) return;
        if (!blocked) message(errors() || (dirty() ? 'Unsaved changes' : 'Saved'), Boolean(errors()));
        controls();
    }
    function populateFields(name, value) {
        if (!value) throw new Error('MOVEit returned incomplete MOVEit Settings. Reload MOVEit Settings.');
        groupFields(name).forEach(input => {
            const saved = value[input.dataset.systemField];
            const expected = input.type === 'checkbox' ? 'boolean' : input.type === 'number' || numberSelects.includes(input.dataset.systemField) ? 'number' : 'string';
            if (typeof saved !== expected || expected === 'number' && !integer(saved)) throw new Error('MOVEit returned incomplete or invalid MOVEit Settings.');
            if (input.tagName === 'SELECT' && ![...input.options].some(option => option.value === String(saved))) throw new Error(name === 'logging' ? 'MOVEit returned an unsupported System Log Level. Use the native editor to inspect these settings.' : 'MOVEit returned an unsupported MOVEit Settings option. Use the native editor to inspect these settings.');
        });
        groupFields(name).forEach(input => { const saved = value[input.dataset.systemField]; if (input.type === 'checkbox') input.checked = saved; else input.value = String(saved); });
    }
    function populate(state) {
        if (!state?.logging || typeof state.editVersion !== 'string' || !state.editVersion) throw new Error('MOVEit returned incomplete MOVEit Settings. Reload MOVEit Settings.');
        // Check the complete projection before any field can become savable.
        ['logging','eventLog','tasks','tamper'].forEach(name => populateFields(name, state[name]));
        const value = state.logging, cache = state.stateFile;
        if (!Number.isSafeInteger(value.maxLogFileSize) || value.maxLogFileSize < 0) throw new Error('MOVEit returned an invalid log file size.');
        if (!cache || !integer(cache.defStateCacheTime, -1) || cache.defStateCacheTime > 0 && !['Minutes','Hours'].includes(cache.defStateCacheUnit)) throw new Error('MOVEit returned invalid State File settings.');
        const unit = [1073741824, 1048576, 1024].find(size => value.maxLogFileSize > 0 && value.maxLogFileSize % size === 0) || 1024;
        find('unit').value = String(unit); find('size').value = String(value.maxLogFileSize / unit);
        const mode = cache.defStateCacheTime === -1 ? 'always' : cache.defStateCacheTime === 0 ? 'task' : 'timed';
        dialog.querySelectorAll('[data-system-cache-mode]').forEach(input => { input.checked = input.value === mode; });
        find('cache-time').value = String(cache.defStateCacheTime > 0 ? cache.defStateCacheTime : 1);
        find('cache-unit').value = cache.defStateCacheUnit || 'Minutes';
        editVersion = state.editVersion;
        ['logging','eventLog','tasks','stateFile','tamper'].forEach(name => { baseline[name] = draft(name); });
        if (state.lockout) populateLockout(state.lockout);
        blocked = false; blockedMessage = ''; message('Saved');
    }
    function addTrusted(address = '') {
        const row = document.createElement('div'); row.className = 'system-settings-trusted-row';
        const input = document.createElement('input'); input.type = 'text'; input.className = 'text-input'; input.value = address;
        input.dataset.systemTrustedAddress = ''; input.setAttribute('aria-label', 'Trusted IP Address'); input.autocomplete = 'off'; input.spellcheck = false;
        const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'danger-button'; remove.textContent = 'Remove';
        remove.addEventListener('click', () => { row.remove(); changed(); find('trusted-add').focus(); });
        input.addEventListener('input', changed); row.append(input, remove); find('trusted-list').append(row); return input;
    }
    function populateLockout(state) {
        if (!state?.policy || !state.editVersion || !state.trustedEditVersion || !Array.isArray(state.trustedIps) || state.trustedIps.some(address => typeof address !== 'string' || !ipKey(address))) throw new Error('MOVEit returned incomplete IP/User Lockout settings. Reload MOVEit Settings.');
        if (typeof state.policy.ipAutoUnlockEnabled !== 'boolean' || typeof state.policy.userAutoUnlockEnabled !== 'boolean') throw new Error('MOVEit returned incomplete lockout expiration settings.');
        populateFields('lockout', state.policy);
        for (const prefix of ['ip','user']) dialog.querySelectorAll('[data-system-auto="' + prefix + '"]').forEach(input => { input.checked = (input.value === 'timed') === state.policy[prefix + 'AutoUnlockEnabled']; });
        find('trusted-list').replaceChildren(); state.trustedIps.forEach(address => addTrusted(address));
        lockoutVersion = state.editVersion; trustedVersion = state.trustedEditVersion;
        baseline.lockout = draft('lockout'); baseline.trustedIps = draft('trustedIps');
    }
    async function request(url, body, signal) {
        const token = document.querySelector('.maui-editor-routing-antiforgery input[name="__RequestVerificationToken"]')?.value;
        const response = await fetch(url, { method: 'POST', cache: 'no-store', credentials: 'same-origin', signal,
            headers: { Accept: 'application/json', 'Content-Type': 'application/json', RequestVerificationToken: token || '' }, body: JSON.stringify(body) });
        let data;
        try { data = await response.json(); }
        catch { throw Object.assign(new Error('MAUI could not read the MOVEit response. Reload MOVEit Settings before saving again.'), { saveBlocked: true }); }
        if (!response.ok) {
            const error = Object.assign(new Error(data.message || 'The MOVEit Settings request failed.'), { payload:data, saveBlocked:data.saveBlocked || response.status >= 500, refreshLockouts:data.refreshLockouts, status:response.status });
            try {
                if (await window.MauiObjectEditLease?.recoverConflict(error, 'MOVEit Settings', token)) {
                    error.message = window.MauiObjectEditLease.restoredFormMessage;
                    error.saveBlocked = true;
                }
            } catch (recoveryError) { error.message = recoveryError.message; error.saveBlocked = true; }
            throw error;
        }
        return data;
    }
    async function loadLockout() {
        if (!editVersion || lockoutVersion || busy || blocked) return;
        busy = true; controls(); message('Loading IP/User Lockout Policy...');
        try { const data = await request(dialog.dataset.lockoutUrl, {}); populateLockout(data.state); message(dirty() ? 'Unsaved changes' : 'Saved'); }
        catch (error) { blocked = true; blockedMessage = error.message || 'IP/User Lockout Policy could not be loaded.'; message(blockedMessage, true); }
        finally { busy = false; controls(); resumeTab(); }
    }
    function editorStatus() {
        const error = errors();
        message(blocked ? blockedMessage || 'Reload MOVEit Settings before saving again.' : error || (dirty() ? 'Unsaved changes' : 'Saved'), blocked || Boolean(error));
    }
    function resumeTab() {
        if (busy || !dialog.open) return;
        if (activeTab === 'lockout' && !lockoutVersion && !blocked) void loadLockout();
        if (activeTab === 'active-lockouts') lockouts?.resume();
    }
    function tab(name, focus = false) {
        activeTab = name;
        tabs.forEach(button => { const active = button.dataset.systemTab === name; button.classList.toggle('active', active); button.setAttribute('aria-selected', String(active)); button.tabIndex = active ? 0 : -1; if (active && focus) button.focus(); });
        dialog.querySelectorAll('[data-system-panel]').forEach(panel => { panel.hidden = panel.dataset.systemPanel !== name; });
        find('active-actions').hidden = name !== 'active-lockouts';
        find('body').scrollTop = 0;
        if (!busy && name !== 'active-lockouts') editorStatus();
        if (name === 'lockout') void loadLockout();
        if (name === 'active-lockouts') lockouts?.enter();
    }
    async function load() {
        const withLockout = Boolean(lockoutVersion) || activeTab === 'lockout';
        busy = true; lockouts?.reset(); controls(); message('Loading MOVEit Settings...');
        try {
            const data = await request(dialog.dataset.shellUrl, {}); populate(data.state);
            lockoutVersion = trustedVersion = ''; delete baseline.lockout; delete baseline.trustedIps;
            if (withLockout || activeTab === 'lockout') { const policy = await request(dialog.dataset.lockoutUrl, {}); populateLockout(policy.state); }
            message('Saved');
        }
        catch (error) { blocked = true; blockedMessage = error.message || 'MOVEit Settings could not be loaded.'; message(blockedMessage, true); }
        finally { busy = false; controls(); resumeTab(); }
    }
    async function confirmDiscard(title, text, confirmText) {
        return !dirty() || await window.mauiConfirm?.({ title, message: text, confirmText, danger: true });
    }
    async function close() {
        if (busy || !await confirmDiscard('Discard MOVEit Settings Changes?', 'Close this editor and discard unsaved MOVEit Settings changes?', 'Discard Changes')) return;
        browser?.close(); browser = null;
        dialog.close(); baseline = {}; editVersion = lockoutVersion = trustedVersion = ''; lockouts?.reset(); returnFocus?.focus();
    }
    async function browse(name) {
        if (busy || !editVersion || browser?.isOpen()) return;
        const input = field(name);
        browser = window.MauiHostBrowser?.open({ title: 'Browse Folders', hostName: 'Local File System', path: input.value,
            hint: 'Double-click a folder to open it. Select a folder and click OK.',
            load: async (directory, signal) => {
                const data = await request(dialog.dataset.browseUrl, { directory, rootName: '', rootId: '' }, signal);
                return { currentPath: data.currentDirectory, parentPath: data.parentDirectory,
                    items: (data.items || []).filter(item => item.isDirectory).map(item => ({ ...item, isFolder: true })) };
            },
            onSelect: selection => { input.value = selection.path; changed(); }, onClose: () => { browser = null; input.focus(); }
        });
        if (!browser) message('The folder browser is unavailable. You can enter the directory directly.', true);
    }
    find('form').addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || blocked || !dirty()) return;
        const error = errors();
        if (error) { message(error, true); controls(); return; }
        const body = { editVersion };
        changedGroups().forEach(name => { body[name] = values(name); });
        if (body.lockout) body.lockoutVersion = lockoutVersion;
        if (body.trustedIps) body.trustedVersion = trustedVersion;
        busy = saving = true; controls(); message('Saving...');
        try { const data = await request(dialog.dataset.saveUrl, body); populate(data.state); }
        catch (error) {
            // Never replay a native mutation after a lost/invalid response; retain local inputs.
            blocked = Boolean(error.saveBlocked || !error.status);
            blockedMessage = error.status ? error.message : 'The save result could not be confirmed. Reload MOVEit Settings before saving again.';
            message(blockedMessage, true);
        } finally {
            busy = saving = false; controls();
            resumeTab();
        }
    });
    lockouts = window.MauiSystemLockouts?.create({ dialog, request, isBusy: () => busy,
        setBusy: value => { busy = value; controls(); }, report: message, settled: resumeTab });
    fields.forEach(input => { input.addEventListener('input', changed); input.addEventListener('change', changed); });
    [find('size'), find('unit'), find('cache-time'), find('cache-unit'), ...dialog.querySelectorAll('[data-system-cache-mode], [data-system-auto]')].forEach(input => { input.addEventListener('input', changed); input.addEventListener('change', changed); });
    find('trusted-add').addEventListener('click', () => { addTrusted().focus(); changed(); });
    dialog.querySelectorAll('[data-system-close]').forEach(button => button.addEventListener('click', () => void close()));
    dialog.addEventListener('cancel', event => { event.preventDefault(); void close(); });
    find('reload').addEventListener('click', async () => {
        if (!busy && await confirmDiscard('Reload MOVEit Settings?', 'Discard unsaved changes and load the current MOVEit Settings from MOVEit?', 'Reload MOVEit Settings')) await load();
    });
    tabs.forEach((button, index) => {
        button.addEventListener('click', () => tab(button.dataset.systemTab));
        button.addEventListener('keydown', event => {
            const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : event.key === 'ArrowRight' ? (index + 1) % tabs.length : event.key === 'ArrowLeft' ? (index + tabs.length - 1) % tabs.length : -1;
            if (next >= 0) { event.preventDefault(); tab(tabs[next].dataset.systemTab, true); }
        });
    });
    dialog.querySelectorAll('[data-system-browse]').forEach(button => button.addEventListener('click', () => void browse(button.dataset.systemBrowse)));
    window.MauiModalUI?.register?.(dialog, { error: text => message(text, true), clear: () => { if (feedbackError) changed(); }, footerStatus: find('status') });
    document.addEventListener('click', event => {
        const link = event.target.closest('[data-system-settings-open]');
        if (!link || document.documentElement.dataset.useMauiSystemSettingsEditor !== 'true') return;
        event.preventDefault(); event.stopImmediatePropagation();
        if (dialog.open) return;
        returnFocus = link; baseline = {}; editVersion = lockoutVersion = trustedVersion = ''; blocked = false; blockedMessage = ''; lockouts?.reset(); tab('logging');
        dialog.showModal(); void load();
    }, true);
    window.addEventListener('beforeunload', event => { if (dialog.open && (dirty() || busy)) { event.preventDefault(); event.returnValue = ''; } });
})();
