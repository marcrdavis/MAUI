/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * Date/Time grid filters. Keep calendar/operator semantics synchronized with
 * Services/Reporting/GridDateFilterMacros.cs for scheduled Reports.
 */
(() => {
    const dayMilliseconds = 86400000;
    const englishMonths = { jan: 1, january: 1, feb: 2, february: 2, mar: 3, march: 3, apr: 4, april: 4, may: 5,
        jun: 6, june: 6, jul: 7, july: 7, aug: 8, august: 8, sep: 9, september: 9, oct: 10, october: 10, nov: 11, november: 11, dec: 12, december: 12 };

    function epochDayFromParts(year, month, day) {
        const utc = new Date(0);
        utc.setUTCFullYear(year, month - 1, day);
        utc.setUTCHours(0, 0, 0, 0);
        if (year < 1 || year > 9999 || utc.getUTCFullYear() !== year || utc.getUTCMonth() !== month - 1 || utc.getUTCDate() !== day) return null;
        return Math.floor(utc.getTime() / dayMilliseconds);
    }

    function partsFromEpochDay(epochDay) {
        const date = new Date(epochDay * dayMilliseconds);
        return { year: date.getUTCFullYear(), month: date.getUTCMonth() + 1, day: date.getUTCDate() };
    }

    function formatEpochDay(epochDay) {
        const parts = partsFromEpochDay(epochDay);
        return `${parts.month}/${parts.day}/${parts.year}`;
    }

    // Calendar filters use the application clock. Explicit-offset timestamps are converted
    // using the application's time zone, including historical DST, rather than the browser's.
    function createClock(serverLocalNow, timeZone = 'UTC', locale = 'en-US') {
        const match = String(serverLocalNow || '').match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})$/);
        const browserNow = new Date();
        const anchorMs = match
            ? Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]), Number(match[4]), Number(match[5]), Number(match[6]))
            : Date.UTC(browserNow.getFullYear(), browserNow.getMonth(), browserNow.getDate(), browserNow.getHours(), browserNow.getMinutes(), browserNow.getSeconds());
        const capturedAtMs = Date.now();
        let formatter;
        try {
            formatter = new Intl.DateTimeFormat('en-US', {
                timeZone, year: 'numeric', month: 'numeric', day: 'numeric',
                hour: 'numeric', minute: 'numeric', second: 'numeric', hourCycle: 'h23'
            });
        } catch {
            // A browser may not recognize a custom server time-zone identifier. Keep the
            // grid usable and fall back to its already-local display values, not browser time.
            formatter = null;
        }
        const months = { ...englishMonths };
        const dayPeriods = [];
        for (const hour of [1, 13]) {
            const period = new Intl.DateTimeFormat(locale || 'en-US', { hour: 'numeric', hour12: true, timeZone: 'UTC' })
                .formatToParts(new Date(Date.UTC(2000, 0, 1, hour))).find(part => part.type === 'dayPeriod')?.value;
            if (period) dayPeriods.push([period.replace(/\s+/g, ' ').toLowerCase(), hour === 1 ? 'AM' : 'PM']);
        }
        for (let month = 1; month <= 12; month++) {
            for (const style of ['short', 'long']) {
                for (const includeDay of [false, true]) {
                    const monthFormat = new Intl.DateTimeFormat(locale || 'en-US', { month: style, ...(includeDay ? { day: 'numeric' } : {}), timeZone: 'UTC' });
                    const sample = new Date(Date.UTC(2000, month - 1, 15));
                    const name = includeDay ? monthFormat.formatToParts(sample).find(part => part.type === 'month')?.value : monthFormat.format(sample);
                    if (name) months[name.toLowerCase().replace(/\.$/, '')] = month;
                }
            }
        }
        return {
            today: () => Math.floor((anchorMs + (Date.now() - capturedAtMs)) / dayMilliseconds),
            months,
            dayPeriods,
            toWallTime(instant) {
                if (!formatter) return null;
                const parts = Object.fromEntries(formatter.formatToParts(new Date(instant)).map(part => [part.type, part.value]));
                const day = epochDayFromParts(Number(parts.year), Number(parts.month), Number(parts.day));
                return day === null ? null : day * dayMilliseconds + Number(parts.hour) * 3600000
                    + Number(parts.minute) * 60000 + Number(parts.second) * 1000 + ((instant % 1000) + 1000) % 1000;
            }
        };
    }

    // Macros denote whole calendar periods, never a rolling number of days.
    function resolve(rawValue, clock) {
        let value = String(rawValue || '').trim().toLowerCase();
        if (!value || !clock?.today) return null;
        let negate = false;
        if (value.startsWith('not:') || value.startsWith('!')) {
            negate = true;
            value = value.slice(value.startsWith('not:') ? 4 : 1).trim();
        }
        const today = clock.today();
        const parts = partsFromEpochDay(today);
        let startDay, endDay;
        if (value === '[today]' || value === '[yesterday]' || value === '[tomorrow]') {
            startDay = today + (value === '[yesterday]' ? -1 : value === '[tomorrow]' ? 1 : 0);
            endDay = startDay + 1;
        } else if (value === '[thisweek]' || value === '[lastweek]') {
            const thisWeekStart = today - ((new Date(today * dayMilliseconds).getUTCDay() + 6) % 7);
            startDay = thisWeekStart - (value === '[lastweek]' ? 7 : 0);
            endDay = startDay + 7;
        } else if (['[thismonth]', '[lastmonth]', '[nextmonth]', '[nextquarter]', '[nextyear]'].includes(value)) {
            let monthIndex = parts.year * 12 + parts.month - 1;
            let count = 1;
            if (value === '[lastmonth]') monthIndex--;
            if (value === '[nextmonth]') monthIndex++;
            if (value === '[nextquarter]') { monthIndex = Math.floor(monthIndex / 3) * 3 + 3; count = 3; }
            if (value === '[nextyear]') { monthIndex = (parts.year + 1) * 12; count = 12; }
            startDay = epochDayFromParts(Math.floor(monthIndex / 12), monthIndex % 12 + 1, 1);
            const endIndex = monthIndex + count;
            endDay = epochDayFromParts(Math.floor(endIndex / 12), endIndex % 12 + 1, 1);
        } else {
            const offset = value.match(/^\[today([+-])(\d+)d\]$/i);
            if (!offset) return null;
            const days = Number(offset[2]);
            if (!Number.isInteger(days) || days > 36500) return null;
            startDay = today + (offset[1] === '-' ? -days : days);
            endDay = startDay + 1;
        }
        if (startDay === null || endDay === null || startDay >= endDay) return null;
        return { kind: 'date-macro', macro: value, startDay, endDay, negate };
    }

    // Parse MAUI's numeric, named-month and ISO dates without Date.parse's browser-local
    // timezone assumptions or its silent rollover of invalid calendar dates.
    function parseDateValue(rawValue, clock) {
        const text = String(rawValue || '').trim().replace(/^(?:Mon(?:day)?|Tue(?:sday)?|Wed(?:nesday)?|Thu(?:rsday)?|Fri(?:day)?|Sat(?:urday)?|Sun(?:day)?),?\s+/i, '');
        let match = text.match(/^(\d{4})-(\d{1,2})-(\d{1,2})(.*)$/);
        let year, month, day, tail;
        if (match) [, year, month, day, tail] = match;
        else if ((match = text.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})(.*)$/))) [, month, day, year, tail] = match;
        else if ((match = text.match(/^([\p{L}\p{N}.]+)\s+(\d{1,2}),?\s+(\d{4})(.*)$/u))) {
            [, month, day, year, tail] = match;
            month = clock?.months?.[month.toLowerCase().replace(/\.$/, '')] || englishMonths[month.toLowerCase().replace(/\.$/, '')];
        } else if ((match = text.match(/^(\d{1,2})\s+([\p{L}\p{N}.]+)\s+(\d{4})(.*)$/u))) {
            [, day, month, year, tail] = match;
            month = clock?.months?.[month.toLowerCase().replace(/\.$/, '')] || englishMonths[month.toLowerCase().replace(/\.$/, '')];
        } else return null;
        const epochDay = epochDayFromParts(Number(year), Number(month), Number(day));
        if (epochDay === null) return null;
        if (!tail.trim()) return { start: epochDay * dayMilliseconds, end: (epochDay + 1) * dayMilliseconds, range: true };
        tail = tail.replace(/\s+/g, ' ');
        for (const [period, replacement] of clock?.dayPeriods || []) {
            if (tail.toLowerCase().endsWith(period)) {
                tail = tail.slice(0, -period.length) + replacement;
                break;
            }
        }
        match = tail.match(/^[T\s]+(\d{1,2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?\s*(AM|PM)?\s*(Z|[+-]\d{2}:?\d{2})?$/i);
        if (!match) return null;
        let hours = Number(match[1]);
        const minutes = Number(match[2]), seconds = Number(match[3] || 0);
        if (minutes > 59 || seconds > 59 || (match[5] ? hours < 1 || hours > 12 : hours > 23)) return null;
        if (match[5]) hours = hours % 12 + (match[5].toUpperCase() === 'PM' ? 12 : 0);
        let start = epochDay * dayMilliseconds + hours * 3600000 + minutes * 60000 + seconds * 1000 + Number((match[4] || '').padEnd(3, '0').slice(0, 3));
        if (match[6]) {
            const zone = match[6].toUpperCase().replace(':', '');
            const zoneHours = zone === 'Z' ? 0 : Number(zone.slice(1, 3));
            const zoneMinutes = zone === 'Z' ? 0 : Number(zone.slice(3));
            if (zoneHours > 14 || zoneMinutes > 59 || (zoneHours === 14 && zoneMinutes !== 0)) return null;
            const offset = (zoneHours * 60 + zoneMinutes) * (zone.startsWith('-') ? -1 : 1);
            start = clock?.toWallTime ? clock.toWallTime(start - offset * 60000) : start - offset * 60000;
        }
        return start === null ? null : { start, end: start, range: false };
    }

    function parseFilter(rawValue, clock) {
        let value = String(rawValue || '').trim();
        let negate = false;
        if (/^(not:|!)/i.test(value)) {
            negate = true;
            value = value.replace(/^(not:|!)\s*/i, '');
        }
        const comparison = value.match(/^(<=|>=|<|>)\s*/);
        const operator = comparison?.[1] || '';
        if (comparison) value = value.slice(comparison[0].length);
        const macro = resolve(value, clock);
        const date = macro
            ? { start: macro.startDay * dayMilliseconds, end: macro.endDay * dayMilliseconds, range: true }
            : parseDateValue(value, clock);
        return date ? { kind: 'date-filter', ...date, operator, negate, macro: macro?.macro } : null;
    }

    // Commas separating month/day from year belong to the date, not the OR list.
    function splitAlternatives(rawValue) {
        const value = String(rawValue || '').trim();
        if (!value) return [];
        const parts = [];
        for (const term of value.split(/\s+or\s+/i)) {
            let start = 0;
            for (let index = 0; index < term.length; index++) {
                if (term[index] !== ',') continue;
                const left = term.slice(start, index), right = term.slice(index + 1);
                if (/[\p{L}.]+\s+\d{1,2}$/u.test(left) && /^\s*\d{4}\b/.test(right)) continue;
                if (/\b(?:Mon(?:day)?|Tue(?:sday)?|Wed(?:nesday)?|Thu(?:rsday)?|Fri(?:day)?|Sat(?:urday)?|Sun(?:day)?)$/i.test(left)
                    && /^\s*[\p{L}.]+\s+\d/u.test(right)) continue;
                parts.push(term.slice(start, index).trim());
                start = index + 1;
            }
            parts.push(term.slice(start).trim());
        }
        return parts.length > 1 && parts.every(Boolean) ? parts : [value];
    }

    function matches(cell, filter, clock) {
        let date = null;
        for (const text of [cell?.dataset?.value, cell?.textContent]) {
            date = parseDateValue(text, clock);
            if (date) break;
        }
        const start = filter.kind === 'date-macro' ? filter.startDay * dayMilliseconds : filter.start;
        const end = filter.kind === 'date-macro' ? filter.endDay * dayMilliseconds : filter.end;
        const range = filter.kind === 'date-macro' || filter.range;
        let result = false;
        if (date) {
            switch (filter.operator || '') {
                case '<': result = date.start < start; break;
                case '<=': result = range ? date.start < end : date.start <= start; break;
                case '>': result = range ? date.start >= end : date.start > start; break;
                case '>=': result = date.start >= start; break;
                default: result = range ? date.start >= start && date.start < end : date.start === start;
            }
        }
        return filter.negate ? !result : result;
    }

    function updateHint(input, table, clock) {
        const key = String(input?.dataset.columnFilter || '').trim();
        const escaped = window.CSS?.escape ? window.CSS.escape(key) : key.replace(/"/g, '\\"');
        const header = key ? table?.querySelector(`.column-header-row th[data-column="${escaped}"]`) : null;
        const filter = header?.dataset.type === 'datetime' ? parseFilter(input.value, clock) : null;
        if (!filter?.macro) {
            input?.removeAttribute('title');
            if (input?.dataset) delete input.dataset.dateMacro;
            return;
        }
        const rangeText = `${formatEpochDay(filter.start / dayMilliseconds)} through ${formatEpochDay(filter.end / dayMilliseconds - 1)}`;
        const meaning = { '<': 'Before', '<=': 'Through', '>': 'After', '>=': 'On or after', '': 'Match' }[filter.operator];
        input.dataset.dateMacro = filter.macro;
        input.title = `${filter.negate ? 'Exclude: ' : ''}${meaning} ${rangeText} using the MAUI server date (${filter.macro}).`;
    }

    window.MauiGridDateMacros = Object.freeze({ createClock, resolve, parseFilter, splitAlternatives, matches, updateHint });
})();
