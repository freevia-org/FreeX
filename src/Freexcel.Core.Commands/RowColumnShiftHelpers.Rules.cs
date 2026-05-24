using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static (
        List<(DataValidation Rule, GridRange AppliesTo)> DataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo)> ConditionalFormats)
        CaptureRuleRanges(Sheet sheet)
    {
        return (
            sheet.DataValidations.Select(rule => (rule, rule.AppliesTo)).ToList(),
            sheet.ConditionalFormats.Select(rule => (rule, rule.AppliesTo)).ToList());
    }

    internal static void RestoreRuleRanges(
        List<(DataValidation Rule, GridRange AppliesTo)>? dataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo)>? conditionalFormats)
    {
        if (dataValidations is not null)
            foreach (var (rule, appliesTo) in dataValidations)
                rule.AppliesTo = appliesTo;

        if (conditionalFormats is not null)
            foreach (var (rule, appliesTo) in conditionalFormats)
                rule.AppliesTo = appliesTo;
    }

    // Full rebuild variant: used when rules may have been removed (e.g. DeleteRows/DeleteColumns).
    internal static void RestoreRuleRanges(
        Sheet sheet,
        List<(DataValidation Rule, GridRange AppliesTo)>? dataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo)>? conditionalFormats)
    {
        if (dataValidations is not null)
        {
            sheet.DataValidations.Clear();
            foreach (var (rule, appliesTo) in dataValidations)
            {
                rule.AppliesTo = appliesTo;
                sheet.DataValidations.Add(rule);
            }
        }
        if (conditionalFormats is not null)
        {
            sheet.ConditionalFormats.Clear();
            foreach (var (rule, appliesTo) in conditionalFormats)
            {
                rule.AppliesTo = appliesTo;
                sheet.ConditionalFormats.Add(rule);
            }
        }
    }

    internal static void ShiftRuleRowsUp(Sheet sheet, uint start, uint count)
    {
        foreach (var rule in sheet.DataValidations)
            rule.AppliesTo = ShiftRangeRowsUp(rule.AppliesTo, start, count);
        foreach (var rule in sheet.ConditionalFormats)
            rule.AppliesTo = ShiftRangeRowsUp(rule.AppliesTo, start, count);
    }

    internal static void ShiftRuleRowsDown(Sheet sheet, uint start, uint count)
    {
        for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var shifted = ShiftRangeRowsDown(sheet.DataValidations[i].AppliesTo, start, count);
            if (shifted is null) sheet.DataValidations.RemoveAt(i);
            else sheet.DataValidations[i].AppliesTo = shifted.Value;
        }
        for (int i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
        {
            var shifted = ShiftRangeRowsDown(sheet.ConditionalFormats[i].AppliesTo, start, count);
            if (shifted is null) sheet.ConditionalFormats.RemoveAt(i);
            else sheet.ConditionalFormats[i].AppliesTo = shifted.Value;
        }
    }

    internal static void ShiftRuleColumnsUp(Sheet sheet, uint start, uint count)
    {
        foreach (var rule in sheet.DataValidations)
            rule.AppliesTo = ShiftRangeColumnsUp(rule.AppliesTo, start, count);
        foreach (var rule in sheet.ConditionalFormats)
            rule.AppliesTo = ShiftRangeColumnsUp(rule.AppliesTo, start, count);
    }

    internal static void ShiftRuleColumnsDown(Sheet sheet, uint start, uint count)
    {
        for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var shifted = ShiftRangeColumnsDown(sheet.DataValidations[i].AppliesTo, start, count);
            if (shifted is null) sheet.DataValidations.RemoveAt(i);
            else sheet.DataValidations[i].AppliesTo = shifted.Value;
        }
        for (int i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
        {
            var shifted = ShiftRangeColumnsDown(sheet.ConditionalFormats[i].AppliesTo, start, count);
            if (shifted is null) sheet.ConditionalFormats.RemoveAt(i);
            else sheet.ConditionalFormats[i].AppliesTo = shifted.Value;
        }
    }
}
