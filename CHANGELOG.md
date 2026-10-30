# MAUI Changelog

**Current candidate:** v0.18.511-RC3, build 20261029 — **Release Candidate 3, not the final release**. Bugs, regressions and incomplete behavior may remain; validate the candidate against the target MOVEit environment.

## RC3 release overview

RC3 builds on the RC2 Task/Host/resource editors, permissions, shared Browse and Copy To baseline. Late RC2 added MOVEit Settings and Lockouts; RC3 organizes those controls and refines the existing workflows. Current application identity is **0.18.511-RC3, build 20261029**, with database/configuration versions **1 / 1**.

- Editor preferences are in **Configuration > Settings > Editors**; Resource Group folder creation is in **General > Folders**. Task JSON Editor and Copy Between Environments are under Experimental and off by default.
- MOVEit 2026.0 / native engine 18.0 adds version-gated OneDrive Hosts and S3 AWS Config File/EC2 IMDS authentication. Shared version checks cover controls, REST operations and Copy To destinations. SharePoint App Access and Google OAuth use the supplied Test Host/Browse contracts.
- Mixed Automation 2025.x/2026.x environments require COM API 18.x or a later compatible client on the MAUI host. Backward compatibility with 2025.x is described as observed so far, not guaranteed; validate retained COM workflows for each environment.
- Build/publish validation checks the DLL, release template and upgrade manifest as one package. Preserve live appsettings.json/Data, replace matching release files together and verify the deployed package to catch mixed-file IIS startup failures.
- Current workflows include MAUI MOVEit Settings/Lockouts, consistent editor action feedback, on-demand Task filename search, in-process IIS hosting and the configurable Keep Alive worker. Keep Alive cannot start a stopped application; IIS startup/preload remains necessary.
- Checkout messages use MOVEit's response. Administrator overrides are confirmed, retained edits require manual Save, and failed requests do not create a separate user-facing lock state. The current release manifest does not force an additional upgrade restart; startup-sensitive configuration still can.
- The candidate remains pre-release. Historical CI records apply only to their recorded commits; the current build/test status and remaining live MOVEit acceptance are recorded in [TESTING](TESTING.md#current-validation).

**Read by topic:** [RC3 history](#rc3-version-history) · [Shared implementation](#shared-crud-and-integration-reference) · [RC2 overview](#rc2-release-overview) · [RC2 history](#rc2-version-history) · [RC1 history](#rc1-development-history) · [Validation history](#validation-history).

## RC3 version history

### 0.18.511 RC3 — Build 20261029

- Documentation-only follow-up: reorganize README around current RC3 requirements, capabilities, editors/experimental features, permissions, setup, operations and limitations. Consolidate detailed guidance in the existing Installation, Upgrade, Security and Testing guides; preserve compatibility notes, license/disclaimers, historical release records and navigation anchors. Document current JSON warning/undo/reset behavior and clarify the actual Settings locations and keep release history out of README. No application/configuration/version changes or rebuild; previous CI records continue to apply only to their recorded application/test commits.

- Require the explicit Task JSON corruption warning before each Save, with Cancel and the shared red/white destructive Proceed button. Cancel retains staged edits without starting a save. Reject stale confirmation responses after closing/reopening the editor; revalidate current Task revisions after Proceed.
- Confirm the existing per-Task undo capture remains before each Task update. A failed undo snapshot blocks that Task's write. Add controller regressions for every affected Task and snapshot failures, plus browser checks for the exact warning, Cancel and Proceed behavior.
- [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40965532186) passed for application/test commit `b77f056ca59e7dbd0fb804a606c8ebf448b1d591`: zero build warnings/errors, all 2,591 xUnit tests (zero skipped) and all Node/Edge suites, including 131 Task JSON browser checks. The result-recording follow-up changes documentation only. No live MOVEit testing is claimed. Database/configuration versions remain 1; RestartRequired remains false. The Task JSON feature and these refinements are now on master.

### 0.18.510 RC3 — Build 20261029

- Add a Task JSON save progress bar alongside the footer status, matching the Copy To native progress control. Show initial activity, then progress per processed Task; keep failures in the existing results and hide the bar after completion or Close. Do not imply that failed Tasks were saved.
- Start the Find field blank. Retain explicit local Apply Changes, Review Changes and shared save/checkout behavior. Extend browser coverage for the blank field, an in-flight multi-Task save, progress updates and completion cleanup. Local checks pass; [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40945541849) passed for application/test commit `e258b62019132fc356342e0290ac74d933122221`: zero build warnings/errors, all 2,589 xUnit tests and all Node/Edge suites, including 111 Task JSON browser checks. The result-recording follow-up changes documentation only. No live MOVEit testing is claimed. Database/configuration versions remain 1; RestartRequired remains false. Work remains on feature/task-json-editor.

### 0.18.509 RC3 — Build 20261029

- Correct Task JSON Editor tree commands to use existing MAUI secondary-button styles. Use the normal form font and vertically centered padding for single-line scalar values; retain editable multiline text without changing its contents. Keep code formatting for the JSON context and paths.
- Use the same standard MAUI table, header, scroll container, Rows selector, page arrows, page indicator and range summary for Find & Replace and Review Changes. Reuse the Tasks grid's row-size choices and current preference; keep row-size changes local to the modal. Remove duplicate empty-result counters and hide empty pagers.
- Rename Apply Replacements to Apply Changes and keep applied rows on Find & Replace. Mark them Applied and retain selection across pages. Review Changes is disabled/dimmed until staged changes exist; only explicit Review Changes navigation validates them, and Save retains the shared permission/checkout/revision/readback workflow.
- Expand rendered regression coverage for the requested control styles, single/multiline values, both result tables, configured page sizes, more than 50 matches, cross-page selection, local-only Apply and empty states. Local model/syntax checks pass; [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40943102914) passed for application/test commit `ff3f6be74518dbb8bb9e2e400827a3bf08be3d19`: zero build warnings/errors, all 2,589 xUnit tests and all Node/Edge suites, including 103 Task JSON browser checks. This result-recording follow-up changes documentation only. No live MOVEit testing is claimed. Database/configuration versions remain 1; RestartRequired remains false. Work remains on feature/task-json-editor; master is unchanged.

### 0.18.508 RC3 — Build 20261029

- Add the opt-in Task JSON Editor under Configuration > Settings > Experimental, off by default. JSON Editor appears in Tasks Bulk Actions independently of the normal Task editor preference; existing Edit permissions control availability and every endpoint rechecks access.
- Add the MAUI modal with lazy Task trees, typed scalar editing, exact element-name/path search, wildcard paths, optional current-value filters, selectable before/after replacement previews and review/save results. Edit Values and Find & Replace use Review; Review Changes uses Save. Apply operations are local only; there is no Auto Save or browser draft persistence.
- Submit only existing scalar-path changes through the shared Task CRUD coordinator. Keep native identities, references, Task type, Resource Groups, authentication selectors, Sync direction/delete/comparison policy, runtime fields, null/unsafe numeric values and credentials read-only. Mask secure parameters and credentials before returning definitions; never send masked placeholders back as edits. Preserve untouched/version-specific fields and validate types, integer flags, Host-step profiles, names and Task revisions before writes.
- Use existing optional edit-duration leases, independent per-Task heartbeats, administrator checkout override, native Undo capture, metadata audit and cache invalidation. Recheck revisions under checkout and verify persisted edited values using shared rules for native flag/default/newline normalization. Refresh saved JSON so displayed values and types match MOVEit. Preserve authoritative overnight Schedule pairs and unknown XML attributes through the common native Schedule boundary. Retain unsuccessful edits, highlight failed Tasks and require reload/recovery after an unconfirmed save; never replay a PUT automatically or claim cross-Task atomicity.
- Bound one session to 100 Tasks / 16 MiB of definitions, 1 MiB and 20,000 nodes per Task, 2,000 changed values per Task and 20,000 search matches. Lazy tree expansion and paged result tables limit rendered rows. Errors remain in the modal footer and MAUI log; no native JSON, credentials or edited values enter server diagnostic logs.
- Add backend permission, masking, type/path, concurrency, transport selection, readback and native Schedule tests; client model/workflow and rendered modal checks cover review-only staging, whole-batch validation, partial saves, recovery, cancellation and light/dark responsive geometry. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40933138335) passed for application/test commit `5a2a8dd4e606a93a8bffb44f9725961c23c14b8b`: zero build warnings/errors, all 2,589 xUnit tests (zero skipped), 14 release-package checks, publishing and all Node/Edge suites. Task JSON rendered workflows passed 56 checks; Host layouts passed 10,243 checks and Sync/Traditional layouts passed 3,103. Both Task JSON review themes were visually inspected. The result-recording follow-up changes documentation only. Work remains on feature/task-json-editor; master is unchanged. Database/configuration versions remain 1; RestartRequired remains false. No live MOVEit or deployed IIS testing is claimed.

### 0.18.507 RC3 — Build 20261028

- In the shared Test Host modal, show Cancel while a test batch is running and Close when it finishes, including failure, incomplete and cancelled results. Run Tests Again restores Cancel during testing; retain the existing safe cancellation and checkout cleanup behavior.
- Validate production button-state transitions and the existing modal smoke suite locally; JavaScript syntax and release identity checks pass. This label-only change does not require a full build. Database/configuration versions remain 1; RestartRequired remains false. No live MOVEit testing is claimed.

### 0.18.506 RC3 — Build 20261028

- Render shared modal footer feedback as plain severity text: red errors, normal theme text for information, and dark yellow warnings (theme-appropriate gold in dark mode). Remove banner classes/backgrounds/borders/padding from footer notices while preserving body alerts and existing registered editor error handlers.
- Limit shared footer notices to two lines without scrollbars and preserve fixed editor-footer geometry. Collapse/shorten generic long footer text; Test Host and Task Preflight provide concise operation summaries. Log the original diagnostic through the existing notification path before summarizing, once per notice; preserve detailed diagnostic rows and existing logger redaction/size/availability behavior.
- Expand production callback checks for concise summaries, full diagnostic logging, severity and error precedence. Add actual Test Host/Preflight footer rendering checks across both themes and four window widths for colors, transparent backgrounds, two-line/no-scroll behavior and stable controls. Local modal/notification/integration checks passed. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40767166816) passed for application/test commit `482f90e6c44cce86a9a7b3f9f1584f6798f22fb1`: zero build warnings/errors, all 2,543 xUnit tests (zero skipped), 14 release-package checks, publishing and all Node/Edge suites. Host rendering passed 10,243 checks, including 576 added diagnostic-footer assertions across both themes and four widths; Sync/Traditional rendering passed 3,103 checks and Audit comparison workflows passed 58 checks. The final result-recording follow-up changes documentation only. Database/configuration versions remain 1; RestartRequired remains false. No live MOVEit or deployed IIS testing is claimed.

### 0.18.505 RC3 — Build 20261028

- Add View Changes to Audit row menus when the native HasAuditDiff flag is true/1 and the connected MOVEit version supports it. Use the shared release/engine registry (2026.0 / 18.0 or later); old/unknown servers retain the existing Audit view. Read comparison data only after selection, through GET /api/v0/reports/audit/diff/{LogID}, with the current user's REST session and native authorization. Existing grid batching/refreshes make no additional comparison requests.
- Add a read-only Configuration Changes modal using shared MAUI headers, modal geometry, footer feedback, theme colors and Close controls. Show merged before/after line numbers, red removals, green additions and unchanged text, with counts and visible plus/minus indicators. Keep native XML/protected values as text; do not execute markup, decrypt values, persist snapshots, acquire checkout or offer writes.
- Normalize XML indentation/attribute ordering for readable comparisons while preserving child order, mixed content and leaf whitespace. Prohibit external XML entities/DTDs; fall back to original text on both sides when XML formatting is unavailable. Bound snapshot size, nesting and comparison work; group large changed sections rather than allocate an unbounded diff table. Render large results in cancellable chunks and clear them on Close. A closed/reopened modal ignores stale requests/results; native permission, expired-history, timeout and network errors stay in the footer and use shared notification logging.
- Add the supplied native capture, parser/REST/controller/comparison tests and production modal/menu browser coverage, including older versions, no extra grid calls, no checkout/write, escaping, errors, abort/reopen, large Log IDs and responsive light/dark layouts. Local integration/syntax checks passed. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40761745247) passed for application/test commit `2bd45f5c65ceab0cb63d2fe314fe8d63cac7bdd4`: zero build warnings/errors, all 2,543 xUnit tests (zero skipped), 14 release-package checks, publishing and all Node/Edge suites. The new Audit modal/menu suite passed 58 checks; Host layouts passed 9,667 checks and Sync/Traditional layouts passed 3,103 checks. Rendered Audit screenshots were reviewed in both themes. The final result-recording follow-up changes documentation only. Database/configuration versions remain 1; RestartRequired remains false. No live MOVEit or deployed IIS testing is claimed.

### 0.18.504 RC3 — Build 20261028

- Normalize `DOMAIN/username` to `DOMAIN\username` in the login field and server-side authentication boundary. Preserve supplied username case for MOVEit and display; do not alter passwords, local usernames or UPN account forms.
- Share invariant, case-insensitive MAUI account keys and normalize Security settings before duplicate validation. Recognize existing slash-key accounts on login/role lookup without a schema migration or primary-key rewrite, preserving assigned roles and creation history. If both forms already exist, prefer the backslash account and do not combine/elevate roles or delete records on login. Administrator Security saves can explicitly remove an old duplicate; preserve concurrent enrollments and the last-Admin guard.
- Add SQLite-backed account/authorization regressions, production password-grant/controller checks and browser form coverage. Local login checks passed. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40752827458) passed for application/test commit `c594fdcd93ee35568ea1f4b9e6c3907f6f093e6e`: zero build warnings/errors, all 2,514 xUnit tests (zero skipped), 14 release-package checks, publishing and all Node/Edge checks, including 9,667 Host layout checks and 3,103 Sync/Traditional checks. The final result-recording follow-up changes documentation only. Database/configuration versions remain 1; RestartRequired remains false. No live MOVEit or deployed IIS testing is claimed.

### 0.18.503 RC3 — Build 20261028

- Reduce Task/Host spreadsheet import viewports to five data rows plus the header, accounting for native scrollbar height and later modal visibility. Preserve configured import batch/page limits, pagination, outcomes and completed Close labels.
- Keep every shared toast severity in the active modal while it is open, including page-origin updates. Label page notices separately and preserve editor save errors; hide old page toasts on modal opening. Use modal footers for fallback feedback where available instead of inserting header/body rows.
- Show Test Host and Task Preflight failures/warnings in the footer as well as diagnostic rows. Log these messages and retain the existing native-aware operation budgets and independent checkout cleanup. Reopen/retry clears stale feedback; transport failures restore diagnostic controls.
- Capture signed-in toast errors, warnings and info once through an authenticated, antiforgery-protected, bounded endpoint and the existing MAUI log channel. Derive identity/environment on the server, preserve severity including Info, redact common credential/token fields in both layers and omit form values/native payloads/URL queries. Mark records ClientReported, not native audit events. Logging is best effort; outages never block UI actions or generate recursive toasts.
- Expand modal/logging/viewport regression coverage and review the six RC3 Markdown files. Keep release history in this changelog, remove duplicated validation history from Testing while preserving anchors, and add operator acceptance checks. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40745536577) passed for application/test commit `69448c32c24ec45a2a6264c3e508aaea4fc3ba74`: zero build warnings/errors, all 2,495 xUnit tests (zero skipped), 14 release-package checks, publishing and all Node/Edge checks. Host rendering passed 9,667 checks, including actual five-row viewport sizing and fixed footer geometry in both themes; the Task layout suite also passed. No live MOVEit or deployed IIS testing is claimed. The final result-recording follow-up changes documentation only. Database/configuration versions remain 1; RestartRequired remains false.

### 0.18.502 RC3 — Build 20261028

- Clean up the downloadable Host import workbook: consistent MAUI blue headers, readable column widths/row heights, text-safe certificate identifiers, filter tables and frozen header/name/type columns. Preserve the Host Import / Examples / Instructions tab order, exact 28 import headers, existing one-row sample and Y/N validation. Keep the CSV template and application import logic unchanged.
- Expand Examples to 13 supported profiles/variants, including explicit/implicit FTPS and password/key-based SFTP. Use illustrative endpoints/credentials and certificate placeholders, certificate validation by default and explicit implicit-FTPS port 990.
- Replace duplicate security guidance and old development-phase notes with an organized column reference, spreadsheet-specific authentication scope and 16 validation scenarios with expected outcomes. Keep legitimate SharePoint certificate-profile fields; distinguish that import profile from broader Host Editor support.
- Review rendered workbook ranges and verify the exported first-sheet populated values exactly against the original. Add an xUnit check that reads all Examples rows with the production file reader/profile; retain existing CSV/XLSX header and first-sheet-only assertions. Local Host import contract/UI checks passed. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40709153626) passed for application/test commit `3daea2374b8db588d3219811c5b6b6e26128d980`: zero build warnings/errors, all 2,482 xUnit tests (zero skipped), 14 release-package checks, publishing and all Node/Edge checks, including 9,625 Host layout checks and the Task layout suite. The new shipped-workbook test successfully parses all 13 Examples rows; existing header/sample/first-sheet-only checks also pass. No live Host creation or deployed IIS validation is claimed. The result-recording follow-up changes documentation only. Database/configuration versions remain 1; RestartRequired remains false.

### 0.18.501 RC3 — Build 20261028

- Add Load Template to the Script Templates card using the existing card-heading/row-heading branding. Accept .txt/.ps1 files up to 2 MiB; prefill Template Name from the filename without its extension and PowerShell Source from the file contents.
- Reuse the existing environment-scoped Script Template modal/service for Add Script Template, with Add Template and Update Template labels for Settings creation/editing. Keep the Script editor's Save as Template workflow, duplicate-name checks, permissions and in-modal save errors. Loaded data is ready to add without a dummy edit; unchanged edits and empty sources remain disabled.
- Share UTF-8/UTF-16 BOM decoding with Script Import. Keep source as plain text, reject binary/invalid files and report file-read failures within the Settings card. Loading and closing do not save or execute a Script.
- Extend the existing Edge Script editor regression with production Settings header branding, prefilled Add/Update, rename, UTF-16, invalid files, cancellation, empty sources and permissions. Run existing configuration/template checks locally. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40699969880) passed for application/test commit `a7a7f7416c128f71439eb04de0a3260e79346b8c`: zero build warnings/errors, all 2,481 xUnit tests (zero skipped), 14 release-package checks, publishing and all Node/Edge checks. The expanded Script editor suite passed 254 checks, including both production template card headers and file loading; Host layouts passed 9,625 checks and the Task layout suite also passed. No deployed IIS or live MOVEit validation is claimed. The result-recording follow-up changes documentation only. Database/configuration versions remain 1; RestartRequired remains false.

### 0.18.500 RC3 — Build 20261028

- Restore Notification Templates Settings card branding. The new Load Template header mistakenly used the section-heading class, bypassing the card header padding, divider and typography. Use the existing configuration-card-heading row-heading pattern to match Script Templates while keeping Load Template on the right. No new CSS or template behavior changes.
- Run existing configuration and notification-template checks locally. This markup-only correction does not claim a new full .NET build or deployed IIS visual validation; the last full build remains the successful 0.18.499 CI record below. Database/configuration versions remain 1; RestartRequired remains false.

### 0.18.499 RC3 — Build 20261028

- Add Load Template to Configuration > Settings > General > Notification Templates. Accept .txt/.htm/.html files up to 2 MB; infer Text/HTML from contents, default the name from the filename, and allow reviewing Format, Subject/macros and Message Body before adding.
- Match Script Templates with Edit / Rename and Delete. Reuse one Add/Edit modal with Close and Add Template/Update Template, Task Notification message styles, macro choices, sanitized HTML visual/source editing and inline errors. Save templates independently of Save Configuration, scoped to the current environment and existing admin policy.
- Extend shared template saves to update the name and contents atomically; preserve legacy Task Notifications overwrite requests that omit Name and enforce duplicate-name checks. Existing Task notifications remain unchanged.
- Add service and browser regressions. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40696335012) passed for application/test commit `6fa2a1e3b7f9f351a70f7a797846e517b99e6fea`: zero build warnings/errors, all 2,481 xUnit tests (zero skipped), 14 release-package checks, publishing and all Node/Edge checks, including the new template-management regression, 9,625 Host layout checks and the Task layout suite. No deployed IIS or live MOVEit validation is claimed. The result-recording follow-up changes documentation only. Database/configuration versions remain 1; RestartRequired remains false.

### 0.18.498 RC3 — Build 20261027

- Set appsettings.json CopyToOutputDirectory and CopyToPublishDirectory to Never. Live settings stay installation-owned and are not part of fresh build/publish output. New deployments must create an existing `{}` file before startup; update operator guidance and assert settings exclusion in Windows publish validation. Clean staging output prevents old settings files being included by external packaging.
- Derive Task spreadsheet import Cancel/Close from completion state. Completed imports, including failed/unverified rows, retain Close through paging; new files, option changes, mode switches and reopen reset it appropriately.
- Route all captured Host test/browse REST endpoints through one native-aware timeout policy: a 5-minute minimum, native connection/data/retry allowance and 30-minute maximum, covering headers and body reads. The diagnostic checkout adapter gives release an independent matching budget instead of the previous 15 seconds; a queued Host DELETE no longer inherits the ordinary 60-second request cap. Ordinary write-scoped cleanup and borrowed editor checkouts retain their existing lifecycle. Never release a rejected acquisition, force ownership automatically or replay a diagnostic.
- Return timeout/network failures as Host results for Preflight/Test Host/Host Health rather than unhandled HTTP failures. Host-stage errors remain in the open Preflight results. Defer automatic Task/Host grid refresh during diagnostics without changing Pause preferences; refresh resumes afterwards. Retain genuine release failures as failures with MAUI-local recovery wording.
- Preserve timing learned from sparse-checkout enrichment through cleanup. If MOVEit reports another editor took checkout during a connection/path test, retain the native lock message and retire release intent without DELETE.
- Add regression coverage for native budgets, real transport dispatch, timeout/network results, Preflight outcomes, cancellation, release beyond the old 15-second limit, rejected/transferred ownership and non-replayed release failure. Extend import/modal checks and publish settings-exclusion checks. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40555073956) passed for application/test commit `da6bacaea5fbe30fbde48727e7682469044db019`: zero build warnings/errors, all 2,479 xUnit tests, 14 package checks, publishing and 29 Node/Edge suites passed, including 9,625 Host and 3,103 Task layout checks. Publish verified the live settings file is excluded. The result-recording follow-up changes documentation only. No live MOVEit validation is claimed. Database/configuration versions remain 1; RestartRequired remains false.

### 0.18.497 RC3 — Build 20261026

- Display **ON** for Microsoft OneDrive in the Host editor header, using the existing Host icon styles.
- Add prominent README/installation guidance for mixed Automation 2025.x/2026.x environments: install/register COM API 18.x or a later compatible release on the MAUI server. Earlier COM APIs cannot connect to Automation 2026.x. Describe backward compatibility with 2025.x as observed so far, and require environment-specific validation after upgrading. Repeat the requirement in the COM prerequisites/installation steps.
- Consolidate the RC3 documentation: shorten the README into an overview, retain operator procedures in Installation/Upgrade, place older validation records in the changelog, group MOVEit 2026/cloud acceptance with Host tests and correct stale current-reference wording. Retain only the six established Markdown files.
- Validate JavaScript syntax, the production icon mapping, existing Host editor regressions, matching release identities and unchanged installation-owned settings. This cosmetic/documentation change does not claim a new full .NET build; the last full CI record remains 0.18.496. Database/configuration versions remain 1; RestartRequired remains false.

### 0.18.496 RC3 — Build 20261026

- Implement the supplied SharePoint App Access Test Host request alongside Certificate Access. Validate Realm ID for App Access and Tenant ID/certificate fields for Certificate Access; omit inactive authentication fields. Keep deprecated User Access unsupported. A blank Site uses the default site, consistently with Host saves and Browse.
- Complete Google Storage OAuth Test Host with Project ID, OAuth Client ID and the saved-private-key field. Keep HMAC credentials out of OAuth tests and clear HMAC login fields in both shared Host and Task-editor OAuth Browse requests. Saved Host IDs resolve protected secrets; no GET ciphertext is forwarded.
- Retain the existing S3 Access Keys response parsers for IAM Test Host/Browse, the existing SharePoint certificate-test response parser for App Access, and existing Google HMAC response parsers for OAuth, as requested. These response contracts are assumptions accepted by the user; live S3/Google testing is unavailable. No new minimum-version gate is imposed on these existing SharePoint/Google authentication modes.
- Add authentication-mode, required-field enrichment, error and checkout-release regression coverage, including App Access with and without stale certificate values. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40374046677) passed for application/test commit `935a24c3400a51b557851e37151957d86b945335`: zero build warnings/errors, all 2,463 xUnit tests, 14 package checks, publishing and 29 Node/Edge suites passed, including 9,625 Host and 3,103 Task layout checks. The result-recording follow-up changes documentation only. No live MOVEit testing is claimed. Database/configuration versions remain 1; RestartRequired remains false.

### 0.18.495 RC3 — Build 20261026

- Add one shared minimum-version registry for release-specific MOVEit features. OneDrive Hosts and Amazon S3 IAM authentication require Automation 2026.0+. Accept both release names (2026.0+) and Progress's [documented native engine versions](https://docs.progress.com/bundle/moveit-automation-release-notes-2026/page/Whats-New-in-MOVEit-Automation-2026.html) (18.0+). Prefer the reported engine version over Web Admin; use Web Admin's version only if the engine version is absent. Unrecognized versions do not enable new features. Enforce support in Host creation/editor controls and REST writes/browse/test, and verify Copy To destination compatibility during analysis and execution.
- Add the captured OneDrive REST v0 New Host shell and editable Host profile, including certificate/client-secret authentication, Tenant ID, Username, Client ID, TLS certificate picker, HTTP/HTTPS proxy, limits, timeouts and state-cache controls. Reuse shared Host CRUD, checkout, Undo, metadata and modal controls. Test Host and Browse Host share the supplied `/hosts/onedrive/browse` request contract; use OAuth Client ID inside logininfo and the OneDrive user at the top level. Resolve stored client secrets by hostid; never send GET ciphertext or editor helper fields.
- Add S3 Access Keys, AWS Config File (optional profile name) and EC2 Instance Role (IMDS) authentication. Serialize numeric AuthMethod 0/1/2, map profile name to native DefUsername, omit inapplicable credentials, and restrict alternate providers to Access Keys. Forward authentication mode through both shared Host and Task-editor S3 browse paths and native S3 Test Host. Require explicit replacement credentials when switching back to Access Keys.
- Extend version, payload, transport, credential-preservation and editor-state tests; add OneDrive and S3 IAM rendered fixtures. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40360465956) passed for application/test commit `11bfce370a58dfdd96ad4ca4acabf36c1455ddcb`: zero build warnings/errors, all 2,443 xUnit tests, 14 package checks, publishing and 29 Node/Edge suites passed. The expanded Host matrix covered 86 fixtures / 9,625 layout checks; Sync/Traditional Task checks totaled 3,103. Retain existing proxy-password null-preservation while omitting inactive cloud authentication fields. A reporting-only follow-up counts profiles from fixture types instead of an old fixed value; application code/test assertions remain unchanged. Live MOVEit 2026 validation remains separate. Schema/configuration versions remain 1; RestartRequired remains false.
- Historical browse/test audit (superseded by 0.18.496 for SharePoint/Google): SharePoint Browse already distinguishes App Access from Certificate Access, but its separate Test Host contract is certificate-only. Google Storage Browse already distinguishes HMAC from OAuth; its separate test sends UseOAuth without explicit project/client fields and needs a native OAuth test capture to confirm completeness. FTP/FTPS and MOVEit Transfer browse include client-certificate values; SFTP includes client-key settings. SMTP/POP3 have no Host file-browser implementation. Additional native SharePoint App Access and Google OAuth test captures, plus S3 role-mode test/browse captures for live confirmation, remain useful. OneDrive Task Source/Destination override payloads were not supplied; their editing/Quick Transfer profiles remain unverified and are not enabled by the new Host profile.

### 0.18.494 RC3 — Build 20261026

- Prevent stale release metadata from surviving incremental build/publish output when its destination timestamp is newer: copy `appsettings.template.json`, `appsettings.custom.json` and `upgrade.json` unconditionally. Preserve the separate deployment rules for live `appsettings.json` and installation-owned Data.
- Add a read-only Windows build/publish package check against the actual DLL product version, template version, manifest target and configuration-version contract. Ship the same verifier for optional post-copy validation on IIS; it never launches MAUI or reads/modifies live settings/Data. Reject mixed output even with `publish --no-build`. Keep runtime release validation enabled and make its mismatch error actionable.
- Add real PowerShell rejection/state-preservation checks and seed newer stale build/publish files in CI to reproduce the packaging regression. Document complete-package redeployment, destination exclusions and 500.30 diagnosis. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40346848258) passed for application/test/package commit `78fb8f8f5d770169a670d5eb7f00802ea9e7a011`: zero build warnings/errors; all 14 package-validation cases, 2,408 xUnit tests, publish checks and 29 Node/Edge suites passed (including 8,849 Host and 3,103 Sync/Traditional Task layout checks). Documentation-only result recording leaves the tested code/package scripts unchanged; live IIS/MOVEit acceptance remains separate. Schema/configuration versions remain 1; `RestartRequired` remains false.

### 0.18.493 RC3 — Build 20261025

- Correct two test-project compile errors from the checkout refactor: add the missing `System.Net` import in `ObjectEditLeaseTests`, and replace the stale `Uncertain` property reference in `TraditionalAdvancedConversionTests` with `PendingRequest`. No runtime checkout behavior or persisted journal format changes.
- [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40243163063) passed on master for application/test commit `e1d51bba29b3c184cfca5c03a4e35ab195e08254`: Release solution build with zero warnings/errors, all 2,408 xUnit tests passed (zero failed/skipped), publish validation and all 29 Node/Edge suites passed, including 8,849 Host and 3,103 Sync/Traditional Task layout checks. Live MOVEit/IIS acceptance remains separate. Identity is 0.18.493 RC3, build 20261025. Database schema/configuration versions remain 1; the release restart flag remains false.

### 0.18.492 RC3 — Build 20261024

- Fix a reproduced manual Override Checkout confirmation race: Close, navigation or a newer Open invalidates the old confirmation before any override request. Preserve cleanup of a late successful override response without taking over the newer editor. Extend the shared Task/Host/Script/Date List regression cases.
- Exclude native **not checked out** messages from lock-conflict detection, including the documented 4015 text. An unlocked response must not prompt for an administrator force-checkout. Add native response/classification coverage for all REST checkout routes.
- Correct the pre-save journal failure text to require Reload, matching the blocked editing session. Update all six Markdown guides with current checkout/retry behavior, administrator force rules, retained-journal compatibility, restart policy and validation limits; preserve historical release notes.
- Focused Node checks passed. No .NET build, xUnit run, CI build or live MOVEit validation was performed for this pass. The initial review was performed on `fix/simple-checkout-handling` and was subsequently merged into master. Identity is 0.18.492 RC3, build 20261024. Database schema/configuration versions remain 1 and the restart flag remains false.
- Documentation-only follow-up: reorganize all six guides around current RC3 usage, installation, upgrade operation, security and workflow-specific validation; retain historical release records and legacy anchors. Correct the current Group & Permissions setting label, local COM setup guidance, and Node/browser fixture instructions. Application version/build, configuration, code and license are unchanged; no build is run for this documentation pass.

### 0.18.491 RC3 — Build 20261024

- Use the normal native checkout response when reopening Tasks, Hosts (including Local File System), custom Scripts and Date Lists. A saved MAUI cleanup record no longer creates a separate user-facing lock state or supplies an old username/timestamp. A rejected second open preserves the first editor; a native checkout success replaces the old local editor record without force.
- Keep failed-request cleanup bookkeeping internal. Preserve existing encrypted records, ordinary post-save reacquisition, close/logout/abandon cleanup, revision checks and administrator-only confirmed `force:true` overrides. Never automatically force checkout or replay a failed save.
- Standardize checkout request/override errors and restored-checkout footer messages across entity editors, MOVEit Settings, Resource Groups and Global Parameters. Preserve native lock-owner/time details through Script/Date List save rejection and COM handoff errors.
- Remove the unused local editor-open timestamp, the synthetic confirmed/unconfirmed MAUI lock messages, and duplicate response text. Add regressions for native lock details, retry after request failure, stale editor replacement, retained first-editor ownership and deployed journal compatibility.
- Identity is 0.18.491 RC3, build 20261024. Schema/configuration versions remain 1. The release restart flag remains false.

### 0.18.490 RC3 — Build 20261024

- Reset the release manifest's `RestartRequired` flag to `false` for routine upgrades. Startup-sensitive configuration changes still trigger the existing automatic restart requirement; otherwise upgrade completion offers **Return to MAUI**.
- Identity is 0.18.490 RC3, build 20261024. Database schema/configuration versions remain 1; preserve live settings and Data.

### 0.18.489 RC3 — Build 20261024

- Fix confirmed administrator checkout overrides: send the captured v0 POST body `{"force":true}` through a separate shared REST transport. Task, Host (including ID 0), custom Script and Date List overrides use this explicit path; ordinary editor opening, post-save reacquisition and cleanup remain unforced. Apply the same force input to confirmed MOVEit Settings and Resource Group recovery. Global Parameter recovery retains its existing administrator-only COM force/release path.
- Normalize the native lock message to **This object has been checked out to ...**, preserving the reported owner/time and removing the REST error prefix. Use the same message for HTTP and native-envelope checkout rejections.
- Add wire-level request-body/authorization tests, verify force is not reused for subsequent saves, and exercise opening-conflict recovery through complete Traditional, Advanced and Sync editor loading. Verify Settings reload after recovery and unchanged Resource Group recovery payloads. Never save or retry user changes automatically after override.
- Identity is 0.18.489 RC3, build 20261024. Database schema/configuration versions remain 1; preserve live settings and Data.

### 0.18.488 RC3 — Build 20261024

- Keep checkout recovery inside MAUI. Preserve native lock-owner/time messages from REST success/error envelopes and HTTP errors for Tasks, Hosts, Scripts, Date Lists, MOVEit Settings and Resource Groups. Show recorded MAUI editor identity/open time for local conflicts without claiming it is the current native owner when ownership is uncertain.
- Offer confirmed Override Checkout when an entity editor first encounters a lock, and make the existing administrator recovery controls available after uncertain saves across Task, Host, Script and Date List editors. Retain draft edits, pause Auto Save and require manual review/save; never resubmit the failed mutation automatically. Read-only users cannot override, and the server checks MOVEit administrator authority.
- Add shared confirmed recovery for MOVEit Settings, Resource Groups and Global Parameters through their existing CRUD/checkout contracts. Global Parameters uses the documented COM CheckoutItem force option and release without modifying configuration. Resource Groups retains the captured unchanged-dataset PUT release pattern. Users remain in MAUI and review/reload their form before continuing.
- Stop unattended checkout POSTs when edit-duration ownership is uncertain: a failed acquisition/release does not prove current ownership. Retain recovery state and report pending cleanup without repeating the same warning every sweep. A native lock rejection retires stale local ownership without deleting the newer checkout.
- Extend native-message, administrator authorization, opening-conflict, cancellation/race, dataset recovery and retained-draft regression coverage. Build remains 20261024; database schema/configuration versions remain 1.

### 0.18.487 RC3 — Build 20261024

- Record short-lived REST checkout intent before acquisition using the existing protected cleanup-token format. Preserve ambiguous acquisition/release outcomes for administrator review; never repeat a write or blindly override/release an unverified checkout. A later acknowledged interactive acquisition retires stale local records.
- Persist protected Run As Of checkpoints before temporary Task changes and run dispatch. Reconcile interrupted operations after restart using the original account; wait for confirmed completion, preserve concurrent field/enable changes and restore through shared Task CRUD. Never replay a run. Retain the original Undo snapshot and recovery details when authentication, checkout availability or run identity is uncertain.
- Purge expired Copy To reports and protected recovery files periodically using the live retention setting. Protect active operations and downloads; keep startup interruption reconciliation separate from normal retention.
- Remove unused Script language normalization, unreachable Script-editor metadata-save JavaScript, and obsolete placeholder, Setup completion, body read-only banner and Sync panel CSS. Preserve the separate Script Information metadata API and intentionally retained COM support.
- Retire five pre-consolidation smoke scripts already excluded from CI; current shared CRUD, permissions, IIS/login and Quick Transfer suites supersede their obsolete interface/markup assertions. Correct stale editor descriptions and Undo comments.
- Add focused recovery/retention regression tests. No database schema or configuration version change. Restart after upgrade to activate the new recovery workers; preserve installation-owned Data and its Data Protection keys.

### v0.18.486 RC3 — Settings layout consistency (20261024)

- Move Idle Session Timeout immediately above Task Run History Maximum within General > Application Behavior, preserving its visibility and restart behavior.
- Move General > Folders immediately before System Message. Retain administrator visibility, permissions, the immediate Create Folders action and existing Save Configuration behavior; update current placement guidance.
- Add the missing Grid Defaults card heading and description using the same Settings card markup and styles as Editors. No CSS, configuration-key or runtime-behavior changes. Identity: `0.18.486-RC3`, build `20261024`; schema/configuration versions remain `1`.

### v0.18.485 RC3 — MOVEit Settings labels, email templates and Keep Alive (20261023)

- Rename Configuration's MOVEit settings navigation/modal/messages to **MOVEit Settings** and its editor option to **Use MAUI Settings Editor**. Retain the existing `UseMauiSystemSettingsEditor` key, routing and API contracts so saved editor preferences continue to work.
- Include the three supplied Task success, failure and no-transfer email bodies unchanged under `Templates/Email`; copy them to build/publish output alongside the existing templates and verify their presence during publishing. This does not import or overwrite notification templates stored in a MAUI environment.
- Add **Advanced > Keep Alive Service**, on by default, with a validated 1–20 minute Ping Interval defaulting to 5. The cancellable worker observes live configuration changes and sends a bounded local-only HTTP request to a private, session-free endpoint. Preserve application PathBase and HTTPS certificate validation; disable redirects, proxies and cookies. Log one warning per failure episode and record recovery.
- Keep Alive can reduce idle shutdown while MAUI is running; it cannot start a cold/stopped process or replace IIS startup/preload. Document this limitation and unattended validation. Add transport, PathBase, private-endpoint, shutdown, hot-configuration and interval-validation coverage. Identity: `0.18.485-RC3`, build `20261023`; no database-schema/configuration-version migration.

### v0.18.484 RC3 — Explicit IIS hosting and branch integration (20261023)

- Explicitly select `InProcess` IIS hosting in the project file and verify that the published `web.config` contains `hostingModel="inprocess"` during the existing Windows publish check. MAUI's scheduler remains hosted inside the web application; no CRUD or scheduler implementation changes.
- Document Application Initialization, AlwaysRunning, idle-timeout and preload settings, hosting-mode differences, recycling considerations, and an unattended System Job validation procedure in the installation guide.
- Update `appsettings.custom.json` metadata definitions: Tasks and Hosts use ServiceNow ID, Client Name, Client ID, Internal Contact and External Contact; Keys and Certificates use Company Name and both contacts; Scripts, Date Lists and Global Parameters use Notes. All six retain Tags and multiline Notes; contacts use Email fields. The custom file is not loaded automatically, and upgrades preserve live metadata definitions and stored values.
- Consolidate the recent Task filename-search, toast feedback and search-hint consistency work for master. Update package/current-guide identity to `0.18.484-RC3`, build `20261023`; database schema and configuration version remain `1`.

### v0.18.483 RC3 — Task filename-search validation and recovery (20261023)

- Fix Back/Forward cached-page restoration after a failed filename read. Clear the prior failure state through the existing invalidation path so an active search can load again; restored empty searches still make no filename request. Successful reads stay silent, and failed reads retain the standard page-error toast without render-driven retries or repeated notifications.
- Validate known For/If/Otherwise containers consistently with Source/Destination containers. Reject malformed containers rather than silently returning a partial successful index. Continue accepting omitted/null optional containers and singleton/array step projections, while ignoring unknown step types. Reuse one object-shape check and read each filename property once.
- Return the existing safe JSON error for an upstream cancellation/timeout when the browser request is still active. Preserve genuine request cancellation. No native write, checkout, permission, search-matching, grid-style or initial-load behavior changes.
- Extend existing tests for failed/empty cached-page restoration, query-change and clear/retype recovery, malformed loop/conditional containers, optional null/unknown steps, and timeout versus request cancellation. Package/current-guide identity: `0.18.483-RC3`, build `20261023`; no schema/configuration migration. Work remains on `feature/task-filename-search`; master is unchanged.

### v0.18.482 RC3 — Quiet Task filename search and consistent hints (20261022)

- Remove the filename-search loading spinner, loading text and reserved status-line height. Delete all filename-specific searchbox wrappers, inline-feedback controls and layout CSS, returning to the common grid searchbox. Report a failed filename read once through MAUI's standard page-origin error toast; changing the query or Refresh Now retries. Keep folder/search/count controls aligned, including when errors occur. On-demand loading, debounce, cache, cancellation and matching behavior are unchanged.
- Use lowercase words after **Search** in searchbox hints throughout the application. Correct Tasks and Report-edit views, Reports, Date Lists, Groups and Permissions, and resource/user-group pickers; already-consistent hints retain their wording. This changes hint/accessible-label text only, without changing searches, filters or object labels.
- Update existing regression fixtures for silent reads, the restored 44px control row, alignment, standard error toasts and retry through Refresh Now in both themes. Package/current-guide identity: `0.18.482-RC3`, build `20261022`; no schema/configuration migration. Changes remain on `feature/task-filename-search`; master is unchanged.

### v0.18.481 RC3 — Task search alignment and reliability (20261022)

- Align the Task folder selector, Manage Folders, searchbox and counter on the same control row. Keep filename progress directly beneath the searchbox and retain its reserved space so successful loading/completion does not move the grid. Scope the layout adjustment to filename-enabled Tasks grids; Report editing and other grid layouts retain their existing styling.
- Skip searchable row-text DOM reads and filename lookups when the global search is empty. Keep active-search text, literal masks, OR/NOT, column filters and paging unchanged. Avoid rewriting unchanged filename feedback on repeated renders, including the accessible live region.
- Ignore cancelled/obsolete filename responses before session handling and before building their index, including reads invalidated while JSON is loading. Retain the on-demand bulk read, debounce, five-minute page cache, signed-in-user visibility and explicit Retry behavior; no native writes or checkout changes.
- Extend the rendered grid fixture with the production Task folder-selector and Manage Folders markup. Check alignment before/during/after lookup and after errors in both themes and at desktop/narrow widths; cover empty-search DOM work and obsolete body/session responses. Package/current-guide identity: `0.18.481-RC3`, build `20261022`. Work remains on `feature/task-filename-search`; no master merge or schema/configuration migration.

### v0.18.480 RC3 — On-demand Task filename search (20261022)

- Extend the Tasks grid searchbox with case-insensitive stored Source file masks and Destination filenames, including nested Advanced branches/loops and both Sync folders. Literal text such as `*.txt`, macros, OR alternatives and NOT retain the existing text-search semantics; filenames are not expanded or tested against configured wildcard expressions.
- Load only `ID,steps` through the established bulk REST field-projection pattern after a 300 ms typing pause and after the normal grid is ready. The read uses the signed-in user's native permissions, without checkout, COM, Data Access credentials or changes to Task save payloads. Only Task IDs and filename text reach the browser.
- Keep a compact five-minute cache only in the open Tasks page. Empty searches and normal grid loads/refreshes do not fetch steps; active searches reuse fresh data. Manual Refresh and returning from the MAUI Task editor invalidate it; expired data refreshes on the next active search/render. Nothing is written to browser storage or the database.
- Show a small spinner and **Searching filenames…** directly beneath the searchbox, then hide it on completion. Reserve the line so grid/table geometry remains stable. Failed reads show an inline explanation that results include Task details only, with Retry; malformed/partial indexes are not accepted. Clearing search, invalidation and navigation cancel obsolete reads, and late responses cannot replace newer results. Browser Back/Forward cache restoration reactivates searching.
- Add native transport/projection, authentication/HTTP/PathBase, cache/debounce/cancellation and rendered grid/filter/layout regression coverage. Reports and their editing views retain existing behavior. Work remains isolated on `feature/task-filename-search`; master is unchanged. Package/current-guide identity: `0.18.480-RC3`, build `20261022`; no schema or configuration migration.

- Validation: [Windows CI 39874248768](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39874248768) passed for application/test commit `5cbbd6c368dbfe5d5bde78c29883e7579a25610e`: Release build/publish with zero compiler warnings/errors, **2,288 xUnit tests passed** (none failed/skipped), all registered Node/Edge suites, **105 Task filename-search browser checks**, **8,849 Host layout checks** and **3,088 Task layout checks**. All 18 locally runnable registered Node suites and six-guide repository-link/heading checks also passed. Filename coverage verifies no initial step read, bulk field selection, signed-in-user transport, root/virtual routes, nested/Sync extraction, literal OR/NOT matching, cache/debounce/expiry, cancellation/stale responses, retry and stable progress placement in both themes. Grid checks retain MAUI's existing desktop minimum width. Native responses remain simulated; confirm live MOVEit access and large-inventory search performance before broad use. The following changelog evidence commit changes documentation only.

### v0.18.479 RC3 — Release cleanup and master promotion (20261022)

- Review the MOVEit Settings and Lockouts services, REST contracts, administrator/feature guards, checkout/revision handling, Settings organization and shared editor feedback/export paths. No additional functional change was needed in the reviewed paths; retain the tested save, checkout and download behavior.
- Correct the current Date List testing instructions to use Settings > Editors, remove feature-branch wording from the MOVEit Settings acceptance guide and include the MOVEit Settings off-by-default preference in Installation. Extend the manual acceptance checklist for object-specific action labels, successful export footer messages, nested Export failures and unchanged editor geometry.
- Keep release history and validation evidence in this changelog. Refresh current package/guide identities to `0.18.479-RC3`, build `20261022`, without changing database-schema/configuration versions or existing editor choices. Promote the validated branch into master; retain the feature branch.

- Validation: [Windows CI 39831311357](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39831311357) passed for application/release commit `231d08fa21356a4168155570e45d21de47533bb7`: Release build/publish with zero compiler warnings/errors, **2,268 xUnit tests passed** (none failed/skipped), all registered Node/Edge suites, **170 MOVEit Settings checks**, **232 Script checks**, **96 Date List checks**, **8,849 Host layout checks** and **3,088 Task layout checks**. All 17 locally runnable registered Node suites, release-identity checks and six-guide repository-link/heading checks also passed. Native responses remain simulated in automated tests; live MOVEit acceptance remains separate. The following changelog evidence commit changes documentation only.

### v0.18.478 RC3 — Consistent editor export results (20261022)

- Script and Date List export completion now shows **Exported 1 Script.** / **Exported 1 Date List.** in the editor footer, matching Task and Host exports, rather than replacing completion with the draft's saved/unsaved status. Normal subsequent edits resume draft status.
- Export failures retain supplied server/network detail and use matching object-specific fallback messages. Empty downloads are rejected for Tasks and Hosts as they already are for Scripts and Date Lists; no success notice or download is produced for an empty result.
- Move Task/Host Export dialog errors into its footer with shared MAUI typography, wrapping and accessible alert semantics. Register that existing error surface with shared modal feedback; errors stay visible in the active Export dialog while successful completion returns to the editor footer.
- Extend real-browser export checks for Scripts, Date Lists, Hosts and all three Task types to cover success, HTTP/non-JSON/empty/network failures, retry, retained editor values and footer geometry. Creation/save payloads, checkout handling and export formats are unchanged. Package/current-guide identity: `0.18.478-RC3`, build `20261022`.
- Validation: [Windows CI 39826046149](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39826046149) passed for application/test commit `73be203c64a64ff821a5651990eeeac6184b940e`: Release build/publish with zero compiler warnings/errors, **2,268 xUnit tests passed** (none failed/skipped), all registered Node/Edge suites, **232 Script checks**, **96 Date List checks**, **8,849 Host layout checks** and **3,088 Task layout checks**. Export coverage verifies matching completion text, server/non-JSON/malformed-JSON/empty/network failures, retry, preserved drafts and footer geometry. Native responses remain simulated in automated tests; live acceptance remains required.

### v0.18.477 RC3 — Editor action labels (20261022)

- Rename the shared Task editor Actions menu's **Export** entry to **Export Task** for Traditional, Advanced and Synchronization Tasks. Audited Host, Script, Date List and step/schedule/Next Action menus: object-specific labels already identify their target. Established descriptive commands such as Undo Last Change, Run Now and Run As Of retain their wording.
- Increment package/current-guide identities to `0.18.477-RC3`, build `20261022`. This is a label-only editor change; action handlers, permissions, payloads and styling are unchanged.
- Validation: [Windows CI 39821824770](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39821824770) passed for application/test commit `b37cd7542755a4645d648b0278800764b278c0d9`: Release build/publish with zero compiler warnings/errors, **2,268 xUnit tests passed** (none failed/skipped), all registered Node/Edge suites, **8,842 Host layout checks** and **2,788 Task layout checks**. Focused local action, modal and release-metadata checks passed. Live MOVEit acceptance remains separate.

### v0.18.476 RC3 — Editor action feedback layout (20261022)

- Fix the shared modal feedback helper inserting an extra header banner after Host/Task Export and Clone completion notices. Editors register their existing footer status field; success and warning notices use that field, and unresolved footer errors retain priority. No extra row is added to fixed editor layouts.
- Apply the same footer policy to Hosts, Traditional/Advanced/Sync Tasks, Scripts, Date Lists and MOVEit Settings. Export-child failures remain inside the child modal; returning to the editor routes completion to its footer. Parent-grid refresh messages retain their explicit page-level behavior.
- Add regression checks executing each editor registration, nested-child return, success/warning/error precedence, successful Host export/download, Task Export actions and unchanged rendered geometry in both themes and supported widths. Retain the existing save, checkout, permission and export payload paths.
- Increment application/package identities to `0.18.476-RC3`, build `20261022`; update current guide identities without adding release history to the README.
- Validation: [Windows CI 39819299652](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39819299652) passed for application/test commit `1344bd43c3c6ce288d9bce8a5c144d1ae32d1951`: Release build/publish with zero compiler warnings/errors, **2,268 xUnit tests passed** (none failed/skipped), all registered Node/Edge suites, **8,842 Host layout checks** and **2,788 Task layout checks**. Coverage includes successful nested Host export/download, Clone workflow, unchanged editor geometry, footer routing across all five editor runtimes and unresolved-error precedence. Automated native responses remain simulated; live MOVEit acceptance remains required.

### v0.18.475 RC3 — Release identity and documentation (20261022)

- Promote the application, Login/About/footer version, upgrade manifest and release templates to `0.18.475-RC3`. Assembly/file versions are numeric `0.18.475.0`; the independent build date is `20261022`. Upgrade version comparison already supports the RC suffix. Database-schema and configuration versions remain `1`.
- Keep release-change summaries, the RC1/RC2 comparison and historical CI results in this changelog. The README describes current functionality, supported workflows and entry points rather than release history.
- Correct the obsolete Installation application-identity example and update the active installation, upgrade, security and testing guides to RC3. Preserve the separate native integration asset revision `0.16.206-RC1` and the repository name `MAUI-RC2`.
- Document the current Editors category, General/Folders placement, per-environment backup restore selections, built-in Script metadata permissions and current validation gates. Existing editor choices, permissions and native operations are unchanged.
- Remove obsolete RC2 wording from user-facing unmapped-step hints; retain their existing read-only preservation behavior. Extend the release-metadata check to accept the supported RC suffix while verifying numeric assembly/file versions and matching package identities.
- Validation: [Windows CI 39809441622](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39809441622) passed for application/test commit `4e8c8eb11ac3dec124725cb523389667b5434546`: Release build/publish with zero compiler warnings/errors, **2,268 xUnit tests passed** (none failed/skipped), all registered Node/Edge suites, **170 MOVEit Settings checks**, **8,549 Host layout checks** and **2,368 Task layout checks**. Focused local release-metadata, routing, upgrade and Markdown-link checks passed. Automated native responses remain simulated; live MOVEit acceptance is still required.

### v0.17.474 RC3 — Settings organization (20261022)

- Begin RC3 cleanup on the existing feature branch. RC3 remains a Release Candidate and carries forward the RC2 feature set and live-validation requirements.
- Move all six MAUI/native editor preference controls into a dedicated **Configuration > Settings > Editors** category immediately after General in the default navigation. Preserve existing configuration keys, values, defaults and Hidden behavior; editor controls no longer depend on Experimental placement metadata.
- Move **Folders / Create Folders** into **Configuration > Settings > General**, immediately after Recycle Bin. Retain its existing MOVEit-administrator checks, immediate action, duplicate protection and toast feedback. Copy Between Environments remains under Experimental.
- Existing saved Settings navigation orders gain Editors through the existing default-section fallback. No database-schema or configuration-version migration is needed.
- Update current candidate documentation and configuration paths; retain RC2 history and its comparison with RC1.
- Validation: #### 0.17.474 RC3 validation

[Windows CI 39803386874](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39803386874) passed for application/test commit `7b11d346b0614afdb4bdb17786506e2a494e28db`: Release build/publish, zero compiler warnings/errors, **2,268 xUnit tests** (none failed/skipped), all registered Node/Edge suites, **170 MOVEit Settings checks**, **8,549 Host layout checks** and **2,368 Task layout checks**. Local Settings checks verified moved controls, hidden values, unchanged editor definitions, folder permissions and dirty tracking. Automated native responses remain simulated; live acceptance is still required.

<a id="rc2-consolidated-implementation-notes"></a>

## Shared CRUD and integration reference

This maintained reference consolidates the shared implementation through 0.18.498 RC3. Version entries record behavior at the time; historical pending/deferred statements may have been superseded. Current operator instructions belong in [Installation](INSTALLATION.md), [Upgrade](UPGRADE.md), [Security](SECURITY.md) and [Testing](TESTING.md).

### Shared object boundaries and transport ownership

| Operation | Task implementation | Host implementation |
| --- | --- | --- |
| Object CRUD boundary | `ITaskCrudCoordinator` / `Services/MoveIt/Tasks/TaskCrudCoordinator.cs` | `IHostCrudCoordinator` / `Services/MoveIt/Hosts/HostCrudCoordinator.cs` |
| New empty object | Traditional uses v1 REST; Advanced/Sync use captured v0 `Task` POST envelopes with `TT=COS` / `TT=Sy`, disabled initially. Sync includes `SameSecs=-1`. | New Host uses captured v0 typed POST shells for Share, siLock, FTP, SSHFTP, POP3, SMTP, S3, AzureBlob, SharePoint, GoogleStorage and version-gated OneDrive. |
| Editor and common Information saves | `TaskJsonPersistenceService` owns checked-out JSON writes: Traditional v1, Advanced/Sync v0. Name/Description/Operator Notes and enabled-state patches share this lifecycle. | `HostDefinitionSaveService` owns supported typed v0 writes for editors and Information Name/Description. Unsupported profiles retain their explicit native common-information path. |
| Configured spreadsheet workflows | Traditional JSON construction/persistence share the composer and coordinator used by Quick Transfer and editors. | Configured Host spreadsheet creation/updates retain COM; the change to New Host POST did not change spreadsheet transport. |
| Native configuration operations | XML clone/copy, restore, native schedule exceptions and Traditional-to-Advanced conversion retain native adapters/COM support. Deletion uses shared REST. | Clone, deletion, XML snapshots/restore and native configuration adapters retain COM. |

Controllers, optional editor modules and browser scripts adapt user intent to these boundaries; they must not introduce parallel JSON writers. Inventory/search/reporting reads retain their snapshot/cache services, while saves require fresh authoritative definitions. The shared Traditional composer owns Source/Destination defaults, protocol paths/roots, Process/PGP and root assembly for editor, import and Quick Transfer callers. It clones input nodes and preserves unowned/version-specific fields. Omitted optional Group/action arrays differ from an explicitly empty array; do not normalize them indiscriminately.

<a id="checkout-concurrency-and-uncertain-outcomes"></a>

### Checkout, concurrency and saved-state verification

- MOVEit determines checkout availability. Open/retry uses normal native checkout, reports native owner/time when returned and never substitutes a local cleanup record as a lock. A failed request is a request error, not another native checkout state. Only a confirmed MOVEit administrator override sends REST `{"force":true}`; Global Parameters retain their COM force/release contract.
- MAUI defaults to **save-scoped checkout**. Optional **Checkout Objects for Duration of Edit** holds a shared lease from Task/Host/Script/Date List editor open through Close/logout, with re-establishment after saves and abandonment/idle cleanup. Both modes read the current definition under checkout, validate permission, exact nonempty ID/type and the editor revision, compose a detached patch, write once and verify acknowledgement/readback. Cleanup uses an independent bounded cancellation scope; already-unlocked responses are distinguished from real release failures.
- Full editor revision protection includes JSON and native credential state; Information edits compare their displayed fields so unrelated current settings survive. Stale values cannot silently overwrite concurrent edits. A confirmed MOVEit administrator Override Checkout establishes a fresh revision, retains form values and requires manual Save; it does not bypass later revision checks. Unsaved values are not durable drafts. The Local File System name is fixed, while captured settings and Description are editable. Validate MAUI metadata before native writes; native object and local metadata/audit writes are not one cross-system transaction.
- An accepted POST/PUT whose outcome cannot be confirmed is an **uncertain/partial result**, not permission to repeat it. Preserve the known ID, invalidate relevant inventory, show the recovery message inside the active modal and require inspection/reload. Never automatically replay POST/PUT, guess ownership by name, delete a possibly created object or fall back to COM after uncertainty.
- New Host acknowledgement/readback can reconcile bounded visibility delays through fresh inventory, but only by one exact returned ID/name/type match. Missing Host type discriminators in individual save GETs may be resolved by fresh exact-ID inventory; the original GET projection remains the revision/field-verification source. A flat checkout definition still needs matching identity and a type corroborated under checkout. Contradictory IDs/types, ambiguous inventory, authorization errors and requested-field mismatches remain failures.
- The shared audit baseline additionally requires matching IDs in the Host shell before render/reload and in Task definitions before patch, before PUT and at readback. Footer Saved feedback requires authoritative reload. Safe stage/reason diagnostics exclude native bodies and submitted secrets.

### Effective permissions and read-only behavior

The shared `MoveItObjectAccessService` and server permission filter enforce current native object rights independently of browser controls. Creation grants are combined across eligible Resource Groups; non-admin New Task/Host/Script/TLS/SSH/PGP forms require a Group as the first field above Name and administrator forms hide that selector. Task/Host Auto Format Name stays directly below Name. Edit Existing authorizes full existing Task edits but does not authorize New/Delete. Imports require creation rights; configuration exports, notifications and MAUI metadata updates require edit rights. Global Parameter management, Resource Group administration and Task folder management/assignment require a MOVEit administrator; non-admin folder viewing/filtering remains available.

Grid selection uses effective rights and the actual configured bulk operations, with disabled controls while permissions are checked. Read-only MAUI editors lock all configuration/properties/metadata, including HTML email bodies; status is footer-only and Close skips discard prompts. Task/Host native configuration reads resolve edit access before privileged snapshot paths. Read-only MAUI/native launch preparation skips edit snapshots and checkout. Information Save buttons are hidden for read-only access, remain hidden across tabs and reset for a later editable object. Native save permissions remain enforced independently of visibility.

### Host captured profiles and import contracts

Captured protocol profiles govern editable fields, defaults, inherited overrides and readback; unsupported properties stay preserved/read-only. These include SMTP/POP3 authentication and OAuth/TLS, UNC Run-As credentials, FTP/FTPS security/CCC/directory parsing, SFTP client keys/host fingerprints and ordered algorithms, SharePoint App/Certificate authentication/site/library fields, S3 alternative-provider settings, storage roots/integrity and the captured Local File System properties. Supported SSH/KEX choices come from MOVEit; new SFTP/Reset defaults come from saved MAUI configuration. Coupled native property groups must be validated/written together; do not infer equivalence between similarly named protocol properties.

Unsubmitted editor credentials retain established null-preserve semantics; nonempty replacements are handled as secrets rather than trimmed generic text. Spreadsheet blank-update rules are intentionally distinct from explicit editor clearing. Use the shipped Host CSV/XLSX templates and current shared profile definitions for canonical headers and Y/N values, rather than the retired phase proposal. Analyze resolves permissions, names, type/role, secure-field intent and visible referenced resources before import. Preserve row outcomes and block replay after an uncertain native mutation.

UNC Share creation requires **Resource UNC**. A visible duplicate path is rejected before POST, and MOVEit's server-side uniqueness check is authoritative for concurrent creations. Restored Share shells carry their archived UNC. New Host v0 transport does not imply that spreadsheet creation or Clone Host has moved off COM.

### Version-specific and cloud Host contracts

`MoveItFeatureSupport` is the shared registry for release-specific support. OneDrive Hosts and S3 IAM authentication require release 2026.0+ or native engine 18.0+. Prefer the reported Automation engine over Web Admin; only use Web Admin when the engine version is absent. Unknown versions grant no new capability. Host creation/editor controls, REST mutation/browse/test boundaries and Copy To create/overwrite checks reuse these rules; reauthenticate after a server upgrade.

OneDrive uses its captured typed v0 shell and shared Host save lifecycle. Authentication is Certificate or Client Secret. Test Host uses the captured `/hosts/onedrive/browse` endpoint, not an invented test endpoint; logininfo username is the client ID while the top-level username is the OneDrive user. Its header icon is ON. OneDrive Task-step/Quick Transfer composition remains disabled until native Source/Destination payloads are supplied.

S3 AuthMethod 0/1/2 means Access Keys / AWS Config File / EC2 Instance Role (IMDS). The optional AWS profile maps to DefUsername. IAM modes omit inactive access-key/alternate-provider fields; returning to Access Keys requires an explicit replacement secret. Both Host and Task browse paths forward the selected method.

SharePoint Test Host validates Realm ID for App Access (UsingAppAuthentication 1), or Tenant ID and certificate fields for Certificate Access (2). Stale certificate values are ignored in App Access; a blank Site selects the default site. Deprecated User Access remains unsupported. Google OAuth Test Host includes ProjectID/OAuthClientID and the saved-secret field; both OAuth browse callers clear HMAC login fields. Saved Host IDs resolve unsubmitted secrets through null-preservation; GET ciphertext is not forwarded. These existing SharePoint/Google modes do not gain a new 2026-only gate.

As directed by the user, S3 IAM responses reuse Access Keys parsing, SharePoint App Access reuses certificate-test parsing and Google OAuth reuses HMAC parsing. These are accepted response-contract assumptions; live S3/Google tests were unavailable. Automated regression evidence and remaining live checks are separate in TESTING.

### Editor integration and Advanced conversion

The optional `Features/Editors` modules share the MAUI modal runtime, read/patch services, captured Host capability catalog, metadata, audit, Undo and uncertain-save handling. `Editors:UseMauiTaskEditor:Value` selects MAUI editing for Traditional/Advanced/Sync together; `Editors:UseMauiHostEditor:Value` selects mapped Host editors. Related links in Information dialogs, Calendar and Host Tasks honor the same routing preference and originating-modal context. Keep optional UI adapters separate from shared core services used by imports and Quick Transfer.

Traditional panels provide the reusable Source/Destination/Process, schedules, parameters, Email/Run Task and override forms. Advanced adds a hierarchical flow with insertion points, selection, clone/move/delete, file loops and If/Else If/Otherwise branches. Array order controls execution; moves retain IDs, clones allocate fresh subtree IDs, and NextEID/branch bookkeeping is reconciled centrally. Unknown steps/criteria are preserved without fabricating editable semantics. Save Task Flow exports the whole flow including Start/End. Responsive flow/properties switching and the divider affect presentation only.

Traditional **Create Advanced Task** is a copy/conversion routine rather than an assumed server conversion call. The new name ends in Advanced with a unique suffix when necessary, and the new Task is disabled. Preserve schedules, parameters and native settings; place Sources first, per-file/process-as-destination processing and Destinations in a For loop, once-after-download processing after that loop, then task-level actions. Task actions test `[TaskStatus]` for Success, Failure or No xfers and preserve selected combinations; unconditional actions remain unconditional. Each File actions belong at the end of the loop and test `[ErrorCodeFile]` (0 success, greater than 0 failure); per-file No Transfer has no equivalent and is omitted.

Sync uses the captured `Sy` shape and shared checkout/save boundary, including folder/Host changes, direction/policy and captured Clear Sync Folders, Clear Sync Listings and View Sync Preview operations. Configuration PUTs and listing/preview operations are separate contracts. Sync omits Create Advanced Task and Transfer Exceptions. Source/Destination defaults such as the visible wildcard must be materialized into saved native fields, not displayed only as placeholders.

Host and Task editors share navigation widths, active-item borders, grouped property sheets, footer controls/status, modal-local errors and open/closed menu indicators. Host Test is in Actions; Close dismisses the editors/Information dialogs. Host footer clean/dirty/progress/Saved states use the existing draft baseline without per-keystroke requests; error messages take priority and completion timers cannot leak into another modal session.

### Copy Between Environments

The opt-in Copy To feature requires both MOVEit and MAUI administrator access and administrator Data Access accounts in both environments. Its isolated coordinator plans dependencies, unique names, compatible reuse and reviewed overwrites while existing object services own native reads/writes. It protects built-in Scripts and Local File System from copying, matches keys by verified identity, remaps recognized structured references and leaves copied Tasks disabled. Unsupported standalone SSH/private-PGP transfer, AS1/AS2/AS3, Global Parameters and free-form Script/macro references remain explicit limitations.

Source Group arrays take precedence over sparse inventory projections. Placement supports matching source groups, creating missing groups, one destination group or None; existing assignments/credentials/metadata are preserved on overwrite by default. Group creation does not copy User Group permissions. New Tasks default to Root. Compatible metadata copies independently; compatibility differences remain informational and blue, actual follow-up warnings are yellow, and failed/unverified writes are red.

Execution journals known IDs and outcomes, continues after modal Close, honors Stop between writes and never replays uncertain writes. Recovery definitions are protected, owner-gated downloads rather than automatic rollback. Abandoned plans expire; restart marks unfinished operations Interrupted. Reports/recovery default to 30-day retention (1–90). Backup Restore separately selects native configurations per backed-up environment, all unchecked by default, while MAUI settings/metadata restoration retains its existing lifecycle.

### MOVEit grid connection feedback

One request-scoped transport observer and browser policy cover MOVEit-backed grid loads/refreshes. DNS/refused/network failures raise the shared banner immediately; consecutive timed-out grid requests raise it at two. Cached responses cannot confirm recovery. A successful live read, including read-only report POSTs, clears the warning. Failed reads retain displayed rows and suppress repeated connection toasts while the banner is active. HTTP/authentication/TLS/data errors remain separate. The policy does not probe local grids, change refresh schedules or retry writes/checkouts.

### Grid, portability and operational contracts

Task filename search reads stored Source masks and Destination filenames on demand, including nested Advanced/Sync locations. Matching is case-insensitive and literal (`*.txt` finds that configured mask); macros remain as stored. A five-minute page cache avoids eager step reads. Refresh/return from MAUI editing invalidates an active search; failed reads retain Task-detail search with one standard page-error toast. Date/time filters support comparison, NOT and OR, explicit times/offsets, named-month dates and next calendar month/quarter/year macros; partial text matching remains available. Commas inside a named-month date are not OR separators.

Task/Host `.maui` portability wraps native XML in the v2.0 ZIP container with MAUI metadata and Tags. Server Key protection applies to same-key environments; Portable/Passphrase handles other environments and requires at least 16 characters with upper/lowercase, digit and special character. Native dependency export governs included objects/key material. Host export uses a temporary disabled carrier Task and one free Task slot; Host import uses an empty Tasks collection and creates no carrier. Folder placement, audit timestamps and Undo/Recycle history are not portable. Pre-v2.0 packages remain unsupported. Task spreadsheet import is a separate parse/analyze/commit path; current templates, root syntax, required Resource Group and metadata-column rules are tested in TESTING.

Dashboard File Activity KPIs and the 24-hour transfer chart share one snapshot. Initial descending 10,000-row pages render lower-bound progress, complete a catch-up pass, then use the completed LogID high-water mark and expire old rows. Task grid schedule state and configured schedule are distinct; Preflight reads configuration/runtime, does not run a Task and reports unsupported/runtime-dependent paths rather than guessing. Task email notifications target exact result combinations and preserve selected recipients when requested; templates carry Subject/Body and optional Text/HTML format. Legacy template format is handled conservatively.

Native Web Admin Undo is separate from MAUI editor-local Undo and grid history. Task/Host/Script/Date List integration keeps one native last-change baseline while the popup is active; Task child routes preserve context, and a converted Task ID establishes a new baseline rather than making conversion itself an Undo target. Safe baseline failures withhold Undo. Key/certificate Undo is unsupported. Integration assets and exact trusted origins must be deployed together on every applicable Tomcat node.

Backup Restore selects native configurations per environment, initially unchecked; MAUI-only restore leaves all unchecked. Backup Cleanup defaults to daily 1:00 AM application-local time with 1–30-day retention. Task Failure Alerts default off and group newly observed failures into a 30-second banner/audio alert without replaying history. Keep Alive defaults on at five minutes (1–20), makes local lightweight requests and cannot start a stopped app pool; IIS in-process hosting/startup/preload and one worker remain the supported topology. Live server-definition edits apply to new connections; endpoint/COM-host/environment changes clear unattended credentials, while startup-sensitive runtime/database changes still require restart.

### Operational safeguards and historical fixes

- **Task construction/import phases and Pass A/B (20261011–20261012):** consolidated composition and fresh persistence, exact creation reconciliation without POST retry, shared visible Resource Group lookup, mandatory spreadsheet RESOURCE_GROUP and MOVEit-admin-only Quick Transfer. Old phase notes that described Advanced/Sync JSON editing as future work are superseded by the implemented shared editors. Imported files are bounded before analysis: 50 MiB upload, 10,000 data rows/20,000 physical rows, 100 columns, 250,000 populated cells, 32,767 characters per cell and 5,000,000 retained characters. XLSX decompression is capped, uses forward-only parsing, prohibits DTDs/external resolution and rejects oversized input instead of truncating it. The UI Import Row Limit is a batch/preview limit, not the parser limit.
- **Quick Transfer:** process-local browser tokens are separate from durable cleanup/confirmation intent in `Data/quick-transfer-cleanup.json`. A background worker verifies Task/server/run identity, Success, unchanged name and no later reused run before eligible cleanup. Failed, cancelled, No Transfer, running, renamed or ambiguous Tasks remain. Recovery after restart requires the original environment's administrator Data Access Credentials; corrupt tracking state fails closed. Confirmation uses existing MAUI SMTP, supports persistent/temporary Tasks, and persists its at-most-once attempt marker before sending. SMTP failure does not change transfer success; delivery is not guaranteed or automatically retried. Pending confirmation state also contains recipient addresses and needs restricted ACLs.
- **IIS/Reports (v0.17.301–v0.17.304, v0.17.311–v0.17.312):** browser navigation/assets/cookies honor root, virtual, nested and route-name-colliding application paths. MOVEit REST URLs and native Angular fragments belong to the separate MOVEit server and do not get MAUI's PathBase. Reports store canonical application-relative source routes; transient returnUrl/returnLabel are removed from portable source context while data filters survive. Shared return validation rejects cross-application routes, encoded separators and traversal at path-segment boundaries. Actual IIS/proxy acceptance remains environment-specific. RC1 backport status is independent of this RC3 release; these records do not certify the older RC1 branch.
- **Restricted configuration and live connections (v0.17.306–v0.17.314):** deployment must preserve installation-owned appsettings.json/Data. The application root is read-only; the existing config file and Data are writable. Shared in-place writes retain file ACLs and save a last-good copy under Data/ConfigurationRecovery; they are not crash-atomic. Settings uses one parsed live document/revision with an in-process writer gate, rejects stale saves with HTTP 409 and centrally publishes reload-safe changes. External environment overrides may still supersede JSON runtime values. New server definitions apply to new sessions without restart; endpoint/COM-host/environment changes clear unattended credentials. Startup-sensitive settings stay pinned until restart. First-run Setup activates its database/runtime snapshot in-process only after successful persistence and remains gated on activation failure.
- **Authentication/reset (v0.17.315–v0.17.316):** persist machine-DPAPI Data Protection keys under Data/DataProtection-Keys; normalize only Login's anonymous antiforgery principal and recover stale login tokens via allowlisted AF codes. Authenticated mutations retain identity-bound CSRF validation. Factory Reset requires administrator confirmation, clears local configuration/Data on the next startup and preserves external SQL Server databases and SQLite files outside Data. It is not a database backup or a multi-node coordination mechanism.
- **Validation history:** retired ZIP/source reports explicitly did not run .NET or live MOVEit; their counts and pending gates were snapshots, not current certification. The v0.17.372 Windows CI baseline passed Release build/publish, 1,256 xUnit cases, ten registered Node suites and 8,137 rendered Edge checks (6,263 Host/Information/New Host plus 1,874 Task). Validation is commit-scoped; skipped CI and a pending current release gate must not be treated as a pass. Live MOVEit REST/COM, credentials, transfer semantics, IIS ACLs/DPAPI and operational recovery still require the acceptance checks in TESTING.md.

### Resources, Script guidance and modal feedback

Scripts use their shared coordinator for custom inventory/configuration, v0 creation and source/Information saves, native restore and deletion adapters; existing dependency/Recycle Bin guardrails remain in the service. Date Lists use a shared v0 coordinator. XML export/Undo and retained native operations continue to require COM. Key/certificate and Global Parameter services keep their established transports. Controllers/editor modules adapt intent rather than implementing parallel native writers.

Script New/Edit supports PowerShell/VBScript source, text import, templates, Properties/Tasks and independent per-user Auto Save. Imports accept UTF-8 or BOM-marked UTF-16 `.txt`/`.ps1` source without executing it. General Script Templates are environment-scoped; four supplied PowerShell defaults are seeded once, and subsequent edits/deletions are retained. Save as Template is PowerShell-only, with case-insensitive uniqueness and explicit overwrite. The PowerShell selector/import/template controls share a wrapping row above Source; VBScript retains Import below Source. Confirmed New Script/Date List creation returns to the caller; it does not automatically open Edit or acquire an edit-duration checkout.

Shared Script/email template saves require a live session with either MOVEit administrator or MAUI administrator status, enforced by `TemplateAdmin` on both save endpoints and corresponding modal controls. Ordinary users can load existing templates under their normal object access; read-only object views remain locked. Configuration template management and other MAUI administration keep their MAUI-only role requirement.

The built-in MAUI Script editor is entirely read-only, including MAUI properties/metadata, without Custom Scripts licensing, native checkout, custom Source retrieval or edit snapshots. Date List entries support `YYYY-MM-DD` and `YYYY-*-DD`; full-file text validation occurs before adding entries. MAUI editor Undo Last Change is a one-step local reversal scoped to the current open editor, consumed after use and reset on reopen/reload. Grid Undo Changes uses its separate saved history.

Global Parameter Information is the sole edit surface for Name, Value, Secure value/reset handling, MAUI metadata and related Tasks; values remain suppressed/preserved until explicit reset. No Auto Save or Experimental preference is added for it. Key/certificate creation uses the shared resource headers/icons; Names are plain text and supported edits use Information. PGP Information displays the service's Public/Private value as Key Type beside ID, hiding/resetting it for SSH/TLS.

Task Process guidance reads unfiltered built-in Script definitions through v1 REST and presents description, direction, required flags, defaults and choices. Traditional parameters are Task-scoped; Advanced parameters are Process-scoped with inherited Task values. Required placeholders never serialize blank native values or initiate Auto Save. Global resolution remains advisory. Unavailable metadata is reported, not guessed; reads are isolated per editor and late responses cannot replace another selection.

Shared feedback keeps modal-origin success/warning/error messages inside the active dialog, with registered editor errors in their existing footer. Native error fields retain their layout and severity classes; status banners do not repurpose them or erase unresolved errors. Background grid load/refresh notices retain their page origin but appear in the active modal footer with a Page update label; no toast is shown behind an open modal. Nested dialogs own their feedback, reopening clears stale messages, and supported editor routing has no intermediate fallback pages.

The current dark-theme colors are Clear Blue accent `#5aa9f4` and strong accent `#8bc4ff`, with matching translucent highlights and dark foregrounds on solid accent controls. Light-theme colors are unchanged. The earlier v0.17.429/v0.17.430 blue selections below are historical and were superseded by v0.17.431.

## RC2 release overview

RC2 expands the RC1 operational foundation with Traditional/Advanced/Sync Task editors, mapped Host editing and CSV/XLSX import, shared CRUD/revision/checkout handling, optional Script/Date List editors, Script templates and parameter guidance, and MAUI Groups & Permissions management. It also unifies modal feedback, related-editor navigation, Properties/Tasks presentation and retained native integration. Advanced/Sync/new-Host operations use the captured REST v0 patterns where implemented; COM remains required for retained native operations. See [release comparison](#major-differences-between-rc1-and-rc2) for the RC1 comparison and [TESTING](TESTING.md) for the acceptance gates.

Detailed version entries below are historical records. Their statements about incomplete features, pending validation or native transports describe that build, not necessarily the current candidate. The consolidated reference and operational guides describe current behavior.

### Major differences between RC1 and RC2

RC1 established the operational interface, reporting, Quick Transfer, metadata and native-editor integration, with early opt-in Traditional Task and Host editors. RC2 expands that foundation rather than replacing MOVEit's execution engine. The RC1 comparison baseline is the recorded `0.16.210-RC1` release; detailed version entries follow below.

| Area | RC1 baseline | RC2 additions and refinements |
| --- | --- | --- |
| Task editors | Native MOVEit editing plus the early opt-in Traditional editor. | MAUI Traditional, Advanced and Synchronization editors; nested Advanced flows, Sync preview/clear operations, Traditional-to-Advanced copy/conversion, Run As Of and flow-image export. |
| Host editors and imports | Initial experimental Host editing and existing native workflows. | Expanded mapped Host properties, credentials/OAuth, keys and SSH/KEX defaults, Local File System editing, shared Task overrides/Browse, and Host CSV/XLSX create/update workflows. AS1/AS2/AS3 remain deferred. |
| Save and checkout handling | Shared Traditional persistence and initial Host save profiles. | Common object coordinators, revision/readback checks, optional checkout for the duration of editing, abandoned-checkout cleanup, explicit administrator recovery and Host Auto Save. |
| Resource editing | Resource inventories, Information/creation forms and native editing. | Optional Script and Date List editors, Script templates and Process parameter guidance, editable Global Parameter values in Information, and consistent key/certificate creation and Information forms. |
| Groups and Permissions | Native MOVEit permission administration. | Opt-in MAUI hierarchy with All/Some/None summaries, Resource Group membership, User Group assignments, inline permission editing and guarded deletion. |
| REST usage | Traditional Task REST creation/persistence with retained COM paths. | Captured v0 creation/saves for Advanced/Sync Tasks and new Hosts, plus shared Host Information saves. COM remains required for the retained native operations. |
| Navigation and feedback | Initial modal, grid and native-editor integration patterns. | Consistent Properties/Tasks panels, editor preference routing, nested-editor return/refresh, in-modal feedback, refreshed Task Logs and broader automated browser coverage. |
| Object permissions | Existing MOVEit authorization. | Shared effective-rights checks for object operations, eligible Resource Groups for non-admin creation, permission-aware menus/bulk selection and read-only preferred editors. |
| Environment copy | Native and MAUI export/import workflows. | Experimental Copy To with dependency mapping, explicit reuse/overwrite, Resource Group and Task folder placement, compatible metadata, progress and protected recovery reports. |

Existing RC1 capabilities such as Dashboard, Reports, Quick Transfer and native `.maui` portability remain part of RC2. Their presence in RC2 does not mean they were introduced in RC2.

### RC2 editor and permission upgrade baseline

Recent RC2 updates include Local File System Source/Destination editing, in-place Host 0 editing, Google OAuth and SharePoint App/Certificate/default-Site Browse with supported proxies, SMTP/POP3 Task OAuth overrides, and text Not Matches conditions. Existing save/checkout/revision and retained COM paths continue to apply. Test the editor and Browse combinations against MOVEit after upgrade; automated checks do not establish live authentication or listing success.

Recent object-permission changes alter which controls are available to non-admin users; they do not grant new MOVEit rights. After upgrading, test New/Edit/Delete/notifications/export/import with the intended Resource Groups, read-only preferred-editor viewing and hidden Information Save buttons. Global Parameter management and Resource Group administration remain native-admin-only. Editor Undo now reverses one local change in the current opening; grid saved-history restore is separate. No database-schema or configuration-version migration is added for these changes.

The latest refinements put the non-admin Resource Group selector before Name on supported New forms, keep Task/Host Auto Format Name directly below Name, and restrict Task folder management/assignment to MOVEit administrators. Shared Script/email template saving now requires either MOVEit administrator or MAUI administrator status; loading existing templates remains available under normal access rules, and Configuration administration still requires MAUI Admin. PowerShell places the template selector and import/template buttons above Source; VBScript retains its existing Import placement. The dark theme uses the approved Clear Blue accent (`#5aa9f4`) and strong accent (`#8bc4ff`); light-theme colors are unchanged. These updates do not require manual configuration edits or a schema migration.

## RC2 version history

### v0.17.473 RC2 — SFTP compression setting (20261022)

- Move SFTP **Disable Compression** to Transfers as a Yes/No selector: Yes persists numeric `CmpL: 0`; No persists numeric `CmpL: 6`. Reuse the shared Host writer and preserve the saved selection on reload.

### v0.17.472 RC2 — Host certificates and MOVEit Help (20261022)

- Keep FTP and MOVEit Transfer client certificate controls visible with TLS off, including Set/Clear Certificate, Store and Subject. FTP certificate changes use the existing shared Host save path in every TLS mode.
- Add **Help > MOVEit Help** before About, opening Progress documentation in a new tab. Collapsed-sidebar navigation honors the new-tab target.

### v0.17.471 RC2 — Lockouts refresh placement (20261022)

- Move Lockouts Refresh to the right of the MOVEit Settings tab row, visible only while Lockouts is selected. Retain refresh/unlock behavior and responsive layout.

### v0.17.470 RC2 — Manage Lockouts (20261022)

- Added a Lockouts tab to the experimental MOVEit Settings modal with separate grey address/user panels, fixed-height tables, counts, select-all/partial selections, sorting, refresh, row unlock and Unlock selected. Empty/loading states stay inside the tables; empty unlock buttons remain visible and disabled.
- Confirmations name one identity or the selected count. Unlocks use the captured v1 GET/DELETE endpoints and bare string-array payloads, independently of the settings draft and without checkout. A fresh read verifies success; a changed lockout timestamp blocks a stale selection and expired lockouts are reported as already unlocked. Uncertain unlocks are not replayed; Refresh and a new selection are required.
- The separate native Manage MOVEit Lockouts navigation link hides while the MAUI Settings editor is enabled and returns when disabled, including after live Save Configuration. Retained MAUI/MOVEit administrator, feature, antiforgery and virtual-application guards. Footer errors stay in the editor. No schema migration.
- Added native transport, HTTP and rendered Edge checks covering the operational tab, no-checkout contract, exact arrays, permissions, stale/expired selections, uncertain responses, dirty settings preservation, navigation and layout.
- Validation: Windows CI [39784907065](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39784907065) passed for application/test commit `9401aecc219461b00a61af08b1068d82cf34eb3c`: zero build warnings/errors, **2,265 xUnit tests passed with none failed/skipped**, **159 MOVEit Settings/Lockouts rendered checks**, and all existing Node/Edge suites, including **8,301 Host layout checks** and **2,368 Task layout checks**. Light/dark empty/populated Lockouts images were reviewed. Native responses are simulated in automated tests; live MOVEit acceptance remains required.

### v0.17.469 RC2 — MOVEit Settings, Part 2 (20261022)

- Implemented Windows Event Log levels/name, task defaults and AS2 polling, State File cache modes, Tamper Detection, and IP/User Lockout Policy with trusted IPv4/IPv6 addresses. Settings groups reuse the Information modal grey surface; all six tabs share footer validation, dirty status, Save/Saving/Save and discard/reload handling.
- Globals sections patch one fresh full native definition under checkout and preserve unknown settings. State cache sentinels omit their unit; Tamper Detection inverts `SuppressHashing`; AS2 polling uses `AS2PollIntervalSecs` with zero as the inherited interval.
- Lockout policy uses captured GET/POST; trusted IPs load from GET and use one POST/DELETE per address. Empty successful mutation responses are verified by fresh reads. Only changed sections are submitted. Revision checks and per-environment MAUI save serialization guard stale submissions; native policy/list endpoints have no captured checkout contract.
- Added IPs precede policy updates and removed IPs. Partial/uncertain multi-endpoint saves are not replayed or rolled back automatically; the form retains its values and requires Reload. Added transport, HTTP and rendered-browser coverage for these contracts and footer behavior. No schema migration; feature defaults off and remains on its separate branch.
- Validation: Windows CI [39721418760](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39721418760) passed for application/test commit `b0cd3c7196235f52242b18d12ec9877ac91eb00c`: zero build warnings/errors, **2,239 xUnit tests passed with none failed/skipped**, **116 rendered MOVEit Settings checks**, and all existing Node/Edge suites, including **8,301 Host layout checks** and **2,368 Task layout checks**. Light/dark modal images were reviewed. Automated native responses are simulated; live MOVEit acceptance remains required.

### v0.17.468 RC2 — MOVEit Settings saved preference (20261022)

- Fixed the MOVEit Settings endpoint reading an unbound `IOptionsSnapshot<MauiEditorOptions>`, whose default remained false after the editor was enabled. The endpoint now reads the same published live configuration used by navigation and Groups and Permissions. Enabling/disabling applies to opening and saving without an application restart; the feature still defaults off.
- Added real HTTP/MVC regression coverage with production-style unbound editor options, live setting changes, administrator and antiforgery checks, root/virtual application paths, actual Logging serialization and shared Globals save/readback/cleanup. Existing transport and browser suites remain in the build gate.
- Confirmed `DELETE /api/v0/globals/checkout` as the native release call following Globals PUT; removed the initial unconfirmed-call note from current guidance.
- Validation: [Windows CI 39715786974](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39715786974) passed for application/test commit `44e757c4cdc0627954e614d3d622ec4408e98a71`: zero build warnings/errors, 2,210 xUnit tests passed (none failed/skipped), and all Node/Edge suites passed. The new HTTP tests cover live enable/open/save/disable, JSON round-trip, administrator/antiforgery enforcement and root/virtual application routes.

### v0.17.467 RC2 — MOVEit Settings, Part 1 (20261022)

- New off-by-default **Use MAUI Settings Editor** option under Configuration > Settings > Experimental > Editors. Configuration > MOVEit Settings honors the preference without an intermediate page or launch notification.
- Six-tab modal framework: Logging, Windows Event Log, Tasks, State File, Tamper Detection and IP/User Lockout Policy. Logging is implemented; the other five tabs are reserved for subsequent iterations.
- Logging includes all supplied native fields, eight log levels, KB/MB/GB file size units (up to 2 GB), optional Task/ASx directories using the shared Local File System folder browser, retained log count and transfer retry auditing.
- A dedicated Globals save service preserves unedited/unknown settings, checks the full revision under a short checkout, performs one REST v0 PUT, finishes the write checkout and verifies the saved Logging values under a fresh checkout. A rejected verification checkout is not released by MAUI. Conflicts/uncertain results block further saves until reload; errors stay in the modal footer. Opening/reloading performs a short checkout/read/release without a PUT. The duration-of-edit preference does not extend to this initial singleton editor.
- Admin policy, native MOVEit administrator verification, antiforgery-protected open/reload/save, disabled unchanged/invalid Save, discard confirmation and readback/cleanup regression coverage. No schema migration.
- Validation: [Windows CI 39713972081](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39713972081) passed for application/test commit `dbecede8b709030d1accbb8199371f2eb2bc2ac3`: Release build/publish with zero warnings/errors; **2,207 xUnit tests passed, none failed/skipped**; **63 rendered MOVEit Settings checks**; and all existing registered Node/Edge regression suites. Logging tests cover native AS2/browser JSON naming, 2 GB values, preserved fields, conflicts, cancellation, rejected checkouts, retained/released PUT checkout behavior and uncertain results. Light/dark modal images were reviewed.
- Live acceptance still required for the supplied Globals POST/PUT captures and folder access. Globals checkout release uses the existing resource DELETE-checkout convention; that specific singleton release call was not included in the supplied capture and must be confirmed on the target MOVEit build.

### v0.17.466 RC2 — Build 20261021

- Date/Time grid filters now support `<`, `<=`, `>`, `>=`, `!`/`not:` and comma/`or` alternatives across MAUI's numeric, named-month and ISO date/timestamp formats. Date-only targets include the whole day, timestamp comparisons preserve time and millisecond precision, and raw timestamps take precedence over display strings. Partial text filters retain their existing behavior.
- Added `[nextmonth]`, `[nextquarter]` and `[nextyear]` as complete next calendar periods, including year/leap-year transitions. Comparisons against macros use period boundaries and remain dynamic in Saved Views and scheduled Reports. Commas inside named-month dates are preserved.
- Kept browser and scheduled Report evaluation aligned, including application-local time-zone conversion for explicit-offset timestamps. Updated Filter Help with operators/examples and the Date Filter Macros picker with the new macros, reusing existing modal controls/styles. Shared cross-runtime fixtures and production grid/report regressions cover boundaries, alternatives, invalid dates, missing values and existing text/numeric behavior.

[Windows CI run 39702383234](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39702383234) passed for application/test commit `89b015b4bede535910dff6c67191c636e3032743` (**0.17.466 RC2, Build 20261021**): Release build/publish with zero warnings/errors; **2,177 xUnit tests passed, zero failed/skipped**; **102 Date/Time filter checks** including three browser time zones; **195 connection/UI checks**; and all registered Node/Edge suites, including **8,301 Host** and **2,368 Task** layout checks. Shared browser/Report fixtures cover date formats, calendar boundaries, negation, alternatives and timestamp precision; Report integration covers partial text and existing numeric/text behavior. Automated fixtures do not replace live MOVEit acceptance.

### v0.17.465 RC2 — Build 20261021

- Corrected Tamper Reset to send `POST /api/v0/actions/resethashing` with no request body, matching the native Web Admin capture. The `id`/`status`/`errorcode` object is the response acknowledgement and is no longer submitted as input.
- Kept success/error acknowledgement checks and administrator/antiforgery protection. Added production REST-client regressions for the empty request, failed/incomplete acknowledgements and HTTP 422 rejection without replay. Live MOVEit testing is required to confirm whether correcting this mismatch resolves the reported `IPAddress` database error; no server network or database settings were changed.

[Windows CI run 39694967685](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39694967685) passed for application/test commit `e772f37035ab185fe31eadf43661a743834e437b` (**0.17.465 RC2, Build 20261021**): Release build/publish with zero warnings/errors; **2,097 xUnit tests passed, zero failed/skipped**; **195 connection/UI checks**; and all registered Node/Edge suites, including **8,301 Host** and **2,368 Task** layout checks. The eight new Tamper Reset cases cover the captured bodyless request, acknowledgement/error handling and action protection. Automated fixtures do not replace live MOVEit acceptance.

### v0.17.464 RC2 — Build 20261021

- Completed the grid connection-handling cleanup for integration into master. Only marked grid responses can change the connection state; unrelated editor/action authorization responses no longer reset a pending grid timeout streak. The shared two-timeout threshold, cached-data behavior and live-read recovery remain unchanged.
- Consolidated current connection acceptance guidance, moved older test-run identities into the changelog archive, corrected stale Security version text and updated README/Installation/Upgrade guidance. No new Markdown files, configuration settings or schema changes were added.

[Windows CI run 39670930729](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39670930729) passed for application/test commit `49cff7c80bc574fb76dadc92ae6f32936d29d47f` (**0.17.464 RC2, Build 20261021**): Release build/publish with zero warnings/errors; **2,089 xUnit tests passed, zero failed/skipped**; **195 connection/UI checks**; and all registered Node/Edge suites, including **8,301 Host** and **2,368 Task** layout checks. The non-grid authorization isolation regression also fails against the earlier implementation and passes after cleanup. Automated transport fixtures do not replace live MOVEit acceptance.

### v0.17.463 RC2 — Build 20261021

- Shared grid connection observation now covers response-body reads, including the combined SSH/TLS/PGP inventory. A connection reset, prematurely ended response or timeout after HTTP headers no longer escapes as an ordinary grid error. Buffered authentication reads also retain transport evidence without changing token renewal, timeout or retry behavior. HTTP/authentication/certificate errors and malformed JSON remain distinct.
- Successful read-only MOVEit report POSTs now confirm recovery for Records grids. Token renewal alone still does not. Upcoming Runs clears its earlier local timeout message when the shared connection banner takes over.
- Expanded regression coverage executes all nine production page JSON adapters, their common lifecycle, and the custom Tasks/Records/Upcoming Runs refresh catches. Real HTTP/MVC tests exercise both Keys & Certificates load/refresh actions with the production REST client, parallel SSH/TLS/PGP inventory, response-body failures, cached navigation, recovery and IIS application prefixes.

[Windows CI run 39647109366](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39647109366) passed for application/test commit `c3a95af0aa2b8ff873774de015946464c70c343d`: Release build/publish with zero warnings/errors; **2,089 xUnit tests passed, zero failed/skipped**; **189 connection/UI checks**; and every registered Node/Edge suite, including **8,301 Host** and **2,368 Task** layout checks. New coverage adds 39 real HTTP/MVC Keys & Certificates cases (controlled upstream transport, both load/refresh actions and application prefixes, cache/recovery and buffered token-read failure), plus report POST recovery and body cancellation checks. Live MOVEit outage acceptance remains separate.

### v0.17.462 RC2 — Build 20261021

- Grid loads/refreshes show one shared **MOVEit Unreachable** application banner for confirmed DNS, refused connection or network failures, or after two consecutive timed-out grid requests. Existing rows remain visible after failed reads; refresh timing, manual refresh and pause controls are unchanged. Initial loading shells still finish.
- The shared MOVEit HTTP client supplies request-scoped transport evidence only for Tasks, Upcoming Runs, Hosts, Scripts, Date Lists, Global Parameters, Keys/Certificates, Groups and Permissions, and Records grid endpoints. HTTP 401/403/500, TLS validation errors, browser-to-MAUI failures and cancelled browser navigation do not imply MOVEit is unreachable. Multiple upstream timeouts within one grid request count once.
- Banner state/timeout count are scoped to the tab, login, selected connection and IIS application. Cached responses cannot confirm recovery; a successful live grid read clears the banner. Repeated connection-error toasts are suppressed while the banner is active. No probes, backoff, write retries, editor/save/checkout changes or configuration/schema changes were introduced. Local-only grids remain unchanged.
- Non-admin shell permission lookup also handles upstream timeout by retaining its existing fail-closed permissions fallback, allowing the grid shell to render. Validation includes controlled server transport/cancellation tests and production JavaScript lifecycle checks; live MOVEit outage acceptance is still required.

[Windows CI run 39642551232](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39642551232) passed for application/test commit `e543776789d68bc79bd3b6f6ac3f3717ae013996`: Release build/publish with zero warnings/errors; **2,045 xUnit tests passed, zero failed/skipped**; **47 production JavaScript connection/lifecycle checks**; and all registered Node/Edge suites, including **8,301 Host** and **2,368 Task** layout checks. The 48 new server cases include real HttpClient timeout, browser cancellation, request isolation, HTTP/authentication/TLS distinctions and cached-versus-live outcomes. Live MOVEit outage acceptance remains separate.

### v0.17.461 RC2 — Build 20261021

- Hosts and Quick Transfer now use one New Host partial and one client controller. UNC validation, Resource Group selection for permitted non-admins, name formatting, busy/error handling and uncertain-create replay guards share the existing Hosts/CreateHost endpoint and Host CRUD layer.
- Quick Transfer preserves its transfer draft and Source/Destination selection, opens the configured MAUI or MOVEit Host editor and refreshes the created Host on return. POP3/SMTP remain excluded from Quick Transfer's creation choices. Quick Transfer remains MOVEit-admin-only.
- Validated by Windows CI run 39628808351: Release build/publish with zero warnings/errors, 1,997 xUnit tests and all registered Node/Edge suites, including 8,301 Host and 2,368 Task layout checks. Shared creation and both Quick Transfer panel/editor combinations passed. Live MOVEit acceptance remains separate.

### v0.17.460 RC2 — Build 20261021

- Native MOVEit launches no longer send opening, sign-in or opened notifications into the originating MAUI page or modal. This prevents the extra banner from changing the Task Editor layout when Edit Host uses MOVEit.
- Removed unused launch-message options from Task, Host, Script, Date List, Quick Transfer and shared navigation callers. Actual launch/login/skin failures remain visible as errors; shared editor routing uses the registered modal error surface instead of inserting informational banners.
- Regression coverage exercises the native handshake and return routing without success notifications, while retaining error reporting. Validated by Windows CI run 39621054878: Release build/publish with zero warnings/errors, 1,997 xUnit tests, all registered Node/Edge suites, 8,301 Host and 2,368 Task layout checks. Live MOVEit acceptance remains separate.

### v0.17.459 RC2 — Build 20261021

- Copy Between Environments requires at least two distinct configured MOVEit environments. The Experimental toggle and recovery retention control are disabled with a tooltip when unavailable. Connection changes update availability immediately; saving fewer than two environments forces the feature off, and server requests enforce the same requirement. HA nodes in one environment count once.
- Groups and Permissions reserves its final viewport height before JavaScript and the first data response, retaining the existing short-window scrolling fallback.
- Regression coverage includes same-environment HA nodes, connection removal, stale enabled settings, retained retention values and first-load grid height before/after scripts and data. Validated by Windows CI run 39605498480: Release build/publish with zero warnings/errors, 1,997 xUnit tests, all registered browser/Node suites, 197 Copy To checks and 333 Groups workflow checks plus held initial-load sizing assertions. Live MOVEit acceptance remains separate.

### v0.17.458 — Build 20261020

- Simplify the Advanced Task Flow PNG header to the Task name, ID and current local date/time. Remove the MAUI banner and Current editor draft label, and reclaim their header space without changing flow content.
- Show Add User Groups loading feedback once inside the table. Keep the filter/help controls from shrinking and retain the final form/table dimensions throughout directory reads.
- Extend the existing rendered browser suites to check timestamp output and unchanged loading/loaded geometry in both themes at desktop and narrow widths.
- Validation: [Windows CI 39532100235](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39532100235) passed with zero build warnings/errors, all 1,991 server tests, 330 Groups and Permissions browser checks, 2,368 Task layout checks and every registered Node/Edge suite. Reviewed the loading-state screenshots and full-flow PNG.

### v0.17.457 — Build 20261020

- Complete the final Copy To review of dependency/identity mapping, administrator boundaries, revision/checkout safeguards, protected key transfer, placement, cancellation, progress and durable outcomes. No additional functional code change was required.
- Consolidate current Copy To behavior and limitations in the existing documentation; remove review-branch deployment instructions, correct the Upgrade guide's stale version/build, and include environment copy in the RC1/RC2 comparison.
- Retain experimental opt-in defaults, existing CRUD ownership and Release Candidate guidance; include the reviewed Copy To feature and the recent per-environment Backup Restore and compact retention-control refinements in RC2.
- Validation: [Windows CI 39526969992](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39526969992) passed Release build/publish with zero warnings/errors, all 1,991 server tests, 197 Copy To browser checks, 38 Backup Restore checks and every registered Node/Edge suite. Live MOVEit acceptance remains separate.

### v0.17.456 — Build 20261020 (review branch)

- Apply the existing compact Settings number-control style to Copy recovery retention, matching other Settings controls without changing retention values or save behavior.
- Validation: [Windows CI 39525022604](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39525022604) passed with zero build warnings/errors, all 1,991 server tests and every registered Node/Edge suite.

### v0.17.455 — Build 20261020 (review branch)

- Replace Backup Restore's restore-all MOVEit checkbox with an individual, unchecked-by-default checkbox for every native environment export in the selected backup. Keep long lists scrollable and explain MAUI-only restore and non-atomic multi-environment imports.
- Carry the explicit selection through the controller and shared backup service. Validate selection labels against the backup and filter before resolving/authenticating targets, so unselected environments are never imported or contacted.
- Preserve the existing MAUI settings/metadata restore, write coordination, safety copy, Server-Key imports, full selected-target preflight, uncertain-outcome handling and restart flow. Freeze selections and prevent duplicate submissions while restoring; keep errors in the dialog.
- Add server integration cases and a registered Edge fixture for selective restore, MAUI-only behavior, invalid/unavailable exports, partial/uncertain outcomes, busy-state guardrails and many-environment layouts.
- Validation: [Windows CI 39523493181](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39523493181) passed with zero build warnings/errors, all 1,991 server tests, 38 selected-environment restore browser checks, 197 Copy To browser checks and every registered Node/Edge suite. Reviewed long-list Restore layouts in both themes; no live environment was restored by these tests.

### v0.17.454 — Build 20261020 (environment-copy review branch)

- Use the shared 38px grid header row in Copy To's Objects selection table. Column labels and the selection checkbox retain the shared vertical centering; existing table viewport and modal bounds remain in place.
- Validation: [Windows CI 39519278262](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39519278262) passed with zero build warnings/errors, all 1,977 server tests, 197 Copy To browser checks and every registered Node/Edge regression suite. Reviewed light/dark Objects screenshots.

### v0.17.453 — Build 20261020 (environment-copy review branch)

- Replace Completed with skips with Completed with warnings for actual follow-up warnings. Missing/incompatible MAUI metadata and empty-source destination retention are informational and leave a successful operation Completed.
- Highlight result objects with warnings in yellow, failed/unverified writes in red, and metadata-only differences in light blue. Keep explicit text labels and per-object details; active writes remain neutral.
- Separate metadata differences in plans and durable reports. Normalize known metadata notices in retained reports without hiding actual write failures or changing native copy outcomes.
- Identify audit, Task folder and metadata write failures separately while retaining the existing follow-up order and stop-on-failure behavior. Do not retry native writes.
- Add server and browser regression coverage for result classification, legacy reports, null notice lists, local follow-up failures and both-theme result highlighting.
- Validation: [Windows CI 39516540753](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39516540753) passed Release build/publish with zero warnings/errors, all 1,977 xUnit tests, 197 Copy To browser checks and every registered Node/Edge suite. Reviewed rendered result screenshots; live MOVEit acceptance remains separate.

### v0.17.452 — Build 20261020 (environment-copy review branch)

- Apply Resolve choices atomically. A rejected row leaves all prior choices, overwrite-impact checks and the existing reviewed plan unchanged; an accepted resolution invalidates the old Review token before applying changes.
- Reject missing/invalid request fields and malformed selection rows before Data Access connections or copy-plan changes. Block comparison requests after a copy starts.
- Validate saved report identities, ownership fields and object lists before use. An incomplete/corrupt report gives a clear error and cannot prevent reconciliation of other reports; no writes are replayed.
- Ignore placement/comparison errors from closed dialogs when a new wizard is opened. Preserve HTTP status on non-JSON error responses so access-denied progress reads stop retrying.
- Clarify missing-source-dependency and unexpected-response messages. Keep feedback inside the existing modals and retain the current native transfer/CRUD paths.
- Add focused server and browser regressions for the corrected cases. Existing branch isolation, default-off setting and dual-administrator requirement remain in force.
- Validation: [Windows CI 39512886390](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39512886390) passed Release build/publish with zero warnings/errors, all 1,960 xUnit tests, 165 Copy To browser checks and every registered Node/Edge regression suite. Live MOVEit acceptance remains separate.

### v0.17.451 — Build 20261020 (environment-copy review branch)

- Consolidate Copy To progress into one line containing phase/current object, verified count and elapsed phase time. Remove duplicate running/finished footer messages; Stop acknowledgement uses the same line.
- Preserve distinct report details, group-creation outcomes and inline errors. Keep the existing progress bar, polling and copy/verification behavior.
- Validation: Windows Release build/publish passed without warnings/errors; all 1,939 xUnit tests, 161 Copy browser checks and existing regression suites passed. See Testing for the executed commit/run.

### v0.17.450 — Build 20261020 (environment-copy review branch)

- Show real copy phases, the current object, elapsed phase time and verified/total object counts. Preparation and the first object use an indeterminate bar with activity feedback; stopped/cancelled operations retain their actual verified count rather than being filled to 100%.
- Publish lightweight owned progress snapshots without rewriting the durable journal for every inventory read. Retain write-ahead identities/outcomes, and label the active pending write In progress while preserving Not verified after failure/restart.
- Bound status reads, retry transient read failures, stop retrying access failures, deduplicate in-flight polling and ignore late responses after Close/reopen. Preserve unchanged result rows and enable New Copy when polling observes completion. No Start/write request is automatically replayed.
- Honor queued/preflight Stop and configuration disablement before Resource Group/folder/object writes, including each new folder segment; finish an in-flight object and its checkout cleanup.
- Release protected plan/key data even when initial/final journal I/O fails. Reconcile other retained reports when one journal is corrupt; interrupted work remains unreplayed.
- Require explicit resolution when multiple destination keys match one verified identity. Update the existing operator, security, installation, upgrade and acceptance documentation.
- Validation: Windows Release build/publish passed without warnings/errors; all 1,939 xUnit tests, 155 Copy browser checks and existing regression suites passed. See Testing for the executed commit/run.

### v0.17.449 — Build 20261020 (environment-copy review branch)

- Correct Copy To's group provenance: Task/Host/custom-Script structured Group data now takes precedence over the inverse Resource Group inventory, matching the working Information readers. Keep known inventory display text when an inverse membership projection omits the object; do not split group names on commas.
- Carry authoritative assignments through dependency analysis, source revalidation, overwrite preservation and destination guards. Reuse the existing Script raw reader through its shared coordinator interface.
- Immediately refresh group/folder selection previews through the server's existing placement rules. Preview only calculates over the owned plan; it does not authenticate, export, checkout or write MOVEit objects. Full Review remains required and revalidates before copying.
- Identify source groups separately from destination before/after placement, including when overwrite preservation keeps destination groups. Remove stale placement rows on preview failure, keep errors inside the modal and preserve control focus. Invalidate the previous execution token when placement changes.
- Add regressions for the Information-visible Task group missing from inverse membership/XML, explicit group removal, comma-containing names, owned-plan preview, and immediate Create groups as needed feedback. Windows CI passed 1,929 xUnit tests, 138 Copy browser checks and all existing suites.

### v0.17.448 — Build 20261020 (environment-copy review branch)

- Restore the Copy To bulk menu to the shared grid menu dimensions while keeping its label on one line.
- Default uniquely matching compatible destination names to Reuse Existing for Tasks, Hosts, Scripts and Date Lists. Preserve explicit resolution for ID-only conflicts, incompatible/ambiguous matches and fingerprint/private-key restrictions.
- Read exact source and destination Resource Group assignments through the shared, fully paged REST inventory. Carry membership snapshots through dependency analysis, Review, missing-group creation and destination placement; do not infer membership from exported XML or comma-separated display text.
- Preserve destination memberships on overwrite and require a new plan if source assignments or reviewed destination assignments change before copying.
- Add focused server and browser regressions for name reuse, native member types/IDs, multiple/comma-containing group names, XML without Group elements, placement modes, stale assignments and original menu sizing. Windows CI passed all 1,924 xUnit tests, 128 copy-browser checks and the existing suites.

### v0.17.447 — Build 20261020 (environment-copy review branch)

- Ensure cancelled authentication/Info reads dispose their temporary Data Access connection scope. Windows CI passed 1,907 xUnit tests, 125 copy-browser checks and all existing regression suites; see the Testing guide for the executed run.
- Rename the feature's bulk command to Copy To and add isolated Copy To entries to all five supported grid row menus, seeding the current object without changing grid selection. Built-in Scripts and Local File System remain unavailable copy roots.
- Fix Copy From to the current connection. Keep the destination selector usable during connection reads; cancel superseded reads, ignore late responses, bound read/planning timeouts and preserve explicit source selection. Destination connection validation no longer fetches full object inventories.
- Remove repeated Action required prefixes and validate Resolve dropdown changes immediately so row feedback/highlighting follows the current action. Preserve focus after validation and retain overwrite-impact guardrails.
- Add Create groups as needed placement. Plan/validate missing source names in Review, create them through the shared checked-out Resource Groups service before object writes, preserve existing groups and record planned/created names and uncertain outcomes. Source user-group permissions are not copied.

### v0.17.446 — Build 20261020 (environment-copy review branch)

- Make built-in Scripts and Local File System unselectable copy roots; retain explicit reuse-only dependency mapping and server-side protection against creation, renaming and overwrite.
- Show automatically included dependencies checked, dimmed and locked in Objects. Selection preview shares Analyze's bounded source resolver without exporting configuration, creating carrier Tasks or storing a copy plan; explicit selections survive dependency changes.
- Widen the feature-enabled Bulk Actions menu, compact the Objects checkbox column and allocate more width to Name. Return focus/scroll to the form top after action buttons while preserving comparison-modal context and keyboard checkbox focus.
- Add server/browser coverage for protected roots, recursive/shared dependencies, preview errors/retry, environment changes, table/menu dimensions and action focus.

### v0.17.445 — Build 20261020 (environment-copy review branch)

- Add an isolated experimental Copy Between Environments coordinator, shared modal and bulk-menu entry points for Tasks, Hosts, Scripts, Date Lists and Keys/Certificates. The setting defaults off and every endpoint requires a live MOVEit administrator session plus MAUI Admin authorization.
- Require Data Access accounts for both environments. Analyze structured dependencies, resolve names/IDs independently, generate unique new names, compare configuration and metadata in a nested modal, and block Review until every row is resolved. Overwrites require impact review and confirmation; built-ins/default Host are reused and copied Tasks remain disabled.
- Reuse shared native protected Task/Host export/encode/import and resource CRUD operations, with current revision/checkouts, disabled creation, prerequisite ordering, saved-state checks and retained IDs after uncertain responses. Add exact Resource Group placement to the shared Groups service without changing reused dependencies. Preserve destination metadata/groups/credentials by default on overwrite; report unsupported metadata, missing source groups and unsupported Date List placement.
- Run copy execution independently of the browser. Keep redacted per-object reports and protected overwrite recovery definitions, honor Stop Copy between writes, expire abandoned plans and expose retained results/recovery downloads. Restarted operations are marked interrupted and are never replayed automatically.
- Enforce key capabilities: protected native dependency transfer where available, existing standalone TLS/public-PGP exports, fingerprint-based reuse and full PGP fingerprint derivation from public packets. Standalone SSH/private-PGP exports, key rename/overwrite, AS1/AS2/AS3 and free-form/global-parameter references remain outside automatic transfer.
- Keep temporary Data Access logout isolated from the browser login, align background checkout scopes with editor reservations, and mask secure Parameter values in comparisons. Add planning, native transfer, journal/recovery, identity and modal regressions. Live two-environment MOVEit acceptance remains required.

### v0.17.444 — Build 20261020

- Add a MOVEit Key Details / Certificate Details section above Additional Information in Keys and Certificates Information. Show PGP creation/expiration, expired status, preferred encryption algorithm, key type and length; SSH fingerprint and both public-key formats; and TLS validity, expiration, thumbprint and serial number. All controls are read-only; public-key text areas preserve complete copyable contents.
- Use MOVEit's PGP Expired flag in the shared grid/Information expiration rule when present, with the existing date fallback otherwise. Recognize the documented native pub value in public-only PGP export help.
- Omit Signing Length because the current v1 inventory does not expose a verified value.
- Validation: [Windows CI run 39446304426](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39446304426) passed Release build/publish with zero warnings/errors, 1,851 xUnit tests with zero failures/skips, and all registered Node/Edge suites, including 429 Resource Information checks for native details, complete public-key text, missing values and both themes.

### v0.17.443 — Build 20261020

- Explain the disabled Include Private Key option for public-only PGP exports with inline help below the checkbox. Hide the note for TLS, private keys and keypairs.
- Validation: [Windows CI run 39440317932](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39440317932) passed Release build/publish with zero warnings/errors, 1,849 xUnit tests with zero failures/skips, and all registered Node/Edge suites.

### v0.17.442 — Build 20261020

- Normalize key export checkbox and password-row spacing to the standard 16px form gap. Scope hidden-element rules to the export modal so styled password containers remain hidden for PGP and TLS public exports.
- Keep Include Private Key unchecked/disabled for public-only PGP keys and enabled for native pair/private keys. PGP exports never display or require export passwords. TLS private exports enable and require both matching password values; public exports hide and disable them.
- Extend browser regressions to inspect actual field visibility, spacing, required/disabled state and submitted values for public/private/pair PGP and TLS in both themes; retain server-side payload and password validation.
- Validation: [Windows CI run 39436335871](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39436335871) passed Release build/publish with zero warnings/errors, 1,849 xUnit tests with zero failures/skips, and all registered Node/Edge suites. Resource Information passed 319 checks, including rendered export visibility and 16px row spacing in both themes.

### v0.17.441 — Build 20261020

- Give Restart MAUI the same 18px separation from the completion banner as Return to MAUI, including the refreshed pending-restart page.
- Reset the release manifest's RestartRequired flag to false. The v0.17.440 export feature did not require an additional post-upgrade restart; its true flag was unnecessary. Startup-sensitive configuration changes and future explicit restart requirements still use the existing restart flow.
- Validation: [Windows CI run 39432861585](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39432861585) passed Release build/publish with zero warnings/errors, 1,845 xUnit tests with zero failures/skips, and all registered Node/Edge suites, including explicit restart, refreshed completion, retry and timeout workflows.

### v0.17.440 — Build 20261020

- Add Export to TLS/SSL certificate and PGP key row menus through the shared Keys and Certificates service and native REST v0 export endpoints. SSH export remains unavailable. Export follows existing Edit permission and PGP license checks.
- Reuse one modal for public/private export, with matching password fields only for TLS private-key export. Keep failures inside the modal, clear password inputs on completion/close, and serve downloads without caching. Preserve binary/armored downloads and decode the captured TLS base64 response.
- Add payload, download, permission and modal regressions. Windows CI run [39431194406](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39431194406) passed Release build/publish with zero warnings/errors, 1,845 xUnit tests with zero failures/skips, and every registered Node/Edge suite. The 226 Resource Information checks include TLS/PGP export. Live MOVEit export/import acceptance remains separate.

### v0.17.439 — Build 20261019

- Limit Advanced Task flow dragging to editable step rows. Loop/conditional containers and their nested (+) insertion controls cannot start moves; (+) controls remain insertion menus and drop targets. Recompute row drag permission on every render, including collapse/expand, while retaining read-only and branch restrictions.
- Add Edge regression coverage for click-and-hold on root, loop, If, Else If and Otherwise insertion points in both themes, rejected insertion/container drag events, unchanged topology/draft state, normal step moves, and read-only rows. Windows CI run [39349210347](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39349210347) passed Release build/publish with zero warnings/errors, 1,832 xUnit tests with zero failures/skips, and every registered Node/Edge suite (8,301 Host and 2,366 Task checks). Live MOVEit acceptance remains separate.

### v0.17.438 — Build 20261019

- Wire Test Host, Clone Host and Export Host when a Host editor opens from a Task. Both entry points use one shared set of Host action dialogs and existing CRUD controllers. Host export uses its own dialog scope so it cannot bind to Task export controls.
- Keep the source Host editor and checkout open while Clone Host is displayed. Cancel returns to that Host; successful cloning opens the clone in the same editor and preserves the original Task return callback. Tests forward the active Host edit lease.
- Disable Delete Host with an explanation when Task usage is known; retain server-side live Task/alternate-Host dependency checks and Recycle Bin guardrails. Display preflight failures inside the Delete modal.
- Add callback/routing regression cases and rendered Edge coverage for nested action availability, lease handoff, clone cancel/error/success, Host export isolation and errors, live Delete blockers, and return to the original Task context. Windows CI run [39348128523](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39348128523) passed Release build/publish with zero warnings/errors, 1,832 xUnit tests with zero failures/skips, and all registered Node/Edge suites, including 8,301 Host rendered checks. Live MOVEit acceptance remains separate.

### v0.17.437 — Build 20261019

- Treat Traditional-to-Advanced conversion as a read of the source Task. Borrow its verified editor checkout without the save lifecycle's release/reacquire/revision cycle; retain source editing even when verification of a separately created Advanced Task fails.
- Synchronize checkout maintenance with the active owner's latest token pair. Compare the observed token when merging a cleanup refresh, rather than treating expiration duration as a token revision. Preserve environment/login isolation and do not restore credentials, rights, idle activity or a logged-out session from cleanup work.
- Reject explicit native checkout errors inside successful HTTP responses consistently for Tasks, Hosts, Scripts and Date Lists. Definitive acquisition rejection leaves no MAUI reservation and does not issue a release.
- Use the shared read/checkout layer for Host connection/path tests, forward the originating Host editor lease, and prevent tests from releasing its checkout or borrowing another editor's reservation. Background tests use their current server scope.
- Retry temporary heartbeat network/timeouts/5xx failures without falsely revoking editor ownership. Authoritative rejection and server Save validation still block stale/expired leases. Log administrator recovery failures with acquisition/revision-read stages and handle network/journal cleanup errors consistently.
- Add regression cases for converting an open clone, failed new-object readback followed by source Save/Close, token rotation/identity isolation, native checkout acknowledgements, Host tests and heartbeat retry.
- Windows CI run [39345069294](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39345069294) passed Release build/publish with zero warnings/errors, 1,832 xUnit tests with zero failures/skips, and all registered Node/Edge suites (8,274 Host and 2,336 Task rendered checks). Live MOVEit validation of the reported sequence remains separate.

### v0.17.436 — Build 20261019

- Add Experimental > Folders > Create Folders: use the current MOVEit Resource Group inventory to create missing root-level Task folders alphabetically. Preserve all existing case-insensitively matching Task folders and their assignments in the active environment. A single database transaction prevents partial batches; repeat clicks are guarded and success/failure is shown in a toast. The action follows existing MOVEit-admin folder-management permissions and does not save Configuration edits.
- Simplify the Factory Reset modal's configuration-reset wording. Present Restart Application guidance in a yellow warning box with the same typography as Factory Reset, a bold first line and normal explanatory text below it.
- Add database/controller checks for additive behavior, environment/type isolation, ordering, repeat execution, validation, rollback and authorization, plus the client operation's busy/antiforgery/toast/retry checks.
- Windows CI run [39340903578](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39340903578) passed Release build/publish with zero warnings/errors, 1,810 xUnit tests with zero failures/skips, and all registered Node/Edge suites. Live MOVEit/IIS acceptance remains separate.

### v0.17.435 — Build 20261019

- Standardize the Experimental editor descriptions around their supported objects and the same native-editor opt-out wording. Remove the obsolete Host-specific development warning and simplify the Script description.
- Shorten the Task Editors Run As Of prompt to “Choose the date and time to use for the macros.” No editor routing, permissions or Run As Of behavior changes.
- Windows CI run [39327840525](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39327840525) passed Release build/publish with zero warnings/errors, 1,802 xUnit tests with zero failures/skips, and all registered Node/Edge suites.

### v0.17.434 — Build 20261019

- Keep the shared Browse modal at a preset, viewport-bounded size across loading, empty, populated and failed folder listings. Long listings scroll inside the frame; Task Editors, Quick Transfer and Browse Host share the fix.
- Add rendered regression coverage for stable geometry and visible footer controls in both themes at desktop and smaller viewport sizes.
- Windows CI run [39319471341](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39319471341) passed Release build/publish with zero warnings/errors, 1,802 xUnit tests with zero failures/skips, and all registered Node/Edge suites (including 8,273 Host and 2,336 Task rendered checks).

### v0.17.433 — Build 20261019

- Consolidate Task Editor and Quick Transfer browsing into one shared modal, runtime and stylesheet using the Task Editor design, sortable Name/Size/Last Modified columns and modal-local loading/errors. Preserve captured protocol requests, cloud-root selection and caller-specific field handling.
- Double-clicking a file in Task Source/Destination Browse applies its current folder and literal filename; Destination also clears Use original filename so the selected name is written. Folder-only selection updates only the folder. The combined selection is one local Undo change. Update the selection hint accordingly.
- Add MOVEit-administrator Browse Host immediately below Test Host for supported file-oriented Hosts, including Local File System. Inspection shows no selection hint, does not apply selections or write Host configuration, and returns to the retained Host editor.
- Share browse checkout ownership handling: borrow an editor's verified duration checkout without releasing/reacquiring it, retain independent save-scoped cleanup for other browse requests, and reject another editor's reservation. Guard cancelled/late listings against replacing a later modal session.
- Windows CI run [39317374852](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39317374852) passed Release build/publish with zero warnings/errors, 1,802 xUnit tests with zero failures/skips, and all registered Node/Edge suites (including 8,229 Host and 2,336 Task rendered checks). Live MOVEit listing/credential acceptance remains separate.

### v0.17.432 — Build 20261019

- Place PowerShell's narrower Script Template selector, Import Script File and Save as Template controls together above Source. Keep VBScript's existing Import placement below Source, preserve template-save permissions and wrap the PowerShell controls when space is limited.
- Remove the creation-permission helper sentence from New Task's Resource Group field. Keep the selector's visibility and validation unchanged.
- Validate the application in [Windows CI run 39280602200](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39280602200): zero build warnings/errors, 1,797 xUnit tests passed with zero failures/skips, and all registered Node/Edge steps passed. The non-admin Script fixture includes Resource Group and checks both language layouts at desktop and narrow widths. This validates fixtures/contracts, not live MOVEit/IIS acceptance.
- Refresh the existing RC2 documentation for Resource Group placement, template-save roles, Task folder restrictions, PowerShell/VBScript layout, current colors and validation evidence. Correct stale optional-Resource-Group and predefined-prefix spreadsheet instructions against the current required-column/row and literal-prefix contracts. Documentation cleanup leaves application version/build, runtime code and schema/configuration versions unchanged; no new Markdown files.

### v0.17.431 — Build 20261019

- Apply the approved Clear Blue dark-theme accent (#5aa9f4) and strong accent (#8bc4ff), with matching translucent highlights and dark foregrounds for solid accent buttons and calendar selections. Keep light-theme colors unchanged.
- Script New/Edit and Task notification/email controls disable shared template saving unless the user is a MOVEit administrator or MAUI administrator. The same live-session authorization protects both template save endpoints; existing template loading remains available.

### v0.17.430 — Build 20261019

- Change the dark theme strong accent from #8b96ff to #1e5a8a, matching the requested blue used by hover/active accent styles.

### v0.17.429 — Build 20261019

- Replace the dark theme's #7885ff accent with #3e8ac7 and update matching translucent accent, navigation and focus colors while preserving opacity.
- Restrict Task folder creation, renaming, moving, deletion and Task assignment changes to MOVEit administrators. Disable Manage Folders and Information/editor assignment controls for non-admins while retaining folder viewing/filtering and other authorized edits.
- Ignore folder values in non-admin Information/editor saves so a stale or tampered form cannot overwrite current folder placement. Reject direct folder/assignment mutation requests before local writes.

### v0.17.428 — Build 20261019

- Move Resource Group before Name in New Task, Host, Script, SSH, TLS and PGP forms, including both Script creation routes. Keep administrator/non-admin visibility rules and Auto Format Name placement unchanged.
- Review effective non-admin creation/edit/delete/run grants, endpoint filters, shared grid selection/menu checks, read-only editor/Information behavior, imports, metadata, leases and native editor preparation. Retain the common permission service; no new permission interpretation or authentication role mapping.
- Apply shared effective-access checks inside Date List create/save/restore/delete, matching other object coordinators. Check current edit access before native editor Undo snapshots, advancement, rebinding or restore; cleanup remains available after permission loss.
- Keep Task editor Run Now disabled when Run access is absent, independently of Edit and task completeness. Add server and browser regression coverage for these paths, creation-cache isolation/revocation and stale/failed permission reads.

### v0.17.427 — Build 20261019

- Hide Information-modal Save buttons when effective edit access is absent or cannot be verified. Preserve the denial across tab changes and restore normal visibility/readiness for a later editable object.
- Refresh the existing RC2 documentation, version/build examples, permissions/read-only guidance and editor-local Undo instructions. Preserve historical release entries and the Release Candidate notices; no new Markdown files or schema/configuration migrations.

### v0.17.426 — Build 20261019

- Derive bulk selection from effective object rights and the grid's actual operations; disable row/header selection and Bulk Actions when no permitted operation is available.
- Make read-only editors lock MOVEit configuration, MAUI properties/metadata and HTML email bodies. Keep read-only status in the footer, close without discard prompts and prevent edit-only snapshots/checkouts during read-only opens.
- Resolve Task/Host access before native configuration reads, avoiding the COM snapshot permission error for visible read-only objects. Retain the existing authorized save, revision and checkout safeguards.
- Windows validation passed with zero build errors, 1,757 server tests and all Edge browser suites. Live MOVEit acceptance remains a separate requirement.

### v0.17.425 — Build 20261019

- Make Host/Script/Date List Undo Last Change reverse the most recent local editor change once, consume Undo after use and re-enable it after another edit. Keep saved grid history separate; fix the Host grid restore handler's undefined hostType reference.
- Allow visible objects to open read-only while retaining write authorization. Hide mutation controls and skip read-only edit preparation, modification audit writes and editing checkout.

### v0.17.424 — Build 20261019

- Restore Auto Format Name placement directly below Task/Host Name, hide administrator creation Group selectors, dim disabled menu items and show read-only Resource Groups on editor Properties.
- Restrict Global Parameter Information/delete and Task bulk Resource Group administration to MOVEit administrators. Limit editor Undo to the current opening; v0.17.425 subsequently replaces restore-point behavior with local one-step Undo.

### v0.17.423 — Build 20261019

- Centralize effective MOVEit permissions for new/edit/delete, notifications, metadata, imports, exports and bulk operations. Require eligible Resource Groups for non-admin object creation and preserve native Group assignments in portability data.
- Treat Task Edit Existing as permission for the complete existing Task definition, including element additions/removals, without granting object creation/deletion.

### v0.17.422 — Build 20261018

- Increase Script Template Source height to 350px. Order Experimental editor switches as Task, Host, Script, Date List and Groups & Permissions, and clarify native/MAUI preference behavior.

### v0.17.421 — Build 20261018

- Fix empty CacheNames enum values in shared Task editor saves, including system Task 222222. Resolve unspecified values from the checked-out Task or native random default, preserve original-name selections and verify equivalent default readbacks.

### v0.17.420 — Build 20261018

- Changed Process guidance to “Required parameters not configured. A Global Parameter may supply a value.” and aligned the existing browser assertions.
- Reorganized the README around current RC2 usage, explicit non-final Release Candidate status, the author's project-origin statement (independent AI-assisted implementation; no compiled MOVEit code disassembled/decompiled), and a sourced RC1/RC2 feature comparison that acknowledges RC1's early opt-in editors.
- Centralized RC2 implementation/change context here, moved the consolidated notes out of the v0.17.373 entry, incorporated current Resources/Script guidance/modal behavior, and removed superseded per-build announcements from the upgrade instructions. Kept historical release records intact.
- Updated Installation/Upgrade/Testing/Security candidate notices, current editor preferences, resource security sections and implemented Groups & Permissions acceptance; preserved deployment, backup, COM and live-validation requirements. No new Markdown files, runtime appsettings.json edits, schema or configuration migrations.

### v0.17.419 — Build 20261018

- Extended shared modal feedback to success and warning messages as well as errors, so modal actions do not post unreadable toasts behind the backdrop. Existing editor error footers remain authoritative; nested dialogs own their messages, errors take precedence over informational banners, and feedback clears on reopening. Removed the duplicate Save View error toast.
- Kept grid load/refresh failures on their parent pages through explicit message origin, including when an editor is open. Routed the remaining grid notification helper through the shared feedback path.
- Added PGP-only Key Type beside ID in Key Information, using the existing Public/Private service value and identity styles; reset/hide it for SSH/TLS. No new MOVEit request is required.
- Audited supported editor routing: no remaining editor fallback pages were found. Corrected stale Groups & Permissions configuration help. Expanded shared-feedback and real Edge Information coverage. Runtime appsettings.json and schema/configuration versions are unchanged.

### v0.17.418 — Build 20261018

- Prevented the Script parameter Direction header from wrapping in narrow Advanced Task Editor properties panels while preserving description wrapping. Added browser layout coverage at 1920px and 1366px widths. Runtime appsettings.json is unchanged.

### v0.17.417 — Build 20261018

- Added read-only built-in Script metadata through the authoritative Script coordinator using the unfiltered v1 standard-script GET. The Task Process panel shows the server description, parameter descriptions, input/output direction, required flags, defaults and choices. Custom Script inventory descriptions remain available without requesting custom source.
- Reused existing Task/Process parameter editors. Traditional Task requirements appear in Task Parameters; Advanced Process parameters recognize inherited Task values. Missing required inputs appear as virtual Configure rows, retain existing parameters and never serialize blank placeholders. Defaults are prefilled only when configuring a value. Metadata reads do not dirty Tasks or trigger Auto Save; no new CRUD write path or COM call is introduced.
- Isolated Script request caches per editor opening, deduplicated/bounded background reads, protected against late responses, and added an in-panel reload/error state. Unsupported or absent parameter definitions are reported without guessing, blocking Task saves or navigating away from the draft. Global inheritance remains explicitly advisory when no value is configured in the editor.
- Added service/schema/session and real Edge coverage for guidance, required rows, scopes, defaults, safe text rendering, preservation, serialization, asynchronous reads and Auto Save. Runtime appsettings.json and schema/configuration versions are unchanged.

### v0.17.416 — Build 20261018

- Enabled Delete Resource Group using the captured v0 checkout/complete-dataset PUT through the shared Group mutation layer. A centered confirmation checks the live target revision, preserves unrelated Groups and native fields, retains member resources, and blocks repeat deletion after uncertain outcomes. No separate member or ACL deletion calls are introduced.
- Changed the grid counter to “x of x resource groups” and disabled Edit Permissions when no User Groups are assigned, including a matching inline-edit guard.
- Reused the Host Type selectable-card style for horizontal VBScript/PowerShell choices in Script New/Edit, retaining radio semantics and built-in read-only handling.
- Added service, MVC authorization/antiforgery/PathBase and Edge regression coverage. Runtime appsettings.json and schema/configuration versions remain unchanged.

### v0.17.415 — Build 20261018

- Validated Group dataset saves against the supplied native status=success/errorcode=0 acknowledgement. Empty, partial or malformed acknowledgements and HTTP 408 after PUT are treated as unconfirmed without retrying the write or restoring an older dataset.
- Matched Group editor and User Group dialog retry guards to the inline permission editor for proxy/server/session errors and timeouts. Blocked-save explanations remain visible while editing membership or reloading/switching User Group lists. Background session expiry no longer navigates away from open Group modal drafts.
- Reject missing, invalid or reused visible ACL identities before publishing an editable permissions snapshot; ACL ID zero remains valid. Preserved keyboard focus when selecting resources in the Group member picker, corrected singular member counts, and corrected escaped Domain names in the browser fixture.
- Added focused service and Edge regression coverage for these cases. Runtime appsettings.json and schema/configuration versions are unchanged.

### v0.17.414 — Build 20261018

- Added inline Edit Permissions to Resource Group rows: automatic expansion, editable grants for every assigned User Group and parent-row Cancel/Save. Tasks Add/Edit/Delete automatically selects and requires Edit Existing, which can also be granted independently. Save is disabled until there are changes; grid controls/refreshes cannot replace the draft.
- The authoritative permissions service validates all requested ACL identities/revisions and the supported flags before writing, rechecks later assignments, and sends one captured v1 PUT per changed ACL. Unknown native fields and unrelated grants are preserved. The existing Group/ACL write gate and cache invalidation are reused; partial outcomes retain successful rows and retry only unresolved changes, while conflicts/unconfirmed outcomes block replay pending inspection.
- Corrected Remove User Group centering by matching its dialog shell and visible card widths. Expanded service, transport, HTTP/PathBase/authorization and Edge workflow/layout coverage. Runtime appsettings.json and schema/configuration versions remain unchanged.

### v0.17.413 — Build 20261018

- Restricted Groups and Permissions navigation, grid, editor and ACL endpoints to live MOVEit administrators. MAUI roles do not substitute for MOVEit administrator status; existing Task bulk Resource Group operations retain their authorization.
- Matched the shared grid footer with Showing ranges, Rows selector, previous/next buttons and page counter. Parent Resource Groups remain the paging unit, with shared per-user row-size persistence and error rollback.
- Added a branded Add User Groups modal with Local/Domain discovery, Group Name/Description search, multi-selection across lists, already-assigned indicators and the standard disabled/busy Save behavior. The shared permissions service validates live selections and POSTs v1 ACL shells with ACLID null and all eight permission flags zero; existing grants are retained.
- Added assigned-row ellipses with Remove User Group and a confirmation modal. The shared service checks the live ACL identity and revision before v1 DELETE, accepts ACL ID zero, preserves unrelated assignments and invalidates the current snapshot cache. Group dataset and ACL writes share the existing MAUI mutation gate. Lost acknowledgements are inspected once without automatic write replay; partial/unconfirmed results stay in the modal and trigger a grid refresh.
- Expanded service, HTTP/PathBase/authorization and Edge tests for discovery/paging, exact REST contracts, concurrency checks, partial outcomes, cancellation/stale reads, modal state, grid refresh and both themes. Runtime appsettings.json and schema/configuration versions are unchanged.

### v0.17.412 — Build 20261018

- Standardized disabled MAUI modal buttons at 48% opacity across primary, secondary, danger, toolbar and nested picker/menu controls. Native disabled semantics and explanatory tooltips remain available; disabled buttons no longer use enabled hover effects.
- Added shared quiet form readiness checks and unchanged-save handling to Information panels and Script/Date List editors. Tag-only metadata changes, Task folder changes and secure Parameter resets count as edits; reverting to loaded values disables Save again.
- Added missing readiness checks to New/Clone dialogs, key/certificate creation/import, Script and notification templates, Tags, Resource Group bulk dialogs, saved views and grid settings. Optional values remain optional. Saved View updates still capture changes made on the calling grid.
- Preserved owner-controlled busy/loading, permission, checkout-conflict and uncertain-save guards, plus Auto Save's usable Close button. Expanded real Edge tests for both themes and representative modal states; CRUD payloads and transports are unchanged.

### v0.17.411 — Build 20261018

- Renamed the New Resource Group modal footer action from Add to Create Group, including its restored label after loading or saving. Member picker Add and Edit Group Save labels are unchanged.
- Application-wide disabled-button styling and form readiness were audited for review; no changes to other modals are included in this release. Runtime `appsettings.json` and schema/configuration versions remain unchanged.

### v0.17.410 — Build 20261018

- Added branded New/Edit Resource Group modals with Group Name, Description and horizontal Tasks/Hosts/Scripts/SSH/TLS/PGP tabs. Searchable multi-select pickers show ID, Name and Description; required dependencies have disabled Remove buttons and remain until no selected root needs them. No MAUI metadata or Properties page is added.
- Extended the shared Resource Group management service used by Task bulk actions. It expands Task/Host dependencies, validates selections, checks the edited Group revision under the existing whole-dataset v0 checkout, and preserves other groups, unknown native members and extra fields. Ambiguous PUT outcomes block blind retries; no stale cleanup snapshot is written after PUT was attempted. New endpoints require a live MOVEit session and anti-forgery protection for writes; native authorization remains authoritative, matching existing Task bulk Group operations.
- Fixed hierarchy grid viewport sizing with the standard fixed-height shell and short-window fallback; retained sticky multirow headers. Removed iteration placeholder text and moved Delete to the bottom of the row menu with the standard separator/red styling. Closing the Group modal forces a grid refresh while retaining the current view. Delete/ACL mutation controls remain disabled pending their patterns.
- Added service, HTTP/PathBase/authorization, and Edge workflow coverage for dependency expansion/removal, full-dataset preservation, concurrency conflicts, create/edit, selector behavior, in-modal errors, refresh and fixed grid height. Runtime `appsettings.json` and schema/configuration versions remain unchanged.

### v0.17.409 — Build 20261018

- Aligned Groups and Permissions with the Scripts grid: counters beside Search, Actions at the left immediately above the table, and the standard refresh clock, Clear filters and Grid Settings at the right. Removed the All / Some / None explanation and the Clear filters link from the filter-header row.
- Reused the shared resource-refresh lifecycle for the clock menu's Pause/Resume and Refresh Now actions. Existing data, hierarchy, filters and routing are unchanged; runtime `appsettings.json` and schema/configuration versions remain unchanged.

### v0.17.408 — Build 20261018

- Fixed Groups and Permissions navigation after enabling its Experimental setting: the controller had requested an unbound `IOptionsSnapshot<MauiEditorOptions>` and always read the default false, while navigation correctly read the saved value. Both page and data endpoints now use the same live configuration as navigation.
- Save Configuration now updates the existing navigation link immediately, so enabling/disabling this editor takes effect without reloading the Settings page.
- Removed the obsolete native-page placeholder view, model and controller helper. Enabled navigation opens the real grid; disabled navigation uses the established native MOVEit launcher. Direct disabled MAUI endpoints return 404 without an intermediate page.
- Added HTTP integration coverage for actual MVC dependency injection, compiled Razor/layout, runtime preference changes, data loading and IIS PathBase. The Edge grid suite now consumes the HTML produced by that application-route test in CI, rather than relying only on a separately assembled markup fixture.
- Runtime `appsettings.json` and schema/configuration versions are unchanged.

### v0.17.407 — Build 20261018

- Added the opt-in Groups and Permissions grid: expandable Resource Groups, exact assigned user-group grants and All / Some / None parent summaries. Empty Resource Groups remain visible. Summaries describe ACL assignments rather than effective individual-user access.
- Name-only search covers Resource Groups and assigned user groups. Only Name sorts; permission summary filters, parent-based pagination, shared row-size preferences, CSV and Print preserve the hierarchy and view context. Export/Print include the current filtered/sorted expansion state across pages.
- The shared Resource Group read service loads all v1 Resource Group/ACL pages, validates complete grants and joins within the visible inventory. Cache entries are user/server scoped. Failed reads preserve the last complete view with an inline error instead of replacing permissions with empty/denied values. The grid takes no checkout and performs no writes.
- Added **Use MAUI Groups & Permissions Editor** under Experimental > Editors (default off), including Save Configuration and navigation routing. Create/Edit/Delete/Add User Groups/Edit Permissions controls are explicitly disabled for this grid-first iteration while their modal details and write patterns are supplied.
- Added Resource Group parsing/cache/pagination regression tests and rendered Edge coverage for hierarchy, filters, export/print, row preferences, refresh errors and responsive light/dark layouts. Existing installation-owned runtime configuration remains untouched; schema/configuration versions stay at 1.

### v0.17.406 — Build 20261018

- Remove the separate Global Parameter editor and its Edit action. Information owns editable Parameter Name, Parameter Value, secure/reset handling and MAUI metadata through the existing revision-checked save. Preserve secured values until explicitly reset; retain Information/Tasks tabs and preferred Task routing. Keep New Parameter as a compact creation form.
- Match New SSH Key, TLS Certificate and PGP Key headers to Scripts with shared key/certificate icons. Remove grid Edit Key/Certificate and render Names as plain text in both initial and refreshed rows; edits remain in Information. Existing creation payloads and licensing are unchanged; no Experimental setting is added.
- Successful New Script/Date List forms close and refresh their caller without loading an editor, opening a native popup or acquiring an edit-duration checkout. Apply this to both editor preferences while retaining failed/uncertain creation guards and explicit Edit behavior.
- Update rendered regression coverage for consolidated Parameter/security saves, key creation headers/plain Names, and Script/Date List create-and-return behavior. Runtime appsettings.json and schema/configuration version 1 remain unchanged.

### v0.17.405 — Build 20261018

- Brand New/Edit Global Parameter headers with the shared resource header and parameter icon. Add Parameter, Properties and Tasks editor tabs, native identity/resource groups, editable MAUI properties, metadata audit and saved/unsaved feedback. Keep manual Save and the existing secure-value/reset behavior; no Auto Save or Experimental setting.
- Limit editor Actions to Delete Parameter, reusing the current preflight/confirmation and deletion paths with in-modal errors. Add Information/Tasks tabs to Parameter Information using the Script/Host grouping and assigned-Task styles.
- Resolve visible Task references through the existing parameter-macro scanner and REST task definitions; open related Tasks through the configured editor and preserve return context. Validate metadata before native Parameter updates.
- Add focused reference/validation and rendered secure-value, metadata, delete, tab, routing and responsive-layout coverage. Existing native Global Parameter transports, runtime appsettings.json and schema/configuration version 1 remain unchanged.

### v0.17.404 — Build 20261018

- Standardized Script Name and Date List Name labels and validation messages across New/Edit forms. New Script/Date List headers hide the grey kicker while Edit headers retain it. New Task/Host headers reuse the same header surface and the shared Task/Host icons.
- Date List headers now show the saved Description, when present, alongside the Date List ID.
- Built-in Scripts allow manual and Auto Save of MAUI properties while native configuration and audit fields remain read-only. The editor and Script Information modal share a MAUI-only metadata routine, without native checkout, Custom Scripts license requirements, native Undo or configuration writes.
- Added server coverage for built-in metadata validation, visibility/session checks and both entry points; expanded rendered tests for metadata saves, headers and Date List subtitles.

### v0.17.403 — Build 20261017

- Added the experimental MAUI Date List Editor, default off, with Script-style New and Edit forms, a calendar icon, Date List / Properties / Tasks tabs, metadata, per-user Auto Save and Saved / Unsaved status.
- Added current-year–2045 selectors, wildcard months, date-row Delete buttons and atomic text import of YYYY-MM-DD / YYYY-*-DD entries. Invalid dates report the import line; duplicate additions are skipped. Existing native entry annotations are preserved without a comment field.
- Centralized Date List creation and Information/editor updates through checked-out REST v0 payloads with concurrency checks, explicit acknowledgement and read-back verification. Extended edit-duration leases and admin checkout recovery to Date Lists. COM export/Undo/delete support remains available through the coordinator with existing dependency and Recycle Bin guardrails.
- Added Date List payload/concurrency tests and rendered Edge workflows covering import, metadata, Auto Save, checkout conflicts, Undo, export, deletion and responsive layouts.

### v0.17.402 — Build 20261017

- Align Host editor Undo availability with the Script editor: read the shared per-user/environment history store on editor load and post-save refresh. Preserve legitimate history through metadata-only saves and retain Local File System/permission exclusions and action preflight guardrails.
- Make built-in Script links honor the Script editor preference. When enabled, show a read-only MAUI Properties/Tasks viewer with Script hidden, Name/Description/MAUI fields read-only, and no Save/Auto Save/Actions. When disabled, retain native read-only viewing. Server-side classification and direct Save rejection protect built-ins; viewing requires no custom Source read, checkout, template seeding or Custom Scripts license.
- Extend server, routing and rendered regressions for Host history and built-in viewing, including preference on/off and read-only state isolation. Runtime appsettings.json and schema/configuration version 1 remain unchanged.

### v0.17.401 — Build 20261017

- Standardize Information and editor Properties section titles, including Host/Script metadata subheadings, at 15px through one shared size. Verify MOVEit/MAUI pills use the same font, line height, weight and padding across Information and Task/Host/Script Properties panels.
- Label New Script creation as Create Script / Creating…. Drive Script Undo availability from the existing per-user/environment Undo store on load and save: a newly created Script and metadata-only changes do not advertise native Undo; native edits with captured history enable it.
- Extend rendered typography, creation and Undo regressions plus server-side creation/history checks. Runtime appsettings.json, schema version 1 and configuration version 1 are unchanged.

### v0.17.400 — Build 20261017

- Separate compact New Script creation from the tabbed Edit Script modal. Match Host/Task header branding with a Script icon; add Script, Properties and assigned Tasks panels with native identity, MAUI fields/audit and preferred Task routing. Reduce New Description/source height to expose Import/Template buttons on desktop.
- Add independent per-user/environment Script Auto Save through existing preference storage. Share the authoritative Script save path, validate MAUI fields before native writes, avoid native Undo/audit updates for metadata-only saves, and retain explicit recovery for stale/uncertain writes.
- Wire editor Actions to existing resource Undo history, XML Export and Delete/Recycle Bin guardrails. Release editor checkout before Undo/Delete handoff, retain inline errors and require explicit acknowledgement for failed recovery copies. Remove New Template from Settings; templates are created from the Script editor.
- Extend preference, metadata/native-save and rendered editor regression coverage. No schema/configuration-version change or manual runtime appsettings edits.

### v0.17.399 — Build 20261017

- Begin the Resources CRUD boundary with a Script coordinator shared by Script creation, source/Information saves, native configuration/Undo and deletion adapters. Use the supplied v0 `Script` POST/PUT envelopes; preserve fresh server properties, group assignments and literal source text. Retain COM support for native XML operations.
- Add the opt-in Experimental MAUI Script editor: one New/Edit modal, PowerShell/VBScript fields, `.txt`/`.ps1` import, saved/unsaved footer state, Save/Saving…/Save and Close/discard handling. Script links and post-Undo opening honor the preference; nested dialogs retain their calling form. Built-in Scripts remain native/read-only.
- Extend shared checkout leases, revisions and explicit administrator recovery to Scripts. Block stale or uncertain writes; verify returned IDs and saved fields. Never retry a possibly completed creation or silently roll it back.
- Add environment-scoped PowerShell Script Templates below Notification Templates in General Settings, with the four supplied fragments, one-time seeding, create/edit/rename/delete and explicit overwrite-by-name. Reuse metadata storage without a schema/configuration-version change. No manual runtime configuration edits are required.
- Add focused Script payload/concurrency/template tests and rendered Edge workflows; retain Host/Task regressions. Other resource editors await their designs.

### v0.17.398 — Build 20261017

- Fix POP3 Task OAuth provider inference to use the same exact Microsoft 365 scope/endpoint contract as SMTP and Host editors. Preserve Custom Microsoft scopes, query strings and noncanonical endpoints instead of rebuilding them as Office 365 on an unrelated save. Remove the obsolete URL-only inference helper.
- Consolidate POP3/SMTP Task credential validation in the shared Task layer. Require explicit credentials for new overrides and authentication-mode changes in both protocols; preserve untouched same-mode secrets. Add read-model, credential-context and rendered form/save regressions.
- Refresh and organize the existing guides for the recent SMTP overrides, Not Matches conditions, local steps, Browse variants and checkout behavior. Correct stale Browse rejection guidance and test numbering; separate automated verification from live acceptance. No new Markdown files. Runtime appsettings.json, database schema 1 and configuration version 1 remain unchanged.

### v0.17.397 — Build 20261017

- Add captured `NOTMASK` (Not Matches) to Advanced Task filename and Custom Macro condition operators. Rename Matches mask to Matches, show readable flow summaries, and accept the native operator through shared condition composition/readback. Cover single and All/Any criteria, File Loop conditions and both dropdowns in regression checks.
- Source Special Filters remain as implemented; no additional gap in native UI filter/condition options has been established. Runtime appsettings.json, database schema 1 and configuration version 1 remain unchanged.

### v0.17.396 — Build 20261017

- Enable captured SMTP Task Destination credential overrides for Microsoft Office 365 and Custom OAuth, alongside Legacy credentials and Host-default inheritance. Traditional and Advanced editors share the native mail credential patch composer and common Task save/verification layer. Preserve untouched protected secrets; require explicit credentials for new/changed Hosts, newly enabled overrides, clones and authentication-mode changes.
- Reuse existing mail form styles; infer Microsoft 365 only from the exact captured scope/token endpoint so Custom settings round-trip unchanged. Add composition/readback, credential-transition, read-model and rendered UI regressions. Runtime appsettings.json, database schema 1 and configuration version 1 remain unchanged.

### v0.17.395 — Build 20261017

- Enable Local File System Source/Destination editing in Traditional and Advanced Tasks through one shared capability/composition profile. Reuse the existing local Browse contract and saved Host 0 defaults. Support applicable retry, rescan, download limits and destination timestamps; exclude network credentials and UNC-only CopyFile overrides. Enable administrator Edit Host 0 with the existing modal return/refresh behavior. Sync retains its separate folder policy.
- Extend existing Google Browse envelopes for OAuth and SharePoint envelopes for App Access as well as Certificate Access. Allow the blank default Site. Share proxy/authentication field composition across Task Browse, operational Browse and the Host library picker; derive allowed proxies from the Host editor catalog and preserve normal checkout cleanup/error handling. Respect the S3 alternative-endpoint enable flag.
- Keep stored protected secrets out of native Browse request bodies; MOVEit resolves them through hostid and null credential fields. Explicit unsaved Host picker secret replacements remain request-scoped. Add Local Task/Advanced composition, proxy/authentication request, cleanup and rendered editor regressions. These additional Browse variants reuse existing envelopes/parsers as requested and require live MOVEit acceptance; automated checks do not establish live protocol success.
- AS1/AS2/AS3 remain deferred. COM support, runtime appsettings.json, database schema 1 and configuration version 1 are unchanged; synchronize existing release guides/metadata without adding Markdown files.

### v0.17.394 — Build 20261017

- Complete S3 Host General settings using the supplied native fields: `IsNonAmazonEndPoint` is a Yes/No control and `S3AlternativeEndpoint` is a conditional text field. Save the flag as numeric 0/1 through the shared v0 Host lifecycle; require a URL when enabled, accept scheme-less text, retain the inactive URL and preserve credentials/unowned fields. Spreadsheet and clone creation are unchanged.
- Increase the Host Editor desktop height from 820px to 840px while retaining viewport limits and responsive navigation.
- Refresh the six existing Markdown guides for the v0.17.393 RC2 baseline and this update. Correct obsolete checkout, Local File System, native-editor and upgrade-restart guidance; document the recent editor features, cleanup journal and manual acceptance cases. No new Markdown files. Runtime appsettings.json, database schema 1 and configuration version 1 remain unchanged.

### v0.17.393 — Build 20261017

- Replace misleading draft guidance with consistent save-blocked recovery in Traditional, Advanced, Sync and Host editors. Unsaved values exist only in the open form. Confirmed Reload discards them and reads MOVEit; confirmed MOVEit-admin-only Override Checkout retains them and establishes a fresh checkout/revision without saving.
- Pause Auto Save after takeover until an explicit manual Save, preserve ordinary validation/concurrency checks and capture a fresh Task Undo baseline. Retire lost ownership without releasing/reacquiring another editor's checkout; exclude it from crash cleanup. Failed recovery reads release provisional leases. Uncertain write outcomes require Reload instead of takeover/replay.
- Both final push and PR Windows CI passed at `8908b2fed7e83034b5fbcfe5891db2eb96a835ff`: zero build warnings/errors, 1,429 xUnit cases, thirteen dependency-free Node suites, 7,905 Host rendered checks and 2,104 Task rendered/workflow checks. These checks used fixtures, not live MOVEit writes.

### v0.17.392 — Build 20261017

- Enable captured Local File System Host 0 General, Uploads, Limits and Advanced settings and Description through the shared Host REST lifecycle. Preserve its fixed name and create/clone/delete/export restrictions. Include edit-duration checkout, Host Auto Save and shared recovery; expose no uncaptured connection/credential properties.

### v0.17.390–v0.17.391 — Build 20261017

- Complete captured UNC Run-As/specified-credential modes and Drive choices, MOVEit Transfer password/certificate selection, FTP Account/control-only TLS, and POP3/SMTP Custom/Office 365 OAuth. Generate native mail OAuth fields in the shared layer; helper fields never reach MOVEit. SharePoint permits the native blank default Site; deprecated User Access authentication is excluded.
- Add captured FTP `implicit-ccc`. Selecting implicit sets Port 990; returning to none/explicit restores the previous non-implicit port during the open editor, or 21 when unknown. Preserve that memory across Save/readback. CCC remains an independent Yes/No dropdown. Resource lookups honor the owning editor lease.

### v0.17.388–v0.17.389 — Build 20261017

- Add default-off Configuration > Settings > General > Application Behavior > Checkout Objects for Duration of Edit. Task/Host editors acquire on open, re-establish after saves and release on Close/logout; default save-scoped behavior remains available. Add independent per-user Host Auto Save consistent with Tasks.
- Centralize session/editor/object leases, 30-second heartbeats, five-minute abandonment expiry and login-idle deadlines. Persist atomic ownership records and encrypted cleanup tokens under Data/ObjectEditLeases; retry failed cleanup without restoring interactive logins. Native schedule writes explicitly hand off/reacquire checkout. Harden interrupted writes, failed native handoffs, shutdown, logout, overlapping opens and nested editor ownership.

### v0.17.374–v0.17.387 — RC2 editor and upgrade refinements

- Correct New Host Resource UNC placement/help and Task Save/Saving feedback. Discover supported SSH/KEX choices through MOVEit; add saved ordered defaults in Configuration > Settings > SSH/KEX Algorithms, used by new SFTP Hosts and editor Reset without replacing existing saved selections.
- Add MOVEit-admin-only Edit Host beside Task Source/Destination Host selection, preserve the Task modal and refresh Host properties on return. Sort Browse Folders columns. Add Traditional/Advanced Run As Of for supported filename/folder date-time macros, with temporary changes/restoration, corrected native predicates and a refined date-time picker.
- Correct revision expiry/concurrent-change checks and new Source/Destination placeholder warnings. Move Send Email/Run Task Conditions into General, recipients into Message, and refresh Task Logs on entry in editors and Task Information. Refine notification/report/reset wording, Close labels, destructive discard buttons and message-body spacing.
- Complete FTP CCC and automatic/custom/script directory parsing controls. Upgrade completion now presents Return to MAUI when no restart is required or Restart MAUI using the existing controlled flow. Add optional upgrade.json RestartRequired, default false.

### v0.17.373 — Build 20261016

- Consolidate the 43 Markdown documents into README, Changelog, Installation, Security, Testing and Upgrade; retain the existing extensionless LICENSE unchanged. Remove 37 superseded architecture, design, phase and validation reports after preserving useful information below. Historical release entries remain historical; obsolete plans are not presented as current behavior.
- Refresh RC2 scope, experimental-editor preferences, REST/COM ownership, checkout/recovery guidance, installation and upgrade identity, testing instructions and security notes. Repair references to removed reports and retain the native integration asset's independently versioned revision.
- Record the documentation policy in README: no new Markdown files without Marc's approval. Runtime appsettings.json, application behavior, database schema 1 and configuration version 1 are unchanged. Synchronize release version metadata and correct the upgrade manifest's release description to RC2.

### v0.17.372 — Build 20261016

- Show Host Editor footer state using Task Editor wording and colors: No unsaved changes, Unsaved changes, save progress and a brief Saved confirmation. Keep footer errors visible in place of normal status; clear completion timers on close/reopen and use the existing dirty baseline without extra network calls.
- Validate the Host Editor shell identity on load and authoritative reload so a missing/wrong Host cannot be rendered or reported Saved. Require matching nonempty Task IDs in the shared JSON save lifecycle before editing, after patch composition and during readback. Incomplete pre-save definitions cannot reach PUT; incomplete post-save readback remains unverified without retry or COM fallback.
- Audit shared Host/Task ownership, checkout/revision handling, captured REST transports, UNC creation, information saves, readback, cleanup and uncertain-result recovery. Update stale transport comments and integration/validation documentation.
- Runtime appsettings.json and schemas remain unchanged; synchronize release metadata.

### v0.17.371 — Build 20261016

- Resolve missing Host type discriminators in Information and editor save reads through fresh REST inventory by unique exact ID. Preserve the original individual GET projection for revision and saved-field checks; accept flat checkout definitions only with an ID match and a type corroborated under checkout.
- Retain explicit identity/type conflicts, stale-draft protection, strict requested-field readback, checkout cleanup and one PUT with no retry or COM fallback.
- Preserve safe PUT/acknowledgement/readback/release stage diagnostics in partial-save messages and logs without exposing native response bodies or submitted credentials.
- Runtime appsettings.json and schemas remain unchanged; synchronize release metadata.

### v0.17.370 — Build 20261016

- Require Resource UNC when creating a UNC Share Host. Show the styled field only for Share, preserve it while switching types, and include its trimmed value in the typed REST v0 creation payload.
- Validate the required path in shared creation and reject visible duplicate UNC Share connections before POST; server validation remains authoritative for concurrent creation. Keep errors inside New Host and existing uncertain-outcome safeguards.
- Carry the archived UNC path when Recycle Bin restore creates a replacement Share shell. Spreadsheet creation, clone transport, COM support, runtime appsettings.json and schemas remain unchanged.

### v0.17.369 — Build 20261016

- Correct New Host verification's dependence on one immediate complete individual GET after a successful v0 POST. Reconcile unavailable/incomplete readback against fresh REST Host inventory by the returned ID, with a unique ID/name/type match and bounded read-only visibility checks.
- Keep contradictory identity/type/name results, ambiguous IDs, authorization failures and cancellation unverified. Never identify ownership by name, repeat POST, fall back to COM or delete a possibly created Host.
- Include safe acknowledgement/readback stage and failure reason in creation errors and controller logs. Distinguish an acknowledged created Host whose details could not be verified from an uncertain POST outcome; retain the known ID and existing modal reconciliation guard.
- Add all-type response-shape and actual REST-client HTTP regressions, synthetic NEWMOVEIT visibility, delayed inventory, conflict/ambiguity, permissions, cancellation and no-replay coverage. Spreadsheet creation/updates, cloning, Information/editor saves, COM methods, runtime appsettings.json and schemas are unchanged; release metadata remains synchronized.

### v0.17.368 — Build 20261016

- Move Host Information Name/Description updates for all ten supported profiles into the existing Host v0 REST save lifecycle. One shared checkout/typed PUT/acknowledgement/readback/release implementation serves editors and Information; unverified types retain their explicit COM path without fallback after an uncertain REST write.
- Check the Information field revision, identity and edit permission under checkout. Preserve unrelated fresh settings, groups and unknown fields; use established null-preserve semantics for unsubmitted secrets. Permit information-only edits on incomplete Hosts; retain full editor JSON/native credential revision protection and the immutable Local File System Host guard.
- Match Task Information recovery: validate metadata before the native write, distinguish uncertain saves from verified-native/local-audit failures, keep errors in the modal and require reopen before another uncertain save. Accept Description transport newline normalization.
- Add all-type shared mutation, stale/concurrent data, permission/identity, cancellation/cleanup/no-fallback, controller/native-local outcome and modal recovery regressions. Spreadsheet creation/updates, cloning, COM support methods, runtime appsettings.json and schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.367 — Build 20261016

- Move New Host shell creation only to POST /api/v0/hosts through the shared Host CRUD boundary, using all ten supplied typed payloads and exact protocol defaults. Reuse the shared SSH algorithm catalog; preserve empty IDs, credential null/empty distinctions and empty Resource Groups.
- Verify the captured success/ID/errorcode acknowledgement and saved identity/name/type through REST. Preserve a known ID after uncertain outcomes; never repeat creation, guess ownership by name, delete speculatively or fall back automatically to COM. Keep New Host errors inside the modal and block repeated Create after uncertainty until inspection/reopen; refresh inventory.
- Leave spreadsheet configured creation/updates, Clone Host and all COM support methods unchanged. Host Editor supported-profile writes remain v0 REST; native revision/undo reads and common Information/unsupported-profile saves retain COM.
- Add captured seed/actual HTTP, readback/rejection/cancellation/no-replay, retained configured/clone transport, cache and preferred-editor/modal recovery regressions. Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.366 — Build 20261016

- Move common Task enable/disable and Name/Description/Operator Notes updates into one shared REST mutation implementation: Traditional stays on v1; Advanced and Sync reuse the captured v0 Task PUT. Keep all COM methods available; no automatic fallback or retry follows an uncertain write.
- Reuse save-scoped checkout, authoritative GET, detached field patch, type-selected PUT, readback verification and independent checkout release. Preserve unrelated definition fields, opaque schedules, parameters and Host-step settings. Task Information checks its displayed field revision under checkout; full editor JSON/native credential revision checks remain intact.
- Validate local metadata before committing Task Information, report uncertain saves and post-save local failures explicitly, and keep errors/recovery inside the modal. Invalidate inventory after unconfirmed information or bulk-state mutations, including uncertainty on the first item.
- Add shared coordinator/transport, field preservation, stale edit, no-fallback, checkout/permission, acknowledgement/readback/release, inventory refresh, controller outcome and modal recovery regressions. Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.365 — Build 20261016

- Create empty Advanced and Synchronization Tasks through the supplied POST /api/v0/tasks contract in the shared Task CRUD layer. Preserve the captured disabled COS/Sy seeds, empty ID, NextEID=11 and Sync SameSecs=-1; Traditional creation remains v1.
- Verify the captured acknowledgement and saved identity/type/disabled/empty state through REST readback. Preserve a known Task ID after uncertain outcomes; never repeat creation, guess ownership by name, delete a possibly created Task or fall back automatically to COM.
- Keep errors inside New Task, refresh inventory after uncertainty and block repeated Create until inspection/reopen. Existing license/capacity, duplicate-name, audit and editor-preference routing remain in place.
- Retain the COM client and creation strategy for explicit reuse. Native XML cloning, Traditional-to-Advanced conversion, restore and Host-export carrier creation continue through COM; no COM support methods are removed.
- Add captured seed/HTTP envelope, result-validation, rejection/cancellation/no-replay and New Task modal routing/recovery regressions. Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.364 — Build 20261016

- Fix Records Back links to name their source page: Tasks, Hosts, Scripts, Date Lists and Task Runs. Preserve existing return URLs, grid state, filters, selected context and IIS application paths; add runtime navigation coverage for the related grids and fallback routes.
- Correct Advanced Task edit-format metadata to the existing captured v0 JSON save transport while retaining COM shell/native-copy creation. Extend the common checked-out save regression to Advanced Tasks; save behavior and concurrent-edit protections remain unchanged.
- Remove retired Change Host/placeholder action handlers, unused Host save locals and the superseded standalone Host-import smoke fixture; the canonical host-import-ui-parity regression remains in CI. Refresh current Task/Host architecture, editor ownership/removal guidance and the default Task-editor setting description; distinguish dated implementation notes from current support.
- Validation scope covers shared CRUD ownership, checkout/revision/save outcomes, captured Host fields, Task composition/conversion/Sync operations, editor routing/modal feedback and rendered layouts. Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.363 — Build 20261016

- Complete Traditional-to-Advanced Each File Next Action conversion using the supplied native capture: append conditional notifications at the end of the file loop, comparing [ErrorCodeFile] numerically to zero (NEQ for success, NGT for failure).
- Omit Each File No Transfer selections, including No Transfer-only actions; mixed selections retain their success/failure behavior. Combined outcomes execute once per matching file. Task-level conditions remain outside the loop and retain their own No Transfers handling.
- Reuse the common conversion planner for JSON verification and credential-preserving native creation. Preserve action order within branches and emit branches in first-use order, matching both supplied captures; no alternate CRUD path is introduced.
- Add captured-pattern, outcome-combination, mixed-scope/order and shared checkout/native creation/readback regressions. Exercise the real roving keyboard selection path in the existing Advanced browser regression so it cannot race a prior Save status redraw by focusing an unselected node. Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.362 — Build 20261015

- Add shared Traditional-to-Advanced conversion and wire Traditional editor Actions → Create Advanced Task. Save pending edits, create a unique `<source> Advanced` disabled copy and open it in the shared Advanced modal.
- One conversion planner orders Sources, per-file/process-destination work and Destinations in a file loop, once-only Processes after the loop, and TaskStatus notification branches. Preserve schedules, parameters, native Host/Process settings, resource groups and opaque XML/credential values through existing native Task creation.
- Protect the coherent source snapshot with shared checkout/release, enforce Advanced licensing/capacity, verify created identity/disabled state/topology/native configuration, and prevent retries after uncertain creation. Source Task remains unchanged by conversion.
- Task-level Next Action outcome combinations follow the supplied native conversion capture; unconditional Each File actions are placed inside the loop. Conditional Each File actions reject before creation until their native conversion pattern is available.
- Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.361 — Build 20261015

- Fix disabled Tasks being omitted from Success, Failed and No Transfers views after manual runs: preserve the latest recognized run outcome independently of the Enabled flag. Continue prioritizing live Running state and use Ready/Disabled only when no recognized outcome is available.
- Reuse the shared TaskRunStatus normalizer already used by reports and Dashboard so native result labels and their existing aliases produce the same Task grid status.
- Add service-to-controller-filter regressions for enabled/disabled Traditional, Advanced and Sync Tasks, latest-run selection, zero-file successful runs, running precedence, missing/unknown history, and forced-refresh transitions between all three outcome views. Enabled values and CRUD behavior are unchanged.
- Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.360 — Build 20261015

- Initialize new Source File(s) to an actual `*.*` value in the common Advanced/Traditional/Sync form model, rather than showing a wildcard placeholder while saving an empty FileMask.
- Replace the wildcard placeholder with descriptive guidance so an existing blank filespec appears blank. Report missing File(s) inside the editor and reject explicitly blank Source FileMask patches in the shared composer; omitted fields retain partial-edit semantics and preserve authoritative values.
- Verify untouched Source/Destination defaults in Advanced and Traditional production forms, including original filenames, false/zero values, and Host inheritance flags. Cover explicit wildcard, filename and macro edits, missing-filespec recovery, and shared validation for new/existing steps.
- Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.359 — Build 20261015

- Fix Advanced Task saves with newly added Source or Destination steps. Allocated IDs no longer cause empty step shells to be treated as existing server steps before Host fields are applied.
- Route new steps through the shared Task composer creation path, including Host defaults and new-credential validation. Preserve the existing authoritative-template path for edits and clones, nested flow topology, checkout and API v0 persistence/readback.
- Add captured-pattern regressions for new Source/Destination steps at the root and inside loops, and a full checkout/save/readback/release followed by editing both steps in the supplied nested Advanced flow.
- Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.358 — Build 20261015

- Move Date List Information Entries and Script Information Language directly below Description, preserving read-only controls and value bindings.
- Fix Information footers reverting from Close to Cancel at runtime: remove per-tab, Task reset and Global Parameter metadata label overrides. Keep the existing Close markup, save visibility and close/discard behavior.
- Remove the MOVEit badge from the shared Host configuration property card, covering General, Authentication and every other configuration section. Retain the separate Properties sheet identity/source layout.
- Exercise production Information tab/reset/metadata callbacks so footer labels are checked after runtime transitions, alongside rendered field order and Host section badge checks.
- Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.357 — Build 20261015

- Correct editor Properties and object Information identity headers to the supplied reference: Type and identifier side by side, labels above values, both columns left-aligned. Retain the top-right source badge.
- Render other read-only Information properties and Created/Modified audit values in read-only textboxes. Keep multiline Global Parameter values in a read-only textarea, preserve secured-value suppression and bind values through the form-control value property.
- Use the same audit textbox presentation in shared Task editor Properties. Preserve existing identifiers, data hooks, editable controls, permissions, saves and routing.
- Rename Information modal footer Cancel buttons to Close, preserving existing close/discard handlers. Use the grid row selection centering wrapper for the header checkbox and verify row/header centers in all themes and widths.
- Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.356 — Build 20261015

- Remove the remaining MOVEit badge from shared Task Advanced settings.
- Standardize Host/Task Properties and all object Information identities as two left-aligned rows with the source badge at the top-right; remove duplicate Host Type/ID inputs. Preserve actual identifiers, Script language, Date List entry counts and resource names; Reports remain MAUI-owned and Global Parameters use their name.
- Keep MOVEit/MAUI pills at the shared 10px size, including Task editors; remove source pills from Information related-Tasks sheets.
- Render grid row-menu dots with centered SVG geometry and normalize selection checkbox margins, retaining the existing buttons, accessible labels and handlers.
- Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release metadata remains synchronized.

### v0.17.355 — Build 20261015

- Remove the redundant Advanced title and Task Logs heading/source badge from the shared Traditional, Synchronization and Advanced Task editor panels.
- Align Host, Script, Date List and Key/Certificate Information related-Tasks cards on Assigned Tasks, with consistent object-specific descriptions and empty states. Preserve Task Information and the separate related Hosts section.
- Global Parameter Information has no related-Tasks tab or reference-list contract; this presentation cleanup does not introduce one.
- Runtime appsettings.json and configuration/database schemas remain unchanged; template/custom release versions remain synchronized.

### v0.17.354 — Build 20261015

- Add the shared Settings gear to each Host configuration navigation tab, rename the Host Editor footer Cancel control to Close, and move Test Host into Actions using its existing test callback.
- Share Host-style property containers across Task editor Properties/Advanced and grid Information/related-object panels for Tasks, Hosts and Resources. Group Report Information into Properties, Delivery/Schedule and Metadata cards.
- Add the Host navigation accent to active Task sections and child items, including Advanced custom/action steps. Keep their parent Steps section active while inspecting them.
- Drive shared Actions/Add disclosure arrows from aria-expanded; preserve the dynamically rebuilt Traditional Add Step arrow, reset arrows when menus close, and reflect Task rail expansion.
- Validate real shared SVG icons, Information cards and menu states in rendered Edge checks. Runtime appsettings.json and schema versions remain unchanged; template/custom release versions remain synchronized.

### v0.17.353 — Build 20261015

- Match all Advanced step property titles to the Run Task heading and center the Start/End flow pills.
- Add Advanced Actions → Save Task Flow to download a full-resolution PNG of the current draft, including Start/End, nested/collapsed branches, summaries and disabled states. Export leaves editor state intact, performs no Task write and reports image creation failures inside the modal.
- Move Host Test Host immediately before Actions with 50px spacing. Present Host configuration sections in a Traditional-style left navigation rail with a scrollable properties area and compact navigation on narrow windows.
- Center shared calendar, pagination and close icons using font-independent geometry; align other single-symbol controls consistently.
- Runtime appsettings.json and configuration/database schema versions remain unchanged; template/custom release metadata remains synchronized.

### v0.17.352 — Build 20261015

- Remove Change Host from Advanced and Traditional Source/Destination step menus; retain the existing Host selectors in the properties panels and the existing Sync menu.
- Replace the Advanced Run Task fallback question mark with a running-person stroke icon and Update Original's curved arrow with a document-and-pencil icon, using the same monochrome accent and size as the flow symbols.
- Remove the shared read-only Action Type field for Run Task/Send Email across Task editors; expand SMTP Host across the available form row. Keep action headings and existing save payloads.
- Runtime appsettings.json and configuration/database schema versions remain unchanged; template/custom release metadata remains synchronized.

### v0.17.351 — Build 20261015

- Use the Traditional Task navigation width as the shared standard. Align Auto Save, Task Enabled and Actions with matching text-before-toggle styling and 50px gaps; keep footer status/progress/errors on the left. Wrap the controls as one group on narrow modals.
- Remove Advanced Selected Step captions and rename Task editor modal footer Cancel buttons to Close, retaining their existing discard/close behavior.
- Verify Advanced ordered topology and changed values against the fresh pre-save definition. Accept text line-ending, Boolean/numeric, disabled/inherited-default and shared parameter read-shape normalization; do not compare unchanged native properties as edited fields. Retain mismatch checks for submitted values, IDs, steps, criteria and branches.
- Identify the failing readback stage and safe field location in genuine partial-save errors. Do not expose values, retry writes or bypass checkout/revision protections.
- Add captured readback and shared save regression tests and rendered control/layout checks for all Task types. Runtime appsettings.json and configuration/database schema versions remain unchanged; template/custom release metadata remains synchronized.

### v0.17.350 — Build 20261015

- Keep Advanced step selection, property editing and confirmed moves available after save failures. Only the in-flight save temporarily locks the workspace; inspectors recover after both successful refreshes and rejected saves.
- Keep Save/Auto Save blocked for uncertain or conflicting outcomes. Add an explicit Reload Task action in the shared Task modal, with discard confirmation and draft preservation if the authoritative read fails. Allow confirmed draft discard after a failed Auto Save. Subsequent saves use the reloaded revision.
- Align REST Task/Host checkout cleanup with COM: native error 4015 means already unlocked. Accept only an explicit structured native code; retain authentication, ownership and transport failures, without retrying writes or forcing checkout.
- Add REST cleanup response and rendered Advanced save/recovery regressions. Runtime appsettings.json and configuration/database schema versions remain unchanged; template/custom release metadata remains synchronized.

### v0.17.349 — Build 20261015

- Apply the Advanced Task type and modal dimensions before showing its loading state. Pass the known type through the shared launcher/return context; untyped callers wait for the authoritative shell. Closing during loading keeps the editor closed.
- Expand the Advanced navigation rail to 300px. Keep the adjustable flow/property divider and use full-width Flow/Properties views where three columns would compress the forms.
- Replace the Add Step toolbar with a MAUI-styled step menu at each (+) insertion point. Insert directly at the chosen root, loop or branch position; retain existing form, Undo and shared CRUD behavior.
- Show the live If/Else If condition in the properties heading. Put each criterion's item, comparison, value and removal control on one row, with consistent separator/button spacing.
- Move Advanced Run Task Wait below Task to Run. Use Subject and SMTP Host in Advanced Email summaries, without Traditional execution-result wording.
- Runtime appsettings.json and configuration/database schema versions remain unchanged; template/custom release metadata remains synchronized.

### v0.17.348 — Build 20261015

- Widen the Advanced Task modal with the window, give the navigation rail enough room and add a draggable, keyboard-accessible divider between flow and properties.
- Put step and branch actions in each flow row's ellipsis menu. Remove the global step action toolbar and Move Into selector; retain confirmed drag/drop at explicit insertion points.
- Match native If/Else If/Otherwise presentation at one visual level. Selecting If opens its criteria directly; branch menus add alternate/default branches, enable/disable children or delete a branch while preserving native identities and order.
- Use Selected Step consistently in Advanced property headers and theme-aware green Start/red End pills. Traditional and Sync presentation and the common Task CRUD lifecycle remain unchanged.
- Add the supplied Else If/Otherwise capture to shared composition tests and extend rendered layout, menu, divider and branch-save checks. Runtime appsettings.json and configuration/database schema versions remain unchanged.

### v0.17.347 — Build 20261015

- Enable the basic Advanced Task Editor through the existing experimental MAUI Task Editor setting. Reuse the Task modal and Source/Destination/Process, Email/Run Task, Schedules, Properties, Advanced, logs, parameters, exceptions and Task actions.
- Add an ordered flow tree with nested File Loops and If/Else If/Otherwise branches, insertion points, keyboard navigation, disable/clone/delete, confirmed moves and drag/drop at explicit insertion points. Keep forms alongside the tree on desktop and switch Flow/Properties on narrow screens.
- Add captured Match All/Any criteria, Run Task Wait, Update Original Delete/Rename/overwrite and Comment fields. Hide Traditional Next Action execution conditions for Advanced Email/Run Task steps.
- Extend the common Task JSON persistence boundary to COS v0 saves, sharing checkout/revision/Undo/readback and uncertain-outcome handling. Preserve unknown native fields, allocate nested IDs using NextEID and creation order, retain IDs on move and rebuild branch bookkeeping.
- Validate the supplied baseline/insert/move/clone captures and reuse existing Host capability validation. Keep Traditional-to-Advanced conversion disabled pending its separate operation pattern.
- Ignore queued close events from a previous modal session after the same dialog has reopened, so they cannot clear a new footer error.
- Configuration/database schema versions remain 1. Runtime appsettings.json is unchanged; template/custom setting descriptions now include all three Task types.

### v0.17.346 — Build 20261015

- Route error toasts into the active modal: editor footer errors and consistent inline banners in other dialogs, including nested notification-template actions. Clear routed errors when reopening dialogs.
- Honor the configured MAUI/MOVEit editor preference in related Task/Host links from Information panels, Scripts, Date Lists, Keys/Certificates, Records and Upcoming Runs.
- Restore the originating editor or Information panel and selected tab after closing a related MAUI editor. Save pending editor changes before leaving; guard unsaved Information edits. Return context contains object identity and selection only.
- Host Editor tabs now read Host, Properties, Tasks, with Host first. Remove duplicate Destination and Secure properties, align the remaining properties, place errors in the footer and retain the selected tab after saving.
- Add modal feedback, nested-dialog, return-navigation and dirty-guard regression coverage. Runtime appsettings.json and configuration/database schema versions remain unchanged.

### v0.17.345 — Build 20261014

- Consolidate Traditional and Sync JSON saves behind `ITaskDefinitionSaveService`; route compatibility updates and native schedules through the same common implementations. Creation strategies now own creation only.
- Use one save-scoped checkout lifecycle for Task and Host REST writes: acquire without force, read/validate, patch, write, verify and release with an independent cleanup token. Do not write or release after a rejected checkout. Report uncertain cleanup and writes without retries.
- Add opaque editor revisions and reject stale Task/Host drafts under checkout, including native credential changes hidden by REST suppression. Protect native schedule follow-up writes and carry the verified revision through queued Task saves. Reopening loads a new baseline; tabs do not hold long-lived locks.
- Apply revision checks to Script/Date List information edits and native Global Parameter edits; consolidate COM checkout cleanup reporting across object types. Keep the existing Clone Host grid row menu and shared clone workflow.
- Runtime appsettings.json is unchanged. Windows CI and live MOVEit checkout interoperability are separate validation gates.

### v0.17.344 — Build 20261014

- Wire Task-editor Clone Task to the existing grid clone dialog and shared Task portability service. Pending editor changes save first; the cloned Task follows the configured MAUI/MOVEit editor preference and existing disabled-creation/license policies.
- Add Clone Host to the Host grid and editor through the shared Host coordinator and existing COM creation lifecycle. Compose a detached native copy with a new ID/name, preserving encrypted credentials, references and unknown configuration; enforce identity, source permissions, duplicate names, license capacity and readback. Copy MAUI metadata and surface incomplete creation/verification without automatic retry.
- Wire Save Notification Template for Task email Next Actions and SMTP destination steps to the existing Tasks modal and template save service. Support create, overwrite, cancellation, errors and explicit Text/HTML format. Keep template controls accessible for plain-text messages and preserve format when loading a template.
- Defer Create Advanced Task until its editor patterns are available; keep the action disabled in Traditional and hidden in Sync.
- Correct bulk Enable/Disable completion text: already-matching Tasks count as completed and are described as already enabled/disabled, never skipped. Retain changed-only modification audit and separate failures. Host assigned-Task outcomes use the same wording.
- Add larger-batch state accounting, shared Host clone composition/dispatch, reusable template and rendered editor action regressions. Native calls are mocked; no timing root cause is claimed for the reported bulk message.
- Configuration/database schema versions remain 1; runtime `appsettings.json` is unchanged.

### v0.17.343 — Build 20261014

- Sync Host Overrides: hide the empty UNC Share Transfer tab and all Download Limits tabs. Traditional Task override controls remain available.
- Task Calendar: Open Task Editor now uses the shared editor selector, honoring the MAUI/MOVEit preference and resolving the Task type. Removed the separate native-only Calendar launcher and its unused endpoints.
- Task Editor: Delete Task now closes through the existing unsaved-change guard and reuses the Tasks page's dependency preflight, delete confirmation, permission checks, and Recycle Bin recovery workflow. Delete is unavailable while saving, for read-only Tasks, or after an uncertain save outcome.
- Added Calendar preference/type/PathBase regressions and rendered editor coverage for tab visibility and Delete cancellation/discard/read-only behavior.
- Save-path review: Task saves also refresh the full Tasks cache and rebuild the editor's Task XML/reference data after verified persistence. No live timing measurements were available; this release retains the existing verification and save lifecycle.
- Remaining Task-editor placeholders: Clone Task, Create Advanced Task (Traditional only), and Save Notification Template in Next Actions/SMTP. Host-editor Undo, Export and Delete are connected to the existing Host workflows.
- Configuration/database schema versions remain 1; runtime `appsettings.json` is unchanged.

### v0.17.342 — Complete MAUI Sync Task Editor (Build 20261014)

- Add Swap Sync Folders, Clear Sync Listings and View Sync Preview to the Sync Actions menu using the supplied native contracts. Remove the native-editor handoff for these actions.
- Shared Task CRUD owns folder swaps, native comparison GET/DELETE, Preview OK saves and confirmed policy-triggered listing resets. Swaps retain folder IDs and native settings while applying the A/B direction/deletion roles in the captured order; they do not clear listings.
- Listing clear verifies the Task is disabled, requires the native id=-1/status=success/errorcode=0 acknowledgement, verifies readback and blocks automatic retries after uncertain outcomes. Policy changes save disabled and then clear listings through this same implementation.
- Add a MAUI Sync Preview modal with Folder A/B paths, sizes, timestamps and native actions, refresh, no-action filtering, visible errors, cancellation, and a 1,000-row display limit with the known response total.
- Add all four Initial Run choices, including name-only matching (SameSecs=-3), name/size matching (-1), timestamp tolerance and copy-all (-2). The preview button follows native SavedDirs state; timespans convert to signed 32-bit seconds through the shared save layer.
- Allow existing Folder A/B Host changes across the eight requested types, retaining folder identity and removing old protocol credentials. Local browsing preserves server drive/UNC paths without checking out synthetic Host 0.
- Correct menu CSS overriding hidden controls: Create Advanced Task and Transfer Exceptions are hidden for Sync and retain their Traditional behavior.
- Add native-contract, uncertainty, parser, menu and rendered modal regressions. Automated native transports are mocked; live MOVEit acceptance testing remains required.
- Configuration/database schema versions remain 1; installation-owned appsettings.json is unchanged.

### v0.17.341 — MAUI Synchronization Task Editor (Build 20261014)

- Route Synchronization Tasks and new Sync shells to the MAUI Task modal when the MAUI Task Editor is enabled; Advanced Tasks retain the native editor.
- Reuse the Traditional editor runtime for Folder A/B, Host overrides, schedules, Next Actions, task parameters, properties, metadata, audit, Undo and Advanced settings.
- Both Sync folders remain native Source entries. Support Local Folder, UNC Share, FTP/FTPS, SFTP, S3, Azure Blob, SharePoint and Google Storage; exclude mail, ASx and MOVEit Transfer Hosts.
- Centralize fresh-read/full-definition PUT/readback in the Tasks layer. Select the captured v0 envelope for Sync and v1 for Traditional; verify acknowledgement, task identity, Sync roles, deletion/comparison flags, timestamps and SameSecs.
- Preserve native fields, IDs, unknown settings and unchanged secret values. Reject inappropriate Traditional fields, reordered/removed Sync folders and cross-Host mutations.
- Sync direction/deletion/comparison changes, swaps, listing resets, preview and initial-run options remain available through the native Sync editor until their reset/preview operation contracts are verified. Local folder paths can be entered; native local browsing remains gated.
- Add captured/mock contract regressions and production-modal Edge checks at four widths in both themes. No live MOVEit integration is claimed.
- Configuration and database schema versions remain 1. Runtime appsettings.json is unchanged.

### v0.17.340 — Host Editor and shared CRUD validation (Build 20261014)

- All verified REST Host profiles, including UNC Share and MOVEit Transfer, use one checkout/compose/PUT/release/readback lifecycle. Failed or cancelled PUT acknowledgements are treated as uncertain results with no retry; initial reads, checkouts and saved readback verify the requested Host ID and type.
- Host Editor saves require a matching successful acknowledgement. Lost, malformed and indeterminate responses block another save until reload; explicit pre-write rejection remains correctable and known partial-save messages remain visible. Form interaction and loading another Host are blocked while saving.
- Picker events are registered once rather than on every tab switch. Removed unreachable Google-specific presentation branches now owned by the shared captured profile catalog.
- Regression coverage includes uncertain outcomes, identity mismatches, checkout cleanup, metadata-only no-op saves, one-request picker refreshes and in-flight UI behavior. Installation-owned appsettings.json and schema/configuration versions remain unchanged.

### v0.17.339 — Shared Host state-cache settings (Build 20261014)

- Host Advanced sections now use the shared state-cache contract and a consistent checkbox/radio form: inherit defaults, Always (-1), Until end of task run (0), or a positive duration in Minutes/Hours. Shared composition validates values, omits inapplicable unit/time fields, and verifies effective readback through the existing checkout/Undo/save lifecycle.
- Google Storage no longer displays the unsupported TLS control or Security tab; its stored native Secure value is preserved.
- State-cache controls remain read-only for accounts without configuration permission and the default Local File System Host. Installation-owned appsettings.json and schema/configuration versions remain unchanged.

### v0.17.338 — SharePoint authentication and Task import consistency (Build 20261014)

- SharePoint Host and Traditional Task override editors offer App Access (Client ID, Secret, Realm) and Certificate Access only. The shared Task write profile rejects username/password patches and unsupported authentication overrides while preserving unsubmitted native properties.
- Task CSV/XLSX Import uses the same optional 60-character prefix textbox as Hosts Import. Prefix text is prepended literally, without an automatic underscore; changing it invalidates prior analysis.
- Installation-owned appsettings.json and database/configuration versions are unchanged.

### v0.17.337 — SharePoint/FTP captures and SFTP algorithm selectors (Build 20261014)

- SharePoint supports captured Client Secret Access and Certificate Access settings, site/library references, HTTP/HTTPS proxies and advanced properties through HostCapturedProfiles and the common verified save lifecycle. ClientSecret and ProxyPassword use blank/null-to-preserve and exact-string replacement semantics.
- FTP/FTPS uses the same captured save policy for passwords, the supplied none/on/tls-c-ccc/implicit TLS modes, SOCKS5 proxies, client certificates, directory parsing and advanced properties. Superseded FTP save/presentation catalogs were removed; native property casing is retained and duplicate secret variants are suppressed.
- Set Certificate uses the captured v0 sslcerts inventory. Certificate thumbprint/store/subject references are staged and validated together against the available inventory. Clear Certificate uses the MAUI destructive action style.
- SharePoint Browse presents document libraries as a single-level MAUI picker with Refresh/OK/Cancel. Selected names and GUIDs are staged together. Browse uses draft site/certificate settings and shares captured request composition with the existing Task/Quick Transfer Host browser. The supplied browse contract covers Certificate Access without a proxy; other modes can enter library name/GUID manually.
- SFTP encryption and key exchange use checkbox lists with ordering and Reset controls styled for MAUI. Provided default selections and native comma-separated order are retained; unknown current algorithms are preserved until explicitly reset.
- Protected lookups route through the common Host facade/coordinator and honor live edit permissions, no-store responses and POST antiforgery. Late/cancelled picker responses cannot stage a selection for another Host.
- Backend/browser regressions cover native saves, secret preservation, reference validation, draft browsing, TLS modes and algorithm ordering. Real Edge layout/interaction regression includes the new controls.
- No installation settings added; appsettings.json is unchanged. Template/custom changes update only Ui.Version; database/configuration versions remain 1.

### v0.17.336 — Host Editor form alignment and dropdown consistency (Build 20261014)

- Host form captions and controls align across columns, including wrapped labels, secret replacement help text and SSH key actions. Field bodies remain top-aligned; read-only values use the same single-line height as inputs, and controls inherit the MAUI font.
- Boolean configuration fields always use Yes/No dropdowns, including Google TLS and default state-cache flags. Read-only flags and choice controls remain disabled and cannot enter save patches; an unexposed flag retains an empty display rather than being presented as No.
- Read-only authentication/proxy modes retain their current values for conditional field visibility. SSH Clear Host Key uses the established destructive-action button. Host loading, read-only and muted-text styling uses existing theme tokens.
- Added a real Edge/Chromium layout regression using the production Host dialog, CSS and complete editor script with HostEditorService-generated fixtures for all 11 mapped Host types. It checks control alignment, dimensions, overflow, Boolean permissions and save behavior at four widths in light/dark themes, and records screenshots in CI artifacts.
- Common Host CRUD save contracts are unchanged. No new application settings; appsettings.json is unchanged. Template/custom configuration changes update only Ui.Version; schema/configuration versions remain 1.

### v0.17.335 — Captured Host profiles, credentials and SFTP keys (Build 20261014)

- Shared HostCapturedProfiles catalog owns captured POP3, SMTP, SFTP, S3, Azure Blob and Google Storage field schemas and editor presentation metadata. Their saves use one shared checkout/patch/PUT/release/readback lifecycle.
- Mail hosts support basic credentials, OAuth settings, TLS flags, timeouts and alternate Hosts. Cloud hosts support captured proxy/advanced properties; Google supports HMAC/OAuth editing and exact multiline private-key replacement.
- Secrets use native null-to-preserve / string-to-replace semantics. Stored credentials never appear in the editor response or get forwarded as new plaintext; omitted/blank replacement fields preserve storage. PUT acknowledgement is authoritative for suppressed secrets, with nonsecret readback verification.
- SFTP supports a friendly-name SSH client-key selector, clearing a key with None, host-key retrieval/clear through the shared service, proxy settings, encryption/key-exchange lists and captured compression choices. Fingerprint retrieval uses the supplied GET query contract and excludes passwords from URLs; old responses cannot change a different opened Host.
- Unsupported custom state-cache children and unrelated native settings remain intact. Existing SharePoint certificate import/test support is retained; no new SharePoint authentication/save pattern was supplied.
- Added shared profile/transport/shell secrecy regressions and a portable Node editor state/fingerprint regression, included in CI. Existing Host Import schemas and templates remain unchanged.
- No new application settings; appsettings.json remains unchanged. Template/custom files update only Ui.Version. Schema/configuration versions remain 1.

### v0.17.334 — Shared Host CRUD and save reliability (Build 20261014)

- Host Editor saves are a thin adapter over AutomationHostService and the common HostCrudCoordinator. The verified protocol patch/save policy and native JSON interpretation now belong to the shared Host layer.
- Host inventory reads, shell/configured creation, spreadsheet updates, common details, snapshot/restore and deletion all delegate through the same coordinator. REST v0 editor writes and COM XML writes keep their captured contracts; no transport fallback or automatic retry is introduced.
- Configured creation verifies submitted connection fields, encrypted credentials and description; updates verify changed credentials and description as well as existing connection checks. COM release failures after successful saves become explicit partial-save outcomes and caches invalidate after uncertain mutations.
- Lost/malformed Host import responses now show Needs Attention and refresh the inventory. FTPS cannot explicitly select plaintext security modes.
- Added mock-transport save regressions for all seven existing writable editor profiles, rename/clear behavior, preserved credentials/native settings, permission/no-op checks, partial saves, verifier checks and lost-response browser flows.
- Application/schema configuration remains version 1. appsettings.json is unchanged; template/custom release metadata advances only.
- Live MOVEit 2025.1 COM/REST acceptance remains required; automated transport mocks do not establish native interoperability.

### v0.17.333 — Host Import Phase 2 Step 3: SharePoint (Build 20261013)

- Extended the **existing authoritative Host CSV/XLSX definition profile** to create and update SharePoint Hosts (`SharePoint` native type) using the certificate-based application authentication mode `UsingAppAuthentication=2` already present in the MOVEit COM Host skeleton and supported by MAUI's Host Connection Test.
- Added canonical columns `TENANT_NAME`, `SITE`, `SITE_URL_PREFIX`, `CLIENT_ID`, `TENANT_ID`, `DOCUMENT_LIBRARY`, `DOCUMENT_LIBRARY_GUID`, `CERT_STORE`, `CERT_SUBJECT`, and `CERT_SHA1`. Native `SiteUrlPrefix` defaults to `/sites/` when omitted. Document library GUID is optional.
- Creation requires Tenant Name, Site, Client ID, Tenant ID, Document Library, Certificate Store, Certificate Subject and a 40-character hexadecimal SHA1 thumbprint. The certificate is referenced by Windows store/subject/thumbprint; the importer never imports a private key or accepts SharePoint plaintext passwords.
- No speculative username/password, client secret, OAuth mode change, or arbitrary SharePoint login configuration. Protocol-only cloud/FTP/SFTP/UNC columns are rejected if populated; `IGNORE_CERT_PROBS=Y` is forbidden for SharePoint.
- Existing-host updates use sparse, non-destructive native XML composition. Blank cells preserve saved certificate and connection settings, existing Host IDs, Resource Groups, and unrelated native fields. Updates refuse saved SharePoint Hosts with an unverified application authentication mode (not 2).
- Added native COM readback verification for modified Tenant/Site/Document Library/Certificate fields. Retains existing Undo, authorization/license policy, metadata, preview, progress and optional SharePoint connection test behavior.
- Updated Host CSV/XLSX templates in `Templates/HostImport` alongside TaskImport, retaining the first worksheet as the only import source and adding a dedicated SharePoint example and field guidance.
- Added xUnit regression coverage for create/update XML, required certificate settings, invalid cross-type/credential fields and unverified authentication-mode guardrails. The Windows Release build, xUnit suite, browser contract/regression and publish path checks remain acceptance gates.
- Database and configuration schema versions unchanged. Live MOVEit Automation Windows COM write and certificate-based SharePoint authentication remain environment-dependent functional acceptance tests.

### v0.17.332 — Host Import Phase 2 Step 2: S3, Azure Blob and Google Storage (Build 20261013)

- Extended the **same authoritative shared Host CRUD definition profile** and existing CSV/XLSX import modal to create and update **Amazon S3** (native `S3`), **Azure Blob** (native `AzureBlob`) and **Google Cloud Storage** (native `GoogleStorage`).
- New canonical spreadsheet fields: `BUCKET` for S3/Google, `CONTAINER` for Azure, and `DATA_INTEGRITY` for S3/Azure. Updated the release-owned Host CSV/XLSX templates under `Templates/HostImport` (not `wwwroot`); the Excel workbook has examples and usage notes on non-import sheets.
- Basic cloud credentials use `USERNAME` (S3 access key ID / Azure storage account / Google HMAC access key ID) and `PASSWORD` (secret/account/HMAC key). Creation requires a bucket/container and credentials. Passwords are encrypted using existing MOVEit COM `EncryptPW`; masked previews and session-bound import plans are unchanged.
- `SECURE=Y/N` maps to native `Secure=1/0` for S3/Azure. S3 accepts integrity `never/ifsure/ignore/always`; Azure accepts `never/ignore/always`. For Google, native HTTPS defaults and HMAC mode (`UseOAuth=0`) are retained; OAuth configuration and changing HMAC credentials on existing Google OAuth Hosts are deliberately blocked until the native write contract is verified.
- Cloud definitions deliberately do not set invented `HOST` or `PORT` properties. Nonblank cloud `HOST`/`PORT`, cross-type locations, invalid integrity modes, and unsupported protocol fields fail analysis rather than being silently discarded.
- Existing Host updates retain the Host ID, task references, Resource Group, unrelated native settings and unchanged secrets, capturing Undo before native modification. Added cloud bucket/container/integrity values to native readback verification.
- Added regression tests for cloud alias normalization, default skeleton preservation, protected credentials, in-place updates, Google OAuth guardrails and invalid field combinations. Extended the CSV/XLSX template parity tests and existing browser contract.
- No schema/configuration migration. Native Windows COM creation, cloud authentication, and live connection testing must still be tested with disposable Hosts. **Phase 2 Step 3 remains SharePoint.**

### v0.17.331 — Host import templates relocated to /Templates (Build 20261013)

- Moved both Host CSV/XLSX templates from public `wwwroot/templates` to release-owned `Templates/HostImport`, adjacent to the existing `Templates/TaskImport` directory. Original `wwwroot` copies are removed.
- Added authenticated, session-validated `HostSpreadsheetImport/DownloadTemplate?format=csv|xlsx` actions that read only known files from the application's ContentRoot and return attachment downloads. Modal URLs use MVC route generation (including IIS virtual application PathBase) instead of static `~/templates` URLs.
- Added explicit `CopyToOutputDirectory` and `CopyToPublishDirectory` handling so the Host templates ship alongside the application with the Task template.
- Updated xUnit template-parity test and browser source-contract test to use the new paths, and added the contract test to GitHub Actions.
- Added an actual `dotnet publish` validation in CI asserting that Task and Host templates are included under `Templates` and Host templates are not published under `wwwroot`.
- No change to spreadsheet schema, import/CRUD behavior, database version, or configuration version.

### v0.17.330 — Host Import Phase 2, Step 1 (Build 20261013)

- Extended the authoritative shared Host CSV/XLSX definition profile for **MOVEit Transfer** (`siLock`), **POP3**, and **SMTP**, retaining Phase 1 FTP/FTPS/SFTP/UNC create/update behavior, licensing rules, Undo, credential encryption, and native Host ID preservation.
- Added native-type normalization and base connection defaults: MOVEit Transfer TCP 443; POP3 TCP 110; SMTP TCP 25. Connection fields are `HOST`, `PORT`, `USERNAME`, `PASSWORD`, `DESCRIPTION`; POP3/SMTP support basic username/password only.
- MOVEit Transfer accepts `SECURE=Y/N` and `IGNORE_CERT_PROBS=Y/N`, converted to native `Secure`/`IgnoreCertProbs` 1/0. `SCRIPT_PATH` optionally overrides its native `ScriptPath` (the skeleton default remains `siLockNet`). FTP/FTPS still uses its established non-Boolean `SECURE` TLS mode.
- SMTP supports optional `FROM_ADDRESS` mapped to the native `DefAddressFrom`; line breaks are forbidden. Invalid cross-type `SCRIPT_PATH`, `FROM_ADDRESS`, `SECURE`, `SSHKEY`, and `UNC` entries fail validation rather than being silently dropped.
- POP3/SMTP Host connection testing is not implemented by the existing Host Test service; choosing that option for these types raises an analysis error before import. POP3 Source-only / SMTP Destination-only Task Editor rules remain unchanged.
- POP3 OAuth, advanced SMTP authentication, and mail-specific SSL/TLS Host write contracts are excluded pending captured native definitions; the basic spreadsheet import does not claim support for them.
- Updated both downloadable templates with `SCRIPT_PATH` and `FROM_ADDRESS`. The Excel workbook adds three reference examples and field guidance; the first import sheet retains its SFTP sample. Updated UI connection-test wording.
- Added xUnit tests for type/port parsing, native create/update XML, encryption and password preservation, protocol field restrictions, and MOVEit Transfer Y/N normalization. The existing CSV/XLSX template parity and browser tests remain part of Windows CI.
- Documented the three-stage Host import sequence; its useful contracts are retained in [the consolidated implementation notes](#rc2-consolidated-implementation-notes). Database and configuration schema versions unchanged. CI compilation/tests do not replace live Windows COM integration with disposable Host objects.

### v0.17.329 — Host CSV/XLSX Y/N binary field consistency (Build 20261013)

- Aligned the Host import spreadsheet's binary choices with the Tasks CSV/XLSX contract: `IGNORE_CERT_PROBS` now uses **Y/N** (case-insensitive), not raw `1/0`; numeric inputs are rejected with a column-specific validation message.
- The shared Host definition profile converts Y/N to the native MOVEit `IgnoreCertProbs=1/0` field. FTP/FTPS accepts Y or N; SFTP/UNC accepts N or blank and rejects Y. Blank means N when creating and preserves the existing setting when updating.
- Kept `SECURE` as the existing FTP/FTPS protocol-mode field (for example `tls-p` or `implicit`), not a binary Y/N choice.
- Updated both downloadable CSV and XLSX Host templates with a sample SFTP row using N. The XLSX template includes examples, field guidance, and Y/N validation for that column; the UI notes the Y/N requirement.
- Added/updated xUnit regression tests for Y/N normalization, invalid legacy numeric values, explicit native XML composition, blank-field preservation, and supported Host types. Existing tests for credential preservation and import UI behavior remain in CI.
- Database schema/configuration version unchanged. Windows Release/xUnit/browser-flow CI verification applies to this build separately; it does not substitute for live COM integration testing.

### v0.17.328 — Host spreadsheet validation and update regression fixes (Build 20261013)

- Corrected the **SFTP Update existing Host** regression: `IGNORE_CERT_PROBS=0` is an ordinary disabled/default setting, not an FTP-only enabled option. Creation and updates now share one field validator; `1` remains restricted to FTP/FTPS in the current profile and other values are rejected.
- Added specific xUnit regression cases for the shipped import template's SFTP row and `IGNORE_CERT_PROBS=0` across FTP, FTPS, SFTP and UNC, including invalid enabled/unsupported values.
- Create-only Host spreadsheet imports now skip existing names without needlessly validating credentials, SSH keys or metadata that will not be applied.
- Existing Host description updates preserve the native `Desc`/`Description` attribute or element rather than creating duplicate description representations.
- Field validation exceptions now identify the offending spreadsheet column instead of reporting every error under `HOST`.
- Import outcomes distinguish verified pre-write failures from indeterminate post-COM errors; unsafe blind retries remain blocked.
- Updated dialog copy to describe Host updates, and added the existing browser-flow regression to GitHub Actions alongside the Windows .NET 8 Release build and xUnit tests.
- Database schema and configuration versions remain unchanged. A successful GitHub build verifies compilation/tests, not actual MOVEit COM operations; perform a disposable Host update and Undo integration test prior to production use.

### v0.17.327 — Host CSV/XLSX update-existing option (Build 20261013)

- Added the Task Import-consistent **Update existing Host if name already exists** checkbox, default unchecked. Existing names are skipped by default; when checked, analysis targets their existing Host IDs.
- New Host rows retain the established configured-create/creation-license path. Matching Hosts of the same supported type (FTP/FTPS, SFTP, UNC) use the shared Host service and a native COM `CheckoutItem` / sparse `ModifyItem` / readback flow, preserving IDs, Task references and unrelated Host XML.
- Empty input cells preserve saved Host fields, passwords and SSH keys. Nonblank passwords are encrypted through MOVEit COM; nonblank SSH key names resolve through the existing client-key lookup. Explicit FTPS/FTP security, port, connection and supported description values can be changed. Metadata updates apply only nonblank fields.
- Analysis and import recheck Host name/ID, type, edit permissions and Resource Group assignment. **RESOURCE_GROUP is preserved** on existing Hosts; updates requesting a different group are explicitly rejected rather than silently ignored (native group reassignment is out of this change's verified contract).
- Captures a Host Undo snapshot before mutation, records a configuration modification in MAUI metadata, supports optional Host connection testing, and reports Updated separately from Created/Skipped/Failed/Needs Attention. Updates do not consume Host license capacity.
- Added focused xUnit profile tests and expanded simulated browser regression for checkbox state, request payload, and Updated completion flow. Full .NET Release/xUnit and live Windows MOVEit COM checks remain necessary.

### v0.17.326 — Host Spreadsheet Import UI parity and initialization hardening (Build 20261013)

- Matched the Tasks CSV/XLSX import summary presentation: colored Valid/Warnings/Errors pills plus Host-specific Skipped, Created, Failed, and Needs Attention counts. Warning totals count affected rows, not repeated field messages.
- Added the source spreadsheet **Row** column before Status; preview rows show Not analyzed, while created/skipped/failed/attention outcomes use the same status-pill patterns as Tasks.
- Replaced **Finished** with **Import Complete** on full success; incomplete batches use **Import Next N Hosts** and failure/attention states receive explicit review text. Completed actions are disabled to prevent accidental duplicate imports.
- Unified progress text and cumulative eligible-row progress, preview guidance, validation issue formatting, and individual import result pills with Tasks; preserved secure password redaction.
- Reused the shared native/Tasks Import error surface, kept defensive missing-control validation, and protected active Host imports from programmatic dialog-close resets.
- Made the shared Task/Host Results panels independently scrollable and added a simulated browser regression for modal initialization, preview, analysis, row numbering, counters, import completion, mode switching, and missing-markup recovery.
- Schema and configuration format versions unchanged. Static and simulated browser checks are not substitutes for .NET Release compilation or live MOVEit validation.

### v0.17.325 — Unified Hosts Import modal / Task Import UI parity (Build 20261013)

- Consolidated the Host Actions menu to one **Import Hosts** action, matching Tasks. Native portability and CSV/XLSX now share one import modal with identical, vertically stacked Import Source cards.
- Reused the existing Task Import layout, grid, Results panel, footer action switching, and progress styling; removed the redundant stand-alone Host spreadsheet dialog.
- Removed the redundant "Create only · Existing Host names are skipped · No Host updates" inline note. Preserved FTP/FTPS/SFTP/UNC import, credential redaction, resource groups, metadata, and the native `.maui` workflow.
- Added/updated source and simulated browser-flow checks for shared modal switching, navigation, cleanup, and footer behaviors. Windows .NET/8 Release compilation and xUnit validation required before merge.

### v0.17.324 — Host Import template and modal consistency (Build 20261013)

- Standardized CSV/XLSX Host Import headers to the Task Import uppercase/underscore convention; older Phase 1 headers remain accepted through Host-only normalization.
- Isolated Host spreadsheet modal sizing selector while reusing Task Import spacing, grid, results, and footer styles. Added column-alias collision checks and regression tests.

### 2026-10-13 — v0.17.323: Host CSV/XLSX import Phase 1
- Added create-only FTP/FTPS, SSH/SFTP, and UNC Host spreadsheet import using shared COM Host creation profiles, verification, and duplicate/license safeguards.
- Added masked plaintext credential handling, resource groups, MAUI Host metadata, CSV/XLSX templates, and Task Import-style preview, analyze, results and progress UI.
- Expanded Host creation via common native XML skeleton composition and COM password encryption; existing Host Editor and portability flows remain unchanged.
- New service/profile tests and browser/source checks; live .NET build and MOVEit COM import tests remain outstanding.

### 2026-10-13 — v0.17.322: RC2 development-readiness housekeeping

- Reviewed controller/service dependencies, security and startup boundaries, the nested xUnit project, editor and Quick Transfer code, Razor/JS/CSS assets, and current release contracts.
- Removed five private, unreferenced helpers from Records, Quick Transfer, Automation Task, portability, and backup services; no active public contracts or runtime paths changed.
- Consolidated the nonconflicting `.nav-tree` scrollbar declarations into the existing navigation CSS rule; preserved editor-specific overrides and all visual values.
- Ran all 25 RC2 browser/source checks, JavaScript parsing, stylesheet parsing, project/configuration parsing, and source-structure checks. Full Windows .NET Release build, xUnit, and IIS/MOVEit smoke tests remain release gates.

### 2026-10-13 — v0.17.318: RC2 restart controls and effective idle-timeout display

- Added Advanced Settings → Restart Application above Factory Reset and a shared restart button on the restart-required banner. Both open the same confirmation modal and call an administrator-only, antiforgery-protected POST using the existing application restart service.
- Browser polls a read-only instance identifier and returns to the IIS application-relative Login page only when the replacement process is responding. No Settings, files, or database contents are deleted by this operation.
- Browser idle countdown now uses the effective startup session timeout until settings are activated by restart, matching the server-side authentication/session expiration policy.
- Added controller and integration-contract xUnit tests plus RC2 browser-flow checks. RC2 editors, Quick Transfer, Reports, Factory Reset, and configuration revision safeguards are unchanged. .NET Release build and xUnit execution remain required in Windows.

### 2026-10-13 — v0.17.317: RC2 independent Settings revision synchronization

- Data Access Credentials Save/Clear and Clear SMTP Password now return the committed configuration revision while holding the shared write gate. The Settings page advances its revision without discarding unsaved edits or creating a false dirty state.
- Existing HTTP 409 conflict protection and RC2-specific Settings fields are preserved.

### 2026-10-13 — v0.17.316: RC2 Factory Reset port (polished RC1 parity)

- Added Configuration → Settings → Advanced → Factory Reset, using the padded Settings operation-card layout and a typed `RESET` confirmation modal.
- Added an Admin-only POST with antiforgery validation, explicit session sign-out, shared configuration-write serialization, reset marker and controlled host shutdown.
- Added crash-recoverable startup purge before Data Protection, database, and Quick Transfer background services initialize; clears `appsettings.json` to `{}` and all contents inside `Data` while retaining the directory for restricted IIS ACLs. External SQL Server databases and SQLite files outside Data are not touched.
- Added regression tests for interrupted/replayed resets, invalid confirmation, restart conflicts, endpoint authorization, IIS-friendly URLs, and padded UI structure.
- Preserved RC2-specific Quick Transfer, editors, Reports, scheduler, Setup/Upgrade and configuration services. Database/configuration schema remain version 1. .NET build, xUnit and live IIS destructive-reset validation are required in the target environment.

### 2026-10-12 — v0.17.315: RC2 login/antiforgery stabilization (cleaned RC1 parity)

- Fixed the demonstrated IIS first-attempt login `AF-IDENTITY` transition: Login GET and POST now use the same anonymous identity for antiforgery token generation/validation. Other authenticated CSRF checks remain bound to the actual user.
- Added Windows machine-DPAPI-protected persistent Data Protection key storage under installation-owned `Data/DataProtection-Keys`, keeping the stable MAUI app discriminator and excluding key XML from publish output.
- Replaced silent invalid-token redirects and obsolete `LoginUsername`/`LoginServer` TempData handling with categorized `AF-*` recovery on an explicit current-PathBase Login URL. Retained session `LoginError` TempData use.
- Ported focused xUnit tests into `MoveItAutomationUI.Tests` covering principal transitions, token-pair security, key persistence after service-provider restart, safe diagnostics, stale sessions, and virtual IIS paths.
- Preserved all RC2 configuration architecture, user preferences, Upgrade authorization, Quick Transfer, Task/Host editors and MOVEit integration registrations. No database or configuration schema change.
- Release verification still requires the .NET Release build, full xUnit suite, and Windows IIS login/logout acceptance tests.

### 2026-10-12 — v0.17.314: RC2 configuration architecture stabilization (RC1 parity)

- Centralized successful configuration publication and failed-write rollback reload in `AppSettingsDocumentStore`. Backup Restore deliberately defers in-process publication until its controlled restart; Initial Setup activates its startup-sensitive baseline only after persistence.
- Settings now derives editable values (including RC2 editor preferences, MOVEit servers, SMTP, refresh/report settings and logging options) from one installation-owned `appsettings.json` snapshot, with a SHA-256 revision.
- Settings writes validate the submitted revision and database target while holding the existing shared write gate; conflicting/stale browser submissions return HTTP 409 instead of overwriting another configuration change. Save responses return the committed revision.
- Removed obsolete per-caller reload logic from Settings, SMTP password clearing, unattended credential writes, and Upgrade. Server catalog remains reload-aware, and credential selection uses a coherent list for each operation.
- Preserved restricted-IIS in-place file writes with `Data/ConfigurationRecovery` backup, bounded sharing-violation read retries, database/security rollback, startup-bound TLS/session/database behavior and RC2 Task/Host Editor toggles. No database/schema changes.
- Extended the existing `MoveItAutomationUI.Tests` project with configuration snapshot, reload, rollback and deferred-restore tests. Updated architecture-sensitive browser guards (no root-level `tests` folder).
- **Validation:** Node browser/source tests and JavaScript syntax checks are available; .NET Release build, xUnit and IIS integration remain required in the deployment environment.

### 2026-10-12 — v0.17.313: RC2 login MOVEit server dropdown regression fix

- Fixed the login view collection contract after restart-free MOVEit server updates: `MoveItServerCatalog.GetServers()` returns an `IReadOnlyList<MoveItServerDefinition>` backed by an array, which cannot be cast to `List<MoveItServerDefinition>`. The view now uses the catalog interface, so configured server choices are not incorrectly hidden.
- Ported `LoginServerCollectionContractTests.cs` from RC1 into the existing `MoveItAutomationUI.Tests` xUnit project. Preserved all restart-free configuration/loading, Setup, Settings write coordination, deployment seed, and MOVEit integrations unchanged.
- Advanced RC2 version to `0.17.313`, build `20261012` unchanged. No schema/configuration-version changes. Build/xUnit and live IIS login require validation in the target environment.

### 2026-10-12 — v0.17.312: RC2 end-to-end IIS application path audit

- Ported RC1 Records back-link safety to `RecordsController` using a centralized IIS application-boundary validator. It rejects out-of-app, protocol-relative, and dot-segment escape URLs while retaining valid app-local navigation, query strings and fragments.
- Corrected Reports edit-return URL validation in `ReportsController` and the shared layout using the same validator, preventing cross-application paths and plain/encoded traversal segments. Existing grid URLs, Reports routing, editor, and task/host behavior are unchanged.
- Added Records return-link xUnit coverage and source-level IIS regression tests for root, `/MAUI`, nested `/Apps/QA`, route-name collisions and cross-application links.
- Updated RC2 version to 0.17.312; build stays 20261012; schema and configuration format remain unchanged. Live .NET/IIS validation remains mandatory.

### 2026-10-12 — v0.17.311: RC2 IIS login redirect parity and path review

- Audited the v0.17.310 source for IIS website-root assumptions in Controllers, Razor, JavaScript, middleware and Reports. Confirmed the Dashboard Task Run links and Help/About logo were already PathBase-aware in RC2 and were left unchanged.
- Matched the RC1 post-login redirect fix: explicitly generate Dashboard/Tasks/Hosts destinations using the actual `Request.PathBase` and a canonical, allowlisted preference route, including the already-authenticated Login GET. Existing Upgrade and Setup redirects, session behavior and user preferences are preserved.
- Added C# and JavaScript regression contracts for site root, virtual/nested applications, conflicting path names and unexpected start-page preferences. Updated release metadata to v0.17.311, build 20261012; no database or configuration schema change. Live IIS and Windows .NET validation remain release gates.

### 2026-10-12 — v0.17.310: Initial Setup without IIS application restart

- First-run Setup now saves the pre-created `appsettings.json` using restricted IIS file permissions, reloads the live configuration, activates the selected SQLite/SQL Server metadata target and initial TLS/runtime policy in-process, inspects schema/Upgrade state in a fresh dependency-injection scope, and only then opens normal application routes and redirects to Login. No IIS app-pool recycle, `StopApplication`, or elevated IIS permissions are required.
- Corrected startup-captured settings consumed by scoped metadata DB, MOVEit REST/session services, Task Failure Alerts, cookie session creation and HTTP handlers. Later administrator Settings changes to database, TLS and global runtime policy are still staged until a real restart; MOVEit server list hot-loading is preserved.
- Deferred MOVEit session-service resolution on public Setup/Login/Upgrade layout pages and in Upgrade middleware to avoid pre-Setup service initialization. RC2 Quick Transfer worker and System Job scheduler continue operating with the activated runtime; no changes to their processing rules.
- Added C# lifecycle tests for first-run gating, nested IIS PathBase and runtime policy pinning, updated the session-state test fixture, and added an automated source contract. Version `0.17.310`, build `20261012`; database/configuration schema remains 1. Windows .NET Release build/xUnit and live IIS testing remain outstanding.

### 2026-10-12 — v0.17.309: RC2 MOVEit hot-reload compile fix

- Corrected a compile-time argument-type mismatch in `ConfigurationSettingsMutator.SameConnectionIdentity()`: `MoveItEnvironmentKey.Resolve()` requires a `MoveItServerDefinition`, not the `ConfigurationServerSaveRequest` received from Settings. The request now uses `MoveItEnvironmentKey.Normalize()` with the same empty-EnvironmentKey-to-Server-Key fallback as existing server definitions.
- Preserves credential safety: display-name-only changes retain saved unattended credentials, while endpoint, COM host, and environment changes clear them. Added an xUnit regression case for a legacy missing EnvironmentKey and a source-level guard against reintroducing the incompatible call.
- Version `0.17.309`, build `20261012`; database/configuration schema remains 1. No other runtime features changed. Windows .NET build/tests and IIS/MOVEit integration remain to be verified in the target environment.

### 2026-10-12 — v0.17.308: MOVEit server configuration hot-loading

- Removed the restart requirement for adding, editing, and removing MOVEit Automation server definitions in Configuration > Settings. A shared live server catalog reads the saved/reloaded configuration for login, REST authentication, Data Access Credential testing/readiness, scheduled Host Health Checks, and backups.
- Unattended administrator credentials are retained for display-name edits but cleared when the server endpoint, COM host or Environment Key is changed; the Settings UI immediately reflects this and prompts re-entry. Existing authenticated sessions remain bound to their original MOVEit Web Admin URL and credentials until sign-out/expiry; newly authenticated sessions use the saved connection definitions. Server removal prevents new logins and new unattended credentials, rather than silently redirecting active sessions.
- Preserved restart detection for startup-sensitive database settings, REST request timeout, idle-session lifetime, Task Run history limit, and server-certificate validation behavior. Updated the MOVEit Connections Settings messaging accordingly.
- Added regression coverage for live add/edit/remove, restart-detection boundaries and source integration. Version `0.17.308`, build `20261012`; database and configuration schemas remain at 1. Does not alter Task CRUD, Quick Transfer, editor functionality, or the restricted IIS file-write contract.

### 2026-10-12 — v0.17.307: Quick Transfer Task Options and CSV/Excel import template

- Quick Transfer Task Options now displays its three checkboxes in one desktop row at a 1920px browser width; confirmation recipients and Task Name remain in separate rows below. At reduced widths, options reflow to two columns and then one. Checkbox behavior, SMTP gating, and Task persistence are unchanged.
- Added the user-supplied `Templates/TaskImport/MAUI_ImportTasksTemplate.xlsx` unchanged, with build/publish copy rules. The template is outside `wwwroot` and does not create a public file endpoint; the README, INSTALLATION and TESTING guides reference the file.
- Version `0.17.307`, build `20261012`; database/configuration schemas remain version 1. No import parsing or Quick Transfer backend behavior changed.

### 2026-10-12 — v0.17.306: restricted IIS appsettings persistence and deployment seed

- Updated the RC2 configuration store to write only to an existing appsettings.json, retaining its individual NTFS ACL. No temporary files or replacement operations are performed in the application root.
- Added preflight checks for the file and Data/ConfigurationRecovery permissions before first-run database provisioning. Initial Setup overlays any existing administrator values on the release template (including the new `{}` seed). Anonymous Setup receives only curated, non-sensitive file/deployment errors.
- Before a configuration write, preserve a validated last-known-good snapshot in Data/ConfigurationRecovery; on write failure, attempt rollback and give recovery instructions. Upgrade and Backup Restore safety snapshots reside under Data, not the application root. Writes are in-place and therefore not filesystem-atomic.
- Included `appsettings.json` containing `{}` in source and build/publish output for first installation only. The deployment tool MUST skip it when the destination file exists and preserve the Data folder. Updated IIS permissions and release/upgrade documentation.
- Added targeted C# tests for the restricted ACL contract. Version `0.17.306`, build `20261012`; database schema and configuration version unchanged. No Quick Transfer, Tasks, Hosts, editor, or REST/COM behavior changed. Windows .NET/IIS validation required.

### 2026-10-12 — v0.17.305: Quick Transfer success confirmation email

- Optional confirmation email checkbox (Off by default), disabled with an explanatory tooltip when SMTP is unavailable; comma/semicolon-delimited recipient input is validated server-side.
- Existing outbound SMTP sender reused for a summary-only HTML message, with no CSV attachment.
- Background completion tracker sends only for an exactly correlated successful MOVEit run and supports retained or temporary Tasks; failed/cancelled/no-transfer outcomes never send.
- Notification attempt is durably recorded before SMTP to prevent duplicate sends across polls or restarts. If SMTP fails or the app terminates during sending, delivery is not retried automatically; the failure is logged.
- Existing temporary-Task deletion remains guarded by the original identity and run correlation checks.

### 2026-10-12 — v0.17.304: final IIS application-path source audit and navigation fixes

- Audited controllers, Razor assets/forms/links, JavaScript requests/navigation, configuration logos, middleware, authentication and cookies, report/saved-view navigation, file downloads, and MOVEit Web Admin integration against root and nested IIS application deployments.
- Report source context now excludes transient `returnUrl` / `returnLabel` parameters when captured. The common Report source normalizer removes those legacy values on edit/import/export while retaining Task, Record, and filter criteria; prevents stale Return links after cross-environment movement.
- Saved View fallback navigation now uses `Url.Content("~/")` instead of the shared IIS site root if MVC route generation unexpectedly returns null.
- Added browser coverage for these cases and `iis-pathbase-static-contract-smoke.cjs` to detect future unqualified site-root navigation in controllers, Razor markup, or JavaScript.
- Version advanced to `0.17.305`; date-based build stays `20261012`; database schema and configuration version remain unchanged. No Task/Host CRUD, MOVEit API routes, or scheduled Report execution logic was intentionally modified.
- Source/browser validation is not .NET compilation or an actual IIS deployment test. See [the consolidated implementation notes](#rc2-consolidated-implementation-notes).

### 2026-10-12 — v0.17.303: portable Report source navigation across IIS application paths

- Reports save canonical application-relative grid navigation routes instead of the browser's full PathBase-qualified pathname; both client and repository enforce the rule.
- Edit Report resolves the saved route through the **current** IIS application path, supporting root, nested, and renamed applications.
- Existing Report definitions with previous application prefixes are recognized by their actual source grid routes at read time; no database migration or manual editing is required.
- Report package import, preflight and export normalize the same route context, so reports remain portable between environments.
- Added focused C# and Node/browser contract tests. RC1 backport required after finishing IIS compatibility work.
- Version `0.17.303`, build `20261012`. Database schema and configuration version unchanged. No Task/Host CRUD or Report execution-data changes.

### 2026-10-12 — v0.17.302: additional IIS virtual application compatibility validation

- Fixed the native MOVEit editor reserved loading popup logo, which was still using `/images/automation-mark.svg` at the IIS site root. It now resolves through the existing MAUI application-aware browser URL helper.
- Corrected a route-prefix collision in `window.mauiAppUrl`: an IIS application named `/Tasks`, `/Records`, or another MAUI route segment could be mistaken for an already-prefixed URL. All existing callers supply application-relative fallback routes, so the helper now prepends `PathBase` without ambiguous de-duplication.
- Changed six Records controller emergency URL fallbacks to use `Url.Content("~/Records/...")` so they remain application-relative if normal route generation fails.
- Expanded browser regression tests to execute native popup reservation and verify URL generation for root, named, nested, and route-colliding IIS application paths; expanded `MauiIisApplicationPathTests` input coverage.
- Version advanced to `0.17.302`; build stays `20261012`. No Task/Host/Resource CRUD behavior, database schema, or configuration version changes.

### 2026-10-12 — v0.17.301: IIS application-path support and CSV Import text

- Corrected first-run and upgrade lifecycle redirects to include `Request.PathBase` (for example `/MAUI/Setup`, `/MAUI/Upgrade`, and `/MAUI/Account/Login`) while retaining normal root-site behavior.
- Corrected Dashboard navigation, Task Run links, session-expiry/login fallback URLs, and configured logo images for IIS applications hosted under a virtual path. Normal Razor tag-helper/action URLs remain authoritative.
- Scoped display/auth-cookie cleanup to the current application path to avoid interfering with sibling IIS applications on the same hostname.
- Removed the root/folder interpretation sentence from the CSV/Excel Import file-selection help; actual location parsing is unchanged.
- Advanced the application version to `0.17.301` and retained date-based build `20261012`. No database/configuration schema changes.

### 2026-10-12 — v0.17.300: Traditional Task Editor dependent-control spacing and Settings copy

- Aligned Source Ignore folder(s) and Ignore file(s) masks to the same 25px dependent-control indent as Special Filters.
- Applied the same reusable checkbox/value spacing to Source Host Overrides (timeouts, download limits, MD5 options) and Destination Host Override timeouts. Existing dependent Host credentials and Destination File Name offsets retain the same standard spacing.
- Aligned the Advanced Task Log Retention value beneath its controlling checkbox. No controls, bindings, API contracts, or business rules were changed.
- Updated both requested Quick Transfer Settings descriptions exactly. Quick Transfer permission checks and feature gating remain unchanged.
- Advanced the application version to `0.17.300` with build `20261012` across compiled assembly metadata, application configuration templates, upgrade manifest, and current README. Future delivered builds advance the patch to `.301`, `.302`, etc.; the date-based build number remains independent.
- Database schema and configuration versions remain `1`. .NET Release compilation and MOVEit runtime verification remain external validation requirements.

### 2026-10-12 — Configuration Settings save-button regression and major-feature stability pass

- Fixed the Configuration Settings initialization failure caused by two references to the undefined `checkboxValue` helper. The editor settings now use the existing `checked()` helper, which supports both checkbox and hidden Boolean controls.
- Added `configuration-settings-dirty-state-smoke.cjs`, a dependency-free JavaScript runtime regression test that exercises page initialization, save-handler registration, and input/toggle dirty-state transitions. Confirmed it fails against the prior package with the reported ReferenceError and passes with the fix.
- Verified the Settings Save workflow in a Chromium fixture: initial disabled state, toggle/text changes, reversions, successful mocked Save JSON submission, baseline reset, and subsequent edits.
- Re-ran all existing browser/source-contract smoke tests, JavaScript syntax checks, local Razor script/style asset resolution, configuration JSON, and project XML validation. No other confirmed defects were identified in those checks.
- No Task CRUD, Quick Transfer, CSV/XLSX parsing, Traditional Task Editor, COM/REST, Host Editor, scheduler, resource or database behavior was changed. See [the consolidated implementation notes](#rc2-consolidated-implementation-notes) for coverage limits and required live tests.
- Retained release version `v0.16.210 RC1`, build `20261012`, database schema 1 and configuration version 1. This is not a claim of a successful .NET Release build or live MOVEit testing.

### 2026-10-12 — RC1 test diagnostics and targeted reliability corrections

- Corrected `TaskCrudCoordinatorTests.TraditionalCreateWithMissingIdDoesNotRetryOrFallbackToCom` to assert the intentional exact `TraditionalTaskCreationUnverifiedException` type and verify exactly one REST POST and one ID-lookup attempt.
- Constrained Quick Transfer's persisted background cleanup to the original MOVEit server. An environment-matched credential from a different server must never authorize Task deletion. Hardened cached session and persisted cleanup record identity checks.
- Rejected duplicate or out-of-order XLSX cell references rather than silently assigning their values to the wrong Task columns; normal sparse and sequential worksheets remain supported.
- Added an `Unverified` CSV Import row outcome when MOVEit accepts a creation request but Task identity/readback cannot be confirmed. These rows are marked attempted to block automatic replay within the same analyzed file; operators must inspect MOVEit before retrying.
- Added focused xUnit and dependency-free browser regressions. The existing shared Task CRUD engine and Traditional Task Editor persistence paths remain unchanged. Database/configuration schema remain version 1.
- Advanced the date-based RC1 build to `20261012` (release version stays `v0.16.210 RC1`). This is not a claim of a successful Windows Release build or live MOVEit validation.

### 2026-10-11 — Pass B: Import boundaries and Quick Transfer cleanup

- Added bounded XLSX XML/ZIP parsing and CSV/XLSX row/column/cell/text limits; retained the existing 500-row default import batching.
- Added server-owned, durable, success-only cleanup tracking for opted-in temporary Quick Transfer Tasks, using the shared Task CRUD coordinator and existing run-correlation policy.
- Centralized current-user Resource Group inventory and validation for New Task, CSV Import, and group management.
- Added targeted xUnit tests and browser/source contract tests; see [the consolidated implementation notes](#rc2-consolidated-implementation-notes).
- Version/build unchanged, no database or configuration schema changes.

## RC1 development history

### Pass A: Shared Task architecture completion — build 20261011

- Introduces `TraditionalTaskRootDefinition` and common composer methods for Task root properties, with verified omission versus empty-array behavior preserved across Quick Transfer, CSV Import, and disabled New Task skeletons.
- Centralizes REST-created Task ID reconciliation within Traditional Task persistence; Quick Transfer, CSV Import and New Task share one no-retry create path. Adds explicit accepted-but-unverified creation exceptions and opt-in readback verification (used by Quick Transfer Process/PGP).
- Declares separate REST-JSON Traditional editing versus native-XML Advanced/Sync strategy contracts, leaving Advanced/Sync editors unimplemented and their COM native operations untouched.
- Routes Preflight and Resource Group single-Task reads through the shared Task CRUD coordinator. Workflow-specific runtime/inventory and resource-group configuration handling remain unchanged.
- Adds C# regression cases for root payload ownership, no duplicated POST, reconciliation, readback, and native format boundaries, plus a dependency-free browser contract test.
- Version v0.16.210 RC1 / build 20261011 / DB schema 1 / configuration version 1 remain unchanged. This is source-level validation; Windows .NET 8 build and live MOVEit tests are still required.

### Quick Transfer feature setting, shared field macros and UI consistency — build 20261011

- **Dashboard > Hosts**: the View all Hosts link now targets `/Hosts/Index?filter=all`.
- **Grids > Bulk Actions**: Delete actions reuse the existing `.menu-danger-action` dropdown styling, matching established MAUI destructive-action color behavior.
- **Quick Transfer macros**: the Source Filename/Filespec dropdown now uses the Traditional Task Editor `source-file` macro context; Destination Filename uses `runtime-file`. Both features consume a single shared `wwwroot/js/shared/traditional-task-field-macros.js` catalog, with `Macros…` as the placeholder. The original editor entries, order, grouping and descriptions are retained.
- **Configuration > Settings > General > Quick Transfer**: adds an Enable Quick Transfer toggle, default Off. The Transfers left navigation is visible only when enabled and the current MOVEit session is an administrator. All seven Quick Transfer controller actions reject access when disabled or the session is non-admin. The option is persisted under `Ui:EnableQuickTransfer`, with missing-key default Off for existing installations, and is active after normal configuration reload without restarting the application.
- Includes an isolated browser/source regression test for setting persistence, authorization, macros and UI hooks; existing regression tests remain enabled.
- Version `v0.16.210 RC1`, build `20261011`, database schema `1`, and configuration version `1` remain unchanged.
- .NET/Razor compilation and live MOVEit integration testing are not available in this environment; no .NET SDK installation was attempted.

### Shared Task CRUD coordinator — build 20261011

- Adds `ITaskCrudCoordinator` with explicit Traditional, Advanced and Synchronization Task creation strategies and one object CRUD entry point for feature workflows.
- Traditional disabled/new Task creation remains REST; Advanced/Sync retain COM; native Task XML clone/import/snapshot/restore and overnight schedule operations retain existing COM behavior.
- Migrates Task CRUD from AutomationTaskService, Quick Transfer, spreadsheet import, Task Editor patch/read, New Task modal, native Host-export Task carriers and Source Timestamp mutation transport into the coordinator. Shared Traditional REST persistence remains an internal delegate, not another feature-layer API.
- Centralizes REST-created Task ID reconciliation without retrying a possibly committed POST; preserves Resource Group rules, Task permission/license checks, bulk dependency ordering, Undo, metadata/audit and runtime operations.
- Adds C# unit tests and updates all five browser/source-contract regression checks; source-only validation cannot establish .NET build or MOVEit REST/COM compatibility.
- Retains v0.16.210 RC1, build 20261011; DB and configuration versions remain 1. No Advanced/Sync editor or Host CRUD implementation is introduced.

### Quick Transfer permissions, New Task Resource Groups, and Experimental settings — build 20261011

- Restricts **Transfers > Quick Transfer** navigation and all seven controller actions to authenticated **MOVEit administrators** (not merely MAUI application administrators), with server-side HTTP 403 responses for unauthorized access.
- Removes Resource Group selection from Quick Transfer Task Options, request normalization/fingerprints and REST Task creation. Temporary and persistent Quick Transfer Tasks created by MOVEit administrators no longer submit a Resource Group.
- For New Task, MOVEit administrators no longer see Resource Group selection. Non-administrators select from a dynamically loaded, account-visible MOVEit Resource Group dropdown; the server revalidates visibility on Traditional Task creation.
- Keeps Recycle Bin settings in **Configuration > Settings > General** irrespective of outdated experimental metadata, removing the orphaned Experimental Recycle Bin card and redundant hidden controls.
- Updates all three Experimental Editor descriptions to the requested text.
- Adds browser/source smoke coverage for MOVEit administrator gating and populated, empty and failed Resource Group loads. Existing CSV Import/Editor behavior is unchanged.
- The v0.16.210 RC1 version, build number 20261011, database version 1 and configuration version 1 remain unchanged. .NET compilation and live MOVEit testing require Windows environment verification.

### Phase 3 build diagnostics correction — build 20261011

- Corrected two xUnit2013 analyzer errors in `TaskSpreadsheetImportTests` using `Assert.Single(result.Rows)`.
- Corrected CS8602 in `TaskSpreadsheetImportFileReader` by consistently treating the non-null CSV text as required input.
- Corrected CS8625 in `TaskSpreadsheetMacroTranslator` by setting an unsuccessful translation's `out string` to `string.Empty` rather than null.
- Rechecked the remaining test assertions for collection-size analyzer patterns, application JavaScript syntax, JSON/project XML, and existing spreadsheet/editor/REST source-contract smoke tests.
- This is a source correction. .NET 8 compilation, Razor compilation, xUnit execution and live MOVEit API verification must be performed in the target environment.
- Release version remains v0.16.210 RC1, build 20261011. Database/configuration versions remain 1.

### Phase 3 integration validation — build 20261011

- Repaired cross-page Task/Host editor routing contracts (missing Task Details URL and unconsumed editor-target query parameters).
- Prevented a stale Resource Group value in the New Task dialog.
- Prevented null Host dereferences during CSV/XLSX row analysis when a Host is missing but its Path is populated.
- Corrected the New Task dialog description for configurable editor routing.
- Added dependency-free routing/source-contract smoke tests and an implementation audit, now summarized in [the consolidated implementation notes](#rc2-consolidated-implementation-notes).
- No changes to the application version, build number, DB schema, configuration schema, or REST/COM creation strategy.

### v0.16.210-RC1 — Build 20261011

- **Release date:** 2026-10-11
- **Database schema:** `1`
- **Configuration version:** `1`

This is the authoritative RC1 release entry. Earlier internal `0.16.210-RC1` candidate builds are superseded by build `20261011` and are not documented separately in this release package.

#### Phase 3 — REST Traditional Task creation + optional RC2 editors

- Standardizes Traditional Task creation on REST via the shared Phase 2 persistence service: Quick Transfer (including ADHOC temporary Tasks), CSV/XLSX Import, and New Traditional Task creation for the MAUI editor. Advanced/Sync Task creation and native Task configuration/schedule COM pathways remain intact.
- Reintroduces the RC2 v0.16.289 Traditional Task Editor and incomplete experimental Host Editor behind independent feature toggles. Task writes use shared fresh-definition REST persistence; native overnight schedule XML support remains opt-in. Host saves use independent verified Host checkout/PUT/release profiles.
- Reuses the authoritative shared Host capability catalog through an RC2-compatible adapter; adds editor-specific REST v0 Host update, transfer-exception and Source Timestamp operations.
- Restores required `RESOURCE_GROUP` header and nonblank row values in Task spreadsheet import, with named-group lookup. Quick Transfer currently uses REST creation without a Resource Group for MOVEit administrators (see the newer change entry above).
- Restores per-user Traditional Task Editor Auto Save preferences with existing metadata write locking; settings integrated into MAUI Experimental styles. No database/config version change.
- Adds REST creation, required Resource Group and Quick Transfer normalization tests. See [the consolidated implementation notes](#rc2-consolidated-implementation-notes). This is a source candidate; .NET compilation and live MOVEit acceptance are pending.

#### Phase 2 — shared Traditional Task persistence (RC1-safe refactor)

- Adds `ITraditionalTaskPersistenceService` with explicit, existing **COM-skeleton + REST PUT** and **REST full-definition create** strategies. Quick Transfer and CSV/Excel Import now use one coordinator for Task creation/update calls while retaining their existing licensing/Undo/audit/cache/reconciliation and execution workflows.
- Extracts `TraditionalTaskDefinitionMerger` for case-insensitive, explicit ownership of live Task properties. Spreadsheet updates still replace only `Name`, `Info`, `Schedules`, `steps` and `NextActions`; missing Next Actions intentionally clears them, and Resource Group/unknown root properties are preserved. Other workflows can supply their own narrower patch policy.
- Uses a fresh full MOVEit Task for every REST update. Optional post-PUT readback/verification distinguishes **accepted but unverified writes** from pre-PUT validation errors. Quick Transfer retains Process/PGP readback; spreadsheet imports retain their existing immediate commit behavior pending native parity testing.
- Transplants RC2 v0.16.289's **opt-in native Task XML checkout/modify/readback** COM method and isolated Multi=1/Multi=2 Schedule XML adapter, without invoking the native schedule path from the existing RC1 workflows.
- Adds `TraditionalTaskPersistenceTests` for field preservation, REST update/readback ordering, creation strategies, partial-save classification, and native XML roundtripping. No editor UI integration or schema/configuration changes.
- See [the consolidated implementation notes](#rc2-consolidated-implementation-notes) for ownership boundaries and remaining validation gates.

#### Phase 1 — shared Traditional Task construction core

- Extracts shared native Source/Destination/Process JSON composition behind `ITraditionalTaskDefinitionComposer`, consumed by Quick Transfer and CSV/Excel Import. Preserves their distinct field shapes and existing COM/REST create/update behavior.
- Consolidates current RC1 Host type identity/default mapping with RC2 v0.16.289's richer protocol capability metadata and field allow-lists in one registered Host catalog. POP3/SMTP editor direction rules are carried forward without enabling new RC1 import/transfer functionality.
- Adds automated contract/parity tests and [the consolidated implementation notes](#rc2-consolidated-implementation-notes) defining the Stage 2 persistence and Stage 3 RC2 integration boundaries. No database schema or configuration change.

#### Release highlights

- Adds the modern MAUI administration experience for Tasks, Hosts, Resources, Records, Reports, Configuration, Dashboard, Calendar, System Jobs, metadata, Tags, saved views, and operational tooling around MOVEit Automation.
- Retains MOVEit Automation as the system of record for Automation objects and execution data while storing MAUI-only metadata, preferences, report definitions/history, authorization, Undo/Recycle data, and System Job state in the MAUI metadata database.
- Supports SQLite or Microsoft SQL Server for MAUI metadata while retaining the RC1 requirement for one active MAUI application instance/worker.
- Adds license-aware feature gating for supported MOVEit capabilities and finite Task/Host capacity guardrails.
- Adds MAUI-native operational workflows, including **Transfers > Quick Transfer**, while continuing to use native MOVEit Web Admin for supported object editing in RC1.

#### Dashboard and operational visibility

- Provides KPI summaries for Tasks, Hosts, File Activity, running work, recent Task activity, certificate/key expiration, and a rolling 24-hour transfer trend.
- Aligns Successful/Failed File Activity classification with captured native MOVEit Dashboard semantics: only `get`, `send`, and `append` actions count; `StatusCode 0` is successful; `5000` and `5010` are excluded; other numeric status codes are failed.
- Replaces repeated full 24-hour File Activity rescans with a per-environment/per-user rolling in-memory snapshot. After the initial history load, normal refreshes request only rows newer than the completed LogID high-water mark.
- Makes initial File Activity loads above 10,000 rows progressive. The first completed page renders immediately with a `+` lower-bound indicator and advances as additional pages complete.
- Preserves progressive values on later-page failures and retries from the existing cursor rather than clearing useful partial data.
- Adds a bounded catch-up pass after multi-page cold loads so activity created while history is being read is not lost.

#### Failover Status

- Adds read-only **Failover Status** to the footer Server menu when `GET /api/v1/failoverstatus` reports `ResilEnabled: true`.
- Shows the returned two-node topology with hostname, current primary/secondary role, native role-specific status text/timestamp, and health derived strictly from `NodeHasError`.
- Maps `ResilStatus`/`ResilStatusStamp` to the node currently marked `primary` and `ResilStatusSec`/`ResilStatusStampSec` to the node currently marked `secondary`, so the display remains correct after role reversal.
- Preserves MOVEit-provided failover timestamps verbatim rather than inventing a timezone conversion.
- Requires the MAUI Admin policy server-side as well as through the Admin-only Server menu.

#### Quick Transfer

- Adds **Transfers > Quick Transfer** for immediate one-off file movement through a generated MOVEit Traditional Task, with optional retention as a named reusable Traditional Task.
- Supports saved UNC Share, MOVEit Transfer, FTP/FTPS, SSH/SFTP, Amazon S3, Azure Blob, SharePoint, and Google Storage Source/Destination profiles using shared verified Traditional Task Host contracts; the MOVEit **(Default) Local File System** Host is intentionally excluded from Quick Transfer.
- Reuses verified Host browse contracts where available. Browse is offered only for verified Host types, while the server-side browse service performs final authentication/proxy/blind-download shape validation and rejects unsupported patterns instead of guessing a native request. Otherwise-supported profiles can still use manual folder/path entry; root-based profiles use the saved Host default root unless supported Browse selects another root.
- Keeps Source/Destination Browse geometry stable: the results frame permanently fills the modal space below Location, with Loading and error states rendered inside that frame so content/state changes do not resize the browser surface.
- Supports optional MOVEit built-in **PGP Encrypt Only** and **PGP Decrypt** processing. PGP choices use the shared MOVEit license policy: Encrypt/Decrypt are disabled when PGP is not licensed and expose the license reason as a tooltip; refreshed Quick Transfer reference data keeps that state synchronized. Expired PGP keys are filtered from Encrypt, encryption requires a selected non-expired PGP key, and MAUI re-reads the saved Task to verify the requested recipient key was actually persisted before execution.
- Adds **Preflight** for the prepared Quick Transfer Task using the standard Task/Hosts/Resources/Certificates & Keys/Notifications/Scheduling/Configuration/Runtime staged checks without starting the Task.
- Enforces one available Traditional Task slot, current Host/key visibility, root/path rules, parent-traversal rejection, and safe wildcard/multi-file destination naming before Task creation/start.
- Adds **Delete Source File After Successful Transfer** beneath Source Filename / Filespec, disabled by default and persisted to MOVEit's native Source `DeleteOrig` field (`1` when checked, `0` when cleared). The setting participates in prepared-Task identity so changing it cannot reuse a Task prepared with the opposite source-delete behavior.
- Adds **Overwrite Existing File** beneath Destination Filename, enabled by default and persisted to MOVEit's native Destination `OverwriteOrig` field (`1` when checked, `0` when cleared). The setting participates in prepared-Task identity so changing it cannot reuse a Task prepared with the opposite overwrite behavior.
- Deletes successful temporary Tasks by default while retaining failed/cancelled/no-transfer Tasks for troubleshooting; persistent Quick Transfer Tasks remain available as normal Traditional Tasks.
- Treats native Task start as a point of no return. Ambiguous post-submit failures are not automatically retried, preventing an uncertain MOVEit start from launching a duplicate transfer.
- Keeps the last verified live byte progress visible while a completed run propagates from MOVEit's running-task endpoint into Task Run history. Running/status/cancellation correlation now shares one conservative identity path: a known non-empty `NominalStart` is never replaced by a different non-empty run identity, missing identities may fall back only to one bounded start-time candidate, and ambiguous/late candidates are never guessed.
- Changes the running **Transfer** action to **Cancel**, including while MAUI is still waiting for the native start response. Cancellation reuses MOVEit's exact Task kill contract, remains pending when the run cannot be correlated safely, and reports **Cancelled** only after the stop is confirmed.
- Aligns the Quick Transfer page heading and descriptive text with the standard grid-page heading treatment. Task Options spans the page below the three workflow cards with side-by-side choices when space permits; Transfer Status uses normal grid-scale typography, presents Task, Files, and Transferred together on one compact summary row, and places Preflight/Transfer/Cancel in an integrated modal-style footer.
- RC1 cleanup makes Source/Destination Host inventories role-aware, canonicalizes request fields that are irrelevant to the selected processing/task mode before prepared-Task fingerprinting, and removes unused Quick Transfer response/model fields and stale presentation naming without changing supported transfer behavior.
- Quick Transfer monitoring state is process-local in RC1. An IIS/application recycle does not stop a MOVEit run that was already submitted, but its MAUI transfer token/status cannot be resumed afterward; temporary Task deletion occurs when MAUI observes successful completion, so interrupted monitoring can leave an `ADHOC_*` Task for manual review.

#### Task spreadsheet import

- Refines **Import Tasks** modal styling: native `.maui` mode retains standard portability-dialog width and evenly spaced labels, inputs, help text, and checkboxes. Spreadsheet mode retains its wider paginated grid, with Prefix checkboxes vertically centered alongside the select control. Results messages now use a stable location/message grid so long errors wrap consistently, and the component uses standard MAUI theme colors in both light and dark mode.
- Makes `RESOURCE_GROUP` truly optional at both the header and row level: an omitted/blank group creates a new Task with native `Group: []`, while a provided group still must resolve; updates preserve the live Task's group and only warn if a different group was explicitly requested. Eliminates duplicate blank-group errors and unnecessary duplicate blank Host-reference errors.

- Expands **Import Tasks** into two isolated modes: the existing **MAUI Portability Package** workflow remains unchanged, while a new **CSV / Excel** workflow builds Traditional Tasks from `.csv` or first-worksheet `.xlsx` data.
- CSV/Excel file selection performs a parse-only preview before **Analyze File**. The import modal body scrolls vertically while the **Results** panel grows with its content and has no separate vertical scrollbar; the import progress bar remains in the footer. The preview grid is paginated to bound rendered rows rather than building a browser table for the entire file.
- Adds **Configuration > Settings > General > Import Settings > Import Row Limit**, a validated numeric setting (default `500`, minimum `1`, maximum `1000`). It controls both rows rendered per preview page and the number of Tasks processed per import action. Larger files use successive batches without discarding the analysis, prior results, or completed row outcomes. The active analysis has a 30-minute sliding idle timeout.
- Displays actual per-row **Created / Updated / Failed** outcomes in the preview grid as import requests finish, with those outcomes retained when navigating between pages and summarized alongside the validation counts. Failed rows are not automatically retried.
- Adds Analyze-before-import validation with normalized preview data, per-row Valid/Warning/Error status, detailed validation issues, and row-by-row import progress/results. Blocking errors prevent Import Tasks from becoming available.
- Supports `TASK_NAME` plus the legacy `REQ_NAME` alias and optional Task prefixes `PROD`, `DEV`, `QA`, and `STG`. Prefixes are applied after the proven uppercase/underscore Task-name normalization.
- Carries forward the proven ImportFromCSV normalization/guardrails for required fields, canonical Y/N values/defaults, Source masks, date/time macros, schedule values, Host resolution, Share UNC-relative paths, Resource Groups, Date Lists, PGP scripts/keys, notifications, archive destinations, and destination/source behavior.
- Expands spreadsheet Task construction to reuse MAUI's shared verified Traditional Task Host profiles for **UNC Share, MOVEit Transfer, FTP/FTPS, SSH/SFTP, Amazon S3, Azure Blob, SharePoint, and Google Storage**. POP3/SMTP/ASx and the MOVEit `(Default)` Local File System Host remain outside this importer.
- Resolves exact Host friendly names first, preserves the proven unique FTP/SFTP/UNC endpoint fallback, and rejects ambiguous Host references. MOVEit Transfer and cloud/object-storage Hosts are intentionally resolved by Friendly Name rather than guessed from endpoints.
- Reuses the shared `AutomationHostBrowseService` for root-based spreadsheet locations. A leading `/` uses the Host default bucket/container/Document Library; `root/folder` explicitly selects and validates a root. SharePoint Document Library GUIDs are taken from MOVEit's Browse response, never from spreadsheet input. Existing Browse guardrails remain authoritative, so unsupported explicit-root shapes (for example Google Storage OAuth bucket discovery or non-Certificate SharePoint library discovery) fail Analyze instead of inventing a native request.
- Maps unmatched spreadsheet columns to MAUI Task custom metadata only when the normalized column name matches a configured Task metadata field; other columns are ignored and counted in the Analyze summary.
- Supports MAUI Notification Template names for Failure/Success/No Action messages. When no template is specified, neutral built-in text derived from the proven importer is used with Holman-specific wording removed. Rows requesting notifications require exactly one visible MOVEit SMTP Host.
- Supports exact-name update of an existing Traditional Task when explicitly enabled, captures MAUI Undo before update, preserves the existing Resource Group on update because MOVEit REST does not reliably move Tasks between groups, and blocks replacement of Advanced, Synchronization, or other non-Traditional Tasks.
- Revalidates Task name/capacity at commit time and serializes Task creation through the shared license-capacity gate. New imports respect the finite MOVEit Task license limit.
- Spreadsheet analysis state is short-lived and server-side; the browser receives an opaque analysis ID and normalized preview rather than a writable Task payload. Import executes one analyzed row per request so progress reflects confirmed create/update operations.
- First validation/hardening pass makes malformed XLSX packages return a controlled validation error, preserves real Excel worksheet row numbers in diagnostics, prevents concurrent/replayed row requests from applying the same analyzed row twice, and treats session expiry during Analyze/Import as the standard MAUI 401 response.
- Existing-Task updates now start from the live MOVEit Task definition and replace only spreadsheet-owned fields, preserving unknown/version-specific root properties while still clearing spreadsheet-owned notifications when none are supplied. Update is allowed only for Traditional Tasks.
- PGP key resolution now handles normalized MOVEit key IDs safely, rejects empty fingerprint suffix matches, and persists the native MOVEit key ID used by the verified Quick Transfer PGP contract. Ambiguous normalized MAUI metadata column mappings are reported as Analyze errors instead of failing dictionary construction.
- Analyze-level blocking errors are retained in the server-side analysis state as well as the preview, so a stale/direct row-import request cannot bypass file-wide validation such as license capacity or ambiguous metadata-column mappings.
- Create/update intent is now bound to Analyze-time Task identity. A planned create will not silently become an update if a same-name Task appears, and a planned update will not become a create or target a replacement Task if the analyzed Task disappears or its ID changes before commit.
- Host, SMTP Host, existing Task, and custom Date List identifiers are validated before Task JSON construction so malformed/incomplete MOVEit inventory is reported as an Analyze error instead of surfacing later as a payload-construction exception.
- Expected Task-builder/profile validation failures are contained to the affected spreadsheet row, allowing Analyze to return the rest of the file preview instead of aborting the entire file.
- The legacy `SCHED_WINDOWS` helper path is no longer allowed to influence spreadsheet Task construction because that column is not part of the approved MAUI import schema; an extra `SCHED_WINDOWS` column now follows the normal custom-metadata/ignored-column rules instead of being reported as ignored while still changing the schedule.
- Second compile-readiness validation pass fixes the Quick Transfer run-correlation local-scope name collision (`CS0136`) and gives the spreadsheet XLSX/style and day-of-month parser locals unique names to avoid similar C# scope ambiguity; these are non-functional naming corrections.
- The same validation pass removes unused spreadsheet Analyze-context/cache timestamp fields so the new import service carries only state that participates in validation/import execution.

#### Task and Host portability

- Replaces MAUI-specific Task/Host object serialization with MOVEit's native configuration export/import model while preserving MAUI custom metadata and Tags.
- Supports **Server Key** export for same-key environments and **Portable / Passphrase** export for cross-environment migration.
- Portable passphrases require at least 16 characters with an uppercase letter, lowercase letter, digit, and special character. Export dialogs generate a compliant random 16-character value by default while allowing administrators to replace it.
- Supports optional referenced key/certificate dependencies and explicit conflict resolution for overwrite, use-existing dependency, and import-as-new/rename workflows.
- Revalidates analyzed conflict targets immediately before native commit so deleted, replaced, renamed, or read-only destination objects cannot silently become a different overwrite/use-existing target.
- Keeps the native import commit non-cancellable once submitted. Ambiguous post-submit transport/time-out/5xx conditions are reported as outcome-unknown with verify-before-retry guidance rather than as proof that no import occurred.
- Host export uses a temporary disabled carrier Task because MOVEit does not directly export Hosts. The carrier consumes one temporary Task license slot, is removed during cleanup, and is never placed in the package.
- Host import requires the captured native empty `<Tasks></Tasks>` collection and creates no carrier Task.
- Imported Tasks can remain disabled by default; successful imports refresh the corresponding grid without automatically opening the native editor.
- Folder placement, audit timestamps, and Undo/Recycle history are intentionally not portable.

#### `.maui` package format

- Standardizes all user-facing `.maui` exports on the MAUI 2.0 ZIP container.
- Task, Host, Script, Date List, and Report packages contain exactly:
  - `manifest.json`
  - `payload.xml`
  - `maui-metadata.xml`
- Every package manifest uses `format: MAUI.PortablePackage` and `formatVersion: 2.0`.
- Task/Host packages use the native MOVEit package kind; Script/Date List/Report packages use MAUI object-export payloads while retaining the same physical ZIP envelope.
- Import requires the `.maui` extension and a valid ZIP package. Pre-2.0/plain-XML `.maui` files are intentionally unsupported.
- Removes obsolete alternate Host portability code and narrows the legacy Task portability service to internal Clone support so there is one user-facing Task/Host package implementation.

#### MOVEit Configuration import/export and Backup Restore

- Adds MOVEit Configuration export modes for **Portable — Passphrase**, **Server Key**, and **Sanitized / Troubleshooting** using the shared MAUI radio-card presentation.
- Portable export shows plaintext Passphrase/Confirm Passphrase fields and generates a compliant 16-character value by default. Server Key and Sanitized hide and clear those fields.
- Configuration import presents the file picker before Import Type and offers only **Portable — Passphrase** and **Server Key**. Import passphrases remain empty password fields and are never auto-populated.
- Restores Server-Key configuration XML through the required raw `application/xml` path and keeps Portable configuration import on the captured JSON/passphrase contract.
- Discards successful MOVEit configuration-import response bodies server-side because they can contain broad configuration data that MAUI does not need to expose.
- Applies point-of-no-return handling to whole-configuration import and MOVEit configuration restore inside Backup Restore. Preflight remains cancellable; destructive native mutation is not cancelled by a browser disconnect.
- Ambiguous post-submit configuration-import/restore outcomes are logged and surfaced with explicit verify-before-retry guidance.

#### Backup and retention

- Adds configurable **Backup Retention** from 1–30 days, default 30 days.
- Adds the visible **Backup Cleanup** System Job, enabled by default and scheduled daily at 1:00 AM server-local time.
- Cleanup removes recognized MAUI-created backup folders that meet retention criteria and leaves unrecognized folders untouched.
- Available Backups refresh when the Backup panel opens and support guarded download, removal, and restore.
- Backup Restore participates in the shared appsettings write coordinator to prevent destructive overlap with Settings/Credentials/configuration writers.

#### Tasks, Hosts, Resources, and notifications

- Adds unified Task views, Task Information, Run/Kill/Enable/Disable/Delete operations, Run History, File Activity, Audit, Upcoming Runs, Calendar, folders, metadata, Tags, and Preflight Check.
- Adds Host inventory/type filtering, Host Information, dependencies, metadata/Tags, guarded delete/Undo behavior, Health/Test Results, and supported connection tests.
- Provides Scripts, Date Lists, Global Parameters, and Keys & Certificates resource workspaces with metadata, Tags, import/export where supported, dependency information, and protected built-in resource behavior.
- Adds Traditional Task **Edit Notifications** with exact Email NextAction selection, Failure/No Transfer/Success result combinations, Text/basic sanitized HTML authoring, Visual/Source modes, MOVEit macros, bulk operations, Undo protection, and reusable environment-scoped Notification Templates.
- Keeps legacy Notification Templates without stored Text/HTML format compatible through conservative format detection.
- Adds read-only Task **Preflight Check** stages for Task state, Hosts, Resources, Keys/Certificates, Notifications, Scheduling, Configuration references, and runtime eligibility.

#### Reports and System Jobs

- Adds saved One Time and Recurring Reports with manual Run Now, history, enable/disable, deletion, CSV export, and scheduled SMTP delivery.
- Enforces Report-source authorization server-side during definition changes and execution so administrator-only sources cannot be obtained by normal users through direct requests.
- Provides visible System Jobs for Reports, Backup Cleanup, Host Health Check, and Job History Cleanup with applicable schedule and Run Now controls.
- Missed System Jobs are rescheduled rather than automatically replayed.

#### Native MOVEit Web Admin integration

- Provides `maui-integration.js`, `maui-native-editor.css`, and `automation-mark.svg` for native-editor launch/integration.
- Adds strict trusted-origin handling through the `LAUNCHER_ORIGINS` allow-list, per-launch nonces, exact MOVEit origin checks, and exact popup source checks.
- Supports one-level **Undo Last Change** for native Task, Host, Script, and Date List editing where a safe baseline/restore can be confirmed.
- Task Undo remains bound through supported child routes and safely rebinds after MOVEit's Traditional-to-Advanced conversion creates a new Task ID; the conversion itself is not an Undo target.

#### Configuration and administrative consistency

- Adds Setup validation for the local `MICentralAPICOM.MICentralAPI` COM prerequisite by resolving/instantiating/releasing the COM object without a MOVEit username/password or COM login.
- Adds safe create-if-missing handling for editable settings absent from older RC1 `appsettings.json` files while preserving existing values and rejecting incompatible JSON shapes.
- Standardizes retention selectors, destructive Configuration controls, modal loading states, confirmation wording, row ellipsis controls, and shared grid refresh behavior.
- Adds optional Task Failure Alerts with grouped Critical System Messages, audible notification, and 30-second auto-dismiss behavior.
- Adds **Auto Format New Task/Host Names** and maintains centrally managed Tags and Notification Templates.

#### Security and reliability hardening

- Requires a live server-side MOVEit session for protected operations in addition to the MAUI cookie principal.
- Keeps MAUI Admin authorization database-backed and distinct from MOVEit administrator status.
- Applies antiforgery validation to normal state-changing MVC requests and explicit antiforgery validation to Login so stale login pages can recover after application restart.
- Rejects unsafe persistent SQLite `file:` URI paths and keeps the metadata database outside `wwwroot`.
- Uses shared keyed async locking for cache refreshes, token renewal, per-user preference writes, and other serialized operations without retaining unbounded semaphore dictionaries.
- Prevents Report completion from silently undoing an administrator disable through status/revision reconciliation.
- Avoids browser-native `alert()`, `confirm()`, and `prompt()` dialogs in favor of MAUI dialogs/toasts.
- Redacts/suppresses passphrases, native configuration XML/JSON, encrypted key material, and similar sensitive values from new portability/configuration diagnostics.

#### Testing and release validation

- Maintains `TreatWarningsAsErrors=true` for the test project and uses analyzer-safe xUnit patterns, including the xUnit2031-safe `Assert.Single(collection.Where(...))` form.
- Adds focused regression coverage for native portability contracts, conflict-target revalidation, MAUI 2.0 ZIP containers, configuration import/restore contracts, Dashboard File Activity classification/incremental predicates, Failover Status authorization/parsing, Settings radio-card behavior, Backup cleanup/retention, license/capacity rules, Reports authorization, and other RC1 hardening work.
- See [TESTING.md](TESTING.md) for the current automated and manual validation gate; the entry above records the historical RC1 baseline.

#### RC1 scope and known limitations

- RC1 supports a single active MAUI application instance/worker. SQL Server is supported as the metadata provider but does not make the MAUI web tier active/active or provide distributed System Job coordination.
- SSO is not supported in RC1; users authenticate with MOVEit Automation credentials.
- RC1 uses native MOVEit Web Admin for supported object editing. MOVEit System Log, Service Shutdown, Virus Test, and Watched Tasks are not replaced by MAUI RC1.
- AS2 and Backup Host license attributes are visible to MAUI but are not enforced in RC1.
- Dashboard File Activity interprets unzoned MOVEit report timestamps using the MAUI host's local timezone. Environments where MAUI and MOVEit use different timezones should validate the rolling 24-hour boundary against native MOVEit before changing timestamp parsing.

#### Deployment notes

- Install the .NET 8 Hosting Bundle on the MAUI IIS server and configure the MAUI application pool with **Maximum Worker Processes = 1**.
- Install/register the compatible MOVEit Automation Windows COM API on the MAUI IIS server.
- Preserve installation-owned `appsettings.json` and `Data\` during deployment; do not use destructive destination-cleanup options.
- Deploy the MAUI Web Admin integration assets to every MOVEit Web Admin/Tomcat node users may reach and configure the exact trusted `LAUNCHER_ORIGINS` list before deployment.
- The Web Admin integration asset revision remains `0.16.206-RC1`; application builds after that revision do not require the asset revision to match the MAUI application version.

For prerequisites and deployment instructions, see [INSTALLATION.md](INSTALLATION.md). For upgrade behavior, see [UPGRADE.md](UPGRADE.md). For security guidance, see [SECURITY.md](SECURITY.md).

#### Shared Traditional Task CRUD consolidation (2026-10-11)
- Moved Traditional Task editor Step patching/validation/defaulting/schedule logic into the shared Traditional Tasks layer; the editor retains only a controller-compatible save adapter.
- Exposed shared editor Step application/readback and disabled skeleton composition on `ITraditionalTaskDefinitionComposer`.
- Centralized Quick Transfer's Traditional Task REST read/delete through the shared persistence interface.
- Added C# regression test cases and Node source contract smoke test for shared composition and persistence.
- Added [the consolidated implementation notes](#rc2-consolidated-implementation-notes) documenting current scope and the future polymorphic Advanced/Sync/Host architecture.
- No intended UI changes, database/configuration schema changes, or change to Advanced/Sync/Host CRUD semantics.

## Validation history

Historical records below apply only to the stated application/test commits. They do not certify the current RC3 candidate.

### RC3 validation archive

These records describe their exact historical application/test commits, not the current release by implication.

#### 0.18.495 RC3 validation

[Windows CI 40360465956](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40360465956) passed on master for application/test commit `11bfce370a58dfdd96ad4ca4acabf36c1455ddcb` (**0.18.495 RC3, Build 20261026**):

- Release solution build: **zero warnings and zero errors**.
- xUnit: **2,443 passed, zero failed, zero skipped**.
- All **14 package checks**, publishing validation and **29 Node/Edge suites** passed.
- Host rendering: **86 fixtures and 9,625 layout checks** across both themes and four widths. Task rendering: **3,103 Sync/Traditional layout checks**.

The result-recording follow-up updates documentation and replaces the report's old fixed profile count with a count of distinct fixture type codes. The counter expression/syntax is checked with Node; production code and test assertions are unchanged. This does not replace live MOVEit 2026 acceptance.

New tests cover minimum-version handling in both published release and native engine forms (2026.0 / 18.0), absent/older/mismatched engine versions, OneDrive creation and browse/authentication shapes, S3 IAM payloads and inactive credentials, REST dispatch guards, shared Host test/browser checkout cleanup, and editor DTO secret suppression. The rendered Host fixture matrix includes OneDrive certificate/client-secret and S3 AWS profile/IMDS modes. Live native MOVEit 2026 acceptance remains required.

#### 0.18.494 RC3 validation

Windows build and publish now run the read-only `Build/Verify-ReleasePackage.ps1` check. It compares the actual DLL product version with both release JSON versions and checks the configuration-version contract; it does not start MAUI or read live `appsettings.json` or Data. CI seeds stale release files with newer timestamps into build and publish output, verifies that the current files replace them, and runs the PowerShell rejection/state-preservation cases. [Windows CI 40346848258](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40346848258) passed on master for application/test/package commit `78fb8f8f5d770169a670d5eb7f00802ea9e7a011` (**0.18.494 RC3, Build 20261026**):

- Full Windows Release solution build: **zero warnings and zero errors**.
- **14 real PowerShell package-validation cases** passed, covering matching/mixed/missing/malformed release files, an incorrect DLL/build version, configuration-version errors and preservation of installation-owned state.
- Build and publish replaced the deliberately newer stale release files; published release JSON hashes matched the source. The published package passed the same DLL/template/manifest verifier.
- All **2,408 xUnit tests** passed (zero failed/skipped), publish/Data-exclusion checks passed, and all **29 Node/Edge suites** passed, including **8,849 Host** and **3,103 Sync/Traditional Task** layout checks.

The subsequent documentation-only result recording does not change the tested application, packaging scripts or workflow. Live IIS/MOVEit acceptance remains separate. A complete tested publish must still be copied to the destination; the verifier cannot guarantee a later manual transfer includes every file.

#### 0.18.493 RC3 validation

The test-project compile errors are corrected: the checkout lease tests import `System.Net`, and the Traditional-to-Advanced conversion test uses the renamed `PendingRequest` property.

[Windows CI 40243163063](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40243163063) completed successfully on master for application/test commit `e1d51bba29b3c184cfca5c03a4e35ab195e08254` (**0.18.493 RC3, Build 20261025**):

- Full Release solution build: **zero warnings and zero errors**.
- xUnit: **2,408 passed, zero failed, zero skipped**.
- Publish checks: IIS in-process configuration, release templates and exclusion of installation-owned Data passed.
- All **29 registered Node/Edge suites** passed, including **8,849 Host** and **3,103 Sync/Traditional Task** rendered-layout checks.

The documentation-only result recording does not change the tested application or test sources. This workflow does not perform live MOVEit or deployed IIS acceptance; validate native checkout, administrator override, save and cleanup against the target environment before production use.

#### 0.18.489 RC3 validation

[Windows CI 40136010743](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40136010743) completed successfully for application/test commit `1b8e22cfa3df05e0600e66fbbda4016fd8293467` (**0.18.489 RC3, Build 20261024**). The later 0.18.491/0.18.492 checkout changes were committed with CI skipped; this historical result does not validate those C# changes. Those changes are covered by the subsequent [0.18.493 validation record](#018493-rc3-validation).

[Windows CI 39803386874](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39803386874) passed for application/test commit `7b11d346b0614afdb4bdb17786506e2a494e28db` (**0.17.474 RC3, Build 20261022**): Release build/publish with zero compiler warnings/errors, **2,268 xUnit tests passed** (none failed/skipped), all registered Node/Edge suites, **170 MOVEit Settings checks**, **8,549 Host layout checks** and **2,368 Task layout checks**. Local Configuration checks covered section ownership/order, hidden editor values, preserved configuration definitions, Settings dirty tracking, folder action permissions and editor routing. The complete Settings layout and live MOVEit behavior still require the acceptance checks in [TESTING.md](TESTING.md).

### RC2 validation archive

Historical Windows CI identities consolidated from TESTING.md during RC2 cleanup. Each record describes its own source commit and release, not the current candidate. Detailed feature and coverage notes remain in the corresponding version history above; current acceptance and test commands belong in TESTING.md.

| Version | Build | Windows CI run | Application/test commit | Passed xUnit tests |
| --- | --- | --- | --- | --- |
| 0.17.458 | 20261020 | [39532100235](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39532100235) | `4a7c10f4f56869000d065a150186b9e442dc5814` | 1,991 |
| 0.17.457 | 20261020 | [39526969992](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39526969992) | `4d31aa5b7904b2c6383d7753a0e9541918c6856b` | 1,991 |
| 0.17.456 | 20261020 | [39525022604](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39525022604) | `a20b2f45b6c4c8e6ce208fe396fbf1b24ecbf921` | 1,991 |
| 0.17.455 | 20261020 | [39523493181](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39523493181) | `49d6195fadce7f1b4aa9f5f45fddb9427b6357e2` | 1,991 |
| 0.17.454 | 20261020 | [39519278262](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39519278262) | `ca79714d1d6d10a6d559a49c01fe1e36fe560777` | 1,977 |
| 0.17.453 | 20261020 | [39516540753](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39516540753) | `90003ce98519cf6deb5710296fa96739c3815a5f` | 1,977 |
| 0.17.452 | 20261020 | [39512886390](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39512886390) | `3dc885c685cdefa0fdd304bd6046f368d1c623f6` | 1,960 |
| 0.17.451 | 20261020 | [39488971164](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39488971164) | `3d225309b469038ffa3f82ed38d4c3d1eb431372` | 1,939 |
| 0.17.450 | 20261020 | [39485964427](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39485964427) | `b6a3ed0e449ec3e04e5f17b2d739b3a0e62b274e` | 1,939 |
| 0.17.449 | 20261020 | [39480930530](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39480930530) | `8a3e2f79b2072bd7708ef7191efb119467ef2d8e` | 1,929 |
| 0.17.448 | 20261020 | [39476972524](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39476972524) | `eae8dc0f521871c0a98211659dc0ad97cde0b605` | 1,924 |
| 0.17.447 | 20261020 | [39473423538](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39473423538) | `65ede77c7e5810df1f1bbeb8e5494a6ff1c2c509` | 1,907 |
| 0.17.446 | 20261020 | [39467551208](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39467551208) | `51c6f77e4d0603f0741361bad0a02bdb2c17a6ae` | 1,897 |
| 0.17.445 | 20261020 | [39458871370](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39458871370) | `499d367b5bc03ba6bc5388268c578a160d586c32` | 1,892 |
| 0.17.444 | 20261020 | [39446304426](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39446304426) | `09ba1eaa2ad0e4675879da7a7f45ebf1e3775f40` | 1,851 |
| 0.17.443 | 20261020 | [39440317932](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39440317932) | `aba4ed797516dc67f819c5676f623d7bf7f6ca08` | 1,849 |
| 0.17.442 | 20261020 | [39436335871](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39436335871) | `08f21145ea840e53e0409c5063f63232fa27c71b` | 1,849 |
| 0.17.441 | 20261020 | [39432861585](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39432861585) | `b623d6ef626bc9753d795ef4966b53d82ebfab13` | 1,845 |
| 0.17.440 | 20261020 | [39431194406](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39431194406) | `69c51b506c3a487cdacf2e84161184d14bf22c77` | 1,845 |
| 0.17.438 | 20261019 | [39348128523](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39348128523) | `cdca296907c33d166755991fe23d08c905d5cfaa` | 1,832 |
| 0.17.437 | 20261019 | [39345069294](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39345069294) | `fc45331dbf154de7bdf487ddeb0a44583160d6d2` | 1,832 |


#### Historical Windows CI details

[Windows CI 39715786974](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39715786974) passed for application/test commit `44e757c4cdc0627954e614d3d622ec4408e98a71` (**0.17.468 RC2, Build 20261022**): zero build warnings/errors, **2,210 xUnit tests passed with none failed/skipped**, all registered Node/Edge suites passed, including **63 MOVEit Settings checks**, **8,301 Host layout checks** and **2,368 Task layout checks**. New real MVC/HTTP tests deliberately leave editor options unbound to reproduce production startup, then enable the live preference, load populated Logging values, save changes, and disable access again in the same running host at both root and `/maui`. Administrator, antiforgery and method restrictions are retained.

[Windows CI 39713972081](https://github.com/marcrdavis/MAUI-RC2/actions/runs/39713972081) passed for application/test commit `dbecede8b709030d1accbb8199371f2eb2bc2ac3` (**0.17.467 RC2, Build 20261022**): Release build/publish with zero warnings/errors; **2,207 xUnit tests passed, zero failed/skipped**; **63 rendered MOVEit Settings checks**; all existing Node/Edge suites passed, including **8,301 Host layout checks**. The new suites include JSON casing/long-value round trips, preservation of other Globals sections, native PUT retaining or releasing checkout, rejected readback ownership, conflict and uncertain-response handling, independent cleanup cancellation, saved preference routing, directory browsing, dirty/discard behavior and layout at 1920/1000/800/600 widths in both themes. Modal images were visually reviewed. Native Globals endpoint acceptance remains a live test.
