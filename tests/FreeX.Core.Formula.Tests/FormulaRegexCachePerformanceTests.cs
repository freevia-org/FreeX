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
        var criteriaSource = FormulaSourceTestSupport.ReadFormulaSource("BuiltInFunctions.Criteria.cs");
        var textSource = FormulaSourceTestSupport.ReadFormulaSource("BuiltInFunctions.TextCore.cs");

        criteriaSource.Should().Contain("FormulaSafetyLimits.MaxRegexCacheEntries");
        criteriaSource.Should().Contain("FormulaSafetyLimits.RegexTimeout");
        textSource.Should().Contain("FormulaSafetyLimits.MaxRegexCacheEntries");
        textSource.Should().Contain("FormulaSafetyLimits.RegexTimeout");
    }

    private const string StaticRegexCallPattern = @"\bRegex\.(?:Match|IsMatch)\s*\(";

}
