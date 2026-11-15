using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public class CustomViewSheetRenameTests
{
    [Fact]
    public void RenameUpdatesCustomViewIdentityAndOpaqueReferences_AndUndoRedoRestoreThem()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.AddSheet("Other");
        var ctx = new TestCommandContext(workbook);
        var state = new WorksheetCustomViewState("Data", WorksheetViewMode.Normal, 0, 0, null, null,
            ZoomPercent: 125,
            PrintSettings: new WorksheetCustomViewPrintSettings(),
            OpaquePrintAreaExpression: "Data!$A$1:$B$2,OFFSET(Data!$C$1,0,0,2,1)",
            OpaquePrintTitlesExpression: "=OFFSET(Data!$A$1,0,0,2,1)");
        var otherState = new WorksheetCustomViewState("Other", WorksheetViewMode.Normal, 0, 0, null, null,
            OpaquePrintAreaExpression: "OFFSET('[1]Data'!$A$1,0,0,2,1),Data!$A$1",
            OpaquePrintTitlesExpression: "OFFSET(Data!$A$1,");
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state, otherState]));
        var command = new RenameSheetCommand(sheet.Id, "New Data");
        command.Apply(ctx).Success.Should().BeTrue();
        var updated = workbook.CustomViews[0].Sheets;
        updated[0].SheetName.Should().Be("New Data");
        updated[0].OpaquePrintAreaExpression.Should().Be("'New Data'!$A$1:$B$2,OFFSET('New Data'!$C$1,0,0,2,1)");
        updated[0].OpaquePrintTitlesExpression.Should().Be("=OFFSET('New Data'!$A$1,0,0,2,1)");
        updated[1].OpaquePrintAreaExpression.Should().Be("OFFSET('[1]Data'!$A$1,0,0,2,1),'New Data'!$A$1");
        updated[1].OpaquePrintTitlesExpression.Should().Be("OFFSET(Data!$A$1,");
        sheet.ZoomPercent = 90;
        new ApplyCustomViewCommand("View").Apply(ctx).Success.Should().BeTrue();
        sheet.ZoomPercent.Should().Be(125);
        command.Revert(ctx);
        workbook.CustomViews[0].Sheets[0].Should().Be(state);
        workbook.CustomViews[0].Sheets[1].Should().Be(otherState);
        command.Apply(ctx).Success.Should().BeTrue();
        workbook.CustomViews[0].Sheets[0].SheetName.Should().Be("New Data");
    }
    [Fact]
    public void Rename_RewritesLocalReferenceInsideMixedFunctionAndPreservesExternalAndBracketText()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var ctx = new TestCommandContext(workbook);
        const string original = "=SUM(Data!A1,'[1]First:Data'!B2,\"[literal]\",TABLE1[Column])";
        var state = new WorksheetCustomViewState("Data", WorksheetViewMode.Normal, 0, 0, null, null,
            OpaquePrintAreaExpression: original);
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var command = new RenameSheetCommand(sheet.Id, "New Data");
        command.Apply(ctx).Success.Should().BeTrue();
        workbook.CustomViews[0].Sheets[0].OpaquePrintAreaExpression.Should()
            .Be("=SUM('New Data'!A1,'[1]First:Data'!B2,\"[literal]\",TABLE1[Column])");
        command.Revert(ctx);
        workbook.CustomViews[0].Sheets[0].OpaquePrintAreaExpression.Should().Be(original);
    }

}
