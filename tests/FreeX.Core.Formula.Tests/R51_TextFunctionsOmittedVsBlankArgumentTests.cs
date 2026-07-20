using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R51-formula-textbefore-after-split-3-1/-2/-3/-4: TEXTBEFORE/TEXTAFTER's instance_num, FIND/SEARCH's
/// start_num, LEFT/RIGHT's num_chars, and SUBSTITUTE's instance_num all conflated "argument genuinely
/// omitted" with "explicit argument evaluates to blank" (e.g. a reference to an empty cell). Real Excel
/// coerces an explicit blank-cell reference to numeric 0 for these arguments -- it does NOT fall back to
/// the function's normal default the way a truly-omitted trailing argument does (the same distinction
/// VLOOKUP's range_lookup makes between a blank reference and an omitted argument). FormulaEvaluator.
/// Functions.cs now substitutes a dedicated OmittedOptionalOrdinalArgumentValue sentinel for a genuinely-
/// omitted slot at these specific call sites, mirroring the existing TextSplitOmittedPadArgumentValue
/// fix for TEXTSPLIT's pad_with, so each function's argument parser can tell the two cases apart.
/// </summary>
public sealed class R51_TextFunctionsOmittedVsBlankArgumentTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet SheetWithBlankB1()
    {
        // B1 is never set, so referencing it evaluates to BlankValue -- an explicit blank-cell
        // reference, distinct from the argument slot being omitted entirely.
        return new Sheet(SheetId.New(), "S");
    }

    [Fact]
    public void TextBefore_InstanceNumAsBlankCellReference_ReturnsValueError()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=TEXTBEFORE(\"alpha\",\"a\",B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void TextBefore_InstanceNumOmitted_StillDefaultsToOne_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        // Omitted instance_num defaults to 1: first "a" in "alpha" -> "" before it.
        _eval.Evaluate("=TEXTBEFORE(\"alpha\",\"a\")", sheet).Should().Be(new TextValue(""));
    }

    [Fact]
    public void Find_StartNumAsBlankCellReference_ReturnsValueError()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=FIND(\"l\",\"hello\",B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Find_StartNumOmitted_StillDefaultsToOne_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=FIND(\"l\",\"hello\")", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Search_StartNumAsBlankCellReference_ReturnsValueError()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=SEARCH(\"l\",\"hello\",B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Search_StartNumOmitted_StillDefaultsToOne_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=SEARCH(\"l\",\"hello\")", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Left_NumCharsAsBlankCellReference_ReturnsEmptyString()
    {
        var sheet = SheetWithBlankB1();

        // Blank-cell reference coerces to 0, not the omitted-argument default of 1.
        _eval.Evaluate("=LEFT(\"hello\",B1)", sheet).Should().Be(new TextValue(""));
    }

    [Fact]
    public void Left_NumCharsOmitted_StillDefaultsToOne_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=LEFT(\"hello\")", sheet).Should().Be(new TextValue("h"));
    }

    [Fact]
    public void Right_NumCharsAsBlankCellReference_ReturnsEmptyString()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=RIGHT(\"hello\",B1)", sheet).Should().Be(new TextValue(""));
    }

    [Fact]
    public void Right_NumCharsOmitted_StillDefaultsToOne_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=RIGHT(\"hello\")", sheet).Should().Be(new TextValue("o"));
    }

    [Fact]
    public void Substitute_InstanceNumAsBlankCellReference_ReturnsValueError()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=SUBSTITUTE(\"aaa\",\"a\",\"b\",B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Substitute_InstanceNumOmitted_StillReplacesAll_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        // Genuinely-omitted instance_num means "replace all" -> "bbb".
        _eval.Evaluate("=SUBSTITUTE(\"aaa\",\"a\",\"b\")", sheet).Should().Be(new TextValue("bbb"));
    }
}
