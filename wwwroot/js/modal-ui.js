/* Shared modal feedback and return context. Native definitions and secrets are never persisted here. */
(() => {
    'use strict';
    const registrations = new WeakMap();
    const contexts = new Map();
    const baselines = new WeakMap();
    const saveActions = new WeakMap();
    const opened = new WeakMap();
    let sequence = 0;

    function activeDialog() {
        const dialogs = Array.from(document.querySelectorAll('dialog[open]'));
        const focused = document.activeElement?.closest?.('dialog[open]');
        if (!dialogs.some(dialog => opened.has(dialog))) return focused || dialogs.at(-1) || null;
        return dialogs.sort((a, b) => (opened.get(a) || 0) - (opened.get(b) || 0)).at(-1)
            || focused || null;
    }

    function clear(dialog) {
        const feedback = registrations.get(dialog);
        feedback?.clear?.();
        dialog.querySelectorAll('[data-maui-modal-feedback]').forEach(node => { node.hidden = true; node.textContent = ''; });
    }

    // Every severity stays above the backdrop. Page updates get a separate labelled
    // footer surface, preserving the editor's own save/error state.
    function showMessage(message, kind = 'default', dialog = activeDialog(), origin = 'modal', summary = '') {
        if (!dialog?.open) return false;
        // Record the complete diagnostic before choosing a short footer summary.
        window.mauiLogNotification?.(message, kind, origin);
        const isError = kind === 'error';
        const registered = origin === 'page' ? null : registrations.get(dialog);
        if (origin === 'page') message = `Page update: ${String(message || '')}`;
        if (isError && registered?.error) {
            dialog.querySelectorAll('[data-maui-modal-feedback]').forEach(node => { if (node.closest('dialog') === dialog) node.hidden = true; });
            registered.error(String(message || 'Unable to complete the operation.'));
            return true;
        }
        const feedback = Array.from(dialog.querySelectorAll('[data-maui-modal-feedback]')).filter(node => node.closest('dialog') === dialog && (node.dataset.feedbackOrigin || 'modal') === origin);
        // An informational update must not erase an unresolved error.
        if (!isError && feedback.some(node => node.dataset.feedbackKind === 'error' && !node.hidden)) return true;
        // Editors have fixed header/tab/workspace/footer rows. Reuse their existing status
        // field for action feedback; an inserted banner would shift those grid rows.
        if (!isError && registered?.footerStatus) {
            feedback.forEach(node => { node.hidden = true; });
            const status = registered.footerStatus;
            // Native footer errors (including a separate Host error field) take priority.
            if (!status.hidden && !status.classList.contains('error')) status.textContent = String(message || '');
            return true;
        }
        if (isError) feedback.filter(node => node.dataset.feedbackKind !== 'error').forEach(node => { node.hidden = true; });
        // Keep native error fields dedicated to errors; their owners also write them directly.
        const footer = Array.from(dialog.querySelectorAll('.settings-footer, .task-details-footer, footer')).find(node => node.closest('dialog') === dialog);
        let banner = feedback.find(node => (node.dataset.feedbackKind === 'error') === isError);
        if (!banner) {
            // Reuse an existing visible-panel error surface before creating a standard banner.
            banner = !footer && origin !== 'page' && isError && Array.from(dialog.querySelectorAll('.inline-alert.error-alert')).find(node =>
                node.closest('dialog') === dialog && !node.parentElement?.closest('[hidden]')
                && node.parentElement?.getClientRects().length);
            if (!banner) {
                banner = document.createElement('div');
                banner.className = 'inline-alert error-alert maui-modal-feedback';
                const header = dialog.querySelector('.settings-header, header, .task-details-header');
                if (footer) footer.prepend(banner);
                else if (header) header.insertAdjacentElement('afterend', banner);
                else (dialog.firstElementChild || dialog).prepend(banner);
            }
            banner.dataset.mauiModalFeedback = '';
        }
        const inFooter = footer && banner.parentElement === footer;
        const classes = (banner.className || '').split(/\s+/).filter(name => name && !['inline-alert','error-alert','warning-alert','info-alert'].includes(name));
        banner.className = Array.from(new Set([...classes, 'maui-modal-feedback', ...(inFooter ? [] :
            ['inline-alert', isError ? 'error-alert' : kind === 'warning' ? 'warning-alert' : 'info-alert'])])).join(' ');
        banner.dataset.feedbackKind = kind;
        banner.dataset.feedbackOrigin = origin;
        banner.setAttribute('role', isError ? 'alert' : 'status');
        banner.setAttribute('aria-live', isError ? 'assertive' : 'polite');
        const text = String(message || (isError ? 'Unable to complete the operation.' : ''));
        banner.textContent = inFooter ? footerSummary(summary ? (origin === 'page' ? `Page update: ${summary}` : summary) : text) : text;
        banner.hidden = false;
        return true;
    }

    function footerSummary(message) {
        const text = String(message || '').replace(/\s+/g, ' ').trim();
        return text.length <= 200 ? text : `${text.slice(0, 160).trimEnd()}… See MAUI logs for details.`;
    }

    function showError(message, dialog = activeDialog()) {
        return showMessage(message, 'error', dialog);
    }

    function signature(dialog) {
        return JSON.stringify(Array.from(dialog.querySelectorAll('input, textarea, select'))
            .filter(control => !control.disabled && !control.readOnly && (!['hidden','button','submit'].includes(control.type) || control.dataset.mauiMetadataTags === 'true' || control.hasAttribute('data-modal-value')))
            .map(control => [control.name, control.type, control.value, Boolean(control.checked)]));
    }

    function track(dialog) {
        if (!dialog) return;
        baselines.set(dialog, signature(dialog));
        saveActions.get(dialog)?.update();
    }

    // Quiet readiness checks: never reportValidity(), change values, or show errors while typing.
    // Existing submit validators remain authoritative. Hidden Tag values are real editable metadata.
    function formValid(root) {
        const email = document.createElement('input'); email.type = 'email';
        return Array.from(root.querySelectorAll('input,textarea,select')).every(control => {
            if (control.disabled || control.readOnly || ['button','submit','reset','file'].includes(control.type)
                || control.type === 'hidden' && control.dataset.mauiMetadataTags !== 'true') return true;
            const value = String(control.value || '').trim();
            if (control.required && !value && !['checkbox','radio'].includes(control.type)) return false;
            if (control.dataset.mauiMetadataEmail === 'true' && value) {
                const addresses = value.split(/[;,]/).map(item => item.trim()).filter(Boolean);
                if (!addresses.length || addresses.some(address => { email.value = address; return !email.validity.valid; })) return false;
            }
            // Do not introduce the browser's implicit integer step restriction on existing numeric metadata.
            const validity = control.validity;
            return !validity || validity.valid || validity.stepMismatch && !control.hasAttribute('step')
                && !['badInput','customError','patternMismatch','rangeOverflow','rangeUnderflow','tooLong','tooShort','typeMismatch','valueMissing'].some(key => validity[key]);
        });
    }

    function watchAction(dialog, button, eligible, valid = () => true) {
        if (!dialog || !button) return () => {};
        const update = () => { button.disabled = !eligible() || !valid(); };
        dialog.addEventListener('input', update); dialog.addEventListener('change', update);
        // Open/reset handlers often assign values without dispatching input. Recheck after they settle.
        dialog.addEventListener('beforetoggle', event => { if (event.newState === 'open') queueMicrotask(update); });
        dialog.addEventListener('reset', () => queueMicrotask(update));
        return update;
    }

    // Information saves combine a loaded baseline with explicit owner busy/conflict guards.
    // No observer ever overrides native disabled properties or guesses that a blocked save may proceed.
    function registerSaveAction(dialog, button, valid = () => true) {
        if (!dialog || !button) return { update() {}, busy() {}, block() {}, reset() {} };
        let busy = false, blocked = false;
        const update = watchAction(dialog, button,
            () => !busy && !blocked && baselines.has(dialog) && baselines.get(dialog) !== signature(dialog),
            () => valid() && formValid(dialog));
        const state = {
            update,
            busy(value) { busy = Boolean(value); update(); },
            block(value) { blocked = Boolean(value); update(); },
            reset() { busy = blocked = false; baselines.delete(dialog); update(); }
        };
        saveActions.set(dialog, state); update(); return state;
    }

    async function confirmLeave(dialog) {
        if (!baselines.has(dialog) || baselines.get(dialog) === signature(dialog)) return true;
        return await window.mauiConfirm?.({ title: 'Unsaved Changes',
            message: 'This Information panel has unsaved changes. Continue without saving them?',
            confirmText: 'Discard Changes', danger: true }) === true;
    }

    function safeUrl(value) {
        if (typeof value !== 'string' || !value.trim()) return '';
        try {
            const url = new URL(value, window.location.href);
            const current = new URL(window.location.href);
            const base = new URL(document.documentElement.dataset.appBaseUrl || '/', current).pathname.replace(/\/$/, '');
            if (url.origin !== current.origin || url.username || url.password
                || (base && url.pathname !== base && !url.pathname.startsWith(base + '/'))) return '';
            return url.pathname + url.search + url.hash;
        } catch { return ''; }
    }

    let pending = null;
    const initialUrl = new URL(window.location.href);
    const resume = initialUrl.searchParams.get('editorResume');
    if (resume) {
        try { if (resume.length <= 4096) pending = JSON.parse(resume); } catch { /* Invalid return context is ignored. */ }
        initialUrl.searchParams.delete('editorResume');
        window.history.replaceState(window.history.state, '', initialUrl.toString());
    }

    function registerContext(key, dialog, options) {
        if (!dialog) return;
        contexts.set(key, { dialog, ...options });
        if (pending?.key !== key) return;
        const state = pending.state;
        pending = null;
        queueMicrotask(async () => {
            try { await options.restore(state); }
            catch (error) { showError(error?.message || 'Unable to reopen the Information panel.', dialog); }
        });
    }

    async function prepareReturn() {
        const dialog = activeDialog();
        const entry = Array.from(contexts.entries()).find(([, context]) => context.dialog === dialog);
        const url = new URL(window.location.href);
        url.searchParams.delete('editorResume');
        if (entry) {
            const [key, context] = entry;
            const state = context.capture();
            if (context.prepare ? !await context.prepare() : !await confirmLeave(dialog)) return null;
            if (state) url.searchParams.set('editorResume', JSON.stringify({ key, state }));
        }
        return safeUrl(url.toString());
    }

    document.addEventListener('beforetoggle', event => {
        const dialog = event.target;
        if (dialog?.tagName !== 'DIALOG' || event.newState !== 'open') return;
        document.querySelectorAll('.toast.visible').forEach(toast => toast.classList.remove('visible'));
        opened.set(dialog, ++sequence);
        baselines.delete(dialog);
        saveActions.get(dialog)?.reset();
        clear(dialog);
    }, true);
    document.addEventListener('close', event => {
        // The close event is queued. Reopening before it fires starts a new modal session.
        if (event.target?.tagName === 'DIALOG' && !event.target.open) { clear(event.target); saveActions.get(event.target)?.reset(); }
    }, true);

    // One in-memory reversal, owned by the current editor opening. It never calls
    // configuration history/restore endpoints or retains more than the last edit.
    function createLocalUndo({ capture, restore, changed = () => {} }) {
        let stable = '', last = null, group = null, applying = false;
        const encode = () => JSON.stringify(capture());
        function rebase(clear = false) { stable = encode(); group = null; if (clear) last = null; }
        function record(key = null, label = 'change', coalesce = false) {
            if (applying) return;
            const next = encode();
            if (next === stable) return;
            if (!coalesce || group !== key || !last) last = { state: stable, label };
            stable = next; group = coalesce ? key : null; changed();
        }
        async function undo() {
            if (!last || applying) return false;
            const selected = last;
            if (!await window.mauiConfirm?.({title:'Undo Last Change?',message:`This will undo the last change: ${selected.label}.`,confirmText:'Undo Change'})) return false;
            if (last !== selected) return false;
            applying = true;
            try { restore(JSON.parse(selected.state)); last = null; rebase(); }
            finally { applying = false; changed(); }
            return true;
        }
        return { rebase, record, undo, end: () => { group = null; }, available: () => last !== null };
    }

    const viewStates = new WeakMap();
    // Inspectable fields and navigation stay available; write controls and checkout
    // are owned by the editor's effective permission, not by visibility alone.
    function viewOnly(dialog, enabled) {
        let state = viewStates.get(dialog);
        if (!state) {
            state = { enabled:false, previous:new Map(), draggable:new Map(), contentEditable:new Map() };
            state.observer = new MutationObserver(() => { if (state.enabled) enforce(); });
            viewStates.set(dialog,state);
        }
        function enforce() {
            const keep = '[data-traditional-task-editor-panel="logs"], [data-host-editor-tasks], [data-script-editor-tasks], [data-date-list-editor-tasks], [data-traditional-task-editor-panel="tasks"]';
            dialog.querySelectorAll('input,textarea,select,button').forEach(control => {
                if (control.closest(keep)) return;
                const navigation = control.tagName === 'BUTTON' && (control.classList.contains('dialog-close')
                    || control.classList.contains('row-primary-link') || [...control.attributes].some(attribute => /^data-.*(?:tab|close|cancel|step-key|schedule-key|next-action-key|section-button)$/.test(attribute.name)));
                if (navigation) return;
                if (!state.previous.has(control)) state.previous.set(control,{disabled:control.disabled,readOnly:control.readOnly});
                if (control.tagName === 'BUTTON') { control.dataset.mauiReadonlyWrite = ''; if (!control.disabled) control.disabled = true; }
                else if (control.tagName === 'SELECT' || ['checkbox','radio','file'].includes(control.type)) { if (!control.disabled) control.disabled = true; }
                else { if (!control.readOnly) control.readOnly = true; if (control.disabled) control.disabled = false; }
            });
            // Email HTML bodies are editable divs, so native input readOnly is insufficient.
            dialog.querySelectorAll('[contenteditable]').forEach(node=>{
                if (node.closest(keep)) return;
                if (!state.contentEditable.has(node)) state.contentEditable.set(node,node.getAttribute('contenteditable'));
                if (node.contentEditable !== 'false') node.contentEditable='false';
            });
            dialog.querySelectorAll('[draggable]').forEach(node=>{
                if (!state.draggable.has(node)) state.draggable.set(node,node.draggable);
                if (node.draggable) node.draggable=false;
            });
        }
        state.enabled = Boolean(enabled); dialog.classList.toggle('maui-editor-readonly',state.enabled);
        if (state.enabled) {
            enforce(); state.observer.observe(dialog,{subtree:true,childList:true,attributes:true,attributeFilter:['disabled','readonly','draggable','contenteditable']});
        } else {
            state.observer.disconnect();
            for (const [control,previous] of state.previous) { control.disabled=previous.disabled; if (previous.readOnly!==undefined) control.readOnly=previous.readOnly; delete control.dataset.mauiReadonlyWrite; }
            state.previous.clear();
            for(const [node,draggable] of state.draggable) node.draggable=draggable;
            state.draggable.clear();
            for(const [node,value] of state.contentEditable) node.setAttribute('contenteditable',value);
            state.contentEditable.clear();
        }
    }

    const tableViewports = new WeakMap();
    function sizeTableViewport(frame, rows = 5) {
        if (!frame) return;
        const size = () => {
            if (!frame.offsetHeight) return;
            const style = getComputedStyle(frame);
            const borders = parseFloat(style.borderTopWidth || 0) + parseFloat(style.borderBottomWidth || 0);
            const scrollbar = Math.max(0, frame.offsetHeight - frame.clientHeight - borders);
            const header = frame.querySelector('thead')?.getBoundingClientRect().height || 32;
            const row = frame.querySelector('tbody tr')?.getBoundingClientRect().height || 34;
            const height = Math.ceil(header + row * rows + scrollbar + borders);
            if (frame.style.height !== `${height}px`) frame.style.height = `${height}px`;
        };
        if (!tableViewports.has(frame) && typeof ResizeObserver !== 'undefined') {
            const observer = new ResizeObserver(() => requestAnimationFrame(size)); observer.observe(frame); tableViewports.set(frame, observer);
        }
        size();
    }

    window.MauiModalUI = { activeDialog, showMessage, showError, clear, track, confirmLeave, safeUrl, prepareReturn, registerContext,
        formValid, watchAction, registerSaveAction, createLocalUndo, viewOnly, sizeTableViewport,
        readOnlyMessage: type => `Read-only: your MOVEit account does not permit editing this ${type}.`,
        register: (dialog, handlers) => { if (dialog) registrations.set(dialog, handlers); } };
})();
