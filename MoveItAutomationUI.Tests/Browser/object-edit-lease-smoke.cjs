'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const read = file => fs.readFileSync(path.resolve(__dirname, '../..', file), 'utf8');
function fixture(settings = {}) {
    const calls = [], intervals = new Map(), listeners = {}, errors = [], prompts = [];
    let sequence = 0, rejectHeartbeat = false, transientHeartbeat = 0, busy = false, pendingOpen = null;
    let pendingConfirmation = null, pendingOverride = null;
    const window = { setTimeout, clearTimeout, mauiConfirm: async options => { prompts.push(options); return pendingConfirmation || settings.confirm !== false; }, mauiAppUrl: value => '/maui' + value, setInterval(fn) { intervals.set(++sequence, fn); return sequence; },
        clearInterval(id) { intervals.delete(id); }, addEventListener(name, fn) { listeners[name] = fn; } };
    const context = { window, document: { documentElement: { dataset: {moveitAdmin:settings.admin?'true':'false'} } }, console, AbortController,
        fetch: async (url, options) => {
            const body = JSON.parse(options.body);
            calls.push({ url, options, body });
            assert.equal(options.headers.RequestVerificationToken, 'csrf');
            if (settings.throwAction && url.endsWith('/' + settings.throwAction)) throw new Error('Failed to fetch');
            if (settings.failStatus && url.endsWith('/Open')) return {ok:false,status:settings.failStatus,json:async()=>null};
            if (url.endsWith('/Open') && pendingOpen) return pendingOpen;
            if (url.endsWith('/Override') && pendingOverride) return pendingOverride;
            if (url.endsWith('/Open') && settings.lockOnOpen) return {ok:false,status:409,json:async()=>({message:'This object has been checked out to alice since 2026-10-24 10:30:00',checkoutConflict:true})};
            if (url.endsWith('/Override')) return {ok:true,json:async()=>settings.incomplete ? {success:true} : {success:true,leaseId:'recovered',editVersion:'fresh-revision'}};
            if (url.endsWith('/Heartbeat') && transientHeartbeat) {
                if (transientHeartbeat === -1) throw new Error('Connection interrupted');
                return {ok:false,status:transientHeartbeat,json:async()=>({message:'Temporarily unavailable'})};
            }
            const denied = url.endsWith('/Heartbeat') && rejectHeartbeat;
            return { ok: !denied, status: denied ? 409 : 200, json: async () => url.endsWith('/Open')
                ? { required: true, leaseId: 'editor-' + calls.length } : { success: !denied, message: 'Checkout expired.' } };
        } };
    vm.runInNewContext(read('wwwroot/js/editors/object-edit-lease.js'), context);
    const lease = window.MauiObjectEditLease.create({ type: settings.type || 'Task', token: 'csrf', onLost: value => errors.push(value), busy: () => busy });
    return { lease, calls, intervals, listeners, errors, prompts, api:window.MauiObjectEditLease, setBusy: value => busy = value,
        deferConfirmation: () => { let resolve; pendingConfirmation = new Promise(r=>resolve=r); return resolve; },
        deferOverride: () => { let resolve; pendingOverride = new Promise(r=>resolve=r); return resolve; },
        rejectHeartbeat: () => rejectHeartbeat = true, transientHeartbeat: value => transientHeartbeat = value, deferOpen: () => { let resolve; pendingOpen = new Promise(r => resolve = r); return resolve; } };
}
(async () => {
    for (const type of ['Task','Host','Script','DateList']) {
        const options = {type,admin:true,throwAction:'Open'}, retry = fixture(options);
        await assert.rejects(retry.lease.open('42',true,true), {message:'Unable to complete the MOVEit checkout request. Please try again.'});
        assert.equal(retry.prompts.length,0,'a failed network request is not a native checkout conflict');
        assert.equal(retry.calls.filter(c=>c.url.endsWith('/Override')).length,0);
        options.throwAction = '';
        assert.equal(await retry.lease.open('42',true,true),true,'retry must use normal Open');
        await retry.lease.close();
        for (const status of [408,429,503]) {
            const offline = fixture({type,admin:true,failStatus:status});
            await assert.rejects(offline.lease.open('42',true,true), {message:'Unable to complete the MOVEit checkout request. Please try again.'});
            assert.equal(offline.prompts.length,0); assert.equal(offline.calls.length,1);
        }
    }
    for (const type of ['Task','Host','Script','DateList']) {
        const ordinary=fixture({type,lockOnOpen:true});
        await assert.rejects(ordinary.lease.open('42',true,true),/checked out to alice since/);
        assert.equal(ordinary.prompts.length,0); assert.equal(ordinary.calls.length,1);
        const declined=fixture({type,lockOnOpen:true,admin:true,confirm:false});
        await assert.rejects(declined.lease.open('42',true,true),/checked out to alice/);
        assert.equal(declined.calls.length,1,'cancel must not acquire, release or override');
        const recovered=fixture({type,lockOnOpen:true,admin:true});
        assert.equal(await recovered.lease.open('42',true,true),true);
        assert.match(recovered.prompts[0].message,/alice since 2026-10-24 10:30:00/);
        assert.equal(recovered.calls.filter(c=>c.url.endsWith('/Override')).length,1);
        assert.equal(recovered.calls.filter(c=>c.url.endsWith('/Close')).length,0);
        await recovered.lease.close();
    }
    const stale=fixture({lockOnOpen:true,admin:true}), confirm=stale.deferConfirmation();
    const openingWithPrompt=stale.lease.open('42',true,true); await new Promise(setImmediate);
    await stale.lease.close(); confirm(true); assert.equal(await openingWithPrompt,false);
    assert.equal(stale.calls.filter(c=>c.url.endsWith('/Override')).length,0,'closing while confirming must not acquire');
    for (const type of ['MOVEit Settings','Resource Groups','Global Parameters']) {
        const error=Object.assign(new Error('Checked out to alice since 2026-10-24 10:30:00'),{payload:{checkoutConflict:true}});
        const denied=fixture(); assert.equal(await denied.api.recoverConflict(error,type,'csrf'),false); assert.equal(denied.calls.length,0);
        const cancelled=fixture({admin:true,confirm:false}); assert.equal(await cancelled.api.recoverConflict(error,type,'csrf'),false); assert.equal(cancelled.calls.length,0);
        const recovered=fixture({admin:true}); assert.equal(await recovered.api.recoverConflict(error,type,'csrf'),true);
        assert.equal(recovered.calls.length,1); assert.equal(recovered.calls[0].url,'/maui/ObjectEditLease/Recover');
        assert.equal(recovered.calls[0].body.type,type,'recovery must not resubmit a user mutation');
        const offline = fixture({admin:true,throwAction:'Recover'});
        await assert.rejects(offline.api.recoverConflict(error,type,'csrf'),{message:'Unable to override the checkout. Please try again.'});
        assert.equal(offline.calls.length,1,'do not repeat a failed override');
        assert.equal(recovered.api.restoredFormMessage,'Checkout restored. Close and reopen this form before continuing.');
    }
    for (const type of ['Task','Host','Script','DateList']) {
        const retry = fixture({type}); await retry.lease.open('42',true,true);
        for (const status of [-1,408,429,503]) {
            retry.transientHeartbeat(status); await retry.lease.heartbeat();
            assert.equal(retry.lease.lost,false,'Transient heartbeat failures cannot revoke an editor checkout');
            assert.equal(retry.intervals.size,1); assert.equal(retry.errors.length,0);
        }
        retry.transientHeartbeat(0); await retry.lease.heartbeat(); retry.lease.assert();
        retry.rejectHeartbeat(); await retry.lease.heartbeat();
        assert.equal(retry.lease.lost,true,'An authoritative expiry still blocks editing');
        await retry.lease.close();
    }
    const f = fixture();
    assert.equal(await f.lease.open('42', true, false), false);
    assert.equal(await f.lease.open('42', false, true), false);
    assert.equal(f.calls.length, 0, 'disabled setting and read-only objects never acquire');
    assert.equal(await f.lease.open('42', true, true), true);
    assert.equal(f.calls[0].url, '/maui/ObjectEditLease/Open');
    assert.equal(f.intervals.size, 1);
    f.setBusy(true); await f.lease.heartbeat();
    assert.equal(f.calls.length, 1, 'long save/run requests own their lease without a competing heartbeat');
    f.setBusy(false); await f.lease.heartbeat();
    assert.equal(f.calls.at(-1).body.leaseId, f.lease.id);
    f.rejectHeartbeat(); await f.lease.heartbeat();
    assert.equal(f.lease.lost, true);
    assert.equal(f.intervals.size, 0);
    assert.throws(() => f.lease.assert(), /Saving is blocked/);
    assert.equal(f.errors.length, 1);
    await f.lease.close(); await f.lease.close();
    assert.equal(f.calls.filter(c => c.url.endsWith('/Close')).length, 1);

    const serverLoss = fixture(); await serverLoss.lease.open('2', true, true);
    serverLoss.lease.invalidate('The save could not retain checkout.');
    assert.equal(serverLoss.lease.lost, true);
    assert.equal(serverLoss.intervals.size, 0);
    assert.throws(() => serverLoss.lease.assert(), /Saving is blocked/);
    await serverLoss.lease.close();

    const page = fixture(); await page.lease.open('1', true, true);
    page.listeners.pagehide(); await new Promise(setImmediate);
    assert.equal(page.calls.at(-1).options.keepalive, true);
    assert.equal(page.lease.lost, true, 'BFCache restoration cannot reuse a released checkout');

    const race = fixture(); const resolve = race.deferOpen();
    const opening = race.lease.open('9', true, true);
    await new Promise(setImmediate);
    await race.lease.close();
    resolve({ ok: true, json: async () => ({ required: true, leaseId: 'late-checkout' }) });
    assert.equal(await opening, false);
    assert.equal(race.calls.at(-1).body.leaseId, 'late-checkout', 'a late acquire after Close must release its own checkout');
    assert.equal(race.lease.id, '');

    const concurrent = fixture();
    const first = concurrent.lease.open('old-host', true, true);
    const second = concurrent.lease.open('new-host', true, true);
    assert.equal(await first, false, 'a superseded Open cannot acquire after awaiting cleanup');
    assert.equal(await second, true);
    assert.equal(concurrent.calls.filter(c => c.url.endsWith('/Open')).length, 1);
    assert.equal(concurrent.calls[0].body.id, 'new-host');
    assert.equal(concurrent.intervals.size, 1, 'overlapping opens must not leak heartbeat timers');
    await concurrent.lease.close();

    for(const type of ['Task','Host','Script','DateList']) {
        for (const leave of ['close','pagehide','open another object']) {
            const stale = fixture({type,admin:true}); await stale.lease.open('42',true,true);
            const confirm = stale.deferConfirmation(), recovering = stale.lease.override('42');
            await new Promise(setImmediate);
            if (leave === 'close') await stale.lease.close();
            else if (leave === 'pagehide') { stale.listeners.pagehide(); await new Promise(setImmediate); }
            else await stale.lease.open('43',true,true);
            const currentLease = stale.lease.id;
            confirm(true);
            assert.equal(await recovering,null,'a superseded confirmation must not take checkout');
            assert.equal(stale.calls.filter(c=>c.url.endsWith('/Override')).length,0,leave);
            assert.equal(stale.lease.id,currentLease,'the new editor must keep its own checkout');
            await stale.lease.close();
        }
        const late = fixture({type,admin:true}); await late.lease.open('42',true,true);
        const finishOverride = late.deferOverride(), recovering = late.lease.override('42');
        await new Promise(setImmediate); await late.lease.close();
        finishOverride({ok:true,json:async()=>({success:true,leaseId:'late-recovery',editVersion:'r2'})});
        assert.equal(await recovering,null);
        assert.equal(late.calls.filter(c=>c.url.endsWith('/Close')&&c.body.leaseId==='late-recovery').length,1);
        assert.equal(late.lease.id,''); assert.equal(late.intervals.size,0);
        const denied=fixture({type}); await assert.rejects(denied.lease.override('42'),/administrator/);
        assert.equal(denied.calls.length,0);
        const cancelled=fixture({type,admin:true,confirm:false});
        assert.equal(await cancelled.lease.override('42'),null); assert.equal(cancelled.calls.length,0);
        const recovered=fixture({type,admin:true}); await recovered.lease.open('42',true,true);
        recovered.lease.invalidate('Checkout taken over');
        assert.equal(await recovered.lease.override('42'),'fresh-revision');
        assert.equal(recovered.lease.lost,false); recovered.lease.assert();
        assert.equal(recovered.lease.id,'recovered'); assert.equal(recovered.intervals.size,1);
        assert.equal(recovered.calls.at(-1).url,'/maui/ObjectEditLease/Override');
        assert.equal(recovered.calls.at(-1).body.type,type);
        assert.equal(recovered.calls.filter(c=>c.url.endsWith('/Close')).length,0,'override does not DELETE the previous checkout');
        await recovered.lease.close(); assert.equal(recovered.calls.at(-1).body.leaseId,'recovered');
        const incomplete=fixture({type,admin:true,incomplete:true});
        await assert.rejects(incomplete.lease.override('42'),{message:'Unable to override the checkout. Please try again.'}); assert.equal(incomplete.lease.lost,true);
        assert.equal(recovered.api.restoredMessage,'Checkout restored. Review your changes and click Save; Auto Save is paused.');
    }

    // Exercise production Host Auto Save scheduling and save coalescing.
    const host = read('wwwroot/js/editors/host-editor.js');
    function production(name) {
        const match = new RegExp('        (?:async )?function ' + name + '\\(').exec(host);
        assert.ok(match, name); const end = host.indexOf('\n        }', match.index) + 10;
        return host.slice(match.index, end);
    }
    const timers = new Map(); let counter = 0, saves = 0, dirty = true, finish;
    class Input {} class Textarea {}
    const focus = { activeElement: new Input() };
    const ctx = { autoSave: true, autoSaveTimer: 0, saving: false, partialSave: false, manualRecoverySave: false, checkout: null,
        fingerprintBusy: false, picker: null, hostBrowser: null, autoSaveBlocked: '', activeSavePromise: null,
        isDirty: () => dirty, collectPayload: () => ({name:'changed'}), stablePayload: JSON.stringify,
        document: focus, dialog: { contains: () => true }, HTMLInputElement: Input, HTMLTextAreaElement: Textarea,
        window: { clearTimeout(id) { timers.delete(id); }, setTimeout(fn) { timers.set(++counter, fn); return counter; } },
        performSave: () => { saves++; return new Promise(resolve => finish = resolve); } };
    vm.createContext(ctx); vm.runInContext(production('save') + production('scheduleAutoSave'), ctx);
    ctx.scheduleAutoSave(); assert.equal(timers.size, 1);
    let [id, callback] = [...timers][0]; timers.delete(id); callback();
    assert.equal(saves, 0, 'typing defers Auto Save'); assert.equal(timers.size, 1);
    focus.activeElement = null;
    [id, callback] = [...timers][0]; timers.delete(id); callback();
    const joined = ctx.save(); assert.equal(saves, 1, 'manual close joins the current Auto Save');
    finish(true); assert.equal(await joined, true);
    ctx.autoSaveBlocked = JSON.stringify({name:'changed'}); ctx.scheduleAutoSave();
    assert.equal(timers.size, 0, 'validation failure does not loop on an unchanged draft');
    ctx.autoSaveBlocked = ''; ctx.checkout = {lost:true}; ctx.scheduleAutoSave();
    assert.equal(timers.size, 0, 'lost checkout pauses Auto Save');
    ctx.checkout=null; ctx.manualRecoverySave=true; ctx.scheduleAutoSave();
    assert.equal(timers.size,0,'an explicit recovery waits for manual Save even when Auto Save is enabled');
    dirty = false; assert.equal(await ctx.save(), false, 'a clean draft does not report a save');
    console.log('PASS shared editor checkout lifecycle, nested-instance isolation, abandoned-page cleanup and Host Auto Save scheduling/coalescing.');
})().catch(error => { console.error(error); process.exitCode = 1; });
