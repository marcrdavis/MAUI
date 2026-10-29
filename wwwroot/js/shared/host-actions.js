/* Shared Host action dialogs. Both the Hosts grid and nested Task editor call the same controllers and guardrails. */
(() => {
    'use strict';

    function create(options) {
        const root = options.root || document;
        const { endpoint, fetchJson } = options;
        const showStatus = options.status || (() => {});
        const grid = options.grid;
        const useMauiHostEditor = options.useMauiHostEditor;
        function closeReservedPopup(popup) { try { popup?.close(); } catch { /* already closed */ } }

        // One Clone Host modal serves the row menu and optional MAUI Host editor.
        const cloneHostDialog = root.querySelector('[data-clone-host-dialog]');
        const cloneHostForm = cloneHostDialog?.querySelector('[data-clone-host-form]');
        const cloneHostName = cloneHostDialog?.querySelector('[data-clone-host-name]');
        const cloneHostError = cloneHostDialog?.querySelector('[data-clone-host-error]');
        const cloneHostSubmit = cloneHostDialog?.querySelector('[data-clone-host-submit]');
        let cloneHostSource = '', cloneHostBusy = false, cloneHostUncertain = false;
        const updateCloneHostAction = window.MauiModalUI.watchAction(cloneHostDialog, cloneHostSubmit,
            () => !cloneHostBusy && !cloneHostUncertain && Boolean(cloneHostSource),
            () => Boolean(cloneHostName?.value.trim()) && cloneHostName.value.trim().length <= 254);

        function openCloneHostDialog(id, name) {
            if (!cloneHostDialog || cloneHostBusy || String(id || '') === '0') return false;
            if (options.cloneLicenseLimited?.()) { showStatus('The MOVEit Host license limit has been reached.',5000,'error'); return false; }
            cloneHostSource = String(id || '').trim();
            if (!cloneHostSource) return false;
            cloneHostUncertain = false;
            cloneHostName.value = `${name || id} Clone`;
            cloneHostName.disabled = false; cloneHostSubmit.disabled = false;
            cloneHostSubmit.textContent = 'Create Host';
            cloneHostError.hidden = true; cloneHostDialog.showModal(); cloneHostName.focus();
            return true;
        }

        async function submitCloneHost(event) {
            event.preventDefault();
            if (cloneHostBusy || cloneHostUncertain || !cloneHostSource) return;
            const name = cloneHostName.value.trim();
            if (!name || name.length > 254) { cloneHostError.textContent='Enter a Host Name from 1 to 254 characters.'; cloneHostError.hidden=false; return; }
            const requiresNative = !useMauiHostEditor;
            const popup = requiresNative ? window.MauiNativeEditor?.reserve?.('MAUI Host Editor - Cloned Host') : null;
            if (requiresNative && (!popup || popup.closed || !window.MauiNativeEditor?.open)) {
                closeReservedPopup(popup); cloneHostError.textContent='The MOVEit editor popup is unavailable. Allow popups and try again.'; cloneHostError.hidden=false; return;
            }
            cloneHostBusy = true; cloneHostSubmit.disabled = true; cloneHostName.disabled = true;
            cloneHostSubmit.textContent='Creating…'; cloneHostError.hidden=true;
            let dispatched = false, created = false;
            try {
                dispatched = true;
                const result = await fetchJson(endpoint('cloneHostUrl'), {method:'POST',body:JSON.stringify({hostId:cloneHostSource,newHostName:name})});
                if (result?.success !== true || !result?.host?.id || String(result.host.id) === cloneHostSource) throw new Error('The cloned Host acknowledgement could not be confirmed.');
                created = true; cloneHostDialog.close();
                showStatus(result.warning || `Created cloned Host ${result.host.name}.`,5000);
                await options.openEditor(String(result.host.id),result.host.name,popup,false);
                await options.refresh?.();
            } catch(error) {
                closeReservedPopup(popup);
                cloneHostUncertain = created || Boolean(error?.payload?.moveItSaved) || (dispatched && (!error?.status || error.status >= 500));
                cloneHostError.textContent=cloneHostUncertain
                    ? `${error?.message || 'Clone outcome uncertain.'} Refresh Hosts and inspect the destination before trying another clone.`
                    : error?.message || 'Unable to clone Host.';
                cloneHostError.hidden=false;
                if (created) showStatus(cloneHostError.textContent,5000,'error');
            } finally {
                cloneHostBusy=false; cloneHostSubmit.disabled=cloneHostUncertain; cloneHostName.disabled=cloneHostUncertain; cloneHostSubmit.textContent='Create Host'; updateCloneHostAction();
            }
        }
        cloneHostForm?.addEventListener('submit',submitCloneHost);
        cloneHostDialog?.querySelectorAll('[data-clone-host-cancel]').forEach(button => button.addEventListener('click',()=>{if(!cloneHostBusy) cloneHostDialog.close();}));
        cloneHostDialog?.addEventListener('cancel',event=>{if(cloneHostBusy) event.preventDefault();});

        const hostTestDialog = root.querySelector('[data-host-test-dialog]');
        const hostTestTitle = hostTestDialog?.querySelector('[data-host-test-title]');
        const hostTestError = hostTestDialog?.querySelector('[data-host-test-error]');
        const hostTestSummary = hostTestDialog?.querySelector('[data-host-test-summary]');
        const hostTestRows = hostTestDialog?.querySelector('[data-host-test-rows]');
        const hostTestCancel = hostTestDialog?.querySelector('[data-host-test-cancel]');
        const hostTestRun = hostTestDialog?.querySelector('[data-host-test-run]');
        const hostTestCloseButtons = Array.from(hostTestDialog?.querySelectorAll('[data-host-test-close]') || []);
        let hostTestItems = [];
        let hostTestLeaseId = '';
        let hostTestRunning = false;
        let hostTestCancelRequested = false;

        // Opens the shared Host-test modal and immediately starts the same sequential checks used by Run Tests Again.
        function openHostTestDialog(contexts, leaseId = '') {
            if (!hostTestDialog || hostTestRunning) return;
            const items = (contexts || []).filter(item => item?.id).map(item => ({
                id: String(item.id).trim(),
                name: String(item.name || item.id).trim(),
                type: String(item.type || item.typeCode || 'Host').trim(),
                typeCode: String(item.typeCode || '').trim(),
                destination: String(item.destination || '').trim(),
                supported: Boolean(item.testSupported),
                status: item.testSupported ? 'Waiting' : 'Unsupported',
                message: item.testSupported ? '' : 'No MAUI connection-test pattern is available for this Host type.'
            }));
            if (!items.length) {
                showStatus('Test Hosts: No selected Hosts are available to test.', 5000);
                return;
            }

            window.MauiModalUI?.clear(hostTestDialog);
            hostTestItems = items;
            hostTestLeaseId = leaseId;
            hostTestCancelRequested = false;
            if (hostTestTitle) hostTestTitle.textContent = items.length === 1 ? 'Test Host' : 'Test Hosts';
            if (hostTestError) { hostTestError.hidden = true; hostTestError.textContent = ''; }
            renderHostTestRows();
            updateHostTestControls(false);
            updateHostTestSummary();
            hostTestDialog.showModal();
            void runHostTests();
        }

        // Renders the selected Host list with the fixed set of connection-test statuses.
        function renderHostTestRows() {
            if (!hostTestRows) return;
            const fragment = document.createDocumentFragment();
            hostTestItems.forEach(item => {
                const row = document.createElement('tr');
                row.dataset.hostTestId = item.id;

                const name = document.createElement('td');
                name.textContent = item.name || item.id;
                const type = document.createElement('td');
                type.textContent = item.type || item.typeCode || '—';
                const destination = document.createElement('td');
                destination.textContent = item.destination || '—';
                const status = document.createElement('td');
                const statusText = document.createElement('span');
                statusText.dataset.hostTestStatus = '';
                status.appendChild(statusText);
                const detail = document.createElement('td');
                detail.dataset.hostTestMessage = '';

                row.append(name, type, destination, status, detail);
                fragment.appendChild(row);
            });
            hostTestRows.replaceChildren(fragment);
            hostTestItems.forEach(updateHostTestRow);
        }

        // Updates one row without rebuilding the table so Waiting/Testing/Passed/Failed transitions are visible immediately.
        function updateHostTestRow(item) {
            if (!hostTestRows || !item) return;
            const row = Array.from(hostTestRows.querySelectorAll('tr')).find(candidate => candidate.dataset.hostTestId === item.id);
            if (!row) return;
            const status = row.querySelector('[data-host-test-status]');
            const message = row.querySelector('[data-host-test-message]');
            if (status) {
                const displayStatus = item.status === 'Testing'
                    ? 'Testing Host'
                    : (item.status === 'Passed' ? 'Test Successful' : item.status);
                status.textContent = displayStatus;
                status.className = `host-test-status host-test-status-${String(item.status || '').toLowerCase().replaceAll(' ', '-')}`;
            }
            if (message) {
                message.textContent = item.status === 'Testing' || item.status === 'Waiting'
                    ? ''
                    : (item.status === 'Passed' ? 'No errors' : (item.message || ''));
            }
        }

        // Mirrors Preflight: Testing... / Cancel while busy, then Run Tests Again / Close after completion.
        function updateHostTestControls(running) {
            hostTestRunning = Boolean(running);
            hostTestDialog?.setAttribute('aria-busy', hostTestRunning ? 'true' : 'false');
            if (hostTestRun) {
                hostTestRun.disabled = hostTestRunning || !hostTestItems.some(item => item.supported);
                hostTestRun.textContent = hostTestRunning ? 'Testing...' : 'Run Tests Again';
            }
            if (hostTestCancel) {
                hostTestCancel.disabled = false;
                hostTestCancel.textContent = hostTestRunning ? 'Cancel' : 'Close';
            }
            hostTestCloseButtons.forEach(button => { button.disabled = hostTestRunning; });
        }

        // Summarizes readiness, progress, cancellation, and completion without changing individual row results.
        function updateHostTestSummary() {
            if (!hostTestSummary) return;
            const total = hostTestItems.length;
            const counts = hostTestItems.reduce((map, item) => {
                map[item.status] = (map[item.status] || 0) + 1;
                return map;
            }, {});
            const testing = counts.Testing || 0;
            if (hostTestRunning) {
                const completed = total - (counts.Waiting || 0) - testing;
                hostTestSummary.textContent = hostTestCancelRequested
                    ? 'Cancellation requested. The Host currently being tested will finish; un-run Hosts have been cancelled.'
                    : `Testing Hosts one at a time. ${completed} of ${total} completed.`;
                return;
            }

            const hasResults = hostTestItems.some(item => !['Waiting', 'Unsupported'].includes(item.status));
            if (!hasResults && !hostTestCancelRequested) {
                const runnable = counts.Waiting || 0;
                hostTestSummary.textContent = runnable
                    ? `${runnable} ${runnable === 1 ? 'Host is' : 'Hosts are'} ready to test.`
                    : 'No selected Hosts have a supported MAUI connection-test pattern.';
                return;
            }

            const parts = ['Passed', 'Failed', 'Incomplete', 'Unsupported', 'Cancelled', 'Waiting']
                .filter(status => counts[status])
                .map(status => `${counts[status]} ${status}`);
            hostTestSummary.textContent = `${hostTestCancelRequested ? 'Testing canceled.' : 'Testing complete.'} ${parts.join(', ')}.`;
        }

        function resetHostTestQueueForRun() {
            window.MauiModalUI?.clear(hostTestDialog);
            hostTestCancelRequested = false;
            hostTestItems.forEach(item => {
                if (item.supported) {
                    item.status = 'Waiting';
                    item.message = '';
                } else {
                    item.status = 'Unsupported';
                    item.message = 'No MAUI connection-test pattern is available for this Host type.';
                }
                updateHostTestRow(item);
            });
        }

        // Tests supported Hosts sequentially so MOVEit checkouts never overlap because of this MAUI operation.
        async function runHostTests() {
            if (hostTestRunning || !hostTestItems.length) return;
            resetHostTestQueueForRun();
            updateHostTestControls(true);
            updateHostTestSummary();
            try {
                for (const item of hostTestItems) {
                    if (hostTestCancelRequested || item.status === 'Cancelled') break;
                    if (!item.supported || item.status === 'Unsupported') continue;

                    item.status = 'Testing';
                    item.message = '';
                    updateHostTestRow(item);
                    updateHostTestSummary();

                    try {
                        const result = await fetchJson(endpoint('testHostUrl'), {
                            method: 'POST',
                            headers: hostTestLeaseId ? { 'X-MAUI-Edit-Lease': hostTestLeaseId } : {},
                            body: JSON.stringify({ hostId: item.id })
                        });
                        const allowedStatuses = new Set(['Unsupported', 'Incomplete', 'Passed', 'Failed']);
                        item.status = allowedStatuses.has(String(result?.status || '')) ? String(result.status) : 'Failed';
                        item.message = item.status === 'Passed' ? '' : String(result?.message || 'Connection test failed.');
                    } catch (error) {
                        item.status = 'Failed';
                        item.message = error?.message || 'Unable to test the Host connection.';
                    }
                    updateHostTestRow(item);
                    updateHostTestSummary();
                    if (item.status !== 'Passed') {
                        const kind = item.status === 'Failed' ? 'error' : 'warning';
                        const text = `${item.name || item.id}: ${item.message}`;
                        const summary = item.status === 'Failed'
                            ? `${hostTestItems.length > 1 ? 'One or more Host tests failed.' : 'Host test failed.'} See MAUI logs for details.`
                            : item.status === 'Incomplete' ? 'Host test incomplete. See MAUI logs for details.'
                            : 'This Host type does not support testing in MAUI.';
                        if (!window.MauiModalUI?.showMessage(text, kind, hostTestDialog, 'modal', summary)) window.mauiLogNotification?.(text, kind, 'modal');
                    }
                }
            } finally {
                if (hostTestCancelRequested) {
                    hostTestItems.filter(item => item.status === 'Waiting').forEach(item => {
                        item.status = 'Cancelled';
                        item.message = 'Cancelled before testing.';
                        updateHostTestRow(item);
                    });
                }
                updateHostTestControls(false);
                updateHostTestSummary();
                // Interactive tests persist the same Health metadata as the scheduled Host Health job.
                // Refresh once after the batch so Health and Last Health Check update immediately in the grid.
                options.afterTests?.();
            }
        }

        // Closes while idle; while testing, cancels only Hosts that have not started yet.
        function cancelHostTests() {
            if (!hostTestRunning) {
                hostTestDialog?.close();
                return;
            }
            if (hostTestCancelRequested) return;
            hostTestCancelRequested = true;
            hostTestItems.filter(item => item.status === 'Waiting').forEach(item => {
                item.status = 'Cancelled';
                item.message = 'Cancelled before testing.';
                updateHostTestRow(item);
            });
            updateHostTestControls(true);
            updateHostTestSummary();
        }

        function closeHostTestDialog() {
            if (hostTestRunning) return;
            hostTestDialog?.close();
        }

        const deleteHostDialog = root.querySelector('[data-delete-host-dialog]');
        const deleteHostTitle = deleteHostDialog?.querySelector('[data-delete-host-title]');
        const deleteHostMessage = deleteHostDialog?.querySelector('[data-delete-host-message]');
        const deleteHostError = deleteHostDialog?.querySelector('[data-delete-host-error]');
        const deleteHostList = deleteHostDialog?.querySelector('[data-delete-host-list]');
        const deleteHostYes = deleteHostDialog?.querySelector('[data-delete-host-yes]');
        const deleteHostNoButtons = Array.from(deleteHostDialog?.querySelectorAll('[data-delete-host-no]') || []);
        let pendingDeleteHostIds = [];
        let deleteHostWithoutRecycle = false;
        let deleteHostBusy = false;

        // Purpose: Normalizes selected host ids.
        function normalizeSelectedHostIds(hostIds) {
            return [...new Set((Array.isArray(hostIds) ? hostIds : [hostIds])
                .map(id => String(id || '').trim())
                .filter(Boolean))];
        }

        // Purpose: Formats host delete result.
        function formatHostDeleteResult(data) {
            const succeeded = Number(data?.succeeded || 0);
            const failed = Number(data?.failed || 0);
            const failures = Array.isArray(data?.items)
                ? data.items.filter(item => item?.success === false).map(item => `${item?.name || item?.id || 'Host'}: ${item?.message || 'Delete failed.'}`)
                : [];
            const summary = failed > 0
                ? `Delete Host: deleted ${succeeded}; failed ${failed}.`
                : `Delete Host: deleted ${succeeded} Host${succeeded === 1 ? '' : 's'}.`;
            return [summary, failures.slice(0, 3).join(' | ')].filter(Boolean).join(' ');
        }

        // Runs one live dependency/permission preflight for the complete selected Host set before confirmation.
        async function beginDeleteHosts(hostIds)
        {
            const ids = normalizeSelectedHostIds(hostIds);
            if (!ids.length) { showStatus('Select at least one Host.', 5000); return; }
            if (deleteHostBusy || options.isBlocked?.()) return;
            deleteHostBusy = true;
            pendingDeleteHostIds = [];
            deleteHostWithoutRecycle = false;
            if (deleteHostYes) { deleteHostYes.disabled = true; deleteHostYes.textContent = 'Yes, Delete'; }
            if (deleteHostTitle) deleteHostTitle.textContent = ids.length === 1 ? 'Delete Host' : 'Delete Hosts';
            if (deleteHostMessage) deleteHostMessage.textContent = 'Checking Host permissions and dependencies…';
            if (deleteHostList) { deleteHostList.replaceChildren(); deleteHostList.hidden = true; }
            if (deleteHostError) { deleteHostError.hidden = true; deleteHostError.textContent = ''; }
            deleteHostDialog?.showModal();
            try {
                const preflight = await fetchJson(endpoint('preflightDeleteHostsUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ hostIds: ids })
                });

                if (!preflight?.success) {
                    const blockers = Array.isArray(preflight?.blockers) ? preflight.blockers : [];
                    const detail = blockers.length
                        ? blockers.slice(0, 3).map(item => `${item.hostName || item.hostId}: ${item.message || 'cannot be deleted'}`).join(' | ')
                        : '';
                    if (deleteHostError) { deleteHostError.textContent = [preflight?.message || 'One or more selected Hosts cannot be deleted.', detail].filter(Boolean).join(' '); deleteHostError.hidden = false; }
                    return;
                }

                const targets = Array.isArray(preflight?.targets)
                    ? preflight.targets.map(item => ({
                        hostId: String(item?.hostId || '').trim(),
                        hostName: String(item?.hostName || item?.hostId || '').trim()
                    })).filter(item => item.hostId)
                    : ids.map(id => ({ hostId: id, hostName: id }));

                pendingDeleteHostIds = ids;
                deleteHostWithoutRecycle = false;
                if (deleteHostYes) deleteHostYes.textContent = 'Yes, Delete';
                if (deleteHostTitle) deleteHostTitle.textContent = ids.length === 1 ? 'Delete Host' : 'Delete Hosts';
                if (deleteHostMessage) {
                    deleteHostMessage.textContent = ids.length === 1
                        ? 'The following Host will be deleted from MOVEit Automation:'
                        : `The following ${ids.length} Hosts will be deleted from MOVEit Automation:`;
                }
                if (deleteHostList) {
                    deleteHostList.replaceChildren();
                    targets.forEach(item => {
                        const row = document.createElement('div');
                        row.className = 'delete-object-list-item';
                        row.textContent = item.hostName || item.hostId;
                        deleteHostList.appendChild(row);
                    });
                    deleteHostList.hidden = targets.length === 0;
                }
                if (deleteHostError) { deleteHostError.hidden = true; deleteHostError.textContent = ''; }
                window.requestAnimationFrame(() => deleteHostNoButtons.find(button => button.textContent.trim() === 'Cancel')?.focus());
            } catch (error) {
                if (deleteHostError) { deleteHostError.textContent = `Delete Host: ${error?.message || 'Dependency check failed.'}`; deleteHostError.hidden = false; }
            } finally {
                deleteHostBusy = false;
                if (deleteHostYes) deleteHostYes.disabled = !pendingDeleteHostIds.length;
            }
        }

        // Single-row deletion deliberately uses the same bulk path so guardrails and Recycle Bin behavior cannot diverge.
        function beginDeleteHost(hostId) {
            return beginDeleteHosts([hostId]);
        }

        // Re-submits the selected Host IDs after confirmation; each Host is revalidated by the server immediately before COM deletion.
        async function confirmDeleteHost()
        {
            if (deleteHostBusy || !pendingDeleteHostIds.length) return;
            deleteHostBusy = true;
            if (deleteHostYes) deleteHostYes.disabled = true;
            deleteHostNoButtons.forEach(button => { button.disabled = true; });
            if (deleteHostMessage) deleteHostMessage.textContent = `Deleting ${pendingDeleteHostIds.length} Host${pendingDeleteHostIds.length === 1 ? '' : 's'}…`;

            try {
                const data = await fetchJson(endpoint('deleteHostsUrl'), {
                    method: 'POST',
                    body: JSON.stringify({ hostIds: pendingDeleteHostIds, deleteWithoutRecycle: deleteHostWithoutRecycle })
                });
                const warning = String(data?.warning || '').trim();
                deleteHostDialog?.close();
                const deletedHostIds = Array.isArray(data?.items)
                    ? data.items.filter(item => item?.success === true).map(item => item?.id).filter(Boolean)
                    : [];
                grid?.dispatchEvent(new CustomEvent('maui:grid-remove-rows', { detail: { rowIds: deletedHostIds } }));
                pendingDeleteHostIds = [];
                deleteHostWithoutRecycle = false;
                if (deleteHostYes) deleteHostYes.textContent = 'Yes, Delete';
                showStatus([formatHostDeleteResult(data), warning].filter(Boolean).join(' '), 5000);
                await options.refresh?.();
            } catch (error) {
                if (error?.status === 409 && error?.payload?.code === 'RecycleCaptureFailed') {
                    deleteHostWithoutRecycle = true;
                    if (deleteHostError) { deleteHostError.textContent = error?.message || 'MAUI could not create a recovery copy. Delete anyway?'; deleteHostError.hidden = false; }
                    if (deleteHostYes) deleteHostYes.textContent = 'Delete Anyway';
                } else if (deleteHostError) {
                    deleteHostError.textContent = error?.message || 'Unable to delete the selected Hosts.';
                    deleteHostError.hidden = false;
                }
            } finally {
                deleteHostBusy = false;
                if (deleteHostYes) deleteHostYes.disabled = false;
                deleteHostNoButtons.forEach(button => { button.disabled = false; });
            }
        }

        // Purpose: Cancels delete host.
        function cancelDeleteHost()
        {
            if (deleteHostBusy) return;
            pendingDeleteHostIds = [];
            deleteHostWithoutRecycle = false;
            if (deleteHostYes) deleteHostYes.textContent = 'Yes, Delete';
            if (deleteHostList) { deleteHostList.replaceChildren(); deleteHostList.hidden = true; }
            deleteHostDialog?.close();
        }

        hostTestRun?.addEventListener('click', runHostTests);
        hostTestCancel?.addEventListener('click', cancelHostTests);
        hostTestCloseButtons.forEach(button => button.addEventListener('click', closeHostTestDialog));
        hostTestDialog?.addEventListener('cancel', event => {
            if (hostTestRunning) event.preventDefault();
        });
        hostTestDialog?.addEventListener('close', () => {
            if (!hostTestRunning) {
                hostTestItems = [];
                hostTestLeaseId = '';
                hostTestCancelRequested = false;
            }
        });

        deleteHostYes?.addEventListener('click', confirmDeleteHost);
        deleteHostNoButtons.forEach(button => button.addEventListener('click', cancelDeleteHost));
        deleteHostDialog?.addEventListener('cancel', event => {
            if (deleteHostBusy) event.preventDefault();
            else cancelDeleteHost();
        });
        return { openCloneHostDialog, openHostTestDialog, beginDeleteHost, beginDeleteHosts };
    }

    window.MauiHostActions = { create };
})();
