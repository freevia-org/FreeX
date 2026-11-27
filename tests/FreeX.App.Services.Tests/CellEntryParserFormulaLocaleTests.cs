using System.Globalization;

using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class CellEntryParserFormulaLocaleTests
{
    [Fact]
    public void CreateCell_StoresLocalizedFormulaUsingInvariantFormulaSeparators()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var cell = CellEntryParser.CreateCell(
                "=SUM(1,5;IF(A1=2,5;3,5;4,5))",
                new CellAddress(SheetId.New(), 0, 0),
                useR1C1ReferenceStyle: false);

            cell.FormulaText.Should().Be("SUM(1.5,IF(A1=2.5,3.5,4.5))");
            FormulaEvaluator.ParseFormula(cell.FormulaText!).Should().NotBeNull();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void CreateCell_ConvertsRegionalSeparatorsBeforeR1C1ReferenceConversion()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var cell = CellEntryParser.CreateCell(
                "=SUM(R1C1;1,5)",
                new CellAddress(SheetId.New(), 1, 1),
                useR1C1ReferenceStyle: true);

            cell.FormulaText.Should().Be("SUM($A$1,1.5)");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
