/* In-place Host editing for Task and Quick Transfer drafts; Host CRUD stays in the shared editor. */
(() => {
    window.MauiTaskHostEditor = {
        initialize(config, antiForgeryToken, { fetchJson } = {}) {
            if (document.documentElement.dataset.moveitAdmin !== 'true' || !window.MauiEditorRouting?.useMauiHostEditor) return;
            let returned = null;
            const hostDialog = document.querySelector('[data-host-editor-dialog]');
            const endpoint = name => String(config.dataset[name] || '');
            const status = (message, _duration, kind = 'default') => window.MauiModalUI?.showMessage(message, kind);
            // A separate export root keeps Host packages isolated from the Task portability dialog.
            const portability = window.MauiNativePortability?.create({
                root: document.querySelector('[data-host-portability-root]'),
                objectType: 'Host', exportUrl: endpoint('exportHostUrl'),
                antiForgeryToken, loginUrl: endpoint('loginUrl'), status
            });
            const actions = window.MauiHostActions?.create({
                endpoint, fetchJson, status, useMauiHostEditor: true,
                openEditor: (id, name) => editor.open(id, name)
            });
            const editor = window.MauiHostEditor?.initialize({
                shellUrl: config.dataset.hostEditorShellUrl, saveUrl: config.dataset.hostEditorSaveUrl,
                fingerprintUrl: config.dataset.hostEditorFingerprintUrl, certificatesUrl: config.dataset.hostEditorCertificatesUrl,
                documentLibrariesUrl: config.dataset.hostEditorDocumentLibrariesUrl, loginUrl: config.dataset.loginUrl,
                antiForgeryToken,
                onTestHost: actions ? (host, leaseId) => actions.openHostTestDialog([host], leaseId) : null,
                onCloneHost: actions ? host => actions.openCloneHostDialog(host.id, host.name) : null,
                onExportHost: portability ? host => portability.openExport([host.id], [host.name]) : null,
                onDeleteHost: actions ? host => actions.beginDeleteHost(host.id) : null,
                onClosed: () => { if (hostDialog?.open) return; const callback = returned; returned = null; callback?.(); }
            });
            if (!editor) return;
            window.MauiEditorRouting.registerHostOpen(async (id, name, context = {}) => {
                if (hostDialog?.open) return false;
                returned = context.onClosed || null;
                const opened = await editor.open(id, name);
                if (!hostDialog?.open) { const callback = returned; returned = null; callback?.(); }
                return opened;
            });
        }
    };
})();
