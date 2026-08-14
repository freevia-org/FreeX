using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationMainWindowRendererCoordinatorTests
{
    [Fact]
    public void SmartArt_adapter_projects_state_rows_and_accessibility_in_one_sequence()
    {
        var events = new List<string>();
        var accessibility = new List<PresentationPaneAccessibilityItemPlan>();
        var adapter = new PresentationSmartArtTextPaneNativeViewAdapter<string>(
            new(
                SetUpdating: value => events.Add($"updating:{value}"),
                ClearRows: () => events.Add("clear"),
                SetHeading: value => events.Add($"heading:{value}"),
                SetMessage: value => events.Add($"message:{value}"),
                SetApplyEnabled: value => events.Add($"apply:{value}"),
                SetAssistantEnabled: value => events.Add($"assistant:{value}"),
                SetEditActionsEnabled: value => events.Add($"edit:{value}"),
                BuildRow: item => item.ModelId,
                ApplyAccessibility: (_, plan) => accessibility.Add(plan),
                AddRow: row => events.Add($"row:{row}")));
        var plan = new PresentationSmartArtTextPanePlan(
            "SmartArt", "Ready", [new("node-1", "Plan", 0, 0, false)], "node-1",
            CanApply: true, CanToggleAssistant: false, CanEditSelectedRow: true);

        adapter.Render(plan);

        events.Should().Equal(
            "updating:True", "clear", "heading:SmartArt", "message:Ready",
            "apply:True", "assistant:False", "edit:True", "row:node-1", "updating:False");
        accessibility.Should().ContainSingle().Which.State.Should()
            .Be(PresentationPaneAccessibilityPlanner.SelectedState);
    }

    [Fact]
    public void Proofing_action_catalog_preserves_order_enablement_and_native_spacing()
    {
        var row = new PresentationProofingIssueRowPlan(
            2,
            new(PresentationProofingScopeKind.ShapeText, 0, 7, null, null, null, null,
                "Shape", "teh", "teh"),
            0, 3, "teh", "Spelling", "Shape", "Slide 1", "teh", "the", true,
            Action("correct", "Change", true),
            Action("ignore", "Ignore", true),
            Action("ignore-all", "Ignore All", false),
            Action("dictionary", "Add", true));

        var actions = PresentationMainWindowReviewPaneCoordinator.BuildProofingRowActions(row);

        actions.Select(item => item.Kind).Should().Equal(
            PresentationProofingRowActionKind.ApplyCorrection,
            PresentationProofingRowActionKind.Ignore,
            PresentationProofingRowActionKind.IgnoreAll,
            PresentationProofingRowActionKind.AddToDictionary,
            PresentationProofingRowActionKind.Select);
        actions.Select(item => item.MinimumWidth).Should().Equal(72, 72, 72, 120, 72);
        actions.Select(item => item.HasLeadingSpacing).Should().Equal(false, true, true, true, true);
        actions[2].IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Media_control_catalog_owns_labels_and_playback_defaults()
    {
        PresentationMediaPaneControlCatalog.ShowWhenStopped.IsCheckedByDefault.Should().BeTrue();
        PresentationMediaPaneControlCatalog.Loop.IsCheckedByDefault.Should().BeNull();
        PresentationMediaPaneControlCatalog.StopAfterSlides.InitialValue.Should()
            .Be(PresentationMediaPaneSession.DefaultStopAfterSlides.ToString());
        PresentationMediaPaneControlCatalog.TrimStart.Label.Should()
            .Be(PresentationPaneTextResources.TrimStartMilliseconds);
        PresentationMediaPaneControlCatalog.BookmarkTime.Label.Should()
            .Be(PresentationPaneTextResources.BookmarkTimeMilliseconds);
    }

    private static PresentationReviewWorkflowActionPlan Action(
        string id,
        string label,
        bool enabled) =>
        new(
            id,
            label,
            PresentationReviewWorkflowIntentKind.ApplyProofingCorrection,
            enabled,
            PresentationWorkflowCapabilityStatus.Available,
            enabled ? null : "Unavailable");
}
