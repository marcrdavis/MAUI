# MAUI Installation Guide

- **Version:** `0.18.511-RC3`
- **Release:** Release Candidate 3 (pre-release)
- **Build:** `20261029`
- **Database schema:** `1`
- **Configuration version:** `1`

> **Release Candidate 3 — not the final release.** This pre-release may contain bugs, regressions or incomplete behavior. Install it in a controlled test environment first, preserve backups, and complete the applicable live acceptance checks before relying on it in production.

This guide covers a new MAUI installation and the required MAUI Integration on MOVEit Automation Web Admin. Before production deployment, also review [`SECURITY.md`](SECURITY.md) and complete the release/smoke-test guidance in [`TESTING.md`](TESTING.md).

**In this guide:** [1. Supported deployment](#1-supported-deployment) · [2. Prerequisite checklist](#2-prerequisite-checklist) · [4. Build MAUI](#4-build-maui) · [5. Prepare the IIS application](#5-prepare-the-iis-application) · [7. Configure MAUI Integration on MOVEit Web Admin](#7-configure-maui-integration-on-moveit-web-admin) · [8. Run Initial Setup](#8-run-initial-setup) · [10. Protect installation-owned files](#10-protect-installation-owned-files) · [12. Troubleshooting checklist](#12-troubleshooting-checklist)

## 1. Supported deployment

MAUI RC3 is intended for MOVEit Automation **2025.x** and **2026.x** with the MOVEit **API capability licensed and enabled**. Validate the exact MOVEit build and required workflows in your environment before production use.

MAUI must be hosted on Windows using IIS and ASP.NET Core/.NET 8.

> **Do not install MAUI on the MOVEit Automation server.** Deploy MAUI to a dedicated Windows/IIS server or virtual machine. The MAUI server must be able to reach MOVEit Web Admin and, when used, the SQL Server hosting the MAUI metadata database.

The MOVEit Automation Windows COM API must also be installed and registered **on the MAUI IIS server**. MAUI uses the COM API for operations that are not consistently available through REST.

> **Important — mixed MOVEit versions:** If MAUI connects to both MOVEit Automation 2025.x and 2026.x, install and register the **MOVEit Automation COM API 18.x**, or a later compatible version, on the MAUI server. Older COM API versions cannot connect to MOVEit Automation 2026.x. The newer COM API appears backward compatible with 2025.x at this time; validate connections and required workflows against each environment after upgrading. Restart the MAUI application pool after updating the COM API.

## 2. Prerequisite checklist

Before deploying MAUI, prepare the following.

### Windows / IIS host

- A dedicated Windows Server or Windows VM for MAUI.
- IIS installed and configured for HTTPS.
- The **.NET 8 ASP.NET Core Hosting Bundle** installed on the IIS server. This provides the .NET runtime and IIS ASP.NET Core Module required to host the application.
- A dedicated IIS site/application pool for MAUI.
- The IIS **Application Initialization** role service for unattended startup of MAUI scheduled jobs; see [Keep MAUI running for scheduled jobs](#keep-maui-running-for-scheduled-jobs).
- An application-pool identity with **Read** access to application files, **Modify** access to the **existing** `appsettings.json` file and **Modify** access to `Data` (including `Data\ConfigurationRecovery`). **Do not** grant Modify or Full Control access to the application root.
- Network access from the MAUI server to the MOVEit Automation Web Admin URL.
- Network access to SQL Server when SQL Server is used.
- A production TLS certificate for the MAUI IIS site.

### MOVEit Automation

- MOVEit Automation 2025.x or 2026.x.
- MOVEit API functionality licensed/enabled.
- MAUI evaluates the MOVEit license attributes returned by `/api/v0/info` at sign-in. MAUI uses those attributes to gate Advanced Task creation, custom Script management, PGP operations, and finite Task/Host creation capacity. AS2 and Backup Host attributes are informational only in RC3.
- MOVEit Web Admin reachable from the MAUI IIS server over HTTPS.
- MOVEit credentials are **not** required for the Initial Setup connectivity test. Credentials are supplied later at normal MAUI sign-in.
- The MOVEit Automation Windows COM API installed and registered on the MAUI IIS server. Mixed 2025.x/2026.x environments require COM API 18.x or a later compatible version; see the compatibility notice in [Supported deployment](#1-supported-deployment).

### Build tools when compiling from source

- Visual Studio **2022 or later**.
- The **ASP.NET and web development** workload.
- .NET 8 SDK.

The .NET 8 SDK alone can also build/publish from the command line.

The listed MOVEit version families are compatibility targets. Validate the exact server build and licensed workflows using [the RC3 acceptance guide](TESTING.md#rc3-editor-and-crud-acceptance).

## 3. Choose the MAUI metadata database

MAUI stores only MAUI-owned state in this database. MOVEit Automation remains the system of record for Automation objects and execution data.

### Option A - SQLite

SQLite is the default and is appropriate for the supported single-instance RC3 deployment model, evaluation, and testing.

Default location:

```text
Data\maui.db
```

Requirements:

- The IIS application-pool identity must have Modify access to the database directory.
- Relative paths are resolved under the MAUI application content root; absolute filesystem paths are also supported when permissions allow.
- The path must remain outside `wwwroot`.
- Persistent SQLite `file:` URI data sources are intentionally rejected in RC3; use a normal filesystem path. `:memory:` is reserved for isolated testing.
- The database file and its `-wal` / `-shm` files are installation-owned and must not be deleted during application upgrades.
- Include the database in the site's backup plan. MAUI Backup can create a consistent SQLite snapshot when configured.

RC3 supports one active MAUI application instance/worker regardless of whether SQLite or SQL Server is selected.

### Option B - SQL Server

SQL Server is appropriate for centralized database administration and larger installations. **MAUI RC3 supports one active MAUI application instance/worker.** Do not run multiple active MAUI web instances against the same metadata database; SQL Server does not by itself provide shared session state or distributed System Job ownership.

Prepare an empty MAUI database or an existing valid MAUI database at the schema level expected by the release. Initial Setup refuses to silently adopt an unrelated non-empty database.

Supported authentication modes in Setup:

- Windows Integrated Authentication
- SQL Server Authentication

The MAUI database principal must be able to:

- connect to the selected database;
- read and write MAUI application tables;
- create/alter/drop tables, columns, and indexes required by Initial Setup and future schema migrations.

For Windows Integrated Authentication, SQL Server sees the MAUI IIS application-pool identity; grant that identity appropriate database permissions.

MAUI exposes SQL encryption and **Trust server certificate** options during Setup. Trusting an unvalidated SQL certificate should be limited to environments where that risk is understood and accepted.

SQL Server database backup/restore is administrator-managed. MAUI does not replace normal SQL Server backup procedures. When restoring a MAUI backup, native MOVEit configuration imports are optional and selected individually per backed-up environment. Leave every environment unchecked for MAUI-only restore. MAUI settings and supported metadata are still restored, and a restart is required.

## 4. Build MAUI

Skip this section when deploying prebuilt publish output.

### Visual Studio

1. Open `MoveItAutomationUI.sln` in Visual Studio 2022 or later.
2. Confirm the .NET 8 SDK and ASP.NET/web workload are installed.
3. Restore NuGet packages.
4. Select **Release** configuration.
5. Build the solution and confirm there are no unresolved build warnings/errors.
6. Run the `MoveItAutomationUI.Tests` test project and confirm the regression suite passes.
7. Publish `MoveItAutomationUI` (not the test project) to a Folder target for deployment to IIS.

### Command line

From the source directory, use the same release gate documented in `TESTING.md`:

```powershell
dotnet clean .\MoveItAutomationUI.sln
dotnet restore .\MoveItAutomationUI.sln
dotnet build .\MoveItAutomationUI.sln --configuration Release --no-restore
dotnet test .\MoveItAutomationUI.sln --configuration Release --no-build
dotnet publish .\MoveItAutomationUI.csproj --configuration Release --output .\publish
```

RC3 application identity:

```text
Version 0.18.511-RC3
Build   20261029
```

See [`TESTING.md`](TESTING.md) for automated coverage and the recommended manual RC3 smoke test.

### Run MAUI locally from Visual Studio

MAUI can be run directly from Visual Studio for development and pre-deployment testing; IIS is not required for this workflow. Local MAUI still connects to a real MOVEit Automation environment, so use an appropriate development/test Automation environment when exercising operations that change Automation configuration or execute Tasks.

1. Open `MoveItAutomationUI.sln` in Visual Studio and set `MoveItAutomationUI` as the startup project.
2. Select the **https** launch profile. The project is currently configured to launch at:

   ```text
   https://localhost:7184
   ```

   The project also provides `http` (`http://localhost:5084`) and IIS Express profiles, but the `https` profile most closely matches the intended production browser behavior.
3. Press **F5** to run with the debugger or **Ctrl+F5** to run without debugging. If the development HTTPS certificate is not trusted, allow Visual Studio/.NET to trust the local development certificate before continuing.
4. On a clean source checkout, appsettings.json is the source-tree `{}` file; it is excluded from build/publish output. The browser should open the normal **Initial Setup** workflow. `appsettings.Development.json` supplies development logging only; it does not bypass Initial Setup or provide a separate development MAUI configuration.
5. Complete Initial Setup using the MOVEit and metadata-database settings intended for the local test instance. SQLite is the simplest choice for isolated local testing unless SQL Server behavior specifically needs to be exercised.
6. Initial Setup populates the existing `appsettings.json`, reloads the configuration and activates the metadata database in the running process. The browser proceeds directly to Login; **do not stop/restart Visual Studio just to finish Setup**.
7. Sign in normally and test the required MAUI workflows.

The development workstation must reach the configured MOVEit Automation Web Admin URL and have the compatible **MOVEit Automation Windows COM API installed and registered before Initial Setup**. Setup checks local COM activation, just as it does on the production IIS host. Some REST-backed pages may still load in an already configured instance when COM becomes unavailable, but that is a partially working installation rather than an alternative deployment mode.

#### Testing native MOVEit editor integration from localhost

The native editor bridge validates the browser origin that launched the MOVEit Web Admin window. To test native-editor integration from the recommended Visual Studio `https` profile, include `https://localhost:7184` in the deployed MOVEit Web Admin copy of `maui-integration.js`:

```javascript
const LAUNCHER_ORIGINS = [
    "https://maui-dev.example.com",
    "https://maui-qa.example.com",
    "https://maui.example.com",
    "https://localhost:7184"
];
```

Use `http://localhost:5084` instead when intentionally testing with the `http` profile. IIS Express uses its own localhost origin/port from `Properties\launchSettings.json`. Add only the local origins that should be trusted by this MOVEit Web Admin instance. Ordinary MAUI testing that does not launch the native editor does not require a localhost entry.

## 5. Prepare the IIS application

1. Create a dedicated application directory, for example:

   ```text
   D:\Apps\MAUI
   ```

2. Copy the published MAUI files into the application directory. Build/publish excludes appsettings.json. For a NEW installation only, create that file containing `{}` before starting MAUI; never replace an existing configured file.
3. Create a dedicated IIS application pool for MAUI.
   - Under **Advanced Settings**, set **Maximum Worker Processes** to **1**. RC3 does not provide distributed session state or System Job ownership across multiple IIS workers.
4. Configure an IIS site/application that points to the MAUI application directory.
5. Bind the site to HTTPS using the intended MAUI hostname.
6. Grant the application-pool identity Read access to the MAUI application root, Modify access to the **existing** `appsettings.json`, and Modify access to `Data` (including `Data\ConfigurationRecovery`). Keep the application root read-only for the app pool.
7. If SQL Server Integrated Authentication is used, grant the same identity appropriate access to the MAUI SQL database.
8. Confirm the .NET 8 Hosting Bundle is installed and IIS has been restarted after Hosting Bundle installation if required.

Both CopyToOutputDirectory and CopyToPublishDirectory are Never for appsettings.json, so normal build/publish output does not carry installation settings. Publish/package from a clean staging directory; an old file left in a reused staging directory or an existing deployment package can still be deployed by external tooling. Do not clean the live installation to obtain a clean package.

For a NEW installation only, create appsettings.json containing `{}` and apply its individual Modify ACL. For upgrades, preserve the existing configured file and Data. Initial Setup populates the existing empty JSON object from appsettings.template.json; a missing or zero-byte file is not a valid substitute.

### Keep MAUI running for scheduled jobs

MAUI's System Job scheduler runs inside the web application. If IIS shuts down the application after inactivity, scheduled jobs cannot run while it is stopped. A browser does not need to remain open when IIS is configured to keep MAUI running.

MAUI's project explicitly selects **in-process** IIS hosting. Confirm the published `web.config` contains `hostingModel="inprocess"` on its `<aspNetCore>` element. In Server Manager, use **Add Roles and Features > Web Server (IIS) > Web Server > Application Development** to install **Application Initialization**.

In IIS Manager, apply these settings to MAUI:

| Location | Setting | Value |
| --- | --- | --- |
| Application Pools > MAUI pool > Advanced Settings > General | Start Mode | `AlwaysRunning` |
| Application Pools > MAUI pool > Advanced Settings > Process Model | Idle Time-out (minutes) | `0` |
| Sites > MAUI site/application > Advanced Settings | Preload Enabled | `True` |

`AlwaysRunning` alone does not prevent idle shutdown. Application Initialization and preload start MAUI when its pool starts or recycles. Keep **Maximum Worker Processes** at **1**.

If the published configuration uses `hostingModel="outofprocess"`, idle-timeout guidance differs: use an external keep-alive request and verify unattended operation rather than relying on the pool settings alone. See Microsoft's [Application Initialization and idle-timeout guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/advanced?view=aspnetcore-8.0#application-initialization-module-and-idle-timeout).

MAUI also provides **Configuration > Settings > Advanced > Keep Alive Service**, enabled by default with a **Ping Interval** of **5 minutes** (allowed range **1–20**). Save Configuration applies changes immediately. The worker periodically requests a private, lightweight MAUI endpoint without signing in, extending a user session, or calling MOVEit or the metadata database. It uses the observed local server binding and preserves the IIS application path, including `/MAUI`; HTTPS certificate validation remains enabled.

Keep Alive works only while MAUI is already running and after a normal request has established its local address. It cannot start a stopped/cold application pool, survive a process/server shutdown, or replace IIS startup and preload. Choose an interval shorter than any configured IIS idle timeout. IIS in-process hosting and the settings above remain the recommended deployment; keep-alive connection/certificate failures appear in MAUI logs without repeated toasts.

After applying the settings:

1. Configure a harmless System Job to run after the previous idle-timeout period.
2. Close MAUI browser tabs and recycle the MAUI application pool once.
3. Leave MAUI unopened until after the scheduled time, then check the job's recorded run time and output. Confirm it ran at the scheduled time, before opening MAUI again.

Review the pool's **Recycling** schedule separately. Idle Time-out `0` does not disable scheduled recycling, and recycling, deployment, or a server restart can interrupt work. Plan maintenance around important jobs; retain appropriate IIS health protections. See Microsoft's [application-pool recycling settings](https://learn.microsoft.com/en-us/iis/configuration/system.applicationhost/applicationpools/add/recycling/periodicrestart/).

A separate Windows Service for the scheduler could provide independence from IIS in a future release. That deployment option is not currently implemented; these instructions configure the existing web-hosted scheduler.

### IIS application path deployment (DEV / QA)

MAUI supports either a dedicated IIS website at `https://maui.company.com/` or an **IIS application** under a shared website such as `https://iisserver.company.com/MAUI/`.

1. In IIS Manager, use **Convert to Application** on the `MAUI` folder (or use **Add Application**). Point its physical path at MAUI's published ASP.NET Core files; assign the intended app pool. A plain virtual directory alone is not a substitute for an ASP.NET Core application.
2. Visit `https://iisserver.company.com/MAUI/`. MAUI's first-run redirect should lead to `/MAUI/Setup`, followed by `/MAUI/Account/Login` once configuration has been completed.
3. Test `/MAUI/Upgrade` during a pending upgrade, Dashboard **View all Hosts/Tasks**, static assets, and session-expiration routing. Cookies and standard Razor/action-generated links are scoped to the current application path.
4. A dedicated root website continues to use `/Setup`, `/Account/Login`, and other root-relative routes. No `/MAUI` path should be hardcoded in configuration or application source.

The application prefix is taken from ASP.NET Core's `HttpRequest.PathBase` supplied by IIS integration. If a reverse proxy strips the prefix before passing the request to ASP.NET Core, configure that proxy and the app's forwarded path-base handling separately; this package does not infer a missing prefix from arbitrary request headers. Avoid running two conflicting MAUI instances with shared installation state.

## 6. Install the MOVEit Automation COM API on the MAUI server

MAUI requires `MICentralAPICOM.MICentralAPI` for COM-backed Automation operations.

For mixed 2025.x/2026.x environments, use COM API **18.x or a later compatible version**; older COM API versions cannot connect to Automation 2026.x. See the compatibility notice in [Supported deployment](#1-supported-deployment).

Install/register the MOVEit Automation Windows COM API on the **MAUI IIS server**, not merely on the MOVEit Automation server. After installation:

1. Confirm the COM component is registered on the MAUI server.
2. Confirm the IIS application-pool identity can instantiate/use the component under your Windows security policy.
3. Restart the MAUI application pool after installing or updating the COM components.

A missing or inaccessible COM API may allow REST-backed areas of MAUI to work while COM-backed operations fail. Initial Setup tests the registered `MICentralAPICOM.MICentralAPI` ProgID by instantiating and releasing the COM object locally. This does **not** log in to MOVEit and requires no MOVEit username/password; it verifies installation/registration, process compatibility, and COM activation under the MAUI process identity.

## 7. Configure MAUI Integration on MOVEit Web Admin

MAUI offers optional Task, Host, Script, Date List and Groups & Permissions editors. Native MOVEit Web Admin is still used according to editor preferences and for native-only operations and unsupported types. The browser bridge and native-editor styling must be installed on each MOVEit Web Admin/Tomcat node that users may reach.

The MAUI source/release contains:

```text
IntegrationAssets\MOVEitWebAdmin\maui-integration.js
IntegrationAssets\MOVEitWebAdmin\maui-native-editor.css
IntegrationAssets\MOVEitWebAdmin\automation-mark.svg
```

### 7.1 Back up the native Web Admin files

Before modifying MOVEit Web Admin, back up the existing Tomcat ROOT `index.html` and any prior MAUI integration assets according to your change-management procedures.

### 7.2 Configure trusted MAUI launcher origins

Open the release copy of:

```text
maui-integration.js
```

Before copying the integration script to MOVEit Web Admin, configure `LAUNCHER_ORIGINS` with every MAUI browser origin that is allowed to launch this MOVEit Web Admin instance. This supports environments where development, QA, and production MAUI applications share one Automation/Web Admin server while keeping the browser trust boundary explicit.

Example:

```javascript
const LAUNCHER_ORIGINS = [
    "https://maui-dev.example.com",
    "https://maui-qa.example.com",
    "https://maui.example.com"
];
```

A non-default port is part of the origin and must be included:

```javascript
const LAUNCHER_ORIGINS = [
    "https://maui-dev.example.com:8443",
    "https://maui-qa.example.com:8443",
    "https://maui.example.com:8443"
];
```

Origins are exact scheme/host/port trust entries. Wildcards such as `https://*.example.com` are not supported, and the integration never uses `"*"` as a `postMessage` target origin. Keep the list limited to MAUI applications that are intentionally trusted to perform the native-editor login handoff.

For each popup launch, MOVEit initially sends only non-sensitive readiness messages against the configured exact origins. The first valid message received from the actual `window.opener`, with the matching per-launch ID, binds that popup session to one exact MAUI origin. Login acknowledgements, editor state, Task-ID rebind, and Undo Last Change messages then remain pinned to that origin for the life of the popup/editor session.

### 7.3 Copy the integration assets

Copy all three files to the MOVEit Web Admin Tomcat ROOT directory:

```text
maui-integration.js
maui-native-editor.css
automation-mark.svg
```

The usual target is the MOVEit Web Admin `Tomcat\webapps\ROOT` directory. Use the actual Tomcat ROOT location for your MOVEit installation.

For a new installation, deploy all three files together. For an upgrade, ensure the deployed integration assets match the current MAUI package. Whenever `maui-integration.js` is replaced, configure `LAUNCHER_ORIGINS` for all trusted MAUI environments before copying it to Tomcat. `MAUI_INTEGRATION_ASSET_REVISION` in `maui-integration.js` controls browser cache-busting for the dependent CSS/logo and is intentionally independent of the MAUI application version when later application releases do not change the Web Admin integration assets.

### 7.4 Update `index.html`

Edit the MOVEit Web Admin Tomcat ROOT `index.html` and add the following immediately before the closing `</body>` tag:

```html
<script src="maui-integration.js" type="text/javascript"></script>
```

Save the file and verify the script remains present after any MOVEit Automation/Web Admin upgrade or repair operation. Vendor upgrades may replace `index.html`; reapply the MAUI script reference if necessary.

### 7.5 HA/failover environments

If users can reach more than one MOVEit Web Admin/Tomcat node, deploy the same MAUI integration assets and `index.html` change to **every applicable node**. Keep the `LAUNCHER_ORIGINS` allow-list consistent across those nodes.

### 7.6 Validate native-editor launch

After MAUI and MOVEit Web Admin changes are complete:

1. Sign in to MAUI.
2. Open a supported object from MAUI into the native editor.
3. Confirm the native page loads in the MAUI-launched window.
4. Confirm MAUI native-editor styling loads.
5. Confirm the MAUI working overlay behaves correctly during login/navigation/refresh.
6. From a Task ellipsis menu, run **Preflight Check** and confirm the **Task Preflight Checks** modal advances through all stages and reports only read-only checks.

7. For a Task, Host, Script, or Date List, make a successful native change and confirm **Undo Last Change** appears in the native header. Test one undo and confirm the editor reloads with the prior saved state.
   - For a Task, move between **Task Details**, **Settings**, and any other Task-detail child routes used by the native editor. Make a saved change and confirm **Undo Last Change** remains tied to the active Task.
   - When validating MOVEit's **Convert to Advanced Task** workflow, confirm the same popup establishes a fresh baseline for the newly created Advanced Task, then make a later saved change (including from **Settings**) and confirm **Undo Last Change** appears. The conversion itself is not an Undo target.
   - If an Undo cannot be completed safely, confirm the editor remains usable and the unavailable Undo is not repeatedly offered.
8. Test native-editor launch from **each configured MAUI origin** (for example development, QA, and production), including a successful saved change and **Undo Last Change**. If MOVEit Web Admin is HA/load-balanced, also validate the integration on each Web Admin node users may reach.

SSO is not currently supported by MAUI. Native integration should be tested with the same credential-based login workflow used by the application.

## 8. Run Initial Setup

Start the IIS site and browse to the MAUI URL. With the administrator-created, unconfigured `{}` appsettings.json, MAUI routes to the application-relative `/Setup`.

### MOVEit connection

Enter:

- Display Name
- Environment Key
- MOVEit Web Admin URL
- invalid-certificate option only when intentionally required for a test/non-production environment

MAUI generates the internal server key. Additional HA/failover nodes can be added later in **Configuration > Settings > MOVEit Connections** using the same Environment Key.

### Database

Choose SQLite or SQL Server and enter the applicable connection information.

Use **Test** for both MOVEit and the database. **Complete Setup** remains unavailable until both tests pass. MAUI repeats validation when Setup is saved; the browser Test buttons are not treated as proof that the final submitted values are valid.

Setup then:

1. provisions the MAUI metadata database to schema version `1`;
2. seeds initial MAUI data where applicable;
3. populates the existing `appsettings.json` from the release template;
4. records configuration version `1`;
5. reloads the live configuration, activates the selected metadata database and runtime options, and checks Upgrade state;
6. opens normal application routes only after successful activation, then takes the browser to Login.

**An IIS application-pool recycle is not required after Initial Setup.** If in-process activation fails after saving, the application remains Setup-gated; retry Complete Setup and consult MAUI's server log for the underlying exception.

The first successfully authenticated user in a new MAUI database becomes a MAUI Admin.

## 9. After Setup - First Steps

After Initial Setup, sign in immediately as the first MAUI Admin account; no IIS recycle is needed. Complete these items before relying on unattended/background functions.

### 9.1 Configure Data Access Credentials

Go to **Configuration > Settings > MOVEit Connections**. For each MOVEit environment that will use unattended functions, configure and test **Data Access Credentials** on at least one active server/node.

Use a MOVEit Administrator account intended for unattended MAUI operations. These credentials are used by scheduled Reports, Host Health Check, and automatic Backup. Passwords are protected with Windows DPAPI LocalMachine scope and are not returned to the browser.

For HA/failover environments, configure credentials on the server entries you expect MAUI to use and test them before enabling unattended jobs.

### 9.2 Configure Backup

Go to **Configuration > Settings > Backup**.

1. Review the backup destination under `Data\Backups`.
2. Choose the desired automatic backup frequency.
3. Review **Backup Retention** (1-30 days; default 30 days).
4. Enable **Automatic Backup of MAUI and MOVEit Configuration Data**.
5. Run **Backup Now** once and verify that a backup is created successfully.
6. Confirm the new backup appears in **Available Backups**. The inventory refreshes whenever the Backup panel is opened and can also be refreshed manually.

SQLite metadata is included using a live database backup. SQL Server metadata is **not** backed up by MAUI; configure normal SQL Server database backups separately. MOVEit configuration and `appsettings.json` are still included where applicable.

When restoring a backup, select the individual MOVEit environments to restore. Each environment starts unchecked; leave them all unchecked for a MAUI-only restore. Test restores on a non-production instance before relying on backups.

The visible **Backup Cleanup** System Job is enabled by default and runs daily at 1:00 AM using the MAUI application server's local time. Administrators can adjust its enabled state or daily schedule under **Configuration > Settings > Scheduler > System Jobs**. Cleanup removes recognized MAUI-created backup folders that meet the configured retention age and skips unrecognized folders conservatively.

### 9.3 Configure SMTP if outbound email is desired

Go to **Configuration > Settings > SMTP Configuration** when scheduled/outbound Reports or other email delivery will be used. Configure the SMTP host, port/security mode, optional authentication, From address/name, and then use **Send Test Email** to validate delivery.

SMTP is optional if outbound email is not required.

### 9.4 Enable the Host Health Check System Job

Go to **Configuration > Settings > Scheduler > System Jobs**. Enable **Host Health Check**, choose/confirm its schedule, and use **Run Now** for an initial validation.

Host Health Check requires working Data Access Credentials for each MOVEit environment being checked. It uses the same timeout/cleanup policy as Test Host and Preflight: at least five minutes for diagnostics, extended by native connection/data/retry settings up to 30 minutes. Checkout release has an independent deadline; cancelling the request does not cancel release. MAUI attempts supported Host connection tests and records unsupported patterns as **Unknown** rather than treating them as failures.

### 9.5 Validate Quick Transfer

Quick Transfer has no separate server configuration, but it exercises several MOVEit contracts at once. Validate it in a non-production environment before relying on it operationally:

1. Open **Transfers > Quick Transfer** and select known Source/Destination Hosts. Confirm the MOVEit **(Default) Local File System** Host is not offered. Use **Browse** for a verified Host/type combination; if the saved Host uses an authentication/proxy/blind-download pattern that the native browse service has not verified, expect MAUI to reject browsing rather than fabricate a request.
2. Run **Preflight** and confirm the generated Task passes the expected Task/Host/resource checks without starting a transfer.
3. Transfer a small test file and confirm status reaches the expected MOVEit Task Run result. Verify Source **Delete Source File After Successful Transfer** is off by default and Destination **Overwrite Existing File** is on by default before exercising those options deliberately.
4. While a non-production transfer is running, confirm **Transfer** changes to **Cancel** and cancellation reports **Cancelled** only after MOVEit confirms the stop.
5. With the default cleanup option enabled, confirm a successfully monitored temporary `ADHOC_*` Task is removed. Also test **Create Traditional Task from this request** if reusable Task creation will be used.
6. If PGP is licensed and required, test both Encrypt and Decrypt. For Encrypt, verify expired keys are not selectable and the selected PGP key is preserved in the generated Task. If PGP is not licensed, confirm Encrypt/Decrypt are disabled with the license tooltip.

Quick Transfer requires permission to use the selected MOVEit Hosts/resources and capacity for one Traditional Task. Manual folder/path entry does not make an otherwise unsupported Host type valid; for root-based profiles, an unselected root means the saved Host default root is used. Quick Transfer status tokens are process-local in RC3; finish active transfers before deployment. A submitted MOVEit run can continue independently, but its original MAUI monitor token does not survive restart. Background cleanup can recover eligible successful temporary Tasks using administrator Data Access credentials; failed, cancelled, ambiguous or unverifiable Tasks remain for review. See [Quick Transfer recovery](CHANGELOG.md#operational-safeguards-and-historical-fixes).

### Additional optional setup

| Settings | Operator action |
| --- | --- |
| Editors | Choose MAUI/native preferences, Save Configuration and reload. Tasks default on; the other five preferences default off. Existing installation choices are preserved. |
| General > Application Behavior | Configure naming defaults, optional edit-duration checkout and Task Failure Alerts. Close/reopen editors after changing checkout mode. |
| SSH/KEX Algorithms | Load supported choices, choose ordered defaults and Save Configuration. Existing Hosts retain their selections; new SFTP Hosts/Reset use these defaults. |
| Metadata, Tags, Grid Defaults/Definitions | Define organization fields, shared Tags and display defaults. Tags itself is protected from structural metadata edits. |
| General > Script Templates / Notification Templates | Manage environment-scoped templates as a MAUI Admin. Use Load Template in Script Templates to select .txt/.ps1 files (up to 2 MiB), review the prefilled name/source, then Add Template. For notifications, use Load Template to select .txt/.htm/.html (up to 2 MB), review the detected format, enter a Subject and click Add Template. Edit / Rename and Delete save immediately without Save Configuration. Saving shared templates from editable modals requires either administrator role. |

Editor preferences are under **Configuration > Settings > Editors**, immediately after General. MOVEit Settings requires both administrator roles; Groups & Permissions requires MOVEit administrator status. Validate permissions, save/readback, checkout recovery, retained credentials and read-only viewing before enabling editors for operators; use the [acceptance matrix](TESTING.md#rc3-editor-and-crud-acceptance).

**General > Folders**, immediately before System Message, creates root Task folders from Resource Group names as a MOVEit-administrator-only action. It runs independently of Save Configuration. **Task JSON Editor** and **Copy Between Environments** are under Experimental, as described below.

The edit-duration option defaults off and applies to editable Task/Host/Script/Date List editors. Host/Task Auto Save remains independent and per user. Preserve checkout journals and Data Protection keys across deployment. [Security](SECURITY.md#shared-host-and-task-mutation-safety) documents the lifecycle; [Testing](TESTING.md#object-permissions-and-read-only-acceptance) covers administrator/non-admin and read-only validation.

### Version-specific MOVEit features

Sign in again after upgrading MOVEit to refresh its server information. OneDrive Hosts and S3 AWS Config File / EC2 IMDS authentication require **Automation 2026.0+ / native engine 18.0+**. Older/unrecognized versions retain existing controls; the same rules protect REST writes and Copy To destinations. OneDrive Task-step/Quick Transfer composition is not enabled by the Host profile. SharePoint App/Certificate and Google HMAC/OAuth remain separate existing authentication modes. See [MOVEit 2026/cloud acceptance](TESTING.md#moveit-2026-host-acceptance).

### Host import templates

Use the packaged CSV/XLSX files under `Templates/HostImport`, outside `wwwroot`, through the authenticated Host import workflow. New UNC Share Hosts require a unique Resource UNC. Spreadsheet creation/updates and native cloning retain their existing COM paths; captured New Host REST creation is a separate operation.

### CSV / Excel Task import template

The packaged Task import workbook is `Templates\TaskImport\MAUI_ImportTasksTemplate.xlsx`. It is included in the build/publish output and is located outside `wwwroot` (it is not a public HTTP endpoint). Copy and populate this workbook when preparing Task imports through **Tasks > Actions > Import Tasks > CSV / Excel**; only the first worksheet is used. The template does not require write permissions for the MAUI IIS application pool.

### Experimental features

#### Experimental Task JSON Editor

Task JSON Editor defaults off under Configuration > Settings > Experimental. Enable it, Save Configuration and reload Tasks to expose Bulk Actions > JSON Editor. It uses the signed-in MOVEit account and existing Task Edit rights, not Data Access credentials. Keep the compatible COM API installed for revision checks, native Undo and overnight Schedule preservation. Save requires the explicit corruption warning and Proceed confirmation. Each affected Task is captured through the usual undo process before its update; a failed capture prevents that Task's write. Validate the [Task JSON Editor acceptance scenarios](TESTING.md#task-json-editor-acceptance) before enabling it for operators.

#### Experimental environment copy

Copy Between Environments defaults off and requires both MOVEit administrator status and MAUI Admin access. Configure administrator Data Access credentials for both environments before enabling it. Keep `Data/EnvironmentCopy` writable and restrict access to retained reports/protected recovery definitions; preserve Data Protection keys. Enable it through Configuration > Settings > Experimental and Save Configuration; reload MAUI to expose Copy To in supported row and bulk menus. Copy recovery retention is 1–90 days, default 30. See [Copy acceptance](TESTING.md#copy-between-environments-acceptance) before live use.

### Post-install validation

After completing the first steps, validate:

- Dashboard and inventory retrieval;
- Task/Host/Resource native editor launch;
- Task Run, File Activity, and Audit Records;
- Run Task Now and other permitted operations;
- a Quick Transfer Preflight and small non-production transfer using representative Hosts;
- a successful manual backup;
- a scheduled System Job that runs without browser traffic, using the [IIS scheduler configuration](#keep-maui-running-for-scheduled-jobs);
- Report delivery if SMTP is configured;
- Host Health testing for supported Host types; and
- access/roles for additional MAUI users.

## 10. Protect installation-owned files

The following must survive application upgrades:

```text
appsettings.json
Data\
```

`Data` includes the default SQLite database and runtime backups. A deployment that wipes the destination directory before copying new files can destroy production configuration/data even though those files are excluded from normal publish output.

Recommended operational practice:

1. Keep MAUI application files and persistent data on protected storage.
2. Restrict filesystem ACLs to the IIS identity and administrators who require access.
3. Back up `appsettings.json` and the MAUI metadata database before an upgrade.
4. Never use **Delete existing files**, **Remove additional files at destination**, or equivalent destructive deployment options against an existing installation unless persistent files have been explicitly relocated and protected.

`appsettings.json` can contain sensitive configuration such as database connection strings. Treat it as a secret-bearing server configuration file.

### ASP.NET Core Data Protection keys

On Windows IIS, MAUI stores encrypted ASP.NET Core Data Protection keys at `<installation>/Data/DataProtection-Keys`, using machine-level DPAPI. The application pool needs **Modify** permission on the existing `Data` folder so MAUI can create and write this directory; no IIS app-pool profile setting changes are needed. MAUI intentionally fails startup instead of silently falling back to transient or plaintext keys when this location is inaccessible.

Preserve the `Data/DataProtection-Keys` directory along with the installed `appsettings.json` and other installation-owned `Data` contents when deploying an upgrade. Do **not** publish developer-machine keys or overwrite the deployed key ring. Local-machine DPAPI keys are not portable to another Windows server; design a shared key repository and cross-machine encryption strategy before deploying MAUI on multiple nodes. If changing from a previous default key store, open a fresh sign-in form after deployment; existing protected cookies may require reauthentication.

### Recovery files and continuous retention

Preserve `Data/ObjectEditLeases` (including `ShortLived`), `Data/RunAsOfRecovery` and `Data/DataProtection-Keys` during deployment. Failed checkout requests retain internal cleanup details in Configuration > Logs. Retry opening the object to obtain the current MOVEit checkout response; only a MOVEit administrator can confirm Override Checkout when needed. MAUI does not force checkout or release an unverified acquisition in the background. Run As Of recovery never starts a Task again and needs the original account's still-valid cleanup tokens; otherwise inspect the Task and its original Undo snapshot. Copy To recovery/report retention is enforced periodically while MAUI runs. Release 0.18.511 RC3 does not force an additional post-upgrade restart. MAUI still requests a restart when startup-sensitive configuration changes require one.

## 11. Upgrading MAUI

See [`UPGRADE.md`](UPGRADE.md) for the full upgrade framework. The normal operator procedure is:

1. Read the new release notes and compatibility requirements.
2. Back up `appsettings.json` and the MAUI metadata database.
3. Stop the MAUI application pool, or let Web Deploy take the application offline.
4. Copy one complete matching publish **without deleting** installation-owned files. Preserve exactly live appsettings.json and Data; replace release JSON with the binaries, then run the [destination package verifier](UPGRADE.md#release-package-contract). Do not use a broad appsettings*.json exclusion.
5. Start the application pool, or finish Web Deploy's offline cycle.
6. If MAUI reports an upgrade is pending, authenticate as a MAUI Admin and complete `/Upgrade`.
7. MAUI completes version-only, schema-only, and reload-safe configuration upgrades without restarting. If startup-sensitive configuration changed or the release manifest requires a restart, select **Restart MAUI** after completion; MAUI waits for the replacement process and returns to Login; no IIS application-pool access is required for the MAUI administrator.
8. Sign in again and validate normal login and core workflows.
9. Compare the deployed MOVEit Web Admin integration assets with the current package. If `maui-integration.js` must be replaced, configure `LAUNCHER_ORIGINS` with every trusted MAUI browser origin **before** deploying it to each Web Admin/Tomcat node, then re-verify the `index.html` script reference.

Application releases may change while database schema/configuration versions remain unchanged. RC3 remains at database schema **1** and configuration version **1**.

## 12. Troubleshooting checklist

### Release package mismatch (IIS 500.30)

A log such as `appsettings.template.json Ui.Version is '0.18.484-RC3', but upgrade.json TargetVersion is '0.18.486-RC3'` proves that the deployed release files do not match. It does not identify whether the stale file came from the publish output, a deployment exclusion, an interrupted copy or another deployment step. This specific exception occurs in release-contract validation before normal startup; it is not a Keep Alive worker failure.

1. Stop the MAUI application pool, or let Web Deploy take the application offline. Use one method; do not copy release files into a running process.
2. Publish the intended release to a staging folder. The Windows build/publish verifier must pass.
3. Copy the complete publish output, always replacing `MoveItAutomationUI.dll`, `appsettings.template.json` and `upgrade.json` together. Preserve the existing live `appsettings.json` and the complete Data directory. Do not enable delete-all/clean-destination deployment.
4. Run the shipped `Build\Verify-ReleasePackage.ps1` against the IIS physical directory as shown in the [release package contract](UPGRADE.md#release-package-contract). Correct any mismatch by copying matching release files, not by changing version strings.
5. Start the application pool (or finish Web Deploy's offline cycle), then complete MAUI's authenticated upgrade if requested.

### General startup checks

Validate layers independently:

1. IIS site/app-pool health and filesystem permissions.
2. .NET 8 Hosting Bundle installation.
3. MOVEit Web Admin HTTPS connectivity from the MAUI server.
4. MOVEit API license/capability and user authentication.
5. MAUI metadata database connectivity and write/DDL permissions.
6. MOVEit Windows COM API registration/access on the MAUI IIS server.
7. `LAUNCHER_ORIGINS`, integration assets, and Tomcat `index.html` change.
8. Browser cache/hard refresh after integration-asset changes.
9. Confirm the application pool still has **Maximum Worker Processes = 1**.
10. If authentication appears stale after an application recycle, sign in again; normal protected requests require a live server-side MOVEit session.
11. MAUI application logs under **Configuration > Logs** after login.
12. For a **MOVEit Unreachable** grid banner, check DNS and Web Admin HTTPS connectivity from the MAUI host. After restoring access, use Refresh Now to confirm a live read; cached rows alone cannot clear it. Two consecutive grid timeouts trigger the banner, while HTTP/authentication/certificate errors remain separate. See [connection feedback](README.md#moveit-connection-feedback).

### Factory Reset

Administrators can open **Configuration → Settings → Advanced → Factory Reset** and type `RESET` to confirm permanent removal of locally stored MAUI data. The host schedules shutdown and purges `Data` contents on the subsequent startup (the parent directory is preserved). `appsettings.json` is cleared to `{}`. An external SQL Server metadata database is not dropped or modified; SQLite files outside `Data` are not removed. Keep the reset-pending marker and the Data Protection keys installation-owned; do not ship them with deployments. IIS `StopApplication()` shuts down the ASP.NET Core application, not necessarily its application pool. Test only on a disposable installation.

### Verify modal feedback and notification logs

After deploying RC3, check Import Tasks and Import Hosts: their spreadsheet preview should show a header and five rows, with remaining rows available by scrolling. Configured import batch/page limits are unchanged. Test a slow/unreachable Host in Test Host and Task Preflight; errors should appear in the modal footer and remain in the diagnostic result rows. Trigger a harmless page notice and confirm its entry in Configuration > Logs with the expected severity, user, environment and page. Page-origin notices during an open modal are labelled Page update. Logging failures must not cause another notice or block the original action.
