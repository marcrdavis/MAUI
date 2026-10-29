/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Shared CSV export behavior for MAUI data grids.
 */

(() => {
    window.MauiGridExport = {
        createManager(options) {
            const {
                grid,
                table,
                layout,
                getOrderedRows,
                cssEscape
            } = options;

            // Returns visible export columns in the exact order currently shown in the grid.
            function getExportColumns() {
                return layout.getColumnOrder()
                    .filter(key => layout.isColumnVisible(key))
                    .map(key => {
                        const header = table.querySelector(`.column-header-row th[data-column="${cssEscape(key)}"]`);
                        return {
                            key,
                            label: header?.querySelector('.sort-button span')?.textContent?.trim() || key,
                            type: header?.dataset.type || 'text',
                            width: layout.getColumnWidth?.(key) ?? null
                        };
                    });
            }

            // Protects text values that spreadsheet applications could otherwise interpret as formulas.
            function protectCsvCell(value, type) {
                const text = String(value ?? '');
                if (type !== 'number' && /^[=+\-@]/.test(text)) return `'${text}`;
                return text;
            }

            // Escapes one RFC-style CSV field using double quotes when required.
            function escapeCsvField(value) {
                const text = String(value ?? '');
                if (/[",\r\n]/.test(text)) return `"${text.replaceAll('"', '""')}"`;
                return text;
            }

            // Builds the existing readable export filename from page/view context and local timestamp.
            function buildCsvFileName() {
                const heading = document.querySelector('.page-heading h1')?.textContent?.trim()
                    || grid.dataset.grid
                    || 'Grid';
                const parts = [heading];

                if (grid.dataset.grid === 'tasks') {
                    const folderSelect = document.getElementById('task-folder-filter');
                    const folderName = folderSelect?.selectedOptions?.[0]?.textContent?.trim();
                    if (folderName && folderName.toLowerCase() !== 'all tasks') parts.push(folderName);
                }

                const now = new Date();
                const pad = value => String(value).padStart(2, '0');
                const timestamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}_${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
                parts.push(timestamp);

                const safeName = parts
                    .join('_')
                    .replace(/[<>:"/\\|?*\u0000-\u001f]+/g, '_')
                    .replace(/\s+/g, '_')
                    .replace(/_+/g, '_')
                    .replace(/^_|_$/g, '');

                return `${safeName || 'Grid'}.csv`;
            }

            // Captures the current visible/sorted/filtered grid as a transport-safe snapshot for Send Now reports.
            function captureCurrentViewSnapshot() {
                const columns = getExportColumns().map((column, index) => ({ ...column, order: index }));
                const rows = getOrderedRows().map(row => {
                    const values = {};
                    const rawValues = {};
                    columns.forEach(column => {
                        const cell = row.querySelector(`td[data-column="${cssEscape(column.key)}"]`);
                        const displayValue = cell?.textContent?.trim() || '';
                        const rawValue = cell?.dataset.value ?? displayValue;
                        values[column.key] = column.type === 'tags' ? rawValue : displayValue;
                        rawValues[column.key] = rawValue;
                    });
                    return { id: String(row.dataset.rowId || ''), values, rawValues };
                });
                return { columns, rows };
            }

            // Exports every row matching the current filters/sort and only currently visible columns.
            function exportCurrentViewToCsv() {
                const exportColumns = getExportColumns();
                if (!exportColumns.length) return;

                const lines = [];
                lines.push(exportColumns.map(column => escapeCsvField(column.label)).join(','));

                getOrderedRows().forEach(row => {
                    const values = exportColumns.map(column => {
                        const cell = row.querySelector(`td[data-column="${cssEscape(column.key)}"]`);
                        const displayValue = column.type === 'tags'
                            ? (cell?.dataset.value ?? cell?.textContent?.trim() ?? '')
                            : (cell?.textContent?.trim() || '');
                        return escapeCsvField(protectCsvCell(displayValue, column.type));
                    });
                    lines.push(values.join(','));
                });

                const blob = new Blob([`\uFEFF${lines.join('\r\n')}`], { type: 'text/csv;charset=utf-8' });
                const url = URL.createObjectURL(blob);
                const download = document.createElement('a');
                download.href = url;
                download.download = buildCsvFileName();
                download.style.display = 'none';
                document.body.appendChild(download);
                download.click();
                download.remove();
                window.setTimeout(() => URL.revokeObjectURL(url), 0);
            }

            return { exportCurrentViewToCsv, captureCurrentViewSnapshot, getExportColumns };
        }
    };
})();
