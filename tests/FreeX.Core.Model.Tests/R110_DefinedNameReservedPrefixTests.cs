using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-110 finding: Workbook.ValidateNamedRangeName never rejected a candidate name starting
/// with the "_xlnm." (or "_xlchart.") reserved prefix, so DefineNamedRangeCommand/
/// DefineNamedFormulaCommand — the real entry points behind the WPF Name Manager and the
/// Avalonia Define-Name dialog — would happily create a defined name like "_xlnm.Foo" and let
/// it be used live in formulas. But FreeX.Core.IO's XlsxNamedRangeMapper.IsExcelReservedDefinedName
/// treats ANY name with that prefix as Excel-internal and unconditionally skips emitting a
/// &lt;definedName&gt; element for it in CreateDefinedNameEntries, and skips loading one with that
/// prefix in LoadDefinedNames — so the name a user just created would be silently and
/// permanently dropped on the very next save/reload round-trip. This matches real Excel: the New
/// Name / Name Manager dialogs refuse a name that impersonates the "_xlnm." built-in namespace.
/// </summary>
public sealed class R110_DefinedNameReservedPrefixTests
{
    private static (Workbook Workbook, TestCommandContext Ctx) CreateContext()
    {
        var wb = new Workbook("reserved-prefix-test");
        wb.AddSheet("Sheet1");
        return (wb, new TestCommandContext(wb));
    }

    // ── Core fix: Workbook.ValidateNamedRangeName rejects the reserved prefixes ──────────────

    [Theory]
    [InlineData("_xlnm.Foo")]
    [InlineData("_XLNM.Foo")] // case-insensitive
    [InlineData("_xlnm.Print_Area")] // even the exact built-in text, as a user-typed candidate
    [InlineData("_xlchart.Bar")]
    [InlineData("_XLCHART.Bar")]
    public void ValidateNamedRangeName_ReservedPrefix_IsRejected(string name)
    {
        var wb = new Workbook();

        wb.ValidateNamedRangeName(name).Should().NotBeNull();
    }

    // ── Real entry point: DefineNamedRangeCommand.Apply must refuse it too, not just the raw
    //    validator, since that command (not the validator alone) is what the Name Manager /
    //    Define Name dialogs actually invoke. ──────────────────────────────────────────────

    [Fact]
    public void DefineNamedRangeCommand_ReservedPrefixName_Fails_AndNameIsNotCreated()
    {
        var (wb, ctx) = CreateContext();
        var sheet = wb.Sheets[0];
        var range = new GridRange(new CellAddress(sheet.Id, 0, 0), new CellAddress(sheet.Id, 0, 0));

        var outcome = new DefineNamedRangeCommand("_xlnm.Foo", range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
        wb.NamedRanges.Should().NotContainKey("_xlnm.Foo");
    }

    [Fact]
    public void DefineNamedFormulaCommand_ReservedPrefixName_Fails_AndNameIsNotCreated()
    {
        var (wb, ctx) = CreateContext();

        var outcome = new DefineNamedFormulaCommand("_xlnm.Rate", "1.05").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
        wb.NamedFormulas.Should().NotContainKey("_xlnm.Rate");
    }

    // ── Sibling/regression coverage: names that merely contain "_xlnm." or "_xlchart." without
    //    starting with it, and ordinary underscore-prefixed names, must still be accepted — only
    //    a genuine leading match on the reserved prefix is rejected. ─────────────────────────

    [Theory]
    [InlineData("My_xlnm.Foo")] // does not START with the prefix
    [InlineData("_FilterDatabase")] // FreeX's own bare reserved token, unrelated to this prefix rule
    [InlineData("_Foo")]
    [InlineData("Revenue")]
    public void ValidateNamedRangeName_NonPrefixedNames_StillValid(string name)
    {
        var wb = new Workbook();

        wb.ValidateNamedRangeName(name).Should().BeNull();
    }

    [Fact]
    public void DefineNamedRangeCommand_OrdinaryUnderscoreName_StillSucceeds()
    {
        var (wb, ctx) = CreateContext();
        var sheet = wb.Sheets[0];
        var range = new GridRange(new CellAddress(sheet.Id, 0, 0), new CellAddress(sheet.Id, 0, 0));

        var outcome = new DefineNamedRangeCommand("_Internal", range).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        wb.NamedRanges.Should().ContainKey("_Internal");
    }
}
