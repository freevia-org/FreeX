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
    private readonly TableOfAuthoritiesDialogSession _session;
    private readonly ComboBox _category;
    private readonly CheckBox _passim;
    private readonly CheckBox _keepFormatting;
    private readonly ComboBox _leader;

    internal TableOfAuthoritiesDialog(ToaOptions options)
    {
        var metrics = TableOfAuthoritiesDialogPlanner.VisualMetrics;
        _session = TableOfAuthoritiesDialogPlanner.CreateSession(options);
        var state = _session.State;
        Title = TableOfAuthoritiesDialogPlanner.Title;
        Width = metrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _category = Combo(_session.Categories, state.CategoryIndex);
        _category.Margin = new Thickness(0, 0, 0, metrics.ComboBottomMargin);
        _passim = CheckBox(
            TableOfAuthoritiesDialogPlanner.UsePassimLabel,
            state.UsePassim,
            new Thickness(0, 0, 0, metrics.PassimBottomMargin));
        _keepFormatting = CheckBox(
            TableOfAuthoritiesDialogPlanner.KeepOriginalFormattingLabel,
            state.KeepOriginalFormatting,
            new Thickness(0, 0, 0, metrics.KeepFormattingBottomMargin));
        _leader = Combo(_session.TabLeaders, state.TabLeaderIndex);
        _leader.Margin = new Thickness(0, 0, 0, metrics.ComboBottomMargin);
        _category.SelectionChanged += (_, _) => _session.UpdateCategory(_category.SelectedIndex);
        _passim.IsCheckedChanged += (_, _) => _session.UpdateUsePassim(_passim.IsChecked is true);
        _keepFormatting.IsCheckedChanged += (_, _) =>
            _session.UpdateKeepOriginalFormatting(_keepFormatting.IsChecked is true);
        _leader.SelectionChanged += (_, _) => _session.UpdateTabLeader(_leader.SelectedIndex);

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

    internal ToaOptions? BuildResultForTest()
    {
        SynchronizeSession();
        return _session.PlanAcceptance().Options;
    }

    private void Accept()
    {
        SynchronizeSession();
        var acceptance = _session.PlanAcceptance();
        if (!acceptance.IsAccepted)
        {
            (acceptance.Validation?.Field == TableOfAuthoritiesDialogField.TabLeader
                ? _leader
                : _category).Focus();
            return;
        }

        Close(acceptance.Options);
    }

    private void SynchronizeSession()
    {
        _session.UpdateCategory(_category.SelectedIndex);
        _session.UpdateUsePassim(_passim.IsChecked is true);
        _session.UpdateKeepOriginalFormatting(_keepFormatting.IsChecked is true);
        _session.UpdateTabLeader(_leader.SelectedIndex);
    }

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
