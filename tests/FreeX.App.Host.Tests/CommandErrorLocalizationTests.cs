using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class CommandErrorLocalizationTests
{
    [Theory]
    [InlineData("Sheet 00000000 not found")]
    [InlineData("Sheet 00000000 not found.")]
    [InlineData("Command failed: Sheet 00000000 not found")]
    [InlineData("Command failed: Sheet 00000000 not found.")]
    public void LocalizeCommandErrorMessage_NormalizesRawMissingSheetIds(string message)
    {
        var localized = MainWindow.LocalizeCommandErrorMessage(message);

        localized.Should().Be(UiText.Get("MainWindowMessage_SheetNotFound"));
    }

    [Fact]
    public void LocalizeCommandErrorMessage_PreservesNonSheetFailures()
    {
        const string message = "Command failed: simulated apply failure";

        var localized = MainWindow.LocalizeCommandErrorMessage(message);

        localized.Should().Be(message);
    }
}
