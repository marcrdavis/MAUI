/* Read-only, on-demand audit comparison; keep request/render state out of the Records grid lifecycle. */
(() => {
    'use strict';
    const dialog = document.querySelector('[data-audit-changes-dialog]');
    if (!dialog) return;
    const context = dialog.querySelector('[data-audit-changes-context]');
    const body = dialog.querySelector('[data-audit-changes-body]');
    const state = dialog.querySelector('[data-audit-changes-state]');
    const table = dialog.querySelector('[data-audit-changes-table]');
    const lines = dialog.querySelector('[data-audit-changes-lines]');
    const summary = dialog.querySelector('[data-audit-changes-summary]');
    const errorMessage = dialog.querySelector('[data-audit-changes-error]');
    let sequence = 0;
    let activeRequest = null;

    function reset() {
        window.MauiModalUI?.clear(dialog);
        lines.replaceChildren(); summary.replaceChildren();
        summary.hidden = true; table.hidden = true; state.hidden = false;
        state.textContent = 'Loading configuration changes…';
        errorMessage.textContent = ''; errorMessage.hidden = true;
        body.scrollTop = 0; body.scrollLeft = 0;
    }

    function showError(message) {
        if (window.MauiModalUI?.showMessage(message, 'error', dialog)) return;
        errorMessage.textContent = message; errorMessage.hidden = false;
        window.mauiLogNotification?.(message, 'error', 'modal');
    }

    function describe(data) {
        for (const [kind, number, title] of [['added', data.added, 'added'], ['removed', data.removed, 'removed']]) {
            const label = document.createElement('span'); label.className = `audit-changes-${kind}`;
            label.textContent = `${kind === 'added' ? '+' : '−'} ${number} ${title}`; summary.append(label);
        }
        const note = document.createElement('p'); note.className = 'audit-changes-note';
        note.textContent = 'Values are displayed as recorded by MOVEit. Protected values are shown as returned by MOVEit.';
        if (data.grouped) note.textContent += ' Some large changes are grouped to keep the comparison responsive.';
        if (data.plainText) note.textContent += ' XML formatting was unavailable; the original text is compared.';
        summary.append(note); summary.hidden = false;
    }

    async function open(logId, name = '') {
        if (!/^[1-9]\d*$/.test(logId)) return;
        activeRequest?.abort();
        const request = new AbortController(); activeRequest = request;
        const mine = ++sequence;
        reset(); context.textContent = `Audit record ${logId}${name ? ` · ${name}` : ''}`;
        if (!dialog.open) dialog.showModal();
        body.setAttribute('aria-busy', 'true');
        const current = () => mine === sequence && dialog.open && !request.signal.aborted;
        try {
            const url = new URL(dialog.dataset.detailsUrl, window.location.href);
            url.searchParams.set('logId', logId); // Preserve large numeric IDs as text in the browser.
            const response = await window.fetch(url.toString(), { credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json' }, signal: request.signal });
            if (!current()) return;
            if (response.status === 401 || response.redirected)
                throw new Error('Your MOVEit session has expired. Sign in again.');
            const data = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
            if (!current()) return;
            if (!response.ok) throw new Error(data?.message || 'Configuration changes could not be loaded. Close and try again.');
            if (!data || !Array.isArray(data.lines) || data.lines.length > 40000
                || !Number.isSafeInteger(data.added) || !Number.isSafeInteger(data.removed))
                throw new Error('MOVEit returned an invalid audit comparison.');
            // Yield between chunks so large, valid snapshots do not freeze Close or paint stale results.
            for (let start = 0; start < data.lines.length; start += 200) {
                if (!current()) return;
                const fragment = document.createDocumentFragment();
                for (const line of data.lines.slice(start, start + 200)) {
                    if (!['same', 'added', 'removed'].includes(line.kind) || typeof line.text !== 'string')
                        throw new Error('MOVEit returned an invalid audit comparison.');
                    const row = document.createElement('tr'); row.className = `audit-line-${line.kind}`;
                    for (const value of [line.before ?? '', line.after ?? '', line.kind === 'added' ? '+' : line.kind === 'removed' ? '−' : '', line.text]) {
                        const cell = document.createElement('td'); cell.textContent = String(value); row.append(cell);
                    }
                    fragment.append(row);
                }
                lines.append(fragment);
                if (start + 200 < data.lines.length) await new Promise(window.requestAnimationFrame);
            }
            if (!current()) return;
            describe(data); table.hidden = data.lines.length === 0;
            state.hidden = data.lines.length > 0;
            state.textContent = 'No configuration differences were recorded.';
        } catch (error) {
            if (!current()) return;
            lines.replaceChildren(); table.hidden = true; state.hidden = true;
            showError(error?.message || 'Configuration changes could not be loaded. Close and try again.');
        } finally {
            if (mine === sequence) { activeRequest = null; body.setAttribute('aria-busy', 'false'); }
        }
    }

    dialog.addEventListener('close', () => {
        if (dialog.open) return; // Ignore an older queued close event after a newer open.
        sequence++; activeRequest?.abort(); activeRequest = null;
        body.setAttribute('aria-busy', 'false');
        // Snapshots stay only in this open view; closing does not cache configuration contents.
        reset();
    });
    window.MauiAuditChanges = { open };
})();
