const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const layout = read('Views/Shared/_Layout.cshtml');
const inlineScript = layout.match(/<script>([\s\S]*?window\.mauiAppUrl[\s\S]*?)<\/script>/)?.[1];
assert.ok(inlineScript, 'Layout must expose application-path URL helper before page scripts');
assert.ok(layout.indexOf('window.mauiAppUrl') < layout.indexOf('src="~/js/native-editor.js"'));
assert.ok(layout.includes('data-app-base-url="@Url.Content("~/")"'));
for (const [base, expected] of [['/', '/Setup'], ['/MAUI/', '/MAUI/Setup'], ['/dev/MAUI/', '/dev/MAUI/Setup'], ['/Tasks/', '/Tasks/Setup'], ['/Records/', '/Records/Setup'], ['/dev/Records/', '/dev/Records/Setup']]) {
    const context = { window: {}, document: { documentElement: { dataset: { appBaseUrl: base } } } };
    vm.runInNewContext(inlineScript, context);
    const resolve = context.window.mauiAppUrl;
    assert.equal(resolve('/Setup'), expected);
    assert.equal(resolve('/Account/Login'), base === '/' ? '/Account/Login' : `${base.slice(0, -1)}/Account/Login`);
    assert.equal(resolve('/Records/TaskRun'), base === '/' ? '/Records/TaskRun' : `${base.slice(0, -1)}/Records/TaskRun`, 'An app named Records must not be confused with a route');
    assert.equal(resolve('/Tasks/Index'), base === '/' ? '/Tasks/Index' : `${base.slice(0, -1)}/Tasks/Index`, 'An app named Tasks must not be confused with a route');
    assert.equal(resolve('https://moveit.example.com/api/v1/tasks'), 'https://moveit.example.com/api/v1/tasks');
}
for (const name of ['InitialSetupAccessMiddleware.cs', 'UpgradeAccessMiddleware.cs']) {
    const source = read(`Services/Lifecycle/${name}`);
    assert.ok(source.includes('MauiLifecyclePaths.ForApplication(context.Request'), `${name} must use PathBase`);
    assert.ok(!/Response\.Redirect\(MauiLifecyclePaths\.(Login|Setup|Upgrade)\)/.test(source));
}
const dashboard = read('Views/Home/Index.cshtml');
assert.ok(!/href="\/(Tasks|Hosts)\//.test(dashboard));
assert.ok(read('wwwroot/js/dashboard.js').includes("window.mauiAppUrl('/Records/TaskRun')"));
assert.ok(!read('wwwroot/js/dashboard.js').includes('link.href = `/Records/'));
const help = read('Views/Shared/_NativePortabilityDialogs.cshtml');
assert.ok(!help.includes('For S3, Azure Blob, SharePoint, and Google Storage paths'));
for (const name of ['Views/Shared/_Layout.cshtml','Views/Account/Login.cshtml','Views/Upgrade/Index.cshtml','Views/Setup/Index.cshtml','Views/Help/About.cshtml']) {
    assert.ok(read(name).includes('Url.Content("~" +'), `${name}: root-relative logo must honor PathBase`);
}
const csproj = read('MoveItAutomationUI.csproj');
// The reserved native MOVEit popup must load its MAUI logo from this app, not the shared IIS site root.
const nativeEditor = read('wwwroot/js/native-editor.js');
assert.ok(!nativeEditor.includes("new URL('/images/automation-mark.svg', window.location.origin)"));
for (const [base, expectedSrc] of [
    ['/', 'https://iis.example.com/images/automation-mark.svg'],
    ['/MAUI/', 'https://iis.example.com/MAUI/images/automation-mark.svg'],
    ['/Records/', 'https://iis.example.com/Records/images/automation-mark.svg'],
    ['/dev/AutomationUI/', 'https://iis.example.com/dev/AutomationUI/images/automation-mark.svg']
]) {
    const elements = [];
    const doc = {
        title: '',
        body: { style: {}, replaceChildren() {}, appendChild(el) { elements.push(el); } },
        head: { appendChild(el) { elements.push(el); } },
        createElement(tag) {
            return { tagName: tag, style: {}, setAttribute() {}, addEventListener() {}, appendChild(el) { elements.push(el); } };
        }
    };
    const win = {
        location: { origin: 'https://iis.example.com' },
        screen: { availWidth: 1500, availHeight: 900 },
        outerWidth: 1500, outerHeight: 900,
        open() { return { document: doc }; },
        addEventListener() {}
    };
    const ctx = { window: win, document: { documentElement: { dataset: { appBaseUrl: base } } }, URL };
    vm.runInNewContext(inlineScript, ctx);
    vm.runInNewContext(nativeEditor, ctx);
    const popup = win.MauiNativeEditor.reserve();
    assert.ok(popup, 'Native editor popup should be reserved');
    const logo = elements.find(x => x.tagName === 'img');
    assert.equal(logo?.src, expectedSrc, 'Native editor reserved popup logo must honor PathBase');
}
const records = read('Controllers/RecordsController.cs');
for (const action of ['TaskRun', 'TaskRunBatch', 'FileActivity', 'FileActivityBatch', 'Audit', 'AuditBatch']) {
    assert.ok(records.includes(`Url.Action(nameof(${action})) ?? Url.Content("~/Records/${action}")`),
        `Records ${action} fallback must be IIS application-aware`);
}
const currentVersion = /<Version>([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)<\/Version>/.exec(csproj)?.[1];
assert.ok(currentVersion, 'Project must declare a version');
assert.ok(csproj.includes(`<AssemblyVersion>${currentVersion.split('-')[0]}.0</AssemblyVersion>`));
console.log('IIS root/virtual/nested/colliding application routing, lifecycle redirects, editor popup assets, Records fallback URLs, and help text: PASS');
