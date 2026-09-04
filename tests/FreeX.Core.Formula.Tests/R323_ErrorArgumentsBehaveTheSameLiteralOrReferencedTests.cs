using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// r323: a function must treat an error argument the same way whether the error is written
/// literally or arrives through a cell reference.
///
/// <para>Excel's own rules for which functions swallow an error and which propagate it are
/// intricate, and this machine has no Excel to check them against -- so asserting "every function
/// propagates" would encode my belief about Excel rather than a verified fact, which is how this
/// program has produced false findings before. This asserts something narrower that needs no
/// external ground truth: whatever a VALUE-taking function does with <c>#REF!</c>, it must do the
/// same when that same error comes from a cell, so a disagreement is a bug regardless of what Excel
/// does. The "value-taking" qualifier is not decoration -- see <see cref="ReferenceTaking"/>, where
/// the first run of this test proved the unqualified version wrong.</para>
///
/// <para>The function list comes from <see cref="BuiltInFunctions.Names"/> rather than being typed
/// out, so a function added tomorrow is covered by construction.</para>
/// </summary>
public sealed class R323_ErrorArgumentsBehaveTheSameLiteralOrReferencedTests
{
    private readonly FormulaEvaluator _eval = new();

    /// <summary>
    /// Special forms whose arguments are not ordinary values: LAMBDA/LET bind names, SINGLE and
    /// ISOMITTED are argument-context markers. Passing them a bare error is a parse-level question,
    /// not the value-level one this test is about.
    /// </summary>
    private static readonly HashSet<string> SpecialForms =
        new(StringComparer.OrdinalIgnoreCase) { "LAMBDA", "LET", "SINGLE", "ISOMITTED" };

    /// <summary>
    /// Volatile functions return a different value on each call by design, so comparing two
    /// evaluations of them says nothing about error handling.
    /// </summary>
    private static readonly HashSet<string> Volatile =
        new(StringComparer.OrdinalIgnoreCase)
        { "NOW", "TODAY", "RAND", "RANDBETWEEN", "RANDARRAY", "INDIRECT", "OFFSET", "CELL", "INFO" };

    /// <summary>
    /// Functions that take a REFERENCE rather than a value, where the two spellings are not two ways
    /// of passing the same thing and are supposed to differ.
    ///
    /// <para>This is the correction the first run forced. The premise above -- "an error is the same
    /// error however it reaches a function" -- is true only of functions that consume a VALUE. For a
    /// reference-taker, <c>A1</c> is a location and <c>#REF!</c> is a broken location: <c>ROW(A1)</c>
    /// asks where A1 sits and is not entitled to look at what A1 contains, so it answers 1 while
    /// <c>ROW(#REF!)</c> answers #REF!. Both are right. The first run reported all twelve of these as
    /// disagreements, which would have been twelve false findings in a single test.</para>
    /// </summary>
    private static readonly HashSet<string> ReferenceTaking =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "AREAS", "COLUMN", "COLUMNS", "ROW", "ROWS", "SHEET", "SHEETS",
            "COUNT", "COUNTA", "COUNTBLANK", "ISREF", "ISFORMULA",
        };

    [Fact]
    public void EveryFunctionTreatsALiteralErrorLikeAReferencedOne()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("S");
        // A1 holds the same error the literal form writes, so the two spellings differ only in how
        // the error reaches the function.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.Ref);

        var disagreements = new List<string>();
        var examined = 0;

        foreach (var name in BuiltInFunctions.Names.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (SpecialForms.Contains(name) || Volatile.Contains(name) || ReferenceTaking.Contains(name))
                continue;

            examined++;

            var literal = Evaluate($"={name}(#REF!)");
            var referenced = Evaluate($"={name}(A1)");

            // Rendered, not Equals: RangeValue has no value equality, so two structurally identical
            // ranges compared unequal and HSTACK/VSTACK were reported as differing when they agree.
            if (!string.Equals(Describe(literal), Describe(referenced), StringComparison.Ordinal))
                disagreements.Add($"{name}: literal [{Describe(literal)}] referenced [{Describe(referenced)}]");

            continue;

            ScalarValue? Evaluate(string formula)
            {
                try
                {
                    return _eval.Evaluate(formula, sheet, workbook) as ScalarValue;
                }
                catch (Exception ex)
                {
                    // A throw is itself a behaviour worth comparing: if one spelling throws and the
                    // other returns a value, that is exactly the inconsistency being looked for.
                    return new TextValue($"threw:{ex.GetType().Name}");
                }
            }
        }

        examined.Should().BeGreaterThan(100,
            "the built-in catalog is large; if this collapses the name source stopped working and "
            + "the contract is passing vacuously");

        disagreements.Should().BeEmpty(
            "an error is the same error however it reaches a function, so these spellings must agree:\n"
            + string.Join("\n", disagreements));
    }

    private static string Describe(ScalarValue? value) => value switch
    {
        null => "<non-scalar>",
        ErrorValue error => error.Code,
        TextValue text => $"\"{text.Value}\"",
        RangeValue range => RenderRange(range),
        _ => value.ToString() ?? "<null>",
    };

    /// <summary>A range's CONTENT, so two structurally identical ranges compare as identical.</summary>
    private static string RenderRange(RangeValue range)
    {
        var cells = new List<string>();
        for (var r = 0; r < range.RowCount; r++)
        {
            for (var c = 0; c < range.ColCount; c++)
                cells.Add(Describe(range.Cells[r, c]));
        }

        return $"range[{range.RowCount}x{range.ColCount}]{{{string.Join(",", cells)}}}";
    }
}
