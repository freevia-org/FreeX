using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class FormControlListResolverTests
{
    private static Workbook NewWorkbookWithChoices(out Sheet sheet)
    {
        var workbook = new Workbook("test");
        sheet = workbook.AddSheet("highlight-options");
        // I6:I10 source list.
        sheet.SetCell(new CellAddress(sheet.Id, 6, 9), Cell.FromValue(new TextValue("Due in next 7 days")));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 9), Cell.FromValue(new TextValue("Due in next 14 days")));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 9), Cell.FromValue(new TextValue("Last 7 days")));
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), Cell.FromValue(new TextValue("Last 14 days")));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 9), Cell.FromValue(new TextValue("Custom date Range")));
        return workbook;
    }

    [Fact]
    public void ResolveSelectedText_PlainSameSheetRange_ReturnsSelectedIndexCellText()
    {
        var workbook = NewWorkbookWithChoices(out var sheet);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "$I$6:$I$10",
            SelectedIndex = 2,
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().Be("Due in next 14 days");
    }

    [Fact]
    public void ResolveSelectedText_DefinedName_ReturnsSelectedIndexCellText()
    {
        var workbook = NewWorkbookWithChoices(out var sheet);
        // high.choices = 'highlight-options'!$I$6:$I$10
        workbook.DefineNamedRange(
            "high.choices",
            new GridRange(
                new CellAddress(sheet.Id, 6, 9),
                new CellAddress(sheet.Id, 10, 9)));

        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "high.choices",
            SelectedIndex = 2,
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().Be("Due in next 14 days");
    }

    [Fact]
    public void ResolveSelectedText_SheetQualifiedRange_ReturnsSelectedIndexCellText()
    {
        var workbook = NewWorkbookWithChoices(out var sheet);
        var other = workbook.AddSheet("other");
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            // Referenced from a different active sheet but pointing back to highlight-options.
            ListFillRange = "'highlight-options'!$I$6:$I$10",
            SelectedIndex = 3,
        };

        FormControlListResolver.ResolveSelectedText(control, other, workbook)
            .Should().Be("Last 7 days");
    }

    [Fact]
    public void ResolveSelectedText_FirstIndex_ReturnsFirstCell()
    {
        var workbook = NewWorkbookWithChoices(out var sheet);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "$I$6:$I$10",
            SelectedIndex = 1,
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().Be("Due in next 7 days");
    }

    [Fact]
    public void ResolveSelectedText_NoSelection_ReturnsNull()
    {
        var workbook = NewWorkbookWithChoices(out var sheet);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "$I$6:$I$10",
            SelectedIndex = 0, // Excel uses 0 / absent for "nothing selected".
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveSelectedText_IndexBeyondRange_ReturnsNull()
    {
        var workbook = NewWorkbookWithChoices(out var sheet);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "$I$6:$I$10",
            SelectedIndex = 99,
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveSelectedText_UnresolvableName_ReturnsNull()
    {
        var workbook = NewWorkbookWithChoices(out var sheet);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "no.such.name",
            SelectedIndex = 1,
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveSelectedText_EmptyListFillRange_ReturnsNull()
    {
        var workbook = NewWorkbookWithChoices(out var sheet);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = null,
            SelectedIndex = 1,
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveSelectedText_NonListControl_ReturnsNull()
    {
        var workbook = NewWorkbookWithChoices(out var sheet);
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            ListFillRange = "$I$6:$I$10",
            SelectedIndex = 2,
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveSelectedText_ItemCellIsSpillMember_ReturnsSpilledText()
    {
        // I6 is a dynamic-array anchor whose formula spills down into I7:I10. Only I6 lives in the
        // sheet's cell dictionary; I7:I10 are live spill members that exist solely in the sheet's
        // spill overlay (Sheet.SetSpillRange / _spillValues), exactly like a real SORT()/FILTER() spill.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("spill-options");
        var anchor = new CellAddress(sheet.Id, 6, 9); // I6
        sheet.SetFormula(anchor, "=SORT(H6:H10)");
        sheet.SetCell(anchor, new TextValue("Due in next 7 days"));
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,]
        {
            { new TextValue("Due in next 7 days") },  // slot [0,0] — ignored by SetSpillRange (anchor's own cell)
            { new TextValue("Due in next 14 days") },
            { new TextValue("Last 7 days") },
            { new TextValue("Last 14 days") },
            { new TextValue("Custom date Range") },
        }));

        // Sanity: I8 (the 3rd item) really is spill-only, not a real cell.
        sheet.GetCell(8, 9).Should().BeNull();

        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "$I$6:$I$10",
            SelectedIndex = 3, // -> I8, a spill member
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().Be("Last 7 days");
    }

    [Fact]
    public void ResolveSelectedText_ItemCellIsOrdinaryCell_StillResolvesAfterSpillFix()
    {
        // Sibling/no-regression case: the ordinary (non-spill) plain-range lookup used by every
        // other test in this file must keep working unchanged now that resolution goes through
        // Sheet.GetValue instead of Sheet.GetCell.
        var workbook = NewWorkbookWithChoices(out var sheet);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "$I$6:$I$10",
            SelectedIndex = 4,
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().Be("Last 14 days");
    }

    [Fact]
    public void PopulateSelectedText_FillsSelectedTextFieldForListControls()
    {
        var workbook = NewWorkbookWithChoices(out var sheet);
        var dropDown = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "$I$6:$I$10",
            SelectedIndex = 2,
        };
        var checkBox = new FormControlModel { Kind = FormControlKind.CheckBox };
        sheet.FormControls.Add(dropDown);
        sheet.FormControls.Add(checkBox);

        FormControlListResolver.PopulateSelectedText(sheet, workbook);

        dropDown.SelectedText.Should().Be("Due in next 14 days");
        checkBox.SelectedText.Should().BeNull();
    }
}
