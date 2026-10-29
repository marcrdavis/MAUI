// MAUI Host Editor - isolated editor feature with common Host Information persistence
// and profile-gated protocol controls backed by the shared Host CRUD layer.
(() => {
    'use strict';

    function initialize(options = {}) {
        const dialog = document.querySelector('[data-host-editor-dialog]');
        if (!dialog) return null;

        const shellUrl = String(options.shellUrl || '').trim();
        const fingerprintUrl = String(options.fingerprintUrl || '').trim();
        const certificatesUrl = String(options.certificatesUrl || '').trim();
        const documentLibrariesUrl = String(options.documentLibrariesUrl || '').trim();
        const picker = document.querySelector('[data-host-editor-picker]');
        let pickerMode = '', pickerItems = [], pickerSelection = -1, pickerSequence = 0, pickerBusy = false;
        let algorithmOrders = Object.create(null);
        const saveUrl = String(options.saveUrl || '').trim();
        const loginUrl = String(options.loginUrl || window.mauiAppUrl('/Account/SessionExpired')).trim();
        const antiForgeryToken = String(options.antiForgeryToken || '').trim();
        const onTestHost = typeof options.onTestHost === 'function' ? options.onTestHost : null;
        const onSaved = typeof options.onSaved === 'function' ? options.onSaved : null;
        const onExportHost = typeof options.onExportHost === 'function' ? options.onExportHost : null;
        const onDeleteHost = typeof options.onDeleteHost === 'function' ? options.onDeleteHost : null;
        const onCloneHost = typeof options.onCloneHost === 'function' ? options.onCloneHost : null;
        const onClosed = typeof options.onClosed === 'function' ? options.onClosed : null;

        const title = dialog.querySelector('[data-host-editor-title]');
        const subtitle = dialog.querySelector('[data-host-editor-subtitle]');
        const type = dialog.querySelector('[data-host-editor-type]');
        const id = dialog.querySelector('[data-host-editor-id]');
        const icon = dialog.querySelector('[data-host-editor-icon]');
        const loading = dialog.querySelector('[data-host-editor-loading]');
        const content = dialog.querySelector('[data-host-editor-content]');
        const error = dialog.querySelector('[data-host-editor-error]');
        const dirtyState = dialog.querySelector('[data-host-editor-dirty-state]');
        const informationFields = dialog.querySelector('[data-host-editor-information-fields]');
        const metadata = dialog.querySelector('[data-host-editor-metadata]');
        const configTabs = dialog.querySelector('[data-host-editor-config-tabs]');
        const settingsIcon = dialog.querySelector('[data-host-editor-settings-icon]');
        const testAction = dialog.querySelector('[data-host-editor-test]');
        const browseAction = dialog.querySelector('[data-host-editor-browse]');
        let hostBrowser = null;
        const configTitle = dialog.querySelector('[data-host-editor-config-title]');
        const configDescription = dialog.querySelector('[data-host-editor-config-description]');
        const configFields = dialog.querySelector('[data-host-editor-config-fields]');
        const tasks = dialog.querySelector('[data-host-editor-tasks]');
        const tasksEmpty = dialog.querySelector('[data-host-editor-tasks-empty]');
        const taskCount = dialog.querySelector('[data-host-editor-task-count]');
        const audit = dialog.querySelector('[data-host-editor-audit]');
        const cloneAction = dialog.querySelector('[data-host-editor-clone]');
        const actionsButton = dialog.querySelector('[data-host-editor-actions]');
        const actionsMenu = dialog.querySelector('[data-host-editor-actions-menu]');
        const undoAction = dialog.querySelector('[data-host-editor-undo]');
        const exportAction = dialog.querySelector('[data-host-editor-export]');
        const deleteAction = dialog.querySelector('[data-host-editor-delete]');
        const saveButton = dialog.querySelector('[data-host-editor-save]');
        const reloadButton = dialog.querySelector('[data-host-editor-reload]');
        const overrideButton = dialog.querySelector('[data-host-editor-override]');
        const autoSaveToggle = dialog.querySelector('[data-host-editor-auto-save]');
        const cancelButton = dialog.querySelector('[data-host-editor-cancel]');
        const tabs = [...dialog.querySelectorAll('[data-host-editor-tab]')];
        const panels = [...dialog.querySelectorAll('[data-host-editor-panel]')];

        let current = null;
        let rendering = false, undoEvent = null;
        const localUndo = window.MauiModalUI.createLocalUndo({
            capture: () => ({ name: informationFields?.querySelector('[data-host-editor-name]')?.value || '',
                description: informationFields?.querySelector('[data-host-editor-description]')?.value || '',
                metadata: Object.fromEntries([...metadata.querySelectorAll('[data-host-editor-metadata-field]')].map(control=>[control.dataset.hostEditorMetadataField,control.value])),
                configuration:configurationState, ftpPort:ftpNonImplicitPort,
                cacheDuration:stateCacheDuration, cacheUnit:stateCacheUnit }),
            restore: draft => {
                informationFields.querySelector('[data-host-editor-name]').value=draft.name;
                informationFields.querySelector('[data-host-editor-description]').value=draft.description;
                metadata.querySelectorAll('[data-host-editor-metadata-field]').forEach(control=>control.value=draft.metadata[control.dataset.hostEditorMetadataField] || '');
                configurationState=draft.configuration; algorithmOrders=Object.create(null); ftpNonImplicitPort=draft.ftpPort;
                stateCacheDuration=draft.cacheDuration; stateCacheUnit=draft.cacheUnit;
                if(activeConfigurationSection) showConfigurationSection(activeConfigurationSection);
            },
            changed: () => updateDirtyState()
        });
        for (const eventName of ['input','change','click']) dialog.addEventListener(eventName,event=>{
            undoEvent={key:event.target,label:event.target.closest('label')?.querySelector('span')?.textContent?.trim() || 'Host setting',coalesce:eventName!=='click'};
            queueMicrotask(()=>{undoEvent=null;});
        },true);
        dialog.addEventListener('focusout',()=>localUndo.end());
        let activeConfigurationSection = '';
        let configurationState = Object.create(null);
        let configurationBaseline = Object.create(null);
        let configurationDefinitions = Object.create(null);
        let ftpNonImplicitPort = '21';
        const stateCacheKeys = ['UseDefStateCaching', 'StateCacheTime', 'StateCacheUnit'];
        let stateCacheDuration = '1';
        let stateCacheUnit = 'Minutes';
        let baselinePayload = '';
        let loadSequence = 0;
        let saving = false;
        let savedStatusTimer = 0;
        let partialSave = false, checkoutConflict = false, manualRecoverySave = false;
        let fingerprintBusy = false;
        let fingerprintSequence = 0;

        let autoSave = false, autoSaveTimer = 0, autoSaveBlocked = '', activeSavePromise = null;
        const checkout = window.MauiObjectEditLease?.create({ type: 'Host', token: antiForgeryToken,
            busy: () => saving || fingerprintBusy || pickerBusy,
            onLost: message => { checkoutConflict = true; window.clearTimeout(autoSaveTimer); showError(message); updateDirtyState(); } });
        function updateSaveButton() {
            if (saveButton) saveButton.textContent = saving ? 'Saving…' : autoSave && !manualRecoverySave && !partialSave && !checkout?.lost ? 'Close' : 'Save';
            if (cancelButton) cancelButton.hidden = autoSave && !manualRecoverySave && !partialSave && !checkout?.lost;
            if (autoSaveToggle) autoSaveToggle.disabled = saving || !current;
        }
        function scheduleAutoSave() {
            window.clearTimeout(autoSaveTimer); autoSaveTimer = 0;
            if (manualRecoverySave || !autoSave || !isDirty() || saving || partialSave || checkout?.lost || fingerprintBusy || picker?.open || hostBrowser?.isOpen()
                || autoSaveBlocked === stablePayload(collectPayload())) return;
            autoSaveTimer = window.setTimeout(() => {
                const focused = document.activeElement;
                const editingText = focused && dialog.contains(focused) && (focused instanceof HTMLTextAreaElement
                    || focused instanceof HTMLInputElement && !['checkbox', 'radio', 'button'].includes(focused.type) || focused.isContentEditable);
                if (editingText) { scheduleAutoSave(); return; }
                void save(true);
            }, 650);
        }
        async function save(automatic = false) {
            if (activeSavePromise) return activeSavePromise;
            if (!isDirty()) return false;
            const promise = performSave(automatic);
            activeSavePromise = promise;
            try { return await promise; }
            finally { if (activeSavePromise === promise) activeSavePromise = null; }
        }

        const formatDate = value => {
            if (!value) return '—';
            const parsed = new Date(value);
            return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
        };
        const display = value => String(value ?? '').trim() || '—';
        const normalizeBoolean = value => {
            const text = String(value ?? '').trim().toLowerCase();
            return text === 'yes' || text === 'true' || text === '1' || text === 'on';
        };

        function reset() {
            window.MauiModalUI.viewOnly(dialog,false);
            ftpNonImplicitPort = '21';
            window.clearTimeout(autoSaveTimer); autoSaveBlocked = '';
            clearSavedStatus();
            closePicker();
            algorithmOrders = Object.create(null);
            current = null;
            localUndo.rebase(true);
            activeConfigurationSection = '';
            configurationState = Object.create(null);
            configurationBaseline = Object.create(null);
            configurationDefinitions = Object.create(null);
            baselinePayload = '';
            saving = false;
            partialSave = false; checkoutConflict = false; manualRecoverySave = false;
            fingerprintBusy = false;
            fingerprintSequence++;
            if (title) title.textContent = 'Host';
            if (subtitle) subtitle.textContent = '';
            if (type) type.textContent = '';
            if (id) id.textContent = '';
            if (icon) icon.textContent = 'H';
            if (informationFields) informationFields.replaceChildren();
            if (metadata) metadata.replaceChildren();
            if (configTabs) configTabs.replaceChildren();
            if (configFields) configFields.replaceChildren();
            if (tasks) tasks.replaceChildren();
            if (audit) audit.replaceChildren();
            if (error) { error.hidden = true; error.textContent = ''; }
            if (content) { content.hidden = true; content.inert = false; }
            if (loading) loading.hidden = false;
            if (saveButton) { saveButton.disabled = true; saveButton.textContent = 'Save'; }
            if (undoAction) { undoAction.disabled = true; undoAction.title = 'No Host is loaded.'; }
            if (exportAction) { exportAction.disabled = true; exportAction.title = 'No Host is loaded.'; }
            if (browseAction) { browseAction.hidden = true; browseAction.disabled = true; }
            if (cloneAction) cloneAction.disabled = true;
            if (testAction) testAction.disabled = true;
            if (deleteAction) { deleteAction.disabled = true; deleteAction.title = 'No Host is loaded.'; }
            switchTab('configuration');
            closeActions();
            updateDirtyState();
        }

        function switchTab(key) {
            closeActions();
            tabs.forEach(tab => {
                const active = tab.dataset.hostEditorTab === key;
                tab.classList.toggle('active', active);
                tab.setAttribute('aria-selected', active ? 'true' : 'false');
            });
            panels.forEach(panel => { panel.hidden = panel.dataset.hostEditorPanel !== key; });
        }

        function createReadOnlyField(label, value, full = false, multiline = false) {
            const wrapper = document.createElement('div');
            wrapper.className = `maui-host-editor-field${full ? ' full' : ''}`;
            const labelElement = document.createElement('span');
            labelElement.textContent = label;
            const valueElement = document.createElement('div');
            valueElement.className = `maui-host-editor-value${multiline ? ' multiline' : ''}`;
            const normalized = String(value ?? '').trim();
            if (!normalized) valueElement.classList.add('empty');
            valueElement.textContent = normalized || '—';
            const body = document.createElement('div');
            body.className = 'maui-host-editor-field-body';
            body.append(valueElement);
            wrapper.append(labelElement, body);
            return wrapper;
        }

        function createInputField(label, value, { full = false, multiline = false, disabled = false, key = '', kind = 'text' } = {}) {
            const wrapper = document.createElement('label');
            wrapper.className = `maui-host-editor-field${full ? ' full' : ''}`;
            const labelElement = document.createElement('span');
            labelElement.textContent = label;
            const control = multiline ? document.createElement('textarea') : document.createElement('input');
            control.className = multiline ? 'text-input textarea-input maui-host-editor-control' : 'text-input maui-host-editor-control';
            if (multiline) control.rows = 4;
            else control.type = kind;
            control.value = String(value ?? '');
            control.disabled = disabled;
            if (key) control.dataset[key] = '';
            const body = document.createElement('div');
            body.className = 'maui-host-editor-field-body';
            body.append(control);
            wrapper.append(labelElement, body);
            return { wrapper, control };
        }

        function createMetadataControl(field, value) {
            const fieldType = String(field?.type || 'String').toLowerCase();
            if (fieldType === 'multilinestring' || fieldType === 'multiline') {
                const textarea = document.createElement('textarea');
                textarea.className = 'text-input textarea-input maui-host-editor-control';
                textarea.rows = 4;
                textarea.value = value;
                return textarea;
            }
            if (fieldType === 'boolean') {
                const select = document.createElement('select');
                select.className = 'text-input maui-host-editor-control';
                select.append(new Option('False', 'false'), new Option('True', 'true'));
                select.value = String(value).toLowerCase() === 'true' ? 'true' : 'false';
                return select;
            }
            if (fieldType === 'list') {
                const select = document.createElement('select');
                select.className = 'text-input maui-host-editor-control';
                (field?.possibleValues || []).forEach(optionValue => select.appendChild(new Option(optionValue, optionValue)));
                select.value = value;
                return select;
            }
            const input = document.createElement('input');
            input.className = 'text-input maui-host-editor-control';
            input.type = fieldType === 'numeric' || fieldType === 'number' ? 'number' : fieldType === 'date' ? 'date' : 'text';
            input.value = value;
            return input;
        }

        function renderInformation(model) {
            const host = model.host || {};
            if (title) title.textContent = display(host.name);
            if (subtitle) subtitle.textContent = host.description || `${display(host.type)} Host`;
            if (type) type.textContent = display(host.type);
            if (id) id.textContent = display(host.id);
            if (icon) icon.textContent = hostIcon(host.typeCode);

            const nameField = createInputField('Host Name', host.name, { disabled: !host.canEditMoveItFields || host.typeCode === 'FileSystem' });
            nameField.control.dataset.hostEditorName = '';
            nameField.control.maxLength = 254;
            const descriptionField = createInputField('Description', host.description, { full: true, multiline: true, disabled: !host.canEditMoveItFields });
            descriptionField.control.dataset.hostEditorDescription = '';
            const resourceGroupsField = createInputField('Resource Groups', host.resourceGroups || '—', { full: true, kind: 'text' });
            resourceGroupsField.control.readOnly = true;
            informationFields?.append(
                nameField.wrapper,
                createReadOnlyField('# Assigned', String(host.assignedCount ?? 0)),
                descriptionField.wrapper,
                resourceGroupsField.wrapper
            );

            const values = model.metadata || {};
            const definitions = Array.isArray(model.fields) ? model.fields : [];
            if (!definitions.length) {
                metadata?.append(createReadOnlyField('MAUI Metadata', 'No Host metadata fields are configured.', true));
            } else {
                definitions.forEach(field => {
                    const key = String(field?.name || field?.key || '').trim();
                    if (!key) return;
                    const labelText = String(field?.displayName || field?.label || field?.name || key).trim();
                    const value = Object.entries(values).find(([candidate]) => candidate.toLowerCase() === key.toLowerCase())?.[1] || '';
                    const wrapper = document.createElement('label');
                    const isMultiline = String(field?.type || '').toLowerCase().includes('multiline');
                    wrapper.className = `maui-host-editor-field${isMultiline ? ' full' : ''}`;
                    const caption = document.createElement('span');
                    caption.textContent = labelText;
                    if (field?.description) caption.title = field.description;
                    const control = createMetadataControl(field, value);
                    window.mauiConfigureMetadataControl?.(control, field);
                    control.dataset.hostEditorMetadataField = key;
                    control.required = Boolean(field?.required);
                    const body = document.createElement('div');
                    body.className = 'maui-host-editor-field-body';
                    body.append(control);
                    wrapper.append(caption, body);
                    metadata?.append(wrapper);
                });
            }

        }

        function initializeConfigurationState(model) {
            algorithmOrders = Object.create(null);
            configurationState = Object.create(null);
            configurationDefinitions = Object.create(null);
            const sections = Array.isArray(model.host?.configurationSections) ? model.host.configurationSections : [];
            sections.forEach(section => {
                (Array.isArray(section?.fields) ? section.fields : []).forEach(field => {
                    const key = String(field?.key || '').trim();
                    if (!key) return;
                    // Retain mode values for read-only visibility; only editable fields can form a save patch.
                    if (field?.editable) configurationDefinitions[key] = field;
                    const valueType = String(field?.valueType || 'Text').toLowerCase();
                    if (valueType.startsWith('secret')) configurationState[key] = '';
                    else if (valueType === 'boolean') configurationState[key] = normalizeBoolean(field?.value);
                    else configurationState[key] = String(field?.value ?? '');
                });
            });
            // Keep the previous explicit/plain port across successful saves in this editor.
            // Opening another Host resets it; an already-implicit Host has no earlier port to restore.
            if (model.host?.typeCode === 'FTP' && !String(configurationState.Secure).startsWith('implicit'))
                ftpNonImplicitPort = String(configurationState.Port || '21');
            stateCacheDuration = Number(configurationState.StateCacheTime) > 0 ? String(configurationState.StateCacheTime) : '1';
            stateCacheUnit = configurationState.StateCacheUnit || 'Minutes';
            configurationBaseline = Object.keys(configurationState).reduce((copy, key) => {
                copy[key] = configurationState[key];
                return copy;
            }, Object.create(null));
        }

        function renderConfiguration(model) {
            const sections = Array.isArray(model.host?.configurationSections) ? model.host.configurationSections : [];
            configTabs?.replaceChildren();
            if (!sections.length) {
                configFields?.replaceChildren(createReadOnlyField('Configuration', 'No mapped fields are available for this Host type.', true));
                return;
            }
            activeConfigurationSection = sections[0].key;
            sections.forEach(section => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'maui-host-editor-subtab';
                button.append(settingsIcon.content.firstElementChild.cloneNode(true));
                const label = document.createElement('span'); label.textContent = section.label; button.append(label);
                button.dataset.hostEditorConfigTab = section.key;
                button.id = `maui-host-config-tab-${configTabs.children.length}`;
                button.setAttribute('aria-controls', 'maui-host-config-panel');
                button.setAttribute('role', 'tab');
                button.setAttribute('aria-selected', 'false');
                button.addEventListener('click', () => showConfigurationSection(section.key));
                configTabs?.append(button);
            });
            showConfigurationSection(activeConfigurationSection);
        }

        function showConfigurationSection(key) {
            const sections = Array.isArray(current?.host?.configurationSections) ? current.host.configurationSections : [];
            const section = sections.find(item => item.key === key) || sections[0];
            if (!section) return;
            activeConfigurationSection = section.key;
            configTabs?.querySelectorAll('[data-host-editor-config-tab]').forEach(button => {
                const active = button.dataset.hostEditorConfigTab === section.key;
                button.classList.toggle('active', active);
                button.setAttribute('aria-selected', active ? 'true' : 'false');
                button.tabIndex = active ? 0 : -1;
                if (active) configFields?.closest('[role=tabpanel]')?.setAttribute('aria-labelledby', button.id);
            });
            if (configTitle) configTitle.textContent = section.label;
            if (configDescription) {
                const suffix = current?.host?.canEditConfiguration ? 'Editable MOVEit Host settings.' : 'MOVEit Host settings.';
                configDescription.textContent = `${display(current?.host?.type)} Host — ${suffix}`;
            }
            if (!configFields) return;
            configFields.replaceChildren();
            const fields = Array.isArray(section.fields) ? section.fields : [];
            if (section.key === 'directory-parsing' && current.host.typeCode === 'FTP') {
                configFields.append(createDirectoryParsingField(fields));
                return;
            }
            fields.filter(field => !['StateCacheTime', 'StateCacheUnit'].includes(field.key)).filter(isConfigurationFieldVisible).forEach(field => {
                configFields.append(createConfigurationField(field));
                if (field.key === 'Secure' && current.host.typeCode === 'FTP') configFields.append(createFtpCccField(field.editable));
            });
            if (!fields.length) configFields.append(createReadOnlyField(section.label, 'No mapped properties are exposed for this section in the current Host response.', true));
        }

        const narrowNavigation = window.matchMedia('(max-width:700px)');
        const updateNavigationOrientation = () => configTabs?.setAttribute('aria-orientation', narrowNavigation.matches ? 'horizontal' : 'vertical');
        updateNavigationOrientation();
        narrowNavigation.addEventListener('change', updateNavigationOrientation);
        configTabs?.addEventListener('keydown', event => {
            const buttons = [...configTabs.querySelectorAll('[data-host-editor-config-tab]')];
            const index = buttons.indexOf(event.target);
            const previous = narrowNavigation.matches ? 'ArrowLeft' : 'ArrowUp';
            const next = narrowNavigation.matches ? 'ArrowRight' : 'ArrowDown';
            if (index < 0 || ![previous, next, 'Home', 'End'].includes(event.key)) return;
            event.preventDefault();
            const target = buttons[event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (index + (event.key === next ? 1 : -1) + buttons.length) % buttons.length];
            target.click(); target.focus();
        });

        function isConfigurationFieldVisible(field) {
            const mail = ['POP3', 'SMTP'].includes(current?.host?.typeCode);
            if (mail && ['OAuthTenantID', 'OAuthScope', 'OAuthTokenURL'].includes(field?.key)) {
                if (!configurationState.UseOAuth) return false;
                return (configurationState.OAuthProvider === 'microsoft365') === (field.key === 'OAuthTenantID');
            }
            if (current?.host?.typeCode === 'S3' && field.key === 'S3AlternativeEndpoint' && String(configurationState.AuthMethod ?? '0') !== '0') return false;
            const key = String(field?.conditionKey || '');
            if (!key) return true;
            const value = configurationState[key];
            if (field.conditionValue === 'active') return String(value || '').length > 0 && String(value).toLowerCase() !== 'none';
            if (field.conditionValue !== 'true' && field.conditionValue !== 'false') return String(value ?? '').toLowerCase() === String(field.conditionValue).toLowerCase();
            return Boolean(value) === (field.conditionValue === 'true');
        }

        function createConfigurationField(field) {
            const key = String(field?.key || '').trim();
            const valueType = String(field?.valueType || 'Text').toLowerCase();
            const editable = Boolean(field?.editable);
            if (key === 'UseDefStateCaching') return createStateCacheField(field);
            if (valueType === 'algorithmlist') return createAlgorithmField(field);
            if (field?.editable && valueType === 'certificatepart') return createReadOnlyField(field.label || key, configurationState[key]);
            const choiceField = ['boolean', 'hostselect', 'keyselect', 'scriptselect', 'select'].includes(valueType);
            if (!editable && !choiceField) return createReadOnlyField(field?.label || key, field?.value, valueType.endsWith('multiline') || valueType === 'certificate', valueType.endsWith('multiline'));
            const value = editable ? configurationState[key] : field?.value;

            const wrapper = document.createElement('div');
            wrapper.className = `maui-host-editor-field${valueType.endsWith('multiline') ? ' full' : ''}`;
            const caption = document.createElement('label');
            caption.textContent = field?.label || key;
            const body = document.createElement('div');
            body.className = 'maui-host-editor-field-body';
            let control;

            if (valueType === 'boolean') {
                control = document.createElement('select');
                control.className = 'text-input maui-host-editor-control';
                control.append(new Option('No', 'false'), new Option('Yes', 'true'));
                if (!editable && !String(value ?? '').trim()) control.append(new Option('—', ''));
                control.value = !editable && !String(value ?? '').trim() ? '' : normalizeBoolean(value) ? 'true' : 'false';
            } else if (valueType === 'hostselect' || valueType === 'keyselect' || valueType === 'scriptselect' || valueType === 'select') {
                control = document.createElement('select');
                control.className = 'text-input maui-host-editor-control';
                const ftpSecurity = key === 'Secure' && current?.host?.typeCode === 'FTP';
                const choices = (Array.isArray(field?.options) ? field.options : []).filter(option => !ftpSecurity || !String(option.value).endsWith('-ccc'));
                choices.forEach(option => control.append(new Option(option?.label || option?.value || 'None', option?.value || '')));
                const currentValue = ftpSecurity && ['on-ccc', 'tls-c-ccc', 'implicit-ccc'].includes(value) ? value.slice(0, -4) : String(value ?? '');
                if (currentValue && !choices.some(option => String(option?.value) === currentValue))
                    control.append(new Option(currentValue + ' (current value)', currentValue));
                control.value = currentValue;
            } else if (valueType === 'secretmultiline') {
                control = document.createElement('textarea');
                control.className = 'text-input textarea-input maui-host-editor-control';
                control.rows = 4;
                control.autocomplete = 'off';
                control.value = String(value ?? '');
                control.placeholder = 'Leave blank to keep the current stored value';
            } else if (valueType.startsWith('secret')) {
                control = document.createElement('input');
                control.className = 'text-input maui-host-editor-control';
                control.type = 'password';
                control.autocomplete = 'new-password';
                control.value = String(value ?? '');
                control.placeholder = 'Leave blank to keep the current stored value';
            } else if (valueType === 'number') {
                control = document.createElement('input');
                control.className = 'text-input maui-host-editor-control';
                control.type = 'number';
                control.min = key.toLowerCase().endsWith('port') ? '1' : '0';
                if (key.toLowerCase().endsWith('port')) control.max = '65535';
                control.step = '1';
                control.value = String(value ?? '0');
            } else if (valueType === 'multiline') {
                control = document.createElement('textarea');
                control.className = 'text-input textarea-input maui-host-editor-control';
                control.rows = 4;
                control.value = String(value ?? '');
            } else {
                control = document.createElement('input');
                control.className = 'text-input maui-host-editor-control';
                control.type = 'text';
                control.readOnly = valueType === 'certificate';
                control.value = String(value ?? '');
            }

            control.id = `maui-host-editor-control-${key}`;
            caption.htmlFor = control.id;
            control.disabled = !editable;
            if (editable) {
                control.dataset.hostEditorConfigField = key;
                control.addEventListener('input', () => updateConfigurationValue(key, valueType, control));
                control.addEventListener('change', () => updateConfigurationValue(key, valueType, control));
            }
            body.append(control);
            wrapper.append(caption, body);
            if (valueType.startsWith('secret')) {
                const note = document.createElement('small');
                note.className = 'maui-host-editor-control-note';
                note.textContent = 'The stored secret is suppressed. Enter a value only to replace it.';
                note.id = `${control.id}-note`;
                control.setAttribute('aria-describedby', note.id);
                body.append(note);
            }
            if (valueType === 'certificate' || valueType === 'documentlibrary') {
                const actions = document.createElement('div');
                actions.className = 'maui-host-editor-field-actions';
                const select = document.createElement('button');
                select.type = 'button'; select.className = 'secondary-button';
                select.textContent = valueType === 'certificate' ? 'Set Certificate' : 'Browse';
                select.dataset.hostEditorLookup = valueType;
                select.addEventListener('click', () => openPicker(valueType));
                if (valueType === 'documentlibrary') {
                    const supported = ['1', '2'].includes(String(configurationState.UsingAppAuthentication));
                    select.disabled = !supported;
                    if (!supported) select.title = 'Library browsing requires App Access or Certificate Access.';
                } else {
                    wrapper.classList.add('full');
                    const clear = document.createElement('button');
                    clear.type = 'button'; clear.className = 'danger-button'; clear.textContent = 'Clear Certificate';
                    clear.addEventListener('click', () => {
                        if (saving || partialSave) return;
                        ['DefClientCertSHA1', 'DefClientCertStore', 'DefClientCertSubject'].forEach(key => { configurationState[key] = ''; });
                        showConfigurationSection(activeConfigurationSection); updateDirtyState();
                    });
                    actions.append(clear);
                }
                actions.prepend(select); body.append(actions);
            }
            if (valueType === 'hostfingerprint') {
                wrapper.classList.add('full');
                const actions = document.createElement('div');
                actions.className = 'maui-host-editor-field-actions';
                const retrieve = document.createElement('button');
                retrieve.type = 'button'; retrieve.className = 'secondary-button';
                retrieve.textContent = 'Retrieve Host Key'; retrieve.disabled = fingerprintBusy;
                retrieve.addEventListener('click', () => retrieveHostKey(retrieve));
                const clear = document.createElement('button');
                clear.type = 'button'; clear.className = 'danger-button'; clear.textContent = 'Clear Host Key';
                clear.addEventListener('click', () => {
                    if (saving || partialSave) return;
                    fingerprintSequence++; fingerprintBusy = false;
                    configurationState.SSHFingerprint = ''; configurationState.IgnoreCertProbs = true;
                    showConfigurationSection(activeConfigurationSection); updateDirtyState();
                });
                actions.append(retrieve, clear); body.append(actions);
            }
            return wrapper;
        }

        function createFtpCccField(editable) {
            const wrapper = document.createElement('div'); wrapper.className = 'maui-host-editor-field';
            const label = document.createElement('label'); label.textContent = 'Cleartext after signon (CCC)';
            const body = document.createElement('div'); body.className = 'maui-host-editor-field-body';
            const control = document.createElement('select'); control.className = 'text-input maui-host-editor-control';
            control.id = 'maui-host-editor-control-FtpCcc'; label.htmlFor = control.id;
            control.dataset.hostEditorFtpCcc = '';
            control.append(new Option('No', 'false'), new Option('Yes', 'true'));
            control.value = String(configurationState.Secure).endsWith('-ccc') ? 'true' : 'false';
            control.disabled = !editable || !['on', 'on-ccc', 'tls-c', 'tls-c-ccc', 'implicit', 'implicit-ccc'].includes(String(configurationState.Secure));
            control.addEventListener('change', () => {
                const base = String(configurationState.Secure).replace(/-ccc$/, '');
                configurationState.Secure = control.value === 'true' ? base + '-ccc' : base;
                updateDirtyState();
            });
            body.append(control); wrapper.append(label, body); return wrapper;
        }

        function createDirectoryParsingField(fields) {
            const definition = fields.find(field => field.key === 'DirectoryParsingMode');
            const mode = String(configurationState.DirectoryParsingMode || 'automatic');
            const wrapper = document.createElement('fieldset'); wrapper.className = 'maui-host-editor-field full maui-host-ftp-directory';
            wrapper.setAttribute('aria-label', 'Directory list parsing');
            for (const option of definition?.options || []) {
                const label = document.createElement('label'); label.className = 'maui-host-state-cache-check';
                const radio = document.createElement('input'); radio.type = 'radio'; radio.name = 'mauiHostFtpDirectoryMode';
                radio.value = option.value; radio.checked = mode === option.value; radio.dataset.hostEditorDirectoryMode = option.value;
                const scriptField = fields.find(field => field.key === 'DirScriptID');
                radio.disabled = !definition.editable || option.value === 'script' && !scriptField?.editable && mode !== 'script';
                radio.addEventListener('change', () => {
                    if (!radio.checked) return;
                    configurationState.DirectoryParsingMode = radio.value;
                    showConfigurationSection(activeConfigurationSection); updateDirtyState();
                    configFields.querySelector(`[data-host-editor-directory-mode="${radio.value}"]`)?.focus();
                });
                const text = document.createElement('span'); text.textContent = option.label; label.append(radio, text); wrapper.append(label);
                if (option.value === 'custom') {
                    for (const [heading, keys] of [['Skip lines', ['CustomDirSkipTop', 'CustomDirSkipBottom']], ['Start columns', ['CustomDirColFilename', 'CustomDirColDate']]]) {
                        const group = document.createElement('fieldset'); group.className = 'maui-host-ftp-directory-columns';
                        group.disabled = !definition.editable || mode !== 'custom';
                        const title = document.createElement('legend'); title.textContent = heading; group.append(title);
                        for (const key of keys) {
                            const field = fields.find(item => item.key === key);
                            if (field) group.append(createConfigurationField(field));
                        }
                        wrapper.append(group);
                    }
                }
                if (option.value === 'script' && scriptField) {
                    const group = document.createElement('fieldset'); group.className = 'maui-host-ftp-directory-script';
                    group.disabled = mode !== 'script'; group.append(createConfigurationField(scriptField));
                    if (scriptField.helpText) { const help = document.createElement('small'); help.className = 'field-help'; help.textContent = scriptField.helpText; group.append(help); }
                    wrapper.append(group);
                }
            }
            return wrapper;
        }

        function createAlgorithmField(field) {
            const key = field.key, editable = Boolean(field.editable);
            const choices = Array.isArray(field.options) ? field.options : [];
            const csv = String(configurationState[key] ?? field.value ?? '');
            const widget = window.MauiSshAlgorithmList;
            if (!algorithmOrders[key]) algorithmOrders[key] = widget.createState(choices, csv);
            const wrapper = document.createElement('div'); wrapper.className = 'maui-host-editor-field full';
            const caption = document.createElement('span'); caption.textContent = field.label;
            const body = document.createElement('div'); body.className = 'maui-host-editor-field-body';
            const list = document.createElement('div'); list.dataset.hostEditorAlgorithms = key;
            widget.render(list, { choices, state: algorithmOrders[key], editable,
                onChange: value => { configurationState[key] = value; updateDirtyState(); }, onError: showError });
            body.append(list);
            if (field.helpText) { const help = document.createElement('span'); help.className = 'field-help'; help.textContent = field.helpText; body.append(help); }
            wrapper.append(caption, body); return wrapper;
        }

        function closePicker() {
            pickerSequence++; pickerBusy = false; pickerSelection = -1; pickerItems = [];
            if (picker?.open) picker.close();
            updateDirtyState();
        }

        function openPicker(mode) {
            if (!picker || saving || partialSave || !current?.host) return;
            pickerMode = mode; pickerSelection = -1; pickerItems = [];
            picker.querySelector('[data-host-picker-title]').textContent = mode === 'certificate' ? 'Select Client Certificate' : 'Select Document Library';
            picker.showModal(); updateDirtyState(); void refreshPicker();
        }

        async function refreshPicker() {
            if (!picker?.open || !current?.host) return;
            const sequence = ++pickerSequence, modelSequence = loadSequence, hostId = String(current.host.id), mode = pickerMode;
            const list = picker.querySelector('[data-host-picker-list]'), error = picker.querySelector('[data-host-picker-error]');
            const loading = picker.querySelector('[data-host-picker-loading]'), ok = picker.querySelector('[data-host-picker-ok]');
            const refresh = picker.querySelector('[data-host-picker-refresh]');
            pickerBusy = true; pickerSelection = -1; pickerItems = []; ok.disabled = true; refresh.disabled = true;
            error.hidden = true; loading.hidden = false; list.replaceChildren();
            try {
                const endpoint = mode === 'certificate' ? certificatesUrl : documentLibrariesUrl;
                if (!endpoint) throw new Error('This Host lookup is unavailable. Reload MAUI.');
                const url = new URL(endpoint, window.location.href);
                const options = { headers: { Accept: 'application/json' }, cache: 'no-store' };
                if (checkout?.id) options.headers['X-MAUI-Edit-Lease'] = checkout.id;
                if (mode === 'certificate') url.searchParams.set('id', hostId);
                else {
                    options.method = 'POST'; options.headers['Content-Type'] = 'application/json'; options.headers.RequestVerificationToken = antiForgeryToken;
                    options.body = JSON.stringify({ hostId, configuration: collectPayload().configuration });
                }
                const response = await fetch(url.toString(), options);
                if (response.status === 401) { window.location.replace(loginUrl); return; }
                const result = await response.json().catch(() => ({}));
                if (sequence !== pickerSequence || modelSequence !== loadSequence || !picker.open || String(current?.host?.id) !== hostId) return;
                if (!response.ok) throw new Error(result.message || 'Unable to load the available selections.');
                pickerItems = Array.isArray(result.items) ? result.items : [];
                pickerItems.forEach((item, index) => {
                    const row = document.createElement('button'); row.type = 'button'; row.className = 'maui-host-picker-row'; row.setAttribute('role', 'option'); row.setAttribute('aria-selected', 'false');
                    const name = document.createElement('span'); name.textContent = mode === 'certificate' ? item.subject : item.name;
                    const detail = document.createElement('small'); detail.textContent = mode === 'certificate' ? `${item.store} · ${item.thumbprint} · Expires ${item.expires || '—'}` : item.guid;
                    row.append(name, detail);
                    row.addEventListener('click', () => {
                        pickerSelection = index; ok.disabled = false;
                        list.querySelectorAll('[role="option"]').forEach(option => option.setAttribute('aria-selected', option === row ? 'true' : 'false'));
                    });
                    row.addEventListener('dblclick', () => { pickerSelection = index; applyPicker(); });
                    list.append(row);
                });
                if (!pickerItems.length) {
                    const empty = document.createElement('div'); empty.className = 'maui-host-picker-empty';
                    empty.textContent = mode === 'certificate' ? 'No available client certificates.' : 'No document libraries were returned.'; list.append(empty);
                }
            } catch (err) {
                if (sequence === pickerSequence && modelSequence === loadSequence && picker.open) { error.textContent = err.message; error.hidden = false; }
            } finally {
                if (sequence === pickerSequence && modelSequence === loadSequence) { pickerBusy = false; loading.hidden = true; refresh.disabled = false; }
            }
        }

        function applyPicker() {
            const item = pickerItems[pickerSelection];
            if (!item || pickerBusy || !picker?.open || saving || partialSave) return;
            if (pickerMode === 'certificate') {
                configurationState.DefClientCertSHA1 = item.thumbprint;
                configurationState.DefClientCertStore = item.store;
                configurationState.DefClientCertSubject = item.subject;
            } else {
                configurationState.DefDocumentLibrary = item.name;
                configurationState.DefDocumentLibraryGuid = item.guid;
            }
            closePicker(); showConfigurationSection(activeConfigurationSection); updateDirtyState();
        }

        function createStateCacheField(field) {
            const editable = Boolean(field.editable);
            const inherited = Boolean(configurationState.UseDefStateCaching);
            const time = String(configurationState.StateCacheTime || '-1');
            const mode = Number(time) > 0 ? 'for' : time === '0' ? 'task' : 'always';
            const wrapper = document.createElement('div');
            wrapper.className = 'maui-host-editor-field full maui-host-editor-state-cache';
            wrapper.dataset.hostEditorStateCache = '';
            const defaultLabel = document.createElement('label');
            defaultLabel.className = 'maui-host-state-cache-check';
            const defaultControl = document.createElement('input');
            defaultControl.type = 'checkbox';
            defaultControl.id = 'maui-host-editor-control-UseDefStateCaching';
            defaultControl.checked = inherited;
            defaultControl.disabled = !editable;
            defaultControl.addEventListener('change', () => {
                configurationState.UseDefStateCaching = defaultControl.checked;
                if (!defaultControl.checked && !String(configurationState.StateCacheTime || '').trim()) configurationState.StateCacheTime = '-1';
                showConfigurationSection(activeConfigurationSection);
                updateDirtyState();
            });
            const defaultText = document.createElement('span');
            defaultText.textContent = 'Use default state cache settings';
            defaultLabel.append(defaultControl, defaultText);
            const choices = document.createElement('fieldset');
            choices.className = 'maui-host-state-cache-choices';
            choices.disabled = !editable || inherited;
            const legend = document.createElement('legend');
            legend.textContent = "Keep this Host's state information cached:";
            choices.append(legend);
            for (const [value, text] of [['always', 'Always'], ['task', 'Until end of task run'], ['for', 'For']]) {
                const row = document.createElement('div');
                row.className = 'maui-host-state-cache-row';
                const label = document.createElement('label');
                const radio = document.createElement('input');
                radio.type = 'radio'; radio.name = 'mauiHostStateCacheMode'; radio.value = value; radio.checked = mode === value;
                radio.dataset.hostStateCacheMode = value;
                radio.addEventListener('change', () => {
                    if (!radio.checked) return;
                    configurationState.StateCacheTime = value === 'always' ? '-1' : value === 'task' ? '0' : stateCacheDuration;
                    if (value === 'for') configurationState.StateCacheUnit = stateCacheUnit;
                    showConfigurationSection(activeConfigurationSection);
                    updateDirtyState();
                });
                const caption = document.createElement('span'); caption.textContent = text;
                label.append(radio, caption); row.append(label);
                if (value === 'for') {
                    const duration = document.createElement('input');
                    duration.type = 'number'; duration.min = '1'; duration.max = '2147483647'; duration.step = '1';
                    duration.className = 'text-input maui-host-editor-control';
                    duration.id = 'maui-host-editor-control-StateCacheTime';
                    duration.setAttribute('aria-label', 'State cache duration');
                    duration.value = stateCacheDuration; duration.disabled = mode !== 'for';
                    duration.addEventListener('input', () => { stateCacheDuration = duration.value; configurationState.StateCacheTime = duration.value; updateDirtyState(); });
                    const unit = document.createElement('select');
                    unit.className = 'text-input maui-host-editor-control';
                    unit.id = 'maui-host-editor-control-StateCacheUnit';
                    unit.setAttribute('aria-label', 'State cache duration unit');
                    const definition = current.host.configurationSections.flatMap(section => section.fields).find(item => item.key === 'StateCacheUnit');
                    (definition?.options || []).forEach(option => unit.append(new Option(option.label, option.value)));
                    if (stateCacheUnit && ![...unit.options].some(option => option.value === stateCacheUnit)) unit.append(new Option(stateCacheUnit + ' (current value)', stateCacheUnit));
                    unit.value = stateCacheUnit; unit.disabled = mode !== 'for';
                    unit.addEventListener('change', () => { stateCacheUnit = unit.value; configurationState.StateCacheUnit = unit.value; updateDirtyState(); });
                    const suffix = document.createElement('span'); suffix.textContent = 'after last task run';
                    row.append(duration, unit, suffix);
                }
                choices.append(row);
            }
            wrapper.append(defaultLabel, choices);
            return wrapper;
        }

        async function retrieveHostKey(button) {
            if (saving || partialSave || fingerprintBusy || !current?.host) return;
            if (!fingerprintUrl) { showError('Host key retrieval is unavailable. Reload MAUI.'); return; }
            const hostId = String(current.host.id);
            const sequence = ++fingerprintSequence, modelSequence = loadSequence;
            fingerprintBusy = true; button.disabled = true; button.textContent = 'Retrieving...'; updateDirtyState();
            hideError();
            try {
                const payload = {
                    hostId, host: String(configurationState.Host || ''), port: Number(configurationState.Port),
                    kexAlgs: String(configurationState.KexAlgs || ''), proxyType: String(configurationState.ProxyType || 'None'),
                    proxyHost: String(configurationState.ProxyHost || ''),
                    proxyPort: configurationState.ProxyPort ? Number(configurationState.ProxyPort) : null,
                    proxyUsername: String(configurationState.ProxyUsername || '')
                };
                const headers = { 'Content-Type': 'application/json', 'Accept': 'application/json', 'RequestVerificationToken': antiForgeryToken };
                if (checkout?.id) headers['X-MAUI-Edit-Lease'] = checkout.id;
                const response = await fetch(fingerprintUrl, {
                    method: 'POST', headers,
                    body: JSON.stringify(payload), cache: 'no-store'
                });
                if (response.status === 401) { window.location.replace(loginUrl); return; }
                const data = await response.json().catch(() => ({}));
                if (sequence !== fingerprintSequence || modelSequence !== loadSequence || String(current?.host?.id) !== hostId) return;
                if (!response.ok || !String(data.fingerprint || '').trim()) throw new Error(data.message || 'MOVEit did not return an SSH Host key.');
                configurationState.SSHFingerprint = String(data.fingerprint).trim();
                configurationState.IgnoreCertProbs = false;
                showConfigurationSection(activeConfigurationSection);
            } catch (err) {
                if (sequence === fingerprintSequence && modelSequence === loadSequence) showError(err.message);
            } finally {
                if (sequence === fingerprintSequence && modelSequence === loadSequence) {
                    fingerprintBusy = false; button.disabled = false; button.textContent = 'Retrieve Host Key';
                    if (current?.host) showConfigurationSection(activeConfigurationSection);
                    updateDirtyState();
                }
            }
        }

        function updateConfigurationValue(key, valueType, control) {
            if (key === 'Secure' && current?.host?.typeCode === 'FTP') {
                const wasImplicit = String(configurationState.Secure).startsWith('implicit');
                const implicit = String(control.value).startsWith('implicit');
                if (implicit) {
                    if (!wasImplicit) ftpNonImplicitPort = String(configurationState.Port || '21');
                    configurationState.Port = '990';
                } else if (wasImplicit) configurationState.Port = ftpNonImplicitPort;
            }
            if (valueType === 'boolean') configurationState[key] = control.value === 'true';
            else configurationState[key] = control.value ?? '';
            if (key === 'SSHFingerprint' && String(control.value || '').trim()) configurationState.IgnoreCertProbs = false;
            if (['POP3', 'SMTP'].includes(current?.host?.typeCode) && configurationState.UseOAuth
                && configurationState.OAuthProvider === 'microsoft365' && ['UseOAuth', 'OAuthProvider', 'OAuthTenantID'].includes(key)) {
                // The shared Host layer composes the native scope and URL. Reflect its required TLS setting immediately.
                configurationState.Secure = true;
            }
            if (['UseOAuth', 'OAuthProvider', 'UncAuthenticationMode', 'ProxyType', 'UsingAppAuthentication', 'Secure', 'CustomDir', 'IsNonAmazonEndPoint', 'AuthMethod', 'OneDriveAuthenticationMode'].includes(key)) showConfigurationSection(activeConfigurationSection);
            updateDirtyState();
        }

        function renderTasks(model) {
            const rows = Array.isArray(model.tasks) ? model.tasks : [];
            if (taskCount) taskCount.textContent = String(rows.length);
            if (tasksEmpty) tasksEmpty.hidden = rows.length > 0;
            if (tasks) tasks.hidden = rows.length === 0;
            tasks?.replaceChildren();
            rows.forEach(item => {
                const row = document.createElement('div');
                row.className = 'maui-host-editor-task-row';
                const taskId = document.createElement('div');
                taskId.className = 'maui-host-editor-task-id';
                taskId.textContent = item.id || '—';
                const name = document.createElement('button');
                name.type = 'button';
                name.className = 'row-primary-link';
                name.dataset.hostTaskEditor = '';
                name.dataset.taskId = item.id || '';
                name.dataset.taskName = item.name || '';
                name.textContent = item.name || item.id || 'Task';
                row.append(taskId, name);
                tasks?.append(row);
            });
        }

        function renderAudit(model) {
            const source = model.audit || {};
            audit?.replaceChildren(
                createReadOnlyField('Created On', formatDate(source.createdUtc)),
                createReadOnlyField('Created By', source.createdBy),
                createReadOnlyField('Modified On', formatDate(source.modifiedUtc)),
                createReadOnlyField('Modified By', source.modifiedBy)
            );
        }

        function hostIcon(typeCode) {
            const value = String(typeCode || '').toLowerCase();
            if (value === 'ftp') return 'F';
            if (value === 'sshftp') return 'S';
            if (value === 's3') return 'S3';
            if (value === 'azureblob') return 'A';
            if (value === 'sharepoint') return 'SP';
            if (value === 'googlestorage') return 'G';
            if (value === 'onedrive') return 'ON';
            if (value === 'pop3') return 'P';
            if (value === 'smtp') return 'M';
            if (value === 'share') return 'UNC';
            if (value === 'silock') return 'MT';
            return 'H';
        }

        function collectPayload() {
            const host = current?.host || {};
            const nameControl = informationFields?.querySelector('[data-host-editor-name]');
            const descriptionControl = informationFields?.querySelector('[data-host-editor-description]');
            const metadataValues = {};
            metadata?.querySelectorAll('[data-host-editor-metadata-field]').forEach(control => {
                metadataValues[control.dataset.hostEditorMetadataField] = control.value ?? '';
            });
            const configuration = {};
            Object.keys(configurationDefinitions).forEach(key => {
                const definition = configurationDefinitions[key];
                if (stateCacheKeys.includes(key)) return;
                if (!isConfigurationFieldVisible(definition)) return;
                const valueType = String(definition?.valueType || 'Text').toLowerCase();
                const value = configurationState[key];
                const baseline = configurationBaseline[key];
                if (valueType.startsWith('secret')) {
                    if (String(value || '').length) configuration[key] = value;
                    return;
                }
                if (valueType === 'algorithmlist' && !String(value || '').trim()) {
                    // An empty native field must use the same defaults shown by the widget.
                    // Including this fallback in the initial payload keeps an untouched Host
                    // clean, while persisting the selections when another field is saved.
                    const defaults = (definition.options || []).filter(option => option.defaultEnabled).map(option => option.value).join(',');
                    if (defaults) configuration[key] = defaults;
                    return;
                }
                if (value !== baseline) configuration[key] = value;
            });
            if (['POP3', 'SMTP'].includes(host.typeCode) && configurationState.UseOAuth
                && configurationState.OAuthProvider === 'microsoft365'
                && ['UseOAuth', 'OAuthProvider', 'OAuthTenantID'].some(key => configurationState[key] !== configurationBaseline[key])) {
                configuration.OAuthProvider = 'microsoft365';
                configuration.OAuthTenantID = String(configurationState.OAuthTenantID || '').trim();
            }
            if (host.typeCode === 'S3' && configurationDefinitions.AuthMethod && configurationState.AuthMethod !== configurationBaseline.AuthMethod) {
                configuration.AuthMethod = configurationState.AuthMethod;
                if (String(configurationState.AuthMethod) === '0') configuration.DefUsername = configurationState.DefUsername || '';
                if (String(configurationState.AuthMethod) === '1') configuration.S3ProfileName = configurationState.S3ProfileName || '';
            }
            if (configurationDefinitions.UseDefStateCaching) {
                const inherited = Boolean(configurationState.UseDefStateCaching);
                const changed = inherited !== configurationBaseline.UseDefStateCaching
                    || (!inherited && ['StateCacheTime', 'StateCacheUnit'].some(key => configurationState[key] !== configurationBaseline[key]));
                if (changed) {
                    configuration.UseDefStateCaching = inherited;
                    if (!inherited) {
                        configuration.StateCacheTime = String(configurationState.StateCacheTime ?? '-1');
                        if (Number(configuration.StateCacheTime) > 0) configuration.StateCacheUnit = configurationState.StateCacheUnit || stateCacheUnit;
                    }
                }
            }
            for (const keys of [['DefClientCertSHA1', 'DefClientCertStore', 'DefClientCertSubject'], ['DefDocumentLibrary', 'DefDocumentLibraryGuid']]) {
                if (keys.some(key => Object.prototype.hasOwnProperty.call(configuration, key)))
                    keys.forEach(key => { if (configurationDefinitions[key] && isConfigurationFieldVisible(configurationDefinitions[key])) configuration[key] = configurationState[key] ?? ''; });
            }
            return {
                editVersion: host.editVersion || '',
                name: String(nameControl?.value ?? host.name ?? '').trim(),
                description: String(descriptionControl?.value ?? host.description ?? ''),
                metadata: metadataValues,
                configuration
            };
        }

        function stablePayload(payload) {
            const sortObject = value => {
                if (Array.isArray(value)) return value.map(sortObject);
                if (!value || typeof value !== 'object') return value;
                return Object.keys(value).sort((a, b) => a.localeCompare(b)).reduce((result, key) => {
                    result[key] = sortObject(value[key]);
                    return result;
                }, {});
            };
            return JSON.stringify(sortObject(payload));
        }

        function isDirty() {
            if (!current || current.readOnly || !baselinePayload) return false;
            return stablePayload(collectPayload()) !== baselinePayload;
        }

        function clearSavedStatus() {
            if (savedStatusTimer) window.clearTimeout(savedStatusTimer);
            savedStatusTimer = 0;
        }

        function updateDirtyState() {
            if(current && !rendering && !saving && !current.readOnly) localUndo.record(undoEvent?.key,undoEvent?.label || 'Host setting',undoEvent?.coalesce);
            window.MauiModalUI.viewOnly(dialog,Boolean(current?.readOnly));
            updateActionAvailability();
            const dirty = isDirty();
            if (dirty) clearSavedStatus();
            dialog.classList.toggle('has-unsaved-changes', dirty);
            if (saveButton) saveButton.disabled = saving || partialSave || checkout?.lost || fingerprintBusy || picker?.open || hostBrowser?.isOpen() || (!autoSave || manualRecoverySave) && !dirty;
            updateSaveButton();
            if (reloadButton) { reloadButton.hidden = !partialSave && !checkout?.lost; reloadButton.disabled = saving || fingerprintBusy || picker?.open || hostBrowser?.isOpen(); }
            if (overrideButton) { overrideButton.hidden = !(checkoutConflict || checkout?.lost || partialSave) || document.documentElement.dataset.moveitAdmin !== 'true'; overrideButton.disabled = saving || fingerprintBusy || picker?.open || hostBrowser?.isOpen(); }
            scheduleAutoSave();
            if (!dirtyState) return;
            // Match Task Editor messages; the existing in-modal error takes precedence.
            dirtyState.hidden = Boolean(error && !error.hidden);
            dirtyState.classList.toggle('error', partialSave);
            if (dirtyState.hidden) return;
            const message = saving ? 'Saving changes to MOVEit Automation…'
                : !current ? (loading?.hidden ? '' : 'Loading Host…')
                : current.readOnly ? window.MauiModalUI.readOnlyMessage('Host')
                : partialSave ? 'Save outcome needs review. Reopen the Host Editor before saving.'
                : manualRecoverySave ? window.MauiObjectEditLease.restoredMessage
                : dirty ? (autoSave ? 'Changes pending Auto Save…' : 'Unsaved changes.')
                : savedStatusTimer ? 'Saved.'
                : 'No unsaved changes.';
            if (dirtyState.textContent !== message) dirtyState.textContent = message;
        }

        function wireDirtyTracking() {
            informationFields?.querySelectorAll('input,textarea,select').forEach(control => {
                control.addEventListener('input', updateDirtyState);
                control.addEventListener('change', updateDirtyState);
            });
            metadata?.querySelectorAll('input,textarea,select').forEach(control => {
                control.addEventListener('input', updateDirtyState);
                control.addEventListener('change', updateDirtyState);
            });
        }

        function validateBeforeSave() {
            const payload = collectPayload();
            if (current?.host?.typeCode === 'FTP' && configurationDefinitions.DirectoryParsingMode
                && configurationState.DirectoryParsingMode === 'script' && !/^[1-9]\d*$/.test(String(configurationState.DirScriptID || ''))) {
                showError('Select a directory parsing Script before saving.'); return false;
            }
            const nameControl = informationFields?.querySelector('[data-host-editor-name]');
            if (current?.host?.canEditMoveItFields) {
                if (!payload.name) { showError('Host Name is required.'); nameControl?.focus(); return false; }
                if (payload.name.length > 254) { showError('Host Name cannot exceed 254 characters.'); nameControl?.focus(); return false; }
            }
            if (window.mauiValidateMetadataControls && metadata && !window.mauiValidateMetadataControls(metadata)) return false;
            const invalid = configFields?.querySelector(':invalid');
            if (invalid) { invalid.reportValidity?.(); invalid.focus?.(); return false; }
            if (current?.host?.canEditConfiguration && Object.keys(payload.configuration || {}).length > 0) {
                if (Object.prototype.hasOwnProperty.call(payload.configuration, 'UNC')) {
                    const unc = String(payload.configuration.UNC || '').trim();
                    if (!unc) { showError('Resource UNC is required.'); return false; }
                    if (!unc.startsWith('\\\\')) { showError('Resource UNC must begin with \\\\.'); return false; }
                }
                for (const [key, definition] of Object.entries(configurationDefinitions)) {
                    if (!Object.prototype.hasOwnProperty.call(payload.configuration, key)) continue;
                    if (String(definition?.valueType || '').toLowerCase() !== 'number') continue;
                    const raw = String(configurationState[key] ?? '').trim();
                    if (key === 'StateCacheTime') {
                        if (!/^-?\d+$/.test(raw) || Number(raw) < -1 || Number(raw) > 2147483647) {
                            showError('State Cache Time must be Always, Until end of task run, or a positive whole number.');
                            return false;
                        }
                        continue;
                    }
                    if (!/^\d+$/.test(raw)) {
                        showError(`${definition?.label || key} must be a whole number.`);
                        return false;
                    }
                    if (key.toLowerCase() === 'port') {
                        const port = Number(raw);
                        if (port < 1 || port > 65535) {
                            showError('Port must be a whole number from 1 through 65535.');
                            return false;
                        }
                    }
                }
                if (Object.prototype.hasOwnProperty.call(payload.configuration, 'DefSetFileAttr')
                    && payload.configuration.DefSetFileAttr === true) {
                    const permissions = String(configurationState.DefDestFileAttr ?? '').trim();
                    if (!/^[0-7]{3}$/.test(permissions)) {
                        showError('Default Destination File Permissions must be a three-digit octal value from 000 through 777.');
                        return false;
                    }
                }
            }
            return true;
        }

        function showError(message) {
            clearSavedStatus();
            if (loading) loading.hidden = true;
            if (!error) return;
            error.textContent = message || 'Unable to complete the Host Editor operation.';
            error.hidden = false;
            updateDirtyState();
        }

        function hideError() {
            if (!error) return;
            error.textContent = '';
            error.hidden = true;
            updateDirtyState();
        }

        async function fetchShell(hostId) {
            const url = new URL(shellUrl, window.location.href);
            url.searchParams.set('id', hostId);
            const headers = { Accept: 'application/json' };
            if (checkout?.id) headers['X-MAUI-Edit-Lease'] = checkout.id;
            const response = await fetch(url.toString(), { headers, cache: 'no-store' });
            if (response.status === 401) { window.location.replace(loginUrl); return null; }
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(payload?.message || `Request failed (${response.status}).`);
            if (String(payload?.host?.id ?? '').trim() !== String(hostId).trim())
                throw new Error('The Host Editor response did not identify the requested Host. Close and reopen the Host Editor.');
            payload.readOnly = payload.readOnly === true || payload.host?.canEditConfiguration === false;
            return payload;
        }

        function updateActionAvailability() {
            const host = current?.host;
            const typeCode = String(host?.typeCode || '').trim().toLowerCase();
            const isLocalFileSystem = String(host?.id || '').trim() === '0'
                || typeCode === 'filesystem'
                || String(host?.type || '').trim().toLowerCase() === 'local file system';
            const canModify = Boolean(host?.canEditDelete) && !isLocalFileSystem;
            if (testAction) testAction.disabled = !host || !onTestHost || saving;
            if (browseAction) {
                browseAction.hidden = document.documentElement.dataset.moveitAdmin !== 'true'
                    || !['FileSystem', 'Share', 'siLock', 'FTP', 'SSHFTP', 'S3', 'AzureBlob', 'SharePoint', 'GoogleStorage', 'OneDrive'].includes(host?.typeCode);
                browseAction.disabled = !host || saving || partialSave || Boolean(checkout?.lost) || !window.MauiHostBrowser;
            }
            if (cloneAction) cloneAction.disabled = !canModify || !onCloneHost;

            if (undoAction) {
                undoAction.disabled = !host || current.readOnly || !localUndo.available() || saving || partialSave;
                undoAction.title = undoAction.disabled ? 'No editor change is available to undo.' : '';
            }
            if (exportAction) {
                exportAction.disabled = !host || saving || isLocalFileSystem || typeof onExportHost !== 'function';
                exportAction.title = exportAction.disabled
                    ? (isLocalFileSystem ? 'The Local File System Host is managed by MOVEit Automation and cannot be exported.' : 'Export is not available for this Host.')
                    : '';
            }
            if (deleteAction) {
                const inUse = Number(host?.assignedCount || 0) > 0 || Boolean(host?.tasks?.length);
                deleteAction.disabled = !canModify || inUse || typeof onDeleteHost !== 'function';
                deleteAction.title = deleteAction.disabled
                    ? (isLocalFileSystem ? 'The Local File System Host cannot be deleted.' : inUse ? 'This Host is used by Tasks and cannot be deleted.' : 'Delete is not available for this Host.')
                    : '';
            }
        }

        async function runSavedHostAction(callback) {
            closeActions();
            if (typeof callback !== 'function' || !current?.host) return false;
            if (isDirty()) {
                const saved = await save();
                if (!saved) return false;
            }
            return await callback(current.host);
        }

        async function runClosedHostAction(callback) {
            closeActions();
            if (typeof callback !== 'function' || !current?.host) return false;
            const host = { ...current.host };
            const closed = await requestClose();
            if (!closed) return false;
            await callback(host);
            return true;
        }

        function renderModel(payload) {
            rendering=true;
            current = payload;
            current.readOnly = current.readOnly === true || current.host?.canEditConfiguration === false;
            autoSave = !payload.readOnly && Boolean(payload.preferences?.autoSave);
            if (autoSaveToggle) autoSaveToggle.checked = autoSave;
            updateSaveButton();
            updateActionAvailability();
            initializeConfigurationState(payload);
            renderInformation(payload);
            renderConfiguration(payload);
            renderTasks(payload);
            renderAudit(payload);
            wireDirtyTracking();
            switchTab('configuration');
            if (loading) loading.hidden = true;
            if (content) content.hidden = false;
            baselinePayload = stablePayload(collectPayload());
            localUndo.rebase(); rendering=false;
            updateDirtyState();
        }

        async function open(hostId) {
            const normalized = String(hostId || '').trim();
            if (!normalized || !shellUrl || saving) return false;
            await checkout?.close();
            reset();
            dialog.showModal();
            const sequence = ++loadSequence;
            try {
                let payload = await fetchShell(normalized);
                if (!payload || sequence !== loadSequence) return false;
                if (payload.checkoutObjectsForDurationOfEdit && !checkout) throw new Error('Checkout support is unavailable. Reload MAUI before editing.');
                if (await checkout?.open(normalized, !payload.readOnly && (payload.host?.canEditMoveItFields || document.documentElement?.dataset?.moveitAdmin === 'true'), payload.checkoutObjectsForDurationOfEdit))
                    payload = await fetchShell(normalized);
                if (!payload || sequence !== loadSequence) return false;
                renderModel(payload);
                return true;
            } catch (loadError) {
                if (sequence !== loadSequence) return false;
                if (loading) loading.hidden = true;
                await checkout?.close();
                showError(loadError?.message || 'Unable to load Host information.');
                return false;
            }
        }

        async function performSave(automatic = false) {
            if (current?.readOnly || !current?.host?.id || !saveUrl || saving || partialSave || checkout?.lost || fingerprintBusy || picker?.open || hostBrowser?.isOpen() || !isDirty()) return false;
            hideError();
            autoSaveBlocked = stablePayload(collectPayload());
            if (!validateBeforeSave()) return false;

            const selection = { tab: tabs.find(tab => tab.classList.contains('active'))?.dataset.hostEditorTab || 'configuration', section: activeConfigurationSection };
            clearSavedStatus();
            saving = true;
            updateDirtyState();
            let serverSaveSucceeded = false;
            let writeAttempted = false;
            let safeRejection = false;
            if (content) content.inert = true;
            if (saveButton) { saveButton.disabled = true; saveButton.textContent = 'Saving…'; }
            const hostId = String(current.host.id).trim();
            try {
                const url = new URL(saveUrl, window.location.href);
                url.searchParams.set('id', hostId);
                const headers = new Headers({ Accept: 'application/json', 'Content-Type': 'application/json' });
                if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
                if (checkout?.id) headers.set('X-MAUI-Edit-Lease', checkout.id);
                const body = JSON.stringify(collectPayload());
                writeAttempted = true;
                const response = await fetch(url.toString(), { method: 'POST', headers, body });
                if (response.status === 401) { window.location.replace(loginUrl); return false; }
                const result = await response.json();
                if (!response.ok) {
                    if (result?.leaseLost || result?.checkoutConflict) checkout?.invalidate(result.message);
                    checkoutConflict = Boolean(result?.checkoutConflict || result?.editConflict && result?.moveItSaved === false);
                    safeRejection = result?.moveItSaved === false;
                    if (result?.moveItSaved === true || result?.editConflict) partialSave = true;
                    throw new Error(result?.message || `Request failed (${response.status}).`);
                }
                if (result?.success !== true || String(result?.hostId || '').trim() !== hostId)
                    throw new Error('The Host save acknowledgement was incomplete or did not match the requested Host.');
                serverSaveSucceeded = true;

                const refreshed = await fetchShell(hostId);
                if (!refreshed) { partialSave = true; return false; }
                if (informationFields) informationFields.replaceChildren();
                if (metadata) metadata.replaceChildren();
                if (configTabs) configTabs.replaceChildren();
                if (configFields) configFields.replaceChildren();
                if (tasks) tasks.replaceChildren();
                if (audit) audit.replaceChildren();
                partialSave = false; checkoutConflict = false; manualRecoverySave = false;
                autoSaveBlocked = '';
                renderModel(refreshed);
                switchTab(selection.tab);
                if (selection.section) showConfigurationSection(selection.section);
                try {
                    await onSaved?.(result);
                } catch (callbackError) {
                    console.warn('Host saved, but the Hosts grid refresh callback did not complete.', callbackError);
                }
                return true;
            } catch (saveError) {
                if (serverSaveSucceeded) {
                    partialSave = true;
                    showError('The Host was saved, but the editor could not reload the authoritative saved state. Use Reload Host before saving again; unsaved changes in this editor will be discarded.');
                } else if (partialSave) {
                    showError((saveError?.message || 'The Host save needs review.') + ' ' + (window.MauiObjectEditLease?.blockedMessage?.('Host') || 'Saving is blocked. Reload Host to discard unsaved changes and load the current MOVEit copy.'));
                } else if (writeAttempted && !safeRejection) {
                    partialSave = true;
                    showError('The Host save result could not be confirmed. Use Reload Host before saving again; unsaved changes in this editor will be discarded.');
                } else {
                    showError(saveError?.message || 'Unable to save the Host.');
                }
                return false;
            } finally {
                saving = false;
                if (content) content.inert = false;
                updateSaveButton();
                if (serverSaveSucceeded && !partialSave && !isDirty() && (!error || error.hidden)) {
                    const sequence = loadSequence;
                    savedStatusTimer = window.setTimeout(() => {
                        if (sequence !== loadSequence) return;
                        savedStatusTimer = 0;
                        if (dialog.open) updateDirtyState();
                    }, 2200);
                }
                updateDirtyState();
            }
        }

        function closeActions() {
            if (actionsMenu) actionsMenu.hidden = true;
            actionsButton?.setAttribute('aria-expanded', 'false');
        }

        async function reloadHost() {
            if ((!partialSave && !checkout?.lost) || saving || fingerprintBusy || picker?.open || hostBrowser?.isOpen()) return false;
            const signature = stablePayload(collectPayload());
            const confirmed = await window.mauiConfirm?.({ title: 'Reload Host?',
                message: 'Load the current Host from MOVEit? Any unsaved changes in this editor will be discarded.', confirmText: 'Reload Host', danger: true });
            if (!confirmed || saving || signature !== stablePayload(collectPayload())) return false;
            saving = true; if (content) content.inert = true; updateDirtyState();
            try {
                localUndo.rebase(true);
                const id = current.host.id;
                if (checkout?.lost || checkoutConflict) await checkout.open(id, true, current.checkoutObjectsForDurationOfEdit || Boolean(checkout.id));
                const refreshed = await fetchShell(id);
                if (!refreshed) throw new Error('The current Host could not be loaded.');
                for (const panel of [informationFields, metadata, configTabs, configFields, tasks, audit]) panel?.replaceChildren();
                partialSave = false; checkoutConflict = false; manualRecoverySave = false; autoSaveBlocked = '';
                hideError(); renderModel(refreshed);
                return true;
            } catch (reloadError) { showError('Unable to reload the Host. Unsaved changes remain in this editor. ' + reloadError.message); return false; }
            finally { saving = false; if (content) content.inert = false; updateDirtyState(); }
        }

        async function overrideCheckout() {
            if (!(checkoutConflict || checkout?.lost || partialSave) || saving || fingerprintBusy || picker?.open || hostBrowser?.isOpen() || !checkout?.override) return false;
            const signature = stablePayload(collectPayload());
            saving = true; if (content) content.inert = true; updateDirtyState();
            try {
                const revision = await checkout.override(current.host.id);
                if (!revision) return false;
                if (signature !== stablePayload(collectPayload())) throw new Error('Editor values changed during recovery; try again.');
                current.host.editVersion = revision;
                if (baselinePayload) baselinePayload = stablePayload({ ...JSON.parse(baselinePayload), editVersion: revision });
                partialSave = false; checkoutConflict = false; manualRecoverySave = true;
                autoSaveBlocked = ''; hideError();
                return true;
            } catch (recoveryError) { showError(recoveryError.message); return false; }
            finally { saving = false; if (content) content.inert = false; updateDirtyState(); }
        }

        async function requestClose() {
            if (saving || activeSavePromise) {
                if (!autoSave || !activeSavePromise || !await activeSavePromise) return false;
            }
            if (autoSave && !manualRecoverySave && !partialSave && !checkout?.lost && isDirty()) {
                if (!await save()) return false;
            }
            if (isDirty()) {
                const confirmed = await window.mauiConfirm?.({
                    title: 'Discard Host Changes?',
                    message: 'You have unsaved changes in the Host Editor. Close without saving them?',
                    confirmText: 'Discard Changes', danger: true
                });
                if (confirmed !== true) return false;
            }
            ++loadSequence;
            window.clearTimeout(autoSaveTimer);
            dialog.inert = true;
            await checkout?.close();
            closePicker();
            closeActions();
            if (dialog.open) dialog.close();
            dialog.inert = false;
            return true;
        }

        picker?.querySelectorAll('[data-host-picker-cancel]').forEach(button => button.addEventListener('click', closePicker));
        picker?.addEventListener('cancel', event => { event.preventDefault(); closePicker(); });
        picker?.querySelector('[data-host-picker-refresh]')?.addEventListener('click', () => { void refreshPicker(); });
        picker?.querySelector('[data-host-picker-ok]')?.addEventListener('click', applyPicker);

        tabs.forEach(tab => tab.addEventListener('click', () => { if (!saving) switchTab(tab.dataset.hostEditorTab || 'configuration'); }));
        dialog.querySelectorAll('[data-host-editor-close], [data-host-editor-cancel]').forEach(button => button.addEventListener('click', () => { void requestClose(); }));
        saveButton?.addEventListener('click', () => { void (autoSave && !manualRecoverySave ? requestClose() : save()); });
        reloadButton?.addEventListener('click', reloadHost);
        overrideButton?.addEventListener('click', overrideCheckout);
        autoSaveToggle?.addEventListener('change', async () => {
            const requested = Boolean(autoSaveToggle.checked);
            try {
                const url = document.documentElement.dataset.hostEditorPreferencesUrl;
                if (!url) throw new Error('The Host Auto Save preference endpoint is unavailable.');
                const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json', RequestVerificationToken: antiForgeryToken }, body: JSON.stringify({ autoSave: requested }) });
                const result = await response.json();
                if (!response.ok) throw new Error(result?.message || 'Unable to save the Auto Save preference.');
                autoSave = Boolean(result?.autoSave);
                if (current) current.preferences = { autoSave };
                autoSaveToggle.checked = autoSave; autoSaveBlocked = ''; hideError(); updateDirtyState();
            } catch (error) { autoSaveToggle.checked = autoSave; showError(error?.message || 'Unable to save the Auto Save preference.'); }
        });
        testAction?.addEventListener('click', () => {
            closeActions();
            const host = current?.host;
            if (host && onTestHost && !saving) onTestHost(host, checkout?.id || '');
        });
        browseAction?.addEventListener('click', () => {
            closeActions();
            const host = current?.host;
            const endpoint = document.documentElement.dataset.hostBrowseUrl;
            if (!host || browseAction.disabled || browseAction.hidden || !endpoint || hostBrowser?.isOpen()) return;
            window.clearTimeout(autoSaveTimer);
            let root = { name: '', id: '', kind: '' };
            const rootItem = item => ['Bucket', 'Container', 'DocumentLibrary'].includes(item?.nativeType);
            hostBrowser = window.MauiHostBrowser.open({
                title: 'Browse Host', hostName: host.name, path: '', inspect: true,
                load: async (directory, signal) => {
                    const url = new URL(endpoint, window.location.href); url.searchParams.set('id', host.id);
                    const headers = { Accept: 'application/json', 'Content-Type': 'application/json', RequestVerificationToken: antiForgeryToken };
                    if (checkout?.id) headers['X-MAUI-Edit-Lease'] = checkout.id;
                    const response = await fetch(url, { method: 'POST', cache: 'no-store', headers, signal,
                        body: JSON.stringify({ directory, rootName: root.name, rootId: root.id }) });
                    const data = await response.json();
                    if (response.status === 401) { window.location.replace(loginUrl); throw new Error('MOVEit session expired.'); }
                    if (!response.ok) { if (data.leaseLost || data.checkoutConflict) checkout?.invalidate(data.message); throw new Error(data.message || 'Unable to browse this Host.'); }
                    root = { name: data.rootName ?? root.name, id: data.rootId ?? root.id, kind: data.rootKind || root.kind };
                    return { currentPath: data.currentDirectory, parentPath: data.parentDirectory,
                        hasParent: Boolean(data.parentDirectory || root.name), hostName: data.hostName,
                        items: (data.items || []).map(item => ({ ...item, isFolder: item.isDirectory })) };
                },
                openFolder: item => {
                    if (!rootItem(item)) return item.path;
                    root = { name: item.name, id: item.nativeId || '', kind: item.nativeType };
                    return item.nativeType === 'DocumentLibrary' ? '' : '/';
                },
                parent: path => { if (path) return path; root = { name: '', id: '', kind: root.kind }; return ''; },
                onClose: () => { hostBrowser = null; updateDirtyState(); scheduleAutoSave(); }
            });
            updateDirtyState();
        });
        undoAction?.addEventListener('click', () => { closeActions(); if (!undoAction.disabled) void localUndo.undo(); });
        exportAction?.addEventListener('click', () => { void runSavedHostAction(onExportHost); });
        deleteAction?.addEventListener('click', () => { if (!deleteAction.disabled) void runClosedHostAction(onDeleteHost); });
        cloneAction?.addEventListener('click', async () => {
            if (!onCloneHost || saving || partialSave || fingerprintBusy || picker?.open || hostBrowser?.isOpen() || !current?.host?.canEditDelete || String(current.host.id) === '0') return;
            // Keep the source editor and its lease while the Clone modal is open. Cancellation
            // returns here; successful creation opens the clone through the existing editor.
            await runSavedHostAction(onCloneHost);
        });
        actionsButton?.addEventListener('click', event => {
            event.stopPropagation();
            if (saving) return;
            if (!actionsMenu) return;
            actionsMenu.hidden = !actionsMenu.hidden;
            actionsButton.setAttribute('aria-expanded', actionsMenu.hidden ? 'false' : 'true');
        });
        dialog.addEventListener('cancel', event => { event.preventDefault(); void requestClose(); });
        document.addEventListener('click', event => {
            if (!event.target.closest('[data-host-editor-actions]') && !event.target.closest('[data-host-editor-actions-menu]')) closeActions();
        });

        window.MauiModalUI?.register(dialog, { error: showError, clear: hideError, footerStatus: dirtyState });
        window.MauiModalUI?.registerContext('host-editor', dialog, {
            capture: () => ({ id: current?.host?.id, tab: tabs.find(tab => tab.classList.contains('active'))?.dataset.hostEditorTab,
                section: activeConfigurationSection }),
            prepare: async () => {
                if (saving || partialSave || fingerprintBusy || picker?.open || hostBrowser?.isOpen()) { showError('Finish or reopen the Host Editor before opening another editor.'); return false; }
                return !isDirty() || await save();
            },
            restore: async state => {
                if (await open(state.id)) { switchTab(state.tab || 'configuration'); if (state.section) showConfigurationSection(state.section); }
            }
        });
        dialog.addEventListener('close', () => { if (dialog.open) return; hostBrowser?.close(); window.clearTimeout(autoSaveTimer); void checkout?.close(); clearSavedStatus(); onClosed?.(); });
        return { open, close: requestClose, save, getCurrent: () => current };
    }

    window.MauiHostEditor = { initialize };
})();

