using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class WatchWindowMessageFormatterTests
{
    [Theory]
    [InlineData(1, "A1", "1 cell added to Watch Window.")]
    [InlineData(2, "A1:B1", "2 cells added to Watch Window.")]
    [InlineData(0, "A1:B1", "A1:B1 is already watched.")]
    public void FormatAddResult_HandlesSingularPluralAndNoOp(int added, string rangeText, string expected)
    {
        WatchWindowMessageFormatter.FormatAddResult(added, rangeText).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "A1", "1 cell removed from Watch Window.")]
    [InlineData(2, "A1:B1", "2 cells removed from Watch Window.")]
    [InlineData(0, "A1:B1", "A1:B1 is not watched.")]
    public void FormatRemoveResult_HandlesSingularPluralAndNoOp(int removed, string rangeText, string expected)
    {
        WatchWindowMessageFormatter.FormatRemoveResult(removed, rangeText).Should().Be(expected);
    }

    [Fact]
    public void WatchWindowDialog_ExposesKeyboardAccessKeysForCommandButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WatchWindowDialog.cs"));

        source.Should().Contain("Content = \"_Add Watch\"");
        source.Should().Contain("Content = \"_Refresh\"");
        source.Should().Contain("Content = \"_Delete Watch\"");
        source.Should().Contain("Content = \"_Close\"");
    }

    [Fact]
    public void WatchWindowDialog_ExposesExcelLikeWatchColumns()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WatchWindowDialog.cs"));

        source.Should().Contain("Header = \"Book\"");
        source.Should().Contain("Header = \"Sheet\"");
        source.Should().Contain("Header = \"Name\"");
        source.Should().Contain("Header = \"Cell\"");
        source.Should().Contain("Header = \"Value\"");
        source.Should().Contain("Header = \"Formula\"");
    }
}
