using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class TableOfAuthoritiesDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            ComboBoxHeight = TableOfAuthoritiesDialogPlanner.VisualMetrics.ComboBoxHeight
                + TableOfAuthoritiesDialogPlanner.VisualMetrics.AvaloniaComboBoxHeightCompensation,
            ActionSpacing = TableOfAuthoritiesDialogPlanner.VisualMetrics.ActionSpacing,
            DefaultButtonBorderBrush = AvaloniaCompactDialogChrome.NeutralButtonBorderBrush,
        };
    private readonly IReadOnlyList<TableOfAuthoritiesCategoryChoice> _categories;
    private readonly IReadOnlyList<TableOfAuthoritiesTabLeaderChoice> _leaders;
    private readonly ComboBox _category;
    private readonly CheckBox _passim;
    private readonly CheckBox _keepFormatting;
    private readonly ComboBox _leader;

    internal TableOfAuthoritiesDialog(ToaOptions options)
    {
        var metrics = TableOfAuthoritiesDialogPlanner.VisualMetrics;
        var state = TableOfAuthoritiesDialogPlanner.BuildInitialState(options);
        _categories = TableOfAuthoritiesDialogPlanner.BuildCategoryChoices();
        _leaders = TableOfAuthoritiesDialogPlanner.BuildTabLeaderChoices();
        Title = TableOfAuthoritiesDialogPlanner.Title;
        Width = metrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _category = Combo(_categories,
            TableOfAuthoritiesDialogPlanner.SelectCategoryIndex(_categories, state.CategoryFilter));
        _category.Margin = new Thickness(0, 0, 0, metrics.ComboBottomMargin);
        _passim = CheckBox(
            TableOfAuthoritiesDialogPlanner.UsePassimLabel,
            state.UsePassim,
            new Thickness(0, 0, 0, metrics.PassimBottomMargin));
        _keepFormatting = CheckBox(
            TableOfAuthoritiesDialogPlanner.KeepOriginalFormattingLabel,
            state.KeepOriginalFormatting,
            new Thickness(0, 0, 0, metrics.KeepFormattingBottomMargin));
        _leader = Combo(_leaders,
            TableOfAuthoritiesDialogPlanner.SelectTabLeaderIndex(_leaders, state.TabLeader));
        _leader.Margin = new Thickness(0, 0, 0, metrics.ComboBottomMargin);

        var ok = Button("OK", true, false, Accept);
        var cancel = Button("Cancel", false, true, () => Close(null));
        Content = new StackPanel
        {
            Margin = new Thickness(
                metrics.OuterInset,
                metrics.OuterInset,
                metrics.OuterInset + metrics.AvaloniaOuterRightCompensation,
                metrics.OuterInset),
            Children =
            {
                Label(TableOfAuthoritiesDialogPlanner.CategoryLabel),
                _category,
                _passim,
                _keepFormatting,
                Label(TableOfAuthoritiesDialogPlanner.TabLeaderLabel),
                _leader,
                AvaloniaCompactDialogChrome.CreateActionRow(
                    [ok, cancel],
                    new Thickness(
                        0,
                        metrics.ActionTopMargin + metrics.AvaloniaActionTopCompensation,
                        0,
                        0),
                    Chrome),
            },
        };
        Opened += (_, _) =>
        {
            AvaloniaCompactDialogChrome.ApplyComboBox(_category, Chrome);
            AvaloniaCompactDialogChrome.ApplyComboBox(_leader, Chrome);
            // WPF's dialog resource keeps neutral buttons white with a small radius. The shared
            // Avalonia chrome owns the common metrics; this local pass preserves that authority
            // without changing the defaults used by unrelated dialogs.
            foreach (var button in new[] { ok, cancel })
            {
                button.Background = Brushes.White;
                button.BorderBrush = AvaloniaCompactDialogChrome.NeutralButtonBorderBrush;
                button.CornerRadius = new CornerRadius(3);
            }
        };
        Opened += (_, _) => _category.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Close(null);
            args.Handled = true;
        };
    }

    public static Task<ToaOptions?> ShowAsync(Window owner, ToaOptions? options = null) =>
        new TableOfAuthoritiesDialog(options ?? ToaOptions.Default).ShowDialog<ToaOptions?>(owner);

    internal ToaOptions BuildResultForTest() => TableOfAuthoritiesDialogPlanner.BuildOptions(
        new TableOfAuthoritiesDialogState(
            _passim.IsChecked == true,
            _keepFormatting.IsChecked == true,
            _category.SelectedIndex >= 0 ? _categories[_category.SelectedIndex].Category : null,
            _leader.SelectedIndex >= 0 ? _leaders[_leader.SelectedIndex].Leader : ToaTabLeader.Dots));

    private void Accept() => Close(BuildResultForTest());

    private static ComboBox Combo(IEnumerable<object> items, int selectedIndex)
    {
        var combo = new ComboBox { ItemsSource = items.ToArray(), SelectedIndex = selectedIndex };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, Chrome);
        return combo;
    }

    private static CheckBox CheckBox(string content, bool isChecked, Thickness margin)
    {
        var checkBox = new CheckBox { Content = content, IsChecked = isChecked, Margin = margin };
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, Chrome);
        return checkBox;
    }

    private static Button Button(string text, bool isDefault, bool isCancel, Action click)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            Chrome,
            TableOfAuthoritiesDialogPlanner.VisualMetrics.ActionButtonWidth,
            isDefault);
        button.Background = Brushes.White;
        button.CornerRadius = new CornerRadius(3);
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Thickness(
            0,
            0,
            0,
            TableOfAuthoritiesDialogPlanner.VisualMetrics.LabelBottomMargin)
    };
}
