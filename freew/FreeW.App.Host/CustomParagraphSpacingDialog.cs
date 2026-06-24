using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Custom Paragraph Spacing" dialog (Design &gt; Document Formatting &gt; Paragraph Spacing &gt;
/// Custom Paragraph Spacing…). Lets the user set explicit space-before / space-after / line-spacing
/// values for the document default, mirroring the same model fields that the six paragraph-spacing
/// presets write. Returns a <see cref="DocumentParagraphSpacingSet"/> on OK, or null on Cancel.
/// </summary>
internal sealed class CustomParagraphSpacingDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _beforeBox;
    private readonly TextBox _afterBox;
    private readonly TextBox _lineBox;

    private DocumentParagraphSpacingSet? _result;

    private CustomParagraphSpacingDialog(Window? owner, DocumentParagraphSpacingSet? current)
    {
        Owner = owner;
        Title = "Custom Paragraph Spacing";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var seed = current ?? DocumentParagraphSpacingSet.Default;
        _beforeBox = NumberBox(seed.SpaceBeforePt);
        _afterBox  = NumberBox(seed.SpaceAfterPt);
        _lineBox   = NumberBox(seed.LineSpacing);

        Content = BuildContent();
        Loaded += (_, _) => _beforeBox.Focus();
    }

    private UIElement BuildContent()
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void AddRow(int row, string label, UIElement field)
        {
            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            if (field is FrameworkElement fe)
                fe.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
        }

        // Helper note row.
        var hint = new TextBlock
        {
            Text = "All values in points (pt). Line spacing is a multiple (e.g. 1.15 = 115%).",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(hint, 0);
        Grid.SetColumnSpan(hint, 2);
        grid.Children.Add(hint);

        AddRow(1, "Space before (pt):", _beforeBox);
        AddRow(2, "Space after (pt):",  _afterBox);
        AddRow(3, "Line spacing (×):",  _lineBox);

        // Buttons.
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 4);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        return grid;
    }

    private void Accept()
    {
        if (!TryParse(_beforeBox.Text, out var before) || before < 0 || before > 200)
        {
            DialogMessageHelper.ShowWarning(this, "Space before must be between 0 and 200 pt.", Title);
            _beforeBox.Focus();
            return;
        }
        if (!TryParse(_afterBox.Text, out var after) || after < 0 || after > 200)
        {
            DialogMessageHelper.ShowWarning(this, "Space after must be between 0 and 200 pt.", Title);
            _afterBox.Focus();
            return;
        }
        if (!TryParse(_lineBox.Text, out var line) || line <= 0 || line > 10)
        {
            DialogMessageHelper.ShowWarning(this, "Line spacing must be between 0.01 and 10.", Title);
            _lineBox.Focus();
            return;
        }

        _result = new DocumentParagraphSpacingSet("Custom", before, after, line);
        Close();
    }

    private static TextBox NumberBox(double value) => new()
    {
        Text = value.ToString("0.##", CultureInfo.CurrentCulture),
        MinWidth = 120
    };

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    /// <summary>
    /// Show the dialog seeded with <paramref name="current"/> spacing; returns a new
    /// <see cref="DocumentParagraphSpacingSet"/> on OK, or null on Cancel.
    /// </summary>
    public static DocumentParagraphSpacingSet? Prompt(Window? owner, DocumentParagraphSpacingSet? current)
    {
        var dialog = new CustomParagraphSpacingDialog(owner, current);
        dialog.ShowDialog();
        return dialog._result;
    }
}
