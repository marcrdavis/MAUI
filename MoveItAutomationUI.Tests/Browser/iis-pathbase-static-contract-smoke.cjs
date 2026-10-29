/*
 * Source-level guard against MAUI site-root URLs escaping an IIS virtual application.
 * MOVEit REST URL literals are deliberately excluded: they are addressed to the
 * separate MOVEit Automation server, not the MAUI IIS application.
 */
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

function walk(directory, extension) {
    const result = [];
    for (const entry of fs.readdirSync(path.join(root, directory), { withFileTypes: true })) {
        const relative = path.join(directory, entry.name);
        if (entry.isDirectory()) result.push(...walk(relative, extension));
        else if (entry.isFile() && extension.some(ext => entry.name.endsWith(ext))) result.push(relative);
    }
    return result;
}

let inspected = 0;
for (const file of walk('wwwroot/js', ['.js'])) {
    const source = read(file);
    // Direct browser HTTP/navigation targets cannot use a literal site-root MAUI route.
    const patterns = [
        /\bfetch\(\s*['"`]\/(?!\/|api\/)/,
        /\bnew\s+URL\(\s*['"`]\/(?!\/|api\/)/,
        /\b(?:window\.)?location\.(?:assign|replace)\(\s*['"`]\/(?!\/)/,
        /\b(?:window\.)?location\.href\s*=\s*['"`]\/(?!\/)/
    ];
    for (const pattern of patterns) {
        assert.doesNotMatch(source, pattern, `${file}: use a Razor endpoint or mauiAppUrl for MAUI URLs`);
    }
    inspected++;
}
for (const file of walk('Views', ['.cshtml'])) {
    const source = read(file);
    assert.doesNotMatch(source, /\b(?:href|src|action)\s*=\s*['"]\/(?!\/)/i,
        `${file}: href/src/action must be emitted via tag helpers or Url.Content/Action`);
    inspected++;
}
for (const file of walk('Controllers', ['.cs'])) {
    const source = read(file);
    for (const pattern of [
        /\b(?:Response\.)?Redirect\(\s*"\/(?!\/)/,
        /\bLocalRedirect\(\s*"\/(?!\/)/,
        /\bUrl\.Action\([^;]*\)\s*\?\?\s*"\/"/
    ]) assert.doesNotMatch(source, pattern, `${file}: use path-base-aware routing for navigation`);
    inspected++;
}
const reports = read('Services/Reporting/OutboundReportSourceRoutes.cs');
assert.match(reports, /pair\.Key\.Equals\("returnUrl"/);
assert.match(reports, /pair\.Key\.Equals\("returnLabel"/);
const saved = read('Controllers/SavedViewsController.cs');
assert.match(saved, /\?\? Url\.Content\("~\/"\)/);
const grid = read('wwwroot/js/grid.js');
assert.match(grid, /normalizedKey === 'returnurl'/);
assert.match(grid, /normalizedKey === 'returnlabel'/);
console.log(`IIS PathBase static navigation checks: PASS (${inspected} JS/Razor/controller files inspected)`);
