# MAUI — MOVEit Automation User Interface

- **Version:** `0.18.583-RC3`
- **Release:** Release Candidate 3 (RC3) — **not the final release**
- **Build:** `20261124`
- **Target framework:** .NET 8
- **Database schema / configuration version:** `2` / `1`

MAUI is a web administration interface for on-premises MOVEit Automation. MOVEit remains the system of record and execution engine for Tasks, Hosts, Resources and Automation configuration. MAUI adds operational grids, metadata, reporting, administrative tools and optional editors.

> **Release Candidate 3:** This is pre-release software. It may contain bugs, regressions or incomplete behavior, including differences between MOVEit builds. Evaluate it in a controlled environment, preserve backups and validate the workflows you need before production use. Automated tests do not replace live MOVEit acceptance testing.

**On this page:** [Quick Start](#quick-start-with-visual-studio) · [Requirements](#requirements-and-compatibility) · [Capabilities](#main-capabilities) · [Editors and experimental features](#editors-and-experimental-features) · [Access and safeguards](#access-and-editing-safeguards) · [Getting started](#getting-started) · [Administration](#administration-and-maintenance) · [Limitations](#known-limitations) · [Documentation and project information](#documentation-project-origin-and-license).

## Quick Start with Visual Studio

For a new local instance, use a Windows development computer separate from the MOVEit Automation server. Install **Visual Studio 2022 or later** with the **ASP.NET and web development** workload and **.NET 8 SDK**, plus the compatible **MOVEit Automation Windows COM API**, installed and registered on that computer. Have a development or QA Automation Web Admin HTTPS URL and a MOVEit account ready. See [requirements](INSTALLATION.md#basic-requirements) for COM version compatibility.

1. **Open the solution.** Clone or extract the project into a writable folder, open `MoveItAutomationUI.sln`, and set `MoveItAutomationUI` as the startup project.
2. **Prepare the local settings file.** If `appsettings.json` is missing beside `MoveItAutomationUI.csproj`, create it with just `{}`. Keep any existing settings file and `Data` directory; do not replace them with an empty file or the release template.
3. **Run MAUI.** Select **Debug** and the **https** project launch profile, restore NuGet packages if prompted, then press **F5** or **Ctrl+F5**. Trust the ASP.NET Core development certificate if prompted. MAUI opens at **https://localhost:7184**; IIS is not needed for this local run.
4. **Complete Initial Setup.** Enter the MOVEit Display Name, Environment Key and Web Admin URL, then click **Test Connection**. Choose **SQLite**, keep `Data\maui.db`, click **Test Database**, and select **Complete Setup** after both tests pass. Setup saves the configuration and opens sign-in without an application restart.
5. **Sign in and check the basics.** Select your Automation server and sign in with your MOVEit username and password. Open **Dashboard**, **Tasks > All Tasks** and **Hosts > All Hosts**. The first successful sign-in to a new MAUI database becomes a **MAUI Admin**.

**The MAUI Integration components for MOVEit Web Admin are not required when using the MAUI Editors exclusively.** Enable the editors you need under **Configuration > Settings > Editors**, save the configuration and reload. The COM API remains required. If you choose native MOVEit editors, follow the [optional integration setup](INSTALLATION.md#install-the-moveit-integration-assets), including [reinstallation after every MOVEit Automation upgrade](INSTALLATION.md#reinstall-after-a-moveit-automation-upgrade).

For deployment instructions, start with [INSTALLATION.md](INSTALLATION.md). Release history and RC1/RC2 comparisons are in [CHANGELOG.md](CHANGELOG.md).

<a id="compatibility-and-current-support"></a>

## Requirements and compatibility

| Component | RC3 requirements and targets |
| --- | --- |
| MOVEit Automation | Existing, appropriately licensed **2025.x or 2026.x** installation with API capability enabled. Validate the exact build and required workflows. |
| MAUI host | Windows computer separate from MOVEit Automation. Local Visual Studio runs use the **https** project profile; deployments use a dedicated Windows/IIS server or VM with .NET 8 / ASP.NET Core. Install and register the compatible COM API locally in either case. |
| Application topology | **One active MAUI instance/worker.** For IIS, set Maximum Worker Processes to **1**. SQL Server does not make MAUI active-active capable. |
| Metadata database | SQLite or Microsoft SQL Server. SQLite defaults to `Data\maui.db`. |
| Browser/display | Desktop Google Chrome or Microsoft Edge; recommended resolution **1920×1080**. |

> **Mixed MOVEit versions:** When connecting to both Automation 2025.x and 2026.x, install and register the **MOVEit Automation COM API 18.x**, or a later compatible version, on the MAUI host. Older COM clients cannot connect to Automation 2026.x. Backward compatibility with 2025.x has been observed so far; validate each environment after updating the COM API, then restart the MAUI application pool.

MOVEit version-specific controls are gated by the connected server's reported capabilities. OneDrive Host support and S3 AWS Config File / EC2 Instance Role authentication require **Automation 2026.0+ / native engine 18.0+**. Older or unrecognized versions do not enable these features. Sign in again after upgrading MOVEit to refresh server capabilities. Future MOVEit releases require separate validation.

<a id="key-capabilities"></a>
<a id="application-workspaces"></a>
<a id="grid-datetime-filters"></a>

## Main capabilities

| Workspace | Main functions |
| --- | --- |
| Dashboard | Task/Host health, running Tasks, recent activity, certificate expiry and transfer trends. |
| Tasks | Operational views, Calendar, execution controls, notifications, Preflight, folders, metadata, import/export and optional editors. |
| Hosts | Protocol inventories, configuration, Test Host, shared Browse, assigned Tasks, metadata and supported import/export. |
| Resources | Scripts, Date Lists, Global Parameters, SSH/PGP keys, TLS certificates and Resource Group/User Group permissions. |
| Quick Transfer | MOVEit-admin-only one-off transfers using generated Traditional Tasks, optional PGP processing, Preflight and live status/cancellation. |
| Records and Reports | Task Run, File Activity and Audit grids; Audit configuration comparisons on supported MOVEit 2026 servers; saved reports with scheduled/on-demand HTML and CSV email delivery. |
| Configuration | MOVEit connections, editor preferences, metadata, templates, SMTP, backups, System Jobs, security and application logs. |

Saved Reports support shared CSV access through `/api/reports/{reportGuid}/csv`. Manage the link in **Create Report > API Access** or **Reports > Edit Report Information > API Access**. Create and Edit use matching **Delivery and Schedule** panels, with Time and Repeat alongside each other for recurring delivery. Choose **Delivery: None** to save a report without email recipients or scheduling; API Access remains independent of delivery. Enabling API Access prepares its URL immediately, with **Copy Link** and **Regenerate Link** available before Save. **Save Report** activates that exact URL; closing without saving discards the prepared link. Anyone with the enabled link can retrieve its current report data. See [Reporting API setup](INSTALLATION.md#shared-reporting-api) and the [changelog](CHANGELOG.md).

CSV responses suggest the filename `report.csv`. The default response uses `text/csv` with an inline disposition; browsers can still download it according to their CSV handling. Add `?download=true` to explicitly request an attachment. Both modes return the same raw CSV data.

Shared grids provide sorting, column filters, saved views, CSV/Print and per-user preferences. MAUI metadata includes Tags and organization-defined fields. Task filename search reads configured Source masks and Destination filenames on demand; ordinary grid loads do not fetch Task steps. Date/time filters support comparison operators, alternatives and relative-date macros; use the grid's Filter Help for syntax and examples.

In **Configuration > Settings > Metadata**, choose a resource type and use the **up/down arrows beside Remove** to order its metadata fields. **Save Changes** applies that order to newly opened information dialogs and MAUI editors; **Discard Changes** restores the last saved configuration. Tags can move while its definition remains protected. Metadata Excel exports follow the configured order, and existing values stay associated with their field names.

**List** metadata dropdowns include a blank first option for clearing optional values. The configured possible values follow in their saved order; required fields still require a selection.

**Task Run** includes an optional **Tags** column in Grid Settings and Configuration > Grid Definitions. It uses the Task's current MAUI Tags, matched by Task ID within the active MOVEit environment, with the usual tag pills and column filters. Tags are hidden by default and can be included in CSV/Print and saved, scheduled or API Reports. These are current metadata values when each batch is loaded, not historical tag snapshots; missing metadata is blank. **Refresh Now** reloads the retained rows and their current Tags.

Tasks and Hosts also support JSON element/value searches when the search begins with **`JSON:`**, for example `JSON: DoIfFailure=1`. Only matching objects appear; ordinary search and textbox hints are unchanged. This read-only search is independent of the experimental JSON Editor. In the Tasks grid, join comparisons with `AND` or `OR`, for example `JSON: DoIfFailure=1 AND DoIfSuccess=1`. `AND` takes precedence over `OR`. Put double quotes around literal values that could be read as another comparison. Calendar **Search** highlights matching days/time slots while keeping all runs visible; **Filter** shows only matching runs. Both Calendar modes match Task names.

The **Tasks grid** also supports `ANY(collection, conditions)` and `NONE(collection, conditions)`. Conditions inside a function apply to the **same collection item**, with field paths relative to that item. For example, restrict the grid to Traditional Tasks and use this query to find Tasks without a failure Email Next Action:

```text
JSON:NONE(NextActions.NextAction[*], Type=Email AND DoIfFailure=1)
```

This includes Tasks with no Next Actions, success-only email, or a failure Run Task action without a failure email. Change `NONE` to `ANY` to find Tasks with a failure email. To find Tasks with no failure Next Action of any type, use `JSON:NONE(NextActions.NextAction[*], DoIfFailure=1)`. Arrays and MOVEit's single-object collection shape are supported. You can combine collection functions with ordinary comparisons using `AND`/`OR`; inside each function, use comparisons joined by `AND`/`OR`, rather than nested functions. Quote values containing commas, parentheses or Boolean joins. Existing unscoped comparisons retain their meaning: `DoIfFailure=0` finds at least one zero flag, and unscoped `AND` can match fields from different actions. These collection functions extend the Tasks grid search; the Host grid and JSON Editor search retain their existing syntax.

Task editors and Quick Transfer use the same sortable Browse modal. Double-click a file to select its folder and filename, or select a folder and click OK to leave the filename unchanged. Host Editor **Browse Host** is inspection-only; selecting an entry there does not transfer or modify it.

## Editors and experimental features

<a id="optional-maui-editors"></a>
<a id="moveit-settings-experimental"></a>
<a id="groups-and-permissions"></a>
<a id="built-in-script-guidance-in-task-editors"></a>

### Editor preferences

Choose MAUI/native editors under **Configuration > Settings > Editors**, then Save Configuration and reload MAUI. These are optional RC3 candidate features; this settings location is separate from **Experimental**. Fresh-install defaults are **Task Editor on**, with the other five preferences **off**. Existing installation choices are preserved.

The Task preference covers Traditional, Advanced and Synchronization Tasks together. Supported grid, Calendar and Information links honor the same preferences. Disabling a preference uses the normal native MOVEit integration, without an intermediate fallback page. Related editor modals return to their originating editor where supported.

| Editor | Main workflows and restrictions |
| --- | --- |
| Traditional Task | Source/Process/Destination steps, Local File System steps, schedules, Next Actions, parameters, exceptions, timestamps and Run As Of. Traditional-to-Advanced conversion creates a unique disabled copy. |
| Advanced Task | Nested flow, File Loops, conditional branches, shared transfer/process panels, Send Email, Run Task, Update Original, comments, schedules and flow-image export. |
| Synchronization Task | Folder configuration, direction/policy settings, schedules, Clear Listings/Clear Folders and Sync Preview. |
| Host | Supported protocol profiles, credential preservation/replacement, overrides and shared Browse. Version-specific cloud controls require the corresponding MOVEit capability. |
| Script | Custom PowerShell/VBScript Source, imports/templates, Properties/Tasks, Auto Save and guarded actions. Built-in Scripts are read-only in the editor; permitted MAUI properties/metadata updates use Script Information. |
| Date List | Entries and text import, Properties/Tasks, Auto Save, Undo/export/delete and shared checkout/revision handling. |
| Groups & Permissions | MOVEit-administrator-only Resource Group membership, User Group assignments, permission editing and guarded deletion. Parent rows summarize All / Some / None; expanded rows show exact grants. Deleting a Resource Group does not delete its member objects. |
| MOVEit Settings | Logging, Windows Event Log, Task defaults/AS2 polling, state caching, tamper detection, IP/User policy, trusted IP addresses and lockouts. Requires **both** MOVEit and MAUI administrator access. Unlock operations are confirmed immediate actions, separate from settings Save. |

There are six preference switches: the three Task types share one switch. **Keys & Certificates and Global Parameters always use MAUI forms**, without an editor-preference switch. Global Parameters are maintained through Information; keys/certificates use creation and Information forms rather than separate editors.

Task Process panels display built-in Script descriptions and parameter requirements retrieved from MOVEit. Required parameters can also be supplied through Global Parameters. Built-in Script source/configuration remains read-only.

### Experimental features

The features below are configured under **Configuration > Settings > Experimental** and are **off by default**. Save Configuration and reload the page after enabling a feature. Editor preferences are managed separately.

<a id="experimental-task-json-editor"></a>

#### Task JSON Editor

Select Tasks and choose **Bulk Actions > JSON Editor**. Edit existing scalar values or find matching elements across Traditional, Advanced and Sync Tasks. **Apply Value / Apply Changes stage edits locally**; **Review Changes** opens the review step, and only **Save** writes them. The **Find** and **Replace with** fields reset when the modal opens or Tasks reload successfully. Both result tables use standard Rows and paging controls. The tree search shows only matching Tasks and counts them in the footer. Combine an element with a value using expressions such as `DoIfFailure=1`, `RetryCount>=3` or `Name contains invoice`; ordered comparisons are numeric and text matching ignores case. **Reload Tasks** reloads the entire selected set, even Tasks hidden by the search, and confirms discarding unsaved edits. Filtering changes visibility only; review/save still includes all staged changes.

Save requires an explicit corruption warning with **Cancel / Proceed**. Each affected Task gets its normal undo snapshot before its update; a snapshot failure prevents that Task's write. Shared Task Edit permissions, checkout, revision checks and readback verification still apply. Native identities/references, credentials and other managed values are read-only. There is no Auto Save or durable browser draft. Bulk saves are independent Task operations, not one transaction.

Use this editor cautiously: incorrect JSON values can damage MOVEit configuration. See [Task JSON acceptance and limits](TESTING.md#task-json-editor-acceptance).

<a id="experimental-copy-between-environments"></a>

#### Copy Between Environments — Copy To

Requires **both MOVEit administrator and MAUI Admin access**, at least **two distinct configured environments**, and working administrator **Data Access credentials for both**. Nodes sharing an Environment Key count as one environment. The source is the current connection.

Choose **Copy To** from supported row or Bulk Actions menus in Tasks, Hosts, Scripts, Date Lists or Keys & Certificates. The modal guides object/dependency selection, conflict resolution, naming, Resource Group/Task-folder placement and compatible metadata. Resolve conflicts before Review; overwrites require explicit confirmation. New and overwritten Tasks remain disabled.

- Built-in Scripts and Local File System Host 0 are reused, not copied. Keys are matched by identity and cannot be renamed or overwritten through Copy To.
- Standalone SSH/private-PGP transfer, AS1/AS2/AS3, Global Parameters and references embedded in free-form text are outside automatic copying. Native Task/Host dependency export can carry supported key material.
- Resource Group permission assignments are not copied. Date List group placement is not supported by MOVEit's group member types. Incompatible MAUI metadata fields are skipped without changing destination field definitions.
- Copying continues after modal Close. Stop Copy stops before the next write; process restart marks unfinished work Interrupted. Uncertain writes are not automatically replayed. Retain reports/recovery files and Data Protection keys; recovery retention defaults to 30 days, configurable from 1–90.

See [Copy To limitations and contracts](CHANGELOG.md#copy-between-environments) and [two-environment acceptance](TESTING.md#copy-between-environments-acceptance).

#### MOVEit User Management

MAUI and MOVEit administrators can manage local security groups and their local or domain members on the current Automation server under **Configuration > Settings > Security**. Each managed server runs the optional Local Groups Service from `IntegrationAssets\MOVEitUserManagement`. See [service installation, IIS configuration and certificates](INSTALLATION.md#13-optional-moveit-user-management).

<a id="authentication-and-authorization"></a>
<a id="moveit-object-permissions"></a>
<a id="moveit-license-aware-behavior"></a>
<a id="security"></a>

## Access and editing safeguards

Users sign in with MOVEit credentials. MAUI maintains a separate **MAUI Admin/User** role: the first authenticated user in a new database becomes a MAUI Admin; later new users become MAUI Users. MAUI Admins manage roles under **Configuration > Settings > Security**. MOVEit administrator status does not automatically grant MAUI Admin access.

MOVEit object permissions and license rights remain authoritative. MAUI enforces them on the server and in the UI:

- Creation/import requires eligible permissions. Non-admin creation of Tasks, Hosts, Scripts, TLS certificates, SSH keys and PGP keys requires an eligible Resource Group, shown above Name. The selector is hidden for MOVEit administrators. Date List creation is administrator-only.
- Task **Edit Existing** permits editing the whole existing Task, including adding/removing steps; it does not grant creation or deletion. Editing, notifications, metadata updates and configuration exports require edit access. Running and deleting require their respective permissions.
- Visible objects can open read-only through the configured editor. Read-only viewing locks configuration and MAUI metadata, hides mutation controls, shows status in the footer, and does not acquire edit checkout or Undo snapshots. Information Save is hidden without edit rights.
- All users can see/use Global Parameters; only MOVEit administrators can open their Information modal or delete them. Resource Group administration and Task-folder management/assignment are also MOVEit-admin-only. Non-admins can still view and filter Task folders.
- Saving shared Script/email templates from editable modals requires **either** administrator role. Configuration administration retains its MAUI Admin requirement; template privileges do not grant additional object permissions.

### Checkout, Auto Save and Undo

Normal saves use the shared object services, validate revisions under checkout and verify persisted state. **Checkout Objects for Duration of Edit**, under General > Application Behavior, defaults off. When enabled, editable Task/Host/Script/Date List editors retain checkout for the editing session, with save reacquisition and Close/logout/abandonment cleanup.

Checkout conflicts show MOVEit's owner/time. Only a MOVEit administrator may explicitly override; an override does not itself save edits. Recovery retains the open form, pauses Auto Save and requires manual Save. Unconfirmed writes are not automatically replayed. Unsaved form values are not durable drafts.

Task and Host Auto Save are independent per-user preferences; supported Script/Date List editors also offer Auto Save. Editor **Undo Last Change** reverses one local edit in the current opening and is consumed after use; reopening/reloading resets it. Grid saved-history Undo is a separate operation.

**Rename Task / Rename Host** is below Export in each grid row menu. The modal preloads the current name and immediately honors **Auto Format New Task/Host Names**. Rename validates the name and live duplicates, uses the shared save/Undo/audit workflow and preserves unrelated configuration. Uncertain save responses require refresh and verification before another Rename submission. Local File System Host 0 cannot be renamed.

Passwords/tokens stay server-side; COM/native editing can require the MOVEit password in active-session memory. See [SECURITY.md](SECURITY.md) for credential handling, authorization, antiforgery and checkout details, and [editor acceptance](TESTING.md#rc3-editor-and-crud-acceptance) for validation.

<a id="deployment-topology"></a>
<a id="integration-architecture"></a>
<a id="metadata-database-choices"></a>
<a id="native-moveit-web-admin-integration"></a>
<a id="build-and-source"></a>
<a id="initial-setup"></a>

<a id="shared-moveit-task-crud-architecture"></a>
<a id="shared-resources-crud-architecture"></a>

## Getting started

For local development, use the [Visual Studio Quick Start](#quick-start-with-visual-studio) above. The steps below cover deployment to a separate IIS installation.

**Do not install MAUI on the MOVEit Automation server.** Use a dedicated Windows/IIS host with network access to MOVEit Web Admin and the metadata database. MAUI uses REST for supported operations, a locally registered COM client for retained native operations, and the native Web Admin integration when launching MOVEit's editors. SQLite/SQL Server stores MAUI-owned state; MOVEit remains authoritative.

**The MAUI Integration components for MOVEit Web Admin are not required when using the MAUI Editors exclusively.** These are the browser bridge, styling and logo assets under `IntegrationAssets/MOVEitWebAdmin`. Install them when launching native MOVEit editors from MAUI; enable the applicable MAUI editor preferences for MAUI-only editing. The locally registered MOVEit COM API remains a separate prerequisite. When the integration is used, **reinstall it after every MOVEit Automation upgrade**, because the upgrade overwrites the Web Admin Tomcat folder. Follow the [reinstallation steps](INSTALLATION.md#reinstall-after-a-moveit-automation-upgrade).

1. Review [prerequisites and database choices](INSTALLATION.md#2-prerequisite-checklist).
2. Build/publish from source or prepare a complete matching publish package. See [Build MAUI](INSTALLATION.md#4-build-maui); Visual Studio 2022 or later with the ASP.NET/web workload and .NET 8 is supported.
3. Prepare IIS and install/register the compatible COM API. If native MOVEit editors will be launched from MAUI, configure the [native Web Admin integration](INSTALLATION.md#7-configure-maui-integration-on-moveit-web-admin) on applicable nodes with exact trusted MAUI browser origins.
4. For a **new installation only**, create `appsettings.json` containing `{}` if absent. Grant application Read access and Modify only on that existing file and `Data`, following the installation guide. Preserve any existing settings/data.
5. Open MAUI and complete [Initial Setup](INSTALLATION.md#8-run-initial-setup), which tests REST, COM and database access, provisions the database and proceeds to Login.
6. Configure [Data Access credentials, backups, SMTP and optional workflows](INSTALLATION.md#9-after-setup---first-steps), then complete the applicable [live acceptance checks](TESTING.md#manual-rc3-smoke-test).

Local Visual Studio execution still requires the compatible COM API. RC3 supports IIS site-root and virtual-application deployments; follow the installation guidance for the selected path and bindings.

<a id="configuration"></a>

## Administration and maintenance

| Settings location | Purpose |
| --- | --- |
| General > Application Behavior | Naming defaults, optional edit-duration checkout and Task Failure Alerts. |
| General > Folders | Create root Task folders from Resource Group names; MOVEit-admin-only immediate action. |
| General > Script/Notification Templates | Load, edit/rename and delete environment-scoped shared templates. Script files: `.txt`/`.ps1`; notification files: `.txt`/`.htm`/`.html`. |
| Editors | The six preferences described in [Editor preferences](#editor-preferences). |
| MOVEit Connections | Environment definitions and unattended Data Access credentials. |
| SSH/KEX Algorithms | Live supported choices and configured ordered defaults for new SFTP Hosts/Reset. |
| Backup | Manual/automatic backups, retention and per-environment MOVEit restore selection. |
| Advanced | Keep Alive, controlled restart and Factory Reset. |
| Experimental | The opt-in tools described in [Experimental features](#experimental-features). |

Connection-list updates apply to new logins/background connections without restarting. Existing sessions retain their original endpoint. Changing an endpoint, COM host or Environment Key clears unattended credentials. Startup-sensitive runtime/database changes can require restart.

### Scheduled work, imports and backups

Traditional, Advanced and Synchronization Task editors share these schedule rules. Normal permission, read-only and busy locks still apply.

| Schedule control | When it can be edited |
| --- | --- |
| **Repeat only until first success** | At least one time row in this schedule has **Repeat** selected. It remains editable when all source Hosts have file notifications enabled, with or without forced polling. |
| **Poll for files even though file notifications are also enabled** | At least one time row in this schedule has **Repeat** selected and a source uses notifications. Confirmed non-notifying/no sources disable it; unknown Host metadata remains editable when Repeat is selected. |
| **Every (minutes)** | Its own time row has **Repeat** selected and periodic polling applies: forced polling is selected or not all sources are known to use notifications. |

Schedule help uses full weekday names and describes file arrival, polling or both: **when files arrive between 12:00 AM and 11:59 PM**, **when files arrive and every 5 minutes between 12:00 AM and 11:59 PM**, or **every 5 minutes between 12:00 AM and 11:59 PM**. Mixed notifying/non-notifying sources use the combined wording. One-time rows say **once at [time]**; overnight schedules retain their midnight note.

Each schedule is evaluated independently. Disabling an option retains its saved checked value; source/Host refresh, Repeat changes and Undo update these dependencies immediately. Disabled explanations appear only as tooltips on the control and its label. **Log failure if no files found during scheduled run** remains available with notifications; MOVEit can run the task at the end of the window to determine that failure.

Progress distinguishes editability from execution: its [Task Schedule documentation](https://docs.progress.com/bundle/moveit-automation-web-admin-help-2026/page/Task-Schedule.html) describes first-success as available with Repeat and separately describes when the engine ignores the option. MAUI leaves the choice editable and forwards its value to MOVEit.

Scheduled reports need configured SMTP. IIS startup/preload is needed for scheduled work when the pool would otherwise be cold; **Keep Alive cannot start a stopped application**. See [IIS scheduling guidance](INSTALLATION.md#keep-maui-running-for-scheduled-jobs).

Automatic Backup uses the selected 4-, 8-, 12- or 24-hour interval after the latest successful backup completes, including a manual backup. When an upgrade or restart causes a run to be missed, MAUI advances it to the next interval and retains that time when it becomes due.

**Available Backups** distinguishes In Progress, Complete, Partial, Failed and Interrupted runs, with the current phase or a short explanation. The list refreshes automatically while the Backup section is visible (every 3 seconds during a run, otherwise every 30 seconds). MAUI copies its configuration before exporting MOVEit, logs cancellation, and recovers tracked interrupted runs at startup. Empty interrupted folders are removed only when they contain MAUI status files alone; existing backup data and legacy folders are preserved. Restore still requires a completed, validated backup. In **Restore MAUI Backup > Restore Scope**, **MAUI only** is selected by default and restores appsettings.json and the backed-up SQLite database without importing MOVEit configuration. Choose **MAUI and selected MOVEit configurations** to include checked environments; that scope requires at least one selection. SQL Server databases require separate recovery.

Quick Transfer is separately enabled under General and uses an available Traditional Task slot. **Delete temporary task after successful transfer** controls retention; the redundant Create Traditional Task option has been removed. Successful temporary Tasks are eligible for verified cleanup; unsuccessful/ambiguous runs are retained. Confirmation-email recipients stay visible inside the email option grouping and are disabled while it is unchecked. Unattended cleanup needs Data Access credentials. See [Quick Transfer acceptance](TESTING.md#quick-transfer-validation).

Use the packaged spreadsheets under `Templates/TaskImport` and `Templates/HostImport`, outside `wwwroot`, for imports. Restore and import operations need their normal permissions and backups; see the relevant [acceptance guidance](TESTING.md#imports-exports-and-environment-copy). Back up external SQL Server databases separately from MAUI file backups.

Native Task/Host `.maui` imports default compatible dependency conflicts to **Reuse Existing**. Reused Hosts, Scripts and Date Lists retain destination configuration, credentials, Resource Groups and MAUI metadata; recognized references are remapped when IDs differ. Key/certificate reuse requires the same native ID. Task overwrite/import-as-new choices are unchanged, and Global Parameters continue to resolve by name. See [dependency reuse acceptance](TESTING.md#native-import-dependency-reuse-acceptance).

### MOVEit connection feedback

MOVEit-backed grid loads/refreshes show **MOVEit Unreachable** for detected DNS/refused-connection/network failures or after two consecutive timeouts. This describes connectivity from the MAUI host, not proof that the Windows server is shut down. Existing rows remain visible and may be stale; repeated connectivity-error toasts are suppressed while the banner is active.

A successful live grid read clears the banner; cached rows alone do not prove recovery. Authentication, permission, certificate, ordinary HTTP 500 and malformed-data errors remain separate. Grid connection handling does not replay editor writes or checkout operations. See [connection acceptance](TESTING.md#moveit-connection-banner-acceptance).

Messages from an open modal stay inside that modal; signed-in toast notices are also sent to Configuration > Logs on a best-effort basis.

<a id="upgrades"></a>
<a id="release-controlled-vs-installation-owned-files"></a>

### Upgrades and persistent data

Deploy one complete matching publish output. Preserve **live `appsettings.json` and the entire `Data` directory**, including databases, backups, cleanup/recovery records and Data Protection keys. Build/publish excludes live appsettings.json. Release templates, `appsettings.custom.json`, `upgrade.json` and assets must match the deployed binaries; do not use a broad `appsettings*.json` exclusion or delete the destination directory.

Back up first, finish active work, take MAUI offline, replace release-owned files, verify the package, and complete `/Upgrade` as a MAUI Admin if requested. The current release manifest does not force an additional restart; startup-sensitive changes can still require Restart MAUI. Follow [UPGRADE.md](UPGRADE.md) for the full procedure and recovery rules.

<a id="rc3-scope-and-limitations"></a>
<a id="native-functionality-not-included-in-rc3"></a>

## Known limitations

- **RC3 is a candidate, not a production-readiness certification.** Automated checks use fixtures/mocks; live authentication, protocol behavior, IIS deployment and each MOVEit build still need validation. Live S3/Google testing has not been available; accepted response patterns are documented in [TESTING.md](TESTING.md#moveit-2026-host-acceptance).
- **AS1/AS2/AS3 Host/editor support is deferred** because the required license is unavailable. SSO is not supported.
- **OneDrive Host** configuration, Test and Browse are implemented for supported 2026 servers. OneDrive Task-step editing and Quick Transfer composition await native Source/Destination patterns.
- **MOVEit System Log, Service Shutdown, Virus Test and Watched Tasks** have no MAUI replacement in RC3; use native MOVEit where applicable.
- The COM support layer remains required for retained native operations. Native-editor integration assets are optional for MAUI-only editing. When that integration is used, **reinstall it after every MOVEit Automation upgrade** because the upgrade overwrites the Tomcat folder; see the [reinstallation steps](INSTALLATION.md#reinstall-after-a-moveit-automation-upgrade).
- A native rename can complete before a local MAUI audit update fails. Logging preserves the underlying database details, but the reported SQLite audit failure's cause remains unconfirmed. Verify the saved name before retrying.
- MAUI's web tier is limited to the [single-instance topology](#requirements-and-compatibility). Copy To has additional [object/dependency limitations](#copy-between-environments--copy-to).

## Documentation, project origin and license

### Documentation

| Document | Purpose |
| --- | --- |
| [INSTALLATION.md](INSTALLATION.md) | Prerequisites, build, IIS/database setup, Initial Setup and native integration. |
| [UPGRADE.md](UPGRADE.md) | Upgrade procedure, installation-owned data, release-package checks and recovery. |
| [TESTING.md](TESTING.md) | Commit-specific validation evidence, automated checks and live acceptance scenarios. |
| [SECURITY.md](SECURITY.md) | Security policy, roles/credentials, request controls, operational safeguards and scanning guidance. |
| [CHANGELOG.md](CHANGELOG.md) | RC3 history, historical RC1/RC2 changes, shared CRUD/transport contracts and validation records. |
| [LICENSE](LICENSE) | MAUI Community Source License 1.0. |
| [Data/README.txt](Data/README.txt) | Persistent Data-directory guidance. |

### Project origin and independence

MAUI is an independently developed administration interface. **This project is not an AI reverse-engineered version of MOVEit Automation. No compiled MOVEit Automation code was disassembled or decompiled to build MAUI.** AI-assisted development was used to help implement this separate application; it does not constitute a reconstruction of MOVEit's compiled implementation.

MAUI integrates with MOVEit through REST APIs, the MOVEit Automation Windows COM API, and captured Web Admin request/response patterns. MOVEit Automation remains the system of record and execution engine. MAUI requires an existing, appropriately licensed MOVEit Automation installation; it does not replace that product or its license. The project is not affiliated with or supported by Progress Software.

### License and source availability

Copyright (C) 2026 Marc R. Davis.

MAUI is **source-available** software distributed under the **MAUI Community Source License 1.0**, included in [`LICENSE`](LICENSE). The license permits internal organizational use, study, modification, noncommercial redistribution, and professional implementation/support services subject to its terms. Distributed derivative works must remain source-available under the same license. MAUI or a derivative may not be sold, commercially licensed as software, or offered as a paid hosted software service without separate written permission from the copyright holder.

Because the license restricts certain commercial software uses, MAUI should not be described as OSI-approved open-source software. Review the complete `LICENSE` terms before use or redistribution.

### Disclaimer and trademarks

MAUI is an independent project. It is **not affiliated with, endorsed by, sponsored by, or supported by Progress Software Corporation**. Progress Software provides no support or warranty for MAUI.

This software is provided **"as is"**, without warranty of any kind, express or implied, including but not limited to warranties of merchantability, fitness for a particular purpose, and noninfringement. Use of MAUI is at your own risk. The author(s) and contributors are not liable for claims, damages, data loss, service interruption, or other liability arising from use of the software to the extent permitted by applicable law.

Administrators are responsible for validating MAUI in their own environment, maintaining appropriate backups, protecting credentials/configuration, complying with applicable MOVEit licensing, and following their organization's security/change-management requirements.

Progress, MOVEit, and related Progress product names and marks are trademarks or registered trademarks of **Progress Software Corporation and/or its subsidiaries or affiliates** in the United States and other countries. Their use in this project is referential only and does not imply affiliation, endorsement, or sponsorship. See the [Progress Software Corporation trademark policy](https://www.progress.com/legal/trademarks/trademarks-use-policy) for current trademark guidance.

### Documentation maintenance

Maintain only `README.md`, `CHANGELOG.md`, `INSTALLATION.md`, `SECURITY.md`, `TESTING.md`, `UPGRADE.md` and the existing `LICENSE`. Incorporate implementation and validation notes into these documents. **Do not create a new `.md` file without Marc's approval.** Keep current RC3 guidance separate from historical release notes and distinguish automated checks from live IIS/MOVEit acceptance.

