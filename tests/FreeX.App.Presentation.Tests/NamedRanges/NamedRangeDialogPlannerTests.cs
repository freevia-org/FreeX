using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.App.Presentation.NamedRanges;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.NamedRanges;

public sealed class NamedRangeDialogPlannerTests
{
    [Fact]
    public void FilterItems_SplitsWorkbookAndWorksheetScopedNames()
    {
        var workbookName = Row("Sales");
        var sheetName = Row("Local", DefinedNameScope.ForSheet(SheetId.New(), "Sheet2"));

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
        var validName = Row("Sales");
        var errorValueName = Row("BadValue", value: "#REF!");
        var errorRefersToName = Row("BadRef", refersTo: "#NAME?");

        NamedRangeDialogPlanner.FilterItems(
                [validName, errorValueName, errorRefersToName],
                NamedRangeFilterOption.Errors)
            .Should().Equal(errorValueName, errorRefersToName);

        NamedRangeDialogPlanner.FilterItems(
                [validName, errorValueName, errorRefersToName],
                NamedRangeFilterOption.NoErrors)
            .Should().Equal(validName);
    }

    private static DefinedNameRow Row(
        string name,
        DefinedNameScope? scope = null,
        string refersTo = "Sheet1!A1:A2",
        string value = "Sheet1!A1:A2") =>
        DefinedNameListProjector.CreateRow(name, scope ?? DefinedNameScope.Workbook, refersTo, value);
}
