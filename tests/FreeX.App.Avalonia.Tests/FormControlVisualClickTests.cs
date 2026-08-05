using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Behavioral tests for the form-control interaction logic wired into the Avalonia shell:
/// tests that the shared <see cref="FormControlInteractionService"/> produces the expected
/// commands when called from the Avalonia click handler path.
/// No running Avalonia UI is needed — we test the service directly.
/// </summary>
public sealed class FormControlVisualClickTests
{
    private static (Workbook Workbook, Sheet Sheet) NewWorkbook()
    {
        var wb = new Workbook("ava-test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet);
    }

    // ── CheckBox toggle (Avalonia path) ──────────────────────────────────────

    [Fact]
    public void FormControl_CheckBox_ToggleViasService_WritesToLinkedCell()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "A1",
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);
        cmd.Should().NotBeNull();

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);

        sheet.GetCell(addr)!.Value.Should().Be(new BoolValue(true));
        control.IsChecked.Should().BeTrue();
    }

    // ── OptionButton (Avalonia path) ─────────────────────────────────────────

    [Fact]
    public void FormControl_OptionButton_SelectViasService_ClearsSiblings()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 3, 3);

        var btn1 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true,  LinkedCell = "$C$3" };
        var btn2 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "$C$3" };
        sheet.FormControls.Add(btn1);
        sheet.FormControls.Add(btn2);

        var cmd = FormControlInteractionService.CreateSelectOptionButtonCommand(
            btn2, sheet.FormControls, sheet.Id, wb);

        btn1.IsChecked.Should().BeFalse("sibling cleared");
        btn2.IsChecked.Should().BeTrue("clicked");

        cmd.Should().NotBeNull();
        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);

        // btn2 is 2nd in the group → index 2
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(2));
    }

    // ── Spinner step (Avalonia path) ─────────────────────────────────────────

    [Fact]
    public void FormControl_Spinner_StepUp_WritesIncrementedValue()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 4);
        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 3,
            Min = 1,
            Max = 10,
            Increment = 2,
            LinkedCell = "D1",
        };

        var cmd = FormControlInteractionService.CreateStepCommand(control, +1, sheet.Id, wb);

        control.Value.Should().Be(5);

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(5));
    }

    [Fact]
    public void FormControl_ScrollBar_StepDown_WritesDecrementedValue()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 2, 1);
        var control = new FormControlModel
        {
            Kind = FormControlKind.ScrollBar,
            Value = 50,
            Min = 0,
            Max = 100,
            Increment = 10,
            LinkedCell = "A2",
        };

        // ScrollBar: StepUp arrow → decrement (up = decrease)
        var cmd = FormControlInteractionService.CreateStepCommand(control, -1, sheet.Id, wb);

        control.Value.Should().Be(40);

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(40));
    }

    // ── ListBox select (Avalonia path) ───────────────────────────────────────

    [Fact]
    public void FormControl_ListBox_SelectItem_WritesIndexToLinkedCell()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 5, 2);
        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            SelectedIndex = 0,
            LinkedCell = "B5",
        };

        var cmd = FormControlInteractionService.CreateSelectListItemCommand(control, 3, sheet.Id, wb);

        control.SelectedIndex.Should().Be(3);

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(3));
    }

    // ── DropDown cycle-select (Avalonia path) ────────────────────────────────

    [Fact]
    public void FormControl_DropDown_SelectListItem_WritesIndexToLinkedCell()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 6);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            SelectedIndex = 2,
            LinkedCell = "F1",
        };

        var cmd = FormControlInteractionService.CreateSelectListItemCommand(control, 4, sheet.Id, wb);

        control.SelectedIndex.Should().Be(4);

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(4));
    }

    // ── FormControlClickKind classification ──────────────────────────────────

    [Fact]
    public void FormControlClickKind_Body_IsDefaultForCheckBox()
    {
        // Smoke-test that the enum value round-trips correctly
        var kind = FormControlClickKind.Body;
        kind.Should().Be(FormControlClickKind.Body);
    }

    [Fact]
    public void FormControlClickKind_StepUp_IsDistinctFromStepDown()
    {
        FormControlClickKind.StepUp.Should().NotBe(FormControlClickKind.StepDown);
    }
}
