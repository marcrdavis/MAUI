// Shared Task/Host checkout ownership. A lease heartbeat is not login activity.
(() => {
    'use strict';
    const label = type => type === 'DateList' ? 'Date List' : type;
    const requestFailed = 'Unable to complete the MOVEit checkout request. Please try again.';
    const overrideFailed = 'Unable to override the checkout. Please try again.';
    const restoredMessage = 'Checkout restored. Review your changes and click Save; Auto Save is paused.';
    const restoredFormMessage = 'Checkout restored. Close and reopen this form before continuing.';
    async function confirmOverride(type, details = '') {
        if (document.documentElement.dataset.moveitAdmin !== 'true') return false;
        return Boolean(await window.mauiConfirm?.({ title: 'Override ' + label(type) + ' Checkout?',
            message: (details ? details + '\n\n' : '') + 'Take control of this checkout in MAUI? No changes will be saved now. Review the current object before saving; saving retained edits may replace another user’s changes.',
            confirmText: 'Override Checkout', danger: true }));
    }
    // Dataset editors use their existing short checkout/release contracts. Recovery is
    // a separate confirmed action and never resubmits the failed create/save/delete.
    async function recoverConflict(error, type, token) {
        if (!error?.payload?.checkoutConflict || !await confirmOverride(type, error.message)) return false;
        let response;
        try { response = await fetch(window.mauiAppUrl('/ObjectEditLease/Recover'), { method: 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: { 'Content-Type': 'application/json', Accept: 'application/json', RequestVerificationToken: token || '' }, body: JSON.stringify({type}) }); }
        catch { throw new Error(overrideFailed); }
        const payload = await response.json().catch(() => null);
        if (!response.ok || payload?.success !== true) throw new Error(payload?.message || overrideFailed);
        return true;
    }
    function blockedMessage(type) {
        const admin = document.documentElement.dataset.moveitAdmin === 'true';
        return 'Saving is blocked. Your changes are still in this editor. Reload ' + label(type) + ' to discard them and load the current MOVEit copy.'
            + (admin ? ' Override Checkout lets you retain your changes.' : ' Only a MOVEit administrator can override checkout.');
    }
    function create({ type, token, onLost, busy = () => false }) {
        const root = document.documentElement.dataset;
        let leaseId = '', lost = false, timer = 0, generation = 0, heartbeatBusy = false;
        let closing = Promise.resolve();
        const url = action => root['objectEditLease' + action + 'Url'] || window.mauiAppUrl('/ObjectEditLease/' + action);
        async function post(action, body, keepalive = false) {
            const controller = action === 'Heartbeat' ? new AbortController() : null;
            const timeout = controller ? window.setTimeout(() => controller.abort(), 12000) : 0;
            try {
            const response = await fetch(url(action), { method: 'POST', credentials: 'same-origin', cache: 'no-store', keepalive, signal: controller?.signal,
                headers: { 'Content-Type': 'application/json', Accept: 'application/json', RequestVerificationToken: token }, body: JSON.stringify(body) });
            const payload = await response.json().catch(() => null);
            if (!response.ok) {
                const error = new Error(payload?.message || (response.status === 401 ? 'Your session expired. Close this editor and sign in again.' : requestFailed));
                error.status = response.status;
                error.payload = payload;
                throw error;
            }
            if (action !== 'Open' && payload?.success !== true) throw new Error(action === 'Override' ? overrideFailed : requestFailed);
            return payload;
            } catch (error) {
                if (error.status || error.payload) throw error;
                throw new Error(action === 'Override' ? overrideFailed : requestFailed);
            } finally { if (timeout) window.clearTimeout(timeout); }
        }
        function fail(message) { if (lost) return; lost = true; window.clearInterval(timer); timer = 0; onLost?.(message); }
        async function heartbeat() {
            if (!leaseId || lost || heartbeatBusy || busy()) return;
            const owner = leaseId, sequence = generation;
            heartbeatBusy = true;
            try { await post('Heartbeat', { leaseId: owner }); }
            catch (error) {
                if (sequence !== generation || owner !== leaseId) return;
                // A timeout/offline/5xx response does not prove that MOVEit lost checkout.
                // Keep retrying heartbeats; every Save still validates the server lease.
                if (error.status >= 400 && error.status < 500 && ![408, 429].includes(error.status))
                    fail(error.message + ' ' + blockedMessage(type));
                else console.warn('MAUI checkout heartbeat will retry:', error.message);
            }
            finally { heartbeatBusy = false; }
        }
        async function close(keepalive = false) {
            window.clearInterval(timer); timer = 0; generation++;
            const owner = leaseId; leaseId = ''; lost = false;
            if (!owner) return closing;
            closing = post('Close', { leaseId: owner }, keepalive).catch(error => {
                // The durable server lease remains eligible for background cleanup.
                console.warn('MAUI checkout cleanup deferred:', error.message);
            });
            return closing;
        }
        async function open(id, editable, enabled) {
            // Capture before awaiting cleanup: another Open/Close can supersede this call
            // while the preceding checkout is being released. Only the latest call may acquire.
            const cleanup = close(), sequence = generation;
            await cleanup;
            if (sequence !== generation) return false;
            lost = false;
            if (!editable || !enabled) return false;
            let response;
            try { response = await post('Open', { type, id: String(id) }); }
            catch (error) {
                if (sequence !== generation) return false;
                if (!error.payload?.checkoutConflict || !await confirmOverride(type, error.message)) throw error;
                if (sequence !== generation) return false;
                const recovered = await post('Override', { type, id: String(id) });
                if (!recovered?.leaseId || !recovered.editVersion) throw new Error(overrideFailed);
                response = {required:true, leaseId:recovered.leaseId};
            }
            if (typeof response?.required !== 'boolean') throw new Error(requestFailed);
            if (response?.required && !response.leaseId) throw new Error(requestFailed);
            if (sequence !== generation) {
                if (response?.leaseId) await post('Close', { leaseId: response.leaseId }, true).catch(() => {});
                return false;
            }
            leaseId = response?.leaseId || '';
            if (leaseId) timer = window.setInterval(() => { void heartbeat(); }, 30000);
            return Boolean(leaseId);
        }
        async function override(id) {
            if (root.moveitAdmin !== 'true') throw new Error('Only a MOVEit administrator can override checkout.');
            const promptedGeneration = generation;
            const confirmed = await confirmOverride(type, 'Retain the values currently shown in this editor. Auto Save will pause until you click Save.');
            // Close, pagehide or a newer Open may supersede the editor while the
            // confirmation is pending. Its old prompt must not acquire checkout.
            if (!confirmed || promptedGeneration !== generation) return null;
            window.clearInterval(timer); timer = 0;
            const sequence = ++generation;
            try {
                const response = await post('Override', { type, id: String(id) });
                if (!response?.leaseId || typeof response.editVersion !== 'string' || !response.editVersion.trim())
                    throw new Error(overrideFailed);
                if (sequence !== generation) {
                    await post('Close', { leaseId: response.leaseId }, true).catch(() => {});
                    return null;
                }
                leaseId = response.leaseId; lost = false;
                timer = window.setInterval(() => { void heartbeat(); }, 30000);
                return response.editVersion;
            } catch (error) { if (sequence === generation) fail(error.message + ' ' + blockedMessage(type)); throw error; }
        }
        const onPageHide = () => {
            const owned = Boolean(leaseId);
            void close(true);
            if (owned) fail('This editing session ended when the page was left. Reload this editor before saving.');
        };
        window.addEventListener('pagehide', onPageHide);
        const dispose = () => { window.removeEventListener('pagehide', onPageHide); return close(); };
        return { open, close, heartbeat, override, dispose, invalidate: fail, get id() { return leaseId; }, get lost() { return lost; },
            assert() { if (lost) throw new Error(blockedMessage(type)); } };
    }
    window.MauiObjectEditLease = { create, blockedMessage, recoverConflict, confirmOverride, restoredMessage, restoredFormMessage };
})();
