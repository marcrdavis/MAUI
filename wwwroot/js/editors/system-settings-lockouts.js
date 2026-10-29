/* Operational Lockouts tab. It never participates in the MOVEit Settings draft or Globals save. */
(() => {
    'use strict';
    window.MauiSystemLockouts = {
        create({ dialog, request, isBusy, setBusy, report, settled }) {
            const refreshButton = dialog.querySelector('[data-system-active-refresh]');
            const sections = [...dialog.querySelectorAll('[data-system-lockouts-kind]')].map(root => ({
                root, kind: root.dataset.systemLockoutsKind, items: [], selected: new Set(), sort: { key: 'stamp', direction: -1 }
            }));
            let loaded = false, pending = false, recovery = false;
            const say = report;
            const section = kind => sections.find(item => item.kind === kind);
            function updateControls() {
                refreshButton.disabled = isBusy();
                for (const model of sections) {
                    const disabled = isBusy() || !loaded || recovery;
                    const all = model.root.querySelector('[data-system-lockouts-all]');
                    all.disabled = disabled || model.items.length === 0;
                    all.checked = model.items.length > 0 && model.selected.size === model.items.length;
                    all.indeterminate = model.selected.size > 0 && model.selected.size < model.items.length;
                    model.root.querySelector('[data-system-unlock-selected]').disabled = disabled || model.selected.size === 0;
                    model.root.querySelectorAll('[data-system-lockout-select], [data-system-unlock-row]').forEach(control => { control.disabled = disabled; });
                    model.root.querySelectorAll('[data-system-lockouts-sort]').forEach(control => { control.disabled = isBusy() || !loaded; });
                }
            }
            function empty(model, text, loading = false) {
                const row = document.createElement('tr'), cell = document.createElement('td'); cell.colSpan = 4; cell.className = 'system-settings-lockouts-empty';
                if (loading) { const spinner = document.createElement('span'); spinner.className = 'inline-spinner'; spinner.setAttribute('aria-hidden', 'true'); cell.append(spinner); }
                cell.append(document.createTextNode(text)); row.append(cell); model.root.querySelector('[data-system-lockouts-rows]').replaceChildren(row);
            }
            function render(model) {
                const rows = model.root.querySelector('[data-system-lockouts-rows]'); rows.replaceChildren();
                model.root.querySelector('[data-system-lockouts-count]').textContent = String(model.items.length);
                model.root.querySelectorAll('[data-system-lockouts-sort]').forEach(button => {
                    const active = button.dataset.systemLockoutsSort === model.sort.key;
                    button.closest('th').setAttribute('aria-sort', !active || !model.sort.direction ? 'none' : model.sort.direction > 0 ? 'ascending' : 'descending');
                    button.querySelector('.sort-indicator').textContent = active ? model.sort.direction > 0 ? '↑' : model.sort.direction < 0 ? '↓' : '' : '';
                });
                const items = model.items.map((item, index) => ({ item, index }));
                if (model.sort.direction) items.sort((a, b) => {
                    // Native Stamp is an offset-free YYYY-MM-DD HH:mm:ss string. Keep its
                    // displayed time intact and sort that captured representation directly.
                    const first = a.item[model.sort.key], second = b.item[model.sort.key];
                    return model.sort.direction * first.localeCompare(second, undefined, { numeric: true, sensitivity: 'base' }) || a.index - b.index;
                });
                for (const { item } of items) {
                    const row = document.createElement('tr'), selection = document.createElement('td'); selection.className = 'select-column';
                    const checkbox = document.createElement('input'); checkbox.type = 'checkbox'; checkbox.checked = model.selected.has(item.value);
                    checkbox.dataset.systemLockoutSelect = ''; checkbox.setAttribute('aria-label', 'Select ' + item.value);
                    checkbox.addEventListener('change', () => { if (checkbox.checked) model.selected.add(item.value); else model.selected.delete(item.value); updateControls(); });
                    selection.append(checkbox); row.append(selection);
                    for (const key of ['value', 'stamp']) { const cell = document.createElement('td'); cell.textContent = item[key]; cell.title = item[key]; row.append(cell); }
                    const action = document.createElement('td'), unlock = document.createElement('button'); unlock.type = 'button'; unlock.className = 'secondary-button'; unlock.textContent = 'Unlock';
                    unlock.dataset.systemUnlockRow = ''; unlock.setAttribute('aria-label', 'Unlock ' + item.value);
                    unlock.addEventListener('click', () => void unlockItems(model, [item])); action.append(unlock); row.append(action); rows.append(row);
                }
                if (!items.length) empty(model, model.kind === 'ip' ? 'No locked out IP addresses.' : 'No locked out users.');
                updateControls();
            }
            function validateItems(items) {
                if (!Array.isArray(items) || items.some(item => !item || typeof item.value !== 'string' || !item.value || typeof item.stamp !== 'string' || !item.stamp || typeof item.editVersion !== 'string' || !item.editVersion)
                    || new Set(items.map(item => item.value)).size !== items.length) throw new Error('MOVEit returned an incomplete Lockouts list. Refresh Lockouts.');
                return items;
            }
            function finish() {
                pending = false; setBusy(false); updateControls(); settled();
            }
            async function load() {
                if (isBusy() || pending) return;
                pending = true; loaded = false; setBusy(true); say('Loading Lockouts...');
                sections.forEach(model => { model.selected.clear(); model.root.querySelector('[data-system-lockouts-list]').setAttribute('aria-busy', 'true'); empty(model, 'Loading Lockouts...', true); });
                try {
                    const data = await request(dialog.dataset.activeLockoutsUrl, {});
                    const addresses = validateItems(data.state?.addresses), users = validateItems(data.state?.users);
                    section('ip').items = addresses; section('user').items = users; loaded = true; recovery = false;
                    sections.forEach(render); say('Lockouts refreshed.');
                } catch (error) {
                    recovery = true; sections.forEach(model => { model.items = []; model.root.querySelector('[data-system-lockouts-count]').textContent = '—'; empty(model, 'Lockouts could not be loaded.'); });
                    say(error.status ? error.message : 'Lockouts could not be loaded. Refresh Lockouts to try again.', true);
                } finally {
                    sections.forEach(model => model.root.querySelector('[data-system-lockouts-list]').setAttribute('aria-busy', 'false')); finish();
                }
            }
            function confirmation(model, items) {
                const singular = model.kind === 'ip' ? 'IP address' : 'user', plural = model.kind === 'ip' ? 'IP addresses' : 'users';
                return items.length === 1 ? {
                    title: 'Unlock ' + (model.kind === 'ip' ? 'IP Address' : 'User') + '?',
                    message: 'Unlock ' + singular + ' “' + items[0].value + '” in MOVEit?', confirmText: 'Unlock'
                } : { title: 'Unlock ' + (model.kind === 'ip' ? 'IP Addresses' : 'Users') + '?', message: 'Unlock the ' + items.length + ' selected ' + plural + ' in MOVEit?', confirmText: 'Unlock Selected' };
            }
            async function unlockItems(model, items) {
                if (isBusy() || pending || !loaded || recovery || !items.length) return;
                pending = true; setBusy(true);
                let attempted = false;
                try {
                    if (typeof window.mauiConfirm !== 'function') throw new Error('The confirmation dialog is unavailable. Refresh MAUI and try again.');
                    if (!await window.mauiConfirm(confirmation(model, items))) return;
                    attempted = true; say('Unlocking...');
                    const data = await request(dialog.dataset.unlockUrl, { kind: model.kind, selections: items.map(item => ({ value: item.value, editVersion: item.editVersion })) });
                    const result = data.state;
                    if (!result || result.kind !== model.kind || !Number.isInteger(result.unlocked) || !Number.isInteger(result.alreadyUnlocked)
                        || result.unlocked < 0 || result.alreadyUnlocked < 0 || result.unlocked + result.alreadyUnlocked !== items.length) throw new Error('The unlock result could not be confirmed. Refresh Lockouts before trying again.');
                    const saved = validateItems(result.items);
                    if (saved.some(item => items.some(selected => selected.value === item.value))) throw new Error('The unlock result could not be confirmed. Refresh Lockouts before trying again.');
                    model.items = saved; model.selected.clear(); render(model);
                    const label = number => model.kind === 'ip' ? number === 1 ? 'IP address' : 'IP addresses' : number === 1 ? 'user' : 'users';
                    const text = result.unlocked ? 'Unlocked ' + result.unlocked + ' ' + label(result.unlocked) + '.' : '';
                    const already = result.alreadyUnlocked ? result.alreadyUnlocked + ' ' + label(result.alreadyUnlocked) + (result.alreadyUnlocked === 1 ? ' was' : ' were') + ' already unlocked.' : '';
                    say([text, already].filter(Boolean).join(' '));
                } catch (error) {
                    // Never repeat an ambiguous DELETE. A fresh list produces a new explicit selection.
                    recovery = Boolean(attempted && (error.refreshLockouts || !error.status || error.status >= 500));
                    say(attempted && !error.status ? 'The unlock result could not be confirmed. Refresh Lockouts and review the list before trying again.' : error.message || 'Unlocking failed. Refresh Lockouts and try again.', true);
                } finally { finish(); }
            }
            sections.forEach(model => {
                model.root.querySelector('[data-system-lockouts-all]').addEventListener('change', event => { model.selected = new Set(event.target.checked ? model.items.map(item => item.value) : []); render(model); });
                model.root.querySelector('[data-system-unlock-selected]').addEventListener('click', () => void unlockItems(model, model.items.filter(item => model.selected.has(item.value))));
                model.root.querySelectorAll('[data-system-lockouts-sort]').forEach(button => button.addEventListener('click', () => {
                    if (isBusy() || !loaded) return;
                    const key = button.dataset.systemLockoutsSort;
                    model.sort = { key, direction: model.sort.key !== key || model.sort.direction === 0 ? 1 : model.sort.direction === 1 ? -1 : 0 }; render(model);
                }));
            });
            refreshButton.addEventListener('click', () => void load());
            return {
                updateControls,
                enter() { loaded = false; recovery = false; void load(); },
                resume() { if (!loaded && !recovery && !pending && !isBusy()) void load(); },
                reset() { loaded = pending = recovery = false; sections.forEach(model => { model.items = []; model.selected.clear(); model.root.querySelector('[data-system-lockouts-count]').textContent = '—'; empty(model, 'Select Lockouts to load the list.'); }); updateControls(); }
            };
        }
    };
})();
