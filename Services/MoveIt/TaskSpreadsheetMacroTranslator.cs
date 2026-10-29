// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Macro translation adapted from the proven ImportTasksFromCSV project.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MoveItAutomationUI.Services.MoveIt
{
    /// <summary>
    /// Translates known external filename macro markers (e.g., [0004]) and naked
    /// date/time format strings into MOVEit Automation macros.
    ///
    /// MOVEit Automation macros are bracketed tokens, for example: [YYYY][MM][DD].
    /// </summary>
    internal static class TaskSpreadsheetMacroTranslator
    {
        // Pattern code -> MOVEit Automation macro string
        // MOVEit token reference: Macro Date and Time Syntax (minutes are T/TT, seconds S/SS, etc.)
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 0001-0009
            { "[0001]", "[mm][dd][yy]" },                         // mmddyy
            { "[0002]", "[mm][dd][yy]" },                         // mmddyy (duplicate)
            { "[0003]", "[yyyy][mm][dd]" },                       // yyyymmdd
            { "[0004]", "[yyyy][mm][dd]" },                       // yyyymmdd (duplicate)
            { "[0005]", "[yy][dd][mm]" },                         // YYDDMM
            { "[0006]", "[mm]-[dd]-[yy]" },                       // mm-dd-yy
            { "[0007]", "[mm][dd][yyyy]_[hh]_[tt]_[ss]" },        // MMDDYYYY_HH24_MI_SS
            { "[0008]", "[mm][dd][yyyy]" },                       // MMDDYYYY
            { "[0009]", "[yy][mm][dd]" },                         // YYMMDD

            // 0010-0019
            { "[0010]", "[yyyy][mm][dd]_[hh][tt][ss]" },          // YYYYMMDD_HH24MISS
            { "[0011]", "[ss][tt]" },                             // SSMI (seconds+minutes; closest)
            { "[0012]", "[mmmm]" },                               // FMMONTH (no FM in MOVEit)
            { "[0013]", "[hh][tt][ss][yyyy][mm][dd]" },           // HHmmssYYYYMMDD
            { "[0014]", "[mm][yy]" },                             // MMYY
            { "[0015]", "[kkk]" },                                // DDD (day-of-year; 1-based)
            { "[0016]", "[dd][mm][yyyy]" },                       // DDMMYYYY
            { "[0017]", "[mmmm] [yyyy]" },                        // FMMONTH YYYY
            { "[0018]", "[yyyy][mm]" },                           // YYYYMM
            { "[0019]", "[mm]_[yy]" },                            // MM_YY

            // 0020-0029
            { "[0020]", "[yy][mm]" },                             // YYMM
            { "[0021]", "[mmm]_[dd]_[yyyy]" },                    // FMMM_DD_YYYY
            { "[0022]", "[yyyy][mm][dd][hh][tt]" },               // yyyymmddHH24MI
            { "[0023]", "[mmm]" },                                // MON
            { "[0024]", "[yyyy][mm][dd][hh]" },                   // yyyymmddHH
            { "[0025]", "[mm]-[dd]-[yyyy]" },                     // MM-DD-YYYY
            { "[0026]", "[mmm][yy]" },                            // MONYY
            { "[0027]", "[yyyy]-[mm]-[dd]" },                     // YYYY-MM-DD
            { "[0028]", "[yyyy]_[mm]" },                          // YYYY_MM
            { "[0029]", "[yyyy]-[mm]-[dd]-[hh]-[tt]-[ss]" },      // YYYY-MM-DD-HH24-MI-SS

            // 0030-0038
            { "[0030]", "[xx]" },                                 // IW (ISO week). Closest: week-of-year, 1-based. (May differ from ISO)
            { "[0031]", "[mmmm] [dd]" },                          // FMMONTH DD
            { "[0032]", "[mmmm][dd]" },                           // FMMONTHDD
            { "[0033]", "[mmm][dd]_[yyyy]" },                     // FMMONDD_YYYY
            { "[0034]", "[yyyy]_[mm]_[dd]" },                     // YYYY_MM_DD
            { "[0035]", "[mm][dd][yyyy][hh][tt][ss]-[mm][dd]" },  // MMDDYYYYHHMISS-MMDD
            { "[0036]", "[mm][dd][yyyy][hh][tt][ss]" },           // MMDDYYYYHHMISS
            { "[0037]", "[yyyy]_[mm]_[dd]_[hh]_[tt]_[ss]" },      // YYYY_MM_DD_HH24_MI_SS
            { "[0038]", "[yyyy][mm][dd]" },                       // YYYYfmMMDD (no FM; treat as YYYYMMDD)

            // 0040-0048 (note: 0039 and 0049 are not in your list)
            { "[0040]", "[mm][yyyy]" },                           // MMYYYY
            { "[0041]", "[yyyy][mm][dd]" },                       // YYYYMMfmDD (no FM; treat as YYYYMMDD)
            { "[0042]", "[yyyy][mm][dd][hh][tt][ss]" },           // YYYYMMDDHHMISS
            { "[0043]", "[dd][mm][yy]" },                         // DDMMYY
            { "[0044]", "[yyyy][mm][dd]" },                       // fmyyyymmdd (no FM; treat as YYYYMMDD)
            { "[0045]", "[mm]_[yyyy]" },                          // MM_YYYY
            { "[0046]", "[dd]TH [mmmm] [yyyy]" },                 // 'FMDDTH" "MONTH" "YYYY' (ordinal not supported; 'TH' is literal)
            { "[0047]", "[mmm][yyyy]" },                          // MonYYYY
            { "[0048]", "[yyyy][mm][dd][hh][tt]" },               // YYYYMMDDHHMI

            // 0050-0059
            { "[0050]", "[mmm]_[yyyy]" },                         // MON_YYYY
            { "[0051]", "[yyyy][mm][dd][hh]" },                   // yyyymmddHH24
            { "[0052]", "[mm][dd][yyyy]_[hh][tt][ss]" },          // MMDDYYYY_HHMISS
            { "[0053]", "[yyyy]-[mm]-[dd]" },                     // YYYY-MM-DD (duplicate of 0027)
            { "[0054]", "[yyyy][mm][dd]_[hh]" },                  // yyyymmdd_HH24
            { "[0055]", "[yyyy][mm][dd]_[hh][tt]" },              // yyyymmdd_HH24MM (MM here means minutes in source; MOVEit minutes is TT)
            { "[0056]", "[yyyy][mm][dd]_[hh]" },                  // yyyymmdd_HH24 (duplicate)
            { "[0057]", "[dd].[mm].[yyyy]" },                     // DD.MM.YYYY
            { "[0058]", "[mm].[dd].[yyyy]" },                     // MM.DD.YYYY
            { "[0059]", "[mm]_[dd]_[yyyy]" },                     // MM_DD_YYYY

            // 0060-0067
            { "[0060]", "[dd][mmm][yy]" },                        // DDMonYY
            { "[0061]", "[yyyy][mm][dd][hh][tt][ss]" },           // YYYYMMDDHH24MISS
            { "[0062]", "[yyyy][mm][dd][hh][tt][ss]" },           // YYYYMMDDHH24MISS (duplicate)
            { "[0063]", "[mmm][dd][yyyy]" },                      // MonFMddYYYY (no FM; treat as MonDDYYYY)
            { "[0064]", "[mmm][dd][yyyy]" },                      // MonddYYYY
            { "[0065]", "[mmm][dd][yyyy]" },                      // FMMMDDYYYY
            { "[0066]", "[dd]-[mm]-[yyyy]" },                     // DD-MM-YYYY
            { "[0067]", "[mm] [yyyy]" },                          // MM YYYY
        };

        private static readonly List<MacroPattern> NakedPatterns = new List<MacroPattern>
        {
            // Combined date + time patterns. In MOVEit date/time syntax, TT is minute; MM is month.
            new MacroPattern("YYYYMMDDHH24MISS", "[YYYY][MM][DD][HH][TT][SS]"),
            new MacroPattern("YYYYMMDDHH24MMSS", "[YYYY][MM][DD][HH][TT][SS]"),
            new MacroPattern("YYYYMMDDHHMISS", "[YYYY][MM][DD][HH][TT][SS]"),
            new MacroPattern("YYYYMMDDHHMMSS", "[YYYY][MM][DD][HH][TT][SS]"),
            new MacroPattern("YYYYMMDDHH24MI", "[YYYY][MM][DD][HH][TT]"),
            new MacroPattern("YYYYMMDDHH24MM", "[YYYY][MM][DD][HH][TT]"),
            new MacroPattern("YYYYMMDDHHMI", "[YYYY][MM][DD][HH][TT]"),
            new MacroPattern("YYYYMMDDHHMM", "[YYYY][MM][DD][HH][TT]"),
            new MacroPattern("YYYYMMDD-HH24MISS", "[YYYY][MM][DD]-[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD_HH24MISS", "[YYYY][MM][DD]_[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD.HH24MISS", "[YYYY][MM][DD].[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD-HH24MMSS", "[YYYY][MM][DD]-[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD_HH24MMSS", "[YYYY][MM][DD]_[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD.HH24MMSS", "[YYYY][MM][DD].[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD-HHMISS", "[YYYY][MM][DD]-[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD_HHMISS", "[YYYY][MM][DD]_[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD.HHMISS", "[YYYY][MM][DD].[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD-HHMMSS", "[YYYY][MM][DD]-[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD_HHMMSS", "[YYYY][MM][DD]_[HH][TT][SS]"),
            new MacroPattern("YYYYMMDD.HHMMSS", "[YYYY][MM][DD].[HH][TT][SS]"),
            new MacroPattern("YYYY-MM-DD-HH24-MI-SS", "[YYYY]-[MM]-[DD]-[HH]-[TT]-[SS]"),
            new MacroPattern("YYYY_MM_DD_HH24_MI_SS", "[YYYY]_[MM]_[DD]_[HH]_[TT]_[SS]"),
            new MacroPattern("YYYY.MM.DD.HH24.MI.SS", "[YYYY].[MM].[DD].[HH].[TT].[SS]"),
            new MacroPattern("YYYY-MM-DD-HH24-MM-SS", "[YYYY]-[MM]-[DD]-[HH]-[TT]-[SS]"),
            new MacroPattern("YYYY_MM_DD_HH24_MM_SS", "[YYYY]_[MM]_[DD]_[HH]_[TT]_[SS]"),
            new MacroPattern("YYYY.MM.DD.HH24.MM.SS", "[YYYY].[MM].[DD].[HH].[TT].[SS]"),
            new MacroPattern("YYYY-MM-DD-HH-MI-SS", "[YYYY]-[MM]-[DD]-[HH]-[TT]-[SS]"),
            new MacroPattern("YYYY_MM_DD_HH_MI_SS", "[YYYY]_[MM]_[DD]_[HH]_[TT]_[SS]"),
            new MacroPattern("YYYY.MM.DD.HH.MI.SS", "[YYYY].[MM].[DD].[HH].[TT].[SS]"),
            new MacroPattern("YYYY-MM-DD-HH-MM-SS", "[YYYY]-[MM]-[DD]-[HH]-[TT]-[SS]"),
            new MacroPattern("YYYY_MM_DD_HH_MM_SS", "[YYYY]_[MM]_[DD]_[HH]_[TT]_[SS]"),
            new MacroPattern("YYYY.MM.DD.HH.MM.SS", "[YYYY].[MM].[DD].[HH].[TT].[SS]"),
            new MacroPattern("YYYY-MM-DD_HH24-MI-SS", "[YYYY]-[MM]-[DD]_[HH]-[TT]-[SS]"),
            new MacroPattern("YYYY-MM-DD_HH24-MM-SS", "[YYYY]-[MM]-[DD]_[HH]-[TT]-[SS]"),
            new MacroPattern("YYYY-MM-DD_HH-MI-SS", "[YYYY]-[MM]-[DD]_[HH]-[TT]-[SS]"),
            new MacroPattern("YYYY-MM-DD_HH-MM-SS", "[YYYY]-[MM]-[DD]_[HH]-[TT]-[SS]"),
            new MacroPattern("YYYYMMDD-HH24MI", "[YYYY][MM][DD]-[HH][TT]"),
            new MacroPattern("YYYYMMDD_HH24MI", "[YYYY][MM][DD]_[HH][TT]"),
            new MacroPattern("YYYYMMDD.HH24MI", "[YYYY][MM][DD].[HH][TT]"),
            new MacroPattern("YYYYMMDD-HH24MM", "[YYYY][MM][DD]-[HH][TT]"),
            new MacroPattern("YYYYMMDD_HH24MM", "[YYYY][MM][DD]_[HH][TT]"),
            new MacroPattern("YYYYMMDD.HH24MM", "[YYYY][MM][DD].[HH][TT]"),
            new MacroPattern("YYYYMMDD-HHMI", "[YYYY][MM][DD]-[HH][TT]"),
            new MacroPattern("YYYYMMDD_HHMI", "[YYYY][MM][DD]_[HH][TT]"),
            new MacroPattern("YYYYMMDD.HHMI", "[YYYY][MM][DD].[HH][TT]"),
            new MacroPattern("YYYYMMDD-HHMM", "[YYYY][MM][DD]-[HH][TT]"),
            new MacroPattern("YYYYMMDD_HHMM", "[YYYY][MM][DD]_[HH][TT]"),
            new MacroPattern("YYYYMMDD.HHMM", "[YYYY][MM][DD].[HH][TT]"),

            // Date patterns.
            new MacroPattern("YYYY-MMMM-DD", "[YYYY]-[MMMM]-[DD]"),
            new MacroPattern("YYYY_MMMM_DD", "[YYYY]_[MMMM]_[DD]"),
            new MacroPattern("YYYY.MMMM.DD", "[YYYY].[MMMM].[DD]"),
            new MacroPattern("YYYY MMMM DD", "[YYYY] [MMMM] [DD]"),
            new MacroPattern("YYYYMMMMDD", "[YYYY][MMMM][DD]"),
            new MacroPattern("YYYY-MMM-DD", "[YYYY]-[MMM]-[DD]"),
            new MacroPattern("YYYY_MMM_DD", "[YYYY]_[MMM]_[DD]"),
            new MacroPattern("YYYY.MMM.DD", "[YYYY].[MMM].[DD]"),
            new MacroPattern("YYYY MMM DD", "[YYYY] [MMM] [DD]"),
            new MacroPattern("YYYYMMMDD", "[YYYY][MMM][DD]"),
            new MacroPattern("DD-MMMM-YYYY", "[DD]-[MMMM]-[YYYY]"),
            new MacroPattern("DD_MMMM_YYYY", "[DD]_[MMMM]_[YYYY]"),
            new MacroPattern("DD.MMMM.YYYY", "[DD].[MMMM].[YYYY]"),
            new MacroPattern("DD MMMM YYYY", "[DD] [MMMM] [YYYY]"),
            new MacroPattern("DDMMMMYYYY", "[DD][MMMM][YYYY]"),
            new MacroPattern("DD-MMM-YYYY", "[DD]-[MMM]-[YYYY]"),
            new MacroPattern("DD_MMM_YYYY", "[DD]_[MMM]_[YYYY]"),
            new MacroPattern("DD.MMM.YYYY", "[DD].[MMM].[YYYY]"),
            new MacroPattern("DD MMM YYYY", "[DD] [MMM] [YYYY]"),
            new MacroPattern("DDMMMYYYY", "[DD][MMM][YYYY]"),
            new MacroPattern("MMM-DD-YYYY", "[MMM]-[DD]-[YYYY]"),
            new MacroPattern("MMM_DD_YYYY", "[MMM]_[DD]_[YYYY]"),
            new MacroPattern("MMM.DD.YYYY", "[MMM].[DD].[YYYY]"),
            new MacroPattern("MMM DD YYYY", "[MMM] [DD] [YYYY]"),
            new MacroPattern("MMMDDYYYY", "[MMM][DD][YYYY]"),
            new MacroPattern("MMMM-DD-YYYY", "[MMMM]-[DD]-[YYYY]"),
            new MacroPattern("MMMM_DD_YYYY", "[MMMM]_[DD]_[YYYY]"),
            new MacroPattern("MMMM.DD.YYYY", "[MMMM].[DD].[YYYY]"),
            new MacroPattern("MMMM DD YYYY", "[MMMM] [DD] [YYYY]"),
            new MacroPattern("MMMMDDYYYY", "[MMMM][DD][YYYY]"),
            new MacroPattern("DD-MMM-YY", "[DD]-[MMM]-[YY]"),
            new MacroPattern("DD_MMM_YY", "[DD]_[MMM]_[YY]"),
            new MacroPattern("DD.MMM.YY", "[DD].[MMM].[YY]"),
            new MacroPattern("DDMMMYY", "[DD][MMM][YY]"),
            new MacroPattern("MMM-DD-YY", "[MMM]-[DD]-[YY]"),
            new MacroPattern("MMM_DD_YY", "[MMM]_[DD]_[YY]"),
            new MacroPattern("MMM.DD.YY", "[MMM].[DD].[YY]"),
            new MacroPattern("MMMDDYY", "[MMM][DD][YY]"),
            new MacroPattern("YYYY-MMMM", "[YYYY]-[MMMM]"),
            new MacroPattern("YYYY_MMMM", "[YYYY]_[MMMM]"),
            new MacroPattern("YYYY.MMMM", "[YYYY].[MMMM]"),
            new MacroPattern("YYYYMMMM", "[YYYY][MMMM]"),
            new MacroPattern("YYYY-MMM", "[YYYY]-[MMM]"),
            new MacroPattern("YYYY_MMM", "[YYYY]_[MMM]"),
            new MacroPattern("YYYY.MMM", "[YYYY].[MMM]"),
            new MacroPattern("YYYYMMM", "[YYYY][MMM]"),
            new MacroPattern("MMM-YYYY", "[MMM]-[YYYY]"),
            new MacroPattern("MMM_YYYY", "[MMM]_[YYYY]"),
            new MacroPattern("MMM.YYYY", "[MMM].[YYYY]"),
            new MacroPattern("MMMYYYY", "[MMM][YYYY]"),
            new MacroPattern("MMMM-YYYY", "[MMMM]-[YYYY]"),
            new MacroPattern("MMMM_YYYY", "[MMMM]_[YYYY]"),
            new MacroPattern("MMMM.YYYY", "[MMMM].[YYYY]"),
            new MacroPattern("MMMMYYYY", "[MMMM][YYYY]"),
            new MacroPattern("YYYY-MM-DD", "[YYYY]-[MM]-[DD]"),
            new MacroPattern("YYYY_MM_DD", "[YYYY]_[MM]_[DD]"),
            new MacroPattern("YYYY.MM.DD", "[YYYY].[MM].[DD]"),
            new MacroPattern("YYYYMMDD", "[YYYY][MM][DD]"),
            new MacroPattern("YYYY-MM", "[YYYY]-[MM]"),
            new MacroPattern("YYYY_MM", "[YYYY]_[MM]"),
            new MacroPattern("YYYY.MM", "[YYYY].[MM]"),
            new MacroPattern("YYYYMM", "[YYYY][MM]"),
            new MacroPattern("YY-MM-DD", "[YY]-[MM]-[DD]"),
            new MacroPattern("YY_MM_DD", "[YY]_[MM]_[DD]"),
            new MacroPattern("YY.MM.DD", "[YY].[MM].[DD]"),
            new MacroPattern("YYMMDD", "[YY][MM][DD]"),
            new MacroPattern("MM-DD-YYYY", "[MM]-[DD]-[YYYY]"),
            new MacroPattern("MM_DD_YYYY", "[MM]_[DD]_[YYYY]"),
            new MacroPattern("MM.DD.YYYY", "[MM].[DD].[YYYY]"),
            new MacroPattern("MMDDYYYY", "[MM][DD][YYYY]"),
            new MacroPattern("MM-DD-YY", "[MM]-[DD]-[YY]"),
            new MacroPattern("MM_DD_YY", "[MM]_[DD]_[YY]"),
            new MacroPattern("MM.DD.YY", "[MM].[DD].[YY]"),
            new MacroPattern("MMDDYY", "[MM][DD][YY]"),
            new MacroPattern("DD-MM-YYYY", "[DD]-[MM]-[YYYY]"),
            new MacroPattern("DD_MM_YYYY", "[DD]_[MM]_[YYYY]"),
            new MacroPattern("DD.MM.YYYY", "[DD].[MM].[YYYY]"),
            new MacroPattern("DDMMYYYY", "[DD][MM][YYYY]"),
            new MacroPattern("DD-MM-YY", "[DD]-[MM]-[YY]"),
            new MacroPattern("DD_MM_YY", "[DD]_[MM]_[YY]"),
            new MacroPattern("DD.MM.YY", "[DD].[MM].[YY]"),
            new MacroPattern("DDMMYY", "[DD][MM][YY]"),

            // Time patterns. MI and MM are both accepted as input minute markers here;
            // both translate to MOVEit's [TT] minute attribute.
            new MacroPattern("HH24-MI-SS", "[HH]-[TT]-[SS]"),
            new MacroPattern("HH24_MI_SS", "[HH]_[TT]_[SS]"),
            new MacroPattern("HH24.MI.SS", "[HH].[TT].[SS]"),
            new MacroPattern("HH24-MM-SS", "[HH]-[TT]-[SS]"),
            new MacroPattern("HH24_MM_SS", "[HH]_[TT]_[SS]"),
            new MacroPattern("HH24.MM.SS", "[HH].[TT].[SS]"),
            new MacroPattern("HH-MI-SS", "[HH]-[TT]-[SS]"),
            new MacroPattern("HH_MI_SS", "[HH]_[TT]_[SS]"),
            new MacroPattern("HH.MI.SS", "[HH].[TT].[SS]"),
            new MacroPattern("HH-MM-SS", "[HH]-[TT]-[SS]"),
            new MacroPattern("HH_MM_SS", "[HH]_[TT]_[SS]"),
            new MacroPattern("HH.MM.SS", "[HH].[TT].[SS]"),
            new MacroPattern("HH24MISS", "[HH][TT][SS]"),
            new MacroPattern("HH24MMSS", "[HH][TT][SS]"),
            new MacroPattern("HHMISS", "[HH][TT][SS]"),
            new MacroPattern("HHMMSS", "[HH][TT][SS]"),
            new MacroPattern("HH:MI:SS", "[HH]:[TT]:[SS]"),
            new MacroPattern("HH:MM:SS", "[HH]:[TT]:[SS]"),
            new MacroPattern("HH24-MI", "[HH]-[TT]"),
            new MacroPattern("HH24_MI", "[HH]_[TT]"),
            new MacroPattern("HH24.MI", "[HH].[TT]"),
            new MacroPattern("HH24-MM", "[HH]-[TT]"),
            new MacroPattern("HH24_MM", "[HH]_[TT]"),
            new MacroPattern("HH24.MM", "[HH].[TT]"),
            new MacroPattern("HH-MI", "[HH]-[TT]"),
            new MacroPattern("HH_MI", "[HH]_[TT]"),
            new MacroPattern("HH.MI", "[HH].[TT]"),
            new MacroPattern("HH-MM", "[HH]-[TT]"),
            new MacroPattern("HH_MM", "[HH]_[TT]"),
            new MacroPattern("HH.MM", "[HH].[TT]"),
            new MacroPattern("HH24MI", "[HH][TT]"),
            new MacroPattern("HH24MM", "[HH][TT]"),
            new MacroPattern("HHMI", "[HH][TT]"),
            new MacroPattern("HHMM", "[HH][TT]"),
            new MacroPattern("HH:MI", "[HH]:[TT]"),
            new MacroPattern("HH:MM", "[HH]:[TT]")
        };

        private static readonly List<MacroPattern> IsolatedNakedTokens = new List<MacroPattern>
        {
            new MacroPattern("YYYY", "[YYYY]"),
            new MacroPattern("YY", "[YY]"),
            new MacroPattern("MMMM", "[MMMM]"),
            new MacroPattern("MMM", "[MMM]"),
            new MacroPattern("MM", "[MM]"),
            new MacroPattern("DD", "[DD]"),
            new MacroPattern("HH24", "[HH]"),
            new MacroPattern("HH", "[HH]"),
            new MacroPattern("MI", "[TT]"),
            new MacroPattern("TT", "[TT]"),
            new MacroPattern("SS", "[SS]")
        };

        public static string TranslateKnownPatterns(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            string output = input;

            foreach (var kv in Map)
                output = output.Replace(kv.Key, kv.Value);

            return TranslateNakedDateTimePatterns(output);
        }

        /// <summary>
        /// Returns warning text for external date/time format styles that may be ambiguous
        /// or invalid in a filename field even after safe macro translations are applied.
        /// </summary>
        public static IList<string> GetFilenameDateTimeWarnings(string input)
        {
            return GetDateTimeMacroWarnings(input, "Filename", false);
        }

        /// <summary>
        /// Returns warning text for external date/time format styles that may be ambiguous
        /// or invalid in an import field even after safe macro translations are applied.
        /// </summary>
        public static IList<string> GetDateTimeMacroWarnings(string input, string fieldLabel, bool isPathField)
        {
            var warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(input))
                return warnings;

            if (string.IsNullOrWhiteSpace(fieldLabel))
                fieldLabel = "Value";

            AddWarningIfMatch(
                warnings,
                input,
                @"\{[^}]*?(?:y{2,4}|m{1,4}|d{1,2}|h{1,2}|s{1,2}|\[(?:YYYY|YY|MMMM|MMM|MM|DD|HH|TT|SS)\])[^}]*?\}",
                fieldLabel + " contains curly-brace date/time syntax such as {yyyyMMdd}. MOVEit Automation macros should use bracketed attributes such as [YYYY][MM][DD].");

            AddWarningIfMatch(
                warnings,
                input,
                @"%[YymdHMS]",
                fieldLabel + " contains strftime-style date/time syntax such as %Y%m%d. MOVEit Automation macros should use bracketed attributes such as [YYYY][MM][DD].");

            AddWarningIfMatch(
                warnings,
                input,
                @"(?:\b(?:yyyy|yy|mm|m|dd|d)\b|\[(?:YYYY|YY|MM|DD)\])\s*/\s*(?:\b(?:yyyy|yy|mm|m|dd|d)\b|\[(?:YYYY|YY|MM|DD)\])",
                isPathField
                    ? fieldLabel + " contains slash-separated date/time syntax. This may be intentional as folder separators; verify the converted path before creating the task."
                    : fieldLabel + " contains slash-separated date/time syntax. Slash characters can be treated as path separators; review this filename value.");

            AddWarningIfMatch(
                warnings,
                input,
                @"(?:\b(?:hh24|hh|h|mi|mm|m|tt|ss|s)\b|\[(?:HH|TT|MM|SS)\])\s*:\s*(?:\b(?:hh24|hh|h|mi|mm|m|tt|ss|s)\b|\[(?:HH|TT|MM|SS)\])",
                isPathField
                    ? fieldLabel + " contains colon-separated time syntax. Colons may be invalid in Windows/Share paths; review this path value."
                    : fieldLabel + " contains colon-separated time syntax. Colons are not valid in Windows filenames; review this filename value.");

            AddWarningIfMatch(
                warnings,
                input,
                @"\[(?=[^\]]*(?:/|:))(?=[^\]]*(?:YYYY|YY|MMMM|MMM|MM|DD|HH24|HH|MI|TT|SS))[^\]]+\]",
                fieldLabel + " contains a bracketed date/time pattern with slash or colon separators. Review this value before creating the task.");

            return warnings;
        }


        /// <summary>
        /// After the normal filename date/time conversions have run, corrects date/time
        /// macro delimiters so MOVEit-compatible square brackets are used consistently.
        /// This intentionally runs as a second pass so existing pattern translators retain
        /// their current behavior.
        /// </summary>
        public static string NormalizeFilenameMacroDelimiters(string input, out IList<string> warnings)
        {
            return NormalizeMacroDelimiters(input, "Filename", out warnings);
        }

        public static string NormalizeMacroDelimiters(string input, string fieldLabel, out IList<string> warnings)
        {
            warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var changed = false;
            var output = input;

            // If a prior conversion produced ([YYYY][MM][DD]) or <[YYYY]-[MM]-[DD]>,
            // remove the non-MOVEit wrapper while preserving the valid square-bracket
            // macro sequence and any literal separators between the macro attributes.
            output = Regex.Replace(
                output,
                @"[\(\{<]((?=[^\)\}>]*\[(?:YYYY|YY|MMMM|MMM|MM|DD|HH|TT|SS)\])(?:\[(?:YYYY|YY|MMMM|MMM|MM|DD|HH|TT|SS)\]|[-_. :/])+)[\)\}>]",
                delegate(Match match)
                {
                    changed = true;
                    return match.Groups[1].Value;
                },
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Correct one date/time macro token at a time when it is wrapped in (), {},
            // <>, or a mismatched delimiter such as [MM}. Non-date bracketed macros
            // such as [TaskName] are intentionally preserved.
            output = Regex.Replace(
                output,
                @"[\[\(\{<]([A-Za-z0-9][A-Za-z0-9 _./:\-]*?)[\]\)\}>]",
                delegate(Match match)
                {
                    string replacement;
                    if (!TryTranslateDateTimeToken(match.Groups[1].Value, out replacement))
                        return match.Value;

                    if (!string.Equals(match.Value, replacement, StringComparison.Ordinal))
                        changed = true;

                    return replacement;
                },
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // A token correction may produce a valid bracketed macro sequence that is
            // still wrapped by (), {}, or <>. Strip those outer non-MOVEit delimiters
            // after token-level correction as well.
            output = Regex.Replace(
                output,
                @"[\(\{<]((?=[^\)\}>]*\[(?:YYYY|YY|MMMM|MMM|MM|DD|HH|TT|SS)\])(?:\[(?:YYYY|YY|MMMM|MMM|MM|DD|HH|TT|SS)\]|[-_. :/])+)[\)\}>]",
                delegate(Match match)
                {
                    changed = true;
                    return match.Groups[1].Value;
                },
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (changed)
            {
                warnings.Add(
                    (string.IsNullOrWhiteSpace(fieldLabel) ? "Value" : fieldLabel) +
                    " date/time macro delimiters were normalized to MOVEit square-bracket syntax. Only [ ] delimiters are valid for Automation macros.");
            }

            return output;
        }

        private static bool TryTranslateDateTimeToken(string token, out string automationMacro)
        {
            automationMacro = string.Empty;
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var candidate = token.Trim();
            foreach (var pattern in NakedPatterns)
            {
                if (string.Equals(candidate, pattern.Marker, StringComparison.OrdinalIgnoreCase))
                {
                    automationMacro = pattern.AutomationMacro;
                    return true;
                }
            }

            foreach (var tokenPattern in IsolatedNakedTokens)
            {
                if (string.Equals(candidate, tokenPattern.Marker, StringComparison.OrdinalIgnoreCase))
                {
                    automationMacro = tokenPattern.AutomationMacro;
                    return true;
                }
            }

            return false;
        }

        private static void AddWarningIfMatch(IList<string> warnings, string input, string pattern, string message)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                !warnings.Contains(message))
            {
                warnings.Add(message);
            }
        }

        private static string TranslateNakedDateTimePatterns(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var builder = new StringBuilder(input.Length);
            var index = 0;

            while (index < input.Length)
            {
                if (input[index] == '[')
                {
                    var closeIndex = input.IndexOf(']', index + 1);
                    if (closeIndex >= 0)
                    {
                        var bracketedToken = input.Substring(index + 1, closeIndex - index - 1);
                        builder.Append(TranslateBracketedDateTimePattern(bracketedToken, input.Substring(index, closeIndex - index + 1)));
                        index = closeIndex + 1;
                        continue;
                    }
                }

                // Curly-brace date/time syntax is common in .NET-style format strings, but
                // MOVEit Automation does not use it for filename macros. Preserve it for
                // warning/reporting instead of partially converting the content inside braces.
                if (input[index] == '{')
                {
                    var closeIndex = input.IndexOf('}', index + 1);
                    if (closeIndex >= 0)
                    {
                        builder.Append(input.Substring(index, closeIndex - index + 1));
                        index = closeIndex + 1;
                        continue;
                    }
                }

                var nextSpecial = input.Length;
                var nextBracket = input.IndexOf('[', index);
                if (nextBracket >= 0 && nextBracket < nextSpecial)
                    nextSpecial = nextBracket;

                var nextCurly = input.IndexOf('{', index);
                if (nextCurly >= 0 && nextCurly < nextSpecial)
                    nextSpecial = nextCurly;

                var segment = input.Substring(index, nextSpecial - index);
                builder.Append(TranslateNakedDateTimePatternsInSegment(segment));
                index = nextSpecial;
            }

            return builder.ToString();
        }


        private static string TranslateBracketedDateTimePattern(string token, string originalBracketedValue)
        {
            if (string.IsNullOrWhiteSpace(token))
                return originalBracketedValue;

            // Preserve standard one-token MOVEit macros such as [YYYY], [MM], [DD], [HH], [TT], and [SS].
            // Expand compound bracketed date/time patterns such as [YYYYMMDD] and [YYYYMMDDHHMMSS]
            // into the Automation-compatible sequence of individual bracketed macro attributes.
            foreach (var pattern in NakedPatterns)
            {
                if (string.Equals(token, pattern.Marker, StringComparison.OrdinalIgnoreCase))
                    return pattern.AutomationMacro;
            }

            return originalBracketedValue;
        }

        private static string TranslateNakedDateTimePatternsInSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment))
                return segment;

            var output = segment;

            foreach (var pattern in NakedPatterns)
            {
                output = Regex.Replace(
                    output,
                    Regex.Escape(pattern.Marker),
                    pattern.AutomationMacro,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            foreach (var token in IsolatedNakedTokens)
            {
                output = Regex.Replace(
                    output,
                    @"(?<![A-Za-z0-9\[])" + Regex.Escape(token.Marker) + @"(?![A-Za-z0-9\]])",
                    token.AutomationMacro,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return output;
        }

        /// <summary>
        /// Allows future iterations to register additional translations without changing call sites.
        /// </summary>
        public static void RegisterTranslation(string marker, string automationMacro)
        {
            if (string.IsNullOrWhiteSpace(marker)) throw new ArgumentException(nameof(marker));
            if (string.IsNullOrWhiteSpace(automationMacro)) throw new ArgumentException(nameof(automationMacro));
            Map[marker] = automationMacro;
        }

        private sealed class MacroPattern
        {
            public MacroPattern(string marker, string automationMacro)
            {
                Marker = marker;
                AutomationMacro = automationMacro;
            }

            public string Marker { get; private set; }
            public string AutomationMacro { get; private set; }
        }
    }
}
