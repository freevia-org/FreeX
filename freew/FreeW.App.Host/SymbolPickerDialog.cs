using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// A tiny modal picker showing a grid of common glyphs (symbols, punctuation, currency, Greek/math).
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
        var grid = new UniformGrid { Columns = FreeWSymbolPickerDialogPlanner.Columns };
        foreach (var glyph in FreeWSymbolPickerDialogPlanner.Glyphs)
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
        panel.Children.Add(grid);

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

    /// <summary>Show the picker; returns the chosen glyph, or null if cancelled.</summary>
    public static string? Prompt(Window? owner)
    {
        var dialog = new SymbolPickerDialog(owner);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
