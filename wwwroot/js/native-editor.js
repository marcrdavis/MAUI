/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * This JavaScript is part of the MAUI application.
 */

(() => {
    'use strict';

    const activeLaunches = new Set();
    const HORIZONTAL_REDUCTION_PX = 80;
    const VERTICAL_REDUCTION_PX = 80;
    const MIN_POPUP_WIDTH = 640;
    const MIN_POPUP_HEIGHT = 480;
    const CREDENTIAL_LIFETIME_MS = 60000;

    // Creates a per-launch nonce so only the MOVEit window opened for this request
    // can participate in the MAUI login/configuration handshake.
    function createLaunchId() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID();
        }

        const bytes = new Uint8Array(16);
        window.crypto.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;

        const hex = Array.from(bytes, value => value.toString(16).padStart(2, '0'));
        return `${hex.slice(0, 4).join('')}-${hex.slice(4, 6).join('')}-${hex.slice(6, 8).join('')}-${hex.slice(8, 10).join('')}-${hex.slice(10).join('')}`;
    }

    // Adds the launch nonce outside the Angular hash route. MOVEit's maui-integration.js
    // captures it into sessionStorage and removes it from the visible URL.
    function buildEditorUrl(baseUrl, returnHash, launchId) {
        const url = new URL(`${baseUrl.replace(/\/+$/, '')}/`);
        url.searchParams.set('MauiLaunch', launchId);
        url.hash = returnHash.startsWith('#') ? returnHash.substring(1) : returnHash;
        return url.toString();
    }

    // Calculates a popup slightly smaller than MAUI and centered over the current browser window.
    function calculatePopupGeometry() {
        const currentWidth = window.outerWidth || window.screen.availWidth;
        const currentHeight = window.outerHeight || window.screen.availHeight;
        const width = Math.max(MIN_POPUP_WIDTH, currentWidth - HORIZONTAL_REDUCTION_PX);
        const height = Math.max(MIN_POPUP_HEIGHT, currentHeight - VERTICAL_REDUCTION_PX);
        const currentLeft = typeof window.screenX === 'number' ? window.screenX : (window.screenLeft || 0);
        const currentTop = typeof window.screenY === 'number' ? window.screenY : (window.screenTop || 0);

        return {
            width: Math.round(width),
            height: Math.round(height),
            left: Math.round(currentLeft + ((currentWidth - width) / 2)),
            top: Math.round(currentTop + ((currentHeight - height) / 2))
        };
    }

    // Removes sensitive credentials from browser memory as soon as the structured-clone postMessage completes.
    function clearCredentials(launch) {
        if (!launch) return;
        launch.username = '';
        launch.password = '';
        if (launch.credentialTimer) {
            window.clearTimeout(launch.credentialTimer);
            launch.credentialTimer = null;
        }
    }

    function discardEditorUndo(launch) {
        if (!launch?.editorUndoEnabled || launch.undoDiscardRequested || !launch.undoDiscardUrl) return;
        launch.undoDiscardRequested = true;
        const headers = new Headers({ Accept: 'application/json', 'Content-Type': 'application/json' });
        if (launch.antiForgeryToken) headers.set('RequestVerificationToken', launch.antiForgeryToken);
        void fetch(launch.undoDiscardUrl, {
            method: 'POST',
            headers,
            keepalive: true,
            body: JSON.stringify({
                launchId: launch.launchId,
                objectType: launch.objectType,
                objectId: launch.objectId
            })
        }).catch(error => {
            console.warn('MAUI native editor: unable to discard closed editor undo state.');
        });
    }

    function notifyEditorUndoEnded(launch, message = '') {
        if (!launch?.editorUndoEnabled || !launch.popup || launch.popup.closed) return;
        launch.popup.postMessage({
            type: 'MAUI_EDITOR_SESSION_ENDED',
            launchId: launch.launchId,
            objectType: launch.objectType,
            objectId: launch.objectId,
            message: String(message || '')
        }, launch.origin);
    }

    function clearUndoRecoveryTimer(launch) {
        if (!launch?.undoRecoveryTimer) return;
        window.clearTimeout(launch.undoRecoveryTimer);
        launch.undoRecoveryTimer = null;
    }

    function endEditorUndoSession(launch, message = '') {
        if (!launch?.editorUndoEnabled) return;
        clearUndoRecoveryTimer(launch);
        notifyEditorUndoEnded(launch, message);
        discardEditorUndo(launch);
        launch.undoAvailable = false;
        launch.editorUndoEnabled = false;
        launch.undoRecoveryInProgress = false;
    }

    // Removes a completed/failed launch and its remaining sensitive/editor-local state.
    function removeLaunch(launch) {
        if (!launch) return;
        clearCredentials(launch);
        clearUndoRecoveryTimer(launch);
        discardEditorUndo(launch);
        // Retire the local Undo session immediately. Any in-flight request may still
        // unwind, but it must not schedule a new baseline after the popup/session ended.
        launch.undoAvailable = false;
        launch.editorUndoEnabled = false;
        launch.undoRecoveryInProgress = false;
        if (launch.popupMonitorTimer) {
            window.clearInterval(launch.popupMonitorTimer);
            launch.popupMonitorTimer = null;
        }
        activeLaunches.delete(launch);
    }

    // Restricts browser redirects initiated by the native-editor bridge to this MAUI origin.
    function redirectToSessionExpired(value) {
        const fallback = window.mauiAppUrl('/Account/SessionExpired');
        let safeTarget = fallback;
        try {
            const candidate = new URL(String(value || fallback), window.location.origin);
            if (candidate.origin === window.location.origin) {
                safeTarget = `${candidate.pathname}${candidate.search}${candidate.hash}`;
            }
        } catch {
            safeTarget = fallback;
        }
        window.location.replace(safeTarget);
    }

    function messageMatchesLaunchObject(data, launch) {
        return Boolean(launch?.editorUndoEnabled) &&
            String(data?.objectType || '') === launch.objectType &&
            String(data?.objectId || '') === launch.objectId;
    }

    // Maps only native editor routes backed by MAUI's reversible configuration services.
    // Any child route beneath a Task Details ID remains the same Task editor identity;
    // Keys/certificates and administrative MOVEit pages intentionally remain excluded.
    function getUndoIdentity(returnHash) {
        const hash = String(returnHash || '');
        let match = hash.match(/^#?\/tasks\/details\/([^/?]+)(?:\/[^?]*)?(?:\?.*)?$/i);
        if (match) return { objectType: 'Task', objectId: decodeURIComponent(match[1]) };
        match = hash.match(/^#?\/hosts\/([^?]+)/i);
        if (match) return { objectType: 'Host', objectId: decodeURIComponent(match[1]) };
        match = hash.match(/^#?\/resources\/scripts\/([^?]+)/i);
        if (match) return { objectType: 'Script', objectId: decodeURIComponent(match[1]) };
        match = hash.match(/^#?\/resources\/datelists\/([^?]+)/i);
        if (match) return { objectType: 'DateList', objectId: decodeURIComponent(match[1]) };
        return null;
    }

    async function postUndoRequest(launch, url) {
        if (!launch || !url) throw new Error('Native editor undo endpoint is unavailable.');
        const headers = new Headers({ Accept: 'application/json', 'Content-Type': 'application/json' });
        if (launch.antiForgeryToken) headers.set('RequestVerificationToken', launch.antiForgeryToken);
        const response = await fetch(url, {
            method: 'POST',
            headers,
            body: JSON.stringify({
                launchId: launch.launchId,
                objectType: launch.objectType,
                objectId: launch.objectId
            })
        });
        if (response.status === 401) {
            redirectToSessionExpired(launch.loginUrl);
            throw new Error('MOVEit session expired.');
        }
        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json') ? await response.json() : null;
        if (!response.ok) throw new Error(payload?.message || `Native editor undo request failed (${response.status}).`);
        return payload || {};
    }

    async function postUndoRebindRequest(launch, newObjectId) {
        if (!launch || !launch.undoRebindUrl) throw new Error('Native editor undo rebind endpoint is unavailable.');
        const headers = new Headers({ Accept: 'application/json', 'Content-Type': 'application/json' });
        if (launch.antiForgeryToken) headers.set('RequestVerificationToken', launch.antiForgeryToken);
        const response = await fetch(launch.undoRebindUrl, {
            method: 'POST',
            headers,
            body: JSON.stringify({
                launchId: launch.launchId,
                objectType: launch.objectType,
                previousObjectId: launch.objectId,
                objectId: String(newObjectId || '')
            })
        });
        if (response.status === 401) {
            redirectToSessionExpired(launch.loginUrl);
            throw new Error('MOVEit session expired.');
        }
        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json') ? await response.json() : null;
        if (!response.ok) throw new Error(payload?.message || `Native editor undo rebind failed (${response.status}).`);
        return payload || {};
    }

    function postUndoState(launch, available, message = '') {
        if (!launch?.popup || launch.popup.closed) return;
        launch.popup.postMessage({
            type: 'MAUI_EDITOR_UNDO_STATE',
            launchId: launch.launchId,
            objectType: launch.objectType,
            objectId: launch.objectId,
            available: available === true,
            message: String(message || '')
        }, launch.origin);
    }

    async function rebindEditorUndo(launch, newObjectId) {
        const nextObjectId = String(newObjectId || '').trim();
        if (!launch?.editorUndoEnabled || launch.objectType !== 'Task' || !nextObjectId || nextObjectId === launch.objectId) return;
        if (launch.identityRebindInProgress) return;

        // Do not overlap an identity transition with an in-flight save snapshot or restore.
        // The MOVEit route has already changed, so waiting briefly is safer than letting
        // two operations mutate the editor-local baseline concurrently.
        if (launch.undoAdvanceInProgress || launch.restoreInProgress) {
            window.setTimeout(() => { void rebindEditorUndo(launch, nextObjectId); }, 250);
            return;
        }

        const previousObjectId = launch.objectId;
        clearUndoRecoveryTimer(launch);
        launch.undoRecoveryInProgress = false;
        launch.undoAvailable = false;
        launch.identityRebindInProgress = true;

        try {
            const result = await postUndoRebindRequest(launch, nextObjectId);
            const reboundObjectId = String(result?.objectId || nextObjectId).trim();
            if (!reboundObjectId) throw new Error('MOVEit did not return the new Task ID after rebinding Undo Last Change.');

            launch.objectId = reboundObjectId;
            launch.undoAvailable = false;
            launch.undoDiscardRequested = false;
            launch.popup?.postMessage({
                type: 'MAUI_EDITOR_IDENTITY_REBOUND',
                launchId: launch.launchId,
                objectType: launch.objectType,
                previousObjectId,
                objectId: reboundObjectId,
                available: false
            }, launch.origin);
        } catch (error) {
            console.warn('MAUI native editor: unable to rebind Undo Last Change to the new Task ID.');
            launch.popup?.postMessage({
                type: 'MAUI_EDITOR_IDENTITY_REBIND_FAILED',
                launchId: launch.launchId,
                objectType: launch.objectType,
                previousObjectId,
                objectId: nextObjectId,
                message: error?.message || 'Undo Last Change could not continue for the new Task.'
            }, launch.origin);
            endEditorUndoSession(
                launch,
                'Undo Last Change was disabled because MAUI could not establish a safe baseline for the new Task. Close and reopen the editor to start a new undo session.'
            );
        } finally {
            launch.identityRebindInProgress = false;
        }
    }

    async function rebaselineEditorUndo(launch) {
        if (!launch?.editorUndoEnabled || launch.restoreInProgress) return;
        if (launch.undoAdvanceInProgress) {
            scheduleUndoRebaseline(launch);
            return;
        }

        launch.undoRecoveryTimer = null;
        try {
            await postUndoRequest(launch, launch.undoInitializeUrl);
            launch.undoAvailable = false;
            launch.undoRecoveryInProgress = false;
            postUndoState(launch, false);
        } catch (error) {
            console.warn('MAUI native editor: unable to rebaseline Undo Last Change state.');
            endEditorUndoSession(
                launch,
                'Undo Last Change was disabled because MAUI could not safely re-establish the editor baseline. Close and reopen the editor to start a new undo session.'
            );
        }
    }

    function scheduleUndoRebaseline(launch, message = '') {
        if (!launch?.editorUndoEnabled || launch.restoreInProgress) return;
        launch.undoAvailable = false;
        launch.undoRecoveryInProgress = true;
        postUndoState(
            launch,
            false,
            message || 'Undo Last Change is being reset because MAUI could not safely identify a single previous editor state.'
        );
        clearUndoRecoveryTimer(launch);
        launch.undoRecoveryTimer = window.setTimeout(() => {
            void rebaselineEditorUndo(launch);
        }, 500);
    }

    async function advanceEditorUndo(launch) {
        if (!launch?.editorUndoEnabled || launch.restoreInProgress) return;
        if (launch.undoAdvanceInProgress || launch.undoRecoveryInProgress) {
            scheduleUndoRebaseline(launch);
            return;
        }

        launch.undoAvailable = false;
        postUndoState(launch, false);
        launch.undoAdvanceInProgress = true;
        try {
            const result = await postUndoRequest(launch, launch.undoAdvanceUrl);
            if (launch.undoRecoveryInProgress) return;
            launch.undoAvailable = result?.undoAvailable === true;
            postUndoState(launch, launch.undoAvailable);
        } catch (error) {
            console.warn('MAUI native editor: unable to capture Undo Last Change state.');
            scheduleUndoRebaseline(launch, error?.message || 'Undo Last Change is being reset after a snapshot error.');
        } finally {
            launch.undoAdvanceInProgress = false;
        }
    }

    async function restoreEditorUndo(launch) {
        if (!launch?.editorUndoEnabled || launch.restoreInProgress) return;
        if (launch.undoAdvanceInProgress || launch.undoRecoveryInProgress || !launch.undoAvailable) {
            launch.popup?.postMessage({
                type: 'MAUI_EDITOR_UNDO_RESULT',
                launchId: launch.launchId,
                objectType: launch.objectType,
                objectId: launch.objectId,
                success: false,
                message: 'Undo Last Change is not available while MAUI is establishing the latest editor state.'
            }, launch.origin);
            return;
        }

        let restoreFailed = false;
        launch.restoreInProgress = true;
        try {
            const result = await postUndoRequest(launch, launch.undoRestoreUrl);
            launch.undoAvailable = false;
            postUndoState(launch, false);
            launch.popup?.postMessage({
                type: 'MAUI_EDITOR_UNDO_RESULT',
                launchId: launch.launchId,
                objectType: launch.objectType,
                objectId: launch.objectId,
                success: true,
                warning: String(result?.warning || '')
            }, launch.origin);
        } catch (error) {
            // A failed restore is intentionally fail-closed. The request may have been
            // rejected because the object changed, or the response may be ambiguous.
            // Do not continue advertising the previous one-level Undo.
            restoreFailed = true;
            launch.undoAvailable = false;
            postUndoState(launch, false);
            launch.popup?.postMessage({
                type: 'MAUI_EDITOR_UNDO_RESULT',
                launchId: launch.launchId,
                objectType: launch.objectType,
                objectId: launch.objectId,
                success: false,
                message: error?.message || 'Unable to undo the last native editor change.'
            }, launch.origin);
        } finally {
            launch.restoreInProgress = false;
            if (restoreFailed && launch.editorUndoEnabled) {
                scheduleUndoRebaseline(
                    launch,
                    'Undo Last Change was reset after the restore could not be completed safely.'
                );
            }
        }
    }

    function startPopupMonitor(launch) {
        if (!launch || launch.popupMonitorTimer) return;
        launch.popupMonitorTimer = window.setInterval(() => {
            if (!launch.popup || launch.popup.closed) removeLaunch(launch);
        }, 3000);
    }

    // Obtains the current credentials from MAUI only after a real user click creates the popup.
    async function getLaunchContext(sessionUrl, antiForgeryToken, loginUrl) {
        const headers = new Headers({ Accept: 'application/json' });
        if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);

        const response = await fetch(sessionUrl, { method: 'POST', headers });
        if (response.status === 401) {
            redirectToSessionExpired(loginUrl);
            throw new Error('MOVEit session expired.');
        }

        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json') ? await response.json() : null;
        if (!response.ok) throw new Error(payload?.message || `Native editor launch failed (${response.status}).`);
        return payload || {};
    }

    // Accepts messages only from the exact MOVEit popup and origin associated with an active launch.
    window.addEventListener('message', event => {
        const data = event.data;
        if (!data || typeof data.type !== 'string') return;

        const launchId = typeof data.launchId === 'string' ? data.launchId : '';
        const launch = Array.from(activeLaunches).find(item => item.launchId === launchId);
        if (!launch) return;

        // Accept messages only from the exact MOVEit origin and popup WindowProxy created for this launch.
        // The launchId is a per-launch nonce; all three checks must match before any message is processed.
        if (event.origin !== launch.origin || event.source !== launch.popup) return;

        console.info('MAUI native editor: received', data.type, 'from', event.origin);

        switch (data.type) {
            case 'MOVEIT_PAGE_READY':
                // Used only for an already-authenticated MOVEit page; this remains separate from the login credential handshake.
                if (data.loginRequired === false) {
                    launch.popup.postMessage({
                        type: 'MAUI_EDITOR_CONFIG',
                        launchId: launch.launchId,
                        returnHash: launch.returnHash,
                        skin: launch.skin,
                        windowTitle: launch.windowTitle,
                        editorUndoEnabled: launch.editorUndoEnabled,
                        objectType: launch.objectType,
                        objectId: launch.objectId
                    }, launch.origin);

                    // An authenticated MOVEit page never needs the credential copy.
                    // Keep only the non-sensitive launch record until maui-integration.js
                    // acknowledges that the target editor is fully configured.
                    clearCredentials(launch);
                }
                break;

            case 'MOVEIT_READY':
                console.info('MAUI native editor: MOVEIT_READY received; sending MOVEIT_LOGIN.');
                if (launch.credentialsSent) return;
                if (!launch.username || !launch.password) {
                    launch.status?.('MOVEit opened, but the MAUI credential handoff expired. Close the editor and try again.', 5000, 'error');
                    removeLaunch(launch);
                    return;
                }

                launch.credentialsSent = true;
                launch.popup.postMessage({
                    type: 'MOVEIT_LOGIN',
                    launchId: launch.launchId,
                    username: launch.username,
                    password: launch.password,
                    returnHash: launch.returnHash,
                    windowTitle: launch.windowTitle,
                    skin: launch.skin
                }, launch.origin);

                console.info('MAUI native editor: MOVEIT_LOGIN posted to', launch.origin);

                // postMessage performs a structured clone synchronously, so these values are no longer needed here.
                clearCredentials(launch);
                break;

            case 'MOVEIT_LOGIN_RECEIVED':
                break;

            case 'MOVEIT_LOGIN_SUBMITTING':
                // Launch progress belongs to the native window, never the originating MAUI dialog.
                break;

            case 'MOVEIT_ROUTE_RESTORED':
                // The MAUI presentation is applied only at this point so it cannot
                // interfere with MOVEit's login form/button behavior. Keep the launch
                // record until maui-integration.js confirms that the target page is configured.
                launch.popup.postMessage({
                    type: 'MAUI_EDITOR_CONFIG',
                    launchId: launch.launchId,
                    returnHash: launch.returnHash,
                    skin: launch.skin,
                    windowTitle: launch.windowTitle,
                    editorUndoEnabled: launch.editorUndoEnabled,
                    objectType: launch.objectType,
                    objectId: launch.objectId
                }, launch.origin);
                break;

            case 'MAUI_EDITOR_READY':
                if (String(data.hash || '') !== launch.returnHash) return;
                if (launch.editorUndoEnabled) {
                    clearCredentials(launch);
                    startPopupMonitor(launch);
                    postUndoState(launch, false);
                } else {
                    removeLaunch(launch);
                }
                break;

            case 'MAUI_EDITOR_IDENTITY_CHANGED': {
                const previousObjectId = String(data.previousObjectId || '');
                const nextObjectId = String(data.objectId || '');
                if (
                    launch.editorUndoEnabled &&
                    launch.objectType === 'Task' &&
                    String(data.objectType || '') === 'Task' &&
                    previousObjectId === launch.objectId &&
                    nextObjectId &&
                    nextObjectId !== previousObjectId
                ) {
                    void rebindEditorUndo(launch, nextObjectId);
                }
                break;
            }

            case 'MAUI_EDITOR_UPDATED':
                if (messageMatchesLaunchObject(data, launch) && !launch.identityRebindInProgress) void advanceEditorUndo(launch);
                break;

            case 'MAUI_EDITOR_UNDO_REQUEST':
                if (messageMatchesLaunchObject(data, launch)) void restoreEditorUndo(launch);
                break;

            case 'MAUI_EDITOR_STATE_REQUEST':
                if (messageMatchesLaunchObject(data, launch)) {
                    const available = launch.undoAvailable === true &&
                        !launch.undoAdvanceInProgress &&
                        !launch.undoRecoveryInProgress &&
                        !launch.identityRebindInProgress &&
                        !launch.restoreInProgress;
                    postUndoState(launch, available);
                }
                break;

            case 'MOVEIT_SKIN_ERROR':
                launch.status?.(data.message || 'MOVEit could not load the MAUI editor stylesheet.', 5000, 'error');
                break;

            case 'MOVEIT_LOGIN_ERROR':
                launch.status?.(`MOVEit native editor login failed: ${data.message || 'Unknown error.'}`, 5000, 'error');
                removeLaunch(launch);
                break;
        }
    });

    // Reserves a popup synchronously from a real user gesture when MAUI must perform
    // server-side work (such as creating a new task) before it knows the final MOVEit route.
    // Purpose: Reserve a popup synchronously before asynchronous work determines the final MOVEit route.
    function reserve(windowTitle = 'MAUI Native Editor') {
        const geometry = calculatePopupGeometry();
        const features = [
            'popup=yes',
            'resizable=yes',
            'scrollbars=yes',
            `width=${geometry.width}`,
            `height=${geometry.height}`,
            `left=${geometry.left}`,
            `top=${geometry.top}`
        ].join(',');

        const popup = window.open('about:blank', windowTitle, features);
        if (!popup) return null;

        try {
            const doc = popup.document;
            doc.title = windowTitle;
            doc.body.replaceChildren();
            doc.body.style.margin = '0';
            doc.body.style.minHeight = '100vh';
            doc.body.style.display = 'flex';
            doc.body.style.alignItems = 'center';
            doc.body.style.justifyContent = 'center';
            doc.body.style.background = '#ffffff';
            doc.body.style.color = '#212529';
            doc.body.style.fontFamily = 'Arial, Helvetica, sans-serif';
            doc.body.style.fontSize = '1.1rem';
            doc.body.style.fontWeight = '500';

            const style = doc.createElement('style');
            style.textContent = `
                @keyframes mauiLaunchPulse {
                    0%, 100% { transform: scale(1); opacity: 1; }
                    50% { transform: scale(1.08); opacity: 0.72; }
                }

                #maui-reserved-popup-logo {
                    width: 88px;
                    height: 88px;
                    display: block;
                    animation: mauiLaunchPulse 1.4s ease-in-out infinite;
                    transform-origin: center center;
                }

                @media (prefers-reduced-motion: reduce) {
                    #maui-reserved-popup-logo { animation: none; }
                }
            `;
            doc.head.appendChild(style);

            const status = doc.createElement('div');
            status.setAttribute('role', 'status');
            status.setAttribute('aria-live', 'polite');
            status.setAttribute('aria-label', 'Working');

            const logo = doc.createElement('img');
            logo.id = 'maui-reserved-popup-logo';
            logo.src = new URL(window.mauiAppUrl('/images/automation-mark.svg'), window.location.origin).href;
            logo.alt = '';
            logo.setAttribute('aria-hidden', 'true');

            const fallback = doc.createElement('div');
            fallback.textContent = 'Working...';
            fallback.style.display = 'none';

            logo.addEventListener('error', function () {
                logo.style.display = 'none';
                fallback.style.display = 'block';
            }, { once: true });

            status.appendChild(logo);
            status.appendChild(fallback);
            doc.body.appendChild(status);
        } catch {
            /* The reserved popup can still be reused. */
        }

        return popup;
    }

    // Report only failures to MAUI; lifecycle messages can disrupt the editor's fixed layout.
    // Opens one native MOVEit Web Admin route and completes the custom MOVEIT_READY / MOVEIT_LOGIN handshake.
    async function open(options) {
        // Retire any popup the browser has already closed before starting another editor.
        // This closes the short interval between a user closing one editor and the 3-second
        // popup monitor noticing it, and discards that editor's temporary undo state immediately.
        for (const existingLaunch of Array.from(activeLaunches)) {
            if (!existingLaunch.popup || existingLaunch.popup.closed) removeLaunch(existingLaunch);
        }

        const sessionUrl = String(options?.sessionUrl || '');
        const returnHash = String(options?.returnHash || '');
        const windowTitle = String(options?.windowTitle || 'MAUI Native Editor');
        const skin = String(options?.skin || 'maui').trim().toLowerCase();
        const status = typeof options?.status === 'function' ? options.status : null;
        if (!sessionUrl || !returnHash) {
            status?.('The native editor launch configuration is incomplete.', 5000, 'error');
            return false;
        }

        // The popup opens here for normal edit clicks. New-task creation can pass a popup reserved
        // synchronously before its server-side COM creation request begins.
        const popup = options?.popup || reserve(windowTitle);
        if (!popup || popup.closed) {
            status?.('The MOVEit native editor popup was blocked by the browser.', 5000, 'error');
            return false;
        }

        try { popup.document.title = windowTitle; }
        catch { /* The popup remains usable if it has already navigated cross-origin. */ }

        try {
            const context = await getLaunchContext(sessionUrl, options.antiForgeryToken || '', options.loginUrl || window.mauiAppUrl('/Account/SessionExpired'));
            const baseUrl = String(context.webAdminUrl || '').replace(/\/+$/, '');
            const parsedBaseUrl = new URL(baseUrl);
            const launchId = createLaunchId();
            const editorUrl = buildEditorUrl(baseUrl, returnHash, launchId);
            const undoIdentity = getUndoIdentity(returnHash);
            const undoDisabled = options?.disableUndo === true;
            let editorUndoEnabled = false;
            if (!undoDisabled && undoIdentity && context.undoInitializeUrl && context.undoAdvanceUrl && context.undoRebindUrl && context.undoRestoreUrl && context.undoDiscardUrl) {
                try {
                    const headers = new Headers({ Accept: 'application/json', 'Content-Type': 'application/json' });
                    if (options.antiForgeryToken) headers.set('RequestVerificationToken', options.antiForgeryToken);
                    const undoResponse = await fetch(context.undoInitializeUrl, {
                        method: 'POST',
                        headers,
                        body: JSON.stringify({ launchId, objectType: undoIdentity.objectType, objectId: undoIdentity.objectId })
                    });
                    const undoContentType = undoResponse.headers.get('content-type') || '';
                    const undoPayload = undoContentType.includes('application/json') ? await undoResponse.json() : null;
                    if (undoResponse.ok) editorUndoEnabled = true;
                    else console.warn('MAUI native editor: Undo Last Change initialization was unavailable.');
                } catch (error) {
                    console.warn('MAUI native editor: Undo Last Change initialization failed; editor will open without it.');
                }
            }

            const launch = {
                popup,
                origin: parsedBaseUrl.origin,
                launchId,
                returnHash,
                windowTitle,
                skin,
                username: String(context.username || ''),
                password: String(context.password || ''),
                credentialsSent: false,
                credentialTimer: null,
                status,
                antiForgeryToken: options.antiForgeryToken || '',
                loginUrl: options.loginUrl || window.mauiAppUrl('/Account/SessionExpired'),
                editorUndoEnabled,
                objectType: undoIdentity?.objectType || '',
                objectId: undoIdentity?.objectId || '',
                undoInitializeUrl: String(context.undoInitializeUrl || ''),
                undoAdvanceUrl: String(context.undoAdvanceUrl || ''),
                undoRebindUrl: String(context.undoRebindUrl || ''),
                undoRestoreUrl: String(context.undoRestoreUrl || ''),
                undoDiscardUrl: String(context.undoDiscardUrl || ''),
                undoDiscardRequested: false,
                undoAdvanceInProgress: false,
                undoRecoveryInProgress: false,
                undoRecoveryTimer: null,
                identityRebindInProgress: false,
                restoreInProgress: false,
                undoAvailable: false,
                popupMonitorTimer: null
            };

            launch.credentialTimer = window.setTimeout(() => {
                if (!launch.credentialsSent) {
                    removeLaunch(launch);
                }
            }, CREDENTIAL_LIFETIME_MS);

            activeLaunches.add(launch);
            popup.location.replace(editorUrl);
            popup.focus();
            return true;
        } catch (error) {
            try { popup.close(); } catch { /* ignored */ }
            status?.(error?.message || 'Unable to open the MOVEit native editor.', 5000, 'error');
            return false;
        }
    }

    window.addEventListener('pagehide', () => {
        for (const launch of Array.from(activeLaunches)) {
            if (launch.editorUndoEnabled) {
                notifyEditorUndoEnded(
                    launch,
                    'Undo Last Change ended because the MAUI page that opened this editor was closed or navigated away.'
                );
            }
            removeLaunch(launch);
        }
    });

    window.MauiNativeEditor = Object.freeze({ open, reserve });
})();
