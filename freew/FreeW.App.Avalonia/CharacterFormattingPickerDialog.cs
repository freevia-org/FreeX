using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

internal sealed partial class CharacterFormattingPickerDialog : FreeWDialogWindow
{
    private enum PickerKind { Border, Shading }

    private readonly PickerKind _kind;
    private readonly WrapPanel _palette;
    private readonly TextBlock? _prompt;
    private readonly Button _clear;

    private CharacterFormattingPickerDialog(PickerKind kind)
    {
        _kind = kind;
        Title = kind == PickerKind.Border
            ? CharacterFormattingPickerPlanner.BorderTitle
            : CharacterFormattingPickerPlanner.ShadingTitle;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var layout = CharacterFormattingPickerPlanner.Layout;
        var panel = new StackPanel { Margin = new Thickness(layout.PanelMargin) };
        _palette = new WrapPanel { Width = layout.PaletteWidth };
        var choices = kind == PickerKind.Border
            ? CharacterFormattingPickerPlanner.BorderPalette
            : CharacterFormattingPickerPlanner.ShadingPalette;

        _prompt = kind == PickerKind.Border
            ? new TextBlock
            {
                Text = CharacterFormattingPickerPlanner.BorderPrompt,
                Margin = new Thickness(0, 0, 0, 4),
            }
            : null;

        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index];
            var swatch = new Button
            {
                Width = layout.SwatchSize,
                Height = layout.SwatchSize,
                MinWidth = 0,
                MinHeight = 0,
                Margin = new Thickness(layout.SwatchMargin),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Focusable = true,
                Content = new Border
                {
                    Width = layout.SwatchSize,
                    Height = layout.SwatchSize,
                    Background = Brush.Parse(choice.Hex),
                    BorderBrush = Brush.Parse(layout.SwatchBorderHex),
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false,
                },
            };
            ToolTip.SetTip(swatch, choice.Hex);
            AutomationProperties.SetAutomationId(swatch, $"Character{(_kind == PickerKind.Border ? "Border" : "Shading")}Swatch{index}");
            AutomationProperties.SetName(swatch, choice.Label);
            var selectedIndex = index;
            swatch.Click += (_, _) => Select(selectedIndex);
            _palette.Children.Add(swatch);
        }

        if (_prompt is not null)
            panel.Children.Add(_prompt);
        panel.Children.Add(_palette);
        _clear = new Button
        {
            Content = kind == PickerKind.Border
                ? CharacterFormattingPickerPlanner.NoBorderLabel
                : CharacterFormattingPickerPlanner.NoColorLabel,
            Margin = new Thickness(layout.ClearHorizontalMargin, layout.ClearTopMargin, layout.ClearHorizontalMargin, 0),
            Padding = new Thickness(layout.ClearHorizontalPadding, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Focusable = true,
        };
        AutomationProperties.SetAutomationId(_clear, kind == PickerKind.Border
            ? "CharacterBorderNoBorderButton"
            : "CharacterShadingNoColorButton");
        _clear.Click += (_, _) => Close(kind == PickerKind.Border
            ? CharacterFormattingPickerPlanner.SelectNoBorder()
            : CharacterFormattingPickerPlanner.SelectNoColor());
        panel.Children.Add(_clear);
        Content = panel;

        Opened += (_, _) => _palette.Children.OfType<Button>().FirstOrDefault()?.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close(kind == PickerKind.Border
                ? CharacterFormattingPickerPlanner.CancelBorder()
                : CharacterFormattingPickerPlanner.CancelShading());
            e.Handled = true;
        };
    }

    private void Select(int index)
    {
        Close(_kind == PickerKind.Border
            ? CharacterFormattingPickerPlanner.SelectBorder(index)
            : CharacterFormattingPickerPlanner.SelectShading(index));
    }

    public static async Task ShowAndApplyBorderAsync(Window owner, DocumentView editor)
    {
        var result = await new CharacterFormattingPickerDialog(PickerKind.Border)
            .ShowDialog<CharacterBorderPickerResult?>(owner);
        if (result is { Accepted: true })
            editor.SetCharacterBorder(result.Border);
        editor.Focus();
    }

    public static async Task ShowAndApplyShadingAsync(Window owner, DocumentView editor)
    {
        var result = await new CharacterFormattingPickerDialog(PickerKind.Shading)
            .ShowDialog<CharacterShadingPickerResult?>(owner);
        if (result is { Accepted: true })
            editor.SetCharacterShading(result.Hex);
        editor.Focus();
    }

}
