# MAUI Security Policy and Engineering Notes

- **Version:** `0.18.511-RC3`
- **Release:** Release Candidate 3 (pre-release)
- **Build:** `20261029`

> **Release Candidate 3 — not the final release.** This pre-release may contain defects, including security defects. The controls described here document implemented behavior, not a guarantee of security or production readiness.

MAUI is an administrative UI for MOVEit Automation. Security-sensitive behavior is designed around authenticated server-side MOVEit sessions, database-backed MAUI roles, ASP.NET Core antiforgery protection, server-side token storage, and conservative handling of browser-rendered content.

**In this guide:** [Supported versions](#supported-versions) · [Reporting a vulnerability](#reporting-a-vulnerability) · [Core request and identity controls](#core-request-and-identity-controls) · [Object and feature boundaries](#object-and-feature-boundaries) · [Deployment and scanning](#deployment-and-scanning)

## Supported versions

Security fixes are applied to the current supported MAUI release line. The current RC3 candidate is `0.18.511-RC3` build `20261029`. Older prerelease/candidate builds should be upgraded before security evaluation or deployment.

## Reporting a vulnerability

Report suspected security vulnerabilities privately. If the GitHub repository has **Private vulnerability reporting** enabled, use the repository's **Security > Report a vulnerability** workflow. Otherwise, contact the repository owner privately using the contact method published with the repository.

Do not open a public GitHub issue containing exploit details, credentials, secrets, sensitive logs, or reproduction material that would expose an unpatched vulnerability. Include the MAUI version/build, affected component, reproduction steps, impact, and any suggested remediation when available.

## Core request and identity controls

### Authentication and authorization

- ASP.NET Core uses a global fallback authorization policy that requires an authenticated user unless an endpoint explicitly declares `[AllowAnonymous]`.
- Security-sensitive MVC controllers also declare `[Authorize]` explicitly so the authorization boundary is visible in source and to static-analysis tools.
- MAUI Admin authorization requires both the current database-backed Admin role and a live server-side MOVEit session for normal application operations.
- The Upgrade flow has a separate target-version-specific authorization path by design.
- Login names use `DOMAIN\username`; forward slashes normalize to backslashes before native authentication and session creation. MAUI account keys and Security duplicate checks use invariant, case-insensitive identity matching. Existing slash-key accounts remain recognizable without changing their primary key or role; when both forms already exist the backslash account takes precedence. Login does not delete/merge existing accounts or infer additional permissions.
- MOVEit object grants are distinct from MAUI roles. The shared access service/filter checks native creation, edit, delete and run rights on mutation endpoints; UI hiding/disabling is not the authorization boundary. Task Edit Existing covers the full existing definition but does not grant New/Delete.
- Non-admin creation of Tasks, Hosts, Scripts and TLS/SSH/PGP resources requires an eligible Resource Group. Imports use creation rights; configuration exports, notifications and MAUI metadata updates use edit rights. Global Parameter management and Resource Group administration require a MOVEit administrator.
- Read-only editor views lock configuration, MAUI properties/metadata and rich-text fields. Information Save buttons stay hidden for missing/unverifiable edit permission, including across tabs. Read-only viewing does not prepare edit snapshots or acquire checkout; server write guards remain authoritative.
- Task folder management and assignment changes require a MOVEit administrator. Non-admins retain folder viewing/filtering; their object saves must preserve current folder placement.
- Shared Script/email template saves use the dedicated `TemplateAdmin` policy: an authenticated user with a live MOVEit session and either the MAUI Admin role or MOVEit administrator status. Both save endpoints enforce this independently of disabled buttons. Loading existing templates does not require this policy; normal session/object access rules still apply. Configuration template management and other MAUI administration retain the MAUI-only Admin policy.

### Audit configuration comparisons

MOVEit 2026 / engine 18 or later Audit comparisons use the signed-in user's REST session and native authorization. They are read-only, require no checkout and are not stored in MAUI. Snapshots are rendered as text, protected values remain in their native returned form, XML external entities/DTDs are disabled, and size/work limits apply. Close clears the view; stale responses cannot replace a newer selection. Server diagnostics log record IDs/error categories without recording snapshot bodies.

### Antiforgery protection

Normal state-changing MVC actions use ASP.NET Core antiforgery validation.

The Login POST deliberately validates the antiforgery token inside `AccountController.Login` by calling `IAntiforgery.ValidateRequestAsync(HttpContext)` rather than using only `[ValidateAntiForgeryToken]`. This allows MAUI to recover cleanly when a login page remains open across an IIS/application restart and its token becomes stale. The Login view still emits the normal ASP.NET Core antiforgery token. Login GET and POST explicitly use the same anonymous antiforgery principal, even if the browser still has an older authenticated cookie; normal authenticated endpoints retain identity-bound CSRF validation. Token validation failures redirect back to Login with an allowlisted `AF-*` code, without transporting security diagnostics through cookie-backed TempData.

On Windows, MAUI persists Data Protection keys under `Data/DataProtection-Keys` using local-machine DPAPI. The installation-owned directory must be writable by the MAUI app-pool identity, retained across upgrades, and excluded from deployment payloads. Machine-level DPAPI keys are tied to that Windows machine; multi-server deployments require a different shared-key strategy and protection mechanism. Do not copy the encrypted key ring onto a different machine and expect it to decrypt.

Static-analysis rule `WS-I011-CSHARP-00008` may report the Login POST because the validation is implemented explicitly rather than by the action attribute. This is an expected false positive; the request is validated before MOVEit authentication is attempted.

### Request binding

The Hosts and Records actions reported by static-analysis rule `WS-I013-CSHARP-00001` accept only explicitly declared scalar query parameters such as filter names, Task IDs/names, run IDs, return URLs/labels, and dashboard view names. They do not model-bind database entities, security-role models, MOVEit configuration objects, or persistence objects from arbitrary request properties.

These findings are treated as false positives rather than replacing the existing narrow scalar action contracts with broader model binding.

### Browser-side rendering

- Dynamic text is rendered with `textContent`, DOM node creation, or equivalent typed DOM properties wherever practical.
- Modal-origin feedback stays in the active modal; parent-page feedback uses the page surface. Diagnostic output must not expose credentials or secret-bearing native payloads.
- The Task Notification HTML editor intentionally supports HTML email fragments. Input is parsed into a detached template-context fragment, filtered through an allow-list of supported elements/attributes/styles, and only the sanitized fragment is inserted into the visual editor.
- Native-editor `postMessage` processing requires the per-launch nonce, exact expected MOVEit origin, and exact popup `WindowProxy` before a message is accepted.
- Native-editor session-expiration redirects are constrained to the current MAUI origin.

### Client-visible notification logs

Signed-in UI notices are recorded through an authenticated, antiforgery-protected endpoint in the existing MAUI application log. User and MOVEit environment are taken from the server session. Only the displayed message, severity, origin and page path are submitted; no form values, native definitions, request bodies or URL query strings are collected. Common named credential/token values are redacted in the browser and again on the server; treat logs as sensitive operational data and avoid putting secrets in user-visible messages. Messages are bounded and control characters normalized. Records are marked ClientReported and are not authoritative MOVEit audit events. Delivery uses the existing bounded log channel/retention policy; unavailable services and logging database failures can prevent capture.

## Object and feature boundaries

### Shared Host and Task mutation safety

Optional MAUI editors and Information saves use the shared CRUD services. Authoritative identity/type and revision checks reject stale values before mutation. Full editor revisions include native credential state, while Information edits compare the fields owned by that form. Unowned native fields and unsubmitted secrets are preserved. The default mode checks out for each save; optional **Checkout Objects for Duration of Edit** acquires checkout when an editable Task/Host/Script/Date List editor opens and re-establishes it after saves. Close/logout release it; 30-second heartbeats, five-minute abandonment expiry and the login idle deadline govern shared lease cleanup. Heartbeats do not count as user activity. Cleanup uses an independent bounded cancellation scope.

A checkout/revision conflict blocks saving. **Reload** requires confirmation before discarding the open form. **Override Checkout** requires a live MOVEit administrator session and explicit confirmation; the server enforces the same authority independently of the UI. REST overrides send the captured `{"force":true}` checkout POST; Global Parameters retain their confirmed COM force/release contract. An override retires the old local editor record without releasing another editor's lock and returns a fresh revision under checkout. It never writes the form itself. Auto Save pauses until an explicit manual Save, which keeps normal validation and revision checks; later concurrent edits can still block it. When a save result could not be verified, the current MOVEit copy must be reviewed. An administrator may explicitly recover checkout while retaining the open form, but that action never replays the failed write. In Task, Host, Script and Date List editors, a confirmation superseded by Close, navigation or a newer editor cannot dispatch an override; a late acknowledged acquisition is closed without attaching it to the newer editor. Unsaved form values are not durable drafts.

`Data/ObjectEditLeases` stores an atomic ownership journal and Data-Protection-encrypted cleanup tokens, never passwords. Keep the directory and the machine-protected `Data/DataProtection-Keys` restricted and persistent. Restored records authorize cleanup only, not interactive login or native lock-owner claims. Opening/retrying asks MOVEit for the current checkout result; failed requests are not presented as a third checkout state. Native "not checked out" responses are not lock conflicts. Failed cleanup is retained/logged; unavailable authentication may require signing in again. Retired ownership records are excluded from checkout release/reacquisition. These protections assume the supported single MAUI worker topology.

Traditional Task writes use v1; captured Advanced/Sync and supported Host writes use v0. REST transport does not remove authorization, license, antiforgery, checkout or readback requirements. Accepted but unconfirmed writes are not automatically retried or replayed through COM. Safe stage diagnostics and modal-local recovery messages must not include raw native responses, credential values or secret-bearing JSON/XML. Retained COM/native adapters remain required for operations described in [the consolidated implementation notes](CHANGELOG.md#shared-crud-and-integration-reference).

Host spreadsheet files may contain plaintext replacement credentials. Restrict their access and retention; do not put real credentials in templates, fixtures, changelog entries or logs. Blank spreadsheet update fields preserve established values according to the current import profile; explicit editor clear/replace operations are a separate contract.

Browse authentication/proxy fields use shared server-side composition across Task editors, operational Browse and Host resource pickers. Supported proxy types come from the Host field catalog. Persisted passwords, OAuth private keys and App Access secrets are never copied into Browse requests; null fields let MOVEit resolve saved credentials by Host ID. Only explicit, request-scoped Host picker replacements are forwarded to MOVEit. Deprecated SharePoint User Access remains excluded.

Host diagnostics/listings have a native-aware budget (5-minute minimum, 30-minute maximum) that covers headers and response-body reads. An acquired diagnostic checkout gets an independent cleanup budget, even if its caller cancels; normal editor-write cleanup and borrowed editor leases retain their established behavior. Failed acquisitions never authorize DELETE or force, and failed writes/tests are not automatically replayed.

The shared browser only lists saved Host contents; choosing a file does not download, upload or delete it. Host Editor inspection requires a live MOVEit administrator and antiforgery validation. Borrowing an existing editor checkout for listing does not release/reacquire that checkout or write a definition. Other browse reads retain bounded checkout cleanup. Cancelling or replacing the modal invalidates pending responses before they can affect another caller.

### Version-specific and cloud Hosts

The shared feature registry requires Automation 2026.0 / native engine 18.0 for OneDrive Hosts and S3 IAM. Unknown versions do not enable these capabilities; server-side REST guards protect writes/test/browse independently of hidden controls. Copy To rechecks destination support before creating/overwriting version-specific Host configuration.

SharePoint App Access tests do not require or forward stale certificate fields. Certificate Access retains its active certificate requirements. Google OAuth test/browse excludes HMAC login fields; saved IDs resolve protected app/private-key secrets through null-preservation, without forwarding GET ciphertext. These existing authentication modes do not acquire a new 2026-only version gate. Accepted response-pattern assumptions and unperformed live tests are recorded separately in [Testing](TESTING.md#moveit-2026-host-acceptance).

### MOVEit Settings editor

Enable the preference under **Configuration > Settings > Editors**. The editor requires the MAUI administrator policy, a live MOVEit administrator session and the saved server-side preference. Open/reload and writes are antiforgery-protected; the browser receives projected settings and opaque revisions. Globals saves use short checkouts, fresh revision checks and readback verification. Lockout policy and trusted-IP changes use their separate native contracts; partial or uncertain results require Reload. Runtime Lockouts use their dedicated read/unlock endpoints without a Globals checkout or settings save. Rejected checkouts are not released by an operation that did not acquire them. See [TESTING](TESTING.md#moveit-settings-editor-acceptance) for live acceptance.

### Custom Script editor

Custom Script source is stored as literal text and sent only to the connected MOVEit server through the authenticated v0 Script envelope. The editor does not execute imported files or templates. File import accepts `.txt`/`.ps1` text up to 2 MiB; server-side limits also apply to posted Source. Source is never included in MAUI audit messages or save exceptions. Built-in Script writes are excluded by using only the custom Script endpoint, and license/permission checks are enforced server-side. Script revisions, checkout leases, abandoned-session recovery and explicit administrator override use the common editor machinery. Native XML export and grid saved-history restore continue to require the COM support layer; MAUI editor Undo Last Change reverses one local edit without a server restore.

Built-in Script viewing honors the Script editor preference. The MAUI viewer resolves built-in status server-side, exposes read-only Properties/Tasks, including MAUI properties and metadata, performs no custom Source read or checkout, and remains available without a Custom Scripts license. The Script editor Save endpoint independently rejects built-in IDs; client read-only flags do not grant write permission.

### Date List editor

Date List edits use the shared object checkout, revision, edit-duration lease and admin recovery checks. Date imports are parsed as plain text and validated on the server before native writes. Existing configuration fields outside the Date List form are preserved from a fresh server copy. Ambiguous creation/update outcomes block automatic resubmission.

### Groups and Permissions

Inline Resource Group permission updates require a live MOVEit administrator and anti-forgery token. The shared permissions service verifies each ACL's Resource Group, User Group and opaque loaded revision against live inventory, validates the exact editable permission set and enforces the Task permission dependency before PUT. All requested ACLs are checked before writes begin, with later assignments rechecked before their individual PUTs. An uncertain acknowledgement is reconciled once and never automatically replayed; partial/conflicting outcomes remain visible for inspection. These checks do not invent a native batch transaction or server-side conditional PUT contract.

Group dataset saves require the captured successful v0 acknowledgement (status success, numeric error code zero). Unknown acknowledgements and timeouts after PUT block blind replay; no pre-save dataset is restored after the commit was attempted. Permissions snapshots reject malformed/reused visible ACL IDs before exposing editable rows.

### Task CSV/XLSX import

- Task spreadsheet import is implemented independently of native `.maui` portability. CSV/XLSX input is parsed server-side and is never treated as executable content.
- Analyze stores the validated Task plans in short-lived server memory and returns only an opaque analysis ID plus normalized preview data. The browser cannot submit arbitrary Task JSON through the row-import endpoint.
- Each row import is bound to the current MOVEit environment and MAUI username, rechecks exact-name state, and applies the shared MOVEit Task-license creation lock/capacity check before creating a Task.
- Existing Traditional Task updates capture MAUI Undo before the MOVEit PUT. Advanced/COS Tasks are not overwritten by this importer.
- Spreadsheet PGP references are resolved only against visible, non-expired PGP keys and are blocked when PGP is not licensed. Notification templates are resolved from the current MAUI environment.
- Spreadsheet Host construction consumes the same verified Traditional Task Host profile catalog used by Quick Transfer. Root-based S3/Azure Blob/SharePoint/Google Storage locations accept only a root returned by the shared MOVEit Browse service when an explicit root is requested; SharePoint library GUIDs come from MOVEit and cannot be supplied by the browser/file. Paths beginning with `/` use the Host's configured default root without exposing or duplicating protected Host configuration in the spreadsheet. Unsupported Browse/authentication shapes are rejected during Analyze rather than approximated.
- File uploads use the existing Task import request-size ceiling. Only `.csv` and `.xlsx` are accepted by the spreadsheet endpoint; XLSX processing reads the first worksheet.

### Native Task/Host portability

- MAUI delegates Task/Host configuration encryption, passphrase handling, dependency discovery, decode, and commit semantics to MOVEit Automation's native configuration portability endpoints.
- Export passphrases are request-scoped only; MAUI does not persist them in the metadata database or `.maui` package.
- Native MOVEit export XML can contain encrypted credentials/private-key material and is treated as sensitive. It is not written to application logs.
- MOVEit's successful `/api/v0/config/import` response can contain broad Settings/configuration data; MAUI intentionally discards that successful response body server-side and never returns it to the browser.
- Host export uses a uniquely named temporary disabled carrier Task only because MOVEit does not support direct Host export. The carrier consumes one temporary MOVEit Task license slot, is deleted in cleanup, and is never stored in the package. Host import never creates a carrier Task.
- Current Task/Host portability packages preserve MAUI configured custom metadata fields and Tags for native objects represented in the package. Folder placement, audit timestamps, and Undo/Recycle history are intentionally excluded.

### Quick Transfer

- Quick Transfer requires an authenticated MAUI session. Its POST requests (`Browse`, `Prepare`, `Transfer`, and `Cancel`) use the normal ASP.NET Core antiforgery boundary; reference/status reads remain authenticated server-side operations.
- Posted Host IDs are re-resolved against the current MOVEit Host inventory and must match a verified Traditional Task Source/Destination profile. MAUI does not trust a browser-supplied Host type or capability claim.
- Relative parent traversal (`..`) is rejected in Source and Destination paths. Object-storage roots are validated through the selected Host profile rather than being copied into arbitrary Task properties.
- PGP processing is guarded by the connected MOVEit license. Encryption re-resolves the selected PGP key from the live key inventory and verifies that MOVEit preserved the selected recipient key after the Task update before the Task can be started.
- Quick Transfer acquires the same Task-creation capacity guard used by other create workflows and requires one available Traditional Task slot. Incomplete temporary Tasks are cleaned up on preparation failure where possible.
- Starting the generated MOVEit Task is treated as a non-cancellable native mutation boundary. If the post-submit outcome is ambiguous, MAUI records the attempt, retains the Task, and refuses to submit a second start automatically; operators are directed to verify MOVEit running/history state before retrying.
- Live status and cancellation use the same conservative run-correlation rules. A known non-empty `NominalStart` is never replaced by a different non-empty identity; missing identities can use only one bounded start-time candidate. This prevents status/cancel from attaching to an unrelated concurrent or later run of a retained Task.
- Quick Transfer transfer tokens/state are stored only in the active MAUI process and are scoped through the authenticated MOVEit environment/user cache key. They are not durable recovery credentials. After an IIS/application recycle, use normal MOVEit/MAUI Task Run records to determine the authoritative outcome; do not assume an interrupted MAUI monitor means MOVEit stopped the transfer.

Cleanup and optional success-confirmation intent are persisted separately in `Data/quick-transfer-cleanup.json`, without MOVEit passwords/access tokens. Pending confirmations include recipient addresses. Protect this file with the installation's Data ACLs; recovery after restart requires appropriate unattended credentials for the original server. Confirmation is an at-most-once attempt, not a guarantee of delivery.

### Task JSON Editor

This experimental feature defaults off and checks the saved setting, live session and per-Task Edit authorization at its endpoints. Writes require antiforgery protection and opaque current revisions; the browser sends scalar-path changes, never a full Task replacement. Native identities/references, Resource Groups, protected credentials/parameters and managed fields are not editable. Credential values are masked before reaching the browser. JSON content is rendered as text; no JSON is evaluated as code, stored in browser drafts or included in server diagnostic logs. Existing checkout ownership, explicit administrator override and persisted-value verification apply. Bulk saves are independent operations; validation is not a transaction and a concurrent change can still stop a later Task save.

The JSON Editor requires the corruption-warning Proceed confirmation before its Save sequence. Confirmation does not grant permissions or bypass revision/checkout checks. Each affected Task captures its ordinary per-user undo snapshot before the update; snapshot failure blocks that Task's write. A later Task's failure does not roll back earlier successful saves.

### Experimental environment copy

Copy Between Environments defaults off. Its modal/menu and server endpoints require both MOVEit administrator status and MAUI Admin authorization; Data Access credentials do not grant callers additional MAUI access. Both environment accounts are required and must authenticate as MOVEit administrators. Connections use isolated background sessions and do not replace or clear the browser login.

Plans and protected native/key payloads remain server-side. Comparisons mask structured secrets, including secure Task Parameter values; Script source is displayed as code and is never interpreted. Reports contain identities, mappings and outcomes, not configuration/private-key payloads. Optional recovery XML is protected using the application's Data Protection keys, gated separately and downloadable only through the initiating dual administrator's operation. Recovery download is deliberate and is not automatic rollback. Retention defaults to 30 days; abandoned in-memory plans expire after 30 minutes or are discarded when closed. Interrupted operations are not automatically replayed, and unconfirmed checkout ownership requires inspection rather than a forced takeover. Live progress snapshots expose only phase/object identities after the report-owner check; write-ahead IDs and outcomes retain their durable journal. Status retries are read-only and stop on denied/missing access. Worker/finalization cleanup clears retained protected plan data even if journal I/O fails. Expired reports cannot be downloaded; expired journal/recovery files are pruned periodically while MAUI runs and at application startup. Copy is not a transaction across objects or environments: completed writes remain after Stop or a later failure. New Resource Groups copy names and membership placement, not source User Group permission grants.

### MOVEit connection feedback

The grid connection banner is diagnostic feedback, not an authorization or checkout decision. Transport evidence is collected only for the explicit MOVEit-backed grid endpoints; editor writes and local-only grids retain their existing behavior. Request observations do not contain credentials or response bodies. Browser state is scoped by a digest of the login/connection/application context and does not expose the server-side session identifier. No monitoring probes or automatic write retries are added.

## Deployment and scanning

### Restricted IIS application-pool permissions

Grant the MAUI pool identity only Read permission on application binaries, views, templates and `wwwroot`, Modify on the **existing** `appsettings.json`, and Modify on `Data` for SQLite, backups, Quick Transfer state, `ConfigurationRecovery`, `ObjectEditLeases` and Data Protection keys. Avoid Modify/Full Control on the application root.

appsettings.json is excluded from build and publish output. New installations require an administrator-created `{}` file before startup; existing installations retain their configured file. Deployment must **skip** an existing configured `appsettings.json` and preserve the entire `Data` directory. Both files and recovery snapshots may contain credentials; keep `Data` outside `wwwroot`, restrict NTFS access, and exclude runtime data from public static-file mapping. Configuration is updated in-place to retain the individual file ACL; a previous verified copy is written to `Data\ConfigurationRecovery\appsettings.lastgood.json` before every change. A power loss during the direct write may require an administrator to recover that copy.

### Dependency scanning

The production and test projects should be restored from their declared NuGet package versions before security scanning. The test project uses `coverlet.collector` 6.0.4 to avoid the stale transitive dependencies present in the older 6.0.2 collector package.

### Static-analysis scan scope

Run SAST against the source tree once. Generated publish/output folders such as `AriDotnetPublish/**`, `bin/**`, and `obj/**` should be excluded from duplicate SAST source analysis when the scanner already analyzes the source files that produced them.

Dependency/SCA scanning should still inspect the resolved dependency graph and/or publish output as appropriate. Excluding generated source copies from SAST is intended only to prevent duplicate findings; it is not a recommendation to omit dependency scanning.
