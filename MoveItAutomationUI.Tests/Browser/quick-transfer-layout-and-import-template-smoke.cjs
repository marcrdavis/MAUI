// Source contract checks for three desktop Task Options and release-owned Excel import template.
// This is not a browser screenshot, .NET publish, or live MOVEit integration test.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');
const css = read('wwwroot/css/site.css');
const view = read('Views/QuickTransfers/Index.cshtml');
const options = view.match(/<div class="quick-transfer-options-list">([\s\S]*?)<\/section>/)?.[1];
assert.ok(options, 'Task Options section must exist');
assert.equal((options.match(/class="quick-transfer-switch-row"/g) || []).length, 3,
  'Task Options should have exactly three independent checkboxes');
assert.ok(options.indexOf('data-quick-persist-task') < options.indexOf('data-quick-delete-temp'));
assert.ok(options.indexOf('data-quick-delete-temp') < options.indexOf('data-quick-confirm-email'));
assert.ok(options.indexOf('data-quick-confirm-email') < options.indexOf('data-quick-confirm-recipients-row'),
  'Recipient field must remain below the three checkbox choices');
assert.match(css, /\.quick-transfer-options-list \{[^}]*grid-template-columns:\s*repeat\(3,\s*minmax\(0,\s*1fr\)\)/);
assert.match(css, /max-width:\s*1500px[\s\S]*?\.quick-transfer-options-list \{ grid-template-columns: repeat\(2, minmax\(0, 1fr\)\); \}/);
assert.match(css, /max-width:\s*1100px[\s\S]*?\.quick-transfer-options-list \{ grid-template-columns: 1fr; \}/);
assert.match(css, /\.quick-transfer-task-name \{ grid-column: 1 \/ -1;/);
const templateRel = 'Templates/TaskImport/MAUI_ImportTasksTemplate.xlsx';
const template = fs.readFileSync(path.join(root, templateRel));
assert.ok(template.length > 500);
assert.equal(template.readUInt32LE(0), 0x04034b50, 'Template must be a real XLSX ZIP package');
const project = read('MoveItAutomationUI.csproj');
assert.ok(project.includes('<None Update="Templates\\TaskImport\\MAUI_ImportTasksTemplate.xlsx">'));
assert.match(project, /<None Update="Templates\\TaskImport\\MAUI_ImportTasksTemplate.xlsx">[\s\S]*?<CopyToOutputDirectory>PreserveNewest<\/CopyToOutputDirectory>[\s\S]*?<CopyToPublishDirectory>PreserveNewest<\/CopyToPublishDirectory>/);
for (const doc of ['README.md','INSTALLATION.md','TESTING.md']) {
  assert.ok(read(doc).includes('MAUI_ImportTasksTemplate.xlsx'), `${doc} should reference the template`);
}
console.log('PASS three-column Quick Transfer options, responsive layout, dependent fields and published XLSX template contract.');
