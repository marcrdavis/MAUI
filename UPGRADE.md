# MAUI Upgrade Guide

- **Target release:** `0.18.511-RC3`
- **Release:** Release Candidate 3 (pre-release)
- **Build:** `20261029`
- **Database schema:** `1`
- **Configuration version:** `1`

> **Release Candidate 3 — not the final release.** This candidate may contain bugs or regressions. Test the upgrade and rollback procedure on a non-production instance, preserve installation-owned configuration/data, and complete live acceptance before relying on it in production.

MAUI upgrades are driven by the cumulative `upgrade.json` manifest included with each release. The live `appsettings.json` and MAUI metadata database are installation-owned and must be preserved when application files are replaced. Source-built RC3 packages should satisfy the validation gate in [`TESTING.md`](TESTING.md) before deployment.

**In this guide:** [RC3 upgrade state](#rc3-upgrade-state) · [Before upgrading](#before-upgrading) · [Recommended deployment procedure](#recommended-deployment-procedure) · [After-upgrade checks](#after-upgrade-checks) · [Upgrade implementation reference](#upgrade-implementation-reference)

Audit View Changes becomes available automatically for native rows with comparison data on MOVEit Automation 2026 / engine 18 or later. Sign in again after a MOVEit server upgrade to refresh version capabilities. Existing Audit behavior is unchanged on older or unknown versions; no MAUI schema/configuration migration is required.

## RC3 upgrade state

| Item | Current package |
| --- | --- |
| Application / build | `0.18.511-RC3` / `20261029` |
| Minimum directly supported application version | `0.16.77-integration` |
| Database schema / configuration version | `1` / `1` |
| Manifest RestartRequired | `false`; startup-sensitive changes can still require restart. |
| Editor defaults | Tasks on; Hosts, Scripts, Date Lists, Groups & Permissions and MOVEit Settings off. Existing choices are preserved. |

Live appsettings.json and Data remain installation-owned. Release templates/manifest/binaries must match. Application releases can advance without schema/configuration migrations; missing editable Settings definitions are added from the template when saved, preserving existing values.

## Before upgrading

RC3 includes optional MAUI editors for Traditional, Advanced and Synchronization Tasks, mapped Hosts, Scripts, Date Lists and Groups & Permissions. Review existing editor preferences under Configuration > Settings > Editors after upgrade; retain the native Web Admin integration and compatible COM API. New Host and empty Advanced/Sync creation use captured REST v0, but Host spreadsheet creation/updates, native clone/restore and other native configuration operations still require COM. Validate these paths, both checkout modes and conflict/partial-save recovery using [TESTING.md](TESTING.md#rc3-editor-and-crud-acceptance).

Close Task/Host/Script/Date List editors before deployment so their checkouts can release normally. Preserve `Data/ObjectEditLeases` and `Data/DataProtection-Keys`; the cleanup worker can recover abandoned records after restart, but unavailable MOVEit authentication may require a fresh owner login or administrator inspection.

Before stopping/replacing the MAUI application, confirm no operator is actively relying on a Quick Transfer status token. MAUI does not persist that transient monitor state across process restarts. Finish or stop active Copy To work before deployment; a restart marks unfinished copies Interrupted rather than resuming their writes.

1. Review `CHANGELOG.md` for compatibility and release-specific requirements.
2. Confirm supported MOVEit versions and COM compatibility. Mixed 2025.x/2026.x environments require COM API 18.x or a later compatible client on the MAUI host; validate retained COM operations against each environment after updating. See the [important compatibility notice](INSTALLATION.md#1-supported-deployment).
3. Back up `appsettings.json`.
4. Back up the MAUI metadata database:
   - SQLite: back up the database using a consistent snapshot/MAUI Backup where appropriate.
   - SQL Server: use normal SQL Server backup procedures.
5. Back up the current application directory or retain a known-good deployment package for rollback.
6. Confirm the IIS application pool uses **Maximum Worker Processes = 1**; RC3 supports only one active MAUI application instance/worker.
7. If MAUI Integration assets will change, back up MOVEit Web Admin Tomcat ROOT `index.html` and existing MAUI integration assets.

## Protect installation-owned content

The following must survive every MAUI deployment:

```text
appsettings.json
Data\
```

`Data` normally contains the SQLite database and runtime backups. If an administrator has configured an absolute SQLite path, protect that location as well.

> **Do not use a deployment option that deletes all existing destination files before copying the release.** Options such as **Delete existing files**, **Remove additional files at destination**, clean-directory deployment, or equivalent tooling can remove production configuration, databases, or backups even though MAUI excludes those files from normal publish output.

## Recommended deployment procedure

1. Stop the MAUI IIS application pool, or let Web Deploy take the application offline.
2. While the application pool is stopped (or Web Deploy has taken the application offline), copy the complete output of one successful publish over the existing application directory without deleting installation-owned content. Always replace release JSON files; any skip/preserve rule must distinguish them from live `appsettings.json` and Data.
3. Verify `appsettings.json` and `Data` are still present. Run the shipped [release package verifier](#release-package-contract) against the destination before starting the application. Preserve rules must name the exact live `appsettings.json`; a broad `appsettings*.json` exclusion would also skip the release template.
4. Start the MAUI IIS application pool, or finish Web Deploy's offline cycle.
5. Browse to MAUI.
6. If no upgrade is pending, sign in and validate the release.
7. If an upgrade is pending, authenticate as a MAUI Admin and complete `/Upgrade`.
8. MAUI completes version-only, schema-only, and reload-safe configuration upgrades without a process restart. If startup-sensitive configuration changed or the manifest requires a restart, select **Restart MAUI** after completion; IIS application-pool access is not required for the MAUI administrator.
9. Sign in again and validate Dashboard, object inventories, Records, native editor launch, System Jobs, Quick Transfer (in non-production), and any environment-specific workflows.
10. Ensure every applicable MOVEit Web Admin/Tomcat node has the current MAUI Integration assets and verify `index.html` still loads `maui-integration.js`.
    - **Before deploying or replacing `maui-integration.js`, configure its `LAUNCHER_ORIGINS` allow-list with every trusted browser-visible MAUI origin that uses this MOVEit Web Admin instance.** Use exact origins only; wildcards are not supported.
    - The current package's `maui-integration.js` advertises `MAUI_INTEGRATION_ASSET_REVISION = 0.16.206-RC1` (the bundled integration asset revision, independent of the RC3 application version). Deploy this script to every applicable Web Admin/Tomcat node, keep the trusted-origin allow-list consistent across nodes, and retain the package CSS/logo alongside it.

MOVEit Automation upgrades may replace Tomcat `index.html`. Recheck the MAUI Integration after a MOVEit upgrade even when the MAUI release itself did not change the integration assets.

## After-upgrade checks

### Upgrade validation priorities

After upgrading, validate the editors and Browse combinations against your MOVEit build, including credentials and supported proxy modes. Confirm non-admin actions match their existing Resource Group grants, read-only object views remain read-only, Task folders remain administrator-managed, and template-saving access follows the configured roles. Loading existing templates is available under normal access rules.

Editor Undo reverses one local change in the current opening; grid saved-history restore is separate. No database-schema or configuration-version migration is required for this release. Release-by-release changes and earlier validation records are in [CHANGELOG.md](CHANGELOG.md).

### MOVEit version-specific Host features

No MAUI schema/configuration migration is needed for the 2026 Host additions. Reauthenticate after a MOVEit server upgrade to refresh the reported engine version. OneDrive Host and S3 IAM choices require Automation 2026.0 / native engine 18.0 or later and remain hidden when the server version is unknown. Existing access-key Hosts keep their older payload defaults; IAM mode changes use native AuthMethod values and omit inactive credentials. OneDrive Task-step/Quick Transfer composition remains outside the implemented Host profile. SharePoint App Access and Google OAuth use the supplied Test Host/Browse requests; response parsing follows the accepted certificate/HMAC equivalents, with no live S3/Google test claim. Validate against the [MOVEit 2026 matrix](TESTING.md#moveit-2026-host-acceptance) before enabling operational use.

### Editable settings in existing installations

The current release uses configuration version **1**, so an older installation-owned `appsettings.json` may not contain every editable setting that exists in the current release template. **Configuration > Settings** treats the release template as the authoritative definition for missing editable settings: when an administrator saves a setting whose section/property is absent, MAUI safely creates the missing definition and applies the selected value.

Existing values remain installation-owned and are preserved. This behavior covers newer settings such as **Backup Retention**, **Auto Format New Task/Host Names**, **Task Failure Alerts**, **Checkout Objects for Duration of Edit** and **SSH/KEX Algorithms** when they are absent from an older live file. If an existing JSON node has a conflicting or malformed type, MAUI fails the save rather than silently replacing that structure. Do not replace the live `appsettings.json` with the template as an upgrade shortcut.

### Settings locations and preserved preferences

The six editor preference switches are under **Configuration > Settings > Editors**. **General > Folders / Create Folders** is immediately before System Message. Copy Between Environments is under Experimental.

There is no configuration-key rename or schema migration. Keep the installation-owned `appsettings.json`; existing preferences and defaults are preserved. An older saved Settings navigation order automatically gains Editors near General while retaining its configured categories. Factory Reset and replacing the live configuration file are not needed for this reorganization.

### Copy Between Environments

Copy Between Environments is an opt-in experimental feature. It does not require a database-schema or configuration-version migration. Existing installations can save its settings through Configuration; missing setting definitions are added from the release template without replacing the live appsettings.json. Retain `Data/EnvironmentCopy` and Data Protection keys across deployment. An application restart marks unfinished copies Interrupted; it does not resume or replay writes. Inspect known destination IDs and checkout outcomes before retrying. The experimental toggle remains off by default, and both Data Access accounts must be configured as MOVEit administrators. Progress/status refresh recovery does not restart a copy. Follow [Copy acceptance](TESTING.md#copy-between-environments-acceptance).

### MOVEit connection feedback

The shared grid banner needs no new configuration setting, database-schema change or configuration-version migration. Deploy the normal application/static assets and validate [connection feedback](TESTING.md#moveit-connection-banner-acceptance), including cached navigation, a successful live read after recovery and both site-root/virtual-application routes. Grid failure handling does not replay editor writes or checkout operations.

## Upgrade implementation reference

The sections below describe the existing upgrade engine and future migration authoring. They do not replace the operator deployment procedure.

### Version ownership

- `appsettings.json` `Ui:Version` records the application version successfully applied to the installation.
- `appsettings.json` `UpgradeState:ConfigurationVersion` records the configuration-schema version successfully applied.
- `MauiSchemaVersion` in the MAUI metadata database records the database-schema version successfully applied.
- `upgrade.json` declares the target application version, compatibility floor, required configuration/schema versions, and cumulative migration operations supported by the release.
- The application build string is release/code metadata and is not persisted as an administrator setting in `appsettings.json`. Deploy the rebuilt/published application binaries as well as the manifest/templates: changing a version setting alone cannot update build metadata embedded in an older binary.

A configured installation is upgrade-pending when its installed application/configuration/database state does not meet the manifest target. `MinimumSupportedVersion` is a compatibility floor, not necessarily the immediately previous application build. Application builds may be skipped when the installed version remains at or above that floor; schema and configuration migrations remain cumulative and sequential.

### Release package contract

At startup, MAUI validates release-controlled files before upgrade detection continues:

- `appsettings.template.json` `Ui:Version` must exactly match `upgrade.json` `TargetVersion`.
- `appsettings.template.json` `UpgradeState:ConfigurationVersion` must match `upgrade.json` `RequiredConfigurationVersion`.
- The manifest `Description` is required and is displayed on the Upgrade screen.

Windows builds and publishing run `Build\Verify-ReleasePackage.ps1` automatically. It reads the DLL's product version, `appsettings.template.json` and `upgrade.json`; their release versions must match, and the template/manifest configuration versions must agree. Release metadata is copied unconditionally rather than relying on destination timestamps. Validation does not start the application or read/write the installed settings, database or Data directory. The startup contract remains enabled.

For manual copying or a remote deployment, validate the **destination** before restarting IIS (an existing deployed version does not need to start):

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File 'D:\MDEV\OPSSC\Maui\Build\Verify-ReleasePackage.ps1' -PackageDirectory 'D:\MDEV\OPSSC\Maui'
```

The script is shipped from 0.18.494 RC3 onward. Adjust the path for the installation. If verification fails, deploy the complete output of one successful publish, including its DLL and release JSON files; do not edit version numbers to conceal a mismatch. A successful local publish cannot guarantee that a later manual copy transferred every file.

The live `appsettings.json` is not a release file and must not be replaced during normal deployment. Both build and publish exclude appsettings.json. For a NEW deployment only, create it manually containing `{}` before startup. For upgrades, preserve the existing destination appsettings.json and Data. Publish/package from a clean staging directory so an old settings file from a previous output cannot be included by external deployment tooling. SQLite database files are excluded.

### Upgrade access and authorization

When an upgrade is pending, normal MAUI application access is locked.

- Credentials are freshly authenticated against MOVEit Automation.
- MAUI verifies upgrade eligibility from the database-backed `SecurityUsers` table.
- Upgrade login does not change `LastLoginUtc`, MAUI roles, or `appsettings.json`.
- A successful MAUI Admin login receives an upgrade-only authorization claim bound to the exact target package version.
- `/Upgrade` uses that claim rather than the normal server-side MOVEit token cache.
- The upgrade-only cookie is session-scoped and is cleared when MAUI transitions into a non-pending state and the administrator chooses **Return to MAUI**, or after a required controlled restart returns the browser to Login.

When no upgrade is pending, `/Upgrade` returns HTTP 404.

### Runtime upgrade flow

1. Startup reads the installed application/configuration/database versions and validates the release package contract.
2. If the installed state matches the manifest target, MAUI starts normally.
3. If an upgrade is pending, normal application routes are locked and users are directed to the upgrade login path.
4. A freshly authenticated MAUI Admin is routed to `/Upgrade`.
5. **Begin Upgrade** validates/stages configuration changes and backs up `appsettings.json` under `Data\ConfigurationRecovery`.
6. If a SQLite schema change is pending, MAUI creates a live pre-upgrade SQLite snapshot under `Data\Backups\PreUpgrade` before the first schema operation. SQL Server database backup remains administrator-managed.
7. Required database migrations run sequentially. Each schema-version step runs in its own transaction and advances the recorded schema version only after commit.
8. Configuration migrations are applied and the upgraded `appsettings.json` is written in-place under an exclusive file handle with an earlier recovery snapshot saved to `Data\ConfigurationRecovery`. This write is **not filesystem-atomic**. `Ui:Version` is updated only after all required work succeeds.
9. MAUI compares the staged configuration with the startup configuration using the same restart policy as Configuration > Settings. Version-only upgrades, schema-only upgrades, and configuration changes that are safe to reload complete without restarting the process.
10. If startup-sensitive configuration changed or the manifest requests a restart, MAUI remains upgrade-gated and presents **Restart MAUI**. Select it to request a controlled application restart. Under IIS, the ASP.NET Core Module/WAS process manager starts the replacement application process; the browser waits for the new process and then returns to Login.
11. If no restart is required, MAUI clears the in-memory upgrade gate immediately, reloads `IConfiguration`, leaves the completed upgrade results visible, and presents **Return to MAUI**. Selecting it returns the administrator through the normal Login path.

If an upgrade fails, processing stops at the failing step. Already committed schema-version transactions remain recorded so a retry resumes from the database's actual version.

#### Restart policy

An application restart is required when the upgraded `appsettings.json` changes settings that are bound at process startup, or when the release manifest sets `RestartRequired` to `true`. The flag defaults to `false` when omitted; the current manifest sets it to `false` so routine upgrades do not force an additional restart. The current restart-sensitive policy includes:

- MAUI metadata database provider or connection string;
- MOVEit REST/authentication runtime settings captured at startup;
- invalid-server-certificate handling;
- Task Run history retrieval limit.

MOVEit server definitions use the live catalog and do not require restart. Changes apply to new logins/background connections; existing sessions remain bound to their original server. Changing an endpoint URL, COM host or Environment Key clears unattended credentials, which must be re-entered.

The policy is implemented by the same `ConfigurationRestartDetector` used by Configuration > Settings so normal configuration saves and upgrade migrations do not diverge. When a restart is required, the completed `/Upgrade` page replaces **Return to MAUI** with **Restart MAUI** and uses the existing controlled restart flow; the MAUI administrator does not need IIS Manager or application-pool recycle permission.

Checkout changes do not require a database-schema or configuration-schema migration; both remain at version 1. Preserve `Data/ObjectEditLeases` and Data Protection keys. Existing cleanup records remain readable and do not supply the displayed lock owner/time. After upgrade, reopening an object uses normal native checkout; a MOVEit administrator can explicitly override a reported checkout in MAUI. In edit-duration editors, cancelled or superseded confirmations must not acquire checkout. See [checkout acceptance](TESTING.md#native-checkout-response-and-retry-consistency).

### Initial Setup is a separate in-process transition

For a fresh installation, `/Setup` activates the saved configuration and database without a restart, and routes to Login. This does **not** remove restart requirements from subsequent Settings changes to the database provider/connection, global MOVEit REST/session runtime policy or TLS certificate validation. The authenticated `/Upgrade` restart policy remains separate.

### Initial database provisioning

Schema version `0` means no MAUI schema exists. Initial Setup provisions a new database through the same migration engine used for upgrades. The `0 -> 1` migration defines the complete initial MAUI schema and creates `MauiSchemaVersion`.

A non-empty database without a valid `MauiSchemaVersion` table/record is treated as unversioned/invalid rather than inferred as a MAUI database. This prevents unrelated databases from being adopted silently.

### Declarative database operations

Manifest version 1 supports:

- `CreateTable`
- `AddColumn`
- `RenameColumn`
- `DropColumn`
- `CreateIndex`
- `DropIndex`
- `ExecuteSql`

Supported portable column types are `String`, `Json`, `Integer`, `Long`, `Boolean`, `DateTime`, `UtcDateTime`, `Decimal`, `Double`, `Guid`, and `Binary`.

MAUI translates these logical types to SQLite or SQL Server types. `CreateTable` supports table-level primary keys and declarative foreign keys, including provider-aware delete behavior.

`ExecuteSql` is reserved for transformations or provider-specific DDL that cannot be expressed cleanly by portable operations. Both `SqliteSql` and `SqlServerSql` are required so the migration remains valid for both supported providers. `ExecuteSql` runs inside the migration transaction and must not create or commit its own transaction.

### Declarative appsettings operations

Manifest version 1 supports:

- `AddIfMissing`
- `AddArrayItemIfMissing`
- `RemoveArrayItemIfPresent`
- `ReplaceArrayItemIfPresent`
- `SetValue`
- `Remove`
- `Rename`
- `Move`

Paths use dot notation, for example `Grid.FitColumnsToGrid.Value`.

Prefer additive/preserving operations for administrator-customizable settings. Array operations require the target path to exist and be an array; an incompatible target fails the upgrade rather than silently changing configuration shape.

### Authoring future migrations

For a release that changes database or configuration shape:

1. Keep `upgrade.json` cumulative from the supported baseline.
2. Add exactly sequential schema/configuration version steps; do not skip intermediate versions.
3. Prefer portable declarative operations over provider-specific SQL.
4. Preserve administrator-customized values wherever practical.
5. Update `appsettings.template.json` to represent the final configuration shape for a new installation.
6. Keep `Ui:Version`, `UpgradeState:ConfigurationVersion`, `TargetVersion`, and required manifest versions synchronized.
7. Move `MinimumSupportedVersion` only when a release intentionally drops compatibility with an older installed baseline.
8. Validate the resulting database against the EF Core runtime model after provisioning/migration.
9. If a new configuration setting is consumed only at application startup, add it to `ConfigurationRestartDetector` so both Configuration > Settings and `/Upgrade` classify its changes as restart-required.

During prerelease RC3 development, keep database schema and configuration versions at 1 unless an explicitly approved persistent migration requires otherwise; do not add sequential migrations for individual interim builds. The upgrade framework intentionally allows application releases to advance independently from schema/configuration versions when persistent configuration/database shape does not change.

RC3 feedback changes require no database/configuration migration. Toasts now stay in an open modal, including labelled page-origin notices, and signed-in messages feed the existing application log. Verify the normal log-retention policy and the five-row import preview after deployment; import batch/page limits are unchanged.

## Task JSON Editor opt-in

The Task JSON Editor is off by default under Configuration > Settings > Experimental. Save Configuration and reload Tasks after enabling it; the new JSON Editor bulk action does not change the normal MAUI/native editor preference. Existing installations acquire the missing setting definition from the release template when Settings are saved. There is no schema migration or required extra restart. Keep COM support for native revision/Undo/Schedule operations and complete the [acceptance checks](TESTING.md#task-json-editor-acceptance) before use.
