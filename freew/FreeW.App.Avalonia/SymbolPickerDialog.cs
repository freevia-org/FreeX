using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>Compact modal glyph picker matching FreeW's WPF Symbol dialog.</summary>
internal sealed class SymbolPickerDialog : Window
{
    private readonly List<Button> _glyphButtons = [];

    public string? Result { get; private set; }

    public SymbolPickerDialog()
    {
        Title = FreeWSymbolPickerDialogPlanner.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "SymbolPickerDialog");

        var panel = new StackPanel { Margin = new Thickness(8) };
        var grid = new UniformGrid { Columns = FreeWSymbolPickerDialogPlanner.Columns };
        foreach (var glyph in FreeWSymbolPickerDialogPlanner.Glyphs)
        {
            var button = new Button
            {
                Content = glyph,
                Width = 36,
                Height = 36,
                FontSize = 18,
                Margin = new Thickness(2),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            var codePoint = FreeWSymbolPickerDialogPlanner.BuildCodePointLabel(glyph);
            ToolTip.SetTip(button, codePoint);
            AutomationProperties.SetName(button, $"{glyph} {codePoint}");
            AutomationProperties.SetAutomationId(button, $"SymbolPicker{codePoint[2..]}Button");
            button.Click += (_, _) => SelectGlyph(glyph, close: true);
            _glyphButtons.Add(button);
            grid.Children.Add(button);
        }
        panel.Children.Add(grid);

        var cancel = new Button
        {
            Content = FreeWSymbolPickerDialogPlanner.CancelText,
            IsCancel = true,
            MinWidth = 72,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(2, 8, 2, 0),
            Padding = new Thickness(8, 2),
        };
        AutomationProperties.SetAutomationId(cancel, "SymbolPickerCancelButton");
        cancel.Click += (_, _) => Close();
        panel.Children.Add(cancel);

        Content = panel;
        Opened += (_, _) => _glyphButtons[0].Focus();
    }

    internal IReadOnlyList<Button> GlyphButtonsForTest => _glyphButtons;

    internal string? SelectGlyphForTest(string glyph)
    {
        SelectGlyph(glyph, close: false);
        return Result;
    }

    private void SelectGlyph(string glyph, bool close)
    {
        if (!FreeWSymbolPickerDialogPlanner.Glyphs.Contains(glyph, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(glyph));

        Result = glyph;
        if (close)
            Close();
    }
}
