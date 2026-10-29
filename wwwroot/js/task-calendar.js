/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Task Calendar: renders historical Task Runs and projected future schedule occurrences.
 */
(() => {
    const config = document.querySelector('[data-task-calendar-config]');
    const stage = document.querySelector('[data-calendar-stage]');
    if (!config || !stage) return;

    const errorBanner = document.querySelector('[data-task-calendar-error]');
    const warningBanner = document.querySelector('[data-task-calendar-warning]');
    const loading = document.querySelector('[data-calendar-loading]');
    const shell = document.querySelector('[data-task-calendar-shell]');
    const searchMode = document.querySelector('[data-calendar-search-mode]');
    const search = document.querySelector('[data-calendar-search]');
    const searchClear = document.querySelector('[data-calendar-search-clear]');
    const searchCount = document.querySelector('[data-calendar-search-count]');
    const matchCount = document.querySelector('[data-calendar-match-count]');
    const matchLabel = document.querySelector('[data-calendar-match-label]');
    const title = document.querySelector('[data-calendar-title]');
    const legendItems = document.querySelector('[data-calendar-legend-items]');
    const colorBy = document.querySelector('[data-calendar-color-by]');
    const viewButtons = [...document.querySelectorAll('[data-calendar-view]')];
    const previousButton = document.querySelector('[data-calendar-previous]');
    const nextButton = document.querySelector('[data-calendar-next]');
    const todayButton = document.querySelector('[data-calendar-today]');
    const refreshButton = document.querySelector('[data-calendar-refresh]');
    const toast = document.querySelector('[data-task-calendar-toast]');
    const antiForgeryToken = document.querySelector('.task-calendar-antiforgery input[name="__RequestVerificationToken"]')?.value || '';

    const detailsEmpty = document.querySelector('[data-calendar-details-empty]');
    const detailsContent = document.querySelector('[data-calendar-details-content]');
    const detailsKind = document.querySelector('[data-calendar-details-kind]');
    const detailsName = document.querySelector('[data-calendar-details-name]');
    const detailsId = document.querySelector('[data-calendar-details-id]');
    const runDetails = document.querySelector('[data-calendar-run-details]');
    const taskDetails = document.querySelector('[data-calendar-task-details]');
    const organizationDetails = document.querySelector('[data-calendar-organization-details]');
    const detailsError = document.querySelector('[data-calendar-details-error]');
    const openEditorButton = document.querySelector('[data-calendar-open-editor]');

    const slotDialog = document.querySelector('[data-calendar-slot-dialog]');
    const slotTitle = document.querySelector('[data-calendar-slot-title]');
    const slotSubtitle = document.querySelector('[data-calendar-slot-subtitle]');
    const slotList = document.querySelector('[data-calendar-slot-list]');
    const slotCloseButtons = [...document.querySelectorAll('[data-calendar-slot-close]')];

    const state = {
        view: 'month',
        anchorDate: parseDateKey(config.dataset.initialDate) || todayUtcDate(),
        events: [],
        eventsByDate: new Map(),
        eventsByHour: new Map(),
        selectedEvent: null,
        taskDetailsCache: new Map(),
        matchingEventIds: new Set(),
        searchMode: 'search',
        searchTerm: '',
        requestSequence: 0
    };


    const minimumFixedCalendarHeight = 280;

    // Matches the fixed-viewport grid behavior: the page shell stays stationary and the
    // calendar itself consumes the remaining viewport above the footer. Very short windows
    // fall back to ordinary page scrolling rather than compressing the calendar unusably.
    function sizeCalendarToViewport() {
        if (!shell) return;
        const footer = document.querySelector('.app-footer');
        const shellTop = shell.getBoundingClientRect().top;
        const footerHeight = footer?.getBoundingClientRect().height || 0;
        const rootStyles = getComputedStyle(document.documentElement);
        const configuredGap = parseFloat(rootStyles.getPropertyValue('--grid-footer-gap'));
        const footerGap = Number.isFinite(configuredGap) ? configuredGap : 14;
        const availableHeight = Math.floor(window.innerHeight - shellTop - footerHeight - footerGap);
        const useFixedCalendar = availableHeight >= minimumFixedCalendarHeight;

        document.documentElement.classList.toggle('calendar-fixed-page-root', useFixedCalendar);
        document.body.classList.toggle('calendar-fixed-page', useFixedCalendar);
        shell.style.height = useFixedCalendar ? `${availableHeight}px` : '';
    }

    function endpoint(name) {
        return String(config.dataset[name] || '').trim();
    }

    function showStatus(message, durationMs = 5000, kind = 'default', origin = 'modal') {
        if (typeof window.mauiDisplayToast === 'function') { window.mauiDisplayToast(toast, message, kind, durationMs, origin); return; }
        if (!toast) return;
        toast.textContent = message || '';
        toast.classList.toggle('error', kind === 'error');
        toast.classList.add('visible');
        window.clearTimeout(toast._mauiCalendarTimer);
        toast._mauiCalendarTimer = window.setTimeout(() => toast.classList.remove('visible'), durationMs);
    }

    async function fetchJson(url, options = {}) {
        const headers = new Headers(options.headers || {});
        headers.set('Accept', 'application/json');
        if (options.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
        if (options.method && options.method.toUpperCase() !== 'GET' && antiForgeryToken)
            headers.set('RequestVerificationToken', antiForgeryToken);

        const response = await fetch(url, { ...options, headers, cache: 'no-store' });
        if (response.status === 401) {
            window.location.assign(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
            throw new Error('MOVEit session expired.');
        }

        let data = null;
        try { data = await response.json(); } catch { /* response may not contain JSON */ }
        if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
        return data;
    }

    function parseDateKey(value) {
        const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(value || '').trim());
        if (!match) return null;
        const result = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
        return Number.isNaN(result.getTime()) ? null : result;
    }

    function todayUtcDate() {
        const now = new Date();
        return new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()));
    }

    function dateKey(date) {
        return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}-${String(date.getUTCDate()).padStart(2, '0')}`;
    }

    function addDays(date, amount) {
        const result = new Date(date.getTime());
        result.setUTCDate(result.getUTCDate() + amount);
        return result;
    }

    function startOfWeek(date) {
        return addDays(date, -date.getUTCDay());
    }

    function startOfMonth(date) {
        return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1));
    }

    function endOfMonth(date) {
        return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0));
    }

    function daysBetween(start, end) {
        return Math.round((end.getTime() - start.getTime()) / 86400000);
    }

    function visibleRange() {
        if (state.view === 'day') {
            const start = new Date(state.anchorDate.getTime());
            return { start, end: addDays(start, 1) };
        }
        if (state.view === 'week') {
            const start = startOfWeek(state.anchorDate);
            return { start, end: addDays(start, 7) };
        }

        const monthStart = startOfMonth(state.anchorDate);
        const monthEnd = endOfMonth(state.anchorDate);
        const start = startOfWeek(monthStart);
        const trailing = 6 - monthEnd.getUTCDay();
        let end = addDays(monthEnd, trailing + 1);
        if (daysBetween(start, end) < 35) end = addDays(end, 7);
        return { start, end };
    }

    function formatDate(date, options) {
        return new Intl.DateTimeFormat(undefined, { timeZone: 'UTC', ...options }).format(date);
    }

    function formatDateKeyLong(value) {
        const date = parseDateKey(value);
        return date ? formatDate(date, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' }) : value;
    }

    function eventDateKey(event) {
        return String(event?.eventTime || '').slice(0, 10);
    }

    function eventHour(event) {
        const match = /T(\d{2}):/.exec(String(event?.eventTime || ''));
        return match ? Number(match[1]) : 0;
    }

    function formatIsoTime(value) {
        const match = /T(\d{2}):(\d{2})/.exec(String(value || ''));
        if (!match) return '—';
        let hour = Number(match[1]);
        const minute = match[2];
        const suffix = hour >= 12 ? 'PM' : 'AM';
        hour %= 12;
        if (hour === 0) hour = 12;
        return `${hour}:${minute} ${suffix}`;
    }

    function formatIsoDateTime(value) {
        const key = String(value || '').slice(0, 10);
        if (!parseDateKey(key)) return String(value || '') || '—';
        return `${formatDateKeyLong(key)}, ${formatIsoTime(value)}`;
    }

    function formatHour(hour) {
        const normalized = hour % 24;
        if (normalized === 0) return '12 AM';
        if (normalized === 12) return '12 PM';
        return normalized > 12 ? `${normalized - 12} PM` : `${normalized} AM`;
    }

    function formatDuration(seconds) {
        const value = Number(seconds);
        if (!Number.isFinite(value) || value < 0) return '—';
        if (value < 60) return `${Math.round(value)} sec`;
        if (value < 3600) return `${Math.floor(value / 60)} min ${Math.round(value % 60)} sec`;
        const hours = Math.floor(value / 3600);
        const minutes = Math.floor((value % 3600) / 60);
        return `${hours} hr ${minutes} min`;
    }

    function updateTitle() {
        if (!title) return;
        if (state.view === 'month') {
            title.textContent = formatDate(state.anchorDate, { month: 'long', year: 'numeric' });
            return;
        }
        if (state.view === 'week') {
            const start = startOfWeek(state.anchorDate);
            const end = addDays(start, 6);
            const sameMonth = start.getUTCMonth() === end.getUTCMonth();
            title.textContent = sameMonth
                ? `${formatDate(start, { month: 'short' })} ${start.getUTCDate()} – ${end.getUTCDate()}, ${end.getUTCFullYear()}`
                : `${formatDate(start, { month: 'short', day: 'numeric' })} – ${formatDate(end, { month: 'short', day: 'numeric', year: 'numeric' })}`;
            return;
        }
        title.textContent = formatDate(state.anchorDate, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });
    }

    function colorProperty() {
        const value = colorBy?.value || 'sourceHostType';
        if (value === 'resourceGroup') return { key: 'resourceGroup', label: 'Resource Group' };
        if (value === 'mauiFolder') return { key: 'mauiFolder', label: 'MAUI Folder' };
        if (value === 'status') return { key: 'status', label: 'Status' };
        return { key: 'sourceHostType', label: 'Source Host Type' };
    }

    function statusCategoryFor(event) {
        if (event?.kind === 'upcoming') return 'Future';
        const status = String(event?.status || '').trim().toLowerCase();
        if (status.includes('no transfer') || status.includes('no xfer')) return 'No Transfer';
        if (status.includes('fail')) return 'Failed';
        if (status.includes('success')) return 'Success';
        return 'Other';
    }

    function categoryFor(event) {
        const property = colorProperty().key;
        if (property === 'status') return statusCategoryFor(event);
        return String(event?.[property] || 'Unknown').trim() || 'Unknown';
    }

    function hueForCategory(category) {
        let hash = 2166136261;
        const text = String(category || 'Unknown');
        for (let i = 0; i < text.length; i += 1) {
            hash ^= text.charCodeAt(i);
            hash = Math.imul(hash, 16777619);
        }
        return Math.abs(hash >>> 0) % 360;
    }

    function statusClass(category) {
        if (category === 'Success') return 'calendar-status-success';
        if (category === 'Failed') return 'calendar-status-failed';
        if (category === 'No Transfer') return 'calendar-status-no-transfer';
        if (category === 'Future') return 'calendar-status-future';
        return 'calendar-status-other';
    }

    function applyCategoryColor(node, category) {
        if (colorProperty().key === 'status') {
            node.classList.add(statusClass(category));
            return;
        }
        node.style.setProperty('--calendar-hue', String(hueForCategory(category)));
    }

    function applyEventColor(node, event) {
        applyCategoryColor(node, categoryFor(event));
    }

    function renderLegend() {
        if (!legendItems) return;
        const property = colorProperty();
        legendItems.replaceChildren();

        const legendEvents = eventsForDisplay(state.events);
        let categories;
        if (property.key === 'status') {
            const actual = new Set(legendEvents.map(statusCategoryFor));
            const standard = ['Success', 'Failed', 'No Transfer', 'Future'];
            categories = state.searchMode === 'filter' && state.searchTerm
                ? standard.filter(category => actual.has(category))
                : standard;
            if (actual.has('Other')) categories.push('Other');
        } else {
            categories = [...new Set(legendEvents.map(categoryFor))]
                .sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }));
        }

        if (categories.length === 0) {
            const empty = document.createElement('span');
            empty.className = 'task-calendar-legend-empty';
            empty.textContent = 'No runs in the visible range';
            legendItems.appendChild(empty);
            return;
        }

        categories.forEach(category => {
            const item = document.createElement('span');
            item.className = 'task-calendar-legend-item';
            applyCategoryColor(item, category);
            const dot = document.createElement('span');
            dot.className = 'task-calendar-legend-dot';
            const text = document.createElement('span');
            text.textContent = category;
            item.append(dot, text);
            legendItems.appendChild(item);
        });
    }

    function normalizeSearch(value) {
        return String(value || '').trim().toLocaleLowerCase();
    }

    function eventMatchesSearch(event) {
        if (!state.searchTerm) return false;
        return String(event?.taskName || '').toLocaleLowerCase().includes(state.searchTerm);
    }

    function rebuildSearchMatches() {
        state.matchingEventIds = new Set();
        if (state.searchTerm) {
            state.events.forEach(event => {
                if (eventMatchesSearch(event)) state.matchingEventIds.add(event.id);
            });
        }
        if (matchCount) matchCount.textContent = String(state.matchingEventIds.size);
        if (matchLabel) matchLabel.textContent = state.searchMode === 'filter' ? 'filtered runs' : 'matching runs';
        if (searchCount) searchCount.hidden = !state.searchTerm;
        if (searchClear) searchClear.classList.toggle('visible', Boolean(state.searchTerm));
    }

    function eventsForDisplay(events) {
        if (state.searchMode !== 'filter' || !state.searchTerm) return events;
        if (state.matchingEventIds.size === 0) return [];
        return events.filter(event => state.matchingEventIds.has(event.id));
    }

    function slotMatchesSearch(events) {
        if (state.searchMode !== 'search' || !state.searchTerm || state.matchingEventIds.size === 0) return false;
        return events.some(event => state.matchingEventIds.has(event.id));
    }

    function rebuildEventIndexes() {
        state.eventsByDate = new Map();
        state.eventsByHour = new Map();

        state.events.slice().sort(compareEvents).forEach(event => {
            const key = eventDateKey(event);
            if (!state.eventsByDate.has(key)) state.eventsByDate.set(key, []);
            state.eventsByDate.get(key).push(event);

            const hourKey = `${key}|${eventHour(event)}`;
            if (!state.eventsByHour.has(hourKey)) state.eventsByHour.set(hourKey, []);
            state.eventsByHour.get(hourKey).push(event);
        });
    }

    function eventsForDate(key) {
        return state.eventsByDate.get(key) || [];
    }

    function eventsForHour(key, hour) {
        return state.eventsByHour.get(`${key}|${hour}`) || [];
    }

    function compareEvents(a, b) {
        const at = String(a?.eventTime || '');
        const bt = String(b?.eventTime || '');
        const byTime = at.localeCompare(bt);
        return byTime || String(a?.taskName || '').localeCompare(String(b?.taskName || ''), undefined, { sensitivity: 'base' });
    }

    function createEventButton(event, compact = false) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = `task-calendar-event ${event.kind === 'upcoming' ? 'upcoming' : 'actual'}${compact ? ' compact' : ''}`;
        button.dataset.calendarEventId = event.id;
        button.dataset.status = String(event.status || '').toLowerCase().replace(/\s+/g, '-');
        applyEventColor(button, event);
        button.title = `${formatIsoTime(event.eventTime)} — ${event.taskName}`;

        const marker = document.createElement('span');
        marker.className = 'task-calendar-event-marker';
        marker.setAttribute('aria-hidden', 'true');
        marker.textContent = event.kind === 'upcoming' ? '○' : '●';
        const time = document.createElement('span');
        time.className = 'task-calendar-event-time';
        time.textContent = formatIsoTime(event.eventTime);
        const name = document.createElement('span');
        name.className = 'task-calendar-event-name';
        name.textContent = event.taskName || event.taskId || 'Task';
        button.append(marker, time, name);
        button.addEventListener('click', () => selectEvent(event));
        return button;
    }

    function createCountButton(count, events, label) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'task-calendar-slot-count';
        button.textContent = `(${count})`;
        button.title = `Show all ${count} runs`;
        button.setAttribute('aria-label', `Show all ${count} runs for ${label}`);
        button.addEventListener('click', event => {
            event.stopPropagation();
            openSlotDialog(events, label);
        });
        return button;
    }

    function createMoreButton(hiddenCount, events, label) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'task-calendar-more';
        button.textContent = `+${hiddenCount} more`;
        button.addEventListener('click', event => {
            event.stopPropagation();
            openSlotDialog(events, label);
        });
        return button;
    }

    function renderMonth() {
        const { start, end } = visibleRange();
        const grid = document.createElement('div');
        grid.className = 'task-calendar-month';

        ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'].forEach(day => {
            const heading = document.createElement('div');
            heading.className = 'task-calendar-weekday';
            heading.textContent = day;
            grid.appendChild(heading);
        });

        const anchorMonth = state.anchorDate.getUTCMonth();
        for (let cursor = start; cursor < end; cursor = addDays(cursor, 1)) {
            const key = dateKey(cursor);
            const allEvents = eventsForDate(key);
            const events = eventsForDisplay(allEvents);
            const cell = document.createElement('div');
            cell.className = 'task-calendar-day-cell';
            if (cursor.getUTCMonth() !== anchorMonth) cell.classList.add('outside-month');
            if (key === config.dataset.initialDate) cell.classList.add('server-today');
            if (slotMatchesSearch(allEvents)) cell.classList.add('calendar-search-match');

            const header = document.createElement('div');
            header.className = 'task-calendar-day-header';
            const number = document.createElement('span');
            number.className = 'task-calendar-day-number';
            number.textContent = String(cursor.getUTCDate());
            header.appendChild(number);
            if (events.length > 0) header.appendChild(createCountButton(events.length, events, formatDateKeyLong(key)));
            cell.appendChild(header);

            const eventList = document.createElement('div');
            eventList.className = 'task-calendar-day-events';
            events.slice(0, 3).forEach(event => eventList.appendChild(createEventButton(event, true)));
            if (events.length > 3) eventList.appendChild(createMoreButton(events.length - 3, events, formatDateKeyLong(key)));
            cell.appendChild(eventList);
            grid.appendChild(cell);
        }
        stage.replaceChildren(grid);
    }

    function renderWeek() {
        const start = startOfWeek(state.anchorDate);
        const headers = document.createElement('div');
        headers.className = 'task-calendar-week-headers';
        const spacer = document.createElement('div');
        spacer.className = 'task-calendar-time-header';
        headers.appendChild(spacer);
        for (let day = 0; day < 7; day += 1) {
            const date = addDays(start, day);
            const key = dateKey(date);
            const heading = document.createElement('div');
            heading.className = 'task-calendar-week-day-heading';
            if (key === config.dataset.initialDate) heading.classList.add('server-today');
            const weekday = document.createElement('span');
            weekday.textContent = formatDate(date, { weekday: 'short' });
            const dayNumber = document.createElement('strong');
            dayNumber.textContent = String(date.getUTCDate());
            heading.append(weekday, dayNumber);
            headers.appendChild(heading);
        }

        const scroller = document.createElement('div');
        scroller.className = 'task-calendar-time-scroller';
        const grid = document.createElement('div');
        grid.className = 'task-calendar-week-grid';
        for (let hour = 0; hour < 24; hour += 1) {
            const time = document.createElement('div');
            time.className = 'task-calendar-hour-label';
            time.textContent = formatHour(hour);
            grid.appendChild(time);

            for (let day = 0; day < 7; day += 1) {
                const date = addDays(start, day);
                const key = dateKey(date);
                const allEvents = eventsForHour(key, hour);
                const events = eventsForDisplay(allEvents);
                const slot = document.createElement('div');
                slot.className = 'task-calendar-hour-slot';
                if (key === config.dataset.initialDate) slot.classList.add('server-today');
                if (slotMatchesSearch(allEvents)) slot.classList.add('calendar-search-match');
                if (events.length > 0) slot.appendChild(createCountButton(events.length, events, `${formatDateKeyLong(key)}, ${formatHour(hour)}`));
                const list = document.createElement('div');
                list.className = 'task-calendar-hour-events';
                events.slice(0, 2).forEach(event => list.appendChild(createEventButton(event, true)));
                if (events.length > 2) list.appendChild(createMoreButton(events.length - 2, events, `${formatDateKeyLong(key)}, ${formatHour(hour)}`));
                slot.appendChild(list);
                grid.appendChild(slot);
            }
        }
        scroller.appendChild(grid);
        const wrapper = document.createElement('div');
        wrapper.className = 'task-calendar-week';
        wrapper.append(headers, scroller);
        stage.replaceChildren(wrapper);
        scrollTimeGrid(scroller);
    }

    function renderDay() {
        const key = dateKey(state.anchorDate);
        const scroller = document.createElement('div');
        scroller.className = 'task-calendar-time-scroller task-calendar-day-scroller';
        const grid = document.createElement('div');
        grid.className = 'task-calendar-day-grid';
        for (let hour = 0; hour < 24; hour += 1) {
            const allEvents = eventsForHour(key, hour);
            const events = eventsForDisplay(allEvents);
            const time = document.createElement('div');
            time.className = 'task-calendar-hour-label';
            time.textContent = formatHour(hour);
            const slot = document.createElement('div');
            slot.className = 'task-calendar-hour-slot task-calendar-day-hour-slot';
            if (slotMatchesSearch(allEvents)) slot.classList.add('calendar-search-match');
            if (events.length > 0) slot.appendChild(createCountButton(events.length, events, `${formatDateKeyLong(key)}, ${formatHour(hour)}`));
            const list = document.createElement('div');
            list.className = 'task-calendar-hour-events task-calendar-day-hour-events';
            events.slice(0, 5).forEach(event => list.appendChild(createEventButton(event)));
            if (events.length > 5) list.appendChild(createMoreButton(events.length - 5, events, `${formatDateKeyLong(key)}, ${formatHour(hour)}`));
            slot.appendChild(list);
            grid.append(time, slot);
        }
        scroller.appendChild(grid);
        stage.replaceChildren(scroller);
        scrollTimeGrid(scroller);
    }

    function scrollTimeGrid(scroller) {
        window.requestAnimationFrame(() => {
            // Use a stable workday starting position; browser-local time may differ from MOVEit server time.
            const target = document.body.classList.contains('calendar-fixed-page') ? stage : scroller;
            target.scrollTop = 5 * 76;
        });
    }

    function renderCalendar() {
        updateTitle();
        viewButtons.forEach(button => button.classList.toggle('active', button.dataset.calendarView === state.view));
        if (state.view === 'week') renderWeek();
        else if (state.view === 'day') renderDay();
        else {
            renderMonth();
            if (document.body.classList.contains('calendar-fixed-page')) stage.scrollTop = 0;
        }
        renderLegend();
    }

    async function loadCalendar() {
        const requestId = ++state.requestSequence;
        const range = visibleRange();
        const url = new URL(endpoint('calendarDataUrl'), window.location.href);
        url.searchParams.set('start', dateKey(range.start));
        url.searchParams.set('end', dateKey(range.end));
        if (loading) loading.hidden = false;
        if (errorBanner) { errorBanner.hidden = true; errorBanner.textContent = ''; }
        if (warningBanner) { warningBanner.hidden = true; warningBanner.textContent = ''; }

        try {
            const data = await fetchJson(url.toString());
            if (requestId !== state.requestSequence) return;
            state.events = Array.isArray(data?.events) ? data.events : [];
            rebuildEventIndexes();
            rebuildSearchMatches();
            const warnings = [];
            const unsupported = Number(data?.unsupportedScheduleCount || 0);
            if (unsupported > 0)
                warnings.push(`${unsupported} schedule(s) contained definitions MAUI could not project safely; affected future occurrences were omitted.`);
            if (data?.historicalLimitReached)
                warnings.push('The 100,000-row historical Task Run safety limit was reached for this range; additional past runs may exist.');
            if (warningBanner && warnings.length > 0) {
                warningBanner.textContent = warnings.join(' ');
                warningBanner.hidden = false;
            }
            renderCalendar();

            if (state.selectedEvent) {
                const replacement = state.events.find(item => item.id === state.selectedEvent.id);
                if (replacement) selectEvent(replacement, false);
            }
        } catch (error) {
            if (requestId !== state.requestSequence) return;
            state.events = [];
            rebuildEventIndexes();
            rebuildSearchMatches();
            renderCalendar();
            if (errorBanner) {
                errorBanner.textContent = error.message || 'Unable to load Task Calendar data.';
                errorBanner.hidden = false;
            }
        } finally {
            if (requestId === state.requestSequence) {
                if (loading) loading.hidden = true;
                requestAnimationFrame(sizeCalendarToViewport);
            }
        }
    }

    function openSlotDialog(events, label) {
        if (!slotDialog || !slotList) return;
        if (slotTitle) slotTitle.textContent = `${events.length} Run${events.length === 1 ? '' : 's'}`;
        if (slotSubtitle) slotSubtitle.textContent = label;
        slotList.replaceChildren();
        events.slice().sort(compareEvents).forEach(event => {
            const row = document.createElement('button');
            row.type = 'button';
            row.className = `task-calendar-slot-row ${event.kind === 'upcoming' ? 'upcoming' : 'actual'}`;
            applyEventColor(row, event);
            const time = document.createElement('span');
            time.className = 'task-calendar-slot-row-time';
            time.textContent = formatIsoTime(event.eventTime);
            const body = document.createElement('span');
            body.className = 'task-calendar-slot-row-body';
            const name = document.createElement('strong');
            name.textContent = event.taskName || event.taskId;
            const meta = document.createElement('span');
            meta.textContent = event.kind === 'upcoming' ? 'Upcoming' : (event.status || 'Past Run');
            body.append(name, meta);
            row.append(time, body);
            row.addEventListener('click', () => {
                slotDialog.close();
                selectEvent(event);
            });
            slotList.appendChild(row);
        });
        slotDialog.showModal();
    }

    function addDetail(container, label, value) {
        if (!container) return;
        const row = document.createElement('div');
        row.className = 'calendar-detail-row';
        const key = document.createElement('span');
        key.textContent = label;
        const text = document.createElement('strong');
        text.textContent = String(value ?? '').trim() || '—';
        row.append(key, text);
        container.appendChild(row);
    }

    async function selectEvent(event, scrollIntoView = true) {
        state.selectedEvent = event;
        if (detailsEmpty) detailsEmpty.hidden = true;
        if (detailsContent) detailsContent.hidden = false;
        if (detailsKind) {
            detailsKind.textContent = event.kind === 'upcoming' ? 'Upcoming Run' : 'Past Run';
            detailsKind.className = `calendar-details-kind ${event.kind === 'upcoming' ? 'upcoming' : 'actual'}`;
        }
        if (detailsName) {
            const fullName = event.taskName || event.taskId || 'Task';
            detailsName.textContent = fullName;
            detailsName.title = fullName;
        }
        if (detailsId) detailsId.textContent = `Task ID ${event.taskId || '—'}`;
        if (detailsError) { detailsError.hidden = true; detailsError.textContent = ''; }
        runDetails?.replaceChildren();
        taskDetails?.replaceChildren();
        organizationDetails?.replaceChildren();

        if (event.kind === 'upcoming') {
            addDetail(runDetails, 'Scheduled', formatIsoDateTime(event.eventTime));
            addDetail(runDetails, 'Status', 'Upcoming');
            addDetail(runDetails, 'Schedule', event.schedule);
            addDetail(runDetails, 'Notes', event.notes);
        } else {
            addDetail(runDetails, 'Started', formatIsoDateTime(event.eventTime));
            addDetail(runDetails, 'Ended', event.endTime ? formatIsoDateTime(event.endTime) : '—');
            addDetail(runDetails, 'Duration', formatDuration(event.durationSeconds));
            addDetail(runDetails, 'Status', event.status);
            addDetail(runDetails, 'Status Message', event.statusMessage);
            addDetail(runDetails, 'Run ID', event.runId);
        }

        addDetail(taskDetails, 'Task Name', event.taskName);
        addDetail(taskDetails, 'Task ID', event.taskId);
        addDetail(taskDetails, 'Description', event.description);
        addDetail(organizationDetails, 'Resource Group', event.resourceGroups || event.resourceGroup);
        addDetail(organizationDetails, 'MAUI Folder', event.mauiFolder);

        if (scrollIntoView && document.querySelector('[data-calendar-details]'))
            document.querySelector('[data-calendar-details]').scrollIntoView({ block: 'nearest', behavior: 'smooth' });

        await loadTaskDetails(event);
    }

    async function loadTaskDetails(event) {
        const taskId = String(event?.taskId || '').trim();
        if (!taskId) return;
        try {
            let promise = state.taskDetailsCache.get(taskId);
            if (!promise) {
                const url = new URL(endpoint('taskDetailsUrl'), window.location.href);
                url.searchParams.set('id', taskId);
                promise = fetchJson(url.toString());
                state.taskDetailsCache.set(taskId, promise);
            }
            const data = await promise;
            if (state.selectedEvent?.id !== event.id) return;
            const task = data?.task || {};
            taskDetails?.replaceChildren();
            addDetail(taskDetails, 'Task Name', task.name || event.taskName);
            addDetail(taskDetails, 'Task ID', task.id || event.taskId);
            addDetail(taskDetails, 'Task Type', task.type);
            addDetail(taskDetails, 'Description', task.description || event.description);
            addDetail(taskDetails, 'Enabled', task.enabled ? 'Yes' : 'No');
        } catch (error) {
            state.taskDetailsCache.delete(taskId);
            if (state.selectedEvent?.id !== event.id) return;
            if (detailsError) {
                detailsError.textContent = `Task Information: ${error.message || 'Unable to load Task details.'}`;
                detailsError.hidden = false;
            }
        }
    }

    async function openTaskEditor() {
        const selected = state.selectedEvent;
        const id = String(selected?.taskId || '').trim();
        const name = String(selected?.taskName || '').trim();
        if (!id) return false;
        if (!window.MauiEditorRouting?.openTask) {
            showStatus('The MAUI Task editor routing is unavailable. Reload MAUI and try again.', 5000, 'error');
            return false;
        }
        // The shared route resolves Task type and honors the configured MAUI/MOVEit preference.
        // Calendar events need not carry a Task type or maintain another native launcher.
        const opened = await window.MauiEditorRouting.openTask(id, name);
        if (opened) state.taskDetailsCache.delete(id);
        return opened;
    }

    function navigatePeriod(direction) {
        if (state.view === 'month')
            state.anchorDate = new Date(Date.UTC(state.anchorDate.getUTCFullYear(), state.anchorDate.getUTCMonth() + direction, 1));
        else if (state.view === 'week')
            state.anchorDate = addDays(state.anchorDate, direction * 7);
        else
            state.anchorDate = addDays(state.anchorDate, direction);
        loadCalendar();
    }

    viewButtons.forEach(button => button.addEventListener('click', () => {
        const view = String(button.dataset.calendarView || '').trim();
        if (!['month', 'week', 'day'].includes(view) || view === state.view) return;
        state.view = view;
        loadCalendar();
    }));
    previousButton?.addEventListener('click', () => navigatePeriod(-1));
    nextButton?.addEventListener('click', () => navigatePeriod(1));
    todayButton?.addEventListener('click', () => {
        state.anchorDate = parseDateKey(config.dataset.initialDate) || todayUtcDate();
        loadCalendar();
    });
    refreshButton?.addEventListener('click', () => loadCalendar());
    colorBy?.addEventListener('change', () => renderCalendar());
    searchMode?.addEventListener('change', () => {
        state.searchMode = searchMode.value === 'filter' ? 'filter' : 'search';
        if (search) search.placeholder = state.searchMode === 'filter' ? 'Filter task names...' : 'Search task names...';
        rebuildSearchMatches();
        renderCalendar();
    });

    let searchTimer = 0;
    search?.addEventListener('input', () => {
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(() => {
            state.searchTerm = normalizeSearch(search.value);
            rebuildSearchMatches();
            renderCalendar();
        }, 120);
    });
    searchClear?.addEventListener('click', () => {
        if (search) search.value = '';
        state.searchTerm = '';
        rebuildSearchMatches();
        renderCalendar();
        search?.focus({ preventScroll: true });
    });

    openEditorButton?.addEventListener('click', openTaskEditor);
    slotCloseButtons.forEach(button => button.addEventListener('click', () => slotDialog?.close()));
    slotDialog?.addEventListener('click', event => {
        if (event.target === slotDialog) slotDialog.close();
    });

    window.addEventListener('resize', sizeCalendarToViewport);
    sizeCalendarToViewport();
    requestAnimationFrame(sizeCalendarToViewport);
    loadCalendar();
})();
