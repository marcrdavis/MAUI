/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * This JavaScript is part of the MAUI application.
 */

/*
 * maui-integration.js
 *
 * MOVEit Automation customization
 *
 * This customization provides the following behavior:
 *   1. Hide the MOVEit navigation bar when HideNav is present.
 *   2. Isolate each MAUI native-editor launch with a per-window launch ID.
 *   3. Cover MAUI-launched login/redirect pages with a centered pulsing MAUI logo overlay.
 *   4. Receive login credentials from the trusted MAUI opener by postMessage().
 *   5. Populate the MOVEit login controls and submit the normal login form.
 *   6. Preserve and restore the requested Angular/hash route after login.
 *   7. Apply the MAUI native-editor skin and title only after authentication.
 *   8. Keep editor-local Undo Last Change aligned when the same Task editor popup moves to a new Task ID.
 *   9. Emit diagnostic console messages for the postMessage/login handshake.
 *
 * Route-specific backlink visibility is handled by maui-native-editor.css so the
 * Task Details backlink remains visible while the Task Editor backlink is hidden.
 *
 * Security rules:
 *   - LAUNCHER_ORIGINS is an explicit allow-list of trusted MAUI origins.
 *   - "*" is never used as a target origin.
 *   - Messages are accepted only from an allowed origin and window.opener.
 *   - The first valid opener message binds this popup session to one exact MAUI origin.
 *   - MAUI messages must also contain the launch ID assigned to this popup.
 *
 * Loaded with:
 *   <script src="maui-integration.js" type="text/javascript"></script>
 */

(function () {
    "use strict";

    /* ============================================================
     * CONFIGURATION
     * ============================================================ */

    // List every MAUI browser origin that is allowed to launch this MOVEit Web Admin instance.
    // Each entry is normalized to its exact scheme + host + port origin. Wildcards are not supported.
    // Example production entries: "https://maui-dev.example.com", "https://maui-qa.example.com", "https://maui.example.com"
    // Visual Studio local test example: "https://localhost:7184"
    const LAUNCHER_ORIGINS = [
        "https://localhost:7184"
    ];

    const STORAGE_KEY = "MOVEIT_Custom_OriginalRoute";
    const MAUI_LAUNCH_PARAMETER = "MauiLaunch";
    const MAUI_LAUNCH_STORAGE_KEY = "MOVEIT_Custom_MauiLaunchId";
    const MAUI_LAUNCHER_ORIGIN_STORAGE_KEY = "MOVEIT_Custom_MauiLauncherOrigin";
    const MAUI_TARGET_HASH_STORAGE_KEY = "MOVEIT_Custom_MauiTargetHash";
    const MAUI_OVERLAY_PENDING_STORAGE_KEY = "MOVEIT_Custom_MauiOverlayPending";
    const MAUI_CONFIGURED_STORAGE_KEY = "MOVEIT_Custom_MauiConfigured";
    const MAUI_COMPLETED_STORAGE_KEY = "MOVEIT_Custom_MauiCompleted";
    const MAUI_EDITOR_UNDO_ENABLED_STORAGE_KEY = "MOVEIT_Custom_EditorUndoEnabled";
    const MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY = "MOVEIT_Custom_EditorObjectType";
    const MAUI_EDITOR_OBJECT_ID_STORAGE_KEY = "MOVEIT_Custom_EditorObjectId";
    const MAUI_UNDO_RELOAD_STORAGE_KEY = "MOVEIT_Custom_EditorUndoReload";
    const MAUI_UNDO_AVAILABLE_STORAGE_KEY = "MOVEIT_Custom_EditorUndoAvailable";
    const MAUI_UNDO_BUTTON_ID = "maui-undo-last-change";
    const MAUI_UNDO_DIALOG_ID = "maui-undo-confirm-dialog";
    const MAUI_WORKING_OVERLAY_ID = "maui-launch-working-overlay";
    const MAUI_WORKING_STYLE_ID = "maui-launch-working-style";
    const MAUI_WORKING_LOGO_ID = "maui-launch-working-logo";
    // Increment this revision whenever any MOVEit-side MAUI integration asset changes.
    // A single revision keeps the bridge, stylesheet, and logo cache contract explicit and synchronized.
    const MAUI_INTEGRATION_ASSET_REVISION = "0.16.206-RC1";
    const MAUI_WORKING_LOGO_URL = `/automation-mark.svg?v=${encodeURIComponent(MAUI_INTEGRATION_ASSET_REVISION)}`;

    // This value controls how many animation frames Angular receives to enable the Login button
    // after credentials are written into the form controls.
    const MAX_SUBMIT_WAIT_FRAMES = 20;
    const MAUI_UNDO_RESPONSE_TIMEOUT_MS = 90000;
    const MAUI_UNDO_IDENTITY_REBIND_TIMEOUT_MS = 15000;

    // The MAUI native-editor stylesheet is expected in the Web Admin root.
    const MAUI_SKIN_NAME = "maui";
    const MAUI_STYLESHEET_ID = "maui-native-editor-styles";
    const MAUI_STYLESHEET_URL = `/maui-native-editor.css?v=${encodeURIComponent(MAUI_INTEGRATION_ASSET_REVISION)}`;
    const MAUI_BODY_CLASS = "maui-native-editor";
    const MAUI_SKIN_STORAGE_KEY = "MOVEIT_Custom_EditorSkin";
    const MAUI_TITLE_STORAGE_KEY = "MOVEIT_Custom_WindowTitle";


    /* ============================================================
     * STATE
     * ============================================================ */

    let pendingLogin = null;
    let submitStarted = false;
    let readySentForCurrentLoginForm = false;
    let currentUsernameElement = null;
    let lastPageReadySignature = "";
    let activeMauiLaunchId = "";
    let activeLauncherOrigin = "";
    let overlayCompletionTimer = null;
    let mauiStylesheetState = "idle";
    let suppressEditorUpdatedToast = false;
    let undoStateRequestedForLaunch = "";
    let undoToastArmedForLaunch = "";
    let undoIdentityRebindRequestedFor = "";
    let undoIdentityRebindTimer = null;
    let undoResponseTimer = null;
    const processedUpdateToasts = new WeakSet();


    /* ============================================================
     * GENERAL HELPERS
     * ============================================================ */

    function normalizeLauncherOrigin(value) {
        try {
            const url = new URL(String(value || "").trim());
            if (url.protocol !== "https:" && url.protocol !== "http:") return "";
            if (url.hostname.includes("*")) return "";
            return url.origin;
        } catch {
            return "";
        }
    }

    const TRUSTED_LAUNCHER_ORIGINS = Object.freeze(
        Array.from(new Set(LAUNCHER_ORIGINS.map(normalizeLauncherOrigin).filter(Boolean)))
    );

    function isTrustedLauncherOrigin(origin) {
        return TRUSTED_LAUNCHER_ORIGINS.includes(String(origin || ""));
    }

    function bindLauncherOrigin(origin) {
        const candidate = String(origin || "");
        if (!isTrustedLauncherOrigin(candidate)) return false;
        if (activeLauncherOrigin && activeLauncherOrigin !== candidate) return false;
        if (!activeLauncherOrigin) {
            activeLauncherOrigin = candidate;
            sessionStorage.setItem(MAUI_LAUNCHER_ORIGIN_STORAGE_KEY, candidate);
            console.info("maui-integration.js: bound launcher origin to", candidate);
        }
        return true;
    }

    function getLauncherTargetOrigins() {
        return activeLauncherOrigin ? [activeLauncherOrigin] : TRUSTED_LAUNCHER_ORIGINS;
    }

    // Purpose: Capture a MAUI launch ID into this popup's sessionStorage. Overlay
    // state is tracked separately so a completed editor refresh can never be mistaken
    // for a new login launch.
    function initializeMauiLaunchContext() {
        const url = new URL(window.location.href);
        const launchIdFromUrl = (url.searchParams.get(MAUI_LAUNCH_PARAMETER) || "").trim();

        if (launchIdFromUrl) {
            activeMauiLaunchId = launchIdFromUrl;
            activeLauncherOrigin = "";
            sessionStorage.setItem(MAUI_LAUNCH_STORAGE_KEY, launchIdFromUrl);
            sessionStorage.removeItem(MAUI_LAUNCHER_ORIGIN_STORAGE_KEY);
            sessionStorage.setItem(MAUI_OVERLAY_PENDING_STORAGE_KEY, launchIdFromUrl);
            sessionStorage.removeItem(MAUI_CONFIGURED_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_COMPLETED_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_EDITOR_UNDO_ENABLED_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_UNDO_RELOAD_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_UNDO_AVAILABLE_STORAGE_KEY);
            undoStateRequestedForLaunch = "";
            undoToastArmedForLaunch = "";
            undoIdentityRebindRequestedFor = "";
            setUndoButtonAvailable(false);

            if (window.location.hash) {
                sessionStorage.setItem(MAUI_TARGET_HASH_STORAGE_KEY, window.location.hash);
            }

            url.searchParams.delete(MAUI_LAUNCH_PARAMETER);
            try {
                window.history.replaceState(
                    window.history.state,
                    document.title,
                    url.pathname + url.search + url.hash
                );
            } catch (error) {
                console.warn("maui-integration.js: unable to remove MauiLaunch from the URL.");
            }

            return;
        }

        activeMauiLaunchId = (sessionStorage.getItem(MAUI_LAUNCH_STORAGE_KEY) || "").trim();
        const storedLauncherOrigin = (sessionStorage.getItem(MAUI_LAUNCHER_ORIGIN_STORAGE_KEY) || "").trim();
        activeLauncherOrigin = isTrustedLauncherOrigin(storedLauncherOrigin) ? storedLauncherOrigin : "";
        if (storedLauncherOrigin && !activeLauncherOrigin) {
            sessionStorage.removeItem(MAUI_LAUNCHER_ORIGIN_STORAGE_KEY);
        }

        // Completed undo-capable editor sessions intentionally retain the launch ID so the
        // same popup can continue exchanging trusted Undo Last Change messages with MAUI.
        if (
            activeMauiLaunchId &&
            sessionStorage.getItem(MAUI_COMPLETED_STORAGE_KEY) === activeMauiLaunchId
        ) {
            const keepEditorSession = sessionStorage.getItem(MAUI_EDITOR_UNDO_ENABLED_STORAGE_KEY) === "1";
            sessionStorage.removeItem(MAUI_TARGET_HASH_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_OVERLAY_PENDING_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_CONFIGURED_STORAGE_KEY);
            if (!keepEditorSession) {
                sessionStorage.removeItem(MAUI_LAUNCH_STORAGE_KEY);
                sessionStorage.removeItem(MAUI_LAUNCHER_ORIGIN_STORAGE_KEY);
                activeMauiLaunchId = "";
                activeLauncherOrigin = "";
            }
        }
    }

    // Purpose: Return true only while this exact MOVEit window belongs to an active MAUI launch.
    function hasActiveMauiLaunch() {
        return activeMauiLaunchId.length > 0;
    }

    function isEditorUndoSessionActive() {
        return hasActiveMauiLaunch() &&
            sessionStorage.getItem(MAUI_EDITOR_UNDO_ENABLED_STORAGE_KEY) === "1" &&
            Boolean(sessionStorage.getItem(MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY)) &&
            Boolean(sessionStorage.getItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY));
    }

    function getCurrentUndoEditorRouteIdentity() {
        const hash = window.location.hash || "";
        const patterns = [
            // Task Details can navigate through multiple child routes in the same native editor window,
            // especially for Advanced Task editing. Every child route under the same Task ID shares
            // the same one-level undo session.
            ["Task", /^#?\/tasks\/details\/([^/?]+)(?:\/[^?]*)?(?:\?.*)?$/i],
            ["Host", /^#?\/hosts\/([^?]+)/i],
            ["Script", /^#?\/resources\/scripts\/([^?]+)/i],
            ["DateList", /^#?\/resources\/datelists\/([^?]+)/i]
        ];
        for (const [objectType, pattern] of patterns) {
            const match = pattern.exec(hash);
            if (!match) continue;
            let objectId = match[1];
            try { objectId = decodeURIComponent(objectId); }
            catch { /* Keep the route value unchanged if it is not valid URI encoding. */ }
            return { objectType, objectId };
        }
        return null;
    }

    function isCurrentUndoEditorRoute() {
        if (!isEditorUndoSessionActive()) return false;
        const type = sessionStorage.getItem(MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY) || "";
        const id = sessionStorage.getItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY) || "";
        const routeIdentity = getCurrentUndoEditorRouteIdentity();
        return Boolean(routeIdentity) && routeIdentity.objectType === type && routeIdentity.objectId === id;
    }

    // Purpose: Return true only while the initial MAUI login/redirect workflow is pending.
    function isMauiOverlayPending() {
        return (
            hasActiveMauiLaunch() &&
            sessionStorage.getItem(MAUI_OVERLAY_PENDING_STORAGE_KEY) === activeMauiLaunchId &&
            sessionStorage.getItem(MAUI_COMPLETED_STORAGE_KEY) !== activeMauiLaunchId
        );
    }

    // Purpose: Install the presentation-only animation used by the MAUI launch overlay.
    function ensureMauiWorkingOverlayStyles() {
        if (document.getElementById(MAUI_WORKING_STYLE_ID)) {
            return;
        }

        const style = document.createElement("style");
        style.id = MAUI_WORKING_STYLE_ID;
        style.textContent = `
            @keyframes mauiLaunchPulse {
                0%, 100% { transform: scale(1); opacity: 1; }
                50% { transform: scale(1.08); opacity: 0.72; }
            }

            #${MAUI_WORKING_LOGO_ID} {
                width: 88px !important;
                height: 88px !important;
                display: block !important;
                animation: mauiLaunchPulse 1.4s ease-in-out infinite !important;
                transform-origin: center center !important;
            }

            @media (prefers-reduced-motion: reduce) {
                #${MAUI_WORKING_LOGO_ID} {
                    animation: none !important;
                }
            }
        `;

        (document.head || document.documentElement).appendChild(style);
    }

    // Purpose: Cover the underlying MOVEit page only while the initial MAUI launch is in progress.
    // The local SVG is presentation-only; Working... remains as a fallback if the image cannot load.
    function ensureMauiWorkingOverlay(force) {
        if ((!force && !isMauiOverlayPending()) || document.getElementById(MAUI_WORKING_OVERLAY_ID)) {
            return;
        }

        ensureMauiWorkingOverlayStyles();

        const overlay = document.createElement("div");
        overlay.id = MAUI_WORKING_OVERLAY_ID;
        overlay.setAttribute("role", "status");
        overlay.setAttribute("aria-live", "polite");
        overlay.setAttribute("aria-label", "Working");

        overlay.style.setProperty("position", "fixed", "important");
        overlay.style.setProperty("inset", "0", "important");
        overlay.style.setProperty("display", "flex", "important");
        overlay.style.setProperty("align-items", "center", "important");
        overlay.style.setProperty("justify-content", "center", "important");
        overlay.style.setProperty("background", "#ffffff", "important");
        overlay.style.setProperty("color", "#212529", "important");
        overlay.style.setProperty("font-family", "Arial, Helvetica, sans-serif", "important");
        overlay.style.setProperty("font-size", "1.1rem", "important");
        overlay.style.setProperty("font-weight", "500", "important");
        overlay.style.setProperty("z-index", "2147483647", "important");

        const logo = document.createElement("img");
        logo.id = MAUI_WORKING_LOGO_ID;
        logo.src = MAUI_WORKING_LOGO_URL;
        logo.alt = "";
        logo.setAttribute("aria-hidden", "true");

        const fallback = document.createElement("div");
        fallback.textContent = "Working...";
        fallback.style.display = "none";

        logo.addEventListener("error", function () {
            logo.style.display = "none";
            fallback.style.display = "block";
            console.warn("maui-integration.js: MAUI launch logo failed to load:", MAUI_WORKING_LOGO_URL);
        }, { once: true });

        overlay.appendChild(logo);
        overlay.appendChild(fallback);
        (document.body || document.documentElement).appendChild(overlay);
    }

    // Purpose: Remove only the visual mask. Editor presentation remains active.
    function removeMauiWorkingOverlay() {
        const overlay = document.getElementById(MAUI_WORKING_OVERLAY_ID);
        if (overlay) {
            overlay.remove();
        }
    }

    function clearUndoResponseWatchdog() {
        if (!undoResponseTimer) return;
        window.clearTimeout(undoResponseTimer);
        undoResponseTimer = null;
    }

    function clearUndoIdentityRebindWatchdog() {
        if (!undoIdentityRebindTimer) return;
        window.clearTimeout(undoIdentityRebindTimer);
        undoIdentityRebindTimer = null;
    }

    function endLocalEditorUndoSession(message) {
        clearUndoResponseWatchdog();
        clearUndoIdentityRebindWatchdog();
        suppressEditorUpdatedToast = false;
        sessionStorage.removeItem(MAUI_EDITOR_UNDO_ENABLED_STORAGE_KEY);
        sessionStorage.removeItem(MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY);
        sessionStorage.removeItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY);
        sessionStorage.removeItem(MAUI_UNDO_RELOAD_STORAGE_KEY);
        sessionStorage.removeItem(MAUI_UNDO_AVAILABLE_STORAGE_KEY);
        undoStateRequestedForLaunch = "";
        undoToastArmedForLaunch = "";
        undoIdentityRebindRequestedFor = "";
        setUndoButtonAvailable(false);
        hideUndoDialog();
        removeMauiWorkingOverlay();
        if (message) console.info("maui-integration.js:", message);
    }

    // Purpose: End only the transient login/launch handshake. The stored editor skin
    // and title intentionally remain so the native editor survives browser refreshes.
    function clearMauiLaunchContext(markCompleted, preserveEditorUndoSession) {
        if (overlayCompletionTimer) {
            window.clearTimeout(overlayCompletionTimer);
            overlayCompletionTimer = null;
        }

        const launchId = activeMauiLaunchId;
        if (markCompleted && launchId) {
            sessionStorage.setItem(MAUI_COMPLETED_STORAGE_KEY, launchId);
        }

        sessionStorage.removeItem(MAUI_TARGET_HASH_STORAGE_KEY);
        sessionStorage.removeItem(MAUI_OVERLAY_PENDING_STORAGE_KEY);
        sessionStorage.removeItem(MAUI_CONFIGURED_STORAGE_KEY);
        if (!preserveEditorUndoSession) {
            clearUndoResponseWatchdog();
            clearUndoIdentityRebindWatchdog();
            sessionStorage.removeItem(MAUI_LAUNCH_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_LAUNCHER_ORIGIN_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_EDITOR_UNDO_ENABLED_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_UNDO_RELOAD_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_UNDO_AVAILABLE_STORAGE_KEY);
            activeMauiLaunchId = "";
            activeLauncherOrigin = "";
            undoStateRequestedForLaunch = "";
            undoToastArmedForLaunch = "";
            undoIdentityRebindRequestedFor = "";
            setUndoButtonAvailable(false);
        }
        removeMauiWorkingOverlay();
    }

    // Purpose: Complete the initial MAUI launch only after the opener has explicitly
    // configured the authenticated target page. CSS loading is handled independently
    // so a stylesheet problem can never strand the page behind Working....
    function completeMauiLaunchIfReady() {
        if (!isMauiOverlayPending() || overlayCompletionTimer) {
            return;
        }

        const targetHash = sessionStorage.getItem(MAUI_TARGET_HASH_STORAGE_KEY) || "";
        const configuredId = sessionStorage.getItem(MAUI_CONFIGURED_STORAGE_KEY) || "";

        if (configuredId !== activeMauiLaunchId) {
            return;
        }

        if (!targetHash || window.location.hash !== targetHash || !isAuthenticatedPageRendered()) {
            return;
        }

        activateStoredEditorPresentation();

        // If MAUI skin loading is still in progress, keep the overlay briefly so
        // the user does not see a flash of the unskinned native editor. An explicit
        // stylesheet failure is considered terminal and reveals the usable page.
        if (isMauiSkinRequested() && mauiStylesheetState === "loading") {
            return;
        }

        overlayCompletionTimer = window.setTimeout(function () {
            overlayCompletionTimer = null;

            const currentTargetHash = sessionStorage.getItem(MAUI_TARGET_HASH_STORAGE_KEY) || "";
            const currentConfiguredId = sessionStorage.getItem(MAUI_CONFIGURED_STORAGE_KEY) || "";

            if (
                !isMauiOverlayPending() ||
                currentConfiguredId !== activeMauiLaunchId ||
                !currentTargetHash ||
                window.location.hash !== currentTargetHash ||
                !isAuthenticatedPageRendered()
            ) {
                return;
            }

            activateStoredEditorPresentation();

            // Acknowledge completion before clearing the launch nonce so MAUI can
            // retire its matching active-launch record deterministically.
            sendToLauncher({
                type: "MAUI_EDITOR_READY",
                hash: window.location.hash || "",
                skinLoaded: !isMauiSkinRequested() || mauiStylesheetState === "loaded"
            });

            clearMauiLaunchContext(true, isEditorUndoSessionActive());
            ensureNativeEditorUndoUi();
            if (isEditorUndoSessionActive()) {
                const identity = currentUndoIdentity();
                sendToLauncher({ type: "MAUI_EDITOR_STATE_REQUEST", objectType: identity.objectType, objectId: identity.objectId });
            }
        }, 150);
    }

    // Purpose: Send a trusted postMessage response to the MAUI opener. Ordinary
    // MOVEit windows never participate because they do not have a MAUI launch ID.
    function sendToLauncher(message) {
        if (!window.opener || !hasActiveMauiLaunch()) return;

        const targetOrigins = getLauncherTargetOrigins();
        if (!targetOrigins.length) {
            console.warn("maui-integration.js: no trusted launcher origins are configured.");
            return;
        }

        const payload = Object.assign({}, message, { launchId: activeMauiLaunchId });
        for (const targetOrigin of targetOrigins) {
            try {
                window.opener.postMessage(payload, targetOrigin);
            } catch (error) {
                console.warn("maui-integration.js: unable to send message to launcher origin " + targetOrigin + ".");
            }
        }
    }

    // Purpose: Read query parameters embedded in the current hash route.
    function getHashParameters() {
        const hash = window.location.hash || "";
        const queryIndex = hash.indexOf("?");

        if (queryIndex < 0) {
            return new URLSearchParams();
        }

        return new URLSearchParams(hash.substring(queryIndex + 1));
    }

    // Purpose: Check whether a parameter exists in the normal query string or hash route.
    function hasParameter(name) {
        if (new URLSearchParams(window.location.search).has(name)) {
            return true;
        }

        return getHashParameters().has(name);
    }

    // Purpose: Determine whether the current hash represents a MOVEit login/authentication route.
    function isLoginHash(hash) {
        const value = String(hash || "").toLowerCase();

        return (
            value.includes("/login") ||
            value.includes("/signin") ||
            value.includes("/sign-in") ||
            value.includes("/auth")
        );
    }


    /* ============================================================
     * MAUI NATIVE EDITOR PRESENTATION
     * ============================================================ */

    // Purpose: Normalize the requested native-editor skin name.
    function normalizeSkinName(value) {
        return typeof value === "string" ? value.trim().toLowerCase() : "";
    }

    // Purpose: Store and apply the requested native-editor window title.
    function setRequestedWindowTitle(title) {
        const normalizedTitle = typeof title === "string" ? title.trim() : "";
        if (!normalizedTitle) {
            return;
        }

        sessionStorage.setItem(MAUI_TITLE_STORAGE_KEY, normalizedTitle);
        document.title = normalizedTitle;
    }

    // Purpose: Return true when the MAUI native-editor skin was requested for this window.
    function isMauiSkinRequested() {
        return normalizeSkinName(sessionStorage.getItem(MAUI_SKIN_STORAGE_KEY)) === MAUI_SKIN_NAME;
    }

    // Purpose: Load the MAUI native-editor stylesheet once using the same stable injection path
    // used by the native-editor launch lifecycle; launch/overlay state does
    // not control whether the stylesheet is applied.
    function ensureMauiStylesheet() {
        if (mauiStylesheetState === "failed") {
            return null;
        }

        let link = document.getElementById(MAUI_STYLESHEET_ID);
        if (link) {
            if (link.dataset.mauiLoaded === "true") {
                mauiStylesheetState = "loaded";
            } else if (mauiStylesheetState === "idle") {
                mauiStylesheetState = "loading";
            }
            return link;
        }

        if (!document.head) {
            return null;
        }

        link = document.createElement("link");
        link.id = MAUI_STYLESHEET_ID;
        link.rel = "stylesheet";
        link.type = "text/css";
        link.href = MAUI_STYLESHEET_URL;
        link.dataset.mauiLoaded = "false";
        mauiStylesheetState = "loading";

        link.addEventListener("load", function () {
            link.dataset.mauiLoaded = "true";
            mauiStylesheetState = "loaded";
            console.info("maui-integration.js: MAUI stylesheet loaded:", link.href);
            sendToLauncher({ type: "MOVEIT_SKIN_LOADED", url: link.href });
            completeMauiLaunchIfReady();
        });

        link.addEventListener("error", function () {
            mauiStylesheetState = "failed";
            console.error("maui-integration.js: MAUI stylesheet failed to load:", link.href);
            sendToLauncher({
                type: "MOVEIT_SKIN_ERROR",
                message: "MOVEit could not load /maui-native-editor.css."
            });
            link.remove();

            // Never leave the editor hidden because of a presentation-only failure.
            completeMauiLaunchIfReady();
        });

        document.head.appendChild(link);
        return link;
    }

    // Purpose: Apply the stored MAUI title and native-editor skin after authentication.
    function activateStoredEditorPresentation() {
        const fields = getLoginFields();

        // The MAUI skin is never applied to MOVEit's login form. This deliberately
        // keeps the login/submit behavior isolated from presentation changes.
        if (fields.username || fields.password || isLoginHash(window.location.hash)) {
            return;
        }

        if (isMauiSkinRequested()) {
            ensureMauiStylesheet();
            if (document.body) {
                document.body.classList.add(MAUI_BODY_CLASS);
            }
        }

        const title = sessionStorage.getItem(MAUI_TITLE_STORAGE_KEY);
        if (title && document.title !== title) {
            document.title = title;
        }
    }

    // Purpose: Store the MAUI native-editor presentation requested by the opener.
    function storeEditorConfiguration(data) {
        if (!data || typeof data !== "object") {
            return;
        }

        // A new MAUI editor configuration must never inherit visible undo state
        // from a previous route/editor that happened to use this browser window.
        setUndoButtonAvailable(false);
        undoStateRequestedForLaunch = "";
        undoToastArmedForLaunch = "";
        undoIdentityRebindRequestedFor = "";

        if (typeof data.returnHash === "string" && data.returnHash) {
            sessionStorage.setItem(MAUI_TARGET_HASH_STORAGE_KEY, data.returnHash);
        }

        if (normalizeSkinName(data.skin) === MAUI_SKIN_NAME) {
            sessionStorage.setItem(MAUI_SKIN_STORAGE_KEY, MAUI_SKIN_NAME);
        }

        if (typeof data.windowTitle === "string" && data.windowTitle.trim()) {
            sessionStorage.setItem(MAUI_TITLE_STORAGE_KEY, data.windowTitle.trim());
        }

        if (data.editorUndoEnabled === true && typeof data.objectType === "string" && typeof data.objectId === "string") {
            sessionStorage.setItem(MAUI_EDITOR_UNDO_ENABLED_STORAGE_KEY, "1");
            sessionStorage.setItem(MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY, data.objectType.trim());
            sessionStorage.setItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY, data.objectId.trim());
        } else {
            sessionStorage.removeItem(MAUI_EDITOR_UNDO_ENABLED_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY);
            sessionStorage.removeItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY);
        }

        if (hasActiveMauiLaunch()) {
            sessionStorage.setItem(MAUI_CONFIGURED_STORAGE_KEY, activeMauiLaunchId);
        }

        activateStoredEditorPresentation();
        completeMauiLaunchIfReady();
        requestUndoIdentityRebindIfNeeded();
        ensureNativeEditorUndoUi();
        requestUndoStateIfNeeded();
        processUpdateToasts();
        completeUndoReloadIfReady();
    }

    // Purpose: Determine whether MOVEit has rendered a recognizable authenticated page.
    function isAuthenticatedPageRendered() {
        const fields = getLoginFields();
        if (fields.username || fields.password || isLoginHash(window.location.hash)) {
            return false;
        }

        return Boolean(
            document.querySelector("nav.ipsmica_nav_main") ||
            document.querySelector(".ipsmica-detail") ||
            document.querySelector(".ipsmica_container")
        );
    }

    // Purpose: Notify MAUI only after a real login or authenticated MOVEit page can be identified.
    function announcePageReadyIfNeeded() {
        if (!hasActiveMauiLaunch()) {
            return;
        }
        if (isEditorUndoSessionActive() && sessionStorage.getItem(MAUI_COMPLETED_STORAGE_KEY) === activeMauiLaunchId) {
            return;
        }

        const fields = getLoginFields();
        const loginRequired = Boolean(fields.username && fields.password);
        const authenticatedRendered = isAuthenticatedPageRendered();

        // An ambiguous early Angular bootstrap state is not announced. Wait until
        // either the real login fields or an authenticated page is actually rendered.
        if (!loginRequired && !authenticatedRendered) {
            return;
        }

        const signature = [
            window.location.hash || "",
            loginRequired ? "login" : "authenticated"
        ].join("|");

        if (signature === lastPageReadySignature) {
            return;
        }

        lastPageReadySignature = signature;
        console.info("maui-integration.js: MOVEIT_PAGE_READY", {
            hash: window.location.hash || "",
            loginRequired: loginRequired
        });
        sendToLauncher({
            type: "MOVEIT_PAGE_READY",
            hash: window.location.hash || "",
            loginRequired: loginRequired
        });
    }


    /* ============================================================
     * HideNav
     * ============================================================ */

    // Purpose: Hide MOVEit navigation chrome when HideNav is requested.
    function applyHideNav() {
        if (!hasParameter("HideNav")) {
            return;
        }

        /*
         * Only the MOVEit navigation chrome is hidden here. Route-specific
         * backlink visibility is handled by maui-native-editor.css.
         */
        document
            .querySelectorAll("nav.navbar, .navbar")
            .forEach(function (element) {
                element.style.setProperty("display", "none", "important");
            });
    }


    /* ============================================================
     * LOGIN HELPERS
     * ============================================================ */

    // Purpose: Locate the MOVEit username and password controls.
    function getLoginFields() {
        return {
            username: document.getElementById("field_username"),
            password: document.getElementById("field_password")
        };
    }

    // Purpose: Set a framework-managed input value and raise the events Angular expects.
    function setInputValue(input, value) {
        /*
         * The native HTMLInputElement value setter is used because it is more reliable
         * with framework-managed controls than assigning input.value directly.
         */
        const descriptor = Object.getOwnPropertyDescriptor(
            HTMLInputElement.prototype,
            "value"
        );

        if (descriptor && typeof descriptor.set === "function") {
            descriptor.set.call(input, value);
        } else {
            input.value = value;
        }

        /*
         * The input event is dispatched for Angular form bindings and change for compatibility
         * with any additional listeners.
         */
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
    }

    // Purpose: Locate the login submit control associated with the MOVEit credential fields.
    function findSubmitButton(username, password) {
        const form =
            username.closest("form") ||
            password.closest("form");

        if (form) {
            return (
                form.querySelector('button[type="submit"]') ||
                form.querySelector('input[type="submit"]') ||
                form.querySelector("button")
            );
        }

        return (
            document.querySelector('button[type="submit"]') ||
            document.querySelector('input[type="submit"]') ||
            document.querySelector("button")
        );
    }

    // Purpose: Notify MAUI when the MOVEit login controls are ready for credentials.
    function announceLoginReadyIfNeeded() {
        if (!hasActiveMauiLaunch()) {
            return;
        }

        const fields = getLoginFields();

        if (!fields.username || !fields.password) {
            /*
             * A new READY notification is allowed if Angular destroys and recreates
             * the login controls.
             */
            currentUsernameElement = null;
            readySentForCurrentLoginForm = false;
            return;
        }

        if (currentUsernameElement !== fields.username) {
            currentUsernameElement = fields.username;
            readySentForCurrentLoginForm = false;
        }

        if (readySentForCurrentLoginForm) {
            return;
        }

        readySentForCurrentLoginForm = true;

        console.info("maui-integration.js: login form ready; sending MOVEIT_READY to trusted launcher origin(s)", getLauncherTargetOrigins());
        sendToLauncher({
            type: "MOVEIT_READY"
        });
    }

    // Purpose: Wait for Angular to enable Login and then submit the MOVEit form.
    function clickSubmitWhenAngularIsReady(submitButton, frameNumber) {
        if (!submitStarted) {
            return;
        }

        /*
         * Angular receives one or more render cycles after the input events before
         * the Login button is required to be enabled.
         */
        if (!submitButton.disabled) {
            console.info("maui-integration.js: login button enabled; clicking submit.");
            sendToLauncher({
                type: "MOVEIT_LOGIN_SUBMITTING"
            });

            submitButton.click();
            return;
        }

        if (frameNumber >= MAX_SUBMIT_WAIT_FRAMES) {
            submitStarted = false;

            console.warn(
                "maui-integration.js: Login button remained disabled after credentials were populated."
            );

            sendToLauncher({
                type: "MOVEIT_LOGIN_ERROR",
                message: "Login button remained disabled."
            });

            clearMauiLaunchContext();

            return;
        }

        requestAnimationFrame(function () {
            clickSubmitWhenAngularIsReady(
                submitButton,
                frameNumber + 1
            );
        });
    }

    // Purpose: Apply pending credentials when the MOVEit login controls are available.
    function tryProcessLogin() {
        if (!pendingLogin || submitStarted) {
            return;
        }

        const fields = getLoginFields();

        /*
         * Retains a launcher message that arrives before Angular creates the login
         * controls; MutationObserver calls this again when the controls appear.
         */
        if (!fields.username || !fields.password) {
            return;
        }

        const submitButton = findSubmitButton(
            fields.username,
            fields.password
        );

        if (!submitButton) {
            return;
        }

        /*
         * Saves the originally requested route before authentication changes the
         * Angular route.
         */
        if (pendingLogin.returnHash) {
            sessionStorage.setItem(
                STORAGE_KEY,
                pendingLogin.returnHash
            );
            sessionStorage.setItem(
                MAUI_TARGET_HASH_STORAGE_KEY,
                pendingLogin.returnHash
            );
        }

        setInputValue(
            fields.username,
            pendingLogin.username
        );

        setInputValue(
            fields.password,
            pendingLogin.password
        );

        console.info("maui-integration.js: MOVEIT_LOGIN received; credentials populated into MOVEit login controls.");

        /*
         * Clears the pending credential copy as soon as Populates the MOVEit form.
         */
        pendingLogin.username = "";
        pendingLogin.password = "";

        submitStarted = true;

        sendToLauncher({
            type: "MOVEIT_LOGIN_RECEIVED"
        });

        requestAnimationFrame(function () {
            clickSubmitWhenAngularIsReady(
                submitButton,
                1
            );
        });
    }


    /* ============================================================
     * ROUTE RESTORATION
     * ============================================================ */

    // Purpose: Restore the requested MOVEit route after login completes.
    function restoreOriginalRoute() {
        const savedHash = sessionStorage.getItem(STORAGE_KEY);

        if (!savedHash) {
            return;
        }

        const fields = getLoginFields();

        /*
         * Treats visible login controls as proof that authentication has not
         * completed yet.
         */
        if (fields.username || fields.password) {
            return;
        }

        /*
         * Does not restore the requested route while MOVEit is still on a login route.
         */
        if (isLoginHash(window.location.hash)) {
            return;
        }

        /*
         * Clears the stored route before navigation so an invalid or unauthorized
         * route cannot create an endless redirect loop.
         */
        sessionStorage.removeItem(STORAGE_KEY);

        if (window.location.hash !== savedHash) {
            window.location.hash = savedHash;
        }

        activateStoredEditorPresentation();

        sendToLauncher({
            type: "MOVEIT_ROUTE_RESTORED",
            hash: savedHash
        });
    }


    /* ============================================================
     * EDITOR-LOCAL UNDO LAST CHANGE
     * ============================================================ */

    function currentUndoIdentity() {
        return {
            launchId: activeMauiLaunchId,
            objectType: sessionStorage.getItem(MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY) || "",
            objectId: sessionStorage.getItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY) || ""
        };
    }

    function undoMessageMatchesCurrentIdentity(data) {
        if (!data || !isCurrentUndoEditorRoute()) return false;
        const identity = currentUndoIdentity();
        return data.launchId === identity.launchId &&
            String(data.objectType || "") === identity.objectType &&
            String(data.objectId || "") === identity.objectId;
    }

    function isUndoAvailableForCurrentLaunch() {
        return Boolean(activeMauiLaunchId) &&
            sessionStorage.getItem(MAUI_UNDO_AVAILABLE_STORAGE_KEY) === activeMauiLaunchId;
    }

    function hideUndoButtonVisual() {
        const button = document.getElementById(MAUI_UNDO_BUTTON_ID);
        if (!button) return;
        button.hidden = true;
        button.disabled = false;
        button.closest(".ipsmica-detail__header")?.classList.remove("maui-native-undo-host--active");
    }

    function setUndoButtonAvailable(available) {
        if (available === true && isEditorUndoSessionActive()) {
            sessionStorage.setItem(MAUI_UNDO_AVAILABLE_STORAGE_KEY, activeMauiLaunchId);
        } else {
            sessionStorage.removeItem(MAUI_UNDO_AVAILABLE_STORAGE_KEY);
        }

        if (available !== true || !isCurrentUndoEditorRoute()) {
            hideUndoButtonVisual();
            return;
        }

        ensureNativeEditorUndoUi();
        const button = document.getElementById(MAUI_UNDO_BUTTON_ID);
        if (!button) return;
        const identity = currentUndoIdentity();
        if (button.dataset.mauiLaunchId !== identity.launchId ||
            button.dataset.mauiObjectType !== identity.objectType ||
            button.dataset.mauiObjectId !== identity.objectId) {
            hideUndoButtonVisual();
            return;
        }
        button.hidden = false;
        button.disabled = false;
        button.closest(".ipsmica-detail__header")?.classList.add("maui-native-undo-host--active");
    }

    function requestUndoIdentityRebindIfNeeded() {
        if (!isEditorUndoSessionActive() || !window.opener || window.opener.closed || !isAuthenticatedPageRendered()) return;

        const current = currentUndoIdentity();
        const routeIdentity = getCurrentUndoEditorRouteIdentity();
        if (current.objectType !== "Task" || !routeIdentity || routeIdentity.objectType !== "Task") return;
        if (!routeIdentity.objectId || routeIdentity.objectId === current.objectId) {
            undoIdentityRebindRequestedFor = "";
            return;
        }

        const transitionKey = `${current.objectId}->${routeIdentity.objectId}`;
        if (undoIdentityRebindRequestedFor === transitionKey) return;

        undoIdentityRebindRequestedFor = transitionKey;
        undoStateRequestedForLaunch = "";
        undoToastArmedForLaunch = "";
        setUndoButtonAvailable(false);

        // MOVEit creates a new Task ID during Traditional -> Advanced conversion. Until
        // MAUI has captured the new Task as the editor baseline, allowing the user to
        // immediately navigate/save on a child route can race the identity rebind and
        // make that first change impossible to undo. Reuse the existing non-destructive
        // Working overlay to hold the editor briefly while the new baseline is captured.
        ensureMauiWorkingOverlay(true);
        clearUndoIdentityRebindWatchdog();
        undoIdentityRebindTimer = window.setTimeout(function () {
            undoIdentityRebindTimer = null;
            undoIdentityRebindRequestedFor = "";
            endLocalEditorUndoSession(
                "Undo Last Change was disabled because MAUI did not finish establishing the new Task baseline. Close and reopen the editor to start a new undo session."
            );
        }, MAUI_UNDO_IDENTITY_REBIND_TIMEOUT_MS);

        sendToLauncher({
            type: "MAUI_EDITOR_IDENTITY_CHANGED",
            objectType: current.objectType,
            previousObjectId: current.objectId,
            objectId: routeIdentity.objectId
        });
    }

    function ensureNativeEditorUndoUi() {
        const existingButton = document.getElementById(MAUI_UNDO_BUTTON_ID);
        if (!isCurrentUndoEditorRoute() || !window.opener || window.opener.closed || !document.body) {
            if (existingButton) {
                existingButton.hidden = true;
                existingButton.closest(".ipsmica-detail__header")?.classList.remove("maui-native-undo-host--active");
            }
            return;
        }

        const header = document.querySelector(".ipsmica-detail__header");
        if (!header) {
            if (existingButton) existingButton.hidden = true;
            return;
        }

        header.classList.add("maui-native-undo-host");
        let button = existingButton;
        if (!button) {
            button = document.createElement("button");
            button.type = "button";
            button.id = MAUI_UNDO_BUTTON_ID;
            button.className = "maui-native-undo-button";
            button.textContent = "Undo Last Change";
            button.hidden = true;
            button.addEventListener("click", showUndoConfirmation);
        }
        if (button.parentElement !== header) {
            button.closest(".ipsmica-detail__header")?.classList.remove("maui-native-undo-host", "maui-native-undo-host--active");
            header.appendChild(button);
        }

        const identity = currentUndoIdentity();
        button.dataset.mauiLaunchId = identity.launchId;
        button.dataset.mauiObjectType = identity.objectType;
        button.dataset.mauiObjectId = identity.objectId;
        if (isUndoAvailableForCurrentLaunch()) {
            button.hidden = false;
            button.disabled = false;
            header.classList.add("maui-native-undo-host--active");
        } else {
            button.hidden = true;
            header.classList.remove("maui-native-undo-host--active");
        }
        ensureUndoDialog();
    }

    function primeExistingUpdateToasts() {
        document.querySelectorAll("#toast-container toast .toast-message, #toast-container .toast-message").forEach(function (messageElement) {
            processedUpdateToasts.add(messageElement.closest("toast") || messageElement);
        });
    }

    function requestUndoStateIfNeeded() {
        if (!isCurrentUndoEditorRoute() || !window.opener || window.opener.closed || !isAuthenticatedPageRendered()) return;
        if (undoToastArmedForLaunch !== activeMauiLaunchId) {
            primeExistingUpdateToasts();
            undoToastArmedForLaunch = activeMauiLaunchId;
        }
        if (undoStateRequestedForLaunch === activeMauiLaunchId) return;
        undoStateRequestedForLaunch = activeMauiLaunchId;
        ensureNativeEditorUndoUi();
        const identity = currentUndoIdentity();
        sendToLauncher({ type: "MAUI_EDITOR_STATE_REQUEST", objectType: identity.objectType, objectId: identity.objectId });
    }

    function ensureUndoDialog() {
        if (document.getElementById(MAUI_UNDO_DIALOG_ID) || !document.body) return;
        const backdrop = document.createElement("div");
        backdrop.id = MAUI_UNDO_DIALOG_ID;
        backdrop.className = "maui-native-undo-dialog";
        backdrop.hidden = true;
        backdrop.innerHTML = `
            <div class="maui-native-undo-dialog__panel" role="dialog" aria-modal="true" aria-labelledby="maui-undo-dialog-title">
                <div id="maui-undo-dialog-title" class="maui-native-undo-dialog__title">Undo Last Change?</div>
                <div class="maui-native-undo-dialog__message" data-maui-undo-message>
                    The item will be restored to its state before the last successful update. Any unsaved changes currently in the editor will be lost.
                </div>
                <div class="maui-native-undo-dialog__actions">
                    <button type="button" class="btn btn-secondary" data-maui-undo-cancel>Cancel</button>
                    <button type="button" class="btn btn-primary" data-maui-undo-confirm>Undo</button>
                </div>
            </div>`;
        backdrop.querySelector("[data-maui-undo-cancel]")?.addEventListener("click", hideUndoDialog);
        backdrop.querySelector("[data-maui-undo-confirm]")?.addEventListener("click", requestUndoLastChange);
        backdrop.addEventListener("click", function (event) { if (event.target === backdrop) hideUndoDialog(); });
        document.body.appendChild(backdrop);
    }

    function showUndoConfirmation() {
        ensureUndoDialog();
        const dialog = document.getElementById(MAUI_UNDO_DIALOG_ID);
        if (!dialog) return;
        const message = dialog.querySelector("[data-maui-undo-message]");
        if (message) message.textContent = "The item will be restored to its state before the last successful update. Any unsaved changes currently in the editor will be lost.";
        const confirm = dialog.querySelector("[data-maui-undo-confirm]");
        if (confirm) { confirm.hidden = false; confirm.disabled = false; }
        const cancel = dialog.querySelector("[data-maui-undo-cancel]");
        if (cancel) cancel.textContent = "Cancel";
        dialog.hidden = false;
        confirm?.focus();
    }

    function hideUndoDialog() {
        const dialog = document.getElementById(MAUI_UNDO_DIALOG_ID);
        if (dialog) dialog.hidden = true;
    }

    function showUndoError(message) {
        clearUndoResponseWatchdog();
        removeMauiWorkingOverlay();
        suppressEditorUpdatedToast = false;
        ensureUndoDialog();
        const dialog = document.getElementById(MAUI_UNDO_DIALOG_ID);
        if (!dialog) return;
        const text = dialog.querySelector("[data-maui-undo-message]");
        if (text) text.textContent = String(message || "Unable to undo the last change.");
        const confirm = dialog.querySelector("[data-maui-undo-confirm]");
        if (confirm) confirm.hidden = true;
        const cancel = dialog.querySelector("[data-maui-undo-cancel]");
        if (cancel) cancel.textContent = "Close";
        dialog.hidden = false;
        cancel?.focus();
    }

    function requestUndoLastChange() {
        if (!isCurrentUndoEditorRoute()) return;
        if (!window.opener || window.opener.closed) {
            showUndoError("The MAUI window that opened this editor is no longer available. Close and reopen the editor before using Undo Last Change.");
            return;
        }
        hideUndoDialog();
        suppressEditorUpdatedToast = true;
        const button = document.getElementById(MAUI_UNDO_BUTTON_ID);
        if (button) button.disabled = true;
        ensureMauiWorkingOverlay(true);
        clearUndoResponseWatchdog();
        undoResponseTimer = window.setTimeout(function () {
            undoResponseTimer = null;
            suppressEditorUpdatedToast = false;
            setUndoButtonAvailable(false);
            showUndoError("MAUI did not respond to the Undo Last Change request. The editor has been uncovered, but Undo remains unavailable until the editor is closed and reopened.");
        }, MAUI_UNDO_RESPONSE_TIMEOUT_MS);
        const identity = currentUndoIdentity();
        sendToLauncher({ type: "MAUI_EDITOR_UNDO_REQUEST", objectType: identity.objectType, objectId: identity.objectId });
    }

    function processUpdateToasts() {
        if (!isCurrentUndoEditorRoute() || suppressEditorUpdatedToast || undoToastArmedForLaunch !== activeMauiLaunchId) return;
        document.querySelectorAll("#toast-container toast .toast-message, #toast-container .toast-message").forEach(function (messageElement) {
            const toastElement = messageElement.closest("toast") || messageElement;
            if (processedUpdateToasts.has(toastElement)) return;
            const message = (messageElement.textContent || "").trim();
            if (!message) return;
            if (/\b(?:updated|saved)\b/i.test(message)) {
                processedUpdateToasts.add(toastElement);
                // A newer save invalidates the previously displayed one-level Undo until
                // MAUI finishes capturing the new rolling baseline. Clear the popup's
                // persisted availability immediately so route changes cannot briefly
                // resurrect an older Undo while that capture is in flight.
                setUndoButtonAvailable(false);
                const identity = currentUndoIdentity();
                sendToLauncher({ type: "MAUI_EDITOR_UPDATED", objectType: identity.objectType, objectId: identity.objectId, message: message });
            }
        });
    }

    function completeUndoReloadIfReady() {
        if (!isEditorUndoSessionActive()) return;
        if (sessionStorage.getItem(MAUI_UNDO_RELOAD_STORAGE_KEY) !== activeMauiLaunchId) return;
        ensureMauiWorkingOverlay(true);
        if (!isAuthenticatedPageRendered()) return;
        window.setTimeout(function () {
            sessionStorage.removeItem(MAUI_UNDO_RELOAD_STORAGE_KEY);
            removeMauiWorkingOverlay();
            ensureNativeEditorUndoUi();
            setUndoButtonAvailable(false);
            undoStateRequestedForLaunch = "";
            requestUndoStateIfNeeded();
        }, 150);
    }

    /* ============================================================
     * postMessage RECEIVER
     * ============================================================ */

    window.addEventListener("message", function (event) {
        const data = event.data;
        const messageType = data && typeof data.type === "string" ? data.type : "";

        /*
         * Accepts messages only from an explicitly configured trusted launcher origin.
         * Once one valid opener message is accepted, this popup remains bound to that
         * exact origin for the rest of the launch/Undo session.
         */
        if (!isTrustedLauncherOrigin(event.origin) || (activeLauncherOrigin && event.origin !== activeLauncherOrigin)) {
            if (messageType === "MOVEIT_LOGIN" || messageType.indexOf("MAUI_") === 0) {
                console.warn(
                    "maui-integration.js: rejected " + messageType +
                    " because event.origin '" + event.origin + "' is not the trusted origin bound to this launch."
                );
            }
            return;
        }

        /*
         * Accepts messages only from the window that opened this MOVEit window.
         */
        if (!window.opener || event.source !== window.opener) {
            if (messageType === "MOVEIT_LOGIN" || messageType.indexOf("MAUI_") === 0) {
                console.warn("maui-integration.js: rejected " + messageType + " because event.source was not window.opener.");
            }
            return;
        }

        if (!data || typeof data.type !== "string") {
            return;
        }

        if (!hasActiveMauiLaunch()) {
            return;
        }

        if (typeof data.launchId !== "string" || data.launchId !== activeMauiLaunchId) {
            if (messageType === "MOVEIT_LOGIN" || messageType.indexOf("MAUI_") === 0) {
                console.warn("maui-integration.js: rejected " + messageType + " because its launch ID did not match this window.");
            }
            return;
        }

        if (!bindLauncherOrigin(event.origin)) {
            return;
        }

        if (data.type === "MAUI_EDITOR_CONFIG") {
            storeEditorConfiguration(data);
            return;
        }

        if (data.type === "MAUI_SET_WINDOW_TITLE") {
            setRequestedWindowTitle(data.title);
            return;
        }

        if (data.type === "MAUI_EDITOR_IDENTITY_REBOUND") {
            const storedType = sessionStorage.getItem(MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY) || "";
            const storedId = sessionStorage.getItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY) || "";
            const previousObjectId = String(data.previousObjectId || "");
            const objectId = String(data.objectId || "");
            if (storedType !== "Task" || String(data.objectType || "") !== "Task" || previousObjectId !== storedId || !objectId) return;

            sessionStorage.setItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY, objectId);
            sessionStorage.removeItem(MAUI_UNDO_AVAILABLE_STORAGE_KEY);
            undoIdentityRebindRequestedFor = "";
            undoStateRequestedForLaunch = "";
            undoToastArmedForLaunch = "";
            clearUndoIdentityRebindWatchdog();
            setUndoButtonAvailable(false);
            removeMauiWorkingOverlay();
            ensureNativeEditorUndoUi();
            requestUndoStateIfNeeded();
            return;
        }

        if (data.type === "MAUI_EDITOR_IDENTITY_REBIND_FAILED") {
            const storedType = sessionStorage.getItem(MAUI_EDITOR_OBJECT_TYPE_STORAGE_KEY) || "";
            const storedId = sessionStorage.getItem(MAUI_EDITOR_OBJECT_ID_STORAGE_KEY) || "";
            if (storedType !== "Task" || String(data.objectType || "") !== "Task" || String(data.previousObjectId || "") !== storedId) return;
            undoIdentityRebindRequestedFor = "";
            clearUndoIdentityRebindWatchdog();
            endLocalEditorUndoSession(data.message || "Undo Last Change could not continue for the new Task.");
            return;
        }

        if (data.type === "MAUI_EDITOR_SESSION_ENDED") {
            if (!undoMessageMatchesCurrentIdentity(data)) return;
            endLocalEditorUndoSession(data.message || "The MAUI editor undo session ended.");
            return;
        }

        if (data.type === "MAUI_EDITOR_UNDO_STATE") {
            if (undoMessageMatchesCurrentIdentity(data)) setUndoButtonAvailable(data.available === true);
            return;
        }

        if (data.type === "MAUI_EDITOR_UNDO_RESULT") {
            if (!undoMessageMatchesCurrentIdentity(data)) return;
            clearUndoResponseWatchdog();
            if (data.success === true) {
                setUndoButtonAvailable(false);
                sessionStorage.setItem(MAUI_UNDO_RELOAD_STORAGE_KEY, activeMauiLaunchId);
                window.location.reload();
            } else {
                // Restore failures are fail-closed. MAUI re-establishes the current
                // baseline separately; a later clean native save can create a new Undo.
                setUndoButtonAvailable(false);
                showUndoError(data.message || "Unable to undo the last change.");
            }
            return;
        }

        if (data.type !== "MOVEIT_LOGIN") {
            return;
        }

        console.info("maui-integration.js: accepted MOVEIT_LOGIN from", event.origin);

        if (
            typeof data.username !== "string" ||
            typeof data.password !== "string" ||
            data.username.length === 0 ||
            data.password.length === 0
        ) {
            sendToLauncher({
                type: "MOVEIT_LOGIN_ERROR",
                message: "The login message did not contain valid credentials."
            });

            clearMauiLaunchContext();

            return;
        }

        // Remembers presentation preferences here, but Does not apply the skin on the login page.
        if (normalizeSkinName(data.skin) === MAUI_SKIN_NAME) {
            sessionStorage.setItem(MAUI_SKIN_STORAGE_KEY, MAUI_SKIN_NAME);
        }
        if (typeof data.windowTitle === "string" && data.windowTitle.trim()) {
            sessionStorage.setItem(MAUI_TITLE_STORAGE_KEY, data.windowTitle.trim());
        }

        /*
         * Processes only one login request for a single MOVEit page load.
         */
        if (submitStarted || pendingLogin) {
            return;
        }

        pendingLogin = {
            username: data.username,
            password: data.password,
            returnHash:
                typeof data.returnHash === "string"
                    ? data.returnHash
                    : ""
        };

        tryProcessLogin();
    });


    /* ============================================================
     * PAGE LIFECYCLE
     * ============================================================ */

    // Purpose: Re-evaluate login, route, navigation, and editor-presentation state after MOVEit changes the DOM.
    function processPage() {
        ensureMauiWorkingOverlay();
        applyHideNav();
        announcePageReadyIfNeeded();
        announceLoginReadyIfNeeded();
        tryProcessLogin();
        restoreOriginalRoute();
        activateStoredEditorPresentation();
        completeMauiLaunchIfReady();
        requestUndoIdentityRebindIfNeeded();
        ensureNativeEditorUndoUi();
        requestUndoStateIfNeeded();
        processUpdateToasts();
        completeUndoReloadIfReady();
    }

    initializeMauiLaunchContext();
    ensureMauiWorkingOverlay();

    if (!TRUSTED_LAUNCHER_ORIGINS.length) {
        console.warn("maui-integration.js: LAUNCHER_ORIGINS does not contain any valid HTTP/HTTPS origins; MAUI native-editor launches will not complete.");
    }

    console.info("maui-integration.js: loaded.", {
        moveitOrigin: window.location.origin,
        trustedLauncherOrigins: TRUSTED_LAUNCHER_ORIGINS,
        activeLauncherOrigin: activeLauncherOrigin || null,
        mauiLaunch: hasActiveMauiLaunch(),
        stylesheet: MAUI_STYLESHEET_URL
    });

    /*
     * Processes the current MOVEit page immediately after this script loads.
     */
    processPage();

    /*
     * Observes DOM changes because MOVEit's Angular UI can create or replace
     * controls after the initial HTML load; Avoids continuous polling.
     */
    const observer = new MutationObserver(function () {
        processPage();
    });

    observer.observe(document.documentElement, {
        childList: true,
        subtree: true
    });

    /*
     * Reprocesses the page after Angular hash-route changes.
     */
    window.addEventListener("hashchange", function () {
        // MOVEit can navigate between sub-routes (for example Task Details -> Settings)
        // without opening a new window. Re-establish the current editor state for the
        // new route and prime any already-rendered toast elements before watching for
        // subsequent successful updates.
        undoStateRequestedForLaunch = "";
        undoToastArmedForLaunch = "";
        if (!isCurrentUndoEditorRoute()) hideUndoButtonVisual();
        processPage();
    });

    /*
     * Reprocesses the page after browser back/forward cache restoration.
     */
    window.addEventListener("pageshow", processPage);
})();
