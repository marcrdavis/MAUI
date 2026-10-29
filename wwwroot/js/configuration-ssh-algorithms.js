/* Settings adapter for the shared Host SSH/KEX algorithm lists. */
(() => {
    const workspace = document.querySelector('[data-configuration-workspace]');
    const section = workspace?.querySelector('[data-config-section="ssh-algorithms"]');
    const widget = window.MauiSshAlgorithmList;
    if (!section || !widget) return;
    const status = section.querySelector('[data-ssh-algorithms-status]');
    const reload = section.querySelector('[data-ssh-algorithms-reload]');
    const fields = ['encryption', 'keyExchange'].map(key => ({
        key, input: section.querySelector(`[data-ssh-default="${key}"]`),
        container: section.querySelector(`[data-ssh-algorithm-list="${key}"]`), choices: null, state: null
    }));
    let busy = false, loading = false;
    const showStatus = (message, error = false) => {
        status.textContent = message;
        status.classList.toggle('error', error);
        status.setAttribute('role', error ? 'alert' : 'status');
        status.hidden = !message;
    };
    const render = () => {
        reload.disabled = busy || loading;
        fields.forEach(field => {
            if (!field.choices) return;
            widget.render(field.container, {
                choices: field.choices, state: field.state, editable: !busy && !loading,
                title: field.container.dataset.title, description: field.container.dataset.description,
                resetValues: widget.names(field.input.dataset.reset), allowRemoveUnavailable: true,
                onChange: value => {
                    field.input.value = value;
                    field.input.dispatchEvent(new Event('input', { bubbles: true }));
                    showStatus('');
                },
                onError: message => showStatus(message, true)
            });
        });
    };
    async function load() {
        if (loading || busy) return;
        loading = true;
        showStatus('Loading supported SSH/KEX algorithms…');
        render();
        try {
            const response = await fetch(workspace.dataset.sshAlgorithmsUrl, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            const data = await response.json();
            if (!response.ok) throw new Error(data?.message || 'Unable to load supported SSH/KEX algorithms.');
            if (fields.some(field => !Array.isArray(data[field.key]) || !data[field.key].length))
                throw new Error('MOVEit did not return supported SSH/KEX algorithms.');
            fields.forEach(field => {
                field.choices = data[field.key];
                field.state = widget.createState(field.choices, field.input.value);
            });
            showStatus('');
        } catch (error) {
            // Keep the saved values and unrelated Settings edits intact on discovery failures.
            fields.forEach(field => { field.choices = null; field.state = null; field.container.replaceChildren(); });
            showStatus(error?.message || 'Unable to load supported SSH/KEX algorithms. Saved defaults have been preserved.', true);
        } finally {
            loading = false;
            reload.hidden = false;
            render();
        }
    }
    window.MauiSshAlgorithmSettings = {
        collect() {
            const changed = fields.filter(field => field.input.value !== field.input.dataset.original);
            return changed.length ? Object.fromEntries(changed.map(field => [field.key, field.input.value])) : null;
        },
        markSaved() { fields.forEach(field => { field.input.dataset.original = field.input.value; }); },
        setBusy(value) { busy = value; render(); }
    };
    reload.addEventListener('click', load);
    load();
})();
