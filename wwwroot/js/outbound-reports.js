/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Shared Create Report workflow for every standard MAUI data grid.
 */

(() => {
    const root = document.documentElement;
    const createUrl = String(root.dataset.outboundReportCreateUrl || '');
    const emailStatusUrl = String(root.dataset.outboundReportEmailStatusUrl || '');
    const updateViewUrl = String(root.dataset.outboundReportUpdateViewUrl || '');
    let dialog;
    let currentApi;
    let selectedDates = [];
    let updatingView = false;

    function token() {
        return document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    }

    async function getJson(url) {
        const response = await fetch(url, { method: 'GET', credentials: 'same-origin', cache: 'no-store' });
        if (response.status === 401) {
            window.mauiSessionExpired?.();
            throw new Error('Your session has expired.');
        }
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
        return data;
    }

    async function postJson(url, payload) {
        const headers = new Headers({ 'Content-Type': 'application/json' });
        const antiForgery = token();
        if (antiForgery) headers.set('RequestVerificationToken', antiForgery);
        const response = await fetch(url, { method: 'POST', credentials: 'same-origin', headers, body: JSON.stringify(payload) });
        if (response.status === 401) {
            window.mauiSessionExpired?.();
            throw new Error('Your session has expired.');
        }
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
        return data;
    }

    function populateFiveMinuteTimes(select, selectedValue = '08:00') {
        if (!select) return;
        select.replaceChildren();
        for (let hour = 0; hour < 24; hour += 1) {
            for (let minute = 0; minute < 60; minute += 5) {
                const value = `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
                const option = document.createElement('option');
                option.value = value;
                option.textContent = value;
                select.appendChild(option);
            }
        }
        select.value = selectedValue;
    }

    function ensureDialog() {
        if (dialog) return dialog;
        dialog = document.createElement('dialog');
        dialog.className = 'settings-dialog outbound-report-dialog';
        dialog.dataset.outboundReportDialog = '';
        dialog.innerHTML = `
            <div class="settings-card outbound-report-shell">
                <div class="settings-header">
                    <div><h2>Create Report</h2><p>Send or schedule the current grid view.</p></div>
                    <button type="button" class="dialog-close" data-report-close aria-label="Close">×</button>
                </div>
                <div class="outbound-report-body">
                    <div class="inline-alert warning-alert outbound-report-smtp-warning" data-report-smtp-warning hidden></div>
                    <div class="inline-alert error-alert" data-report-error hidden></div>
                    <div class="configuration-field-grid two-column">
                        <label class="configuration-field full"><span>Report Title</span><input class="text-input" maxlength="200" data-report-title /></label>
                        <label class="configuration-field full"><span>Description <em>optional</em></span><textarea class="text-input outbound-report-description" maxlength="1000" rows="3" data-report-description></textarea></label>
                        <label class="configuration-field full"><span>Email Recipients</span><input class="text-input" placeholder="user@example.com; team@example.com" data-report-recipients /><small>Separate multiple addresses with commas or semicolons.</small></label>
                        <label class="configuration-field"><span>Delivery</span><select class="text-input" data-report-delivery><option value="Now">Send Now</option><option value="Once">One Time</option><option value="Recurring">Recurring</option></select></label>
                        <label class="configuration-field" data-report-time-field hidden><span>Time</span><select class="text-input" data-report-time></select><small>MOVEit server time; 5-minute increments.</small></label>
                    </div>
                    <div data-report-once hidden>
                        <div class="configuration-card-heading"><h3>Delivery Date(s)</h3><p>Add one or more specific dates.</p></div>
                        <div class="outbound-report-date-entry"><input class="text-input" type="date" data-report-date /><button type="button" class="secondary-button" data-report-add-date>Add Date</button></div>
                        <div class="outbound-report-date-chips" data-report-date-chips></div>
                    </div>
                    <div data-report-recurring hidden>
                        <div class="configuration-field-grid two-column">
                            <label class="configuration-field"><span>Repeat</span><select class="text-input" data-report-frequency><option value="Daily">Daily</option><option value="Weekly">Weekly</option><option value="Monthly">Monthly</option></select></label>
                        </div>
                        <div data-report-weekly hidden>
                            <div class="configuration-card-heading"><h3>Days of Week</h3><p>Select one or more days.</p></div>
                            <div class="outbound-report-day-picker" data-report-weekdays></div>
                        </div>
                        <div data-report-monthly hidden>
                            <div class="configuration-card-heading"><h3>Days of Month</h3><p>Select one or more calendar days.</p></div>
                            <div class="outbound-report-monthday-picker" data-report-monthdays></div>
                        </div>
                    </div>
                    <div class="configuration-security-note">The report uses the grid's current visible columns, filters, search, ordering, and page context. Scheduled reports regenerate fresh data when they run.</div>
                </div>
                <div class="settings-footer">
                    <button type="button" class="secondary-button" data-report-cancel>Cancel</button>
                    <button type="button" class="primary-button" data-report-submit>Send Report</button>
                </div>
            </div>`;
        document.body.appendChild(dialog);
        populateFiveMinuteTimes(dialog.querySelector('[data-report-time]'));

        const weekdayNames = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
        const weekdays = dialog.querySelector('[data-report-weekdays]');
        weekdayNames.forEach((name, index) => {
            const label = document.createElement('label');
            label.className = 'outbound-report-choice';
            const input = document.createElement('input');
            input.type = 'checkbox';
            input.value = String(index);
            input.dataset.reportWeekday = '';
            const text = document.createElement('span');
            text.textContent = name;
            label.append(input, text);
            weekdays.appendChild(label);
        });
        const monthdays = dialog.querySelector('[data-report-monthdays]');
        for (let day = 1; day <= 31; day += 1) {
            const label = document.createElement('label');
            label.className = 'outbound-report-choice compact';
            const input = document.createElement('input');
            input.type = 'checkbox';
            input.value = String(day);
            input.dataset.reportMonthday = '';
            const text = document.createElement('span');
            text.textContent = String(day);
            label.append(input, text);
            monthdays.appendChild(label);
        }

        dialog.querySelectorAll('[data-report-close],[data-report-cancel]').forEach(button => button.addEventListener('click', () => dialog.close()));
        dialog.querySelector('[data-report-delivery]').addEventListener('change', syncScheduleUi);
        dialog.querySelector('[data-report-frequency]').addEventListener('change', () => {
            syncScheduleUi();
            if (dialog.querySelector('[data-report-delivery]').value === 'Recurring' &&
                dialog.querySelector('[data-report-frequency]').value === 'Monthly') {
                scrollScheduleToBottom();
            }
        });
        dialog.querySelector('[data-report-add-date]').addEventListener('click', addSelectedDate);
        dialog.querySelector('[data-report-submit]').addEventListener('click', submit);
        return dialog;
    }

    function renderDates() {
        const holder = dialog.querySelector('[data-report-date-chips]');
        holder.replaceChildren();
        selectedDates.sort().forEach(date => {
            const chip = document.createElement('button');
            chip.type = 'button';
            chip.className = 'outbound-report-date-chip';
            chip.textContent = `${date} ×`;
            chip.title = `Remove ${date}`;
            chip.addEventListener('click', () => { selectedDates = selectedDates.filter(item => item !== date); renderDates(); });
            holder.appendChild(chip);
        });
    }

    function addSelectedDate() {
        const input = dialog.querySelector('[data-report-date]');
        const value = String(input.value || '');
        if (!value || selectedDates.includes(value)) return;
        selectedDates.push(value);
        input.value = '';
        renderDates();
    }

    function scrollScheduleToBottom() {
        const body = dialog?.querySelector('.outbound-report-body');
        if (!body) return;

        // Wait until the Monthly controls have been unhidden and laid out before
        // moving the modal's own scrollable body to the end of the schedule form.
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                body.scrollTo({ top: body.scrollHeight, behavior: 'smooth' });
            });
        });
    }

    function syncScheduleUi() {
        const mode = dialog.querySelector('[data-report-delivery]').value;
        const frequency = dialog.querySelector('[data-report-frequency]').value;
        dialog.querySelector('[data-report-time-field]').hidden = mode === 'Now';
        dialog.querySelector('[data-report-once]').hidden = mode !== 'Once';
        dialog.querySelector('[data-report-recurring]').hidden = mode !== 'Recurring';
        dialog.querySelector('[data-report-weekly]').hidden = mode !== 'Recurring' || frequency !== 'Weekly';
        dialog.querySelector('[data-report-monthly]').hidden = mode !== 'Recurring' || frequency !== 'Monthly';
        dialog.querySelector('[data-report-submit]').textContent = mode === 'Now' ? 'Send Report' : 'Save Report';
    }

    async function refreshEmailStatus() {
        const warning = dialog?.querySelector('[data-report-smtp-warning]');
        if (!warning) return;
        warning.hidden = true;
        warning.textContent = '';
        if (!emailStatusUrl) {
            warning.textContent = 'Unable to verify outbound email configuration. Scheduled reports can still be saved, but delivery may be unavailable.';
            warning.hidden = false;
            return;
        }
        try {
            const status = await getJson(emailStatusUrl);
            if (status?.configured) return;
            warning.textContent = 'SMTP has not been configured. You can still save a scheduled report, but it cannot be delivered until SMTP Configuration is completed.';
            warning.hidden = false;
        } catch (ex) {
            warning.textContent = ex?.message || 'Unable to verify outbound email configuration. Scheduled reports can still be saved, but delivery may be unavailable.';
            warning.hidden = false;
        }
    }

    function reset(api) {
        currentApi = api;
        selectedDates = [];
        dialog.querySelector('[data-report-title]').value = '';
        dialog.querySelector('[data-report-description]').value = '';
        dialog.querySelector('[data-report-recipients]').value = '';
        dialog.querySelector('[data-report-delivery]').value = 'Now';
        dialog.querySelector('[data-report-frequency]').value = 'Daily';
        dialog.querySelector('[data-report-time]').value = '08:00';
        dialog.querySelectorAll('[data-report-weekday],[data-report-monthday]').forEach(item => { item.checked = false; });
        dialog.querySelector('[data-report-error]').hidden = true;
        const smtpWarning = dialog.querySelector('[data-report-smtp-warning]');
        if (smtpWarning) { smtpWarning.hidden = true; smtpWarning.textContent = ''; }
        renderDates();
        syncScheduleUi();
    }

    function isFiveMinuteTime(value) {
        const match = /^(\d{2}):(\d{2})$/.exec(String(value || ''));
        if (!match) return false;
        const hours = Number(match[1]);
        const minutes = Number(match[2]);
        return hours >= 0 && hours <= 23 && minutes >= 0 && minutes <= 59 && minutes % 5 === 0;
    }

    function buildSchedule() {
        const deliveryMode = dialog.querySelector('[data-report-delivery]').value;
        const schedule = { deliveryMode };
        if (deliveryMode === 'Now') return schedule;
        schedule.timeOfDay = dialog.querySelector('[data-report-time]').value;
        if (deliveryMode === 'Once') {
            const pendingDate = String(dialog.querySelector('[data-report-date]')?.value || '');
            if (pendingDate && !selectedDates.includes(pendingDate)) selectedDates.push(pendingDate);
            schedule.dates = [...selectedDates];
            return schedule;
        }
        schedule.frequency = dialog.querySelector('[data-report-frequency]').value;
        if (schedule.frequency === 'Weekly') schedule.daysOfWeek = [...dialog.querySelectorAll('[data-report-weekday]:checked')].map(item => Number(item.value));
        if (schedule.frequency === 'Monthly') schedule.daysOfMonth = [...dialog.querySelectorAll('[data-report-monthday]:checked')].map(item => Number(item.value));
        return schedule;
    }

    async function submit() {
        const error = dialog.querySelector('[data-report-error]');
        const button = dialog.querySelector('[data-report-submit]');
        error.hidden = true;
        const schedule = buildSchedule();
        if (schedule.deliveryMode !== 'Now' && !isFiveMinuteTime(schedule.timeOfDay)) {
            error.textContent = 'Report delivery times must use 5-minute increments (00, 05, 10, ... 55).';
            error.hidden = false;
            return;
        }
        const payload = {
            gridKey: currentApi?.gridKey || '',
            title: dialog.querySelector('[data-report-title]').value,
            description: dialog.querySelector('[data-report-description]').value,
            recipients: dialog.querySelector('[data-report-recipients]').value,
            viewState: currentApi?.captureViewState?.(),
            sourceContext: currentApi?.captureSourceContext?.(),
            schedule,
            snapshot: schedule.deliveryMode === 'Now' ? currentApi?.captureSnapshot?.() : null
        };
        button.disabled = true;
        try {
            const result = await postJson(createUrl, payload);
            dialog.close();
            const confirmation = result?.warning
                ? `${result?.message || 'Report schedule saved.'} ${result.warning}`
                : (result?.message || (schedule.deliveryMode === 'Now' ? 'Report sent.' : 'Report schedule saved.'));
            window.mauiShowToast?.(confirmation);
        } catch (ex) {
            error.textContent = ex?.message || 'The report could not be processed.';
            error.hidden = false;
        } finally {
            button.disabled = false;
        }
    }

    function openCreate(api) {
        if (!api || !createUrl) {
            window.mauiShowToast?.('Report creation is unavailable.', 'error');
            return;
        }
        ensureDialog();
        reset(api);
        dialog.showModal();
        void refreshEmailStatus();
        requestAnimationFrame(() => dialog.querySelector('[data-report-title]')?.focus());
    }

    async function updateView(api, editContext) {
        if (updatingView) return;
        if (!api || !editContext?.id || !updateViewUrl) {
            window.mauiShowToast?.('Report update is unavailable on this page.', 'error');
            return;
        }

        const payload = {
            id: editContext.id,
            gridKey: api.gridKey || '',
            viewState: api.captureViewState?.(),
            sourceContext: api.captureSourceContext?.()
        };

        updatingView = true;
        try {
            const result = await postJson(updateViewUrl, payload);
            api.markViewSaved?.(payload.viewState, payload.sourceContext);
            window.mauiShowToast?.(`Report "${result?.report?.title || editContext.title || 'Report'}" updated.`);
        } catch (ex) {
            window.mauiShowToast?.(ex?.message || 'The Report view could not be updated.', 'error');
        } finally {
            updatingView = false;
        }
    }

    window.MauiOutboundReports = { openCreate, updateView };
})();
