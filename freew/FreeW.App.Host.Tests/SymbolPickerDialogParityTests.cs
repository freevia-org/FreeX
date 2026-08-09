using System.IO;
using System.Windows;
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
            var grid = (UniformGrid)panel.Children[0];
            grid.Columns.Should().Be(FreeWSymbolPickerDialogPlanner.Columns);
            grid.Children.OfType<Button>().Select(button => button.Content)
                .Should().Equal(FreeWSymbolPickerDialogPlanner.Glyphs);
            grid.Children.OfType<Button>().Should().OnlyContain(button =>
                button.Width == FreeWSymbolPickerDialogPlanner.ButtonSize
                && button.Height == FreeWSymbolPickerDialogPlanner.ButtonSize
                && button.Margin == new Thickness(FreeWSymbolPickerDialogPlanner.ButtonMargin)
                && button.FontSize == FreeWSymbolPickerDialogPlanner.ButtonFontSize);

            var cancel = (Button)panel.Children[1];
            cancel.Content.Should().Be(FreeWSymbolPickerDialogPlanner.CancelText);
            cancel.MinWidth.Should().Be(FreeWSymbolPickerDialogPlanner.FooterButtonMinWidth);
            cancel.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
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

        source.Should().Contain("FreeWSymbolPickerDialogPlanner.Glyphs");
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
        TestWorkspaceFileLocator.Find(RepoFile);
}
