/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Configuration > Settings > SMTP Configuration behavior.
 * Saved/scheduled Report administration lives in the first-class Reports workspace.
 */

(() => {
    const workspace = document.querySelector('[data-configuration-workspace]');
    if (!workspace) return;

    const urls = {
        smtpClearPassword: workspace.dataset.outboundReportSmtpClearPasswordUrl || '',
        smtpTest: workspace.dataset.outboundReportSmtpTestUrl || ''
    };
    const token = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    let smtpBaseline = '';

    async function request(url, payload) {
        const headers = new Headers({ 'Accept': 'application/json', 'Content-Type': 'application/json' });
        if (token) headers.set('RequestVerificationToken', token);
        const response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            cache: 'no-store',
            headers,
            body: JSON.stringify(payload || {})
        });
        if (response.status === 401) {
            window.mauiSessionExpired?.();
            throw new Error('Your session has expired.');
        }
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
        return data;
    }

    function toast(message) {
        window.mauiShowToast?.(message);
    }

    function setStatus(element, message, error = false) {
        if (!element) return;
        element.textContent = message || '';
        element.classList.toggle('error', error);
    }

    // Keep this signature aligned with Configuration's shared payload so Test Email can
    // reject stale/unsaved SMTP edits without creating a second save path.
    function smtpSignature() {
        const get = key => workspace.querySelector(`[data-report-smtp="${key}"]`);
        return JSON.stringify({
            host: String(get('host')?.value || '').trim(),
            port: Number(get('port')?.value || 25),
            enableSsl: Boolean(get('enableSsl')?.checked),
            ignoreCertificateErrors: Boolean(get('ignoreCertificateErrors')?.checked),
            username: String(get('username')?.value || '').trim(),
            password: get('password')?.value || '',
            fromAddress: String(get('fromAddress')?.value || '').trim(),
            fromName: String(get('fromName')?.value || '').trim()
        });
    }

    function hasUnsavedSmtpChanges() {
        return smtpSignature() !== smtpBaseline;
    }

    workspace.querySelector('[data-report-smtp-clear-password]')?.addEventListener('click', async event => {
        const button = event.currentTarget;
        const password = workspace.querySelector('[data-report-smtp="password"]');
        const status = workspace.querySelector('[data-report-smtp-status]');
        button.disabled = true;
        setStatus(status, 'Clearing SMTP password...');
        try {
            const data = await request(urls.smtpClearPassword, {});
            if (password) {
                password.value = '';
                password.placeholder = '';
            }
            const revision = String(data?.configurationRevision || '');
            if (revision) window.dispatchEvent(new CustomEvent('maui:configuration-revision-updated', { detail: { revision } }));
            // Clearing is an immediate persisted operation, so establish the new saved baseline.
            smtpBaseline = smtpSignature();
            setStatus(status, data.message || 'SMTP password cleared.');
            toast(data.message || 'SMTP password cleared.');
        } catch (ex) {
            setStatus(status, ex.message, true);
        } finally {
            button.disabled = false;
        }
    });

    workspace.querySelector('[data-report-smtp-test]')?.addEventListener('click', async event => {
        const recipient = String(workspace.querySelector('[data-report-smtp-test-recipient]')?.value || '').trim();
        const status = workspace.querySelector('[data-report-smtp-status]');
        if (hasUnsavedSmtpChanges()) {
            setStatus(status, 'Save your SMTP changes before sending a test email.', true);
            return;
        }
        if (!recipient) {
            setStatus(status, 'Enter a Test Recipient first.', true);
            return;
        }
        event.currentTarget.disabled = true;
        setStatus(status, 'Sending test email...');
        try {
            const data = await request(urls.smtpTest, { recipient });
            setStatus(status, data.message || 'SMTP test message sent.');
            toast(data.message || 'SMTP test message sent.');
        } catch (ex) {
            setStatus(status, ex.message, true);
        } finally {
            event.currentTarget.disabled = false;
        }
    });

    window.addEventListener('maui:configuration-saved', () => {
        smtpBaseline = smtpSignature();
        setStatus(workspace.querySelector('[data-report-smtp-status]'), '');
    });

    smtpBaseline = smtpSignature();
})();
