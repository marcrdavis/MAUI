/*
 * Project: MAUI - MOVEit Automation UI
 * Confirmation and process-generation polling for the administrator-initiated restart.
 */
(() => {
    const workspace = document.querySelector('[data-configuration-workspace]');
    const dialog = document.querySelector('[data-application-restart-dialog]');
    // Both the restart-required banner (outside the workspace) and Advanced Settings
    // open the same protected confirmation dialog and use the same restart endpoint.
    const openButtons = [...document.querySelectorAll('[data-application-restart-open]')];
    if (!workspace || !dialog || openButtons.length === 0) return;

    const confirm = dialog.querySelector('[data-application-restart-confirm]');
    const cancel = dialog.querySelector('[data-application-restart-cancel]');
    const close = dialog.querySelector('[data-application-restart-close]');
    const error = dialog.querySelector('[data-application-restart-error]');
    const token = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    let submitting = false;

    const showError = message => {
        error.textContent = message || '';
        error.hidden = !message;
    };
    const dismiss = () => {
        if (!submitting && dialog.open) dialog.close();
    };

    openButtons.forEach(button => button.addEventListener('click', () => {
        confirm.textContent = 'Restart MAUI';
        showError('');
        dialog.showModal();
    }));
    cancel.addEventListener('click', dismiss);
    close.addEventListener('click', dismiss);
    dialog.addEventListener('cancel', event => {
        if (submitting) event.preventDefault();
    });

    confirm.addEventListener('click', async () => {
        if (submitting) return;
        submitting = true;
        confirm.disabled = true;
        cancel.disabled = true;
        close.disabled = true;
        showError('');
        try {
            const response = await fetch(workspace.dataset.applicationRestartUrl, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'RequestVerificationToken': token }
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.accepted || typeof result.instanceId !== 'string')
                throw new Error(result?.message || `Restart request failed (HTTP ${response.status}).`);

            // Wait for a *different* process, not simply a successful response from the old one.
            confirm.textContent = 'Restarting MAUI…';
            const statusUrl = workspace.dataset.applicationRestartStatusUrl;
            const loginUrl = workspace.dataset.applicationLoginUrl;
            if (!statusUrl?.startsWith('/') || statusUrl.startsWith('//') ||
                !loginUrl?.startsWith('/') || loginUrl.startsWith('//'))
                throw new Error('Invalid MAUI restart navigation URL. Open the MAUI login page after restart.');

            for (let attempt = 0; attempt < 45; attempt += 1) {
                await new Promise(resolve => window.setTimeout(resolve, 2000));
                try {
                    const probe = await fetch(statusUrl, {
                        method: 'GET', credentials: 'same-origin', cache: 'no-store', redirect: 'manual'
                    });
                    if (!probe.ok) continue;
                    const current = await probe.json().catch(() => null);
                    if (current?.instanceId && current.instanceId !== result.instanceId) {
                        window.location.assign(loginUrl);
                        return;
                    }
                } catch (_) { /* IIS may briefly be unavailable while restarting. */ }
            }
            showError('MAUI has not confirmed its restart yet. Try opening the MAUI login page again when the server is available.');
        } catch (failure) {
            showError(failure?.message || 'Unable to restart MAUI.');
        } finally {
            submitting = false;
            confirm.disabled = false;
            cancel.disabled = false;
            close.disabled = false;
            confirm.textContent = 'Restart MAUI';
        }
    });
})();
