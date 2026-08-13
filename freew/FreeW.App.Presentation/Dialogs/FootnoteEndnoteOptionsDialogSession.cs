using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record FootnoteEndnoteOptionsDialogAcceptance(
    FootnoteEndnoteOptionsDialogResult? Result,
    FootnoteEndnoteOptionsValidation? Validation)
{
    public bool IsAccepted => Result is not null && Validation is null;
}

public sealed record FootnoteEndnoteOptionsCommitPlan(
    FootnoteEndnoteOptionsDialogResult? Result)
{
    public bool ShouldApply => Result is not null;
}

/// <summary>
/// Owns the renderer-neutral state and acceptance policy for the paired Footnote and Endnote dialogs.
/// </summary>
public sealed class FootnoteEndnoteOptionsDialogSession
{
    private readonly CultureInfo _culture;

    public FootnoteEndnoteOptionsDialogSession(
        NoteNumberingOptions footnote,
        NoteNumberingOptions endnote,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        _culture = culture;
        InitialState = FootnoteEndnoteOptionsDialogPlanner.BuildInitialState(footnote, endnote, culture);
        State = new FootnoteEndnoteOptionsDialogInput(
            InitialState.FootnoteFormatIndex,
            InitialState.FootnoteStartAtText,
            InitialState.FootnoteRestartIndex,
            InitialState.EndnoteFormatIndex,
            InitialState.EndnoteStartAtText,
            InitialState.EndnoteRestartIndex);
    }

    public IReadOnlyList<FootnoteEndnoteOptionsChoice<NoteNumberFormat>> FormatItems =>
        FootnoteEndnoteOptionsDialogPlanner.FormatItems;

    public IReadOnlyList<FootnoteEndnoteOptionsChoice<NoteNumberRestart>> RestartItems(
        FootnoteEndnoteNoteKind kind) => kind switch
        {
            FootnoteEndnoteNoteKind.Footnote => FootnoteEndnoteOptionsDialogPlanner.FootnoteRestartItems,
            FootnoteEndnoteNoteKind.Endnote => FootnoteEndnoteOptionsDialogPlanner.EndnoteRestartItems,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public FootnoteEndnoteOptionsInitialState InitialState { get; }

    public FootnoteEndnoteOptionsDialogInput State { get; private set; }

    public void UpdateIndex(
        FootnoteEndnoteNoteKind note,
        FootnoteEndnoteFieldKind field,
        int selectedIndex) => State = (note, field) switch
        {
            (FootnoteEndnoteNoteKind.Footnote, FootnoteEndnoteFieldKind.NumberFormat) =>
                State with { FootnoteFormatIndex = selectedIndex },
            (FootnoteEndnoteNoteKind.Footnote, FootnoteEndnoteFieldKind.Numbering) =>
                State with { FootnoteRestartIndex = selectedIndex },
            (FootnoteEndnoteNoteKind.Endnote, FootnoteEndnoteFieldKind.NumberFormat) =>
                State with { EndnoteFormatIndex = selectedIndex },
            (FootnoteEndnoteNoteKind.Endnote, FootnoteEndnoteFieldKind.Numbering) =>
                State with { EndnoteRestartIndex = selectedIndex },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    public void UpdateStartAt(FootnoteEndnoteNoteKind note, string? text) =>
        State = note switch
        {
            FootnoteEndnoteNoteKind.Footnote => State with { FootnoteStartAtText = text },
            FootnoteEndnoteNoteKind.Endnote => State with { EndnoteStartAtText = text },
            _ => throw new ArgumentOutOfRangeException(nameof(note), note, null),
        };

    public FootnoteEndnoteOptionsDialogAcceptance PlanAcceptance() =>
        FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(
            State,
            _culture,
            out var result,
            out var validation)
            ? new FootnoteEndnoteOptionsDialogAcceptance(result, Validation: null)
            : new FootnoteEndnoteOptionsDialogAcceptance(Result: null, validation);
}
