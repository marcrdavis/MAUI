// Exercise production Metadata row markup, configuration.js and save/discard flow in Edge/Chromium.
// All Configuration writes stay in this local fixture; no MOVEit or installed MAUI endpoints are used.
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const http = require('node:http');
const { spawn } = require('node:child_process');
const stopBrowser = require('./rename-browser-cleanup.cjs');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const source = read('wwwroot/js/configuration.js');
const settings = read('Views/Configuration/Settings.cshtml');
const partial = read('Views/Configuration/_MetadataSettings.cshtml');
const template = settings.match(/<template data-metadata-field-template>([\s\S]*?)<\/template>/)?.[1];
assert.ok(template, 'Production new-field template exists');
for (const markup of [template, partial]) {
    assert.match(markup, /data-metadata-up[^>]+aria-label=/);
    assert.match(markup, /data-metadata-down[^>]+aria-label=/);
}
const initial = JSON.parse(read('appsettings.template.json')).Metadata;
let saved = Object.entries(initial).map(([key, definition]) => ({ key, fields: definition.Fields.map(field => ({
    name: field.Name, label: field.Label, description: field.Description,
    type: field.Type, possibleValues: field.PossibleValues || [], required: Boolean(field.Required)
})) }));
// Include a resource with no fields to cover the first row and single-row boundaries.
saved.find(resource => resource.key === 'KeysAndCertificates').fields = [];
const defaults = { keepAliveIntervalMinutes: 5, requestTimeoutSeconds: 30, importRowLimit: 500,
    taskRunHistoryMaxCount: 10000, recycleBinRetentionDays: 30, logRetentionDays: 30,
    jobHistoryRetentionDays: 30, systemMessageType: 'Information', backupFrequencyHours: 24,
    backupRetentionDays: 7, databaseConnectionString: 'Data Source=fixture-only.db' };
const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
const scriptJson = value => JSON.stringify(value).replaceAll('<', '\\u003c');
function page() {
    return `<!doctype html><html data-theme="light"><head><meta charset="utf-8"><style>${read('wwwroot/css/site.css')}</style></head><body>
    <main class="configuration-workspace" data-configuration-workspace data-save-url="/maui/Configuration/SaveSettings">
    <aside class="configuration-sidebar"><h2>Settings</h2><button type="button" data-config-section-target="metadata">Metadata</button></aside>
    <div class="configuration-content"><section class="configuration-section active" data-config-section="metadata">
    <div class="configuration-section-heading"><div><h2>Metadata</h2><p>Define MAUI-only metadata fields.</p></div><span class="configuration-state-badge live">Live Settings</span></div>
    <div class="panel-card configuration-card"><div class="configuration-card-heading row-heading"><div><h3>Metadata Fields</h3><p>Use the arrows to change display order, then Save Changes.</p></div>
    <select class="text-input configuration-resource-select" data-metadata-resource-select>${saved.map(resource => `<option>${resource.key}</option>`).join('')}</select></div>
    ${saved.map(resource => `<div class="configuration-resource-panel" data-metadata-resource="${resource.key}" data-metadata-allowed-types="String,MultilineString,Email,List,Boolean,Numeric,Date"><div class="configuration-metadata-fields" data-metadata-fields></div><button type="button" class="secondary-button configuration-add-field" data-add-metadata-field>+ Add Metadata Field</button></div>`).join('')}
    </div></section><footer class="configuration-save-bar"><span data-configuration-save-state>No unsaved changes</span><button type="button" class="secondary-button" data-config-discard>Discard Changes</button><button type="button" class="primary-button" data-config-save>Save Changes</button></footer></div>
    <div hidden>${Object.entries(defaults).map(([key, value]) => `<input data-setting="${key}" value="${escape(value)}">`).join('')}
    <div data-server-list><div data-server-row>${Object.entries({key:'qa',environmentKey:'qa',displayName:'QA',webAdminUrl:'https://example.invalid'}).map(([key,value])=>`<input data-server-field="${key}" value="${value}">`).join('')}</div></div>
    <div data-security-user-list><div data-security-user data-security-original="true" data-security-username="admin"><input data-security-field="username" value="admin"><select data-security-field="role"><option>Admin</option></select></div></div></div></main>
    <template data-metadata-field-template>${template}</template><div data-configuration-toast></div>
    <script>
    window.errors=[];window.requests=[];window.mode='success';window.hold=false;window.release=null;
    addEventListener('error',event=>errors.push(event.message));addEventListener('unhandledrejection',event=>errors.push(String(event.reason)));
    window.rows=key=>[...document.querySelector('[data-metadata-resource="'+key+'"]').querySelectorAll('[data-metadata-row]')];
    window.names=key=>rows(key).map(row=>row.querySelector('[data-metadata-field="name"]').value);
    window.field=(key,name)=>rows(key).find(row=>row.querySelector('[data-metadata-field="name"]').value===name);
    window.move=(key,name,direction)=>field(key,name).querySelector('[data-metadata-'+direction+']').click();
    window.select=key=>{const input=document.querySelector('[data-metadata-resource-select]');input.value=key;input.dispatchEvent(new Event('change',{bubbles:true}));};
    for(const resource of ${scriptJson(saved)}) for(const definition of resource.fields){
        const row=document.querySelector('[data-metadata-field-template]').content.firstElementChild.cloneNode(true);
        row.dataset.metadataPersisted='true';
        for(const [key,value]of Object.entries(definition)){const control=row.querySelector('[data-metadata-field="'+key+'"]');if(key==='required')control.checked=value;else if(key==='type'&&value==='Tags')control.add(new Option('Tags','Tags'));if(key!=='required')control.value=Array.isArray(value)?value.join(', '):value;}
        row.querySelector('[data-metadata-field="name"]').readOnly=true;
        if(definition.type==='Tags'){row.dataset.metadataSystemManaged='true';row.querySelector('[data-metadata-field="name"]').previousElementSibling.insertAdjacentHTML('beforeend',' <em class="configuration-metadata-managed-label">MAUI Managed</em>');}
        document.querySelector('[data-metadata-resource="'+resource.key+'"]').querySelector('[data-metadata-fields]').appendChild(row);
    }
    const originalFetch=window.fetch.bind(window);window.fetch=async(url,options)=>{
        requests.push({url,body:JSON.parse(options.body)});if(hold)await new Promise(resolve=>release=resolve);
        if(mode==='failure')return new Response(JSON.stringify({message:'Fixture save failed'}),{status:503,headers:{'Content-Type':'application/json'}});
        return originalFetch(url,options);
    };
    </script><script>${source.replace(/<\/script/gi, '<\\/script')}</script><script>window.ready=true;</script></body></html>`;
}
const candidates = [process.env.MAUI_BROWSER_EXECUTABLE,
    process.env['ProgramFiles(x86)'] && path.join(process.env['ProgramFiles(x86)'], 'Microsoft/Edge/Application/msedge.exe'),
    process.env.ProgramFiles && path.join(process.env.ProgramFiles, 'Microsoft/Edge/Application/msedge.exe'), '/usr/bin/chromium'];
const executable = candidates.find(file => file && fs.existsSync(file));
for (const [, script] of page().matchAll(/<script>([\s\S]*?)<\/script>/g)) new (require('node:vm').Script)(script);
if (process.argv.includes('--syntax-only')) { console.log('PASS Metadata ordering browser fixture syntax'); process.exit(0); }
assert.ok(executable, 'Edge or Chromium is required');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'maui-metadata-order-'));
let browser, socket, server, requestId = 0;
const pending = new Map();
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function command(method, params = {}) {
    const id = ++requestId;
    const result = new Promise((resolve, reject) => {
        const timer = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 15000);
        pending.set(id, { resolve, reject, timer });
    });
    socket.send(JSON.stringify({ id, method, params }));
    return result;
}
async function evaluate(expression) {
    const result = await command('Runtime.evaluate', { expression: '(async()=>{' + expression + '})()', awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
async function until(expression) {
    for (let i = 0; i < 120; i++) { if (await evaluate('return ' + expression)) return; await pause(30); }
    throw new Error('Timed out: ' + expression + '; ' + JSON.stringify(await evaluate('return window.errors')));
}
async function check(expression, expected, label) { assert.deepEqual(await evaluate('return ' + expression), expected, label); }
(async () => {
    server = http.createServer((request, response) => {
        if (request.method === 'POST' && request.url === '/maui/Configuration/SaveSettings') {
            let body = ''; request.on('data', chunk => body += chunk);
            request.on('end', () => { saved = JSON.parse(body).metadataResources; response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ configurationRevision: 'saved-fixture', message: 'Configuration saved.' })); });
        } else { response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end(page()); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    browser = spawn(executable, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--no-sandbox', '--remote-debugging-port=0', '--remote-allow-origins=*', '--user-data-dir=' + profile, 'about:blank'], { stdio: 'ignore' });
    let launchError; browser.on('error', error => { launchError = error; });
    let port = '';
    for (let i = 0; i < 200 && !port; i++) {
        if (launchError) throw launchError;
        if (browser.exitCode !== null) throw new Error('Browser exited before startup');
        try { const value = fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split(/\r?\n/)[0]; if (/^\d+$/.test(value)) port = value; }
        catch (error) { if (!['ENOENT', 'EBUSY', 'EACCES'].includes(error.code)) throw error; }
        if (!port) await pause(100);
    }
    assert.ok(port, 'Browser startup timed out');
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    socket = new WebSocket(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    socket.addEventListener('message', event => { const message = JSON.parse(event.data), callback = pending.get(message.id); if (!callback) return; pending.delete(message.id); clearTimeout(callback.timer); message.error ? callback.reject(new Error(JSON.stringify(message.error))) : callback.resolve(message.result); });
    await command('Page.enable');
    await command('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/` });
    await until('window.ready');
    await check('errors', [], 'No initialization errors');
    const original = await evaluate('return Object.fromEntries([...document.querySelectorAll("[data-metadata-resource]")].map(panel=>[panel.dataset.metadataResource,names(panel.dataset.metadataResource)]))');
    await check('document.querySelector("[data-config-save]").disabled', true, 'Initial state is clean');
    for (const [key, fields] of Object.entries(original)) {
        if (fields.length < 2) continue;
        await evaluate(`select(${scriptJson(key)});move(${scriptJson(key)},${scriptJson(fields.at(-1))},'up');`);
        const expected = [...fields]; [expected[expected.length - 1], expected[expected.length - 2]] = [expected.at(-2), expected.at(-1)];
        await check(`names(${scriptJson(key)})`, expected, 'Move changes the selected resource order');
        for (const other of Object.keys(original).filter(value => value !== key)) await check(`names(${scriptJson(other)})`, original[other], 'Other resources retain their order');
        await evaluate(`move(${scriptJson(key)},${scriptJson(fields.at(-1))},'down');`);
        await check('document.querySelector("[data-config-save]").disabled', true, 'Returning to original order clears dirty state');
    }
    await evaluate("select('Tasks');move('Tasks','Tags','up');");
    await check("field('Tasks','Tags').querySelector('[data-metadata-up]').disabled", true, 'First Up is disabled');
    await check("document.activeElement===field('Tasks','Tags').querySelector('[data-metadata-down]')", true, 'Focus stays with the moved field at the boundary');
    await check("[...field('Tasks','Tags').querySelectorAll('[data-metadata-field], [data-remove-metadata-field]')].every(control=>control.disabled)", true, 'Managed definition and Remove remain locked');
    await check("field('Tasks','Tags').querySelector('[data-metadata-down]').disabled", false, 'Managed field can move');
    await evaluate("select('KeysAndCertificates');document.querySelector('[data-metadata-resource=KeysAndCertificates] [data-add-metadata-field]').click();");
    await check("[...rows('KeysAndCertificates')[0].querySelectorAll('[data-metadata-up], [data-metadata-down]')].every(control=>control.disabled)", true, 'Single new field has both arrows disabled');
    await evaluate("rows('KeysAndCertificates')[0].querySelector('[data-metadata-field=name]').value='First';document.querySelector('[data-metadata-resource=KeysAndCertificates] [data-add-metadata-field]').click();rows('KeysAndCertificates')[1].querySelector('[data-metadata-field=name]').value='Second';move('KeysAndCertificates','Second','up');");
    await check("names('KeysAndCertificates')", ['Second', 'First'], 'New fields reorder before their first save');
    await evaluate("field('KeysAndCertificates','First').querySelector('[data-remove-metadata-field]').click();");
    await check("[...rows('KeysAndCertificates')[0].querySelectorAll('[data-metadata-up], [data-metadata-down]')].every(control=>control.disabled)", true, 'Removing a neighbor updates boundaries');
    await evaluate("field('KeysAndCertificates','Second').querySelector('[data-remove-metadata-field]').click();select('Tasks');mode='failure';document.querySelector('[data-config-save]').click();");
    await until('requests.length===1 && !document.querySelector("[data-config-save]").disabled');
    await check("names('Tasks')[0]", 'Tags', 'Failed save preserves the pending order');
    await check("!field('Tasks','Reference').querySelector('[data-metadata-field=label]').disabled && !document.querySelector('[data-metadata-resource=Tasks] [data-add-metadata-field]').disabled", true, 'Failed save restores metadata editing and Add');
    await evaluate("mode='success';hold=true;document.querySelector('[data-config-save]').click();");
    await until('window.release!==null');
    await check("[...document.querySelectorAll('[data-metadata-up], [data-metadata-down]')].every(control=>control.disabled)", true, 'Arrows are locked during save');
    await check("[...document.querySelectorAll('[data-metadata-field], [data-remove-metadata-field], [data-add-metadata-field]')].every(control=>control.disabled)", true, 'Definitions and Add/Remove are locked until the saved baseline is established');
    await evaluate("field('Tasks','Tags').querySelector('[data-metadata-down]').dispatchEvent(new Event('click'));hold=false;release();");
    await until('document.querySelector("[data-config-save]").disabled && document.querySelector("[data-config-discard]").disabled && !document.querySelector(".configuration-save-bar").classList.contains("is-saving")');
    await check("requests.at(-1).url", '/maui/Configuration/SaveSettings', 'Uses the existing virtual-path save endpoint');
    assert.equal(saved.find(resource => resource.key === 'Tasks').fields[0].name, 'Tags', 'Save persists the reordered list');
    assert.deepEqual(saved.find(resource => resource.key === 'Tasks').fields.find(field => field.name === 'Tags'), {
        name: initial.Tasks.Fields.find(field => field.Name === 'Tags').Name,
        label: initial.Tasks.Fields.find(field => field.Name === 'Tags').Label,
        description: initial.Tasks.Fields.find(field => field.Name === 'Tags').Description,
        type: 'Tags', possibleValues: [], required: false
    }, 'Tags definition is unchanged');
    await evaluate("move('Tasks','Tags','down');window.ready=false;document.querySelector('[data-config-discard]').click();");
    await until('window.ready');
    await check("names('Tasks')[0]", 'Tags', 'Discard restores the last saved order');
    await check('document.querySelector("[data-config-save]").disabled', true, 'Reload is clean');
    await check("field('Tasks','Reference').querySelector('[data-metadata-field=label]').disabled", false, 'Editable fields are restored after save');
    await check("[...field('Tasks','Tags').querySelectorAll('[data-metadata-field], [data-remove-metadata-field]')].every(control=>control.disabled)", true, 'Managed fields remain protected after save');
    const output = path.join(root, 'TestResults/MetadataOrdering'); fs.mkdirSync(output, { recursive: true });
    for (const theme of ['light', 'dark']) for (const width of [1400, 1000, 600]) {
        await command('Emulation.setDeviceMetricsOverride', { width, height: 950, deviceScaleFactor: 1, mobile: false });
        await evaluate(`document.documentElement.dataset.theme='${theme}';select('Tasks');`);
        await pause(200); // Capture the theme after the normal button-color transitions finish.
        await check("rows('Tasks').every(row=>row.scrollWidth<=row.clientWidth)", true, 'Metadata controls fit the existing responsive row');
        await check("[...document.querySelectorAll('[data-metadata-resource=Tasks] .configuration-metadata-order-button')].every(button=>{const box=button.getBoundingClientRect(),style=getComputedStyle(button),icon=button.querySelector('svg');return box.width>=40&&box.height===36&&style.borderTopStyle==='solid'&&parseFloat(style.borderTopWidth)>=1&&icon.getBoundingClientRect().width===22;})", true, 'Reorder controls are full-size outlined buttons with visible SVG arrows in both themes');
        const screenshot = await command('Page.captureScreenshot', { format: 'png' });
        fs.writeFileSync(path.join(output, `${theme}-${width}.png`), Buffer.from(screenshot.data, 'base64'));
    }
    await check('errors', [], 'No browser errors during editing, save, discard or responsive rendering');
    console.log('PASS Metadata resource isolation, boundaries, managed Tags, focus, new/remove, save/failure/discard, definition preservation and light/dark responsive layout.');
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => {
    for (const callback of pending.values()) { clearTimeout(callback.timer); callback.reject(new Error('Browser closed')); } pending.clear();
    socket?.close(); stopBrowser(browser);
    if (server) { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
    try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); } catch { /* Browser profile cleanup only. */ }
});
