using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// A small modal picker showing a grid of glyphs: the curated common set (symbols, punctuation, currency,
/// Greek/math) or any Unicode subset of the shared symbol catalog chosen from the subset list.
/// Clicking a glyph closes the dialog and returns it; the caller inserts it at the caret as plain text.
/// Returns the chosen glyph, or null if the user cancels.
/// </summary>
internal sealed class SymbolPickerDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private string? _result;

    internal SymbolPickerDialog(Window? owner)
    {
        Owner = owner;
        Title = FreeWSymbolPickerDialogPlanner.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, FreeWSymbolPickerDialogPlanner.DialogAutomationId);

        var panel = new StackPanel { Margin = new Thickness(FreeWSymbolPickerDialogPlanner.OuterMargin) };
        var subsetChoice = new ComboBox
        {
            ItemsSource = FreeWSymbolPickerDialogPlanner.GetSubsetNames(),
            SelectedIndex = 0,
            Margin = new Thickness(FreeWSymbolPickerDialogPlanner.ButtonMargin, 0, FreeWSymbolPickerDialogPlanner.ButtonMargin, FreeWSymbolPickerDialogPlanner.FooterTopMargin),
        };
        AutomationProperties.SetAutomationId(subsetChoice, FreeWSymbolPickerDialogPlanner.SubsetAutomationId);
        AutomationProperties.SetName(subsetChoice, FreeWSymbolPickerDialogPlanner.SubsetAutomationName);
        panel.Children.Add(subsetChoice);

        var grid = new UniformGrid { Columns = FreeWSymbolPickerDialogPlanner.Columns };
        PopulateGlyphs(grid, FreeWSymbolPickerDialogPlanner.GetGlyphs(null));
        subsetChoice.SelectionChanged += (_, _) =>
            PopulateGlyphs(grid, FreeWSymbolPickerDialogPlanner.GetGlyphs(subsetChoice.SelectedItem as string));
        panel.Children.Add(new ScrollViewer
        {
            Content = grid,
            MaxHeight = FreeWSymbolPickerDialogPlanner.GridMaxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        var cancel = new Button
        {
            Content = FreeWSymbolPickerDialogPlanner.CancelText,
            IsCancel = true,
            MinWidth = FreeWSymbolPickerDialogPlanner.FooterButtonMinWidth,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(
                FreeWSymbolPickerDialogPlanner.ButtonMargin,
                FreeWSymbolPickerDialogPlanner.FooterTopMargin,
                FreeWSymbolPickerDialogPlanner.ButtonMargin,
                0),
            Padding = new Thickness(8, 2, 8, 2),
        };
        AutomationProperties.SetAutomationId(cancel, FreeWSymbolPickerDialogPlanner.CancelAutomationId);
        panel.Children.Add(cancel);

        Content = panel;
    }

    private void PopulateGlyphs(UniformGrid grid, IReadOnlyList<string> glyphs)
    {
        grid.Children.Clear();
        foreach (var glyph in glyphs)
        {
            var semantic = FreeWSymbolPickerDialogPlanner.BuildSemantic(glyph);
            var button = new Button
            {
                Content = glyph,
                Width = FreeWSymbolPickerDialogPlanner.ButtonSize,
                Height = FreeWSymbolPickerDialogPlanner.ButtonSize,
                FontSize = FreeWSymbolPickerDialogPlanner.ButtonFontSize,
                Margin = new Thickness(FreeWSymbolPickerDialogPlanner.ButtonMargin),
                ToolTip = semantic.CodePointLabel,
            };
            AutomationProperties.SetName(button, semantic.AutomationName);
            AutomationProperties.SetAutomationId(button, semantic.AutomationId);
            button.Click += (_, _) => { _result = glyph; DialogResult = true; };
            grid.Children.Add(button);
        }
    }

    /// <summary>Show the picker; returns the chosen glyph, or null if cancelled.</summary>
    public static string? Prompt(Window? owner)
    {
        var dialog = new SymbolPickerDialog(owner);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
