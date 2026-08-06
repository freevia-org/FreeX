using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record ColumnsDialogAcceptance(
    ColumnsDialogResult? Result,
    string? ValidationMessage)
{
    public bool IsAccepted => Result is not null;
}

public sealed class ColumnsDialogSession
{
    private readonly CultureInfo _culture;
    private readonly double _contentWidthPt;

    public ColumnsDialogSession(PageSettings page, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
        InitialState = ColumnsDialogPlanner.BuildInitialState(page, culture);
        _contentWidthPt = InitialState.ContentWidthPt;
    }

    public ColumnsDialogInitialState InitialState { get; }

    public IReadOnlyList<ColumnsDialogPreset> Presets => ColumnsDialogPlanner.Presets;

    public string CountTextForPreset(int presetIndex) =>
        ColumnsDialogPlanner.ColumnCountForPreset(presetIndex).ToString(_culture);

    public ColumnsDialogAcceptance PlanAcceptance(
        int presetIndex,
        string? countText,
        string? spacingText,
        bool lineBetween)
    {
        var input = new ColumnsDialogInput(
            presetIndex,
            countText,
            spacingText,
            lineBetween,
            _contentWidthPt);
        return ColumnsDialogPlanner.TryBuildResult(input, _culture, out var result, out var error)
            ? new ColumnsDialogAcceptance(result, ValidationMessage: null)
            : new ColumnsDialogAcceptance(null, error ?? ColumnsDialogPlanner.ValidationMessage);
    }
}

public sealed record CustomParagraphSpacingDialogAcceptance(
    DocumentParagraphSpacingSet? Result,
    CustomParagraphSpacingValidation? Validation)
{
    public bool IsAccepted => Result is not null;
}

public sealed class CustomParagraphSpacingDialogSession
{
    private readonly CultureInfo _culture;

    public CustomParagraphSpacingDialogSession(
        DocumentParagraphSpacingSet? current,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
        InitialState = CustomParagraphSpacingDialogPlanner.BuildInitialState(current, culture);
    }

    public CustomParagraphSpacingInitialState InitialState { get; }

    public CustomParagraphSpacingDialogAcceptance PlanAcceptance(
        CustomParagraphSpacingDialogInput input) =>
        CustomParagraphSpacingDialogPlanner.TryBuildResult(input, _culture, out var result, out var validation)
            ? new CustomParagraphSpacingDialogAcceptance(result, Validation: null)
            : new CustomParagraphSpacingDialogAcceptance(null, validation);
}

public sealed class DropCapOptionsDialogSession
{
    private readonly CultureInfo _culture;

    public DropCapOptionsDialogSession(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
        InitialState = DropCapOptionsDialogPlanner.BuildInitialState(culture);
    }

    public DropCapOptionsInitialState InitialState { get; }

    public IReadOnlyList<string> FontNames => DropCapOptionsDialogPlanner.FontNames;

    public DropCapOptionsDialogResult PlanAcceptance(DropCapOptionsDialogInput input) =>
        DropCapOptionsDialogPlanner.BuildResult(input, _culture);
}

public sealed record HyphenationOptionsDialogAcceptance(
    HyphenationOptionsDialogResult? Result,
    string? ValidationMessage)
{
    public bool IsAccepted => Result is not null;
}

public sealed class HyphenationOptionsDialogSession
{
    private readonly CultureInfo _culture;

    public HyphenationOptionsDialogSession(PageSettings page, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
        InitialState = HyphenationOptionsDialogPlanner.BuildInitialState(page, culture);
    }

    public HyphenationOptionsInitialState InitialState { get; }

    public HyphenationOptionsDialogAcceptance PlanAcceptance(HyphenationOptionsDialogInput input) =>
        HyphenationOptionsDialogPlanner.TryBuildResult(input, _culture, out var result, out var error)
            ? new HyphenationOptionsDialogAcceptance(result, ValidationMessage: null)
            : new HyphenationOptionsDialogAcceptance(null, error ?? HyphenationOptionsDialogPlanner.ValidationMessage);
}

public sealed record LineNumberOptionsDialogAcceptance(
    LineNumberOptionsDialogResult? Result,
    string? ValidationMessage)
{
    public bool IsAccepted => Result is not null;
}

public sealed class LineNumberOptionsDialogSession
{
    private readonly CultureInfo _culture;

    public LineNumberOptionsDialogSession(PageSettings page, CultureInfo culture)
        : this(
            page?.LineNumberStartAt ?? throw new ArgumentNullException(nameof(page)),
            page.LineNumberCountBy,
            page.LineNumberMode,
            culture)
    {
    }

    public LineNumberOptionsDialogSession(
        int startAt,
        int countBy,
        LineNumberMode mode,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
        var initialMode = mode == LineNumberMode.None
            ? LineNumberMode.RestartEachPage
            : mode;
        InitialState = LineNumberOptionsDialogPlanner.BuildInitialState(
            startAt,
            countBy,
            initialMode,
            culture);
    }

    public LineNumberOptionsInitialState InitialState { get; }

    public IReadOnlyList<string> ModeLabels => LineNumberOptionsDialogPlanner.ModeLabels;

    public LineNumberOptionsDialogAcceptance PlanAcceptance(LineNumberOptionsDialogInput input) =>
        LineNumberOptionsDialogPlanner.TryBuildResult(input, _culture, out var result, out var error)
            ? new LineNumberOptionsDialogAcceptance(result, ValidationMessage: null)
            : new LineNumberOptionsDialogAcceptance(
                null,
                error ?? LineNumberOptionsDialogPlanner.StartAtValidationMessage);
}

public sealed class ManualHyphenationDialogSession
{
    public ManualHyphenationDialogSession(ManualHyphenationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
    }

    public ManualHyphenationCandidate Candidate { get; }

    public IReadOnlyList<ManualHyphenationOption> Options => Candidate.Options;

    public string CandidateLabel => ManualHyphenationPlanner.FormatCandidateLabel(Candidate.Number);

    public ManualHyphenationDialogResult? PlanAcceptance(int selectedIndex) =>
        selectedIndex >= 0 && selectedIndex < Options.Count
            ? new ManualHyphenationDialogResult(
                ManualHyphenationDialogAction.Accept,
                Options[selectedIndex].BreakPoint)
            : null;

    public ManualHyphenationDialogResult PlanSkip() =>
        new(ManualHyphenationDialogAction.Skip);

    public ManualHyphenationDialogResult PlanCancel() =>
        new(ManualHyphenationDialogAction.Cancel);
}
