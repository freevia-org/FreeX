using FluentAssertions;
using FreeX.App.Presentation.FormulaBar;

namespace FreeX.App.Presentation.Tests.FormulaBar;

/// <summary>
/// R91-formula-editing-assist-5-2: there was no live argument-signature tooltip while typing a
/// function call (typing "=VLOOKUP(" never showed "VLOOKUP(lookup_value, table_array, ...)" with
/// the current argument bolded). These tests exercise the new portable resolver directly.
/// </summary>
public sealed class R91_FormulaSignatureHelpPlannerTests
{
    [Theory]
    [InlineData("=SUM(1,2,3)", "SUM")]
    [InlineData(" =_xlfn.XLOOKUP(A1,B:B,C:C)", "_XLFN.XLOOKUP")]
    [InlineData("=A1+B1", null)]
    public void ResolveLeadingFunctionName_UsesOnlyAFormulaLeadingCall(string formula, string? expected)
    {
        FormulaSignatureHelpPlanner.ResolveLeadingFunctionName(formula).Should().Be(expected);
    }

    [Fact]
    public void Resolve_RightAfterOpeningParen_HighlightsFirstArgument()
    {
        var info = FormulaSignatureHelpPlanner.Resolve("=VLOOKUP(", caretIndex: 9);

        info.Should().NotBeNull();
        info!.FunctionName.Should().Be("VLOOKUP");
        info.Arguments.Select(a => a.Name).Should().Equal("Lookup_value", "Table_array", "Col_index_num", "Range_lookup");
        info.Arguments.Single(a => a.IsCurrent).Name.Should().Be("Lookup_value");
    }

    [Fact]
    public void Resolve_AfterSecondComma_HighlightsThirdArgument()
    {
        var text = "=VLOOKUP(A1,B1:C10,";
        var info = FormulaSignatureHelpPlanner.Resolve(text, caretIndex: text.Length);

        info.Should().NotBeNull();
        info!.Arguments.Single(a => a.IsCurrent).Name.Should().Be("Col_index_num");
    }

    [Fact]
    public void Resolve_CommaInsideNestedCall_DoesNotAdvanceOuterArgumentIndex()
    {
        // The comma inside SUM(A1,A2) is nested one level deeper than the IF(...) call's own
        // argument list, so it must not be mistaken for a top-level comma of the outer call.
        var text = "=IF(SUM(A1,A2)>10,";
        var info = FormulaSignatureHelpPlanner.Resolve(text, caretIndex: text.Length);

        info.Should().NotBeNull();
        info!.FunctionName.Should().Be("IF");
        info.Arguments.Single(a => a.IsCurrent).Name.Should().Be("Value_if_true");
    }

    [Fact]
    public void Resolve_CommaInsideStringLiteral_DoesNotAdvanceArgumentIndex()
    {
        var text = "=TEXT(1234, \"#,##0\"";
        var info = FormulaSignatureHelpPlanner.Resolve(text, caretIndex: text.Length);

        info.Should().NotBeNull();
        info!.FunctionName.Should().Be("TEXT");
        // Still on the second argument (Format_text) -- the comma inside "#,##0" must be ignored.
        info.Arguments.Single(a => a.IsCurrent).Name.Should().Be("Format_text");
    }

    [Fact]
    public void Resolve_ExtraArgumentsPastCatalog_HighlightsLastKnownArgument()
    {
        // SUM's catalog only documents Number1/Number2, but Excel repeats bolding the trailing
        // optional argument for every further comma (SUM(1,2,3,4,...)).
        var text = "=SUM(1,2,3,";
        var info = FormulaSignatureHelpPlanner.Resolve(text, caretIndex: text.Length);

        info.Should().NotBeNull();
        info!.Arguments.Should().HaveCount(2);
        info.Arguments.Single(a => a.IsCurrent).Name.Should().Be("Number2");
    }

    // ── No-regression siblings ──────────────────────────────────────────────

    [Fact]
    public void Resolve_PlainGroupingParenWithNoPrecedingIdentifier_ReturnsNull_NoRegression()
    {
        var info = FormulaSignatureHelpPlanner.Resolve("=(1+2)", caretIndex: 2);

        info.Should().BeNull();
    }

    [Fact]
    public void Resolve_AfterClosingParen_ReturnsNull_NoRegression()
    {
        var text = "=SUM(A1,A2)";
        var info = FormulaSignatureHelpPlanner.Resolve(text, caretIndex: text.Length);

        info.Should().BeNull();
    }

    [Fact]
    public void Resolve_NonFormulaText_ReturnsNull_NoRegression()
    {
        FormulaSignatureHelpPlanner.Resolve("SUM(", caretIndex: 4).Should().BeNull();
    }
}
