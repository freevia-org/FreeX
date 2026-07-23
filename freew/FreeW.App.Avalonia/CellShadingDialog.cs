using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>WPF-parity 6x2 cell shading palette used by Table Design &gt; Shading.</summary>
internal sealed class CellShadingDialog : FreeWDialogWindow
{
    public CellShadingDialog()
    {
        Title = CellShadingDialogPlanner.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(8) };
        var palette = new WrapPanel { Width = 6 * 26 };
        for (var index = 0; index < CellShadingDialogPlanner.Palette.Count; index++)
        {
            var choice = CellShadingDialogPlanner.Palette[index];
            var swatch = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Background = Brush.Parse(choice.Hex),
                BorderBrush = Brush.Parse("#808080"),
                BorderThickness = new Thickness(1),
            };
            ToolTip.SetTip(swatch, choice.Hex);
            var selectedIndex = index;
            swatch.Click += (_, _) => Close(CellShadingDialogPlanner.SelectPaletteColor(selectedIndex));
            palette.Children.Add(swatch);
        }

        panel.Children.Add(palette);
        var clear = new Button
        {
            Content = CellShadingDialogPlanner.NoColorLabel,
            Margin = new Thickness(2, 6, 2, 0),
            Padding = new Thickness(8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        clear.Click += (_, _) => Close(CellShadingDialogPlanner.SelectNoColor());
        panel.Children.Add(clear);
        Content = panel;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            Close(null);
            e.Handled = true;
        };
    }

    public static void ApplyResult(DocumentView editor, CellShadingDialogResult? result)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (result is not { Accepted: true })
            return;

        editor.SetCellShading(result.Hex);
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var result = await new CellShadingDialog().ShowDialog<CellShadingDialogResult?>(owner);
        ApplyResult(editor, result);
    }
}
