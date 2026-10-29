/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Shared live-refresh row renderer for MAUI data grids.
 */

(() => {
    // Purpose: Escape column identifiers used in query-selector attribute expressions.
    function cssEscape(value) {
        return window.CSS?.escape ? window.CSS.escape(value) : String(value).replace(/"/g, '\\"');
    }

    // Purpose: Create a row renderer bound to one grid's stable column and selection configuration.
    function createRenderer({ grid, table, originalColumnOrder, rowSelectionEnabled }) {
        if (!grid || !table) throw new Error('Grid row renderer requires grid and table elements.');
        const columns = Array.isArray(originalColumnOrder) ? originalColumnOrder : [];

        function buildRowFromModel(model) {
            const row = document.createElement('tr');
            const rowId = String(model?.id ?? '');
            const values = model?.values && typeof model.values === 'object' ? model.values : {};
            const rawValues = model?.rawValues && typeof model.rawValues === 'object' ? model.rawValues : {};
            const infoActionUrl = String(model?.infoActionUrl ?? '');
            row.dataset.rowId = rowId;
            const reportGrid = grid.dataset.grid === 'taskruns' || grid.dataset.grid === 'fileactivity' || grid.dataset.grid === 'audit';
            const rowDetailsGrid = reportGrid || grid.dataset.grid === 'upcomingruns';
            row.dataset.rowValues = rowDetailsGrid ? JSON.stringify(values) : '{}';
            if (grid.dataset.grid === 'keysandcertificates' && String(rawValues.IsExpiredCertificate ?? '0') === '1') {
                row.classList.add('row-expired-certificate');
            }

            const selectCell = document.createElement('td');
            selectCell.className = `select-column ${(reportGrid || infoActionUrl) ? 'has-row-info' : ''}`.trim();
            const selectTools = document.createElement('div');
            selectTools.className = 'row-select-tools';
            if (rowSelectionEnabled) {
                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.dataset.rowSelect = '';
                checkbox.value = rowId;
                const explicitlyNotSelectable = String(rawValues.Selectable ?? '1') === '0';
                const builtInScriptSelectionDisabled = grid.dataset.grid === 'scripts' && String(rawValues.IsBuiltIn ?? '0') === '1';
                const nonAdmin = document.documentElement.dataset.moveitAdmin !== 'true';
                const type = grid.dataset.grid;
                const permissionDenied = nonAdmin && ['tasks','hosts','scripts','datelists','globalparameters','keysandcertificates','keysandcerts'].includes(type);
                checkbox.dataset.rowSelectionBlocked = String(explicitlyNotSelectable || builtInScriptSelectionDisabled);
                const selectionDisabled = explicitlyNotSelectable || builtInScriptSelectionDisabled || permissionDenied;
                checkbox.disabled = selectionDisabled;
                checkbox.setAttribute('aria-label', selectionDisabled
                    ? (builtInScriptSelectionDisabled ? 'Built-in Scripts cannot be selected for bulk actions' : 'This row cannot be selected for bulk actions')
                    : 'Select row');
                if (selectionDisabled) checkbox.title = builtInScriptSelectionDisabled
                    ? 'Built-in MOVEit Scripts are read-only and cannot be selected for bulk actions.'
                    : 'This row is not eligible for bulk actions.';
                selectTools.appendChild(checkbox);
            }

            if (grid.dataset.grid === 'taskruns' || grid.dataset.grid === 'fileactivity' || grid.dataset.grid === 'audit') {
                const actionLabel = grid.dataset.grid === 'taskruns'
                    ? 'Run'
                    : grid.dataset.grid === 'fileactivity'
                        ? 'Activity'
                        : 'Audit';
                const actionButton = document.createElement('button');
                actionButton.type = 'button';
                actionButton.className = 'row-actions-button';
                actionButton.dataset.reportRowMenuButton = '';
                actionButton.dataset.rowId = rowId;
                actionButton.title = `${actionLabel} actions`;
                actionButton.setAttribute('aria-label', `${actionLabel} actions`);
                actionButton.setAttribute('aria-haspopup', 'menu');
                actionButton.setAttribute('aria-expanded', 'false');
                actionButton.textContent = '…';
                selectTools.appendChild(actionButton);
            } else if (grid.dataset.grid === 'reports') {
                const reportName = String(values.ReportName ?? rowId);
                const actionButton = document.createElement('button');
                actionButton.type = 'button';
                actionButton.className = 'row-actions-button';
                actionButton.dataset.reportManagementRowMenuButton = '';
                actionButton.dataset.rowId = rowId;
                actionButton.dataset.reportName = reportName;
                actionButton.dataset.reportEnabled = String(rawValues.Enabled ?? '0') === '1' ? 'true' : 'false';
                actionButton.title = `Report actions for ${reportName}`;
                actionButton.setAttribute('aria-label', `Report actions for ${reportName}`);
                actionButton.setAttribute('aria-haspopup', 'menu');
                actionButton.setAttribute('aria-expanded', 'false');
                actionButton.textContent = '…';
                selectTools.appendChild(actionButton);
            } else if (infoActionUrl && grid.dataset.grid === 'tasks') {
                const taskName = String(values.Name ?? rowId);
                const actionButton = document.createElement('button');
                actionButton.type = 'button';
                actionButton.className = 'row-actions-button';
                actionButton.dataset.taskRowMenuButton = '';
                actionButton.dataset.rowId = rowId;
                actionButton.dataset.taskName = taskName;
                actionButton.dataset.taskInfoUrl = infoActionUrl;
                actionButton.dataset.taskEnabled = String(rawValues.Enabled ?? '0') === '1' ? 'true' : 'false';
                actionButton.dataset.taskRunnable = String(rawValues.Runnable ?? '0') === '1' ? 'true' : 'false';
                actionButton.dataset.taskRunning = String(rawValues.Running ?? '0') === '1' ? 'true' : 'false';
                actionButton.dataset.taskHasUndo = String(rawValues.HasUndo ?? '0') === '1' ? 'true' : 'false';
                actionButton.dataset.taskType = String(rawValues.TaskType ?? values.Type ?? '');
                actionButton.title = `Task actions for ${taskName}`;
                actionButton.setAttribute('aria-label', `Task actions for ${taskName}`);
                actionButton.setAttribute('aria-haspopup', 'menu');
                actionButton.setAttribute('aria-expanded', 'false');
                actionButton.textContent = '…';
                selectTools.appendChild(actionButton);
            } else if (grid.dataset.grid === 'upcomingruns') {
                const taskId = String(rawValues.TaskID ?? values.TaskID ?? '').trim();
                const taskName = String(values.TaskName ?? taskId).trim();
                if (taskId) {
                    const actionButton = document.createElement('button');
                    actionButton.type = 'button';
                    actionButton.className = 'row-actions-button';
                    actionButton.dataset.upcomingTaskRowMenuButton = '';
                    actionButton.dataset.taskId = taskId;
                    actionButton.dataset.taskName = taskName;
                    actionButton.dataset.taskInfoUrl = infoActionUrl;
                    actionButton.title = `Task actions for ${taskName}`;
                    actionButton.setAttribute('aria-label', `Task actions for ${taskName}`);
                    actionButton.setAttribute('aria-haspopup', 'menu');
                    actionButton.setAttribute('aria-expanded', 'false');
                    actionButton.textContent = '…';
                    selectTools.appendChild(actionButton);
                }
            } else if (infoActionUrl && grid.dataset.grid === 'hosts') {
                const hostName = String(values.Name ?? rowId);
                const hostType = String(values.Type ?? '');
                const actionButton = document.createElement('button');
                actionButton.type = 'button';
                actionButton.className = 'row-actions-button';
                actionButton.dataset.hostRowMenuButton = '';
                actionButton.dataset.rowId = rowId;
                actionButton.dataset.hostName = hostName;
                actionButton.dataset.hostType = hostType;
                actionButton.dataset.hostTypeCode = String(rawValues.TypeCode ?? '');
                actionButton.dataset.hostDestination = String(values.Destination ?? '');
                actionButton.dataset.hostTestSupported = String(rawValues.TestSupported ?? '0') === '1' ? 'true' : 'false';
                actionButton.dataset.hostInfoUrl = infoActionUrl;
                actionButton.dataset.hostHasUndo = String(rawValues.HasUndo ?? '0') === '1' ? 'true' : 'false';
                actionButton.title = `Host actions for ${hostName}`;
                actionButton.setAttribute('aria-label', `Host actions for ${hostName}`);
                actionButton.setAttribute('aria-haspopup', 'menu');
                actionButton.setAttribute('aria-expanded', 'false');
                actionButton.textContent = '…';
                selectTools.appendChild(actionButton);
            } else if (infoActionUrl && grid.dataset.grid === 'datelists') {
                const dateListName = String(values.Name ?? rowId);
                const actionButton = document.createElement('button');
                actionButton.type = 'button'; actionButton.className = 'row-actions-button';
                actionButton.dataset.dateListRowMenuButton = ''; actionButton.dataset.rowId = rowId; actionButton.dataset.dateListName = dateListName; actionButton.dataset.dateListInfoUrl = infoActionUrl;
                actionButton.dataset.dateListHasUndo = String(rawValues.HasUndo ?? '0') === '1' ? 'true' : 'false';
                actionButton.dataset.dateListCanEditDelete = String(rawValues.CanEditDelete ?? '1') === '1' ? 'true' : 'false';
                actionButton.title = `Date List actions for ${dateListName}`; actionButton.setAttribute('aria-label', `Date List actions for ${dateListName}`); actionButton.setAttribute('aria-haspopup','menu'); actionButton.setAttribute('aria-expanded','false'); actionButton.textContent='…'; selectTools.appendChild(actionButton);
            } else if (infoActionUrl && grid.dataset.grid === 'globalparameters' && document.documentElement.dataset.moveitAdmin === 'true') {
                const parameterName = String(values.Name ?? rowId);
                const actionButton = document.createElement('button');
                actionButton.type = 'button';
                actionButton.className = 'row-actions-button';
                actionButton.dataset.globalParameterRowMenuButton = '';
                actionButton.dataset.rowId = rowId;
                actionButton.dataset.parameterName = parameterName;
                actionButton.dataset.parameterInfoUrl = infoActionUrl;
                actionButton.dataset.parameterSecure = String(rawValues.Secure ?? '0') === '1' ? 'true' : 'false';
                actionButton.title = `Global Parameter actions for ${parameterName}`;
                actionButton.setAttribute('aria-label', `Global Parameter actions for ${parameterName}`);
                actionButton.setAttribute('aria-haspopup', 'menu');
                actionButton.setAttribute('aria-expanded', 'false');
                actionButton.textContent = '…';
                selectTools.appendChild(actionButton);
            } else if (infoActionUrl && grid.dataset.grid === 'keysandcertificates') {
                const itemName = String(values.Name ?? rowId);
                const actionButton = document.createElement('button');
                actionButton.type = 'button';
                actionButton.className = 'row-actions-button';
                actionButton.dataset.keyCertificateRowMenuButton = '';
                actionButton.dataset.rowId = rowId;
                actionButton.dataset.keyCertificateName = itemName;
                actionButton.dataset.keyCertificateType = String(values.Type ?? '');
                const nativeEditorId = String(rawValues.NativeEditorId ?? rawValues.Id ?? '').trim();
                actionButton.dataset.keyCertificateAuditId = nativeEditorId || rowId;
                actionButton.dataset.keyCertificateInfoUrl = infoActionUrl;
                actionButton.title = `Key/Certificate actions for ${itemName}`;
                actionButton.setAttribute('aria-label', `Key/Certificate actions for ${itemName}`);
                actionButton.setAttribute('aria-haspopup', 'menu');
                actionButton.setAttribute('aria-expanded', 'false');
                actionButton.textContent = '…';
                selectTools.appendChild(actionButton);
            } else if (infoActionUrl && grid.dataset.grid === 'scripts') {
                const scriptName = String(values.Name ?? rowId);
                const actionButton = document.createElement('button');
                actionButton.type = 'button';
                actionButton.className = 'row-actions-button';
                actionButton.dataset.scriptRowMenuButton = '';
                actionButton.dataset.rowId = rowId;
                actionButton.dataset.scriptName = scriptName;
                actionButton.dataset.scriptInfoUrl = infoActionUrl;
                actionButton.dataset.scriptHasUndo = String(rawValues.HasUndo ?? '0') === '1' ? 'true' : 'false';
                actionButton.dataset.scriptCanEditDelete = String(rawValues.CanEditDelete ?? '1') === '1' ? 'true' : 'false';
                actionButton.dataset.scriptBuiltIn = String(rawValues.IsBuiltIn ?? '0') === '1' ? 'true' : 'false';
                actionButton.title = `Script actions for ${scriptName}`;
                actionButton.setAttribute('aria-label', `Script actions for ${scriptName}`);
                actionButton.setAttribute('aria-haspopup', 'menu');
                actionButton.setAttribute('aria-expanded', 'false');
                actionButton.textContent = '…';
                selectTools.appendChild(actionButton);
            } else if (infoActionUrl && grid.dataset.grid !== 'globalparameters') {
                const infoButton = document.createElement('button');
                infoButton.type = 'button';
                infoButton.className = 'row-info-button';
                infoButton.dataset.rowInfoUrl = infoActionUrl;
                infoButton.dataset.rowId = rowId;
                infoButton.title = 'Task information';
                infoButton.setAttribute('aria-label', 'Open task information');
                const icon = document.createElement('span');
                icon.className = 'icon';
                icon.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9"></circle><path d="M12 11v6M12 7h.01"></path></svg>';
                infoButton.appendChild(icon);
                selectTools.appendChild(infoButton);
            }

            selectCell.appendChild(selectTools);
            row.appendChild(selectCell);

            columns.forEach(key => {
                const header = table.querySelector(`.column-header-row th[data-column="${cssEscape(key)}"]`);
                if (!header) return;

                const cell = document.createElement('td');
                cell.dataset.column = key;
                const value = String(values[key] ?? '');
                const rawValue = String(rawValues[key] ?? value);
                cell.dataset.value = rawValue;

                const type = header.dataset.type || 'text';
                if (type === 'status') {
                    const status = document.createElement('span');
                    status.className = `status-pill status-${value.toLowerCase().replaceAll(' ', '-')}`;
                    if ((value.toLowerCase() === 'failed' || value.toLowerCase() === 'failure') && values.StatusMessage) {
                        status.title = String(values.StatusMessage ?? '');
                    } else if (grid.dataset.grid === 'tasks') {
                        status.title = String(values.LastResult ?? '');
                    } else if (grid.dataset.grid === 'hosts' && key === 'Health') {
                        status.title = String(values.HealthDetail ?? '');
                    }
                    status.appendChild(document.createElement('span'));
                    status.appendChild(document.createTextNode(value));
                    cell.appendChild(status);
                } else if (type === 'boolean') {
                    const booleanValue = document.createElement('span');
                    booleanValue.className = `boolean-value ${value === 'Yes' ? 'is-yes' : 'is-no'}`;
                    booleanValue.textContent = value;
                    cell.appendChild(booleanValue);
                } else if (type === 'tags') {
                    const tagList = document.createElement('div');
                    tagList.className = 'metadata-tag-cell';
                    cell.appendChild(tagList);
                    if (window.mauiRenderTagPills) void window.mauiRenderTagPills(tagList, value);
                    else tagList.textContent = value;
                } else if (key === 'TaskName' && (grid.dataset.grid === 'taskruns' || grid.dataset.grid === 'fileactivity' || grid.dataset.grid === 'audit' || grid.dataset.grid === 'upcomingruns')) {
                    const taskId = String(rawValues.TaskID ?? values.TaskID ?? '').trim();
                    if (taskId) {
                        const button = document.createElement('button');
                        button.type = 'button';
                        button.className = 'row-primary-link';
                        button.dataset.nativeTaskEditor = '';
                        button.dataset.rowId = taskId;
                        button.dataset.taskName = value;
                        button.title = `Open ${value} in MOVEit`;
                        button.textContent = value;
                        cell.appendChild(button);
                    } else {
                        const text = document.createElement('span');
                        text.title = value;
                        text.textContent = value;
                        cell.appendChild(text);
                    }
                } else if (key === 'ReportName' && grid.dataset.grid === 'reports') {
                    const editUrl = String(rawValues.ReportEditViewUrl ?? '').trim();
                    if (editUrl) {
                        const link = document.createElement('a');
                        link.className = 'row-primary-link';
                        link.href = editUrl;
                        link.title = `Edit Report view for ${value}`;
                        link.textContent = value;
                        cell.appendChild(link);
                    } else {
                        const text = document.createElement('span');
                        text.textContent = value;
                        cell.appendChild(text);
                    }
                } else if (key === 'Name' && grid.dataset.grid === 'tasks') {
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'row-primary-link';
                    button.dataset.nativeTaskEditor = '';
                    button.dataset.rowId = rowId;
                    button.dataset.taskName = value;
                    button.title = String(values.Description ?? '');
                    button.textContent = value;
                    cell.appendChild(button);
                } else if (key === 'Name' && grid.dataset.grid === 'hosts') {
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'row-primary-link';
                    button.dataset.nativeHostEditor = '';
                    button.dataset.rowId = rowId;
                    button.dataset.hostName = value;
                    button.title = String(values.Description ?? '');
                    button.textContent = value;
                    cell.appendChild(button);
                } else if (key === 'Name' && grid.dataset.grid === 'datelists') {
                    const button = document.createElement('button'); button.type='button'; button.className='row-primary-link'; button.dataset.nativeDateListEditor=''; button.dataset.rowId=rowId; button.dataset.dateListName=value; button.title=String(values.Description ?? ''); button.textContent=value; cell.appendChild(button);
                } else if (key === 'Name' && grid.dataset.grid === 'globalparameters' && document.documentElement.dataset.moveitAdmin !== 'true') {
                    const text = document.createElement('span'); text.textContent = value; text.title = value; cell.appendChild(text);
                } else if (key === 'Name' && grid.dataset.grid === 'globalparameters') {
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'row-primary-link';
                    button.dataset.globalParameterInformation = '';
                    button.dataset.rowId = rowId;
                    button.dataset.parameterName = value;
                    button.dataset.parameterInfoUrl = infoActionUrl;
                    button.textContent = value;
                    cell.appendChild(button);
                } else if (key === 'Name' && grid.dataset.grid === 'keysandcertificates') {
                    const text = document.createElement('span');
                    text.title = value;
                    text.textContent = value;
                    cell.appendChild(text);
                } else if (key === 'Name' && grid.dataset.grid === 'scripts') {
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'row-primary-link';
                    button.dataset.nativeScriptEditor = '';
                    button.dataset.rowId = rowId;
                    button.dataset.scriptName = value;
                    button.dataset.scriptBuiltIn = String(rawValues.IsBuiltIn ?? '0') === '1' ? 'true' : 'false';
                    button.title = String(values.Description ?? '');
                    button.textContent = value;
                    cell.appendChild(button);
                } else if (key === 'Name') {
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'row-primary-link';
                    button.dataset.gridAction = `Open ${String(grid.dataset.grid || 'record')}`;
                    button.textContent = value;
                    cell.appendChild(button);
                } else {
                    const text = document.createElement('span');
                    text.title = value;
                    text.textContent = value;
                    cell.appendChild(text);
                }

                row.appendChild(cell);
            });

            return row;
        }

        return Object.freeze({ buildRowFromModel });
    }

    window.MauiGridRows = Object.freeze({ createRenderer });
})();
