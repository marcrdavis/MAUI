/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * This JavaScript is part of the MAUI application.
 */

(() => {
    const savedViewManager = window.MauiGridSavedViews.createManager();
    const navigationStateManagers = new Map();

    // Exposes a narrow transient-state seam for workflows that temporarily navigate away from a grid
    // and then return (for example Tasks -> Task Runs -> Back to Tasks). This is intentionally separate
    // from Saved Views and user preferences: callers decide when to persist the returned object.
    window.MauiGridNavigationState = {
        capture(gridId) {
            return navigationStateManagers.get(String(gridId || '').trim())?.capture() || null;
        },
        restore(gridId, state) {
            return Boolean(navigationStateManagers.get(String(gridId || '').trim())?.restore(state));
        },
        // Captures one reusable grid's transient search/filter/sort state and returns the current page URL
        // with a one-time session-storage token. The URL remains usable without storage if capture fails.
        createReturnUrl(gridId, sourceHref = window.location.href) {
            const normalizedGridId = String(gridId || '').trim();
            const currentUrl = new URL(sourceHref, window.location.href);
            currentUrl.searchParams.delete('gridReturnState');
            if (!normalizedGridId) return `${currentUrl.pathname}${currentUrl.search}${currentUrl.hash}`;

            try {
                const state = navigationStateManagers.get(normalizedGridId)?.capture();
                if (!state) return `${currentUrl.pathname}${currentUrl.search}${currentUrl.hash}`;
                const key = `maui-grid-return-${normalizedGridId}-${Date.now()}-${Math.random().toString(36).slice(2)}`;
                window.sessionStorage.setItem(key, JSON.stringify({ schemaVersion: 1, gridId: normalizedGridId, state }));
                currentUrl.searchParams.set('gridReturnState', key);
            } catch (error) {
                console.warn(`Unable to preserve ${normalizedGridId} grid return state.`);
            }

            return `${currentUrl.pathname}${currentUrl.search}${currentUrl.hash}`;
        }
    };

    document.querySelectorAll('[data-grid]').forEach(initializeGrid);

    // Initializes one reusable grid and wires filtering, sorting, paging, selection, layout, settings behavior, and saved views.
    function initializeGrid(grid) {
        const table = grid.querySelector('[data-grid-table]');
        if (!table) return;

        const tableScroll = table.closest('.table-scroll');
        const body = grid.querySelector('[data-grid-body]');
        const loadShell = grid.closest('[data-grid-load-shell]');
        const initialDataLoadDeferred = String(loadShell?.dataset.gridInitialLoadDeferred || 'false').toLowerCase() === 'true';
        const minimumFixedGridHeight = 280;

        // Chooses between the normal CSS-owned fixed viewport and the short-window page-scroll fallback.
        // CSS owns the normal grid height from the first paint; JavaScript no longer writes an inline height,
        // which prevents the Loading shell from visibly growing after grid initialization starts.
        // Purpose: Preserve the usable-height fallback without making JavaScript responsible for normal sizing.
        function sizeGridToViewport() {
            const footer = document.querySelector('.app-footer');
            const gridTop = grid.getBoundingClientRect().top;
            const footerHeight = footer?.getBoundingClientRect().height || 0;
            const rootStyles = getComputedStyle(document.documentElement);
            const configuredGap = parseFloat(rootStyles.getPropertyValue('--grid-footer-gap'));
            const footerGap = Number.isFinite(configuredGap) ? configuredGap : 14;
            const availableHeight = Math.floor(window.innerHeight - gridTop - footerHeight - footerGap);
            const useFixedGrid = availableHeight >= minimumFixedGridHeight;

            document.documentElement.classList.toggle('grid-fixed-page-root', useFixedGrid);
            document.body.classList.toggle('grid-fixed-page', useFixedGrid);
            document.body.classList.toggle('grid-page-scroll-fallback', !useFixedGrid);

            // Accounts for viewport-width changes when the fallback enables or disables the page scrollbar.
            requestAnimationFrame(() => layout.synchronizeTableWidth());
        }

        // Returns the internal grid row viewport to the first row after an explicit paging/filter/sort action.
        // Live data refreshes intentionally do not call this so the user's current scroll position is preserved.
        // Purpose: Return the row viewport to the first row after explicit grid navigation.
        function scrollGridToTop() {
            if (tableScroll) tableScroll.scrollTop = 0;
        }

        // Gives the primary grid search keyboard focus on the initial page load without scrolling the page.
        // This is intentionally called only during initial grid setup; live row/report refreshes must never
        // take focus away from whatever control the user is currently using.
        // Purpose: Make the grid search box immediately ready for typing when a grid page opens.
        function focusInitialSearch() {
            if (!search || search.disabled || search.offsetParent === null) return;

            // A saved view can require an asynchronous load. If the user moved focus while that was
            // happening, respect that interaction instead of stealing focus back to the search box.
            const activeElement = document.activeElement;
            if (activeElement && activeElement !== document.body && activeElement !== document.documentElement && activeElement !== search) return;

            try {
                search.focus({ preventScroll: true });
            } catch {
                search.focus();
            }
        }

        let rows = Array.from(body.querySelectorAll('tr'));
        const search = grid.querySelector('[data-grid-search]');
        const searchClear = grid.querySelector('[data-search-clear]');
        const filters = Array.from(grid.querySelectorAll('[data-column-filter]'));
        const filterClearButtons = Array.from(grid.querySelectorAll('[data-column-filter-clear]'));
        const filterHelpButton = grid.querySelector('[data-filter-help-button]');
        const filterHelpDialog = grid.querySelector('[data-filter-help-dialog]');
        const filterHelpMacros = filterHelpDialog?.querySelector('[data-filter-help-macros]');
        const dateMacroButtons = Array.from(grid.querySelectorAll('[data-date-macro-button]'));
        const dateMacroDialog = grid.querySelector('[data-date-macro-dialog]');
        const dateMacroTarget = dateMacroDialog?.querySelector('[data-date-macro-target]');
        const dateMacroOptions = Array.from(dateMacroDialog?.querySelectorAll('[data-date-macro-option]') || []);

        // Populates Filter Help from the existing Date Filter Macro choices so both help surfaces
        // use one authoritative macro list. Purpose: Keep macro documentation synchronized.
        function populateFilterHelpMacros() {
            if (!filterHelpMacros || filterHelpMacros.childElementCount > 0) return;

            dateMacroOptions.forEach(option => {
                const codeText = option.querySelector('code')?.textContent?.trim() || String(option.dataset.dateMacroOption || '').trim();
                const description = option.querySelector('span')?.textContent?.trim() || '';
                if (!codeText) return;

                const item = document.createElement('div');
                item.className = 'filter-help-macro-item';
                const code = document.createElement('code');
                code.textContent = codeText;
                const text = document.createElement('span');
                text.textContent = description;
                item.append(code, text);
                filterHelpMacros.appendChild(item);
            });
        }

        // Opens the shared grid Filter Help dialog.
        function openFilterHelp() {
            if (!filterHelpDialog) return;
            populateFilterHelpMacros();
            if (!filterHelpDialog.open) filterHelpDialog.showModal();
        }

        const scopeFilters = Array.from(grid.querySelectorAll('[data-grid-scope-filter]'));
        const totalCount = grid.querySelector('[data-total-count]');
        const filteredCount = grid.querySelector('[data-filtered-count]');
        const pagerSummary = grid.querySelector('[data-pager-summary]');
        const pageIndicator = grid.querySelector('[data-page-indicator]');
        const pageSizeSelect = grid.querySelector('[data-page-size]');
        const previousButton = grid.querySelector('[data-page-prev]');
        const nextButton = grid.querySelector('[data-page-next]');
        const emptyState = grid.querySelector('[data-empty-state]');
        const pageSelect = grid.querySelector('[data-page-select]');
        const bulkButton = grid.querySelector('[data-bulk-button]');
        const selectionCount = grid.querySelector('[data-selection-count]');
        const settingsDialog = grid.querySelector('[data-settings-dialog]');
        const settingsPageSize = grid.querySelector('[data-settings-page-size]');
        const columnChoices = grid.querySelector('[data-column-choices]');
        const defaultColumns = parseJsonArray(grid.dataset.defaultColumns);
        const availableColumns = parseJsonArray(grid.dataset.availableColumns);
        const searchColumns = parseJsonArray(grid.dataset.searchColumns);
        // De-duplicates configured values so the Rows selector can never render repeated sizes.
        const pageSizes = [...new Set(parseJsonArray(grid.dataset.pageSizes).map(Number).filter(Number.isFinite))];
        const fitColumnsToGrid = String(grid.dataset.fitColumnsToGrid).toLowerCase() === 'true';
        const highlightFailedTasksInGrid = String(grid.dataset.highlightFailedTasksInGrid).toLowerCase() === 'true';
        const selectRowsBeyondGridSize = String(grid.dataset.selectRowsBeyondGridSize).toLowerCase() === 'true';
        const rowSelectionEnabled = String(grid.dataset.rowSelectionEnabled ?? 'true').toLowerCase() === 'true';
        const originalColumnOrder = getColumns(table).map(header => header.dataset.column);
        const rowRenderer = window.MauiGridRows.createRenderer({ grid, table, originalColumnOrder, rowSelectionEnabled });
        const layout = window.MauiGridLayout.createManager({
            grid,
            table,
            tableScroll,
            defaultColumns,
            availableColumns,
            originalColumnOrder,
            fitColumnsToGrid,
            getRows: () => rows
        });

        let gridInitializationReady = false;
        let initialDataReady = !initialDataLoadDeferred;
        let initialLoadingComplete = false;

        // Keeps the shared Loading overlay in place until both reusable-grid initialization and any
        // page-deferred first dataset are complete, then allows two paint cycles for final sizing to settle.
        function completeInitialLoadingWhenReady() {
            if (initialLoadingComplete || !gridInitializationReady || !initialDataReady) return;
            initialLoadingComplete = true;

            window.requestAnimationFrame(() => {
                sizeGridToViewport();
                layout.synchronizeTableWidth();

                window.requestAnimationFrame(() => {
                    layout.synchronizeTableWidth();
                    loadShell?.classList.remove('is-loading');
                    grid.setAttribute('aria-busy', 'false');
                    focusInitialSearch();
                    grid.dispatchEvent(new CustomEvent('maui:grid-ready'));
                });
            });
        }

        let originalRowIndex = new Map(rows.map((row, index) => [row, index]));
        const textCollator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base', ignorePunctuation: true });
        // Relative-date macro calculations live in one shared helper so the core grid lifecycle
        // remains focused on filtering/rendering rather than calendar arithmetic.
        const dateMacros = window.MauiGridDateMacros;
        if (!dateMacros) throw new Error('MauiGridDateMacros is required before grid.js.');
        const dateMacroClock = dateMacros.createClock(grid.dataset.dateMacroNow, grid.dataset.dateFilterZone, grid.dataset.dateFilterLocale);

        const defaultPageSize = normalizePageSize(grid.dataset.defaultPageSize);
        const initialPageSize = normalizePageSize(document.documentElement.dataset.userGridPageSize || defaultPageSize);
        let persistedGlobalPageSize = initialPageSize;
        const viewState = window.MauiGridViewState.createManager({
            grid,
            pageSelect,
            bulkButton,
            selectionCount,
            pagerSummary,
            pageIndicator,
            previousButton,
            nextButton,
            emptyState,
            filteredCount,
            totalCount,
            selectRowsBeyondGridSize,
            initialPageSize,
            defaultPageSize,
            getRows: () => rows,
            isRowSelectable,
            canUseBulkActions: ids => window.MauiObjectAccess?.canBulk(grid, ids) ?? true
        });
        const configuredDefaultSortColumn = String(grid.dataset.defaultSortColumn || '').trim();
        const configuredDefaultSortDirection = String(grid.dataset.defaultSortDirection || '').trim().toLowerCase();
        let sortColumn = originalColumnOrder.includes(configuredDefaultSortColumn) ? configuredDefaultSortColumn : '';
        let sortDirection = sortColumn && (configuredDefaultSortDirection === 'asc' || configuredDefaultSortDirection === 'desc')
            ? configuredDefaultSortDirection
            : '';
        if (!sortDirection) sortColumn = '';
        let activeSavedView = null;
        let activeReportEdit = null;
        let reportEditSavedFingerprint = null;
        const reportEditId = new URLSearchParams(window.location.search).get('reportEdit')?.trim() || '';
        const reportEditIndicator = grid.querySelector('[data-grid-report-edit-context]');
        const reportEditBack = document.querySelector('[data-grid-report-edit-back]');
        const reportEditCancelDialog = grid.querySelector('[data-grid-report-edit-cancel-dialog]');
        const reportEditCancelName = grid.querySelector('[data-grid-report-edit-cancel-name]');
        const reportEditCancelConfirm = grid.querySelector('[data-grid-report-edit-cancel-confirm]');
        const reportEditCancelDismissButtons = [...grid.querySelectorAll('[data-grid-report-edit-cancel-dismiss]')];
        const reportEditDelete = grid.querySelector('[data-grid-delete-report]');
        const reportEditDeleteDialog = grid.querySelector('[data-delete-reports-dialog]');
        const reportEditDeleteConfirm = reportEditDeleteDialog?.querySelector('[data-delete-reports-confirm]');
        const reportEditDeleteCancelButtons = [...(reportEditDeleteDialog?.querySelectorAll('[data-delete-reports-cancel]') || [])];
        const reportEditDeleteError = reportEditDeleteDialog?.querySelector('[data-delete-reports-error]');
        const reportEditDeleteMessage = reportEditDeleteDialog?.querySelector('[data-delete-reports-message]');
        const reportEditDeleteList = reportEditDeleteDialog?.querySelector('[data-delete-reports-list]');

        // Restricts a row-size value to the configured PossibleValues list, falling back to the application default.
        function normalizePageSize(value) {
            const numericValue = Number(value);
            if (pageSizes.includes(numericValue)) return numericValue;
            const configuredDefault = Number(grid.dataset.defaultPageSize);
            if (pageSizes.includes(configuredDefault)) return configuredDefault;
            return pageSizes[0] || 10;
        }

        // Persists the shared row-size preference in MAUI's server-side User Preferences table so
        // it follows the signed-in user across browsers and devices.
        function saveGlobalPageSize() {
            const pageSize = viewState.getPageSize();
            document.documentElement.dataset.userGridPageSize = String(pageSize);

            // Grid Settings frequently changes only columns. Avoid a redundant metadata write when
            // the shared page-size preference did not actually change. This also prevents a column
            // layout save and an unnecessary page-size save from competing for SQLite at the same time.
            if (pageSize === persistedGlobalPageSize) return Promise.resolve(pageSize);
            if (typeof window.mauiSaveGridPageSizePreference !== 'function') {
                persistedGlobalPageSize = pageSize;
                return Promise.resolve(pageSize);
            }

            return window.mauiSaveGridPageSizePreference(pageSize).then(saved => {
                const normalized = normalizePageSize(saved);
                persistedGlobalPageSize = normalized;
                if (normalized === viewState.getPageSize()) return normalized;
                viewState.setPageSize(normalized);
                if (pageSizeSelect) pageSizeSelect.value = String(normalized);
                if (settingsPageSize) settingsPageSize.value = String(normalized);
                render();
                return normalized;
            }).catch(error => {
                console.warn('Unable to persist MAUI grid page-size preference.');
                if (typeof window.mauiShowToast === 'function') window.mauiShowToast('The Grid Page Size preference could not be saved.', 'error');
                return pageSize;
            });
        }

        // Safely parse a JSON array embedded in grid data attributes.
        function parseJsonArray(value) {
            try { return JSON.parse(value || '[]'); } catch { return []; }
        }

        // Returns actual data-column header cells and excludes the row-selection column.
        function getColumns(sourceTable) {
            return Array.from(sourceTable.querySelectorAll('.column-header-row th[data-column]'));
        }

        // Escapes column identifiers before they are used inside query-selector attribute expressions.
        function cssEscape(value) {
            return window.CSS?.escape ? window.CSS.escape(value) : String(value).replace(/"/g, '\\"');
        }

        // Highlights failed Task rows only when the deployment setting is enabled and Status is visible.
        // This deliberately follows the user's current column visibility so hiding Status removes the
        // contextual row highlight and showing Status restores it without requiring a page reload.
        function applyTaskFailureHighlighting() {
            const shouldEvaluate = grid.dataset.grid === 'tasks' && highlightFailedTasksInGrid && layout.isColumnVisible('Status');
            rows.forEach(row => {
                const statusCell = row.querySelector('td[data-column="Status"]');
                const status = String(statusCell?.dataset.value ?? statusCell?.textContent ?? '').trim();
                row.classList.toggle('row-failed-task', shouldEvaluate && status.toLowerCase() === 'failed');
            });
        }

        // Populates both page-size controls from the configuration-provided list.
        function populatePageSizes() {
            const controls = [...new Set([pageSizeSelect, settingsPageSize].filter(Boolean))];
            controls.forEach(select => {
                const fragment = document.createDocumentFragment();
                pageSizes.forEach(size => {
                    const option = document.createElement('option');
                    option.value = String(size);
                    option.textContent = String(size);
                    option.selected = size === viewState.getPageSize();
                    fragment.appendChild(option);
                });
                select.replaceChildren(fragment);
            });
        }

        const filenameSearch = window.MauiTaskFilenameSearch?.create({ grid, search,
            changed: () => render()
        });
        grid.addEventListener('maui:grid-ready', () => filenameSearch?.ensure());

        // Returns the searchable text for one row using the grid-specific configured search columns.
        function getRowSearchText(row) {
            const keys = searchColumns.length ? searchColumns : layout.getColumnOrder();
            const text = keys.map(key => {
                const cell = row.querySelector(`td[data-column="${cssEscape(key)}"]`);
                const header = table.querySelector(`.column-header-row th[data-column="${cssEscape(key)}"]`);
                return String(header?.dataset.type || '').toLowerCase() === 'tags'
                    ? (cell?.dataset.value || '')
                    : (cell?.textContent || '');
            }).join(' ').toLowerCase();
            return text + '\n' + (filenameSearch?.text(row.dataset.rowId) || '');
        }

        function resolveDateMacro(rawValue) {
            return dateMacros.parseFilter(rawValue, dateMacroClock);
        }

        function matchesDateMacro(cell, filter) {
            return dateMacros.matches(cell, filter, dateMacroClock);
        }

        function updateDateMacroHint(input) {
            dateMacros.updateHint(input, table, dateMacroClock);
        }

        let activeDateMacroFilter = null;

        // Opens the shared date-macro picker for one datetime column. Column type detection is
        // declarative in _DataGrid, so text/number/status filters never receive this control.
        function openDateMacroPicker(button) {
            if (!dateMacroDialog) return;
            const key = String(button?.dataset.dateMacroColumn || '').trim();
            const input = filters.find(item => String(item.dataset.columnFilter || '') === key);
            if (!input) return;

            activeDateMacroFilter = input;
            if (dateMacroTarget) {
                const label = String(button.dataset.dateMacroLabel || '').trim();
                dateMacroTarget.textContent = label || 'this Date/Time column';
            }
            dateMacroDialog.showModal();
        }

        // Inserts a selected macro through the normal filter-input event path so filtering, paging,
        // tooltips, Saved Views, and Report state all observe exactly the same value as typed input.
        function applyDateMacroOption(button) {
            const input = activeDateMacroFilter;
            const macro = String(button?.dataset.dateMacroOption || '').trim();
            if (!input || !macro) return;

            dateMacroDialog?.close();
            input.value = macro;
            input.dispatchEvent(new Event('input', { bubbles: true }));
            try { input.focus({ preventScroll: true }); } catch { input.focus(); }
        }

        // Splits one filter expression into OR alternatives. A comma is always an OR separator; the
        // word "or" is a separator only when it appears as a standalone word between two non-empty
        // values. Invalid/partial separator expressions fall back to the original literal filter.
        function splitFilterAlternatives(rawValue) {
            const value = String(rawValue || '').trim();
            if (!value) return [];

            const parts = value.split(/,|\s+or\s+/i).map(part => part.trim());
            return parts.length > 1 && parts.every(Boolean) ? parts : [value];
        }

        // Parses one user-entered text filter. Prefixing a non-empty term with "not:" or "!" negates
        // the normal case-insensitive contains match. A bare negation prefix is treated as an empty filter.
        function parseTextFilter(rawValue) {
            const value = String(rawValue || '').trim().toLowerCase();
            if (!value) return null;

            if (value.startsWith('not:')) {
                const term = value.slice(4).trim();
                return term ? { kind: 'text', term, negate: true } : null;
            }
            if (value.startsWith('!')) {
                const term = value.slice(1).trim();
                return term ? { kind: 'text', term, negate: true } : null;
            }

            return { kind: 'text', term: value, negate: false };
        }

        // Applies a column's normal single-value parser to every valid OR alternative. If any
        // alternative is not a valid filter value, preserve the prior behavior by treating the
        // entire expression as one literal filter instead of partially interpreting it.
        function parseFilterExpression(rawValue, parseSingle, split = splitFilterAlternatives) {
            const alternatives = split(rawValue);
            if (alternatives.length === 0) return null;
            if (alternatives.length === 1) return parseSingle(alternatives[0]);

            const filters = alternatives.map(parseSingle);
            return filters.every(Boolean)
                ? { kind: 'any', filters }
                : parseSingle(String(rawValue || '').trim());
        }

        // Tests a lower-cased text value against one parsed contains/not-contains filter.
        function matchesTextFilter(text, filter) {
            if (!filter) return true;
            if (filter.kind === 'any') return filter.filters.some(item => matchesTextFilter(text, item));
            const contains = String(text || '').toLowerCase().includes(filter.term);
            return filter.negate ? !contains : contains;
        }

        // Parses numeric comparison filters such as <1, <=1, >1, and >=1. Numeric columns retain
        // their existing text-filter behavior when the input is not a valid comparison expression.
        function parseNumericComparisonFilter(rawValue) {
            const value = String(rawValue || '').trim();
            const match = value.match(/^(<=|>=|<|>)\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)$/);
            if (!match) return null;

            const target = Number(match[2]);
            return Number.isFinite(target) ? { kind: 'number-comparison', operator: match[1], target } : null;
        }

        function matchesNumericComparison(cell, filter) {
            if (!filter) return true;
            const rawValue = String(cell?.dataset.value ?? cell?.textContent ?? '').trim().replace(/,/g, '');
            const value = Number(rawValue);
            if (!Number.isFinite(value)) return false;

            switch (filter.operator) {
                case '<': return value < filter.target;
                case '<=': return value <= filter.target;
                case '>': return value > filter.target;
                case '>=': return value >= filter.target;
                default: return false;
            }
        }

        function matchesColumnFilter(cell, filter, type = 'text') {
            if (!filter) return true;
            if (filter.kind === 'any') return filter.filters.some(item => matchesColumnFilter(cell, item, type));
            if (filter.kind === 'date-macro' || filter.kind === 'date-filter') return matchesDateMacro(cell, filter);
            if (filter.kind === 'number-comparison') return matchesNumericComparison(cell, filter);
            const textValue = type === 'tags' ? (cell?.dataset.value || '') : (cell?.textContent || '');
            return matchesTextFilter(textValue, filter);
        }

        // Returns rows matching scope filters first, then the fixed page search, then every active
        // per-column filter. Scope filters represent the broad view (for example Key/Certificate Type)
        // and intentionally run before all other user-entered filters.
        function getFilteredRows() {
            const searchFilter = parseFilterExpression(search?.value, parseTextFilter);
            const activeScopeFilters = scopeFilters
                .map(control => ({
                    key: String(control.dataset.filterColumn || '').trim(),
                    value: String(control.dataset.filterValue || '').trim().toLowerCase()
                }))
                .filter(item => item.key && item.value);
            const activeFilters = filters
                .map(input => {
                    const key = String(input.dataset.columnFilter || '').trim();
                    const header = key ? table.querySelector(`.column-header-row th[data-column="${cssEscape(key)}"]`) : null;
                    const type = String(header?.dataset.type || 'text').toLowerCase();
                    const parseSingle = type === 'datetime'
                        ? value => resolveDateMacro(value) || parseTextFilter(value)
                        : type === 'number'
                            ? value => parseNumericComparisonFilter(value) || parseTextFilter(value)
                            : parseTextFilter;
                    const filter = parseFilterExpression(input.value, parseSingle, type === 'datetime' ? dateMacros.splitAlternatives : splitFilterAlternatives);
                    updateDateMacroHint(input);
                    return { key, filter, type };
                })
                .filter(item => item.key && item.filter);

            return rows.filter(row => {
                const matchesScope = activeScopeFilters.every(filter => {
                    const cell = row.querySelector(`td[data-column="${cssEscape(filter.key)}"]`);
                    const cellValue = String(cell?.dataset.value ?? cell?.textContent ?? '').trim().toLowerCase();
                    return cellValue === filter.value;
                });
                if (!matchesScope) return false;

                // Empty global searches need no row-text DOM reads or filename-index lookups.
                if (searchFilter && !matchesTextFilter(getRowSearchText(row), searchFilter)) return false;

                return activeFilters.every(item => {
                    const cell = row.querySelector(`td[data-column="${cssEscape(item.key)}"]`);
                    return matchesColumnFilter(cell, item.filter, item.type);
                });
            });
        }

        // Sorts a row collection by the active column while respecting date and numeric raw values where provided.
        // Equal values fall back to the original dataset order so repeated renders are deterministic.
        // Purpose: Sort the filtered row collection while preserving deterministic ties.
        function sortRows(filteredRows) {
            if (!sortColumn || !sortDirection) return [...filteredRows];
            const header = table.querySelector(`.column-header-row th[data-column="${cssEscape(sortColumn)}"]`);
            const type = header?.dataset.type || 'text';
            const multiplier = sortDirection === 'asc' ? 1 : -1;

            return [...filteredRows].sort((left, right) => {
                const leftCell = left.querySelector(`td[data-column="${cssEscape(sortColumn)}"]`);
                const rightCell = right.querySelector(`td[data-column="${cssEscape(sortColumn)}"]`);
                const leftValue = leftCell?.dataset.value ?? leftCell?.textContent.trim() ?? '';
                const rightValue = rightCell?.dataset.value ?? rightCell?.textContent.trim() ?? '';
                let comparison = 0;

                if (type === 'number') {
                    const leftNumber = Number(leftValue);
                    const rightNumber = Number(rightValue);
                    comparison = (Number.isFinite(leftNumber) ? leftNumber : Number.NEGATIVE_INFINITY)
                        - (Number.isFinite(rightNumber) ? rightNumber : Number.NEGATIVE_INFINITY);
                } else if (type === 'datetime') {
                    const leftTime = Date.parse(leftValue);
                    const rightTime = Date.parse(rightValue);
                    comparison = (Number.isFinite(leftTime) ? leftTime : Number.NEGATIVE_INFINITY)
                        - (Number.isFinite(rightTime) ? rightTime : Number.NEGATIVE_INFINITY);
                } else {
                    comparison = textCollator.compare(String(leftValue), String(rightValue));
                }

                if (comparison !== 0) return comparison * multiplier;
                return (originalRowIndex.get(left) ?? 0) - (originalRowIndex.get(right) ?? 0);
            });
        }

        const exporter = window.MauiGridExport.createManager({
            grid,
            table,
            layout,
            getOrderedRows: () => sortRows(getFilteredRows()),
            cssEscape
        });
        const printer = window.MauiGridPrint.createManager({ grid, exporter });

        // Reorders the actual table rows to match the sorted and filtered collection so visible row order matches the active sort.
        // Purpose: Reorder table rows to match the sorted and filtered row collection.
        function applyRowOrder(orderedRows) {
            const orderedSet = new Set(orderedRows);
            const remainder = rows.filter(row => !orderedSet.has(row));
            [...orderedRows, ...remainder].forEach(row => body.appendChild(row));
        }

        // Returns whether a row is eligible for selection/bulk actions. Pages can disable a row's
        // checkbox (for example, built-in Scripts) and the shared grid will exclude it everywhere.
        function isRowSelectable(row) {
            const checkbox = row?.querySelector?.('[data-row-select]');
            return Boolean(checkbox) && !checkbox.disabled;
        }

        function refreshSelectionPermissions() {
            void window.MauiObjectAccess?.selection(grid, rows, () => {
                viewState.reconcileRows();
                render();
            });
        }

        // Recalculates visible rows, paging controls, counts, and selection state after any grid interaction.
        function render() {
            if (initialLoadingComplete) filenameSearch?.ensure();
            applyTaskFailureHighlighting();
            const filtered = sortRows(getFilteredRows());
            applyRowOrder(filtered);
            viewState.render(filtered);

            // Keep the table/header visible when filters return zero rows so users can see and correct
            // the active column filters. Only tbody rows are hidden through normal paging/filtering.
            table.hidden = false;
            updateSearchClear();
            updateFilterClearButtons();

            // Reconciles widths after the browser has applied row visibility/paging.
            window.requestAnimationFrame(() => layout.synchronizeTableWidth());
        }

        // Shows the search clear button only when the fixed grid search contains text.
        function updateSearchClear() {
            searchClear?.classList.toggle('visible', Boolean(search?.value));
        }

        // Keeps each per-column Clear Filter button enabled only while its filter contains a value.
        function updateFilterClearButtons() {
            filterClearButtons.forEach(button => {
                const key = String(button.dataset.columnFilterClear || '');
                const input = filters.find(item => String(item.dataset.columnFilter || '') === key);
                button.disabled = !String(input?.value || '');
            });
        }

        // Captures transient navigation state. Search text is intentionally included here even though
        // Saved Views omit it; this object is meant only for a short leave-and-return workflow.
        function captureNavigationState() {
            const columnFilters = {};
            filters.forEach(input => {
                const key = String(input.dataset.columnFilter || '').trim();
                if (key) columnFilters[key] = String(input.value || '');
            });
            const scopeState = {};
            scopeFilters.forEach(control => {
                const key = String(control.dataset.filterColumn || '').trim();
                if (key) scopeState[key] = String(control.dataset.filterValue || '');
            });
            return {
                schemaVersion: 1,
                search: String(search?.value || ''),
                filters: columnFilters,
                scopeFilters: scopeState,
                sortColumn,
                sortDirection
            };
        }

        // Restores a previously captured transient navigation state without changing saved preferences.
        function restoreNavigationState(state) {
            if (!state || Number(state.schemaVersion) !== 1) return false;
            if (search) search.value = String(state.search || '');
            filters.forEach(input => {
                const key = String(input.dataset.columnFilter || '').trim();
                input.value = key && state.filters && Object.prototype.hasOwnProperty.call(state.filters, key)
                    ? String(state.filters[key] ?? '')
                    : '';
                updateDateMacroHint(input);
            });
            scopeFilters.forEach(control => {
                const key = String(control.dataset.filterColumn || '').trim();
                if (!key) return;
                control.dataset.filterValue = state.scopeFilters && Object.prototype.hasOwnProperty.call(state.scopeFilters, key)
                    ? String(state.scopeFilters[key] ?? '')
                    : '';
                updateScopeFilterUi(control);
            });
            const requestedSortColumn = String(state.sortColumn || '').trim();
            const requestedSortDirection = String(state.sortDirection || '').trim().toLowerCase();
            if (originalColumnOrder.includes(requestedSortColumn) && (requestedSortDirection === 'asc' || requestedSortDirection === 'desc')) {
                sortColumn = requestedSortColumn;
                sortDirection = requestedSortDirection;
            } else {
                sortColumn = '';
                sortDirection = '';
            }
            viewState.resetPage();
            updateSortIndicators();
            render();
            scrollGridToTop();
            return true;
        }

        const navigationGridId = String(grid.dataset.grid || '').trim();
        navigationStateManagers.set(navigationGridId, {
            capture: captureNavigationState,
            restore: restoreNavigationState
        });

        // Restores a one-time navigation state only after the reusable grid has finished its normal
        // initial data/saved-view load. This keeps Back-to-grid navigation consistent across all grids.
        function wireNavigationReturnStateRestore() {
            const currentUrl = new URL(window.location.href);
            const key = String(currentUrl.searchParams.get('gridReturnState') || '').trim();
            if (!key || !navigationGridId) return;

            const restore = () => {
                let shouldConsume = false;
                try {
                    const raw = window.sessionStorage.getItem(key);
                    if (!raw) { shouldConsume = true; return; }
                    const payload = JSON.parse(raw);
                    if (String(payload?.gridId || '') !== navigationGridId) return;
                    shouldConsume = true;
                    if (Number(payload?.schemaVersion) !== 1 || !payload?.state) return;
                    restoreNavigationState(payload.state);
                } catch (error) {
                    shouldConsume = true;
                    console.warn(`Unable to restore ${navigationGridId} grid return state.`);
                } finally {
                    if (shouldConsume) {
                        try { window.sessionStorage.removeItem(key); } catch { }
                        currentUrl.searchParams.delete('gridReturnState');
                        window.history.replaceState(window.history.state, '', `${currentUrl.pathname}${currentUrl.search}${currentUrl.hash}`);
                    }
                }
            };

            grid.addEventListener('maui:grid-ready', restore, { once: true });
        }

        wireNavigationReturnStateRestore();

        // Updates one scope-filter control's label and selected option from its current data value.
        function updateScopeFilterUi(control) {
            const currentValue = String(control?.dataset.filterValue || '');
            const options = Array.from(control?.querySelectorAll('[data-grid-scope-filter-option]') || []);
            const selectedOption = options.find(option => String(option.dataset.value || '') === currentValue) || options[0];
            if (control && selectedOption && String(selectedOption.dataset.value || '') !== currentValue) {
                control.dataset.filterValue = String(selectedOption.dataset.value || '');
            }
            const label = control?.querySelector('[data-grid-scope-filter-label]');
            if (label) label.textContent = selectedOption?.dataset.label || selectedOption?.textContent?.trim() || 'All';
            options.forEach(option => {
                const selected = option === selectedOption;
                option.classList.toggle('selected', selected);
                option.setAttribute('aria-checked', selected ? 'true' : 'false');
            });
        }

        // Clears only the per-column grid filters. Search, scope filters, and sorting are intentionally preserved.
        function clearFilters() {
            filters.forEach(input => {
                input.value = '';
                updateDateMacroHint(input);
            });
            viewState.resetPage();
            render();
            scrollGridToTop();
        }

        // Cycles a column through ascending, descending, and unsorted states.
        function cycleSort(column) {
            if (sortColumn !== column) {
                sortColumn = column;
                sortDirection = 'asc';
            } else if (sortDirection === 'asc') {
                sortDirection = 'desc';
            } else if (sortDirection === 'desc') {
                sortColumn = '';
                sortDirection = '';
            } else {
                sortDirection = 'asc';
            }
            viewState.resetPage();
            updateSortIndicators();
            render();
            scrollGridToTop();
        }

        // Updates visual sort arrows on all sortable headers.
        function updateSortIndicators() {
            getColumns(table).forEach(header => {
                const indicator = header.querySelector('.sort-indicator');
                header.classList.toggle('sorted', header.dataset.column === sortColumn && Boolean(sortDirection));
                if (indicator) indicator.textContent = header.dataset.column === sortColumn ? (sortDirection === 'asc' ? '↑' : sortDirection === 'desc' ? '↓' : '') : '';
            });
        }

        // The header checkbox is the master selection control. By default it affects only rows
        // visible on the current page. The application setting can explicitly extend this to every
        // filtered row across pages when that behavior is appropriate for a deployment.
        // Purpose: Apply the header checkbox selection scope to current-page or all filtered rows.
        function selectRowsForHeaderCheckbox(checked) {
            viewState.selectRowsForHeaderCheckbox(checked, sortRows(getFilteredRows()));
        }

        const updateSettingsAction = window.MauiModalUI.watchAction(settingsDialog, grid.querySelector('[data-settings-apply]'),
            () => true, () => {
                const selected = [...columnChoices.querySelectorAll('input:checked')].map(input => input.value).sort();
                return selected.length > 0 && (normalizePageSize(settingsPageSize.value) !== viewState.getPageSize()
                    || JSON.stringify(selected) !== JSON.stringify(layout.getVisibleColumns().sort()));
            });

        // Opens the grid settings dialog after synchronizing its controls to current preferences.
        function openSettings() {
            layout.populateColumnChoices(columnChoices);
            settingsPageSize.value = String(viewState.getPageSize());
            settingsDialog.showModal();
        }

        // Applies dialog choices to the live grid and persists them for this user.
        function applySettings() {
            const selectedColumns = Array.from(columnChoices.querySelectorAll('input:checked')).map(input => input.value);
            if (!selectedColumns.length) {
                showToast('Keep at least one data column visible.');
                return;
            }
            layout.setVisibleColumns(selectedColumns);
            const pageSize = normalizePageSize(settingsPageSize.value);
            viewState.setPageSize(pageSize);
            pageSizeSelect.value = String(pageSize);
            const pageSizeSave = saveGlobalPageSize();
            layout.applyColumnVisibility();
            layout.synchronizeTableWidth();
            // If both settings changed, complete the global page-size write before the layout write.
            // Column-only changes skip the page-size request entirely.
            void pageSizeSave.finally(() => layout.savePreferences());
            settingsDialog.close();
            render();
        }

        // Restores configuration-defined grid columns, order, widths, and the global default row size.
        function restoreDefaults() {
            viewState.restoreDefaultPageSize();
            saveGlobalPageSize();
            layout.restoreDefaults();
            populatePageSizes();
            layout.populateColumnChoices(columnChoices);
            updateSettingsAction();
            render();
        }

        // Removes server-confirmed rows immediately so destructive actions feel responsive while the normal
        // feature refresh still reconciles the grid with MOVEit afterward. Failed deletes are never supplied here.
        // Purpose: Remove successfully deleted rows while preserving compatible grid state.
        function removeRows(rowIds) {
            const requestedIds = Array.isArray(rowIds) ? rowIds : [];
            if (requestedIds.length === 0) return;

            const normalizedIds = new Set(requestedIds
                .map(value => String(value ?? '').trim().toLowerCase())
                .filter(Boolean));
            if (normalizedIds.size === 0) return;

            const savedScrollTop = tableScroll?.scrollTop ?? 0;
            const savedScrollLeft = tableScroll?.scrollLeft ?? 0;
            let removed = false;

            rows.forEach(row => {
                const rowId = String(row.dataset.rowId || '').trim().toLowerCase();
                if (!rowId || !normalizedIds.has(rowId)) return;
                row.remove();
                removed = true;
            });

            if (!removed) return;

            rows = Array.from(body.querySelectorAll('tr'));
            originalRowIndex = new Map(rows.map((row, index) => [row, index]));
            refreshSelectionPermissions();
            viewState.reconcileRows();
            render();

            if (tableScroll) {
                tableScroll.scrollTop = Math.min(savedScrollTop, tableScroll.scrollHeight);
                tableScroll.scrollLeft = savedScrollLeft;
            }
        }

        // Replaces the underlying row collection while preserving search/filter/sort/page/layout state.
        // Refreshed row markup is delegated to the shared row renderer so all grid types use one path.
        // Purpose: Replace the row dataset while preserving compatible grid state.
        function replaceRows(rowModels) {
            const incoming = Array.isArray(rowModels) ? rowModels : [];
            const savedScrollTop = tableScroll?.scrollTop ?? 0;
            const savedScrollLeft = tableScroll?.scrollLeft ?? 0;
            const selectionHeader = table.querySelector('.column-header-row .select-column');
            const gridId = String(grid.dataset.grid || '');
            const hasDedicatedRowActions = gridId === 'taskruns' || gridId === 'fileactivity' || gridId === 'audit'
                || gridId === 'reports' || gridId === 'upcomingruns';
            selectionHeader?.classList.toggle('has-row-info', hasDedicatedRowActions || incoming.some(model => Boolean(model?.infoActionUrl)));
            const fragment = document.createDocumentFragment();
            incoming.forEach(model => fragment.appendChild(rowRenderer.buildRowFromModel(model)));
            body.replaceChildren(fragment);

            rows = Array.from(body.querySelectorAll('tr'));
            originalRowIndex = new Map(rows.map((row, index) => [row, index]));
            refreshSelectionPermissions();
            viewState.reconcileRows();

            layout.applyColumnOrder();
            layout.applyColumnVisibility();
            render();
            if (tableScroll) {
                tableScroll.scrollTop = savedScrollTop;
                tableScroll.scrollLeft = savedScrollLeft;
            }
        }

        // Captures only durable presentation state for a named MAUI view. Search text, page number,
        // row selection, scroll position, page size, and open menus are intentionally excluded.
        function captureSavedViewState() {
            const columns = layout.captureSavedViewColumns();
            const savedFilters = {};
            filters.forEach(input => {
                const key = String(input.dataset.columnFilter || '').trim();
                if (!key) return;
                const value = String(input.value || '');
                if (value) savedFilters[key] = value;
            });
            return {
                schemaVersion: 1,
                columns,
                sort: sortColumn && sortDirection ? { column: sortColumn, direction: sortDirection } : null,
                filters: savedFilters
            };
        }

        function captureReportViewState() {
            const columns = exporter.getExportColumns().map((column, index) => ({ ...column, order: index }));
            const reportFilters = {};
            const reportFilterTypes = {};
            filters.forEach(input => {
                const key = String(input.dataset.columnFilter || '').trim();
                const value = String(input.value || '');
                if (!key || !value) return;
                reportFilters[key] = value;
                const header = table.querySelector(`.column-header-row th[data-column="${cssEscape(key)}"]`);
                reportFilterTypes[key] = String(header?.dataset.type || 'text');
            });
            const reportScopeFilters = {};
            scopeFilters.forEach(control => {
                const key = String(control.dataset.filterColumn || '').trim();
                const value = String(control.dataset.filterValue || '').trim();
                if (key && value) reportScopeFilters[key] = value;
            });
            const sortHeader = sortColumn ? table.querySelector(`th[data-column="${cssEscape(sortColumn)}"]`) : null;
            return {
                schemaVersion: 1,
                columns,
                searchText: String(search?.value || ''),
                searchColumns: [...searchColumns],
                filters: reportFilters,
                filterTypes: reportFilterTypes,
                scopeFilters: reportScopeFilters,
                sort: sortColumn && sortDirection ? { column: sortColumn, direction: sortDirection, type: sortHeader?.dataset.type || 'text' } : null
            };
        }

        function captureReportSourceContext() {
            const query = {};
            new URLSearchParams(window.location.search).forEach((value, key) => {
                const normalizedKey = key.toLowerCase();
                if (normalizedKey === 'reportedit' || normalizedKey === 'reportreturn' || normalizedKey === 'reportreturnurl' || normalizedKey === 'gridreturnstate' || normalizedKey === 'returnurl' || normalizedKey === 'returnlabel') return;
                query[key] = value;
            });
            // Report source paths must be independent of the current IIS application name.
            // The server also normalizes and validates these against the source grid route.
            const base = String(document.documentElement.dataset.appBaseUrl || '/').replace(/\/+$/, '');
            const locationPath = window.location.pathname;
            const path = base && (locationPath === base || locationPath.startsWith(`${base}/`))
                ? locationPath.slice(base.length) || '/'
                : locationPath;
            return { path, query };
        }

        function captureReportEditFingerprint() {
            return JSON.stringify({
                viewState: captureReportViewState(),
                sourceContext: captureReportSourceContext()
            });
        }

        function markReportEditSaved(viewState = null, sourceContext = null) {
            if (!activeReportEdit) {
                reportEditSavedFingerprint = null;
                return;
            }
            reportEditSavedFingerprint = JSON.stringify({
                viewState: viewState || captureReportViewState(),
                sourceContext: sourceContext || captureReportSourceContext()
            });
        }

        function hasUnsavedReportEditChanges() {
            if (!activeReportEdit) return false;
            if (!reportEditSavedFingerprint) return true;
            try {
                return captureReportEditFingerprint() !== reportEditSavedFingerprint;
            } catch {
                return true;
            }
        }

        // Applies column-filter state shared by saved views and Reports.
        function applyDurableFilterState(state) {
            let ignoredSettings = 0;
            filters.forEach(input => { input.value = ''; updateDateMacroHint(input); });
            if (state.filters && typeof state.filters === 'object') {
                Object.entries(state.filters).forEach(([key, value]) => {
                    const filter = filters.find(input => String(input.dataset.columnFilter || '') === key);
                    if (!filter) { ignoredSettings += 1; return; }
                    filter.value = String(value ?? '');
                    updateDateMacroHint(filter);
                });
            }
            return ignoredSettings;
        }

        // Applies sort state shared by saved views and Reports.
        function applyDurableSortState(state) {
            const knownColumns = new Set(originalColumnOrder);
            let ignoredSettings = 0;
            sortColumn = '';
            sortDirection = '';
            if (state.sort?.column && (state.sort.direction === 'asc' || state.sort.direction === 'desc')) {
                const key = String(state.sort.column);
                if (knownColumns.has(key)) {
                    sortColumn = key;
                    sortDirection = state.sort.direction;
                } else {
                    ignoredSettings += 1;
                }
            }
            return ignoredSettings;
        }

        // Finalizes a durable view-state change through one render/reset path.
        function finalizeDurableViewState() {
            viewState.resetPage();
            layout.populateColumnChoices(columnChoices);
            updateSortIndicators();
            render();
            scrollGridToTop();
        }

        function applyReportViewState(state) {
            if (!state || Number(state.schemaVersion) !== 1 || !Array.isArray(state.columns) || state.columns.length === 0)
                throw new Error('The saved Report uses an unsupported view schema.');

            let ignoredSettings = layout.applyReportColumns(state.columns);

            if (search) search.value = String(state.searchText || '');
            ignoredSettings += applyDurableFilterState(state);

            const reportScopeFilters = state.scopeFilters && typeof state.scopeFilters === 'object' ? state.scopeFilters : {};
            scopeFilters.forEach(control => {
                const key = String(control.dataset.filterColumn || '').trim();
                control.dataset.filterValue = key && Object.prototype.hasOwnProperty.call(reportScopeFilters, key)
                    ? String(reportScopeFilters[key] ?? '')
                    : '';
                updateScopeFilterUi(control);
            });

            ignoredSettings += applyDurableSortState(state);
            finalizeDurableViewState();
            return ignoredSettings;
        }

        function updateReportEditHeading(report) {
            const pageHeading = document.querySelector('.page-heading');
            const heading = pageHeading?.querySelector('h1');
            if (!heading) return;

            const description = pageHeading.querySelector('p');
            heading.querySelector('[data-report-edit-heading]')?.remove();
            const normalContexts = [...heading.querySelectorAll('.heading-context')];

            if (!report?.id) {
                normalContexts.forEach(context => {
                    if (!Object.prototype.hasOwnProperty.call(context.dataset, 'reportEditPreviousHidden')) return;
                    context.hidden = context.dataset.reportEditPreviousHidden === '1';
                    delete context.dataset.reportEditPreviousHidden;
                });
                if (description && Object.prototype.hasOwnProperty.call(description.dataset, 'reportEditPreviousText')) {
                    description.textContent = description.dataset.reportEditPreviousText;
                    description.hidden = description.dataset.reportEditPreviousHidden === '1';
                    delete description.dataset.reportEditPreviousText;
                    delete description.dataset.reportEditPreviousHidden;
                }
                return;
            }

            normalContexts.forEach(context => {
                if (!Object.prototype.hasOwnProperty.call(context.dataset, 'reportEditPreviousHidden')) {
                    context.dataset.reportEditPreviousHidden = context.hidden ? '1' : '0';
                }
                context.hidden = true;
            });

            const context = document.createElement('span');
            context.className = 'heading-context';
            context.dataset.reportEditHeading = '';
            context.textContent = ` / ${report.title || 'Report'}`;
            context.title = report.title || 'Report';
            heading.appendChild(context);

            if (description) {
                if (!Object.prototype.hasOwnProperty.call(description.dataset, 'reportEditPreviousText')) {
                    description.dataset.reportEditPreviousText = description.textContent || '';
                    description.dataset.reportEditPreviousHidden = description.hidden ? '1' : '0';
                }
                const reportDescription = String(report.description || '').trim();
                description.textContent = reportDescription;
                description.hidden = reportDescription.length === 0;
            }
        }

        function updateReportEditUi(report) {
            activeReportEdit = report?.id ? report : null;
            if (!activeReportEdit) reportEditSavedFingerprint = null;
            const reportButton = grid.querySelector('[data-grid-create-report]');
            if (reportButton) reportButton.textContent = activeReportEdit ? 'Update Report' : 'Create Report';
            if (reportEditIndicator) reportEditIndicator.hidden = !activeReportEdit;
            if (reportEditDelete) reportEditDelete.disabled = !activeReportEdit;
            if (reportEditCancelName) reportEditCancelName.textContent = activeReportEdit?.title || 'this Report';
            updateReportEditHeading(activeReportEdit);
        }

        async function loadActiveReportEdit() {
            if (!reportEditId) { updateReportEditUi(null); return; }
            const endpoint = String(document.documentElement.dataset.outboundReportEditDefinitionUrl || '');
            if (!endpoint) throw new Error('Report editing is unavailable on this page.');

            const url = new URL(endpoint, window.location.origin);
            url.searchParams.set('id', reportEditId);
            const response = await fetch(url.toString(), { credentials: 'same-origin', cache: 'no-store' });
            const data = await response.json().catch(() => ({}));
            if (response.status === 401) { window.mauiSessionExpired?.(); return; }
            if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
            const report = data?.report;
            if (!report || String(report.gridKey || '').toLowerCase() !== String(grid.dataset.grid || '').toLowerCase())
                throw new Error('This Report belongs to a different grid.');

            updateSavedViewUi(null);
            const ignored = applyReportViewState(report.viewState);
            updateReportEditUi({ id: report.id, title: report.title || 'Report', description: report.description || '', gridKey: report.gridKey });
            markReportEditSaved();
            if (ignored > 0) window.mauiShowToast?.('Report loaded. Some saved view settings are no longer available in this grid.');
        }

        // Applies a saved view defensively against the grid definition that exists in this MAUI build.
        // Removed/renamed columns are ignored; newly introduced columns retain their normal defaults.
        function applySavedViewState(state) {
            if (!state || Number(state.schemaVersion) !== 1 || !Array.isArray(state.columns) || state.columns.length === 0)
                throw new Error('The saved view uses an unsupported schema.');

            let ignoredSettings = layout.applySavedViewColumns(state.columns);
            ignoredSettings += applyDurableFilterState(state);
            ignoredSettings += applyDurableSortState(state);
            finalizeDurableViewState();
            return ignoredSettings;
        }

        // Shows the active saved-view name and exposes Update/Delete only for an active saved view.
        // Create View remains available so a new view can be derived from the current grid state.
        function updateSavedViewUi(view) {
            activeSavedView = view?.id ? view : null;
            const updateButton = grid.querySelector('[data-grid-update-view]');
            const deleteButton = grid.querySelector('[data-grid-delete-view]');
            const divider = grid.querySelector('[data-grid-saved-view-divider]');
            if (updateButton) updateButton.hidden = !activeSavedView;
            if (deleteButton) deleteButton.hidden = !activeSavedView;
            if (divider) divider.hidden = false;

            const heading = document.querySelector('.page-heading h1');
            if (heading) {
                heading.querySelector('[data-saved-view-heading]')?.remove();
                heading.querySelectorAll('.heading-context').forEach(context => {
                    if (!context.hasAttribute('data-saved-view-heading')) context.hidden = Boolean(activeSavedView);
                });
                if (activeSavedView) {
                    const context = document.createElement('span');
                    context.className = 'heading-context';
                    context.dataset.savedViewHeading = '';
                    context.textContent = ` / ${activeSavedView.viewName}`;
                    context.title = activeSavedView.viewName;
                    heading.appendChild(context);
                }
            }

            const pageDescription = document.querySelector('.page-heading p');
            if (pageDescription) {
                if (!pageDescription.dataset.savedViewDefaultDescription) {
                    pageDescription.dataset.savedViewDefaultDescription = pageDescription.textContent || '';
                }
                pageDescription.textContent = activeSavedView?.description?.trim()
                    ? activeSavedView.description.trim()
                    : pageDescription.dataset.savedViewDefaultDescription;
            }
        }

        // Purpose: Displays a one-time Update View success toast after the saved-view redirect completes.
        function showPendingSavedViewUpdateToast(viewId) {
            const key = 'maui-saved-view-update-toast';
            try {
                const raw = window.sessionStorage.getItem(key);
                if (!raw) return;
                window.sessionStorage.removeItem(key);
                const pending = JSON.parse(raw);
                if (String(pending?.viewId || '') !== String(viewId || '')) return;
                window.mauiShowToast?.(pending?.message || 'View updated.');
            } catch {
                try { window.sessionStorage.removeItem(key); } catch { }
            }
        }

        // Purpose: Loads and applies the active saved view for the current grid.
        async function loadActiveSavedView() {
            const id = String(grid.dataset.activeSavedViewId || '').trim();
            if (!id) { updateSavedViewUi(null); return; }
            const endpoint = document.documentElement.dataset.savedViewGetUrl;
            if (!endpoint) return;
            try {
                const url = new URL(endpoint, window.location.origin);
                url.searchParams.set('id', id);
                const response = await fetch(url.toString(), { credentials: 'same-origin', cache: 'no-store' });
                const data = await response.json().catch(() => ({}));
                if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
                const view = data?.view;
                if (!view || String(view.gridKey || '').toLowerCase() !== String(grid.dataset.grid || '').toLowerCase())
                    throw new Error('This saved view belongs to a different grid.');
                updateSavedViewUi({ id: view.id, viewName: view.viewName, description: view.description || '', gridKey: view.gridKey });
                if (!view.state) {
                    window.mauiShowToast?.(view.warning || 'The saved view could not be applied. The normal grid has been loaded instead.');
                    return;
                }
                const ignored = applySavedViewState(view.state);
                showPendingSavedViewUpdateToast(view.id);
                if (view.warning) window.mauiShowToast?.(view.warning);
                else if (ignored > 0) window.mauiShowToast?.('Saved view loaded. Some settings are no longer available in this grid.');
            } catch (error) {
                updateSavedViewUi(null);
                window.mauiShowToast?.(error?.message || 'The saved view could not be loaded.', 'error');
            }
        }

        const rootSavedViewId = String(document.documentElement.dataset.activeSavedViewId || '').trim();
        const rootSavedViewGrid = String(document.documentElement.dataset.activeSavedViewGrid || '').trim();
        if (rootSavedViewId && rootSavedViewGrid.toLowerCase() === String(grid.dataset.grid || '').toLowerCase()) {
            activeSavedView = {
                id: rootSavedViewId,
                viewName: String(document.documentElement.dataset.activeSavedViewName || ''),
                description: String(document.documentElement.dataset.activeSavedViewDescription || ''),
                gridKey: rootSavedViewGrid
            };
        }

        const savedViewApi = {
            gridKey: String(grid.dataset.grid || ''),
            captureState: captureSavedViewState,
            getActiveView: () => activeSavedView
        };

        const reportApi = {
            gridKey: String(grid.dataset.grid || ''),
            captureViewState: captureReportViewState,
            captureSourceContext: captureReportSourceContext,
            captureSnapshot: () => exporter.captureCurrentViewSnapshot(),
            getEditContext: () => activeReportEdit,
            markViewSaved: markReportEditSaved
        };

        // Displays a short notice for controls whose server-side actions are intentionally not wired yet.
        function showToast(message) {
            const toast = grid.querySelector('[data-prototype-toast]');
            const text = `${message} — this action is not wired yet.`;
            if (typeof window.mauiDisplayToast === 'function') { window.mauiDisplayToast(toast, text); return; }
            toast.textContent = text;
            toast.classList.add('visible');
            window.clearTimeout(toast._timer);
            toast._timer = window.setTimeout(() => toast.classList.remove('visible'), 5000);
        }

        const hasSavedPreferences = layout.loadPreferences();
        layout.applyColumnOrder();
        layout.applyColumnVisibility();
        layout.initializeBaseColumnWidths();
        if (!hasSavedPreferences) {
            layout.fitInitialColumnsToGrid();
            layout.synchronizeTableWidth(false);
        } else {
            layout.synchronizeTableWidth();
        }
        populatePageSizes();
        layout.populateColumnChoices(columnChoices);
        layout.wireColumnReordering();
        layout.wireColumnResizing();

        if (pageSelect) {
            pageSelect.setAttribute('aria-label', selectRowsBeyondGridSize
                ? 'Select all rows matching the current filters'
                : 'Select all rows visible on the current page');
            pageSelect.title = selectRowsBeyondGridSize
                ? 'Select all filtered rows, including rows on other pages'
                : 'Select all visible rows on this page';
        }

        scopeFilters.forEach(control => {
            updateScopeFilterUi(control);
            control.querySelectorAll('[data-grid-scope-filter-option]').forEach(option => {
                option.addEventListener('click', event => {
                    event.preventDefault();
                    event.stopPropagation();
                    control.dataset.filterValue = String(option.dataset.value || '');
                    updateScopeFilterUi(control);
                    control.classList.remove('open');
                    control.querySelector('[data-menu-button]')?.setAttribute('aria-expanded', 'false');
                    viewState.resetPage();
                    render();
                    scrollGridToTop();
                });
            });
        });
        search?.addEventListener('input', () => { viewState.resetPage(); render(); scrollGridToTop(); });
        searchClear?.addEventListener('click', () => { search.value = ''; search.focus(); viewState.resetPage(); render(); scrollGridToTop(); });
        filters.forEach(input => input.addEventListener('input', () => {
            updateDateMacroHint(input);
            viewState.resetPage();
            render();
            scrollGridToTop();
        }));
        filterClearButtons.forEach(button => button.addEventListener('click', () => {
            const key = String(button.dataset.columnFilterClear || '');
            const input = filters.find(item => String(item.dataset.columnFilter || '') === key);
            if (!input || !String(input.value || '')) return;
            input.value = '';
            updateDateMacroHint(input);
            viewState.resetPage();
            render();
            scrollGridToTop();
            input.focus();
        }));
        filterHelpButton?.addEventListener('click', openFilterHelp);
        dateMacroButtons.forEach(button => button.addEventListener('click', () => openDateMacroPicker(button)));
        dateMacroOptions.forEach(button => button.addEventListener('click', () => applyDateMacroOption(button)));
        dateMacroDialog?.addEventListener('close', () => { activeDateMacroFilter = null; });
        table.querySelectorAll('[data-sort-button]').forEach(button => button.addEventListener('click', () => cycleSort(button.closest('th').dataset.column)));
        pageSizeSelect?.addEventListener('change', () => { const pageSize = normalizePageSize(pageSizeSelect.value); viewState.setPageSize(pageSize); settingsPageSize.value = String(pageSize); saveGlobalPageSize(); render(); scrollGridToTop(); });
        previousButton?.addEventListener('click', () => { viewState.movePage(-1); render(); scrollGridToTop(); });
        nextButton?.addEventListener('click', () => { viewState.movePage(1); render(); scrollGridToTop(); });
        pageSelect?.addEventListener('change', () => selectRowsForHeaderCheckbox(pageSelect.checked));
        body.addEventListener('change', event => {
            const checkbox = event.target.closest('[data-row-select]');
            if (!checkbox || checkbox.disabled) return;
            viewState.setRowSelected(checkbox.value, checkbox.checked, sortRows(getFilteredRows()));
        });
        grid.querySelectorAll('[data-clear-filters]').forEach(button => button.addEventListener('click', clearFilters));
        function openReportEditCancelDialog(event) {
            if (!hasUnsavedReportEditChanges()) return;
            event?.preventDefault();
            if (!reportEditCancelDialog) {
                const reportsUrl = String(reportEditBack?.href || document.documentElement.dataset.outboundReportIndexUrl || window.mauiAppUrl('/Reports'));
                window.location.assign(reportsUrl);
                return;
            }
            if (!reportEditCancelDialog.open) reportEditCancelDialog.showModal();
        }

        reportEditBack?.addEventListener('click', openReportEditCancelDialog);
        reportEditCancelDismissButtons.forEach(button => button.addEventListener('click', () => reportEditCancelDialog?.close()));
        reportEditCancelConfirm?.addEventListener('click', () => {
            const reportsUrl = String(reportEditBack?.href || document.documentElement.dataset.outboundReportIndexUrl || window.mauiAppUrl('/Reports'));
            window.location.assign(reportsUrl);
        });

        function openReportEditDeleteDialog() {
            if (!activeReportEdit?.id || !reportEditDeleteDialog) return;
            const title = String(activeReportEdit.title || 'Report');
            const heading = reportEditDeleteDialog.querySelector('[data-delete-reports-title]');
            if (heading) heading.textContent = 'Delete Report';
            if (reportEditDeleteMessage) reportEditDeleteMessage.textContent = `Delete Report "${title}"?`;
            if (reportEditDeleteList) {
                reportEditDeleteList.replaceChildren();
                reportEditDeleteList.hidden = true;
            }
            if (reportEditDeleteError) {
                reportEditDeleteError.hidden = true;
                reportEditDeleteError.textContent = '';
            }
            if (!reportEditDeleteDialog.open) reportEditDeleteDialog.showModal();
        }

        async function confirmReportEditDelete() {
            if (!activeReportEdit?.id || !reportEditDeleteDialog) return;
            const endpoint = String(document.documentElement.dataset.outboundReportDeleteUrl || '');
            if (!endpoint) {
                if (reportEditDeleteError) {
                    reportEditDeleteError.textContent = 'Report deletion is unavailable on this page.';
                    reportEditDeleteError.hidden = false;
                }
                return;
            }

            const token = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
            if (reportEditDeleteConfirm) reportEditDeleteConfirm.disabled = true;
            reportEditDeleteCancelButtons.forEach(button => button.disabled = true);
            try {
                const response = await fetch(endpoint, {
                    method: 'POST',
                    credentials: 'same-origin',
                    cache: 'no-store',
                    headers: {
                        Accept: 'application/json',
                        'Content-Type': 'application/json',
                        ...(token ? { RequestVerificationToken: token } : {})
                    },
                    body: JSON.stringify({ reportIds: [activeReportEdit.id] })
                });
                if (response.status === 401) { window.mauiSessionExpired?.(); return; }
                const data = await response.json().catch(() => ({}));
                if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);

                reportEditDeleteDialog.close();
                window.mauiShowToast?.('Deleted 1 Report.');
                const reportsUrl = String(reportEditBack?.href || document.documentElement.dataset.outboundReportIndexUrl || window.mauiAppUrl('/Reports'));
                window.location.assign(reportsUrl);
            } catch (error) {
                if (reportEditDeleteError) {
                    reportEditDeleteError.textContent = error?.message || 'The Report could not be deleted.';
                    reportEditDeleteError.hidden = false;
                }
            } finally {
                if (reportEditDeleteConfirm) reportEditDeleteConfirm.disabled = false;
                reportEditDeleteCancelButtons.forEach(button => button.disabled = false);
            }
        }

        reportEditDelete?.addEventListener('click', event => {
            const menu = event.currentTarget.closest('[data-menu]');
            menu?.classList.remove('open');
            menu?.querySelector('[data-menu-button]')?.setAttribute('aria-expanded', 'false');
            openReportEditDeleteDialog();
        });
        reportEditDeleteCancelButtons.forEach(button => button.addEventListener('click', () => reportEditDeleteDialog?.close()));
        reportEditDeleteConfirm?.addEventListener('click', confirmReportEditDelete);
        grid.querySelector('[data-grid-settings]')?.addEventListener('click', openSettings);
        grid.querySelector('[data-settings-cancel]')?.addEventListener('click', () => settingsDialog?.close());
        grid.querySelector('[data-settings-apply]')?.addEventListener('click', applySettings);
        grid.querySelector('[data-restore-defaults]')?.addEventListener('click', restoreDefaults);
        window.addEventListener('maui:user-grid-page-size-changed', event => {
            const pageSize = normalizePageSize(event?.detail?.value);
            persistedGlobalPageSize = pageSize;
            viewState.setPageSize(pageSize);
            if (pageSizeSelect) pageSizeSelect.value = String(pageSize);
            if (settingsPageSize) settingsPageSize.value = String(pageSize);
            render();
            scrollGridToTop();
        });
        const actionManager = window.MauiGridActions.createManager({
            grid,
            savedViewManager,
            savedViewApi,
            exporter,
            printer,
            reportApi,
            getSelectedIds: () => viewState.getSelectedIds(),
            showFallbackToast: showToast
        });
        actionManager.wire();
        window.addEventListener('resize', () => { sizeGridToViewport(); layout.synchronizeTableWidth(); });
        grid.addEventListener('maui:grid-replace-rows', event => replaceRows(event.detail?.rows));
        grid.addEventListener('maui:grid-remove-rows', event => removeRows(event.detail?.rowIds));
        grid.addEventListener('maui:grid-initial-data-ready', () => {
            initialDataReady = true;
            completeInitialLoadingWhenReady();
        });

        // Re-evaluates true horizontal overflow when the grid viewport changes width without a
        // browser resize event, such as when the application sidebar is expanded or collapsed.
        if (window.ResizeObserver && tableScroll) {
            const viewportObserver = new ResizeObserver(() => layout.synchronizeTableWidth());
            viewportObserver.observe(tableScroll);
            const tableCard = tableScroll.closest('.table-card');
            if (tableCard) viewportObserver.observe(tableCard);
        }

        refreshSelectionPermissions();
        sizeGridToViewport();
        if (activeSavedView) updateSavedViewUi(activeSavedView);
        filters.forEach(updateDateMacroHint);
        updateSortIndicators();
        render();

        // A saved view or Report edit can alter columns, filters, and sorting asynchronously. Report
        // edit mode is authoritative when present, so it bypasses a savedView query if one was supplied.
        const initialViewLoad = reportEditId ? loadActiveReportEdit() : loadActiveSavedView();
        void initialViewLoad.catch(error => {
            updateReportEditUi(null);
            window.mauiShowToast?.(error?.message || 'The Report view could not be loaded.', 'error');
        }).finally(() => {
            gridInitializationReady = true;
            completeInitialLoadingWhenReady();
        });
    }
})();
