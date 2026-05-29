using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class NumberFormatDecimalAdjusterTests
{
    [Theory]
    [InlineData(null, "0.0")]
    [InlineData("", "0.0")]
    [InlineData("General", "0.0")]
    [InlineData("0", "0.0")]
    [InlineData("#,##0", "#,##0.0")]
    [InlineData("#,##0.00", "#,##0.000")]
    [InlineData("$#,##0.00", "$#,##0.000")]
    public void AddDecimalPlace_AddsOneDecimalSlot(string? format, string expected)
    {
        NumberFormatDecimalAdjuster.AddDecimalPlace(format).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "0")]
    [InlineData("", "0")]
    [InlineData("General", "0")]
    [InlineData("0", "0")]
    [InlineData("#,##0.0", "#,##0")]
    [InlineData("#,##0.00", "#,##0.0")]
    [InlineData("$#,##0.000", "$#,##0.00")]
    public void RemoveDecimalPlace_RemovesOneDecimalSlot(string? format, string expected)
    {
        NumberFormatDecimalAdjuster.RemoveDecimalPlace(format).Should().Be(expected);
    }

    [Fact]
    public void DecimalAdjustmentRegexes_AreGeneratedAndCached()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.Host", "NumberFormatDecimalAdjuster.cs"));

        source.Should().Contain("[GeneratedRegex");
        source.Should().NotMatchRegex(@"\bRegex\.Match\s*\(");
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
