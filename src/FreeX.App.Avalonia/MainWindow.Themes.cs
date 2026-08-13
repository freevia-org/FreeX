using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FreeX.App.Presentation.ThemeUI;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Page Layout ▸ Themes (parity gap: the buttons were no-ops). Picker of the three built-in themes
    // (Office / FreeX Colorful / Grayscale); applying one runs SetWorkbookThemeCommand (undo/redo).
    // Built-in theme recipes come from WorkbookThemeCatalog so Avalonia and WPF stay aligned.

    private async Task ShowThemesGalleryAsync()
    {
        WorkbookTheme? picked = null;
        var dialog = new Window
        {
            Title = UiText.Get("Common_Themes"),
            Width = 280,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };

        var panel = new StackPanel { Margin = new Thickness(14), Spacing = 6 };
        foreach (var option in WorkbookThemeCatalog.ThemePresets.Where(option => !option.IsCustomizeAction))
        {
            var local = option.CreateTheme();
            var button = new Button
            {
                Content = UiText.Get(option.LabelResourceKey),
                Width = 230,
                Padding = new Thickness(8, 6),
            };
            button.Click += (_, _) => { picked = local; dialog.Close(); };
            panel.Children.Add(button);
        }

        dialog.Content = panel;
        await dialog.ShowDialog(this);
        if (picked is not { } chosen)
            return;

        var plan = WorkbookThemeCommandPlanner.PlanApply(chosen);
        var result = _session.ExecuteReviewCommand(plan.Command);
        RefreshShell(result.Success
            ? UiText.Format("WTA_Theme_Applied", chosen.Name)
            : result.ErrorMessage ?? UiText.Get("WTA_Theme_ApplyFailed"));
    }

    // Page Layout ▸ Themes ▸ Theme Colors / Theme Fonts / Theme Effects child galleries.
    // The Core model carries a single WorkbookTheme; SetWorkbookThemeCommand applies the whole
    // theme (undo/redo). To represent "apply only this color/font/effect set" we derive a new theme
    // from the current workbook theme via WorkbookTheme.WithColor / WithFonts / WithEffects (keeping
    // the rest unchanged) and apply that through the shared WorkbookThemeWorkflow/catalog.

    private async Task ShowThemeColorsGalleryAsync()
    {
        var options = WorkbookThemeCatalog.ColorPresets
            .Where(option => !option.IsCustomizeAction)
            .ToArray();
        var labels = options.Select(option => UiText.Get(option.LabelResourceKey)).ToArray();
        var index = await ShowThemePartPickerAsync(UiText.Get("WTA_ThemeColors_Title"), labels);
        if (index is not { } chosen)
            return;

        var option = options[chosen];
        ApplyDerivedTheme(
            option.ApplyColors(_session.Workbook.Theme),
            UiText.Format("WTA_ThemeColors_Applied", UiText.Get(option.LabelResourceKey)));
    }

    private async Task ShowThemeFontsGalleryAsync()
    {
        var options = WorkbookThemeCatalog.FontPresets
            .Where(option => !option.IsCustomizeAction)
            .ToArray();
        var index = await ShowThemePartPickerAsync(
            UiText.Get("WTA_ThemeFonts_Title"),
            options.Select(option => UiText.Get(option.LabelResourceKey)).ToArray());
        if (index is not { } chosen)
            return;

        var option = options[chosen];
        ApplyDerivedTheme(
            option.ApplyFonts(_session.Workbook.Theme),
            UiText.Format("WTA_ThemeFonts_Applied", UiText.Get(option.LabelResourceKey)));
    }

    private async Task ShowThemeEffectsGalleryAsync()
    {
        var options = WorkbookThemeCatalog.EffectPresets
            .Where(option => !option.IsCustomizeAction)
            .ToArray();
        var index = await ShowThemePartPickerAsync(
            UiText.Get("WTA_ThemeEffects_Title"),
            options.Select(option => UiText.Get(option.LabelResourceKey)).ToArray());
        if (index is not { } chosen)
            return;

        var option = options[chosen];
        ApplyDerivedTheme(
            option.ApplyEffects(_session.Workbook.Theme),
            UiText.Format("WTA_ThemeEffects_Applied", UiText.Get(option.LabelResourceKey)));
    }

    private void ApplyDerivedTheme(WorkbookTheme theme, string successMessage)
    {
        var plan = WorkbookThemeCommandPlanner.PlanApply(theme);
        var result = _session.ExecuteReviewCommand(plan.Command);
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
