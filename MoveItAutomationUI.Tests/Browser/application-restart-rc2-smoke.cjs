// RC2 Application Restart portability and browser-flow checks (no actual shutdown).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

const view = read('Views/Configuration/Settings.cshtml');
const script = read('wwwroot/js/configuration-application-restart.js');
const controller = read('Controllers/MauiApplicationRestartController.cs');
const layout = read('Views/Shared/_Layout.cshtml');
const css = read('wwwroot/css/site.css');
const start = view.indexOf('data-application-restart-section>');
const reset = view.indexOf('data-factory-reset-section>');
assert.ok(start > 0 && reset > start, 'Restart panel must precede Factory Reset');
assert.match(view.slice(start, reset), /configuration-operation-body[\s\S]*configuration-operation-actions/);
assert.equal((view.match(/data-application-restart-open/g) || []).length, 2);
assert.equal((view.match(/data-application-restart-dialog/g) || []).length, 1);
assert.match(view, /data-application-restart-url="@Url.Action\("Restart", "MauiApplicationRestart"\)"/);
assert.match(view, /data-application-restart-status-url="@Url.Action\("Status", "MauiApplicationRestart"\)"/);
assert.match(view, /MauiLifecyclePaths\.BrowserUrl\(Context\.Request\.PathBase, MauiLifecyclePaths\.Login\)/);
assert.match(view, /configuration-application-restart\.js/);
assert.match(controller, /\[Authorize\(Policy = MauiSecurityPolicies\.Admin\)\]/);
assert.match(controller, /\[HttpPost\][\s\S]*?\[ValidateAntiForgeryToken\]/);
assert.match(controller, /\[AllowAnonymous\][\s\S]*?\[HttpGet\]/);
assert.match(controller, /_restart\.ScheduleRestart\(/);
assert.match(layout, /GetRequiredService<MoveItAutomationUI\.Services\.Configuration\.ConfigurationStartupState>\(\)/);
assert.doesNotMatch(layout, /MoveItOptionsAccessor\.Value\.IdleSessionTimeoutMinutes\.Value/);
assert.match(css, /\.configuration-restart-actions\s*\{/);
for (const feature of ['Enable Quick Transfer', 'Import Row Limit', 'Use MAUI Task Editor', 'Factory Reset']) {
    assert.ok(view.includes(feature), `Missing RC2-specific setting: ${feature}`);
}

// Browser simulation: opening either button cannot directly schedule a restart;
// the confirmation sends a token and polls until a different process answers.
const events = new Map();
function clickable(id) {
    const callbacks = {};
    return { id, disabled: false, textContent: '', hidden: true,
        addEventListener(name, handler) { callbacks[name] = handler; },
        fire(name, event = {}) { assert.ok(callbacks[name], `${id}.${name} handler missing`); return callbacks[name](event); }
    };
}
const confirm = clickable('confirm');
const cancel = clickable('cancel');
const close = clickable('close');
const error = clickable('error');
const button1 = clickable('advanced');
const button2 = clickable('banner');
const workspace = {dataset: {
    applicationRestartUrl: '/maui/MauiApplicationRestart/Restart',
    applicationRestartStatusUrl: '/maui/MauiApplicationRestart/Status',
    applicationLoginUrl: '/maui/Account/Login'
}};
const dialog = {open:false,
    showModal() {this.open=true;},
    close() {this.open=false;},
    querySelector(selector) {
        return ({'[data-application-restart-confirm]':confirm,'[data-application-restart-cancel]':cancel,
                 '[data-application-restart-close]':close,'[data-application-restart-error]':error})[selector] ?? null;
    },
    addEventListener(name,cb) {events.set(name,cb);}
};
global.document = {
    querySelector(selector) {
        if (selector==='[data-configuration-workspace]') return workspace;
        if (selector==='[data-application-restart-dialog]') return dialog;
        if (selector==='.global-antiforgery input[name="__RequestVerificationToken"]') return {value:'test-token'};
        return null;
    },
    querySelectorAll(selector) {
        assert.equal(selector, '[data-application-restart-open]');return [button1,button2];
    }
};
let destination, postCount=0, probeCount=0;
global.window = {setTimeout(fn) {fn();},location: {assign(url) {destination=url;}}};
global.fetch = async (url,options) => {
    if(options.method==='POST') {
        postCount++;
        assert.equal(url, workspace.dataset.applicationRestartUrl);
        assert.equal(options.headers.RequestVerificationToken, 'test-token');
        assert.equal(options.credentials,'same-origin');
        return {ok:true, status:200, json: async()=>({accepted:true,instanceId:'old'})};
    }
    probeCount++;
    assert.equal(url, workspace.dataset.applicationRestartStatusUrl);
    assert.equal(options.credentials,'same-origin');
    assert.equal(options.cache,'no-store');
    return {ok:true,json:async()=>({instanceId:probeCount === 1 ? 'old':'new'})};
};
require(path.join(root, 'wwwroot/js/configuration-application-restart.js'));
button1.fire('click');
assert.equal(dialog.open,true);
assert.equal(postCount,0);
assert.equal(confirm.textContent,'Restart MAUI');
cancel.fire('click');
assert.equal(dialog.open,false);
button2.fire('click');
assert.equal(dialog.open,true);
(async()=>{
    await confirm.fire('click');
    assert.equal(postCount,1);
    assert.equal(probeCount,2);
    assert.equal(destination,'/maui/Account/Login');
    console.log('RC2 Restart section, shared confirmation, admin/CSRF, effective timeout and browser polling: PASS');
})().catch(error=>{console.error(error);process.exitCode=1;});
