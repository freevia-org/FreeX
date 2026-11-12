using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

/// <summary>
/// Font tab helpers for the Format Cells dialog (WPF <c>FormatCellsDialog.Font.cs</c>): the font
/// name list and the labels the shared <see cref="FormatCellsDialogPlanner"/> uses to classify the
/// font style and underline drop-down choices.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// WPF FontNamesWithFallback: the installed font families, de-duplicated and sorted, with the
    /// cell's current font put first when it is not among them. The curated names are merged in:
    /// workbooks authored elsewhere use them even when this machine does not have them installed,
    /// and they are the only entries when the platform font list cannot be read.
    /// </summary>
    private static string[] BuildFormatCellsFontNameChoices(
        IReadOnlyList<string> curatedFontNames,
        string? currentFontName)
    {
        var names = new List<string>(curatedFontNames);
        try
        {
            foreach (var family in FontManager.Current.SystemFonts)
            {
                var name = family.Name;
                // Hidden platform faces start with a dot on macOS and are not user-selectable.
                if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('.'))
                    names.Add(name);
            }
        }
        catch (Exception)
        {
            // No platform font manager (for example a headless host): keep the curated names.
        }

        var fonts = names
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(font => font, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var current = currentFontName?.Trim();
        if (!string.IsNullOrWhiteSpace(current)
            && !fonts.Contains(current, StringComparer.CurrentCultureIgnoreCase))
        {
            fonts.Insert(0, current);
        }

        return fonts.ToArray();
    }

    /// <summary>
    /// WPF FormatCellsDialog.xaml edits the font name and the size in an editable drop-down: one
    /// 24px field with a drop arrow whose list opens beneath it. The text box stays the value the
    /// dialog reads; the pick list moves into a light-dismiss popup under the field.
    /// </summary>
    private static Control CreateFormatCellsEditableDropDown(TextBox editor, ListBox choices)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(choices);

        const double arrowWidth = 18;
        editor.MinWidth = 0;
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.Padding = new Thickness(4, 1, arrowWidth + 2, 1);

        choices.MinWidth = 0;
        choices.Height = double.NaN;
        choices.MaxHeight = 220;
        choices.Margin = new Thickness(0);
        choices.BorderThickness = new Thickness(0);
        // The list is reached through the field (arrow, F4, Alt+Down), not by tabbing.
        KeyboardNavigation.SetIsTabStop(choices, false);

        var popupFrame = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            Child = choices,
        };
        var host = new Grid
        {
            Height = 24,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var popup = new Popup
        {
            PlacementTarget = host,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child = popupFrame,
        };
        var arrow = new Border
        {
            Width = arrowWidth,
            Margin = new Thickness(0, 1, 1, 1),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new global::Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 0 0 L 3.5 3.5 L 7 0"),
                Stroke = Brush(96, 96, 96),
                StrokeThickness = 1,
                Width = 8,
                Height = 5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        void OpenChoices()
        {
            popupFrame.MinWidth = host.Bounds.Width;
            popup.IsOpen = true;
            if (choices.SelectedItem is { } selected)
                choices.ScrollIntoView(selected);
            choices.Focus();
        }

        void CloseChoices(bool restoreFocus)
        {
            popup.IsOpen = false;
            if (restoreFocus)
                editor.Focus();
        }

        void StepChoice(int delta)
        {
            var count = choices.ItemCount;
            if (count == 0)
                return;

            var index = choices.SelectedIndex < 0
                ? (delta > 0 ? 0 : count - 1)
                : Math.Clamp(choices.SelectedIndex + delta, 0, count - 1);
            choices.SelectedIndex = index;
        }

        arrow.PointerPressed += (_, args) =>
        {
            if (popup.IsOpen)
                CloseChoices(restoreFocus: true);
            else
                OpenChoices();
            args.Handled = true;
        };
        editor.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                var opens = args.Key == Key.F4
                    || (args.Key == Key.Down && args.KeyModifiers == KeyModifiers.Alt);
                if (opens)
                {
                    OpenChoices();
                    args.Handled = true;
                }
                else if (args.KeyModifiers == KeyModifiers.None && args.Key is Key.Down or Key.Up)
                {
                    StepChoice(args.Key == Key.Down ? 1 : -1);
                    args.Handled = true;
                }
            },
            RoutingStrategies.Tunnel);
        choices.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Key is Key.Enter or Key.Escape or Key.F4)
                {
                    CloseChoices(restoreFocus: true);
                    args.Handled = true;
                }
            },
            RoutingStrategies.Tunnel);
        choices.AddHandler(
            InputElement.PointerReleasedEvent,
            (_, _) => CloseChoices(restoreFocus: true),
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        host.Children.Add(editor);
        host.Children.Add(arrow);
        host.Children.Add(popup);
        return host;
    }

    /// <summary>The same label set the WPF dialog hands the shared planner.</summary>
    private static FormatCellsDialogFontLabels CreateFormatCellsFontLabels() =>
        new(
            UiText.Get("FormatCells_FontStyleRegular"),
            UiText.Get("FormatCells_FontStyleItalic"),
            UiText.Get("FormatCells_FontStyleBold"),
            UiText.Get("FormatCells_FontStyleBoldItalic"),
            UiText.Get("FormatCells_UnderlineNone"),
            UiText.Get("FormatCells_UnderlineSingle"),
            UiText.Get("FormatCells_UnderlineDouble"),
            UiText.Get("FormatCells_UnderlineSingleAccounting"),
            UiText.Get("FormatCells_UnderlineDoubleAccounting"));
}
