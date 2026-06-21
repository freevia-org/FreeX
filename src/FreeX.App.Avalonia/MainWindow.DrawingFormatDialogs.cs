using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity drawing-object editing dialogs for the Avalonia/macOS shell: Format Picture / Format Shape
/// (size with lock-aspect syncing, rotation, alt text), Crop Picture (per-edge crop percentages), and the shape
/// Gradient Fill (start/end stop colors + direction with a live preview). Input collection lives here; all the
/// validation, parsing, aspect-ratio math, preset/direction catalogs, and result building come from the portable
/// planners in <see cref="FreeX.App.Presentation.DrawingUI"/> so the behavior is single-sourced with the WPF host
/// and reusable on macOS. Results round-trip through the existing Core drawing commands (ResizePicture /
/// ResizeDrawingShape, SetPictureLockAspectRatio, SetDrawingObjectRotation, SetPicture/DrawingShapeAltText,
/// SetPictureCrop, SetDrawingShapeGradient). Reached from the Picture/Shape Format contextual-tab buttons.
/// </summary>
public sealed partial class MainWindow
{
    // -------------------------------------------------------------------------------------------------------
    // Format Picture / Format Shape (size + rotation + alt text)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task OpenFormatPictureDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        FormatPicturePlanner.FormatObjectValues values;
        bool isPicture;
        switch (_selectedDrawingObjectKind)
        {
            case SelectionPaneObjectKind.Picture when ResolveSelectedPicture() is { } picture:
                values = FormatPicturePlanner.Capture(picture);
                isPicture = true;
                break;
            case SelectionPaneObjectKind.Shape when ResolveSelectedShape() is { } shape:
                values = FormatPicturePlanner.Capture(shape);
                isPicture = false;
                break;
            default:
                RefreshShell(UiText.Get("Drawing_SelectObjectFirst"));
                return;
        }

        var aspectRatio = FormatPicturePlanner.AspectRatio(values.Width, values.Height);
        var suppressSync = false;

        var widthBox = new TextBox { Text = FormatPicturePlanner.FormatSize(values.Width), Width = 140 };
        AutomationProperties.SetAutomationId(widthBox, "FormatObjectWidthBox");
        AutomationProperties.SetName(widthBox, UiText.Get("FormatPicture_WidthLabel"));

        var heightBox = new TextBox { Text = FormatPicturePlanner.FormatSize(values.Height), Width = 140 };
        AutomationProperties.SetAutomationId(heightBox, "FormatObjectHeightBox");
        AutomationProperties.SetName(heightBox, UiText.Get("FormatPicture_HeightLabel"));

        var rotationBox = new TextBox { Text = FormatPicturePlanner.FormatRotation(values.RotationDegrees), Width = 140 };
        AutomationProperties.SetAutomationId(rotationBox, "FormatObjectRotationBox");
        AutomationProperties.SetName(rotationBox, UiText.Get("FormatPicture_RotationLabel"));

        var lockAspectBox = new CheckBox
        {
            Content = UiText.Get("FormatPicture_LockAspectRatio"),
            IsChecked = values.LockAspectRatio,
            IsVisible = values.LockAspectRatioSupported,
        };
        AutomationProperties.SetAutomationId(lockAspectBox, "FormatObjectLockAspectBox");

        var altTextBox = new TextBox
        {
            Text = values.AltText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
        };
        AutomationProperties.SetAutomationId(altTextBox, "FormatObjectAltTextBox");
        AutomationProperties.SetName(altTextBox, UiText.Get("FormatPicture_AltTextLabel"));

        bool AspectLocked() => values.LockAspectRatioSupported && lockAspectBox.IsChecked == true;

        widthBox.TextChanged += (_, _) =>
        {
            if (suppressSync || !AspectLocked())
                return;
            if (FormatPicturePlanner.SyncHeightFromWidth(widthBox.Text, aspectRatio) is { } h)
            {
                suppressSync = true;
                heightBox.Text = FormatPicturePlanner.FormatSize(h);
                suppressSync = false;
            }
        };
        heightBox.TextChanged += (_, _) =>
        {
            if (suppressSync || !AspectLocked())
                return;
            if (FormatPicturePlanner.SyncWidthFromHeight(heightBox.Text, aspectRatio) is { } w)
            {
                suppressSync = true;
                widthBox.Text = FormatPicturePlanner.FormatSize(w);
                suppressSync = false;
            }
        };

        var dialog = new Window
        {
            Title = isPicture
                ? UiText.Get("FormatPicture_PictureTitle")
                : UiText.Get("FormatPicture_ShapeTitle"),
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "FormatObjectDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "FormatObjectOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "FormatObjectCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            if (!FormatPicturePlanner.TryCreateResult(
                    widthBox.Text, heightBox.Text, rotationBox.Text,
                    lockAspectBox.IsChecked == true, altTextBox.Text, out _, out var error))
            {
                ShowEditIssue(error ?? FormatPicturePlanner.InvalidSizeMessage);
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(SectionHeader(UiText.Get("FormatPicture_SizeHeader")));
        content.Children.Add(FormRow(UiText.Get("FormatPicture_WidthLabel"), widthBox));
        content.Children.Add(FormRow(UiText.Get("FormatPicture_HeightLabel"), heightBox));
        content.Children.Add(FormRow(UiText.Get("FormatPicture_RotationLabel"), rotationBox));
        if (values.LockAspectRatioSupported)
            content.Children.Add(lockAspectBox);
        content.Children.Add(SectionHeader(UiText.Get("FormatPicture_AltTextHeader")));
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("FormatPicture_AltTextLabel"),
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(altTextBox);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = content;

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        if (!FormatPicturePlanner.TryCreateResult(
                widthBox.Text, heightBox.Text, rotationBox.Text,
                lockAspectBox.IsChecked == true, altTextBox.Text, out var result, out _) ||
            result is null)
        {
            return;
        }

        ApplyFormatObjectResult(result, isPicture);
    }

    /// <summary>
    /// Applies the validated Format dialog result as a small batch of existing Core commands: resize, then
    /// rotation, then (pictures only) lock-aspect-ratio, then alt text. Each runs only when its value actually
    /// changed so undo history stays tight; the first failure stops and surfaces its message.
    /// </summary>
    private void ApplyFormatObjectResult(FormatPicturePlanner.FormatObjectResult result, bool isPicture)
    {
        if (_selectedDrawingObjectId is not { } id)
        {
            RefreshShell(UiText.Get("Drawing_ObjectNoLongerAvailable"));
            return;
        }

        var sheetId = _session.ActiveSheet.Id;
        var kind = isPicture ? SelectionPaneObjectKind.Picture : SelectionPaneObjectKind.Shape;

        if (!RunFormatStep(DrawingObjectCommandPlanner.BuildResizeCommand(sheetId, kind, id, result.Width, result.Height)))
            return;

        if (!RunFormatStep(DrawingObjectCommandPlanner.BuildRotateCommand(sheetId, kind, id, result.RotationDegrees)))
            return;

        if (isPicture &&
            !RunFormatStep(new SetPictureLockAspectRatioCommand(sheetId, id, result.LockAspectRatio)))
            return;

        if (!RunFormatStep(DrawingObjectCommandPlanner.BuildAltTextCommand(sheetId, kind, id, result.AltText)))
            return;

        RefreshShell(UiText.Get("FormatPicture_Applied"));
    }

    /// <summary>Runs one Format-dialog command, surfacing the error and returning false on failure.</summary>
    private bool RunFormatStep(IWorkbookCommand command)
    {
        var outcome = _session.ExecuteReviewCommand(command);
        if (!outcome.Success)
        {
            ShowEditIssue(outcome.ErrorMessage ?? UiText.Get("FormatPicture_Applied"));
            return false;
        }

        return true;
    }

    // -------------------------------------------------------------------------------------------------------
    // Crop Picture (per-edge crop percentages)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task OpenPictureCropDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedPicture() is not { } picture)
            return;

        var values = PictureCropDialogPlanner.Capture(picture);
        if (!values.IsCroppable)
        {
            ShowEditIssue(PictureCropDialogPlanner.NotImageMessage);
            return;
        }

        var leftBox = CropBox("PictureCropLeftBox", PictureCropDialogPlanner.FormatPercent(values.Left));
        var topBox = CropBox("PictureCropTopBox", PictureCropDialogPlanner.FormatPercent(values.Top));
        var rightBox = CropBox("PictureCropRightBox", PictureCropDialogPlanner.FormatPercent(values.Right));
        var bottomBox = CropBox("PictureCropBottomBox", PictureCropDialogPlanner.FormatPercent(values.Bottom));

        var dialog = new Window
        {
            Title = UiText.Get("PictureCrop_Title"),
            Width = 320,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PictureCropDialog");

        var resetButton = new Button { Content = UiText.Get("PictureCrop_Reset"), MinWidth = 80 };
        AutomationProperties.SetAutomationId(resetButton, "PictureCropResetButton");
        resetButton.Click += (_, _) =>
        {
            leftBox.Text = PictureCropDialogPlanner.FormatPercent(0);
            topBox.Text = PictureCropDialogPlanner.FormatPercent(0);
            rightBox.Text = PictureCropDialogPlanner.FormatPercent(0);
            bottomBox.Text = PictureCropDialogPlanner.FormatPercent(0);
        };

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "PictureCropOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "PictureCropCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            if (!PictureCropDialogPlanner.TryCreateResult(
                    leftBox.Text, topBox.Text, rightBox.Text, bottomBox.Text, out _, out var error))
            {
                ShowEditIssue(error ?? PictureCropDialogPlanner.InvalidPercentMessage);
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(FormRow(UiText.Get("PictureCrop_LeftLabel"), leftBox));
        content.Children.Add(FormRow(UiText.Get("PictureCrop_TopLabel"), topBox));
        content.Children.Add(FormRow(UiText.Get("PictureCrop_RightLabel"), rightBox));
        content.Children.Add(FormRow(UiText.Get("PictureCrop_BottomLabel"), bottomBox));
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { resetButton, ok, cancel },
        });
        dialog.Content = content;

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        if (!PictureCropDialogPlanner.TryCreateResult(
                leftBox.Text, topBox.Text, rightBox.Text, bottomBox.Text, out var result, out _) ||
            result is null)
        {
            return;
        }

        if (ResolveSelectedPicture() is not { } current)
            return;

        RunDrawingObjectCommand(
            new SetPictureCropCommand(_session.ActiveSheet.Id, current.Id, result.Left, result.Top, result.Right, result.Bottom),
            UiText.Get("PictureCrop_Applied"),
            "Crop Picture");
    }

    /// <summary>"Reset Crop" menu item — clears all four crop edges on the selected image picture.</summary>
    private void ResetSelectedPictureCrop()
    {
        if (ResolveSelectedPicture() is not { } picture)
            return;
        if (picture.Kind != PictureKind.Image)
        {
            ShowEditIssue(PictureCropDialogPlanner.NotImageMessage);
            return;
        }

        RunDrawingObjectCommand(
            new SetPictureCropCommand(_session.ActiveSheet.Id, picture.Id, 0, 0, 0, 0),
            UiText.Get("PictureCrop_Applied"),
            "Crop Picture");
    }

    private static TextBox CropBox(string automationId, string text)
    {
        var box = new TextBox { Text = text, Width = 120, HorizontalAlignment = AvaloniaHorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }

    // -------------------------------------------------------------------------------------------------------
    // Shape Gradient (start/end stop colors + direction with live preview)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task OpenShapeGradientDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedShape() is not { } shape)
            return;

        var values = ShapeGradientPlanner.Capture(shape);
        var directionOptions = ShapeGradientPlanner.CreateDirectionOptions();
        var startColor = values.StartColor;
        var endColor = values.EndColor;

        var preview = new Border
        {
            Height = 40,
            BorderBrush = HeaderForeground,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
        };
        AutomationProperties.SetAutomationId(preview, "ShapeGradientPreview");
        AutomationProperties.SetName(preview, UiText.Get("ShapeGradient_PreviewLabel"));

        var startSwatch = new Border { Width = 26, Height = 20, BorderBrush = HeaderForeground, BorderThickness = new Thickness(1) };
        var endSwatch = new Border { Width = 26, Height = 20, BorderBrush = HeaderForeground, BorderThickness = new Thickness(1) };

        var directionBox = new ComboBox { MinWidth = 180 };
        foreach (var option in directionOptions)
            directionBox.Items.Add(UiText.Get(option.LabelKey));
        directionBox.SelectedIndex = ShapeGradientPlanner.FindDirectionIndex(directionOptions, values.Direction);
        AutomationProperties.SetAutomationId(directionBox, "ShapeGradientDirectionBox");
        AutomationProperties.SetName(directionBox, UiText.Get("ShapeGradient_DirectionLabel"));

        DrawingShapeGradientDirection SelectedDirection() =>
            ShapeGradientPlanner.DirectionAt(directionOptions, directionBox.SelectedIndex);

        void UpdatePreview()
        {
            startSwatch.Background = SolidColor(startColor);
            endSwatch.Background = SolidColor(endColor);
            var (sx, sy, ex, ey) = ShapeGradientPlanner.PreviewVector(SelectedDirection(), preview.Bounds.Width, preview.Bounds.Height);
            preview.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(sx, sy, RelativeUnit.Relative),
                EndPoint = new RelativePoint(ex, ey, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(ToColor(startColor), 0),
                    new GradientStop(ToColor(endColor), 1),
                },
            };
        }

        directionBox.SelectionChanged += (_, _) => UpdatePreview();
        preview.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty)
                UpdatePreview();
        };

        var startButton = new Button { Content = UiText.Get("ShapeGradient_ChooseStartColor"), MinWidth = 130 };
        AutomationProperties.SetAutomationId(startButton, "ShapeGradientStartColorButton");
        startButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ShapeGradient_StartColorLabel"), startColor);
            if (chosen is { } c)
            {
                startColor = c;
                UpdatePreview();
            }
        };

        var endButton = new Button { Content = UiText.Get("ShapeGradient_ChooseEndColor"), MinWidth = 130 };
        AutomationProperties.SetAutomationId(endButton, "ShapeGradientEndColorButton");
        endButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ShapeGradient_EndColorLabel"), endColor);
            if (chosen is { } c)
            {
                endColor = c;
                UpdatePreview();
            }
        };

        var dialog = new Window
        {
            Title = UiText.Get("ShapeGradient_Title"),
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ShapeGradientDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "ShapeGradientOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "ShapeGradientCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) => dialog.Close(true);

        StackPanel ColorRow(string label, Border swatch, Button button) => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = label, Width = 96, VerticalAlignment = AvaloniaVerticalAlignment.Center, Foreground = HeaderForeground },
                swatch,
                button,
            },
        };

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(ColorRow(UiText.Get("ShapeGradient_StartColorLabel"), startSwatch, startButton));
        content.Children.Add(ColorRow(UiText.Get("ShapeGradient_EndColorLabel"), endSwatch, endButton));
        content.Children.Add(FormRow(UiText.Get("ShapeGradient_DirectionLabel"), directionBox));
        content.Children.Add(new TextBlock { Text = UiText.Get("ShapeGradient_PreviewLabel"), Foreground = HeaderForeground });
        content.Children.Add(preview);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = content;

        UpdatePreview();

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;
        if (ResolveSelectedShape() is not { } current)
            return;

        var result = ShapeGradientPlanner.CreateResult(startColor, endColor, SelectedDirection());
        RunDrawingObjectCommand(
            new SetDrawingShapeGradientCommand(_session.ActiveSheet.Id, current.Id, result.StartColor, result.EndColor, result.Direction),
            UiText.Format("ShapeGradient_Applied", FormatHex(result.StartColor), FormatHex(result.EndColor)),
            "Shape Gradient");
    }

    private static IBrush SolidColor(CellColor color) => new SolidColorBrush(ToColor(color));

    private static Color ToColor(CellColor color) => Color.FromRgb(color.R, color.G, color.B);

    /// <summary>Shared label-left / control-right row used by the drawing dialogs.</summary>
    private static StackPanel FormRow(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        VerticalAlignment = AvaloniaVerticalAlignment.Center,
        Children =
        {
            new TextBlock { Text = label, Width = 96, VerticalAlignment = AvaloniaVerticalAlignment.Center, Foreground = HeaderForeground },
            control,
        },
    };
}
