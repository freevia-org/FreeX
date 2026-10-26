using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static void LoadConditionalFormats(Sheet sheet, IEnumerable<ConditionalFormatDto>? formats, int loadedSchemaVersion)
    {
        foreach (var formatDto in formats ?? [])
        {
            if (string.IsNullOrWhiteSpace(formatDto?.AppliesTo))
                continue;
            if (!IsSupportedConditionalFormat(formatDto))
                continue;

            try
            {
                sheet.ConditionalFormats.Add(ToConditionalFormat(formatDto, sheet.Id, loadedSchemaVersion));
            }
            catch (FormatException)
            {
                // Skip conditional formats with unparseable ranges.
            }
        }
    }

    private static List<ConditionalFormatDto> ToConditionalFormatDtos(
        IEnumerable<ConditionalFormat> formats,
        SheetId sheetId) =>
        formats
            .Where(format =>
                format.AppliesTo.Start.Sheet == sheetId &&
                format.AppliesTo.End.Sheet == sheetId &&
                IsSupportedConditionalFormat(format))
            .Select(FromConditionalFormat)
            .ToList();

    private static ConditionalFormat ToConditionalFormat(ConditionalFormatDto formatDto, SheetId sheetId, int loadedSchemaVersion)
    {
        IReadOnlyList<GridRange>? additionalRanges = null;
        if (formatDto.AdditionalRanges is { Count: > 0 })
        {
            var parsed = new List<GridRange>(formatDto.AdditionalRanges.Count);
            foreach (var rangeStr in formatDto.AdditionalRanges)
            {
                if (!string.IsNullOrWhiteSpace(rangeStr))
                    parsed.Add(GridRange.Parse(rangeStr, sheetId));
            }
            if (parsed.Count > 0)
                additionalRanges = parsed;
        }

        var format = new ConditionalFormat
        {
            AppliesTo = GridRange.Parse(formatDto.AppliesTo!, sheetId),
            AdditionalRanges = additionalRanges,
            Priority = formatDto.Priority < 1 ? 1 : formatDto.Priority,
            RuleType = formatDto.RuleType,
            Operator = formatDto.Operator,
            Value1 = formatDto.Value1,
            Value2 = formatDto.Value2,
            FormatIfTrue = ToCellStyle(formatDto.FormatIfTrue),
            MinColor = formatDto.MinColor,
            MidColor = formatDto.MidColor,
            MaxColor = formatDto.MaxColor,
            UseThreeColorScale = formatDto.UseThreeColorScale,
            MinThresholdType = ValidCfThresholdTypeOrDefault(formatDto.MinThresholdType, CfThresholdType.Min),
            MinThresholdValue = formatDto.MinThresholdValue,
            MinThresholdGreaterThanOrEqual = formatDto.MinThresholdGreaterThanOrEqual,
            MidThresholdType = ValidCfThresholdTypeOrDefault(formatDto.MidThresholdType, CfThresholdType.Percentile),
            MidThresholdValue = formatDto.MidThresholdValue,
            MidThresholdGreaterThanOrEqual = formatDto.MidThresholdGreaterThanOrEqual,
            MaxThresholdType = ValidCfThresholdTypeOrDefault(formatDto.MaxThresholdType, CfThresholdType.Max),
            MaxThresholdValue = formatDto.MaxThresholdValue,
            MaxThresholdGreaterThanOrEqual = formatDto.MaxThresholdGreaterThanOrEqual,
            DataBarColor = formatDto.DataBarColor,
            // Pre-r70 .fxl files (loaded schema version < 2) persisted a data-bar "Automatic"
            // default min/max as the plain Min/Max ordinal (AutoMin/AutoMax did not exist yet), so
            // loading such a legacy file must migrate the legacy Min/Max to AutoMin/AutoMax to
            // preserve the zero-baseline clamp -- matching the XLSX read path's Min->AutoMin /
            // Max->AutoMax migration in XlsxFileAdapter.ConditionalFormats.cs. Schema version was
            // bumped to 2 specifically to gate this: a v2+ file's Min/Max is an EXPLICIT user choice
            // (e.g. "Lowest Value"/"Highest Value" in the Data Bar dialog) and must round-trip as-is
            // -- migrating it unconditionally would silently clobber that choice back to Automatic
            // on every reload (R72-meta-1).
            DataBarMinThresholdType = loadedSchemaVersion < 2
                ? MigrateLegacyDataBarThresholdType(
                    ValidCfThresholdTypeOrDefault(formatDto.DataBarMinThresholdType, CfThresholdType.Min),
                    CfThresholdType.Min, CfThresholdType.AutoMin)
                : ValidCfThresholdTypeOrDefault(formatDto.DataBarMinThresholdType, CfThresholdType.Min),
            DataBarMinThresholdValue = formatDto.DataBarMinThresholdValue,
            DataBarMaxThresholdType = loadedSchemaVersion < 2
                ? MigrateLegacyDataBarThresholdType(
                    ValidCfThresholdTypeOrDefault(formatDto.DataBarMaxThresholdType, CfThresholdType.Max),
                    CfThresholdType.Max, CfThresholdType.AutoMax)
                : ValidCfThresholdTypeOrDefault(formatDto.DataBarMaxThresholdType, CfThresholdType.Max),
            DataBarMaxThresholdValue = formatDto.DataBarMaxThresholdValue,
            DataBarShowValue = formatDto.DataBarShowValue,
            DataBarMinLength = ValidDataBarLengthOrNull(formatDto.DataBarMinLength),
            DataBarMaxLength = ValidDataBarLengthOrNull(formatDto.DataBarMaxLength),
            DataBarGradient = formatDto.DataBarGradient,
            DataBarBorder = formatDto.DataBarBorder,
            DataBarBorderColor = formatDto.DataBarBorderColor,
            DataBarAxisPosition = ValidDataBarAxisPositionOrNull(formatDto.DataBarAxisPosition),
            DataBarAxisColor = formatDto.DataBarAxisColor,
            DataBarNegativeFillColor = formatDto.DataBarNegativeFillColor,
            // r198: see the DTO -- these seven were absent from both directions.
            MinColorSource = formatDto.MinColorSource,
            MidColorSource = formatDto.MidColorSource,
            MaxColorSource = formatDto.MaxColorSource,
            DataBarColorSource = formatDto.DataBarColorSource,
            DataBarNegativeFillSameAsPositive = formatDto.DataBarNegativeFillSameAsPositive,
            DataBarNegativeBorderSameAsPositive = formatDto.DataBarNegativeBorderSameAsPositive,
            DataBarDirection = formatDto.DataBarDirection,
            DataBarNegativeBorderColor = formatDto.DataBarNegativeBorderColor,
            AboveAverage = formatDto.AboveAverage,
            EqualAverage = formatDto.EqualAverage,
            StdDevCount = formatDto.StdDevCount,
            FormulaText = formatDto.FormulaText,
            IconSetStyle = NormalizeOptionalText(formatDto.IconSetStyle),
            IconSetShowValue = formatDto.IconSetShowValue,
            IconSetReverse = formatDto.IconSetReverse,
            TopBottomRank = ValidTopBottomRankOrDefault(formatDto.TopBottomRank),
            TopBottomPercent = formatDto.TopBottomPercent,
            TextRuleText = formatDto.TextRuleText,
            DateOccurringPeriod = ValidDateOccurringPeriodOrDefault(formatDto.DateOccurringPeriod),
            StopIfTrue = formatDto.StopIfTrue,
            NativeAttributes = CleanOptionalNativeAttributes(formatDto.NativeAttributes),
            NativeChildXmls = CleanNativeXmlList(formatDto.NativeChildXmls),
            NativePayloadAttributes = CleanOptionalNativeAttributes(formatDto.NativePayloadAttributes),
            NativePayloadChildXmls = CleanNativeXmlList(formatDto.NativePayloadChildXmls),
            NativeContainerAttributes = CleanOptionalNativeAttributes(formatDto.NativeContainerAttributes),
            NativeContainerChildXmls = CleanNativeXmlList(formatDto.NativeContainerChildXmls)
        };
        format.IconSetThresholds.AddRange((formatDto.IconSetThresholds ?? [])
            .OfType<CfThresholdModel>()
            .Where(threshold => Enum.IsDefined(threshold.Type)));
        format.IconOverrides.AddRange((formatDto.IconOverrides ?? [])
            .OfType<CfIconOverride>()
            .Select(NormalizeCfIconOverride)
            .Where(IsValidCfIconOverride));
        return format;
    }

    private static ConditionalFormatDto FromConditionalFormat(ConditionalFormat format) =>
        new()
        {
            AppliesTo = format.AppliesTo.ToString(),
            AdditionalRanges = format.AdditionalRanges is null
                ? null
                : format.AdditionalRanges.Select(r => r.ToString()).ToList(),
            Priority = format.Priority < 1 ? 1 : format.Priority,
            RuleType = format.RuleType,
            Operator = format.Operator,
            Value1 = format.Value1,
            Value2 = format.Value2,
            FormatIfTrue = FromCellStyle(format.FormatIfTrue),
            MinColor = format.MinColor,
            MidColor = format.MidColor,
            MaxColor = format.MaxColor,
            UseThreeColorScale = format.UseThreeColorScale,
            MinThresholdType = ValidCfThresholdTypeOrDefault(format.MinThresholdType, CfThresholdType.Min),
            MinThresholdValue = format.MinThresholdValue,
            MinThresholdGreaterThanOrEqual = format.MinThresholdGreaterThanOrEqual,
            MidThresholdType = ValidCfThresholdTypeOrDefault(format.MidThresholdType, CfThresholdType.Percentile),
            MidThresholdValue = format.MidThresholdValue,
            MidThresholdGreaterThanOrEqual = format.MidThresholdGreaterThanOrEqual,
            MaxThresholdType = ValidCfThresholdTypeOrDefault(format.MaxThresholdType, CfThresholdType.Max),
            MaxThresholdValue = format.MaxThresholdValue,
            MaxThresholdGreaterThanOrEqual = format.MaxThresholdGreaterThanOrEqual,
            DataBarColor = format.DataBarColor,
            DataBarMinThresholdType = ValidCfThresholdTypeOrDefault(format.DataBarMinThresholdType, CfThresholdType.Min),
            DataBarMinThresholdValue = format.DataBarMinThresholdValue,
            DataBarMaxThresholdType = ValidCfThresholdTypeOrDefault(format.DataBarMaxThresholdType, CfThresholdType.Max),
            DataBarMaxThresholdValue = format.DataBarMaxThresholdValue,
            DataBarShowValue = format.DataBarShowValue,
            DataBarMinLength = ValidDataBarLengthOrNull(format.DataBarMinLength),
            DataBarMaxLength = ValidDataBarLengthOrNull(format.DataBarMaxLength),
            DataBarGradient = format.DataBarGradient,
            DataBarBorder = format.DataBarBorder,
            DataBarBorderColor = format.DataBarBorderColor,
            DataBarAxisPosition = ValidDataBarAxisPositionOrNull(format.DataBarAxisPosition),
            DataBarAxisColor = format.DataBarAxisColor,
            DataBarNegativeFillColor = format.DataBarNegativeFillColor,
            MinColorSource = format.MinColorSource,
            MidColorSource = format.MidColorSource,
            MaxColorSource = format.MaxColorSource,
            DataBarColorSource = format.DataBarColorSource,
            DataBarNegativeFillSameAsPositive = format.DataBarNegativeFillSameAsPositive,
            DataBarNegativeBorderSameAsPositive = format.DataBarNegativeBorderSameAsPositive,
            DataBarDirection = format.DataBarDirection,
            DataBarNegativeBorderColor = format.DataBarNegativeBorderColor,
            AboveAverage = format.AboveAverage,
            EqualAverage = format.EqualAverage,
            StdDevCount = format.StdDevCount,
            FormulaText = format.FormulaText,
            IconSetStyle = NormalizeOptionalText(format.IconSetStyle),
            IconSetShowValue = format.IconSetShowValue,
            IconSetReverse = format.IconSetReverse,
            IconSetThresholds = [.. format.IconSetThresholds.OfType<CfThresholdModel>().Where(threshold => Enum.IsDefined(threshold.Type))],
            IconOverrides = [.. format.IconOverrides.OfType<CfIconOverride>().Select(NormalizeCfIconOverride).Where(IsValidCfIconOverride)],
            TopBottomRank = ValidTopBottomRankOrDefault(format.TopBottomRank),
            TopBottomPercent = format.TopBottomPercent,
            TextRuleText = format.TextRuleText,
            DateOccurringPeriod = ValidDateOccurringPeriodOrDefault(format.DateOccurringPeriod),
            StopIfTrue = format.StopIfTrue,
            NativeAttributes = CleanOptionalNativeAttributes(format.NativeAttributes),
            NativeChildXmls = CleanNativeXmlList(format.NativeChildXmls),
            NativePayloadAttributes = CleanOptionalNativeAttributes(format.NativePayloadAttributes),
            NativePayloadChildXmls = CleanNativeXmlList(format.NativePayloadChildXmls),
            NativeContainerAttributes = CleanOptionalNativeAttributes(format.NativeContainerAttributes),
            NativeContainerChildXmls = CleanNativeXmlList(format.NativeContainerChildXmls)
        };

    private static bool IsSupportedConditionalFormat(ConditionalFormat format) =>
        Enum.IsDefined(format.RuleType) && Enum.IsDefined(format.Operator);

    private static bool IsSupportedConditionalFormat(ConditionalFormatDto format) =>
        Enum.IsDefined(format.RuleType) && Enum.IsDefined(format.Operator);

    private static CfThresholdType ValidCfThresholdTypeOrDefault(CfThresholdType value, CfThresholdType fallback) =>
        Enum.IsDefined(value) ? value : fallback;

    /// <summary>
    /// Migrates a legacy data-bar min/max threshold type read from a pre-r70 .fxl file (loaded
    /// schema version &lt; 2 -- see the call sites in <see cref="ToConditionalFormat"/>): only the
    /// specific legacy value (Min for the min threshold, Max for the max threshold) is remapped to
    /// its Automatic equivalent (AutoMin/AutoMax); any other threshold type (including an already-
    /// migrated AutoMin/AutoMax, or a genuinely explicit Number/Percent/Percentile/Formula) passes
    /// through unchanged. Color-scale and icon-set thresholds never call this -- they have no
    /// Automatic concept and always use Min/Max literally. Callers must gate this on schema version
    /// so a v2+ file's explicit Min/Max choice is never migrated (R72-meta-1).
    /// </summary>
    private static CfThresholdType MigrateLegacyDataBarThresholdType(
        CfThresholdType value, CfThresholdType legacyValue, CfThresholdType migratedValue) =>
        value == legacyValue ? migratedValue : value;

    private static bool IsValidCfIconOverride(CfIconOverride icon) =>
        !string.IsNullOrWhiteSpace(icon.IconSet) && icon.IconId >= 0;

    private static CfIconOverride NormalizeCfIconOverride(CfIconOverride icon) =>
        icon with { IconSet = icon.IconSet?.Trim() ?? string.Empty };

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ValidDataBarLengthOrNull(int? value) =>
        value is >= 0 and <= 100 ? value : null;

    private static string? ValidDataBarAxisPositionOrNull(string? value)
    {
        var normalized = value?.Trim();
        return normalized is "automatic" or "middle" or "none"
            ? normalized
            : null;
    }

    private static int ValidTopBottomRankOrDefault(int value) =>
        value is >= 1 and <= 1000 ? value : 10;

    private static string ValidDateOccurringPeriodOrDefault(string? value)
    {
        var normalized = value?.Trim();
        return normalized is "yesterday" or "today" or "tomorrow" or "last7Days" or
            "lastWeek" or "thisWeek" or "nextWeek" or
            "lastMonth" or "thisMonth" or "nextMonth"
            ? normalized
            : "today";
    }
}
