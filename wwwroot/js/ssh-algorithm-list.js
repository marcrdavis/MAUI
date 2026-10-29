/* Shared ordered SSH/KEX choices for Host editors and Configuration > Settings. */
(() => {
    const names = csv => [...new Set(String(csv || '').split(',').map(value => value.trim()).filter(Boolean))];
    const defaults = choices => choices.filter(choice => choice.defaultEnabled).map(choice => choice.value);
    const createState = (choices, csv) => {
        const selected = String(csv || '').trim() ? names(csv) : defaults(choices);
        return selected.map(value => ({ value, enabled: true }))
            .concat(choices.filter(choice => !selected.includes(choice.value)).map(choice => ({ value: choice.value, enabled: false })));
    };
    const values = state => state.filter(item => item.enabled).map(item => item.value).join(',');
    function render(container, settings) {
        const { choices, state, editable = true, onChange, onError, allowRemoveUnavailable = false } = settings;
        const supported = new Set(choices.map(choice => choice.value));
        const resetValues = (settings.resetValues || defaults(choices)).filter(value => supported.has(value));
        const redraw = () => render(container, settings);
        const commit = () => { onChange?.(values(state)); redraw(); };
        container.classList.add('maui-host-algorithm-list');
        const toolbar = document.createElement('div'); toolbar.className = 'maui-host-algorithm-toolbar';
        if (settings.title) {
            const heading = document.createElement('div'); heading.className = 'maui-host-algorithm-heading';
            const title = document.createElement('strong'); title.textContent = settings.title; heading.append(title);
            if (settings.description) {
                const description = document.createElement('p'); description.textContent = settings.description; heading.append(description);
            }
            toolbar.append(heading);
        }
        const reset = document.createElement('button'); reset.type = 'button'; reset.className = 'secondary-button'; reset.textContent = 'Reset';
        reset.disabled = !editable || !resetValues.length;
        reset.addEventListener('click', () => { state.splice(0, state.length, ...createState(choices, resetValues.join(','))); commit(); });
        toolbar.append(reset); container.replaceChildren(toolbar);
        state.forEach((item, index) => {
            const available = supported.has(item.value);
            const row = document.createElement('div'); row.className = 'maui-host-algorithm-row'; row.dataset.algorithm = item.value;
            const label = document.createElement('label');
            const enabled = document.createElement('input'); enabled.type = 'checkbox'; enabled.checked = item.enabled;
            enabled.disabled = !editable || !available && !(allowRemoveUnavailable && item.enabled);
            const caption = document.createElement('span'); caption.textContent = item.value + (available ? '' : ' (saved; unavailable)');
            enabled.addEventListener('change', () => {
                if (!enabled.checked && state.filter(entry => entry.enabled).length === 1) {
                    enabled.checked = true; onError?.('Keep at least one algorithm enabled in each SSH/KEX list.'); return;
                }
                item.enabled = enabled.checked; commit();
            });
            label.append(enabled, caption);
            const actions = document.createElement('div');
            for (const [delta, text, action] of [[-1, '↑', 'up'], [1, '↓', 'down']]) {
                const button = document.createElement('button'); button.type = 'button'; button.className = 'secondary-button'; button.textContent = text;
                button.dataset.algorithmMove = action; button.setAttribute('aria-label', `Move ${item.value} ${action}`);
                button.disabled = !editable || !available || index + delta < 0 || index + delta >= state.length;
                button.addEventListener('click', () => { [state[index], state[index + delta]] = [state[index + delta], state[index]]; commit(); });
                actions.append(button);
            }
            row.append(label, actions); container.append(row);
        });
    }
    window.MauiSshAlgorithmList = { names, createState, values, render };
})();
