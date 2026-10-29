'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const read=file=>fs.readFileSync(path.resolve(__dirname,'../..',file),'utf8');
const posted=[],document={documentElement:{dataset:{browserNotificationLogUrl:'/maui/BrowserNotifications/Record'}},querySelector:()=>({value:'csrf'})};
const window={location:{pathname:'/maui/Hosts',search:'?password=not-sent'}};
const context=vm.createContext({document,window,fetch:(url,options)=>{posted.push({url,options});return Promise.reject(new Error('offline'));}});
vm.runInContext(read('wwwroot/js/browser-notifications.js'),context);
for(const kind of ['default','warning','error'])window.mauiLogNotification('password="hidden value" '+kind,kind,'page');
assert.equal(posted.length,3);
for(const item of posted){
 const data=JSON.parse(item.options.body);assert.equal(item.url,'/maui/BrowserNotifications/Record');assert.equal(data.page,'/maui/Hosts');
 assert.ok(data.message.includes('[redacted]'));assert.ok(!item.options.body.includes('hidden value'));assert.ok(!item.options.body.includes('not-sent'));
 assert.equal(item.options.headers.RequestVerificationToken,'csrf');assert.equal(item.options.keepalive,true);
}
window.mauiLogNotification('Authorization: Bearer private-token','error');
assert.ok(!posted.at(-1).options.body.includes('private-token'));posted.pop();
window.mauiLogNotification('');assert.equal(posted.length,3);
document.querySelector=()=>null;window.mauiLogNotification('No token');assert.equal(posted.length,3);
// Production viewport sizing must retain exactly five rows regardless of native scrollbar size.
let observer;
const root={querySelectorAll:()=>[],addEventListener(){},documentElement:{dataset:{}},activeElement:null};
const runtime=vm.createContext({document:root,window:{location:{href:'https://maui.test/'},history:{replaceState(){}}},URL,
 requestAnimationFrame:callback=>callback(),getComputedStyle:()=>({borderTopWidth:'1',borderBottomWidth:'1'}),ResizeObserver:class {constructor(callback){observer=callback;}observe(){}},WeakMap,Map});
vm.runInContext(read('wwwroot/js/modal-ui.js'),runtime);
for(const scrollbar of [0,15,17]){
 const frame={offsetHeight:220,clientHeight:218-scrollbar,style:{},querySelector:selector=>({getBoundingClientRect:()=>({height:selector==='thead'?32:34})})};
 runtime.window.MauiModalUI.sizeTableViewport(frame,5);assert.equal(frame.style.height,`${32+5*34+2+scrollbar}px`);
 frame.offsetHeight=0;frame.style.height='hidden';observer();assert.equal(frame.style.height,'hidden');
 frame.offsetHeight=220;observer();assert.equal(frame.style.height,`${32+5*34+2+scrollbar}px`);
}
setImmediate(()=>console.log('PASS toast logging, severity, credential redaction, outage isolation, antiforgery and five-row viewports.'));
