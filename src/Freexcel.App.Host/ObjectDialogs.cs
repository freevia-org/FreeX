using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum HyperlinkLinkType
{
    ExistingFileOrWebPage,
    CreateNewDocument,
    PlaceInThisDocument,
    EmailAddress
}

public sealed record HyperlinkDialogResult(
    HyperlinkLinkType LinkType,
    string Target,
    string DisplayText,
    string ScreenTip,
    string Bookmark);

public sealed class HyperlinkDialog : Window
{
    private readonly TextBox _targetBox = new();
    private readonly TextBox _displayBox = new();
    private readonly Button _screenTipButton = new() { Content = "_ScreenTip..." };
    private readonly Button _bookmarkButton = new() { Content = "_Bookmark..." };
    private readonly ListBox _linkTypes = new();
    private string _screenTip = "";
    private string _bookmark = "";

    public HyperlinkDialogResult Result { get; private set; }

    public HyperlinkDialog(string target = "https://", string displayText = "")
    {
        Result = CreateResult(target, displayText);
        Title = "Insert Hyperlink";
        Width = 560;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16) };
        _linkTypes.Width = 170;
        _linkTypes.Margin = new Thickness(0, 0, 12, 0);
        _linkTypes.ItemsSource = new[]
        {
            "Existing File or Web Page",
            "Create New Document",
            "Place in This Document",
            "E-mail Address"
        };
        _linkTypes.SelectedIndex = 0;
        DockPanel.SetDock(_linkTypes, Dock.Left);
        root.Children.Add(_linkTypes);

        var grid = DialogGrid(3);
        AddTextRow(grid, 0, "Text to _display:", _displayBox, displayText);
        AddTextRow(grid, 1, "_Address:", _targetBox, target);
        _screenTipButton.Click += ScreenTipButton_Click;
        _bookmarkButton.Click += BookmarkButton_Click;
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        _screenTipButton.Width = 96;
        _screenTipButton.Margin = new Thickness(0, 0, 8, 0);
        _bookmarkButton.Width = 96;
        buttonRow.Children.Add(_screenTipButton);
        buttonRow.Children.Add(_bookmarkButton);
        grid.Children.Add(buttonRow);
        Grid.SetRow(buttonRow, 2);
        Grid.SetColumn(buttonRow, 1);

        grid.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            Result = CreateResult(_targetBox.Text, _displayBox.Text, SelectedLinkType, _screenTip, _bookmark);
            DialogResult = true;
        }));
        Grid.SetRow(grid.Children[^1], 3);
        Grid.SetColumnSpan(grid.Children[^1], 2);
        root.Children.Add(grid);
        Content = root;
    }

    public static HyperlinkDialogResult CreateResult(
        string target,
        string? displayText,
        HyperlinkLinkType linkType = HyperlinkLinkType.ExistingFileOrWebPage,
        string? screenTip = "",
        string? bookmark = "")
    {
        var normalizedTarget = target.Trim();
        var normalizedDisplay = string.IsNullOrWhiteSpace(displayText)
            ? normalizedTarget
            : displayText.Trim();
        return new HyperlinkDialogResult(
            linkType,
            normalizedTarget,
            normalizedDisplay,
            (screenTip ?? "").Trim(),
            (bookmark ?? "").Trim());
    }

    private HyperlinkLinkType SelectedLinkType => _linkTypes.SelectedIndex switch
    {
        1 => HyperlinkLinkType.CreateNewDocument,
        2 => HyperlinkLinkType.PlaceInThisDocument,
        3 => HyperlinkLinkType.EmailAddress,
        _ => HyperlinkLinkType.ExistingFileOrWebPage
    };

    private void ScreenTipButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScreenTipDialog(_screenTip) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _screenTip = dialog.Result.Text;
        _screenTipButton.ToolTip = string.IsNullOrWhiteSpace(_screenTip) ? null : _screenTip;
    }

    private void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BookmarkDialog(_bookmark) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _bookmark = dialog.Result.Text;
        _bookmarkButton.ToolTip = string.IsNullOrWhiteSpace(_bookmark) ? null : _bookmark;
    }

    private static Grid DialogGrid(int inputRows)
    {
        var grid = new Grid { Margin = new Thickness(16) };
        for (var index = 0; index < inputRows; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddTextRow(Grid grid, int row, string label, TextBox box, string value)
    {
        grid.Children.Add(new Label
        {
            Content = label,
            Target = box,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);

        box.Text = value;
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
    }
}

public sealed class ScreenTipDialog : TextEntryDialog
{
    public ScreenTipDialog(string? initialText = "")
        : base("Set Hyperlink ScreenTip", "_ScreenTip text:", initialText)
    {
    }
}

public sealed class BookmarkDialog : TextEntryDialog
{
    public BookmarkDialog(string? initialText = "")
        : base("Select Place in Document", "_Bookmark or cell reference:", initialText)
    {
    }
}

public sealed record ObjectSizeDialogResult(double Width, double Height);

public sealed class ObjectSizeDialog : Window
{
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly CheckBox _lockAspectRatioBox = new() { Content = "_Lock aspect ratio", IsChecked = true };
    private readonly double _originalWidth;
    private readonly double _originalHeight;
    private bool _updatingSize;

    public ObjectSizeDialogResult Result { get; private set; }

    public ObjectSizeDialog(double width, double height, string title = "Object Size")
    {
        Result = new ObjectSizeDialogResult(width, height);
        _originalWidth = Math.Max(1, width);
        _originalHeight = Math.Max(1, height);
        Title = title;
        Width = 360;
        Height = 250;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _widthBox.Text = width.ToString(CultureInfo.InvariantCulture);
        _heightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        _widthBox.TextChanged += WidthBox_TextChanged;
        _heightBox.TextChanged += HeightBox_TextChanged;
        Content = CreateSizeContent(Accept);
    }

    public static bool TryParseSize(string input, out ObjectSizeDialogResult result)
    {
        result = new ObjectSizeDialogResult(0, 0);
        if (!DrawingInputParser.TryParseSize(input, out var width, out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        result = new ObjectSizeDialogResult(width, height);
        return true;
    }

    private void Accept()
    {
        if (!TryParseSize($"{_widthBox.Text}x{_heightBox.Text}", out var result))
            return;
        Result = result;
        DialogResult = true;
    }

    private void WidthBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _lockAspectRatioBox.IsChecked != true)
            return;

        if (!double.TryParse(_widthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) || width <= 0)
            return;

        SetHeight(CalculateLockedAspectHeight(width, _originalWidth, _originalHeight));
    }

    private void HeightBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _lockAspectRatioBox.IsChecked != true)
            return;

        if (!double.TryParse(_heightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) || height <= 0)
            return;

        SetWidth(CalculateLockedAspectWidth(height, _originalWidth, _originalHeight));
    }

    internal static double CalculateLockedAspectHeight(double width, double originalWidth, double originalHeight) =>
        originalWidth <= 0 || originalHeight <= 0 ? width : width * originalHeight / originalWidth;

    internal static double CalculateLockedAspectWidth(double height, double originalWidth, double originalHeight) =>
        originalWidth <= 0 || originalHeight <= 0 ? height : height * originalWidth / originalHeight;

    private void SetWidth(double width)
    {
        _updatingSize = true;
        try
        {
            _widthBox.Text = FormatSize(width);
        }
        finally
        {
            _updatingSize = false;
        }
    }

    private void SetHeight(double height)
    {
        _updatingSize = true;
        try
        {
            _heightBox.Text = FormatSize(height);
        }
        finally
        {
            _updatingSize = false;
        }
    }

    private static string FormatSize(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private StackPanel CreateSizeContent(Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        AddLabeledTextBox(stack, "Height:", _heightBox);
        AddLabeledTextBox(stack, "Width:", _widthBox);
        stack.Children.Add(_lockAspectRatioBox);
        stack.Children.Add(DialogButtonRowFactory.Create(accept, 72));
        return stack;
    }

    private static void AddLabeledTextBox(Panel stack, string label, TextBox box)
    {
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(box);
    }

    internal static StackPanel CreateSingleInputContent(string label, TextBox box, Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(box);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(accept));
        return stack;
    }
}

public sealed record RotationDialogResult(double Degrees);

public sealed class RotationDialog : Window
{
    private readonly TextBox _rotationBox = new();

    public RotationDialogResult Result { get; private set; }

    public RotationDialog(double degrees, string title = "Rotation")
    {
        Result = new RotationDialogResult(degrees);
        Title = title;
        Width = 300;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _rotationBox.Text = degrees.ToString(CultureInfo.InvariantCulture);
        Content = ObjectSizeDialog.CreateSingleInputContent("Degrees:", _rotationBox, Accept);
    }

    public static bool TryParseRotation(string input, out RotationDialogResult result)
    {
        result = new RotationDialogResult(0);
        if (!DrawingInputParser.TryParseRotationDegrees(input, out var value))
            return false;

        result = new RotationDialogResult(value);
        return true;
    }

    private void Accept()
    {
        if (!TryParseRotation(_rotationBox.Text, out var result))
            return;
        Result = result;
        DialogResult = true;
    }
}

public sealed record PictureCropDialogResult(double Left, double Top, double Right, double Bottom);

public sealed class PictureCropDialog : Window
{
    private readonly TextBox _cropLeftBox = new();
    private readonly TextBox _cropTopBox = new();
    private readonly TextBox _cropRightBox = new();
    private readonly TextBox _cropBottomBox = new();

    public PictureCropDialogResult Result { get; private set; }

    public PictureCropDialog(PictureModel picture)
    {
        Result = new PictureCropDialogResult(picture.CropLeft, picture.CropTop, picture.CropRight, picture.CropBottom);
        Title = "Crop Picture";
        Width = 420;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _cropLeftBox.Text = DrawingInputParser.FormatCropPercent(picture.CropLeft);
        _cropTopBox.Text = DrawingInputParser.FormatCropPercent(picture.CropTop);
        _cropRightBox.Text = DrawingInputParser.FormatCropPercent(picture.CropRight);
        _cropBottomBox.Text = DrawingInputParser.FormatCropPercent(picture.CropBottom);
        Content = CreateCropContent(Accept);
    }

    public static bool TryCreateResult(string input, out PictureCropDialogResult result, out string? error)
    {
        result = new PictureCropDialogResult(0, 0, 0, 0);
        error = null;
        if (!DrawingInputParser.TryParseCropPercents(input, out var left, out var top, out var right, out var bottom))
        {
            error = "Enter four crop percentages.";
            return false;
        }

        result = new PictureCropDialogResult(left, top, right, bottom);
        return true;
    }

    private void Accept()
    {
        var input = string.Join(", ", _cropLeftBox.Text, _cropTopBox.Text, _cropRightBox.Text, _cropBottomBox.Text);
        if (!TryCreateResult(input, out var result, out _))
            return;
        Result = result;
        DialogResult = true;
    }

    private StackPanel CreateCropContent(Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        AddCropBox(stack, "Left:", _cropLeftBox);
        AddCropBox(stack, "Top:", _cropTopBox);
        AddCropBox(stack, "Right:", _cropRightBox);
        AddCropBox(stack, "Bottom:", _cropBottomBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(accept));
        return stack;
    }

    private static void AddCropBox(Panel stack, string label, TextBox box)
    {
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(box);
    }
}

public sealed record TextEntryDialogResult(string Text);

public class TextEntryDialog : Window
{
    private readonly TextBox _textBox = new();

    public TextEntryDialogResult Result { get; private set; }

    public TextEntryDialog(string title, string label, string? initialText = "")
    {
        Result = CreateResult(initialText);
        Title = title;
        Width = 420;
        Height = 170;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _textBox.Text = initialText ?? "";
        Content = ObjectSizeDialog.CreateSingleInputContent(label, _textBox, () =>
        {
            Result = CreateResult(_textBox.Text);
            DialogResult = true;
        });
    }

    public static TextEntryDialogResult CreateResult(string? text) => new((text ?? "").Trim());
}
