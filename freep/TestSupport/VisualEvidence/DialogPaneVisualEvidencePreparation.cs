using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum DialogPaneVisualEvidenceFocusIntent
{
    None,
    PreserveNativeOrFirstEditable,
}

public enum DialogPaneVisualEvidenceFixtureIntent
{
    Preserve,
    ClearCustomShows,
}

public enum DialogPaneVisualEvidenceValidationIntent
{
    None,
    BeforeShow,
    AfterLoad,
}

public sealed record DialogPaneVisualEvidenceExpectedAssertion(
    string Id,
    bool ExpectedOperationResult,
    string PassedDetail,
    string FailedDetail)
{
    private const string ValidationDetailToken = "{validation}";

    public DialogPaneVisualEvidenceAssertion Evaluate(
        bool operationResult,
        string? validationDetail = null)
    {
        var passed = operationResult == ExpectedOperationResult;
        var detail = passed ? PassedDetail : FailedDetail;
        return new(
            Id,
            passed,
            detail.Replace(
                ValidationDetailToken,
                validationDetail ?? string.Empty,
                StringComparison.Ordinal));
    }
}

public abstract record DialogPaneVisualEvidenceDialogPreparation(
    DialogPaneVisualEvidenceValidationIntent ValidationIntent,
    DialogPaneVisualEvidenceExpectedAssertion? ExpectedAssertion)
{
    public DialogPaneVisualEvidenceAssertion? EvaluateExpectedAssertion(
        bool operationResult,
        string? validationDetail = null) =>
        ExpectedAssertion?.Evaluate(operationResult, validationDetail);
}

public sealed record DialogPaneVisualEvidenceSlideSizeInput(
    string WidthText,
    string HeightText,
    SlideSizeDialogUnit Unit);

public sealed record DialogPaneVisualEvidenceSlideSizePreparation(
    DialogPaneVisualEvidenceSlideSizeInput? InitialInput,
    DialogPaneVisualEvidenceValidationIntent ValidationIntent = DialogPaneVisualEvidenceValidationIntent.None,
    DialogPaneVisualEvidenceExpectedAssertion? ExpectedAssertion = null)
    : DialogPaneVisualEvidenceDialogPreparation(ValidationIntent, ExpectedAssertion);

public sealed record DialogPaneVisualEvidenceHeaderFooterPreparation(
    HeaderFooterCommandFocus InitialFocus,
    bool ShowDateTime,
    bool ShowFooter,
    bool ShowSlideNumber,
    string FooterText)
    : DialogPaneVisualEvidenceDialogPreparation(
        DialogPaneVisualEvidenceValidationIntent.None,
        null);

public sealed record DialogPaneVisualEvidenceFindReplacePreparation(
    bool ReplaceMode,
    string Query,
    string Replacement,
    bool MatchCase,
    bool WholeWord)
    : DialogPaneVisualEvidenceDialogPreparation(
        DialogPaneVisualEvidenceValidationIntent.None,
        null);

public sealed record DialogPaneVisualEvidenceHyperlinkValue(
    string Url,
    string Tooltip)
{
    public Hyperlink ToModel() => new()
    {
        Url = this.Url,
        Tooltip = this.Tooltip,
    };
}

public sealed record DialogPaneVisualEvidenceHyperlinkInput(
    HyperlinkDialogTargetKind TargetKind,
    string Url,
    int SelectedSlideIndex,
    string Tooltip);

public sealed record DialogPaneVisualEvidenceHyperlinkPreparation(
    DialogPaneVisualEvidenceHyperlinkValue? InitialLink,
    DialogPaneVisualEvidenceHyperlinkInput? ValidationInput,
    DialogPaneVisualEvidenceValidationIntent ValidationIntent = DialogPaneVisualEvidenceValidationIntent.None,
    DialogPaneVisualEvidenceExpectedAssertion? ExpectedAssertion = null)
    : DialogPaneVisualEvidenceDialogPreparation(ValidationIntent, ExpectedAssertion);

public sealed record DialogPaneVisualEvidenceChartDataPreparation(
    DialogPaneVisualEvidenceValidationIntent ValidationIntent = DialogPaneVisualEvidenceValidationIntent.None,
    DialogPaneVisualEvidenceExpectedAssertion? ExpectedAssertion = null)
    : DialogPaneVisualEvidenceDialogPreparation(ValidationIntent, ExpectedAssertion);

public sealed record DialogPaneVisualEvidenceCustomShowsPreparation(
    DialogPaneVisualEvidenceValidationIntent ValidationIntent = DialogPaneVisualEvidenceValidationIntent.None)
    : DialogPaneVisualEvidenceDialogPreparation(ValidationIntent, null);

public sealed record DialogPaneVisualEvidencePreparationPlan(
    DialogPaneVisualEvidenceDialogPreparation? Dialog,
    DialogPaneVisualEvidenceFixtureIntent FixtureIntent,
    DialogPaneVisualEvidenceFocusIntent FocusIntent);

public static class DialogPaneVisualEvidencePreparationPlanner
{
    private static readonly DialogPaneVisualEvidenceExpectedAssertion SlideSizeValidationAssertion = new(
        "validation-visible",
        ExpectedOperationResult: false,
        PassedDetail: "{validation}",
        FailedDetail: "{validation}");

    private static readonly DialogPaneVisualEvidenceExpectedAssertion HyperlinkValidationAssertion = new(
        "validation-visible",
        ExpectedOperationResult: false,
        PassedDetail: "Invalid URL remains open with inline validation.",
        FailedDetail: "Invalid URL remains open with inline validation.");

    private static readonly DialogPaneVisualEvidenceExpectedAssertion ChartValidationAssertion = new(
        "validation-visible",
        ExpectedOperationResult: true,
        PassedDetail: "Invalid chart value remains open with inline validation: {validation}",
        FailedDetail: "The chart dialog could not enter and reject an invalid numeric cell.");

    public static DialogPaneVisualEvidencePreparationPlan Create(
        DialogPaneVisualEvidenceScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        if (scenario.SurfaceKind != DialogPaneVisualEvidenceSurfaceKind.Dialog)
        {
            return new(
                null,
                DialogPaneVisualEvidenceFixtureIntent.Preserve,
                FocusIntentFor(scenario));
        }

        return (scenario.RouteId, scenario.StateId) switch
        {
            ("design.slide-size", "initial") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceSlideSizePreparation(null)),
            ("design.slide-size", "invalid") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceSlideSizePreparation(
                    new("0", "7.5", SlideSizeDialogUnit.Inches),
                    DialogPaneVisualEvidenceValidationIntent.AfterLoad,
                    SlideSizeValidationAssertion)),

            ("insert.header-footer", "date-time") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceHeaderFooterPreparation(
                    HeaderFooterCommandFocus.DateTime,
                    ShowDateTime: true,
                    ShowFooter: false,
                    ShowSlideNumber: false,
                    FooterText: string.Empty)),
            ("insert.header-footer", "apply-to-all") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceHeaderFooterPreparation(
                    HeaderFooterCommandFocus.Footer,
                    ShowDateTime: true,
                    ShowFooter: true,
                    ShowSlideNumber: true,
                    FooterText: "Confidential")),

            ("home.find-replace", "find") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceFindReplacePreparation(
                    ReplaceMode: false,
                    Query: "revenue",
                    Replacement: string.Empty,
                    MatchCase: false,
                    WholeWord: false)),
            ("home.find-replace", "replace") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceFindReplacePreparation(
                    ReplaceMode: true,
                    Query: "revenue",
                    Replacement: "sales",
                    MatchCase: false,
                    WholeWord: false)),

            ("insert.hyperlink", "initial") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceHyperlinkPreparation(null, null)),
            ("insert.hyperlink", "validation") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceHyperlinkPreparation(
                    InitialLink: null,
                    ValidationInput: new(
                        HyperlinkDialogTargetKind.Url,
                        "not a url",
                        0,
                        string.Empty),
                    DialogPaneVisualEvidenceValidationIntent.BeforeShow,
                    HyperlinkValidationAssertion)),
            ("insert.hyperlink", "populated") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceHyperlinkPreparation(
                    new("https://example.com/review", "Open review"),
                    null)),

            ("chart.edit-data", "initial") or
            ("chart.edit-data", "populated") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceChartDataPreparation()),
            ("chart.edit-data", "validation") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceChartDataPreparation(
                    DialogPaneVisualEvidenceValidationIntent.AfterLoad,
                    ChartValidationAssertion)),

            ("slideshow.custom-shows", "initial") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceCustomShowsPreparation(),
                DialogPaneVisualEvidenceFixtureIntent.ClearCustomShows),
            ("slideshow.custom-shows", "validation") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceCustomShowsPreparation(
                    DialogPaneVisualEvidenceValidationIntent.BeforeShow),
                DialogPaneVisualEvidenceFixtureIntent.ClearCustomShows),
            ("slideshow.custom-shows", "populated") => Dialog(
                scenario,
                new DialogPaneVisualEvidenceCustomShowsPreparation()),

            _ => throw new InvalidOperationException(
                $"No dialog visual-evidence preparation plan for {scenario.Id}."),
        };
    }

    private static DialogPaneVisualEvidencePreparationPlan Dialog(
        DialogPaneVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceDialogPreparation preparation,
        DialogPaneVisualEvidenceFixtureIntent fixtureIntent = DialogPaneVisualEvidenceFixtureIntent.Preserve) =>
        new(preparation, fixtureIntent, FocusIntentFor(scenario));

    private static DialogPaneVisualEvidenceFocusIntent FocusIntentFor(
        DialogPaneVisualEvidenceScenario scenario) =>
        scenario.CompareFocus
            ? DialogPaneVisualEvidenceFocusIntent.PreserveNativeOrFirstEditable
            : DialogPaneVisualEvidenceFocusIntent.None;
}
