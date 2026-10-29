const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

const account = read('Controllers/AccountController.cs');
const lifecycle = read('Services/Lifecycle/MauiLifecyclePaths.cs');
const preferences = read('Models/UserPreferencesModels.cs');
const base = 'MauiLifecyclePaths.BrowserUrl(Request.PathBase, UserPreferenceStartPages.ResolvePath(';
assert.match(account, /else if \(_applicationSessionService\.HasLiveMoveItSession\)[\s\S]*?return Redirect\(MauiLifecyclePaths\.BrowserUrl\(Request\.PathBase, UserPreferenceStartPages\.ResolvePath\(null\)\)\);/);
assert.ok(account.includes(`${base}preferences.StartPage))`), 'Login POST must use the current PathBase and stored start page');
assert.ok(account.includes('return Redirect(destination);'), 'Post-login navigation must return the explicit destination');
assert.ok(!account.includes('return RedirectToAction(startRoute.Action, startRoute.Controller);'), 'Do not regress to implicit post-login route');
assert.match(lifecycle, /public static string BrowserUrl\(PathString pathBase, string applicationRoute\)/);
assert.match(lifecycle, /pathBase\.Add\(new PathString\(applicationRoute\)\)\.ToUriComponent\(\)/);
assert.match(lifecycle, /applicationRoute\.StartsWith\("\/\/", StringComparison\.Ordinal\)/, 'Reject external protocol-relative routes');
assert.match(preferences, /public static string ResolvePath\(string\? value\)/);
assert.match(preferences, /var route = ResolveRoute\(value\)/, 'Only allowlisted preference routes may be used');
const routes = read('Program.cs');
assert.match(routes, /pattern: "\{controller=Account\}\/\{action=Login\}\/\{id\?\}"/, 'The application root resolves to Account/Login, not Dashboard');
const iis = read('MoveItAutomationUI.Tests/Browser/iis-application-path-smoke.cjs');
assert.match(iis, /Records\/TaskRun/, 'Retain Dashboard path regression coverage');
assert.match(iis, /Help\/About.cshtml/, 'Retain About logo PathBase regression coverage');
console.log('RC2 login post-authentication and existing virtual-app navigation source contracts: PASS');

// Run the production form behavior: normalize on change and on native POST, never touch the password.
const vm = require('node:vm');
const handlers = new Map();
const classes = { toggle() {} };
const username = { value: ' DOMAIN/Username ', setAttribute() {}, addEventListener(name, fn) { handlers.set(`username:${name}`, fn); } };
const password = { value: ' P/a\\ss word ', setAttribute() {} };
const submit = { classList: classes };
const label = {};
const form = {
    classList: classes, setAttribute() {},
    querySelector(selector) {
        return selector === 'input[name="username"]' ? username
            : selector === '[data-login-submit]' ? submit
            : selector === '[data-login-button-label]' ? label : null;
    },
    querySelectorAll() { return [username, password]; },
    addEventListener(name, fn) { handlers.set(name, fn); }
};
vm.runInNewContext(read('wwwroot/js/login.js'), {
    document: { querySelector() { return form; }, activeElement: null }, HTMLElement: class {},
    window: { addEventListener(name, fn) { handlers.set(`window:${name}`, fn); } }
});
handlers.get('username:change')();
assert.equal(username.value, 'DOMAIN\\Username');
username.value = ' domain/USER ';
let prevented = false;
handlers.get('submit')({ preventDefault() { prevented = true; } });
assert.equal(username.value, 'domain\\USER');
assert.equal(password.value, ' P/a\\ss word ');
assert.equal(prevented, false, 'The first submission remains a native POST');
assert.equal(submit.disabled, true);
handlers.get('submit')({ preventDefault() { prevented = true; } });
assert.equal(prevented, true, 'Repeated submit remains blocked');
handlers.get('window:pageshow')();
assert.equal(submit.disabled, false);
assert.equal(username.readOnly, false);
console.log('Login username separators, native POST, unchanged passwords and submission recovery: PASS');
