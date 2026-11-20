using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "PivotTable Styles" gallery for the Avalonia/macOS shell: a single-select list of the
/// built-in style names (Light / Medium / Dark) with the active pivot's current style pre-selected. The
/// catalog, the default-style normalization, and the current-selection lookup come from the portable
/// <see cref="PivotStyleGalleryPlanner"/> so the gallery is single-sourced with the WPF host and reusable on
/// macOS. The chosen style round-trips through the same shared options plan the Design contextual-tab toggles
/// use, carrying only the style name and leaving every other
/// (totals / layout / cache / print) option untouched. Reached from the Design ▸ PivotTable Styles ribbon
/// command (<c>pivotDesign.pivotStyles</c>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Design ▸ PivotTable Styles — opens the styles gallery for the active pivot and applies the chosen
    /// built-in style through the shared options command. Reports an honest status when no pivot is active.
    /// </summary>
    private void OpenPivotStyleGallery()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        RunGuarded(() => OpenPivotStyleGalleryDialogAsync(pivot!));
    }

    private async Task OpenPivotStyleGalleryDialogAsync(PivotTableModel pivot)
    {
        if (_isOpening || _isSaving)
            return;

        var values = PivotStyleGalleryPlanner.Capture(pivot);
        var styleNames = PivotStyleGalleryPlanner.GetStyleNames(values.StyleName);

        var gallery = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            MinHeight = 260,
            ItemsSource = styleNames,
            SelectedIndex = PivotStyleGalleryPlanner.FindStyleIndex(styleNames, values.StyleName),
        };
        ApplyPivotListBoxChrome(gallery);
        AutomationProperties.SetAutomationId(gallery, "PivotStyleGalleryList");
        AutomationProperties.SetName(gallery, UiText.Get("PivotStyleGallery_GalleryName"));

        var dialog = new Window
        {
            // WPF PivotStyleGalleryDialog is 360x430 OUTER; Avalonia sizes are client sizes.
            Title = UiText.Get("PivotStyleGallery_PivotTableStyles"),
            Width = 360 - WpfNoResizeFrameWidth,
            MinWidth = 360 - WpfNoResizeFrameWidth,
            MaxWidth = 360 - WpfNoResizeFrameWidth,
            Height = 430 - WpfNoResizeClientFrameHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotStyleGalleryDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(ok, PivotDialogButtonMinWidth, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotStyleGalleryOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancel, PivotDialogButtonMinWidth);
        AutomationProperties.SetAutomationId(cancel, "PivotStyleGalleryCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) => dialog.Close(true);

        // A Grid (not a DockPanel) keeps the logical order label, list, buttons, so Tab order and the
        // fallback first-stop focus match WPF: the style list first, then OK and Cancel.
        var content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };

        var label = new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("PivotStyleGallery_PivotTableStyle")),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(label, 0);
        content.Children.Add(label);

        Grid.SetRow(gallery, 1);
        content.Children.Add(gallery);

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttonRow, 2);
        content.Children.Add(buttonRow);

        dialog.Content = content;
        ConfigurePivotDialogLifecycle(dialog, gallery);

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        var result = PivotStyleGalleryPlanner.CreateResult(gallery.SelectedItem?.ToString());
        var options = PivotOptionsPlanner.CaptureDesignValues(pivot) with { StyleName = result.StyleName };
        ApplyPivotApplicationPlan(
            PlanPivotDesignOptions(pivot, options),
            UiText.Format("PivotStyleGallery_Applied", result.StyleName));
    }
}
