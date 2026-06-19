using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FreeW.App.Host;

/// <summary>
/// A tiny modal prompt for an inline image's width (points). Height scales proportionally at the
/// call site, so only width is collected. Returns the entered width, or null if the user cancels.
/// </summary>
internal sealed class ImageSizeDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _widthBox;
    private double? _result;

    private ImageSizeDialog(Window? owner, double currentWidthPt)
    {
        Owner = owner;
        Title = "Image Size";
        Width = 280;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _widthBox = new TextBox
        {
            Text = currentWidthPt.ToString("0.##", CultureInfo.CurrentCulture),
            MinWidth = 120
        };

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock { Text = "Width (pt):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetRow(label, 0);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        Grid.SetRow(_widthBox, 0);
        Grid.SetColumn(_widthBox, 1);
        grid.Children.Add(_widthBox);

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 1);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_widthBox);
    }

    private void Accept()
    {
        if (double.TryParse(_widthBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var width) && width > 0)
        {
            _result = width;
            Close();
        }
        else
        {
            DialogMessageHelper.ShowWarning(this, "Enter a positive width in points.");
        }
    }

    /// <summary>Show the dialog; returns the chosen width in points, or null if cancelled.</summary>
    public static double? Prompt(Window? owner, double currentWidthPt)
    {
        var dialog = new ImageSizeDialog(owner, currentWidthPt);
        dialog.ShowDialog();
        return dialog._result;
    }
}
