'use strict';
// Exercise the actual editor state/payload and fingerprint functions without a browser dependency.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'wwwroot/js/editors/host-editor.js'), 'utf8');
function between(start, end) {
    const first = source.indexOf(start), last = source.indexOf(end, first + start.length);
    assert.ok(first >= 0 && last > first, `Editor function not found: ${start}`);
    return source.slice(first, last);
}
const normalize = source.match(/const normalizeBoolean = value => \{[\s\S]*?\n        \};/);
assert.ok(normalize);
const functions = normalize[0] + '\n' + [
    between('function initializeConfigurationState(', 'function renderConfiguration('),
    between('function isConfigurationFieldVisible(', 'function createConfigurationField('),
    between('async function retrieveHostKey(', 'function updateConfigurationValue('),
    between('function updateConfigurationValue(', 'function renderTasks('),
    between('function collectPayload(', 'function stablePayload(')
].join('\n');
const fields = [
    { key: 'UseOAuth', valueType: 'Boolean', value: 'No', editable: true },
    { key: 'DefUsername', valueType: 'Text', value: 'account', editable: true, conditionKey: 'UseOAuth', conditionValue: 'false' },
    { key: 'DefPassword', valueType: 'Secret', value: 'Configured (suppressed)', editable: true, conditionKey: 'UseOAuth', conditionValue: 'false' },
    { key: 'OAuthClientID', valueType: 'Text', value: 'client', editable: true, conditionKey: 'UseOAuth', conditionValue: 'true' },
    { key: 'OAuthClientSecret', valueType: 'SecretMultiline', value: 'Configured (suppressed)', editable: true, conditionKey: 'UseOAuth', conditionValue: 'true' },
    { key: 'ProxyType', valueType: 'Select', value: 'None', editable: true },
    { key: 'ProxyPassword', valueType: 'Secret', value: 'Configured (suppressed)', editable: true, conditionKey: 'ProxyType', conditionValue: 'active' },
    { key: 'DefClientKeyID', valueType: 'KeySelect', value: '0', editable: true },
    { key: 'SSHFingerprint', valueType: 'HostFingerprint', value: '', editable: true },
    { key: 'IgnoreCertProbs', valueType: 'Boolean', value: 'Yes', editable: true },
    { key: 'Host', valueType: 'Text', value: 'sftp.example.test', editable: true },
    { key: 'Port', valueType: 'Number', value: '22', editable: true },
    { key: 'KexAlgs', valueType: 'Multiline', value: 'gss-test+value==', editable: true },
    { key: 'ProxyHost', valueType: 'Text', value: '', editable: true },
    { key: 'ProxyPort', valueType: 'Number', value: '', editable: true },
    { key: 'ProxyUsername', valueType: 'Text', value: '', editable: true }
];
const model = { host: { id: '42', name: 'Host', description: 'Old', configurationSections: [{ fields }] } };
const name = { value: 'Host' }, description = { value: 'Old' };
let error = '', payload, resolveResponse;
const context = vm.createContext({
    current: model, checkout: {id:'host-test-lease'}, configurationState: {}, configurationBaseline: {}, configurationDefinitions: {}, ftpNonImplicitPort: '21',
    stateCacheKeys:['UseDefStateCaching','StateCacheTime','StateCacheUnit'], stateCacheDuration:'1', stateCacheUnit:'Minutes',
    informationFields: { querySelector: selector => selector.includes('description') ? description : name },
    metadata: { querySelectorAll: () => [] },
    saving: false, partialSave: false, fingerprintBusy: false, fingerprintSequence: 0, loadSequence: 1,
    fingerprintUrl: '/maui/HostEditor/Fingerprint', antiForgeryToken: 'token', activeConfigurationSection: 'authentication',
    loginUrl: '/maui/Account/SessionExpired', window: { location: { replace() {} } },
    hideError() { error = ''; }, showError(message) { error = message; }, updateDirtyState() {}, showConfigurationSection() {},
    fetch: async (_url, options) => {
        payload = JSON.parse(options.body);
        assert.equal(options.headers.RequestVerificationToken, 'token');
        assert.equal(options.headers['X-MAUI-Edit-Lease'], 'host-test-lease');
        return await new Promise(resolve => { resolveResponse = resolve; });
    }
});
vm.runInContext(functions, context);
const configuration = () => JSON.parse(JSON.stringify(context.collectPayload().configuration));
context.initializeConfigurationState(model);
assert.deepEqual(configuration(), {}, 'Suppressed stored secrets do not enter a save payload');
context.configurationState.DefPassword = '  replacement  ';
assert.equal(configuration().DefPassword, '  replacement  ');
context.configurationState.UseOAuth = true;
assert.ok(!('DefPassword' in configuration()), 'Inactive HMAC/password fields are preserved during an OAuth switch');
const privateKey = '  -----BEGIN PRIVATE KEY-----\r\nDUMMY\r\n-----END PRIVATE KEY-----  ';
context.configurationState.OAuthClientSecret = privateKey;
assert.equal(configuration().OAuthClientSecret, privateKey);
context.configurationState.OAuthClientSecret = '';
assert.ok(!('OAuthClientSecret' in configuration()), 'Blank replacement preserves the stored secret');
context.configurationState.ProxyPassword = 'replacement proxy secret';
assert.ok(!('ProxyPassword' in configuration()), 'Inactive proxy changes cannot overwrite stored credentials');
context.configurationState.ProxyType = 'SOCKS5';
assert.equal(configuration().ProxyPassword, 'replacement proxy secret');
context.configurationState.DefClientKeyID = '123';
assert.equal(configuration().DefClientKeyID, '123');
context.configurationState.DefClientKeyID = '0';
assert.ok(!('DefClientKeyID' in configuration()), 'Returning to None removes an unchanged key patch');

// Exercise the production mode handler, including a saved implicit readback and custom port restoration.
const ftpModel = (secure, port) => ({ host: { id:'42', name:'Host', typeCode:'FTP', configurationSections:[{fields:[
    {key:'Secure',valueType:'Select',value:secure,editable:true}, {key:'Port',valueType:'Number',value:String(port),editable:true}
]}] } });
for (const port of ['21','2121','990']) {
    context.current=ftpModel('on',port); context.initializeConfigurationState(context.current);
    context.updateConfigurationValue('Secure','select',{value:'implicit'});
    assert.equal(context.configurationState.Port,'990');
    assert.equal(configuration().Secure,'implicit');
    if(port!=='990')assert.equal(configuration().Port,'990');
    context.current=ftpModel('implicit-ccc','990'); context.initializeConfigurationState(context.current);
    assert.equal(context.ftpNonImplicitPort,port,'saved implicit readback retains the previous explicit/plain port');
    for (const secure of ['none','on','tls-c']) {
        context.updateConfigurationValue('Secure','select',{value:secure});
        assert.equal(context.configurationState.Port,port);
        context.updateConfigurationValue('Secure','select',{value:'implicit'});
        assert.equal(context.configurationState.Port,'990');
    }
}
context.ftpNonImplicitPort='21'; context.current=ftpModel('implicit-ccc','990'); context.initializeConfigurationState(context.current);
assert.deepEqual(configuration(),{},'opening an implicit Host does not rewrite its saved settings');
context.updateConfigurationValue('Secure','select',{value:'on'});
assert.equal(context.configurationState.Port,'21','an already-implicit Host falls back to the standard explicit/plain port');
const s3Fields = [
    {key:'IsNonAmazonEndPoint',valueType:'Boolean',value:'No',editable:true},
    {key:'S3AlternativeEndpoint',valueType:'Text',value:'www.someurl.com',editable:true,conditionKey:'IsNonAmazonEndPoint',conditionValue:'true'}
];
context.current={host:{id:'42',name:'Host',typeCode:'S3',configurationSections:[{fields:s3Fields}]}};
context.initializeConfigurationState(context.current);
assert.equal(context.isConfigurationFieldVisible(s3Fields[1]),false);
context.updateConfigurationValue('IsNonAmazonEndPoint','boolean',{value:'true'});
assert.equal(context.isConfigurationFieldVisible(s3Fields[1]),true);
context.updateConfigurationValue('S3AlternativeEndpoint','text',{value:'https://storage.example.test:9443'});
assert.deepEqual(configuration(),{IsNonAmazonEndPoint:true,S3AlternativeEndpoint:'https://storage.example.test:9443'});
context.updateConfigurationValue('IsNonAmazonEndPoint','boolean',{value:'false'});
assert.equal(context.isConfigurationFieldVisible(s3Fields[1]),false);
assert.deepEqual(configuration(),{},'An inactive URL never enters the save patch');
context.updateConfigurationValue('IsNonAmazonEndPoint','boolean',{value:'true'});
assert.equal(configuration().S3AlternativeEndpoint,'https://storage.example.test:9443','Provider toggling retains unsaved URL text');
context.current=model; context.initializeConfigurationState(model);

// MOVEit 2026 cloud authentication reuses the production state/payload functions.
const iamFields = [
    {key:'AuthMethod',valueType:'Select',value:'0',editable:true},
    {key:'DefUsername',valueType:'Text',value:'access-key',editable:true,conditionKey:'AuthMethod',conditionValue:'0'},
    {key:'DefPassword',valueType:'Secret',value:'suppressed',editable:true,conditionKey:'AuthMethod',conditionValue:'0'},
    {key:'S3ProfileName',valueType:'Text',value:'',editable:true,conditionKey:'AuthMethod',conditionValue:'1'},
    {key:'IsNonAmazonEndPoint',valueType:'Boolean',value:'Yes',editable:true,conditionKey:'AuthMethod',conditionValue:'0'},
    {key:'S3AlternativeEndpoint',valueType:'Text',value:'storage.test',editable:true,conditionKey:'IsNonAmazonEndPoint',conditionValue:'true'}
];
context.current={host:{id:'42',name:'Host',typeCode:'S3',configurationSections:[{fields:iamFields}]}};
context.initializeConfigurationState(context.current);
context.updateConfigurationValue('AuthMethod','select',{value:'1'});
assert.equal(context.isConfigurationFieldVisible(iamFields[1]),false);
assert.equal(context.isConfigurationFieldVisible(iamFields[3]),true);
assert.equal(context.isConfigurationFieldVisible(iamFields[5]),false,'IAM modes hide alternate provider URL');
assert.deepEqual(configuration(),{AuthMethod:'1',S3ProfileName:''},'Default AWS profile is explicitly represented');
context.updateConfigurationValue('S3ProfileName','text',{value:'production'});
assert.equal(configuration().S3ProfileName,'production');
context.updateConfigurationValue('AuthMethod','select',{value:'2'});
assert.deepEqual(configuration(),{AuthMethod:'2'},'IMDS sends no access keys, profile name or alternate provider');
const driveFields = [
    {key:'OneDriveAuthenticationMode',valueType:'Select',value:'certificate',editable:true},
    {key:'OAuthClientSecret',valueType:'Secret',value:'suppressed',editable:true,conditionKey:'OneDriveAuthenticationMode',conditionValue:'client-secret'},
    {key:'DefClientCertSHA1',valueType:'Certificate',value:'AA BB',editable:true,conditionKey:'OneDriveAuthenticationMode',conditionValue:'certificate'}
];
context.current={host:{id:'42',name:'Host',typeCode:'OneDrive',configurationSections:[{fields:driveFields}]}};
context.initializeConfigurationState(context.current);
context.updateConfigurationValue('OneDriveAuthenticationMode','select',{value:'client-secret'});
context.updateConfigurationValue('OAuthClientSecret','secret',{value:'  replacement  '});
assert.equal(context.isConfigurationFieldVisible(driveFields[2]),false);
assert.deepEqual(configuration(),{OneDriveAuthenticationMode:'client-secret',OAuthClientSecret:'  replacement  '});
context.current=model; context.initializeConfigurationState(model);

(async () => {
    context.configurationState.ProxyType = 'None';
    const request = context.retrieveHostKey({});
    await Promise.resolve();
    assert.equal(payload.hostId, '42');
    assert.equal(payload.kexAlgs, 'gss-test+value==');
    assert.ok(!Object.keys(payload).some(key => /password|secret/i.test(key)));
    resolveResponse({ ok: true, status: 200, json: async () => ({ fingerprint: 'RSA-SHA2-256: DUMMY' }) });
    await request;
    assert.equal(error, '');
    assert.equal(context.configurationState.SSHFingerprint, 'RSA-SHA2-256: DUMMY');
    assert.equal(context.configurationState.IgnoreCertProbs, false);
    assert.equal(context.fingerprintBusy, false);
    const oldRequest = context.retrieveHostKey({});
    await Promise.resolve();
    context.loadSequence++;
    context.current = { host: { id: '99', name: 'Next Host', configurationSections: [{ fields }] } };
    context.initializeConfigurationState(context.current);
    resolveResponse({ ok: true, status: 200, json: async () => ({ fingerprint: 'OLD_HOST_KEY' }) });
    await oldRequest;
    assert.equal(context.configurationState.SSHFingerprint, '', 'An old response cannot change the next Host');
    const readonlyFields = fields.map(field => ({ ...field, editable: false, value: field.key === 'UseOAuth' ? 'Yes' : field.value }));
    context.initializeConfigurationState({ host: { configurationSections: [{ fields: readonlyFields }] } });
    assert.equal(context.isConfigurationFieldVisible(readonlyFields.find(field => field.key === 'OAuthClientID')), true);
    assert.equal(context.isConfigurationFieldVisible(readonlyFields.find(field => field.key === 'DefPassword')), false);
    assert.deepEqual(configuration(), {}, 'Read-only mode values cannot form a save patch');
    console.log('Host editor captured-profile state and fingerprint regression: PASS');
})().catch(error => { console.error(error); process.exitCode = 1; });
