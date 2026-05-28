using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class WpfTextContentExtractorTests
{
    [Theory]
    [InlineData("_Publish PDF", "Publish PDF")]
    [InlineData("Save __As", "Save _As")]
    [InlineData("Save ___As", "Save _As")]
    [InlineData("____", "__")]
    [InlineData("", "")]
    public void NormalizeAccessText_RemovesMnemonicsAndKeepsEscapedUnderscores(string text, string expected)
    {
        WpfTextContentExtractor.NormalizeAccessText(text).Should().Be(expected);
    }

    [Fact]
    public void NormalizeAccessText_ReturnsSameStringWhenTextHasNoMnemonicMarker()
    {
        const string text = "Publish PDF";

        WpfTextContentExtractor.NormalizeAccessText(text)
            .Should()
            .BeSameAs(text);
    }

    [Fact]
    public void ExtractText_ReadsNestedTextBlockInlines()
    {
        StaTestRunner.Run(() =>
        {
            var textBlock = new TextBlock();
            textBlock.Inlines.Add(new Run("Revenue"));
            textBlock.Inlines.Add(new LineBreak());
            textBlock.Inlines.Add(new Span(new Run("Total")));
            textBlock.Inlines.Add(new InlineUIContainer(new AccessText { Text = "_OK" }));

            WpfTextContentExtractor.ExtractText(textBlock)
                .Should()
                .Be("Revenue\nTotalOK");
        });
    }

    [Fact]
    public void ExtractFlowDocumentText_TrimsTerminalDocumentBreak()
    {
        StaTestRunner.Run(() =>
        {
            var document = new FlowDocument(new Paragraph(new Run("Flow PDF Text")));

            WpfTextContentExtractor.ExtractFlowDocumentText(document)
                .Should()
                .Be("Flow PDF Text");
        });
    }

    [Fact]
    public void ExtractComboBoxSelectionText_UsesEditableTextBeforeSelectedItem()
    {
        StaTestRunner.Run(() =>
        {
            var comboBox = new ComboBox
            {
                IsEditable = true,
                Text = "Typed value",
                SelectedItem = "Selected value"
            };

            WpfTextContentExtractor.ExtractComboBoxSelectionText(comboBox)
                .Should()
                .Be("Typed value");
        });
    }

    [Fact]
    public void ExtractComboBoxSelectionText_ReadsSelectedElementTextWhenClosed()
    {
        StaTestRunner.Run(() =>
        {
            var selectedItem = new TextBlock(new Run("Element value"));
            var comboBox = new ComboBox
            {
                IsDropDownOpen = false
            };
            comboBox.Items.Add(selectedItem);
            comboBox.SelectedItem = selectedItem;

            WpfTextContentExtractor.ExtractComboBoxSelectionText(comboBox)
                .Should()
                .Be("Element value");
        });
    }

    [Fact]
    public void EnumerateVisibleItemElements_SkipsClosedComboBoxItems()
    {
        StaTestRunner.Run(() =>
        {
            var comboBox = new ComboBox { IsDropDownOpen = false };
            comboBox.Items.Add(new TextBlock(new Run("Hidden item")));

            WpfTextContentExtractor.EnumerateVisibleItemElements(comboBox)
                .Should()
                .BeEmpty();
        });
    }

    [Fact]
    public void ExtractHeaderedContentText_JoinsObjectHeaderAndContent()
    {
        StaTestRunner.Run(() =>
        {
            var control = new HeaderedContentControl
            {
                Header = "Header",
                Content = 123
            };

            WpfTextContentExtractor.ExtractHeaderedContentText(control)
                .Should()
                .Be("Header\n123");
        });
    }
}
