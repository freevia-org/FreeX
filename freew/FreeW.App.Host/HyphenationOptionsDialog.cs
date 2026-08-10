using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
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
    private readonly HyphenationOptionsDialogSession _session;
    private readonly CheckBox _autoBox;
    private readonly TextBox _zoneBox;
    private readonly TextBox _limitBox;
    private readonly CheckBox _hyphenateCapsBox;
    private HyphenationOptionsDialogResult? _result;

    private HyphenationOptionsDialog(Window? owner, PageSettings page)
    {
        var surface = HyphenationOptionsDialogPlanner.Surface;
        _session = new HyphenationOptionsDialogSession(page, CultureInfo.CurrentCulture);
        Owner = owner;
        Title = surface.Title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var state = _session.InitialState;
        _autoBox = new CheckBox { Content = surface.Field(HyphenationOptionsDialogField.Automatic).Label, IsChecked = state.AutoHyphenation };
        _hyphenateCapsBox = new CheckBox { Content = surface.Field(HyphenationOptionsDialogField.HyphenateCaps).Label, IsChecked = state.HyphenateCaps, Margin = new Thickness(0, 6, 0, 0) };
        _zoneBox = NumberBox(state.ZoneText);
        _limitBox = NumberBox(state.ConsecutiveLimitText);
        PageLayoutDialogSurfaceSemantics.Apply(this, surface);
        PageLayoutDialogSurfaceSemantics.Apply(_autoBox, surface.Field(HyphenationOptionsDialogField.Automatic));
        PageLayoutDialogSurfaceSemantics.Apply(_zoneBox, surface.Field(HyphenationOptionsDialogField.Zone));
        PageLayoutDialogSurfaceSemantics.Apply(_limitBox, surface.Field(HyphenationOptionsDialogField.ConsecutiveLimit));
        PageLayoutDialogSurfaceSemantics.Apply(_hyphenateCapsBox, surface.Field(HyphenationOptionsDialogField.HyphenateCaps));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_autoBox, 0);
        Grid.SetColumn(_autoBox, 0);
        Grid.SetColumnSpan(_autoBox, 2);
        grid.Children.Add(_autoBox);

        AddRow(grid, 1, surface.Field(HyphenationOptionsDialogField.Zone).Label, _zoneBox);
        AddRow(grid, 2, surface.Field(HyphenationOptionsDialogField.ConsecutiveLimit).Label, _limitBox);

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

    private static TextBox NumberBox(string text) => new()
    {
        Text = text,
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
        var input = new HyphenationOptionsDialogInput(
            _autoBox.IsChecked == true,
            _zoneBox.Text,
            _limitBox.Text,
            _hyphenateCapsBox.IsChecked == true);

        var acceptance = _session.PlanAcceptance(input);
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, acceptance.ValidationMessage);
            return;
        }

        _result = acceptance.Result;
        Close();
    }

    /// <summary>
    /// Show the dialog seeded with the current page hyphenation settings; returns the chosen settings, or
    /// null if cancelled.
    /// </summary>
    public static HyphenationOptionsDialogResult? Prompt(Window? owner, PageSettings page)
    {
        var dialog = new HyphenationOptionsDialog(owner, page);
        dialog.ShowDialog();
        return dialog._result;
    }
}
