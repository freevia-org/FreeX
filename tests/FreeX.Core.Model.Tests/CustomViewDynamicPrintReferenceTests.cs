using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class CustomViewDynamicPrintReferenceTests
{
    [Fact]
    public void ShowResolvesAreaEachTimeWithoutChangingFormula_AndUndoRestoresLiveArea()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        var before = new GridRange(new CellAddress(sheet.Id, 8, 3), new CellAddress(sheet.Id, 9, 4));
        sheet.SetPrintAreas([before]);
        const string expression = "OFFSET(Data!$A$1,Data!$B$1,0,2,1)";
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { OpaquePrintAreaExpression = expression };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var command = new ApplyCustomViewCommand("View");
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintAreas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 4, 1)));
        workbook.CustomViews[0].Sheets[0].OpaquePrintAreaExpression.Should().Be(expression);
        command.Revert(ctx);
        sheet.PrintAreas.Should().Equal(before);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(4));
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintAreas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 6, 1)));
        command.Revert(ctx);
        sheet.PrintAreas.Should().Equal(before);
    }

    [Fact]
    public void ShowTitlesUsesLastWholeAxisAndIgnoresRectangles_ThenUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var ctx = new TestCommandContext(workbook);
        sheet.PrintTitleRows = new WorksheetRepeatRange(8, 9);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(7, 8);
        const string expression = "Data!$2:$3,Data!$B$2:$C$3,Data!$4:$5,Data!$B:$C,Data!$D:$E";
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { PrintSettings = new WorksheetCustomViewPrintSettings(), OpaquePrintTitlesExpression = expression };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var command = new ApplyCustomViewCommand("View");
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(4, 5));
        sheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(4, 5));
        workbook.CustomViews[0].Sheets[0].OpaquePrintTitlesExpression.Should().Be(expression);
        command.Revert(ctx);
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(8, 9));
        sheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(7, 8));
    }

    [Theory]
    [InlineData("Data!$2:$3,OFFSET(Data!$A$1,-1,0)")]
    [InlineData("Data!$2:$3,Other!$A:$B")]
    [InlineData("NA()")]
    [InlineData("42")]
    public void InvalidDynamicTitlesClearBothFacetsAndUndoRestores(string expression)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.AddSheet("Other");
        var ctx = new TestCommandContext(workbook);
        sheet.PrintTitleRows = new WorksheetRepeatRange(8, 9);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(7, 8);
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { PrintSettings = new WorksheetCustomViewPrintSettings(), OpaquePrintTitlesExpression = expression };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var command = new ApplyCustomViewCommand("View");
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintTitleRows.Should().BeNull();
        sheet.PrintTitleColumns.Should().BeNull();
        command.Revert(ctx);
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(8, 9));
        sheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(7, 8));
    }

    [Fact]
    public void FullSheetTitleGeometryUsesAllValidRowsAndColumns()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { PrintSettings = new WorksheetCustomViewPrintSettings(), OpaquePrintTitlesExpression = "Data!$A:$XFD" };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        new ApplyCustomViewCommand("View").Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, CellAddress.MaxRow));
        sheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, CellAddress.MaxCol));
    }

    [Fact]
    public void InvalidAreaUnionClearsEntireArea_AndDisabledPrintSettingsLeaveItAlone()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.AddSheet("Other");
        var ctx = new TestCommandContext(workbook);
        var before = new GridRange(new CellAddress(sheet.Id, 8, 3), new CellAddress(sheet.Id, 9, 4));
        sheet.SetPrintAreas([before]);
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { OpaquePrintAreaExpression = "Data!A1:B2,Other!A1:B2" };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var command = new ApplyCustomViewCommand("View");
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintAreas.Should().BeEmpty();
        command.Revert(ctx);
        sheet.PrintAreas.Should().Equal(before);
        workbook.CustomViews[0] = workbook.CustomViews[0] with { IncludePrintSettings = false };
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintAreas.Should().Equal(before);
    }

    [Fact]
    public void UnsupportedReferenceLeavesCurrentPrintFacetsUnchanged()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var before = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
        sheet.SetPrintAreas([before]);
        sheet.PrintTitleRows = new WorksheetRepeatRange(7, 8);
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { PrintSettings = new WorksheetCustomViewPrintSettings(), OpaquePrintAreaExpression = "FILTER(Data!A1:A3,Data!B1:B3)", OpaquePrintTitlesExpression = "FILTER(Data!A1:A3,Data!B1:B3)" };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        new ApplyCustomViewCommand("View").Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PrintAreas.Should().Equal(before);
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(7, 8));
    }

    [Theory]
    [InlineData("HeightUnknown")]
    [InlineData("HeightWrapped")]
    [InlineData("HeightUnparsed")]
    [InlineData("IFERROR(UNKNOWN_EXCEL_FUNCTION(),1)")]
    public void UnsupportedScalarInDynamicArea_LeavesLiveAreaUntouched(string height)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedFormulas["HeightUnknown"] = "UNKNOWN_EXCEL_FUNCTION()";
        workbook.NamedFormulas["HeightWrapped"] = "IFERROR(HeightUnknown,1)";
        workbook.NamedFormulas["HeightUnparsed"] = "A1:INDEX(A:A,MATCH(2,B:B,0))";
        var before = new GridRange(new CellAddress(sheet.Id, 8, 3), new CellAddress(sheet.Id, 9, 4));
        sheet.SetPrintAreas([before]);
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { OpaquePrintAreaExpression = $"OFFSET(A1,0,0,{height},1)" };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var command = new ApplyCustomViewCommand("View");
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintAreas.Should().Equal(before);
        command.Revert(ctx);
        sheet.PrintAreas.Should().Equal(before);
    }

    [Theory]
    [InlineData("HeightError")]
    [InlineData("HeightCycleA")]
    public void GenuineNamedScalarErrorInDynamicArea_ClearsAndUndoRestores(string height)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedFormulas["HeightError"] = "NA()";
        workbook.NamedFormulas["HeightCycleA"] = "HeightCycleB";
        workbook.NamedFormulas["HeightCycleB"] = "HeightCycleA";
        var before = new GridRange(new CellAddress(sheet.Id, 8, 3), new CellAddress(sheet.Id, 9, 4));
        sheet.SetPrintAreas([before]);
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { OpaquePrintAreaExpression = $"OFFSET(A1,0,0,{height},1)" };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var command = new ApplyCustomViewCommand("View");
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintAreas.Should().BeEmpty();
        command.Revert(ctx);
        sheet.PrintAreas.Should().Equal(before);
    }

    [Theory]
    [InlineData("IFERROR('[1]Data'!B1,1)")]
    [InlineData("ExternalHeight")]
    public void ExternalScalarInDynamicArea_IsNotTreatedAsAnExcelErrorOrFallback(string height)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedFormulas["ExternalHeight"] = "IFERROR('[1]Data'!B1,1)";
        var before = new GridRange(new CellAddress(sheet.Id, 8, 3), new CellAddress(sheet.Id, 9, 4));
        sheet.SetPrintAreas([before]);
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { OpaquePrintAreaExpression = $"OFFSET(A1,0,0,{height},1)" };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var command = new ApplyCustomViewCommand("View");
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintAreas.Should().Equal(before);
        command.Revert(ctx);
        sheet.PrintAreas.Should().Equal(before);
    }

    [Theory]
    [InlineData("ROWS(CHOOSE(1,42,A1))")]
    [InlineData("ROWS(INDEX({1,2},1,1))")]
    [InlineData("COUNTBLANK(AllData)")]
    [InlineData("COUNTIF(AllData,\"\")")]
    [InlineData("IFERROR(COUNTIF(AllData,\"\"),1)")]
    public void UnverifiedRangeSensitiveHeight_LeavesCurrentAreaUntouched(string height)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedRanges["AllData"] = new GridRange(new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));
        var before = new GridRange(new CellAddress(sheet.Id, 8, 3), new CellAddress(sheet.Id, 9, 4));
        sheet.SetPrintAreas([before]);
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { OpaquePrintAreaExpression = $"OFFSET(B1,0,0,{height},1)" };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var command = new ApplyCustomViewCommand("View");
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintAreas.Should().Equal(before);
        command.Revert(ctx);
        sheet.PrintAreas.Should().Equal(before);
    }
}
