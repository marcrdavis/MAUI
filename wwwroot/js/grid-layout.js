/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Shared grid column layout, width, and preference management.
 */

(() => {
    function createManager(options) {
        const {
            grid,
            table,
            tableScroll,
            defaultColumns,
            availableColumns,
            originalColumnOrder,
            fitColumnsToGrid,
            getRows
        } = options;

        const baseColumnWidths = new Map();
        const gridKey = String(grid.dataset.grid || '').trim();
        const preferenceKey = String(grid.dataset.gridPreferenceKey || gridKey).trim();
        const preferencePersistenceEnabled = String(grid.dataset.gridPreferencePersistenceEnabled ?? 'true').toLowerCase() !== 'false';
        let preferenceSaveQueue = Promise.resolve();
        let visibleColumns = [...defaultColumns];
        let columnOrder = [...originalColumnOrder];

        function cssEscape(value) {
            return window.CSS?.escape ? window.CSS.escape(value) : String(value).replace(/"/g, '\\"');
        }

        function getColumns() {
            return Array.from(table.querySelectorAll('.column-header-row th[data-column]'));
        }

        function loadPreferences() {
            const rawPreferences = String(grid.dataset.userGridLayout || '').trim();
            if (!rawPreferences) return false;

            try {
                const saved = JSON.parse(rawPreferences);
                if (Number(saved?.schemaVersion) !== 1) return false;

                const savedOrder = Array.isArray(saved.columnOrder)
                    ? saved.columnOrder.filter(key => originalColumnOrder.includes(key))
                    : [];
                const newColumns = originalColumnOrder.filter(key => !savedOrder.includes(key));
                columnOrder = [...savedOrder, ...newColumns];

                const savedVisible = new Set(Array.isArray(saved.visibleColumns)
                    ? saved.visibleColumns.filter(key => originalColumnOrder.includes(key))
                    : []);
                visibleColumns = originalColumnOrder.filter(key => savedOrder.includes(key)
                    ? savedVisible.has(key)
                    : defaultColumns.includes(key));
                if (!visibleColumns.length) visibleColumns = defaultColumns.filter(key => originalColumnOrder.includes(key));
                if (!visibleColumns.length && originalColumnOrder.length) visibleColumns = [originalColumnOrder[0]];

                if (saved.widths && typeof saved.widths === 'object') applyColumnWidths(saved.widths);
                return true;
            } catch (error) {
                console.warn(`Unable to load MAUI grid layout preference for '${preferenceKey}'.`);
                return false;
            }
        }

        function capturePreferenceState() {
            const widths = {};
            getColumns().forEach(header => {
                const width = getBaseColumnWidth(header);
                if (Number.isFinite(width) && width > 0) widths[header.dataset.column] = Math.round(width * 100) / 100;
            });
            return {
                schemaVersion: 1,
                visibleColumns: [...visibleColumns],
                columnOrder: [...columnOrder],
                widths
            };
        }

        // Serializes writes per grid so rapid reorder/resize/apply actions cannot persist out of order.
        function queuePreferenceOperation(operation, failureMessage) {
            const queued = preferenceSaveQueue.catch(() => undefined).then(operation);
            preferenceSaveQueue = queued;
            void queued.catch(error => {
                console.warn(failureMessage);
                if (typeof window.mauiShowToast === 'function') window.mauiShowToast(failureMessage, 'error');
            });
            return queued;
        }

        function savePreferences() {
            const layout = capturePreferenceState();
            // Saved Views and Report edit mode own their transient layout state. Do not let edits
            // to those definitions overwrite the user's normal built-in-view preference.
            if (!preferencePersistenceEnabled) return Promise.resolve(layout);
            grid.dataset.userGridLayout = JSON.stringify(layout);
            if (!preferenceKey || typeof window.mauiSaveGridLayoutPreference !== 'function') return Promise.resolve(layout);
            return queuePreferenceOperation(
                () => window.mauiSaveGridLayoutPreference(preferenceKey, layout),
                'The grid layout preference could not be saved.');
        }

        function resetPreferences() {
            // Restore Defaults may still reset the in-memory Saved View/Report editor layout, but it must
            // never delete the user's normal persisted layout while one of those definitions is active.
            if (!preferencePersistenceEnabled) return Promise.resolve();
            delete grid.dataset.userGridLayout;
            if (!preferenceKey || typeof window.mauiResetGridLayoutPreference !== 'function') return Promise.resolve();
            return queuePreferenceOperation(
                () => window.mauiResetGridLayoutPreference(preferenceKey),
                'The grid layout preference could not be reset.');
        }

        function applyColumnWidths(widths) {
            Object.entries(widths).forEach(([key, width]) => {
                const numericWidth = Number.parseFloat(width);
                if (Number.isFinite(numericWidth) && numericWidth > 0) baseColumnWidths.set(key, numericWidth);
            });
        }

        function setRenderedColumnWidth(key, width) {
            const safeWidth = Math.max(0, Number(width) || 0);
            const cssWidth = `${safeWidth.toFixed(2)}px`;
            table.querySelectorAll(`[data-column="${cssEscape(key)}"]`).forEach(cell => {
                cell.style.width = cssWidth;
            });
        }

        function setBaseColumnWidth(key, width, minimumWidth = 96) {
            const safeWidth = Math.max(minimumWidth, Math.round(width));
            baseColumnWidths.set(key, safeWidth);
        }

        function getVisibleHeaders() {
            return getColumns().filter(header => !header.classList.contains('column-hidden'));
        }

        function getConfiguredDefaultWidth(header) {
            const defaultWidth = Number.parseFloat(header?.dataset?.defaultWidth);
            return Number.isFinite(defaultWidth) && defaultWidth > 0 ? defaultWidth : null;
        }

        function getBaseColumnWidth(header) {
            const key = header.dataset.column;
            if (baseColumnWidths.has(key)) return baseColumnWidths.get(key);

            const configuredDefaultWidth = getConfiguredDefaultWidth(header);
            const explicitWidth = Number.parseFloat(header.style.width);
            const baseWidth = configuredDefaultWidth
                ?? (Number.isFinite(explicitWidth) && explicitWidth > 0 ? explicitWidth : 160);
            baseColumnWidths.set(key, baseWidth);
            return baseWidth;
        }

        function getRenderedColumnWidth(header) {
            const explicitWidth = Number.parseFloat(header.style.width);
            if (Number.isFinite(explicitWidth)) return explicitWidth;
            const renderedWidth = header.getBoundingClientRect().width;
            return renderedWidth > 0 ? renderedWidth : getBaseColumnWidth(header);
        }

        function getSelectionColumnWidth() {
            const selectionHeader = table.querySelector('.column-header-row .select-column');
            const renderedWidth = selectionHeader?.getBoundingClientRect().width ?? 0;
            return renderedWidth > 0 ? renderedWidth : 44;
        }

        function initializeBaseColumnWidths() {
            getColumns().forEach(header => getBaseColumnWidth(header));
        }

        function getRequiredTableWidth() {
            return getSelectionColumnWidth() + getVisibleHeaders().reduce((sum, header) => sum + getRenderedColumnWidth(header), 0);
        }

        function getGridViewportWidth() {
            return tableScroll?.clientWidth || 0;
        }

        function setTableWidth(width) {
            const safeWidth = Math.max(1, Number(width) || 1);
            const cssWidth = `${safeWidth.toFixed(2)}px`;
            table.style.width = cssWidth;
            table.style.minWidth = cssWidth;
        }

        function getLastColumnEdgeGap(lastHeader) {
            if (!tableScroll || !lastHeader) return 0;
            const viewportRect = tableScroll.getBoundingClientRect();
            const headerRect = lastHeader.getBoundingClientRect();
            const viewportRight = viewportRect.left + tableScroll.clientWidth;
            return viewportRight - headerRect.right;
        }

        function fitInitialColumnsToGrid() {
            if (!tableScroll) return;
            const visibleHeaders = getVisibleHeaders();
            if (!visibleHeaders.length) return;
            const viewportWidth = getGridViewportWidth();
            if (viewportWidth <= 0) return;

            const availableDataWidth = Math.max(visibleHeaders.length, viewportWidth - getSelectionColumnWidth());
            const configuredWidths = visibleHeaders.map(header => getConfiguredDefaultWidth(header));
            const hasConfiguredWidths = configuredWidths.some(width => width !== null);

            // Preserve the original equal-width initialization when no column defines a default width.
            if (!hasConfiguredWidths) {
                const evenWidth = availableDataWidth / visibleHeaders.length;
                let assignedWidth = 0;
                visibleHeaders.forEach((header, index) => {
                    const isLast = index === visibleHeaders.length - 1;
                    const remainingWidth = Math.max(1, availableDataWidth - assignedWidth);
                    const width = isLast ? remainingWidth : evenWidth;
                    setBaseColumnWidth(header.dataset.column, width, 1);
                    assignedWidth += width;
                });
                return;
            }

            // Honor code-backed default widths while leaving columns without one on the same
            // equal-width calculation they used before column defaults were introduced.
            const legacyEvenWidth = availableDataWidth / visibleHeaders.length;
            visibleHeaders.forEach((header, index) => {
                const width = configuredWidths[index] ?? legacyEvenWidth;
                setBaseColumnWidth(header.dataset.column, width, 1);
            });
        }

        function synchronizeTableWidth(fillUnderflow = fitColumnsToGrid) {
            if (!tableScroll) return;
            const visibleHeaders = getVisibleHeaders();
            if (!visibleHeaders.length) return;

            initializeBaseColumnWidths();
            visibleHeaders.forEach(header => setRenderedColumnWidth(header.dataset.column, getBaseColumnWidth(header)));

            const viewportWidth = getGridViewportWidth() || Math.ceil(getRequiredTableWidth());
            let requestedWidth = getRequiredTableWidth();
            const lastHeader = visibleHeaders.at(-1);
            setTableWidth(requestedWidth);

            if (fillUnderflow && requestedWidth < viewportWidth - 0.5) {
                let lastRenderedWidth = getRenderedColumnWidth(lastHeader);
                const initialGap = viewportWidth - requestedWidth;
                lastRenderedWidth += initialGap;
                setRenderedColumnWidth(lastHeader.dataset.column, lastRenderedWidth);
                requestedWidth += initialGap;
                setTableWidth(requestedWidth);

                for (let pass = 0; pass < 2; pass += 1) {
                    const edgeGap = getLastColumnEdgeGap(lastHeader);
                    if (Math.abs(edgeGap) <= 0.5) break;
                    lastRenderedWidth = Math.max(1, lastRenderedWidth + edgeGap);
                    setRenderedColumnWidth(lastHeader.dataset.column, lastRenderedWidth);
                    requestedWidth = Math.max(1, requestedWidth + edgeGap);
                    setTableWidth(requestedWidth);
                }
            }

            const actualTableWidth = table.getBoundingClientRect().width;
            const hasRows = typeof getRows === 'function' && getRows().length > 0;
            tableScroll.classList.toggle('has-horizontal-overflow', hasRows && actualTableWidth > viewportWidth + 0.75);
        }

        // Captures focus within the table before a real column reorder moves focused DOM nodes.
        // Normal refreshes avoid moving already-ordered header/filter cells entirely.
        function captureTableFocus() {
            const active = document.activeElement;
            if (!active || !table.contains(active)) return null;

            const state = { element: active, selectionStart: null, selectionEnd: null, selectionDirection: null };
            if (typeof active.selectionStart === 'number' && typeof active.selectionEnd === 'number') {
                state.selectionStart = active.selectionStart;
                state.selectionEnd = active.selectionEnd;
                state.selectionDirection = active.selectionDirection;
            }
            return state;
        }

        // Restores the same focused table control after a required DOM move without scrolling the page.
        function restoreTableFocus(state) {
            const element = state?.element;
            if (!element?.isConnected) return;

            try {
                element.focus({ preventScroll: true });
            } catch {
                element.focus();
            }

            if (state.selectionStart == null || typeof element.setSelectionRange !== 'function') return;
            try {
                element.setSelectionRange(state.selectionStart, state.selectionEnd, state.selectionDirection || undefined);
            } catch {
                // Some focusable controls expose selectionStart but do not support setSelectionRange.
            }
        }

        function getDirectColumnOrder(container, selector) {
            if (!container) return [];
            return Array.from(container.children)
                .filter(child => child.matches?.(selector))
                .map(child => child.dataset.column);
        }

        function matchesColumnOrder(container, selector) {
            const currentOrder = getDirectColumnOrder(container, selector);
            return currentOrder.length === columnOrder.length
                && currentOrder.every((key, index) => key === columnOrder[index]);
        }

        function reorderColumns(container, selector) {
            if (!container || matchesColumnOrder(container, selector)) return false;
            columnOrder.forEach(key => {
                const cell = container.querySelector(`${selector}[data-column="${cssEscape(key)}"]`);
                if (cell) container.appendChild(cell);
            });
            return true;
        }

        function applyColumnOrder() {
            const headerRow = table.querySelector('.column-header-row');
            const filterRow = table.querySelector('.column-filter-row');
            const rows = getRows();
            const headerNeedsReorder = !matchesColumnOrder(headerRow, 'th[data-column]');
            const filterNeedsReorder = !matchesColumnOrder(filterRow, 'th[data-column]');
            const rowsNeedingReorder = rows.filter(row => !matchesColumnOrder(row, 'td[data-column]'));

            // This is the common live-refresh path: new body rows are already built in the configured
            // column order and the existing header/filter rows have not changed. Avoid touching the
            // focused filter DOM in that case so browser focus remains stable.
            if (!headerNeedsReorder && !filterNeedsReorder && rowsNeedingReorder.length === 0) return;

            const focusState = captureTableFocus();
            if (headerNeedsReorder) reorderColumns(headerRow, 'th[data-column]');
            if (filterNeedsReorder) reorderColumns(filterRow, 'th[data-column]');
            rowsNeedingReorder.forEach(row => reorderColumns(row, 'td[data-column]'));
            restoreTableFocus(focusState);
        }

        function applyColumnVisibility() {
            table.querySelectorAll('[data-column]').forEach(cell => {
                cell.classList.toggle('column-hidden', !visibleColumns.includes(cell.dataset.column));
            });
        }

        function populateColumnChoices(container) {
            if (!container) return;
            container.innerHTML = '';
            availableColumns.forEach(key => {
                const header = table.querySelector(`.column-header-row th[data-column="${cssEscape(key)}"]`);
                if (!header) return;
                const label = document.createElement('label');
                label.className = 'column-choice';
                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.value = key;
                checkbox.checked = visibleColumns.includes(key);
                const text = document.createElement('span');
                text.textContent = header.querySelector('.sort-button span')?.textContent || key;
                label.append(checkbox, text);
                container.appendChild(label);
            });
        }

        function setVisibleColumns(keys) {
            visibleColumns = Array.isArray(keys) ? [...keys] : [];
        }

        function restoreDefaults() {
            visibleColumns = [...defaultColumns];
            columnOrder = [...originalColumnOrder];
            baseColumnWidths.clear();
            table.querySelectorAll('[data-column]').forEach(cell => cell.style.width = '');
            resetPreferences();
            applyColumnOrder();
            applyColumnVisibility();
            fitInitialColumnsToGrid();
            synchronizeTableWidth(false);
        }

        function wireColumnReordering() {
            let dragged = null;
            getColumns().forEach(header => {
                header.addEventListener('dragstart', event => {
                    if (event.target.closest('[data-resize-handle]')) {
                        event.preventDefault();
                        return;
                    }
                    dragged = header.dataset.column;
                    header.classList.add('dragging');
                });
                header.addEventListener('dragend', () => {
                    header.classList.remove('dragging');
                    dragged = null;
                });
                header.addEventListener('dragover', event => event.preventDefault());
                header.addEventListener('drop', event => {
                    event.preventDefault();
                    const target = header.dataset.column;
                    if (!dragged || dragged === target) return;
                    const from = columnOrder.indexOf(dragged);
                    const to = columnOrder.indexOf(target);
                    if (from < 0 || to < 0) return;
                    columnOrder.splice(from, 1);
                    columnOrder.splice(to, 0, dragged);
                    applyColumnOrder();
                    synchronizeTableWidth();
                    savePreferences();
                });
            });
        }

        function measureTextWidth(text, referenceElement) {
            const canvas = measureTextWidth.canvas || (measureTextWidth.canvas = document.createElement('canvas'));
            const context = canvas.getContext('2d');
            const style = window.getComputedStyle(referenceElement);
            context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
            return context.measureText(String(text || '')).width;
        }

        function calculateAutoFitWidth(header) {
            const key = header.dataset.column;
            const label = header.querySelector('.sort-button span')?.textContent?.trim() || key;
            const bodyCells = getRows().map(row => row.querySelector(`td[data-column="${cssEscape(key)}"]`)).filter(Boolean);
            const referenceCell = bodyCells[0] || header;
            let widest = measureTextWidth(label, header) + 46;
            bodyCells.forEach(cell => {
                const text = cell.textContent?.trim() || '';
                let extra = 30;
                if (cell.querySelector('.status-pill')) extra += 18;
                widest = Math.max(widest, measureTextWidth(text, referenceCell) + extra);
            });
            const viewportWidth = getGridViewportWidth() || 800;
            const sensibleMaximum = Math.max(240, Math.min(640, viewportWidth * 0.7));
            return Math.min(sensibleMaximum, Math.max(96, Math.ceil(widest)));
        }

        function autoFitColumn(header) {
            if (!header || header.classList.contains('column-hidden')) return;
            setBaseColumnWidth(header.dataset.column, calculateAutoFitWidth(header), 96);
            synchronizeTableWidth();
            savePreferences();
        }

        function wireColumnResizing() {
            table.querySelectorAll('[data-resize-handle]').forEach(handle => {
                handle.addEventListener('dblclick', event => {
                    event.preventDefault();
                    event.stopPropagation();
                    autoFitColumn(handle.closest('th'));
                });

                handle.addEventListener('pointerdown', event => {
                    event.preventDefault();
                    event.stopPropagation();
                    const header = handle.closest('th');
                    const key = header.dataset.column;
                    synchronizeTableWidth();
                    const startX = event.clientX;
                    const startRenderedWidth = header.getBoundingClientRect().width;
                    const startBaseWidth = getBaseColumnWidth(header);
                    const minimumResizeWidth = Math.min(96, startBaseWidth);
                    const isElasticLastColumn = fitColumnsToGrid && header === getVisibleHeaders().at(-1);
                    handle.classList.add('is-resizing');
                    document.body.classList.add('grid-column-resizing');
                    handle.setPointerCapture(event.pointerId);

                    function move(moveEvent) {
                        const delta = moveEvent.clientX - startX;
                        let width = startBaseWidth + delta;
                        if (isElasticLastColumn && delta > 0 && startRenderedWidth > startBaseWidth) width = startRenderedWidth + delta;
                        setBaseColumnWidth(key, width, minimumResizeWidth);
                        synchronizeTableWidth();
                    }

                    function end(endEvent) {
                        handle.removeEventListener('pointermove', move);
                        handle.removeEventListener('pointerup', end);
                        handle.removeEventListener('pointercancel', end);
                        if (handle.hasPointerCapture?.(endEvent.pointerId)) handle.releasePointerCapture(endEvent.pointerId);
                        handle.classList.remove('is-resizing');
                        document.body.classList.remove('grid-column-resizing');
                        synchronizeTableWidth();
                        savePreferences();
                    }

                    handle.addEventListener('pointermove', move);
                    handle.addEventListener('pointerup', end);
                    handle.addEventListener('pointercancel', end);
                });
            });
        }

        function captureSavedViewColumns() {
            return columnOrder.map((key, index) => {
                const header = table.querySelector(`.column-header-row th[data-column="${cssEscape(key)}"]`);
                const width = header ? getBaseColumnWidth(header) : null;
                return {
                    key,
                    visible: visibleColumns.includes(key),
                    order: index,
                    width: Number.isFinite(width) ? Math.round(width * 100) / 100 : null
                };
            });
        }

        function applySavedViewColumns(columns) {
            let ignoredSettings = 0;
            const knownColumns = new Set(originalColumnOrder);
            const savedColumns = columns
                .filter(column => {
                    const key = String(column?.key || '').trim();
                    if (!knownColumns.has(key)) { ignoredSettings += 1; return false; }
                    return true;
                })
                .sort((left, right) => Number(left.order) - Number(right.order));

            const savedOrder = savedColumns.map(column => String(column.key));
            const newColumns = originalColumnOrder.filter(key => !savedOrder.includes(key));
            columnOrder = [...savedOrder, ...newColumns];

            const savedVisibility = new Map(savedColumns.map(column => [String(column.key), Boolean(column.visible)]));
            visibleColumns = originalColumnOrder.filter(key => savedVisibility.has(key) ? savedVisibility.get(key) : defaultColumns.includes(key));
            if (!visibleColumns.length) visibleColumns = [...defaultColumns].filter(key => knownColumns.has(key));
            if (!visibleColumns.length && originalColumnOrder.length) visibleColumns = [originalColumnOrder[0]];

            baseColumnWidths.clear();
            savedColumns.forEach(column => {
                const width = Number(column.width);
                if (Number.isFinite(width) && width >= 40 && width <= 2000) baseColumnWidths.set(String(column.key), width);
            });

            applyColumnOrder();
            applyColumnVisibility();
            synchronizeTableWidth();
            return ignoredSettings;
        }

        // Applies the visible column set/order captured by a Report. Width is optional so Reports
        // created before width persistence was introduced continue to load using the current grid widths.
        function applyReportColumns(columns) {
            let ignoredSettings = 0;
            const knownColumns = new Set(originalColumnOrder);
            const ordered = (Array.isArray(columns) ? columns : [])
                .filter(column => {
                    const key = String(column?.key || '').trim();
                    if (!knownColumns.has(key)) { ignoredSettings += 1; return false; }
                    return true;
                })
                .sort((left, right) => Number(left.order) - Number(right.order));

            const reportOrder = ordered.map(column => String(column.key));
            columnOrder = [...reportOrder, ...originalColumnOrder.filter(key => !reportOrder.includes(key))];
            visibleColumns = [...reportOrder];
            if (!visibleColumns.length) {
                visibleColumns = [...defaultColumns].filter(key => knownColumns.has(key));
                if (!visibleColumns.length && originalColumnOrder.length) visibleColumns = [originalColumnOrder[0]];
            }

            ordered.forEach(column => {
                const width = Number(column.width);
                if (Number.isFinite(width) && width >= 40 && width <= 2000)
                    baseColumnWidths.set(String(column.key), width);
            });

            applyColumnOrder();
            applyColumnVisibility();
            synchronizeTableWidth();
            return ignoredSettings;
        }

        return {
            loadPreferences,
            savePreferences,
            applyColumnOrder,
            applyColumnVisibility,
            initializeBaseColumnWidths,
            fitInitialColumnsToGrid,
            synchronizeTableWidth,
            populateColumnChoices,
            setVisibleColumns,
            restoreDefaults,
            wireColumnReordering,
            wireColumnResizing,
            captureSavedViewColumns,
            applySavedViewColumns,
            applyReportColumns,
            getColumnOrder: () => [...columnOrder],
            getVisibleColumns: () => [...visibleColumns],
            isColumnVisible: key => visibleColumns.includes(key),
            getColumnWidth: key => {
                const header = table.querySelector(`.column-header-row th[data-column="${cssEscape(key)}"]`);
                if (!header) return null;
                const width = getBaseColumnWidth(header);
                return Number.isFinite(width) && width > 0 ? Math.round(width * 100) / 100 : null;
            }
        };
    }

    window.MauiGridLayout = { createManager };
})();
