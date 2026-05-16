using FluentAssertions;
using Freexcel.App.Host;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class WorkbookThemeDialogColorCodecTests
{
    [Theory]
    [InlineData("#156082")]
    [InlineData("156082")]
    [InlineData("  #156082  ")]
    public void ParseColor_AcceptsExcelStyleHexText(string text)
    {
        WorkbookThemeDialogColorCodec.ParseColor(text)
            .Should().Be(new CellColor(21, 96, 130));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#12ZZ56")]
    public void ParseColor_RejectsInvalidHexText(string text)
    {
        Action act = () => WorkbookThemeDialogColorCodec.ParseColor(text);

        act.Should().Throw<FormatException>()
            .WithMessage("Enter theme colors as #RRGGBB values.");
    }

    [Fact]
    public void FormatColor_UsesUppercaseHexText()
    {
        WorkbookThemeDialogColorCodec.FormatColor(new CellColor(21, 96, 130))
            .Should().Be("#156082");
    }
}
