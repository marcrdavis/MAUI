/* One sorting policy for all Task folder/bucket/container/library browser modes. */
(() => {
    const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
    const columns = new Set(['name', 'size', 'lastModified']);
    function size(value) {
        const match = String(value ?? '').trim().replaceAll(',', '').match(/^(\d+(?:\.\d+)?)\s*(B|[KMGTPE]I?B)?$/i);
        if (!match) return null;
        const exponent = Math.max(0, 'BKMGTPE'.indexOf((match[2] || 'B')[0].toUpperCase()));
        return Number(match[1]) * 1024 ** exponent;
    }
    function key(item, column) {
        const text = String(item[column] ?? '').trim();
        if (!text) return null;
        if (column === 'size') return size(text);
        if (column === 'lastModified') { const parsed = Date.parse(text); return Number.isFinite(parsed) ? parsed : null; }
        return text;
    }
    function next(state, column) {
        if (!columns.has(column)) return state;
        if (state.column !== column) return { column, direction: 'asc' };
        if (state.direction === 'asc') return { column, direction: 'desc' };
        return { column: '', direction: '' };
    }
    function apply(items, state) {
        if (!columns.has(state.column) || !state.direction) return items.slice();
        return items.map((item, index) => ({ item, index, value: key(item, state.column) })).sort((a, b) => {
            if (a.value === null || b.value === null) return a.value === b.value ? a.index - b.index : a.value === null ? 1 : -1;
            const comparison = state.column === 'name' ? collator.compare(a.value, b.value) : a.value - b.value;
            return comparison * (state.direction === 'desc' ? -1 : 1) || a.index - b.index;
        }).map(entry => entry.item);
    }
    window.MauiFolderBrowserSorting = { next, apply };
})();
