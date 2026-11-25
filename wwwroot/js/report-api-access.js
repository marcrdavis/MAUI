// Shared API Access controls for MAUI's Create and Edit Report dialogs.
(() => {
    'use strict';
    function create(dialog, config, createMode = false) {
        const section = dialog?.querySelector('[data-report-api-section]');
        if (!config || !section) return null;
        const enabled = section.querySelector('[data-report-api-enabled]');
        const fields = section.querySelector('[data-report-api-link-fields]');
        const url = section.querySelector('[data-report-api-url]');
        const copy = section.querySelector('[data-report-api-copy]');
        const regenerate = section.querySelector('[data-report-api-regenerate]');
        const spinner = section.querySelector('[data-report-api-spinner]');
        const status = section.querySelector('[data-report-api-status]');
        const lastAccess = section.querySelector('[data-report-api-last-access]');
        const save = dialog.querySelector(createMode ? '[data-report-submit]' : '[data-report-edit-save]');
        let report = null;
        let saved = null;
        let draft = null;
        let busy = false;
        let generation = 0;

        async function request(address, body) {
            const headers = { Accept: 'application/json' };
            const options = { headers, credentials: 'same-origin' };
            if (body) {
                options.method = 'POST';
                headers['Content-Type'] = 'application/json';
                headers.RequestVerificationToken = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
                options.body = JSON.stringify(body);
            }
            const response = await fetch(address, options);
            if (!response.headers.get('content-type')?.includes('json'))
                throw new Error('Reload Reports and sign in again to manage API access.');
            const result = await response.json();
            if (!response.ok) throw new Error(result.message || 'API access could not be updated.');
            return result.apiAccess;
        }

        const needsLink = () => enabled.checked && !saved?.enabled && !draft;
        function render() {
            enabled.disabled = busy || !saved;
            spinner.hidden = !busy;
            save.disabled = busy || !saved || needsLink();
            fields.hidden = !enabled.checked;
            const path = draft?.path || saved?.path;
            url.value = path ? new URL(config.dataset.appBase + path.slice(1), window.location.origin).href : '';
            copy.disabled = busy || !enabled.checked || !url.value;
            regenerate.disabled = busy || !enabled.checked || !saved;
            lastAccess.hidden = createMode;
            lastAccess.textContent = saved?.lastAccessUtc ? 'Last API access: ' + new Date(saved.lastAccessUtc).toLocaleString() : 'Last API access: Never';
        }

        async function open(value) {
            const current = ++generation;
            report = value;
            saved = null;
            draft = null;
            enabled.checked = false;
            if (createMode) {
                saved = { enabled: false, revision: '' };
                busy = false;
                status.textContent = '';
                render();
                return;
            }
            busy = true;
            status.textContent = 'Loading API access…';
            render();
            try {
                const result = await request(config.dataset.apiAccessUrl + '?id=' + encodeURIComponent(value.id));
                if (generation !== current) return;
                saved = result;
                enabled.checked = Boolean(saved.enabled);
                status.textContent = saved.enabled && !saved.path ? 'Regenerate the link to restore access to its URL.' : '';
            } catch (error) {
                if (generation === current) status.textContent = error.message;
            } finally {
                if (generation === current) { busy = false; render(); }
            }
        }

        async function prepare(replacement = false) {
            const current = generation;
            busy = true;
            status.textContent = replacement ? 'Preparing replacement link…' : 'Preparing API link…';
            render();
            try {
                const result = await request(config.dataset.apiPrepareUrl, createMode ? {} : { id: report.id, revision: saved.revision });
                if (generation !== current) return;
                if (!result?.path || !result.linkDraft) throw new Error('The API link could not be prepared. Select Regenerate Link to retry.');
                draft = result;
                status.textContent = replacement ? 'Replacement link prepared. Save Report to activate it.' : 'Save Report to activate this link.';
            } catch (error) {
                if (generation === current) status.textContent = error.message;
            } finally {
                if (generation === current) { busy = false; render(); }
            }
        }

        enabled.addEventListener('change', async () => {
            status.textContent = '';
            if (needsLink()) await prepare();
            else {
                if (enabled.checked && draft) status.textContent = 'Save Report to activate this link.';
                render();
            }
        });
        copy.addEventListener('click', async () => {
            try { await navigator.clipboard.writeText(url.value); status.textContent = draft ? 'Link copied. Save Report to activate it.' : 'Link copied.'; }
            catch { url.focus(); url.select(); status.textContent = 'Select and copy the Report URL.'; }
        });
        regenerate.addEventListener('click', async () => {
            if (busy || !saved || !enabled.checked) return;
            if (draft || !saved.enabled) { await prepare(true); return; }
            const current = generation;
            const confirmed = await window.mauiConfirm?.({
                title: 'Regenerate Report Link?', message: 'The current API link will stop working. Share the replacement link with its consumers.',
                confirmText: 'Regenerate Link', danger: true
            });
            if (!confirmed || generation !== current) return;
            busy = true;
            status.textContent = 'Regenerating link…';
            render();
            try {
                const result = await request(config.dataset.apiRegenerateUrl, { id: report.id, revision: saved.revision });
                if (generation !== current) return;
                saved = result;
                draft = null;
                enabled.checked = true;
                status.textContent = 'Link regenerated.';
            } catch (error) {
                if (generation === current) status.textContent = error.message;
            } finally {
                if (generation === current) { busy = false; render(); }
            }
        });
        dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
        dialog.addEventListener('close', () => {
            // A queued Close event can arrive after the same dialog has reopened.
            if (dialog.open) return;
            generation++; saved = null; draft = null; report = null; busy = false;
        });
        return {
            open, ready: () => !busy && Boolean(saved) && !needsLink(),
            state: () => saved ? { enabled: enabled.checked, revision: saved.revision,
                ...(enabled.checked && draft ? { linkDraft: draft.linkDraft } : {}) } : undefined
        };
    }
    const config = document.querySelector('[data-reports-page-config]');
    const edit = create(document.querySelector('[data-report-edit-dialog]'), config);
    window.MauiReportApiAccess = { create, ...edit };
})();
