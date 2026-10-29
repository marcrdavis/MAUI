/* Shared Traditional/Advanced Run As Of modal. Native execution/restoration stays server-side. */
(() => {
    const macro = /\[(?:AAAA|AAA|A|B|DD|D|HHHH|HHH|HH|H|II|JJJ|J|KKK|K|MMMM|MMM|MM|M|SS|S|TT|T|WW|W|XX|X|YYYY|YY)[+-]*\]/i;
    const fields = new Set(['path','foldername','filemask','exfile','exfo','renameto','filename','zipfilename']);
    function available(patches) {
        return patches.some(step => ['Source','Destination'].includes(step.kind)
            && Object.entries(step.patch || {}).some(([key,value]) => fields.has(key.toLowerCase()) && macro.test(String(value ?? ''))));
    }
    // An authored picker keeps the footer and focus behavior consistent across browsers.
    // The existing local date-time value remains the only value submitted to Run As Of.
    function createDateTimePicker(dialog, input, changed) {
        const find = name => dialog.querySelector(`[data-run-as-of-picker-${name}]`);
        const trigger = find('toggle'), panel = dialog.querySelector('[data-run-as-of-picker]');
        if (!trigger || !panel) return null;
        const month = find('month'), year = find('year'), days = find('days'), error = find('error');
        const time = ['hour','minute','second'].map(find), pad = value => String(value).padStart(2,'0');
        const makeDate = (y,m,d) => { const value = new Date(0); value.setFullYear(y,m,d); value.setHours(12,0,0,0); return value; };
        const key = value => `${String(value.getFullYear()).padStart(4,'0')}-${pad(value.getMonth()+1)}-${pad(value.getDate())}`;
        const selected = () => input.value ? makeDate(...input.value.slice(0,10).split('-').map((value,index) => Number(value)-(index===1?1:0))) : null;
        const fullDate = new Intl.DateTimeFormat(undefined,{dateStyle:'full'});
        let chosen = null, view = makeDate(new Date().getFullYear(),new Date().getMonth(),1);
        for (let index=0;index<12;index++) {
            const option = document.createElement('option'); option.value = String(index);
            option.textContent = new Intl.DateTimeFormat(undefined,{month:'long'}).format(makeDate(2026,index,1)); month.append(option);
        }
        function position() {
            if (!panel.matches(':popover-open')) return;
            const box = trigger.getBoundingClientRect(), gap = 6, margin = 12;
            const width = Math.min(window.innerWidth,document.documentElement.clientWidth,window.visualViewport?.width || Infinity);
            const height = Math.min(window.innerHeight,document.documentElement.clientHeight,window.visualViewport?.height || Infinity);
            panel.style.maxWidth = `${Math.max(0,width-margin*2)}px`;
            panel.style.maxHeight = `${Math.max(0,height-margin*2)}px`;
            panel.style.left = `${Math.max(margin,Math.min(box.right-panel.offsetWidth,width-panel.offsetWidth-margin))}px`;
            const below = box.bottom+gap, above = box.top-panel.offsetHeight-gap;
            panel.style.top = `${Math.max(margin,below+panel.offsetHeight<=height-margin?below:above)}px`;
        }
        function hide() { if (panel.matches(':popover-open')) panel.hidePopover(); }
        function render(focusDay) {
            month.value = String(view.getMonth()); year.value = String(view.getFullYear());
            find('prev').disabled = view.getFullYear()===1 && view.getMonth()===0;
            find('next').disabled = view.getFullYear()===9999 && view.getMonth()===11;
            const choice = chosen, count = makeDate(view.getFullYear(),view.getMonth()+1,0).getDate();
            const fragment = document.createDocumentFragment();
            for (let row=0;row<6;row++) {
                const line = document.createElement('div'); line.setAttribute('role','row');
                for (let column=0;column<7;column++) {
                    const number = row*7+column-view.getDay()+1, cell = document.createElement('span'); cell.setAttribute('role','gridcell');
                    if (number>=1 && number<=count) {
                        const value = makeDate(view.getFullYear(),view.getMonth(),number), button = document.createElement('button');
                        const active = choice && key(choice)===key(value);
                        button.type = 'button'; button.dataset.pickerDay = key(value); button.textContent = String(number);
                        button.setAttribute('aria-label',fullDate.format(value)); cell.setAttribute('aria-selected',String(Boolean(active)));
                        if (key(value)===key(new Date())) button.setAttribute('aria-current','date');
                        button.tabIndex = active || !choice || choice.getMonth()!==view.getMonth() || choice.getFullYear()!==view.getFullYear() ? (active || number===1 ? 0 : -1) : -1;
                        button.addEventListener('click',() => { commit(value); render(key(value)); }); cell.append(button);
                    } else cell.setAttribute('aria-hidden','true');
                    line.append(cell);
                }
                fragment.append(line);
            }
            days.replaceChildren(fragment); position();
            if (focusDay) {
                days.querySelectorAll('[tabindex="0"]').forEach(button=>{button.tabIndex=-1;});
                const button = days.querySelector(`[data-picker-day="${focusDay}"]`);
                if(button){button.tabIndex=0;button.focus({preventScroll:true});}
            }
        }
        function commit(value = chosen) {
            chosen = value; error.hidden = true;
            if (!value || time.some(control=>!control.value || !control.checkValidity())) { input.value = ''; return false; }
            input.value = `${key(value)}T${time.map(control=>pad(Number(control.value))).join(':')}`;
            changed(); return true;
        }
        function sync() {
            hide(); error.hidden = true;
        }
        trigger.addEventListener('click',() => {
            if (panel.matches(':popover-open')) { hide(); return; }
            const choice = selected() || new Date(), parts = input.value ? input.value.slice(11).split(':') : [pad(choice.getHours()),pad(choice.getMinutes()),pad(choice.getSeconds())];
            time.forEach((control,index)=>{control.value=parts[index]||'00';});
            chosen = choice; view = makeDate(choice.getFullYear(),choice.getMonth(),1); render(); panel.showPopover(); position();
            days.querySelector('[tabindex="0"]')?.focus({preventScroll:true});
        });
        panel.addEventListener('toggle',() => { trigger.setAttribute('aria-expanded',String(panel.matches(':popover-open'))); });
        panel.addEventListener('keydown',event => {
            if (event.key==='Escape') { event.preventDefault(); event.stopPropagation(); hide(); trigger.focus(); return; }
            const button = event.target.closest('[data-picker-day]'); if (!button) return;
            const value = makeDate(...button.dataset.pickerDay.split('-').map((part,index)=>Number(part)-(index===1?1:0)));
            const offsets = {ArrowLeft:-1,ArrowRight:1,ArrowUp:-7,ArrowDown:7,Home:-value.getDay(),End:6-value.getDay()};
            if (Object.hasOwn(offsets,event.key)) value.setDate(value.getDate()+offsets[event.key]);
            else if (event.key==='PageUp' || event.key==='PageDown') {
                const target = makeDate(value.getFullYear(),value.getMonth()+(event.key==='PageUp'?-1:1),1);
                value.setFullYear(target.getFullYear(),target.getMonth(),Math.min(value.getDate(),makeDate(target.getFullYear(),target.getMonth()+1,0).getDate()));
            } else return;
            event.preventDefault(); if(value.getFullYear()<1 || value.getFullYear()>9999)return;
            view = makeDate(value.getFullYear(),value.getMonth(),1); render(key(value));
        });
        for (const [name,offset] of [['prev',-1],['next',1]]) find(name).addEventListener('click',() => { view=makeDate(view.getFullYear(),view.getMonth()+offset,1); render(); });
        month.addEventListener('change',() => { view=makeDate(view.getFullYear(),Number(month.value),1); render(); });
        year.addEventListener('change',() => { if(year.value && year.checkValidity()){view=makeDate(Number(year.value),view.getMonth(),1);render();}else year.value=String(view.getFullYear()); });
        time.forEach(control=>control.addEventListener('input',() => commit()));
        find('clear').addEventListener('click',() => { input.value=''; chosen=null; hide(); trigger.focus(); });
        find('today').addEventListener('click',() => {
            const now = new Date(); time.forEach((control,index)=>{control.value=pad([now.getHours(),now.getMinutes(),now.getSeconds()][index]);});
            view=makeDate(now.getFullYear(),now.getMonth(),1); commit(now); render(key(now));
        });
        find('ok').addEventListener('click',() => {
            if (!commit()) { error.textContent='Select a date and enter a valid time.';error.hidden=false;(time.find(control=>!control.value || !control.checkValidity())||days.querySelector('[tabindex="0"]'))?.focus();return; }
            hide(); trigger.focus();
        });
        window.addEventListener('resize',position); dialog.addEventListener('scroll',position,true); dialog.addEventListener('close',hide);
        // Preserve direct keyboard entry without opening the browser's separate picker.
        input.addEventListener('input',()=>{if(input.value && input.checkValidity())changed();});
        input.addEventListener('keydown',event=>{if(event.key==='F4' || event.altKey && event.key==='ArrowDown'){event.preventDefault();trigger.click();}});
        return {sync,focus:()=>trigger.focus(),disable:value=>{trigger.disabled=value;if(value)hide();}};
    }
    function create(options) {
        const dialog = document.querySelector('[data-run-as-of-dialog]');
        if (!dialog) return null;
        const date = dialog.querySelector('[data-run-as-of-date]'), run = dialog.querySelector('[data-run-as-of-run]');
        const closes = [...dialog.querySelectorAll('[data-run-as-of-close]')], status = dialog.querySelector('[data-run-as-of-status]');
        const bar = dialog.querySelector('[data-run-as-of-progress]'), error = dialog.querySelector('[data-run-as-of-error]');
        const picker = createDateTimePicker(dialog,date,()=>showError(''));
        let busy = false, locked = false, timer = 0, generation = 0;
        function showError(message) { error.textContent = message || ''; error.hidden = !message; }
        function setBusy(value) {
            busy = value; run.disabled = value || locked; run.textContent = value ? 'Running…' : 'Run'; date.disabled = value || locked;
            closes.forEach(button => { button.disabled = value; }); picker?.disable(value || locked); options.busy(value);
        }
        function display(state) {
            if (!state) return;
            status.textContent = state.message || '';
            bar.value = Number(state.progress) || 0;
        }
        async function poll(jobId, owner) {
            if (!busy || owner !== generation) return;
            try {
                const url = new URL(options.progressUrl(), window.location.href); url.searchParams.set('jobId', jobId);
                const state = await options.fetchJson(url.toString());
                if (busy && owner === generation) display(state);
            } catch { /* The POST remains authoritative; transient polling errors must not repeat execution. */ }
            if (busy && owner === generation) timer = window.setTimeout(() => poll(jobId, owner), 1000);
        }
        async function execute() {
            if (busy || locked) return;
            if (!date.value || !date.checkValidity()) { showError('Select a valid date and time.'); if(picker)picker.focus();else date.focus(); return; }
            showError('');
            setBusy(true);
            try {
                if (!await options.flush()) { showError('Save the Task successfully before using Run As Of.'); setBusy(false); return; }
            } catch (failure) { showError(failure?.message || 'Unable to save the Task before running.'); setBusy(false); return; }
            const context = options.context(), jobId = window.crypto.randomUUID(), owner = ++generation;
            const signature = options.signature();
            setBusy(true); display({ message:'Preparing Task…', progress:0 });
            timer = window.setTimeout(() => poll(jobId, owner), 500);
            try {
                const url = new URL(options.runUrl(), window.location.href); url.searchParams.set('id', context.id);
                const state = await options.fetchJson(url.toString(), { method:'POST', body:JSON.stringify({ jobId, dateTime:date.value, editVersion:context.editVersion }) });
                display(state);
                locked = !state.restored;
                if (!state.success) showError(state.message || 'Run As Of could not complete.');
                if (state.restored) {
                    try { await options.reload(signature); }
                    catch { showError('The Task was restored, but its saved state could not be refreshed. Reload the Task before editing again.'); locked = true; }
                }
            } catch (failure) {
                // A lost POST response may follow a successful start. Never enable blind retry.
                let state = null;
                try { const url = new URL(options.progressUrl(), window.location.href); url.searchParams.set('jobId', jobId); state = await options.fetchJson(url.toString()); } catch { /* preserved below */ }
                if (state && !state.completed) {
                    status.textContent = 'The run continues on the server; waiting for restoration…';
                    while (!state.completed) {
                        await new Promise(resolve => window.setTimeout(resolve, 1500));
                        try { const url = new URL(options.progressUrl(), window.location.href); url.searchParams.set('jobId', jobId); state = await options.fetchJson(url.toString()); display(state); } catch { /* retry read only */ }
                    }
                    locked = !state.restored;
                    if (state.restored) { try { await options.reload(signature); } catch { locked = true; } }
                    if (!state.success) showError(state.message);
                } else if (state?.completed) {
                    display(state); locked = !state.restored;
                    if (state.restored) { try { await options.reload(signature); } catch { locked = true; } }
                    if (!state.success) showError(state.message);
                } else {
                    locked = Boolean(state && !state.restored) || !state && ![400,403,404,409,422].includes(failure?.status);
                    showError(state?.message || failure?.message || 'Run outcome could not be confirmed. Inspect the Task and its Undo snapshot before retrying.');
                }
            } finally {
                ++generation; window.clearTimeout(timer); timer = 0; setBusy(false);
                if (locked) options.recovery();
            }
        }
        run.addEventListener('click', execute);
        closes.forEach(button => button.addEventListener('click', () => { if (!busy) dialog.close(); }));
        dialog.addEventListener('cancel', event => { event.preventDefault(); if (!busy) dialog.close(); });
        window.MauiModalUI?.register(dialog, { error:showError, clear:() => showError('') });
        return {
            open() {
                if (busy) return;
                locked = false; showError(''); bar.value = 0; status.textContent = 'Choose the date and time to use for the macros.';
                const now = new Date(), pad = value => String(value).padStart(2,'0');
                date.value = `${now.getFullYear()}-${pad(now.getMonth()+1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;
                date.disabled = false; run.disabled = false; picker?.disable(false); picker?.sync(); dialog.showModal(); closes[0]?.focus();
            },
            isBusy:() => busy
        };
    }
    window.MauiTaskRunAsOf = { create, available };
})();
