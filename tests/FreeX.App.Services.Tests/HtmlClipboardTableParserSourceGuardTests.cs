using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class HtmlClipboardTableParserSourceGuardTests
{
    [Fact]
    public void WpfAndWorkbookSessionDelegateToTheCanonicalParser()
    {
        var owner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "HtmlClipboardTableParser.cs");
        var wpf = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "MainWindow.ClipboardCommands.cs");
        var session = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "WorkbookSession.cs");

        owner.Should().Contain("public static class HtmlClipboardTableParser");
        owner.Should().Contain("public static IReadOnlyList<IReadOnlyList<string>>? Parse(");
        wpf.Should().Contain("HtmlClipboardTableParser.Parse(htmlPayload)");
        session.Should().Contain("HtmlClipboardTableParser.Parse(html)");

        foreach (var adapter in new[] { wpf, session })
        {
            adapter.Should().NotContain("TryParseHtmlClipboardTableRows");
            adapter.Should().NotContain("ExtractHtmlClipboardFragment");
            adapter.Should().NotContain("ExtractFirstHtmlTableInner");
            adapter.Should().NotContain("EnumerateHtmlCells");
            adapter.Should().NotContain("DecodeHtmlCellText");
            adapter.Should().NotContain("MsoTextNumberFormatRegex");
            adapter.Should().NotContain("HtmlCellSpan");
        }
    }
}
