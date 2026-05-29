using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class InsertCommandSourceTests
{
    [Theory]
    [InlineData("Insert Link", "Link", "K", "InsertLinkBtn_Click")]
    [InlineData("New Comment", "New Comment", "CM", "InsertCommentBtn_Click")]
    [InlineData("Text Box", "Text Box", "TX", "DrawTextBtn_Click")]
    [InlineData("Header &amp; Footer", "Header &amp; Footer", "HF", "HeaderFooterBtn_Click")]
    [InlineData("Insert Symbol", "Symbol", "SY", "SymbolPickerBtn_Click")]
    public void InsertTextLinkCommentAndSymbolCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain($"Content=\"{content}\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Object", "O")]
    [InlineData("Equation", "EQ")]
    public void InsertDeferredTextAndSymbolCommands_RemainDisabledWithoutClickHandlers(string title, string keyTip)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain("IsEnabled=\"False\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().NotContain("Click=");
    }

    [Fact]
    public void InsertHandlers_RouteThroughExpectedDialogsCommandsAndReviewDelegate()
    {
        var insertSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.InsertCommands.cs"));
        var drawingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));

        insertSource.Should().Contain("HyperlinkDialogPrefill.FromCell(");
        insertSource.Should().Contain("new HyperlinkDialog(prefill.Target, prefill.DisplayText)");
        insertSource.Should().Contain("new SetHyperlinkCommand(");
        insertSource.Should().Contain("new HyperlinkMetadata(");
        insertSource.Should().Contain("ToCoreHyperlinkTargetKind(dialog.Result.LinkType)");
        insertSource.Should().Contain("private void InsertCommentBtn_Click(object sender, RoutedEventArgs e) => ReviewNewThreadedCommentBtn_Click(sender, e);");
        insertSource.Should().Contain("new HeaderFooterDialog(sheet)");
        insertSource.Should().Contain("new SetHeaderFooterCommand(");
        insertSource.Should().Contain("new SymbolPickerDialog");
        insertSource.Should().Contain("CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)))");

        drawingSource.Should().Contain("private void DrawTextBtn_Click(object sender, RoutedEventArgs e)    => InsertTextBox();");
        drawingSource.Should().Contain("new TextEntryDialog(\"Insert Text Box\", \"Text:\", \"\")");
        drawingSource.Should().Contain("new AddTextBoxCommand(");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractButtonElementByTitle(string xaml, string title)
    {
        var titleIndex = xaml.IndexOf($"local:RibbonTooltip.Title=\"{title}\"", StringComparison.Ordinal);
        titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} Insert command should be present");

        var start = xaml.LastIndexOf("<Button", titleIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {title} Insert command should be a Button");

        var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
        var closingEnd = xaml.IndexOf("</Button>", titleIndex, StringComparison.Ordinal);
        var end = closingEnd >= 0 && (selfClosingEnd < 0 || closingEnd < selfClosingEnd)
            ? closingEnd + "</Button>".Length
            : selfClosingEnd + 2;

        end.Should().BeGreaterThan(titleIndex, $"the {title} Insert button should have a closing marker");
        return xaml[start..end];
    }
}
