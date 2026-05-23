using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record FormatPictureDialogResult(
    double Width,
    double Height,
    double RotationDegrees,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom,
    string? AltText);

public sealed class FormatPictureDialog : Window
{
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly CheckBox _lockAspectRatioBox = new() { Content = "_Lock aspect ratio", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox _rotationBox = new();
    private readonly TextBox _cropLeftBox = new();
    private readonly TextBox _cropTopBox = new();
    private readonly TextBox _cropRightBox = new();
    private readonly TextBox _cropBottomBox = new();
    private readonly TextBox _altTextBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 86 };
    private readonly double _aspectRatio;
    private bool _updatingAspect;

    public FormatPictureDialogResult Result { get; private set; }

    public FormatPictureDialog(PictureModel picture)
    {
        Result = new FormatPictureDialogResult(
            picture.Width,
            picture.Height,
            picture.RotationDegrees,
            picture.CropLeft,
            picture.CropTop,
            picture.CropRight,
            picture.CropBottom,
            picture.AltText);
        _aspectRatio = picture.Height > 0 ? picture.Width / picture.Height : 1;
        Title = "Format Picture";
        Width = 480;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _widthBox.Text = picture.Width.ToString(CultureInfo.InvariantCulture);
        _heightBox.Text = picture.Height.ToString(CultureInfo.InvariantCulture);
        _rotationBox.Text = picture.RotationDegrees.ToString(CultureInfo.InvariantCulture);
        _cropLeftBox.Text = DrawingInputParser.FormatCropPercent(picture.CropLeft);
        _cropTopBox.Text = DrawingInputParser.FormatCropPercent(picture.CropTop);
        _cropRightBox.Text = DrawingInputParser.FormatCropPercent(picture.CropRight);
        _cropBottomBox.Text = DrawingInputParser.FormatCropPercent(picture.CropBottom);
        _altTextBox.Text = picture.AltText ?? "";
        if (picture.Kind != PictureKind.Image)
        {
            foreach (var box in new[] { _cropLeftBox, _cropTopBox, _cropRightBox, _cropBottomBox })
                box.IsEnabled = false;
        }

        _widthBox.TextChanged += (_, _) => SyncAspectFromWidth();
        _heightBox.TextChanged += (_, _) => SyncAspectFromHeight();
        Content = CreateContent(picture.Kind == PictureKind.Image);
    }

    public static bool TryCreateResult(
        string sizeInput,
        string rotationInput,
        string cropInput,
        string? altText,
        out FormatPictureDialogResult result,
        out string? error)
    {
        result = new FormatPictureDialogResult(0, 0, 0, 0, 0, 0, 0, null);
        error = null;
        if (!ObjectSizeDialog.TryParseSize(sizeInput, out var size))
        {
            error = "Enter positive width and height values.";
            return false;
        }

        if (!RotationDialog.TryParseRotation(rotationInput, out var rotation))
        {
            error = "Enter a numeric rotation in degrees.";
            return false;
        }

        if (!PictureCropDialog.TryCreateResult(cropInput, out var crop, out error))
            return false;

        result = new FormatPictureDialogResult(
            size.Width,
            size.Height,
            rotation.Degrees,
            crop.Left,
            crop.Top,
            crop.Right,
            crop.Bottom,
            string.IsNullOrWhiteSpace(altText) ? null : altText.Trim());
        return true;
    }

    private void Accept()
    {
        var cropInput = string.Join(", ", _cropLeftBox.Text, _cropTopBox.Text, _cropRightBox.Text, _cropBottomBox.Text);
        if (!TryCreateResult($"{_widthBox.Text}x{_heightBox.Text}", _rotationBox.Text, cropInput, _altTextBox.Text, out var result, out var error))
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private Grid CreateContent(bool cropEnabled)
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "_Size", Content = CreateSizeTab() });
        tabs.Items.Add(new TabItem { Header = "_Crop", Content = CreateCropTab(cropEnabled) });
        tabs.Items.Add(new TabItem { Header = "_Alt Text", Content = CreateAltTextTab() });
        root.Children.Add(tabs);

        var buttons = DialogButtonRowFactory.Create(Accept, 72);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        return root;
    }

    private Grid CreateSizeTab()
    {
        var grid = CreateTwoColumnGrid();
        AddRow(grid, 0, "_Height:", _heightBox);
        AddRow(grid, 1, "_Width:", _widthBox);
        AddRow(grid, 2, "_Rotation:", _rotationBox);
        Grid.SetColumn(_lockAspectRatioBox, 1);
        Grid.SetRow(_lockAspectRatioBox, 3);
        grid.Children.Add(_lockAspectRatioBox);
        return grid;
    }

    private Grid CreateCropTab(bool cropEnabled)
    {
        var grid = CreateTwoColumnGrid();
        AddRow(grid, 0, "_Left:", _cropLeftBox);
        AddRow(grid, 1, "_Top:", _cropTopBox);
        AddRow(grid, 2, "_Right:", _cropRightBox);
        AddRow(grid, 3, "_Bottom:", _cropBottomBox);
        if (!cropEnabled)
        {
            var note = new TextBlock
            {
                Text = "Crop is available for inserted image pictures.",
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(note, 4);
            Grid.SetColumn(note, 1);
            grid.Children.Add(note);
        }
        return grid;
    }

    private Grid CreateAltTextTab()
    {
        var grid = CreateTwoColumnGrid();
        var label = new Label { Content = "_Description:", Target = _altTextBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(label, 0);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
        Grid.SetRow(_altTextBox, 0);
        Grid.SetColumn(_altTextBox, 1);
        grid.Children.Add(_altTextBox);
        return grid;
    }

    private static Grid CreateTwoColumnGrid()
    {
        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private static void AddRow(Grid grid, int row, string labelText, TextBox box)
    {
        var label = new Label { Content = labelText, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
        box.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
    }

    private void SyncAspectFromWidth()
    {
        if (_updatingAspect || _lockAspectRatioBox.IsChecked != true || _aspectRatio <= 0)
            return;
        if (!double.TryParse(_widthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) || width <= 0)
            return;
        _updatingAspect = true;
        _heightBox.Text = (width / _aspectRatio).ToString("0.###", CultureInfo.InvariantCulture);
        _updatingAspect = false;
    }

    private void SyncAspectFromHeight()
    {
        if (_updatingAspect || _lockAspectRatioBox.IsChecked != true || _aspectRatio <= 0)
            return;
        if (!double.TryParse(_heightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) || height <= 0)
            return;
        _updatingAspect = true;
        _widthBox.Text = (height * _aspectRatio).ToString("0.###", CultureInfo.InvariantCulture);
        _updatingAspect = false;
    }
}
