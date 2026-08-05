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

    public IReadOnlyList<FootnoteEndnoteOptionsChoice<NoteNumberRestart>> FootnoteRestartItems =>
        FootnoteEndnoteOptionsDialogPlanner.FootnoteRestartItems;

    public IReadOnlyList<FootnoteEndnoteOptionsChoice<NoteNumberRestart>> EndnoteRestartItems =>
        FootnoteEndnoteOptionsDialogPlanner.EndnoteRestartItems;

    public FootnoteEndnoteOptionsInitialState InitialState { get; }

    public FootnoteEndnoteOptionsDialogInput State { get; private set; }

    public void UpdateFootnoteFormat(int selectedIndex) =>
        State = State with { FootnoteFormatIndex = selectedIndex };

    public void UpdateFootnoteStartAt(string? text) =>
        State = State with { FootnoteStartAtText = text };

    public void UpdateFootnoteRestart(int selectedIndex) =>
        State = State with { FootnoteRestartIndex = selectedIndex };

    public void UpdateEndnoteFormat(int selectedIndex) =>
        State = State with { EndnoteFormatIndex = selectedIndex };

    public void UpdateEndnoteStartAt(string? text) =>
        State = State with { EndnoteStartAtText = text };

    public void UpdateEndnoteRestart(int selectedIndex) =>
        State = State with { EndnoteRestartIndex = selectedIndex };

    public FootnoteEndnoteOptionsDialogAcceptance PlanAcceptance() =>
        FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(
            State,
            _culture,
            out var result,
            out var validation)
            ? new FootnoteEndnoteOptionsDialogAcceptance(result, Validation: null)
            : new FootnoteEndnoteOptionsDialogAcceptance(Result: null, validation);
}
