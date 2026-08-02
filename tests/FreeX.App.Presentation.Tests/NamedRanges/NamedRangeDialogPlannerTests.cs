using FluentAssertions;
using FreeX.App.Presentation.NamedRanges;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.NamedRanges;

public sealed class NamedRangeDialogPlannerTests
{
    [Fact]
    public void FilterItems_SplitsWorkbookAndWorksheetScopedNames()
    {
        var workbookName = new NamedRangeViewModel("Sales", "Sheet1!A1:A2", "Sheet1!A1:A2", "Workbook", "");
        var sheetName = new NamedRangeViewModel(
            "Local", "Sheet2!B1:B2", "Sheet2!B1:B2", "Sheet2", "", scopeSheetId: new SheetId(Guid.NewGuid()));

        NamedRangeDialogPlanner.FilterItems([workbookName, sheetName], NamedRangeFilterOption.All)
            .Should().Equal(workbookName, sheetName);
        NamedRangeDialogPlanner.FilterItems([workbookName, sheetName], NamedRangeFilterOption.Workbook)
            .Should().Equal(workbookName);
        NamedRangeDialogPlanner.FilterItems([workbookName, sheetName], NamedRangeFilterOption.Worksheet)
            .Should().Equal(sheetName);
    }

    [Fact]
    public void FilterItems_DetectsFormulaErrorsInValueOrReference()
    {
        var validName = new NamedRangeViewModel("Sales", "Sheet1!A1:A2", "Sheet1!A1:A2", "Workbook", "");
        var errorValueName = new NamedRangeViewModel("BadValue", "#REF!", "Sheet1!A1:A2", "Workbook", "");
        var errorRefersToName = new NamedRangeViewModel("BadRef", "Sheet1!A1:A2", "#NAME?", "Workbook", "");

        NamedRangeDialogPlanner.FilterItems(
                [validName, errorValueName, errorRefersToName],
                NamedRangeFilterOption.Errors)
            .Should().Equal(errorValueName, errorRefersToName);

        NamedRangeDialogPlanner.FilterItems(
                [validName, errorValueName, errorRefersToName],
                NamedRangeFilterOption.NoErrors)
            .Should().Equal(validName);
    }
}
