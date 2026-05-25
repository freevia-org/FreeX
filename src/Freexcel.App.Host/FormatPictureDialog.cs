using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record FormatPictureDialogResult(
    double Width,
    double Height,
    double RotationDegrees,
    bool LockAspectRatio,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom,
    string? AltText);

public sealed partial class FormatPictureDialog : Window
{
    private readonly TabControl _tabs = new();
    private readonly TabItem _sizeTab = new() { Header = "_Size" };
    private readonly TabItem _cropTab = new() { Header = "_Crop" };
    private readonly TabItem _altTextTab = new() { Header = "_Alt Text" };
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly CheckBox _lockAspectRatioBox = new() { Content = "_Lock aspect ratio", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox _rotationBox = new();
    private readonly TextBox _cropLeftBox = new();
    private readonly TextBox _cropTopBox = new();
    private readonly TextBox _cropRightBox = new();
    private readonly TextBox _cropBottomBox = new();
    private readonly Button _resetCropButton = new() { Content = "Reset _Crop", MinWidth = 96, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox _altTextBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 86 };
    private readonly FormatPictureDialogResult _initialResult;
    private readonly double _aspectRatio;
    private bool _updatingAspect;

    public FormatPictureDialogResult Result { get; private set; }

    public FormatPictureDialog(PictureModel picture)
    {
        _initialResult = new FormatPictureDialogResult(
            picture.Width,
            picture.Height,
            picture.RotationDegrees,
            picture.LockAspectRatio,
            picture.CropLeft,
            picture.CropTop,
            picture.CropRight,
            picture.CropBottom,
            picture.AltText);
        Result = _initialResult;
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
        _lockAspectRatioBox.IsChecked = picture.LockAspectRatio;
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
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private Grid CreateContent(bool cropEnabled)
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _sizeTab.Content = CreateSizeTab();
        _cropTab.Content = CreateCropTab(cropEnabled);
        _altTextTab.Content = CreateAltTextTab();
        _tabs.Items.Add(_sizeTab);
        _tabs.Items.Add(_cropTab);
        _tabs.Items.Add(_altTextTab);
        root.Children.Add(_tabs);

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
        var resetSizeButton = new Button { Content = "Reset _Size", MinWidth = 96, Margin = new Thickness(0, 0, 0, 8) };
        resetSizeButton.Click += (_, _) => ResetSizeToInitial();
        AddButtonRow(grid, 4, resetSizeButton);
        return grid;
    }

    private Grid CreateCropTab(bool cropEnabled)
    {
        var grid = CreateTwoColumnGrid();
        AddRow(grid, 0, "_Left:", _cropLeftBox);
        AddRow(grid, 1, "_Top:", _cropTopBox);
        AddRow(grid, 2, "_Right:", _cropRightBox);
        AddRow(grid, 3, "_Bottom:", _cropBottomBox);
        _resetCropButton.Click += (_, _) => ResetCropToInitial();
        AddButtonRow(grid, 4, _resetCropButton);
        if (!cropEnabled)
        {
            _resetCropButton.IsEnabled = false;
            var note = new TextBlock
            {
                Text = "Crop is available for inserted image pictures.",
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(note, 5);
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
        for (var i = 0; i < 6; i++)
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

    private static void AddButtonRow(Grid grid, int row, Button button)
    {
        Grid.SetRow(button, row);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
    }

}
