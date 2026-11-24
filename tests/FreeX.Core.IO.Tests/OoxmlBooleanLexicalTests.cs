using Free.Shared.Opc;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class OoxmlBooleanLexicalTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void TryParseRecognizesTheFourXsdBooleanTokens(string value, bool expected) =>
        OoxmlBooleanLexical.TryParse(value).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("TRUE")]
    [InlineData(" true ")]
    [InlineData("bogus")]
    public void TryParseReturnsNullForAbsentOrInvalidValues(string? value) =>
        OoxmlBooleanLexical.TryParse(value).Should().BeNull();

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("on", false)]
    [InlineData("TRUE", false)]
    [InlineData(null, false)]
    public void IsTrueAcceptsOnlyOneAndTrue(string? value, bool expected) =>
        OoxmlBooleanLexical.IsTrue(value).Should().Be(expected);

    [Fact]
    public void SpreadsheetReadersUseTheSharedLexicalInsteadOfLocalTokenSwitches()
    {
        var root = TestWorkspaceFileLocator.Find("src", "FreeX.Core.IO", "XlsxChartDataLabelReader.cs");
        var dir = Path.GetDirectoryName(root)!;
        foreach (var name in new[]
                 {
                     "XlsxChartDataLabelReader.cs",
                     "XlsxChartTrendlineErrorBarReader.cs",
                     "XlsxStructuredTableNativeMetadataReader.cs",
                     "XlsxShapeTextDocumentCodec.cs",
                 })
        {
            var source = File.ReadAllText(Path.Combine(dir, name));
            source.Should().Contain("OoxmlBooleanLexical.TryParse(", name);
            source.Should().NotContain("\"1\" or \"true\" => true", name);
        }
    }
}
