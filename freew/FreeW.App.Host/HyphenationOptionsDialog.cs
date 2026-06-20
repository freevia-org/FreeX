using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Hyphenation Options" dialog (Layout &gt; Page Setup &gt; Hyphenation &gt; Hyphenation Options…).
/// Captures the three document-level automatic-hyphenation settings that round-trip via word/settings.xml:
/// the "Automatically hyphenate document" toggle (w:autoHyphenation), the hyphenation zone
/// (w:hyphenationZone), "Limit consecutive hyphens to" (w:consecutiveHyphenLimit, 0 = no limit) and
/// "Hyphenate words in CAPS" (the inverse of w:doNotHyphenateCaps). Returns the chosen settings to apply to
/// <see cref="PageSettings"/>, or null if cancelled.
///
/// <para>
/// The zone is shown in points (matching FreeW's other page-setup dialogs, e.g. Columns); a zone of 0 means
/// Word's default zone (0.25"). The consecutive-hyphen limit accepts 0 ("No limit") or a positive count.
/// </para>
/// </summary>
internal sealed class HyphenationOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>The settings the dialog produces, mapped onto a <see cref="PageSettings"/> on apply.</summary>
    internal sealed record Result(bool AutoHyphenation, double ZonePt, int ConsecutiveLimit, bool HyphenateCaps);

    private readonly CheckBox _autoBox;
    private readonly TextBox _zoneBox;
    private readonly TextBox _limitBox;
    private readonly CheckBox _hyphenateCapsBox;
    private Result? _result;

    private HyphenationOptionsDialog(Window? owner, PageSettings page)
    {
        Owner = owner;
        Title = "Hyphenation";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _autoBox = new CheckBox { Content = "Automatically hyphenate document", IsChecked = page.AutoHyphenation };
        _hyphenateCapsBox = new CheckBox { Content = "Hyphenate words in CAPS", IsChecked = !page.DoNotHyphenateCaps, Margin = new Thickness(0, 6, 0, 0) };
        _zoneBox = NumberBox(page.HyphenationZonePt);
        _limitBox = NumberBox(page.ConsecutiveHyphenLimit);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_autoBox, 0);
        Grid.SetColumn(_autoBox, 0);
        Grid.SetColumnSpan(_autoBox, 2);
        grid.Children.Add(_autoBox);

        AddRow(grid, 1, "Hyphenation zone (pt):", _zoneBox);
        AddRow(grid, 2, "Limit consecutive hyphens to (0 = no limit):", _limitBox);

        Grid.SetRow(_hyphenateCapsBox, 3);
        Grid.SetColumn(_hyphenateCapsBox, 0);
        Grid.SetColumnSpan(_hyphenateCapsBox, 2);
        grid.Children.Add(_hyphenateCapsBox);

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 4);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_zoneBox);
    }

    private static TextBox NumberBox(double value) => new()
    {
        Text = value.ToString("0.##", CultureInfo.CurrentCulture),
        MinWidth = 90
    };

    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(field);
    }

    private void Accept()
    {
        if (!TryParseDouble(_zoneBox.Text, out var zone) || zone < 0
            || !TryParseDouble(_limitBox.Text, out var limitValue) || limitValue < 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter a non-negative hyphenation zone and a non-negative consecutive-hyphen limit (0 = no limit).");
            return;
        }

        _result = new Result(
            _autoBox.IsChecked == true,
            zone,
            (int)System.Math.Round(limitValue),
            _hyphenateCapsBox.IsChecked == true);
        Close();
    }

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    /// <summary>
    /// Show the dialog seeded with the current page hyphenation settings; returns the chosen settings, or
    /// null if cancelled.
    /// </summary>
    public static Result? Prompt(Window? owner, PageSettings page)
    {
        var dialog = new HyphenationOptionsDialog(owner, page);
        dialog.ShowDialog();
        return dialog._result;
    }
}
