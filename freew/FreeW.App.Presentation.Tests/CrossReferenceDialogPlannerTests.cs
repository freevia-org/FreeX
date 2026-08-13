using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class CrossReferenceDialogPlannerTests
{
    [Fact]
    public void VisualMetrics_PreservePairedWpfAuthorityGeometry()
    {
        CrossReferenceDialogPlanner.VisualMetrics.Should().Be(
            new CrossReferenceDialogVisualMetrics(
                TypeListMinWidth: 150,
                InsertAsListMinWidth: 180,
                TargetListMinWidth: 300,
                ChoiceListHeight: 170,
                TargetListHeight: 200,
                HyperlinkTopMargin: 10,
                TopRowBottomMargin: 10,
                ColumnSpacing: 12,
                LabelTopMargin: 8,
                LabelBottomMargin: 4,
                OuterMargin: 16,
                ActionButtonWidth: 80,
                ActionRowTopMargin: 14,
                AvaloniaListItemHeight: 21,
                AvaloniaInactiveSelectionBackgroundHex: "#F0F0F0",
                AvaloniaInactiveSelectionBorderHex: "#ABADB3"));
    }

    [Fact]
    public void Renderers_ProjectSharedVisualMetricsWithoutLocalGeometryPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Host", "CrossReferenceDialog.cs"));
        var avaloniaFile = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Avalonia", "ReferencesDialogs.cs"));
        var avalonia = avaloniaFile[..avaloniaFile.IndexOf(
            "internal sealed class SourceConflictResolutionDialog", StringComparison.Ordinal)];

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("CrossReferenceDialogPlanner.VisualMetrics");
            source.Should().Contain("Layout.TypeListMinWidth");
            source.Should().Contain("Layout.InsertAsListMinWidth");
            source.Should().Contain("Layout.TargetListMinWidth");
            source.Should().Contain("Layout.ChoiceListHeight");
            source.Should().Contain("Layout.TargetListHeight");
            source.Should().Contain("Layout.OuterMargin");
            source.Should().Contain("Layout.ActionButtonWidth");
            source.Should().Contain("Layout.ActionRowTopMargin");
        }

        wpf.Should().NotContain("MinWidth = 150");
        wpf.Should().NotContain("new Thickness(16)");
        avalonia.Should().NotContain("MinWidth = 150");
        avalonia.Should().NotContain("new Thickness(16)");
        avalonia.Should().Contain("Layout.AvaloniaListItemHeight");
        avalonia.Should().Contain("Layout.AvaloniaInactiveSelectionBackgroundHex");
        avalonia.Should().Contain("Layout.AvaloniaInactiveSelectionBorderHex");
    }

    [Fact]
    public void BuildTypeChoices_UsesWordOrderAndFriendlyLabels()
    {
        var choices = CrossReferenceDialogPlanner.BuildTypeChoices();

        choices.Select(choice => choice.Type).Should().Equal(
            CrossRefType.Heading,
            CrossRefType.Bookmark,
            CrossRefType.Figure,
            CrossRefType.Table,
            CrossRefType.Equation,
            CrossRefType.Footnote,
            CrossRefType.Endnote,
            CrossRefType.NumberedItem);
        choices.Last().Label.Should().Be("Numbered item");
    }

    [Fact]
    public void BuildInsertAsChoices_UsesModelOptionsAndPreservesPreviousSelection()
    {
        var choices = CrossReferenceDialogPlanner.BuildInsertAsChoices(CrossRefType.Heading);

        choices.Select(choice => choice.InsertAs).Should().Equal(
            CrossRefInsertAs.Text,
            CrossRefInsertAs.PageNumber,
            CrossRefInsertAs.HeadingNumber,
            CrossRefInsertAs.AboveBelow);
        choices.Select(choice => choice.Label).Should().Equal(
            "Text",
            "Page number",
            "Heading number",
            "Above/below");

        CrossReferenceDialogPlanner.PreserveInsertAsSelection(choices, CrossRefInsertAs.PageNumber)
            .Should().Be(1);
        CrossReferenceDialogPlanner.PreserveInsertAsSelection(choices, CrossRefInsertAs.ParagraphNumber)
            .Should().Be(0);
    }

    [Fact]
    public void BuildInsertAsChoices_UsesWordsCaptionLabelsAndOrder()
    {
        var choices = CrossReferenceDialogPlanner.BuildInsertAsChoices(CrossRefType.Figure);

        choices.Select(choice => choice.InsertAs).Should().Equal(
            CrossRefInsertAs.Text,
            CrossRefInsertAs.CaptionLabelAndNumber,
            CrossRefInsertAs.CaptionText,
            CrossRefInsertAs.PageNumber,
            CrossRefInsertAs.AboveBelow);
        choices.Select(choice => choice.Label).Should().Equal(
            "Entire caption",
            "Only label and number",
            "Only caption text",
            "Page number",
            "Above/below");
    }

    [Fact]
    public void TryCreateChoice_SelectsRequestedTargetIndexInsteadOfDefaultingToFirstHeading()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("First") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("Second") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("Body"));

        var targets = CrossReferenceDialogPlanner.BuildTargetChoices(document, CrossRefType.Heading);

        targets.Select(target => target.Label).Should().Equal("First", "Second");

        CrossReferenceDialogPlanner.TryCreateChoice(
                document,
                CrossRefType.Heading,
                CrossRefInsertAs.PageNumber,
                selectedTargetIndex: 1,
                hyperlink: false,
                out var choice)
            .Should().BeTrue();

        choice.Should().NotBeNull();
        choice!.Target.Display.Should().Be("Second");
        choice.InsertAs.Should().Be(CrossRefInsertAs.PageNumber);
        choice.Hyperlink.Should().BeFalse();
    }

    [Fact]
    public void TryCreateChoice_RejectsMissingTargetSelection()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));

        CrossReferenceDialogPlanner.TryCreateChoice(
                document,
                CrossRefType.Heading,
                CrossRefInsertAs.Text,
                selectedTargetIndex: 0,
                hyperlink: true,
                out var choice)
            .Should().BeFalse();

        choice.Should().BeNull();
    }
}
