/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Shared paging and row-selection state for MAUI data grids.
 */

(() => {
    window.MauiGridViewState = {
        createManager(options) {
            const {
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
                getRows,
                isRowSelectable,
                canUseBulkActions = () => true
            } = options;

            const selectedIds = new Set();
            let currentPage = 1;
            let pageSize = initialPageSize;

            // Returns the current row-size setting used by paging.
            function getPageSize() {
                return pageSize;
            }

            // Applies a validated row-size setting and optionally returns to the first page.
            function setPageSize(value, resetPage = true) {
                pageSize = value;
                if (resetPage) currentPage = 1;
            }

            // Returns paging to the first page after a filter, sort, or view change.
            function resetPage() {
                currentPage = 1;
            }

            // Moves the active page by a relative amount; render() clamps it to the valid range.
            function movePage(delta) {
                currentPage += Number(delta) || 0;
            }

            // Restores the configured default row size and returns to page one.
            function restoreDefaultPageSize() {
                pageSize = defaultPageSize;
                currentPage = 1;
            }

            // Returns the selectable rows covered by the header checkbox for the current view.
            function getSelectionScope(orderedRows) {
                const start = (currentPage - 1) * pageSize;
                const visible = orderedRows.slice(start, start + pageSize);
                return (selectRowsBeyondGridSize ? orderedRows : visible).filter(isRowSelectable);
            }

            // Synchronizes the master checkbox and bulk-action controls with current selection state.
            function updateSelectionControls(orderedRows) {
                if (pageSelect) {
                    const selectionScope = getSelectionScope(orderedRows);
                    pageSelect.disabled = selectionScope.length === 0;
                    pageSelect.checked = selectionScope.length > 0 && selectionScope.every(row => selectedIds.has(row.dataset.rowId));
                    pageSelect.indeterminate = selectionScope.some(row => selectedIds.has(row.dataset.rowId)) && !pageSelect.checked;
                }

                const count = selectedIds.size;
                if (bulkButton) bulkButton.disabled = count === 0 || !canUseBulkActions(Array.from(selectedIds));
                if (selectionCount) selectionCount.textContent = count ? `${count} selected` : '';
            }

            // Reconciles row-selection visuals without rebuilding row order or disturbing scroll position.
            function refreshSelectionState(orderedRows) {
                getRows().forEach(row => {
                    const isSelected = selectedIds.has(row.dataset.rowId);
                    row.classList.toggle('row-selected', isSelected);
                    const checkbox = row.querySelector('[data-row-select]');
                    if (checkbox) checkbox.checked = isSelected;
                });

                updateSelectionControls(orderedRows);
            }

            // Applies paging and selection presentation to an already filtered/sorted row collection.
            function render(orderedRows) {
                const rows = getRows();

                // Selection follows the current filtered dataset, not hidden historical rows. Preserve
                // selection across paging because every matching row remains in orderedRows, but drop a
                // selection as soon as search/scope/column filters or a data refresh exclude that row.
                const filteredSelectableIds = new Set(orderedRows.filter(canRetainSelection).map(row => row.dataset.rowId));
                Array.from(selectedIds).forEach(id => {
                    if (!filteredSelectableIds.has(id)) selectedIds.delete(id);
                });

                const pageCount = Math.max(1, Math.ceil(orderedRows.length / pageSize));
                currentPage = Math.min(Math.max(1, currentPage), pageCount);
                const start = (currentPage - 1) * pageSize;
                const visible = orderedRows.slice(start, start + pageSize);
                const visibleSet = new Set(visible);

                rows.forEach(row => {
                    row.hidden = !visibleSet.has(row);
                    const isSelected = selectedIds.has(row.dataset.rowId);
                    row.classList.toggle('row-selected', isSelected);
                    const checkbox = row.querySelector('[data-row-select]');
                    if (checkbox) checkbox.checked = isSelected;
                });

                const displayedOverrideText = String(grid.dataset.displayedCountOverride || '').trim();
                const displayedOverride = displayedOverrideText === '' ? Number.NaN : Number(displayedOverrideText);
                if (filteredCount) filteredCount.textContent = Number.isFinite(displayedOverride) && displayedOverride >= 0
                    ? String(displayedOverride)
                    : String(orderedRows.length);

                const totalOverrideText = String(grid.dataset.totalCountOverride || '').trim();
                const totalOverride = totalOverrideText === '' ? Number.NaN : Number(totalOverrideText);
                if (totalCount) totalCount.textContent = Number.isFinite(totalOverride) && totalOverride >= 0
                    ? String(totalOverride)
                    : String(rows.length);

                if (emptyState) emptyState.hidden = orderedRows.length !== 0;
                const displayStart = orderedRows.length ? start + 1 : 0;
                const displayEnd = Math.min(start + pageSize, orderedRows.length);
                if (pagerSummary) pagerSummary.textContent = `Showing ${displayStart}–${displayEnd} of ${orderedRows.length}`;
                if (pageIndicator) pageIndicator.textContent = `${currentPage} / ${pageCount}`;
                if (previousButton) previousButton.disabled = currentPage <= 1;
                if (nextButton) nextButton.disabled = currentPage >= pageCount;

                updateSelectionControls(orderedRows);
                return visible;
            }

            // Applies the header checkbox to either the current page or all filtered rows per configuration.
            function selectRowsForHeaderCheckbox(checked, orderedRows) {
                getSelectionScope(orderedRows).forEach(row => {
                    if (checked) selectedIds.add(row.dataset.rowId);
                    else selectedIds.delete(row.dataset.rowId);
                });
                refreshSelectionState(orderedRows);
            }

            // Applies one row checkbox change to the authoritative selection set.
            function setRowSelected(rowId, checked, orderedRows) {
                if (checked && orderedRows.some(row => row.dataset.rowId === rowId && isRowSelectable(row))) selectedIds.add(rowId);
                else selectedIds.delete(rowId);
                refreshSelectionState(orderedRows);
            }

            // A permission refresh temporarily disables controls without discarding a still-valid
            // selection. New selections remain blocked until effective access is confirmed.
            function canRetainSelection(row) {
                return isRowSelectable(row) || row.querySelector('[data-row-select]')?.dataset.objectSelectionPending === 'true';
            }

            // Removes selections for rows that no longer exist or are no longer selectable after refresh.
            function reconcileRows() {
                const rows = getRows();
                const existingIds = new Set(rows.map(row => row.dataset.rowId));
                const selectableIds = new Set(rows.filter(canRetainSelection).map(row => row.dataset.rowId));
                Array.from(selectedIds).forEach(id => {
                    if (!existingIds.has(id) || !selectableIds.has(id)) selectedIds.delete(id);
                });
            }

            // Returns a snapshot for feature-specific bulk actions without exposing mutable selection state.
            function getSelectedIds() {
                return Array.from(selectedIds);
            }

            return {
                getPageSize,
                setPageSize,
                resetPage,
                movePage,
                restoreDefaultPageSize,
                render,
                refreshSelectionState,
                selectRowsForHeaderCheckbox,
                setRowSelected,
                reconcileRows,
                getSelectedIds
            };
        }
    };
})();
