using FreeX.Core.Formula;
using FluentAssertions;
using System.Text.RegularExpressions;

namespace FreeX.Core.Formula.Tests;

public sealed class FormulaParityCatalogTests
{
    private static readonly string[] ExcludedFunctionNames =
    [
        "CUBEKPIMEMBER",
        "CUBEMEMBER",
        "CUBESET",
        "CUBESETCOUNT",
        "CUBEVALUE",
        "IMAGE",
        "RTD",
        "STOCKHISTORY",
        "WEBSERVICE"
    ];


    [Fact]
    public void ExcludedFunctions_AreNotRegisteredBuiltIns()
    {
        BuiltInFunctions.Names.Should().NotIntersectWith(ExcludedFunctionNames);
    }








    private static string SummaryCategoryToSection(string category) => category switch
    {
        "Lambda / Advanced" => "Lambda / Advanced Calculation",
        _ => category
    };

    private static int ParseCount(string value) =>
        int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private readonly record struct CoverageSummaryCounts(
        int Implemented,
        int Partial,
        int NotImplemented,
        int Excluded,
        int InScopeTotal);

}
