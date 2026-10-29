// MAUI shared MOVEit Traditional Task filename/folder macros.
// Source of truth for Traditional Task Editor and Quick Transfer dropdowns.
(function (global) {
    'use strict';
    const fieldMacroDateComponents = [
        ['Date / Time', '[YYYY]', 'Year (4 digits)'],
        ['Date / Time', '[YY]', 'Year (2 digits)'],
        ['Date / Time', '[MM]', 'Month (2 digits)'],
        ['Date / Time', '[MMM]', 'Month (short name)'],
        ['Date / Time', '[MMMM]', 'Month (full name)'],
        ['Date / Time', '[DD]', 'Day (2 digits)'],
        ['Date / Time', '[AAA]', 'Day of week (short)'],
        ['Date / Time', '[AAAA]', 'Day of week (full)'],
        ['Date / Time', '[HH]', 'Hour (24-hour, 2 digits)'],
        ['Date / Time', '[TT]', 'Minute (2 digits)'],
        ['Date / Time', '[SS]', 'Second (2 digits)'],
        ['Date / Time', '[II]', 'AM/PM']
    ];
    const fieldMacroDatePatterns = [
        ['Common Date Patterns', '[YYYY][MM][DD]', 'YYYYMMDD'],
        ['Common Date Patterns', '[YYYY]-[MM]-[DD]', 'YYYY-MM-DD'],
        ['Common Date Patterns', '[MM][DD][YYYY]', 'MMDDYYYY'],
        ['Common Date Patterns', '[MM]-[DD]-[YYYY]', 'MM-DD-YYYY'],
        ['Common Date Patterns', '[YYYY][MM][DD]_[HH][TT][SS]', 'YYYYMMDD_HHMMSS'],
        ['Common Date Patterns', '[YYYY]-[MM]-[DD]_[HH]-[TT]-[SS]', 'YYYY-MM-DD_HH-MM-SS'],
        ['Common Date Patterns', '[MM][DD][YYYY][HH][TT][SS]', 'MMDDYYYYHHMMSS']
    ];
    const fieldMacroTaskValues = [
        ['Task / Value', '[TaskName]', 'Task name'],
        ['Task / Value', '[TaskID]', 'Task ID'],
        ['Task / Value', '[NominalStart]', 'Official task start time'],
        ['Task / Value', '[Rnd:8]', 'Random 8-digit number'],
        ['Task / Value', '[Parm:ParameterName]', 'Task/global parameter (edit ParameterName)']
    ];
    const fieldMacroFileValues = [
        ['File', '[OrigName]', 'Original filename'],
        ['File', '[OnlyName]', 'Original filename without extension'],
        ['File', '[OnlyExt]', 'Original extension'],
        ['File', '[FolderName]', 'Source folder name'],
        ['File', '[FullPath]', 'Original full source path and filename'],
        ['File', '[FileDateStamp]', 'Source file timestamp'],
        ['File', '[FileSize]', 'Source file size in bytes']
    ];
    const fieldMacroFilePatterns = [
        ['Filename Patterns', '[OnlyName]_[YYYY][MM][DD].[OnlyExt]', 'name_YYYYMMDD.ext'],
        ['Filename Patterns', '[OnlyName]_[MM][DD][YYYY].[OnlyExt]', 'name_MMDDYYYY.ext'],
        ['Filename Patterns', '[OnlyName]_[YYYY][MM][DD]_[HH][TT][SS].[OnlyExt]', 'name_YYYYMMDD_HHMMSS.ext'],
        ['Filename Patterns', '[OnlyName]_[MM][DD][YYYY]_[HH][TT][SS].[OnlyExt]', 'name_MMDDYYYY_HHMMSS.ext'],
        ['Filename Patterns', '[YYYY][MM][DD]_[OrigName]', 'YYYYMMDD_original-name'],
        ['Filename Patterns', '[YYYY]-[MM]-[DD]_[OrigName]', 'YYYY-MM-DD_original-name']
    ];

    function fieldMacrosForContext(context) {
        const values = [...fieldMacroDateComponents, ...fieldMacroDatePatterns, ...fieldMacroTaskValues];
        if (context === 'runtime-file' || context === 'destination-path') {
            values.push(...fieldMacroFileValues);
            if (context === 'runtime-file') values.push(...fieldMacroFilePatterns);
        }
        if (context === 'destination-path') {
            values.push(['File', '[RelativePath]', 'Relative source subdirectory']);
        }
        return values;
    }


    function populateSelect(select, context) {
        if (!select) return;
        select.replaceChildren();
        const first = document.createElement('option');
        first.value = '';
        first.textContent = 'Macros…';
        select.appendChild(first);
        let currentGroup = '';
        let group = null;
        fieldMacrosForContext(context).forEach(([category, macro, description]) => {
            if (category !== currentGroup) {
                group = document.createElement('optgroup');
                group.label = category;
                select.appendChild(group);
                currentGroup = category;
            }
            const option = document.createElement('option');
            option.value = macro;
            option.textContent = description ? `${macro} — ${description}` : macro;
            group.appendChild(option);
        });
    }
    global.MauiTaskFieldMacros = Object.freeze({
        getForContext: fieldMacrosForContext,
        populateSelect
    });
})(window);
