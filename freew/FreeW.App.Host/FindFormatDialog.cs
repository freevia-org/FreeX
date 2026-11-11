using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Thin WPF projection of Find and Replace's Format criteria (font, size, colour, highlight,
/// bold/italic/underline and paragraph style). Parsing and validation live in
/// <see cref="FindReplaceDialogPlanner.TryBuildFormat"/>. Each toggle is tri-state: checked, cleared,
/// or indeterminate for "not part of the criteria".
/// </summary>
internal sealed partial class FindFormatDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly FindReplaceDialogText _text;
    private readonly ComboBox _font = new() { IsEditable = true, MinWidth = 200 };
    private readonly TextBox _size = new() { Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _color = new() { Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _highlightColor = new() { Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _style = new() { MinWidth = 200 };
    private readonly CheckBox _bold = NewToggle();
    private readonly CheckBox _italic = NewToggle();
    private readonly CheckBox _underline = NewToggle();
    private readonly CheckBox _highlight = NewToggle();
    private readonly TextBlock _status = new()
    {
        Foreground = Brushes.Red,
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 6, 0, 0),
    };

    // Kept parameterless so tooling can construct the production surface.
    internal FindFormatDialog()
        : this(FindReplaceDialogFieldKind.Find, null, null, new FindReplaceDialogText(UiText.Get))
    {
    }

    internal FindFormatDialog(
        FindReplaceDialogFieldKind field,
        FindFormat? current,
        TextDocument? document,
        FindReplaceDialogText text)
    {
        _text = text;
        Title = text.Get(field == FindReplaceDialogFieldKind.Find
            ? FindReplaceTextId.FormatDialogFindTitle
            : FindReplaceTextId.FormatDialogReplaceTitle);
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var any = text.Get(FindReplaceTextId.FormatDialogAnyValue);
        foreach (var family in Fonts.SystemFontFamilies.Select(family => family.Source).OrderBy(name => name))
            _font.Items.Add(family);

        _style.Items.Add(new ComboBoxItem { Content = any, Tag = null });
        if (document is not null)
        {
            foreach (var (id, name) in FindReplaceDialogPlanner.BuildFormatStyleChoices(document))
                _style.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        }

        _bold.Content = text.Get(FindReplaceTextId.FormatDialogBold);
        _italic.Content = text.Get(FindReplaceTextId.FormatDialogItalic);
        _underline.Content = text.Get(FindReplaceTextId.FormatDialogUnderline);
        _highlight.Content = text.Get(FindReplaceTextId.FormatDialogHighlight);

        AutomationProperties.SetAutomationId(_font, "FindFormatFontComboBox");
        AutomationProperties.SetAutomationId(_size, "FindFormatSizeTextBox");
        AutomationProperties.SetAutomationId(_color, "FindFormatColorTextBox");
        AutomationProperties.SetAutomationId(_highlightColor, "FindFormatHighlightColorTextBox");
        AutomationProperties.SetAutomationId(_style, "FindFormatStyleComboBox");
        AutomationProperties.SetAutomationId(_bold, "FindFormatBoldCheckBox");
        AutomationProperties.SetAutomationId(_italic, "FindFormatItalicCheckBox");
        AutomationProperties.SetAutomationId(_underline, "FindFormatUnderlineCheckBox");
        AutomationProperties.SetAutomationId(_highlight, "FindFormatHighlightCheckBox");

        Apply(FindFormatDialogInput.From(current));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddRow(grid, text.Get(FindReplaceTextId.FormatDialogFontLabel), _font);
        AddRow(grid, text.Get(FindReplaceTextId.FormatDialogSizeLabel), _size);
        AddRow(grid, text.Get(FindReplaceTextId.FormatDialogColorLabel), _color);
        AddRow(grid, text.Get(FindReplaceTextId.FormatDialogStyleLabel), _style);

        var toggles = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        toggles.Children.Add(_bold);
        toggles.Children.Add(_italic);
        toggles.Children.Add(_underline);
        toggles.Children.Add(_highlight);

        var highlightRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        highlightRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        highlightRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddRow(highlightRow, text.Get(FindReplaceTextId.FormatDialogHighlightColorLabel), _highlightColor);

        var body = new StackPanel { Margin = new Thickness(14) };
        body.Children.Add(grid);
        body.Children.Add(toggles);
        body.Children.Add(highlightRow);
        body.Children.Add(new TextBlock
        {
            Text = text.Get(FindReplaceTextId.FormatDialogTriStateHint),
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        body.Children.Add(_status);
        body.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 80,
            rowMargin: new Thickness(0, 10, 0, 0)));
        Content = body;

        Loaded += (_, _) => _font.Focus();
    }

    public FindFormat? Result { get; private set; }

    /// <summary>Shows the dialog; null means cancelled, an empty format means "no formatting".</summary>
    public static FindFormat? Choose(
        Window? owner,
        FindReplaceDialogFieldKind field,
        FindFormat? current,
        TextDocument? document,
        FindReplaceDialogText text)
    {
        var dialog = new FindFormatDialog(field, current, document, text) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    internal FindFormatDialogInput ReadInput() => new(
        _bold.IsChecked,
        _italic.IsChecked,
        _underline.IsChecked,
        _font.Text,
        _size.Text,
        _color.Text,
        _highlight.IsChecked,
        _highlightColor.Text,
        (_style.SelectedItem as ComboBoxItem)?.Tag as string);

    internal void Apply(FindFormatDialogInput input)
    {
        _bold.IsChecked = input.Bold;
        _italic.IsChecked = input.Italic;
        _underline.IsChecked = input.Underline;
        _highlight.IsChecked = input.Highlight;
        _font.Text = input.FontFamily ?? string.Empty;
        _size.Text = input.FontSizeText ?? string.Empty;
        _color.Text = input.ColorText ?? string.Empty;
        _highlightColor.Text = input.HighlightColorText ?? string.Empty;
        _style.SelectedItem = _style.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => input.ParagraphStyleId is { } id
                ? item.Tag is string tag && FindFormat.SameStyle(tag, id)
                : item.Tag is null);
    }

    /// <summary>Validates the fields; returns false and shows why when they do not parse.</summary>
    internal bool TryAccept()
    {
        if (!FindReplaceDialogPlanner.TryBuildFormat(ReadInput(), out var format, out var error))
        {
            _status.Text = _text.Get(error!.Value);
            _status.Visibility = Visibility.Visible;
            return false;
        }

        Result = format;
        return true;
    }

    private void Accept()
    {
        if (TryAccept())
            DialogResult = true;
    }

    private static CheckBox NewToggle() => new()
    {
        IsThreeState = true,
        IsChecked = null,
        Margin = new Thickness(0, 0, 16, 0),
    };

    private static void AddRow(Grid grid, string label, FrameworkElement field)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 8, 0),
        };
        Grid.SetRow(text, row);
        grid.Children.Add(text);
        field.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }
}
