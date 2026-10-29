// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Dashboard-only live data refresh behavior.
(() => {
    'use strict';

    const root = document.querySelector('[data-dashboard]');
    if (!root) return;

    const summaryUrl = root.dataset.summaryUrl || '';
    const fileActivityUrl = root.dataset.fileActivityUrl || '';
    const runningUrl = root.dataset.runningUrl || '';
    const expirationUrl = root.dataset.expirationUrl || '';
    const summaryIntervalMs = Math.max(10, Number(root.dataset.summaryRefreshSeconds || 60)) * 1000;
    const runningIdleIntervalMs = Math.max(1, Number(root.dataset.runningIdleRefreshSeconds || 10)) * 1000;
    const runningActiveIntervalMs = Math.max(1, Number(root.dataset.runningActiveRefreshSeconds || 3)) * 1000;
    const runningWarningThreshold = 20;
    const runningDangerThreshold = 50;
    const lastRefresh = root.querySelector('[data-dashboard-last-refresh]');
    const liveIndicator = root.querySelector('[data-dashboard-live]');
    const refreshMenu = root.querySelector('.task-refresh-menu[data-menu]');
    const refreshMenuButton = refreshMenu?.querySelector('[data-menu-button]') || null;
    const refreshToggle = root.querySelector('[data-dashboard-refresh-toggle]');
    const refreshNow = root.querySelector('[data-dashboard-refresh-now]');
    const errorBox = root.querySelector('[data-dashboard-error]');
    const runningVolumeAlert = root.querySelector('[data-dashboard-running-volume-alert]');
    const runningVolumeTitle = root.querySelector('[data-dashboard-running-volume-title]');
    const runningVolumeMessage = root.querySelector('[data-dashboard-running-volume-message]');
    const runningKpi = root.querySelector('.dashboard-kpi.status-running');
    const expirationAlert = root.querySelector('[data-dashboard-expiration-alert]');
    const expirationMessage = root.querySelector('[data-dashboard-expiration-message]');
    const licenseCapacity = root.querySelector('[data-dashboard-license-capacity]');
    const taskLicenseAlert = root.querySelector('[data-dashboard-task-license-alert]');
    const hostLicenseAlert = root.querySelector('[data-dashboard-host-license-alert]');
    const transferChart = root.querySelector('[data-dashboard-transfer-chart]');
    const transferFiles = root.querySelector('[data-dashboard-transfer-files]');
    const transferBytes = root.querySelector('[data-dashboard-transfer-bytes]');
    const transferLookback = root.querySelector('[data-dashboard-transfer-lookback]');

    let paused = false;
    let summaryTimer = null;
    let runningTimer = null;
    let summaryBusy = false;
    let fileActivityBusy = false;
    let runningBusy = false;
    let summaryError = '';
    let fileActivityError = '';
    let runningError = '';
    let currentRunningIntervalMs = runningIdleIntervalMs;
    let transferTrendData = null;
    let transferResizeFrame = null;
    let transferResizeObserver = null;

    // Purpose: Updates text content for a Dashboard value or placeholder.
    const setText = (selector, value, suffix = '') => {
        const element = root.querySelector(selector);
        if (!element) return;
        if (value == null) {
            element.textContent = '—';
            return;
        }
        element.textContent = `${Number(value).toLocaleString()}${suffix}`;
    };

    // Purpose: Formats a time value for the current UI.
    const formatTime = value => {
        if (!value) return '—';
        const date = new Date(value);
        return Number.isNaN(date.valueOf()) ? '—' : date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' });
    };

    // Purpose: Returns whether a timestamp already includes an explicit time-zone designator.
    const hasExplicitZone = value => /(?:Z|[+-]\d{2}:?\d{2})$/i.test(String(value || '').trim());

    // MOVEit running-task timestamps may be returned as server-local wall-clock values such as
    // 2026-09-16T16:36:01.000 with no timezone suffix. Parse those components explicitly so the intent
    // remains stable across browsers while still honoring timestamps that include an explicit zone.
    const parseMoveItDateTime = value => {
        if (!value) return null;
        const text = String(value).trim();
        if (hasExplicitZone(text)) {
            const zoned = new Date(text);
            return Number.isNaN(zoned.valueOf()) ? null : zoned;
        }

        const match = text.match(/^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,3}))?)?/);
        if (match) {
            const millis = Number((match[7] || '').padEnd(3, '0') || 0);
            const local = new Date(
                Number(match[1]), Number(match[2]) - 1, Number(match[3]),
                Number(match[4]), Number(match[5]), Number(match[6] || 0), millis);
            return Number.isNaN(local.valueOf()) ? null : local;
        }

        const fallback = new Date(text);
        return Number.isNaN(fallback.valueOf()) ? null : fallback;
    };

    // Purpose: Formats a date/time value for the current UI.
    const formatDateTime = value => {
        if (!value) return '—';
        const date = new Date(value);
        if (Number.isNaN(date.valueOf())) return '—';
        const today = new Date();
        return date.toDateString() === today.toDateString()
            ? date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' })
            : date.toLocaleString([], { month: 'numeric', day: 'numeric', hour: 'numeric', minute: '2-digit' });
    };

    // Purpose: Formats a MOVEit date/time value for the Dashboard.
    const formatMoveItDateTime = value => {
        const date = parseMoveItDateTime(value);
        if (!date) return '—';
        const today = new Date();
        return date.toDateString() === today.toDateString()
            ? date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' })
            : date.toLocaleString([], { month: 'numeric', day: 'numeric', hour: 'numeric', minute: '2-digit' });
    };

    // Purpose: Formats a byte count using compact binary units.
    const formatBytes = value => {
        const number = Number(value);
        if (!Number.isFinite(number) || number < 0) return '—';
        if (number === 0) return '0 B';
        const units = ['B', 'KB', 'MB', 'GB', 'TB'];
        const index = Math.min(Math.floor(Math.log(number) / Math.log(1024)), units.length - 1);
        const amount = number / Math.pow(1024, index);
        return `${amount >= 100 || index === 0 ? amount.toFixed(0) : amount.toFixed(1)} ${units[index]}`;
    };

    // Purpose: Formats large chart-axis counts without obscuring relative values.
    const formatCompactNumber = value => {
        const number = Number(value);
        if (!Number.isFinite(number)) return '0';
        if (Math.abs(number) < 1000) return Math.round(number).toLocaleString();
        return new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(number);
    };

    // Purpose: Expands a positive chart maximum to a readable 1/2/5-based boundary.
    const niceChartMaximum = value => {
        const number = Number(value);
        if (!Number.isFinite(number) || number <= 0) return 1;
        const power = Math.pow(10, Math.floor(Math.log10(number)));
        const scaled = number / power;
        const factor = scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10;
        return factor * power;
    };

    // Purpose: Creates an SVG element with the supplied attributes.
    const createChartSvgElement = (name, attributes = {}) => {
        const element = document.createElementNS('http://www.w3.org/2000/svg', name);
        for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, String(value));
        return element;
    };

    // Purpose: Formats an hourly chart-axis label.
    const formatChartHour = value => {
        const date = new Date(value);
        return Number.isNaN(date.valueOf()) ? '' : date.toLocaleTimeString([], { hour: 'numeric' });
    };

    // Purpose: Formats a rolling-hour range for the transfer chart tooltip.
    const formatChartRange = (startValue, endValue) => {
        const start = new Date(startValue);
        const end = new Date(endValue);
        if (Number.isNaN(start.valueOf()) || Number.isNaN(end.valueOf())) return 'Hourly activity';
        const sameDay = start.toDateString() === end.toDateString();
        const startText = start.toLocaleString([], {
            ...(sameDay ? {} : { month: 'numeric', day: 'numeric' }),
            hour: 'numeric', minute: '2-digit'
        });
        const endText = end.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
        return `${startText} – ${endText}`;
    };

    // Purpose: Draws the dual-metric transfer trend from the same successful File Activity dataset as the KPI.
    function drawTransferTrendChart() {
        if (!transferChart || !transferTrendData) return;
        const points = Array.isArray(transferTrendData.points) ? transferTrendData.points : [];
        if (points.length === 0) return;

        const hasActivity = points.some(point => Number(point.filesTransferred || 0) > 0 || Number(point.totalBytesTransferred || 0) > 0);
        if (!hasActivity) {
            transferChart.replaceChildren();
            const empty = document.createElement('div');
            empty.className = 'dashboard-chart-empty';
            const lookbackHours = Math.max(1, Number(transferTrendData.lookbackHours || 24));
            empty.textContent = `No transfer activity was recorded during the past ${lookbackHours} hours.`;
            transferChart.appendChild(empty);
            return;
        }

        const width = Math.max(520, Math.round(transferChart.clientWidth || 680));
        const height = 315;
        const margin = { top: 22, right: 62, bottom: 40, left: 52 };
        const plotWidth = Math.max(1, width - margin.left - margin.right);
        const plotHeight = Math.max(1, height - margin.top - margin.bottom);
        const slotWidth = plotWidth / points.length;
        const barWidth = Math.max(3, Math.min(18, slotWidth * .58));
        const maxFiles = niceChartMaximum(Math.max(...points.map(point => Number(point.filesTransferred || 0))));
        const maxBytes = niceChartMaximum(Math.max(...points.map(point => Number(point.totalBytesTransferred || 0))));

        transferChart.replaceChildren();
        const svg = createChartSvgElement('svg', { viewBox: `0 0 ${width} ${height}`, role: 'img' });
        const lookbackHours = Math.max(1, Number(transferTrendData.lookbackHours || 24));
        svg.setAttribute('aria-label', `Hourly files transferred and total bytes transferred during the past ${lookbackHours} hours.`);
        const title = createChartSvgElement('title');
        title.textContent = 'Transfer Activity Trend';
        svg.appendChild(title);

        const yForFiles = value => margin.top + plotHeight - (Number(value || 0) / maxFiles) * plotHeight;
        const yForBytes = value => margin.top + plotHeight - (Number(value || 0) / maxBytes) * plotHeight;

        for (let tick = 0; tick <= 4; tick++) {
            const ratio = tick / 4;
            const y = margin.top + plotHeight - ratio * plotHeight;
            svg.appendChild(createChartSvgElement('line', {
                x1: margin.left, y1: y, x2: width - margin.right, y2: y, class: 'dashboard-chart-grid-line'
            }));

            const leftLabel = createChartSvgElement('text', {
                x: margin.left - 8, y: y + 3, 'text-anchor': 'end', class: 'dashboard-chart-axis-label'
            });
            leftLabel.textContent = formatCompactNumber(maxFiles * ratio);
            svg.appendChild(leftLabel);

            const rightLabel = createChartSvgElement('text', {
                x: width - margin.right + 8, y: y + 3, 'text-anchor': 'start', class: 'dashboard-chart-axis-label'
            });
            rightLabel.textContent = formatBytes(maxBytes * ratio);
            svg.appendChild(rightLabel);
        }

        svg.appendChild(createChartSvgElement('line', {
            x1: margin.left, y1: margin.top + plotHeight, x2: width - margin.right, y2: margin.top + plotHeight, class: 'dashboard-chart-axis-line'
        }));

        const filesTitle = createChartSvgElement('text', { x: margin.left, y: 11, class: 'dashboard-chart-axis-title' });
        filesTitle.textContent = 'Files';
        svg.appendChild(filesTitle);
        const bytesTitle = createChartSvgElement('text', { x: width - margin.right, y: 11, 'text-anchor': 'end', class: 'dashboard-chart-axis-title' });
        bytesTitle.textContent = 'Data';
        svg.appendChild(bytesTitle);

        points.forEach((point, index) => {
            const centerX = margin.left + slotWidth * (index + .5);
            const y = yForFiles(point.filesTransferred);
            svg.appendChild(createChartSvgElement('rect', {
                x: centerX - barWidth / 2,
                y,
                width: barWidth,
                height: Math.max(0, margin.top + plotHeight - y),
                rx: 2,
                class: 'dashboard-chart-bar'
            }));

            if (index % 3 === 0 || index === points.length - 1) {
                const label = createChartSvgElement('text', {
                    x: centerX, y: height - 13, 'text-anchor': 'middle', class: 'dashboard-chart-axis-label'
                });
                label.textContent = formatChartHour(point.startTime);
                svg.appendChild(label);
            }
        });

        const linePoints = points.map((point, index) => {
            const x = margin.left + slotWidth * (index + .5);
            const y = yForBytes(point.totalBytesTransferred);
            return { x, y, point };
        });
        if (linePoints.length > 0) {
            const path = createChartSvgElement('path', {
                d: linePoints.map((item, index) => `${index === 0 ? 'M' : 'L'} ${item.x.toFixed(2)} ${item.y.toFixed(2)}`).join(' '),
                class: 'dashboard-chart-line'
            });
            svg.appendChild(path);

            for (const item of linePoints) {
                svg.appendChild(createChartSvgElement('circle', { cx: item.x, cy: item.y, r: 2.8, class: 'dashboard-chart-point' }));
            }
        }

        const tooltip = document.createElement('div');
        tooltip.className = 'dashboard-chart-tooltip';
        tooltip.hidden = true;

        const showTooltip = (event, point) => {
            const rect = transferChart.getBoundingClientRect();
            const x = Math.max(88, Math.min(rect.width - 88, event.clientX - rect.left));
            const y = Math.max(82, Math.min(rect.height - 8, event.clientY - rect.top));
            tooltip.replaceChildren();

            const heading = document.createElement('strong');
            heading.textContent = formatChartRange(point.startTime, point.endTime);
            tooltip.appendChild(heading);

            const filesRow = document.createElement('div');
            filesRow.className = 'dashboard-chart-tooltip-row';
            filesRow.innerHTML = '<span class="dashboard-chart-tooltip-dot is-files"></span>';
            filesRow.append(`${Number(point.filesTransferred || 0).toLocaleString()} files transferred`);
            tooltip.appendChild(filesRow);

            const bytesRow = document.createElement('div');
            bytesRow.className = 'dashboard-chart-tooltip-row';
            bytesRow.innerHTML = '<span class="dashboard-chart-tooltip-dot is-bytes"></span>';
            bytesRow.append(`${formatBytes(point.totalBytesTransferred)} transferred`);
            tooltip.appendChild(bytesRow);

            tooltip.style.left = `${x}px`;
            tooltip.style.top = `${y}px`;
            tooltip.hidden = false;
        };

        points.forEach((point, index) => {
            const hit = createChartSvgElement('rect', {
                x: margin.left + slotWidth * index,
                y: margin.top,
                width: slotWidth,
                height: plotHeight,
                class: 'dashboard-chart-hit'
            });
            hit.addEventListener('mouseenter', event => showTooltip(event, point));
            hit.addEventListener('mousemove', event => showTooltip(event, point));
            hit.addEventListener('mouseleave', () => { tooltip.hidden = true; });
            svg.appendChild(hit);
        });

        transferChart.append(svg, tooltip);
    }

    // Purpose: Renders transfer totals and the responsive trend chart. A trailing + marks a progressive
    // or safety-limited lower bound until the native File Activity snapshot is complete.
    function renderTransferTrend(trend, lowerBound = false) {
        transferTrendData = trend && typeof trend === 'object' ? trend : null;
        const suffix = lowerBound ? '+' : '';
        if (transferFiles) transferFiles.textContent = transferTrendData ? `${Number(transferTrendData.totalFilesTransferred || 0).toLocaleString()}${suffix}` : '—';
        if (transferBytes) transferBytes.textContent = transferTrendData ? `${formatBytes(transferTrendData.totalBytesTransferred)}${suffix}` : '—';
        if (transferLookback && transferTrendData) transferLookback.textContent = String(Math.max(1, Number(transferTrendData.lookbackHours || 24)));
        if (!transferChart) return;
        if (!transferTrendData) {
            renderTransferTrendUnavailable('Transfer activity is unavailable.');
            return;
        }
        drawTransferTrendChart();
    }

    // Purpose: Clears the transfer chart while retaining its Dashboard card.
    function renderTransferTrendUnavailable(message) {
        transferTrendData = null;
        if (transferFiles) {
            transferFiles.textContent = '—';
            transferFiles.removeAttribute('title');
        }
        if (transferBytes) {
            transferBytes.textContent = '—';
            transferBytes.removeAttribute('title');
        }
        if (!transferChart) return;
        transferChart.replaceChildren();
        const empty = document.createElement('div');
        empty.className = 'dashboard-chart-empty';
        empty.textContent = message || 'Transfer activity is unavailable.';
        transferChart.appendChild(empty);
    }

    // Purpose: Returns the CSS status class for a Dashboard task result.
    const statusClass = status => {
        const value = String(status || '').toLowerCase();
        if (value.includes('fail')) return 'status-failed';
        if (value.includes('success')) return 'status-success';
        if (value.includes('no transfer') || value.includes('no xfer')) return 'status-no-transfers';
        if (value.includes('running')) return 'status-running';
        return '';
    };

    // Purpose: Shows or clears the Dashboard error message.
    const updateError = () => {
        if (!errorBox) return;
        const messages = [summaryError, fileActivityError, runningError].filter(Boolean);
        errorBox.textContent = messages.join('  ');
        errorBox.hidden = messages.length === 0;
    };

    // Purpose: Records and displays the most recent successful Dashboard refresh time.
    const noteRefresh = value => {
        if (lastRefresh) lastRefresh.textContent = `Last Refresh: ${formatTime(value || new Date().toISOString())}`;
    };

    // Purpose: Fetches a Dashboard JSON endpoint and normalizes request errors.
    async function fetchJson(url) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
        let payload = null;
        try { payload = await response.json(); } catch { /* leave null */ }
        if (response.status === 401) {
            window.location.replace(window.mauiAppUrl('/Account/SessionExpired'));
            throw new Error('Your MOVEit session has expired.');
        }
        if (!response.ok || payload?.ok === false) throw new Error(payload?.message || `Dashboard request failed (${response.status}).`);
        return payload;
    }

    // Purpose: Renders recent.
    function renderRecent(items) {
        const body = root.querySelector('[data-dashboard-recent-body]');
        if (!body) return;
        body.replaceChildren();
        if (!Array.isArray(items) || items.length === 0) {
            const row = body.insertRow();
            const cell = row.insertCell();
            cell.colSpan = 5;
            cell.className = 'dashboard-table-empty';
            cell.textContent = 'No recent task-run activity is available.';
            return;
        }

        for (const item of items) {
            const row = body.insertRow();
            const name = row.insertCell();
            const link = document.createElement('a');
            link.className = 'dashboard-task-link';
            link.textContent = item.taskName || `Task ${item.taskId || ''}`;
            link.href = `${window.mauiAppUrl('/Records/TaskRun')}?taskId=${encodeURIComponent(item.taskId || '')}&taskName=${encodeURIComponent(item.taskName || '')}`;
            name.appendChild(link);
            row.insertCell().textContent = formatDateTime(item.startTime);
            const statusCell = row.insertCell();
            const badge = document.createElement('span');
            badge.className = `dashboard-status ${statusClass(item.status)}`.trim();
            badge.textContent = item.status || '—';
            if (String(item.status || '').toLowerCase().includes('fail') && item.statusMessage) badge.title = item.statusMessage;
            statusCell.appendChild(badge);
            row.insertCell().textContent = item.filesSent == null ? '—' : Number(item.filesSent).toLocaleString();
            row.insertCell().textContent = formatBytes(item.totalBytesSent);
        }
    }

    // Purpose: Renders recent unavailable.
    function renderRecentUnavailable(message) {
        const body = root.querySelector('[data-dashboard-recent-body]');
        if (!body) return;
        body.replaceChildren();
        const row = body.insertRow();
        const cell = row.insertCell();
        cell.colSpan = 5;
        cell.className = 'dashboard-table-empty';
        cell.textContent = message || 'Recent task activity is unavailable.';
    }

    // Purpose: Shows or clears the running-task volume advisory for the current live count.
    function renderRunningVolumeAdvisory(count) {
        if (!runningVolumeAlert || !runningVolumeTitle || !runningVolumeMessage) return;

        const runningCount = Number(count);
        runningVolumeAlert.classList.remove('is-warning', 'is-danger');
        runningKpi?.classList.remove('is-warning', 'is-danger');

        if (!Number.isFinite(runningCount) || runningCount < runningWarningThreshold) {
            runningVolumeAlert.hidden = true;
            return;
        }

        if (runningCount >= runningDangerThreshold) {
            runningVolumeAlert.classList.add('is-danger');
            runningKpi?.classList.add('is-danger');
            runningVolumeTitle.textContent = 'Warning:';
            runningVolumeMessage.textContent = 'An unusually high number of tasks are currently running. System performance may be affected.';
        } else {
            runningVolumeAlert.classList.add('is-warning');
            runningKpi?.classList.add('is-warning');
            runningVolumeTitle.textContent = 'Attention:';
            runningVolumeMessage.textContent = 'A high number of tasks are currently running. Monitor system activity and performance.';
        }

        runningVolumeAlert.hidden = false;
    }

    // Purpose: Renders running.
    function renderRunning(items) {
        const body = root.querySelector('[data-dashboard-running-body]');
        if (!body) return;
        body.replaceChildren();
        const list = Array.isArray(items) ? items : [];
        setText('[data-dashboard-running-tasks]', list.length);
        renderRunningVolumeAdvisory(list.length);
        if (list.length === 0) {
            const row = body.insertRow();
            const cell = row.insertCell();
            cell.colSpan = 4;
            cell.className = 'dashboard-table-empty';
            cell.textContent = 'No tasks are currently running.';
            return;
        }

        for (const item of list) {
            const row = body.insertRow();
            const name = row.insertCell();
            const link = document.createElement('a');
            link.className = 'dashboard-task-link';
            link.textContent = item.taskName || `Task ${item.taskId || ''}`;
            link.href = `${window.mauiAppUrl('/Records/TaskRun')}?taskId=${encodeURIComponent(item.taskId || '')}&taskName=${encodeURIComponent(item.taskName || '')}`;
            name.appendChild(link);
            row.insertCell().textContent = formatMoveItDateTime(item.startTime);
            row.insertCell().textContent = String(item.startedBy || '').trim() || '—';
            const current = row.insertCell();
            current.className = 'dashboard-running-status';
            current.textContent = item.lastErrorText || item.currentStatus || 'Running';
            if (item.lastErrorText) current.classList.add('has-error');
        }
    }

    // Purpose: Renders running unavailable.
    function renderRunningUnavailable(message) {
        setText('[data-dashboard-running-tasks]', null);
        const body = root.querySelector('[data-dashboard-running-body]');
        if (!body) return;
        body.replaceChildren();
        const row = body.insertRow();
        const cell = row.insertCell();
        cell.colSpan = 4;
        cell.className = 'dashboard-table-empty';
        cell.textContent = message || 'Running-task status is unavailable.';
    }

    // Purpose: Clears core values.
    function clearCoreValues() {
        setText('[data-dashboard-total-tasks]', null);
        setText('[data-dashboard-hosts]', null);
        setText('[data-dashboard-failed-task-runs]', null);
        setText('[data-dashboard-success-task-runs]', null);
        setText('[data-dashboard-no-transfer-task-runs]', null);
    }

    // Purpose: Clears file activity values.
    function clearFileActivityValues() {
        setText('[data-dashboard-failed-file-activity]', null);
        setText('[data-dashboard-success-file-activity]', null);
        root.querySelectorAll('[data-dashboard-failed-file-activity], [data-dashboard-success-file-activity]').forEach(element => element.removeAttribute('title'));
    }

    // Purpose: Renders license capacity advisory.
    function renderLicenseCapacityAdvisory(alert, advisory, titleSelector, messageSelector) {
        if (!alert) return false;
        alert.classList.remove('is-warning', 'is-danger');
        if (!advisory || !advisory.message) {
            alert.hidden = true;
            return false;
        }

        const severity = String(advisory.severity || '').toLowerCase();
        alert.classList.add(severity === 'danger' ? 'is-danger' : 'is-warning');
        const title = alert.querySelector(titleSelector);
        const message = alert.querySelector(messageSelector);
        if (title) title.textContent = severity === 'danger' ? 'License Warning:' : 'Capacity Warning:';
        if (message) message.textContent = advisory.message;
        alert.hidden = false;
        return true;
    }

    // Purpose: Renders license capacity.
    function renderLicenseCapacity(capacity) {
        const taskVisible = renderLicenseCapacityAdvisory(
            taskLicenseAlert, capacity?.tasks, '[data-dashboard-task-license-title]', '[data-dashboard-task-license-message]');
        const hostVisible = renderLicenseCapacityAdvisory(
            hostLicenseAlert, capacity?.hosts, '[data-dashboard-host-license-title]', '[data-dashboard-host-license-message]');
        if (licenseCapacity) licenseCapacity.hidden = !(taskVisible || hostVisible);
    }

    // Purpose: Loads and renders the main Dashboard Task/Host summary.
    async function loadSummary() {
        if (paused || summaryBusy || !summaryUrl) return;
        summaryBusy = true;
        try {
            const data = await fetchJson(summaryUrl);
            const summary = data.summary || {};
            setText('[data-dashboard-total-tasks]', summary.totalTasks);
            setText('[data-dashboard-hosts]', summary.hosts);
            setText('[data-dashboard-failed-task-runs]', summary.failedTasks);
            setText('[data-dashboard-success-task-runs]', summary.successfulTasks);
            setText('[data-dashboard-no-transfer-task-runs]', summary.noTransferTasks);
            renderRecent(summary.recentTaskActivity);
            renderLicenseCapacity(data.licenseCapacity);
            noteRefresh(summary.refreshedUtc);
            summaryError = '';
            updateError();
        } catch (error) {
            clearCoreValues();
            renderRecentUnavailable('Unable to load recent task activity.');
            renderLicenseCapacity(null);
            summaryError = error?.message || 'Unable to refresh Dashboard task/host data.';
            updateError();
        } finally {
            summaryBusy = false;
        }
    }

    // Purpose: Loads and renders the Dashboard File Activity summary. When a 24-hour snapshot exceeds one
    // 10K report page, each completed page is rendered immediately and the next page is requested until the
    // server reports a complete snapshot. Normal and manual refreshes then request only incremental activity.
    async function loadFileActivitySummary() {
        if (paused || fileActivityBusy || !fileActivityUrl) return;
        fileActivityBusy = true;
        let renderedAnyPage = false;
        try {
            let continueProgressiveLoad = false;
            do {
                const data = await fetchJson(fileActivityUrl);
                const summary = data.summary || {};
                const isComplete = summary.isComplete !== false;
                const lowerBound = !isComplete || !!summary.failedLimitReached || !!summary.successfulLimitReached;
                const suffix = lowerBound ? '+' : '';

                setText('[data-dashboard-failed-file-activity]', summary.failedFileActivities, suffix);
                setText('[data-dashboard-success-file-activity]', summary.successfulFileActivities, suffix);
                renderTransferTrend(summary.transferTrend, lowerBound);
                renderedAnyPage = true;

                const progressTitle = !isComplete
                    ? `Loading additional File Activity records. ${Number(summary.rowsLoaded || 0).toLocaleString()} records are currently represented.`
                    : lowerBound
                        ? 'The File Activity safety limit was reached; displayed values are lower bounds.'
                        : '';
                root.querySelectorAll('[data-dashboard-failed-file-activity], [data-dashboard-success-file-activity]').forEach(element => {
                    if (progressTitle) element.setAttribute('title', progressTitle);
                    else element.removeAttribute('title');
                });
                if (transferFiles) {
                    if (progressTitle) transferFiles.setAttribute('title', progressTitle);
                    else transferFiles.removeAttribute('title');
                }
                if (transferBytes) {
                    if (progressTitle) transferBytes.setAttribute('title', progressTitle);
                    else transferBytes.removeAttribute('title');
                }

                noteRefresh(summary.refreshedUtc);
                fileActivityError = '';
                updateError();

                continueProgressiveLoad = !isComplete && !paused;
                if (continueProgressiveLoad) {
                    // Yield so the browser can paint the current lower-bound values before the next report page.
                    await new Promise(resolve => setTimeout(resolve, 0));
                }
            } while (continueProgressiveLoad);
        } catch (error) {
            // If a later progressive page fails, preserve the useful lower-bound values already rendered.
            // The cached cursor remains unchanged, so Resume/next refresh can retry that page safely.
            if (!renderedAnyPage) {
                clearFileActivityValues();
                renderTransferTrendUnavailable('Unable to load transfer activity.');
            }
            fileActivityError = error?.message || 'Unable to refresh Dashboard file activity data.';
            updateError();
        } finally {
            fileActivityBusy = false;
        }
    }

    // Purpose: Schedules running interval.
    function scheduleRunningInterval(intervalMs) {
        const normalized = Math.max(1000, Number(intervalMs) || runningIdleIntervalMs);
        if (runningTimer && currentRunningIntervalMs === normalized) return;
        clearInterval(runningTimer);
        currentRunningIntervalMs = normalized;
        runningTimer = setInterval(() => {
            if (!paused) void loadRunning();
        }, currentRunningIntervalMs);
    }

    // Purpose: Loads and renders the currently running Task list.
    async function loadRunning() {
        if (paused || runningBusy || !runningUrl) return null;
        runningBusy = true;
        try {
            const data = await fetchJson(runningUrl);
            const snapshot = data.snapshot || {};
            const items = Array.isArray(snapshot.items) ? snapshot.items : [];
            renderRunning(items);
            noteRefresh(snapshot.refreshedUtc);
            runningError = '';
            updateError();

            // Poll more aggressively only while work is actually running. Once the running list becomes empty,
            // return to the normal 10-second cadence.
            scheduleRunningInterval(items.length > 0 ? runningActiveIntervalMs : runningIdleIntervalMs);
            return items.length;
        } catch (error) {
            renderRunningUnavailable('Unable to refresh currently running tasks.');
            runningError = error?.message || 'Unable to refresh currently running tasks.';
            updateError();
            scheduleRunningInterval(runningIdleIntervalMs);
            return null;
        } finally {
            runningBusy = false;
        }
    }

    // Purpose: Loads and renders key/certificate expiration information.
    async function loadExpirationSummary() {
        if (!expirationUrl || !expirationAlert || !expirationMessage) return;
        try {
            const data = await fetchJson(expirationUrl);
            const count = Number(data.expiredCount || 0);
            if (!Number.isFinite(count) || count <= 0) {
                expirationAlert.hidden = true;
                return;
            }

            expirationMessage.textContent = count === 1
                ? '1 expired key or certificate requires attention.'
                : `${count.toLocaleString()} expired keys or certificates require attention.`;
            expirationAlert.hidden = false;
        } catch {
            // This is a once-per-page-load advisory. Do not make the operational Dashboard appear unhealthy
            // if the key/certificate inventory cannot be checked independently.
            expirationAlert.hidden = true;
        }
    }

    // Purpose: Schedules the next automatic refresh.
    function schedule() {
        clearInterval(summaryTimer);
        clearInterval(runningTimer);
        runningTimer = null;
        summaryTimer = setInterval(() => {
            if (paused) return;
            void Promise.allSettled([loadSummary(), loadFileActivitySummary()]);
        }, summaryIntervalMs);
        currentRunningIntervalMs = runningIdleIntervalMs;
        scheduleRunningInterval(runningIdleIntervalMs);
    }

    // Purpose: Updates Dashboard refresh controls for the paused/running state.
    function setPaused(value) {
        paused = !!value;
        if (refreshToggle) refreshToggle.textContent = paused ? 'Resume Refresh' : 'Pause Refresh';
        if (liveIndicator) {
            liveIndicator.classList.toggle('is-paused', paused);
            const label = liveIndicator.querySelector('strong');
            if (label) label.textContent = paused ? 'Paused' : 'Live';
        }
        if (!paused) {
            loadRunning();
            void Promise.allSettled([loadSummary(), loadFileActivitySummary()]);
        }
    }

    // Purpose: Keeps the Dashboard refresh-options dropdown independent of grid-only menu wiring.
    function closeRefreshMenu() {
        if (!refreshMenu) return;
        refreshMenu.classList.remove('open');
        refreshMenuButton?.setAttribute('aria-expanded', 'false');
    }

    // Purpose: Opens or closes the Dashboard refresh-options dropdown.
    function toggleRefreshMenu() {
        if (!refreshMenu) return;
        const isOpen = refreshMenu.classList.toggle('open');
        refreshMenuButton?.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
    }

    refreshMenuButton?.addEventListener('click', event => {
        event.stopPropagation();
        toggleRefreshMenu();
    });

    document.addEventListener('click', event => {
        if (event.target.closest('.task-refresh-menu[data-menu]') === refreshMenu) return;
        closeRefreshMenu();
    });

    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || !refreshMenu?.classList.contains('open')) return;
        closeRefreshMenu();
        refreshMenuButton?.focus();
    });

    refreshToggle?.addEventListener('click', () => {
        setPaused(!paused);
        closeRefreshMenu();
    });
    refreshNow?.addEventListener('click', () => {
        closeRefreshMenu();
        const wasPaused = paused;
        paused = false;
        Promise.allSettled([
            loadRunning(),
            loadSummary(),
            loadFileActivitySummary()
        ]).finally(() => { paused = wasPaused; });
    });

    if (transferChart && 'ResizeObserver' in window) {
        transferResizeObserver = new ResizeObserver(() => {
            if (!transferTrendData) return;
            if (transferResizeFrame) cancelAnimationFrame(transferResizeFrame);
            transferResizeFrame = requestAnimationFrame(() => {
                transferResizeFrame = null;
                drawTransferTrendChart();
            });
        });
        transferResizeObserver.observe(transferChart);
    }

    schedule();
    // Start all sources independently/in parallel. File Activity drives both its KPIs and transfer trend from the
    // same rolling snapshot; a cold high-volume snapshot may render progressively before incremental refresh begins.
    void loadRunning();
    void loadExpirationSummary();
    void Promise.allSettled([loadSummary(), loadFileActivitySummary()]);
})();
