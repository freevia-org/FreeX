using System.IO;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewTrackingRibbonWorkflowTests
{
    [Fact]
    public void Register_drives_shared_tracking_display_and_markup_workflow()
    {
        var registry = new RibbonCommandRegistry();
        var prepareCount = 0;
        var trackChanges = false;
        var hasSelection = true;
        var markedSelections = 0;
        var trackFormatting = true;
        var displayMode = ReviewDisplayMode.AllMarkup;
        var showInsertions = true;
        var showComments = true;
        var showFormatting = true;
        var acceptedAll = 0;
        var rejectedAll = 0;

        var commands = ReviewTrackingRibbonWorkflow.Register(
            registry,
            new ReviewTrackingCommandBindings(
                PrepareExecution: () => prepareCount++,
                IsTrackChangesEnabled: () => trackChanges,
                HasSelection: () => hasSelection,
                ToggleTrackChanges: () => trackChanges = !trackChanges,
                MarkSelectionAsInsertion: () => markedSelections++,
                IsTrackFormattingEnabled: () => trackFormatting,
                ToggleTrackFormatting: () => trackFormatting = !trackFormatting,
                GetDisplayForReview: () => displayMode,
                ApplyDisplayForReview: mode => displayMode = mode,
                ShowMarkupInsertionsAndDeletions: () => showInsertions,
                ApplyShowMarkupInsertionsAndDeletions: show => showInsertions = show,
                ShowMarkupComments: () => showComments,
                ApplyShowMarkupComments: show => showComments = show,
                ShowMarkupFormatting: () => showFormatting,
                ApplyShowMarkupFormatting: show => showFormatting = show,
                AcceptAllRevisions: () => acceptedAll++,
                RejectAllRevisions: () => rejectedAll++));

        Execute(registry, "freew.track-changes");
        trackChanges.Should().BeTrue();
        markedSelections.Should().Be(1);
        commands.TrackChanges.GetState().IsChecked.Should().BeTrue();

        hasSelection = false;
        Execute(registry, "freew.track-changes");
        trackChanges.Should().BeFalse();
        markedSelections.Should().Be(1, "disabling Track Changes must not create a revision");

        Execute(registry, "freew.track-formatting");
        trackFormatting.Should().BeFalse();
        commands.TrackFormatting.GetState().IsChecked.Should().BeFalse();

        Execute(registry, "freew.display-for-review-no-markup");
        displayMode.Should().Be(ReviewDisplayMode.NoMarkup);
        commands.DisplayNoMarkup.GetState().IsChecked.Should().BeTrue();
        commands.DisplayAllMarkup.GetState().IsChecked.Should().BeFalse();

        Execute(registry, "freew.show-markup-comments");
        showComments.Should().BeFalse();
        commands.ShowComments.GetState().IsChecked.Should().BeFalse();

        Execute(registry, "freew.accept-all");
        Execute(registry, "freew.reject-all");
        acceptedAll.Should().Be(1);
        rejectedAll.Should().Be(1);
        prepareCount.Should().Be(7);

        registry.TryGet("freew.display-for-review", out var displayRoot).Should().BeTrue();
        registry.TryGet("freew.display-for-review-all-markup", out var allMarkup).Should().BeTrue();
        displayRoot.Should().BeSameAs(allMarkup);
        registry.TryGet("freew.show-markup", out var showMarkupRoot).Should().BeTrue();
        showMarkupRoot.Should().BeSameAs(EmptyRibbonCommand.Instance);
    }

    [Fact]
    public void TrackChanges_locked_by_protection_cannot_be_toggled_off_and_ribbon_reports_disabled()
    {
        var registry = new RibbonCommandRegistry();
        var trackChanges = true; // Restrict Editing > Tracked changes forced this on when protection was applied.
        var locked = true;
        var markedSelections = 0;

        var commands = ReviewTrackingRibbonWorkflow.Register(
            registry,
            new ReviewTrackingCommandBindings(
                PrepareExecution: () => { },
                IsTrackChangesEnabled: () => trackChanges,
                HasSelection: () => false,
                ToggleTrackChanges: () => trackChanges = !trackChanges,
                MarkSelectionAsInsertion: () => markedSelections++,
                IsTrackFormattingEnabled: () => true,
                ToggleTrackFormatting: () => { },
                GetDisplayForReview: () => ReviewDisplayMode.AllMarkup,
                ApplyDisplayForReview: _ => { },
                ShowMarkupInsertionsAndDeletions: () => true,
                ApplyShowMarkupInsertionsAndDeletions: _ => { },
                ShowMarkupComments: () => true,
                ApplyShowMarkupComments: _ => { },
                ShowMarkupFormatting: () => true,
                ApplyShowMarkupFormatting: _ => { },
                AcceptAllRevisions: () => { },
                RejectAllRevisions: () => { },
                IsTrackChangesLockedByProtection: () => locked));

        // Word greys the Track Changes toggle out while Restrict Editing > "Tracked changes" is active.
        commands.TrackChanges.GetState().IsEnabled.Should().BeFalse();

        // Even a direct Execute (e.g. a stale keyboard shortcut) must not defeat the protection.
        Execute(registry, "freew.track-changes");
        trackChanges.Should().BeTrue("toggling Track Changes off must be blocked while the document is restricted to tracked changes only");

        // Once the restriction is lifted, the toggle behaves normally again.
        locked = false;
        commands.TrackChanges.GetState().IsEnabled.Should().BeTrue();
        Execute(registry, "freew.track-changes");
        trackChanges.Should().BeFalse("with no protection in force, Track Changes toggles normally");
    }

    [Fact]
    public void Wpf_and_avalonia_adapters_delegate_tracking_workflow_to_presentation()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ReviewTrackingRibbonWorkflow.Register(");
            source.Should().Contain("new ReviewTrackingCommandBindings(");
            source.Should().NotContain("private sealed class TrackChangesToggleCommand");
            source.Should().NotContain("private sealed class TrackFormattingToggleCommand");
            source.Should().NotContain("private sealed class DisplayForReviewCommand");
            source.Should().NotContain("private sealed class ShowMarkupInsertionsDeletionsCommand");
            source.Should().NotContain("private sealed class ShowMarkupCommentsCommand");
            source.Should().NotContain("private sealed class ShowMarkupFormattingCommand");
        }
    }

    private static void Execute(IRibbonCommandRegistry registry, string id)
    {
        registry.TryGet(id, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static string ReadSource(params string[] relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(relativePath.Aggregate(root, Path.Combine));
    }
}
