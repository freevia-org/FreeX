using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class RotationOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly TextBox _rotationBox;

    internal RotationOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var surface = RotationOptionsPlanner.BuildSurfacePlan();
        Title = surface.Title;
        Width = 360;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _rotationBox = new TextBox { Text = Format(InitialRotation()), MinWidth = 160 };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                MakeButton(surface.OkLabel, true, OnOk),
                MakeButton(surface.CancelLabel, false, () => Close(false)),
            },
        };
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                MakeRow(surface.RotationLabel, _rotationBox),
                new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal void SetRotationForTests(string text) => _rotationBox.Text = text;

    internal bool TryGetRotation(out double degrees) =>
        RotationOptionsPlanner.TryParse(_rotationBox.Text, out degrees);

    internal bool ApplyForTests() => Apply();

    private void OnOk()
    {
        if (Apply())
            Close(true);
    }

    private bool Apply()
    {
        if (!TryGetRotation(out var degrees))
            return false;

        _editor.SetSelectedRotation(degrees);
        return true;
    }

    private double InitialRotation() => _editor.SelectedShapeIds
        .Select(id => _editor.CurrentSlide is { } slide ? SlideShapeTraversal.FindById(slide, id) : null)
        .FirstOrDefault(shape => shape is not null)?.RotationDeg ?? 0;

    private static string Format(double value) => value.ToString("G", CultureInfo.CurrentCulture);
    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170, *") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
