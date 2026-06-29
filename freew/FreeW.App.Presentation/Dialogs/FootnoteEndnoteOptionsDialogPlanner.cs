using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum FootnoteEndnoteOptionsDialogField
{
    FootnoteStartAt,
    EndnoteStartAt
}

public sealed record FootnoteEndnoteOptionsChoice<TValue>(string Label, TValue Value);

public sealed record FootnoteEndnoteOptionsInitialState(
    int FootnoteFormatIndex,
    string FootnoteStartAtText,
    int FootnoteRestartIndex,
    int EndnoteFormatIndex,
    string EndnoteStartAtText,
    int EndnoteRestartIndex);

public sealed record FootnoteEndnoteOptionsDialogInput(
    int FootnoteFormatIndex,
    string? FootnoteStartAtText,
    int FootnoteRestartIndex,
    int EndnoteFormatIndex,
    string? EndnoteStartAtText,
    int EndnoteRestartIndex);

public sealed record FootnoteEndnoteOptionsValidation(
    FootnoteEndnoteOptionsDialogField Field,
    string Message);

public sealed record FootnoteEndnoteOptionsDialogResult(
    NoteNumberFormat FootnoteFormat,
    int FootnoteStartAt,
    NoteNumberRestart FootnoteRestart,
    NoteNumberFormat EndnoteFormat,
    int EndnoteStartAt,
    NoteNumberRestart EndnoteRestart);

public static class FootnoteEndnoteOptionsDialogPlanner
{
    public const string PositiveStartAtMessage = "Enter a positive integer for the start-at values.";

    public static readonly IReadOnlyList<FootnoteEndnoteOptionsChoice<NoteNumberFormat>> FormatItems =
    [
        new("1, 2, 3, \u2026", NoteNumberFormat.Decimal),
        new("i, ii, iii, \u2026", NoteNumberFormat.LowerRoman),
        new("I, II, III, \u2026", NoteNumberFormat.UpperRoman),
        new("a, b, c, \u2026", NoteNumberFormat.LowerLetter),
        new("A, B, C, \u2026", NoteNumberFormat.UpperLetter),
        new("*, \u2020, \u2021, \u2026", NoteNumberFormat.Chicago),
    ];

    public static readonly IReadOnlyList<FootnoteEndnoteOptionsChoice<NoteNumberRestart>> FootnoteRestartItems =
    [
        new("Continuous", NoteNumberRestart.Continuous),
        new("Restart each section", NoteNumberRestart.EachSection),
        new("Restart each page", NoteNumberRestart.EachPage),
    ];

    public static readonly IReadOnlyList<FootnoteEndnoteOptionsChoice<NoteNumberRestart>> EndnoteRestartItems =
    [
        new("Continuous", NoteNumberRestart.Continuous),
        new("Restart each section", NoteNumberRestart.EachSection),
    ];

    public static FootnoteEndnoteOptionsInitialState BuildInitialState(
        NoteNumberingOptions footnote,
        NoteNumberingOptions endnote,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(footnote);
        ArgumentNullException.ThrowIfNull(endnote);
        ArgumentNullException.ThrowIfNull(culture);

        return new FootnoteEndnoteOptionsInitialState(
            FootnoteFormatIndex: IndexOf(FormatItems, footnote.NumberFormat),
            FootnoteStartAtText: FormatInteger(footnote.StartAt, culture),
            FootnoteRestartIndex: IndexOf(FootnoteRestartItems, footnote.NumberRestart),
            EndnoteFormatIndex: IndexOf(FormatItems, endnote.NumberFormat),
            EndnoteStartAtText: FormatInteger(endnote.StartAt, culture),
            EndnoteRestartIndex: IndexOf(EndnoteRestartItems, endnote.NumberRestart));
    }

    public static bool TryBuildResult(
        FootnoteEndnoteOptionsDialogInput input,
        CultureInfo culture,
        out FootnoteEndnoteOptionsDialogResult? result,
        out FootnoteEndnoteOptionsValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        if (!TryParsePositiveInteger(input.FootnoteStartAtText, culture, out var footnoteStartAt))
        {
            validation = new FootnoteEndnoteOptionsValidation(
                FootnoteEndnoteOptionsDialogField.FootnoteStartAt,
                PositiveStartAtMessage);
            return false;
        }

        if (!TryParsePositiveInteger(input.EndnoteStartAtText, culture, out var endnoteStartAt))
        {
            validation = new FootnoteEndnoteOptionsValidation(
                FootnoteEndnoteOptionsDialogField.EndnoteStartAt,
                PositiveStartAtMessage);
            return false;
        }

        result = new FootnoteEndnoteOptionsDialogResult(
            ChoiceAt(FormatItems, input.FootnoteFormatIndex).Value,
            footnoteStartAt,
            ChoiceAt(FootnoteRestartItems, input.FootnoteRestartIndex).Value,
            ChoiceAt(FormatItems, input.EndnoteFormatIndex).Value,
            endnoteStartAt,
            ChoiceAt(EndnoteRestartItems, input.EndnoteRestartIndex).Value);
        return true;
    }

    public static string FormatInteger(int value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString(culture);
    }

    private static bool TryParsePositiveInteger(string? text, CultureInfo culture, out int value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return int.TryParse(trimmed, NumberStyles.Integer, culture, out value) && value >= 1;
    }

    private static FootnoteEndnoteOptionsChoice<TValue> ChoiceAt<TValue>(
        IReadOnlyList<FootnoteEndnoteOptionsChoice<TValue>> choices,
        int index) =>
        choices[Math.Clamp(index, 0, choices.Count - 1)];

    private static int IndexOf<TValue>(
        IReadOnlyList<FootnoteEndnoteOptionsChoice<TValue>> choices,
        TValue value)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (EqualityComparer<TValue>.Default.Equals(choices[i].Value, value))
                return i;
        }

        return 0;
    }
}
