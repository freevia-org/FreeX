using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host.Tests;

public sealed class SymbolPickerDialogParityTests
{
    [StaFact]
    public void Dialog_UsesSharedCatalogAndWpfTileMetrics()
    {
        var dialog = new SymbolPickerDialog(null);
        try
        {
            var panel = (StackPanel)dialog.Content;
            var subsetChoice = (ComboBox)panel.Children[0];
            subsetChoice.ItemsSource.Cast<string>().Should().Equal(FreeWSymbolPickerDialogPlanner.GetSubsetNames());
            AutomationProperties.GetAutomationId(subsetChoice).Should().Be(FreeWSymbolPickerDialogPlanner.SubsetAutomationId);
            var scroller = (ScrollViewer)panel.Children[1];
            scroller.MaxHeight.Should().Be(FreeWSymbolPickerDialogPlanner.GridMaxHeight);
            var grid = (UniformGrid)scroller.Content;
            grid.Columns.Should().Be(FreeWSymbolPickerDialogPlanner.Columns);
            grid.Children.OfType<Button>().Select(button => button.Content)
                .Should().Equal(FreeWSymbolPickerDialogPlanner.Glyphs);
            grid.Children.OfType<Button>().Should().OnlyContain(button =>
                button.Width == FreeWSymbolPickerDialogPlanner.ButtonSize
                && button.Height == FreeWSymbolPickerDialogPlanner.ButtonSize
                && button.Margin == new Thickness(FreeWSymbolPickerDialogPlanner.ButtonMargin)
                && button.FontSize == FreeWSymbolPickerDialogPlanner.ButtonFontSize);
            grid.Children.OfType<Button>().Select(AutomationProperties.GetAutomationId)
                .Should().Equal(FreeWSymbolPickerDialogPlanner.Glyphs
                    .Select(glyph => FreeWSymbolPickerDialogPlanner.BuildSemantic(glyph).AutomationId));
            grid.Children.OfType<Button>().Select(AutomationProperties.GetName)
                .Should().Equal(FreeWSymbolPickerDialogPlanner.Glyphs);

            var cancel = (Button)panel.Children[2];
            cancel.Content.Should().Be(FreeWSymbolPickerDialogPlanner.CancelText);
            cancel.MinWidth.Should().Be(FreeWSymbolPickerDialogPlanner.FooterButtonMinWidth);
            cancel.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
            AutomationProperties.GetAutomationId(cancel).Should()
                .Be(FreeWSymbolPickerDialogPlanner.CancelAutomationId);
        }
        finally
        {
            dialog.Close();
        }
    }

    [StaFact]
    public void Dialog_SubsetSelectionSwapsTheGlyphGridToTheSharedCatalogSubset()
    {
        var dialog = new SymbolPickerDialog(null);
        try
        {
            var panel = (StackPanel)dialog.Content;
            var subsetChoice = (ComboBox)panel.Children[0];
            var grid = (UniformGrid)((ScrollViewer)panel.Children[1]).Content;
            subsetChoice.SelectedItem = "Currency Symbols";

            var shown = grid.Children.OfType<Button>().Select(button => (string)button.Content).ToArray();
            shown.Should().Equal(FreeWSymbolPickerDialogPlanner.GetGlyphs("Currency Symbols"));
            shown.Should().Contain("€").And.HaveCountGreaterThan(5);
            grid.Children.OfType<Button>().Select(AutomationProperties.GetAutomationId)
                .Should().Equal(shown.Select(glyph => FreeWSymbolPickerDialogPlanner.BuildSemantic(glyph).AutomationId));
        }
        finally
        {
            dialog.Close();
        }
    }

    [Fact]
    public void Dialog_SourceKeepsExplicitSelectionAndCancelResults()
    {
        var source = File.ReadAllText(RepoFile("freew", "FreeW.App.Host", "SymbolPickerDialog.cs"));

        source.Should().Contain("FreeWSymbolPickerDialogPlanner.GetGlyphs(");
        source.Should().Contain("FreeWSymbolPickerDialogPlanner.GetSubsetNames()");
        source.Should().Contain("button.Click += (_, _) => { _result = glyph; DialogResult = true; }");
        source.Should().Contain("IsCancel = true");
        source.Should().Contain("dialog.ShowDialog() == true ? dialog._result : null");
        source.Should().NotContain("private static readonly string[] Glyphs");
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value)
            yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in FindVisualChildren<T>(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
