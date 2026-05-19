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
}
