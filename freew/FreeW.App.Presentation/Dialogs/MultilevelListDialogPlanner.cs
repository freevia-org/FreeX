using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record MultilevelListNumberFormatChoice(string Label, ListNumberFormat Format)
{
    public override string ToString() => Label;
}

public sealed record MultilevelListDialogInitialState(
    int LevelsIndex,
    string Level0StartAtText,
    string Level1StartAtText,
    int Level0FormatIndex,
    int Level1FormatIndex,
    int Level2FormatIndex);

public sealed record MultilevelListDialogInput(
    int LevelsIndex,
    string? Level0StartAtText,
    string? Level1StartAtText,
    int Level0FormatIndex,
    int Level1FormatIndex,
    int Level2FormatIndex);

public enum MultilevelListDialogField
{
    Level0StartAt,
    Level1StartAt,
}

public sealed record MultilevelListDialogValidation(MultilevelListDialogField Field, string Message);

public sealed record MultilevelListDefinition(
    int Levels,
    int? Level0StartAt,
    int? Level1StartAt,
    IReadOnlyList<ListNumberFormat> NumberFormats,
    bool LinkToHeadingStyles = false);

public sealed record MultilevelListPreset(
    string CommandId,
    string Name,
    string Description,
    MultilevelListDefinition Definition);

public static class MultilevelListDialogPlanner
{
    public const string Title = "Define New Multilevel List";
    public const string PositiveStartAtMessage = "Start-at values must be positive integers or blank.";
    public const int MaximumLevelCount = 9;
    public const int DialogWidth = 380;
    public const int OuterMargin = 14;
    public const int LevelsMinWidth = 80;
    public const int StartAtMinWidth = 60;
    public const int NumberFormatMinWidth = 130;
    public const int ButtonWidth = 72;
    public const string Description = "Configure multilevel list levels.";
    public const string LevelsLabel = "Number of levels (1-9):";
    public const string Level0StartAtLabel = "Level 1 start at:";
    public const string Level1StartAtLabel = "Level 2 start at:";
    public const string Level0NumberStyleLabel = "Level 1 number style:";
    public const string Level1NumberStyleLabel = "Level 2 number style:";
    public const string Level2NumberStyleLabel = "Level 3 number style:";

    public static MultilevelListDefinition DefaultDefinition { get; } =
        new(
            MultiLevelListFormat.LevelCount,
            Level0StartAt: null,
            Level1StartAt: null,
            MultiLevelListFormat.DecimalNumberFormats);

    public static IReadOnlyList<MultilevelListPreset> Presets { get; } =
    [
        new(
            "freew.multilevel-preset-0",
            "Outline: 1. / 1.1. / 1.1.1.",
            "Decimal outline using the standard FreeW multilevel list.",
            DefaultDefinition),
        new(
            "freew.multilevel-preset-1",
            "Outline: 1. / a. / i.",
            "Decimal + lower-letter + lower-roman per-level numbering.",
            new MultilevelListDefinition(
                MultiLevelListFormat.LevelCount,
                Level0StartAt: null,
                Level1StartAt: null,
                MultiLevelListFormat.DecimalLowerLetterLowerRomanNumberFormats)),
        new(
            "freew.multilevel-preset-2",
            "Outline (Headings): link to Heading styles",
            "Apply multilevel list and map each level to Heading 1-3 styles.",
            DefaultDefinition with { LinkToHeadingStyles = true }),
    ];

    public static IReadOnlyList<MultilevelListNumberFormatChoice> NumberFormatChoices { get; } =
    [
        new("1, 2, 3", ListNumberFormat.Decimal),
        new("a, b, c", ListNumberFormat.LowerLetter),
        new("A, B, C", ListNumberFormat.UpperLetter),
        new("i, ii, iii", ListNumberFormat.LowerRoman),
        new("I, II, III", ListNumberFormat.UpperRoman),
    ];

    public static MultilevelListDialogSession CreateSession(
        IReadOnlyList<ListNumberFormat>? currentNumberFormats,
        CultureInfo culture) =>
        new(currentNumberFormats, culture);

    public static MultilevelListCommitPlan PlanCommit(MultilevelListDefinition? definition) =>
        new(definition);

    public static MultilevelListDialogInitialState BuildInitialState(
        IReadOnlyList<ListNumberFormat>? currentNumberFormats,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return new MultilevelListDialogInitialState(
            LevelsIndex: 8,
            Level0StartAtText: 1.ToString(culture),
            Level1StartAtText: 1.ToString(culture),
            Level0FormatIndex: FormatIndex(FormatAt(currentNumberFormats, 0)),
            Level1FormatIndex: FormatIndex(FormatAt(currentNumberFormats, 1)),
            Level2FormatIndex: FormatIndex(FormatAt(currentNumberFormats, 2)));
    }

    public static bool TryBuildResult(
        MultilevelListDialogInput input,
        CultureInfo culture,
        out MultilevelListDefinition? result,
        out MultilevelListDialogValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);
        result = null;
        validation = null;

        if (!TryParseStartAt(input.Level0StartAtText, culture, out var level0StartAt))
        {
            validation = new MultilevelListDialogValidation(
                MultilevelListDialogField.Level0StartAt,
                PositiveStartAtMessage);
            return false;
        }

        if (!TryParseStartAt(input.Level1StartAtText, culture, out var level1StartAt))
        {
            validation = new MultilevelListDialogValidation(
                MultilevelListDialogField.Level1StartAt,
                PositiveStartAtMessage);
            return false;
        }

        var formats = MultiLevelListFormat.DecimalNumberFormats.ToArray();
        formats[0] = FormatAt(input.Level0FormatIndex);
        formats[1] = FormatAt(input.Level1FormatIndex);
        formats[2] = FormatAt(input.Level2FormatIndex);
        result = new MultilevelListDefinition(
            Math.Clamp(input.LevelsIndex + 1, 1, MaximumLevelCount),
            level0StartAt,
            level1StartAt,
            formats);
        return true;
    }

    public static ParagraphFormatting ApplyDefinition(
        ParagraphFormatting formatting,
        MultilevelListDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var maximumLevel = Math.Clamp(definition.Levels, 1, MultiLevelListFormat.LevelCount) - 1;
        var level = Math.Clamp(formatting.ListLevel, 0, maximumLevel);
        var startAt = level switch
        {
            0 => definition.Level0StartAt,
            1 => definition.Level1StartAt,
            _ => formatting.ListStartOverride,
        };

        return formatting with
        {
            ListKind = ListKind.MultiLevel,
            ListLevel = level,
            ListStartOverride = startAt,
        };
    }

    public static string? ResolveLinkedHeadingStyleId(int level, MultilevelListDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.LinkToHeadingStyles)
            return null;

        return Math.Clamp(level, 0, MultiLevelListFormat.LevelCount - 1) switch
        {
            0 => "Heading1",
            1 => "Heading2",
            _ => "Heading3",
        };
    }

    private static bool TryParseStartAt(string? text, CultureInfo culture, out int? value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            value = null;
            return true;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, culture, out var parsed) && parsed >= 1)
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static ListNumberFormat FormatAt(IReadOnlyList<ListNumberFormat>? formats, int level) =>
        formats is not null && level >= 0 && level < formats.Count
            ? formats[level]
            : ListNumberFormat.Decimal;

    private static ListNumberFormat FormatAt(int index) =>
        NumberFormatChoices[Math.Clamp(index, 0, NumberFormatChoices.Count - 1)].Format;

    private static int FormatIndex(ListNumberFormat format)
    {
        for (var i = 0; i < NumberFormatChoices.Count; i++)
            if (NumberFormatChoices[i].Format == format)
                return i;
        return 0;
    }
}
