using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class PageSetupDialogXamlTests
{
    [Fact]
    public void PageSetupDialog_ExposesKeyboardAccessKeysForTabsOptionsAndButtons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));

        foreach (var header in new[]
        {
            "_Page",
            "_Margins",
            "_Sheet"
        })
            xaml.Should().Contain($"Header=\"{header}\"");

        foreach (var content in new[]
        {
            "_Center horizontally",
            "Center _vertically",
            "_Print gridlines",
            "Print row and column _headings",
            "_Black and white",
            "_Draft quality",
            "_OK",
            "_Cancel"
        })
            xaml.Should().Contain($"Content=\"{content}\"");
    }
}
