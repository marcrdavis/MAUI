# Deploy MAUI Local Groups Service

**MAUI documentation version:** `0.18.584-RC3` · Build `20261125`. Service versioning is separate from the MAUI application version.

This optional service supports **MOVEit User Management**. It is separate from the browser integration under `IntegrationAssets/MOVEitWebAdmin`, which is not required when using the MAUI Editors exclusively. For the MAUI application itself, start with the [Visual Studio Quick Start](../../README.md#quick-start-with-visual-studio); for optional native-editor integration and its required reinstallation after MOVEit Automation upgrades overwrite Tomcat, see [Integration setup and maintenance](../../INSTALLATION.md#install-the-moveit-integration-assets).

Use a Windows Automation member server with .NET Framework 4.8 or later and Windows PowerShell 5.1. Run installation and service commands in an elevated PowerShell window. The service runs as LocalSystem and connects directly to MAUI over outbound HTTPS with TLS 1.2.

## Install and verify

Follow the [MAUI installation guide](../../INSTALLATION.md#optional-moveit-user-management) for the IIS agent endpoint, certificate trust, and service registration. Copy the files in this folder together to the Automation server.

From the copied service folder, run `.\Get-ClientCertificates.ps1` and register the selected client certificate's SHA-256 fingerprint under **Configuration > Settings > Experimental > MOVEit User Management**. Save the registration, then set `brokerUrl` and `certificateSha256` in the `agentsettings.json` beside the EXE. Use the MAUI HTTPS address reachable from the Automation server, including the IIS application path if applicable. Keep `ignoreServerCertificateErrors` set to `false` in production.

In elevated Windows PowerShell, stay in the copied service folder and run:

```powershell
.\MauiLocalGroups.Service.exe --check-config
if ($LASTEXITCODE -ne 0) { throw 'Correct the service configuration before installing.' }
.\Install.ps1
Start-Service MauiLocalGroups
Get-Service MauiLocalGroups
```

The installer registers the EXE from the current folder with Manual startup. To start it at boot, run `Set-Service MauiLocalGroups -StartupType Automatic`. Restart the service after configuration or certificate changes.

Open **Configuration > Settings > Security > MOVEit User Management** and select **Reload Groups** to verify that MAUI loads groups for the intended computer. See the [connectivity checks](../../INSTALLATION.md#4-verify-connectivity) for the service log and IIS status codes.

## Self-signed certificates for a lab

A self-signed certificate has no separate CA chain. For a lab, explicitly trust its public certificate on the host that validates it. Keep the two TLS roles separate and use short-lived test certificates. Check the certificate SHA-256 against a value obtained from its owner before importing it. Import public `.cer` files into Root and keep the private keys on their original hosts. [Windows certificate stores](https://learn.microsoft.com/en-us/windows-server/identity/ad-cs/inspect-certificates-manage-certificate-stores)

### Trust the service client on MAUI

1. On the **Automation server**, create a client certificate in elevated Windows PowerShell. This keeps its non-exportable private key in Local Computer > Personal. The export contains only the public certificate.

```powershell
   $client = New-SelfSignedCertificate -Type Custom `
       -Subject "CN=MAUI Local Groups Lab $env:COMPUTERNAME" `
       -CertStoreLocation 'Cert:\LocalMachine\My' `
       -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
       -KeyExportPolicy NonExportable -KeyUsage DigitalSignature `
       -TextExtension '2.5.29.37={text}1.3.6.1.5.5.7.3.2' `
       -NotAfter (Get-Date).AddDays(30)
   Export-Certificate -Cert $client -FilePath .\MauiLocalGroups-LabClient.cer
   Get-FileHash .\MauiLocalGroups-LabClient.cer -Algorithm SHA256
   ```

2. Copy the `.cer` to the **MAUI host**. Compare its SHA-256 there with the Automation server's output. In elevated PowerShell on the MAUI host, import the verified file.

```powershell
   Get-FileHash .\MauiLocalGroups-LabClient.cer -Algorithm SHA256
   Import-Certificate -FilePath .\MauiLocalGroups-LabClient.cer -CertStoreLocation 'Cert:\LocalMachine\Root'
   ```

3. Use that SHA-256 in `agentsettings.json` and in the MAUI registration for this Automation server. Verify LocalSystem can read the key on the Automation server.

[Create a client certificate](https://learn.microsoft.com/en-us/powershell/module/pki/new-selfsignedcertificate) · [Export a public certificate](https://learn.microsoft.com/en-us/powershell/module/pki/export-certificate) · [Import a certificate into Local Computer Root](https://learn.microsoft.com/en-us/powershell/module/pki/import-certificate)

### Trust a self-signed MAUI listener on the Automation server

On the **MAUI host**, export the public `.cer` of the **server certificate bound to the HTTPS listener**. Use its actual Windows thumbprint and store. A user-scoped development certificate may be in `Cert:\CurrentUser\My` for the account running MAUI.

```powershell
$server = Get-Item 'Cert:\LocalMachine\My\SERVER_CERTIFICATE_THUMBPRINT'
Export-Certificate -Cert $server -FilePath .\MAUI-LabServer.cer
Get-FileHash .\MAUI-LabServer.cer -Algorithm SHA256
```

Copy the file to the **Automation server** and compare its SHA-256 with the MAUI host's value. Import the verified file there in elevated PowerShell.

```powershell
Get-FileHash .\MAUI-LabServer.cer -Algorithm SHA256
Import-Certificate -FilePath .\MAUI-LabServer.cer -CertStoreLocation 'Cert:\LocalMachine\Root'
```

Use the hostname or IP covered by that certificate in `brokerUrl`. A lab VM needs a reachable MAUI address. `localhost` resolves inside the VM itself. With both trust directions configured, use `"ignoreServerCertificateErrors": false`. The optional lab bypass `true` skips validation of the MAUI server certificate only. MAUI still checks the service's client certificate. Restore `false` and restart after any bypass test.

To use the GUI for either Root import, run `certlm.msc` on the receiving host, open **Trusted Root Certification Authorities > Certificates**, and choose **All Tasks > Import**. At the end of the lab, remove only the lab certificates you added and clear the lab registration in MAUI. [Manage Local Computer stores](https://learn.microsoft.com/en-us/windows-server/identity/ad-cs/inspect-certificates-manage-certificate-stores)

## Troubleshoot and maintain

* Look in `%ProgramData%\MAUI\LocalGroups\service-*.jsonl` for polling and operation details. Check the Windows **Application** event log if the service fails to start. After three failed broker exchanges the service logs one error, then one recovery entry when a usable response returns.
* For a TLS name error, check the address against the bound server certificate. For trust errors, check both certificate chains and the Local Computer stores. For HTTP 401/403, check the listener's client-certificate setting, client EKU and private-key access, and the certificate's SHA-256 registration in MAUI.
* For a live diagnostic, stop the service first and run `.\MauiLocalGroups.Service.exe --console` elevated. This uses your interactive account. Restart the installed service to verify LocalSystem access.
* For an upgrade in the same folder, stop the service, replace the EXE and EXE config with the new build, retain your `agentsettings.json` and restart. If a change has an uncertain result, reload groups in MAUI and inspect the current state before another write.
* To uninstall, stop the service, then run `Uninstall.ps1` elevated. It deregisters `MauiLocalGroups` from SCM. Deployment files, certificates, logs and Windows groups remain available.
