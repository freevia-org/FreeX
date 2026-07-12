using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R34-commands-name-manager-deep-3: Excel reserves the single-letter defined names
/// "C"/"c" (current column) and "R"/"r" (current row) — they satisfy the ordinary
/// character rules but must still be rejected. Workbook.ValidateNamedRangeName (the gate
/// used by DefineNamedRangeCommand and the WPF Name Manager) previously let them through,
/// diverging from Excel and from the portable DefinedNameValidator.IsReservedToken used by
/// the Avalonia shell.
/// </summary>
public class R34_NameManagerReservedTokenTests
{
    [Theory]
    [InlineData("C")]
    [InlineData("c")]
    [InlineData("R")]
    [InlineData("r")]
    public void ValidateNamedRangeName_ReservedSingleLetter_IsRejected(string name)
    {
        var wb = new Workbook();

        wb.ValidateNamedRangeName(name).Should().NotBeNull();
    }

    // ── Sibling/regression coverage: names that merely contain or extend the reserved
    // letters must still be accepted — only the exact single-letter tokens are reserved. ──

    [Theory]
    [InlineData("Cost")]
    [InlineData("C_1")] // "C1" alone is already rejected as a cell-reference-lookalike, unrelated to this fix
    [InlineData("Rate")]
    [InlineData("CC")]
    public void ValidateNamedRangeName_NamesContainingReservedLetters_StillValid(string name)
    {
        var wb = new Workbook();

        wb.ValidateNamedRangeName(name).Should().BeNull();
    }
}
