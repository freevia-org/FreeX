using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "Table Styles" gallery for the Avalonia/macOS shell: a single-select list of the built-in
/// table styles (Light / Medium / Dark families) with the active table's current style pre-selected. The
/// catalog, the theme-resolved banding, and the current-selection lookup come from the portable
/// <see cref="TableStyleGalleryPlanner"/> so the gallery is single-sourced with the WPF host and reusable on
/// macOS. The chosen style round-trips through <see cref="ApplyStructuredTableStyleCommand"/> (the same command
/// the WPF host's gallery uses), persisting the style name and repainting the table's banding. Reached from the
/// Table Design ▸ Table Styles ribbon command (<c>tableDesign.tableStyles</c>).
/// </summary>
public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle TableStyleGalleryChromeStyle => new(FormulaBarFontFamily);

    /// <summary>
    /// Table Design ▸ Table Styles — opens the styles gallery for the active table and applies the chosen built-in
    /// style through the shared apply command. Reports an honest status when no table is active.
    /// </summary>
    private void OpenTableStyleGallery()
    {
        if (!TryGetActiveStructuredTable(out var table))
        {
            RefreshShell(UiText.Get("TableStyleGallery_NoTable"));
            return;
        }

        RunGuarded(() => OpenTableStyleGalleryDialogAsync(table));
    }

    private async Task OpenTableStyleGalleryDialogAsync(StructuredTableModel table)
    {
        if (_isOpening || _isSaving)
            return;

        var sheetId = _session.ActiveSheet.Id;
        var surface = TableStyleGalleryPlanner.GetSurface(_session.Workbook.Theme);

        var gallery = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            MinHeight = 280,
            ItemsSource = surface.Items.Select(item => item.Label).ToList(),
            SelectedIndex = TableStyleGalleryPlanner.FindSurfaceItemIndex(surface, table.StyleName),
        };
        AvaloniaCompactDialogChrome.ApplyListBox(gallery, TableStyleGalleryChromeStyle);
        AutomationProperties.SetAutomationId(gallery, "TableStyleGalleryList");
        AutomationProperties.SetName(gallery, UiText.Get("TableStyleGallery_GalleryName"));

        var dialog = new Window
        {
            Title = UiText.Get("TableStyleGallery_Title"),
            Width = 360,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "TableStyleGalleryDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "TableStyleGalleryOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "TableStyleGalleryCancelButton");
        AvaloniaCompactDialogChrome.ApplyButton(ok, TableStyleGalleryChromeStyle, 80, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancel, TableStyleGalleryChromeStyle, 80);
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) => dialog.Close(true);

        var content = new DockPanel { Margin = new Thickness(16) };

        var label = new TextBlock
        {
            Text = UiText.Get("TableStyleGallery_StyleLabel"),
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 0, 0, 6),
        };
        DockPanel.SetDock(label, Dock.Top);
        content.Children.Add(label);

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        content.Children.Add(buttonRow);

        content.Children.Add(gallery);
        dialog.Content = content;

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        var selectedIndex = gallery.SelectedIndex < 0 ? 0 : gallery.SelectedIndex;
        var item = TableStyleGalleryPlanner.GetSurfaceItem(surface, selectedIndex);
        var option = item.Option;

        var result = _session.ExecuteReviewCommand(
            TableDesignCommandPlanner.BuildApplyStyleCommand(sheetId, table, option));

        if (result.Success)
            RefreshShell(UiText.Format("TableStyleGallery_Applied", item.Label));
        else
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableStyleGallery_Failed"));
    }
}
