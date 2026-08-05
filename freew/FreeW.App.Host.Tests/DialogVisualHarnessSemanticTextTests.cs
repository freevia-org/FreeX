extern alias Harness;
using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class DialogVisualHarnessSemanticTextTests
{
    [Fact]
    public void Three_row_route_refresh_retains_unrelated_rows_and_full_report_counts()
    {
        var unrelatedRows = new[]
        {
            new Harness::ComparisonRow("other-route.initial", "captured/captured", "pass", null, null, "heatmaps/other-route.initial.png", null),
            new Harness::ComparisonRow("other-route.validation-error", "captured/captured", "genuine-visual-mismatch", null, null, "heatmaps/other-route.validation-error.png", null),
            new Harness::ComparisonRow("another-route.initial", "avalonia-extension", "avalonia-extension", null, null, null, "extension")
        };
        var baselineRows = unrelatedRows.Concat(new[]
        {
            new Harness::ComparisonRow("multilevel-list.initial", "captured/captured", "genuine-visual-mismatch", null, "old", "heatmaps/old.initial.png", null),
            new Harness::ComparisonRow("multilevel-list.populated", "captured/captured", "genuine-visual-mismatch", null, "old", "heatmaps/old.populated.png", null),
            new Harness::ComparisonRow("multilevel-list.validation-error", "captured/captured", "genuine-visual-mismatch", null, "old", "heatmaps/old.validation-error.png", null)
        }).ToArray();
        var baseline = new Harness::ComparisonReport(
            "freew.dialog-visual-comparison.v1",
            "baseline-source",
            96,
            466,
            187,
            279,
            baselineRows,
            new Dictionary<string, int> { ["genuine-visual-mismatch"] = 3, ["pass"] = 1, ["avalonia-extension"] = 1 },
            new Harness::ComparisonScope("canonical-inputs-only", "test baseline", "test refresh"));
        var refreshedRows = new[]
        {
            new Harness::ComparisonRow("multilevel-list.initial", "captured/captured", "pass", null, null, "heatmaps/multilevel-list.initial.png", null),
            new Harness::ComparisonRow("multilevel-list.populated", "captured/captured", "pass", null, null, "heatmaps/multilevel-list.populated.png", null),
            new Harness::ComparisonRow("multilevel-list.validation-error", "captured/captured", "pass", null, null, "heatmaps/multilevel-list.validation-error.png", null)
        };

        var merged = Harness::ComparisonReportMerger.Merge(baseline, refreshedRows, "multilevel-list");

        merged.Rows.Should().HaveCount(6);
        merged.Rows.Where(row => !row.ScenarioId.StartsWith("multilevel-list.", StringComparison.Ordinal))
            .Should().Equal(unrelatedRows);
        merged.Rows.Where(row => row.ScenarioId.StartsWith("multilevel-list.", StringComparison.Ordinal))
            .Should().BeEquivalentTo(refreshedRows, options => options.WithStrictOrdering());
        merged.InventoryScenarioCount.Should().Be(466);
        merged.WpfCaptureCount.Should().Be(187);
        merged.AvaloniaCaptureCount.Should().Be(279);
        merged.GeneratedFromSha256.Should().Be("baseline-source");
        merged.TargetDpi.Should().Be(96);
        merged.Counts.Should().Contain(new KeyValuePair<string, int>("pass", 4));
        merged.Counts.Should().Contain(new KeyValuePair<string, int>("genuine-visual-mismatch", 1));
        merged.Counts.Should().Contain(new KeyValuePair<string, int>("avalonia-extension", 1));
    }

    [Fact]
    public void Route_refresh_removes_stale_baseline_rows_missing_from_refreshed_rows()
    {
        var baseline = new Harness::ComparisonReport(
            "schema",
            "baseline-source",
            120,
            6,
            4,
            5,
            new[]
            {
                new Harness::ComparisonRow("multilevel-list.initial", "captured/captured", "pass", null, null, "old-initial", null),
                new Harness::ComparisonRow("multilevel-list.populated", "captured/captured", "pass", null, null, "old-populated", null),
                new Harness::ComparisonRow("multilevel-list.validation-error", "captured/captured", "pass", null, null, "old-validation", null),
                new Harness::ComparisonRow("other-route.initial", "captured/captured", "pass", null, null, "other", null)
            },
            new Dictionary<string, int>(),
            new Harness::ComparisonScope("canonical-inputs-only", "test baseline", "test refresh"));
        var refreshed = new[]
        {
            new Harness::ComparisonRow("multilevel-list.initial", "captured/captured", "genuine-visual-mismatch", null, null, "new-initial", null),
            new Harness::ComparisonRow("multilevel-list.populated", "captured/captured", "genuine-visual-mismatch", null, null, "new-populated", null)
        };

        var merged = Harness::ComparisonReportMerger.Merge(baseline, refreshed, "multilevel-list");

        merged.Rows.Select(row => row.ScenarioId).Should().Equal(
            "multilevel-list.initial",
            "multilevel-list.populated",
            "other-route.initial");
        merged.Rows.Should().NotContain(row => row.ScenarioId == "multilevel-list.validation-error");
        merged.InventoryScenarioCount.Should().Be(6);
        merged.WpfCaptureCount.Should().Be(4);
        merged.AvaloniaCaptureCount.Should().Be(5);
    }

    [Fact]
    public void Shared_action_predicate_ignores_visible_unnamed_internal_buttons()
    {
        var included = Harness::FreeW.DialogVisualHarness.DialogSemanticText.TryResolveActionButtonText(
            isVisible: true,
            automationName: null,
            content: null,
            out var actionText);

        included.Should().BeFalse();
        actionText.Should().BeEmpty();
    }

    [Fact]
    public void Shared_action_predicate_includes_visible_named_actions_and_normalizes_access_keys()
    {
        var included = Harness::FreeW.DialogVisualHarness.DialogSemanticText.TryResolveActionButtonText(
            isVisible: true,
            automationName: "_Save __As",
            content: null,
            out var actionText);

        included.Should().BeTrue();
        actionText.Should().Be("Save _As");
    }

    [Fact]
    public void Shared_action_predicate_requires_visibility_and_accepts_user_facing_content()
    {
        Harness::FreeW.DialogVisualHarness.DialogSemanticText.TryResolveActionButtonText(
                isVisible: false,
                automationName: "OK",
                content: "_OK",
                out _)
            .Should().BeFalse();

        Harness::FreeW.DialogVisualHarness.DialogSemanticText.TryResolveActionButtonText(
                isVisible: true,
                automationName: " ",
                content: "_Cancel",
                out var actionText)
            .Should().BeTrue();
        actionText.Should().Be("Cancel");
    }

    [Fact]
    public void Both_visual_harnesses_use_the_shared_button_text_normalization()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Wpf", "Program.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "Program.cs"));

        wpf.Should().Contain("DialogSemanticText.TryResolveActionButtonText(");
        avalonia.Should().Contain("DialogSemanticText.TryResolveActionButtonText(");
        wpf.Should().Contain("static IReadOnlyList<(Button Button, string Text)> ReadActionButtons(Window dialog)");
        avalonia.Should().Contain("static IReadOnlyList<(Button Button, string Text)> ReadActionButtons(Window dialog)");
        wpf.Should().NotContain("button.GetType().Name");
        avalonia.Should().NotContain("button.GetType().Name");
    }

    [Fact]
    public void Both_visual_harnesses_preserve_the_symbol_picker_authority_focus_state()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Wpf", "Program.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "Program.cs"));

        wpf.Should().Contain("if (scenario.RouteId == \"symbol-picker\")");
        avalonia.Should().Contain("if (scenario.RouteId == \"symbol-picker\")");
    }

    [Fact]
    public void Avalonia_semantics_only_enumerate_buttons_attached_to_the_rendered_visual_tree()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "Program.cs"));

        avalonia.Should().Contain("dialog.GetVisualDescendants().OfType<Button>()",
            "logical descendants from inactive tabs must not change the selected dialog's action order");
    }
}
