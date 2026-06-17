using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Page Layout ▸ Themes (parity gap: the buttons were no-ops). Picker of the three built-in themes
    // (Office / FreeX Colorful / Grayscale); applying one runs SetWorkbookThemeCommand (undo/redo).
    // The Colorful/Grayscale color recipes live in the WPF-only host, so they are replicated here
    // (Core WorkbookTheme.WithColor/WithName/WithFonts/WithEffects only) to stay platform-portable.

    private static WorkbookTheme BuildColorfulTheme() =>
        WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(21, 96, 130))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(233, 113, 50))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(25, 107, 36))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(15, 158, 213))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(160, 43, 147))
            .WithColor(WorkbookThemeColorSlot.Accent6, new CellColor(78, 167, 46))
            .WithName("FreeX Colorful")
            .WithFonts("Aptos Display", "Aptos")
            .WithEffects("Office");

    private static WorkbookTheme BuildGrayscaleTheme() =>
        WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Dark1, new CellColor(0, 0, 0))
            .WithColor(WorkbookThemeColorSlot.Light1, new CellColor(255, 255, 255))
            .WithColor(WorkbookThemeColorSlot.Dark2, new CellColor(64, 64, 64))
            .WithColor(WorkbookThemeColorSlot.Light2, new CellColor(230, 230, 230))
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(89, 89, 89))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(127, 127, 127))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(166, 166, 166))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(191, 191, 191))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(217, 217, 217))
            .WithColor(WorkbookThemeColorSlot.Accent6, new CellColor(242, 242, 242))
            .WithName("Grayscale")
            .WithFonts("Aptos Display", "Aptos")
            .WithEffects("Office");

    private async Task ShowThemesGalleryAsync()
    {
        WorkbookTheme? picked = null;
        var dialog = new Window
        {
            Title = "Themes",
            Width = 280,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };

        var panel = new StackPanel { Margin = new Thickness(14), Spacing = 6 };
        foreach (var (label, theme) in new (string, WorkbookTheme)[]
                 {
                     ("Office", WorkbookTheme.Office),
                     ("FreeX Colorful", BuildColorfulTheme()),
                     ("Grayscale", BuildGrayscaleTheme()),
                 })
        {
            var local = theme;
            var button = new Button { Content = label, Width = 230, Padding = new Thickness(8, 6) };
            button.Click += (_, _) => { picked = local; dialog.Close(); };
            panel.Children.Add(button);
        }

        dialog.Content = panel;
        await dialog.ShowDialog(this);
        if (picked is not { } chosen)
            return;

        var result = _session.ExecuteReviewCommand(new SetWorkbookThemeCommand(chosen));
        RefreshShell(result.Success
            ? $"Applied the {chosen.Name} theme"
            : result.ErrorMessage ?? "Could not apply the theme.");
    }
}
