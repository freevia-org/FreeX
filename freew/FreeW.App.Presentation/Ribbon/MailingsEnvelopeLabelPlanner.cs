namespace FreeW.App.Presentation.Ribbon;

public readonly record struct EnvelopeSetupOption(string Name, double WidthPt, double HeightPt, double MarginPt);

public readonly record struct EnvelopeSetupResult(double WidthPt, double HeightPt, double MarginPt, bool Landscape);

public readonly record struct LabelSetupOption(
    string Name,
    int Rows,
    int Columns,
    double PageWidthPt,
    double PageHeightPt,
    double MarginPt)
{
    public bool IsCustom => Rows <= 0 || Columns <= 0;
}

public readonly record struct LabelSetupResult(
    int Rows,
    int Columns,
    double PageWidthPt,
    double PageHeightPt,
    double MarginPt,
    bool Landscape);

public enum LabelSetupIssue
{
    None,
    InvalidCustomGrid,
}

public readonly record struct LabelSetupPlan(LabelSetupResult? Result, LabelSetupIssue Issue)
{
    public bool Success => Result is not null && Issue == LabelSetupIssue.None;

    public static LabelSetupPlan Succeeded(LabelSetupResult result) =>
        new(result, LabelSetupIssue.None);

    public static LabelSetupPlan Failed(LabelSetupIssue issue) =>
        new(null, issue);
}

public readonly record struct EnvelopeDialogPlan(
    IReadOnlyList<EnvelopeSetupOption> Sizes,
    int SelectedIndex,
    string Note);

public readonly record struct LabelDialogPlan(
    IReadOnlyList<LabelSetupOption> Presets,
    int SelectedIndex,
    string CustomRowsText,
    string CustomColumnsText,
    bool ShowCustomGrid,
    LabelSetupIssue Issue);

public static class MailingsEnvelopeLabelPlanner
{
    public const int DefaultEnvelopeIndex = 0;
    public const int DefaultLabelIndex = 0;
    public const int CustomLabelPresetIndex = 4;

    private const double PointsPerMillimeter = 72.0 / 25.4;
    private const double LetterWidthPt = 612;
    private const double LetterHeightPt = 792;
    private const double A4WidthPt = 595.28;
    private const double A4HeightPt = 841.89;

    private static readonly EnvelopeSetupOption[] EnvelopeSizes =
    [
        new("DL  (110 \u00d7 220 mm)", 110 * PointsPerMillimeter, 220 * PointsPerMillimeter, 18),
        new("C5  (162 \u00d7 229 mm)", 162 * PointsPerMillimeter, 229 * PointsPerMillimeter, 18),
        new("C6  (114 \u00d7 162 mm)", 114 * PointsPerMillimeter, 162 * PointsPerMillimeter, 14),
        new("Comm-10 (4.125 \u00d7 9.5 in)", 4.125 * 72, 9.5 * 72, 18),
        new("Monarch (3.875 \u00d7 7.5 in)", 3.875 * 72, 7.5 * 72, 14),
    ];

    private static readonly LabelSetupOption[] LabelPresets =
    [
        new("Avery 5160 \u2014 3 \u00d7 10 (Letter)", 10, 3, LetterWidthPt, LetterHeightPt, 18),
        new("Avery 5162 \u2014 2 \u00d7 7  (Letter)", 7, 2, LetterWidthPt, LetterHeightPt, 18),
        new("Avery 5163 \u2014 2 \u00d7 5  (Letter)", 5, 2, LetterWidthPt, LetterHeightPt, 18),
        new("Avery L7160 \u2014 3 \u00d7 7 (A4)", 7, 3, A4WidthPt, A4HeightPt, 14),
        new("Custom rows \u00d7 columns (Letter)", 0, 0, LetterWidthPt, LetterHeightPt, 18),
    ];

    public static IReadOnlyList<EnvelopeSetupOption> GetEnvelopeSizes() => EnvelopeSizes;

    public static EnvelopeDialogPlan CreateEnvelopeDialogPlan(int selectedIndex = DefaultEnvelopeIndex) =>
        new(
            EnvelopeSizes,
            NormalizeIndex(selectedIndex, EnvelopeSizes.Length, DefaultEnvelopeIndex),
            "Page orientation is set to Landscape. Narrow margins are applied automatically.");

    public static IReadOnlyList<LabelSetupOption> GetLabelPresets() => LabelPresets;

    public static LabelDialogPlan CreateLabelDialogPlan(
        int selectedIndex = DefaultLabelIndex,
        string? customRowsText = null,
        string? customColumnsText = null)
    {
        var index = NormalizeIndex(selectedIndex, LabelPresets.Length, DefaultLabelIndex);
        var preset = LabelPresets[index];
        return new(
            LabelPresets,
            index,
            customRowsText ?? (preset.IsCustom ? "10" : preset.Rows.ToString()),
            customColumnsText ?? (preset.IsCustom ? "3" : preset.Columns.ToString()),
            preset.IsCustom,
            LabelSetupIssue.None);
    }

    public static EnvelopeSetupResult PlanEnvelope(int selectedIndex)
    {
        var size = EnvelopeSizes[NormalizeIndex(selectedIndex, EnvelopeSizes.Length, DefaultEnvelopeIndex)];
        return new EnvelopeSetupResult(size.WidthPt, size.HeightPt, size.MarginPt, Landscape: true);
    }

    public static LabelSetupPlan PlanLabel(int selectedIndex, string? customRowsText, string? customColumnsText)
    {
        var index = NormalizeIndex(selectedIndex, LabelPresets.Length, DefaultLabelIndex);
        var preset = LabelPresets[index];
        if (!preset.IsCustom)
        {
            return LabelSetupPlan.Succeeded(new LabelSetupResult(
                preset.Rows,
                preset.Columns,
                preset.PageWidthPt,
                preset.PageHeightPt,
                preset.MarginPt,
                Landscape: false));
        }

        if (!int.TryParse(customRowsText, out var rows) || rows < 1 ||
            !int.TryParse(customColumnsText, out var columns) || columns < 1)
            return LabelSetupPlan.Failed(LabelSetupIssue.InvalidCustomGrid);

        return LabelSetupPlan.Succeeded(new LabelSetupResult(
            rows,
            columns,
            preset.PageWidthPt,
            preset.PageHeightPt,
            preset.MarginPt,
            Landscape: false));
    }

    private static int NormalizeIndex(int index, int count, int fallback) =>
        index >= 0 && index < count ? index : fallback;
}
