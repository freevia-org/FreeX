using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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

public sealed class FormatPictureDialog : Window
{
    private readonly TabControl _tabs = new();
    private readonly TabItem _sizeTab = new() { Header = UiText.Get("FormatPicture_SizeTab") };
    private readonly TabItem _cropTab = new() { Header = UiText.Get("FormatPicture_CropTab") };
    private readonly TabItem _altTextTab = new() { Header = UiText.Get("FormatPicture_AltTextTab") };
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly CheckBox _lockAspectRatioBox = new() { Content = UiText.Get("FormatPicture_LockAspectRatio"), IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox _rotationBox = new();
    private readonly TextBox _cropLeftBox = new();
    private readonly TextBox _cropTopBox = new();
    private readonly TextBox _cropRightBox = new();
    private readonly TextBox _cropBottomBox = new();
    private readonly Button _resetCropButton = new() { Content = UiText.Get("FormatPicture_ResetCropButton"), MinWidth = 96, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox _altTextBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 86 };
    private readonly FormatPictureDialogResult _initialResult;
    private readonly double _aspectRatio;
    private bool _updatingAspect;

    public FormatPictureDialogResult Result { get; private set; }

    public FormatPictureDialog(PictureModel picture)
    {
        var values = FormatPicturePlanner.Capture(picture);
        var cropValues = PictureCropDialogPlanner.Capture(picture);
        _initialResult = new FormatPictureDialogResult(
            values.Width,
            values.Height,
            values.RotationDegrees,
            values.LockAspectRatio,
            cropValues.Left,
            cropValues.Top,
            cropValues.Right,
            cropValues.Bottom,
            values.AltText);
        Result = _initialResult;
        _aspectRatio = FormatPicturePlanner.AspectRatio(values.Width, values.Height);
        Title = UiText.Get("FormatPicture_Title");
        Width = 480;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _widthBox.Text = FormatPicturePlanner.FormatSize(values.Width);
        _heightBox.Text = FormatPicturePlanner.FormatSize(values.Height);
        _rotationBox.Text = FormatPicturePlanner.FormatRotation(values.RotationDegrees);
        _lockAspectRatioBox.IsChecked = values.LockAspectRatio;
        _cropLeftBox.Text = PictureCropDialogPlanner.FormatPercent(cropValues.Left);
        _cropTopBox.Text = PictureCropDialogPlanner.FormatPercent(cropValues.Top);
        _cropRightBox.Text = PictureCropDialogPlanner.FormatPercent(cropValues.Right);
        _cropBottomBox.Text = PictureCropDialogPlanner.FormatPercent(cropValues.Bottom);
        _altTextBox.Text = values.AltText;
        if (!cropValues.IsCroppable)
        {
            foreach (var box in new[] { _cropLeftBox, _cropTopBox, _cropRightBox, _cropBottomBox })
                box.IsEnabled = false;
        }

        _widthBox.TextChanged += (_, _) => SyncAspectFromWidth();
        _heightBox.TextChanged += (_, _) => SyncAspectFromHeight();
        Content = CreateContent(picture.Kind == PictureKind.Image);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static bool TryCreateResult(
        string sizeInput,
        string rotationInput,
        bool lockAspectRatio,
        string cropInput,
        string? altText,
        out FormatPictureDialogResult result,
        out string? error)
    {
        result = new FormatPictureDialogResult(0, 0, 0, true, 0, 0, 0, 0, null);
        error = null;
        if (!FormatPicturePlanner.TryCreateSizeResult(sizeInput, out var size) || size is null)
        {
            error = UiText.Get("FormatPicture_InvalidSizeMessage");
            return false;
        }

        if (!FormatPicturePlanner.TryCreateRotationResult(rotationInput, out var rotation) || rotation is null)
        {
            error = UiText.Get("FormatPicture_InvalidRotationMessage");
            return false;
        }

        if (!PictureCropDialogPlanner.TryCreateResult(cropInput, out var crop, out _) || crop is null)
        {
            error = UiText.Get("FormatPicture_InvalidCropPercentagesMessage");
            return false;
        }

        result = new FormatPictureDialogResult(
            size.Width,
            size.Height,
            rotation.Degrees,
            lockAspectRatio,
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
        if (!TryCreateResult(
                $"{_widthBox.Text}x{_heightBox.Text}",
                _rotationBox.Text,
                _lockAspectRatioBox.IsChecked == true,
                cropInput,
                _altTextBox.Text,
                out var result,
                out var error))
        {
            DialogMessageHelper.ShowWarning(this, error, Title);
            FocusInvalidInput(error);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void FocusInvalidInput(string? error)
    {
        if (string.Equals(error, UiText.Get("FormatPicture_InvalidRotationMessage"), StringComparison.Ordinal))
        {
            _tabs.SelectedItem = _sizeTab;
            DialogFocus.FocusAndSelect(_rotationBox);
            return;
        }

        if (string.Equals(error, UiText.Get("FormatPicture_InvalidSizeMessage"), StringComparison.Ordinal))
        {
            _tabs.SelectedItem = _sizeTab;
            DialogFocus.FocusAndSelect(ResolveInvalidSizeInput());
            return;
        }

        _tabs.SelectedItem = _cropTab;
        DialogFocus.FocusAndSelect(ResolveInvalidCropInput(error));
    }

    private TextBox ResolveInvalidSizeInput()
    {
        if (!TryParsePositiveSize(_heightBox.Text))
            return _heightBox;

        if (!TryParsePositiveSize(_widthBox.Text))
            return _widthBox;

        return _heightBox;
    }

    private static bool TryParsePositiveSize(string text) =>
        FormatPicturePlanner.TryCreateSizeResult(text, text, out _);

    private TextBox ResolveInvalidCropInput(string? error)
    {
        if (string.Equals(error, UiText.Get("FormatPicture_InvalidCropPercentagesMessage"), StringComparison.Ordinal))
        {
            if (!PictureCropDialogPlanner.TryParsePercent(_cropLeftBox.Text, out _))
                return _cropLeftBox;
            if (!PictureCropDialogPlanner.TryParsePercent(_cropTopBox.Text, out _))
                return _cropTopBox;
            if (!PictureCropDialogPlanner.TryParsePercent(_cropRightBox.Text, out _))
                return _cropRightBox;
            if (!PictureCropDialogPlanner.TryParsePercent(_cropBottomBox.Text, out _))
                return _cropBottomBox;
        }

        return _cropLeftBox;
    }

    private void FocusInitialKeyboardTarget()
    {
        _heightBox.Focus();
        _heightBox.SelectAll();
        Keyboard.Focus(_heightBox);
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
        AddRow(grid, 0, UiText.Get("FormatPicture_HeightLabel"), _heightBox);
        AddRow(grid, 1, UiText.Get("FormatPicture_WidthLabel"), _widthBox);
        AddRow(grid, 2, UiText.Get("FormatPicture_RotationLabel"), _rotationBox);
        Grid.SetColumn(_lockAspectRatioBox, 1);
        Grid.SetRow(_lockAspectRatioBox, 3);
        grid.Children.Add(_lockAspectRatioBox);
        var resetSizeButton = new Button { Content = UiText.Get("FormatPicture_ResetSizeButton"), MinWidth = 96, Margin = new Thickness(0, 0, 0, 8) };
        resetSizeButton.Click += (_, _) => ResetSizeToInitial();
        AddButtonRow(grid, 4, resetSizeButton);
        return grid;
    }

    private Grid CreateCropTab(bool cropEnabled)
    {
        var grid = CreateTwoColumnGrid();
        AddRow(grid, 0, UiText.Get("FormatPicture_LeftLabel"), _cropLeftBox);
        AddRow(grid, 1, UiText.Get("FormatPicture_TopLabel"), _cropTopBox);
        AddRow(grid, 2, UiText.Get("FormatPicture_RightLabel"), _cropRightBox);
        AddRow(grid, 3, UiText.Get("FormatPicture_BottomLabel"), _cropBottomBox);
        _resetCropButton.Click += (_, _) => ResetCropToInitial();
        AddButtonRow(grid, 4, _resetCropButton);
        if (!cropEnabled)
        {
            _resetCropButton.IsEnabled = false;
            var note = new TextBlock
            {
                Text = UiText.Get("FormatPicture_CropUnavailableMessage"),
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
        var label = new Label { Content = UiText.Get("FormatPicture_DescriptionLabel"), Target = _altTextBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 8) };
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

    private void ResetSizeToInitial()
    {
        _updatingAspect = true;
        _widthBox.Text = FormatPicturePlanner.FormatSize(_initialResult.Width);
        _heightBox.Text = FormatPicturePlanner.FormatSize(_initialResult.Height);
        _rotationBox.Text = FormatPicturePlanner.FormatRotation(_initialResult.RotationDegrees);
        _lockAspectRatioBox.IsChecked = _initialResult.LockAspectRatio;
        _updatingAspect = false;
    }

    private void ResetCropToInitial()
    {
        _cropLeftBox.Text = PictureCropDialogPlanner.FormatPercent(_initialResult.CropLeft);
        _cropTopBox.Text = PictureCropDialogPlanner.FormatPercent(_initialResult.CropTop);
        _cropRightBox.Text = PictureCropDialogPlanner.FormatPercent(_initialResult.CropRight);
        _cropBottomBox.Text = PictureCropDialogPlanner.FormatPercent(_initialResult.CropBottom);
    }

    private void SyncAspectFromWidth()
    {
        if (_updatingAspect || _lockAspectRatioBox.IsChecked != true || _aspectRatio <= 0)
            return;
        if (FormatPicturePlanner.SyncHeightFromWidth(_widthBox.Text, _aspectRatio) is { } height)
        {
            _updatingAspect = true;
            _heightBox.Text = FormatPicturePlanner.FormatSize(height);
            _updatingAspect = false;
        }
    }

    private void SyncAspectFromHeight()
    {
        if (_updatingAspect || _lockAspectRatioBox.IsChecked != true || _aspectRatio <= 0)
            return;
        if (FormatPicturePlanner.SyncWidthFromHeight(_heightBox.Text, _aspectRatio) is { } width)
        {
            _updatingAspect = true;
            _widthBox.Text = FormatPicturePlanner.FormatSize(width);
            _updatingAspect = false;
        }
    }
}
