'use strict';
// Source-only guard for the restricted IIS application-pool file ACL contract.
// Complements the C# AppSettingsRestrictedIisTests; does NOT simulate .NET/IIS.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const store = read('Services/Configuration/AppSettingsDocumentStore.cs');
const setup = read('Services/Configuration/InitialSetupService.cs');
const controller = read('Controllers/SetupController.cs');
const upgrade = read('Services/Upgrade/MauiUpgradeService.cs');
const restore = read('Services/Backup/MauiBackupService.Management.cs');
const project = read('MoveItAutomationUI.csproj');
const seed = JSON.parse(read('appsettings.json'));
assert.deepEqual(seed, {}, 'New-installation placeholder must be exactly a JSON object {}');
assert.ok(project.includes('<Content Update="appsettings.json">') &&
    /<Content Update="appsettings.json">\s*<CopyToOutputDirectory>Never<\/CopyToOutputDirectory>\s*<CopyToPublishDirectory>Never<\/CopyToPublishDirectory>/.test(project), 'Installation-owned settings must stay out of build and publish output');
assert.ok(!store.includes('LivePath + ".tmp"'), 'Configuration must not write a root .tmp');
assert.ok(!/File\.Move\([^\n]*LivePath/.test(store), 'Configuration must not replace a root file');
assert.match(store, /new FileStream\(LivePath, FileMode\.Open, FileAccess\.Write, FileShare\.None\)/);
assert.match(store, /Path\.Combine\(environment\.ContentRootPath, "Data", "ConfigurationRecovery"\)/);
assert.match(store, /SaveLastGood\(previous\);/);
assert.match(store, /WriteExisting\(previous\);/);
assert.match(store, /EnsureExistingLiveWritable\(\);/);
assert.match(store, /EnsureRecoveryWritable\(\);/);
assert.match(store, /OverlayExistingValues\(template, live\);/);
assert.ok(setup.indexOf('ReadSeedAsync(cancellationToken)') < setup.indexOf('ProvisionRequiredSchemaAsync('), 'Setup must preflight config writes before provisioning metadata DB');
assert.match(controller, /catch \(AppSettingsDeploymentException ex\)/);
assert.match(upgrade, /GetInstallationRelativePath\(backupPath\)/);
assert.match(restore, /Data\/ConfigurationRecovery/);
console.log('PASS restricted IIS appsettings placeholder, direct existing-file persistence, recovery snapshots, Setup preflight, Upgrade/Restore paths.');
