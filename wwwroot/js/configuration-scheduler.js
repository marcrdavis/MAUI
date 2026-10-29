/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Configuration > Scheduler System Job administration.
 */

(() => {
    const workspace = document.querySelector('[data-configuration-workspace]');
    if (!workspace) return;

    const listUrl = workspace.dataset.systemJobsListUrl || '';
    const updateUrl = workspace.dataset.systemJobsUpdateUrl || '';
    const runNowUrl = workspace.dataset.systemJobsRunNowUrl || '';
    const list = workspace.querySelector('[data-system-job-list]');
    const dialog = document.querySelector('[data-system-job-edit-dialog]');
    const antiForgeryToken = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const toast = document.querySelector('[data-configuration-toast]');
    let jobs = [];
    let pollTimer = 0;
    let toastTimer = 0;

    function showToast(message, kind = 'default') {
        if (!toast) return;
        if (typeof window.mauiDisplayToast === 'function') {
            window.mauiDisplayToast(toast, message, kind, 5000);
            return;
        }
        toast.textContent = message || '';
        toast.classList.toggle('error', kind === 'error');
        toast.classList.add('visible');
        window.clearTimeout(toastTimer);
        toastTimer = window.setTimeout(() => toast.classList.remove('visible'), 5000);
    }

    function headers(json = false) {
        const result = new Headers();
        if (json) result.set('Content-Type', 'application/json');
        if (antiForgeryToken) result.set('RequestVerificationToken', antiForgeryToken);
        return result;
    }

    function resultClass(value) {
        const normalized = String(value || '').toLowerCase();
        if (normalized === 'successful' || normalized === 'success') return 'success';
        if (normalized === 'completed with warnings') return 'warning';
        if (normalized === 'failed') return 'failed';
        if (normalized === 'running') return 'running';
        return 'idle';
    }

    function createCell(text) {
        const cell = document.createElement('td');
        cell.textContent = String(text ?? '');
        return cell;
    }

    function renderMessageRow(message) {
        if (!list) return;
        const row = document.createElement('tr');
        const cell = document.createElement('td');
        cell.colSpan = 7;
        cell.className = 'system-job-loading';
        cell.textContent = String(message || '');
        row.appendChild(cell);
        list.replaceChildren(row);
    }

    function render() {
        if (!list) return;
        if (!jobs.length) {
            renderMessageRow('No System Jobs are registered.');
            return;
        }

        const fragment = document.createDocumentFragment();
        jobs.forEach(job => {
            const result = job.lastResult || 'Never';
            const row = document.createElement('tr');
            row.dataset.systemJobRow = String(job.key || '');

            const nameCell = document.createElement('td');
            const name = document.createElement('strong');
            name.textContent = String(job.name || '');
            nameCell.appendChild(name);
            row.appendChild(nameCell);

            const toggleCell = document.createElement('td');
            const toggleLabel = document.createElement('label');
            toggleLabel.className = 'configuration-switch system-job-row-switch';
            const toggle = document.createElement('input');
            toggle.type = 'checkbox';
            toggle.dataset.systemJobToggle = '';
            toggle.checked = Boolean(job.enabled);
            toggle.disabled = Boolean(job.isRunning);
            toggleLabel.append(toggle, document.createElement('span'));
            toggleCell.appendChild(toggleLabel);
            row.appendChild(toggleCell);

            row.appendChild(createCell(job.scheduleDisplay || '—'));
            row.appendChild(createCell(job.enabled ? (job.nextRunDisplay || '—') : 'Disabled'));
            row.appendChild(createCell(job.lastRunDisplay || '—'));

            const resultCell = document.createElement('td');
            const resultSpan = document.createElement('span');
            resultSpan.className = `system-job-result system-job-result-${resultClass(result)}`;
            resultSpan.textContent = String(result);
            if (job.lastMessage) resultSpan.title = String(job.lastMessage);
            resultCell.appendChild(resultSpan);
            row.appendChild(resultCell);

            const actionsCell = document.createElement('td');
            const actions = document.createElement('div');
            actions.className = 'configuration-inline-actions system-job-actions';
            const runButton = document.createElement('button');
            runButton.type = 'button';
            runButton.className = 'secondary-button';
            runButton.dataset.systemJobRunNow = '';
            runButton.disabled = Boolean(job.isRunning);
            runButton.textContent = job.isRunning ? 'Running…' : 'Run Now';
            const editButton = document.createElement('button');
            editButton.type = 'button';
            editButton.className = 'secondary-button';
            editButton.dataset.systemJobEdit = '';
            editButton.disabled = Boolean(job.isRunning);
            editButton.textContent = 'Edit';
            actions.append(runButton, editButton);
            actionsCell.appendChild(actions);
            row.appendChild(actionsCell);

            toggle.addEventListener('change', event => {
                void updateJob(job, Boolean(event.currentTarget.checked), job.runTime || '');
            });
            runButton.addEventListener('click', () => void runNow(job));
            editButton.addEventListener('click', () => openEdit(job));
            fragment.appendChild(row);
        });

        list.replaceChildren(fragment);
    }

    async function loadJobs(silent = false) {
        if (!listUrl) return;
        try {
            const response = await fetch(listUrl, { headers: headers(), credentials: 'same-origin', cache: 'no-store' });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data?.message || `System Job load failed (${response.status}).`);
            jobs = Array.isArray(data.jobs) ? data.jobs : [];
            render();
            schedulePoll();
        } catch (error) {
            if (!silent && list) renderMessageRow(error?.message || 'Unable to load System Jobs.');
            schedulePoll();
        }
    }

    function schedulePoll() {
        window.clearTimeout(pollTimer);
        pollTimer = window.setTimeout(() => void loadJobs(true), jobs.some(job => job.isRunning) ? 2000 : 30000);
    }

    async function updateJob(job, enabled, runTime) {
        if (!updateUrl) return;
        try {
            const response = await fetch(updateUrl, {
                method: 'POST', headers: headers(true), credentials: 'same-origin',
                body: JSON.stringify({ key: job.key, enabled, runTime })
            });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data?.message || `System Job save failed (${response.status}).`);
            showToast(data?.message || 'System Job settings saved.');
            await loadJobs(true);
        } catch (error) {
            showToast(error?.message || 'Unable to save System Job settings.', 'error');
            await loadJobs(true);
        }
    }

    async function runNow(job) {
        if (!runNowUrl || job.isRunning) return;
        try {
            const response = await fetch(runNowUrl, {
                method: 'POST', headers: headers(true), credentials: 'same-origin',
                body: JSON.stringify({ key: job.key })
            });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data?.message || `System Job could not be started (${response.status}).`);
            showToast(data?.message || 'System Job queued.');
            await loadJobs(true);
        } catch (error) {
            showToast(error?.message || 'Unable to run the System Job.', 'error');
            await loadJobs(true);
        }
    }

    function setEditError(message) {
        const target = dialog?.querySelector('[data-system-job-edit-error]');
        if (!target) return;
        target.textContent = message || '';
        target.hidden = !message;
    }

    function openEdit(job) {
        if (!dialog) return;
        setEditError('');
        dialog.querySelector('[data-system-job-edit-key]').value = job.key;
        dialog.querySelector('[data-system-job-edit-name]').value = job.name;
        dialog.querySelector('[data-system-job-edit-description]').value = job.description || '';
        dialog.querySelector('[data-system-job-edit-enabled]').checked = Boolean(job.enabled);
        dialog.querySelector('[data-system-job-edit-schedule]').value = job.scheduleDisplay || '';
        const timeField = dialog.querySelector('[data-system-job-edit-time-field]');
        if (timeField) timeField.hidden = !job.scheduleEditable;
        const timeInput = dialog.querySelector('[data-system-job-edit-time]');
        if (timeInput) timeInput.value = job.runTime || '06:00';
        dialog.showModal();
    }

    function closeEdit() {
        setEditError('');
        dialog?.close();
    }

    async function saveEdit() {
        if (!dialog) return;
        const key = dialog.querySelector('[data-system-job-edit-key]')?.value || '';
        const job = jobs.find(item => item.key === key);
        const enabled = Boolean(dialog.querySelector('[data-system-job-edit-enabled]')?.checked);
        const runTime = job?.scheduleEditable ? (dialog.querySelector('[data-system-job-edit-time]')?.value || '') : (job?.runTime || '');
        const saveButton = dialog.querySelector('[data-system-job-edit-save]');
        if (!key) {
            setEditError('System Job is required.');
            return;
        }
        if (job?.scheduleEditable && !runTime) {
            setEditError('Run Daily At is required.');
            return;
        }
        saveButton.disabled = true;
        try {
            const response = await fetch(updateUrl, {
                method: 'POST', headers: headers(true), credentials: 'same-origin',
                body: JSON.stringify({ key, enabled, runTime })
            });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data?.message || `System Job save failed (${response.status}).`);
            closeEdit();
            showToast(data?.message || 'System Job settings saved.');
            await loadJobs(true);
        } catch (error) {
            setEditError(error?.message || 'Unable to save System Job settings.');
        } finally {
            saveButton.disabled = false;
        }
    }

    dialog?.querySelector('[data-system-job-edit-close]')?.addEventListener('click', closeEdit);
    dialog?.querySelector('[data-system-job-edit-cancel]')?.addEventListener('click', closeEdit);
    dialog?.querySelector('[data-system-job-edit-save]')?.addEventListener('click', () => void saveEdit());
    void loadJobs();
})();
