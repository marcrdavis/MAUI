/* Capture shared toast feedback without sending form values, request bodies or URL queries.
 * Delivery is best effort: a logging outage never blocks an action or creates another toast. */
(() => {
    'use strict';
    window.mauiLogNotification = (message, kind = 'default', origin = 'modal') => {
        const url = document.documentElement.dataset.browserNotificationLogUrl;
        const token = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value;
        if (!url || !token || !String(message || '').trim()) return;
        const text = String(message).slice(0, 4000)
            .replace(/((?:password|passwd|pwd|client_?secret|secret|access_?token|refresh_?token|token|authorization|api_?key|access_?key)"?\s*[:=]\s*)(?:"[^"]*"|'[^']*'|(?:Bearer|Basic)\s+[A-Za-z0-9._~+/=\-]+|[^\s,;}]+)/gi, '$1[redacted]')
            .replace(/\bBearer\s+[A-Za-z0-9._~+/=\-]+/gi, 'Bearer [redacted]');
        try {
            void fetch(url, { method: 'POST', credentials: 'same-origin', keepalive: true,
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
                body: JSON.stringify({ message: text, kind, origin, page: window.location.pathname })
            }).catch(() => {});
        } catch { /* Logging must not interfere with modal or page behavior. */ }
    };
})();
