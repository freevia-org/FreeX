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
    private static readonly AvaloniaCompactDialogChromeStyle Chrome = AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly IReadOnlyList<TableOfAuthoritiesCategoryChoice> _categories;
    private readonly IReadOnlyList<TableOfAuthoritiesTabLeaderChoice> _leaders;
    private readonly ComboBox _category;
    private readonly CheckBox _passim;
    private readonly CheckBox _keepFormatting;
    private readonly ComboBox _leader;

    internal TableOfAuthoritiesDialog(ToaOptions options)
    {
        var state = TableOfAuthoritiesDialogPlanner.BuildInitialState(options);
        _categories = TableOfAuthoritiesDialogPlanner.BuildCategoryChoices();
        _leaders = TableOfAuthoritiesDialogPlanner.BuildTabLeaderChoices();
        Title = TableOfAuthoritiesDialogPlanner.Title;
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _category = Combo(_categories.Select(choice => choice.Label),
            TableOfAuthoritiesDialogPlanner.SelectCategoryIndex(_categories, state.CategoryFilter));
        _passim = new CheckBox { Content = TableOfAuthoritiesDialogPlanner.UsePassimLabel, IsChecked = state.UsePassim, Margin = new Thickness(0, 6, 0, 0) };
        _keepFormatting = new CheckBox { Content = TableOfAuthoritiesDialogPlanner.KeepOriginalFormattingLabel, IsChecked = state.KeepOriginalFormatting, Margin = new Thickness(0, 6, 0, 0) };
        _leader = Combo(_leaders.Select(choice => choice.Label),
            TableOfAuthoritiesDialogPlanner.SelectTabLeaderIndex(_leaders, state.TabLeader));

        var ok = Button("OK", true, false, Accept);
        var cancel = Button("Cancel", false, true, () => Close(null));
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = TableOfAuthoritiesDialogPlanner.CategoryLabel, Margin = new Thickness(0, 0, 0, 3) },
                _category,
                _passim,
                _keepFormatting,
                new TextBlock { Text = TableOfAuthoritiesDialogPlanner.TabLeaderLabel, Margin = new Thickness(0, 10, 0, 3) },
                _leader,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)),
            },
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

    private static ComboBox Combo(IEnumerable<string> items, int selectedIndex)
    {
        var combo = new ComboBox { ItemsSource = items.ToArray(), SelectedIndex = selectedIndex };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, Chrome);
        return combo;
    }

    private static Button Button(string text, bool isDefault, bool isCancel, Action click)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, Chrome, 72, isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}
