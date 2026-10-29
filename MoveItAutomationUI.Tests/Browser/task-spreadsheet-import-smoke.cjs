/* Run with: node MoveItAutomationUI.Tests/Browser/task-spreadsheet-import-smoke.cjs
 * Dependency-free Task CSV/XLSX modal tests. Simulates the DOM and JSON responses,
 * not MOVEit or the full application browser layout.
 */
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

class Element {
    constructor(tag='div') {
        this.tagName = tag.toLowerCase(); this.children=[]; this._text=''; this.dataset={}; this.style={};
        this.hidden=false; this.disabled=false; this.value=''; this.checked=false; this.files=[]; this.listeners={};
        this.className='';
    }
    set textContent(value) { this._text=String(value); this.children=[]; }
    get textContent() { return this._text + this.children.map(child=>child.textContent).join(''); }
    appendChild(item) {
        if (item.tagName === '#fragment') this.children.push(...item.children);
        else this.children.push(item);
        return item;
    }
    replaceChildren(...items) { this.children=[]; this._text=''; items.forEach(item=>this.appendChild(item)); }
    addEventListener(type, callback) { (this.listeners[type] ??= []).push(callback); }
    dispatch(type) { (this.listeners[type] || []).forEach(callback=>callback()); }
    click() { if (!this.disabled) this.dispatch('click'); }
    walk() { return this.children.flatMap(child=>[child,...child.walk()]); }
    querySelectorAll(selector) {
        if (selector === 'tr') return this.walk().filter(node=>node.tagName==='tr');
        if (selector.startsWith('.')) return this.walk().filter(node=>node.className.split(' ').includes(selector.slice(1)));
        return [];
    }
    querySelector(selector) { return this.querySelectorAll(selector)[0] ?? null; }
}

const dictionary = new Map();
const selectors = [
    'native-portability-import-panel','native-portability-import-action',
    'task-spreadsheet-import-panel','task-spreadsheet-file','task-spreadsheet-prefix',
    'task-spreadsheet-update-existing','task-spreadsheet-leave-disabled','task-spreadsheet-action',
    'native-portability-import-error','task-spreadsheet-analysis','task-spreadsheet-summary',
    'task-spreadsheet-grid-head','task-spreadsheet-grid-body',
    'task-spreadsheet-grid-pagination','task-spreadsheet-page-label',
    'task-spreadsheet-page-prev','task-spreadsheet-page-next',
    'task-spreadsheet-results-panel','task-spreadsheet-results',
    'task-spreadsheet-progress','task-spreadsheet-progress-fill','task-spreadsheet-progress-text'
];
for (const name of selectors) dictionary.set(`[data-${name}]`,new Element());
const dialog = new Element('dialog');
const modeInputs = [Object.assign(new Element('input'),{value:'portability',checked:false}),Object.assign(new Element('input'),{value:'spreadsheet',checked:true})];
const nativeButtons=[new Element('button'),new Element('button')];
dialog.querySelector = selector => {
    if (selector==='input[name="taskImportMode"]:checked') return modeInputs.find(input=>input.checked);
    if (selector==='input[name="taskImportMode"][value="portability"]') return modeInputs[0];
    return dictionary.get(selector) || null;
};
dialog.querySelectorAll = selector => selector==='input[name="taskImportMode"]' ? modeInputs : selector.includes('native-portability-import-cancel') ? nativeButtons : [];
const document = {
    querySelector: selector => selector==='[data-native-portability-import-dialog]'?dialog:null,
    createElement: name=>new Element(name),
    createDocumentFragment: ()=>new Element('#fragment'),
    createTextNode: text=>Object.assign(new Element('#text'),{textContent:text})
};
const rows=Array.from({length:5},(_,i)=>({rowNumber:i+2,values:{TASK_NAME:`TASK_${i+1}`}}));
const imported=[]; const analyzedPrefixes=[]; let refreshed=0;
const fetch=async (url,req)=>{
    if (url === '/analyze') analyzedPrefixes.push(req.body.get('TaskNamePrefix'));
    return ({
    status: url==='/row' && JSON.parse(req.body).rowNumber===3 ? 422 : 200,
    ok: !(url==='/row' && JSON.parse(req.body).rowNumber===3),
    headers:{get:()=> 'application/json'},
    json:async ()=>url==='/preview' ? {result:{columns:['TASK_NAME'],rows,skippedBlankRows:0}} :
        url==='/analyze' ? {result:{analysisId:'test', columns:['TASK_NAME'],rows:rows.map(r=>({...r,status:'Valid'})),canImport:true,validRows:4,warningRows:1,errorRows:0,issues:[{severity:'Warning',rowNumber:2,column:'AUTOMATION_DESTINATION_HOST',message:'Host name was not found. Enter the MOVEit Automation Friendly Name.'}]}} :
        (()=>{const n=JSON.parse(req.body).rowNumber;imported.push(n);
            // Simulate a server-rejected row without stopping the batch.
            if (n===3) return {message:'Simulated validation failure'};
            return {result:{rowNumber:n,taskName:`TASK_${n-1}`,outcome:n===4?'Updated':n===5?'Unverified':'Created',message:n===5?'MOVEit accepted creation but Task ID could not be resolved. Inspect MOVEit before retrying.':'Done'}};
        })()
});
};
const window={mauiAppUrl: route => route, location:{assign:()=>{throw new Error('Unexpected login redirect');}}};
const script=fs.readFileSync(path.resolve(__dirname,'../../wwwroot/js/task-spreadsheet-import.js'),'utf8');
vm.runInNewContext(script,{document,window,fetch,FormData,File,console},{filename:'task-spreadsheet-import.js'});
const importer=window.MauiTaskSpreadsheetImport.create({previewUrl:'/preview',analyzeUrl:'/analyze',importRowUrl:'/row',importRowLimit:2,refresh:async()=>{refreshed++;}});
assert.ok(importer);
const get=key=>dictionary.get(`[data-task-spreadsheet-${key}]`);
const tick=()=>new Promise(resolve=>setImmediate(resolve));
async function until(predicate,label) {for(let i=0;i<150;i++){if(predicate())return;await tick();}throw new Error('Timed out: '+label);}
async function main(){
    const markup=fs.readFileSync(path.resolve(__dirname,'../../Views/Shared/_NativePortabilityDialogs.cshtml'),'utf8');
    assert.match(markup,/<input[^>]*type="text"[^>]*maxlength="60"[^>]*data-task-spreadsheet-prefix/);
    get('prefix').value='team-';
    get('file').files=[new File(['TASK_NAME\nTEST'], 'tasks.csv')];
    get('file').dispatch('change');
    await until(()=>get('grid-body').children.length===2 && !get('action').disabled,'file preview');
    assert.match(get('page-label').textContent,/1–2 of 5/);
    assert.equal(get('grid-body').children[0].querySelector('.task-spreadsheet-row-pill').textContent,'Not analyzed');
    get('action').click();
    await until(()=>get('action').textContent.startsWith('Import First'),'analysis');
    assert.deepEqual(analyzedPrefixes,['team-'],'literal prefix is sent without a separator or case conversion');
    get('prefix').value='PROD_';
    get('prefix').dispatch('input');
    assert.equal(get('action').textContent,'Analyze File','typing invalidates prior analysis');
    get('action').click();
    await until(()=>analyzedPrefixes.length===2 && get('action').textContent.startsWith('Import First'),'prefix reanalysis');
    assert.deepEqual(analyzedPrefixes,['team-','PROD_']);
    const warning = get('results').children[0];
    assert.equal(warning.children.length,2,'result has separate location and message cells');
    assert.equal(warning.children[0].textContent,'Row 2 · AUTOMATION_DESTINATION_HOST');
    assert.equal(warning.children[1].className,'task-spreadsheet-issue-message');
    assert.match(warning.children[1].textContent,/Friendly Name/);
    get('action').click();
    await until(()=>imported.length===2 && !get('action').disabled,'first import');
    assert.equal(nativeButtons[0].textContent,'Cancel','unfinished batches keep Cancel');
    assert.deepEqual(imported,[2,3]);
    assert.equal(get('grid-body').children[0].querySelector('.task-spreadsheet-row-pill').textContent,'Created');
    assert.equal(get('grid-body').children[1].querySelector('.task-spreadsheet-row-pill').textContent,'Failed');
    assert.match(get('action').textContent,/Next 2/);
    get('page-next').click();
    assert.equal(get('grid-body').children.length,2);
    get('action').click();
    await until(()=>imported.length===4 && !get('action').disabled,'second import');
    assert.equal(get('grid-body').children[0].querySelector('.task-spreadsheet-row-pill').textContent,'Updated');
    assert.equal(get('grid-body').children[1].querySelector('.task-spreadsheet-row-pill').textContent,'Unverified');
    get('action').click();
    await until(()=>imported.length===5 && get('action').disabled,'third import');
    assert.deepEqual(imported,[2,3,4,5,6]);
    assert.equal(get('action').textContent,'Import Finished (with failures)');
    assert.equal(nativeButtons[0].textContent,'Close','all attempted rows finish the import even with errors');
    assert.equal(refreshed,3);
    get('page-prev').click();
    assert.equal(nativeButtons[0].textContent,'Close','paging preserves the completed label');
    assert.equal(get('grid-body').children[0].querySelector('.task-spreadsheet-row-pill').textContent,'Created');
    assert.equal(get('results').children.length,6);
    assert.match(get('summary').textContent,/Created: 2/);
    assert.match(get('summary').textContent,/Failed: 1/);
    assert.match(get('summary').textContent,/Updated: 1/);
    assert.match(get('summary').textContent,/Unverified: 1/);
    importer.open();
    assert.equal(nativeButtons[0].textContent,'Cancel','reopening resets the footer');
    assert.equal(get('prefix').value,'','reopening resets to no prefix');
    console.log('PASS Task prefix textbox, literal prefix transmission and reanalysis, 2+2+1 batches, outcomes, no replay.');
}
main().catch(err=>{console.error(err);process.exitCode=1;});

