// End-to-end source contracts for Settings' independent appsettings.json writers.
// Executable xUnit tests validate persisted revisions; this guards the browser/controller wiring.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const credentials = read('Services/MoveIt/DataAccessCredentialService.cs');
const smtp = read('Services/Reporting/ReportConfigurationStore.cs');
const controller = read('Controllers/ConfigurationController.cs');
const outbound = read('Controllers/OutboundReportsController.cs');
const ops = read('wwwroot/js/configuration-operations.js');
const smtpJs = read('wwwroot/js/smtp-settings.js');
const settings = read('wwwroot/js/configuration.js');
const tests = read('MoveItAutomationUI.Tests/Configuration/IndependentSettingsRevisionTests.cs');

assert.match(credentials, /Task<string> SaveAsync\(/);
assert.match(credentials, /Task<string> ClearAsync\(/);
assert.match(smtp, /Task<string> ClearSmtpPasswordAsync\(/);
for (const source of [credentials, smtp]) {
    assert.match(source, /await _writeCoordinator\.Gate\.WaitAsync/);
    assert.match(source, /await _appSettings\.WriteLiveAsync\(root, cancellationToken\)/);
    assert.match(source, /return _snapshots\.GetRevision\(root\)/);
    assert.ok(source.indexOf('await _appSettings.WriteLiveAsync(root, cancellationToken)') < source.indexOf('return _snapshots.GetRevision(root)'),
        'Revision must be returned after the persisted write, while holding the gate');
}
assert.match(controller, /var configurationRevision = await _dataAccessCredentials\.SaveAsync/);
assert.match(controller, /var configurationRevision = await _dataAccessCredentials\.ClearAsync/);
assert.match(outbound, /var configurationRevision = await _configuration\.ClearSmtpPasswordAsync/);
assert.match(ops, /publishConfigurationRevision\(data\)/);
assert.ok((ops.match(/publishConfigurationRevision\(data\);/g) || []).length === 2);
assert.match(smtpJs, /maui:configuration-revision-updated/);
assert.match(settings, /window\.addEventListener\('maui:configuration-revision-updated'/);
assert.match(settings, /delete payload\.configurationRevision/);
assert.match(settings, /enableQuickTransfer: checked\('enableQuickTransfer'\)/);
assert.match(settings, /useMauiTaskEditor: checked\('useMauiTaskEditor'\)/);
assert.match(settings, /useMauiHostEditor: checked\('useMauiHostEditor'\)/);
assert.match(tests, /DataAccessSaveAndClear_ReturnRevisionsOfPersistedDocuments/);
assert.match(tests, /SmtpClear_ReturnsCurrentRevisionAndRetainsOtherSmtpSettings/);
assert.match(read('Services/Configuration/AppSettingsConfigurationService.cs'), /throw new ConfigurationRevisionConflictException\(\)/);
assert.match(controller, /return Conflict\(/);
console.log('Independent Settings revision endpoints, events, RC2-specific fields and concurrency protection: PASS');
