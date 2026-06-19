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
            ? UiText.Format("WTA_Theme_Applied", chosen.Name)
            : result.ErrorMessage ?? UiText.Get("WTA_Theme_ApplyFailed"));
    }

    // Page Layout ▸ Themes ▸ Theme Colors / Theme Fonts / Theme Effects child galleries.
    // The Core model carries a single WorkbookTheme; SetWorkbookThemeCommand applies the whole
    // theme (undo/redo). To represent "apply only this color/font/effect set" we derive a new theme
    // from the current workbook theme via WorkbookTheme.WithColor / WithFonts / WithEffects (keeping
    // the rest unchanged) and apply that. This mirrors the WPF host's WorkbookThemeWorkflow.

    private static readonly (string ColorsLabel, (WorkbookThemeColorSlot Slot, CellColor Color)[] Colors)[] ThemeColorSets =
    {
        ("Office", System.Array.Empty<(WorkbookThemeColorSlot, CellColor)>()),
        ("FreeX Colorful", new[]
        {
            (WorkbookThemeColorSlot.Accent1, new CellColor(21, 96, 130)),
            (WorkbookThemeColorSlot.Accent2, new CellColor(233, 113, 50)),
            (WorkbookThemeColorSlot.Accent3, new CellColor(25, 107, 36)),
            (WorkbookThemeColorSlot.Accent4, new CellColor(15, 158, 213)),
            (WorkbookThemeColorSlot.Accent5, new CellColor(160, 43, 147)),
            (WorkbookThemeColorSlot.Accent6, new CellColor(78, 167, 46)),
        }),
        ("Grayscale", new[]
        {
            (WorkbookThemeColorSlot.Dark1, new CellColor(0, 0, 0)),
            (WorkbookThemeColorSlot.Light1, new CellColor(255, 255, 255)),
            (WorkbookThemeColorSlot.Dark2, new CellColor(64, 64, 64)),
            (WorkbookThemeColorSlot.Light2, new CellColor(230, 230, 230)),
            (WorkbookThemeColorSlot.Accent1, new CellColor(89, 89, 89)),
            (WorkbookThemeColorSlot.Accent2, new CellColor(127, 127, 127)),
            (WorkbookThemeColorSlot.Accent3, new CellColor(166, 166, 166)),
            (WorkbookThemeColorSlot.Accent4, new CellColor(191, 191, 191)),
            (WorkbookThemeColorSlot.Accent5, new CellColor(217, 217, 217)),
            (WorkbookThemeColorSlot.Accent6, new CellColor(242, 242, 242)),
        }),
    };

    private async Task ShowThemeColorsGalleryAsync()
    {
        var labels = ThemeColorSets.Select(set => set.ColorsLabel).ToArray();
        var index = await ShowThemePartPickerAsync(UiText.Get("WTA_ThemeColors_Title"), labels);
        if (index is not { } chosen)
            return;

        var (label, colors) = ThemeColorSets[chosen];
        var theme = _session.Workbook.Theme;
        // "Office" resets every slot to the Office palette; named sets override only their slots.
        if (colors.Length == 0)
        {
            foreach (var slot in System.Enum.GetValues<WorkbookThemeColorSlot>())
                theme = theme.WithColor(slot, WorkbookTheme.Office.GetColor(slot));
        }
        else
        {
            foreach (var (slot, color) in colors)
                theme = theme.WithColor(slot, color);
        }

        ApplyDerivedTheme(theme, UiText.Format("WTA_ThemeColors_Applied", label));
    }

    private async Task ShowThemeFontsGalleryAsync()
    {
        var sets = new (string Label, string Major, string Minor)[]
        {
            ("Office", WorkbookTheme.Office.MajorFontName, WorkbookTheme.Office.MinorFontName),
            ("Arial", "Arial", "Arial"),
            ("Times New Roman", "Times New Roman", "Times New Roman"),
        };
        var index = await ShowThemePartPickerAsync(UiText.Get("WTA_ThemeFonts_Title"), sets.Select(s => s.Label).ToArray());
        if (index is not { } chosen)
            return;

        var (label, major, minor) = sets[chosen];
        ApplyDerivedTheme(_session.Workbook.Theme.WithFonts(major, minor), UiText.Format("WTA_ThemeFonts_Applied", label));
    }

    private async Task ShowThemeEffectsGalleryAsync()
    {
        var sets = new (string Label, string Effects)[]
        {
            ("Office", WorkbookTheme.Office.EffectsName),
            ("Subtle", "Subtle"),
            ("Refined", "Refined"),
        };
        var index = await ShowThemePartPickerAsync(UiText.Get("WTA_ThemeEffects_Title"), sets.Select(s => s.Label).ToArray());
        if (index is not { } chosen)
            return;

        var (label, effects) = sets[chosen];
        ApplyDerivedTheme(_session.Workbook.Theme.WithEffects(effects), UiText.Format("WTA_ThemeEffects_Applied", label));
    }

    private void ApplyDerivedTheme(WorkbookTheme theme, string successMessage)
    {
        var result = _session.ExecuteReviewCommand(new SetWorkbookThemeCommand(theme));
        RefreshShell(result.Success ? successMessage : result.ErrorMessage ?? UiText.Get("WTA_Theme_ApplyFailed"));
    }

    private async Task<int?> ShowThemePartPickerAsync(string title, string[] labels)
    {
        int? picked = null;
        var dialog = new Window
        {
            Title = title,
            Width = 280,
            Height = 60 + (labels.Length * 44),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };

        var panel = new StackPanel { Margin = new Thickness(14), Spacing = 6 };
        for (var i = 0; i < labels.Length; i++)
        {
            var captured = i;
            var button = new Button { Content = labels[i], Width = 230, Padding = new Thickness(8, 6) };
            button.Click += (_, _) => { picked = captured; dialog.Close(); };
            panel.Children.Add(button);
        }

        dialog.Content = panel;
        await dialog.ShowDialog(this);
        return picked;
    }
}
