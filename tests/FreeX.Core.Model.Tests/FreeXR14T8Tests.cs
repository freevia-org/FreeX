using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-14 bucket T8 regression tests (Core.Commands half, hosted alongside the existing
/// <see cref="FormControlInteractionServiceTests"/> in this project). One focused test per finding.
/// </summary>
public sealed class FreeXR14T8Tests
{
    // R14-form-controls-3: a form control's Cell link (LinkedCell) may be a defined name, e.g.
    // LinkedCell = "MyFlag" — Excel resolves it and writes/mirrors the named cell. FreeX previously
    // only accepted a plain A1 reference, so TryResolveLinkedCell failed, CreateToggleCheckBoxCommand
    // flipped the model but returned null, and the named cell was never written.
    [Fact]
    public void ToggleCheckBox_LinkedCellIsDefinedName_WritesTrueToNamedCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 5, 3); // C5
        wb.DefineNamedRange("MyFlag", new GridRange(addr, addr));

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "MyFlag",
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);

        cmd.Should().NotBeNull("a defined-name Cell link must resolve to a writable cell, like Excel");
        control.IsChecked.Should().BeTrue();

        var ctx = new TestCommandContext(wb);
        var outcome = cmd!.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheet.GetCell(addr)!.Value.Should().Be(new BoolValue(true));
    }
}
