using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Integration.Tests;

/// <summary>
/// R100-core-commands-2: r99's <c>NamedDefinitionRecalcHelper.ReferencesNameTransitively</c> (used by
/// DefineNamedRangeCommand/DefineNamedFormulaCommand to compute AffectedCells on a name redefine)
/// recurses into a referenced name's own named-formula text to see if it transitively reaches the
/// redefined name, guarding only against cycles with a call-stack-scoped <c>HashSet</c> -- it never
/// memoized the (boolean) result of expanding a given named formula. So a chain where each level's
/// formula references the next name TWICE (e.g. "Level5" defined as "=Level6+Level6"), a realistic
/// "helper formula reused on both sides of an operator" shape, re-walks the full nested chain beneath
/// each sibling occurrence independently: for a chain of depth N this is O(2^N) parse+traversal work
/// for a SINGLE redefinition of a SINGLE cell's dependency. Fixed by memoizing
/// ReferencesNameTransitively's per-(name, resolved sheet, scanning sheet) result across sibling AST
/// branches and across cells within one redefinition scan, cutting the same chain down to O(N).
/// </summary>
public sealed class R100_NamedDefinitionTransitiveMemoizationTests
{
    /// <summary>
    /// Builds a named-formula chain "Unrelated0" = "Unrelated1+Unrelated1", "Unrelated1" =
    /// "Unrelated2+Unrelated2", ..., bottoming out at a plain literal that never mentions "Rate" at
    /// all, plus a single formula cell containing "=Unrelated0". This chain deliberately never
    /// reaches the redefined name -- which is the REALISTIC and by far most common case in a real
    /// workbook redefinition scan (most formula cells don't reference the one name being redefined).
    /// Because the search result is "not found", C#'s <c>||</c> short-circuit can never skip a
    /// branch on the way down (short-circuiting only helps once a `true` is found) -- unmemoized,
    /// EVERY one of the two occurrences of each nested name must be independently re-parsed and
    /// re-walked, giving genuine O(2^depth) work for a single cell. Memoized, each of the `depth`
    /// distinct names is expanded exactly once no matter how many sibling positions reach it.
    /// </summary>
    private static (Workbook workbook, CellAddress formulaCell) BuildUnrelatedDoublingChainWorkbook(int depth)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        workbook.NamedFormulas["Rate"] = "1.05";
        for (var level = depth - 1; level >= 0; level--)
        {
            var body = level == depth - 1 ? "42" : $"Unrelated{level + 1}+Unrelated{level + 1}";
            workbook.NamedFormulas[$"Unrelated{level}"] = body;
        }

        var formulaCell = new CellAddress(sheet.Id, 1, 2); // B1
        sheet.SetFormula(formulaCell, "Unrelated0");
        return (workbook, formulaCell);
    }

    [Fact]
    public void RedefiningNamedFormula_DeepUnrelatedDoublingChain_CompletesWithoutExponentialBlowup()
    {
        // Depth 26 -> 2^26 (67 million) independent full-chain re-expansions if unmemoized (each
        // doing its own Lexer+Parser pass over the nested formula text), versus 26 expansions
        // memoized. This is squarely within the "realistic helper formula reused on both sides of an
        // operator" shape the finding describes, just carried to enough depth to make the O(2^N) vs
        // O(N) gap unmistakable in wall-clock time (measured: ~24.7s unmemoized vs a few ms memoized)
        // rather than only in a theoretical complexity count.
        const int depth = 26;
        var (workbook, formulaCell) = BuildUnrelatedDoublingChainWorkbook(depth);
        var ctx = new TestCommandContext(workbook);

        var command = new DefineNamedFormulaCommand("Rate", "2");

        var stopwatch = Stopwatch.StartNew();
        var outcome = command.Apply(ctx);
        stopwatch.Stop();

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().NotContain(formulaCell,
            "B1's Unrelated0 chain never mentions Rate at any depth");

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "memoizing ReferencesNameTransitively's per-name result must turn a chain where each " +
            "level references the next name twice from O(2^depth) sibling re-expansions into " +
            "O(depth) -- without memoization this single redefinition takes vastly longer than 5s " +
            "at depth 20 (roughly 2^20 independent full re-parses of the nested chain, since a " +
            "`false` result can never be short-circuited away by the || traversal)");
    }

    [Fact]
    public void RedefiningNamedFormula_SiblingBranchesBothStillDetected_AndUnrelatedChainUnaffected()
    {
        // No-regression sibling: memoizing must not cause a FALSE result to leak across sibling
        // branches or across an unrelated chain that shares some (but not all) of its named-formula
        // vocabulary. "Combined" references BOTH a chain that reaches "Rate" (via "ReachesRate") and
        // a same-shaped chain that does not (via "NeverReachesRate", which bottoms out at a literal,
        // not at Rate) -- if memoization ever mixed up cache entries between differently-scoped or
        // differently-targeted expansions, one of these two would come back wrong.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        workbook.NamedFormulas["Rate"] = "1.05";
        workbook.NamedFormulas["ReachesRate"] = "Rate*2";
        workbook.NamedFormulas["NeverReachesRate"] = "99";
        workbook.NamedFormulas["Combined"] = "ReachesRate+ReachesRate+NeverReachesRate+NeverReachesRate";

        var combinedCell = new CellAddress(sheet.Id, 1, 2); // B1 -- reaches Rate via ReachesRate
        sheet.SetFormula(combinedCell, "Combined");

        var unrelatedCell = new CellAddress(sheet.Id, 2, 2); // B2 -- never reaches Rate at all
        sheet.SetFormula(unrelatedCell, "NeverReachesRate");

        var command = new DefineNamedFormulaCommand("Rate", "2");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Contain(combinedCell,
            "Combined references ReachesRate twice, which reaches the redefined Rate; the sibling " +
            "occurrence of NeverReachesRate in the same formula must not short-circuit that");
        outcome.AffectedCells.Should().NotContain(unrelatedCell,
            "NeverReachesRate never reaches Rate through any chain, memoized or not");
    }
}
