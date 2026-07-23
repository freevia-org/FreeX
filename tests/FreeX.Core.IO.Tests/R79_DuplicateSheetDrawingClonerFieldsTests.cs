using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R79-meta-6 / R79-selfreg-newfield-sweep-1/3/4: DuplicateSheetDrawingCloner's object initializers
/// for shapes and charts omitted several fields added in earlier rounds -- a preset shape's
/// r78 avLst adjust-handle customization (DrawingShapeModel.AdjustValues), a secondary chart axis's
/// own r71 display-unit fields and the r71 axis-title rotations, and a chartsheet's r72
/// UseFirstPageNumber gate flag -- so Duplicate Sheet silently reverted them to their defaults on
/// the copy even though the source was untouched. Verifies each field now survives Duplicate Sheet,
/// plus sibling no-regression cases confirming a plain shape/chart still duplicates cleanly.
/// </summary>
public sealed class R79_DuplicateSheetDrawingClonerFieldsTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Sheet CreateChartSheet(Workbook workbook, out GridRange range)
    {
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        return sheet;
    }

    // R79-meta-6 / R79-selfreg-newfield-sweep-1 (the bug case): a customized adjust-handle (e.g. a
    // rounded rectangle's dragged corner-radius handle) must survive Duplicate Sheet, not silently
    // revert to the preset geometry's default handle position.
    [Fact]
    public void DuplicateSheet_ShapeWithCustomAdjustValues_PreservesOnCopy()
    {
        var workbook = new Workbook("ShapeCloneAdjustValues");
        var sheet = workbook.AddSheet("Sheet1");
        var adjustValues = new List<DrawingShapeAdjustValue> { new("adj", "val 12500") };
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "RoundedRect",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.RoundedRectangle,
            Width = 100,
            Height = 60,
            AdjustValues = adjustValues
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedShape = workbook.Sheets[1].DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.AdjustValues.Should().NotBeNull(
            "a customized adjust handle must not be dropped by Duplicate Sheet");
        copiedShape.AdjustValues!.Should().ContainSingle()
            .Which.Should().Be(new DrawingShapeAdjustValue("adj", "val 12500"));
    }

    // Sibling no-regression case: a plain shape with no adjust-handle customization must still
    // duplicate cleanly, leaving AdjustValues at its default (null).
    [Fact]
    public void DuplicateSheet_ShapeWithoutAdjustValues_LeavesFieldAtDefault()
    {
        var workbook = new Workbook("ShapeCloneAdjustValuesDefault");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "PlainRect",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Width = 100,
            Height = 60
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedShape = workbook.Sheets[1].DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.AdjustValues.Should().BeNull();
    }

    // R79-selfreg-newfield-sweep-3 (the bug case): the secondary axis's own display unit and the
    // axis-title rotations must survive Duplicate Sheet, not silently revert to "none" / the
    // writer's hardcoded default orientation.
    [Fact]
    public void DuplicateSheet_ChartWithSecondaryAxisDisplayUnitAndTitleRotations_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneSecondaryAxisDisplayUnitAndRotation");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = range,
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            SecondaryAxisDisplayUnit = ChartAxisDisplayUnit.Millions,
            SecondaryAxisCustomDisplayUnit = 2_500_000,
            ShowSecondaryAxisDisplayUnitLabel = true,
            XAxisTitleRotation = 45,
            YAxisTitleRotation = -90
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.SecondaryAxisDisplayUnit.Should().Be(ChartAxisDisplayUnit.Millions,
            "the secondary axis's own display unit must not be dropped by Duplicate Sheet");
        copiedChart.SecondaryAxisCustomDisplayUnit.Should().Be(2_500_000);
        copiedChart.ShowSecondaryAxisDisplayUnitLabel.Should().BeTrue();
        copiedChart.XAxisTitleRotation.Should().Be(45,
            "a custom X axis title rotation must not be dropped by Duplicate Sheet");
        copiedChart.YAxisTitleRotation.Should().Be(-90,
            "a custom Y axis title rotation must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a chart with no secondary-axis display unit or title rotation set
    // must still duplicate cleanly, leaving the new fields at their defaults.
    [Fact]
    public void DuplicateSheet_ChartWithoutSecondaryAxisDisplayUnitOrTitleRotation_LeavesFieldsAtDefault()
    {
        var workbook = new Workbook("ChartCloneSecondaryAxisDisplayUnitDefault");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = range
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.SecondaryAxisDisplayUnit.Should().BeNull();
        copiedChart.SecondaryAxisCustomDisplayUnit.Should().BeNull();
        copiedChart.ShowSecondaryAxisDisplayUnitLabel.Should().BeFalse();
        copiedChart.XAxisTitleRotation.Should().BeNull();
        copiedChart.YAxisTitleRotation.Should().BeNull();
    }

    // R79-selfreg-newfield-sweep-4 (the bug case): a chartsheet's UseFirstPageNumber gate flag must
    // survive Duplicate Sheet alongside FirstPageNumber itself, not silently revert to null (which
    // disables the custom first-page-number emission even though the value was copied).
    [Fact]
    public void DuplicateSheet_ChartWithUseFirstPageNumber_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneUseFirstPageNumber");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            PrintSettings = new ChartPrintSettingsModel
            {
                PageSetup = new ChartPageSetupModel
                {
                    FirstPageNumber = 5,
                    UseFirstPageNumber = true
                }
            }
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.PrintSettings.Should().NotBeNull();
        copiedChart.PrintSettings!.PageSetup.Should().NotBeNull();
        copiedChart.PrintSettings!.PageSetup!.FirstPageNumber.Should().Be(5);
        copiedChart.PrintSettings!.PageSetup!.UseFirstPageNumber.Should().BeTrue(
            "the 'Use' first-page-number gate flag must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a chartsheet with FirstPageNumber set but UseFirstPageNumber left
    // unset/false must still duplicate cleanly, leaving the gate flag as-is.
    [Fact]
    public void DuplicateSheet_ChartWithFirstPageNumberButNoUseFlag_LeavesFieldAtDefault()
    {
        var workbook = new Workbook("ChartCloneUseFirstPageNumberDefault");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            PrintSettings = new ChartPrintSettingsModel
            {
                PageSetup = new ChartPageSetupModel
                {
                    FirstPageNumber = 5
                }
            }
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.PrintSettings!.PageSetup!.FirstPageNumber.Should().Be(5);
        copiedChart.PrintSettings!.PageSetup!.UseFirstPageNumber.Should().BeNull();
    }
}
