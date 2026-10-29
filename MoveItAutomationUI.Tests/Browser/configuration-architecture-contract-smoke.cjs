// Source-level contracts complement the executable xUnit configuration tests.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = f => fs.readFileSync(path.join(root, f), 'utf8');
const store = read('Services/Configuration/AppSettingsDocumentStore.cs');
const snapshot = read('Services/Configuration/AppSettingsConfigurationSnapshot.cs');
const settings = read('Services/Configuration/AppSettingsConfigurationService.cs');
const view = read('Services/Configuration/ConfigurationSettingsViewModelBuilder.cs');
const backup = read('Services/Backup/MauiBackupService.Management.cs');
const js = read('wwwroot/js/configuration.js');
assert.match(store, /if \(publishRuntime\) _runtimeConfiguration\.Reload\(\)/);
assert.match(store, /WriteExisting\(previous\)/);
assert.match(backup, /publishRuntime: false/);
assert.match(snapshot, /SHA256\.HashData/);
assert.match(settings, /_writeCoordinator\.Gate\.WaitAsync/);
assert.ok(settings.indexOf('_writeCoordinator.Gate.WaitAsync') < settings.indexOf('_snapshots.FromDocument(root)'));
assert.ok(settings.indexOf('_snapshots.FromDocument(root)') < settings.indexOf('ValidateAndNormalizeAsync('));
assert.match(settings, /ConfigurationRevisionConflictException/);
assert.match(view, /var snapshot = _snapshots.Read\(\)/);
assert.match(view, /ConfigurationRevision = snapshot.Revision/);
assert.match(view, /UseMauiTaskEditor = editors.UseMauiTaskEditor.Value/);
assert.match(view, /UseMauiHostEditor = editors.UseMauiHostEditor.Value/);
assert.match(read('Views/Configuration/Settings.cshtml'), /data-configuration-revision/);
assert.match(js, /configurationRevision = String\(data.configurationRevision/);
assert.match(read('Controllers/ConfigurationController.cs'), /return Conflict\(/);
for (const file of ['Services/Reporting/ReportConfigurationStore.cs','Services/MoveIt/DataAccessCredentialService.cs','Services/Upgrade/MauiUpgradeService.cs']) {
  assert.doesNotMatch(read(file), /ReloadConfiguration\(\)|configurationRoot\.Reload\(\)/);
}
console.log('PASS: centralized configuration snapshot, publication, revision and RC2 editor settings');
