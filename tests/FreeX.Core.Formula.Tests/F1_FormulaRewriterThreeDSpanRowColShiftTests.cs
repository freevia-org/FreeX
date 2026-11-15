using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public class F1_FormulaRewriterThreeDSpanRowColShiftTests
{
    // Verified against Excel 16: editing one sheet does not change the shared 3-D address,
    // including when the formula is a workbook defined name.
    [Theory]
    [InlineData("Sheet1")]
    [InlineData("Sheet2")]
    [InlineData("Sheet3")]
    [InlineData("Other")]
    public void StructuralEdits_LeaveThreeDimensionalAddressUnchanged(string sheet)
    {
        const string formula = "SUM(Sheet1:Sheet3!$B$2:$C$4)";
        FormulaRewriter.Rewrite(formula, new InsertRowsOp(sheet, 1, 1), "Host").Should().BeNull();
        FormulaRewriter.Rewrite(formula, new DeleteRowsOp(sheet, 2, 3), "Host").Should().BeNull();
        FormulaRewriter.Rewrite(formula, new InsertColsOp(sheet, 1, 1), "Host").Should().BeNull();
        FormulaRewriter.Rewrite(formula, new DeleteColsOp(sheet, 2, 2), "Host").Should().BeNull();
    }

    [Fact]
    public void OrdinarySheetReference_StillShifts()
    {
        FormulaRewriter.Rewrite("SUM(Sheet1!A1)", new InsertRowsOp("Sheet1", 1, 1), "Host")
            .Should().Be("SUM(Sheet1!A2)");
    }
}
