using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>Avalonia counterpart of WPF's Create New Theme Colors dialog.</summary>
public sealed class CustomizeThemeColorsDialog : FreeWDialogWindow
{
    internal const double WpfWidthForTests = 440;
    internal const double WpfLabelColumnWidthForTests = 190;
    internal const double WpfColorRowHeightForTests = 29.4;
    internal const double WpfButtonWidthForTests = 72;

    private readonly DocumentTheme _current;
    private readonly TextBox[] _colorBoxes;
    private readonly TextBox _nameBox;
    private readonly TextBlock _status = new();

    public DocumentTheme? Result { get; private set; }

    public CustomizeThemeColorsDialog(DocumentTheme current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _current = current;
        var state = CustomizeThemeColorsDialogPlanner.BuildInitialState(current);
        _colorBoxes = state.ColorHexTexts.Select(text => MakeTextBox(text, 120)).ToArray();
        _nameBox = MakeTextBox(state.NameText, 200);

        Title = CustomizeThemeColorsDialogPlanner.Title;
        Width = WpfWidthForTests;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(new TextBlock
        {
            Text = CustomizeThemeColorsDialogPlanner.Hint,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 6),
        });

        var grid = CreateGrid();
        for (var index = 0; index < _colorBoxes.Length; index++)
            InsertDialogLayout.AddLabeledRow(
                grid,
                index,
                CustomizeThemeColorsDialogPlanner.Slots[index].Label,
                _colorBoxes[index],
                WpfColorRowHeightForTests,
                new Thickness(0, 0, 8, 0));
        content.Children.Add(grid);
        content.Children.Add(new Border
        {
            Height = 1,
            Background = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 4),
        });
        var nameGrid = CreateGrid();
        InsertDialogLayout.AddLabeledRow(
            nameGrid,
            0,
            CustomizeThemeColorsDialogPlanner.NameLabel,
            _nameBox,
            WpfColorRowHeightForTests,
            new Thickness(0, 0, 8, 0));
        content.Children.Add(nameGrid);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, InsertDialogLayout.ChromeStyle, new Thickness(0, 8, 0, 0));
        _status.IsVisible = false;
        content.Children.Add(_status);
        content.Children.Add(CreateActionRow());
        Content = content;
    }

    internal bool AcceptForTests() => Accept(closeOnSuccess: false);

    private bool Accept(bool closeOnSuccess)
    {
        if (!CustomizeThemeColorsDialogPlanner.TryBuildResult(
                _current,
                new CustomizeThemeColorsDialogInput(_colorBoxes.Select(box => box.Text ?? string.Empty).ToArray(), _nameBox.Text),
                out var result,
                out var validation))
        {
            _status.Text = validation?.Message ?? "Enter valid theme colors.";
            _status.IsVisible = true;
            (_colorBoxes.ElementAtOrDefault(validation?.SlotIndex ?? 0) ?? _nameBox).Focus();
            return false;
        }

        Result = result;
        _status.IsVisible = false;
        if (closeOnSuccess)
            Close();
        return true;
    }

    private StackPanel CreateActionRow()
    {
        var ok = new Button { Content = "OK", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, InsertDialogLayout.ChromeStyle, WpfButtonWidthForTests, isDefault: true);
        ok.Click += (_, _) => Accept(closeOnSuccess: true);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, InsertDialogLayout.ChromeStyle, WpfButtonWidthForTests);
        cancel.Click += (_, _) => Close();

        return AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0));
    }

    private static Grid CreateGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WpfLabelColumnWidthForTests) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static TextBox MakeTextBox(string text, double minWidth)
    {
        var box = new TextBox { Text = text, MinWidth = minWidth };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, InsertDialogLayout.ChromeStyle);
        return box;
    }
}

/// <summary>Avalonia counterpart of WPF's Create New Theme Fonts dialog.</summary>
public sealed class CustomizeThemeFontsDialog : FreeWDialogWindow
{
    private readonly ComboBox _heading;
    private readonly ComboBox _body;
    private readonly TextBox _name;
    private readonly TextBlock _status = new();

    public DocumentFontSet? Result { get; private set; }

    public CustomizeThemeFontsDialog(DocumentFontSet current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var state = CustomizeThemeFontsDialogPlanner.BuildInitialState(current);
        _heading = MakeFontBox(state.HeadingFontText);
        _body = MakeFontBox(state.BodyFontText);
        _name = new TextBox { Text = state.NameText, MinWidth = 220 };
        AvaloniaCompactDialogChrome.ApplyTextBox(_name, InsertDialogLayout.ChromeStyle);

        Title = CustomizeThemeFontsDialogPlanner.Title;
        Width = 410;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(new TextBlock
        {
            Text = CustomizeThemeFontsDialogPlanner.Hint,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 10),
        });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, "Heading font:", _heading);
        InsertDialogLayout.AddLabeledRow(grid, 1, "Body font:", _body);
        InsertDialogLayout.AddLabeledRow(grid, 2, "Name:", _name);
        content.Children.Add(grid);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, InsertDialogLayout.ChromeStyle, new Thickness(0, 8, 0, 0));
        content.Children.Add(_status);
        content.Children.Add(CreateActionRow());
        Content = content;
    }

    internal bool AcceptForTests() => Accept(closeOnSuccess: false);

    private bool Accept(bool closeOnSuccess)
    {
        if (!CustomizeThemeFontsDialogPlanner.TryBuildResult(
                new CustomizeThemeFontsDialogInput(_heading.Text, _body.Text, _name.Text),
                out var result,
                out var validation))
        {
            _status.Text = validation?.Message ?? "Enter both font names.";
            _status.IsVisible = true;
            (validation?.Field == CustomizeThemeFontsDialogField.BodyFont ? _body : _heading).Focus();
            return false;
        }

        Result = result;
        _status.IsVisible = false;
        if (closeOnSuccess)
            Close();
        return true;
    }

    private StackPanel CreateActionRow()
    {
        var ok = InsertDialogLayout.MakeButton("OK", (_, _) => Accept(closeOnSuccess: true));
        var cancel = InsertDialogLayout.MakeButton("Cancel", (_, _) => Close());
        return AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0));
    }

    private static ComboBox MakeFontBox(string value)
    {
        var combo = new ComboBox { IsEditable = true, Text = value, MinWidth = 220 };
        combo.ItemsSource = CustomizeThemeFontsDialogPlanner.CommonFonts;
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, InsertDialogLayout.ChromeStyle);
        return combo;
    }
}

/// <summary>Avalonia page-color picker matching WPF's palette, No Color, and More Colors flow.</summary>
public sealed class PageColorDialog : FreeWDialogWindow
{
    private readonly ComboBox _palette;
    private readonly TextBox _custom;
    private readonly TextBlock _status = new();

    public string? Result { get; private set; }
    public bool Accepted { get; private set; }

    public PageColorDialog(string? currentHex)
    {
        var state = PageColorDialogPlanner.BuildInitialState(currentHex);
        _palette = new ComboBox { ItemsSource = PageColorDialogPlanner.Palette.Select(item => item.Label).ToArray(), SelectedIndex = state.SelectedPaletteIndex, MinWidth = 220 };
        _custom = new TextBox { Text = state.CustomColorText, MinWidth = 220 };
        AvaloniaCompactDialogChrome.ApplyComboBox(_palette, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_custom, InsertDialogLayout.ChromeStyle);

        Title = PageColorDialogPlanner.Title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(14) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, "Color:", _palette);
        InsertDialogLayout.AddLabeledRow(grid, 1, "More Colors:", _custom);
        content.Children.Add(grid);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, InsertDialogLayout.ChromeStyle, new Thickness(0, 8, 0, 0));
        content.Children.Add(_status);
        var ok = InsertDialogLayout.MakeButton("OK", (_, _) => AcceptAndClose());
        var cancel = InsertDialogLayout.MakeButton("Cancel", (_, _) => Close());
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)));
        Content = content;
    }

    internal bool AcceptForTests() => TryAccept();

    internal void SelectCustomColorForTests(string value)
    {
        _palette.SelectedIndex = -1;
        _custom.Text = value;
    }

    private void AcceptAndClose()
    {
        if (TryAccept())
            Close();
    }

    private bool TryAccept()
    {
        if (!PageColorDialogPlanner.TryBuildResult(
                new PageColorDialogInput(_palette.SelectedIndex, _custom.Text),
                out var result,
                out var validation))
        {
            _status.Text = validation?.Message ?? PageColorDialogPlanner.CustomColorValidationMessage;
            _status.IsVisible = true;
            _custom.Focus();
            return false;
        }

        Result = result;
        Accepted = true;
        _status.IsVisible = false;
        return true;
    }
}

/// <summary>Small modal selector for the same Effects catalog exposed by WPF's Design gallery.</summary>
public sealed class ThemeEffectsDialog : FreeWDialogWindow
{
    private readonly ComboBox _effects;
    public DocumentEffectSet? Result { get; private set; }

    public ThemeEffectsDialog(string? currentName)
    {
        _effects = new ComboBox
        {
            ItemsSource = DocumentEffectSet.Catalog.Select(effect => effect.Name).ToArray(),
            SelectedIndex = Math.Max(0, DocumentEffectSet.Catalog
                .Select((effect, index) => (effect, index))
                .FirstOrDefault(pair => string.Equals(pair.effect.Name, currentName, StringComparison.OrdinalIgnoreCase)).index),
            MinWidth = 220,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(_effects, InsertDialogLayout.ChromeStyle);
        Title = "Effects";
        Width = 330;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(14) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, "Effect set:", _effects);
        content.Children.Add(grid);
        var ok = InsertDialogLayout.MakeButton("OK", (_, _) =>
        {
            Result = DocumentEffectSet.Catalog[Math.Clamp(_effects.SelectedIndex, 0, DocumentEffectSet.Catalog.Count - 1)];
            Close();
        });
        var cancel = InsertDialogLayout.MakeButton("Cancel", (_, _) => Close());
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)));
        Content = content;
    }

    internal bool AcceptForTests()
    {
        Result = DocumentEffectSet.Catalog[Math.Clamp(_effects.SelectedIndex, 0, DocumentEffectSet.Catalog.Count - 1)];
        return true;
    }
}

/// <summary>Small modal selector for WPF's Design Style Sets gallery.</summary>
public sealed class StyleSetDialog : FreeWDialogWindow
{
    private readonly ComboBox _styleSets;
    public DocumentStyleSet? Result { get; private set; }

    public StyleSetDialog(string? currentName)
    {
        _styleSets = new ComboBox
        {
            ItemsSource = DocumentStyleSet.Catalog.Select(styleSet => styleSet.Name).ToArray(),
            SelectedIndex = Math.Max(0, DocumentStyleSet.Catalog
                .Select((styleSet, index) => (styleSet, index))
                .FirstOrDefault(pair => string.Equals(pair.styleSet.Name, currentName, StringComparison.OrdinalIgnoreCase)).index),
            MinWidth = 220,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(_styleSets, InsertDialogLayout.ChromeStyle);
        Title = "Style Sets";
        Width = 330;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(14) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, "Style set:", _styleSets);
        content.Children.Add(grid);
        var ok = InsertDialogLayout.MakeButton("OK", (_, _) =>
        {
            Result = DocumentStyleSet.Catalog[Math.Clamp(_styleSets.SelectedIndex, 0, DocumentStyleSet.Catalog.Count - 1)];
            Close();
        });
        var cancel = InsertDialogLayout.MakeButton("Cancel", (_, _) => Close());
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)));
        Content = content;
    }

    internal bool AcceptForTests()
    {
        Result = DocumentStyleSet.Catalog[Math.Clamp(_styleSets.SelectedIndex, 0, DocumentStyleSet.Catalog.Count - 1)];
        return true;
    }
}

/// <summary>Lifecycle-only confirmation window for a future shell callback, matching the WPF default action wording.</summary>
public sealed class SetAsDefaultConfirmationDialog : FreeWDialogWindow
{
    public bool Confirmed { get; private set; }

    public SetAsDefaultConfirmationDialog()
    {
        var state = SetAsDefaultConfirmationPlanner.BuildState();
        Title = state.Title;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = state.Message, TextWrapping = TextWrapping.Wrap });
        var yes = InsertDialogLayout.MakeButton(state.ConfirmLabel, (_, _) => { Confirmed = true; Close(); });
        var no = InsertDialogLayout.MakeButton(state.CancelLabel, (_, _) => Close());
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([yes, no], new Thickness(0, 14, 0, 0)));
        Content = content;
    }
}
