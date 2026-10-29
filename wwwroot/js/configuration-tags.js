/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Configuration > Settings > Tags management.
 */

(() => {
    const root = document.documentElement;
    const section = document.querySelector('[data-config-section="tags"]');
    if (!section) return;

    const rows = section.querySelector('[data-tag-rows]');
    const empty = section.querySelector('[data-tag-empty]');
    const error = section.querySelector('[data-tag-error]');
    const createButton = section.querySelector('[data-tag-create]');
    const editor = document.querySelector('[data-tag-editor-dialog]');
    const editorTitle = editor?.querySelector('[data-tag-editor-title]');
    const editorError = editor?.querySelector('[data-tag-editor-error]');
    const editorClose = editor?.querySelector('[data-tag-editor-close]');
    const editorCancel = editor?.querySelector('[data-tag-editor-cancel]');
    const editorSave = editor?.querySelector('[data-tag-editor-save]');
    const nameInput = editor?.querySelector('[data-tag-name]');
    const colorInput = editor?.querySelector('[data-tag-color]');
    const colorPicker = editor?.querySelector('[data-tag-color-picker]');
    const colorWarning = editor?.querySelector('[data-tag-color-warning]');
    const preview = editor?.querySelector('[data-tag-preview]');
    const deleteDialog = document.querySelector('[data-tag-delete-dialog]');
    const deleteName = deleteDialog?.querySelector('[data-tag-delete-name]');
    const deleteClose = deleteDialog?.querySelector('[data-tag-delete-close]');
    const deleteCancel = deleteDialog?.querySelector('[data-tag-delete-cancel]');
    const deleteConfirm = deleteDialog?.querySelector('[data-tag-delete-confirm]');
    const token = document.querySelector('.global-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const urls = {
        list: String(root.dataset.tagsListUrl || ''),
        create: String(root.dataset.tagsCreateUrl || ''),
        update: String(root.dataset.tagsUpdateUrl || ''),
        delete: String(root.dataset.tagsDeleteUrl || '')
    };

    let tags = [];
    let activeTag = null;
    let pendingDelete = null;
    let editorBusy = false;
    const updateEditorAction = window.MauiModalUI.watchAction(editor, editorSave, () => !editorBusy,
        () => Boolean(nameInput?.value.trim()) && !nameInput.value.includes(';') && Boolean(normalizeColor(colorInput?.value))
            && (!activeTag || nameInput.value.trim() !== activeTag.name || normalizeColor(colorInput.value) !== normalizeColor(activeTag.color)));

    function showError(message) {
        if (!error) return;
        error.textContent = message || '';
        error.hidden = !message;
    }

    function showEditorError(message) {
        if (!editorError) return;
        editorError.textContent = message || '';
        editorError.hidden = !message;
    }

    function normalizeColor(value) {
        const raw = String(value || '').trim().toUpperCase();
        const hex = raw.startsWith('#') ? raw : `#${raw}`;
        return /^#[0-9A-F]{6}$/.test(hex) ? hex : '';
    }

    function textColor(color) {
        const hex = normalizeColor(color).slice(1);
        if (!hex) return '#ffffff';
        const r = parseInt(hex.slice(0, 2), 16), g = parseInt(hex.slice(2, 4), 16), b = parseInt(hex.slice(4, 6), 16);
        return ((r * 299 + g * 587 + b * 114) / 1000) >= 155 ? '#111111' : '#ffffff';
    }

    function pill(tag) {
        const value = document.createElement('span');
        value.className = 'metadata-tag-pill';
        value.style.backgroundColor = tag.color || '#808080';
        value.style.color = textColor(tag.color);
        value.textContent = tag.name || '';
        return value;
    }

    async function request(url, payload = null) {
        const headers = new Headers({ Accept: 'application/json' });
        const options = { credentials: 'same-origin', headers };
        if (payload !== null) {
            options.method = 'POST';
            headers.set('Content-Type', 'application/json');
            if (token) headers.set('RequestVerificationToken', token);
            options.body = JSON.stringify(payload);
        }
        const response = await fetch(url, options);
        if (response.status === 401) {
            window.mauiSessionExpired?.();
            throw new Error('Your session has expired.');
        }
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
        return data;
    }

    function renderRows() {
        rows?.replaceChildren();
        if (empty) empty.hidden = tags.length > 0;
        if (!rows) return;
        tags.forEach(tag => {
            const row = document.createElement('tr');
            const tagCell = document.createElement('td');
            tagCell.appendChild(pill(tag));
            const colorCell = document.createElement('td');
            const color = document.createElement('span');
            color.className = 'tag-color-value';
            const swatch = document.createElement('span');
            swatch.className = 'tag-color-swatch';
            swatch.style.backgroundColor = tag.color;
            const code = document.createElement('span');
            code.textContent = tag.color;
            color.append(swatch, code);
            colorCell.appendChild(color);
            const countCell = document.createElement('td');
            countCell.textContent = Number(tag.useCount || 0).toLocaleString();
            const actionCell = document.createElement('td');
            const actions = document.createElement('div');
            actions.className = 'configuration-inline-actions tag-row-actions';
            const edit = document.createElement('button');
            edit.type = 'button'; edit.className = 'secondary-button'; edit.textContent = 'Edit';
            edit.addEventListener('click', () => openEditor(tag));
            const del = document.createElement('button');
            del.type = 'button'; del.className = 'configuration-remove-button'; del.textContent = 'Delete';
            const inUse = Number(tag.useCount || 0) > 0;
            del.disabled = inUse;
            del.title = inUse ? `This Tag is assigned to ${Number(tag.useCount).toLocaleString()} item(s) and cannot be deleted.` : 'Delete this Tag';
            del.addEventListener('click', () => openDelete(tag));
            actions.append(edit, del);
            actionCell.appendChild(actions);
            row.append(tagCell, colorCell, countCell, actionCell);
            rows.appendChild(row);
        });
    }

    async function load() {
        showError('');
        try {
            const separator = urls.list.includes('?') ? '&' : '?';
            const data = await request(`${urls.list}${separator}includeUsage=true`);
            tags = Array.isArray(data?.tags) ? data.tags : [];
            window.mauiInvalidateTags?.();
            renderRows();
        } catch (e) {
            rows?.replaceChildren();
            if (empty) empty.hidden = true;
            showError(e?.message || 'Tags could not be loaded.');
        }
    }

    function updatePreview() {
        if (!preview) return;
        preview.replaceChildren();
        const name = String(nameInput?.value || '').trim() || 'Tag';
        const color = normalizeColor(colorInput?.value) || '#808080';
        preview.appendChild(pill({ name, color }));
        const duplicateColor = tags.find(tag => tag.id !== activeTag?.id && String(tag.color || '').toUpperCase() === color.toUpperCase());
        if (colorWarning) {
            colorWarning.textContent = duplicateColor ? `This color is already used by Tag '${duplicateColor.name}'. You may still save this Tag with the same color.` : '';
            colorWarning.hidden = !duplicateColor;
        }
    }

    function openEditor(tag = null) {
        activeTag = tag;
        showEditorError('');
        if (editorTitle) editorTitle.textContent = tag ? 'Edit Tag' : 'Create Tag';
        if (editorSave) editorSave.textContent = tag ? 'Save Changes' : 'Create Tag';
        if (nameInput) nameInput.value = tag?.name || '';
        if (colorInput) colorInput.value = tag?.color || '#3E8AC7';
        if (colorPicker) colorPicker.value = normalizeColor(tag?.color || '#3E8AC7') || '#3E8AC7';
        updatePreview();
        editor?.showModal();
        window.setTimeout(() => nameInput?.focus(), 0);
    }

    function closeEditor() {
        activeTag = null;
        if (editor?.open) editor.close();
    }

    async function saveEditor() {
        if (editorBusy || editorSave?.disabled) return;
        const name = String(nameInput?.value || '').trim();
        const color = normalizeColor(colorInput?.value);
        showEditorError('');
        if (!name) { showEditorError('Tag Name is required.'); nameInput?.focus(); return; }
        if (name.includes(';')) { showEditorError('Tag Name cannot contain a semicolon.'); nameInput?.focus(); return; }
        const duplicateName = tags.find(tag => tag.id !== activeTag?.id && String(tag.name || '').toLowerCase() === name.toLowerCase());
        if (duplicateName) { showEditorError(`Tag '${name}' already exists.`); nameInput?.focus(); return; }
        if (!color) { showEditorError('Enter a valid six-digit hexadecimal color.'); colorInput?.focus(); return; }
        const wasEditing = Boolean(activeTag);
        editorBusy = true; updateEditorAction();
        try {
            await request(activeTag ? urls.update : urls.create, { id: activeTag?.id || '', name, color });
            closeEditor();
            await load();
            window.mauiShowToast?.(wasEditing ? 'Tag updated.' : 'Tag created.');
        } catch (e) {
            showEditorError(e?.message || 'The Tag could not be saved.');
        } finally {
            editorBusy = false; updateEditorAction();
        }
    }

    function openDelete(tag) {
        if (Number(tag?.useCount || 0) > 0) return;
        pendingDelete = tag;
        if (deleteName) deleteName.textContent = tag.name;
        deleteDialog?.showModal();
    }

    function closeDelete() {
        pendingDelete = null;
        if (deleteDialog?.open) deleteDialog.close();
    }

    async function confirmDelete() {
        if (!pendingDelete) return;
        deleteConfirm.disabled = true;
        try {
            await request(urls.delete, { id: pendingDelete.id });
            closeDelete();
            await load();
            window.mauiShowToast?.('Tag deleted.');
        } catch (e) {
            closeDelete();
            showError(e?.message || 'The Tag could not be deleted.');
        } finally {
            deleteConfirm.disabled = false;
        }
    }

    createButton?.addEventListener('click', () => openEditor());
    editorClose?.addEventListener('click', closeEditor);
    editorCancel?.addEventListener('click', closeEditor);
    editorSave?.addEventListener('click', saveEditor);
    nameInput?.addEventListener('input', updatePreview);
    colorInput?.addEventListener('input', () => {
        const normalized = normalizeColor(colorInput.value);
        if (normalized && colorPicker) colorPicker.value = normalized;
        updatePreview();
    });
    colorPicker?.addEventListener('input', () => {
        if (colorInput) colorInput.value = String(colorPicker.value || '').toUpperCase();
        updatePreview();
    });
    deleteClose?.addEventListener('click', closeDelete);
    deleteCancel?.addEventListener('click', closeDelete);
    deleteConfirm?.addEventListener('click', confirmDelete);
    editor?.addEventListener('cancel', event => { event.preventDefault(); closeEditor(); });
    deleteDialog?.addEventListener('cancel', event => { event.preventDefault(); closeDelete(); });

    void load();
})();
