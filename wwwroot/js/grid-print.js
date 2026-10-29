/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Shared printer-friendly rendering for MAUI data grids.
 */

(() => {
    const largeViewConfirmationThreshold = 2000;

    window.MauiGridPrint = {
        createManager(options) {
            const { grid, exporter } = options || {};
            if (!grid) throw new Error('MauiGridPrint requires a grid element.');

            function getPageTitle() {
                return document.querySelector('.page-heading h1')?.textContent?.trim()
                    || document.title?.replace(/\s+-\s+MAUI.*$/i, '').trim()
                    || grid.dataset.grid
                    || 'MAUI Data';
            }

            function getGeneratedLabel(rowCount) {
                const count = Number.isFinite(rowCount) ? Math.max(0, rowCount) : 0;
                return `Generated ${new Date().toLocaleString()} · ${count.toLocaleString()} ${count === 1 ? 'row' : 'rows'}`;
            }

            function appendPrintableText(target, value) {
                const text = String(value ?? '');
                const parts = text.split('_');
                parts.forEach((part, index) => {
                    if (index > 0) target.appendChild(target.ownerDocument.createTextNode('_'));
                    if (part) target.appendChild(target.ownerDocument.createTextNode(part));
                    if (index < parts.length - 1) target.appendChild(target.ownerDocument.createElement('wbr'));
                });
            }

            function getPrintableTextLength(value) {
                const text = String(value ?? '').trim();
                if (!text) return 0;

                // Treat explicit line breaks as separate printable lines and cap very long values
                // so one Notes/path value cannot consume most of the printed page.
                return Math.min(60, text.split(/\r?\n/).reduce((max, line) => Math.max(max, line.trim().length), 0));
            }

            function calculatePrintColumnWidths(snapshot) {
                const columns = snapshot?.columns || [];
                if (!columns.length) return [];

                const rows = snapshot.rows || [];
                const maxRowsToMeasure = 5000;
                const rowStep = Math.max(1, Math.ceil(rows.length / maxRowsToMeasure));

                const preferredWidths = columns.map(column => {
                    let characterWidth = Math.max(6, getPrintableTextLength(column.label));
                    for (let index = 0; index < rows.length; index += rowStep) {
                        characterWidth = Math.max(
                            characterWidth,
                            getPrintableTextLength(rows[index]?.values?.[column.key])
                        );
                    }

                    // Approximate a useful 9pt print width while keeping short fields compact and
                    // long descriptive fields bounded. Relative widths are normalized below.
                    return Math.max(70, Math.min(320, 28 + (characterWidth * 5.4)));
                });

                const total = preferredWidths.reduce((sum, width) => sum + width, 0) || 1;
                return preferredWidths.map(width => (width / total) * 100);
            }

            function addPrintStyles(printDocument) {
                const style = printDocument.createElement('style');
                style.textContent = `
                    @page { size: landscape; margin: 0.45in; }
                    * { box-sizing: border-box; }
                    body {
                        margin: 0;
                        background: #fff;
                        color: #111;
                        font-family: Arial, Helvetica, sans-serif;
                        font-size: 9pt;
                    }
                    .print-header { margin: 0 0 14px; }
                    .print-title { margin: 0; font-size: 18pt; font-weight: 700; line-height: 1.15; }
                    .print-generated { margin-top: 4px; color: #555; font-size: 8.5pt; }
                    table { width: 100%; table-layout: fixed; border-collapse: collapse; border-spacing: 0; }
                    thead { display: table-header-group; }
                    tr { break-inside: avoid; page-break-inside: avoid; }
                    th, td {
                        border: 1px solid #b8b8b8;
                        padding: 4px 6px;
                        text-align: left;
                        vertical-align: top;
                        overflow-wrap: normal;
                        word-break: normal;
                    }
                    th { background: #ededed; font-weight: 700; }
                    tbody tr:nth-child(even) td { background: #f8f8f8; }
                    .print-empty { padding: 12px; text-align: center; color: #555; }
                    @media print {
                        body { print-color-adjust: exact; -webkit-print-color-adjust: exact; }
                    }
                `;
                printDocument.head.appendChild(style);
            }

            function renderPrintDocument(printWindow, snapshot) {
                const printDocument = printWindow.document;
                printDocument.open();
                printDocument.write('<!doctype html><html><head><meta charset="utf-8"><title></title></head><body></body></html>');
                printDocument.close();

                const title = getPageTitle();
                printDocument.title = title;
                addPrintStyles(printDocument);

                const header = printDocument.createElement('header');
                header.className = 'print-header';

                const heading = printDocument.createElement('h1');
                heading.className = 'print-title';
                heading.textContent = title;

                const generated = printDocument.createElement('div');
                generated.className = 'print-generated';
                generated.textContent = getGeneratedLabel(snapshot.rows?.length || 0);

                header.append(heading, generated);
                printDocument.body.appendChild(header);

                const table = printDocument.createElement('table');
                const columnWidths = calculatePrintColumnWidths(snapshot);
                const colgroup = printDocument.createElement('colgroup');
                snapshot.columns.forEach((column, index) => {
                    const col = printDocument.createElement('col');
                    const width = columnWidths[index];
                    if (Number.isFinite(width) && width > 0) col.style.width = `${width.toFixed(3)}%`;
                    colgroup.appendChild(col);
                });
                table.appendChild(colgroup);

                const thead = printDocument.createElement('thead');
                const headerRow = printDocument.createElement('tr');
                snapshot.columns.forEach(column => {
                    const th = printDocument.createElement('th');
                    appendPrintableText(th, column.label);
                    headerRow.appendChild(th);
                });
                thead.appendChild(headerRow);
                table.appendChild(thead);

                const tbody = printDocument.createElement('tbody');
                if (!snapshot.rows.length) {
                    const row = printDocument.createElement('tr');
                    const cell = printDocument.createElement('td');
                    cell.className = 'print-empty';
                    cell.colSpan = Math.max(1, snapshot.columns.length);
                    cell.textContent = 'No data matches the current view.';
                    row.appendChild(cell);
                    tbody.appendChild(row);
                } else {
                    snapshot.rows.forEach(sourceRow => {
                        const row = printDocument.createElement('tr');
                        snapshot.columns.forEach(column => {
                            const cell = printDocument.createElement('td');
                            appendPrintableText(cell, sourceRow.values?.[column.key] ?? '');
                            row.appendChild(cell);
                        });
                        tbody.appendChild(row);
                    });
                }
                table.appendChild(tbody);
                printDocument.body.appendChild(table);
            }

            async function printCurrentView() {
                const snapshot = exporter?.captureCurrentViewSnapshot?.();
                if (!snapshot?.columns?.length) {
                    window.mauiShowToast?.('There are no visible columns to print.', 'error');
                    return;
                }

                const rowCount = snapshot.rows?.length || 0;
                if (rowCount > largeViewConfirmationThreshold) {
                    const confirmed = await window.mauiConfirm?.({
                        title: 'Prepare Large Print View?',
                        message: `This view contains ${rowCount.toLocaleString()} rows. Continue preparing Print View?`,
                        confirmText: 'Continue'
                    });
                    if (!confirmed) return;
                }

                const printWindow = window.open('', '_blank');
                if (!printWindow) {
                    window.mauiShowToast?.('The Print View window was blocked by the browser.', 'error');
                    return;
                }

                try {
                    printWindow.opener = null;
                    renderPrintDocument(printWindow, snapshot);
                    printWindow.focus();

                    let printFinished = false;
                    const closePrintView = () => {
                        if (printFinished) return;
                        printFinished = true;
                        window.setTimeout(() => {
                            try {
                                if (!printWindow.closed) printWindow.close();
                            } catch { }
                        }, 0);
                    };
                    printWindow.addEventListener('afterprint', closePrintView, { once: true });

                    window.setTimeout(() => {
                        try { printWindow.print(); }
                        catch { /* The printer-friendly view remains available if the browser blocks programmatic printing. */ }
                    }, 50);
                } catch (error) {
                    try { printWindow.close(); } catch { }
                    window.mauiShowToast?.(error?.message || 'Print View could not be prepared.', 'error');
                }
            }

            return { printCurrentView };
        }
    };
})();
