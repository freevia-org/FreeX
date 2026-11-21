using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Home > Styles > Cell Styles. Opens a gallery of the built-in cell-style presets plus the
    // workbook's own named styles; picking one applies it to the selection (undo/redo),
    // "New Cell Style..." captures the active cell's formatting as a named style, and "Modify..."
    // redefines an existing one from the active cell. Avalonia-shell-only.

    private async Task ShowCellStylesGalleryAsync()
    {
        CellStylePreset? pickedPreset = null;
        string? pickedCustom = null;
        string? modifyName = null;
        var createNew = false;

        var presetGrid = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 520 };
        var content = new StackPanel { Spacing = 8 };

        var dialog = new Window
        {
            Title = UiText.Get("MainWindow_TooltipTitle_CellStyles"),
            Width = 560,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var newButton = new Button
        {
            Content = UiText.Get("CellStyles_NewButton"),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            Padding = new Thickness(10, 5),
        };
        AutomationProperties.SetAutomationId(newButton, "CellStylesNewButton");
        newButton.Click += (_, _) => { createNew = true; dialog.Close(); };
        content.Children.Add(newButton);

        var customStyles = _session.Workbook.CustomCellStyles.ToList();
        if (customStyles.Count > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = UiText.Get("CellStyles_CustomHeader"),
                FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
            });
            var customPanel = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 520 };
            foreach (var custom in customStyles)
            {
                var name = custom.Name;
                var apply = new Button { Content = name, MinWidth = 120, Margin = new Thickness(3), Padding = new Thickness(6, 6) };
                AutomationProperties.SetAutomationId(apply, "CellStyleCustom_" + name);
                apply.Click += (_, _) => { pickedCustom = name; dialog.Close(); };
                var modify = new Button
                {
                    Content = UiText.Get("CellStyles_ModifyButton"),
                    Margin = new Thickness(0, 3, 6, 3),
                    Padding = new Thickness(6, 6),
                };
                AutomationProperties.SetAutomationId(modify, "CellStyleModify_" + name);
                modify.Click += (_, _) => { modifyName = name; dialog.Close(); };
                customPanel.Children.Add(apply);
                customPanel.Children.Add(modify);
            }

            content.Children.Add(customPanel);
        }

        foreach (CellStylePreset preset in Enum.GetValues<CellStylePreset>())
        {
            var local = preset;
            var button = new Button
            {
                Content = UiText.Get(CellStyleDiffPlanner.GetCellStylePresetLabelResourceKey(preset)),
                Width = 120,
                Margin = new Thickness(3),
                Padding = new Thickness(6, 6),
            };
            button.Click += (_, _) => { pickedPreset = local; dialog.Close(); };
            presetGrid.Children.Add(button);
        }

        content.Children.Add(presetGrid);
        dialog.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10),
            Content = content,
        };

        await dialog.ShowDialog(this);
        if (pickedPreset is { } preset2)
        {
            ApplyCellStylePreset(preset2);
        }
        else if (pickedCustom is not null)
        {
            ApplyCustomCellStyle(pickedCustom);
        }
        else if (createNew)
        {
            await ShowCustomCellStyleDialogAsync(null);
        }
        else if (modifyName is not null)
        {
            await ShowCustomCellStyleDialogAsync(modifyName);
        }
    }

    /// <summary>Excel's Style dialog: a name plus the groups of the active cell's formatting the style captures.</summary>
    private async Task ShowCustomCellStyleDialogAsync(string? existingName)
    {
        var existing = existingName is null
            ? null
            : _session.Workbook.CustomCellStyles.FirstOrDefault(s => string.Equals(s.Name, existingName, StringComparison.OrdinalIgnoreCase));
        var parts = existing?.Parts ?? CustomCellStyleParts.All;

        var nameBox = new TextBox { Text = existing?.Name ?? "", Width = 280, IsEnabled = existing is null };
        AutomationProperties.SetAutomationId(nameBox, "CellStyleNameBox");
        AutomationProperties.SetName(nameBox, UiText.Get("CellStyles_NameLabel"));

        var checks = new List<(CustomCellStyleParts Part, CheckBox Box)>();
        var includePanel = new StackPanel { Spacing = 4 };
        foreach (var (part, key) in new[]
        {
            (CustomCellStyleParts.NumberFormat, "CellStyles_PartNumber"),
            (CustomCellStyleParts.Alignment, "CellStyles_PartAlignment"),
            (CustomCellStyleParts.Font, "CellStyles_PartFont"),
            (CustomCellStyleParts.Border, "CellStyles_PartBorder"),
            (CustomCellStyleParts.Fill, "CellStyles_PartFill"),
            (CustomCellStyleParts.Protection, "CellStyles_PartProtection"),
        })
        {
            var box = new CheckBox { Content = UiText.Get(key), IsChecked = parts.HasFlag(part) };
            AutomationProperties.SetAutomationId(box, "CellStylePart_" + part);
            checks.Add((part, box));
            includePanel.Children.Add(box);
        }

        var ok = new Button { Content = UiText.Get("Options_OK"), MinWidth = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(ok, "CellStyleOkButton");
        var cancel = new Button { Content = UiText.Get("Options_Cancel"), MinWidth = 80, IsCancel = true };
        var accepted = false;

        var dialog = new Window
        {
            Title = UiText.Get("CellStyles_StyleDialogTitle"),
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = UiText.Get("CellStyles_NameLabel") },
                    nameBox,
                    new TextBlock
                    {
                        Text = UiText.Get("CellStyles_IncludeHeader"),
                        FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
                    },
                    includePanel,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { ok, cancel },
                    },
                },
            },
        };
        ok.Click += (_, _) => { accepted = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        if (!accepted)
            return;

        var chosen = checks
            .Where(c => c.Box.IsChecked == true)
            .Aggregate(CustomCellStyleParts.None, (acc, c) => acc | c.Part);
        DefineCustomCellStyle(existing?.Name ?? nameBox.Text ?? "", chosen);
    }

    private void DefineCustomCellStyle(string name, CustomCellStyleParts parts)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var result = _session.DefineCustomCellStyleFromActiveCell(name, parts);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("MainLoc_Ready"));
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("CellStyles_DefineFailed"));
            return;
        }

        RefreshShell(UiText.Format("CellStyles_SavedStatusFormat", name.Trim()));
    }

    private void ApplyCustomCellStyle(string name)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.ApplyCustomCellStyle(name);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("MainLoc_Ready"));
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("MainLoc_CellStyleFailed"));
            return;
        }

        RefreshShell(UiText.Format("CellStyles_AppliedStatusFormat", name, rangeReference));
    }

    /// <summary>
    /// Applies a built-in cell-style preset from the modal gallery. Keep this entry point on the same
    /// guarded path as the ribbon/native-menu commands: WPF refuses edits while opening/saving and
    /// commits a pending formula edit before applying a style.
    /// </summary>
    private void ApplyCellStylePreset(CellStylePreset preset)
        => ApplySelectedRangeCellStylePreset(preset);
}
