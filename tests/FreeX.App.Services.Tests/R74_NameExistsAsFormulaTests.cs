using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Direct unit tests for <see cref="WorkbookReferenceNavigator.NameExistsAsFormula"/>, added for
/// R74-commands-name-manager-4-2. This is the piece both shells' Name Box "define on Enter" paths
/// consult so an existing named FORMULA/constant (which has no <see cref="GridRange"/> to navigate
/// to, and so previously fell through to the "define a brand-new name" path exactly like a truly-new
/// name) is recognized and the create is refused rather than silently clobbering it with a range.
/// </summary>
public sealed class R74_NameExistsAsFormulaTests
{
    [Fact]
    public void NameExistsAsFormula_WorkbookGlobalFormula_ReturnsTrue()
    {
        var sheetId = SheetId.New();
        var namedFormulas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TaxRate"] = "0.08"
        };

        WorkbookReferenceNavigator.NameExistsAsFormula(
                "TaxRate", sheetId, static _ => null, namedFormulas)
            .Should().BeTrue();
    }

    [Fact]
    public void NameExistsAsFormula_UnknownName_ReturnsFalse()
    {
        var sheetId = SheetId.New();
        var namedFormulas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TaxRate"] = "0.08"
        };

        WorkbookReferenceNavigator.NameExistsAsFormula(
                "BrandNewName", sheetId, static _ => null, namedFormulas)
            .Should().BeFalse();
    }

    [Fact]
    public void NameExistsAsFormula_IsCaseInsensitive()
    {
        var sheetId = SheetId.New();
        var namedFormulas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TaxRate"] = "0.08"
        };

        WorkbookReferenceNavigator.NameExistsAsFormula(
                "taxrate", sheetId, static _ => null, namedFormulas)
            .Should().BeTrue();
    }

    [Fact]
    public void NameExistsAsFormula_ScopedFormulaViaResolver_ReturnsTrue()
    {
        var sheetId = SheetId.New();

        WorkbookReferenceNavigator.NameExistsAsFormula(
                "LocalRate",
                sheetId,
                static _ => null,
                namedFormulas: null,
                resolveScopedFormula: (name, sheet) =>
                    string.Equals(name, "LocalRate", StringComparison.OrdinalIgnoreCase) && sheet.Equals(sheetId)
                        ? "0.05"
                        : null)
            .Should().BeTrue();
    }

    // Sibling no-regression: a plain, non-colliding name resolves false even when both a resolver
    // and a global dictionary are supplied.
    [Fact]
    public void NameExistsAsFormula_NoMatchInEitherSource_ReturnsFalse()
    {
        var sheetId = SheetId.New();
        var namedFormulas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TaxRate"] = "0.08"
        };

        WorkbookReferenceNavigator.NameExistsAsFormula(
                "SomethingElse",
                sheetId,
                static _ => null,
                namedFormulas,
                resolveScopedFormula: (_, _) => null)
            .Should().BeFalse();
    }
}
