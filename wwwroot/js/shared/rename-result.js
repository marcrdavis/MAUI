/* Shared acknowledgement and failure policy for Task/Host rename dialogs. */
(() => {
    'use strict';

    function requireConfirmation(result, kind, id, name) {
        const key = kind.toLowerCase();
        if (result?.success === true && result[`${key}Id`] === id && result[`${key}Name`] === name) return;
        const error = new Error(uncertainMessage(kind));
        error.payload = { outcomeUnknown: true };
        throw error;
    }

    function requiresReload(error) {
        const payload = error?.payload;
        if (payload?.moveItSaved || payload?.outcomeUnknown || payload?.editConflict) return true;
        // The server can positively identify a failure before the native rename began.
        if (payload?.moveItSaved === false && payload?.outcomeUnknown === false) return false;
        // A lost response, invalid JSON or unqualified server failure cannot prove rejection.
        const status = Number(error?.status);
        return !(status >= 400 && status < 500 && status !== 408);
    }

    function uncertainMessage(kind) {
        return `The ${kind} rename result could not be confirmed. Close this dialog and refresh the ${kind}s grid before trying again.`;
    }

    function failureMessage(error, kind) {
        const payload = error?.payload;
        if (requiresReload(error) && !payload?.moveItSaved && !payload?.outcomeUnknown && !payload?.editConflict)
            return uncertainMessage(kind);
        return error?.message || `Unable to rename the ${kind}.`;
    }

    window.MauiRenameResult = { requireConfirmation, requiresReload, failureMessage };
})();
