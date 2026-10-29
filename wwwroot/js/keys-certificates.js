/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 */
(() => {
    'use strict';

    const config = document.querySelector('[data-key-certificate-page-config]');
    if (!config) return;
    const grid = document.querySelector('[data-grid="keysandcertificates"]');
    const toast = grid?.querySelector('[data-prototype-toast]');
    const antiForgeryToken = document.querySelector('.task-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    // Purpose: Builds the URL for one controller endpoint.
    const endpoint = name => String(config.dataset[name] || '');

    // Purpose: Navigates to a report while preserving the relevant return context.
    function navigateToReport(baseUrl, parameters) {
        const target = String(baseUrl || '').trim();
        if (!target) return;
        const url = new URL(target, window.location.href);
        Object.entries(parameters || {}).forEach(([key, value]) => {
            const text = String(value ?? '').trim();
            if (text) url.searchParams.set(key, text);
        });
        const returnUrl = window.MauiGridNavigationState?.createReturnUrl?.('keysandcertificates')
            || `${window.location.pathname}${window.location.search}${window.location.hash}`;
        url.searchParams.set('returnUrl', returnUrl);
        url.searchParams.set('returnLabel', 'Keys & Certificates');
        window.location.assign(url.toString());
    }

    // Purpose: Displays a transient workspace status message.
    function showStatus(message, duration = 5000, kind = 'default', origin = 'modal') {
        if (typeof window.mauiDisplayToast === 'function') { window.mauiDisplayToast(toast, message, kind, duration, origin); return; }
        if (!toast) return;
        toast.textContent = message || '';
        toast.classList.toggle('error', kind === 'error');
        toast.classList.add('visible');
        clearTimeout(showStatus.timer);
        showStatus.timer = setTimeout(() => toast.classList.remove('visible'), duration);
    }

    // Purpose: Calls a Keys/Certificates endpoint and normalizes JSON/error handling.
    async function fetchJson(url, options = {}) {
        const headers = new Headers(options.headers || {});
        headers.set('Accept', 'application/json');
        if (options.body != null && !(options.body instanceof FormData)) headers.set('Content-Type', 'application/json');
        if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
        const response = await (window.MauiMoveItGridConnection?.fetch || window.fetch.bind(window))(url, { ...options, headers });
        if (response.status === 401) {
            window.location.replace(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
            throw new Error('MOVEit session expired.');
        }
        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json') ? await response.json() : null;
        if (!response.ok) throw new Error(payload?.message || `Request failed (${response.status}).`);
        return payload || {};
    }

    // ---------------------------------------------------------------------
    // New SSH Key
    // ---------------------------------------------------------------------
    const newDialog = document.querySelector('[data-new-ssh-key-dialog]');
    const newForm = newDialog?.querySelector('[data-new-ssh-key-form]');
    const newName = newDialog?.querySelector('[data-new-ssh-key-name]');
    const newType = newDialog?.querySelector('[data-new-ssh-key-type]');
    const newSize = newDialog?.querySelector('[data-new-ssh-key-size]');
    const newError = newDialog?.querySelector('[data-new-ssh-key-error]');
    const newSubmit = newDialog?.querySelector('[data-new-ssh-key-submit]');
    const newSpinner = newDialog?.querySelector('[data-new-ssh-key-spinner]');
    const newSubmitLabel = newDialog?.querySelector('[data-new-ssh-key-submit-label]');
    const newCancel = newDialog?.querySelector('[data-new-ssh-key-cancel]');
    const newClose = newDialog?.querySelector('[data-new-ssh-key-close]');
    let newBusy = false;
    const updateNewAction = window.MauiModalUI.watchAction(newDialog, newSubmit,
        () => !newBusy, () => Boolean(newName?.value.trim()) && window.MauiModalUI.formValid(newDialog));

    const sshSizes = {
        RSA: [1024, 2048, 3072, 4096],
        DSA: [1024, 2048, 3072, 4096],
        ECDSA: [256, 384, 521],
        Ed25519: [255]
    };

    // Purpose: Populates SSH sizes.
    function populateSshSizes() {
        if (!newSize) return;
        const type = String(newType?.value || 'RSA');
        const sizes = sshSizes[type] || sshSizes.RSA;
        newSize.replaceChildren(...sizes.map(size => {
            const option = document.createElement('option');
            option.value = String(size);
            option.textContent = String(size);
            return option;
        }));
    }

    // Purpose: Sets new busy.
    function setNewBusy(busy) {
        newBusy = busy;
        [newSubmit, newCancel, newClose, newName, newType, newSize].forEach(control => { if (control) control.disabled = busy; });
        if (newSpinner) newSpinner.hidden = !busy;
        if (newSubmitLabel) newSubmitLabel.textContent = busy ? 'Creating…' : 'Create SSH Key';
        updateNewAction();
    }

    // Purpose: Opens new SSH dialog.
    function openNewSshDialog() {
        if (!newDialog) return;
        setNewBusy(false);
        if (newName) newName.value = '';
        if (newType) newType.value = 'RSA';
        populateSshSizes();
        if (newError) { newError.textContent = ''; newError.hidden = true; }
        newDialog.showModal();
        requestAnimationFrame(() => newName?.focus());
    }

    // Purpose: Creates an SSH client key from the current dialog values.
    async function createSshKey() {
        if (newBusy) return;
        const name = String(newName?.value || '').trim();
        const keyType = String(newType?.value || '').trim();
        const keySize = Number(newSize?.value || 0);
        if (!name) { if (newError) { newError.textContent = 'Name is required.'; newError.hidden = false; } newName?.focus(); return; }
        setNewBusy(true);
        if (newError) newError.hidden = true;
        try {
            const data = await fetchJson(endpoint('createSshKeyUrl'), { method: 'POST', body: JSON.stringify({ resourceGroup: window.MauiObjectAccess?.group(document.querySelector('[data-new-ssh-key-name]')?.closest('dialog')) || '', name, keyType, keySize }) });
            newDialog?.close();
            showStatus(`Created SSH Client Key ${data?.item?.name || name}.`, 5000);
            await refreshGrid(true);
        } catch (error) {
            if (newError) { newError.textContent = error?.message || 'Unable to create the SSH Client Key.'; newError.hidden = false; }
        } finally { setNewBusy(false); }
    }

    // ---------------------------------------------------------------------
    // New TLS Certificate
    // ---------------------------------------------------------------------
    const newTlsDialog = document.querySelector('[data-new-tls-certificate-dialog]');
    const newTlsForm = newTlsDialog?.querySelector('[data-new-tls-certificate-form]');
    const newTlsName = newTlsDialog?.querySelector('[data-new-tls-certificate-name]');
    const newTlsEmail = newTlsDialog?.querySelector('[data-new-tls-certificate-email]');
    const newTlsCountry = newTlsDialog?.querySelector('[data-new-tls-certificate-country]');
    const newTlsState = newTlsDialog?.querySelector('[data-new-tls-certificate-state]');
    const newTlsOrganization = newTlsDialog?.querySelector('[data-new-tls-certificate-organization]');
    const newTlsOrganizationalUnit = newTlsDialog?.querySelector('[data-new-tls-certificate-organizational-unit]');
    const newTlsExpiresAfter = newTlsDialog?.querySelector('[data-new-tls-certificate-expires-after]');
    const newTlsExpireUnit = newTlsDialog?.querySelector('[data-new-tls-certificate-expire-unit]');
    const newTlsKeySize = newTlsDialog?.querySelector('[data-new-tls-certificate-key-size]');
    const newTlsError = newTlsDialog?.querySelector('[data-new-tls-certificate-error]');
    const newTlsSubmit = newTlsDialog?.querySelector('[data-new-tls-certificate-submit]');
    const newTlsSpinner = newTlsDialog?.querySelector('[data-new-tls-certificate-spinner]');
    const newTlsSubmitLabel = newTlsDialog?.querySelector('[data-new-tls-certificate-submit-label]');
    const newTlsCancel = newTlsDialog?.querySelector('[data-new-tls-certificate-cancel]');
    const newTlsClose = newTlsDialog?.querySelector('[data-new-tls-certificate-close]');
    let newTlsBusy = false;
    const updateNewTlsAction = window.MauiModalUI.watchAction(newTlsDialog, newTlsSubmit,
        () => !newTlsBusy, () => Boolean(newTlsName?.value.trim()) && window.MauiModalUI.formValid(newTlsDialog));

    // Purpose: Sets new TLS busy.
    function setNewTlsBusy(busy) {
        newTlsBusy = busy;
        [newTlsSubmit, newTlsCancel, newTlsClose, newTlsName, newTlsEmail, newTlsCountry, newTlsState, newTlsOrganization, newTlsOrganizationalUnit, newTlsExpiresAfter, newTlsExpireUnit, newTlsKeySize]
            .forEach(control => { if (control) control.disabled = busy; });
        if (newTlsSpinner) newTlsSpinner.hidden = !busy;
        if (newTlsSubmitLabel) newTlsSubmitLabel.textContent = busy ? 'Creating…' : 'Create TLS Certificate';
        updateNewTlsAction();
    }

    // Purpose: Displays new TLS error.
    function showNewTlsError(message, control) {
        if (newTlsError) { newTlsError.textContent = message; newTlsError.hidden = false; }
        control?.focus();
    }

    // Purpose: Opens new TLS certificate dialog.
    function openNewTlsCertificateDialog() {
        if (!newTlsDialog) return;
        setNewTlsBusy(false);
        if (newTlsName) newTlsName.value = '';
        if (newTlsEmail) newTlsEmail.value = '';
        if (newTlsCountry) newTlsCountry.value = '';
        if (newTlsState) newTlsState.value = '';
        if (newTlsOrganization) newTlsOrganization.value = '';
        if (newTlsOrganizationalUnit) newTlsOrganizationalUnit.value = '';
        if (newTlsExpiresAfter) newTlsExpiresAfter.value = '2';
        if (newTlsExpireUnit) newTlsExpireUnit.value = 'Years';
        if (newTlsKeySize) newTlsKeySize.value = '1024';
        if (newTlsError) { newTlsError.textContent = ''; newTlsError.hidden = true; }
        newTlsDialog.showModal();
        requestAnimationFrame(() => newTlsName?.focus());
    }

    // Purpose: Creates a TLS certificate from the current dialog values.
    async function createTlsCertificate() {
        if (newTlsBusy) return;

        const name = String(newTlsName?.value || '').trim();
        const emailAddress = String(newTlsEmail?.value || '').trim();
        const country = String(newTlsCountry?.value || '').trim();
        const state = String(newTlsState?.value || '').trim();
        const organization = String(newTlsOrganization?.value || '').trim();
        const organizationalUnit = String(newTlsOrganizationalUnit?.value || '').trim();
        const expiresAfter = Number(newTlsExpiresAfter?.value || 0);
        const expireUnit = String(newTlsExpireUnit?.value || '').trim();
        const keySize = Number(newTlsKeySize?.value || 0);

        if (!name) { showNewTlsError('Name is required.', newTlsName); return; }
        if (newTlsEmail && emailAddress && !newTlsEmail.validity.valid) { showNewTlsError('Enter a valid Email Address.', newTlsEmail); return; }
        if (!Number.isInteger(expiresAfter) || expiresAfter < 1 || expiresAfter > 12) { showNewTlsError('Expires After must be a value from 1 through 12.', newTlsExpiresAfter); return; }
        if (!['Years', 'Months'].includes(expireUnit)) { showNewTlsError('Expiration Unit must be Years or Months.', newTlsExpireUnit); return; }
        if (![1024, 2048, 3072, 4096].includes(keySize)) { showNewTlsError('Key Size must be 1024, 2048, 3072, or 4096.', newTlsKeySize); return; }

        setNewTlsBusy(true);
        if (newTlsError) newTlsError.hidden = true;
        try {
            await fetchJson(endpoint('createTlsCertificateUrl'), {
                method: 'POST',
                body: JSON.stringify({ resourceGroup: window.MauiObjectAccess?.group(document.querySelector('[data-new-tls-certificate-name]')?.closest('dialog')) || '', name, emailAddress, country, state, organization, organizationalUnit, expiresAfter, expireUnit, keySize })
            });
            newTlsDialog?.close();
            showStatus(`Created TLS Certificate ${name}.`, 5000);
            await refreshGrid(true);
        } catch (error) {
            if (newTlsError) { newTlsError.textContent = error?.message || 'Unable to create the TLS Certificate.'; newTlsError.hidden = false; }
        } finally {
            setNewTlsBusy(false);
        }
    }

    // ---------------------------------------------------------------------
    // New PGP Key
    // ---------------------------------------------------------------------
    const newPgpDialog = document.querySelector('[data-new-pgp-key-dialog]');
    const newPgpForm = newPgpDialog?.querySelector('[data-new-pgp-key-form]');
    const newPgpName = newPgpDialog?.querySelector('[data-new-pgp-key-name]');
    const newPgpEmail = newPgpDialog?.querySelector('[data-new-pgp-key-email]');
    const newPgpType = newPgpDialog?.querySelector('[data-new-pgp-key-type]');
    const newPgpLength = newPgpDialog?.querySelector('[data-new-pgp-key-length]');
    const newPgpHashAlgorithm = newPgpDialog?.querySelector('[data-new-pgp-key-hash-algorithm]');
    const newPgpExpiresAfter = newPgpDialog?.querySelector('[data-new-pgp-key-expires-after]');
    const newPgpExpireUnit = newPgpDialog?.querySelector('[data-new-pgp-key-expire-unit]');
    const newPgpPassword = newPgpDialog?.querySelector('[data-new-pgp-key-password]');
    const newPgpConfirmPassword = newPgpDialog?.querySelector('[data-new-pgp-key-confirm-password]');
    const newPgpError = newPgpDialog?.querySelector('[data-new-pgp-key-error]');
    const newPgpSubmit = newPgpDialog?.querySelector('[data-new-pgp-key-submit]');
    const newPgpSpinner = newPgpDialog?.querySelector('[data-new-pgp-key-spinner]');
    const newPgpSubmitLabel = newPgpDialog?.querySelector('[data-new-pgp-key-submit-label]');
    const newPgpCancel = newPgpDialog?.querySelector('[data-new-pgp-key-cancel]');
    const newPgpClose = newPgpDialog?.querySelector('[data-new-pgp-key-close]');
    let newPgpBusy = false;
    const updateNewPgpAction = window.MauiModalUI.watchAction(newPgpDialog, newPgpSubmit,
        () => !newPgpBusy, () => Boolean(newPgpName?.value.trim()) && window.MauiModalUI.formValid(newPgpDialog) && newPgpPassword.value === newPgpConfirmPassword.value);

    // Purpose: Sets new PGP busy.
    function setNewPgpBusy(busy) {
        newPgpBusy = busy;
        [newPgpSubmit, newPgpCancel, newPgpClose, newPgpName, newPgpEmail, newPgpType, newPgpLength, newPgpHashAlgorithm, newPgpExpiresAfter, newPgpExpireUnit, newPgpPassword, newPgpConfirmPassword]
            .forEach(control => { if (control) control.disabled = busy; });
        if (newPgpSpinner) newPgpSpinner.hidden = !busy;
        if (newPgpSubmitLabel) newPgpSubmitLabel.textContent = busy ? 'Creating…' : 'Create PGP Key';
        updateNewPgpAction();
    }

    // Purpose: Displays new PGP error.
    function showNewPgpError(message, control) {
        if (newPgpError) { newPgpError.textContent = message; newPgpError.hidden = false; }
        control?.focus();
    }

    // Purpose: Opens new PGP key dialog.
    function openNewPgpKeyDialog() {
        if (!newPgpDialog) return;
        setNewPgpBusy(false);
        if (newPgpName) newPgpName.value = '';
        if (newPgpEmail) newPgpEmail.value = '';
        if (newPgpType) newPgpType.value = 'RSA';
        if (newPgpLength) newPgpLength.value = '1024';
        if (newPgpHashAlgorithm) newPgpHashAlgorithm.value = 'SHA256';
        if (newPgpExpiresAfter) newPgpExpiresAfter.value = '2';
        if (newPgpExpireUnit) newPgpExpireUnit.value = 'Years';
        if (newPgpPassword) newPgpPassword.value = '';
        if (newPgpConfirmPassword) newPgpConfirmPassword.value = '';
        if (newPgpError) { newPgpError.textContent = ''; newPgpError.hidden = true; }
        newPgpDialog.showModal();
        requestAnimationFrame(() => newPgpName?.focus());
    }

    // Purpose: Creates a PGP keypair from the current dialog values.
    async function createPgpKey() {
        if (newPgpBusy) return;

        const name = String(newPgpName?.value || '').trim();
        const emailAddress = String(newPgpEmail?.value || '').trim();
        const keyType = String(newPgpType?.value || '').trim();
        const keyLength = Number(newPgpLength?.value || 0);
        const hashAlgorithm = String(newPgpHashAlgorithm?.value || '').trim();
        const expiresAfter = Number(newPgpExpiresAfter?.value || 0);
        const expireUnit = String(newPgpExpireUnit?.value || '').trim();
        const password = String(newPgpPassword?.value || '');
        const confirmPassword = String(newPgpConfirmPassword?.value || '');

        if (!name) { showNewPgpError('Name is required.', newPgpName); return; }
        if (newPgpEmail && emailAddress && !newPgpEmail.validity.valid) { showNewPgpError('Enter a valid Email Address.', newPgpEmail); return; }
        if (!['RSA', 'DSS'].includes(keyType)) { showNewPgpError('Format must be RSA or DSS.', newPgpType); return; }
        if (![1024, 2048, 4096].includes(keyLength)) { showNewPgpError('Length must be 1024, 2048, or 4096.', newPgpLength); return; }
        if (!['SHA256', 'SHA512'].includes(hashAlgorithm)) { showNewPgpError('Signing Algorithm must be SHA256 or SHA512.', newPgpHashAlgorithm); return; }
        if (!Number.isInteger(expiresAfter) || expiresAfter < 1 || expiresAfter > 12) { showNewPgpError('Expires After must be a value from 1 through 12.', newPgpExpiresAfter); return; }
        if (!['Years', 'Months'].includes(expireUnit)) { showNewPgpError('Expiration Unit must be Years or Months.', newPgpExpireUnit); return; }
        if (password !== confirmPassword) { showNewPgpError('Password and Confirm Password must match.', newPgpConfirmPassword); return; }

        setNewPgpBusy(true);
        if (newPgpError) newPgpError.hidden = true;
        try {
            const data = await fetchJson(endpoint('createPgpKeyUrl'), {
                method: 'POST',
                body: JSON.stringify({ resourceGroup: window.MauiObjectAccess?.group(document.querySelector('[data-new-pgp-key-name]')?.closest('dialog')) || '', name, emailAddress, keyType, keyLength, hashAlgorithm, expiresAfter, expireUnit, password, confirmPassword })
            });
            newPgpDialog?.close();
            showStatus(`Created PGP Key ${data?.item?.name || name}.`, 5000);
            await refreshGrid(true);
        } catch (error) {
            if (newPgpError) { newPgpError.textContent = error?.message || 'Unable to create the PGP Key.'; newPgpError.hidden = false; }
        } finally {
            setNewPgpBusy(false);
        }
    }

    // ---------------------------------------------------------------------
    // Import Key or Certificate
    // ---------------------------------------------------------------------
    const importDialog = document.querySelector('[data-import-key-certificate-dialog]');
    const importForm = importDialog?.querySelector('[data-import-key-certificate-form]');
    const importType = importDialog?.querySelector('[data-import-key-certificate-type]');
    const importNameField = importDialog?.querySelector('[data-import-key-certificate-name-field]');
    const importName = importDialog?.querySelector('[data-import-key-certificate-name]');
    const importFile = importDialog?.querySelector('[data-import-key-certificate-file]');
    const importFileHelp = importDialog?.querySelector('[data-import-key-certificate-file-help]');
    const importPassword = importDialog?.querySelector('[data-import-key-certificate-password]');
    const importError = importDialog?.querySelector('[data-import-key-certificate-error]');
    const importSubmit = importDialog?.querySelector('[data-import-key-certificate-submit]');
    const importSpinner = importDialog?.querySelector('[data-import-key-certificate-spinner]');
    const importSubmitLabel = importDialog?.querySelector('[data-import-key-certificate-submit-label]');
    const importCancel = importDialog?.querySelector('[data-import-key-certificate-cancel]');
    const importClose = importDialog?.querySelector('[data-import-key-certificate-close]');
    let importBusy = false;

    const importDefinitions = {
        SSH: { accept: '.key', extensions: ['.key'], help: 'Select a .key SSH private key file.', needsName: true },
        TLS: { accept: '.pfx,.p12,.cer', extensions: ['.pfx', '.p12', '.cer'], help: 'Select a .pfx, .p12, or .cer TLS certificate file.', needsName: false },
        PGP: { accept: '.asc', extensions: ['.asc'], help: 'Select an ASCII-armored .asc PGP key file.', needsName: false }
    };

    // Purpose: Returns the import definition for the currently selected key/certificate type.
    function selectedImportDefinition() {
        return importDefinitions[String(importType?.value || 'SSH').toUpperCase()] || importDefinitions.SSH;
    }

    // Purpose: Updates import type.
    function updateImportType(resetFile = true) {
        const definition = selectedImportDefinition();
        if (importNameField) importNameField.hidden = !definition.needsName;
        if (importName) importName.required = definition.needsName;
        if (importFile) {
            importFile.accept = definition.accept;
            if (resetFile) importFile.value = '';
        }
        if (importFileHelp) importFileHelp.textContent = definition.help;
        if (importError) { importError.textContent = ''; importError.hidden = true; }
    }

    // Purpose: Sets import busy.
    const updateImportAction = window.MauiModalUI.watchAction(importDialog, importSubmit, () => !importBusy, () => {
        const definition = selectedImportDefinition(), file = importFile?.files?.[0];
        return Boolean(file && file.size > 0 && file.size <= 10 * 1024 * 1024 && definition.extensions.includes(fileExtension(file.name))
            && (!definition.needsName || importName?.value.trim()));
    });
    function setImportBusy(busy) {
        importBusy = busy;
        [importSubmit, importCancel, importClose, importType, importName, importFile, importPassword]
            .forEach(control => { if (control) control.disabled = busy; });
        if (importSpinner) importSpinner.hidden = !busy;
        if (importSubmitLabel) importSubmitLabel.textContent = busy ? 'Importing…' : 'Import';
        updateImportAction();
    }

    // Purpose: Displays import error.
    function showImportError(message, control) {
        if (importError) { importError.textContent = message; importError.hidden = false; }
        control?.focus();
    }

    // Purpose: Opens import dialog.
    function openImportDialog() {
        if (!importDialog) return;
        setImportBusy(false);
        if (importType) importType.value = 'SSH';
        if (importName) importName.value = '';
        if (importPassword) importPassword.value = '';
        updateImportType(true);
        importDialog.showModal();
        requestAnimationFrame(() => importType?.focus());
    }

    // Purpose: Returns the normalized extension of the selected import file.
    function fileExtension(name) {
        const normalized = String(name || '').toLowerCase();
        const dot = normalized.lastIndexOf('.');
        return dot >= 0 ? normalized.slice(dot) : '';
    }

    // Purpose: Imports the selected key/certificate package.
    async function importKeyCertificate() {
        if (importBusy) return;
        const type = String(importType?.value || '').trim().toUpperCase();
        const definition = selectedImportDefinition();
        const name = String(importName?.value || '').trim();
        const file = importFile?.files?.[0] || null;
        const password = String(importPassword?.value || '');

        if (definition.needsName && !name) { showImportError('Name is required when importing an SSH Key.', importName); return; }
        if (!file) { showImportError('Select a file to import.', importFile); return; }
        if (!definition.extensions.includes(fileExtension(file.name))) {
            showImportError(`The selected file must use ${definition.extensions.join(' or ')}.`, importFile);
            return;
        }
        if (file.size <= 0) { showImportError('The selected file is empty.', importFile); return; }
        if (file.size > 10 * 1024 * 1024) { showImportError('The import file cannot exceed 10 MB.', importFile); return; }

        const formData = new FormData();
        formData.append('Type', type);
        formData.append('ResourceGroup', window.MauiObjectAccess?.group(document.querySelector('[data-import-key-certificate-name]')?.closest('dialog')) || '');
        formData.append('Name', name);
        formData.append('Password', password);
        formData.append('File', file, file.name);

        setImportBusy(true);
        if (importError) importError.hidden = true;
        try {
            const data = await fetchJson(endpoint('importKeyCertificateUrl'), { method: 'POST', body: formData });
            importDialog?.close();
            const importedName = data?.item?.name || file.name;
            const importedType = data?.item?.type || (type === 'TLS' ? 'TLS Certificate' : `${type} Key`);
            showStatus(`Imported ${importedType} ${importedName}.`, 5000);
            await refreshGrid(true);
        } catch (error) {
            showImportError(error?.message || 'Unable to import the selected key or certificate.');
        } finally {
            setImportBusy(false);
        }
    }

    // ---------------------------------------------------------------------
    // Row menu
    // ---------------------------------------------------------------------
    // A single native-export modal is used for TLS certificates and PGP keys. Secrets stay
    // in this dialog/request only; download bodies and passwords never enter a toast or log.
    const exportDialog = document.querySelector('[data-export-key-certificate-dialog]');
    const exportField = name => exportDialog?.querySelector(`[data-export-key-certificate-${name}]`);
    const exportForm = exportField('form'), exportPrivate = exportField('private');
    const exportPassword = exportField('password'), exportConfirm = exportField('confirm');
    const exportSubmit = exportField('submit'), exportError = exportField('error');
    const exportClose = [...(exportDialog?.querySelectorAll('[data-export-key-certificate-close]') || [])];
    let exportItem = null, exportBusy = false, exportGeneration = 0;

    function updateExportAction() {
        if (exportPrivate && !exportItem?.canIncludePrivateKey) exportPrivate.checked = false;
        if (exportField('public-only')) exportField('public-only').hidden = !exportItem || exportItem.isTls
            || !['pub', 'public'].includes(String(exportItem.publicPrivate || '').trim().toLowerCase());
        const passwordNeeded = Boolean(exportItem?.isTls && exportPrivate?.checked);
        if (exportField('password-fields')) exportField('password-fields').hidden = !passwordNeeded;
        [exportPassword, exportConfirm].forEach(field => {
            if (field) { field.disabled = exportBusy || !passwordNeeded; field.required = passwordNeeded; }
        });
        if (exportPrivate) exportPrivate.disabled = exportBusy || !exportItem?.canIncludePrivateKey;
        if (exportSubmit) exportSubmit.disabled = exportBusy || !exportItem || passwordNeeded && (!exportPassword.value || exportPassword.value !== exportConfirm.value);
        if (exportField('spinner')) exportField('spinner').hidden = !exportBusy;
        if (exportField('submit-label')) exportField('submit-label').textContent = exportBusy ? 'Exporting...' : exportItem?.isTls ? 'Export Certificate' : 'Export Key';
        exportClose.forEach(button => { button.disabled = exportBusy; });
    }
    function clearExportPassword() { if (exportPassword) exportPassword.value = ''; if (exportConfirm) exportConfirm.value = ''; }
    function exportFailure(message) { if (exportError) { exportError.textContent = message; exportError.hidden = false; } }
    async function openExport(context) {
        if (!exportDialog || !/^(tls|pgp)\|/i.test(context.rowId)) return;
        const generation = ++exportGeneration;
        exportItem = null; exportBusy = false; clearExportPassword(); exportPrivate.checked = false;
        exportError.hidden = true; exportField('fields').hidden = true; exportField('loading').hidden = false;
        exportField('title').textContent = /^tls\|/i.test(context.rowId) ? 'Export TLS Certificate' : 'Export PGP Key';
        exportField('name').textContent = context.name;
        updateExportAction(); exportDialog.showModal();
        try {
            const url = new URL(endpoint('keyCertificateExportOptionsUrl'), location.href);
            url.searchParams.set('id', context.rowId);
            const options = await fetchJson(url, { cache: 'no-store' });
            if (generation !== exportGeneration || !exportDialog.open) return;
            exportItem = options; exportField('fields').hidden = false; updateExportAction();
        } catch (error) {
            if (generation === exportGeneration && exportDialog.open) exportFailure(error.message || 'Unable to load export options.');
        } finally { if (generation === exportGeneration) exportField('loading').hidden = true; }
    }
    async function exportKeyCertificate() {
        if (exportBusy || !exportItem) return;
        if (exportItem.isTls && exportPrivate.checked && (!exportPassword.value || exportPassword.value !== exportConfirm.value)) {
            exportFailure('Enter and confirm the same export password.'); return;
        }
        exportError.hidden = true; exportBusy = true; updateExportAction();
        try {
            const response = await fetch(endpoint('exportKeyCertificateUrl'), { method: 'POST', cache: 'no-store',
                headers: { Accept: 'application/octet-stream', 'Content-Type': 'application/json', RequestVerificationToken: antiForgeryToken },
                body: JSON.stringify({ rowId: exportItem.rowId, includePrivateKey: exportPrivate.checked,
                    password: exportItem.isTls && exportPrivate.checked ? exportPassword.value : '',
                    confirmPassword: exportItem.isTls && exportPrivate.checked ? exportConfirm.value : '' }) });
            if (response.status === 401) { window.location.replace(endpoint('loginUrl')); throw new Error('MOVEit session expired.'); }
            if (!response.ok) {
                const payload = (response.headers.get('content-type') || '').includes('application/json') ? await response.json() : null;
                throw new Error(payload?.message || `Export failed (${response.status}).`);
            }
            const blob = await response.blob();
            if (!blob.size) throw new Error('The export download was empty.');
            const disposition = response.headers.get('content-disposition') || '';
            const encoded = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
            const filename = encoded ? decodeURIComponent(encoded) : disposition.match(/filename="([^"]+)"/i)?.[1] || 'key-export';
            const objectUrl = URL.createObjectURL(blob), link = document.createElement('a');
            try { link.href = objectUrl; link.download = filename; link.hidden = true; document.body.appendChild(link); link.click(); }
            finally { link.remove(); window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000); }
            exportDialog.close();
        } catch (error) { exportFailure(error.message || 'Unable to export the Key or Certificate.'); }
        finally { exportBusy = false; clearExportPassword(); updateExportAction(); }
    }
    exportForm?.addEventListener('submit', event => { event.preventDefault(); void exportKeyCertificate(); });
    exportPrivate?.addEventListener('change', () => { clearExportPassword(); updateExportAction(); });
    [exportPassword, exportConfirm].forEach(field => field?.addEventListener('input', updateExportAction));
    exportClose.forEach(button => button.addEventListener('click', () => { if (!exportBusy) exportDialog.close(); }));
    exportDialog?.addEventListener('cancel', event => { if (exportBusy) event.preventDefault(); });
    exportDialog?.addEventListener('close', () => { ++exportGeneration; exportItem = null; clearExportPassword(); });

    const rowMenu = document.querySelector('[data-key-certificate-row-action-menu]');
    const informationCommand = rowMenu?.querySelector('[data-key-certificate-information-command]');
    const exportCommand = rowMenu?.querySelector('[data-key-certificate-export-command]');
    let activeRow = null;

    // Purpose: Returns the user-facing information label for a key/certificate type.
    function informationLabel(type) {
        const normalized = String(type || '').trim().toLowerCase();
        if (normalized.includes('tls')) return 'Certificate Information';
        if (normalized.includes('ssh')) return 'SSH Key Information';
        if (normalized.includes('pgp')) return 'PGP Key Information';
        return 'Key/Certificate Information';
    }

    // Purpose: Closes row menu.
    function closeRowMenu() {
        activeRow?.button?.setAttribute('aria-expanded', 'false');
        activeRow = null;
        if (!rowMenu) return;
        rowMenu.hidden = true;
        rowMenu.style.left = rowMenu.style.top = rowMenu.style.visibility = '';
    }

    // Purpose: Opens row menu.
    function openRowMenu(button) {
        if (!rowMenu || !button) return;
        window.MauiObjectAccess?.reset(rowMenu);
        if (activeRow?.button === button && !rowMenu.hidden) { closeRowMenu(); return; }
        closeRowMenu();
        activeRow = {
            button,
            rowId: String(button.dataset.rowId || '').trim(),
            name: String(button.dataset.keyCertificateName || '').trim(),
            type: String(button.dataset.keyCertificateType || '').trim(),
            infoUrl: String(button.dataset.keyCertificateInfoUrl || '').trim(),
            auditId: String(button.dataset.keyCertificateAuditId || button.dataset.rowId || '').trim()
        };
        if (informationCommand) informationCommand.textContent = informationLabel(activeRow.type);
        if (exportCommand) {
            exportCommand.hidden = !/^(tls|pgp)\|/i.test(activeRow.rowId);
            exportCommand.disabled = exportCommand.hidden;
        }
        button.setAttribute('aria-expanded', 'true');
        void window.MauiObjectAccess?.menu(rowMenu,'KeyCertificate',activeRow.rowId);
        rowMenu.hidden = false;
        rowMenu.style.visibility = 'hidden';
        const rect = button.getBoundingClientRect();
        const menuRect = rowMenu.getBoundingClientRect();
        let left = rect.left, top = rect.bottom + 4;
        if (left + menuRect.width > innerWidth - 8) left = Math.max(8, innerWidth - menuRect.width - 8);
        if (top + menuRect.height > innerHeight - 8) top = Math.max(8, rect.top - menuRect.height - 4);
        rowMenu.style.left = `${Math.round(left)}px`;
        rowMenu.style.top = `${Math.round(top)}px`;
        rowMenu.style.visibility = '';
    }

    // ---------------------------------------------------------------------
    // Information + dependencies
    // ---------------------------------------------------------------------
    const detailsDialog = document.querySelector('[data-key-certificate-details-dialog]');
    const detailsTitle = detailsDialog?.querySelector('[data-key-certificate-details-title]');
    const detailsSubtitle = detailsDialog?.querySelector('[data-key-certificate-details-subtitle]');
    const detailsLoading = detailsDialog?.querySelector('[data-key-certificate-details-loading]');
    const detailsContent = detailsDialog?.querySelector('[data-key-certificate-details-content]');
    const detailsError = detailsDialog?.querySelector('[data-key-certificate-details-error]');
    const detailsState = window.mauiCreateInformationDialogState({
        loadingElement: detailsLoading,
        contentElement: detailsContent,
        errorElement: detailsError,
        defaultErrorMessage: 'Unable to load key information.'
    });
    const detailsTabs = Array.from(detailsDialog?.querySelectorAll('[data-key-certificate-details-tab]') || []);
    const detailsDependenciesTabLabel = detailsDialog?.querySelector('[data-key-certificate-dependencies-tab-label]');
    const infoPanel = detailsDialog?.querySelector('[data-key-certificate-details-panel="information"]');
    const depsPanel = detailsDialog?.querySelector('[data-key-certificate-details-panel="dependencies"]');
    const detailsHostsSection = detailsDialog?.querySelector('[data-key-certificate-hosts-section]');
    const nativeDetailsSection = detailsDialog?.querySelector('[data-key-certificate-native-details]');
    const nativeDetailsFields = detailsDialog?.querySelector('[data-key-certificate-native-fields]');
    const metadataFields = detailsDialog?.querySelector('[data-key-certificate-metadata-fields]');
    const metadataAudit = detailsDialog?.querySelector('[data-key-certificate-metadata-audit]');
    const detailsSave = detailsDialog?.querySelector('[data-key-certificate-details-save]');
    const detailsReadiness = window.MauiModalUI.registerSaveAction(detailsDialog, detailsSave);
    let activeDetailsRowId = '';
    let activeInfoUrl = '', activeInfoType = '';

    // Purpose: Sets details tab.
    function setDetailsTab(name) {
        const deps = name === 'dependencies';
        detailsTabs.forEach(tab => { const selected = tab.dataset.keyCertificateDetailsTab === name; tab.classList.toggle('active', selected); tab.setAttribute('aria-selected', selected ? 'true' : 'false'); });
        if (infoPanel) infoPanel.hidden = deps;
        if (depsPanel) depsPanel.hidden = !deps;
        if (detailsSave) detailsSave.hidden = deps;
    }

    // Purpose: Displays native details for the selected key or certificate.
    function populateNativeDetails(item) {
        if (!nativeDetailsSection || !nativeDetailsFields) return;
        const type = String(item.type || '').toLowerCase();
        const isTls = type.includes('tls') || type.includes('ssl');
        const fields = type.includes('pgp') ? [
            ['createdDate', 'Creation Date', item.createdDate],
            ['expirationDate', 'Expiration Date', item.expirationDate],
            ['isExpired', 'Is Expired', typeof item.isExpired === 'boolean' ? (item.isExpired ? 'Yes' : 'No') : '—'],
            ['symmetricAlgorithm', 'Preferred Encryption Algorithm', item.symmetricAlgorithm],
            ['keyType', 'Key Type', item.keyType], ['keySize', 'Key Length', item.keySize]
        ] : type.includes('ssh') ? [
            ['fingerprint', 'Key (fingerprint)', item.fingerprint, 'full'],
            ['publicKeySsh', 'Public Key', item.publicKeySsh, 'multiline'],
            ['publicKeyOpenSsh', 'Public Key (Open SSH Format)', item.publicKeyOpenSsh, 'multiline']
        ] : isTls ? [
            ['validFromDate', 'Valid From', item.validFromDate],
            ['expirationDate', 'Expiration Date', item.expirationDate],
            ['sha1Thumbprint', 'Thumbprint', item.sha1Thumbprint, 'full'],
            ['serialNumber', 'Serial Number', item.serialNumber, 'full']
        ] : [];
        const fragment = document.createDocumentFragment();
        fields.forEach(([key, title, value, layout]) => {
            const label = document.createElement('label');
            label.className = `field-block${layout ? ' full-span' : ''}`;
            const caption = document.createElement('span'); caption.textContent = title;
            const control = document.createElement(layout === 'multiline' ? 'textarea' : 'input');
            control.className = layout === 'multiline' ? 'text-input textarea-input' : 'text-input';
            if (layout === 'multiline') control.rows = 4; else control.type = 'text';
            control.readOnly = true; control.value = value == null || value === '' ? '—' : String(value);
            control.dataset.keyCertificateNativeField = key;
            label.append(caption, control); fragment.appendChild(label);
        });
        nativeDetailsFields.replaceChildren(fragment);
        nativeDetailsSection.querySelector('[data-key-certificate-native-title]').textContent = isTls ? 'Certificate Details' : 'Key Details';
        nativeDetailsSection.hidden = fields.length === 0;
    }

    // Purpose: Builds one metadata editor control for the Information dialog.
    function makeMetadataControl(field, value) {
        const type = String(field.type || '').toLowerCase();
        const label = document.createElement('label');
        label.className = `field-block ${type.includes('multiline') ? 'full-span' : ''}`;
        const caption = document.createElement('span');
        caption.textContent = field.label || field.name || '';
        const control = type.includes('multiline') ? document.createElement('textarea') : document.createElement('input');
        control.className = type.includes('multiline') ? 'text-input textarea-input' : 'text-input';
        if (control.tagName === 'TEXTAREA') control.rows = 3;
        control.value = value ?? '';
        window.mauiConfigureMetadataControl?.(control, field);
        control.dataset.keyCertificateMetadataField = field.name || '';
        control.required = Boolean(field.required);
        label.append(caption, control);
        return label;
    }

    // Purpose: Populates audit.
    function populateAudit(audit) {
        if (!metadataAudit) return;
        const rows = [['Created On', formatDate(audit?.createdUtc)], ['Created By', audit?.createdBy || '—'], ['Modified On', formatDate(audit?.modifiedUtc)], ['Modified By', audit?.modifiedBy || '—']];
        const fragment = document.createDocumentFragment();
        rows.forEach(([labelText, valueText]) => { const item=document.createElement('label'); item.className='audit-item'; const label=document.createElement('span'); label.textContent=labelText; const value=document.createElement('input'); value.type='text'; value.className='text-input'; value.readOnly=true; value.value=valueText; item.append(label,value); fragment.appendChild(item); });
        metadataAudit.replaceChildren(fragment);
    }

    // Purpose: Formats a date value for the current UI.
    function formatDate(value) {
        if (!value) return '—';
        const date = new Date(value);
        return Number.isNaN(date.valueOf()) ? '—' : date.toLocaleString();
    }

    // Purpose: Populates dependency list.
    function populateDependencyList(kind, items) {
        const isTask = kind === 'tasks';
        const rows = detailsDialog?.querySelector(isTask ? '[data-key-certificate-tasks-rows]' : '[data-key-certificate-hosts-rows]');
        const list = detailsDialog?.querySelector(isTask ? '[data-key-certificate-tasks-list]' : '[data-key-certificate-hosts-list]');
        const empty = detailsDialog?.querySelector(isTask ? '[data-key-certificate-tasks-empty]' : '[data-key-certificate-hosts-empty]');
        rows?.replaceChildren();
        if (!Array.isArray(items) || items.length === 0) { if (empty) empty.hidden=false; if (list) list.hidden=true; return; }
        const fragment = document.createDocumentFragment();
        items.forEach(item => {
            const row=document.createElement('div'); row.className='host-task-row';
            const id=document.createElement('div'); id.textContent=item.id || '';
            const name=document.createElement('div');
            const link=document.createElement('button'); link.type='button'; link.className='row-primary-link'; link.textContent=item.name || item.id || (isTask?'Task':'Host');
            link.dataset[isTask ? 'keyCertificateTaskEditor' : 'keyCertificateHostEditor'] = 'true';
            link.dataset[isTask ? 'taskId' : 'hostId'] = item.id || '';
            link.dataset[isTask ? 'taskName' : 'hostName'] = item.name || '';
            name.appendChild(link); row.append(id,name); fragment.appendChild(row);
        });
        rows?.replaceChildren(fragment); if (empty) empty.hidden=true; if (list) list.hidden=false;
    }

    // Purpose: Loads and displays key/certificate details and dependencies.
    async function openDetails(rowId, infoUrl, resourceType = '') {
        window.MauiObjectAccess?.clearInformation(detailsDialog);
        if (!detailsDialog || !rowId || !infoUrl) return;
        activeDetailsRowId = rowId;
        activeInfoUrl = infoUrl; activeInfoType = resourceType;
        const isPgp = String(resourceType || '').trim().toLowerCase().includes('pgp');
        if (detailsTitle) detailsTitle.textContent = informationLabel(resourceType);
        if (detailsSubtitle) detailsSubtitle.textContent = isPgp ? 'MOVEit identity, MAUI metadata, and Task usage.' : 'MOVEit identity, MAUI metadata, and Task/Host usage.';
        if (detailsDependenciesTabLabel) detailsDependenciesTabLabel.textContent = isPgp ? 'Tasks' : 'Tasks & Hosts';
        if (detailsHostsSection) detailsHostsSection.hidden = isPgp;
        const keyTypeField = detailsDialog.querySelector('[data-key-certificate-key-type-field]');
        const keyTypeValue = detailsDialog.querySelector('[data-key-certificate-key-type]');
        if (keyTypeField) keyTypeField.hidden = true;
        if (keyTypeValue) keyTypeValue.textContent = '';
        detailsReadiness.reset();
        detailsState.reset();
        metadataFields?.replaceChildren(); metadataAudit?.replaceChildren();
        if (nativeDetailsSection) nativeDetailsSection.hidden = true;
        nativeDetailsFields?.replaceChildren();
        setDetailsTab('information');
        detailsDialog.showModal();
        try {
            const data = await fetchJson(infoUrl, { method:'GET', cache:'no-store' });
            const item = data.item || {};
            const loadedIsPgp = String(item.type || resourceType || '').trim().toLowerCase().includes('pgp');
            if (detailsTitle) detailsTitle.textContent = informationLabel(item.type || resourceType);
            if (detailsSubtitle) detailsSubtitle.textContent = loadedIsPgp ? 'MOVEit identity, MAUI metadata, and Task usage.' : 'MOVEit identity, MAUI metadata, and Task/Host usage.';
            if (detailsDependenciesTabLabel) detailsDependenciesTabLabel.textContent = loadedIsPgp ? 'Tasks' : 'Tasks & Hosts';
            if (detailsHostsSection) detailsHostsSection.hidden = loadedIsPgp;
            const typeNode=detailsDialog.querySelector('[data-key-certificate-type]'); if(typeNode) typeNode.textContent=item.type || resourceType || '—';
            const idNode=detailsDialog.querySelector('[data-key-certificate-id]'); if(idNode) idNode.textContent=item.id || '';
            if (keyTypeField) keyTypeField.hidden = !loadedIsPgp;
            if (keyTypeValue) keyTypeValue.textContent = loadedIsPgp ? (item.publicPrivate || '—') : '';
            const nameNode=detailsDialog.querySelector('[data-key-certificate-name]'); if(nameNode) nameNode.value=item.name || '';
            const resourceGroupsInput=detailsDialog.querySelector('[data-key-certificate-resource-groups]'); if(resourceGroupsInput) resourceGroupsInput.value=item.resourceGroups || '—';
            populateNativeDetails(item);
            const values=data.metadata || {};
            const fields=Array.isArray(data.fields)?data.fields:[];
            fields.forEach(field => { if(field?.name) metadataFields?.appendChild(makeMetadataControl(field, values[field.name] || '')); });
            populateAudit(data.audit || {});
            populateDependencyList('tasks', data.tasks || []);
            if (!loadedIsPgp) populateDependencyList('hosts', data.hosts || []);
            detailsState.showContent();
            await window.MauiObjectAccess?.information(detailsDialog,'KeyCertificate',activeDetailsRowId);
            window.MauiModalUI?.track(detailsDialog);
            if (detailsSave) detailsSave.hidden = fields.length === 0;
        } catch (error) {
            detailsState.showError(error?.message);
        }
    }

    // Purpose: Saves MAUI metadata for the active key/certificate.
    async function saveMetadata() {
        if (!activeDetailsRowId || detailsSave?.disabled) return;
        if (window.mauiValidateMetadataControls && !window.mauiValidateMetadataControls(metadataFields)) return;
        const metadata={};
        metadataFields?.querySelectorAll('[data-key-certificate-metadata-field]').forEach(control => { metadata[control.dataset.keyCertificateMetadataField] = control.value ?? ''; });
        detailsReadiness.busy(true);
        try {
            await fetchJson(endpoint('saveKeyCertificateMetadataUrl'), { method:'POST', body:JSON.stringify({ rowId:activeDetailsRowId, metadata }) });
            detailsDialog?.close();
            showStatus('Key information saved.', 5000);
            await refreshGrid(true);
        } catch(error) {
            detailsState.showError(error?.message || 'Unable to save key information.');
        } finally { detailsReadiness.busy(false); }
    }

    // Uses the shared Task/Host editor selector from dependency lists.
    async function launchTask(taskId, taskName) {
        try { return await window.MauiEditorRouting.openTask(taskId, taskName || ''); }
        catch (error) { showStatus(error?.message || 'Unable to open the Task Editor.', 5000, 'error'); return false; }
    }
    // Purpose: Opens a referenced Host using the configured editor preference.
    async function launchHost(hostId, hostName) {
        try { return await window.MauiEditorRouting.openHost(hostId, hostName || ''); }
        catch (error) { showStatus(error?.message || 'Unable to open the Host Editor.', 5000, 'error'); return false; }
    }
    // ---------------------------------------------------------------------
    // Delete
    // ---------------------------------------------------------------------
    const deleteDialog=document.querySelector('[data-delete-key-certificate-dialog]');
    const deleteTitle=deleteDialog?.querySelector('[data-delete-key-certificate-title]');
    const deleteMessage=deleteDialog?.querySelector('[data-delete-key-certificate-message]');
    const deleteError=deleteDialog?.querySelector('[data-delete-key-certificate-error]');
    const deleteYes=deleteDialog?.querySelector('[data-delete-key-certificate-yes]');
    const deleteNo=Array.from(deleteDialog?.querySelectorAll('[data-delete-key-certificate-no]')||[]);
    let pendingDelete=[]; let deleteBusy=false;

    // Purpose: Normalizes ids.
    function normalizeIds(ids){return Array.from(new Set((ids||[]).map(id=>String(id||'').trim()).filter(Boolean)));}

    // Purpose: Runs delete preflight and opens the key/certificate confirmation dialog.
    async function beginDelete(ids, singleName='') {
        const rowIds=normalizeIds(ids); if(!rowIds.length){showStatus('Select at least one Key or Certificate.',5000);return;} if(deleteBusy)return;
        deleteBusy=true;
        try {
            const url=rowIds.length===1?endpoint('preflightDeleteKeyCertificateUrl'):endpoint('preflightDeleteKeyCertificatesUrl');
            const body=rowIds.length===1?{rowId:rowIds[0]}:{rowIds};
            const preflight=await fetchJson(url,{method:'POST',body:JSON.stringify(body)});
            if(!preflight?.success){
                const items=Array.isArray(preflight?.items)?preflight.items:(preflight?.item?[preflight.item]:[]);
                const details=items.filter(item=>item?.canDelete===false).slice(0,3).map(item=>{
                    const tasks=(item.tasks||[]).map(x=>x.name||x.id); const hosts=(item.hosts||[]).map(x=>x.name||x.id);
                    return `${item.name||item.rowId}: ${item.message||'Cannot delete.'}${tasks.length?` Tasks: ${tasks.slice(0,3).join(', ')}.`:''}${hosts.length?` Hosts: ${hosts.slice(0,3).join(', ')}.`:''}`;
                });
                showStatus([preflight?.message,...details].filter(Boolean).join(' | '),5000); return;
            }
            pendingDelete=rowIds;
            if(deleteTitle) deleteTitle.textContent=rowIds.length===1?'Delete Key/Certificate':'Delete Keys/Certificates';
            if(deleteMessage) deleteMessage.textContent=rowIds.length===1&&singleName?`You are about to permanently delete "${singleName}".`:`You are about to permanently delete ${rowIds.length} selected resource${rowIds.length===1?'':'s'}.`;
            if(deleteError){deleteError.hidden=true;deleteError.textContent='';}
            deleteDialog?.showModal(); requestAnimationFrame(()=>deleteNo.find(button=>button.textContent.trim()==='No')?.focus());
        } catch(error){showStatus(`Delete: ${error?.message||'Dependency check failed.'}`,5000,'error');} finally{deleteBusy=false;}
    }

    // Purpose: Deletes the confirmed supported key/certificate selection.
    async function confirmDelete(){
        if(deleteBusy||!pendingDelete.length)return; deleteBusy=true; if(deleteYes)deleteYes.disabled=true; deleteNo.forEach(x=>x.disabled=true);
        try{
            const ids=[...pendingDelete];
            const url=ids.length===1?endpoint('deleteKeyCertificateUrl'):endpoint('deleteKeyCertificatesUrl');
            const body=ids.length===1?{rowId:ids[0]}:{rowIds:ids};
            const data=await fetchJson(url,{method:'POST',body:JSON.stringify(body)});
            const deletedIds=ids.length===1
                ? (data?.success===true?[ids[0]]:[])
                : (Array.isArray(data?.items)?data.items.filter(item=>item?.success===true).map(item=>item?.rowId).filter(Boolean):[]);
            grid?.dispatchEvent(new CustomEvent('maui:grid-remove-rows',{detail:{rowIds:deletedIds}}));
            deleteDialog?.close(); pendingDelete=[];
            if(ids.length===1) showStatus(data?.success?`Deleted ${data?.name||'resource'}.`:(data?.message||'Delete failed.'),5000);
            else showStatus(`Delete Keys/Certificates: ${Number(data?.succeeded||0)} succeeded${Number(data?.skipped||0)?`, ${data.skipped} skipped`:''}${Number(data?.failed||0)?`, ${data.failed} failed`:''}.`,5000);
            await refreshGrid(true);
        }catch(error){if(deleteError){deleteError.textContent=error?.message||'Unable to delete the selected resources.';deleteError.hidden=false;}}
        finally{deleteBusy=false;if(deleteYes)deleteYes.disabled=false;deleteNo.forEach(x=>x.disabled=false);}
    }

    // ---------------------------------------------------------------------
    // Refresh
    // ---------------------------------------------------------------------
    const refreshControl = document.querySelector('[data-key-certificate-refresh-control]');
    const interval = Math.max(0, Number(config.dataset.uiKeyCertificateRefreshSeconds || 0));
    const keyCertificateRefreshController = window.mauiCreateResourceRefreshController({
        grid,
        refreshControl,
        lastRefreshElement: refreshControl?.querySelector('[data-key-certificate-last-refresh]'),
        refreshClock: refreshControl?.querySelector('[data-key-certificate-refresh-clock]'),
        refreshToggle: refreshControl?.querySelector('[data-key-certificate-refresh-toggle]'),
        refreshNow: refreshControl?.querySelector('[data-key-certificate-refresh-now]'),
        intervalSeconds: interval,
        fetchJson,
        buildInitialUrl: () => {
            const url = new URL(endpoint('initialKeyCertificateGridUrl'), window.location.href);
            url.searchParams.set('preferCached', 'true');
            return url;
        },
        buildRefreshUrl: forceRefresh => {
            const url = new URL(endpoint('refreshKeyCertificateGridUrl'), window.location.href);
            url.searchParams.set('forceRefresh', forceRefresh ? 'true' : 'false');
            return url;
        },
        beforeRefresh: closeRowMenu,
        onInitialError: error => {
            console.warn('Initial Keys and Certificates load failed.');
            showStatus(`Keys & Certificates load failed: ${error.message}`, 5000, 'error', 'page');
        },
        onRefreshError: error => {
            console.warn('Keys and Certificates refresh failed.');
            showStatus(`Refresh failed: ${error.message}`, 5000, 'error', 'page');
        }
    });

    function refreshGrid(forceRefresh) {
        return keyCertificateRefreshController.refresh(forceRefresh);
    }

    // ---------------------------------------------------------------------
    // Wiring
    // ---------------------------------------------------------------------
    document.addEventListener('maui:grid-action',event=>{
        if(String(event.detail?.gridId||'').toLowerCase()!=='keysandcertificates')return;
        const action=String(event.detail?.action||'').trim().toLowerCase();
        if(action==='new ssh key'){event.preventDefault();openNewSshDialog();}
        else if(action==='new tls certificate'){event.preventDefault();openNewTlsCertificateDialog();}
        else if(action==='new pgp key'){event.preventDefault();openNewPgpKeyDialog();}
        else if(action==='import key or certificate'){event.preventDefault();openImportDialog();}
        else if(action==='delete keys/certificates'){event.preventDefault();beginDelete(event.detail?.selectedIds||[]);}
    });

    document.addEventListener('click',event=>{
        const button=event.target.closest('[data-key-certificate-row-menu-button]'); if(button){event.preventDefault();event.stopPropagation();openRowMenu(button);return;}
        const command=event.target.closest('[data-key-certificate-row-command]'); if(command&&rowMenu?.contains(command)){event.preventDefault();if(command.disabled)return;const context=activeRow;const action=command.dataset.keyCertificateRowCommand;closeRowMenu();if(!context)return;if(action==='information')openDetails(context.rowId,context.infoUrl,context.type);else if(action==='export')void openExport(context);else if(action==='audit')navigateToReport(endpoint('auditReportUrl'),{targetId:context.auditId,targetName:context.name});else if(action==='delete')beginDelete([context.rowId],context.name);return;}
        const task=event.target.closest('[data-key-certificate-task-editor]'); if(task){event.preventDefault();launchTask(task.dataset.taskId,task.dataset.taskName);return;}
        const host=event.target.closest('[data-key-certificate-host-editor]'); if(host){event.preventDefault();launchHost(host.dataset.hostId,host.dataset.hostName);return;}
        if(!event.target.closest('[data-key-certificate-row-action-menu]'))closeRowMenu();
    });

    newType?.addEventListener('change',populateSshSizes);
    newForm?.addEventListener('submit',event=>{event.preventDefault();createSshKey();});
    newCancel?.addEventListener('click',()=>{if(!newBusy)newDialog?.close();});
    newClose?.addEventListener('click',()=>{if(!newBusy)newDialog?.close();});
    newTlsForm?.addEventListener('submit',event=>{event.preventDefault();createTlsCertificate();});
    newTlsCancel?.addEventListener('click',()=>{if(!newTlsBusy)newTlsDialog?.close();});
    newTlsClose?.addEventListener('click',()=>{if(!newTlsBusy)newTlsDialog?.close();});
    newPgpForm?.addEventListener('submit',event=>{event.preventDefault();createPgpKey();});
    newPgpCancel?.addEventListener('click',()=>{if(!newPgpBusy)newPgpDialog?.close();});
    newPgpClose?.addEventListener('click',()=>{if(!newPgpBusy)newPgpDialog?.close();});
    importType?.addEventListener('change',()=>updateImportType(true));
    importForm?.addEventListener('submit',event=>{event.preventDefault();importKeyCertificate();});
    importCancel?.addEventListener('click',()=>{if(!importBusy)importDialog?.close();});
    importClose?.addEventListener('click',()=>{if(!importBusy)importDialog?.close();});
    deleteYes?.addEventListener('click',confirmDelete);
    deleteNo.forEach(button=>button.addEventListener('click',()=>{if(!deleteBusy){pendingDelete=[];deleteDialog?.close();}}));
    detailsTabs.forEach(tab=>tab.addEventListener('click',()=>setDetailsTab(tab.dataset.keyCertificateDetailsTab||'information')));
    detailsSave?.addEventListener('click',saveMetadata);
    detailsDialog?.querySelector('[data-key-certificate-details-cancel]')?.addEventListener('click',()=>detailsDialog.close());
    populateSshSizes();

    keyCertificateRefreshController.loadInitial();
    window.MauiModalUI?.registerContext('key-information', detailsDialog, {
        capture: () => ({ id: activeDetailsRowId, url: activeInfoUrl, type: activeInfoType,
            tab: detailsTabs.find(tab => tab.classList.contains('active'))?.dataset.keyCertificateDetailsTab }),
        restore: async state => { const url = window.MauiModalUI.safeUrl(state.url); if (url) { await openDetails(state.id, url, state.type); setDetailsTab(state.tab || 'information'); } }
    });
})();
