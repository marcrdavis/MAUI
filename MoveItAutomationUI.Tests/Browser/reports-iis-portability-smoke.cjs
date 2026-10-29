const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const grid = read('wwwroot/js/grid.js');
const capture = grid.match(/function captureReportSourceContext\(\) \{[\s\S]*?\n        \}(?=\n\n        function captureReportEditFingerprint)/)?.[0];
assert.ok(capture, 'Report context capture function must exist');
for (const [appBase, locationPath, expected] of [
    ['/', '/Tasks/Index', '/Tasks/Index'],
    ['/MAUI/', '/MAUI/Tasks/Index', '/Tasks/Index'],
    ['/dev/MAUI/', '/dev/MAUI/Records/TaskRun', '/Records/TaskRun'],
    ['/Tasks/', '/Tasks/Tasks/Index', '/Tasks/Index'],
    ['/Records/', '/Records/Records/FileActivity', '/Records/FileActivity'],
]) {
    const ctx = {
        document: { documentElement: { dataset: { appBaseUrl: appBase } } },
        window: { location: { pathname: locationPath, search: '?filter=failed&reportEdit=ignored&folderId=123' } },
        URLSearchParams
    };
    vm.runInNewContext(`${capture}; this.result = captureReportSourceContext();`, ctx);
    assert.equal(ctx.result.path, expected, 'Report source path must not store PathBase');
    assert.equal(ctx.result.query.filter, 'failed');
    assert.equal(ctx.result.query.folderId, '123');
    assert.equal(ctx.result.query.reportEdit, undefined);
}
// Temporary "Back to ..." context must not make exported Reports deployment-specific.
for (const base of ['/DEV/MAUI/', '/QA/AutomationUI/', '/']) {
    const currentPath = base === '/' ? '/Records/TaskRun' : `${base.slice(0, -1)}/Records/TaskRun`;
    const oldPath = '/DEV/MAUI/Tasks/Index';
    const ctx = {
        document: { documentElement: { dataset: { appBaseUrl: base } } },
        window: { location: { pathname: currentPath,
            search: `?taskId=101&returnUrl=${encodeURIComponent(oldPath)}&returnLabel=Task&filter=failed` } },
        URLSearchParams
    };
    vm.runInNewContext(`${capture}; this.result = captureReportSourceContext();`, ctx);
    assert.equal(ctx.result.path, '/Records/TaskRun');
    assert.equal(ctx.result.query.taskId, '101');
    assert.equal(ctx.result.query.filter, 'failed');
    assert.equal(ctx.result.query.returnUrl, undefined);
    assert.equal(ctx.result.query.returnLabel, undefined);
}
const controller = read('Controllers/ReportsController.cs');
assert.ok(controller.includes('OutboundReportSourceRoutes.Normalize(report.GridKey, report.SourceContext)'));
assert.ok(controller.includes('Url.Content("~" + source.Path)'), 'Report edit navigation must use current application PathBase');
const repo = read('Services/Reporting/OutboundReportRepository.cs');
assert.ok(repo.includes('OutboundReportSourceRoutes.Normalize(request.GridKey, request.SourceContext)'));
assert.ok(read('Services/Reporting/OutboundReportSourceRoutes.cs').includes('pair.Key.Equals("returnUrl"'), 'Legacy report context must drop stale return URLs');
assert.ok(read('Controllers/SavedViewsController.cs').includes('?? Url.Content("~/")'), 'Saved View fallback must honor the app base path');
assert.ok(repo.includes('OutboundReportSourceRoutes.Normalize(gridKey, request.SourceContext)'));
const portability = read('Services/Reporting/OutboundReportPortabilityService.cs');
assert.equal((portability.match(/OutboundReportSourceRoutes.NormalizeJson/g) || []).length, 3,
    'Report export, import and preflight should use the common route normalizer');
console.log('Report path storage, legacy route handling, current PathBase resolution, and import/export integration: PASS');
