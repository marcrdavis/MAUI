// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Streams upgrade progress and coordinates any controlled post-upgrade application restart.
(() => {
    "use strict";

    const page = document.querySelector("[data-upgrade-page]");
    if (!page) return;

    const beginButton = page.querySelector("[data-upgrade-begin]");
    const retryButton = page.querySelector("[data-upgrade-retry]");
    const restartButton = page.querySelector("[data-upgrade-restart]");
    const returnButton = page.querySelector("[data-upgrade-return]");
    const ready = page.querySelector("[data-upgrade-ready]");
    const running = page.querySelector("[data-upgrade-running]");
    const progressLine = page.querySelector("[data-upgrade-progress]");
    const success = page.querySelector("[data-upgrade-success]");
    const successVersion = page.querySelector("[data-upgrade-success-version]");
    const summary = page.querySelector("[data-upgrade-summary]");
    const error = page.querySelector("[data-upgrade-error]");
    const errorMessage = page.querySelector("[data-upgrade-error-message]");
    const completionMessages = page.querySelectorAll("[data-upgrade-completion-message]");
    const token = page.querySelector('[data-upgrade-token-form] input[name="__RequestVerificationToken"]')?.value || "";
    const runUrl = page.dataset.runUrl || "";
    const restartUrl = page.dataset.restartUrl || "";
    const restartStatusUrl = page.dataset.restartStatusUrl || "";
    const loginUrl = page.dataset.loginUrl || window.mauiAppUrl("/Account/Login");
    let runningRequest = false;
    let restartRequest = false;

    // Purpose: Shows only the requested Upgrade-page state panel.
    const showOnly = (element) => {
        [ready, running, success, error].forEach(item => {
            if (item) item.hidden = item !== element;
        });
    };

    // Purpose: Updates completion/restart guidance wherever the current Upgrade state renders it.
    const setCompletionMessage = message => {
        completionMessages.forEach(item => {
            item.textContent = message || "";
        });
    };

    // Purpose: Displays the current operation error.
    const showError = (message) => {
        runningRequest = false;
        if (error && errorMessage) {
            errorMessage.textContent = message || "The upgrade failed.";
            showOnly(error);
            return;
        }

        setCompletionMessage(message || "The operation failed.");
        if (restartButton) restartButton.disabled = false;
    };

    // Purpose: Waits without blocking the browser event loop.
    const delay = milliseconds => new Promise(resolve => window.setTimeout(resolve, milliseconds));

    // Purpose: Waits for the restarted MAUI process to report that upgrade gating is no longer pending.
    const waitForRestart = async () => {
        for (let attempt = 0; attempt < 90; attempt += 1) {
            await delay(1000);
            try {
                const response = await fetch(restartStatusUrl, {
                    method: "GET",
                    credentials: "same-origin",
                    cache: "no-store",
                    headers: { "Accept": "application/json" }
                });
                if (!response.ok) continue;
                const status = await response.json();
                if (status?.ready === true) {
                    window.location.replace(loginUrl);
                    return;
                }
                if (status?.requiresAttention === true) {
                    setCompletionMessage("MAUI restarted, but startup validation requires administrator attention. Returning to sign in...");
                    await delay(900);
                    window.location.replace(loginUrl);
                    return;
                }
            } catch {
                // Connection failures are expected while IIS/ANCM replaces the application process.
            }
        }

        restartRequest = false;
        if (restartButton) {
            restartButton.disabled = false;
            restartButton.hidden = false;
        }
        setCompletionMessage("MAUI did not become reachable after the restart request. Try Restart MAUI again.");
    };

    // Purpose: Requests a controlled application restart and waits for the replacement process.
    const restartApplication = async () => {
        if (restartRequest) return;
        restartRequest = true;
        if (restartButton) {
            restartButton.disabled = true;
            restartButton.hidden = true;
        }
        setCompletionMessage("Restarting MAUI to finish applying the upgrade...");

        try {
            const response = await fetch(restartUrl, {
                method: "POST",
                credentials: "same-origin",
                cache: "no-store",
                headers: {
                    "RequestVerificationToken": token,
                    "Accept": "application/json"
                }
            });

            if (!response.ok && response.status !== 409) {
                const text = await response.text();
                let message = text;
                try { message = JSON.parse(text)?.message || text; } catch { /* Plain-text error response. */ }
                restartRequest = false;
                if (restartButton) {
                    restartButton.disabled = false;
                    restartButton.hidden = false;
                }
                setCompletionMessage(message || `Restart request failed with HTTP ${response.status}.`);
                return;
            }

            if (response.status === 409) {
                window.location.replace(loginUrl);
                return;
            }
        } catch (err) {
            // If the host shuts down quickly enough to interrupt this request, polling can still determine
            // whether the restart succeeded. Only surface an error if the replacement process never appears.
            console.debug("MAUI restart request connection ended; waiting for restart status.");
        }

        await waitForRestart();
    };

    // Purpose: Applies one streamed Upgrade progress event to the page.
    const handleEvent = event => {
        if (!event || typeof event !== "object") return;
        if (event.type === "progress") {
            if (progressLine) progressLine.textContent = event.message || "Working...";
            return;
        }
        if (event.type === "error") {
            showError(event.message);
            return;
        }
        if (event.type === "success") {
            runningRequest = false;
            if (successVersion) {
                const targetVersion = document.createElement("strong");
                targetVersion.textContent = String(event.toVersion || "");
                successVersion.replaceChildren(
                    document.createTextNode(`MAUI ${String(event.fromVersion || "")} → `),
                    targetVersion
                );
            }
            if (summary) {
                const schema = document.createElement("span");
                const schemaValue = document.createElement("strong");
                schemaValue.textContent = `v${Number(event.schemaVersion || 0)}`;
                schema.append(document.createTextNode("Database schema "), schemaValue);

                const configuration = document.createElement("span");
                const configurationValue = document.createElement("strong");
                configurationValue.textContent = `v${Number(event.configurationVersion || 0)}`;
                configuration.append(document.createTextNode("Configuration "), configurationValue);

                summary.replaceChildren(schema, configuration);
            }
            showOnly(success);

            if (event.restartRequired === true) {
                if (returnButton) returnButton.hidden = true;
                if (restartButton) restartButton.hidden = false;
                setCompletionMessage("Select Restart MAUI to finish applying the upgrade.");
            } else {
                if (restartButton) restartButton.hidden = true;
                setCompletionMessage("Upgrade completed successfully. No application restart is required.");
                if (returnButton) returnButton.hidden = false;
            }
        }
    };

    // Purpose: Starts the upgrade request and consumes streamed progress events.
    const runUpgrade = async () => {
        if (runningRequest) return;
        runningRequest = true;
        if (progressLine) progressLine.textContent = "Preparing upgrade...";
        showOnly(running);

        try {
            const response = await fetch(runUrl, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "RequestVerificationToken": token,
                    "Accept": "application/x-ndjson"
                }
            });

            if (!response.ok) {
                const text = await response.text();
                throw new Error(text || `Upgrade request failed with HTTP ${response.status}.`);
            }
            if (!response.body) throw new Error("The upgrade response did not contain a progress stream.");

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let buffer = "";
            let completed = false;

            while (true) {
                const { value, done } = await reader.read();
                buffer += decoder.decode(value || new Uint8Array(), { stream: !done });
                const lines = buffer.split("\n");
                buffer = lines.pop() || "";

                for (const line of lines) {
                    if (!line.trim()) continue;
                    const event = JSON.parse(line);
                    handleEvent(event);
                    if (event.type === "success") completed = true;
                    if (event.type === "error") throw new Error(event.message || "The upgrade failed.");
                }

                if (done) break;
            }

            if (buffer.trim()) {
                const event = JSON.parse(buffer);
                handleEvent(event);
                if (event.type === "success") completed = true;
                if (event.type === "error") throw new Error(event.message || "The upgrade failed.");
            }

            if (!completed) throw new Error("The upgrade process ended before reporting successful completion.");
        } catch (err) {
            showError(err instanceof Error ? err.message : String(err));
        }
    };

    beginButton?.addEventListener("click", runUpgrade);
    retryButton?.addEventListener("click", runUpgrade);
    restartButton?.addEventListener("click", () => void restartApplication());
    returnButton?.addEventListener("click", () => window.location.replace(loginUrl));
})();
