/* Shared read-only JSON matching for the Task JSON Editor and Tasks/Hosts grids. */
(() => {
    'use strict';
    const comparisonPattern = /^(.+?)(?:\s+(contains)\s+|\s*(<=|>=|!=|=|<|>)\s*)(.*)$/i;
    function pathPattern(find) {
        const pattern=find.split('*').map(part=>part.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')).join('.*');
        return new RegExp('^'+pattern+'$','i');
    }
    // Compile once per tree search; comparisons never evaluate JavaScript or query the server.
    // Ordered comparisons are numeric (including numeric strings and Boolean flags).
    // Text equality and contains are case-insensitive; quoted strings allow an explicit empty value.
    function compileTreeSearch(input, root = 'Task') {
        const query=input.trim(), plain=query.toLowerCase();
        if(!query)return ()=>true;
        const expression=query.match(comparisonPattern);
        if(!expression){
            if(/^[<>=!]/.test(query)||/\scontains\s*$/i.test(query))throw new Error('Enter an element and value, for example DoIfFailure=1.');
            return (leaf,value)=>leaf.pathText.toLowerCase().includes(plain)||value!==null&&String(value).toLowerCase().includes(plain);
        }
        const field=expression[1].trim(), operator=(expression[2]||expression[3]).toLowerCase(), raw=expression[4].trim();
        if(!raw)throw new Error('Enter a comparison value, or "" for an empty string.');
        let expected=raw;
        if(raw.startsWith('"')){try{expected=JSON.parse(raw);if(typeof expected!=='string')throw new Error();}catch{throw new Error('Close the quoted comparison value with a double quote.');}}
        const fieldName=field.toLowerCase(),target=field.includes('.')||field.includes('[')?pathPattern(field.toLowerCase().startsWith(root.toLowerCase()+'.')||field.toLowerCase().startsWith(root.toLowerCase()+'[')?field:root+'.'+field):null;
        const numeric=value=>{
            if(typeof value==='boolean')return value?1:0;
            const valueText=String(value??'').trim();
            if(!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/i.test(valueText))return null;
            const number=Number(valueText);return Number.isFinite(number)&&Math.abs(number)<=Number.MAX_SAFE_INTEGER?number:null;
        };
        const expectedText=String(expected).toLowerCase(), expectedNumber=numeric(expected);
        return (leaf,value)=>{
            if(!(target?target.test(leaf.pathText):leaf.name.toLowerCase()===fieldName)||leaf.readOnly&&value===null)return false;
            const actualText=String(value).toLowerCase();
            if(operator==='contains')return actualText.includes(expectedText);
            const actualNumber=numeric(value), numbers=actualNumber!==null&&expectedNumber!==null;
            if(operator==='='||operator==='!='){const equal=numbers?actualNumber===expectedNumber:actualText===expectedText;return operator==='='?equal:!equal;}
            if(!numbers)return false;
            return operator==='<'?actualNumber<expectedNumber:operator==='<='?actualNumber<=expectedNumber:operator==='>'?actualNumber>expectedNumber:actualNumber>=expectedNumber;
        };
    }
    function collectionEnd(query, opening) {
        let depth = 1, quoted = false;
        for (let i = opening + 1; i < query.length; i++) {
            const character = query[i];
            if (quoted && character === '\\') { i++; continue; }
            if (character === '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (character === '(') depth++;
            if (character === ')' && --depth === 0) return i + 1;
        }
        throw new Error('Close the ANY or NONE condition with a parenthesis.');
    }
    function collectionCalls(query) {
        const calls = [], tokens = /"(?:[^"\\]|\\[\s\S])*"|\b(ANY|NONE)\s*\(/gi;
        let token;
        while ((token = tokens.exec(query))) {
            if (!token[1] || token.index !== 0 && !/\s+(?:AND|OR)\s+$/i.test(query.slice(0, token.index))) continue;
            const end = collectionEnd(query, tokens.lastIndex - 1);
            calls.push({ start: token.index, end });
            tokens.lastIndex = end;
        }
        return calls;
    }
    function collectionPath(selector) {
        // Parse path segments without evaluating code. A wildcard consumes one array
        // index, or no index for MOVEit's single-object collection representation.
        const tokens = [], pattern = /(?:^|\.)([A-Za-z_$][\w$]*)|\[\s*(\*|\d+|"(?:[^"\\]|\\[\s\S])*")\s*\]/g;
        let offset = 0, token;
        while ((token = pattern.exec(selector))) {
            if (token.index !== offset) break;
            const part = token[1] ?? token[2];
            tokens.push(token[1] ? part : part === '*' ? null : part.startsWith('"') ? JSON.parse(part) : part);
            offset = pattern.lastIndex;
        }
        if (!tokens.length || offset !== selector.length) throw new Error('Enter a collection path, for example NextActions.NextAction[*].');
        if (tokens[0]?.toLowerCase() === 'task') tokens.shift();
        if (!tokens.length) throw new Error('Select a collection inside the Task.');
        if (tokens.at(-1) !== null) tokens.push(null);
        return tokens;
    }
    function compileCollection(query) {
        const call = query.match(/^(ANY|NONE)\s*\(/i), opening = call[0].length - 1;
        const end = collectionEnd(query, opening);
        if (query.slice(end).trim()) throw new Error('Join ANY or NONE conditions with AND or OR.');
        let comma = -1, quoted = false, brackets = 0;
        for (let i = opening + 1; i < end - 1; i++) {
            const character = query[i];
            if (quoted && character === '\\') { i++; continue; }
            if (character === '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (character === '[') brackets++;
            if (character === ']') brackets--;
            if (character === ',' && brackets === 0) { comma = i; break; }
        }
        if (comma < 0) throw new Error('Separate the collection and its conditions with a comma.');
        const selector = collectionPath(query.slice(opening + 1, comma).trim());
        const condition = query.slice(comma + 1, end - 1).trim();
        if (!condition) throw new Error('Enter an element/value comparison inside ANY or NONE.');
        const matchesItem = compileTaskSearch(condition, true), none = call[1].toUpperCase() === 'NONE';
        return leaves => {
            const items = new Map();
            for (const leaf of leaves) {
                const path = leaf.path;
                if (!Array.isArray(path)) continue;
                let position = 0, matched = true;
                for (const token of selector) {
                    if (token === null) { if (/^\d+$/.test(path[position] ?? '')) position++; }
                    else if (path[position]?.toLowerCase() === token.toLowerCase()) position++;
                    else { matched = false; break; }
                }
                if (!matched || position >= path.length) continue;
                const key = JSON.stringify(path.slice(0, position)), relative = path.slice(position);
                if (!items.has(key)) items.set(key, []);
                items.get(key).push({ ...leaf, path: relative, pathText: pathText(relative) });
            }
            const any = [...items.values()].some(matchesItem);
            return none ? !any : any;
        };
    }
    function compileTaskSearch(input, withinCollection = false) {
        const query = input.trim(), calls = collectionCalls(query);
        if (withinCollection && calls.length) throw new Error('Use comparisons inside ANY or NONE; combine collection conditions outside the function.');
        if (withinCollection && [...query.matchAll(/"(?:[^"\\]|\\[\s\S])*"|\s+(AND|OR)$/gi)].some(match => match[1]))
            throw new Error('Enter a comparison after AND or OR.');
        // Preserve existing AND/OR precedence and literal-value handling. Joins
        // inside a collection condition belong to that item, not to the Task.
        const joins = [...query.matchAll(/"(?:[^"\\]|\\[\s\S])*"|\s+(AND|OR)(?=\s)/gi)]
            .filter(match => match[1] && !calls.some(call => match.index >= call.start && match.index < call.end));
        const isClause = text => comparisonPattern.test(text) || /^(ANY|NONE)\s*\(/i.test(text);
        const compileClause = text => {
            if (/^(ANY|NONE)\s*\(/i.test(text)) return compileCollection(text);
            if (withinCollection && !comparisonPattern.test(text)) throw new Error('Use element/value comparisons inside ANY or NONE.');
            const matchesLeaf = compileTreeSearch(text);
            return leaves => leaves.some(leaf => matchesLeaf(leaf, leaf.value));
        };
        const alternatives = [[]];
        let start = 0;
        for (const [index, join] of joins.entries()) {
            const after = join.index + join[0].length;
            const next = query.slice(after, joins[index + 1]?.index).trim();
            const clause = query.slice(start, join.index).trim();
            if (!isClause(next) || !isClause(clause)) continue;
            alternatives.at(-1).push(compileClause(clause));
            if (join[1].toUpperCase() === 'OR') alternatives.push([]);
            start = after;
        }
        alternatives.at(-1).push(compileClause(query.slice(start).trim()));
        // Each AND clause can match a different leaf of the same task.
        return leaves => alternatives.some(group => group.every(matchesDocument => matchesDocument(leaves)));
    }
    const pathText = (path, root = 'Task') => root + path.map(part => /^\d+$/.test(part) ? '[' + part + ']' : /^[A-Za-z_$][\w$]*$/.test(part) ? '.' + part : '[' + JSON.stringify(part) + ']').join('');
    window.MauiJsonSearch = { pathPattern, compileTreeSearch, compileTaskSearch, pathText };
})();
