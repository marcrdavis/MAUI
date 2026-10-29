# MAUI Testing Guide

- **Version:** `0.18.511-RC3`
- **Release:** Release Candidate 3 (pre-release)
- **Build:** `20261029`
- **Database schema / configuration version:** `1` / `1`

> **Release Candidate 3 — not the final release.** Bugs and regressions may remain. Passing CI validates the covered contracts and fixtures; it does not certify the candidate as bug-free or replace testing against your MOVEit environment.

The development regression suite runs outside IIS. Automated tests use fixtures, mocks and rendered Razor dialogs; they do not execute live MOVEit writes. Windows CI builds C#/Razor, runs xUnit, publishes the application and runs Node/Edge checks. A passing run does not certify credentials, protocol behavior, IIS deployment or the target MOVEit build. Complete the live acceptance matrix below before production use.

**In this guide:** [Running validation](#running-validation) · [Tasks, Hosts and checkout](#tasks-hosts-and-checkout) · [Resources and permissions](#resources-and-permissions) · [Imports, exports and environment copy](#imports-exports-and-environment-copy) · [Configuration and operations](#configuration-and-operations) · [Grids and modal consistency](#grids-and-modal-consistency) · [Automated coverage catalogue](#automated-coverage-catalogue) · [Maintaining tests](#maintaining-tests)

## Running validation

### Current validation

0.18.511 adds the explicit Task JSON save warning. Controller regressions verify that every affected Task captures undo before its save and that snapshot failure prevents the write. Browser checks cover the exact warning, shared destructive Proceed button options, Cancel preserving the draft without a write, and the existing reviewed save workflow. [Full Windows CI](https://github.com/marcrdavis/MAUI-RC2/actions/runs/40965532186) passed for application/test commit `b77f056ca59e7dbd0fb804a606c8ebf448b1d591`: zero build warnings/errors, all 2,591 xUnit tests (zero skipped) and all Node/Edge suites, including 131 Task JSON browser checks. The result-recording follow-up changes documentation only. No live MOVEit or deployed IIS testing is claimed.

Historical release results through 0.18.510 are in the corresponding [RC3 changelog entries](CHANGELOG.md#rc3-release-overview). Each applies to its exact tested commit, not later releases.

### Historical validation

<a id="validation-recorded-for-018496-rc3"></a>
<a id="validation-recorded-for-018497-rc3"></a>
<a id="validation-recorded-for-018498-rc3"></a>
<a id="validation-recorded-for-018499-rc3"></a>
<a id="validation-recorded-for-018500-rc3"></a>
<a id="validation-recorded-for-018501-rc3"></a>
<a id="validation-recorded-for-018502-rc3"></a>
<a id="validation-recorded-for-018503-rc3"></a>
<a id="validation-recorded-for-018504-rc3"></a>
<a id="validation-recorded-for-018505-rc3"></a>
<a id="validation-recorded-for-018506-rc3"></a>
<a id="validation-recorded-for-018507-rc3"></a>

<a id="validation-recorded-for-018495-rc3"></a>
<a id="validation-recorded-for-018494-rc3"></a>
<a id="validation-recorded-for-018493-rc3"></a>
<a id="validation-recorded-for-018492-rc3"></a>

Earlier exact build results are retained in the changelog: [0.18.495](CHANGELOG.md#018495-rc3-validation), [0.18.494](CHANGELOG.md#018494-rc3-validation) and [0.18.493](CHANGELOG.md#018493-rc3-validation). The 0.18.493 record covers the preceding checkout changes and their test-project compile fixes. Historical results do not validate later code by implication.

### Project layout and CI

`MoveItAutomationUI.sln` builds the production `MoveItAutomationUI.csproj` and `MoveItAutomationUI.Tests/MoveItAutomationUI.Tests.csproj`. The production project excludes the test subtree and its build output. Test-only packages must not appear in the deployed application.

The authoritative workflow is [MAUI Build and Tests](.github/workflows/maui-build.yml). It restores and builds on Windows/.NET 8, runs all xUnit tests, verifies published Task/Host templates and Data exclusions, runs the dependency-free Node suites listed below and renders Host, Task, Script, Date List, Resource Information and Groups & Permissions fixtures in Edge. Test results are uploaded as the MAUI-Test-Results artifact. Edge fixture checks exercise production views/CSS/JavaScript in both themes at suite-specific widths, not a live MOVEit connection.

### Release validation evidence

Validation applies to an exact application/test commit. Historical green CI does not establish that the current candidate passed compilation, xUnit, rendered-browser or live MOVEit acceptance. See the [validation history](CHANGELOG.md#validation-history) and [current validation record](#current-validation).

#### Node-only checks

Use Node.js 22, matching the workflow. These source/runtime checks use Node and local fixtures; they do not require an installed browser or a live MOVEit server. Run from the repository root and check every command's exit status.

```powershell
node MoveItAutomationUI.Tests/Browser/grid-date-filters-smoke.cjs
node MoveItAutomationUI.Tests/Browser/moveit-grid-connection-smoke.cjs
node MoveItAutomationUI.Tests/Browser/object-access-smoke.cjs
node MoveItAutomationUI.Tests/Browser/task-json-model-smoke.cjs
node MoveItAutomationUI.Tests/Browser/editor-actions-smoke.cjs
node MoveItAutomationUI.Tests/Browser/browser-notifications-smoke.cjs
node MoveItAutomationUI.Tests/Browser/modal-ui-smoke.cjs
node MoveItAutomationUI.Tests/Browser/editor-routing-smoke.cjs
node MoveItAutomationUI.Tests/Browser/shared-traditional-crud-contract-smoke.cjs
node MoveItAutomationUI.Tests/Browser/traditional-task-editor-dependent-spacing-smoke.cjs
node MoveItAutomationUI.Tests/Browser/configuration-folders-smoke.cjs
node MoveItAutomationUI.Tests/Browser/task-filename-search-smoke.cjs
node MoveItAutomationUI.Tests/Browser/host-spreadsheet-import-contract-smoke.cjs
node MoveItAutomationUI.Tests/Browser/host-import-ui-parity.cjs
node MoveItAutomationUI.Tests/Browser/host-editor-captured-profiles-smoke.cjs
node MoveItAutomationUI.Tests/Browser/task-spreadsheet-import-smoke.cjs
node MoveItAutomationUI.Tests/Browser/host-editor-save-smoke.cjs
node MoveItAutomationUI.Tests/Browser/task-editor-host-sort-run-as-of-smoke.cjs
node MoveItAutomationUI.Tests/Browser/upgrade-smoke.cjs
node MoveItAutomationUI.Tests/Browser/object-edit-lease-smoke.cjs
node MoveItAutomationUI.Tests/Browser/resource-create-return-smoke.cjs
```

#### Rendered browser checks

These checks additionally require installed Edge or Chromium. `MAUI_BROWSER_EXECUTABLE` can select an executable; the suites otherwise try their supported default paths. Host, Task and Groups rendered suites require fixtures emitted by xUnit. In a clean Windows source tree, complete the Release build first, then set the same fixture variables used by CI **before** running xUnit:

```powershell
$mauiSourceRoot = (Get-Location).Path
$env:MAUI_HOST_EDITOR_FIXTURE_PATH = Join-Path $mauiSourceRoot 'TestResults/HostEditorFixtures.json'
$env:MAUI_SYNC_EDITOR_FIXTURE_PATH = Join-Path $mauiSourceRoot 'TestResults/SyncEditorFixtures.json'
$env:MAUI_GROUPS_PAGE_FIXTURE_PATH = Join-Path $mauiSourceRoot 'TestResults/GroupsPageRuntimeFixture.json'
dotnet test .\MoveItAutomationUI.sln --configuration Release --no-build
```

Then run the rendered suites from the same source root:

```powershell
node MoveItAutomationUI.Tests/Browser/task-filename-search-browser.cjs
node MoveItAutomationUI.Tests/Browser/task-json-editor-browser.cjs
node MoveItAutomationUI.Tests/Browser/backup-restore-browser.cjs
node MoveItAutomationUI.Tests/Browser/script-editor-browser.cjs
node MoveItAutomationUI.Tests/Browser/date-list-editor-browser.cjs
node MoveItAutomationUI.Tests/Browser/system-settings-editor-browser.cjs
node MoveItAutomationUI.Tests/Browser/global-parameter-information-browser.cjs
node MoveItAutomationUI.Tests/Browser/environment-copy-browser.cjs
node MoveItAutomationUI.Tests/Browser/groups-and-permissions-browser.cjs
node MoveItAutomationUI.Tests/Browser/host-editor-layout.cjs
node MoveItAutomationUI.Tests/Browser/sync-task-editor-layout.cjs
```

The [registered Windows workflow](.github/workflows/maui-build.yml) is authoritative for script order, environment, publish verification and artifact collection. It generates fixtures and runs the suites as one gate; manually running a subset does not replace that gate.

### Clean build requirements

Validate RC3 from a clean source tree. Extract the package to a **new empty source folder** or remove existing `bin` and `obj` directories before building. Stale build output from earlier development packages is not part of the release and should not be reused.

A clean command-line validation is:

```powershell
dotnet clean MoveItAutomationUI.sln
dotnet restore MoveItAutomationUI.sln
dotnet build MoveItAutomationUI.sln --configuration Release --no-restore
dotnet test MoveItAutomationUI.sln --configuration Release --no-build
```

The test project treats C# compiler warnings as errors so a warning-clean test build is required before the suite runs.

### Running the tests

From the source root:

```powershell
dotnet restore MoveItAutomationUI.sln
dotnet build MoveItAutomationUI.sln --configuration Release --no-restore
dotnet test MoveItAutomationUI.sln --configuration Release --no-build
```

To collect code-coverage data:

```powershell
dotnet test MoveItAutomationUI.Tests/MoveItAutomationUI.Tests.csproj --collect:"XPlat Code Coverage"
```

### Deployment

Publish the production project, not the solution or test project:

```powershell
dotnet publish MoveItAutomationUI.csproj --configuration Release --output .\publish
```

No xUnit, test-runner, or coverage assemblies should be present in the normal MAUI IIS deployment.

The production and test projects use EF Core 8.0.31 and pins `SQLitePCLRaw.bundle_e_sqlite3` 2.1.13 in both production and test projects so the test runtime matches the shipped SQLite native dependency.

### RC3 release validation gate

Before packaging or publishing RC3 from source, require all of the following. The package should not be treated as release-ready until this gate is complete:

1. `dotnet restore` succeeds.
2. Release solution build succeeds without unresolved compiler/analyzer warnings.
3. All automated tests pass with **0 failed** and **0 skipped**, unless a future release explicitly documents an intentional skip.
4. Production `dotnet publish` succeeds.
5. The publish directory contains no xUnit/test-runner/test-project assemblies.
6. Re-run SAST/SCA against the clean source/resolved dependency output; do not leave unresolved Critical/High findings, and document any accepted false positives. Avoid duplicate SAST analysis of generated publish copies when the corresponding source files are already scanned. See [`SECURITY.md`](SECURITY.md) for the RC3 scan-scope and false-positive rationale.
7. Complete the manual smoke test below against an appropriate MOVEit test/non-production environment before production deployment.

### Manual RC3 smoke test

Automated tests deliberately do not replace live product validation. Before a public/production RC3 deployment, validate at least:
- On a clean Initial Setup, test the MOVEit server with the COM API installed and confirm the status reports both Web Admin connectivity and COM activation. Repeat once on a disposable/test installation with `MICentralAPICOM.MICentralAPI` unavailable or inaccessible and confirm Setup fails with a specific COM installation/registration/architecture message. No MOVEit credentials should be requested by this test.
- Script and Date List import previews: with a valid package whose object already exists, confirm **Create new** reports **Package is valid** plus an import-mode blocker, then switch to **Update existing** and confirm the blocker clears when the same-name target is eligible. Test the inverse case where Update Existing has no target.

- Login, logout, idle/session-expiration behavior, and MAUI Admin/User authorization.
- Dashboard plus representative Tasks, Hosts, Resources, Records, Reports, and Configuration grids.
- Native MOVEit editor launch and Undo Last Change on a safe test object.
- Task Run/Preflight and Host connection-test paths appropriate to the environment.
- Task Notifications in both Text and HTML modes, including legacy and explicitly formatted templates.
- Report Run Now and scheduled delivery with the configured SMTP server; verify normal users cannot use administrator-only Report sources.
- Backup creation, inventory/download, retention cleanup, and a controlled restore validation appropriate to the environment.
- System Jobs, including Reports, Backup Cleanup, Host Health Check, and Job History Cleanup.
- SQLite or SQL Server metadata behavior for the provider actually deployed. For SQL Server, validate the selected authentication mode.
- Chrome and Edge rendering/navigation, including saved grid state and native-editor return paths.
- IIS recycle behavior: existing transient sessions should require reauthentication when the server-side MOVEit session is lost.

### Task JSON Editor acceptance

Enable Task JSON Editor under Experimental, save configuration and reload Tasks. Verify it is absent when off and denied without Task Edit access. Select mixed Traditional/Advanced/Sync Tasks; confirm collapsed trees, protected IDs/credentials, typed edits and unchanged fields. Find OnlyUntilFirstSuccess, preview 0 to 1, uncheck matches, apply locally, review and save. Verify full-batch validation precedes writes, saved values match MOVEit, failed Tasks are highlighted and unaffected saves are not repeated. Test both checkout modes, close/abandonment cleanup, administrator override, concurrent edits, a lost save response and an overnight Schedule pair. Validate all changed Tasks in MOVEit before running them. The session limits are 100 Tasks / 16 MiB total, 1 MiB / 20,000 nodes per Task and 2,000 changes per Task.

## Tasks, Hosts and checkout

### RC3 editor and CRUD acceptance

Advanced flow (+) insertion controls must never start drag moves at the root, loop or conditional branches. Normal editable step rows must still move, including after branch collapse/expand.

Use disposable Tasks/Hosts on the actual MOVEit build and both MOVEit administrator and ordinary-user sessions as appropriate. Preserve the normal MAUI roles and license gates. The current transport/ownership reference is in [CHANGELOG.md](CHANGELOG.md#shared-crud-and-integration-reference).

| Area | Required live checks |
| --- | --- |
| Preferences and navigation | Toggle Task and Host editors independently in Settings > Editors. Task covers Traditional/Advanced/Sync together. Verify grid, Calendar and related Information links honor the preference; closing a Task launched from a Host returns to that Host. Unsupported types retain native routing. |
| Task creation and common changes | Create disabled Traditional through v1 and Advanced/Sync through v0; check unique names, exact returned IDs and authoritative GETs. Save Information and enable/disable all kinds. Bulk operations over eight Tasks must report actual completed/skipped/failed outcomes. Manually run a disabled Task and compare Success/Failed/No Transfers grids with native MOVEit. |
| Traditional editor | Add/edit Source, Process and Destination steps; confirm untouched visible defaults, including File(s) wildcard, are actually saved. Test schedules (including native overnight pairs), notifications and combinations, parameters, source timestamps, transfer exceptions, credential preservation and save/readback. |
| Advanced editor | Add/clone/move/delete nested steps and branches, edit all supported properties, save and reload; verify IDs, order, NextEID and unknown fields. Save Source/Destination steps and their default masks. After a rejected save, unsaved values remain selectable/editable while unsafe saving stays blocked. Confirm Reload and MOVEit-admin-only Override Checkout recovery; uncertain write outcomes require Reload. Export Save Task Flow with all nested content and Start/End. |
| Traditional conversion | Create Advanced Task from several Sources, per-file/process-as-destination processes, Destinations, once-after-download processes, multiple schedules and task-level/Each File actions. Verify a unique Advanced-suffixed disabled copy, correct loop/action placement and native settings. File actions test ErrorCodeFile 0/>0; per-file No Transfer is omitted. Original Task remains intact. |
| Sync editor | Configure folders/Host changes, direction, comparison/deletion policy and overrides. Exercise Clear Sync Folders, Clear Sync Listings and View Sync Preview separately on disposable state. Verify preview/reset versus save/run behavior and that removed Sync-only menu/tab entries stay absent. |
| New Host | Create each of ten mapped types through v0, including delayed individual GET visibility. UNC requires Resource UNC and rejects duplicate connections; switching away hides the field. Inspect native defaults and exact name/type/ID; no duplicate POST after ambiguous responses. |
| Advanced conditions | Confirm filename and Custom Macro dropdowns offer Matches (`MASK`) and Not Matches (`NOTMASK`). Reopen a captured All/Any condition combining file error code and a filename Not Matches rule; change/save it inside a File Loop and compare native criteria. Verify the flow summary and that readback detects an incorrect operator. |
| Mail Task overrides | In Traditional and Advanced SMTP Destination and POP3 Source steps, save/reopen Microsoft Office 365, Custom OAuth and Legacy credentials. Verify Tenant-to-token URL/scope composition, Custom Microsoft scopes and noncanonical/query-string token endpoints preserved, unchanged blank secret retained, explicit replacement applied, required secret for new/mode-changed overrides, Host changes/clones, and returning to Host defaults. Confirm save readback accepts suppressed secrets and still detects changed identities/non-secret fields. Run these captures against live MOVEit. |
| Host save and credentials | Test editor and Information saves, including flat GETs with omitted type. Verify unrelated fields/groups survive; blank unsubmitted secrets preserve, explicit replacements round-trip. Cover UNC, MOVEit, FTP/FTPS, SFTP keys/fingerprints/algorithms, POP3/SMTP basic/OAuth/TLS, SharePoint authentication/library, S3/Azure/Google roots and integrity. For Local File System Host 0, edit Description and captured General/Uploads/Limits/Advanced settings, preserve its fixed name and verify the create/clone/delete/export restrictions. For S3, toggle Alternative Provider, save a scheme-less URL and a URL with scheme/port, reopen and compare native fields; a blank active URL must fail before PUT, and disabling must preserve the inactive URL. |
| Host TLS and SFTP controls | With FTP and MOVEit Transfer TLS set to None, confirm Client Certificate controls remain visible and saved selections survive. In SFTP Transfers, save/reopen Disable Compression as Yes (`CmpL: 0`) and No (`CmpL: 6`); the SSH Algorithms panel must not contain a duplicate Compression control. |
| Local Task steps and Browse variants | Create/edit local Sources and Destinations in Traditional/Advanced, including loops and changes to/from network Hosts. Verify Host ID 0, actual default filespecs, saved Host defaults, absolute local Browse paths, retry/rescan/limit/timestamp overrides and no network credential fields. Open Edit Host 0 and return without losing Task edits. Exercise Google HMAC/OAuth, SharePoint App/Certificate Access, blank Site, and every proxy choice exposed for each supported Host. Compare native listings; inspect server errors, release checkout on failure and verify stored/replacement secrets never reach browser responses. These variants extend existing envelopes and need live acceptance. |
| Checkout and concurrency | With edit-duration checkout Off, verify save-scoped checkout and stale-change rejection. With it On, verify checkout on open for every editable Task editor, Host 0/other Hosts, Script and Date List, retention after manual/Auto Save, separate nested-editor leases and release on Close/logout. Override the native checkout from another MOVEit administrator and save there; MAUI must block the stale save. Cancel/confirm Reload and administrator-only Override Checkout; ordinary users must not have takeover authority. Override retains form values but never auto-saves; manual Save uses a fresh revision and Undo baseline. Further concurrent changes remain blocked. |
| Abandoned checkout recovery | Close a tab, lose connectivity, expire the login and restart MAUI; verify cleanup after the five-minute heartbeat deadline or login-idle deadline, without reviving a user session. Test failed release, unavailable/expired cleanup credentials, fresh owner login and retained retry records. Old retired ownership must never release a later editor's checkout. Restrict and preserve Data/ObjectEditLeases and Data/DataProtection-Keys. |
| Uncertain outcomes | Interrupt acknowledgement/readback/release after dispatch and inspect native state. No automatic POST/PUT replay or transport fallback. Preserve known ID, refresh inventory and show modal-local recovery. Reload must inspect the authoritative object before another save; do not infer success from an HTTP response alone. |
| Retained native paths | Confirm configured Host spreadsheet create/update, Clone Host, Task clone, native XML/schedule operations, Undo/Recycle restore and native editor launch still work with COM installed. Verify capacity, dependency and delete guardrails. |
| UI and local writes | Check footer No unsaved changes/Unsaved changes/Saving/Saved, error precedence and Close/reopen timer isolation. Check notification-template failures stay in the active modal, Information read-only fields/Type/ID alignment, Close labels, checkbox/ellipsis centering and menu arrows. Validate metadata first and distinguish a native success followed by a local audit/metadata failure. |

### Task filename search acceptance

- Load Tasks with an empty search and confirm the filename endpoint is never called during initial loading or normal automatic refresh. Type rapidly: only one bulk filename read should start after a 300 ms pause. The endpoint uses the current user's visible Tasks and requests only ID/steps; no checkout or native writes occur.
- Search a stored Source mask such as `*.txt`, a Destination filename/macro, a nested Advanced When/Otherwise/File Loop filename, and each Sync folder's mask. Check case-insensitive text, OR and NOT, existing Task-name/description/metadata search, current view/folder/column filters, paging and row replacement. A full hypothetical filename must not be evaluated against a wildcard mask.
- Verify filename searches show no loading spinner or loading text and reserve no extra status line. The search control row should remain 44px high during successful reads. Failures alone show a standard page-origin error notice, with no inline grid message or Retry control. With a modal open, the notice belongs in its footer and is labelled Page update. The folder selector, Manage Folders, searchbox and counter must share the same vertical center before/during/after lookup and after error toasts. Counters/table geometry must remain stable in both themes at desktop and narrow widths. Existing matches remain usable while enrichment is loading.
- Repeated searches and ordinary grid refreshes reuse the five-minute open-page cache. Manual Refresh and returning from the MAUI editor invalidate it; native changes are found after expiry or manual Refresh. No filename index is persisted in browser storage or the database.
- Clear or change the search during a delayed request, invalidate during a read and return with browser Back/Forward. Late results must not override a newer index, produce an obsolete error toast or expire the current session from an obsolete unauthorized response. Invalidate while JSON is loading and verify the old body is not indexed. Empty global searches must skip per-row search-text reads, and repeated renders must not repeat a failed read's error toast. HTTP, network, invalid JSON and incomplete responses must show one standard error toast explaining that results include Task details only. Change the query or use Refresh Now to retry; clearing/retyping must also work. Test an open unrelated modal: the asynchronous grid error must appear in the active modal footer as a labelled Page update, preserving native editor error/dirty state. No toast may remain visible behind the modal. No automatic retry loop or false successful empty filename index is allowed. After a failed read, dispatch/perform a Back/Forward cached-page restoration with an active search and confirm one fresh read succeeds without repeating the old error toast. Restore an empty search and confirm no filename read occurs. Known malformed For/If/Otherwise containers must fail the read, while omitted/null optional containers and unknown step types retain valid filenames. An upstream timeout must return the safe JSON error; a cancelled browser request must propagate cancellation.
- Validate with an administrator and a non-admin/read-only user. Invisible Task filenames must not appear in the response or create grid rows. Test root and IIS virtual-application URLs and expired sessions. Measure the on-demand bulk response/time with the target environment's Task count before broad rollout; automated fixtures do not establish live MOVEit performance.

### Shared New Host acceptance

Open New Host from Hosts and from each Quick Transfer panel. Verify matching branding and controls, Resource UNC required only for UNC Share, name formatting and inline errors. Confirm non-admin Hosts creation requires an allowed Resource Group, with admins seeing no Resource Group field. Quick Transfer itself remains MOVEit-admin-only. Test both Host editor preferences and confirm the transfer draft remains in place, the new Host selects in the originating Source/Destination dropdown and edited Host details refresh on return. Confirm uncertain creation responses do not permit duplicate submissions.

### Native editor return acceptance

From a MAUI Task Editor, use Edit Host while the Host preference selects MOVEit. Verify that the Task modal keeps its layout and draft when returning, with no opening/sign-in/opened banner or toast. Close the native Host window and verify Host properties refresh and Task editing resumes. Also check native Task, Script, Date List and navigation launches; blocked popups, unavailable sessions and login/skin failures must remain visible as errors. Local production-handshake, shared routing, PathBase and modal feedback checks passed.

### Slow Host diagnostics and deployment settings

- Test a Host whose native operation takes over 60 seconds from Test Host, Preflight and the scheduled Host Health job. Confirm the shared diagnostic budget honors connection/data/retry settings (5–30 minutes), the operation finishes before the result is reported and the acquired checkout releases. Cancel/leave the caller during a test; cleanup must use its independent budget and must not DELETE a borrowed editor lease or another user's checkout.
- Simulate real network loss, request timeout and release failure. Preflight must show the Host error in its results; Test Host must show the row failure, and scheduled health must record the result. A genuine release failure remains visible; no operation is silently replayed. Automatic Task/Host grid refresh defers during open running diagnostics, resumes afterward and preserves Pause state.
- Import several spreadsheet batches, including failures/unverified outcomes. Cancel stays until all rows have been attempted, then becomes Close and stays Close through paging/refresh. Reopening or changing input restores Cancel.
- Verify both appsettings.json copy properties are Never, and fresh output/publish excludes it. Create `{}` manually before a fresh deployed startup; upgrades preserve existing settings/Data and deploy from clean staging output. Never clean the installed Data directory to clean a package.

### Shared Host browser acceptance

- Task editors and Quick Transfer must use the same modal and sortable columns. In Traditional, Advanced and Sync Sources/Destinations, double-click a file and confirm folder/filename values; Destination must clear Use original filename. Select a folder and OK without changing filename settings. Undo the combined browse selection as one edit. Verify Cancel/Escape changes nothing.
- Exercise local drive paths, UNC-relative paths, FTP/SFTP, MOVEit Transfer and cloud bucket/container/library selection. Quick Transfer must preserve the selected cloud root and invalidate its prepared request only after applying a selection.
- As a MOVEit administrator, open Host Actions > Browse Host below Test Host. It is unavailable for POP3/SMTP and unsupported types. Inspect folders/files, sort and close back to the unchanged Host editor; no hint, selected-value writeback, Task creation or Host save should occur. Check both checkout modes, especially that an inspection with an edit-duration lease does not release its checkout.
- Delay a listing, cancel and reopen for another caller. The old result must not replace or close the new modal. Listing failures remain inside Browse, keep selection disabled and allow Close/retry. Validate both themes and narrow/short windows. The Host/Task Edge fixtures exercise shared selection, inspection, stale-response handling and caller returns with mocked transport; live Host listings remain a separate acceptance step.

### MOVEit 2026 Host acceptance

- In a mixed 2025.x/2026.x installation, confirm COM API 18.x or a later compatible release is installed on the MAUI host, restart its pool after updating COM, and validate retained COM operations against each environment. REST fixtures do not establish COM compatibility.
- Connect to a 2025.x environment and an environment whose version cannot be recognized: OneDrive creation/navigation and S3 IAM choices must be absent. Verify the existing S3 access-key controls still appear and save. Crafted unsupported REST requests must be rejected before dispatch.
- Connect to 2026.0 / native engine 18.0 or later: create a OneDrive shell, then configure each authentication method. Save/reopen, verify certificate/client-secret selection, update unrelated properties without replacing credentials, and test/browse through the shared modal. Confirm logininfo uses the client ID while the top-level username is the OneDrive user.
- For S3, test Access Keys, AWS Config File with a named and blank/default profile, and EC2 IMDS. Verify native AuthMethod 0/1/2, no alternate-provider controls in IAM modes, no access-key requirement in role modes, and no profile-name requirement for IMDS. Use the server's configured AWS file or EC2 role; MAUI does not add a new global AWS-path setting without its native payload.
- Browse the same S3 IAM Host from the Host editor and Task editors; verify bucket/folder selection and file selection. Save default-credential S3 Task steps and run disposable transfers. New OneDrive Task override/Quick Transfer composition requires native Source/Destination captures before its profile can be enabled.
- Reject creating/overwriting OneDrive or IAM Host configuration in older destination versions before writes. Retain existing compatible-target and Reuse Existing behavior.
- Test SharePoint App Access and Certificate Access with their supplied mode-specific test contracts, including blank/default Site, saved secret resolution and failed-authentication checkout release. Google OAuth test/browse uses the supplied requests; response parsing matches HMAC by the accepted assumption. S3 IAM response parsing matches Access Keys by the accepted assumption. Live S3/Google testing is unavailable; do not treat fixture coverage as live confirmation. For SharePoint App Access, test definitions both without certificate fields and with stale certificate values left after switching modes; neither should require a certificate.

### Checkout conversion and recovery regression

With Checkout Objects for Duration of Edit enabled, open a Traditional Task, clone it, and create an Advanced Task from the clone. Confirm the new Task is disabled, the clone's source checkout is not cycled during conversion, and Close/reopen works. Test both normal conversion and an interrupted new-object readback: inspect any known created ID before creating another copy; an uncertain new Task result must not invalidate the source clone's lease. Repeat near access-token renewal and verify Save, Close, abandoned-session cleanup and administrator Override Checkout. Checkout maintenance must adopt only tokens for the same active browser login/environment; it must not restore passwords, rights or idle activity into abandoned cleanup sessions. HTTP-success/native-error checkout acknowledgements must not create ownership or trigger a DELETE. Temporary heartbeat network/5xx failures retry; confirmed server expiry and server-side Save guards remain authoritative. From an open Host editor, Test Host and Browse Host must borrow its lease without releasing it; another caller cannot borrow that reservation. Check Configuration > Logs for acquisition versus revision-read details if Override Checkout fails. These fixture tests do not replace the exact sequence against the target MOVEit server.

### RC3 recovery and retention acceptance

- Short checkout: lose the acquisition or release acknowledgement and verify that no second PUT or blind cleanup POST/DELETE occurs. Confirm a durable record and a clear modal/Logs message. Verify journal-write failure blocks checkout dispatch and a confirmed native release remains successful if local record deletion needs retry.
- Run As Of: restart after temporary save, before run acknowledgement, during a run and during restore. Never replay the run. Restore only after completion is established, leave concurrently changed temporary fields/enabled state untouched, and retain the original Undo snapshot for review when ownership/authentication is uncertain. Verify recovery records and tokens are protected; malformed records must not prevent other recovery.
- Copy To: age reports/recovery files beyond retention without restarting; verify periodic deletion, preservation of recent/active copy material and downloads, and live retention changes. Periodic pruning must not mark a running operation Interrupted.

### Checkout conflicts and recovery inside MAUI

Test each Task editor, Host (including ID 0), custom Script and Date List with another native/MAUI editor holding checkout. Native owner/time details must appear in the MAUI confirmation/footer; non-admins cannot invoke Override Checkout. Confirm/cancel the opening recovery prompt and leave the page while confirming: cancellation must not acquire/release, and a superseded open must not take checkout. If a save result could not be verified, retain the open form, offer explicit administrator recovery, pause Auto Save and wait for manual Save with a fresh revision; no automatic write is permitted. Losing checkout to another administrator without a configuration change must not trigger DELETE or automatic POST takeover.

Test MOVEit Settings, Resource Groups and Global Parameter conflicts in their existing modals. Confirm recovery, verify no failed user operation is replayed, then reload/review the current form in MAUI. Test direct non-admin recovery POST rejection. Resource Groups recovery releases using its unchanged complete-dataset PUT; Global Parameters force-checkout/release must not call ModifyItem. Preserve the existing MAUI-only read-only/permission rules. Native details are shown only when returned; missing ownership information must not be invented. Simulate network loss/restart: failed-request cleanup records must remain internal and must not fabricate a lock owner/time. Retrying Open must make an ordinary native checkout request; only an actual MOVEit lock rejection offers the opening override prompt. Background cleanup never forces checkout.

### Explicit REST force checkout regression

Capture the HTTP request for confirmed Task, Host, Script and Date List Override Checkout: the native v0 checkout POST must contain exactly `{"force":true}`. Ordinary Open, Save reacquisition and background cleanup must not set force. Non-admins must dispatch no forced HTTP request. Use a locked Task to test opening the editor, confirmation, fresh shell loading and all three Task editor types; the editor must populate, retain normal checkout protection and perform no Save on recovery. Verify the lock message starts `This object has been checked out to` and preserves owner/time without the REST prefix. Settings and Resource Group recovery use the same explicit input with their existing release contracts. Complete live native acceptance for the supported types; browser/native transport fixtures do not contact MOVEit.

### Native checkout response and retry consistency

- For Traditional, Advanced and Sync Tasks, Hosts (including ID 0), custom Scripts and Date Lists: hold a native checkout, then open the MAUI editor. Show the current native owner/time and preserve the original editor when the second checkout is rejected. Clear the native checkout while retaining an old MAUI record and retry: normal checkout must succeed without administrator force. An older editor's delayed Close must not release the replacement editor's checkout.
- Lose a checkout response, then retry as an ordinary user and administrator. Do not show a separate unconfirmed lock or a recorded MAUI username/time. A communication failure shows a request error without an automatic override prompt. Once MOVEit responds, use its current checkout result.
- Cover close/logout, journal-write failure, process restart and pending-request journal files from earlier releases. Never issue DELETE for a request without a completed acquisition, and never force/replay writes in cleanup.
- Confirm one restored-checkout footer message across Task/Host/Script/Date List editors, with Auto Save paused until manual Save. Settings/Resource Group/Global Parameter recovery uses one close/reopen message and never replays the failed mutation. Preserve native lock messages from Script/Date List saves and COM operations.

- Leave a manual Override Checkout confirmation pending, then Close, navigate away or open another object before confirming. Repeat for Task, Host, Script and Date List: no override may be sent by the old confirmation. If override was already dispatched before Close, release its late acknowledged checkout without changing the newer editor's lease.
- Return the native 4015 text **This section is not checked out**, both in a checkout response and an HTTP error. It must not set `checkoutConflict` or prompt for force. Preserve positive native owner/time messages. Cover Tasks, Hosts, Scripts, Date Lists, Globals and Resource Groups.
- Fail the local checkout journal before PUT. No native update may be sent; the message must direct the user to Reload because the editing session is blocked.

### Quick Transfer validation

Quick Transfer depends on live MOVEit Host, Task, Script, key, browse, run-status, and report contracts, so complete this matrix against a non-production MOVEit environment in addition to the normal automated suite:

- Run a basic transfer for each installed/supported saved Host profile that the environment uses: UNC Share (`Share`), MOVEit Transfer (`siLock`), FTP/FTPS (`FTP`), SSH/SFTP (`SSHFTP`), Amazon S3 (`S3`), Azure Blob (`AzureBlob`), SharePoint (`SharePoint`), and Google Storage (`GoogleStorage`). Verify the generated Traditional Task contains the expected native Source/Destination Host IDs and location/root fields. Confirm the MOVEit `(Default)` Local File System Host (ID `0`) is absent from both Quick Transfer Host dropdowns and is rejected server-side if posted by a stale client.
- Verify Browse is offered only for Host types with a captured browse contract. For a saved Host whose authentication/proxy/blind-download shape is intentionally non-browsable, confirm the server-side browse request is rejected with the specific MAUI guidance rather than guessing a native request. For an otherwise supported root-based profile, verify leaving the root unselected uses the saved Host default root; manual folder/path entry must not make an unsupported transfer profile valid.
- Test Source wildcards/multiple-file filespecs. A fixed Destination Filename must be rejected unless it contains `[OrigName]`; leaving Destination Filename blank must preserve the source filename behavior. Confirm Source/Destination paths containing a relative `..` segment are rejected before Task creation.
- Run **Preflight** before a transfer. Confirm the Task is prepared but not started, the standard eight Task preflight stages run, and **Run Preflight Again** reuses the prepared Task safely. Editing the request before start must update the same named persistent Task when appropriate or replace the superseded temporary Task rather than accumulating stale prepared Tasks.
- With PGP licensed, validate **Encrypt** and **Decrypt**. Expired PGP keys must not appear in the Encrypt dropdown, and a stale request that posts an expired PGP key ID must be rejected server-side. Encrypt must require a selected PGP key and the saved Task must contain the selected key's native ID after MOVEit Task PUT; a missing/mismatched persisted recipient must block the transfer. Decrypt must use MOVEit's built-in `PGP Decrypt` Script. Repeat in a non-PGP-licensed environment and confirm Encrypt/Decrypt are visibly disabled, hovering either disabled card shows the MOVEit PGP-license tooltip, a reference refresh preserves the disabled state, and direct/stale requests remain blocked server-side.
- Exhaust or simulate the finite Task license limit and confirm Quick Transfer is blocked before creation with the standard capacity guidance. A Quick Transfer always requires one Traditional Task slot, even when the Task is temporary.
- Under Source > Filename / Filespec, confirm **Delete Source File After Successful Transfer** is unchecked by default. Prepare/run once cleared and verify the generated MOVEit Source step contains `DeleteOrig=0`; repeat with it checked and verify `DeleteOrig=1` and that MOVEit removes the source file only after the transfer succeeds. Change only this checkbox after Preflight/Prepare and confirm MAUI prepares a new Task contract rather than reusing one created with the opposite source-delete setting.
- Under Destination > Filename, confirm **Overwrite Existing File** is checked by default. Prepare/run once checked and verify the generated MOVEit Destination step contains `OverwriteOrig=1`; repeat with it cleared and verify `OverwriteOrig=0`. Change only this checkbox after Preflight/Prepare and confirm MAUI prepares a new Task contract rather than reusing one created with the opposite overwrite setting.
- With **Delete temporary task after successful transfer** enabled, confirm a successful temporary `ADHOC_*` Task is removed and its MAUI metadata is cleaned up. Confirm failed/cancelled/no-transfer Tasks are retained for troubleshooting. With **Create Traditional Task from this request**, confirm the named Task remains and normal MAUI Created/Modified audit metadata is recorded.
- Force an ambiguous error after the native start boundary (for example, an upstream timeout/5xx after MOVEit may have accepted Run Task Now). MAUI must not submit a second start automatically; status should move through the outcome-unknown/verifying path and the prepared Task must be retained for operator verification.
- Start a transfer and confirm the **Transfer** button becomes **Cancel** as soon as the prepared Task is being submitted. Cancel a visible running transfer and verify MAUI uses the matching `TaskID`/`NominalStart`, reports **Cancelled**, stops polling as an active run, and retains the generated Task. Repeat while the browser is still waiting for the native start response; the UI must remain responsive, request cancellation without resubmitting the start, and either confirm **Cancelled** or report the actual terminal MOVEit outcome if the run finished first.
- Create an ambiguous multiple-running-instance condition for a retained Quick Transfer Task and confirm cancellation does not guess which run to kill when no exact/current identity can be isolated.
- On a 1920×1080 display, confirm Quick Transfer uses the same page-heading title/description size, position, and spacing as Tasks/Hosts grid pages. Verify the three workflow cards remain on the first row, all three Task Options checkboxes share one row, the conditional confirmation recipients and Task Name fields appear below that row, and the options reflow into two/one columns at smaller widths. Transfer Status presents Task, Files, and Transferred on one compact summary row; no Temporary Task status field should be shown. Preflight and Transfer/Cancel must appear in the panel footer using the established modal-footer treatment; narrower displays must reflow without clipped controls or undersized body text.
- Verify running/progress/completion status is associated with the current Quick Transfer run rather than an older run of a retained persistent Task. Confirm successful completion reports file/byte totals when MOVEit supplies them.
- Validate the RC3 transient-state boundary in a controlled test: do not expect a Quick Transfer token to survive an IIS/application recycle. If a run was already submitted, confirm MOVEit continues/records the authoritative run independently and that the operator can find it through normal Task Run/File Activity records. Confirm any temporary `ADHOC_*` Task left because MAUI did not observe successful completion is retained for explicit review rather than being silently assumed deleted.
- Use a short transfer that can leave the running-task list before Task Run history refreshes. The status should move to **Finalizing** without clearing the last verified byte progress, then resolve to the completed Task Run once it is visible.
- Validate running/status/Task Run correlation when MOVEit returns the same `NominalStart` with different timestamp formatting and when one current row temporarily omits `NominalStart`. If MAUI already knows a non-empty `NominalStart`, a different non-empty identity must never be substituted. Candidates outside the bounded start-time window and ambiguous multiple candidates must remain pending rather than being selected by guesswork.

## Resources and permissions

### Object permissions and read-only acceptance

Use MOVEit administrator, edit-existing-only, run-only and read-only accounts against disposable objects. Keep MAUI role and native MOVEit permissions distinct.

- Confirm New/import availability combines eligible Resource Group grants. Non-admin Task/Host/Script/TLS/SSH/PGP creation requires a selectable eligible Group as the first field above Name; administrators must not see that selector. Task/Host Auto Format Name stays directly below Name. Date List creation remains administrator-only.
- Exercise direct mutation endpoints as well as menus. Existing Task edit rights permit full step/configuration edits, while New/Delete/Run retain independent checks. Metadata, notifications and configuration exports require edit rights. Confirm native Group assignments survive MAUI portability round-trips.
- For Task, Host, Script, Date List and key/certificate grids, verify row/header checkboxes and Bulk Actions are unavailable without an offered permitted operation. Include missing inventory flags, run-only Tasks with no Run bulk command, mixed selections, filters/paging and refresh; valid selections may persist while actions stay blocked pending revalidation. Global Parameter management controls remain absent for non-admins.
- Open each preferred editor as a read-only user with stored Auto Save on/off. Configuration and MAUI properties/metadata must remain locked; mutation controls are hidden, read-only status appears only in the footer and Close/Escape does not prompt to discard. No edit snapshot, privileged native configuration read, checkout or write may occur. Verify Task HTML email bodies are read-only and a later editable opening restores HTML editing.
- Open Task, Host, Script, Date List and key/certificate Information with read-only rights. Save must be hidden before/after Tasks, Logs or dependency tab changes. An editable object opened next must recover normal Save visibility/readiness; no-fields/dependency-tab hiding must remain intact. Failed permission reads must not expose a write action. Verify related links, log filters and Close still work.
- Confirm non-admin users can view/filter Task folders but cannot open Manage Folders, alter Information/editor folder assignments or mutate folders through direct endpoints. Other permitted Task edits must save without rewriting folder placement; administrators retain management/assignment. Check the dark accent and matching translucent navigation/focus colors in both themes.
- Confirm all users can see/use Global Parameters, but only MOVEit administrators can open Information or delete. Non-admin Task bulk Create/Add Resource Group actions stay unavailable.
- Verify Task editor Run Now stays disabled for edit-without-run grants in Traditional, Advanced and Sync, and returns for a subsequent runnable Task with Run access. Direct Date List service mutations and native Undo snapshot/restore requests must reject missing edit/delete/create rights before native reads, checkout or writes; losing permission must not consume stored Undo.
- Test local Undo in Task/Host/Script/Date List editors: opening/reloading starts empty, one local field/step/metadata edit enables Undo, Undo restores only that edit and consumes availability, and another edit re-enables it. Saving must not substitute stored grid history. Close/reopen resets local Undo; grid Undo Changes retains its independent restore workflow.

The shared `object-access-smoke.cjs` covers effective-action selection and Save visibility for all six Information object kinds. Edge resource/editor suites cover actual tab changes, read-only close/metadata, native-read guards and editable/read-only reuse. Use the current source commit's CI result recorded in CHANGELOG.md for validation status. These fixtures do not replace live MOVEit/IIS acceptance.

### Resources / Script editor acceptance

The Windows gate runs `ScriptCrudTests`, `ScriptTemplateTests`, shared Script checkout lease cases and `script-editor-browser.cjs`. The rendered suite uses the production modal/routing/lease runtime with mock MOVEit transport and captures light/dark layouts at 1400, 800 and 600 pixels, plus the compact New form at 1920×1080. Checks cover Script/Properties/Tasks placement, metadata persistence, related Task routing, per-user Auto Save, close flushing, Undo, saved-version Export, dependency blocking and explicit Delete Anyway after recovery-copy failure.

For live MOVEit acceptance: enable Use MAUI Script Editor and Save Configuration; create and reopen both PowerShell and VBScript Scripts; verify literal quotes, backslashes, newlines and macros in Source; rename/change Description without losing Source or group assignments; import UTF-8/BOM-marked UTF-16 `.txt`/`.ps1`; apply all four templates; save a uniquely named template, reject a duplicate, explicitly overwrite it and edit/delete it from General Settings. Verify environment isolation and that deleted defaults stay deleted.

Repeat edits with edit-duration checkout off/on: checkout denial must not PUT, concurrent edits must block stale saves, a MOVEit administrator can explicitly recover checkout, uncertain results require inspection/reload, Close releases ownership, and abandoned sessions use the existing cleanup service. Confirm built-in Scripts stay read-only, unlicensed custom Scripts stay blocked, preference-off native editing still works, and Script Undo/Delete/Recycle guardrails remain intact. The new Script v0 checkout route follows the shared native resource pattern and needs live server validation; failed acquisition never falls back to an unlocked save.

Verify the Edit header uses the saved name and Script icon; Properties shows Type/ID on the left, MOVEit/MAUI grouping and read-only audit fields. Check Script Auto Save independently for two users/environments, failures pause retries, and manual recovery is required after checkout takeover. At 1920×1080 the New Description/source heights must leave Import/Template buttons visible without body scrolling. For an ordinary user, include the required Resource Group above Name. PowerShell must put its narrower template selector and Import Script File / Save as Template buttons above Source, with no overlap; VBScript must retain Import below Source. Switch languages repeatedly and check keyboard order, narrow-width wrapping and unchanged template-save permissions. Settings must have Edit/Rename/Delete for existing templates, a 350px initial Source window in Edit Script Template and no New Template button.

After creating/reopening a Script, confirm editor Undo Last Change is disabled. Make a local Source/Name/Description/type or MAUI metadata change, undo only that change and confirm Undo is consumed until the next edit. Save must preserve the current local Undo state; reopening must start empty rather than exposing stored grid history. Check Create Script / Creating… labels on creation. Verify every Information and Task/Host/Script Properties section heading is 15px and MOVEit/MAUI pills share 10px text, 12px line height, 700 weight and 3px/8px padding in both themes.

Verify Host editor Undo starts empty, reverses one current-opening configuration/Information/metadata edit locally and becomes disabled after use. Reopening/reloading clears it. Grid Undo Changes is a separate saved-history restore operation; Local File System remains excluded from grid restore. With Use MAUI Script Editor on, built-ins must show only read-only Properties and Tasks, no Save/Auto Save/Actions, and no Source/template/checkout traffic. Verify related Task links, custom editor state after closing a built-in, direct built-in save rejection, and viewing without the Custom Scripts license. With the preference off, built-ins must open native read-only viewing without Undo capture or modification audit.

In both New and Edit Script, verify horizontal VBScript/PowerShell cards match the shared Host Type selection style, checked highlighting follows native radio selection, keyboard navigation works, built-in language remains read-only, and template/source/save behavior is unchanged. Check both themes and New modal footer/tool visibility at 1920×1080.

#### Shared template save permissions

- With a MOVEit administrator who is a MAUI User, and separately a MAUI Admin who is not a MOVEit administrator, confirm shared template saving is available from authorized editable Script New/Edit forms, Edit Notifications and Task email/SMTP controls. Normal object permissions still govern editing; read-only editors must not gain write controls.
- With neither administrator role, confirm Script Save as Template and email template saving are disabled/dimmed, including after changing Script language or Task email tabs. Existing template loading and permitted object/source edits must still work.
- Test both save endpoints directly: ordinary users, anonymous users and expired sessions must be denied. MOVEit administrator status alone must not unlock Configuration template management or other MAUI-only settings.

### Date List editor acceptance

- With Use MAUI Date List Editor off, verify native New/Edit paths. Turn it on under Configuration > Settings > Editors, save configuration and reload; verify New, row links, row Edit, import continuation and Undo continuation open MAUI.
- Create a Date List with Description and a valid date. Confirm YYYY-MM-DD rows, wildcard month support, invalid-date rejection, red/white Delete buttons, current-year–2045 selector and a visible Create Date List button at 1920×1080. No comment field is shown.
- Import a .txt file containing one date per line. Verify BOM/newlines, duplicates, leap days, wildcard day31 and all-or-nothing failure with a line number. Inspect the saved native Date List; old per-entry comments/unknown configuration and resource groups must remain intact.
- Confirm Properties layout, MAUI metadata/audit, Tasks editor preference/return handling, Saved/Unsaved footer status, Save → Saving… → Save and per-user/environment Auto Save.
- Exercise save-scoped and edit-duration checkout, external changes, admin override, abandoned-editor cleanup and blocked uncertain saves. No retry may replay a possibly successful creation.
- Confirm editor Undo starts empty, reverses one local entry/name/description/metadata edit and is consumed until another edit. Reopening/reloading clears it. Grid saved-history restore, Export and Delete retain their shared resource operations and task-reference/Recycle Bin guardrails.
- Windows CI runs DateListCrudTests, the shared Date List lease cases and date-list-editor-browser.cjs using production markup/runtime in Edge, including light/dark and responsive screenshots.

### Editor consistency and built-in metadata acceptance

- Verify Script Name / Date List Name in New and Edit and matching required-name errors. Other editor name labels remain object-specific.
- New Script/Date List headers hide the grey kicker; Edit headers retain it. New Task/Host headers show their shared Task/Host icon and the same title/subtitle styling, without a kicker.
- Date List edit subtitles show Description plus Date List ID, or only ID when Description is blank. Saved description changes refresh the subtitle.
- Open a built-in Script with no Custom Scripts license. MOVEit Name/Description and MAUI properties/metadata stay read-only in the built-in editor. Save/Auto Save/Actions are hidden. No custom Source/template read, metadata save, native write, checkout or edit snapshot may occur.
- The separate Script Information metadata endpoint remains subject to effective edit permission and its MAUI-only metadata contract. Posted native fields must not modify built-in MOVEit configuration. Metadata audit fields remain read-only; custom Script save/Undo/checkout behavior remains covered by the existing regression suite.

### Consolidated resource forms acceptance

- Verify New Parameter creates and closes without an editor. Name links and Information open the Information / Tasks modal; there is no separate Edit Parameter action.
- Verify Parameter Name, then Parameter Value, then the Secure checkbox/reset note. Save native changes and MAUI metadata together, including Boolean metadata and renames. Check required names, inline server errors, Save / Saving… / Save, close/discard handling and stale revisions.
- Existing secure values must be empty/disabled and preserved through name/metadata saves. Reset Value explicitly enables replacement and changing the Secure checkbox. Do not expose or submit the original secret.
- For an authorized Global Parameter administrator, Save stays available on Information even with no MAUI fields; hide it on Tasks. Read-only Information hides Save regardless of fields. Related Task links honor the preferred editor and return to Information/Tasks in context. Failed reads must keep stale content hidden and Save disabled.
- Verify SSH/TLS/PGP New headers, icon styling and Close buttons. Key/certificate grid Names must be plain text before/after refresh. The row menu must have no Edit action; Information remains available for supported updates. Preserve creation payloads, licensing and error placement.
- With each editor preference, create a Script and Date List. Successful creation closes New, retains the calling page/modal and refreshes the grid. It must not open native/MAUI Edit, fetch an editor shell or acquire a checkout. Explicitly reopen Edit and verify native changes, metadata, Auto Save, Undo and lease behavior still work. Failed/uncertain creation must remain visible with no automatic replay.
- Automated resource coverage: `node MoveItAutomationUI.Tests/Browser/resource-create-return-smoke.cjs` and `node MoveItAutomationUI.Tests/Browser/global-parameter-information-browser.cjs` (Edge/Chromium); Script/Date List rendered suites cover their create-and-return lifecycle. Light/dark layouts cover 1920, 1400, 800 and 600px.

### Groups and Permissions acceptance

- `ResourceGroupPermissionsTests` covers the supplied v1 shapes, multiple pages, missing/invalid grants, duplicate assignments, visible-scope joining, empty Resource Groups, ACL ID zero, per-user/server cache isolation, failed-refresh cache retention and default-off configuration binding. Read-only tests reject any checkout/write call.
- Run `node MoveItAutomationUI.Tests/Browser/groups-and-permissions-browser.cjs` in Windows/Edge for the production view/controller/CSS fixture. The CI gate checks expansion, parent pagination, name-only search, permission filters, Name-only sorting, CSV across pages with spreadsheet formula protection, Print, inline settings failures, refresh retention, mutation readiness/disabled states and both themes at 1920/1400/1024/800/600px. `MAUI_PERMISSIONS_MODEL_ONLY=1` runs the dependency-free hierarchy model tests; `MAUI_VALIDATE_BROWSER_FIXTURE_ONLY=1` validates the browser fixture without installing a browser.
- Live MOVEit: enable the Groups & Permissions editor under Configuration > Settings > Editors, Save Configuration, and compare every assignment against the native page. Include a Resource Group with no assignments, differing grants across several assigned user groups, and enough groups/ACLs to exercise REST pagination. Confirm names search at both levels, children stay with their parent page, summary counts include all assignments, and Refresh Now retains filters/sort/expansion/horizontal position. Confirm a read failure displays an inline stale-view error and never substitutes None permissions.
- Confirm setting off restores native navigation and the MAUI JSON endpoint returns 404. Existing Tasks, Hosts, Scripts and Date Lists should retain their prior grid/editor behavior. Then complete the current membership, assignment, permission and deletion checks below; those operations are implemented and require live validation.

### Groups and Permissions entry-route regression

`GroupsAndPermissionsRoutingTests` starts a real MVC/Kestrel test host, renders the production compiled views and layout, and requests the page/data URLs at `/` and `/maui`. It checks native navigation while the preference is off, enables the preference on the running configuration, verifies the grid and its real data endpoint succeed even though `MauiEditorOptions` remains unbound, then verifies session/read failures and disabling the preference. No MOVEit server calls or installation configuration file writes occur in this test host. `MAUI_GROUPS_PAGE_FIXTURE_PATH` exports the rendered HTML for the Edge grid workflow, so CI exercises markup reached through the real entry route. Confirm manually after Save Configuration that enabled navigation opens the grid directly and disabled navigation opens the normal MOVEit editor; neither path should show a placeholder page.

### Resource Group New and Edit acceptance

- `ResourceGroupEditorTests` verifies Task and Host dependency closure (including directory scripts and PGP parameter references), shared dependencies, full-dataset preservation, unknown member kinds/fields, duplicate names, stale revisions, read-only preparation, and ambiguous PUT handling. Existing Task bulk Group operations exercise the same write boundary.
- `GroupsAndPermissionsRoutingTests` covers the compiled modals, generated endpoints at root and `/maui`, live MOVEit session checks and native authorization, expired sessions, disabled feature routes and anti-forgery enforcement. The Groups Edge suite consumes the real rendered Razor page and exercises the modal/pickers, required Remove controls, create/edit failures, discard confirmation and refresh; it records light/dark screenshots.
- Live MOVEit: create a Group with an empty membership, then add multiple Tasks sharing a Host, a Host with a client key/certificate, independent Scripts and each key kind. Confirm dependencies cannot be removed individually, removing one Task keeps shared resources, and removing the last referencing root removes its derived resources. Reopen and compare native membership, Name and Description. Test duplicate names and a native concurrent edit; unrelated Groups and ACL assignments must remain intact. Resize and filter the grid; its table viewport should remain fixed to the available window height.

### Groups and Permissions user assignments

While a User Groups directory read is delayed, verify a single spinner/status inside the table. Type, Search, Reload Lists, table and modal bounds must match their loaded positions and sizes in both themes at desktop and narrow widths. The Advanced Task Flow PNG header must retain Task name/ID, show the export's local date/time and omit the old MAUI banner/draft label.

Only live MOVEit administrators may enter the grid or call its editor/ACL endpoints. Validate with a MOVEit non-admin (including a MAUI Admin) that the navigation entry is absent and direct calls are denied. Task bulk Group actions retain their existing native authorization.

Exercise Local and Domain lists, search and filtered Select All, retained cross-list selections, already-assigned groups, failed/reloaded directory reads, partial batch results and refresh after save/removal. New grants must have all eight flags off. Remove only the exact selected ACL (including ID 0); changed/reused ACL identities or revisions must fail without a DELETE. Unconfirmed outcomes must block retries pending grid inspection. Check the standard footer against other grids, Rows preference persistence/rollback, and modal layout in both themes at 1400, 800 and 600 pixels.

The automated fixtures validate contracts and workflows; they do not contact Windows directory services or perform live MOVEit ACL writes. Finish live acceptance on the target MOVEit server before relying on these permissions controls.

### Inline Resource Group permission editing

`ResourceGroupPermissionUpdateTests` verifies exact per-ACL PUTs (including ID 0 and empty acknowledgements), unchanged-row omission, preservation of other native fields/grants, complete prevalidation, the Task permission dependency, independent Edit Existing, stale/reused identities, the shared write gate, cache invalidation, partial rejection, concurrent changes between PUTs, unavailable reads and lost acknowledgements without write replay. HTTP tests cover live MOVEit admin/session/Experimental guards, antiforgery and root/IIS PathBase routes.

The Groups Edge suite exercises expand/edit/cancel/save, all assigned rows under a child-name search, disabled unchanged/busy states, the dependent checkboxes, only-changed-ACL submission, partial retry excluding saved rows, conflict/unconfirmed/network retry guards and a refresh already in flight. It verifies inline Save remains visible and the Remove User Group card is centered in both themes at several widths. Runtime configuration is not modified by these tests.

Live MOVEit acceptance: grant/revoke each column across several assignments, compare native values, and test a native permission change while MAUI is editing. Confirm unchanged ACLs and unrelated grants are retained, Task Add/Edit/Delete keeps Edit Existing on, and Edit Existing can be used without Run. The native PUTs are individual operations, so a batch can partially succeed; review the per-row results before retrying. No transactional or conditional-write support beyond the supplied native API pattern is assumed.


The Group regression suite covers committed-then-timeout/malformed acknowledgement cases for Group dataset saves, rejects missing/invalid/reused visible ACL IDs while retaining valid zero, and tests Group editor retry guards for HTTP 408/502/401/403. Edge checks also retain blocked-save messages across membership/list changes, preserve selected resource checkbox focus, and keep an open Group modal draft during background refresh expiry. The fixture uses JSON-escaped Domain names so native Windows group separators are represented accurately.

### Resource Group deletion

Verify the counter reads “x of x resource groups”. Edit Permissions must be disabled for a Resource Group with no assigned User Groups and become available after assignment. Delete Resource Group must use a centered red-action confirmation; Cancel makes no mutation. Confirm deletion POSTs the v0 section checkout with `{}`, removes only the selected Group and PUTs the complete retained dataset. Tasks, Hosts, Scripts and Keys/Certificates must remain. Changing/renaming/removing the Group while confirmation is open must block stale deletion; another Group changed in the meantime must retain its latest definition. A timeout, lost response or incomplete acknowledgement must keep the error inside the modal and block replay until inspection. Confirm a last-Group deletion sends an empty Group array. Endpoint tests cover MOVEit admin, antiforgery and application-relative routing.

### Built-in Script metadata and Process requirements

On the installed MOVEit release, capture unfiltered `GET /api/v1/standardscripts/{id}` for Certs Backup, Certs Restore and another Script with choices/defaults. Compare the MAUI description/reference with native Add/Edit Process. Confirm the returned Params entries expose the documented ParamName/ParamDesc/Required/InOut/Type/Default/Choices definition fields. The adapter inspects definitions within Params independently of collection-wrapper names; it must not interpret configured name/value parameters as definitions or hard-code script catalogs. Unit fixtures use the published schema and are synthetic; they do not establish the installed server response shape.

Verify required input rows appear automatically without inserting blank Task/Process JSON, changing the Saved state or triggering Auto Save. Configure must retain existing parameters, show the selected parameter help, prefill optional defaults only in the draft form, reject empty explicitly configured required values without defaults, and preserve secure native values. Traditional configuration is Task-scoped; Advanced configuration is Process-scoped and recognizes inherited Task values. Output parameters must not become input requirements. Global-value resolution is advisory, not a claim that no Global Parameter exists. Check metadata failure/reload, unsupported replies, rapid Script switches and closing/reopening while a response is pending. No response may replace the current guidance or navigate away from an open draft.

### Modal feedback and PGP Information

- The shared modal feedback regression covers success, warning and error placement, nested dialog ownership, error precedence, stale-message reset and labelled parent-page origin in the active modal footer. Registered editor errors retain their existing footer placement.
- In Host, Traditional/Advanced/Sync Task, Script and Date List Actions, verify labels name their object (for example Export Task, Clone Host and Delete Script). Export a saved object and confirm **Exported 1 {object}.** appears in the originating editor footer. Successful Host/Task Clone completion must also use the footer, without inserting a banner or changing header/tab/workspace/footer dimensions.
- Fail an export with a server error, non-JSON/malformed response, empty download or lost connection. No download or success message should appear. Task/Host errors stay in the open Export dialog footer; Script/Date List errors stay in their editor footer. Retry successfully and confirm the prior error clears, the correct filename downloads and unsaved editor values remain intact. Check both themes and desktop/narrow widths; unresolved editor errors must retain priority over unrelated informational feedback.
- Resource Information Edge coverage opens PGP Public/Private keys and then SSH/TLS Information using the production modal/client. It verifies the PGP-only Key Type identity column, missing-value handling and no stale Key Type on other resources. It also exercises shared feedback in real nested dialogs.
- Verify Task, Host, Script, Date List and Groups & Permissions links follow their preferences without an intermediate fallback page. Keep native integration available for disabled preferences and native-only types/operations.

## Imports, exports and environment copy

### TLS and PGP export validation

Native TLS and PGP key/certificate exports use the supplied REST export contracts. Windows CI covers payloads, attachment bytes, permissions, password readiness, public-only PGP keys, download completion and inline modal errors. Live acceptance: export a TLS public certificate and a password-protected private PFX, then re-import and verify identity/private-key availability; export public/private PGP material and verify the intended contents. Confirm SSH has no Export action, read-only accounts cannot export, and MOVEit export failures stay inside the modal.

### Copy Between Environments acceptance

Results distinguish successful native copies with informational metadata differences (Completed, light blue) from actual follow-up warnings (Completed with warnings, yellow) and failed/unverified native writes (red). Confirm the affected object and details are visible in both themes, including mixed warning/metadata results and reopened reports from earlier feature builds. Missing/incompatible metadata and empty-source retention must not elevate a successful copy to a warning. Real audit/folder/metadata write failures must identify the failed follow-up and retain the native destination ID. Follow-up order and stop-on-failure behavior remain unchanged.

The reliability suite includes regression cases for atomic Resolve rejection/acceptance, malformed model/selection/choice inputs, comparisons after Start, and corrupt/null-shaped retained reports. Browser coverage checks late placement/comparison errors after closing/reopening, plus non-JSON HTTP access denial without endless polling. Exercise these cases against the target environment when applicable; the native transfer/CRUD contracts are unchanged.

During a deliberately slow copy, verify one status line for the phase/current object, verified count and elapsed phase time. Routine running/finished messages must not repeat in the footer; distinct report details and errors remain visible. Verify real connection/inventory/validation/recovery/group/folder/object phases. Preparation/first-object activity must be indeterminate; counts advance only for Created/Overwritten/Reused entries. Reopening an active job must retain its total without a browser plan. Hold/timeout/fail a status GET, verify read-only recovery, stop retries on denied access, and deliver a late response after Close/reopen. Start must remain submitted once. Confirm unchanged rows stay mounted, pending writes read In progress, stopped/cancelled bars are not forced full, Stop stays disabled after acknowledgement, and New Copy becomes enabled on completion. Stop while queued, during preflight and between nested folder segments; no next write may occur. Exercise initial/final journal I/O failure and a corrupt retained report; protected plans must be cleared, other reports reconciled and writes unreplayed.

Enable the experimental toggle only for controlled testing. Confirm menus/endpoints are unavailable when disabled or when either MAUI Admin or MOVEit administrator access is absent. Both Data Access accounts must authenticate as MOVEit administrators; Analyze must not use browser credentials or sign out the browser. Verify Tasks, Hosts, Scripts, Date Lists and Keys/Certificates bulk selections seed the Objects panel. Check the Copy To label in bulk and row menus, row-only seeding without changing the grid selection, and exclusion of built-in Scripts/default Host from row copying. Copy From must stay fixed to the current connection. Hold a destination read, switch destinations before it returns, then deliver the stale response; it must not change the latest connection or busy state. Test timeout/retry, inline feedback and preservation of selected source objects. Confirm built-in Scripts and Local File System cannot be selected, renamed, overwritten or duplicated. With Include Dependencies on, verify recursive/shared dependencies are checked, dimmed and locked; turning it off clears automatic checkmarks without dropping explicit selections. Test removing the final root, Select All, source/destination changes, a preview read failure and Refresh retry. The checkbox column should stay compact and the bulk command should fit on one line using the original shared grid menu width. Action completion returns focus/scroll to the form top; closing differences preserves the parent context.

Across two configured environments, copy Traditional, Advanced and Sync Tasks with Source/Destination overrides, schedules/Date Lists, notification SMTP Hosts, Run Task/next-action references, Scripts and key dependencies. Test shared dependency deduplication, task cycles (reuse a suitable Task or receive a blocker), a source ID colliding with an unrelated destination, names differing only in case (uniquely compatible names default to Reuse Existing), edited names, all prefix/suffix scopes, built-in reuse and default Host mapping. Required mappings and overwrite impact must block both routes into Review until resolved. Closing differences preserves choices, scroll and focus.

Confirm Task/Host/custom-Script assignments use the same structured Group values as Information, even if the inverse Resource Group membership projection or native XML omits them. Test a single source Task group missing at the destination, multiple groups and names containing commas. Selecting Create groups as needed must immediately show the planned group and placement before Update Review; changing the selection invalidates the execution token. A failed preview must show an inline error and remove stale rows, while full Review remains necessary before copying. Change source or reviewed destination membership before copying and require a new plan rather than silently applying stale assignments. Exercise None/matching-source/create-as-needed/common groups, preserve-destination overwrite placement, missing source groups, root/existing/new nested Task folders, matching folder reuse, incompatible metadata, empty values and Tag merge. Reused resources must not acquire new groups or metadata through Task placement. Date List group placement is explicitly skipped. Create-as-needed must list missing names in Review, deduplicate names case-insensitively, preserve existing groups/members/native fields and leave source user-group grants uncopied. Reused resources and overwritten objects preserving destination groups must not cause extra groups to be created. Test concurrent same-name creation and an uncertain dataset response: retain planned/created names in the operation journal and never replay the PUT automatically. Resolved rows should clear their attention color immediately after validation; overwrite impact must remain a blocker until reviewed, with no duplicated Action required prefix. Verify key identities and public/private capabilities; use native dependencies or an existing matching destination key for unsupported standalone SSH/private-PGP transfer. Test standalone TLS private/public export and public-only dependency confirmation.

Change a destination after Review, hold it in another MAUI/native editor, override a checkout, fail checkout release, lose an import response after creation, fail a prerequisite and stop a running copy. Verify no automatic second create or replay, retained known IDs, disabled copied Tasks, stopped dependent writes, usable reports and checkout cleanup outcomes. Close/reopen the modal while copying and inspect Previous Results. Restart with an unfinished journal: it must become Interrupted without replaying writes or assuming it owns another user's checkout. Verify recovery definitions are protected at rest, separate from reports and gated by the initiating administrator; restoration is a separate reviewed operation. Validate both themes at 1920×1080 and 1366×768, smaller widths and browser zoom.

Automated coverage uses mocks/public fixtures and Edge; it does not validate live MOVEit portable encryption, COM deployment, licenses or cross-environment native import behavior. Complete this live matrix before enabling general use.

### Task CSV/XLSX import regression checks

The dependency-free JavaScript UI regression check can be run from the source root with `node MoveItAutomationUI.Tests/Browser/task-spreadsheet-import-smoke.cjs` (Node.js 22 or later). It simulates preview, Analyze, paging, 2/2/1 import batches and Created/Updated/Failed outcomes; it does not replace a real browser layout check, .NET build, or MOVEit integration test.

Automated coverage includes Task-name canonicalization, default disabled-import behavior, CSV quoted-newline parsing, unterminated-quote rejection, malformed XLSX rejection, blank-row handling, duplicate normalized headers, unsupported file extensions, schedule conflict validation, day-of-week normalization, Traditional-only update gating, update-payload preservation, native PGP key-token selection, root-path parsing, and shared Traditional Task Host location contracts for MOVEit Transfer/cloud profiles. In addition to automated tests, validate the spreadsheet importer manually against a non-production MOVEit environment. Use the supplied workbook at `Templates/TaskImport/MAUI_ImportTasksTemplate.xlsx` as one test input; verify it is present in the Release publish output and remains unchanged from the source package:

1. Check **Import Tasks** in both MAUI Portability Package and CSV / Excel mode: the portability form must use normal MAUI field/help/checkbox spacing and its standard narrower modal width; CSV mode must use the wider modal, with both option checkboxes centered vertically against the optional Prefix textbox. Check Results wrapping of a long missing-Host message, including the `AUTOMATION_DESTINATION_HOST` field, at desktop width and narrow viewport; all messages for a row must remain aligned with their field labels. Check light and dark theme colors.
2. Validate a CSV without the `RESOURCE_GROUP` header and one with blank group values: the current spreadsheet contract requires this column and a visible named group on every row, including updates, and Analyze must block missing values. A supplied group that does not resolve must also block Analyze. When updating, the live Task's existing membership is preserved; a different requested group produces a warning rather than moving the Task. This spreadsheet rule is distinct from administrator New Task forms, which hide the Group selector.
3. Open **Tasks > Actions > Import Tasks** and confirm **MAUI Portability Package** remains the default mode and its existing Analyze/Import behavior is unchanged.
4. Select **CSV / Excel**, choose a `.csv` or `.xlsx` file, and confirm its first-sheet data loads into the preview grid immediately with **Not analyzed** status before any Analyze call. Confirm **Analyze File** remains available as the separate validation step and unsupported extensions are rejected.
5. Confirm `TASK_NAME` is accepted, legacy `REQ_NAME` is accepted and conflicting values are rejected. Task Name Prefix is optional literal text (maximum 60 characters), not a predefined selector: `PROD_` plus `Client Daily File` produces `PROD_CLIENT_DAILY_FILE`; blank leaves `CLIENT_DAILY_FILE`. Separators must be included in the prefix itself.
6. Verify required columns/values, required/defaulted Y/N fields, `SOURCE_FILE` comma-to-semicolon normalization, macro/path normalization, and schedule validation produce the expected preview values and Error/Warning status.
7. Test Host resolution for UNC Share, MOVEit Transfer, FTP/FTPS, SSH/SFTP, Amazon S3, Azure Blob, SharePoint, and Google Storage. Exact Friendly Name must work for every supported Host; the legacy unique FTP/SFTP endpoint and Share UNC fallbacks must continue to work. Ambiguous references, the MOVEit `(Default)` Local File System Host, and POP3/SMTP/ASx must block import. For Share Hosts, full UNC paths must normalize to the Host-relative path only when they begin with that Host's configured Resource UNC.
8. For S3/Azure Blob/SharePoint/Google Storage locations, verify `/folder` uses the configured Host default root and `root/folder` selects the named root. Analyze must validate explicit roots through the shared Browse service and preserve the native root field (`Bucket`, `Container`, or `DocumentLibrary` plus SharePoint `DocumentLibraryGuid`) in the created Task. Verify explicit-root Analyze fails rather than guessing when the Host's Browse shape is unsupported, such as deprecated SharePoint User Access authentication, invalid proxy settings, or a server-rejected root. Google OAuth and SharePoint App/Certificate discovery now reuse the supported Browse contracts and require live verification; `/folder` using the configured default root must remain available.
9. Verify Source, Destination, and Archive steps use the shared Host-profile defaults/location mapping, including MOVEit Transfer `FolderName` and cloud root/default-root fields. Where a Host profile does not expose `ForceDir`, an explicitly supplied `CREATE_DIRS_IF_NECESSARY` value should produce a warning and must not add an unsupported ForceDir field.
10. Test PGP Encrypt, Decrypt, and Encrypt and Sign. Missing standard scripts/keys, expired keys, or unlicensed PGP must block the affected row.
11. Test Failure/Success/No Action notifications both with a named MAUI Notification Template and with no template. A named template must resolve uniquely; notification rows require exactly one visible SMTP Host.
12. Confirm extra columns matching configured MAUI Task metadata fields are validated and saved; unmatched columns are ignored and counted in the Analyze summary.
13. With **Update existing task if name already exists** cleared, exact-name conflicts must block Analyze. With it enabled, one Traditional Task may update, MAUI Undo must be available, Advanced, Synchronization, and other non-Traditional Tasks must be blocked, and an existing Task's Resource Group must be preserved with a warning if the spreadsheet specifies another group.
14. Verify **Leave imported Tasks disabled** disables newly imported Tasks and forces updated Tasks disabled when selected; when cleared, new Tasks are enabled while updated Tasks retain their existing enabled state.
15. Verify finite Task license capacity is checked during Analyze and again under the shared creation lock at commit time.
16. Set **Configuration > Settings > General > Import Settings > Import Row Limit** to `2` for a five-row test file. Confirm the settings textbox accepts only whole numbers `1`–`1000` (default `500`) and the saved value takes effect after opening/reloading Tasks. Verify the grid renders only 2 data rows at a time, with Previous/Next navigation and row-range text in both parse-only preview and analyzed state. Import the 5 rows in successive 2/2/1 batches, confirming **Import Progress** advances only after row requests return. Verify the modal **body scrolls vertically**, the **Results** panel grows with its content and has **no separate vertical scrollbar**, and the footer controls remain visible. The grid must show actual Created/Updated/Failed status per row, including after paging; prior batch results must remain visible, and failed rows must not be automatically retried. Send/replay two requests for the same analyzed row and confirm only one in-flight/committed operation is permitted.
17. Force a file-wide Analyze error (for example Task license capacity or an ambiguous normalized Task metadata column mapping), then attempt the row-import endpoint with the returned analysis ID. Confirm the server still blocks the import even if a stale/direct client bypasses the disabled **Import Tasks** button.
18. Validate commit-time Task identity rechecks: after Analyze, create a same-name Task for a row that was planned as Create and confirm Import blocks rather than updating it; for a row planned as Update, delete/rename/replace the analyzed Task and confirm Import blocks rather than creating a replacement or updating a different Task ID.
19. With a mocked/injected malformed inventory item, verify a nonnumeric Host/SMTP Host ID or a resolved custom Date List without an ID is reported as a row Analyze error rather than aborting the entire file analysis.
20. Update an existing Traditional Task that contains extra/version-specific root properties and confirm those properties survive while spreadsheet-owned Name/Info/Schedules/steps/notifications are updated. A spreadsheet row with no notifications must clear the old Task notifications.
21. Verify PGP rows persist the selected MOVEit key ID in `PGPRecipientKey`/`PGPSignerKey`, including input supplied as a normalized `0x...` key ID.
22. Re-run the existing `.maui` Task portability smoke tests after spreadsheet-import testing to confirm the two code paths remain isolated.

### Task/Host native portability regression matrix

For RC3 release validation, validate the Task/Host `.maui` workflow against a real MOVEit Automation environment in addition to the automated test suite, including `NativePortabilityContractTests`:

- Task export/import using **Server Key** back into the same MOVEit environment.
- Task export/import using **Portable / Passphrase** into a different MOVEit environment.
- For Portable/Passphrase native import, verify the final `/api/v0/config/import` body places `passphrase` inside `Imported` (not beside it) while preserving MOVEit's decoded PGP/SSH/TLS fields unchanged; validate with a cross-environment Task containing a Host and protected PGP key.
- Host export using the temporary disabled carrier Task, including the one-slot Task-capacity requirement, with carrier cleanup verified after both successful export and a forced export failure.
- Run the Host-export carrier path once for each supported Host type used by the installation: UNC Share (`Share`), MOVEit Transfer (`siLock`), FTP/FTPS (`FTP`), SFTP (`SSHFTP`), Amazon S3 (`S3`), Azure Blob (`AzureBlob`), SharePoint (`SharePoint`), Google Storage (`GoogleStorage`), POP3, and SMTP. Confirm every carrier Task is accepted by MOVEit; SMTP must appear as a Destination step, POP3 as a Source step, and the carrier must remain disabled and unscheduled.
- AS1/AS2/AS3 and any other Host type without a captured carrier-step contract must be rejected by MAUI Host export rather than being exported with a guessed carrier shape.
- Host import from the Host-only native payload and confirm no Task is created; `<Tasks></Tasks>` must remain present and empty.
- Export with referenced keys/certificates enabled and confirm MOVEit carries Host/Task native dependencies and protected key material as expected.
- Test a Task that references a Script in both Custom-Scripts-licensed and unlicensed environments, including any Task that references a built-in Script. Confirm MOVEit's native package dependency behavior matches MAUI's license/read-only guardrails; built-in versus custom Script dependency behavior remains a live native-contract validation item.
- Portable/passphrase validation: verify Task and Host export reject passphrases shorter than 16 characters or missing an uppercase letter, lowercase letter, digit, or special character; verify the same rule is enforced server-side when analyzing/importing a Portable package.
- Portable export passphrase UX: selecting **Portable / Passphrase** for Task/Host export must populate both visible plaintext export fields with the same random 16-character value that already meets uppercase/lowercase/digit/special requirements. Changing the value remains allowed and normal validation still applies.
- Configuration > Settings > MOVEit Configuration Portable export must use the same visible generated 16-character passphrase behavior and enforce the same policy server-side. Configuration and Task/Host **Import** passphrase fields must remain password fields and must never be auto-populated.
- Configuration > Settings > MOVEit Configuration UI: verify Export Type uses the MAUI radio-card pattern for Portable, Server Key, and Sanitized; export Passphrase/Confirm Passphrase controls are visible only for Portable. Verify Import shows Configuration File before the radio-card Import Type selector, offers only Portable and Server Key, and shows the empty password-type passphrase field only for Portable.
- Wrong passphrase returns a controlled decode failure and does not expose the native MOVEit response.
- Duplicate Task and Host **Overwrite Existing** matches native MOVEit behavior and captures the normal MAUI Undo snapshot before overwrite.
- Duplicate Task and Host **Import as New / Rename** creates the new non-conflicting names while MOVEit remaps native relationships.
- Dependency conflicts that MAUI has not captured native rename semantics for offer **Overwrite** only.
- For Script, Date List, SSH key, TLS certificate, and PGP key dependency conflicts, verify Overwrite is rejected when ID and Name point to different destination objects, when the target disappears after analysis, when the analyzed target is replaced by a different destination object, or when MOVEit marks the target read-only. Built-in Scripts must remain non-overwritable.
- For **Use Existing** key/certificate conflicts, analyze the package, then delete or replace the exact destination key/certificate before Import. Import must fail before native commit and require a fresh **Analyze Package** pass; it must never silently import the packaged key or bind to a different same-name target.
- Imported Tasks remain disabled when the default **Leave imported Tasks disabled** option is selected.
- MAUI custom metadata fields and Tags are restored to the final destination IDs after both overwrite and rename/create import paths.
- Simulate or force a post-commit reconciliation failure (for example, make the authoritative inventory refresh fail after a successful native import). MAUI must report that MOVEit completed the import, warn the administrator to refresh/verify before retrying, and must not present the operation as a generic failed import.
- Simulate a MOVEit import timeout/connection loss or upstream HTTP 5xx after the native import request is submitted. MAUI must treat the outcome as **unknown**, close the import workflow, warn that the import may have completed, refresh when possible, and require destination verification before any retry.
- Simulate a successful HTTP import response whose JSON result cannot be read. The UI must treat the native operation as completed-but-unreconciled and require refresh/verification rather than presenting a normal failure.
- Force a definitive pre-commit/import validation failure, then confirm the prior analysis is invalidated and the next submit requires **Analyze Package** again before Import.
- Force MAUI metadata export construction to fail and confirm no Task/Host `.maui` package is downloaded with silently missing metadata.
- For an Overwrite conflict where the native/source ID identifies the destination object, verify post-import metadata/audit reconciliation remains attached to that exact object even when name-based lookup would be less specific.
- Import one selected Task/Host whose native package also contains another dependency of the same top-level type where applicable; confirm the import completes without opening a native editor, the success/warning toast is shown, and the corresponding grid refreshes.
- UI validation: confirm the preview labels explicitly distinguish **Selected** objects from **Dependency** objects and that duplicate reasons display human-friendly labels such as **ID** and **Name**.
- UI validation: while package analysis/import/export is active, confirm the button reads **Analyzing…**, **Importing…**, or **Exporting…** with no icon/spinner artifact and that Escape cannot dismiss the busy modal.
- UI validation: after analyzing a package that requires metadata acknowledgement, change the selected file or passphrase and confirm the stale preview/acknowledgement is cleared before the next analysis.
- UI validation: enter a package passphrase and close/cancel either portability dialog before submitting; confirm the passphrase is cleared immediately and is not retained when the dialog is reopened.
- UI validation: verify warning preview rows and post-import warning toasts use the warning treatment rather than error/default styling, positive portability options use the normal accent checkbox color, and 254-character object names wrap without overflowing the conflict card.
- Folder assignment, audit timestamps, and Undo/Recycle history are not expected to move with the package.
- Re-export a cross-environment imported Host and run **Test Host** to validate MOVEit re-established usable destination-environment credential protection.

### Native portability Razor build regression

Before functional Task/Host portability testing, build the full solution in Release configuration. The Task and Host index views must compile successfully through Razor source generation; failures referencing `Views_Tasks_Index_cshtml.g.cs` or `Views_Hosts_Index_cshtml.g.cs` are release blockers. The Task and Host pages construct a `NativePortabilityDialogViewModel` in the Razor code block and pass that local variable to `_NativePortabilityDialogs`; do not place a C# object initializer containing quoted strings directly inside the partial tag's `model` attribute.

### MAUI 2.0 package-container validation

- Run `MauiPackageContainerContractTests`; both the shared resource/Report builder and native Task/Host builder must produce a ZIP signature and the same canonical three-entry envelope. The tests also verify that plain XML content is rejected instead of being accepted as a `.maui` package.
- Confirm Report Analyze/Import rejects a renamed `.zip` file until it uses the `.maui` extension, matching Task, Host, Script, and Date List import behavior.
- Confirm Report Analyze/Import accepts packages up to the documented 50 MB limit at the MVC request boundary and rejects packages above 50 MB with the MAUI validation message rather than relying on the host's default request-size limit.
- Export a Task, Host, Script, Date List, and Report and confirm each `.maui` file is a valid ZIP archive containing exactly `manifest.json`, `payload.xml`, and `maui-metadata.xml`.
- Inspect each `manifest.json` and confirm `format` is `MAUI.PortablePackage` and `formatVersion` is `2.0`.
- Confirm Task/Host manifests use `packageKind` `NativeMoveIt`; Script/Date List/Report manifests use `packageKind` `ObjectExport`.
- Confirm imports succeed for newly exported 2.0 packages and that pre-2.0/plain-XML `.maui` packages are rejected rather than silently parsed through a legacy fallback.
- Confirm Script, Date List, and Report downloads use ZIP content (`application/zip`) while retaining the `.maui` filename extension.
- Confirm package-type mismatch validation still blocks, for example, importing a Script package through Date List import or a Host package through Task import.

### Native key/certificate dependency conflict validation

- Export a Task with an included PGP dependency, then analyze/import that package back into the same MOVEit instance where the original PGP key still exists. Confirm the PGP dependency is shown as a conflict with **Overwrite Existing**, not as **import as new**.
- Repeat with an included SSH client key and TLS certificate dependency. Existing same-ID or same-Name dependencies must be presented as conflicts. Apply the same-ID Use Existing rule in the next section; name-only conflicts are overwrite-only.
- Confirm a dependency that does not exist by either native ID or Name remains classified as a new dependency.
- Create/identify a test condition where the imported dependency ID matches one destination key/certificate but its Name matches a different destination object. Analyze must stop with an ambiguity error before native import is attempted.
- Confirm key/certificate dependency conflicts do not offer **Import as New / Rename**. Same-ID dependencies may offer **Use Existing**; name-only conflicts remain overwrite-only.

### Task/Host portability - key/certificate ID conflicts

- Export a Task that references an existing PGP key, SSH client key, or TLS/SSL certificate and import it back into the same MOVEit environment.
- Analyze Package should show the dependency as a conflict with **Use Existing** selected by default and **Overwrite Existing** as the alternate choice.
- With **Use Existing**, verify the existing key/certificate is unchanged and the imported Task/Host still references that same native ID.
- With **Overwrite Existing**, verify the existing dependency is intentionally replaced using the native MOVEit import path.
- If a dependency conflicts by Name only and not by native ID, **Use Existing** must not be offered.

### Cross-environment Task/Host portability validation

- Export a portable/passphrase Task package containing at least one Host and one PGP/SSH/TLS dependency; import it into a different MOVEit Automation environment.
- Verify a truly new dependency imports with the Task/Host and that relationships remain valid.
- Verify a key/certificate conflict detected only by MAUI live inventory does **not** cause MAUI to synthesize undocumented key `duplicate` fields or a `Duplicated[]` entry. **Overwrite Existing** must preserve MOVEit's `/config/encode` key/certificate object unchanged.
- Verify **Use Existing** removes the same-ID key/certificate from the working native XML, re-runs `/config/encode`, and submits the fresh MOVEit-generated import model; the destination key/certificate must remain unchanged.
- Force a definitive MOVEit 4xx from native import and verify the browser receives structured MOVEit error detail or a conservatively sanitized short plain-text validation message. Raw Settings/configuration/XML/JSON bodies and opaque encrypted material must never be returned.

### MOVEit whole-configuration import / backup restore regression

Backup Restore lists one unchecked-by-default option for each MOVEit environment included in the selected backup. Test MAUI-only restore, each environment separately, multiple selections and changing/reopening backups. Only checked native configurations may be resolved, authenticated or imported; missing/unavailable/malformed unselected exports must not block the selected subset. Invalid selections must fail before restore writes. Existing local settings/metadata restore, shared write coordination, pre-restore safety copy, Server-Key XML import, complete selected-target preflight, uncertain-outcome handling and restart behavior remain in force. Multi-environment native restore is not atomic. Verify inline failures, frozen selections/no duplicate submission during restore, conditional multi-environment guidance, and bounded scrolling with many environment choices in both themes. The registered `backup-restore-browser.cjs` fixture mocks every restore endpoint; it never restores a live environment.

- Export a MOVEit Configuration using **Server Key**, then import that saved XML through Configuration > Settings > MOVEit Configuration. Verify `PUT /api/v1/config` succeeds and MOVEit does not report an `application/json` content-type error.
- Export/import a **Portable / Passphrase** MOVEit Configuration and verify the existing `POST /api/v1/config/import` JSON flow still succeeds.
- Create a MAUI backup with MOVEit configuration enabled, then select the intended environments under **Restore MOVEit Automation configurations**. Verify only those environments use the Server-Key XML import path before MAUI-local restore operations continue; all unchecked environments must remain untouched by native import.

### MOVEit Configuration import/export mode UI

- Open **Configuration > Settings > MOVEit Configuration**. Confirm Export Type uses MAUI radio cards for **Portable — Passphrase**, **Server Key**, and **Sanitized / Troubleshooting**.
- With **Portable — Passphrase** selected, confirm plaintext **Passphrase** and **Confirm Passphrase** fields are visible and pre-populated with the same generated 16-character value.
- Select **Server Key**. Confirm both export passphrase fields disappear completely and their values are cleared.
- Select **Sanitized / Troubleshooting**. Confirm both export passphrase fields remain hidden and the sanitized/non-restorable warning is visible.
- Switch back to **Portable — Passphrase**. Confirm both fields reappear and a valid generated passphrase is populated.
- In **Import Configuration**, confirm **Configuration File** appears above **Import Type**. Confirm Import Type uses only **Portable — Passphrase** and **Server Key** radio cards.
- With Portable import selected, confirm the Import Passphrase field is visible, uses `type=password`, and is empty. Select Server Key and confirm the Import Passphrase field disappears and remains unpopulated when switching back.

## Configuration and operations

### Keep Alive and MOVEit Settings acceptance

- Confirm Configuration navigation and the modal use **MOVEit Settings**, while the editor switch reads **Use MAUI Settings Editor**. Existing `Editors:UseMauiSystemSettingsEditor` preferences and native/MAUI routing must remain effective.
- In Advanced, confirm Keep Alive defaults on and Ping Interval defaults to 5; accept only integer values from 1 through 20. Save an enabled/disabled state and an interval change, reopen Settings, and confirm persistence without a restart.
- With a trusted local IIS HTTPS binding, visit MAUI once, leave browsers closed, and confirm an unattended System Job runs beyond the previous idle timeout. Test both site-root and `/MAUI` deployments. A direct keep-alive URL request without the private process token must return 404 and expose no settings.
- Stop/recycle the application and confirm IIS startup/preload is still responsible for starting it. Keep Alive cannot revive a stopped process. Verify certificate/connection failures are logged once until recovery, and normal authentication/session expiration remains unchanged.
- Confirm publish output includes the unchanged supplied files `Templates/Email/Task - Failed.txt`, `Task - Success.txt`, and `Task - No Transfer.txt`.

### RC3 Settings organization acceptance

1. Open Configuration > Settings. Verify Editors appears immediately after General in the default navigation. An older installation-owned navigation order that lacks Editors must gain it without removing or reordering existing categories.
2. Verify all six editor preferences are in Editors, keep their configured values, save and reload correctly, and appear only once. Check both visible and hidden settings, including definitions whose Experimental metadata is false. Keys & Certificates and Global Parameters continue to use MAUI.
3. Verify General shows Folders immediately before System Message. Confirm Create Folders keeps its existing administrator-only, busy, antiforgery, duplicate-name and toast behavior and does not save or dirty unrelated settings.
4. Search for editor and folder settings; the matching categories must be Editors and General. Experimental retains Copy Between Environments and its availability/retention controls. Check light/dark themes, normal Save/Discard behavior and existing editor routing.

Use the full Windows build and live acceptance gates below for the current candidate. Historical validation records are kept in the changelog with their original release identities.

### MOVEit Settings editor acceptance

The suite includes contract tests using the supplied Globals, lockout and trusted-IP captures, real HTTP/MVC routing tests and rendered Edge workflows. MOVEit responses remain simulated in automated tests. Globals checkout uses the confirmed native `DELETE /api/v0/globals/checkout` release following PUT. No fallback PUT is made merely to open or close the modal.

1. Verify Use MAUI Settings Editor under Configuration > Settings > Editors defaults off and the navigation link still opens native MOVEit. Enable it and save configuration without restarting the app; open the MAUI modal and verify Logging fields populate without a disabled-editor error. Save a Logging change. Disable the feature and verify both opening/saving endpoints reject access and navigation returns to native MOVEit. Check root and virtual application deployments. Both MAUI and MOVEit administrator access are required.
2. Verify Logging loads existing values, unchanged Save is disabled, all seven tabs fit, and errors/status appear in the footer. Check all seven tabs, grey property group surfaces, fixed modal dimensions and light/dark layouts.
3. Change log level, disk logging, name, file size and retained log count; save and compare in native MOVEit. Check 2 GB (`2147483648` bytes), KB/MB/GB conversion, disabled dependent fields, optional blank directories and retry auditing. Verify unrelated Event Log, Tasks, State and tamper settings remain unchanged.
4. Browse both directories using Host 0; select a directory, cancel browsing and check inline browser errors. Verify no new browser or native host configuration is introduced.
5. Close without changes and confirm no PUT occurred. Discard a dirty form and verify native settings remain unchanged. Check native checkout is released after open, rejected save, confirmed save, cancellation and error cleanup.
6. Make a concurrent native change after opening; save must reject the stale revision, retain local values and offer Reload. Interrupt save/readback; do not automatically retry the write. Reload must confirm discarding local changes and load the current server copy.

7. Save Windows Event Log name, event level and audit level; verify native numeric values. Save Maximum Running Tasks, multiple runs, startup queue suppression, Notification Polling Interval and AS2 Polling Interval. Zero AS2 interval uses the notification interval; older Globals captures omit this optional override and display zero.
8. Test State File Always (`-1`), Until end (`0`), and positive Minutes/Hours. Sentinel modes omit `DefStateCacheUnit`. Verify enabling Tamper Detection writes `SuppressHashing=0`, disabling writes `1`.
9. Load IP/User policy and trusted IPs from their separate GETs. Test disabled dependent controls, Never/Expire after, lowercase native units, adding/removing IPv4/IPv6 rows and duplicate/invalid address validation. Each IP mutation must contain exactly one address. The native POST/DELETE may return an empty successful body; verify the fresh GET result. An unchanged policy/list must not be written during another tab's save.
10. Change native policy or trusted addresses after opening and verify stale revisions block writes. Simulate failure after Globals saves or partway through IP changes: retain the local form, show the possible partial-save message in the footer, block automatic/manual replay, and require Reload. These native endpoints are separate transactions; there is no claimed cross-endpoint rollback or native policy checkout. MAUI serializes its own writers per environment, but native concurrent writers are not locked by that gate.

11. With the MAUI Settings preference enabled, verify the separate native Manage MOVEit Lockouts link is hidden, including immediately after saving Configuration. Disable the preference and verify the native link returns and keeps its native route; no fallback page or launch notification is introduced.
12. Open Lockouts and verify both tables pre-size before data arrives, with loading/empty states inside the tables. Check counts, single/select-all/partial selections, sorting, scrollbar/header alignment, disabled empty unlock buttons and refresh on tab entry/manual Refresh. Native timestamps have no offset and are displayed unchanged.
13. Cancel and confirm single/bulk address/user unlocks; confirmation must name a single identity or the selected count. Verify bare-array `DELETE /api/v1/lockouts/ip` and `/user`, followed by a fresh GET, with no checkout/Globals calls. Settings edits remain unsaved and intact; selection alone must not prompt to discard settings. A newer native lockout for the same identity blocks an old selection. An expired selection is reported as already unlocked.
14. Interrupt DELETE/readback and verify no automatic replay: unlock controls require Refresh and a fresh selection. Definitive native rejection remains an inline footer error; list-load failures disable selections and allow Refresh. Both MAUI and MOVEit administrator checks, disabled-feature guards, antiforgery and PathBase apply to read/unlock endpoints.

Automated suites: `SystemActiveLockoutsTests`, `SystemSettingsPart2Tests`, `SystemSettingsTests`, `SystemSettingsRoutingTests` and `MoveItAutomationUI.Tests/Browser/system-settings-editor-browser.cjs` (Windows CI/Edge). Existing editor, configuration and checkout suites remain in the full build gate.

### Tamper Reset acceptance

The captured native request is `POST /api/v0/actions/resethashing` with no payload; `{"id":"-1","status":"success","errorcode":0}` is its response acknowledgement. The production REST-client contract tests require a null request content, accept that acknowledgement, reject failed/incomplete acknowledgements and preserve HTTP 422 errors without replay.

For each environment, test the action as an administrator against the same MOVEit environment where the native UI succeeds. Confirm successful reset feedback, or retain the exact upstream failure if it persists. Automated fixtures verify the corrected request shape; they do not establish that this fixes the reported `IPAddress` database error in a live deployment. No IPv6, database-schema or network changes are included.

### Configuration, IIS and recovery acceptance

- Test **SSH/KEX Algorithms** list reload, selected ordering, Save Configuration, new SFTP defaults and Host Reset; an existing Host must retain its saved algorithm selections.
- Test completed upgrades with `RestartRequired` omitted/false and true: show Return to MAUI or Restart MAUI respectively. Confirm the existing controlled restart returns to Login.

- Start from a new `{}` configuration under restricted IIS ACLs: Read on application files, Modify on the existing appsettings.json and Data. Complete Setup with SQLite, then SQL Server, without an app-pool recycle. An activation failure after persistence must remain Setup-gated until a successful retry. Test configuration write denial before provisioning.
- Open Settings twice, change/save one copy and confirm the stale copy receives HTTP 409. Verify initial dirty tracking, revert-to-baseline, Save/Discard, hidden/visible editor controls, SMTP and Data Access Credentials. Saved JSON is authoritative for Settings editing; document any environment overrides used at runtime.
- Add/edit/remove a MOVEit connection, save and verify new logins/background connections see it without restart. Existing sessions stay on the original endpoint. Endpoint URL, COM host or Environment Key changes clear unattended credentials; display-name-only edits preserve them. Database/global REST/session/TLS changes retain restart warnings.
- Test site root, `/MAUI`, nested `/Apps/QA` and a route-name-colliding app such as `/Tasks`. Verify Setup/Upgrade/Login destinations, start-page preferences, Dashboard/static assets, Records return links, Reports edit/Back, Saved Views and native popup return. Check Request.PathBase under the actual IIS/proxy configuration. Move a Report between application paths and ensure source filters survive without stale returnUrl/returnLabel.
- Recycle IIS with an old Login tab open; verify recoverable stale antiforgery handling, configured server selection, logout/login and secured-action CSRF rejection. Confirm machine-DPAPI keys persist under Data/DataProtection-Keys with proper ACLs and no developer keys in publish output. A key ring copied to another machine is not a supported recovery strategy.
- Test Upgrade, Backup Restore and rollback/recovery of appsettings.json using Data/ConfigurationRecovery. In-place writes are not crash-atomic; the one-process writer gate is not distributed locking. Preserve installation-owned Data/configuration on deployment.
- Exercise administrator-only Factory Reset **only on a disposable installation**. Verify typed RESET, next-start purge, cleared configuration, retained parent Data ACL and no deletion of external SQL Server databases or SQLite files outside Data.

### Resource Group folders

Configuration > Settings > General > Folders provides an immediate **Create Folders** operation for MOVEit administrators. Verify names are read from the active MOVEit environment, processed alphabetically and created at the Task folder root. Existing Task folder names anywhere in that environment are compared case-insensitively and preserved, including their parent and assignments. Repeat the operation; no duplicates or changes should result. Other environments and object types must not suppress creation. A read, validation or database failure must not leave a partially created batch. Verify success/error toasts, the busy button, non-admin denial, and that this operation does not save or dirty other Configuration edits. Restart and Factory Reset warning text/styles remain separate from executing those destructive operations.

### Failover Status validation

- On a MOVEit Automation environment where `GET /api/v1/failoverstatus` returns `ResilEnabled: false`, confirm **Failover Status** is not shown in the footer Server menu.
- On a configured failover pair where the endpoint returns `ResilEnabled: true`, confirm **Failover Status** appears in the Server menu and opens the standard MAUI modal.
- Confirm both returned nodes show their exact hostname and their current `primary`/`secondary` role. Swap/fail over the MOVEit roles if practical and verify `ResilStatus` follows the node now marked primary while `ResilStatusSec` follows the node now marked secondary.
- Confirm a node with `NodeHasError: false` shows a green check overlay and a node with `NodeHasError: true` shows a red X. Do not infer health from the free-form status message.
- Confirm multiline secondary status text is preserved and each node displays the exact `ResilStatusStamp`/`ResilStatusStampSec` value returned by MOVEit.
- Temporarily make the failover-status request fail after the menu option has been exposed and verify opening the modal shows a meaningful MAUI error without exposing raw credentials/tokens.
- Verify the modal has no mutating failover controls; Close/standard dialog dismissal are the only actions.

### Dashboard File Activity performance and accuracy

- Compare MAUI **Successful File Activity** and **Failed File Activity** against the native MOVEit Dashboard at approximately the same refresh time. MAUI must count only native `get`, `send`, and `append` actions, treat `StatusCode 0` as successful, exclude `5000`/`5010`, and treat other numeric status codes as failed.
- On a system with more than 10,000 qualifying File Activity report rows in the rolling 24-hour window, verify the first Dashboard page appears without waiting for the entire historical scan. KPI and transfer totals should display a trailing `+` while additional pages are loading, update as each page completes, and lose the `+` when the snapshot/catch-up pass completes.
- After the initial snapshot completes, verify a scheduled refresh and **Refresh Now** are materially faster and retrieve only File Activity above the completed LogID high-water mark rather than repeating the complete 24-hour scan.
- During a progressive load, interrupt or induce a later-page report failure. MAUI should retain the already-rendered lower-bound values, surface the Dashboard error, and retry from the same server-side cursor on the next refresh/resume rather than starting over.
- Verify the Successful File Activity KPI and the Transfer Activity Trend file total remain identical because both are calculated from the same retained successful records.

## Grids and modal consistency

### Grid Date/Time filter acceptance

Check Date/Time columns in Tasks/Hosts, metadata columns, Records and Keys & Certificates using the numeric, named-month and ISO date formats displayed by those grids. Cover `<`, `<=`, `>`, `>=`, `!`/`not:`, comma/`or` alternatives and mixed comparisons/macros. Verify midnight and timestamp boundaries, missing dates, raw timestamp precedence and preservation of commas in `Oct 21, 2026`. Partial text filters and numeric/text column operators must continue to work.

Use the clock picker to select `[nextmonth]`, `[nextquarter]` and `[nextyear]`; check calendar/quarter/year boundaries and leap years. Confirm both help dialogs include the new syntax/macros, retain MAUI modal scrolling/spacing in light/dark themes, and that Saved Views preserve expressions after refresh. Compare a saved grid filter with its generated Report. Test a browser time zone different from the application's, including explicit-offset raw timestamps around midnight and DST changes.

The shared fixture runs against the production JavaScript helper and the scheduled Report evaluator. `node MoveItAutomationUI.Tests/Browser/grid-date-filters-smoke.cjs` additionally exercises the production grid matching path, date punctuation, localized month/time text, timezone fallback and existing numeric/text filters across three browser time zones. Windows CI runs the matching C# Report integration tests and the Node suite.

### MOVEit connection banner acceptance

Test Tasks, Upcoming Runs, Hosts, Scripts, Date Lists, Global Parameters, Keys & Certificates, Groups and Permissions, and Task Run/File Activity/Audit Records. On both initial load and automatic/manual refresh, verify one **MOVEit Unreachable** application banner for confirmed DNS, refused connection or network failure, or after two consecutive timed-out grid requests. Include failures after response headers and during token renewal. Multiple upstream failures within one combined grid refresh count once. Failed refreshes retain existing rows and finish loading/busy states; configured refresh intervals and pause/resume remain unchanged.

Cached responses must not clear the warning or reset the timeout streak. Restore connectivity and confirm a successful live grid read clears it; use Refresh Now on cached grids. Include Records' read-only report POSTs. Navigate between grids to verify state persists in the same tab/login/connection; another login or connection must not inherit it. Ordinary HTTP 500, authorization, TLS validation, malformed JSON, browser-to-MAUI failure and navigation cancellation must remain distinct. A non-grid response must not change the grid timeout count. Check that Upcoming Runs does not retain a duplicate local timeout message once the shared banner appears.

For Keys & Certificates, cover All/SSH/TLS/PGP filters and simultaneous SSH/TLS/PGP failures. Test cache retention, missing-cache initial load, recovery, light/dark themes, fixed grid sizing and site-root/virtual-application deployment. Local-only grids do not probe MOVEit. Editor writes, checkout and mutation retries are outside this feature. Automated coverage uses controlled transport failures; complete the corresponding live MOVEit acceptance.

### Connection configuration and grid sizing acceptance

Verify that one configured environment (including multiple HA nodes in that environment) disables and unchecks Copy Between Environments and disables recovery retention, with the two-environment tooltip. Add a second environment, save, enable copying, then remove it: copying must turn off and stay off after reload. Retention must be preserved. Confirm Groups and Permissions keeps the same grid height before and after its first data response, including a slow response; small-window page scrolling must remain usable.

### Modal button readiness

Disabled buttons in MAUI-owned modal shells use one dimmed treatment. Save/Create/Add readiness is controlled by the owning form, using shared quiet validation where appropriate. Busy, loading, checkout-conflict and uncertain-save guards must never be cleared by input/change events. Run/Test/Delete/Close keep their operation-specific rules; the primary Close button used with Auto Save remains enabled when unchanged.

Edge coverage includes blank/changed/reverted Information and Script/Date List forms, hidden Tag and Task folder values, secure Parameter reset/preservation, optional key credentials, PGP password confirmation, native disabled click behavior, both themes and existing responsive layouts. Also test live: New/Clone Task and Host (including required UNC), permitted metadata-only edits through built-in Script Information (the built-in editor remains entirely read-only), template overwrite/retry, grid preference Apply, and returning from nested dialogs. No validation prompt should appear merely from typing; submission validators and server-side guards remain authoritative.

### UI consistency regression checks

- Open representative Quick Transfer Browse/Preflight/New Host, Task, Host, Script, Date List, Global Parameter, Key/Certificate, Report, Resource Group, Failover Status, and Configuration dialogs and confirm they retain the shared MAUI header/body/footer, close-control, alert, loading, and button styling.
- In Quick Transfer Source and Destination Browse, confirm the bordered results frame begins directly below the Location row and retains the same dimensions while loading, empty, populated, and error states are shown; `Loading...` must remain centered inside the frame.
- Open **Create Resource Group** and **Add to Resource Group** and confirm their dependency-loading state uses the standard spinner with `Loading...`, then transitions cleanly to content/error without a separate bespoke loading box.
- Open **Failover Status** and confirm its loading state uses the standard spinner/`Loading...` presentation and the node-card layout is unchanged once data arrives.
- Confirm Global Parameter and Key/Certificate delete dialogs use **Cancel** for the secondary action. Confirm Reset Tamper Detection uses **Cancel** and **Reset Tamper Detection** rather than generic No/Yes labels.
- Under Configuration > Settings > SMTP Configuration, confirm **Clear** uses the same red outline destructive style as other Settings Clear/Remove controls.
- Spot-check Task, Host, Script, Date List, Global Parameter, Key/Certificate, Report, and Records row ellipsis menus for identical trigger/menu styling and normal open/close behavior.
- Spot-check grid refresh controls to confirm Last Refresh, clock menu, Pause Refresh, and Refresh Now remain visually consistent across supported grids.

### Focused validation hardening regression checks

Include these checks in RC3 release validation in addition to the normal release gate:

- Confirm a non-Admin MAUI user cannot call `GET /Server/FailoverStatus`; the action must require `MauiSecurityPolicies.Admin` even if its menu item is not rendered.
- For whole-configuration import, verify a normal definitive MOVEit validation error remains retryable, while an injected upstream timeout/5xx after the native request begins returns an **outcome unknown / verify before retry** response. Do not blindly retry an ambiguous import.
- For Backup Restore with MOVEit configuration selected, verify preflight can still be cancelled before destructive work starts. Once restore mutation begins, closing/disconnecting the browser must not cancel the server-side MOVEit/SQLite/appsettings restore sequence.
- Run the Release test build with warnings-as-errors and confirm there are no xUnit2031 diagnostics. `SystemJobBehaviorTests.GetDueJobKeysAsync_SkipsAndReschedulesMissedDailyRun` intentionally counts the matching saved configuration explicitly instead of filtering with LINQ before `Assert.Single`; do not reintroduce `Assert.Single(collection.Where(...))`.
- Recheck Dashboard File Activity against native MOVEit around the 24-hour boundary. If MAUI and MOVEit are hosted in different timezones, capture representative v1 `LogStamp` values before changing timestamp parsing; do not infer a timezone that the API payload does not establish.

## Automated coverage catalogue

### RC3 automated coverage

The suite targets deterministic application contracts that can be validated without a live MOVEit Automation server or IIS. See [Release validation evidence](#release-validation-evidence) for the current application baseline and its CI run. Historical test counts in CHANGELOG.md describe their own builds. Use the relevant commit's CI results and TRX artifact as the authoritative executed counts; these totals do not certify live MOVEit interoperability.

#### Licensing and capacity

- Permissive fallback when `/api/v0/info` does not expose license information.
- Advanced Task, Custom Script, and PGP feature enforcement.
- Task/Host license-capacity advisories.
- Exact-limit, one-below-limit, exceeded-limit, unlimited-limit, and multi-create boundary behavior.

#### Notification templates

- Optional `html` / `text` template-format normalization.
- Legacy templates with no stored Format remain valid.
- Explicit HTML format persists and reloads correctly.
- Overwriting a template without Format preserves the existing explicit format.
- Notification templates are isolated by MOVEit Environment Key, including identical template names in separate environments.
- Shared Script and email template save authorization accepts either administrator role only with a live session; both save endpoints enforce `TemplateAdmin`, while template loading retains its normal access rules. See the role checks below for live acceptance.

The browser-side legacy HTML-fragment detector remains part of manual UI smoke testing because that logic is intentionally client-side and is not exported as a standalone JavaScript module.

#### Backup retention and cleanup

- Supported retention boundaries (1 and 30 days).
- Unsupported retention values fall back to the configured default.
- Successful no-op cleanup, successful removal, and completed-with-warnings outcomes.
- The Backup Cleanup job passes the expected retention and initiator contract to the backup service.

#### System Jobs

- `HH:mm` run-time validation.
- Known versus unknown System Job keys.
- Scheduler-visible versus hidden/system-managed jobs.
- A genuinely missed daily job is skipped and rescheduled instead of being started late.
- A job inside the scheduler grace period is still due.
- Disabled overdue jobs are neither run nor rescheduled.

#### Configuration safety

- Required-section validation.
- Template-backed creation of missing settings/sections.
- Existing installation-owned values are preserved while missing release metadata is backfilled.
- Malformed existing JSON nodes are rejected rather than silently replaced.
- Nested settings retain the same preservation/create-if-missing rules.

#### Metadata database safety

- Provider normalization.
- Relative SQLite paths resolve under the application content root.
- SQLite `:memory:` remains a special non-file target.
- Persistent SQLite `file:` URI data sources are rejected so they cannot bypass filesystem/web-root path protection.
- SQLite databases under `wwwroot` are rejected.
- Empty databases and existing MAUI databases are accepted for initialization.
- Populated non-MAUI databases are rejected.
- The bundled native SQLite runtime must report version 3.50.2 or later.


#### Release-hardening regressions

- Authenticated MVC controllers reported by static analysis explicitly declare `[Authorize]`, while Login and SessionExpired retain intentional `[AllowAnonymous]` access.
- Admin-only outbound Report sources such as MAUI application Logs are rejected for normal users before data retrieval.
- MAUI Admin authorization requires both the current database-backed Admin role and a live server-side MOVEit session.
- Missing/expired server-side sessions are rejected even when a browser principal still exists.
- Token refresh followed by browser activity preserves the refreshed access/refresh tokens and newest activity timestamp.
- Session-local System Message dismissal cannot overwrite newly refreshed token state.
- Backup Restore waits for the shared `AppSettingsWriteCoordinator`, preventing destructive interleaving with Settings/Credentials/configuration writers.
- Scheduled Report completion preserves an administrator Disable or schedule edit made while the Report is executing.
- Scheduled Report completion still advances `NextRunUtc` when the persisted definition has not changed.

#### Reports and scheduling

- Recipient normalization, duplicate handling, and invalid-recipient rejection.
- Report schedule normalization.
- Weekly-day de-duplication/sorting.
- 5-minute delivery-time enforcement.
- Invalid/incomplete weekly schedules are rejected.
- Daily next-run calculation and exhausted one-time schedules.
- MOVEit time-zone name normalization.

#### Quick Transfer contracts

- Source delete-after-success and Destination overwrite defaults.
- Canonical request normalization clears PGP-key data when Encrypt is not selected and clears temporary-cleanup fields for persistent Task requests.
- PGP key expiration filtering.
- Equivalent `NominalStart` timestamp formatting, missing-identity fallback, conflicting-identity rejection, missing-start-time rejection, bounded start-time correlation, and no-guess behavior for ambiguous running/history rows.

#### Other application contracts

- Stable MOVEit Environment Key normalization.
- Metadata email validation/normalization.
- User preference normalization and start-page routing.
- TLS/PGP expiration evaluation and SSH exclusion.

These tests supplement, rather than replace, MAUI's existing manual functional testing against a real MOVEit environment.

## Maintaining tests

### Adding regression tests

When a defect is fixed, add a regression test when the failure can be reproduced deterministically without depending on production infrastructure. A good regression test should fail on the defective behavior and pass after the fix.

### Test project namespace support

The test project imports `Xunit` globally through its project file, matching the standard xUnit template. Individual test files therefore use `[Fact]`, `[Theory]`, `[InlineData]`, and `Assert` without per-file `using Xunit;` declarations.

### RC3 notification and import acceptance

Host diagnostics retain a five-minute minimum request budget, extended by native connection/data/retry values up to 30 minutes, with independent cleanup. The suite includes a 16-second release check across the former 15-second cleanup limit. These finite budgets cannot guarantee remote cleanup while MOVEit is unreachable.

- Import Tasks and Import Hosts: show five complete data rows plus the header, including with a horizontal scrollbar. Resize/reopen, switch import modes and analyze again; table sizing must stay stable. Scroll to further rows and verify configured page and import batch sizes remain unchanged. Completed imports keep Close rather than Cancel.
- Test Host and Preflight: test returned native failures, transport/timeouts and unavailable MOVEit. Keep detailed result rows, put errors/warnings in the modal footer, reset stale feedback on retry/reopen and restore controls after failure. Retain native-aware Host operation timing and independent checkout cleanup; do not release another editor's checkout or invent a successful cleanup while MOVEit is unavailable.
- Open a parent editor and a child dialog. Trigger modal and background page errors; route to the active dialog, label page notices, preserve native save errors and never display a blurred parent toast. Close/reopen to clear stale feedback.
- Confirm Information, Warning and Error notices in Configuration > Logs. Verify server-derived username/environment and page path, absence of URL queries/form values, credential redaction, and that logging failure causes no retry storm, recursive message or blocked UI action. Unavailable MAUI/logging infrastructure cannot guarantee delivery.
