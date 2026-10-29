/* Optional Advanced Task flow tree. Consumes sanitized topology, never native definitions or transports. */
(() => {
    'use strict';
    const symbols = { Source:'↓', Destination:'↑', Process:'▶', For:'↻', If:'?', When:'↳', Otherwise:'↳', Email:'✉', Comment:'▤', Unsupported:'?' };
    const drawings = {
        RunTask: [['circle',{cx:13,cy:4,r:1.5}],['path',{d:'M4 17l5-1 1-3 M15 21v-4l-4-3 1-6 M7 12V9l5-1 3 4 3 1'}]],
        UpdOrig: [['path',{d:'M13 3H5v18h14v-6 M13 3v6h6 M13 3l6 6 M11 17l7-7 3 3-7 7-4 1z'}]]
    };
    function mount(container, { nodes = [], onSelect = () => {}, getInsertActions = null, getActions = null, canDrag = () => false } = {}) {
        if (!container) throw new Error('A flow container is required.');
        const collapsed = new Set();
        const entries = new Map();
        let selected = '', disposed = false;
        container.classList.add('advanced-task-flow');
        container.setAttribute('role', 'tree');
        container.setAttribute('aria-label', 'Advanced Task flow');
        // Small monochrome stroke icons inherit the same accent as the existing flow symbols.
        function stepSymbol(kind) {
            const icon = element('span','advanced-task-flow-symbol');
            icon.setAttribute('aria-hidden','true');
            if (!drawings[kind]) { icon.textContent=symbols[kind]||'?';return icon; }
            const ns='http://www.w3.org/2000/svg',svg=document.createElementNS(ns,'svg');
            for (const [name,value] of Object.entries({viewBox:'0 0 24 24',fill:'none',stroke:'currentColor','stroke-width':1.7,'stroke-linecap':'round','stroke-linejoin':'round',focusable:'false'})) svg.setAttribute(name,String(value));
            for (const [tag,attributes] of drawings[kind]) {
                const shape=document.createElementNS(ns,tag);
                for (const [name,value] of Object.entries(attributes)) shape.setAttribute(name,String(value));
                svg.append(shape);
            }
            icon.append(svg);return icon;
        }
        function element(tag, className, text) {
            const node = document.createElement(tag); node.className = className;
            if (text !== undefined) node.textContent = text;
            return node;
        }
        const menu = element('div','traditional-task-editor-step-actions-menu advanced-flow-step-menu');
        menu.hidden=true; menu.setAttribute('role','menu'); menu.dataset.advancedStepMenu='';
        (container.closest('dialog')||container.parentElement).append(menu);
        let menuTrigger=null;
        function closeMenu(focus=false) {
            const trigger=menuTrigger;menuTrigger=null;menu.hidden=true;menu.replaceChildren();
            trigger?.setAttribute('aria-expanded','false');if(focus===true&&trigger?.isConnected)trigger.focus();
        }
        function openMenu(actions,trigger,kind='step') {
            if(menuTrigger===trigger&&!menu.hidden){closeMenu(true);return;}
            closeMenu();menu.dataset.advancedMenuKind=kind;menuTrigger=trigger;trigger.setAttribute('aria-expanded','true');
            for(const action of actions) {
                const button=element('button',action.danger?'danger-menu-item':'',action.label);button.type='button';button.setAttribute('role','menuitem');button.disabled=Boolean(action.disabled);button.dataset.advancedAction=action.id;if(action.kind)button.dataset.advancedAddKind=action.kind;
                button.addEventListener('click',event=>{event.stopPropagation();closeMenu();action.run();});menu.append(button);
            }
            const owner=container.closest('dialog')?.getBoundingClientRect()||{left:0,top:0,right:innerWidth,bottom:innerHeight,height:innerHeight};
            menu.style.maxHeight=Math.max(0,owner.height-16)+'px';
            menu.hidden=false;const bounds=trigger.getBoundingClientRect(),size=menu.getBoundingClientRect();
            menu.style.left=Math.max(owner.left+8,Math.min(bounds.right-size.width,owner.right-size.width-8))+'px';
            menu.style.top=Math.max(owner.top+8,Math.min(bounds.bottom+5,owner.bottom-size.height-8))+'px';
            menu.querySelector('button:not(:disabled)')?.focus();
        }
        function outside(event){if(!menu.hidden&&!menu.contains(event.target)&&event.target!==menuTrigger)closeMenu();}
        function menuKeys(event){
            if(!menu.hidden&&event.key==='Escape'){event.preventDefault();event.stopPropagation();closeMenu(true);return;}
            if(event.key==='Tab'){closeMenu();return;}
            if(!['ArrowDown','ArrowUp','Home','End'].includes(event.key))return;
            event.preventDefault();event.stopPropagation();const buttons=[...menu.querySelectorAll('button:not(:disabled)')],index=buttons.indexOf(document.activeElement);
            buttons[event.key==='Home'?0:event.key==='End'?buttons.length-1:(index+(event.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length]?.focus();
        }
        menu.addEventListener('keydown',menuKeys);container.closest('dialog')?.addEventListener('close',()=>closeMenu());document.addEventListener('pointerdown',outside);window.addEventListener('resize',closeMenu);container.parentElement.addEventListener('scroll',closeMenu,true);
        function append(parent, items, disabledByParent = false, parentKey = '', parentKind = '') {
            const slot = beforeKey => {
                if (!getInsertActions || parentKind === 'If') return;
                const insert=element('button','advanced-flow-insert','+'); insert.type='button'; insert.draggable=false; insert.dataset.advancedInsert=''; insert.dataset.parentKey=parentKey; insert.dataset.beforeKey=beforeKey||''; insert.setAttribute('aria-label','Insert step here');insert.setAttribute('aria-haspopup','menu');insert.setAttribute('aria-expanded','false'); insert.addEventListener('click',event=>{event.stopPropagation();openMenu(getInsertActions({parentKey,beforeKey}),insert,'insert');});parent.append(insert);
            };
            for (const item of items) {
                if (!item.key || entries.has(item.key)) throw new Error('Flow locations must be unique.');
                if(item.insertionEligible!==false)slot(item.key);
                const disabled = disabledByParent || item.disabled;
                const node = element('div', 'advanced-task-flow-item');
                // A container also owns nested insertion controls. Only its step row may start a drag.
                node.draggable = false;
                node.dataset.flowKey = item.key;
                node.setAttribute('role','treeitem');
                node.setAttribute('aria-label',[item.label,item.summary,disabled?'Disabled':''].filter(Boolean).join('. '));
                node.setAttribute('aria-selected',String(item.key === selected));
                node.tabIndex = item.key === selected ? 0 : -1;
                const row = element('div','advanced-task-flow-row');
                row.draggable = Boolean(canDrag(item));
                const children = Array.isArray(item.children) ? item.children : [];
                if (children.length) {
                    node.setAttribute('aria-expanded',String(!collapsed.has(item.key)));
                    const toggle = element('button','advanced-task-flow-toggle',collapsed.has(item.key)?'›':'⌄');
                    toggle.type='button'; toggle.tabIndex=-1; toggle.dataset.flowToggle='';
                    toggle.setAttribute('aria-label',(collapsed.has(item.key)?'Expand ':'Collapse ')+item.label);
                    row.append(toggle);
                } else row.append(element('span','advanced-task-flow-spacer'));
                const icon = stepSymbol(item.kind);
                const copy = element('span','advanced-task-flow-copy');
                copy.append(element('strong','advanced-task-flow-label',item.label || 'Step'));
                if (item.summary) copy.append(element('span','advanced-task-flow-summary',item.summary));
                row.append(icon,copy);
                if (disabled) row.append(element('span','advanced-task-flow-disabled',item.disabled?'Disabled':'Parent disabled'));
                if (item.kind === 'Unsupported') row.append(element('span','advanced-task-flow-disabled','Read-only'));
                if(getActions) {
                    const actions=element('button','secondary-button traditional-task-editor-step-actions-button advanced-flow-step-actions','⋯');actions.type='button';actions.dataset.advancedStepActions='';actions.setAttribute('aria-label',item.label+' actions');actions.setAttribute('aria-haspopup','menu');actions.setAttribute('aria-expanded','false');
                    actions.addEventListener('click',event=>{event.stopPropagation();openMenu(getActions(item),actions);});row.append(actions);
                }
                row.title = [item.label,item.summary].filter(Boolean).join(': ');
                node.append(row); parent.append(node); entries.set(item.key,{item,node});
                if (children.length || getInsertActions && ['For','If','When','Otherwise'].includes(item.kind)) {
                    const group = element('div','advanced-task-flow-group'); group.setAttribute('role','group');
                    group.hidden = collapsed.has(item.key); node.append(group); append(group,children,disabled,item.insertParentKey||item.key,item.kind==='If'?'When':item.kind);
                }
            }
            slot('');
        }
        function render() {
            closeMenu();
            const hadFocus=container.contains(document.activeElement);
            entries.clear(); container.replaceChildren();
            const start = element('div','advanced-task-flow-terminal advanced-task-flow-start','Start'); start.setAttribute('role','presentation');
            container.append(start); append(container,nodes);
            if (!entries.has(selected)) selected = entries.keys().next().value || '';
            while (entries.get(selected)?.node.closest('[hidden]')) {
                selected = entries.get(selected).node.parentElement.closest('[data-flow-key]')?.dataset.flowKey || '';
            }
            for (const [key,{node}] of entries) { node.tabIndex=key===selected?0:-1; node.setAttribute('aria-selected',String(key===selected)); }
            const end = element('div','advanced-task-flow-terminal advanced-task-flow-end','End'); end.setAttribute('role','presentation'); container.append(end);
            if (!nodes.length) container.insertBefore(element('p','advanced-task-flow-empty','No steps.'),end);
            if(hadFocus)entries.get(selected)?.node.focus({preventScroll:true});
        }
        function select(key, focus = false, notify = true) {
            if (disposed || !entries.has(key)) return false;
            selected=key;
            let ancestor=entries.get(key).node.parentElement?.closest('[data-flow-key]'), expanded=false;
            while(ancestor) { if(collapsed.delete(ancestor.dataset.flowKey))expanded=true;ancestor=ancestor.parentElement?.closest('[data-flow-key]'); }
            if(expanded)render();
            for (const [id,{node}] of entries) { node.tabIndex=id===key?0:-1; node.setAttribute('aria-selected',String(id===key)); }
            const entry=entries.get(key); if(focus) entry.node.focus(); if(notify) onSelect(entry.item); return true;
        }
        function toggle(key, expand) {
            const entry=entries.get(key); if(!entry?.item.children?.length) return;
            const opening=expand ?? collapsed.has(key);
            if(opening) collapsed.delete(key); else collapsed.add(key);
            // A collapsed descendant cannot remain the only keyboard tab stop.
            const previous=selected;
            if(!opening && entries.get(selected)?.node !== entry.node && entry.node.contains(entries.get(selected)?.node)) selected=key;
            render(); entries.get(key)?.node.focus();
            if(previous !== selected) onSelect(entries.get(selected).item);
        }
        function click(event) {
            const node=event.target.closest('[data-flow-key]'); if(!node || !container.contains(node)) return;
            if(event.target.closest('[data-flow-toggle]')) { toggle(node.dataset.flowKey); return; }
            select(node.dataset.flowKey);
        }
        function keydown(event) {
            if(event.target.closest('button')&&!event.target.matches('[data-flow-key]'))return;
            const current=event.target.closest('[data-flow-key]'); if(!current || !container.contains(current)) return;
            const key=current.dataset.flowKey;
            const visible=[...entries.values()].filter(entry=>!entry.node.closest('[hidden]'));
            const index=visible.findIndex(entry=>entry.item.key===key);
            let target;
            if(event.key==='ArrowDown') target=visible[Math.min(index+1,visible.length-1)];
            else if(event.key==='ArrowUp') target=visible[Math.max(index-1,0)];
            else if(event.key==='Home') target=visible[0];
            else if(event.key==='End') target=visible.at(-1);
            else if(event.key==='ArrowRight') {
                if(collapsed.has(key)) toggle(key,true);
                else target=entries.get(entries.get(key)?.item.children?.[0]?.key);
            } else if(event.key==='ArrowLeft') {
                if(current.getAttribute('aria-expanded')==='true') toggle(key,false);
                else { const parent=current.parentElement?.closest('[data-flow-key]'); if(parent) target=entries.get(parent.dataset.flowKey); }
            } else if(event.key==='Enter' || event.key===' ') select(key,true);
            else return;
            event.preventDefault(); if(target) select(target.item.key,true);
        }
        render();
        container.addEventListener('click',click); container.addEventListener('keydown',keydown);
        return { select, getSelection:()=>selected,
            update:next=>{ if(disposed) return; nodes=Array.isArray(next)?next:[]; render(); },
            destroy:()=>{ disposed=true;closeMenu();menu.remove();document.removeEventListener('pointerdown',outside);window.removeEventListener('resize',closeMenu);container.parentElement.removeEventListener('scroll',closeMenu,true);container.removeEventListener('click',click);container.removeEventListener('keydown',keydown);entries.clear();container.replaceChildren(); }
        };
    }
    // Render the complete sanitized draft to a self-contained PNG. This does not use or
    // mutate the live DOM: collapsed branches, selection and scroll positions stay intact.
    async function renderImage(nodes, { title = 'Task', taskId = '' } = {}) {
        await document.fonts.ready;
        const canvas = document.createElement('canvas'), context = canvas.getContext('2d');
        if (!context) throw new Error('The browser could not create the Task flow image.');
        const flat = [];
        function visit(items, depth = 0, inheritedDisabled = false, parent = null) {
            for (const item of items) {
                const row = { item, depth, disabled: inheritedDisabled || item.disabled, parent };
                flat.push(row);
                visit(item.children || [], depth + 1, row.disabled, row);
            }
        }
        visit(nodes);
        const width = 1040 + flat.reduce((depth, row) => Math.max(depth, row.depth), 0) * 24;
        const scale = 2, maxPixels = 64000000, maxSide = 32760;
        const font = (size, bold = false) => `${bold ? '700 ' : ''}${size}px "Segoe UI", Arial, sans-serif`;
        function wrap(text, available, size, bold = false) {
            context.font = font(size, bold);
            const lines = [];
            for (const paragraph of String(text || '').split(/\r?\n/)) {
                let line = '';
                for (const word of paragraph.split(/\s+/).filter(Boolean)) {
                    if (line && context.measureText(line + ' ' + word).width > available) { lines.push(line); line = ''; }
                    // Long paths and unbroken comments wrap without truncating characters.
                    if (context.measureText(word).width > available) {
                        for (const character of word) {
                            if (line && context.measureText(line + character).width > available) { lines.push(line); line = ''; }
                            line += character;
                        }
                    } else line += (line ? ' ' : '') + word;
                }
                lines.push(line);
            }
            return lines;
        }
        const heading = wrap(title || 'Task', width - 64, 22, true);
        let y = 66 + heading.length * 28;
        const startY = y; y += 56;
        for (const row of flat) {
            row.x = 32 + row.depth * 24;
            const available = width - row.x - 94;
            row.labels = wrap(row.item.label || 'Step', available, 15, true);
            row.summary = row.item.summary ? wrap(row.item.summary, available, 13) : [];
            row.status = [row.disabled ? (row.item.disabled ? 'Disabled' : 'Parent disabled') : '', row.item.kind === 'Unsupported' ? 'Read-only' : ''].filter(Boolean).join(' · ');
            row.y = y;
            row.height = 24 + row.labels.length * 21 + row.summary.length * 18 + (row.status ? 20 : 0);
            y += row.height + 12;
            if (y * scale > maxSide || width * y * scale * scale > maxPixels) throw new Error('This flow is too large for one PNG image. No image was downloaded.');
        }
        if (!flat.length) y += 40;
        const endY = y, height = y + 64;
        if (width * scale > maxSide || height * scale > maxSide || width * height * scale * scale > maxPixels) throw new Error('This flow is too large for one PNG image. No image was downloaded.');
        canvas.width = width * scale; canvas.height = height * scale;
        context.scale(scale, scale);
        context.fillStyle = '#ffffff'; context.fillRect(0, 0, width, height);
        context.textBaseline = 'top';
        function text(value, x, top, size, color, bold = false) {
            context.font = font(size, bold); context.fillStyle = color; context.fillText(value, x, top);
        }
        heading.forEach((line, index) => text(line, 32, 22 + index * 28, 22, '#1c2e42', true));
        text(`Task ID: ${taskId || 'New Task'} · ${new Date().toLocaleString()}`, 32, 28 + heading.length * 28, 12, '#526579');
        function box(x, top, w, h, radius, fill, stroke) {
            context.beginPath(); context.roundRect(x, top, w, h, radius);
            context.fillStyle = fill; context.fill(); context.strokeStyle = stroke; context.lineWidth = 1; context.stroke();
        }
        function terminal(label, top, color, fill) {
            box((width - 82) / 2, top, 82, 32, 16, fill, color);
            context.font = font(13, true);
            text(label, (width - context.measureText(label).width) / 2, top + 8, 13, color, true);
        }
        terminal('Start', startY, '#23784b', '#eaf5ef');
        // Branch guide lines preserve the same nested grouping as the editor tree.
        context.strokeStyle = '#aab9c7'; context.lineWidth = 1; context.setLineDash([3, 3]);
        for (const row of flat) if (row.parent) {
            context.beginPath(); context.moveTo(row.x - 10, row.parent.y + row.parent.height);
            context.lineTo(row.x - 10, row.y + 22); context.lineTo(row.x, row.y + 22); context.stroke();
        }
        context.setLineDash([]);
        for (const row of flat) {
            box(row.x, row.y, width - 32 - row.x, row.height, 8, '#f8fafc', '#ccd6e0');
            context.save(); context.translate(row.x + 14, row.y + 13); context.strokeStyle = '#3e8ac7'; context.lineWidth = 1.7; context.lineCap = 'round'; context.lineJoin = 'round';
            if (drawings[row.item.kind]) {
                for (const [tag, attributes] of drawings[row.item.kind]) {
                    if (tag === 'path') context.stroke(new Path2D(attributes.d));
                    else { context.beginPath(); context.arc(attributes.cx, attributes.cy, attributes.r, 0, Math.PI * 2); context.stroke(); }
                }
            } else text(symbols[row.item.kind] || '?', 2, 0, 19, '#3e8ac7');
            context.restore();
            let top = row.y + 12;
            for (const line of row.labels) { text(line, row.x + 50, top, 15, '#1c2e42', true); top += 21; }
            for (const line of row.summary) { text(line, row.x + 50, top, 13, '#526579'); top += 18; }
            if (row.status) text(row.status, row.x + 50, top + 3, 12, '#6c5362');
        }
        if (!flat.length) text('No steps.', 32, startY + 56, 13, '#526579');
        terminal('End', endY, '#bc3b3b', '#fce9e9');
        return await new Promise((resolve, reject) => canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('The browser could not encode the Task flow image.')), 'image/png'));
    }
    window.MauiAdvancedTaskFlow = { mount, renderImage };
})();
