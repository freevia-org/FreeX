using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class ExcelParityStatisticalAliasTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Devsq_ReturnsSumOfSquaredDeviationsFromMean()
    {
        Number("=DEVSQ(A1:A3)", Values(1, 2, 3)).Should().BeApproximately(2, 1e-12);
    }

    [Fact]
    public void GammalnPrecise_MatchesGammalnForPositiveInputs()
    {
        Number("=GAMMALN.PRECISE(4)", Values()).Should().BeApproximately(Math.Log(6), 1e-12);
    }

    [Fact]
    public void AverageA_IncludesReferencedTextAndLogicalValues()
    {
        var sheet = Values(10, 7, 9, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Not available"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), BlankValue.Instance);

        Number("=AVERAGEA(A1:A5)", sheet).Should().BeApproximately(5.6, 1e-12);
        Number("=AVERAGEA(A1:A4,A7)", sheet).Should().BeApproximately(7, 1e-12);

        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new BoolValue(true));
        Number("=AVERAGEA(A1:A6)", sheet).Should().BeApproximately(29.0 / 6.0, 1e-12);
    }

    [Fact]
    public void AverageA_DirectTextMatchesExcelCoercion()
    {
        Number("=AVERAGEA(\"5\",TRUE,\"\")", Values()).Should().BeApproximately(2, 1e-12);
        _eval.Evaluate("=AVERAGEA(\"Not available\")", Values()).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=AVERAGEA(A1:A2)", Values()).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void MaxAAndMinA_IncludeReferencedLogicalAndTextValues()
    {
        var sheet = Values(0, 0.2, 0.5, 0.4);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new BoolValue(true));
        Number("=MAXA(A1:A5)", sheet).Should().Be(1);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new BoolValue(false));
        Number("=MINA(A1:A5)", sheet).Should().Be(0);

        var negativeText = Values(-5, -2);
        negativeText.SetCell(new CellAddress(negativeText.Id, 3, 1), new TextValue("n/a"));
        Number("=MAXA(A1:A3)", negativeText).Should().Be(0);
        Number("=MINA(A1:A3)", negativeText).Should().Be(-5);
    }

    [Fact]
    public void MaxAAndMinA_DirectInvalidTextReturnsValueError()
    {
        _eval.Evaluate("=MAXA(\"n/a\")", Values()).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=MINA(\"n/a\")", Values()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void VarAAndStdevA_IncludeReferencedLogicalAndTextValuesAsSample()
    {
        var sheet = Values(10);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("n/a"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new BoolValue(false));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), BlankValue.Instance);

        Number("=VARA(A1:A5)", sheet).Should().BeApproximately(23.583333333333332, 1e-12);
        Number("=STDEVA(A1:A5)", sheet).Should().BeApproximately(Math.Sqrt(23.583333333333332), 1e-12);
    }

    [Fact]
    public void VarPAAndStdevPA_IncludeReferencedLogicalAndTextValuesAsPopulation()
    {
        var sheet = Values(10);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("n/a"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new BoolValue(false));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), BlankValue.Instance);

        Number("=VARPA(A1:A5)", sheet).Should().BeApproximately(17.6875, 1e-12);
        Number("=STDEVPA(A1:A5)", sheet).Should().BeApproximately(Math.Sqrt(17.6875), 1e-12);
    }

    [Fact]
    public void AVarianceFunctions_DirectTextAndEmptySetMatchExcel()
    {
        Number("=VARA(\"5\",TRUE,\"\")", Values()).Should().BeApproximately(7, 1e-12);
        Number("=VARPA(\"5\",TRUE,\"\")", Values()).Should().BeApproximately(14.0 / 3.0, 1e-12);

        _eval.Evaluate("=VARA(\"n/a\",1)", Values()).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=STDEVA(A1:A2)", Values()).Should().Be(ErrorValue.DivByZero);
        _eval.Evaluate("=VARPA(A1:A2)", Values()).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void RegressionAndCovarianceFunctions_ReturnExcelResults()
    {
        var sheet = RegressionValues();

        Number("=COVAR(B1:B3,A1:A3)", sheet).Should().BeApproximately(1, 1e-12);
        Number("=COVARIANCE.P(B1:B3,A1:A3)", sheet).Should().BeApproximately(1, 1e-12);
        Number("=COVARIANCE.S(B1:B3,A1:A3)", sheet).Should().BeApproximately(1.5, 1e-12);
        Number("=PEARSON(B1:B3,A1:A3)", sheet).Should().BeApproximately(0.9819805060619657, 1e-12);
        Number("=RSQ(B1:B3,A1:A3)", sheet).Should().BeApproximately(27.0 / 28.0, 1e-12);
        Number("=SLOPE(A1:A3,B1:B3)", sheet).Should().BeApproximately(1.5, 1e-12);
        Number("=INTERCEPT(A1:A3,B1:B3)", sheet).Should().BeApproximately(2.0 / 3.0, 1e-12);
        Number("=STEYX(A1:A3,B1:B3)", sheet).Should().BeApproximately(Math.Sqrt(1.0 / 6.0), 1e-12);
    }

    [Fact]
    public void RegressionAndCovarianceFunctions_IgnoreNonnumericReferencedPairs()
    {
        var sheet = RegressionValues();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("ignored"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new BoolValue(true));

        Number("=SLOPE(A1:A5,B1:B5)", sheet).Should().BeApproximately(1.5, 1e-12);
        Number("=COVARIANCE.P(B1:B5,A1:A5)", sheet).Should().BeApproximately(1, 1e-12);
    }

    [Fact]
    public void RegressionAndCovarianceFunctions_ReturnExcelErrors()
    {
        var sheet = RegressionValues();
        _eval.Evaluate("=SLOPE(A1:A3,B1:B2)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=STEYX(A1:A2,B1:B2)", sheet).Should().Be(ErrorValue.DivByZero);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));
        _eval.Evaluate("=INTERCEPT(A1:A3,B1:B3)", sheet).Should().Be(ErrorValue.DivByZero);
        _eval.Evaluate("=PEARSON(A1:A3,B1:B3)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void LegacyVarianceAndNormalAliases_MatchModernFunctions()
    {
        var values = Values(1, 2, 3);
        Number("=VARP(A1:A3)", values).Should().BeApproximately(Number("=VAR.P(A1:A3)", values), 1e-12);
        Number("=STDEVP(A1:A3)", values).Should().BeApproximately(Number("=STDEV.P(A1:A3)", values), 1e-12);

        var probabilities = Values(0.5, 0.8413447460685429);
        Number("=NORMDIST(1,0,1,TRUE)", values).Should().BeApproximately(Number("=NORM.DIST(1,0,1,TRUE)", values), 1e-12);
        Number("=NORMINV(0.8413447460685429,0,1)", values).Should().BeApproximately(Number("=NORM.INV(0.8413447460685429,0,1)", values), 1e-12);
        Number("=NORMSDIST(1)", values).Should().BeApproximately(Number("=NORM.S.DIST(1,TRUE)", values), 1e-12);
        Number("=NORMSINV(0.8413447460685429)", values).Should().BeApproximately(Number("=NORM.S.INV(0.8413447460685429)", values), 1e-12);

        var spilled = _eval.Evaluate("=NORMSINV(A1:A2)", probabilities).Should().BeOfType<RangeValue>().Subject;
        spilled.At(1, 1).Should().BeOfType<NumberValue>().Subject.Value.Should().BeApproximately(0, 1e-8);
        spilled.At(2, 1).Should().BeOfType<NumberValue>().Subject.Value.Should().BeApproximately(1, 1e-6);
    }

    [Fact]
    public void LegacyBetaAndLognormalAliases_MatchModernFunctions()
    {
        var sheet = Values();
        Number("=BETADIST(0.5,2,3)", sheet).Should().BeApproximately(Number("=BETA.DIST(0.5,2,3,TRUE)", sheet), 1e-12);
        Number("=BETADIST(2,2,3,1,3)", sheet).Should().BeApproximately(Number("=BETA.DIST(2,2,3,TRUE,1,3)", sheet), 1e-12);
        Number("=BETAINV(0.6875,2,3)", sheet).Should().BeApproximately(Number("=BETA.INV(0.6875,2,3)", sheet), 1e-10);
        Number("=LOGNORMDIST(EXP(1),0,1)", sheet).Should().BeApproximately(Number("=LOGNORM.DIST(EXP(1),0,1,TRUE)", sheet), 1e-12);
        Number("=LOGINV(0.8413447460685429,0,1)", sheet).Should().BeApproximately(Number("=LOGNORM.INV(0.8413447460685429,0,1)", sheet), 1e-12);
    }

    [Fact]
    public void LegacyDistributionAliases_ReturnExcelErrors()
    {
        var sheet = Values();
        _eval.Evaluate("=NORMDIST(1,0,0,TRUE)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=NORMSINV(0)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=BETADIST(2,2,3)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=LOGNORMDIST(0,0,1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void ModeSngl_ReturnsFirstLowestMostFrequentValue()
    {
        Number("=MODE.SNGL(A1:A5)", Values(3, 2, 2, 3, 2)).Should().Be(2);
    }

    [Fact]
    public void PercentileInc_InterpolatesLikeExcelInclusivePercentile()
    {
        Number("=PERCENTILE.INC(A1:A4,0.25)", Values(1, 2, 3, 4)).Should().BeApproximately(1.75, 1e-12);
    }

    [Fact]
    public void PercentrankInc_InterpolatesLikeExcelInclusivePercentRank()
    {
        Number("=PERCENTRANK.INC(A1:A4,2.5)", Values(1, 2, 3, 4)).Should().BeApproximately(0.5, 1e-12);
    }

    [Fact]
    public void PercentOf_ReturnsSubsetPercentOfAllValues()
    {
        var sheet = Values(100, 200, 300, 400);

        Number("=PERCENTOF(A1:A2,A1:A4)", sheet).Should().BeApproximately(0.3, 1e-12);
        Number("=PERCENTOF(150,A1:A4)", sheet).Should().BeApproximately(0.15, 1e-12);
    }

    [Fact]
    public void PercentOf_UsesExcelSumRulesForRanges()
    {
        var sheet = Values(100, 200, 300);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("ignored"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new BoolValue(true));

        Number("=PERCENTOF(A1:A2,A1:A5)", sheet).Should().BeApproximately(0.5, 1e-12);
    }

    [Fact]
    public void PercentOf_ReturnsExcelErrorsForZeroTotalAndInputErrors()
    {
        var zeroTotal = Values(0, 0);
        _eval.Evaluate("=PERCENTOF(A1,A1:A2)", zeroTotal).Should().Be(ErrorValue.DivByZero);

        var errorSheet = Values(1, 2);
        errorSheet.SetCell(new CellAddress(errorSheet.Id, 3, 1), ErrorValue.NA);
        _eval.Evaluate("=PERCENTOF(A1:A2,A1:A3)", errorSheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void QuartileInc_ReturnsExcelInclusiveQuartiles()
    {
        Number("=QUARTILE.INC(A1:A4,1)", Values(1, 2, 3, 4)).Should().BeApproximately(1.75, 1e-12);
    }

    [Fact]
    public void RankEq_ReturnsTopRankForTies()
    {
        Number("=RANK.EQ(2,A1:A4,0)", Values(3, 2, 2, 1)).Should().Be(2);
    }

    [Fact]
    public void RankAvg_ReturnsAverageRankForTies()
    {
        Number("=RANK.AVG(2,A1:A4,0)", Values(3, 2, 2, 1)).Should().Be(2.5);
    }

    [Fact]
    public void RankAvg_NumberAndOrderRangeArguments_SpillElementwise()
    {
        var sheet = Values(3, 2, 2, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(0));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));

        var value = _eval.Evaluate("=RANK.AVG(B1:B2,A1:A4,C1:C2)", sheet).Should().BeOfType<RangeValue>().Subject;
        value.RowCount.Should().Be(2);
        value.ColCount.Should().Be(1);
        value.At(1, 1).Should().Be(new NumberValue(2.5));
        value.At(2, 1).Should().Be(new NumberValue(2.5));
    }

    private double Number(string formula, Sheet sheet)
    {
        var value = _eval.Evaluate(formula, sheet);
        value.Should().BeOfType<NumberValue>(formula);
        return ((NumberValue)value).Value;
    }

    private static Sheet Values(params double[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        for (var i = 0; i < values.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i + 1, 1), new NumberValue(values[i]));
        return sheet;
    }

    private static Sheet RegressionValues()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(3));
        return sheet;
    }
}
