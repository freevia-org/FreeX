using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.ThemeUI;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Task ShowThemesGalleryAsync() =>
        CustomizeWorkbookThemeAsync(WorkbookThemeEditorMode.Theme);

    private async Task CustomizeWorkbookThemeAsync(WorkbookThemeEditorMode mode)
    {
        var picked = await ShowWorkbookThemeDialogAsync(_session.Workbook.Theme, mode);
        if (picked is { } chosen)
            ApplyWorkbookTheme(chosen);
    }

    private async Task ShowThemeColorsGalleryAsync()
    {
        var options = WorkbookThemeCatalog.ColorPresets;
        var index = await ShowThemePartPickerAsync(
            UiText.Get("WTA_ThemeColors_Title"),
            options.Select(option => UiText.Get(option.LabelResourceKey)).ToArray());
        if (index is not { } chosen)
            return;

        var option = options[chosen];
        if (option.IsCustomizeAction)
            await CustomizeWorkbookThemeAsync(WorkbookThemeEditorMode.Colors);
        else
            ApplyWorkbookThemeColorPreset(option);
    }

    private async Task ShowThemeFontsGalleryAsync()
    {
        var options = WorkbookThemeCatalog.FontPresets;
        var index = await ShowThemePartPickerAsync(
            UiText.Get("WTA_ThemeFonts_Title"),
            options.Select(option => UiText.Get(option.LabelResourceKey)).ToArray());
        if (index is not { } chosen)
            return;

        var option = options[chosen];
        if (option.IsCustomizeAction)
            await ShowThemesGalleryAsync();
        else
            ApplyWorkbookThemeFontPreset(option);
    }

    private async Task ShowThemeEffectsGalleryAsync()
    {
        var options = WorkbookThemeCatalog.EffectPresets;
        var index = await ShowThemePartPickerAsync(
            UiText.Get("WTA_ThemeEffects_Title"),
            options.Select(option => UiText.Get(option.LabelResourceKey)).ToArray());
        if (index is not { } chosen)
            return;

        var option = options[chosen];
        if (option.IsCustomizeAction)
            await CustomizeWorkbookThemeAsync(WorkbookThemeEditorMode.Effects);
        else
            ApplyWorkbookThemeEffectPreset(option);
    }

    private void ApplyWorkbookThemeColorPreset(WorkbookThemeColorPresetOption option) =>
        ApplyWorkbookTheme(
            option.ApplyColors(_session.Workbook.Theme),
            UiText.Format("WTA_ThemeColors_Applied", UiText.Get(option.LabelResourceKey)));

    private void ApplyWorkbookThemeFontPreset(WorkbookThemeFontPresetOption option) =>
        ApplyWorkbookTheme(
            option.ApplyFonts(_session.Workbook.Theme),
            UiText.Format("WTA_ThemeFonts_Applied", UiText.Get(option.LabelResourceKey)));

    private void ApplyWorkbookThemeEffectPreset(WorkbookThemeEffectPresetOption option) =>
        ApplyWorkbookTheme(
            option.ApplyEffects(_session.Workbook.Theme),
            UiText.Format("WTA_ThemeEffects_Applied", UiText.Get(option.LabelResourceKey)));

    private void ApplyWorkbookTheme(WorkbookTheme theme, string? successMessage = null)
    {
        var plan = WorkbookThemeCommandPlanner.PlanApply(theme);
        var result = _session.ExecuteReviewCommand(plan.Command);
        // A theme command does not affect cell addresses, so refresh theme-colored tabs explicitly.
        if (result.Success)
            _session.RefreshSheetTabsForActiveSheet();
        RefreshShell(result.Success
            ? successMessage ?? UiText.Format("WTA_Theme_Applied", theme.Name)
            : result.ErrorMessage ?? UiText.Get("WTA_Theme_ApplyFailed"));
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
