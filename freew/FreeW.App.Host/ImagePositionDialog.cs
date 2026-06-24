using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Dialog for setting floating image position offsets and anchors. Returns null if the user cancels.
/// </summary>
internal sealed class ImagePositionDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _hBox, _vBox;
    private readonly ComboBox _hAnchorBox, _vAnchorBox;
    private (double HOffset, double VOffset, HorizontalAnchor HAnchor, VerticalAnchor VAnchor)? _result;

    private static readonly string[] HAnchorLabels = ["Column", "Margin", "Page"];
    private static readonly string[] VAnchorLabels = ["Paragraph", "Margin", "Page"];

    private static HorizontalAnchor ParseH(string? s) => s switch { "Margin" => HorizontalAnchor.Margin, "Page" => HorizontalAnchor.Page, _ => HorizontalAnchor.Column };
    private static VerticalAnchor ParseV(string? s) => s switch { "Margin" => VerticalAnchor.Margin, "Page" => VerticalAnchor.Page, _ => VerticalAnchor.Paragraph };

    private static string LabelH(HorizontalAnchor a) => a switch { HorizontalAnchor.Margin => "Margin", HorizontalAnchor.Page => "Page", _ => "Column" };
    private static string LabelV(VerticalAnchor a) => a switch { VerticalAnchor.Margin => "Margin", VerticalAnchor.Page => "Page", _ => "Paragraph" };

    private ImagePositionDialog(Window? owner, double hOffPt, double vOffPt, HorizontalAnchor hAnchor, VerticalAnchor vAnchor)
    {
        Owner = owner;
        Title = "Picture Position";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _hBox = new TextBox { Text = hOffPt.ToString("0.##", CultureInfo.CurrentCulture), MinWidth = 80 };
        _vBox = new TextBox { Text = vOffPt.ToString("0.##", CultureInfo.CurrentCulture), MinWidth = 80 };

        _hAnchorBox = Combo(HAnchorLabels, LabelH(hAnchor));
        _vAnchorBox = Combo(VAnchorLabels, LabelV(vAnchor));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static void Place(Grid g, UIElement el, int row, int col)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el);
        }

        Place(grid, Label("Horizontal offset (pt):"), 0, 0); Place(grid, _hBox, 0, 1);
        Place(grid, Label("Relative to:"),             1, 0); Place(grid, _hAnchorBox, 1, 1);
        Place(grid, Label("Vertical offset (pt):"),   2, 0); Place(grid, _vBox, 2, 1);
        Place(grid, Label("Relative to:"),             3, 0); Place(grid, _vAnchorBox, 3, 1);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Place(grid, buttons, 4, 1);

        Content = grid;
        DialogFocus.FocusAndSelect(_hBox);
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) };

    private static ComboBox Combo(string[] items, string selected)
    {
        var cb = new ComboBox { MinWidth = 100 };
        foreach (var item in items) cb.Items.Add(item);
        cb.SelectedItem = selected;
        return cb;
    }

    private void Accept()
    {
        var okH = double.TryParse(_hBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var h);
        var okV = double.TryParse(_vBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v);
        if (!okH || !okV)
        {
            DialogMessageHelper.ShowWarning(this, "Enter valid numeric offsets in points.");
            return;
        }
        _result = (h, v, ParseH(_hAnchorBox.SelectedItem?.ToString()), ParseV(_vAnchorBox.SelectedItem?.ToString()));
        Close();
    }

    /// <summary>Show the position dialog. Returns offsets + anchors, or null if cancelled.</summary>
    public static (double HOffset, double VOffset, HorizontalAnchor HAnchor, VerticalAnchor VAnchor)? Prompt(
        Window? owner, double hOffPt, double vOffPt, HorizontalAnchor hAnchor, VerticalAnchor vAnchor)
    {
        var dialog = new ImagePositionDialog(owner, hOffPt, vOffPt, hAnchor, vAnchor);
        dialog.ShowDialog();
        return dialog._result;
    }
}
