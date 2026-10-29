/*
 * MAUI experimental editor routing seam.
 * Keeps editor-selection behavior consistent for links rendered outside the primary Tasks/Hosts pages.
 */
(() => {
    'use strict';

    const root = document.documentElement;
    if (!root) return;

    const useMauiTaskEditor = root.dataset.useMauiTaskEditor === 'true';
    const useMauiHostEditor = root.dataset.useMauiHostEditor === 'true';
    const useMauiDateListEditor = root.dataset.useMauiDateListEditor === 'true';
    const useMauiScriptEditor = root.dataset.useMauiScriptEditor === 'true';
    const antiForgeryToken = document.querySelector('.maui-editor-routing-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    let localTaskOpen = null;
    let localHostOpen = null;
    const landingUrl = new URL(window.location.href);
    const returnUrl = window.MauiModalUI?.safeUrl(landingUrl.searchParams.get('editorReturnUrl') || '') || '';
    const returnKind = landingUrl.searchParams.get('editorReturnKind') || '';

    const endpoint = name => String(root.dataset[name] || '').trim();
    const normalize = value => String(value ?? '').trim();

    function closePopup(popup) {
        try { if (popup && !popup.closed) popup.close(); } catch { /* ignored */ }
    }

    function showStatus(message, kind = 'error') {
        if (window.MauiModalUI?.showMessage(message, kind)) return;
        const toast = document.querySelector('[data-prototype-toast]');
        if (toast && typeof window.mauiDisplayToast === 'function') {
            window.mauiDisplayToast(toast, message, kind, 5000);
            return;
        }
        if (kind === 'error') console.error(message); else console.info(message);
    }

    async function fetchJson(url, options = {}) {
        const headers = new Headers(options.headers || {});
        headers.set('Accept', 'application/json');
        if (options.body) headers.set('Content-Type', 'application/json');
        if (antiForgeryToken && options.method && options.method.toUpperCase() !== 'GET') {
            headers.set('RequestVerificationToken', antiForgeryToken);
        }
        const response = await fetch(url, { ...options, headers, cache: options.cache || 'no-store' });
        if (response.status === 401) {
            window.location.replace(endpoint('editorLoginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
            throw new Error('MOVEit session expired.');
        }
        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json') ? await response.json() : null;
        if (!response.ok) throw new Error(payload?.message || `Request failed (${response.status}).`);
        return payload || {};
    }

    function reservePopup(title) {
        if (!window.MauiNativeEditor?.reserve) return null;
        return window.MauiNativeEditor.reserve(title);
    }

    async function openingAccess(type,id) {
        try { return window.MauiObjectAccess ? await window.MauiObjectAccess.read(type,id) : {canEdit:true}; }
        catch(error) { showStatus(error.message || 'The object is unavailable for your MOVEit account.'); return null; }
    }

    async function launchNativeTask(taskId, taskName, popup = null, readOnly = false) {
        const id = normalize(taskId);
        let name = normalize(taskName);
        if (!id) { closePopup(popup); return false; }
        if (!window.MauiNativeEditor?.open || !window.MauiNativeEditor?.reserve) {
            closePopup(popup);
            showStatus('The MAUI native editor launcher is unavailable.');
            return false;
        }

        const reservedPopup = popup || reservePopup(`MAUI Task Editor - ${id}`);
        if (!reservedPopup || reservedPopup.closed) {
            showStatus('The MOVEit native editor popup was blocked by the browser.');
            return false;
        }

        try {
            const prepared = readOnly ? {} : await fetchJson(endpoint('editorPrepareTaskEditUrl'), {
                method: 'POST',
                body: JSON.stringify({ taskId: id })
            });
            name = normalize(prepared?.taskName || name || 'Task');
        } catch (error) {
            closePopup(reservedPopup);
            showStatus(`Task Editor: ${error?.message || 'Unable to save the undo snapshot.'}`);
            return false;
        }

        const opened = await window.MauiNativeEditor.open({
            popup: reservedPopup,
            sessionUrl: endpoint('editorNativeSessionUrl'),
            loginUrl: endpoint('editorLoginUrl') || window.mauiAppUrl('/Account/SessionExpired'),
            antiForgeryToken,
            returnHash: `#/tasks/details/${encodeURIComponent(id)}?HideNav`,
            windowTitle: `MAUI Task Editor - ${id}: ${name || 'Task'}`,
            skin: 'maui', disableUndo:readOnly,
            status: message => showStatus(message),
        });

        if (opened && !readOnly) {
            try {
                await fetchJson(endpoint('editorMarkTaskEditedUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ taskId: id })
                });
            } catch (error) {
                console.warn('MAUI could not update the Task modification audit information.', error);
            }
        }
        return opened;
    }

    async function launchNativeHost(hostId, hostName, popup = null, onClosed = null, readOnly = false) {
        const id = normalize(hostId);
        let name = normalize(hostName);
        if (!id) { closePopup(popup); return false; }
        if (!window.MauiNativeEditor?.open || !window.MauiNativeEditor?.reserve) {
            closePopup(popup);
            showStatus('The MAUI native editor launcher is unavailable.');
            return false;
        }

        const reservedPopup = popup || reservePopup(`MAUI Host Editor - ${id}`);
        if (!reservedPopup || reservedPopup.closed) {
            showStatus('The MOVEit native editor popup was blocked by the browser.');
            return false;
        }

        try {
            const prepared = readOnly ? {} : await fetchJson(endpoint('editorPrepareHostEditUrl'), {
                method: 'POST',
                body: JSON.stringify({ hostId: id })
            });
            name = normalize(prepared?.hostName || name || 'Host');
        } catch (error) {
            closePopup(reservedPopup);
            showStatus(`Host Editor: ${error?.message || 'Unable to save the Host undo snapshot.'}`);
            return false;
        }

        const opened = await window.MauiNativeEditor.open({
            popup: reservedPopup,
            sessionUrl: endpoint('editorNativeSessionUrl'),
            loginUrl: endpoint('editorLoginUrl') || window.mauiAppUrl('/Account/SessionExpired'),
            antiForgeryToken,
            returnHash: `#/hosts/${encodeURIComponent(id)}?HideNav`,
            windowTitle: `MAUI Host Editor - ${id}: ${name || 'Host'}`,
            skin: 'maui', disableUndo:readOnly,
            status: message => showStatus(message),
        });

        if (opened && onClosed) {
            const monitor = window.setInterval(() => {
                if (!reservedPopup.closed) return;
                window.clearInterval(monitor);
                onClosed();
            }, 300);
        }
        if (opened && !readOnly) {
            try {
                await fetchJson(endpoint('editorMarkHostEditedUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ hostId: id })
                });
            } catch (error) {
                console.warn('MAUI could not update the Host modification audit information.', error);
            }
        }
        return opened;
    }

    async function navigateToEditor(baseUrl, idKey, id, nameKey, name, typeKey = '', type = '') {
        const target = normalize(baseUrl);
        if (!target) return false;
        const url = new URL(target, window.location.href);
        url.searchParams.set(idKey, id);
        if (name) url.searchParams.set(nameKey, name);
        if (typeKey && type) url.searchParams.set(typeKey, type);
        const origin = await window.MauiModalUI?.prepareReturn();
        if (origin === null) return false;
        if (origin) {
            url.searchParams.set('editorReturnUrl', origin);
            url.searchParams.set('editorReturnKind', idKey === 'editorTaskId' ? 'task' : 'host');
        }
        window.location.assign(url.toString());
        return true;
    }

    async function openTask(taskId, taskName = '', taskType = '') {
        const id = normalize(taskId);
        let name = normalize(taskName);
        let type = normalize(taskType);
        if (!id) return false;
        const access=await openingAccess('Task',id); if(!access)return false;

        if (!useMauiTaskEditor) return launchNativeTask(id, name, null, !access.canEdit);

        // Resolve the Task type before routing supported Traditional, Advanced and Sync Tasks.
        // When the MAUI editor is enabled, resolve the type before reserving or opening any native-editor
        // window. This prevents Traditional Task links that only carry ID/name (for example Host
        // Information dependencies, Records, Calendar, and resource dependency lists) from visibly
        // starting the MOVEit native editor before routing to MAUI.
        if (!type) {
            try {
                const detailsUrl = new URL(endpoint('editorTaskDetailsUrl'), window.location.href);
                detailsUrl.searchParams.set('id', id);
                const details = await fetchJson(detailsUrl.toString());
                type = normalize(details?.task?.type);
                name = normalize(details?.task?.name || name);
            } catch (error) {
                showStatus(`Task Editor: ${error?.message || 'Unable to determine the Task type.'}`);
                return false;
            }
        }

        if (typeof localTaskOpen === 'function') {
            return await localTaskOpen(id, name, type, null);
        }

        if (!['traditional','synchronization','sync','sy','advanced','cos'].includes(type.toLowerCase())) {
            return launchNativeTask(id, name, null, !access.canEdit);
        }

        return navigateToEditor(endpoint('editorTasksIndexUrl'), 'editorTaskId', id, 'editorTaskName', name, 'editorTaskType', type);
    }

    async function openHost(hostId, hostName = '', context = {}) {
        const id = normalize(hostId);
        const name = normalize(hostName);
        if (!id) return false;
        const access=await openingAccess('Host',id); if(!access){context.onClosed?.();return false;}

        if (!useMauiHostEditor) {
            const opened = await launchNativeHost(id, name, null, context.onClosed, !access.canEdit);
            if (!opened) context.onClosed?.();
            return opened;
        }
        if (typeof localHostOpen === 'function') return await localHostOpen(id, name, context);
        if (context.inPlace) {
            showStatus('The Host editor is unavailable. Reload MAUI and try again.');
            context.onClosed?.();
            return false;
        }
        return navigateToEditor(endpoint('editorHostsIndexUrl'), 'editorHostId', id, 'editorHostName', name);
    }

    async function openScript(scriptId, scriptName = '', context = {}) {
        const id = normalize(scriptId);
        if (!id) return false;
        const access=await openingAccess('Script',id); if(!access){closePopup(context.popup);return false;}
        if (useMauiScriptEditor) {
            closePopup(context.popup);
            if (!window.MauiScriptEditor?.open) { showStatus('The Script editor is unavailable. Reload MAUI and try again.'); return false; }
            return window.MauiScriptEditor.open(id);
        }
        const popup = context.popup || reservePopup(`MAUI Script Editor - ${id}`);
        if (!popup || popup.closed) { showStatus('The MOVEit native editor popup was blocked by the browser.'); return false; }
        try {
            if (!context.isBuiltIn && access.canEdit) await fetchJson(endpoint('editorPrepareScriptEditUrl'), { method: 'POST', body: JSON.stringify({ scriptId: id }) });
            const opened = await window.MauiNativeEditor.open({ popup, sessionUrl: endpoint('editorNativeSessionUrl'),
                loginUrl: endpoint('editorLoginUrl'), antiForgeryToken, returnHash: `#/resources/scripts/${encodeURIComponent(id)}?HideNav`,
                windowTitle: `MAUI Script Editor - ${id}: ${normalize(scriptName) || 'Script'}`, skin: 'maui', status: message => showStatus(message), disableUndo: Boolean(context.isBuiltIn) || !access.canEdit });
            if (opened && !context.isBuiltIn && access.canEdit) {
                try { await fetchJson(endpoint('editorMarkScriptEditedUrl'), { method: 'POST', body: JSON.stringify({ scriptId: id }) }); }
                catch { console.warn('MAUI could not update Script modification audit information.'); }
            }
            return opened;
        } catch (error) { closePopup(popup); showStatus(error.message || 'Unable to open the Script editor.'); return false; }
    }

    async function openDateList(dateListId, dateListName = '', context = {}) {
        const id = normalize(dateListId);
        if (!id) return false;
        const access=await openingAccess('DateList',id); if(!access){closePopup(context.popup);return false;}
        if (useMauiDateListEditor) {
            closePopup(context.popup);
            if (!window.MauiDateListEditor?.open) { showStatus('The Date List editor is unavailable. Reload MAUI and try again.'); return false; }
            return window.MauiDateListEditor.open(id);
        }
        const popup = context.popup || reservePopup(`MAUI Date List Editor - ${id}`);
        if (!popup || popup.closed) { showStatus('The MOVEit native editor popup was blocked by the browser.'); return false; }
        try {
            if(access.canEdit) await fetchJson(endpoint('editorPrepareDateListEditUrl'), { method: 'POST', body: JSON.stringify({ dateListId: id }) });
            const opened = await window.MauiNativeEditor.open({ popup, sessionUrl: endpoint('editorNativeSessionUrl'),
                loginUrl: endpoint('editorLoginUrl'), antiForgeryToken, returnHash: `#/resources/datelists/${encodeURIComponent(id)}?HideNav`,
                windowTitle: `MAUI Date List Editor - ${id}: ${normalize(dateListName) || 'Date List'}`, skin: 'maui', status: message => showStatus(message), disableUndo:!access.canEdit });
            if (opened && access.canEdit) {
                try { await fetchJson(endpoint('editorMarkDateListEditedUrl'), { method: 'POST', body: JSON.stringify({ dateListId: id }) }); }
                catch { console.warn('MAUI could not update Date List modification audit information.'); }
            }
            return opened;
        } catch (error) { closePopup(popup); showStatus(error.message || 'Unable to open the Date List editor.'); return false; }
    }

    function registerTaskOpen(handler) {
        localTaskOpen = typeof handler === 'function' ? handler : null;
    }

    function registerHostOpen(handler) {
        localHostOpen = typeof handler === 'function' ? handler : null;
    }

    function returnToOrigin(kind) {
        // An action may have handed control to Clone/Delete/Undo. Keep that workflow open.
        if (!returnUrl || returnKind !== kind || document.querySelector('dialog[open]')) return false;
        window.location.replace(returnUrl);
        return true;
    }

    window.MauiEditorRouting = {
        openTask,
        openHost,
        openScript,
        openDateList,
        useMauiDateListEditor,
        useMauiScriptEditor,
        registerTaskOpen,
        registerHostOpen,
        returnToOrigin,
        useMauiTaskEditor,
        useMauiHostEditor
    };
})();

