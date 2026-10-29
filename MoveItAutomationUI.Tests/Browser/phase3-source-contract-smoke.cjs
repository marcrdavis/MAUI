// Source-level integration checks only. These do not substitute for C# compilation/xUnit.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const program = read('Program.cs');
const extension = read('Features/Editors/MauiEditorServiceCollectionExtensions.cs');
const rest = read('Services/MoveIt/MoveItRestClient.cs');
const spreadsheet = read('Services/MoveIt/TaskSpreadsheetImportService.cs');
const persistence = read('Services/MoveIt/TraditionalTasks/TraditionalTaskPersistenceService.cs');

const registrations = [
    'IHostEditorService', 'IHostEditorSaveService', 'ITraditionalTaskEditorService',
    'ITraditionalTaskEditorSaveService', 'ITraditionalTaskEditorFolderBrowseService',
    'ITraditionalTaskEditorTransferExceptionService', 'ITraditionalTaskEditorSourceTimestampService'
];
assert.match(program, /AddMauiEditorFeatures\(/);
for (const name of registrations) assert.ok(extension.includes(`AddScoped<${name},`), `${name} is not registered`);
const allApiNames = new Set((rest.split('public interface IMoveItRestClient')[1].split('public sealed class MoveItRestClient')[0]
    .match(/\b[A-Za-z]\w+Async\s*\(/g) || []).map(x => x.replace(/\s*\($/, '')));
const implementations = rest.split('public sealed class MoveItRestClient')[1];
for (const method of allApiNames) assert.ok(implementations.includes(`${method}(`), `Missing REST client implementation: ${method}`);
assert.match(spreadsheet, /"RESOURCE_GROUP",\s*"TASK_NAME",\s*"SOURCE_FILE"/);
for (const host of ['sourceHost', 'destinationHost']) {
    assert.match(spreadsheet, new RegExp(`if \\(!${host}\\.Success\\)\\s*\\{[\\s\\S]{0,400}?\\}\\s*else if`), `Unresolved ${host} must not be dereferenced`);
}
assert.match(persistence, /CreateDisabledDefinitionAsync\(/);
assert.match(persistence, /return CreateDefinitionAsync\(definition, cancellationToken\)/);
assert.doesNotMatch(read('Services/MoveIt/QuickTransferService.cs'), /CreateSkeletonAsync\(/);
console.log(`PASS Phase 3 DI registrations, REST method surface (${allApiNames.size}), required CSV group, nullable Host paths, REST task creation.`);
