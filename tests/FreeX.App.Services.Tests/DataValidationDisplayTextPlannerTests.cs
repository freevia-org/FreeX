using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class DataValidationDisplayTextPlannerTests
{
    [Fact]
    public void RuleTypeMetadata_CoversDialogTypesAndSharedDisplayNames()
    {
        var metadata = DataValidationDisplayTextPlanner.GetRuleTypeMetadata();

        metadata.Select(item => item.Type).Should().Equal(
            DvType.Any,
            DvType.WholeNumber,
            DvType.Decimal,
            DvType.List,
            DvType.Date,
            DvType.Time,
            DvType.TextLength,
            DvType.Custom);
        DataValidationDisplayTextPlanner.GetRuleTypeDisplayName(DvType.WholeNumber).Should().Be("Whole number");
        DataValidationDisplayTextPlanner.GetRuleTypeDisplayName(DvType.TextLength).Should().Be("Text length");
        DataValidationDisplayTextPlanner.RequiresSecondFormula(DvType.WholeNumber, DvOperator.Between).Should().BeTrue();
        DataValidationDisplayTextPlanner.RequiresSecondFormula(DvType.List, DvOperator.Between).Should().BeFalse();
        DataValidationDisplayTextPlanner.RequiresSecondFormula(DvType.Custom, DvOperator.NotBetween).Should().BeFalse();
    }

    [Fact]
    public void AlertStyleMetadata_CoversDialogChoicesAndPreviewLabels()
    {
        var metadata = DataValidationDisplayTextPlanner.GetAlertStyleMetadata();

        metadata.Should().Equal(
            new DataValidationAlertStyleMetadata(DvAlertStyle.Stop, "Stop"),
            new DataValidationAlertStyleMetadata(DvAlertStyle.Warning, "Warning"),
            new DataValidationAlertStyleMetadata(DvAlertStyle.Information, "Information"));
        DataValidationDisplayTextPlanner.FormatAlertStyle(DvAlertStyle.Warning).Should().Be("Warning");
        DataValidationDisplayTextPlanner.FormatAlertStyle((DvAlertStyle)999).Should().Be("999");
    }

    [Fact]
    public void DialogTextFormatting_NormalizesSharedPreviewText()
    {
        var sheetId = SheetId.New();
        var start = new CellAddress(sheetId, 2, 2);
        var end = new CellAddress(sheetId, 3, 4);

        DataValidationDisplayTextPlanner.FormatTitleAndMessage("  Title\r\nText  ", "  Message\nText  ")
            .Should()
            .Be("Title  Text - Message Text");
        DataValidationDisplayTextPlanner.FormatPreviewValue("   ").Should().Be("(blank)");
        DataValidationDisplayTextPlanner.FormatPreviewValue("  value  ").Should().Be("value");
        DataValidationDisplayTextPlanner.FormatCellReference(start).Should().Be("B2");
        DataValidationDisplayTextPlanner.FormatRangeReference(new GridRange(start, end)).Should().Be("B2:D3");
        DataValidationDisplayTextPlanner.FormatRangeReference(new GridRange(start, start)).Should().Be("B2");
    }
}
