using System.Globalization;

namespace FreeX.App.Services;

public enum WorkbookInfoDisplaySurface
{
    WindowsBackstagePane,
    AvaloniaBackstageInfoDialog
}

public sealed record WorkbookInfoDisplayPlan(
    string WorkbookName,
    string FilePath,
    string SheetCount,
    string Format,
    string FileSize,
    string LastModified,
    string StatisticsSummary,
    string WorkbookProtectionSummary,
    string ActiveSheetProtectionSummary,
    string FormulaErrorSummary,
    string? UnsavedChangesNote);

public sealed class WorkbookInfoDisplayStrings
{
    private readonly Func<string, string> _get;
    private readonly Func<string, object?[], string> _format;

    public WorkbookInfoDisplayStrings(Func<string, string> get, Func<string, object?[], string> format)
    {
        _get = get ?? throw new ArgumentNullException(nameof(get));
        _format = format ?? throw new ArgumentNullException(nameof(format));
    }

    public string Get(string key) => _get(key);

    public string Format(string key, params object?[] args) => _format(key, args);
}

public static class WorkbookInfoDisplayPlanner
{
    public static WorkbookInfoDisplayPlan Build(
        WorkbookInfoPlan plan,
        WorkbookInfoDisplaySurface surface,
        WorkbookInfoDisplayStrings strings,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(strings);

        culture ??= CultureInfo.CurrentCulture;

        return new WorkbookInfoDisplayPlan(
            WorkbookName: plan.WorkbookName,
            FilePath: plan.IsSaved ? plan.FilePath ?? string.Empty : strings.Get("Backstage_Info_NotSavedYet"),
            SheetCount: plan.SheetCount.ToString(culture),
            Format: string.IsNullOrWhiteSpace(plan.FormatExtension) ? ".xlsx" : plan.FormatExtension,
            FileSize: FormatFileSize(plan, surface, strings, culture),
            LastModified: FormatLastModified(plan, strings, culture),
            StatisticsSummary: FormatStatistics(plan, surface),
            WorkbookProtectionSummary: FormatProtection(plan, strings, culture),
            ActiveSheetProtectionSummary: plan.ActiveSheetIsProtected
                ? strings.Get("Backstage_Info_ActiveSheetProtected")
                : strings.Get("Backstage_Info_ActiveSheetUnprotected"),
            // R129-model-avalonia-info-formula-issues-1: same wording as the WPF host's
            // BackstageInfoPlanner.FormatFormulaErrorSummary (both now delegate to
            // FormulaIssueSummaryFormatter), so a circular reference (or any other formula issue)
            // reads identically on both shells.
            FormulaErrorSummary: FormulaIssueSummaryFormatter.Format(
                plan.FormulaIssueCount, "Backstage_Info_NoFormulaErrors", strings),
            UnsavedChangesNote: plan.HasUnsavedChanges
                ? strings.Get("Backstage_Info_UnsavedChanges")
                : null);
    }

    private static string FormatFileSize(
        WorkbookInfoPlan plan,
        WorkbookInfoDisplaySurface surface,
        WorkbookInfoDisplayStrings strings,
        CultureInfo culture)
    {
        if (!plan.IsSaved)
            return strings.Get("Backstage_Info_NotSavedYet");
        if (!plan.FileExistsOnDisk || plan.FileSizeBytes is not { } bytes)
            return strings.Get("Backstage_Info_FileMissing");

        return surface == WorkbookInfoDisplaySurface.WindowsBackstagePane
            ? FormatByteSizeWithRawBytes(bytes, strings, culture)
            : FormatByteSizeCompact(bytes, culture);
    }

    private static string FormatLastModified(
        WorkbookInfoPlan plan,
        WorkbookInfoDisplayStrings strings,
        CultureInfo culture)
    {
        if (!plan.IsSaved)
            return strings.Get("Backstage_Info_NotSavedYet");
        if (!plan.FileExistsOnDisk || plan.LastModifiedLocal is not { } modified)
            return strings.Get("Backstage_Info_FileMissing");

        return modified.ToString("g", culture);
    }

    private static string FormatStatistics(WorkbookInfoPlan plan, WorkbookInfoDisplaySurface surface) =>
        surface == WorkbookInfoDisplaySurface.WindowsBackstagePane
            ? WorkbookStatisticsFormatter.Format(plan.Statistics)
            : string.Join(Environment.NewLine,
                $"Cells with data: {plan.Statistics.CellCount}",
                $"Formulas: {plan.Statistics.FormulaCount}",
                $"Charts: {plan.Statistics.ChartCount}",
                $"Pictures: {plan.Statistics.PictureCount}",
                $"Named ranges: {plan.Statistics.NamedRangeCount}");

    private static string FormatProtection(
        WorkbookInfoPlan plan,
        WorkbookInfoDisplayStrings strings,
        CultureInfo culture)
    {
        var protectedSheetCount = plan.ProtectedSheetCount.ToString(culture);
        var sheetCount = plan.SheetCount.ToString(culture);

        return plan.ProtectionPosture switch
        {
            WorkbookProtectionPosture.StructureAndSheetsProtected => strings.Format(
                "Backstage_Info_ProtectionStructureAndSheets",
                protectedSheetCount,
                sheetCount),
            WorkbookProtectionPosture.StructureProtected => strings.Get("Backstage_Info_ProtectionStructure"),
            WorkbookProtectionPosture.SheetsProtected => strings.Format(
                "Backstage_Info_ProtectionSheets",
                protectedSheetCount,
                sheetCount),
            _ => strings.Get("Backstage_Info_ProtectionNone")
        };
    }

    private static string FormatByteSizeWithRawBytes(long bytes, WorkbookInfoDisplayStrings strings, CultureInfo culture)
    {
        bytes = Math.Max(0, bytes);
        var bytesText = bytes.ToString("N0", culture);
        if (bytes == 1)
            return strings.Format("Backstage_Info_ByteSingularFormat", bytesText);

        if (bytes < 1024)
            return strings.Format("Backstage_Info_BytePluralFormat", bytesText);

        var (valueText, unit) = FormatByteSizeValueAndUnit(bytes, culture);
        return strings.Format("Backstage_Info_ByteSizeWithUnitFormat", valueText, unit, bytesText);
    }

    private static string FormatByteSizeCompact(long bytes, CultureInfo culture)
    {
        bytes = Math.Max(0, bytes);
        if (bytes < 1024)
            return $"{bytes.ToString("N0", culture)} B";

        var (valueText, unit) = FormatByteSizeValueAndUnit(bytes, culture);
        return $"{valueText} {unit}";
    }

    private static (string ValueText, string Unit) FormatByteSizeValueAndUnit(long bytes, CultureInfo culture)
    {
        var value = (double)bytes;
        var unitIndex = -1;
        string[] units = ["KB", "MB", "GB", "TB"];
        do
        {
            value /= 1024;
            unitIndex++;
        }
        while (value >= 1024 && unitIndex < units.Length - 1);

        var valueText = value >= 10
            ? value.ToString("N0", culture)
            : value.ToString("N1", culture);

        return (valueText, units[unitIndex]);
    }
}
