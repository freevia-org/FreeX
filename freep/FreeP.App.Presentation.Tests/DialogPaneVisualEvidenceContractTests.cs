using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class DialogPaneVisualEvidenceContractTests
{
    [Fact]
    public void Catalog_covers_every_app_owned_inventory_route_and_requested_state()
    {
        DialogPaneVisualEvidenceCatalog.All.Should().HaveCount(28);
        DialogPaneVisualEvidenceCatalog.All.Select(scenario => scenario.Id).Should().OnlyHaveUniqueItems();
        DialogPaneVisualEvidenceCatalog.All.Select(scenario => scenario.RouteId).Distinct().Should().HaveCount(19);
        DialogPaneVisualEvidenceCatalog.All.Count(scenario => scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog)
            .Should().Be(15);
        DialogPaneVisualEvidenceCatalog.All.Count(scenario => scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Pane)
            .Should().Be(11);
        DialogPaneVisualEvidenceCatalog.All.Count(scenario => scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.ChoiceOverlay)
            .Should().Be(2);

        DialogPaneVisualEvidenceCatalog.All
            .Where(scenario => scenario.RouteId is "insert.hyperlink" or "chart.edit-data" or "slideshow.custom-shows")
            .GroupBy(scenario => scenario.RouteId)
            .Should().AllSatisfy(group => group.Select(scenario => scenario.StateId)
                .Should().Equal("initial", "validation", "populated"));
    }

    [Fact]
    public void Preparation_planner_covers_the_catalog_with_typed_dialog_plans()
    {
        var plans = DialogPaneVisualEvidenceCatalog.All
            .Select(scenario => (Scenario: scenario, Plan: DialogPaneVisualEvidencePreparationPlanner.Create(scenario)))
            .ToArray();

        plans.Where(item => item.Scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog)
            .Should().AllSatisfy(item =>
            {
                item.Plan.Dialog.Should().NotBeNull();
                item.Plan.FocusIntent.Should().Be(
                    DialogPaneVisualEvidenceFocusIntent.PreserveNativeOrFirstEditable);
            });
        plans.Where(item => item.Scenario.SurfaceKind != DialogPaneVisualEvidenceSurfaceKind.Dialog)
            .Should().AllSatisfy(item =>
            {
                item.Plan.Dialog.Should().BeNull();
                item.Plan.FixtureIntent.Should().Be(DialogPaneVisualEvidenceFixtureIntent.Preserve);
                item.Plan.FocusIntent.Should().Be(DialogPaneVisualEvidenceFocusIntent.None);
            });

        plans.Count(item => item.Plan.Dialog is DialogPaneVisualEvidenceSlideSizePreparation).Should().Be(2);
        plans.Count(item => item.Plan.Dialog is DialogPaneVisualEvidenceHeaderFooterPreparation).Should().Be(2);
        plans.Count(item => item.Plan.Dialog is DialogPaneVisualEvidenceFindReplacePreparation).Should().Be(2);
        plans.Count(item => item.Plan.Dialog is DialogPaneVisualEvidenceHyperlinkPreparation).Should().Be(3);
        plans.Count(item => item.Plan.Dialog is DialogPaneVisualEvidenceChartDataPreparation).Should().Be(3);
        plans.Count(item => item.Plan.Dialog is DialogPaneVisualEvidenceCustomShowsPreparation).Should().Be(3);
    }

    [Fact]
    public void Preparation_planner_owns_initial_values_focus_validation_and_fixture_intent()
    {
        var slideSize = Dialog<DialogPaneVisualEvidenceSlideSizePreparation>("design.slide-size.invalid");
        slideSize.InitialInput.Should().Be(new DialogPaneVisualEvidenceSlideSizeInput(
            "0",
            "7.5",
            SlideSizeDialogUnit.Inches));
        slideSize.ValidationIntent.Should().Be(DialogPaneVisualEvidenceValidationIntent.AfterLoad);
        slideSize.EvaluateExpectedAssertion(false, "Width must be greater than zero.")
            .Should().Be(new DialogPaneVisualEvidenceAssertion(
                "validation-visible",
                true,
                "Width must be greater than zero."));

        var headerFooter = Dialog<DialogPaneVisualEvidenceHeaderFooterPreparation>(
            "insert.header-footer.apply-to-all");
        headerFooter.InitialFocus.Should().Be(HeaderFooterCommandFocus.Footer);
        headerFooter.ShowDateTime.Should().BeTrue();
        headerFooter.ShowFooter.Should().BeTrue();
        headerFooter.ShowSlideNumber.Should().BeTrue();
        headerFooter.FooterText.Should().Be("Confidential");

        var findReplace = Dialog<DialogPaneVisualEvidenceFindReplacePreparation>("home.find-replace.replace");
        findReplace.Should().Be(new DialogPaneVisualEvidenceFindReplacePreparation(
            ReplaceMode: true,
            Query: "revenue",
            Replacement: "sales",
            MatchCase: false,
            WholeWord: false));

        var hyperlink = Dialog<DialogPaneVisualEvidenceHyperlinkPreparation>("insert.hyperlink.validation");
        hyperlink.InitialLink.Should().BeNull();
        hyperlink.ValidationInput.Should().Be(new DialogPaneVisualEvidenceHyperlinkInput(
            HyperlinkDialogTargetKind.Url,
            "not a url",
            0,
            string.Empty));
        hyperlink.ValidationIntent.Should().Be(DialogPaneVisualEvidenceValidationIntent.BeforeShow);
        hyperlink.EvaluateExpectedAssertion(false).Should().Be(new DialogPaneVisualEvidenceAssertion(
            "validation-visible",
            true,
            "Invalid URL remains open with inline validation."));
        var populatedHyperlink = Dialog<DialogPaneVisualEvidenceHyperlinkPreparation>(
            "insert.hyperlink.populated");
        populatedHyperlink.InitialLink.Should().Be(new DialogPaneVisualEvidenceHyperlinkValue(
            "https://example.com/review",
            "Open review"));

        var chart = Dialog<DialogPaneVisualEvidenceChartDataPreparation>("chart.edit-data.validation");
        chart.ValidationIntent.Should().Be(DialogPaneVisualEvidenceValidationIntent.AfterLoad);
        chart.EvaluateExpectedAssertion(true, "Enter a number.").Should().Be(new DialogPaneVisualEvidenceAssertion(
            "validation-visible",
            true,
            "Invalid chart value remains open with inline validation: Enter a number."));
        chart.EvaluateExpectedAssertion(false).Should().Be(new DialogPaneVisualEvidenceAssertion(
            "validation-visible",
            false,
            "The chart dialog could not enter and reject an invalid numeric cell."));

        var customShows = DialogPaneVisualEvidencePreparationPlanner.Create(
            DialogPaneVisualEvidenceCatalog.Get("slideshow.custom-shows.validation"));
        customShows.FixtureIntent.Should().Be(DialogPaneVisualEvidenceFixtureIntent.ClearCustomShows);
        customShows.Dialog.Should().BeOfType<DialogPaneVisualEvidenceCustomShowsPreparation>()
            .Which.ValidationIntent.Should().Be(DialogPaneVisualEvidenceValidationIntent.BeforeShow);
        DialogPaneVisualEvidencePreparationPlanner.Create(
                DialogPaneVisualEvidenceCatalog.Get("slideshow.custom-shows.populated"))
            .FixtureIntent.Should().Be(DialogPaneVisualEvidenceFixtureIntent.Preserve);
    }

    [Fact]
    public void Native_capture_adapters_do_not_reintroduce_dialog_route_state_tables()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var paths = new[]
        {
            Path.Combine(root, "freep", "FreeP.App.Host", "WpfDialogPaneVisualEvidenceCapture.cs"),
            Path.Combine(root, "freep", "FreeP.App.Avalonia", "AvaloniaDialogPaneVisualEvidenceCapture.cs"),
        };
        var dialogRouteLiterals = new[]
        {
            "design.slide-size",
            "insert.header-footer",
            "home.find-replace",
            "insert.hyperlink",
            "chart.edit-data",
            "slideshow.custom-shows",
        };

        foreach (var path in paths)
        {
            var source = File.ReadAllText(path);
            source.Should().Contain("DialogPaneVisualEvidencePreparationSession.Create(scenario)");
            source.Should().Contain("preparation.CreateDialog(dialogAdapter, assertions)");
            source.Should().Contain("preparation.PrepareLoadedDialogState(dialog, dialogAdapter, assertions)");
            source.Should().NotContain("DialogPaneVisualEvidencePreparationPlanner.Create(scenario)");
            source.Should().NotContain("switch (scenario.RouteId)");
            source.Should().NotContain("switch (preparation)");
            source.Should().NotContain("scenario.StateId ==");
            source.Should().NotContain("scenario.StateId !=");
            foreach (var route in dialogRouteLiterals)
                source.Should().NotContain($"\"{route}\"");
        }
    }

    [Fact]
    public void Main_window_capture_partials_are_native_route_callback_adapters_only()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var paths = new[]
        {
            Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.VisualEvidence.cs"),
            Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.VisualEvidence.cs"),
        };

        foreach (var path in paths)
        {
            var source = File.ReadAllText(path);
            source.Should().Contain("IDialogPaneVisualEvidenceRouteHost");
            source.Should().Contain("CreateDialogPaneVisualEvidenceRouteHost()");
            source.Should().NotContain("seeded-presentation");
            source.Should().NotContain("no-preselection-mutation");
            source.Should().NotContain("PrepareDialogPaneVisualEvidence(");
            source.Should().NotContain("CompleteDialogPaneVisualEvidence(");
        }
    }

    [Fact]
    public void Comparer_reports_pass_for_equivalent_nonblank_pair()
    {
        var scenario = DialogPaneVisualEvidenceCatalog.Get("design.slide-size.initial");
        var wpf = Capture("wpf", nonBackgroundPixels: 20);
        var avalonia = Capture("avalonia", nonBackgroundPixels: 20);

        var comparison = DialogPaneVisualEvidenceComparer.Compare(scenario, wpf, avalonia);

        comparison.Classification.Should().Be(DialogPaneVisualEvidenceClassification.Pass);
        comparison.Details.Should().BeEmpty();
    }

    [Fact]
    public void Comparer_reports_visual_mismatch_without_turning_it_into_a_semantic_claim()
    {
        var scenario = DialogPaneVisualEvidenceCatalog.Get("design.slide-size.initial");
        var wpf = Capture("wpf", nonBackgroundPixels: 20);
        var avalonia = Capture("avalonia", nonBackgroundPixels: 0) with
        {
            LogicalHeight = 310,
            Buttons = [new("cancel", "Cancel", true, false, true), new("ok", "OK", true, true, false)],
        };

        var comparison = DialogPaneVisualEvidenceComparer.Compare(scenario, wpf, avalonia);

        comparison.Classification.Should().Be(DialogPaneVisualEvidenceClassification.Mismatch);
        comparison.DimensionsMatch.Should().BeFalse();
        comparison.ButtonOrderMatches.Should().BeFalse();
        comparison.AvaloniaNonblank.Should().BeFalse();
    }

    [Fact]
    public void Comparer_reports_missing_capture_as_limitation()
    {
        var scenario = DialogPaneVisualEvidenceCatalog.Get("review.comments-pane.seeded");

        var comparison = DialogPaneVisualEvidenceComparer.Compare(scenario, Capture("wpf", 20), null);

        comparison.Classification.Should().Be(DialogPaneVisualEvidenceClassification.Limitation);
        comparison.Details.Should().ContainSingle().Which.Should().Contain("Avalonia capture is missing");
    }

    private static DialogPaneVisualEvidenceCapture Capture(string host, long nonBackgroundPixels) =>
        new(
            "design.slide-size.initial",
            "design.slide-size",
            "initial",
            host,
            "complete",
            $"captures/{host}/design.slide-size.initial.png",
            380,
            260,
            380,
            260,
            96,
            96,
            nonBackgroundPixels,
            "textbox",
            "Width",
            [new("ok", "OK", true, true, false), new("cancel", "Cancel", true, false, true)],
            [new("textbox", "Width", true)],
            [new("state-prepared", true, "State prepared.")],
            []);

    private static TPreparation Dialog<TPreparation>(string scenarioId)
        where TPreparation : DialogPaneVisualEvidenceDialogPreparation
    {
        var preparation = DialogPaneVisualEvidencePreparationPlanner.Create(
            DialogPaneVisualEvidenceCatalog.Get(scenarioId)).Dialog;
        preparation.Should().BeOfType<TPreparation>();
        return (TPreparation)preparation!;
    }
}
