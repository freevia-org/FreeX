using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// A color choice surfaced by <see cref="FormatCellsColorPicker"/>. <see cref="Color"/> is the
/// resolved RGB value (or null for "no change"/"automatic"); <see cref="Clear"/> marks the
/// "no fill" sentinel. Kept as a top-level type so the picker control and the Format Cells
/// dialog (in <c>MainWindow</c>) share exactly one choice type — the dialog still reads
/// <c>(box.SelectedItem as FormatCellsColorChoice)?.Color</c>.
/// </summary>
internal sealed record FormatCellsColorChoice(string Label, CellColor? Color, bool Clear)
{
    public override string ToString() => Label;
}

/// <summary>
/// Reusable Avalonia color picker that brings the Format Cells color fields to parity with the
/// WPF baseline: a palette swatch grid (sourced from <see cref="CellColorPalettePlanner"/> — never
/// duplicated), a "Recent" row backed by the portable <see cref="RecentColorsStore"/>, a
/// "No color"/"Automatic"/"No fill" option, and a "More colors…" custom RGB entry.
///
/// The control derives from <see cref="Button"/> and shows the current selection as a swatch +
/// label. Clicking opens a <see cref="Flyout"/> with the palette. It exposes a
/// <see cref="SelectedItem"/> of type <see cref="FormatCellsColorChoice"/> so existing dialog
/// wiring (and its source-hygiene assertions) keeps working unchanged.
/// </summary>
internal sealed class FormatCellsColorPicker : Button
{
    private readonly RecentColorsStore _recentColors;
    private readonly Func<string, CellColor, Task<CellColor?>> _showMoreColorsAsync;
    private readonly FormatCellsColorChoice _noColorChoice;
    private readonly bool _includeClear;
    private readonly FormatCellsColorChoice? _clearChoice;
    private readonly string _moreColorsTitle;

    private readonly Border _previewSwatch;
    private readonly TextBlock _previewLabel;

    private FormatCellsColorChoice _selected;
    private bool _compactPickButton;

    public FormatCellsColorPicker(
        RecentColorsStore recentColors,
        Func<string, CellColor, Task<CellColor?>> showMoreColorsAsync,
        string noColorLabel,
        bool includeClear,
        string moreColorsTitle)
    {
        _recentColors = recentColors ?? throw new ArgumentNullException(nameof(recentColors));
        _showMoreColorsAsync = showMoreColorsAsync ?? throw new ArgumentNullException(nameof(showMoreColorsAsync));
        _moreColorsTitle = moreColorsTitle;
        _includeClear = includeClear;
        _noColorChoice = new FormatCellsColorChoice(noColorLabel, null, Clear: false);
        _clearChoice = includeClear ? new FormatCellsColorChoice(UiText.Get("FormatCells_NoFill"), null, Clear: true) : null;
        _selected = _noColorChoice;

        MinWidth = 180;
        HorizontalContentAlignment = AvaloniaHorizontalAlignment.Stretch;
        HorizontalAlignment = AvaloniaHorizontalAlignment.Left;

        _previewSwatch = new Border
        {
            Width = 20,
            Height = 14,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        _previewLabel = new TextBlock
        {
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _previewSwatch, _previewLabel },
        };

        Flyout = BuildFlyout();
        UpdatePreview();
    }

    /// <summary>Raised whenever the selection changes (swatch, recent, text choice, or a custom
    /// "More colors…" pick). Lets the Format Cells dialog drive live previews off the picker.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The selected choice. Typed as <see cref="object"/> so callers mirror the old
    /// ComboBox idiom <c>(picker.SelectedItem as FormatCellsColorChoice)?.Color</c>.</summary>
    public object? SelectedItem => _selected;

    public CellColor? SelectedColor => _selected.Color;

    /// <summary>Use the picker as the compact WPF-style Pick button beside an RGB field.</summary>
    public void ConfigureCompactPickButton()
    {
        _compactPickButton = true;
        Width = 54;
        MinWidth = 54;
        Height = 24;
        MinHeight = 24;
        Padding = new Thickness(6, 1);
        HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center;
        Background = new SolidColorBrush(Color.FromRgb(221, 221, 221));
        BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        BorderThickness = new Thickness(1);
        Content = UiText.Get("ColorPicker_Pick");
    }

    /// <summary>Select the palette/recent entry matching <paramref name="color"/>, or fall back to
    /// the "no change" choice. Mirrors the old <c>SelectFormatCellsColor</c> behavior.</summary>
    public void SelectColor(CellColor color)
    {
        SetSelected(new FormatCellsColorChoice(CellColorPalettePlanner.FormatHexColor(color), color, Clear: false));
    }

    /// <summary>Choose the explicit no-fill sentinel when this picker supports clearing.</summary>
    public void SelectClear()
    {
        if (_clearChoice is not { } clearChoice)
            throw new InvalidOperationException("This color picker does not support clearing.");

        SetSelected(clearChoice);
    }

    /// <summary>Return to the neutral no-change state without applying a color.</summary>
    public void SelectNoChange() => SetSelected(_noColorChoice);

    private void SetSelected(FormatCellsColorChoice choice)
    {
        _selected = choice;
        if (choice.Color is { } chosen)
            _recentColors.Remember(chosen);

        UpdatePreview();
        RebuildFlyoutRecentRow();
        Flyout?.Hide();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePreview()
    {
        if (_compactPickButton)
            return;

        _previewLabel.Text = _selected.Label;
        if (_selected.Color is { } color)
        {
            _previewSwatch.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            _previewSwatch.IsVisible = true;
        }
        else
        {
            _previewSwatch.Background = Brushes.Transparent;
            _previewSwatch.IsVisible = false;
        }
    }

    private Flyout? _flyout;
    private StackPanel? _flyoutRoot;
    private StackPanel? _recentRow;

    private Flyout BuildFlyout()
    {
        _flyoutRoot = new StackPanel
        {
            Spacing = 8,
            MaxWidth = 230,
        };

        // "No change" / "Automatic" entry (always present).
        _flyoutRoot.Children.Add(CreateTextChoiceButton(_noColorChoice, "FormatCellsColorPickerNoColorItem"));

        // "No fill" sentinel where applicable.
        if (_clearChoice is { } clearChoice)
            _flyoutRoot.Children.Add(CreateTextChoiceButton(clearChoice, "FormatCellsColorPickerNoFillItem"));

        _flyoutRoot.Children.Add(new TextBlock { Text = UiText.Get("ColorPicker_ThemeAndStandardColors") });
        _flyoutRoot.Children.Add(BuildSwatchGrid(CellColorPalettePlanner.BuildDefaultSwatches()));

        _recentRow = new StackPanel { Spacing = 4 };
        _flyoutRoot.Children.Add(_recentRow);
        RebuildFlyoutRecentRow();

        var moreColorsButton = new Button
        {
            Content = UiText.Get("ColorPicker_MoreColorsEllipsis"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(moreColorsButton, "FormatCellsColorPickerMoreColorsButton");
        moreColorsButton.Click += async (_, _) => await ShowMoreColorsAsync();
        _flyoutRoot.Children.Add(moreColorsButton);

        _flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = _flyoutRoot,
        };
        return _flyout;
    }

    private void RebuildFlyoutRecentRow()
    {
        if (_recentRow is null)
            return;

        _recentRow.Children.Clear();
        var recent = _recentColors.Swatches;
        if (recent.Count == 0)
            return;

        _recentRow.Children.Add(new TextBlock { Text = UiText.Get("ColorPicker_RecentColors") });
        _recentRow.Children.Add(BuildSwatchGrid(recent, "FormatCellsColorPickerRecentSwatch"));
    }

    private WrapPanel BuildSwatchGrid(
        IReadOnlyList<CellColorSwatch> swatches,
        string? automationIdPrefix = null)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            MaxWidth = 220,
        };

        var index = 0;
        foreach (var swatch in swatches)
        {
            var swatchColor = swatch.Color;
            var button = new Button
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(1),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(swatchColor.R, swatchColor.G, swatchColor.B)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
            };
            AutomationProperties.SetName(button, swatch.Hex);
            if (automationIdPrefix is { } prefix)
                AutomationProperties.SetAutomationId(button, $"{prefix}{index}");

            button.Click += (_, _) => SetSelected(new FormatCellsColorChoice(swatch.Hex, swatchColor, Clear: false));
            panel.Children.Add(button);
            index++;
        }

        return panel;
    }

    private Button CreateTextChoiceButton(FormatCellsColorChoice choice, string automationId)
    {
        var button = new Button
        {
            Content = choice.Label,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => SetSelected(choice);
        return button;
    }

    private async Task ShowMoreColorsAsync()
    {
        Flyout?.Hide();
        var initial = _selected.Color ?? new CellColor(0, 0, 0);
        var chosen = await _showMoreColorsAsync(_moreColorsTitle, initial);
        if (chosen is { } color)
            SetSelected(new FormatCellsColorChoice(CellColorPalettePlanner.FormatHexColor(color), color, Clear: false));
    }
}
