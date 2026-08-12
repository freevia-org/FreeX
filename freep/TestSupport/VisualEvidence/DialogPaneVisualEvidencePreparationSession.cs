using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum DialogPaneVisualEvidencePreparationStage
{
    Created,
    RoutePrepared,
    DialogCreated,
    LoadedStatePrepared,
    Completed,
}

public sealed record DialogPaneVisualEvidenceChoiceState(
    int TableChoiceCount,
    int DefaultTableChoiceCount,
    int CurrentLayoutChoiceCount,
    int DisabledLayoutChoiceCount);

public sealed record DialogPaneVisualEvidenceValidationResult(
    bool Outcome,
    string? ValidationText = null);

public interface IDialogPaneVisualEvidenceRouteHost
{
    void LoadPresentation(Presentation presentation);
    void SelectShape(uint shapeId);
    void RefreshCanvas();

    IReadOnlyList<uint> SelectedShapeIds { get; }
    int SlideCount { get; }
    int CurrentShapeCount { get; }
    string? CurrentLayoutId { get; }
    DialogPaneVisualEvidenceChoiceState ChoiceState { get; }
    bool IsTablePickerVisible { get; }
    bool IsLayoutPickerVisible { get; }

    void ShowReviewCommentsPane();
    void SelectFirstReviewComment();
    void ShowAccessibilityCheckerPane();
    void SelectFirstAccessibilityIssue();
    void ShowAltTextPane();
    void ShowReadingOrderPane();
    void ShowProofingPane();
    void SelectFirstProofingIssue();
    void ShowMediaCaptionPane();
    void ShowSmartArtTextPane();
    void EnsureAnimationPaneVisible();
    void ShowPrintOptionsPane();
    void OpenTablePicker();
    void OpenLayoutPicker();
    void HideTablePicker();
    void HideLayoutPicker();
}

public interface IDialogPaneVisualEvidenceDialogAdapter<TDialog>
    where TDialog : class
{
    TDialog CreateSlideSize(DialogPaneVisualEvidenceSlideSizePreparation preparation);
    TDialog CreateHeaderFooter(DialogPaneVisualEvidenceHeaderFooterPreparation preparation);
    TDialog CreateFindReplace(DialogPaneVisualEvidenceFindReplacePreparation preparation);
    TDialog CreateHyperlink(
        DialogPaneVisualEvidenceHyperlinkPreparation preparation,
        DialogPaneVisualEvidenceFixture fixture);
    TDialog CreateChartData(DialogPaneVisualEvidenceChartDataPreparation preparation);
    TDialog CreateCustomShows(DialogPaneVisualEvidenceCustomShowsPreparation preparation);
    bool ApplyHyperlinkValidation(TDialog dialog, DialogPaneVisualEvidenceHyperlinkInput input);
    void PrepareCustomShowsValidation(TDialog dialog);
    DialogPaneVisualEvidenceValidationResult PrepareSlideSizeLoadedState(TDialog dialog);
    DialogPaneVisualEvidenceValidationResult PrepareChartDataLoadedState(TDialog dialog);
}

public sealed class DialogPaneVisualEvidencePreparationSession
{
    private DialogPaneVisualEvidencePreparationSession(
        DialogPaneVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture,
        DialogPaneVisualEvidencePreparationPlan plan)
    {
        Scenario = scenario;
        Fixture = fixture;
        Plan = plan;
    }

    public DialogPaneVisualEvidenceScenario Scenario { get; }
    public DialogPaneVisualEvidenceFixture Fixture { get; }
    public DialogPaneVisualEvidencePreparationPlan Plan { get; }
    public DialogPaneVisualEvidencePreparationStage Stage { get; private set; }

    public static DialogPaneVisualEvidencePreparationSession Create(
        DialogPaneVisualEvidenceScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var plan = DialogPaneVisualEvidencePreparationPlanner.Create(scenario);
        var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
        if (plan.FixtureIntent == DialogPaneVisualEvidenceFixtureIntent.ClearCustomShows)
            fixture.Presentation.CustomShows.Clear();
        return new DialogPaneVisualEvidencePreparationSession(scenario, fixture, plan);
    }

    public IReadOnlyList<DialogPaneVisualEvidenceAssertion> PrepareRoute(
        IDialogPaneVisualEvidenceRouteHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        RequireStage(DialogPaneVisualEvidencePreparationStage.Created);

        host.LoadPresentation(Fixture.Presentation);
        var selectionShapeId = Fixture.SelectionForRoute(Scenario.RouteId);
        host.SelectShape(selectionShapeId);
        host.RefreshCanvas();

        var seededSelection = host.SelectedShapeIds.ToArray();
        var beforeShapeCount = host.CurrentShapeCount;
        var beforeLayout = host.CurrentLayoutId;
        ActivateRoute(host);
        var choiceState = host.ChoiceState;

        Stage = DialogPaneVisualEvidencePreparationStage.RoutePrepared;
        return
        [
            new DialogPaneVisualEvidenceAssertion(
                "seeded-presentation",
                host.SlideCount == 3,
                $"Loaded {host.SlideCount} seeded slides."),
            new DialogPaneVisualEvidenceAssertion(
                "seeded-selection",
                seededSelection.SequenceEqual([selectionShapeId]),
                $"Initially selected shape ids: {string.Join(",", seededSelection)}."),
            new DialogPaneVisualEvidenceAssertion(
                "no-preselection-mutation",
                Scenario.SurfaceKind != DialogPaneVisualEvidenceSurfaceKind.ChoiceOverlay ||
                    (host.CurrentShapeCount == beforeShapeCount &&
                     StringComparer.Ordinal.Equals(host.CurrentLayoutId, beforeLayout)),
                "Opening the choice overlay did not mutate shape count or layout."),
            new DialogPaneVisualEvidenceAssertion(
                "choice-state",
                Scenario.RouteId switch
                {
                    "insert.table-picker" =>
                        choiceState.TableChoiceCount == 25 &&
                        choiceState.DefaultTableChoiceCount == 1,
                    "design.layout-picker" =>
                        choiceState.CurrentLayoutChoiceCount == 1 &&
                        choiceState.DisabledLayoutChoiceCount == 1,
                    _ => true,
                },
                "The picker exposes its expected default/current/disabled choice state."),
        ];
    }

    public TDialog CreateDialog<TDialog>(
        IDialogPaneVisualEvidenceDialogAdapter<TDialog> adapter,
        ICollection<DialogPaneVisualEvidenceAssertion> assertions)
        where TDialog : class
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(assertions);
        RequireStage(DialogPaneVisualEvidencePreparationStage.RoutePrepared);

        var preparation = Plan.Dialog ?? throw new InvalidOperationException(
            $"Missing dialog preparation plan for {Scenario.Id}.");
        var dialog = preparation switch
        {
            DialogPaneVisualEvidenceSlideSizePreparation slideSize =>
                adapter.CreateSlideSize(slideSize),
            DialogPaneVisualEvidenceHeaderFooterPreparation headerFooter =>
                adapter.CreateHeaderFooter(headerFooter),
            DialogPaneVisualEvidenceFindReplacePreparation findReplace =>
                adapter.CreateFindReplace(findReplace),
            DialogPaneVisualEvidenceHyperlinkPreparation hyperlink =>
                CreateHyperlink(adapter, hyperlink, assertions),
            DialogPaneVisualEvidenceChartDataPreparation chartData =>
                adapter.CreateChartData(chartData),
            DialogPaneVisualEvidenceCustomShowsPreparation customShows =>
                CreateCustomShows(adapter, customShows),
            _ => throw new InvalidOperationException(
                $"No visual-evidence dialog adapter contract for {preparation.GetType().Name}."),
        };

        Stage = DialogPaneVisualEvidencePreparationStage.DialogCreated;
        return dialog;
    }

    public void PrepareLoadedDialogState<TDialog>(
        TDialog? dialog,
        IDialogPaneVisualEvidenceDialogAdapter<TDialog> adapter,
        ICollection<DialogPaneVisualEvidenceAssertion> assertions)
        where TDialog : class
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(assertions);
        if (Stage is not (DialogPaneVisualEvidencePreparationStage.RoutePrepared or
            DialogPaneVisualEvidencePreparationStage.DialogCreated))
        {
            throw new InvalidOperationException(
                $"Cannot prepare loaded dialog state from stage {Stage}.");
        }

        var preparation = Plan.Dialog;
        if (dialog is not null &&
            preparation?.ValidationIntent == DialogPaneVisualEvidenceValidationIntent.AfterLoad)
        {
            DialogPaneVisualEvidenceAssertion? assertion = preparation switch
            {
                DialogPaneVisualEvidenceSlideSizePreparation slideSize =>
                    Evaluate(
                        adapter.PrepareSlideSizeLoadedState(dialog),
                        slideSize.EvaluateExpectedAssertion),
                DialogPaneVisualEvidenceChartDataPreparation chartData =>
                    Evaluate(
                        adapter.PrepareChartDataLoadedState(dialog),
                        chartData.EvaluateExpectedAssertion),
                _ => throw new InvalidOperationException(
                    $"No loaded-state adapter contract for {preparation.GetType().Name}."),
            };
            if (assertion is not null)
                assertions.Add(assertion);
        }

        Stage = DialogPaneVisualEvidencePreparationStage.LoadedStatePrepared;
    }

    public IReadOnlyList<DialogPaneVisualEvidenceAssertion> CompleteRoute(
        IDialogPaneVisualEvidenceRouteHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        RequireStage(DialogPaneVisualEvidencePreparationStage.LoadedStatePrepared);

        if (Scenario.RouteId == "insert.table-picker")
            host.HideTablePicker();
        else if (Scenario.RouteId == "design.layout-picker")
            host.HideLayoutPicker();

        Stage = DialogPaneVisualEvidencePreparationStage.Completed;
        return Scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.ChoiceOverlay
            ?
            [
                new DialogPaneVisualEvidenceAssertion(
                    "dismissal",
                    !host.IsTablePickerVisible && !host.IsLayoutPickerVisible,
                    "Choice overlay is hidden after dismissal."),
            ]
            : [];
    }

    private void ActivateRoute(IDialogPaneVisualEvidenceRouteHost host)
    {
        switch (Scenario.RouteId)
        {
            case "review.comments-pane":
                host.ShowReviewCommentsPane();
                host.SelectFirstReviewComment();
                break;
            case "review.accessibility-pane":
                host.ShowAccessibilityCheckerPane();
                host.SelectFirstAccessibilityIssue();
                break;
            case "review.alt-text-pane":
                host.ShowAltTextPane();
                break;
            case "review.reading-order-pane":
                host.ShowReadingOrderPane();
                break;
            case "review.proofing-pane":
                host.ShowProofingPane();
                host.SelectFirstProofingIssue();
                break;
            case "accessibility.media-caption-pane":
                host.ShowMediaCaptionPane();
                break;
            case "context.smartart-text-pane":
                host.ShowSmartArtTextPane();
                break;
            case "animations.animation-pane":
                host.EnsureAnimationPaneVisible();
                break;
            case "file.print-options":
                host.ShowPrintOptionsPane();
                break;
            case "insert.table-picker":
                host.OpenTablePicker();
                break;
            case "design.layout-picker":
                host.OpenLayoutPicker();
                break;
        }
    }

    private TDialog CreateHyperlink<TDialog>(
        IDialogPaneVisualEvidenceDialogAdapter<TDialog> adapter,
        DialogPaneVisualEvidenceHyperlinkPreparation preparation,
        ICollection<DialogPaneVisualEvidenceAssertion> assertions)
        where TDialog : class
    {
        var dialog = adapter.CreateHyperlink(preparation, Fixture);
        if (preparation.ValidationIntent == DialogPaneVisualEvidenceValidationIntent.BeforeShow)
        {
            var input = preparation.ValidationInput ?? throw new InvalidOperationException(
                "Hyperlink visual-evidence validation requires input values.");
            var outcome = adapter.ApplyHyperlinkValidation(dialog, input);
            if (preparation.EvaluateExpectedAssertion(outcome) is { } assertion)
                assertions.Add(assertion);
        }
        return dialog;
    }

    private static TDialog CreateCustomShows<TDialog>(
        IDialogPaneVisualEvidenceDialogAdapter<TDialog> adapter,
        DialogPaneVisualEvidenceCustomShowsPreparation preparation)
        where TDialog : class
    {
        var dialog = adapter.CreateCustomShows(preparation);
        if (preparation.ValidationIntent == DialogPaneVisualEvidenceValidationIntent.BeforeShow)
            adapter.PrepareCustomShowsValidation(dialog);
        return dialog;
    }

    private static DialogPaneVisualEvidenceAssertion? Evaluate(
        DialogPaneVisualEvidenceValidationResult result,
        Func<bool, string?, DialogPaneVisualEvidenceAssertion?> evaluator) =>
        evaluator(result.Outcome, result.ValidationText);

    private void RequireStage(DialogPaneVisualEvidencePreparationStage expected)
    {
        if (Stage != expected)
        {
            throw new InvalidOperationException(
                $"Expected visual-evidence preparation stage {expected}, but was {Stage}.");
        }
    }
}
