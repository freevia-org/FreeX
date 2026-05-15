using Freexcel.Core.Commands;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public sealed class NumberFormatShortcutServiceTests
{
    [Theory]
    [InlineData(NumberFormatShortcut.General, "General")]
    [InlineData(NumberFormatShortcut.Number, "#,##0.00")]
    [InlineData(NumberFormatShortcut.Currency, "$#,##0.00")]
    [InlineData(NumberFormatShortcut.Percentage, "0%")]
    [InlineData(NumberFormatShortcut.Date, "m/d/yyyy")]
    [InlineData(NumberFormatShortcut.Time, "h:mm AM/PM")]
    [InlineData(NumberFormatShortcut.Scientific, "0.00E+00")]
    public void GetFormat_ReturnsExcelCompatibleFormatCode(NumberFormatShortcut shortcut, string expected)
    {
        NumberFormatShortcutService.GetFormat(shortcut).Should().Be(expected);
    }
}
