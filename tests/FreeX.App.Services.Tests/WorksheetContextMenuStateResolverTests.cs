using FluentAssertions;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorksheetContextMenuStateResolverTests
{
    [Fact]
    public void ResolveWorksheetState_ProjectsObjectAndDropdownStateFromSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var range = new GridRange(address, new CellAddress(sheet.Id, 3, 2));
        sheet.SetCell(address, new TextValue("Header"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Value"));
        sheet.ThreadedComments[address] = new ThreadedComment("Resolved", "Author") { IsResolved = true };
        sheet.Comments[address] = "Note";
        sheet.ShownComments.Add(address);
        sheet.Hyperlinks[address] = "https://example.test";
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.PivotTables.Add(new PivotTableModel { Name = "Pivot1", TargetRange = range });

        var state = WorksheetContextMenuPlanner.ResolveWorksheetState(sheet, address);

        state.HasThreadedComment.Should().BeTrue();
        state.IsThreadedCommentResolved.Should().BeTrue();
        state.HasNote.Should().BeTrue();
        state.NoteIsShown.Should().BeTrue();
        state.HasHyperlink.Should().BeTrue();
        state.HasAutoFilterHeaderTarget.Should().BeTrue();
        state.HasDropdownTarget.Should().BeTrue();
        state.HasPivotTableTarget.Should().BeTrue();
    }

    [Fact]
    public void ResolveWorksheetState_ListValidationEnablesDropdownWithoutAutoFilter()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            Type = DvType.List,
            ShowDropdown = true,
        });

        var state = WorksheetContextMenuPlanner.ResolveWorksheetState(sheet, address);

        state.HasDropdownTarget.Should().BeTrue();
        state.HasAutoFilterHeaderTarget.Should().BeFalse();
        state.HasThreadedComment.Should().BeFalse();
        state.HasPivotTableTarget.Should().BeFalse();
    }
}
