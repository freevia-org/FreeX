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

        var layout = CellShadingDialogPlanner.Layout;
        var panel = new StackPanel { Margin = new Thickness(layout.PanelMargin) };
        var palette = new WrapPanel { Width = layout.PaletteWidth };
        for (var index = 0; index < CellShadingDialogPlanner.Palette.Count; index++)
        {
            var choice = CellShadingDialogPlanner.Palette[index];
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
            AutomationProperties.SetAutomationId(swatch, CellShadingDialogPlanner.SwatchAutomationId(index));
            AutomationProperties.SetName(swatch, choice.Label);
            var selectedIndex = index;
            swatch.Click += (_, _) => Close(CellShadingDialogPlanner.SelectPaletteColor(selectedIndex));
            palette.Children.Add(swatch);
        }

        panel.Children.Add(palette);
        var clear = new Button
        {
            Content = CellShadingDialogPlanner.NoColorLabel,
            Margin = new Thickness(layout.ClearHorizontalMargin, layout.ClearTopMargin, layout.ClearHorizontalMargin, 0),
            Padding = new Thickness(layout.ClearHorizontalPadding, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Focusable = true,
        };
        AutomationProperties.SetAutomationId(clear, CellShadingDialogPlanner.NoColorAutomationId);
        clear.Click += (_, _) => Close(CellShadingDialogPlanner.SelectNoColor());
        panel.Children.Add(clear);
        Content = panel;

        Opened += (_, _) =>
        {
            if (palette.Children[0] is Button first)
                first.Focus();
        };

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
        var commit = CellShadingDialogPlanner.PlanCommit(result);
        if (!commit.ShouldApply)
            return;

        editor.SetCellShading(commit.Hex);
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var result = await new CellShadingDialog().ShowDialog<CellShadingDialogResult?>(owner);
        ApplyResult(editor, result);
    }
}
