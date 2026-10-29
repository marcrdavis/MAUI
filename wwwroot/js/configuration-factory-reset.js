/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Admin-only, typed-confirmation UI for the installation factory reset.
 */
(() => {
    const workspace = document.querySelector('[data-configuration-workspace]');
    const dialog = document.querySelector('[data-factory-reset-dialog]');
    const open = workspace?.querySelector('[data-factory-reset-open]');
    if (!workspace || !dialog || !open) return;

    const input = dialog.querySelector('[data-factory-reset-confirmation]');
    const submit = dialog.querySelector('[data-factory-reset-execute]');
    const cancel = dialog.querySelector('[data-factory-reset-cancel]');
    const close = dialog.querySelector('[data-factory-reset-close]');
    const error = dialog.querySelector('[data-factory-reset-error]');
    const antiforgery = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    let submitting = false;

    function setError(message) {
        error.textContent = message || '';
        error.hidden = !message;
    }
    function sync() {
        submit.disabled = submitting || input.value !== 'RESET';
    }
    function dismiss() {
        if (!submitting && dialog.open) dialog.close();
    }

    open.addEventListener('click', () => {
        input.value = '';
        setError('');
        sync();
        dialog.showModal();
        input.focus();
    });
    input.addEventListener('input', sync);
    cancel.addEventListener('click', dismiss);
    close.addEventListener('click', dismiss);
    dialog.addEventListener('cancel', event => {
        if (submitting) event.preventDefault();
    });
    submit.addEventListener('click', async () => {
        if (submitting || input.value !== 'RESET') return;
        submitting = true;
        sync();
        cancel.disabled = true;
        close.disabled = true;
        setError('');
        try {
            const response = await fetch(workspace.dataset.factoryResetUrl, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': antiforgery
                },
                body: JSON.stringify({ confirmation: input.value })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success)
                throw new Error(result?.message || `Factory reset request failed (HTTP ${response.status}).`);

            // Leave controls disabled; this installation will shortly stop serving requests.
            submit.textContent = 'Restarting MAUI…';
            input.disabled = true;
            setError('');
            const destination = result.setupUrl;
            if (typeof destination !== 'string' || !destination.startsWith('/') || destination.startsWith('//'))
                throw new Error('The Setup URL returned by MAUI is invalid. Open the MAUI /Setup URL after its host restarts.');
            // A graceful ASP.NET Core shutdown may overlap IIS worker recycling. Wait for
            // the new host's Setup page rather than navigating during an HTTP 503 window.
            for (let attempt = 0; attempt < 30; attempt += 1) {
                await new Promise(resolve => window.setTimeout(resolve, 2000));
                try {
                    const ready = await fetch(destination, {
                        method: 'GET', credentials: 'same-origin', cache: 'no-store',
                        redirect: 'manual'
                    });
                    if (ready.ok) {
                        window.location.assign(destination);
                        return;
                    }
                } catch (_) { /* IIS has not restarted the application yet. */ }
            }
            setError('MAUI is still restarting. Open the MAUI Setup URL again when the server is available.');
        } catch (failure) {
            setError(failure?.message || 'Factory reset failed.');
            submitting = false;
            cancel.disabled = false;
            close.disabled = false;
            sync();
        }
    });
})();
