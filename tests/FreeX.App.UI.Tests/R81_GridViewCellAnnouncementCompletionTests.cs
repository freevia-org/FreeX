using System.Windows.Automation.Peers;
using FluentAssertions;
using FreeX.App.Presentation.Accessibility;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R81 completion of the R80-partial screen-reader per-cell metadata announcement
/// (R80-app-accessibility-a11y-5-3). Round 80 wired only the "has note"/"has comment" cue
/// (<see cref="GridViewAutomationPeerTests"/>). This file covers the pure
/// <see cref="CellAnnouncementPlanner"/> builder for every metadata kind the item
/// calls for (comment, formula, merged, hyperlink, data validation, locked), plus end-to-end
/// GridView/AutomationPeer tests proving the three newly-wired cues (formula, merged, hyperlink)
/// are actually reachable from real GridView data, not just the builder in isolation.
/// </summary>
public sealed class R81_GridViewCellAnnouncementCompletionTests
{
    // ---- Pure builder tests (no GridView/AutomationPeer construction needed) ----

    [Fact]
    public void BuildCellAnnouncementName_PlainCell_IsJustAddressAndValue()
    {
        var name = CellAnnouncementPlanner.BuildName("A1", "42", default);

        name.Should().Be("A1: 42");
    }

    [Fact]
    public void BuildCellAnnouncementName_EmptyCell_IsJustAddress()
    {
        var name = CellAnnouncementPlanner.BuildName("A1", null, default);

        name.Should().Be("A1");
    }

    [Fact]
    public void BuildCellAnnouncementName_WithComment_AnnouncesHasComment()
    {
        var metadata = new CellAnnouncementMetadata(HasComment: true, CommentTitle: "Comment");

        var name = CellAnnouncementPlanner.BuildName("A1", "42", metadata);

        name.Should().Be("A1: 42, has comment");
    }

    [Fact]
    public void BuildCellAnnouncementName_WithNote_AnnouncesHasNote()
    {
        var metadata = new CellAnnouncementMetadata(HasComment: true, CommentTitle: "Note");

        var name = CellAnnouncementPlanner.BuildName("A1", "42", metadata);

        name.Should().Be("A1: 42, has note");
    }

    [Fact]
    public void BuildCellAnnouncementName_Formula_AnnouncesIsAFormula()
    {
        var metadata = new CellAnnouncementMetadata(IsFormula: true);

        var name = CellAnnouncementPlanner.BuildName("B2", "3", metadata);

        name.Should().Be("B2: 3, is a formula");
    }

    [Fact]
    public void BuildCellAnnouncementName_Merged_AnnouncesIsMerged()
    {
        var metadata = new CellAnnouncementMetadata(IsMerged: true);

        var name = CellAnnouncementPlanner.BuildName("C3", "Header", metadata);

        name.Should().Be("C3: Header, is merged");
    }

    [Fact]
    public void BuildCellAnnouncementName_DataValidation_AnnouncesHasDataValidation()
    {
        var metadata = new CellAnnouncementMetadata(HasDataValidation: true);

        var name = CellAnnouncementPlanner.BuildName("D4", "Yes", metadata);

        name.Should().Be("D4: Yes, has data validation");
    }

    [Fact]
    public void BuildCellAnnouncementName_Hyperlink_AnnouncesHasAHyperlink()
    {
        var metadata = new CellAnnouncementMetadata(HasHyperlink: true);

        var name = CellAnnouncementPlanner.BuildName("E5", "example.com", metadata);

        name.Should().Be("E5: example.com, has a hyperlink");
    }

    [Fact]
    public void BuildCellAnnouncementName_Locked_AnnouncesIsLocked()
    {
        var metadata = new CellAnnouncementMetadata(IsLocked: true);

        var name = CellAnnouncementPlanner.BuildName("F6", "1", metadata);

        name.Should().Be("F6: 1, is locked");
    }

    [Fact]
    public void BuildCellAnnouncementName_AllMetadataKinds_IncludesEveryCue()
    {
        var metadata = new CellAnnouncementMetadata(
            HasComment: true,
            CommentTitle: "Comment",
            IsFormula: true,
            IsMerged: true,
            HasDataValidation: true,
            HasHyperlink: true,
            IsLocked: true);

        var name = CellAnnouncementPlanner.BuildName("G7", "100", metadata);

        name.Should().Be(
            "G7: 100, has comment, is a formula, is merged, has data validation, has a hyperlink, is locked");
    }

    // ---- End-to-end GridView/AutomationPeer tests for the three newly-wired live signals ----

    [Fact]
    public void GridViewCellAutomationPeer_AnnouncesFormulaPresenceInName()
    {
        // R81: DisplayCell.Formula was already flowing into GridView's viewport (r80 never
        // consumed it for the announcement) -- this fails pre-fix (Name would be just "A1: 3")
        // and passes post-fix.
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [
                        new DisplayCell(1, 1, new NumberValue(3), "3", "=1+2", StyleId.Default, null),
                    ],
                    [new RowMetric(1, 20, 0)],
                    [new ColMetric(1, 64, 0)])
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var cellPeer = peer.GetChildren()[0];

            cellPeer.GetName().Should().Be("A1: 3, is a formula");
        });
    }

    [Fact]
    public void GridViewCellAutomationPeer_AnnouncesMergedCellInName()
    {
        // R81: MergedRegions is already a real GridView dependency property (consumed by
        // rendering) -- this fails pre-fix and passes post-fix.
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var grid = new GridView
            {
                ActiveSheetId = sheetId,
                MergedRegions =
                [
                    new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 2))
                ],
                Viewport = new ViewportModel(
                    [
                        new DisplayCell(1, 1, new TextValue("Header"), "Header", null, StyleId.Default, null),
                    ],
                    [new RowMetric(1, 20, 0)],
                    [new ColMetric(1, 64, 0)])
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var cellPeer = peer.GetChildren()[0];

            cellPeer.GetName().Should().Be("A1: Header, is merged");
        });
    }

    [Fact]
    public void GridViewCellAutomationPeer_AnnouncesHyperlinkInName()
    {
        // R81: HyperlinkCells is already a real GridView dependency property (consumed by
        // GridView.Input.cs's Ctrl+hover hand cursor) -- this fails pre-fix and passes post-fix.
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var grid = new GridView
            {
                ActiveSheetId = sheetId,
                HyperlinkCells = new HashSet<CellAddress> { new(sheetId, 1, 1) },
                Viewport = new ViewportModel(
                    [
                        new DisplayCell(1, 1, new TextValue("example.com"), "example.com", null, StyleId.Default, null),
                    ],
                    [new RowMetric(1, 20, 0)],
                    [new ColMetric(1, 64, 0)])
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var cellPeer = peer.GetChildren()[0];

            cellPeer.GetName().Should().Be("A1: example.com, has a hyperlink");
        });
    }

    [Fact]
    public void GridViewCellAutomationPeer_CombinesMultipleLiveSignalsInName()
    {
        // A cell that is simultaneously a formula, merged, and hyperlinked reports every cue,
        // in the same order the "all metadata kinds" pure-builder test expects.
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var grid = new GridView
            {
                ActiveSheetId = sheetId,
                MergedRegions =
                [
                    new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 2))
                ],
                HyperlinkCells = new HashSet<CellAddress> { new(sheetId, 1, 1) },
                Viewport = new ViewportModel(
                    [
                        new DisplayCell(1, 1, new NumberValue(3), "3", "=1+2", StyleId.Default, null),
                    ],
                    [new RowMetric(1, 20, 0)],
                    [new ColMetric(1, 64, 0)])
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var cellPeer = peer.GetChildren()[0];

            cellPeer.GetName().Should().Be("A1: 3, is a formula, is merged, has a hyperlink");
        });
    }

    [Fact]
    public void GridViewCellAutomationPeer_NoRegression_PlainCellNameIsUnchanged()
    {
        // No-regression sibling: a plain cell with none of the new metadata keeps exactly the
        // pre-existing "address: value" Name -- no stray trailing comma or empty cue text.
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [
                        new DisplayCell(1, 1, new NumberValue(42), "42", null, StyleId.Default, null),
                    ],
                    [new RowMetric(1, 20, 0)],
                    [new ColMetric(1, 64, 0)])
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var cellPeer = peer.GetChildren()[0];

            cellPeer.GetName().Should().Be("A1: 42");
        });
    }
}
