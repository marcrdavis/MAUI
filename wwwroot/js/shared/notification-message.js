// MAUI reusable notification message primitives, matching Task Notifications.
// Parse detached fragments; never place unsanitized imported markup in the live DOM.
(() => {
    'use strict';
    const notificationMacros = [
        ['Task', '[TaskID]'], ['Task', '[TaskName]'], ['Task', '[TaskStatus]'],
        ['Errors', '[ErrorCodeTask]'], ['Errors', '[ErrorDescriptionTask]'], ['Errors', '[ErrorCodeFile]'], ['Errors', '[ErrorDescriptionFile]'],
        ['File', '[OrigName]'], ['File', '[OnlyName]'], ['File', '[OnlyExt]'], ['File', '[FullPath]'], ['File', '[DestFileName]'], ['File', '[DestFolderPath]'], ['File', '[FileSize]'], ['File', '[FileDateStamp]'],
        ['Run', '[NominalStart]'],
        ['Date/Time', '[yyyy]'], ['Date/Time', '[mm]'], ['Date/Time', '[dd]'], ['Date/Time', '[hh]'], ['Date/Time', '[tt]'], ['Date/Time', '[ss]']
    ];

    function buildNotificationMacroOptions(select) {
        if (!select) return;
        select.replaceChildren();
        const first = document.createElement('option'); first.value = ''; first.textContent = 'Macros…'; select.appendChild(first);
        let currentGroup = null; let group = null;
        notificationMacros.forEach(([category, macro]) => {
            if (category !== currentGroup) { group = document.createElement('optgroup'); group.label = category; select.appendChild(group); currentGroup = category; }
            const option = document.createElement('option'); option.value = macro; option.textContent = macro; group.appendChild(option);
        });
    }
    function sanitizeNotificationStyle(styleText) {
        const allowedProperties = new Set([
            'color', 'background-color', 'font-family', 'font-size', 'font-weight', 'font-style',
            'text-decoration', 'text-align', 'vertical-align', 'line-height', 'white-space',
            'width', 'min-width', 'max-width', 'height',
            'border', 'border-width', 'border-style', 'border-color', 'border-collapse', 'border-spacing',
            'padding', 'padding-top', 'padding-right', 'padding-bottom', 'padding-left',
            'margin', 'margin-top', 'margin-right', 'margin-bottom', 'margin-left'
        ]);
        const probe = document.createElement('span');
        probe.style.cssText = String(styleText || '');
        const safe = [];
        Array.from(probe.style).forEach(property => {
            const normalized = String(property || '').toLowerCase();
            if (!allowedProperties.has(normalized)) return;
            const value = String(probe.style.getPropertyValue(property) || '').trim();
            if (!value || /(?:url\s*\(|expression\s*\(|javascript:|@import|behavior\s*:)/i.test(value)) return;
            safe.push(`${normalized}: ${value}`);
        });
        return safe.join('; ');
    }

    function parseNotificationHtml(html) {
        // Parse into a detached template-context fragment so incomplete HTML email fragments
        // (including table rows/cells) retain their structure without touching the live DOM.
        const context = document.createElement('template');
        const range = document.createRange();
        range.selectNodeContents(context);
        return range.createContextualFragment(String(html || ''));
    }

    function sanitizeNotificationHtml(html) {
        const root = parseNotificationHtml(html);
        const allowed = new Set(['P','DIV','SPAN','BR','STRONG','B','EM','I','U','UL','OL','LI','A','TABLE','THEAD','TBODY','TFOOT','TR','TH','TD']);
        const numericAttributes = new Set(['colspan', 'rowspan', 'cellpadding', 'cellspacing', 'border']);
        const dimensionAttributes = new Set(['width', 'height']);
        const clean = node => {
            Array.from(node.children).forEach(child => {
                if (['SCRIPT','STYLE','IFRAME','OBJECT','EMBED','SVG','MATH'].includes(child.tagName)) { child.remove(); return; }
                clean(child);
                if (!allowed.has(child.tagName)) { child.replaceWith(...Array.from(child.childNodes)); return; }
                Array.from(child.attributes).forEach(attr => {
                    const name = attr.name.toLowerCase();
                    const value = String(attr.value || '').trim();
                    if (child.tagName === 'A' && name === 'href') {
                        if (!/^(https?:|mailto:)/i.test(value)) child.removeAttribute(attr.name);
                        return;
                    }
                    if (name === 'style') {
                        const safeStyle = sanitizeNotificationStyle(value);
                        if (safeStyle) child.setAttribute('style', safeStyle); else child.removeAttribute(attr.name);
                        return;
                    }
                    if (numericAttributes.has(name) && ['TABLE','TH','TD'].includes(child.tagName)) {
                        if (!/^\d{1,4}$/.test(value)) child.removeAttribute(attr.name);
                        return;
                    }
                    if (dimensionAttributes.has(name) && ['TABLE','TH','TD'].includes(child.tagName)) {
                        if (!/^\d{1,4}(?:px|%)?$/i.test(value)) child.removeAttribute(attr.name);
                        return;
                    }
                    if (name === 'align' && ['TABLE','TR','TH','TD','P','DIV'].includes(child.tagName)) {
                        if (!/^(left|center|right|justify)$/i.test(value)) child.removeAttribute(attr.name);
                        return;
                    }
                    if (name === 'valign' && ['TR','TH','TD'].includes(child.tagName)) {
                        if (!/^(top|middle|bottom|baseline)$/i.test(value)) child.removeAttribute(attr.name);
                        return;
                    }
                    child.removeAttribute(attr.name);
                });
            });
        };
        clean(root);

        const serialized = document.createElement('div');
        Array.from(root.childNodes).forEach(node => serialized.appendChild(document.importNode(node, true)));
        return serialized.innerHTML;
    }

    window.MauiNotificationMessage = Object.freeze({ sanitize: sanitizeNotificationHtml, populateMacros: buildNotificationMacroOptions,
        detectFormat: value => /<!doctype\s+html|<\/?(?:html|head|body|p|div|span|br|table|thead|tbody|tfoot|tr|td|th|a|b|i|u|strong|em|ul|ol|li|img|hr|h[1-6])(?:\s|\/?>)/i.test(String(value || '')) ? 'html' : 'text' });
})();
