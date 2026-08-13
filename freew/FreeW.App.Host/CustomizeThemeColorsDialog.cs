using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Create New Theme Colors" dialog (Design &gt; Document Formatting &gt; Colors &gt; Customize Colors…).
/// Lets the user author a custom <see cref="DocumentTheme"/> color scheme by picking all 12 DrawingML
/// <c>a:clrScheme</c> slots. Returns a <see cref="DocumentTheme"/> whose palette carries the chosen
/// values (Name = "Custom", fonts preserved from the current theme) on OK, or null on Cancel.
/// </summary>
internal sealed class CustomizeThemeColorsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private static readonly CustomizeThemeColorsDialogVisualMetrics Layout =
        CustomizeThemeColorsDialogPlanner.VisualMetrics;

    private readonly TextBox[] _hexBoxes = new TextBox[12];
    private readonly TextBox _nameBox;
    private readonly DocumentTheme _currentTheme;
    private DocumentTheme? _result;

    private CustomizeThemeColorsDialog(Window? owner, DocumentTheme currentTheme)
    {
        Owner = owner;
        _currentTheme = currentTheme;
        Title = CustomizeThemeColorsDialogPlanner.Title;
        Width = Layout.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var state = CustomizeThemeColorsDialogPlanner.BuildInitialState(currentTheme);
        for (var i = 0; i < _hexBoxes.Length; i++)
            _hexBoxes[i] = new TextBox { Text = state.ColorHexTexts[i], MinWidth = Layout.ColorFieldMinWidth };

        _nameBox = new TextBox { Text = state.NameText, MinWidth = Layout.NameFieldMinWidth };

        Content = BuildContent();
        Loaded += (_, _) => _nameBox.Focus();
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(Layout.DialogMargin) };

        panel.Children.Add(new TextBlock
        {
            Text = CustomizeThemeColorsDialogPlanner.Hint,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontSize = Layout.HintFontSize,
            Margin = new Thickness(0, 0, 0, Layout.HintBottomMargin)
        });

        // Color slot rows.
        for (var i = 0; i < 12; i++)
        {
            var grid = SlotRow(CustomizeThemeColorsDialogPlanner.Slots[i].Label, _hexBoxes[i]);
            panel.Children.Add(grid);
        }

        panel.Children.Add(new Separator
        {
            Margin = new Thickness(0, Layout.SeparatorTopMargin, 0, Layout.SeparatorBottomMargin)
        });
        panel.Children.Add(SlotRow("Name:", _nameBox));

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: Layout.ActionButtonWidth,
            rowMargin: new Thickness(0, Layout.ActionRowTopMargin, 0, 0));
        panel.Children.Add(buttons);

        return panel;
    }

    private static Grid SlotRow(string label, UIElement field)
    {
        var grid = new Grid { Margin = new Thickness(0, Layout.RowVerticalMargin, 0, Layout.RowVerticalMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Layout.LabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Layout.LabelRightMargin, 0)
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(field, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(field);
        return grid;
    }

    private void Accept()
    {
        if (!CustomizeThemeColorsDialogPlanner.TryBuildResult(
                _currentTheme,
                new CustomizeThemeColorsDialogInput(_hexBoxes.Select(box => box.Text).ToArray(), _nameBox.Text),
                out _result,
                out var validation))
        {
            DialogMessageHelper.ShowWarning(
                this,
                validation?.Message ?? DesignDialogTextCatalog.Resolve(UiText.Get).InvalidThemeColorsMessage,
                Title);
            _hexBoxes.ElementAtOrDefault(validation?.SlotIndex ?? 0)?.Focus();
            return;
        }

        Close();
    }

    /// <summary>
    /// Show the Customize Colors dialog seeded with the current theme's color scheme. Returns a
    /// <see cref="DocumentTheme"/> carrying the chosen 12-slot scheme on OK, or null on Cancel.
    /// </summary>
    public static DocumentTheme? Prompt(Window? owner, DocumentTheme currentTheme)
    {
        var dialog = new CustomizeThemeColorsDialog(owner, currentTheme);
        dialog.ShowDialog();
        return dialog._result;
    }
}
