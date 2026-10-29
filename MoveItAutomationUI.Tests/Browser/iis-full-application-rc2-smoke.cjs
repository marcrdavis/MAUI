// RC2 PathBase source regression: Records back-navigation, Reports return links,
// browser links, assets, and the shared IIS application-relative URL helper.
// Runtime/CLR behavior is covered separately by RecordsReturnUrlIisTests and IIS tests.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
function walk(dir, extensions) {
    return fs.readdirSync(path.join(root, dir), { withFileTypes: true }).flatMap(entry => {
        const relative = path.join(dir, entry.name);
        return entry.isDirectory() ? walk(relative, extensions) : entry.isFile() && extensions.some(ext => entry.name.endsWith(ext)) ? [relative] : [];
    });
}
const jsFiles = walk('wwwroot/js', ['.js']);
const viewFiles = walk('Views', ['.cshtml']);
const cssFiles = walk('wwwroot/css', ['.css']);
const controllerFiles = walk('Controllers', ['.cs']);

// Direct, literal website-root browser destinations bypass an IIS application PathBase.
for (const file of viewFiles) {
    assert.doesNotMatch(read(file), /\b(?:href|src|action)\s*=\s*['"]\/(?!\/)/i, `${file}: literal site-root browser URL`);
}
for (const file of jsFiles) {
    const source = read(file);
    assert.doesNotMatch(source, /(?:\.href\s*=|\.src\s*=|\bfetch\s*\(|\blocation\.(?:replace|assign)\s*\()\s*['"`]\/(?!\/)/, `${file}: literal root navigation`);
}
for (const file of cssFiles) {
    assert.doesNotMatch(read(file), /url\(\s*['"]?\/(?!\/)/i, `${file}: literal site-root CSS asset`);
}
for (const file of controllerFiles) {
    assert.doesNotMatch(read(file), /\b(?:Redirect|LocalRedirect|Response\.Redirect)\(\s*['"]\/(?!\/)/, `${file}: hardcoded root redirect`);
}

// Execute the actual helper declared in the layout against virtual/nested applications.
const layout = read('Views/Shared/_Layout.cshtml');
const inlineScript = layout.match(/<script>([\s\S]*?window\.mauiAppUrl[\s\S]*?)<\/script>/)?.[1];
assert.ok(inlineScript, 'Shared layout must define the application-relative resolver');
for (const [base, route, expected] of [
    ['/', '/Records/TaskRun?taskId=7', '/Records/TaskRun?taskId=7'],
    ['/MAUI/', '/Records/TaskRun?taskId=7', '/MAUI/Records/TaskRun?taskId=7'],
    ['/Apps/QA/', '/Resources/Scripts', '/Apps/QA/Resources/Scripts'],
    ['/Tasks/', '/Tasks/Index', '/Tasks/Tasks/Index'],
    ['/Records/', '/Records/Audit', '/Records/Records/Audit']
]) {
    const context = { window: {}, document: { documentElement: { dataset: { appBaseUrl: base } } } };
    vm.runInNewContext(inlineScript, context);
    assert.equal(context.window.mauiAppUrl(route), expected);
}
const records = read('Controllers/RecordsController.cs');
assert.match(records, /private string NormalizeReturnUrl\(string\? value\)/);
assert.match(records, /MauiApplicationReturnUrls\.IsWithinApplication\(candidate, Request\.PathBase\)/);
const boundary = read('Services/Lifecycle/MauiApplicationReturnUrls.cs');
assert.match(boundary, /var appBase = pathBase\.Value!/);
assert.match(boundary, /path\.Equals\(appBase, StringComparison\.OrdinalIgnoreCase\)/);
assert.match(boundary, /path\.StartsWith\(appBase \+ "\/", StringComparison\.OrdinalIgnoreCase\)/);
assert.match(boundary, /Uri\.UnescapeDataString\(segment\)/);
assert.match(read('Views/Shared/_ReportBackLink.cshtml'), /href="@Model\.ReturnUrl"/);
const reports = read('Controllers/ReportsController.cs');
assert.match(reports, /MauiApplicationReturnUrls\.IsWithinApplication\(returnUrl, Request\.PathBase\)/);
assert.match(layout, /MauiApplicationReturnUrls\.IsWithinApplication\(requestedReportsReturnUrl, Context\.Request\.PathBase\)/);
assert.match(read('Services/Reporting/OutboundReportSourceRoutes.cs'), /Path = allowedRoutes\[0\]/);
assert.match(reports, /Url\.Content\("~" \+ source\.Path\)/);
assert.match(layout, /data-app-base-url="@Url\.Content\("~\/"\)"/);
assert.match(read('wwwroot/js/dashboard.js'), /window\.mauiAppUrl\('\/Records\/TaskRun'\)/);
assert.match(read('Views/Help/About.cshtml'), /Url\.Content\("~" \+ Model\.ApplicationLogo\)/);
assert.match(read('Controllers/AccountController.cs'), /MauiLifecyclePaths\.BrowserUrl\(Request\.PathBase, UserPreferenceStartPages\.ResolvePath\(preferences\.StartPage\)\)/);
console.log(`PASS RC2 full IIS PathBase source contract (${jsFiles.length} JS, ${viewFiles.length} Razor, ${cssFiles.length} CSS, ${controllerFiles.length} controllers)`);
