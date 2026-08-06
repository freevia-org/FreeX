using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Create New Theme Fonts" dialog (Design &gt; Document Formatting &gt; Fonts &gt; Customize Fonts…).
/// Lets the user pick a heading font and a body font, creating and applying a custom
/// <see cref="DocumentFontSet"/>. Returns a <see cref="DocumentFontSet"/> on OK, or null on Cancel.
/// </summary>
internal sealed class CustomizeThemeFontsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ComboBox _headingCombo;
    private readonly ComboBox _bodyCombo;
    private readonly TextBox _nameBox;
    private readonly CustomizeThemeFontsDialogSession _session;
    private DocumentFontSet? _result;

    private CustomizeThemeFontsDialog(Window? owner, DocumentFontSet current)
    {
        Owner = owner;
        Title = CustomizeThemeFontsDialogPlanner.Title;
        Width = CustomizeThemeFontsDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _session = CustomizeThemeFontsDialogPlanner.CreateSession(current);
        var state = _session.InitialState;
        _headingCombo = FontCombo(state.HeadingFontText);
        _bodyCombo    = FontCombo(state.BodyFontText);
        _nameBox      = new TextBox { Text = state.NameText, MinWidth = CustomizeThemeFontsDialogPlanner.FieldMinWidth };

        Content = BuildContent();
        Loaded += (_, _) => _headingCombo.Focus();
    }

    private UIElement BuildContent()
    {
        var grid = new Grid { Margin = new Thickness(CustomizeThemeFontsDialogPlanner.DialogMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CustomizeThemeFontsDialogPlanner.LabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var hint = new TextBlock
        {
            Text = CustomizeThemeFontsDialogPlanner.Hint,
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, CustomizeThemeFontsDialogPlanner.HintBottomMargin)
        };
        Grid.SetRow(hint, 0);
        Grid.SetColumnSpan(hint, 2);
        grid.Children.Add(hint);

        void AddRow(int row, string label, UIElement field)
        {
            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, CustomizeThemeFontsDialogPlanner.RowMargin, CustomizeThemeFontsDialogPlanner.LabelRightMargin, CustomizeThemeFontsDialogPlanner.RowMargin)
            };
            if (field is FrameworkElement fe)
                fe.Margin = new Thickness(0, CustomizeThemeFontsDialogPlanner.RowMargin, 0, CustomizeThemeFontsDialogPlanner.RowMargin);
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
            Grid.SetRow(field, row); Grid.SetColumn(field, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(field);
        }

        AddRow(1, CustomizeThemeFontsDialogPlanner.HeadingFontLabel, _headingCombo);
        AddRow(2, CustomizeThemeFontsDialogPlanner.BodyFontLabel, _bodyCombo);

        var sep = new Separator
        {
            Margin = new Thickness(0, CustomizeThemeFontsDialogPlanner.SeparatorTopMargin, 0, CustomizeThemeFontsDialogPlanner.SeparatorBottomMargin)
        };
        Grid.SetRow(sep, 3); Grid.SetColumnSpan(sep, 2);
        grid.Children.Add(sep);

        AddRow(4, CustomizeThemeFontsDialogPlanner.NameLabel, _nameBox);

        // Append button row below the grid.
        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: CustomizeThemeFontsDialogPlanner.ActionButtonWidth,
            rowMargin: new Thickness(
                CustomizeThemeFontsDialogPlanner.DialogMargin,
                CustomizeThemeFontsDialogPlanner.ActionRowTopMargin,
                CustomizeThemeFontsDialogPlanner.DialogMargin,
                CustomizeThemeFontsDialogPlanner.ActionRowBottomMargin)));
        return panel;
    }

    private static ComboBox FontCombo(string current)
    {
        var combo = new ComboBox { IsEditable = true, MinWidth = CustomizeThemeFontsDialogPlanner.FieldMinWidth };
        foreach (var f in CustomizeThemeFontsDialogPlanner.CommonFonts)
            combo.Items.Add(f);
        combo.Text = current;
        return combo;
    }

    private void Accept()
    {
        var acceptance = _session.PlanAcceptance(
            new CustomizeThemeFontsDialogInput(_headingCombo.Text, _bodyCombo.Text, _nameBox.Text));
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, acceptance.ErrorMessage, Title);
            (acceptance.FocusField == CustomizeThemeFontsDialogField.BodyFont ? _bodyCombo : _headingCombo).Focus();
            return;
        }
        _result = acceptance.Result;
        Close();
    }

    /// <summary>
    /// Show the Customize Fonts dialog seeded with the current font set. Returns a new
    /// <see cref="DocumentFontSet"/> on OK, or null on Cancel.
    /// </summary>
    public static DocumentFontSet? Prompt(Window? owner, DocumentFontSet current)
    {
        var dialog = new CustomizeThemeFontsDialog(owner, current);
        dialog.ShowDialog();
        return dialog._result;
    }
}
