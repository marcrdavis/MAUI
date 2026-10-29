/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * First-run setup behavior.
 */

(() => {
    const page = document.querySelector('[data-setup-page]');
    const form = page?.querySelector('[data-setup-form]');
    if (!page || !form) return;

    const antiForgeryToken = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const testMoveItButton = form.querySelector('[data-test-moveit]');
    const testDatabaseButton = form.querySelector('[data-test-database]');
    const completeButton = form.querySelector('[data-complete-setup]');
    const moveItStatus = form.querySelector('[data-moveit-status]');
    const databaseStatus = form.querySelector('[data-database-status]');
    const errorBox = form.querySelector('[data-setup-error]');
    const sqliteFields = form.querySelector('[data-sqlite-fields]');
    const sqlServerFields = form.querySelector('[data-sqlserver-fields]');
    const sqlCredentials = form.querySelector('[data-sql-credentials]');
    const finishMessage = form.querySelector('[data-setup-finish-message]');

    let testedMoveItSignature = '';
    let testedDatabaseSignature = '';
    let busy = false;

    // Purpose: Returns the first setup control matching the supplied selector.
    const input = (group, name) => form.querySelector(`[data-setup-${group}="${name}"]`);
    // Purpose: Returns the currently selected metadata database provider.
    const provider = () => form.querySelector('[data-database-provider]:checked')?.value || 'SQLite';

    // Purpose: Shows the current Setup test or completion status.
    function setStatus(element, state, message) {
        if (!element) return;
        element.dataset.state = state;
        element.textContent = message;
    }

    // Purpose: Displays the current operation error.
    function showError(message) {
        if (!errorBox) return;
        errorBox.textContent = message || '';
        errorBox.hidden = !message;
    }

    // Purpose: Builds a stable signature used for dirty-state comparisons.
    function payloadSignature(payload) {
        return JSON.stringify(payload);
    }

    // Purpose: Returns whether the MOVEit connection test still matches the current form values.
    function moveItTestIsCurrent() {
        return Boolean(testedMoveItSignature) && testedMoveItSignature === payloadSignature(collectMoveIt());
    }

    // Purpose: Returns whether the database test still matches the current form values.
    function databaseTestIsCurrent() {
        return Boolean(testedDatabaseSignature) && testedDatabaseSignature === payloadSignature(collectDatabase());
    }

    // Purpose: Enables Complete Setup only when both current connection tests have passed.
    function refreshCompleteState() {
        const ready = moveItTestIsCurrent() && databaseTestIsCurrent();
        if (completeButton) completeButton.disabled = busy || !ready;
        if (finishMessage) {
            finishMessage.textContent = ready
                ? "Click 'Complete Setup' to store the configuration and setup the database."
                : 'Test both connections before completing setup.';
        }
    }

    // Purpose: Invalidates the MOVEit test result after a related setup value changes.
    function invalidateMoveIt() {
        testedMoveItSignature = '';
        setStatus(moveItStatus, 'idle', 'Not tested');
        refreshCompleteState();
    }

    // Purpose: Invalidates the database test result after a related setup value changes.
    function invalidateDatabase() {
        testedDatabaseSignature = '';
        setStatus(databaseStatus, 'idle', 'Not tested');
        refreshCompleteState();
    }

    // Purpose: Collects the current MOVEit setup values.
    function collectMoveIt() {
        return {
            displayName: input('moveit', 'displayName')?.value.trim() || '',
            environmentKey: input('moveit', 'environmentKey')?.value.trim() || '',
            webAdminUrl: input('moveit', 'webAdminUrl')?.value.trim() || '',
            allowInvalidServerCertificates: Boolean(input('moveit', 'allowInvalidServerCertificates')?.checked)
        };
    }

    // Purpose: Collects database.
    function collectDatabase() {
        return {
            provider: provider(),
            sqliteDatabaseFile: input('database', 'sqliteDatabaseFile')?.value.trim() || '',
            sqlServer: input('database', 'sqlServer')?.value.trim() || '',
            sqlDatabase: input('database', 'sqlDatabase')?.value.trim() || '',
            sqlAuthentication: input('database', 'sqlAuthentication')?.value || 'Integrated',
            sqlUsername: input('database', 'sqlUsername')?.value.trim() || '',
            sqlPassword: input('database', 'sqlPassword')?.value || '',
            sqlEncrypt: Boolean(input('database', 'sqlEncrypt')?.checked),
            sqlTrustServerCertificate: Boolean(input('database', 'sqlTrustServerCertificate')?.checked)
        };
    }

    // Purpose: Validates the MOVEit setup values before testing or completion.
    function validateMoveIt(payload) {
        if (!payload.displayName) throw new Error('MOVEit Display Name is required.');
        if (!payload.environmentKey) throw new Error('Environment Key is required.');
        if (payload.environmentKey.length > 64) throw new Error('Environment Key cannot exceed 64 characters.');
        let url;
        try { url = new URL(payload.webAdminUrl); } catch { throw new Error('Enter a valid MOVEit Web Admin URL.'); }
        if (!['http:', 'https:'].includes(url.protocol)) throw new Error('MOVEit Web Admin URL must use HTTP or HTTPS.');
    }

    // Purpose: Validates database.
    function validateDatabase(payload) {
        if (payload.provider === 'SQLite') {
            if (!payload.sqliteDatabaseFile) throw new Error('SQLite Database File is required.');
            return;
        }
        if (!payload.sqlServer) throw new Error('SQL Server is required.');
        if (!payload.sqlDatabase) throw new Error('SQL Database is required.');
        if (payload.sqlAuthentication === 'Sql') {
            if (!payload.sqlUsername) throw new Error('SQL Username is required.');
            if (!payload.sqlPassword) throw new Error('SQL Password is required.');
        }
    }

    // Purpose: Posts Setup JSON with antiforgery protection and normalized error handling.
    async function postJson(url, payload) {
        const headers = new Headers({ 'Content-Type': 'application/json' });
        if (antiForgeryToken) headers.set('RequestVerificationToken', antiForgeryToken);
        const response = await fetch(url, {
            method: 'POST',
            headers,
            credentials: 'same-origin',
            body: JSON.stringify(payload)
        });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data?.message || `Request failed (${response.status}).`);
        return data;
    }

    // Purpose: Updates database fields.
    function updateDatabaseFields() {
        const sqlServer = provider() === 'SqlServer';
        if (sqliteFields) sqliteFields.hidden = sqlServer;
        if (sqlServerFields) sqlServerFields.hidden = !sqlServer;
        updateSqlAuthenticationFields();
    }

    // Purpose: Shows, enables, and requires SQL credentials only for SQL Server Authentication.
    function updateSqlAuthenticationFields() {
        if (!sqlCredentials) return;
        const sqlAuthenticationSelected = input('database', 'sqlAuthentication')?.value === 'Sql';
        sqlCredentials.hidden = !sqlAuthenticationSelected;
        sqlCredentials.querySelectorAll('input, select, textarea').forEach(control => {
            control.disabled = !sqlAuthenticationSelected;
        });
    }

    testMoveItButton?.addEventListener('click', async () => {
        if (busy) return;
        showError('');
        let payload;
        try {
            payload = collectMoveIt();
            validateMoveIt(payload);
        } catch (error) {
            setStatus(moveItStatus, 'error', error.message);
            return;
        }

        busy = true;
        testedMoveItSignature = '';
        setStatus(moveItStatus, 'testing', 'Testing…');
        refreshCompleteState();
        testMoveItButton.disabled = true;
        try {
            const data = await postJson(page.dataset.testMoveitUrl, payload);
            testedMoveItSignature = payloadSignature(payload);
            setStatus(moveItStatus, 'success', data.message || 'Connection successful.');
        } catch (error) {
            setStatus(moveItStatus, 'error', error.message || 'Connection failed.');
        } finally {
            busy = false;
            testMoveItButton.disabled = false;
            refreshCompleteState();
        }
    });

    testDatabaseButton?.addEventListener('click', async () => {
        if (busy) return;
        showError('');
        let payload;
        try {
            payload = collectDatabase();
            validateDatabase(payload);
        } catch (error) {
            setStatus(databaseStatus, 'error', error.message);
            return;
        }

        busy = true;
        testedDatabaseSignature = '';
        setStatus(databaseStatus, 'testing', 'Testing…');
        refreshCompleteState();
        testDatabaseButton.disabled = true;
        try {
            const data = await postJson(page.dataset.testDatabaseUrl, payload);
            testedDatabaseSignature = payloadSignature(payload);
            setStatus(databaseStatus, 'success', data.message || 'Database connection successful.');
        } catch (error) {
            setStatus(databaseStatus, 'error', error.message || 'Database test failed.');
        } finally {
            busy = false;
            testDatabaseButton.disabled = false;
            refreshCompleteState();
        }
    });

    completeButton?.addEventListener('click', async () => {
        if (busy || !moveItTestIsCurrent() || !databaseTestIsCurrent()) return;
        showError('');
        const payload = { moveIt: collectMoveIt(), database: collectDatabase() };
        try {
            validateMoveIt(payload.moveIt);
            validateDatabase(payload.database);
        } catch (error) {
            showError(error.message);
            return;
        }

        busy = true;
        refreshCompleteState();
        const originalText = completeButton.textContent;
        completeButton.textContent = 'Saving…';
        try {
            const result = await postJson(page.dataset.completeUrl, payload);
            if (result?.restartRequired) throw new Error('Initial setup saved but requires additional application initialization.');
            window.location.assign(result?.loginUrl || (window.mauiAppUrl ? window.mauiAppUrl('/Account/Login') : 'Account/Login'));
        } catch (error) {
            showError(error.message || 'Initial setup could not be completed.');
            busy = false;
            completeButton.textContent = originalText;
            refreshCompleteState();
        }
    });

    form.querySelectorAll('[data-setup-moveit]').forEach(control => {
        control.addEventListener('input', invalidateMoveIt);
        control.addEventListener('change', invalidateMoveIt);
    });

    form.querySelectorAll('[data-setup-database], [data-database-provider]').forEach(control => {
        control.addEventListener('input', invalidateDatabase);
        control.addEventListener('change', () => {
            invalidateDatabase();
            updateDatabaseFields();
        });
    });

    input('database', 'sqlAuthentication')?.addEventListener('change', updateSqlAuthenticationFields);
    updateDatabaseFields();
    refreshCompleteState();
})();
