/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Shared Task Information dialog behavior used by Tasks and Upcoming Runs.
 */

(() => {
    function initialize(options = {}) {
        const dialog = document.querySelector('[data-task-details-dialog]');
        if (!dialog) return null;

        const config = options.configElement || document.querySelector('[data-task-page-config]') || document.querySelector('[data-upcoming-runs-config]');
        if (!config) return null;

        const antiForgeryToken = options.antiForgeryToken || '';
        const onStatus = typeof options.onStatus === 'function' ? options.onStatus : () => {};
        const onSaved = typeof options.onSaved === 'function' ? options.onSaved : async () => {};

        const detailsLoading = dialog.querySelector('[data-task-details-loading]');
        const detailsContent = dialog.querySelector('[data-task-details-content]');
        const detailsError = dialog.querySelector('[data-task-details-error]');
        const detailsSave = dialog.querySelector('[data-task-details-save]');
        const detailsCancel = dialog.querySelector('[data-task-details-cancel]');
        const detailsTabs = Array.from(dialog.querySelectorAll('[data-task-details-tab]'));
        const informationPanel = dialog.querySelector('[data-task-details-panel="information"]');
        const tasksPanel = dialog.querySelector('[data-task-details-panel="tasks"]');
        const logsPanel = dialog.querySelector('[data-task-details-panel="logs"]');
        const taskDependenciesLoading = dialog.querySelector('[data-task-dependencies-loading]');
        const taskDependenciesError = dialog.querySelector('[data-task-dependencies-error]');
        const taskCalledByRows = dialog.querySelector('[data-task-called-by-rows]');
        const taskCalledByList = dialog.querySelector('[data-task-called-by-list]');
        const taskCalledByEmpty = dialog.querySelector('[data-task-called-by-empty]');
        const taskCallsRows = dialog.querySelector('[data-task-calls-rows]');
        const taskCallsList = dialog.querySelector('[data-task-calls-list]');
        const taskCallsEmpty = dialog.querySelector('[data-task-calls-empty]');
        const taskLogsLoading = dialog.querySelector('[data-task-logs-loading]');
        const taskLogsError = dialog.querySelector('[data-task-logs-error]');
        const taskLogsEmpty = dialog.querySelector('[data-task-logs-empty]');
        const taskLogsList = dialog.querySelector('[data-task-logs-list]');
        const taskLogsRows = dialog.querySelector('[data-task-logs-rows]');
        const metadataFields = dialog.querySelector('[data-metadata-fields]');
        const metadataAudit = dialog.querySelector('[data-metadata-audit]');
        const folderSelect = dialog.querySelector('[data-task-folder]');
        const detailsFolderTree = dialog.querySelector('[data-task-folder-tree]');
        const detailsFolderButton = dialog.querySelector('[data-task-folder-button]');
        const detailsFolderButtonIcon = dialog.querySelector('[data-task-folder-button-icon]');
        const detailsFolderLabel = dialog.querySelector('[data-task-folder-label]');
        const detailsFolderMenu = dialog.querySelector('[data-task-folder-menu]');

        let activeTaskId = '';
        let activeInfoUrl = '';
        let activeTaskName = '';
        let canEditMoveItFields = false;
        let saveOutcomeUncertain = false;
        let taskLogsRequest = null;
        let taskLogsGeneration = 0;
        let taskDependenciesLoaded = false;
        const detailsReadiness = window.MauiModalUI.registerSaveAction(dialog, detailsSave, () => {
            const name = dialog.querySelector('[data-task-name]');
            return !canEditMoveItFields || name.value.trim().length > 0 && name.value.trim().length <= 254;
        });


        function endpoint(name) {
            return config.dataset[name] || '';
        }

        async function fetchJson(url, requestOptions = {}) {
            const headers = new Headers(requestOptions.headers || {});
            headers.set('Accept', 'application/json');
            if (requestOptions.body) headers.set('Content-Type', 'application/json');
            if (antiForgeryToken && requestOptions.method && requestOptions.method.toUpperCase() !== 'GET') {
                headers.set('RequestVerificationToken', antiForgeryToken);
            }

            const response = await fetch(url, { ...requestOptions, headers, cache: requestOptions.cache || 'no-store' });
            if (response.status === 401) {
                window.location.replace(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
                throw new Error('MOVEit session expired.');
            }
            if (response.redirected && /\/Account\/(Login|SessionExpired)/i.test(new URL(response.url, window.location.href).pathname)) {
                window.location.replace(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
                throw new Error('MOVEit session expired.');
            }

            const contentType = response.headers.get('content-type') || '';
            const payload = contentType.includes('application/json') ? await response.json() : null;
            if (!response.ok) {
                const error = new Error(payload?.message || `Request failed (${response.status}).`);
                error.status = response.status; error.payload = payload; throw error;
            }
            return payload;
        }

        async function open(taskId, infoUrl) {
            activeTaskId = String(taskId || '').trim();
            const detailsUrl = String(infoUrl || '').trim() || `${endpoint('taskDetailsUrl')}?id=${encodeURIComponent(activeTaskId)}`;
            if (!activeTaskId || !detailsUrl) return;

            activeInfoUrl = detailsUrl;
            reset();
            window.MauiObjectAccess?.clearInformation(dialog);
            dialog.showModal();
            try {
                const data = await fetchJson(detailsUrl);
                populate(data);
                await window.MauiObjectAccess?.information(dialog,'Task',activeTaskId);
                window.MauiModalUI?.track(dialog);
            } catch (error) {
                showError(error.message);
            }
        }

        function reset() {
            detailsReadiness.reset();
            saveOutcomeUncertain = false;
            detailsLoading.hidden = false;
            detailsContent.hidden = true;
            detailsError.hidden = true;
            detailsError.textContent = '';
            detailsSave.disabled = true;
            detailsSave.hidden = false;

            activeTaskName = '';
            taskLogsRequest?.abort(); taskLogsRequest = null; taskLogsGeneration++;
            taskDependenciesLoaded = false;
            metadataFields.replaceChildren();
            metadataAudit.replaceChildren();
            resetFolderSelector();
            taskLogsRows?.replaceChildren();
            taskCalledByRows?.replaceChildren();
            taskCallsRows?.replaceChildren();
            if (taskDependenciesLoading) taskDependenciesLoading.hidden = true;
            if (taskDependenciesError) { taskDependenciesError.hidden = true; taskDependenciesError.textContent = ''; }
            if (taskCalledByEmpty) taskCalledByEmpty.hidden = true;
            if (taskCalledByList) taskCalledByList.hidden = true;
            if (taskCallsEmpty) taskCallsEmpty.hidden = true;
            if (taskCallsList) taskCallsList.hidden = true;
            if (taskLogsLoading) taskLogsLoading.hidden = true;
            if (taskLogsError) { taskLogsError.hidden = true; taskLogsError.textContent = ''; }
            if (taskLogsEmpty) taskLogsEmpty.hidden = true;
            if (taskLogsList) taskLogsList.hidden = true;
            setTab('information', false);
            detailsTabs.forEach(tab => { tab.disabled = true; });
        }

        function populate(data) {
            dialog.dataset.editVersion = String(data.editVersion || '');
            const task = data.task || {};
            canEditMoveItFields = Boolean(task.canEditMoveItFields);
            activeTaskName = task.name || '';
            dialog.querySelector('[data-task-name]').value = activeTaskName;
            dialog.querySelector('[data-task-description]').value = task.description || '';
            dialog.querySelector('[data-task-operator-notes]').value = task.operatorNotes || '';
            dialog.querySelector('[data-task-resource-groups]').value = task.resourceGroups || '—';
            dialog.querySelector('[data-task-type]').textContent = task.type || '—';
            dialog.querySelector('[data-task-id]').textContent = task.id || activeTaskId;

            ['[data-task-name]', '[data-task-description]', '[data-task-operator-notes]'].forEach(selector => {
                dialog.querySelector(selector).disabled = !canEditMoveItFields;
            });

            const editNote = dialog.querySelector('[data-task-edit-note]');
            editNote.hidden = canEditMoveItFields;
            editNote.textContent = canEditMoveItFields
                ? ''
                : 'Your MOVEit permissions do not allow this Task to be edited.';

            populateFolderSelect(data.folders || [], data.folderId);
            populateMetadataFields(data.fields || [], data.metadata || {});
            populateAudit(data.audit || {});
            detailsLoading.hidden = true;
            detailsContent.hidden = false;
            detailsSave.disabled = false;
            detailsTabs.forEach(tab => { tab.disabled = false; });
        }

        function setTab(tabName, loadLazyContent = true) {
            const showTasks = tabName === 'tasks';
            const showLogs = tabName === 'logs';
            const showInformation = !showTasks && !showLogs;
            detailsTabs.forEach(tab => {
                const selected = tab.dataset.taskDetailsTab === tabName;
                tab.classList.toggle('active', selected);
                tab.setAttribute('aria-selected', selected ? 'true' : 'false');
            });

            if (informationPanel) informationPanel.hidden = !showInformation;
            if (tasksPanel) tasksPanel.hidden = !showTasks;
            if (logsPanel) logsPanel.hidden = !showLogs;
            if (detailsSave) detailsSave.hidden = !showInformation;

            if (showTasks && loadLazyContent && !taskDependenciesLoaded) loadDependencies();
            if (showLogs && loadLazyContent) loadLogs();
        }

        async function loadDependencies() {
            if (!activeTaskId || taskDependenciesLoaded) return;
            if (taskDependenciesLoading) taskDependenciesLoading.hidden = false;
            if (taskDependenciesError) { taskDependenciesError.hidden = true; taskDependenciesError.textContent = ''; }
            if (taskCalledByEmpty) taskCalledByEmpty.hidden = true;
            if (taskCalledByList) taskCalledByList.hidden = true;
            if (taskCallsEmpty) taskCallsEmpty.hidden = true;
            if (taskCallsList) taskCallsList.hidden = true;
            taskCalledByRows?.replaceChildren();
            taskCallsRows?.replaceChildren();

            try {
                const data = await fetchJson(`${endpoint('taskDependenciesUrl')}?id=${encodeURIComponent(activeTaskId)}`);
                renderDependencyList(data.calledBy || [], taskCalledByRows, taskCalledByList, taskCalledByEmpty);
                renderDependencyList(data.calls || [], taskCallsRows, taskCallsList, taskCallsEmpty);
                taskDependenciesLoaded = true;
            } catch (error) {
                if (taskDependenciesError) {
                    taskDependenciesError.textContent = error.message || 'Unable to load Task relationships.';
                    taskDependenciesError.hidden = false;
                }
            } finally {
                if (taskDependenciesLoading) taskDependenciesLoading.hidden = true;
            }
        }

        function renderDependencyList(tasks, rowsHost, listHost, emptyHost) {
            rowsHost?.replaceChildren();
            if (!Array.isArray(tasks) || tasks.length === 0) {
                if (emptyHost) emptyHost.hidden = false;
                if (listHost) listHost.hidden = true;
                return;
            }

            const fragment = document.createDocumentFragment();
            tasks.forEach(task => {
                const row = document.createElement('div');
                row.className = 'host-task-row';
                row.setAttribute('role', 'row');

                const id = document.createElement('div');
                id.setAttribute('role', 'cell');
                id.textContent = task.id || '';

                const name = document.createElement('div');
                name.setAttribute('role', 'cell');
                const link = document.createElement('button');
                link.type = 'button';
                link.className = 'row-primary-link';
                link.textContent = task.name || task.id || 'Task';
                link.title = 'Open Task Editor';
                link.addEventListener('click', async () => {
                    try { await window.MauiEditorRouting.openTask(task.id, task.name || '', task.type || ''); }
                    catch (error) { showError(error?.message || 'Unable to open the Task Editor.'); }
                });
                name.appendChild(link);

                row.append(id, name);
                fragment.appendChild(row);
            });

            rowsHost?.appendChild(fragment);
            if (emptyHost) emptyHost.hidden = true;
            if (listHost) listHost.hidden = false;
        }

        async function loadLogs() {
            if (!activeTaskId) return;
            taskLogsRequest?.abort();
            const request = new AbortController(), owner = ++taskLogsGeneration, taskId = activeTaskId;
            taskLogsRequest = request;
            if (taskLogsLoading) taskLogsLoading.hidden = false;
            if (taskLogsError) { taskLogsError.hidden = true; taskLogsError.textContent = ''; }
            if (taskLogsEmpty) taskLogsEmpty.hidden = true;
            if (taskLogsList) taskLogsList.hidden = true;
            taskLogsRows?.replaceChildren();

            try {
                const data = await fetchJson(`${endpoint('taskLogsUrl')}?id=${encodeURIComponent(taskId)}`, { signal: request.signal, cache: 'no-store' });
                if (owner !== taskLogsGeneration || taskId !== activeTaskId || !dialog.open) return;
                renderLogs(data?.logs || []);
            } catch (error) {
                if (owner !== taskLogsGeneration || request.signal.aborted || taskId !== activeTaskId || !dialog.open) return;
                if (taskLogsError) { taskLogsError.textContent = error.message; taskLogsError.hidden = false; }
            } finally {
                if (owner === taskLogsGeneration) { taskLogsRequest = null; if (taskLogsLoading) taskLogsLoading.hidden = true; }
            }
        }

        function renderLogs(logs) {
            taskLogsRows?.replaceChildren();
            if (!Array.isArray(logs) || logs.length === 0) {
                if (taskLogsEmpty) taskLogsEmpty.hidden = false;
                if (taskLogsList) taskLogsList.hidden = true;
                return;
            }

            const fragment = document.createDocumentFragment();
            logs.forEach(log => {
                const row = document.createElement('div');
                row.className = 'task-log-row';
                row.setAttribute('role', 'row');

                const resultCell = document.createElement('div');
                resultCell.className = 'task-log-result-cell';
                resultCell.setAttribute('role', 'cell');
                const indicator = document.createElement('span');
                const status = classifyLogStatus(log.exitStatus);
                indicator.className = `task-log-status ${status.kind}`;
                indicator.textContent = status.symbol;
                indicator.title = log.exitStatus || status.label;
                indicator.setAttribute('aria-label', log.exitStatus || status.label);
                resultCell.appendChild(indicator);

                const logCell = document.createElement('div');
                logCell.className = 'task-log-name-cell';
                logCell.setAttribute('role', 'cell');
                const link = document.createElement('button');
                link.type = 'button';
                link.className = 'task-log-link';
                link.textContent = formatLogName(log);
                link.title = 'Open task log';
                link.addEventListener('click', () => openLogViewer(log));
                logCell.appendChild(link);

                row.append(resultCell, logCell);
                fragment.appendChild(row);
            });

            taskLogsRows?.appendChild(fragment);
            if (taskLogsEmpty) taskLogsEmpty.hidden = true;
            if (taskLogsList) taskLogsList.hidden = false;
        }

        function classifyLogStatus(exitStatus) {
            const value = String(exitStatus || '').trim().toLowerCase();
            if (value.includes('success')) return { kind: 'success', symbol: '✓', label: 'Success' };
            if (value.includes('fail') || value.includes('error')) return { kind: 'failure', symbol: '!', label: 'Failure' };
            if (value.includes('no xfer') || value.includes('noxfer') || value.includes('no transfer')) return { kind: 'neutral', symbol: '−', label: 'No Transfers' };
            return { kind: 'neutral', symbol: '•', label: exitStatus || 'Unknown' };
        }

        function formatLogName(log) {
            if (log?.startTime) {
                const date = new Date(log.startTime);
                if (!Number.isNaN(date.valueOf())) return date.toLocaleString();
                return String(log.startTime);
            }
            const fallbackName = String(log?.fallbackName || '').trim();
            if (fallbackName) return fallbackName;
            return `Task Log ${log?.id || ''}`.trim();
        }

        function openLogViewer(log) {
            if (!activeTaskId || !log?.id) return;
            const baseUrl = endpoint('taskLogViewerUrl');
            if (!baseUrl) return;
            const separator = baseUrl.includes('?') ? '&' : '?';
            const url = `${baseUrl}${separator}id=${encodeURIComponent(activeTaskId)}&logId=${encodeURIComponent(log.id)}`;
            const currentWidth = window.outerWidth || window.screen.availWidth || 1200;
            const currentHeight = window.outerHeight || window.screen.availHeight || 800;
            const width = Math.max(760, currentWidth - 120);
            const height = Math.max(560, currentHeight - 120);
            const currentLeft = typeof window.screenX === 'number' ? window.screenX : (window.screenLeft || 0);
            const currentTop = typeof window.screenY === 'number' ? window.screenY : (window.screenTop || 0);
            const left = Math.max(0, Math.round(currentLeft + ((currentWidth - width) / 2)));
            const top = Math.max(0, Math.round(currentTop + ((currentHeight - height) / 2)));
            const popup = window.open(url, `MauiTaskLog_${activeTaskId}_${log.id}`, [
                'popup=yes', 'resizable=yes', 'scrollbars=yes', `width=${Math.round(width)}`, `height=${Math.round(height)}`, `left=${left}`, `top=${top}`
            ].join(','));
            if (!popup && taskLogsError) {
                taskLogsError.textContent = 'The Task Log viewer popup was blocked by the browser.';
                taskLogsError.hidden = false;
            }
        }

        function normalizeFolderId(value) {
            if (value === null || value === undefined || value === '') return null;
            const numeric = Number(value);
            return Number.isFinite(numeric) ? numeric : null;
        }

        function populateFolderSelect(folders, selectedFolderId) {
            if (!folderSelect || !detailsFolderMenu) return;
            const canManageFolders = document.documentElement.dataset.moveitAdmin === 'true';
            folderSelect.disabled = !canManageFolders;
            if (detailsFolderButton) {
                detailsFolderButton.disabled = !canManageFolders;
                detailsFolderButton.title = canManageFolders ? '' : 'Only MOVEit administrators can change Task folder assignments.';
            }
            const normalizedSelectedId = normalizeFolderId(selectedFolderId);
            const fragment = document.createDocumentFragment();
            fragment.appendChild(createFolderOption({ id: null, name: 'All Tasks', depth: 0, isRoot: true, selected: normalizedSelectedId == null }));
            folders.forEach(folder => {
                const folderId = normalizeFolderId(folder.id);
                fragment.appendChild(createFolderOption({
                    id: folderId,
                    name: folder.name || folder.displayName || `Folder ${folderId}`,
                    depth: Number.isFinite(Number(folder.depth)) ? Number(folder.depth) : 0,
                    isRoot: false,
                    selected: normalizedSelectedId != null && Number(normalizedSelectedId) === Number(folderId)
                }));
            });
            detailsFolderMenu.replaceChildren(fragment);
            const selectedFolder = normalizedSelectedId == null ? null : folders.find(folder => Number(folder.id) === Number(normalizedSelectedId));
            setFolderSelection(normalizedSelectedId, selectedFolder?.displayName || selectedFolder?.name || 'All Tasks', normalizedSelectedId != null, false);
        }

        function createFolderOption({ id, name, depth, isRoot, selected }) {
            const option = document.createElement('button');
            option.type = 'button';
            option.setAttribute('role', 'menuitemradio');
            option.setAttribute('aria-checked', selected ? 'true' : 'false');
            option.className = `task-folder-tree-option${isRoot ? ' root-option' : ''}${selected ? ' selected' : ''}`;
            option.dataset.taskFolderOption = id == null ? '' : String(id);
            option.disabled = document.documentElement.dataset.moveitAdmin !== 'true';
            option.style.setProperty('--folder-depth', String(Math.max(0, depth || 0)));
            if (!isRoot) {
                const branch = document.createElement('span');
                branch.className = 'task-folder-tree-branch';
                branch.setAttribute('aria-hidden', 'true');
                option.appendChild(branch);
            }
            const icon = document.createElement('span');
            icon.className = 'task-folder-tree-option-icon icon';
            icon.appendChild(createFolderIcon(!isRoot));
            const label = document.createElement('span');
            label.className = 'task-folder-tree-option-label';
            label.textContent = name;
            option.append(icon, label);
            option.addEventListener('click', () => setFolderSelection(id, name, !isRoot, true));
            return option;
        }

        function setFolderSelection(folderId, label, isFolder, closeMenu) {
            if (!folderSelect) return;
            folderSelect.value = folderId == null ? '' : String(folderId);
            if (detailsFolderLabel) {
                detailsFolderLabel.textContent = label || 'All Tasks';
                detailsFolderLabel.title = label || 'All Tasks';
            }
            if (detailsFolderButtonIcon) detailsFolderButtonIcon.replaceChildren(createFolderIcon(isFolder));
            detailsFolderMenu?.querySelectorAll('[data-task-folder-option]').forEach(option => {
                const optionId = normalizeFolderId(option.dataset.taskFolderOption);
                const selected = optionId == null ? folderId == null : folderId != null && Number(optionId) === Number(folderId);
                option.classList.toggle('selected', selected);
                option.setAttribute('aria-checked', selected ? 'true' : 'false');
            });
            if (closeMenu) { closeFolderSelector(); detailsReadiness.update(); }
        }

        function resetFolderSelector() {
            if (folderSelect) folderSelect.value = '';
            detailsFolderMenu?.replaceChildren();
            if (detailsFolderLabel) { detailsFolderLabel.textContent = 'All Tasks'; detailsFolderLabel.title = 'All Tasks'; }
            if (detailsFolderButtonIcon) detailsFolderButtonIcon.replaceChildren(createFolderIcon(false));
            closeFolderSelector();
        }

        function toggleFolderSelector() {
            if (!detailsFolderTree || !detailsFolderButton) return;
            if (detailsFolderButton.disabled || document.documentElement.dataset.moveitAdmin !== 'true') return;
            const open = !detailsFolderTree.classList.contains('open');
            detailsFolderTree.classList.toggle('open', open);
            detailsFolderButton.setAttribute('aria-expanded', open ? 'true' : 'false');
        }

        function closeFolderSelector() {
            detailsFolderTree?.classList.remove('open');
            detailsFolderButton?.setAttribute('aria-expanded', 'false');
        }

        function createFolderIcon(isFolder) {
            const svgNamespace = 'http://www.w3.org/2000/svg';
            const svg = document.createElementNS(svgNamespace, 'svg');
            svg.setAttribute('viewBox', '0 0 24 24');
            svg.setAttribute('aria-hidden', 'true');

            const addPath = data => {
                const path = document.createElementNS(svgNamespace, 'path');
                path.setAttribute('d', data);
                svg.appendChild(path);
            };

            if (isFolder) {
                addPath('M3 6.5a2 2 0 0 1 2-2h5l2 2h7a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-11Z');
            } else {
                addPath('M9 6h12M9 12h12M9 18h12');
                addPath('m3 6 1 1 2-2M3 12l1 1 2-2M3 18l1 1 2-2');
            }

            return svg;
        }

        function populateMetadataFields(fields, values) {
            const fragment = document.createDocumentFragment();
            fields.forEach(field => {
                const label = document.createElement('label');
                label.className = `field-block ${field.type?.toLowerCase().includes('multiline') ? 'full-span' : ''}`;
                const caption = document.createElement('span');
                caption.textContent = field.label || field.name;
                if (field.description) caption.title = field.description;
                label.appendChild(caption);
                const control = createMetadataControl(field, values[field.name] ?? '');
                window.mauiConfigureMetadataControl?.(control, field);
                control.dataset.metadataField = field.name;
                control.required = Boolean(field.required);
                label.appendChild(control);
                fragment.appendChild(label);
            });
            metadataFields.replaceChildren(fragment);
        }

        function createMetadataControl(field, value) {
            const type = String(field.type || 'String').toLowerCase();
            if (type === 'multilinestring' || type === 'multiline') {
                const textarea = document.createElement('textarea');
                textarea.className = 'text-input textarea-input';
                textarea.rows = 4;
                textarea.value = value;
                return textarea;
            }
            if (type === 'boolean') {
                const select = document.createElement('select');
                select.className = 'text-input';
                select.append(new Option('False', 'false'), new Option('True', 'true'));
                select.value = String(value).toLowerCase() === 'true' ? 'true' : 'false';
                return select;
            }
            if (type === 'list') {
                const select = document.createElement('select');
                select.className = 'text-input';
                (field.possibleValues || []).forEach(optionValue => select.appendChild(new Option(optionValue, optionValue)));
                select.value = value;
                return select;
            }
            const input = document.createElement('input');
            input.className = 'text-input';
            input.type = type === 'numeric' || type === 'number' ? 'number' : type === 'date' ? 'date' : 'text';
            input.value = value;
            return input;
        }

        function populateAudit(audit) {
            const rows = [
                ['Created On', formatDate(audit.createdUtc)],
                ['Created By', audit.createdBy || '—'],
                ['Modified On', formatDate(audit.modifiedUtc)],
                ['Modified By', audit.modifiedBy || '—']
            ];
            const fragment = document.createDocumentFragment();
            rows.forEach(([labelText, valueText]) => {
                const item = document.createElement('label');
                item.className = 'audit-item';
                const label = document.createElement('span');
                label.textContent = labelText;
                const value = document.createElement('input'); value.type = 'text'; value.className = 'text-input'; value.readOnly = true;
                value.value = valueText || '—';
                item.append(label, value);
                fragment.appendChild(item);
            });
            metadataAudit.replaceChildren(fragment);
        }

        async function save() {
            if (!activeTaskId || saveOutcomeUncertain || detailsSave.disabled) return;
            hideError();
            const taskNameInput = dialog.querySelector('[data-task-name]');
            const taskName = String(taskNameInput?.value || '').trim();
            if (canEditMoveItFields && !taskName) { showError('Task Name is required.'); taskNameInput?.focus(); return; }
            if (canEditMoveItFields && taskName.length > 254) { showError('Task Name cannot exceed 254 characters.'); taskNameInput?.focus(); return; }
            if (window.mauiValidateMetadataControls && !window.mauiValidateMetadataControls(metadataFields)) return;
            detailsReadiness.busy(true);

            const metadata = {};
            metadataFields.querySelectorAll('[data-metadata-field]').forEach(control => { metadata[control.dataset.metadataField] = control.value ?? ''; });
            const payload = {
                editVersion: dialog.dataset.editVersion || '',
                name: taskName,
                description: dialog.querySelector('[data-task-description]').value,
                operatorNotes: dialog.querySelector('[data-task-operator-notes]').value,
                updateMoveItFields: canEditMoveItFields,
                folderId: folderSelect.value ? Number(folderSelect.value) : null,
                metadata
            };

            try {
                await fetchJson(`${endpoint('saveDetailsUrl')}?id=${encodeURIComponent(activeTaskId)}`, { method: 'POST', body: JSON.stringify(payload) });
                dialog.close();
                onStatus('Task information saved.', 5000, 'default');
                await onSaved({ taskId: activeTaskId, taskName });
            } catch (error) {
                saveOutcomeUncertain = Boolean(error?.payload?.editConflict || error?.payload?.moveItSaved || !error?.status || error.status >= 500);
                showError((error?.message || 'Unable to save Task Information.')
                    + (saveOutcomeUncertain ? ' Close and reopen Task Information before saving again.' : ''));
                detailsReadiness.block(saveOutcomeUncertain); detailsReadiness.busy(false);
            }
        }

        function showError(message) {
            detailsLoading.hidden = true;
            detailsError.textContent = message || 'Unable to load Task Information.';
            detailsError.hidden = false;
        }

        function hideError() {
            detailsError.hidden = true;
            detailsError.textContent = '';
        }

        function formatDate(value) {
            if (!value) return '—';
            const date = new Date(value);
            return Number.isNaN(date.valueOf()) ? '—' : date.toLocaleString();
        }

        detailsSave?.addEventListener('click', save);
        detailsCancel?.addEventListener('click', () => dialog.close());
        dialog.addEventListener('close', () => { taskLogsRequest?.abort(); taskLogsRequest = null; taskLogsGeneration++; });
        detailsFolderButton?.addEventListener('click', event => {
            event.preventDefault();
            event.stopPropagation();
            toggleFolderSelector();
        });
        document.addEventListener('click', event => {
            if (detailsFolderTree && !event.target.closest('[data-task-folder-tree]')) closeFolderSelector();
        });
        detailsTabs.forEach(tab => {
            tab.addEventListener('click', () => {
                if (tab.disabled) return;
                setTab(tab.dataset.taskDetailsTab || 'information');
            });
        });

        window.MauiModalUI?.registerContext('task-information', dialog, {
            capture: () => ({ id: activeTaskId, url: activeInfoUrl, tab: detailsTabs.find(tab => tab.classList.contains('active'))?.dataset.taskDetailsTab }),
            restore: async state => { const url = window.MauiModalUI.safeUrl(state.url); if (url) { await open(state.id, url); setTab(state.tab || 'information'); } }
        });
        return { open };
    }

    window.MauiTaskInformation = { initialize };
})();
