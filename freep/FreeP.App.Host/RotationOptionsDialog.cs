using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style exact rotation entry for the selected shapes.</summary>
public sealed class RotationOptionsDialog : DialogWindow
{
    private readonly EditingSession _editor;
    private readonly TextBox _rotationBox;

    public RotationOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var surface = RotationOptionsPlanner.BuildSurfacePlan();
        Title = surface.Title;
        Width = 360;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _rotationBox = new TextBox
        {
            Text = Format(InitialRotation()),
            MinWidth = 160,
            Margin = new Thickness(4),
        };

        var buttons = DialogButtonRowFactory.Create(OnOk, buttonWidth: 80,
            rowMargin: new Thickness(4, 8, 8, 8));
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new Label { Content = surface.RotationLabel });
        panel.Children.Add(_rotationBox);
        panel.Children.Add(new TextBlock
        {
            Text = surface.Hint,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(4, 4, 4, 0),
        });
        panel.Children.Add(buttons);
        Content = panel;
    }

    internal void SetRotationForTests(string text) => _rotationBox.Text = text;

    internal bool TryGetRotation(out double degrees) =>
        RotationOptionsPlanner.TryParse(_rotationBox.Text, out degrees);

    internal bool ApplyForTests() => Apply(showValidation: false);

    private void OnOk()
    {
        if (Apply(showValidation: true))
            DialogResult = true;
    }

    private bool Apply(bool showValidation)
    {
        if (!TryGetRotation(out var degrees))
        {
            if (showValidation)
                MessageBox.Show(this, "Enter a finite angle from -360 to 360 degrees.",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _editor.SetSelectedRotation(degrees);
        return true;
    }

    private double InitialRotation() => _editor.SelectedShapeIds
        .Select(id => _editor.CurrentSlide is { } slide
            ? SlideShapeTraversal.FindById(slide, id)
            : null)
        .FirstOrDefault(shape => shape is not null)?.RotationDeg ?? 0;

    private static string Format(double value) => value.ToString("G", System.Globalization.CultureInfo.CurrentCulture);
}
