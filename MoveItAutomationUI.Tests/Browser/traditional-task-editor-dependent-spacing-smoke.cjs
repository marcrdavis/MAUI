// Regression contract: checkbox-controlled text/number/select values in the Traditional
// Task Editor use the same dependent indentation as Special Filters.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(base, file), 'utf8');
const razor = read('Views/Shared/Editors/_TraditionalTaskEditorDialog.cshtml');
const css = read('wwwroot/css/editors/traditional-task-editor.css');
const config = read('Views/Configuration/Settings.cshtml');

// The named inputs are the complete set of inline checkbox-followed-by-input fields
// in the Source/Destination panels. Count duplicates in both directions deliberately.
const cases = [
    ['source', 'ignoreFolders', 'excludeFolders'],
    ['source', 'ignoreFiles', 'excludeFiles'],
    ['source', 'overrideConnectTimeout', 'connectTimeoutSeconds'],
    ['source', 'overrideDataTimeout', 'dataTimeoutSeconds'],
    ['source', 'overrideRetryCount', 'retryCount'],
    ['source', 'overrideRetryTimeout', 'retryTimeoutSeconds'],
    ['source', 'overrideRescanTime', 'rescanSeconds'],
    ['source', 'overrideMaxFiles', 'maxFiles'],
    ['source', 'overrideMaxBytes', 'maxBytes'],
    ['source', 'overrideMd5Mode', 'md5Mode'],
    ['source', 'overrideMd5FileName', 'md5FileName'],
    ['destination', 'overrideConnectTimeout', 'connectTimeoutSeconds'],
    ['destination', 'overrideDataTimeout', 'dataTimeoutSeconds'],
    ['destination', 'overrideRetryCount', 'retryCount'],
    ['destination', 'overrideRetryTimeout', 'retryTimeoutSeconds']
];
for (const [direction, toggle, value] of cases) {
    // Each value is in the same checkbox/value label; avoid matching unrelated fields.
    const relevantLine = razor.split('\n').find(line =>
        line.includes(`data-${direction}-field="${toggle}"`) &&
        line.includes(`data-${direction}-field="${value}"`));
    assert.ok(relevantLine, `${direction}.${toggle} must own its dependent ${value} control`);
    assert.match(relevantLine, /class="field-block(?: full-span)? traditional-task-editor-check-with-value"/,
        `${direction}.${toggle} must reuse the dependent-spacing class`);
}
assert.match(css, /--traditional-task-editor-dependent-indent:\s*25px;/);
assert.match(css, /\.traditional-task-editor-check-with-value\s*>\s*:is\(/);
assert.match(css, /\.traditional-task-editor-task-log-retention-field[^\n]+margin-left:\s*var\(--traditional-task-editor-dependent-indent\)/);
assert.match(css, /\.traditional-task-editor-destination-file-name\s*\{\s*margin-left:\s*var\(--traditional-task-editor-dependent-indent\)/);
assert.match(css, /\.traditional-task-editor-option-note\s*\{[\s\S]*?var\(--traditional-task-editor-dependent-indent\)/);
assert.ok(!razor.includes('traditional-task-editor-check-with-value traditional-task-editor-check-with-value'),
    'Do not duplicate checkbox/value classes');

assert.ok(config.includes('Control access to the Quick Transfer workspace.'));
assert.ok(config.includes('Enable the Quick Transfers feature and show the Transfers menu in the left navigation. You will need to refresh the page after changing and saving this setting.'));
assert.ok(!config.includes('Control access to the administrator-only Quick Transfer workspace.'));

const project = read('MoveItAutomationUI.csproj');
const version = /<Version>([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)<\/Version>/.exec(project)?.[1];
assert.ok(version, 'Project version must exist');
const assemblyVersion = version.split('-')[0];
assert.ok(project.includes(`<AssemblyVersion>${assemblyVersion}.0</AssemblyVersion>`));
assert.ok(project.includes(`<FileVersion>${assemblyVersion}.0</FileVersion>`));
assert.match(project, new RegExp(`<InformationalVersion>${version.replaceAll('.', '\\.')}\\+build\\.[0-9]{8}</InformationalVersion>`));
for (const file of ['upgrade.json', 'appsettings.custom.json', 'appsettings.template.json']) {
    assert.ok(read(file).includes(`"${version}"`), `${file} must target the current version`);
}
console.log('Traditional Task Editor dependent spacing, Quick Transfer copy, and consistent release metadata: PASS');

