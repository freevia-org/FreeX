using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class FormulaRegexCachePerformanceTests
{
    [Fact]
    public void TextNumberParser_UsesCachedRegexesForRepeatedCoercionChecks()
    {
        var source = FormulaSourceTestSupport.ReadFormulaSource("ExcelTextNumberParser.cs");

        source.Should().Contain("private static readonly Regex FakeLeapDayTextRegex");
        source.Should().Contain("private static readonly Regex MonthNameRegex");
        source.Should().Contain("private static readonly Regex AmPmRegex");
        source.Should().NotMatchRegex(StaticRegexCallPattern);
    }

    [Fact]
    public void DateTimeFunctions_UseCachedRegexesForRepeatedComponentChecks()
    {
        var source = FormulaSourceTestSupport.ReadFormulaSource("BuiltInFunctions.DateTime.cs");

        source.Should().Contain("private static readonly Regex DateTimeTextHasTimeSeparatorRegex");
        source.Should().Contain("private static readonly Regex DateTimeTextHasDateSeparatorRegex");
        source.Should().NotMatchRegex(StaticRegexCallPattern);
    }

    [Fact]
    public void WildcardAndSearchRegexCaches_AreBoundedAndTimed()
    {
        // After consolidation the cache + timeout constraints live in the shared
        // FormulaWildcardHelper; both BuiltInFunctions.Criteria and TextCore delegate to it.
        var helperSource = FormulaSourceTestSupport.ReadFormulaSource("FormulaWildcardHelper.cs");
        var criteriaSource = FormulaSourceTestSupport.ReadFormulaSource("BuiltInFunctions.Criteria.cs");
        var textSource = FormulaSourceTestSupport.ReadFormulaSource("BuiltInFunctions.TextCore.cs");

        helperSource.Should().Contain("FormulaSafetyLimits.MaxRegexCacheEntries");
        helperSource.Should().Contain("FormulaSafetyLimits.RegexTimeout");

        criteriaSource.Should().Contain("FormulaWildcardHelper");
        textSource.Should().Contain("FormulaWildcardHelper");
    }

    private const string StaticRegexCallPattern = @"\bRegex\.(?:Match|IsMatch)\s*\(";

}
