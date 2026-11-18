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
    function compileTaskSearch(input) {
        const query = input.trim();
        // Skip quoted strings and treat a join as syntax only between comparisons.
        const joins = [...query.matchAll(/"(?:[^"\\]|\\[\s\S])*"|\s+(AND|OR)(?=\s)/gi)].filter(match => match[1]);
        const alternatives = [[]];
        let start = 0;
        for (const [index, join] of joins.entries()) {
            const after = join.index + join[0].length;
            const next = query.slice(after, joins[index + 1]?.index).trim();
            const clause = query.slice(start, join.index).trim();
            if (!comparisonPattern.test(next) || !comparisonPattern.test(clause)) continue;
            alternatives.at(-1).push(compileTreeSearch(clause));
            if (join[1].toUpperCase() === 'OR') alternatives.push([]);
            start = after;
        }
        alternatives.at(-1).push(compileTreeSearch(query.slice(start)));
        // Each AND clause can match a different leaf of the same task.
        return leaves => alternatives.some(group => group.every(matchesLeaf => leaves.some(leaf => matchesLeaf(leaf, leaf.value))));
    }
    const pathText = (path, root = 'Task') => root + path.map(part => /^\d+$/.test(part) ? '[' + part + ']' : /^[A-Za-z_$][\w$]*$/.test(part) ? '.' + part : '[' + JSON.stringify(part) + ']').join('');
    window.MauiJsonSearch = { pathPattern, compileTreeSearch, compileTaskSearch, pathText };
})();
