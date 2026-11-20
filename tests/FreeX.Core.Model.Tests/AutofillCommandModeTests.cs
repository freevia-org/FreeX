using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Excel's Auto Fill Options: the same fill-handle drag can be redone as Copy Cells, Fill Series,
/// Fill Formatting Only or Fill Without Formatting.
/// </summary>
public sealed class AutofillCommandModeTests
{
    private static (Workbook Wb, Sheet Sheet, ICommandContext Ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static GridRange Range(Sheet sheet, uint r1, uint r2) =>
        new(new CellAddress(sheet.Id, r1, 1), new CellAddress(sheet.Id, r2, 1));

    [Fact]
    public void CopyCells_RepeatsTheSourceInsteadOfContinuingASeries()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        new AutofillCommand(sheet.Id, Range(sheet, 1, 2), Range(sheet, 3, 5)) { Mode = AutofillMode.CopyCells }.Apply(ctx)
            .Success.Should().BeTrue();

        sheet.GetValue(3, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void FillSeries_CountsUpFromASingleNumber_WhichPlainFillWouldCopy()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));

        new AutofillCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 2, 3)) { Mode = AutofillMode.FillSeries }.Apply(ctx)
            .Success.Should().BeTrue();

        sheet.GetValue(2, 1).Should().Be(new NumberValue(6));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void FillSeries_IgnoresCtrl_AndKeepsTheDetectedSeries()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        new AutofillCommand(sheet.Id, Range(sheet, 1, 2), Range(sheet, 3, 4), ctrlHeld: true) { Mode = AutofillMode.FillSeries }.Apply(ctx)
            .Success.Should().BeTrue();

        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void FormattingOnly_CopiesTheFormatAndLeavesDestinationValuesAlone_AndUndoRestoresThem()
    {
        var (wb, sheet, ctx) = Setup();
        var bold = wb.RegisterStyle(new CellStyle { Bold = true });
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(10));
        sheet.GetCell(a1)!.StyleId = bold;
        sheet.SetCell(a2, new TextValue("keep"));

        var command = new AutofillCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 2, 3)) { Mode = AutofillMode.FormattingOnly };
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(a2).Should().Be(new TextValue("keep"), "values are left alone");
        sheet.GetCell(a2)!.StyleId.Should().Be(bold, "an existing destination cell takes the source format");
        sheet.GetValue(a3).Should().Be(BlankValue.Instance, "an empty destination cell stays empty");
        sheet.GetStyleOnly(3, 1).Should().Be(bold, "but takes the source format");

        command.Revert(ctx);
        sheet.GetCell(a2)!.StyleId.Should().NotBe(bold);
        sheet.GetStyleOnly(3, 1).Should().BeNull();
    }

    [Fact]
    public void WithoutFormatting_FillsValuesButKeepsTheDestinationsOwnFormat()
    {
        var (wb, sheet, ctx) = Setup();
        var bold = wb.RegisterStyle(new CellStyle { Bold = true });
        var italic = wb.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId = bold;
        sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.StyleId = bold;
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("old"));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId = italic;

        new AutofillCommand(sheet.Id, Range(sheet, 1, 2), Range(sheet, 3, 4)) { Mode = AutofillMode.WithoutFormatting }.Apply(ctx)
            .Success.Should().BeTrue();

        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId.Should().Be(italic, "the destination keeps its own format");
        sheet.GetCell(new CellAddress(sheet.Id, 4, 1))!.StyleId.Should().NotBe(bold, "no source format reaches a blank destination");
    }

    [Fact]
    public void DefaultMode_StillCopiesTheSourceFormatting()
    {
        var (wb, sheet, ctx) = Setup();
        var bold = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId = bold;

        new AutofillCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 2, 2)).Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.StyleId.Should().Be(bold);
    }
}
