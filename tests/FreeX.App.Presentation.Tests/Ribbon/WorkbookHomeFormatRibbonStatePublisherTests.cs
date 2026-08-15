using FluentAssertions;
using Free.Shared.Ribbon;
using FreeX.App.Presentation.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Ribbon;

public sealed class WorkbookHomeFormatRibbonStatePublisherTests
{
    [Fact]
    public void Publish_MapsCompleteHomeFormatStateToCanonicalCommands()
    {
        var store = new RibbonStateStore();
        var state = new ToolbarVisualState(
            Bold: true,
            Italic: false,
            Underline: true,
            Strikethrough: false,
            VerticalAlignment: VerticalAlignment.Center,
            HorizontalAlignment: HorizontalAlignment.Right,
            WrapText: true,
            FontName: "Aptos",
            FontSizeText: "14");

        WorkbookHomeFormatRibbonStatePublisher.Publish(store, state);

        Checked(store, "Bold").Should().BeTrue();
        Checked(store, "Italic").Should().BeFalse();
        Checked(store, "Underline").Should().BeTrue();
        Checked(store, "Strikethrough").Should().BeFalse();
        Checked(store, "Top Align").Should().BeFalse();
        Checked(store, "Middle Align").Should().BeTrue();
        Checked(store, "Bottom Align").Should().BeFalse();
        Checked(store, "Align Left").Should().BeFalse();
        Checked(store, "Center").Should().BeFalse();
        Checked(store, "Align Right").Should().BeTrue();
        Checked(store, "Wrap Text").Should().BeTrue();
        store.GetState("Font").Value.Should().Be("Aptos");
        store.GetState("Font Size").Value.Should().Be("14");
    }

    [Fact]
    public void Publish_DeduplicatesUnchangedProjectionThroughStateStore()
    {
        var store = new RibbonStateStore();
        var state = ToolbarVisualState.From(new CellStyle
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        var changes = 0;
        store.StateChanged += (_, _) => changes++;

        WorkbookHomeFormatRibbonStatePublisher.Publish(store, state);
        var firstPublishChanges = changes;
        WorkbookHomeFormatRibbonStatePublisher.Publish(store, state);

        firstPublishChanges.Should().Be(13);
        changes.Should().Be(firstPublishChanges);
    }

    [Fact]
    public void BothRenderers_ConsumeSharedHomeFormatProjection()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "MainWindow.WorkbookUiState.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));

        wpf.Should().Contain("WorkbookHomeFormatRibbonStatePublisher.Publish(_ribbonState, state);");
        avalonia.Should().Contain("WorkbookHomeFormatRibbonStatePublisher.Publish(");
        avalonia.Should().Contain("ToolbarVisualState.From(_session.SelectedRangeStartStyle)");
        avalonia.IndexOf("WorkbookHomeFormatRibbonStatePublisher.Publish(", StringComparison.Ordinal)
            .Should().BeLessThan(
                avalonia.IndexOf("_refreshRibbonToggleStates?.Invoke();", StringComparison.Ordinal),
                "the neutral state store must be current before Avalonia repaints declarative toggles");
        wpf.Should().NotContain("_ribbonState.SetChecked(\"Top Align\"");
    }

    private static bool Checked(IRibbonStateStore store, string id) =>
        store.GetState(id).IsChecked;
}
