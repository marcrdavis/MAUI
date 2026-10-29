/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Login-page submission state.
 */

(() => {
    const form = document.querySelector('[data-login-form]');
    if (!form) return;

    const submitButton = form.querySelector('[data-login-submit]');
    const label = form.querySelector('[data-login-button-label]');
    const textInputs = Array.from(form.querySelectorAll('input.text-input'));
    const serverSelect = form.querySelector('select.text-input');
    const usernameInput = form.querySelector('input[name="username"]');
    let submitting = false;

    function normalizeUsername() {
        if (usernameInput) usernameInput.value = usernameInput.value.trim().replace(/\//g, '\\');
    }
    usernameInput?.addEventListener('change', normalizeUsername);

    // Locks credential controls after submission starts without disabling successful form controls.
    function setSubmittingState(isSubmitting) {
        submitting = isSubmitting;
        form.classList.toggle('is-submitting', isSubmitting);
        form.setAttribute('aria-busy', String(isSubmitting));

        textInputs.forEach(input => {
            input.readOnly = isSubmitting;
            input.setAttribute('aria-disabled', String(isSubmitting));
        });

        if (serverSelect) {
            if (isSubmitting) {
                serverSelect.dataset.previousTabIndex = String(serverSelect.tabIndex);
                serverSelect.tabIndex = -1;
                serverSelect.setAttribute('aria-disabled', 'true');
            } else {
                const previousTabIndex = Number(serverSelect.dataset.previousTabIndex);
                serverSelect.tabIndex = Number.isFinite(previousTabIndex) ? previousTabIndex : 0;
                serverSelect.removeAttribute('aria-disabled');
            }
        }

        if (submitButton) {
            submitButton.disabled = isSubmitting;
            submitButton.classList.toggle('is-loading', isSubmitting);
        }

        if (label) label.textContent = isSubmitting ? 'Signing In…' : 'Sign In';
    }

    form.addEventListener('submit', event => {
        if (submitting) {
            event.preventDefault();
            return;
        }

        normalizeUsername();
        // Keep the first submission as a native POST. Read-only inputs and the enabled select
        // remain successful controls, so username, password, and server are included.
        if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
        setSubmittingState(true);
    });

    // Browsers can restore this page from the back/forward cache after navigation.
    window.addEventListener('pageshow', () => {
        if (submitting) setSubmittingState(false);
    });
})();
