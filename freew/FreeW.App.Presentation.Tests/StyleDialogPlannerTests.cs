using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class StyleDialogPlannerTests
{
    [Fact]
    public void LayoutMetrics_KeepCompactStyleEditorsOnTheSharedContract()
    {
        StyleDialogMetrics.DialogMargin.Should().Be(16);
        StyleDialogMetrics.FieldBottomMargin.Should().Be(10);
        StyleDialogMetrics.NameTextBoxHeight.Should().Be(20);
        StyleDialogMetrics.ActionRowTopMargin.Should().Be(12);
    }

    [Fact]
    public void TryBuildDefinition_TrimsName_AndMapsFormattingChoices()
    {
        var input = new StyleDialogInput(
            "  Callout  ",
            "Normal",
            "Heading1",
            Bold: true,
            Italic: true,
            Underline: false,
            FontSizeIndex: 7,
            ColorIndex: 4,
            AlignmentIndex: 1);

        StyleDialogPlanner.TryBuildDefinition(
                input,
                RunFormatting.Default,
                ParagraphFormatting.Default,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result!.Name.Should().Be("Callout");
        result.BasedOnId.Should().Be("Normal");
        result.NextStyleId.Should().Be("Heading1");
        result.Run.Bold.Should().BeTrue();
        result.Run.Italic.Should().BeTrue();
        result.Run.FontSizePt.Should().Be(16);
        result.Run.ColorHex.Should().Be("#2F5496");
        result.Paragraph.Alignment.Should().Be(TextAlignment.Center);
    }

    [Fact]
    public void TryBuildDefinition_RejectsEmptyName()
    {
        StyleDialogPlanner.TryBuildDefinition(
                new StyleDialogInput("   ", null, null, false, false, false, 0, 0, 0),
                RunFormatting.Default,
                ParagraphFormatting.Default,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(StyleDialogValidationError.EmptyName);
    }

    [Fact]
    public void BuildRows_ByType_OrdersBuiltInsBeforeCustomStyles()
    {
        var doc = TextDocument.CreateEmpty();
        var custom = StyleManager.CreateStyle(
            doc,
            "Callout",
            null,
            RunFormatting.Default,
            ParagraphFormatting.Default);

        var rows = StyleDialogPlanner.BuildRows(doc, StyleDialogSortOrder.ByType);
        var firstCustomIndex = rows.ToList().FindIndex(row => !row.IsBuiltIn);

        rows.TakeWhile(row => row.IsBuiltIn).Should().NotBeEmpty();
        rows.First(row => row.Id == custom.Id).IsBuiltIn.Should().BeFalse();
        rows.Skip(firstCustomIndex).Should().OnlyContain(row => !row.IsBuiltIn);
    }

    [Fact]
    public void BuildRows_ByUse_OrdersMostUsedStylesFirst()
    {
        var doc = TextDocument.CreateEmpty();
        var custom = StyleManager.CreateStyle(
            doc,
            "Frequently Used",
            null,
            RunFormatting.Default,
            ParagraphFormatting.Default);
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("one") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("two") { StyleId = custom.Id });
        doc.Blocks.Add(new Paragraph("three") { StyleId = custom.Id });

        var rows = StyleDialogPlanner.BuildRows(doc, StyleDialogSortOrder.ByUse);

        rows.First().Id.Should().Be(custom.Id);
    }
}
