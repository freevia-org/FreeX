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
