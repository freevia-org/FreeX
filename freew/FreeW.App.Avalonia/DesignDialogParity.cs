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
public sealed partial class CustomizeThemeColorsDialog : FreeWDialogWindow
{
    private const double DialogWidth = 440;
    private const double LabelColumnWidth = 190;
    private const double ColorRowHeight = 29.4;
    private const double ActionButtonWidth = 72;

    private readonly DocumentTheme _current;
    private readonly TextBox[] _colorBoxes;
    private readonly TextBox _nameBox;
    private readonly TextBlock _status = new();
    private readonly DesignDialogText _text;

    public DocumentTheme? Result { get; private set; }

    public CustomizeThemeColorsDialog(DocumentTheme current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _current = current;
        _text = DesignDialogTextCatalog.Resolve(UiText.Get);
        var state = CustomizeThemeColorsDialogPlanner.BuildInitialState(current);
        _colorBoxes = state.ColorHexTexts.Select(text => MakeTextBox(text, 120)).ToArray();
        _nameBox = MakeTextBox(state.NameText, 200);

        Title = CustomizeThemeColorsDialogPlanner.Title;
        Width = DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(CustomizeThemeFontsDialogPlanner.DialogMargin) };
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
                ColorRowHeight,
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
            ColorRowHeight,
            new Thickness(0, 0, 8, 0));
        content.Children.Add(nameGrid);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, InsertDialogLayout.ChromeStyle, new Thickness(0, 8, 0, 0));
        _status.IsVisible = false;
        content.Children.Add(_status);
        content.Children.Add(CreateActionRow());
        Content = content;
    }

    private bool Accept(bool closeOnSuccess)
    {
        if (!CustomizeThemeColorsDialogPlanner.TryBuildResult(
                _current,
                new CustomizeThemeColorsDialogInput(_colorBoxes.Select(box => box.Text ?? string.Empty).ToArray(), _nameBox.Text),
                out var result,
                out var validation))
        {
            _status.Text = validation?.Message ?? _text.InvalidThemeColorsMessage;
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
        var ok = new Button { Content = UiText.Get("Common_OkText"), IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, InsertDialogLayout.ChromeStyle, ActionButtonWidth, isDefault: true);
        ok.Click += (_, _) => Accept(closeOnSuccess: true);

        var cancel = new Button { Content = UiText.Get("Common_CancelText"), IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, InsertDialogLayout.ChromeStyle, ActionButtonWidth);
        cancel.Click += (_, _) => Close();

        return AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0));
    }

    private static Grid CreateGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumnWidth) });
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
public sealed partial class CustomizeThemeFontsDialog : FreeWDialogWindow
{
    private readonly CustomizeThemeFontsDialogSession _session;
    private readonly ComboBox _heading;
    private readonly ComboBox _body;
    private readonly TextBox _name;
    private readonly TextBlock _status = new();

    public DocumentFontSet? Result { get; private set; }

    public CustomizeThemeFontsDialog(DocumentFontSet current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _session = CustomizeThemeFontsDialogPlanner.CreateSession(current);
        var state = _session.InitialState;
        _heading = MakeFontBox(state.HeadingFontText);
        _body = MakeFontBox(state.BodyFontText);
        _name = new TextBox { Text = state.NameText, MinWidth = CustomizeThemeFontsDialogPlanner.FieldMinWidth };
        AvaloniaCompactDialogChrome.ApplyTextBox(_name, InsertDialogLayout.ChromeStyle);

        Title = CustomizeThemeFontsDialogPlanner.Title;
        Width = CustomizeThemeFontsDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(CustomizeThemeFontsDialogPlanner.DialogMargin) };
        content.Children.Add(new TextBlock
        {
            Text = CustomizeThemeFontsDialogPlanner.Hint,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, CustomizeThemeFontsDialogPlanner.HintBottomMargin),
        });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CustomizeThemeFontsDialogPlanner.LabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rowLabelMargin = new Thickness(0, CustomizeThemeFontsDialogPlanner.RowMargin, CustomizeThemeFontsDialogPlanner.LabelRightMargin, CustomizeThemeFontsDialogPlanner.RowMargin);
        var rowFieldMargin = new Thickness(0, CustomizeThemeFontsDialogPlanner.RowMargin, 0, CustomizeThemeFontsDialogPlanner.RowMargin);
        InsertDialogLayout.AddLabeledRow(grid, 0, CustomizeThemeFontsDialogPlanner.HeadingFontLabel, _heading, labelMargin: rowLabelMargin, fieldMargin: rowFieldMargin);
        InsertDialogLayout.AddLabeledRow(grid, 1, CustomizeThemeFontsDialogPlanner.BodyFontLabel, _body, labelMargin: rowLabelMargin, fieldMargin: rowFieldMargin);
        var separator = new Border
        {
            Height = CustomizeThemeFontsDialogPlanner.SeparatorHeight,
            Background = AvaloniaCompactDialogChrome.DialogSeparatorBrush,
            Margin = new Thickness(0, CustomizeThemeFontsDialogPlanner.SeparatorTopMargin, 0, CustomizeThemeFontsDialogPlanner.SeparatorBottomMargin),
        };
        Grid.SetRow(separator, 2);
        Grid.SetColumnSpan(separator, 2);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(separator);
        InsertDialogLayout.AddLabeledRow(grid, 3, CustomizeThemeFontsDialogPlanner.NameLabel, _name, labelMargin: rowLabelMargin, fieldMargin: rowFieldMargin);
        grid.Margin = new Thickness(0, 0, 0, CustomizeThemeFontsDialogPlanner.DialogMargin);
        content.Children.Add(grid);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, InsertDialogLayout.ChromeStyle, new Thickness(0, 8, 0, 0));
        content.Children.Add(_status);
        content.Children.Add(CreateActionRow());
        Content = content;
    }

    private bool Accept(bool closeOnSuccess)
    {
        var acceptance = _session.PlanAcceptance(
            new CustomizeThemeFontsDialogInput(_heading.Text, _body.Text, _name.Text));
        if (!acceptance.IsAccepted)
        {
            _status.Text = acceptance.ErrorMessage;
            _status.IsVisible = true;
            (acceptance.FocusField == CustomizeThemeFontsDialogField.BodyFont ? _body : _heading).Focus();
            return false;
        }

        Result = acceptance.Result;
        _status.IsVisible = false;
        if (closeOnSuccess)
            Close();
        return true;
    }

    private StackPanel CreateActionRow()
    {
        var ok = AvaloniaCompactDialogChrome.CreateActionButton(
            "OK", () => Accept(closeOnSuccess: true), CustomizeThemeFontsDialogPlanner.ActionButtonWidth, isDefault: true);
        var cancel = AvaloniaCompactDialogChrome.CreateActionButton(
            "Cancel", Close, CustomizeThemeFontsDialogPlanner.ActionButtonWidth, isCancel: true);
        return AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(0, CustomizeThemeFontsDialogPlanner.ActionRowTopMargin, 0, 0));
    }

    private static ComboBox MakeFontBox(string value)
    {
        var combo = new ComboBox { IsEditable = true, Text = value, MinWidth = CustomizeThemeFontsDialogPlanner.FieldMinWidth };
        combo.ItemsSource = CustomizeThemeFontsDialogPlanner.CommonFonts;
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, InsertDialogLayout.ChromeStyle);
        return combo;
    }
}

/// <summary>Avalonia page-color picker matching WPF's palette, No Color, and More Colors flow.</summary>
public sealed partial class PageColorDialog : FreeWDialogWindow
{
    private readonly ComboBox _palette;
    private readonly TextBox _custom;
    private readonly TextBlock _status = new();

    public string? Result { get; private set; }
    public bool Accepted { get; private set; }

    public PageColorDialog(string? currentHex)
    {
        var text = DesignDialogTextCatalog.Resolve(UiText.Get);
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
        InsertDialogLayout.AddLabeledRow(grid, 0, text.PageColorLabel, _palette);
        InsertDialogLayout.AddLabeledRow(grid, 1, text.MoreColorsLabel, _custom);
        content.Children.Add(grid);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, InsertDialogLayout.ChromeStyle, new Thickness(0, 8, 0, 0));
        content.Children.Add(_status);
        var ok = InsertDialogLayout.MakeButton("OK", (_, _) => AcceptAndClose());
        var cancel = InsertDialogLayout.MakeButton("Cancel", (_, _) => Close());
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)));
        Content = content;
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
public sealed partial class ThemeEffectsDialog : FreeWDialogWindow
{
    private readonly ComboBox _effects;
    public DocumentEffectSet? Result { get; private set; }

    public ThemeEffectsDialog(string? currentName)
    {
        var text = DesignDialogTextCatalog.Resolve(UiText.Get);
        _effects = new ComboBox
        {
            ItemsSource = DocumentEffectSet.Catalog.Select(effect => effect.Name).ToArray(),
            SelectedIndex = Math.Max(0, DocumentEffectSet.Catalog
                .Select((effect, index) => (effect, index))
                .FirstOrDefault(pair => string.Equals(pair.effect.Name, currentName, StringComparison.OrdinalIgnoreCase)).index),
            MinWidth = 220,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(_effects, InsertDialogLayout.ChromeStyle);
        Title = text.EffectsTitle;
        Width = 330;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(14) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, text.EffectSetLabel, _effects);
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

}

/// <summary>Small modal selector for WPF's Design Style Sets gallery.</summary>
public sealed partial class StyleSetDialog : FreeWDialogWindow
{
    private readonly ComboBox _styleSets;
    public DocumentStyleSet? Result { get; private set; }

    public StyleSetDialog(string? currentName)
    {
        var text = DesignDialogTextCatalog.Resolve(UiText.Get);
        _styleSets = new ComboBox
        {
            ItemsSource = DocumentStyleSet.Catalog.Select(styleSet => styleSet.Name).ToArray(),
            SelectedIndex = Math.Max(0, DocumentStyleSet.Catalog
                .Select((styleSet, index) => (styleSet, index))
                .FirstOrDefault(pair => string.Equals(pair.styleSet.Name, currentName, StringComparison.OrdinalIgnoreCase)).index),
            MinWidth = 220,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(_styleSets, InsertDialogLayout.ChromeStyle);
        Title = text.StyleSetsTitle;
        Width = 330;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(14) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, text.StyleSetLabel, _styleSets);
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
