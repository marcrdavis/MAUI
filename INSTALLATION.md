# MAUI Installation Guide

**0.18.583-RC3** · Build **20261124** · .NET 8 · Database schema **2** / configuration **1**

Test this release candidate against a non-production MOVEit environment before deploying it for operators. For the shortest local setup, start with the [Visual Studio Quick Start](README.md#quick-start-with-visual-studio).

[Requirements](#basic-requirements) · [Integration assets](#install-the-moveit-integration-assets) · [Build](#build-with-visual-studio) · [Local testing](#test-within-visual-studio) · [IIS](#deploy-to-iis) · [Initial Setup](#run-initial-setup) · [First sign-in](#sign-in-for-the-first-time) · [Smoke test](#check-basic-functionality) · [Post-setup checklist](#post-setup-checklist) · [Experimental features](#experimental-features) · [Troubleshooting](#troubleshooting)

<a id="1-supported-deployment"></a>
<a id="2-prerequisite-checklist"></a>
<a id="3-choose-the-maui-metadata-database"></a>

## Basic requirements

| You need | What to prepare |
| --- | --- |
| MOVEit Automation | A supported 2025.x or 2026.x instance with the API licensed and enabled. Have its Web Admin HTTPS URL and a MOVEit account for the first sign-in. |
| Windows host for MAUI | Run MAUI on a separate Windows computer from Automation. The MAUI host must reach MOVEit Web Admin over HTTPS. |
| MOVEit Automation Windows COM API | Install and register the compatible COM API on **each computer running MAUI**, including the Visual Studio workstation and IIS host. Use **18.x or later compatible** for Automation 2026.x, including mixed 2025.x/2026.x environments. |
| MAUI metadata database | **SQLite** is the simplest setup. It uses `Data\maui.db` by default. If you use **SQL Server**, have the hostname, database and authentication details ready. See [SQL Server requirements](#use-sql-server). |
| Build or publish package | To build from source, use Visual Studio 2022 or later with the **ASP.NET and web development** workload and **.NET 8 SDK**. If you already have a complete publish package, go to [Deploy to IIS](#deploy-to-iis) after any optional integration setup. |

<a id="7-configure-maui-integration-on-moveit-web-admin"></a>

## Install the MOVEit Integration Assets

**The MAUI Integration components for MOVEit Web Admin are not required when using the MAUI Editors exclusively.** Skip to [Build with Visual Studio](#build-with-visual-studio) if you will only use MAUI Editors, and enable the applicable preferences under **Configuration > Settings > Editors** after setup. The local COM API is still required; it is separate from these browser integration assets.

Install these assets on **MOVEit Web Admin** when MAUI will launch native MOVEit editors.

1. Back up the Web Admin Tomcat ROOT `index.html` and any MAUI integration files already there.
2. In `IntegrationAssets\MOVEitWebAdmin\maui-integration.js`, set `LAUNCHER_ORIGINS` to the exact MAUI origins allowed to launch this Web Admin. Include a non-default port when used. Omit application paths such as `/MAUI` and use explicit hostnames without wildcards.

   ```javascript
   const LAUNCHER_ORIGINS = [
       "https://maui.example.com",
       "https://maui-qa.example.com:8443"
   ];
   ```

3. Copy `maui-integration.js`, `maui-native-editor.css` and `automation-mark.svg` from `IntegrationAssets\MOVEitWebAdmin` into the Web Admin `Tomcat\webapps\ROOT` directory. Use the actual Tomcat ROOT for your installation.
4. Add this entry immediately before `</body>` in the Tomcat ROOT `index.html`. If the entry is already present, keep a single copy.

   ```html
   <script src="maui-integration.js" type="text/javascript"></script>
   ```

5. For HA or failover, repeat on each Web Admin node users can reach. Keep the origin list consistent across nodes, then verify a native editor launches from MAUI and returns correctly.

### Reinstall after a MOVEit Automation upgrade

**When native-editor integration is used, reinstall the MAUI Integration components after every MOVEit Automation upgrade.** The Automation upgrade overwrites the Web Admin Tomcat folder, removing the deployed integration files and the `index.html` script reference. This applies even when the MAUI application version has not changed.

Retain the integration files and your `LAUNCHER_ORIGINS` settings outside Tomcat before the upgrade. After the upgrade, repeat steps 2–5 above on **every applicable Web Admin node**: copy `maui-integration.js`, `maui-native-editor.css` and `automation-mark.svg` into the new Tomcat ROOT, restore the exact trusted MAUI origins, and add the script entry once to the **upgraded** `index.html`. Do not restore the old `index.html` over MOVEit's upgraded file. Refresh the browser cache and verify native editor launch and return. After a MOVEit repair, check the assets and reinstall them if they were replaced.

Installations using the MAUI Editors exclusively can skip this browser integration and its reinstallation steps. The compatible COM API remains a separate requirement.

<a id="4-build-maui"></a>

## Build with Visual Studio

1. Open `MoveItAutomationUI.sln` and set `MoveItAutomationUI` as the startup project.
2. Restore NuGet packages. Select **Release**, then choose **Build > Build Solution**.
3. For IIS deployment, right-click the `MoveItAutomationUI` project, choose **Publish**, and publish to a **Folder** using **Release**. Use a clean folder for the publish output.

For command-line builds and the full regression suite, see [Testing](TESTING.md#running-validation).

## Test within Visual Studio

1. Use a writable project folder. For a fresh checkout, create `appsettings.json` containing `{}` beside `MoveItAutomationUI.csproj` **only if that file is absent**. Preserve any existing configuration and `Data` directory.
2. Select the **https** project launch profile and press **F5** or **Ctrl+F5**. Open `https://localhost:7184` and trust the local ASP.NET Core development certificate if prompted. IIS is not required for this project profile.
3. Complete [Initial Setup](#run-initial-setup) against a development or QA MOVEit server. Choose SQLite and keep `Data\maui.db` for a quick local setup. Initial Setup saves the source-folder `appsettings.json`; keep it with the local test instance.
4. Follow [First sign-in](#sign-in-for-the-first-time) and [Check basic functionality](#check-basic-functionality). For MAUI-only editing, enable the applicable MAUI editor preferences; the Web Admin Integration components are not required. If testing a native editor, install the [optional integration](#install-the-moveit-integration-assets), include `https://localhost:7184` in the target Web Admin's `LAUNCHER_ORIGINS`, and verify launch and return. Reinstall that integration after every MOVEit Automation upgrade as described above.

<a id="5-prepare-the-iis-application"></a>
<a id="6-install-the-moveit-automation-com-api-on-the-maui-server"></a>

## Deploy to IIS

Install IIS, then the **.NET 8 ASP.NET Core Hosting Bundle** on the MAUI host. Install and register the MOVEit COM API there too. Initial Setup checks that the running MAUI process can activate the component. [Microsoft IIS deployment instructions](https://learn.microsoft.com/en-us/aspnet/core/tutorials/publish-to-iis)

1. Copy a complete matching publish to a dedicated application directory, such as `D:\Apps\MAUI`. For a **new installation**, create `appsettings.json` containing `{}` if it is absent. Preserve a configured `appsettings.json` and the entire `Data` directory on redeployments.
2. Create a dedicated IIS application pool and set **Maximum Worker Processes** to **1**. Point a website or **IIS application** at the published directory and assign that pool. On a shared site such as `https://iis.example.com/MAUI/`, add MAUI as an application or use **Convert to Application** on the folder.
3. Bind the site to HTTPS using a certificate trusted by users and valid for the MAUI hostname. Keep the published `web.config` with the ASP.NET Core handler and `hostingModel="inprocess"`.
4. Grant the application-pool identity **Read** on the application files and **Modify** on the existing `appsettings.json` and `Data`. If SQLite lives elsewhere, grant **Modify** on its database directory too. Keep write access off the application root. [IIS file permissions](SECURITY.md#restricted-iis-application-pool-permissions)
5. Start the application pool and open the MAUI URL. A new installation opens **Initial Setup**. If an existing instance opens **Upgrade**, follow the [Upgrade guide](UPGRADE.md).

<a id="8-run-initial-setup"></a>

## Run Initial Setup

1. Enter the initial **MOVEit connection**. For example, use Display Name **Automation QA**, Environment Key **QA**, and Web Admin URL `https://automation-qa.example.com`. Use the same Environment Key for HA nodes that share one Automation configuration and different keys for separate environments.
2. Click **Test Connection**. Setup tests MOVEit reachability and local COM activation without a MOVEit username or password. Leave certificate validation enabled for normal use.
3. Select **SQLite** and keep `Data\maui.db` for the simplest setup. If you have a prepared SQL Server database, select **SQL Server** and supply its connection details.
4. Click **Test Database**, then **Complete Setup** after both tests pass. MAUI initializes its database, saves the settings and opens the sign-in page in the same process.

### Use SQL Server

Use an empty database for a new installation or an existing compatible MAUI database. The MAUI database principal needs to connect, read and write MAUI tables, and apply the schema changes required by Setup and upgrades. With **Windows Integrated Authentication** on IIS, grant access to the effective app-pool identity. **SQL Server Authentication** uses the supplied database account. Keep the SQL certificate verifiable and arrange SQL Server backups separately.

## Sign in for the first time

Select the **Automation server**, enter your MOVEit Automation username and password, and click **Sign In**. The first successful sign-in against a new MAUI database becomes **MAUI Admin**. To complete tasks that also require MOVEit administrator rights, use an account that already has those rights in Automation. Later users' MAUI roles are managed under **Configuration > Settings > Security > MAUI Users**.

## Check basic functionality

| Open | Confirm |
| --- | --- |
| **Dashboard** | The page loads, and the overview reflects the Automation server you selected. |
| **Tasks > All Tasks** | The grid displays the Tasks visible to your MOVEit account. Check one known Task. |
| **Hosts > All Hosts** | The grid displays the Hosts visible to your MOVEit account. Check one known Host. |

If a page reports a connection error, follow [Troubleshooting](#troubleshooting). For broader acceptance, use the [RC3 smoke test](TESTING.md#manual-rc3-smoke-test).

For **Records > Task Run**, use **Grid Settings** to show the optional **Tags** column, or set it as a default through **Configuration > Settings > Grid Definitions > Task Run**. The column uses current task Tags from MAUI metadata in the selected MOVEit environment. It supports the existing filters and Report output; it does not represent the Tags that existed at the historical run time. No database schema change is needed for this column.

<a id="9-after-setup---first-steps"></a>

## Post-setup checklist

Open **Configuration > Settings** and complete the items you plan to use.

| Settings | What to do |
| --- | --- |
| **MOVEit Connections** | Add any other Automation servers. For scheduled reports, automatic backups, or Host Health Check, configure and test administrator **Data Access Credentials** on an active server in each applicable environment. |
| **SMTP Configuration** | For outbound mail, set the SMTP connection and From details, then use **Send Test Email**. |
| **Scheduler > System Jobs** | Review schedules and enable the jobs you need. For example, run **Host Health Check** once after configuring Data Access Credentials. For unattended operation on IIS, [keep MAUI running](#keep-maui-running-for-scheduled-jobs). |
| **Backup** | Review the backup schedule and retention. Run **Backup Now** and verify it reaches **Complete** in **Available Backups**. The visible list refreshes automatically and shows progress or interruption details; incomplete backups cannot be restored. Restore defaults to **MAUI only**; select the combined scope and intended environments to include MOVEit configuration. MAUI can snapshot SQLite. Back up SQL Server through your normal database process. [Backup and restore checks](TESTING.md#moveit-whole-configuration-import--backup-restore-regression) |
| **Editors** | Choose which editors open in MAUI and which open in MOVEit Web Admin, then **Save Configuration** and reload. The Integration components are not required for exclusive MAUI Editor use. Native editors require the [integration assets](#install-the-moveit-integration-assets), reinstalled after every MOVEit Automation upgrade. |
| **General > Quick Transfer** | Enable it if needed, **Save Configuration**, and reload. In **Transfers > Quick Transfer**, select known test Hosts, run **Preflight**, and transfer a small non-production file. [Quick Transfer checks](TESTING.md#quick-transfer-validation) |

### Keep MAUI running for scheduled jobs

On the IIS host, install **Application Initialization**. Set the MAUI app pool **Start Mode** to `AlwaysRunning` and **Idle Time-out (minutes)** to `0`. Set **Preload Enabled** to `True` on the MAUI site or application. Keep **Maximum Worker Processes** at `1`. Verify that a scheduled job runs after a recycle without someone opening MAUI. [Microsoft IIS startup and idle timeout guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/advanced?view=aspnetcore-8.0#application-initialization-module-and-idle-timeout)

Review the pool recycle schedule around important jobs. MAUI's **Keep Alive Service** helps while the application is running, and IIS startup/preload starts the application after a restart.

## Experimental features

Enable only the features you want under **Configuration > Settings > Experimental** and **Save Configuration**.

- **Task JSON Editor.** Select Tasks and open **Bulk Actions > JSON Editor**. Review proposed changes before saving. [Task JSON Editor acceptance](TESTING.md#task-json-editor-acceptance)
- **Copy Between Environments.** Configure administrator Data Access Credentials in both environments. The feature requires two distinct environments and both MAUI and MOVEit administrator access. Use **Copy To** on supported grids. Preserve `Data\EnvironmentCopy` and the Data Protection keys for retained reports and protected recovery files. [Copy To acceptance](TESTING.md#copy-between-environments-acceptance)
- **MOVEit User Management.** Follow the [service setup below](#13-optional-moveit-user-management) before enabling it. Each managed Automation server needs the optional Windows service.

<a id="13-optional-moveit-user-management"></a>

### Optional MOVEit User Management

The feature defaults **Off**. Enable it only for Automation servers whose Windows local groups you want to manage from MAUI. The **MAUI Local Groups Service** runs on each managed Automation server and polls MAUI directly over HTTPS using TLS 1.2. Allow outbound access from the Automation server to the MAUI address and port. The service needs no inbound listener on the Automation server. The MAUI web application and its service endpoint use the same IIS application and single worker. MAUI reads the client certificate from the TLS connection, so connect without a TLS-terminating proxy.

Get the Local Groups Service 1.0.6 files from `IntegrationAssets\MOVEitUserManagement` in the repository. Copy them to a folder on the Automation server where Administrators and SYSTEM control write access. Keep `MauiLocalGroups.Service.exe`, `MauiLocalGroups.Service.exe.config`, `agentsettings.json` and the PowerShell scripts together. The service requires .NET Framework 4.8 or later. Use elevated Windows PowerShell 5.1 for the setup commands.

#### 1. Configure the IIS endpoint

Configure HTTPS for MAUI and finish MAUI Initial Setup or Upgrade before checking the endpoint. For a root IIS application, the endpoint is `https://maui.example.com/api/local-groups-agent/`. If MAUI is an IIS application under a shared site at `/MAUI`, use `https://iis.example.com/MAUI/api/local-groups-agent/`.

At the effective IIS configuration path for `api/local-groups-agent`, including its `poll` and `result` routes, set **SSL Settings > Require SSL** and **Client certificates > Accept**. Under **Authentication**, enable **Anonymous Authentication** and disable **Windows Authentication** for this path only. IIS then passes requests to MAUI, which authenticates the service POSTs using the TLS client certificate. A browser GET returns only endpoint and protocol information. [IIS SSL settings](https://learn.microsoft.com/en-us/iis/configuration/system.webServer/security/access) · [IIS authentication settings](https://learn.microsoft.com/en-us/iis/configuration/system.webserver/security/authentication/)

When these IIS sections are delegated, add the following `<location>` as a child of the existing `<configuration>` in the **published MAUI application's** `web.config`. The path is relative to that file, so omit the IIS application alias here. Retain the published `<location path="." inheritInChildApplications="false">` block with its `aspNetCore` handler and hosting settings.

```xml
<location path="api/local-groups-agent">
  <system.webServer>
    <security>
      <access sslFlags="Ssl,SslNegotiateCert" />
      <authentication>
        <anonymousAuthentication enabled="true" />
        <windowsAuthentication enabled="false" />
      </authentication>
    </security>
  </system.webServer>
</location>
```

If IIS returns **500.19** with **0x80070021**, one of the sections is locked. Have the IIS administrator apply the equivalent settings in `applicationHost.config` at the full configuration path, such as `Your IIS Site/MAUI/api/local-groups-agent` or `Your IIS Site/api/local-groups-agent` for a root deployment. Use one configuration source for the override. Keep the agent URL as a route inside MAUI. If it returns **404.0**, inspect the effective handler mapping for the endpoint and confirm `aspNetCore` / `AspNetCoreModuleV2` is present. If IIS selects `StaticFile`, correct the application/override scope or handler configuration. The published `inheritInChildApplications="false"` limits inheritance into separate child applications. [IIS 500.19 guidance](https://learn.microsoft.com/en-us/troubleshoot/developer/webapps/iis/site-behavior-performance/http-error-500-19-webpage) · [ASP.NET Core IIS configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/web-config)

Have the IIS administrator verify that the existing HTTPS binding requests an optional client certificate at the initial TLS handshake. This avoids relying on renegotiation for the service's POST requests. Use `netsh http show sslcert` to inspect the binding and check **Client Certificate Negotiation** (`clientcertnegotiation`). On a shared HTTPS binding, this setting applies at the binding level. Keep client certificates optional at IIS and preserve the authentication settings for ordinary MAUI pages. If the override lives in `web.config`, carry it forward with each publish. [HTTP.sys binding options](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-http) · [TLS negotiation in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth#client-certificates)

#### 2. Check HTTPS and client certificate requirements

Use the certificate already bound to the MAUI IIS HTTPS site and, if it meets the requirements below, the Automation server's existing domain-issued computer certificate. A computer certificate with both Client Authentication and Server Authentication EKUs can be used by the Local Groups Service. This setup can use existing certificates on both hosts.

| TLS role | Certificate to use | Requirements |
| --- | --- | --- |
| MAUI HTTPS server | The certificate already bound to the IIS HTTPS site | The Automation server must trust its chain. The certificate must cover the DNS name or IP used in `brokerUrl`. |
| Local Groups Service client | A certificate in **Local Computer > Personal** (`Cert:\LocalMachine\My`) on the Automation server | Use a distinct, currently valid certificate for each managed Automation server. It must include the **Client Authentication** EKU (`1.3.6.1.5.5.7.3.2`) and have a private key readable by the service account, **LocalSystem** by default. IIS and MAUI must trust its chain. |

If IIS already trusts the issuing CA chain, leave the IIS certificate stores as they are. The service presents the public client certificate during TLS, and its private key stays on the Automation server. Enter that client certificate's **SHA-256** fingerprint in both `agentsettings.json` and the matching MAUI registration. The fingerprint identifies the enrolled certificate. Certificate trust comes from the CA chain.

If the IIS host cannot validate the chain, ask the web team to establish trust or use a certificate from a CA the IIS host already trusts. Use the Local Computer **Trusted Root Certification Authorities** store for approved roots and **Intermediate Certification Authorities** for intermediate CAs as needed. Allow both hosts to reach the applicable certificate-chain and revocation endpoints. MAUI performs online revocation checking on the client chain. In `certlm.msc` on the Automation server, use **Personal > Certificates > All Tasks > Manage Private Keys** to check the service account's Read access. [Windows certificate stores](https://learn.microsoft.com/en-us/windows-server/identity/ad-cs/inspect-certificates-manage-certificate-stores) · [IIS client-certificate trust](https://learn.microsoft.com/en-us/troubleshoot/developer/webapps/iis/site-behavior-performance/http-403-forbidden-access-website)

For a **self-signed lab certificate**, export only the public `.cer` and explicitly trust it on the host that validates it. Verify its SHA-256 fingerprint before import. On the IIS host, run `certlm.msc`, then import the public client certificate using **Trusted Root Certification Authorities > Certificates > All Tasks > Import**. If the MAUI HTTPS server certificate is also self-signed, import its public certificate into the same store on the Automation server. Keep the private keys on their respective hosts. The SHA-256 value in MAUI enrolls the certificate for that Automation server. IIS and MAUI still verify certificate trust.

#### 3. Enable MAUI and install the service

1. On the Automation server, run `.\Get-ClientCertificates.ps1` from the copied service folder. Select the certificate with Client Authentication and copy its 64-character **SHA256** value. The Windows **Thumbprint** column is a separate value.
2. Sign in to MAUI using the intended MOVEit connection and an account with both **MAUI Admin** and **MOVEit administrator** access. Open **Configuration > Settings > Experimental > MOVEit User Management**, enter the fingerprint for that server, turn on the feature and **Save Configuration**. Leave unused servers blank.
3. In the same card, copy the broker endpoint. Use the HTTPS address reachable from the Automation server, including the port and IIS application path where applicable. Edit `agentsettings.json` beside the service executable and enter the same SHA-256 value.

   ```json
   {
     "brokerUrl": "https://iis.example.com/MAUI/api/local-groups-agent/",
     "certificateSha256": "REPLACE_WITH_THE_64_CHARACTER_SHA256",
     "ignoreServerCertificateErrors": false
   }
   ```

4. In elevated Windows PowerShell, change to the copied service folder. Run the local check, register the service and start it.

   ```powershell
   .\MauiLocalGroups.Service.exe --check-config
   if ($LASTEXITCODE -ne 0) { throw 'Correct the service configuration before installing.' }
   .\Install.ps1
   Start-Service MauiLocalGroups
   Get-Service MauiLocalGroups
   ```

`Install.ps1` registers the EXE from the current folder with Manual startup. For startup at boot, run `Set-Service MauiLocalGroups -StartupType Automatic`. The service reports its Windows computer name automatically. Keep `ignoreServerCertificateErrors` set to `false` in production. The lab-only bypass affects validation of the MAUI HTTPS server certificate on the Automation server. The service client certificate still needs to be trusted and enrolled.

#### 4. Verify connectivity

| Check | What it verifies |
| --- | --- |
| Open the broker base URL from the Automation server | HTTP 200 with static endpoint information and no Windows credentials prompt confirms HTTPS reachability and routing. A browser GET alone cannot verify service authentication. |
| Run `MauiLocalGroups.Service.exe --check-config` | Checks the local configuration and certificate selection under the account running the command. It does not contact MAUI. |
| Open **Configuration > Settings > Security > MOVEit User Management** | The group list below **MAUI Users** loads automatically for the current Automation server. Successful load confirms the running service can authenticate, poll and return results. Use **Reload Groups** after any connectivity changes. |

If the card cannot load groups, check `%ProgramData%\MAUI\LocalGroups\service-*.jsonl` on the Automation server and the IIS request log, including the status and substatus. The service logs a connection error after three consecutive failures and logs when communication is restored. The Windows **Application** event log can help with service startup failures.

| Symptom | Check |
| --- | --- |
| Windows credentials prompt | Confirm Anonymous is enabled and Windows Authentication is disabled at the effective agent path, including `poll` and `result`. |
| TLS error, IIS 403.7 or IIS 403.16 | Check the HTTPS binding, client certificate negotiation, LocalSystem's private-key access, the Client Authentication EKU and both directions of certificate trust. A hostname mismatch refers to the server certificate and the address in `brokerUrl`. |
| MAUI HTTP 401 on an agent POST | Check that the feature is enabled, that MAUI received the client certificate from IIS, and that the SHA-256 fingerprint matches the registration for the intended connection. Check validity, EKU, chain and revocation if the fingerprint matches. |

For a console diagnostic, stop the installed service, then run `.\MauiLocalGroups.Service.exe --console` in elevated PowerShell. This uses your interactive account. Stop the console process and restart the installed service to confirm connectivity under LocalSystem.

For local development, the Visual Studio **https** project profile uses Kestrel and requests optional client certificates automatically. For an Automation VM, add an HTTPS address assigned to the MAUI development host to your local copy of `Properties\launchSettings.json`. Use that reachable address in `brokerUrl` and a trusted certificate covering it. In a VM, `localhost` resolves to the VM itself. IIS Express uses its own HTTPS and certificate settings.

#### 5. Maintain and operate the service

- **Update.** Use `sc.exe qc MauiLocalGroups` to locate `BINARY_PATH_NAME`. Stop the service, replace the packaged EXE and EXE configuration in that folder, retain the installed `agentsettings.json`, then restart and verify **Reload Groups**.
- **Renew a client certificate.** Install the replacement with its private key on the Automation server. Update the SHA-256 registration in MAUI and `agentsettings.json`, restart the service, and verify connectivity before retiring the old certificate.
- **Disable or uninstall.** Turn off the feature and save to hide the Security card and reject service POSTs. On the Automation server, stop `MauiLocalGroups` and run `.\Uninstall.ps1` elevated. The script deregisters the service. Files, settings, certificates, logs and group memberships remain on the server.

For a functional check, use a disposable custom group. Default Windows groups are protected. MAUI disables deletion while a group has members or MOVEit Resource Group assignments. In **Add Users or Groups**, use `.\name` for local matches or `DOMAIN\prefix` to search by account prefix within a domain. After an uncertain write, reload and inspect the group before trying another change. Preserve `Data\LocalGroupsBroker` with the rest of the MAUI application data. See [live acceptance checks](TESTING.md#optional-moveit-user-management-acceptance).

## Shared Reporting API

**Report environment readiness:** Removing a MOVEit connection does not delete its saved Reports or history. All Reports/My Reports check the current connection catalog: environments with configured connections but no Data Access Credentials receive the credential warning; retained Reports for removed environments receive a separate explanation in the same warning area that the environment is no longer configured. Restoring a connection with the same EnvironmentKey updates readiness on the next list load. Reports remain tied to their original environment and are never reassigned automatically.

In **Create Report**, Delivery appears directly below Description. Choose **None** to save the current grid view without email recipients or scheduling. API Access is available for None, Send Now, One Time and Recurring delivery. Enabling it prepares the URL before Save; Copy Link and Regenerate Link work immediately, and Save activates the exact prepared URL. A new draft stays inactive if you cancel or the save fails. Existing Reports can switch to **None** in **Edit Report Information**, clearing email recipients and the next scheduled run without disabling API access.

**Send Now with API Access** saves an unscheduled report for the API and sends the current grid snapshot once. If email delivery fails after the report is saved, MAUI reports that failure explicitly while retaining the saved report and API link. **None** does not require SMTP or a future delivery time. API generation still requires the environment's Data Access Credentials; scheduled email delivery retains its existing requirements.

### IIS permissions for reports

IIS must allow anonymous requests to **`api/reports`** so MAUI can serve `/api/reports/{reportGuid}/csv` without a Windows credentials prompt. The CSV endpoint already allows anonymous access in MAUI; normal MAUI pages retain their application login requirements. If the following settings are already inherited by this path, no additional override is needed.

| IIS setting at the report API path | Required value |
| --- | --- |
| Anonymous Authentication | Enabled; **Edit > Application pool identity** |
| Windows Authentication | Disabled |
| Require SSL | Enabled |
| Client certificates | Ignore |

For a scoped override, add this `<location>` directly inside `<configuration>` in the **published MAUI application's** `web.config`, alongside its existing `<location>` block. Keep the ASP.NET Core handler/hosting block intact. The path is relative to MAUI: use `api/reports` even when the public URL starts with `/MAUI/api/reports`. This is an application route, so it needs no physical folder, virtual directory or separate IIS application.

```xml
<location path="api/reports">
  <system.webServer>
    <security>
      <access sslFlags="Ssl" />
      <authentication>
        <anonymousAuthentication enabled="true" userName="" />
        <windowsAuthentication enabled="false" />
      </authentication>
    </security>
  </system.webServer>
</location>
```

If IIS URL Authorization rules restrict this path, configure it to allow anonymous users, including overriding any inherited deny rule. If these configuration sections are locked (`500.19`, `0x80070021`), have the IIS administrator apply the same settings in `applicationHost.config` at the full site/application/report path. Disable Basic Authentication at this path if it is enabled. Continue using the existing app-pool file permissions from [Deploy to IIS](#deploy-to-iis); anonymous report access needs no additional NTFS grants. The Local Groups agent retains its separate client-certificate configuration.

After saving API access for a Report, test its copied URL in a private browser window without a MAUI session. [IIS anonymous authentication](https://learn.microsoft.com/en-us/iis/configuration/system.webserver/security/authentication/anonymousauthentication/) · [IIS SSL settings](https://learn.microsoft.com/en-us/iis/configuration/system.webserver/security/access) · [IIS authorization rules](https://learn.microsoft.com/en-us/iis/configuration/system.webserver/security/authorization/)

### Enable and use a report link

1. Apply the normal MAUI upgrade after deploying this build. Database schema 2 adds persisted shared Report links. Initial Setup includes this migration for a new installation.
2. Configure Data Access Credentials for the Report's MOVEit environment.
3. Open **Reports > All Reports** or **Reports > My Reports**. Choose **Edit Report Information** for a saved Report.
4. Under **API Access**, turn on **Enable API access**. The Report URL is prepared immediately; **Copy Link** and **Regenerate Link** are available before saving.
5. Copy the URL as needed, then select **Save Report** to activate that exact link. The URL has the form `/api/reports/{reportGuid}/csv` and includes the IIS application path when applicable. Reopening the Report is not required to copy its link.

Anyone with the link can retrieve current CSV data without a MOVEit account. Add `?download=true` to request a file attachment named `report.csv`. The default response (also `?download=false`) uses an inline disposition with the same suggested filename. Both modes return `text/csv; charset=utf-8` and the same CSV body. Inline follows the browser's handling of CSV; it does not guarantee on-screen display, and a browser may still download the response. The CSV starts with its column headings and uses the saved Report's displayed values. The link follows subsequent edits to the saved Report definition.

API access has its own switch, independently of the email schedule. A newly prepared link remains inactive until Save succeeds; closing without saving discards it. Before the first Save, **Regenerate Link** replaces only the prepared URL. For an already active link, **Regenerate Link** retains its immediate replacement after confirmation. To revoke access, turn the switch off and save. Share the active URL with its consumers.

The API uses a 30-second cache, up to two concurrent generations per MAUI instance, and a limit of 60 requests per minute per source IP. A busy response includes Retry-After. This implementation targets a single active MAUI application instance.

Records retrieval has a 10,000-source-row ceiling before saved filters. Response headers include generation time, row count, definition revision, and source-limit information. API generation appears in Report History with zero email recipients.

Preserve the metadata database and Data Protection keys with backups. Restore operations can reinstate previously active links. Regenerate links affected by a recovery. IIS and reverse-proxy access logs may contain the shared URL, so treat those logs as sensitive and configure URI redaction or logging exclusions where available.

<a id="10-protect-installation-owned-files"></a>
<a id="11-upgrading-maui"></a>

## Protect data and deploy updates


Back up and preserve the live `appsettings.json` and the entire `Data` directory, including the default SQLite database, backups, Data Protection keys and runtime recovery files. Preserve any database stored outside `Data` separately. Keep these files outside `wwwroot`. Avoid deployment options that delete the destination before copying a release.

Deploy the binaries, `appsettings.template.json` and `upgrade.json` from one matching publish. If you customized the deployed `web.config` for User Management or shared Reporting API access, carry forward the path-specific settings. See the [Upgrade guide](UPGRADE.md) for verification, upgrade and rollback. On Windows, MAUI's Data Protection keys under `Data\DataProtection-Keys` use machine-level DPAPI. [Security guidance](SECURITY.md#restricted-iis-application-pool-permissions)

<a id="12-troubleshooting-checklist"></a>

## Troubleshooting

| Symptom | Check |
| --- | --- |
| IIS fails to start MAUI | Check the app pool, the .NET 8 Hosting Bundle, the published `web.config` and the app-pool file permissions. Inspect Windows Application logs for the startup error. |
| IIS 500.30 with mismatched release versions | Redeploy a complete matching publish while preserving live `appsettings.json` and `Data`. Run the [release package verifier](UPGRADE.md#release-package-contract). |
| **Test Connection** fails | From the machine running MAUI, verify the Web Admin URL and TLS trust, the licensed MOVEit API and local COM registration. On IIS, verify the app-pool identity can activate `MICentralAPICOM.MICentralAPI`. |
| **Test Database** fails | Check Modify access on the SQLite directory, or SQL Server connectivity and the database permissions for the account MAUI is using. |
| Sign-in or grid access fails | Verify the selected Automation server, MOVEit account and permissions. For **MOVEit Unreachable**, check DNS and HTTPS from the MAUI host, then use **Refresh Now** to confirm a live read. Review **Configuration > Logs** after signing in. |
| Native editor launch fails | If MOVEit Automation was upgraded, [reinstall the optional integration](#reinstall-after-a-moveit-automation-upgrade) in the overwritten Tomcat folder. Check the exact MAUI origin and script reference on every applicable node, refresh the browser cache and launch again. These assets are not needed for exclusive MAUI Editor use. |
| User Management agent cannot connect | See [Verify connectivity](#4-verify-connectivity) for IIS authentication, certificate and service log checks. |
