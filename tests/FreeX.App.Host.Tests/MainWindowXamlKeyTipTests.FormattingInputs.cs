using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void EditableFontSizeBox_CommitsTypedKeyboardInputWithEnter()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var fontSizeBox = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FontSizeBox");

        fontSizeBox.Attribute("IsEditable")?.Value.Should().Be("True");
        fontSizeBox.Attribute("KeyDown")?.Value.Should().Be("FontSizeBox_KeyDown");
        source.Should().Contain("private void FontSizeBox_KeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("if (e.Key != Key.Enter) return;");
        source.Should().Contain("private void CommitFontSizeBoxText(bool preferSelectedItem = false)");
        source.Should().Contain("var text = preferSelectedItem ? GetSelectedFontSizeText() : FontSizeBox.Text;");
        source.Should().Contain("WorksheetSizeInputParser.TryParsePositiveSize(text, out var size)");
        source.Should().Contain("ApplyFontSizeAndFitRows(size);");
    }

    [Fact]
    public void EditableFontNameBox_CommitsTypedKeyboardInputWithEnter()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var fontNameBox = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FontNameBox");

        fontNameBox.Attribute("IsEditable")?.Value.Should().Be("True");
        fontNameBox.Attribute("IsTextSearchEnabled")?.Value.Should().Be("True");
        fontNameBox.Attribute("KeyDown")?.Value.Should().Be("FontNameBox_KeyDown");
        source.Should().Contain("private void FontNameBox_KeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("if (e.Key != Key.Enter) return;");
        source.Should().Contain("private void CommitFontNameBoxText()");
        source.Should().Contain("var name = FontNameBox.Text?.Trim();");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(FontName: name));");
    }

    [Fact]
    public void EditableFontBoxes_CommitTypedKeyboardInputWhenFocusLeaves()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var fontNameBox = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FontNameBox");
        var fontSizeBox = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FontSizeBox");

        fontNameBox.Attribute("LostKeyboardFocus")?.Value.Should().Be("FontNameBox_LostKeyboardFocus");
        fontSizeBox.Attribute("LostKeyboardFocus")?.Value.Should().Be("FontSizeBox_LostKeyboardFocus");
        source.Should().Contain("private void FontNameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        source.Should().Contain("private void FontSizeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        source.Should().Contain("CommitFontNameBoxText();");
        source.Should().Contain("CommitFontSizeBoxText();");
    }
}
