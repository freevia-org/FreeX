using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Real wiring for the Picture Format (picture.selected) and Shape Format (shape.selected) contextual tabs.
/// Handlers operate on the currently-selected drawing object (<see cref="_selectedDrawingObjectKind"/> /
/// <see cref="_selectedDrawingObjectId"/>) and run Core commands through the shared session command path
/// (<see cref="WorkbookSession.ExecuteReviewCommand"/>). Where Core exposes a real command for the object kind
/// the button does the real thing (rotate / size / alt text for both kinds; z-order, fill, outline, gradient,
/// effects for shapes); where it does not (picture z-order, picture/shape crop, format-picture, selection
/// pane) it reports an honest, clearly-labeled status — no silent no-ops, no invented APIs.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Picture/Shape Format command id -> handler entries. Merge these into
    /// <see cref="BuildContextualTabCommands"/> (they replace the four Phase-1 picture/shape shells and add
    /// the new Arrange / Shape Styles / Accessibility commands).
    /// </summary>
    private IEnumerable<KeyValuePair<string, Action>> BuildPictureShapeTabCommands()
    {
        return new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            // --- Picture Format (picture.selected). ---
            ["pictureFormat.formatPicture"] = () => ReportContextualNotYetAvailable("Format Picture"),
            ["pictureFormat.crop"] = () => ReportContextualNotYetAvailable("Crop Picture"),
            // Picture z-order has no Core command yet (Core's z-order commands are shape-only); honest stub.
            ["pictureFormat.bringForward"] = () => ReportContextualNotYetAvailable("Bring Forward (pictures)"),
            ["pictureFormat.sendBackward"] = () => ReportContextualNotYetAvailable("Send Backward (pictures)"),
            ["pictureFormat.selectionPane"] = () => ReportContextualNotYetAvailable("Selection Pane"),
            ["pictureFormat.rotate"] = () => _ = RotateSelectedDrawingObjectAsync(),
            ["pictureFormat.size"] = () => _ = ResizeSelectedDrawingObjectAsync(),
            ["pictureFormat.altText"] = () => _ = EditSelectedDrawingObjectAltTextAsync(),

            // --- Shape Format (shape.selected). ---
            ["shapeFormat.shapeFill"] = () => _ = SetSelectedShapeFillColorAsync(),
            ["shapeFormat.shapeOutline"] = () => _ = SetSelectedShapeOutlineColorAsync(),
            ["shapeFormat.shapeGradient"] = () => _ = SetSelectedShapeGradientAsync(),
            ["shapeFormat.shapeEffectNone"] = () => ApplySelectedShapeEffect(DrawingShapeEffectPreset.None),
            ["shapeFormat.shapeEffectShadow"] = () => ApplySelectedShapeEffect(DrawingShapeEffectPreset.Shadow),
            ["shapeFormat.bringForward"] = () => BringSelectedShapeForward(),
            ["shapeFormat.sendBackward"] = () => SendSelectedShapeBackward(),
            ["shapeFormat.selectionPane"] = () => ReportContextualNotYetAvailable("Selection Pane"),
            ["shapeFormat.rotate"] = () => _ = RotateSelectedDrawingObjectAsync(),
            ["shapeFormat.size"] = () => _ = ResizeSelectedDrawingObjectAsync(),
            ["shapeFormat.altText"] = () => _ = EditSelectedDrawingObjectAltTextAsync(),
        };
    }

    // -------------------------------------------------------------------------------------------------------
    // Selected-object resolution
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the selected picture on the active sheet, or reports an explanatory status and returns null.
    /// </summary>
    private PictureModel? ResolveSelectedPicture()
    {
        if (_selectedDrawingObjectKind != SelectionPaneObjectKind.Picture || _selectedDrawingObjectId is not { } id)
        {
            RefreshShell("Select a picture first.");
            return null;
        }

        var picture = _session.ActiveSheet.Pictures.FirstOrDefault(p => p.Id == id);
        if (picture is null)
            RefreshShell("The selected picture is no longer available.");
        return picture;
    }

    /// <summary>
    /// Resolves the selected drawing shape on the active sheet, or reports an explanatory status and returns null.
    /// </summary>
    private DrawingShapeModel? ResolveSelectedShape()
    {
        if (_selectedDrawingObjectKind != SelectionPaneObjectKind.Shape || _selectedDrawingObjectId is not { } id)
        {
            RefreshShell("Select a shape first.");
            return null;
        }

        var shape = _session.ActiveSheet.DrawingShapes.FirstOrDefault(s => s.Id == id);
        if (shape is null)
            RefreshShell("The selected shape is no longer available.");
        return shape;
    }

    /// <summary>Runs a drawing-object command and reports success/failure on the status bar.</summary>
    private void RunDrawingObjectCommand(IWorkbookCommand command, string successStatus, string failurePrefix)
    {
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? $"{failurePrefix} failed.");
            return;
        }

        RefreshShell(successStatus);
    }

    // -------------------------------------------------------------------------------------------------------
    // Shape z-order (real: BringDrawingShapeForwardCommand / SendDrawingShapeBackwardCommand)
    // -------------------------------------------------------------------------------------------------------

    private void BringSelectedShapeForward()
    {
        if (ResolveSelectedShape() is not { } shape)
            return;

        RunDrawingObjectCommand(
            new BringDrawingShapeForwardCommand(_session.ActiveSheet.Id, shape.Id),
            "Brought shape forward.",
            "Bring Forward");
    }

    private void SendSelectedShapeBackward()
    {
        if (ResolveSelectedShape() is not { } shape)
            return;

        RunDrawingObjectCommand(
            new SendDrawingShapeBackwardCommand(_session.ActiveSheet.Id, shape.Id),
            "Sent shape backward.",
            "Send Backward");
    }

    // -------------------------------------------------------------------------------------------------------
    // Shape fill / outline / gradient / effects (real)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task SetSelectedShapeFillColorAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedShape() is not { } shape)
            return;

        var initial = shape.FillColor ?? DrawingShapeModel.DefaultFillColor;
        var color = await ShowMoreColorsDialogAsync("Shape Fill", initial);
        if (color is not { } chosen)
            return;
        if (ResolveSelectedShape() is not { } current)
            return;

        RunDrawingObjectCommand(
            new SetDrawingShapeColorsCommand(_session.ActiveSheet.Id, current.Id, fillColor: chosen, outlineColor: null, updateFill: true, updateOutline: false),
            $"Shape fill set to {FormatHex(chosen)}.",
            "Shape Fill");
    }

    private async System.Threading.Tasks.Task SetSelectedShapeOutlineColorAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedShape() is not { } shape)
            return;

        var initial = shape.OutlineColor ?? DrawingShapeModel.DefaultOutlineColor;
        var color = await ShowMoreColorsDialogAsync("Shape Outline", initial);
        if (color is not { } chosen)
            return;
        if (ResolveSelectedShape() is not { } current)
            return;

        RunDrawingObjectCommand(
            new SetDrawingShapeColorsCommand(_session.ActiveSheet.Id, current.Id, fillColor: null, outlineColor: chosen, updateFill: false, updateOutline: true),
            $"Shape outline set to {FormatHex(chosen)}.",
            "Shape Outline");
    }

    private async System.Threading.Tasks.Task SetSelectedShapeGradientAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedShape() is not { } shape)
            return;

        var startInitial = shape.FillColor ?? DrawingShapeModel.DefaultFillColor;
        var start = await ShowMoreColorsDialogAsync("Gradient Start Color", startInitial);
        if (start is not { } startColor)
            return;
        var end = await ShowMoreColorsDialogAsync("Gradient End Color", new CellColor(0xFF, 0xFF, 0xFF));
        if (end is not { } endColor)
            return;
        if (ResolveSelectedShape() is not { } current)
            return;

        RunDrawingObjectCommand(
            new SetDrawingShapeGradientCommand(_session.ActiveSheet.Id, current.Id, startColor, endColor),
            $"Shape gradient set ({FormatHex(startColor)} -> {FormatHex(endColor)}).",
            "Shape Gradient");
    }

    private void ApplySelectedShapeEffect(DrawingShapeEffectPreset preset)
    {
        if (ResolveSelectedShape() is not { } shape)
            return;

        RunDrawingObjectCommand(
            new SetDrawingShapeEffectCommand(_session.ActiveSheet.Id, shape.Id, preset),
            preset == DrawingShapeEffectPreset.None ? "Shape effect cleared." : $"Shape effect set to {preset}.",
            "Shape Effects");
    }

    // -------------------------------------------------------------------------------------------------------
    // Rotation (real for both kinds via SetDrawingObjectRotationCommand)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task RotateSelectedDrawingObjectAsync()
    {
        if (_isOpening || _isSaving)
            return;

        double current;
        SelectionPaneObjectKind kind;
        Guid id;
        switch (_selectedDrawingObjectKind)
        {
            case SelectionPaneObjectKind.Picture when ResolveSelectedPicture() is { } picture:
                current = picture.RotationDegrees;
                kind = SelectionPaneObjectKind.Picture;
                id = picture.Id;
                break;
            case SelectionPaneObjectKind.Shape when ResolveSelectedShape() is { } shape:
                current = shape.RotationDegrees;
                kind = SelectionPaneObjectKind.Shape;
                id = shape.Id;
                break;
            default:
                RefreshShell("Select a picture or shape first.");
                return;
        }

        var input = await ShowSingleValueDialogAsync(
            "Rotate Object",
            "Rotation (degrees):",
            current.ToString("0.##", CultureInfo.CurrentCulture));
        if (input is null)
            return;
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out var degrees))
        {
            ShowEditIssue("Enter a valid rotation in degrees.");
            return;
        }

        RunDrawingObjectCommand(
            new SetDrawingObjectRotationCommand(_session.ActiveSheet.Id, kind, id, degrees),
            $"Rotated object to {degrees:0.##} degrees.",
            "Rotate Object");
    }

    // -------------------------------------------------------------------------------------------------------
    // Size (real: ResizePictureCommand / ResizeDrawingShapeCommand)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task ResizeSelectedDrawingObjectAsync()
    {
        if (_isOpening || _isSaving)
            return;

        double width;
        double height;
        IWorkbookCommand BuildResize(double w, double h)
        {
            return _selectedDrawingObjectKind == SelectionPaneObjectKind.Picture
                ? new ResizePictureCommand(_session.ActiveSheet.Id, _selectedDrawingObjectId!.Value, w, h)
                : new ResizeDrawingShapeCommand(_session.ActiveSheet.Id, _selectedDrawingObjectId!.Value, w, h);
        }

        switch (_selectedDrawingObjectKind)
        {
            case SelectionPaneObjectKind.Picture when ResolveSelectedPicture() is { } picture:
                width = picture.Width;
                height = picture.Height;
                break;
            case SelectionPaneObjectKind.Shape when ResolveSelectedShape() is { } shape:
                width = shape.Width;
                height = shape.Height;
                break;
            default:
                RefreshShell("Select a picture or shape first.");
                return;
        }

        var size = await ShowSizeDialogAsync(width, height);
        if (size is not { } chosen)
            return;
        if (_selectedDrawingObjectId is null)
        {
            RefreshShell("The selected object is no longer available.");
            return;
        }

        RunDrawingObjectCommand(
            BuildResize(chosen.Width, chosen.Height),
            $"Resized object to {chosen.Width:0.##} x {chosen.Height:0.##}.",
            "Object Size");
    }

    // -------------------------------------------------------------------------------------------------------
    // Alt Text (real: SetPictureAltTextCommand / SetDrawingShapeAltTextCommand)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task EditSelectedDrawingObjectAltTextAsync()
    {
        if (_isOpening || _isSaving)
            return;

        string? current;
        IWorkbookCommand BuildAltText(string? text)
        {
            return _selectedDrawingObjectKind == SelectionPaneObjectKind.Picture
                ? new SetPictureAltTextCommand(_session.ActiveSheet.Id, _selectedDrawingObjectId!.Value, text)
                : new SetDrawingShapeAltTextCommand(_session.ActiveSheet.Id, _selectedDrawingObjectId!.Value, text);
        }

        switch (_selectedDrawingObjectKind)
        {
            case SelectionPaneObjectKind.Picture when ResolveSelectedPicture() is { } picture:
                current = picture.AltText;
                break;
            case SelectionPaneObjectKind.Shape when ResolveSelectedShape() is { } shape:
                current = shape.AltText;
                break;
            default:
                RefreshShell("Select a picture or shape first.");
                return;
        }

        var input = await ShowSingleValueDialogAsync(
            "Alt Text",
            "Describe this object for accessibility:",
            current ?? string.Empty,
            multiline: true);
        if (input is null)
            return;
        if (_selectedDrawingObjectId is null)
        {
            RefreshShell("The selected object is no longer available.");
            return;
        }

        RunDrawingObjectCommand(
            BuildAltText(input),
            string.IsNullOrWhiteSpace(input) ? "Alt text cleared." : "Alt text updated.",
            "Alt Text");
    }

    // -------------------------------------------------------------------------------------------------------
    // Small input dialogs (reuse the Avalonia custom-dialog idiom from MoreColors)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task<string?> ShowSingleValueDialogAsync(
        string title,
        string prompt,
        string initial,
        bool multiline = false)
    {
        var input = new TextBox
        {
            Text = initial,
            MinWidth = 260,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 64 : double.NaN,
        };
        AutomationProperties.SetAutomationId(input, "PictureShapeInputBox");

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PictureShapeInputDialog");

        var okButton = new Button { Content = "OK", Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, "PictureShapeInputOkButton");
        okButton.Click += (_, _) => dialog.Close(input.Text ?? string.Empty);

        var cancelButton = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "PictureShapeInputCancelButton");
        cancelButton.Click += (_, _) => dialog.Close((string?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            MinWidth = 300,
            Children =
            {
                new TextBlock { Text = prompt },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Children = { okButton, cancelButton },
                },
            },
        };

        return await dialog.ShowDialog<string?>(this);
    }

    private async System.Threading.Tasks.Task<(double Width, double Height)?> ShowSizeDialogAsync(
        double initialWidth,
        double initialHeight)
    {
        var widthBox = new TextBox
        {
            Text = initialWidth.ToString("0.##", CultureInfo.CurrentCulture),
            Width = 120,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(widthBox, "ObjectSizeWidthBox");

        var heightBox = new TextBox
        {
            Text = initialHeight.ToString("0.##", CultureInfo.CurrentCulture),
            Width = 120,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(heightBox, "ObjectSizeHeightBox");

        var warning = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        var dialog = new Window
        {
            Title = "Object Size",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ObjectSizeDialog");

        var okButton = new Button { Content = "OK", Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, "ObjectSizeOkButton");
        okButton.Click += (_, _) =>
        {
            if (!double.TryParse(widthBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var w) ||
                !double.TryParse(heightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var h) ||
                !(w > 0) || !(h > 0))
            {
                warning.Text = "Enter positive numbers for width and height.";
                warning.IsVisible = true;
                return;
            }

            dialog.Close(((double, double)?)(w, h));
        };

        var cancelButton = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "ObjectSizeCancelButton");
        cancelButton.Click += (_, _) => dialog.Close(((double, double)?)null);

        StackPanel Row(string label, TextBox box) => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = label, Width = 64, VerticalAlignment = AvaloniaVerticalAlignment.Center },
                box,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            MinWidth = 260,
            Children =
            {
                Row("Width:", widthBox),
                Row("Height:", heightBox),
                warning,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Children = { okButton, cancelButton },
                },
            },
        };

        var result = await dialog.ShowDialog<(double, double)?>(this);
        return result is { } tuple ? (tuple.Item1, tuple.Item2) : null;
    }
}
