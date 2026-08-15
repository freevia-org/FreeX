using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity drawing-object editing dialogs for the Avalonia/macOS shell: Format Picture / Format Shape
/// (size with lock-aspect syncing, rotation, alt text), Crop Picture (per-edge crop percentages), and the shape
/// Gradient Fill (start/end stop colors + direction with a live preview). Input collection lives here; all the
/// validation, parsing, aspect-ratio math, preset/direction catalogs, and result building come from portable
/// planners in <see cref="FreeX.App.Presentation.DrawingUI"/> and <see cref="FreeX.App.Services"/> so the behavior
/// is single-sourced with the WPF host and reusable on macOS. Results round-trip through the existing Core drawing commands (ResizePicture /
/// ResizeDrawingShape, SetPictureLockAspectRatio, SetDrawingObjectRotation, SetPicture/DrawingShapeAltText,
/// SetPictureCrop, SetDrawingShapeGradient). Reached from the Picture/Shape Format contextual-tab buttons.
/// </summary>
public sealed partial class MainWindow
{
    // -------------------------------------------------------------------------------------------------------
    // Drawing dialog chrome helpers
    // -------------------------------------------------------------------------------------------------------

    private static AvaloniaCompactDialogChromeStyle DrawingDialogChromeStyle => new(FormulaBarFontFamily);

    private static void ApplyDrawingButtonChrome(Button button, double width = 0, bool isDefault = false)
    {
        if (width > 0)
            button.Width = width;
        AvaloniaCompactDialogChrome.ApplyButton(button, DrawingDialogChromeStyle, width, isDefault);
    }

    private static void ApplyDrawingTextBoxChrome(TextBox tb)
        => AvaloniaCompactDialogChrome.ApplyTextBox(tb, DrawingDialogChromeStyle);

    private static void ApplyDrawingComboBoxChrome(ComboBox cb)
        => AvaloniaCompactDialogChrome.ApplyComboBox(cb, DrawingDialogChromeStyle);

    private static void ApplyDrawingCheckBoxChrome(CheckBox cb)
    {
        StripContentMnemonic(cb);
        AvaloniaCompactDialogChrome.ApplyCheckBox(cb, DrawingDialogChromeStyle);
    }

    // -------------------------------------------------------------------------------------------------------
    // Format Picture / Format Shape (size + rotation + alt text)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task OpenFormatPictureDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (ResolveSelectedFormatTarget() is not { } target)
            return;

        var state = FormatPicturePlanner.CreateDialogState(target.Values);
        var suppressSync = false;

        var widthBox = new TextBox { Text = state.WidthText, Width = 140 };
        ApplyDrawingTextBoxChrome(widthBox);
        AutomationProperties.SetAutomationId(widthBox, "FormatObjectWidthBox");
        AutomationProperties.SetName(widthBox, UiText.Get("FormatPicture_WidthLabel"));

        var heightBox = new TextBox { Text = state.HeightText, Width = 140 };
        ApplyDrawingTextBoxChrome(heightBox);
        AutomationProperties.SetAutomationId(heightBox, "FormatObjectHeightBox");
        AutomationProperties.SetName(heightBox, UiText.Get("FormatPicture_HeightLabel"));

        var rotationBox = new TextBox { Text = state.RotationText, Width = 140 };
        ApplyDrawingTextBoxChrome(rotationBox);
        AutomationProperties.SetAutomationId(rotationBox, "FormatObjectRotationBox");
        AutomationProperties.SetName(rotationBox, UiText.Get("FormatPicture_RotationLabel"));

        var lockAspectBox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("FormatPicture_LockAspectRatio")),
            IsChecked = state.LockAspectRatio,
            IsVisible = state.LockAspectRatioSupported,
        };
        ApplyDrawingCheckBoxChrome(lockAspectBox);
        AutomationProperties.SetAutomationId(lockAspectBox, "FormatObjectLockAspectBox");

        var altTextBox = new TextBox
        {
            Text = state.AltText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1),
        };
        AutomationProperties.SetAutomationId(altTextBox, "FormatObjectAltTextBox");
        AutomationProperties.SetName(altTextBox, UiText.Get("FormatPicture_AltTextLabel"));

        bool AspectLocked() => state.LockAspectRatioSupported && lockAspectBox.IsChecked == true;

        widthBox.TextChanged += (_, _) =>
        {
            if (suppressSync || !AspectLocked())
                return;
            if (FormatPicturePlanner.SyncHeightFromWidth(widthBox.Text, state.AspectRatio) is { } h)
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
            if (FormatPicturePlanner.SyncWidthFromHeight(heightBox.Text, state.AspectRatio) is { } w)
            {
                suppressSync = true;
                widthBox.Text = FormatPicturePlanner.FormatSize(w);
                suppressSync = false;
            }
        };

        var dialog = new FreeXDialogWindow(DrawingDialogChromeStyle)
        {
            Title = target.Kind == DrawingObjectTargetKind.Picture
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
        ApplyDrawingButtonChrome(ok, width: 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "FormatObjectOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyDrawingButtonChrome(cancel, width: 80);
        AutomationProperties.SetAutomationId(cancel, "FormatObjectCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            if (!FormatPicturePlanner.TryCreateResult(
                    new FormatPicturePlanner.FormatObjectSubmission(
                        widthBox.Text,
                        heightBox.Text,
                        rotationBox.Text,
                        lockAspectBox.IsChecked == true,
                        altTextBox.Text),
                    out _,
                    out var error))
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
        if (state.LockAspectRatioSupported)
            content.Children.Add(lockAspectBox);
        content.Children.Add(SectionHeader(UiText.Get("FormatPicture_AltTextHeader")));
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("FormatPicture_AltTextLabel"),
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(altTextBox);
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 8, 0, 0)));
        dialog.Content = content;

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        if (!FormatPicturePlanner.TryCreateResult(
                new FormatPicturePlanner.FormatObjectSubmission(
                    widthBox.Text,
                    heightBox.Text,
                    rotationBox.Text,
                    lockAspectBox.IsChecked == true,
                    altTextBox.Text),
                out var result,
                out _) ||
            result is null)
        {
            return;
        }

        ApplyFormatObjectResult(result, target);
    }

    /// <summary>
    /// Applies the validated Format dialog result as a small batch of existing Core commands: resize, then
    /// rotation, then (pictures only) lock-aspect-ratio, then alt text. The first failure stops and surfaces
    /// its message.
    /// </summary>
    private void ApplyFormatObjectResult(FormatPicturePlanner.FormatObjectResult result, DrawingObjectFormatTarget target)
    {
        var sheetId = _session.ActiveSheet.Id;
        foreach (var command in DrawingObjectFormatCommandPolicy.BuildFormatCommands(sheetId, target, result))
        {
            if (!RunFormatStep(command))
                return;
        }

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

        var dialog = new FreeXDialogWindow(DrawingDialogChromeStyle)
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
        ApplyDrawingButtonChrome(resetButton, width: 80);
        AutomationProperties.SetAutomationId(resetButton, "PictureCropResetButton");
        resetButton.Click += (_, _) =>
        {
            leftBox.Text = PictureCropDialogPlanner.FormatPercent(0);
            topBox.Text = PictureCropDialogPlanner.FormatPercent(0);
            rightBox.Text = PictureCropDialogPlanner.FormatPercent(0);
            bottomBox.Text = PictureCropDialogPlanner.FormatPercent(0);
        };

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplyDrawingButtonChrome(ok, width: 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PictureCropOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyDrawingButtonChrome(cancel, width: 80);
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
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([resetButton, ok, cancel], new Thickness(0, 8, 0, 0)));
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
            PictureCropDialogPlanner.BuildCommand(_session.ActiveSheet.Id, current.Id, result),
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

        _isPictureCropMode = true;
        RunDrawingObjectCommand(
            PictureCropDialogPlanner.BuildResetCommand(_session.ActiveSheet.Id, picture.Id),
            UiText.Get("PictureCrop_Applied"),
            "Crop Picture");
    }

    private static TextBox CropBox(string automationId, string text)
    {
        var box = new TextBox { Text = text, Width = 120, HorizontalAlignment = AvaloniaHorizontalAlignment.Left };
        ApplyDrawingTextBoxChrome(box);
        AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }

    // -------------------------------------------------------------------------------------------------------
    // Shape Effects (preset picker backed by ShapeEffectsPlanner)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task OpenShapeEffectsDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedShape() is not { } shape)
            return;

        var plan = ShapeEffectsPlanner.CreateResolvedPlan(shape.GetEffectiveEffectPreset(), UiText.Get);

        var effectBox = new ComboBox
        {
            ItemsSource = plan.Options,
            SelectedItem = plan.SelectedOption,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ShapeEffectsPlanner.ResolvedShapeEffectOption.Label)),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        effectBox.Margin = new Thickness(0, 0, 0, 10);
        ApplyDrawingComboBoxChrome(effectBox);
        AutomationProperties.SetName(effectBox, UiText.Get("ShapeEffects_EffectAutomationName"));
        AutomationProperties.SetAutomationId(effectBox, "ShapeEffectsPresetBox");
        AutomationProperties.SetHelpText(effectBox, UiText.Get("ShapeEffects_EffectHelpText"));

        var descriptionText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(descriptionText, UiText.Get("ShapeEffects_DescriptionAutomationName"));
        AutomationProperties.SetAutomationId(descriptionText, "ShapeEffectsDescriptionText");

        void UpdateDescription()
        {
            descriptionText.Text = effectBox.SelectedItem is ShapeEffectsPlanner.ResolvedShapeEffectOption choice
                ? choice.Description
                : string.Empty;
        }

        effectBox.SelectionChanged += (_, _) => UpdateDescription();

        var dialog = new FreeXDialogWindow(DrawingDialogChromeStyle)
        {
            Title = UiText.Get("ShapeEffects_Title"),
            Width = 380,
            Height = 190,
            Background = Brushes.White,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ShapeEffectsDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplyDrawingButtonChrome(ok, width: 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "ShapeEffectsOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyDrawingButtonChrome(cancel, width: 80);
        AutomationProperties.SetAutomationId(cancel, "ShapeEffectsCancelButton");
        cancel.Click += (_, _) => dialog.Close((DrawingShapeEffectPreset?)null);
        ok.Click += (_, _) => dialog.Close(
            effectBox.SelectedItem is ShapeEffectsPlanner.ResolvedShapeEffectOption choice
                ? (DrawingShapeEffectPreset?)choice.Preset
                : null);

        var content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 0,
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get("ShapeEffects_EffectLabel")),
                    Foreground = HeaderForeground,
                    FontSize = 16,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                effectBox,
                new Border
                {
                    Child = descriptionText,
                    Margin = new Thickness(0, 0, 0, 12),
                },
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]),
            },
        };
        dialog.Content = content;
        ConfigureDialogTabCycle(dialog, content);
        ConfigureNativeDialogInitialFocus(dialog, content, effectBox);

        UpdateDescription();

        var chosen = await dialog.ShowDialog<DrawingShapeEffectPreset?>(this);
        if (chosen is not { } preset)
            return;
        if (ResolveSelectedShape() is not { } current)
            return;

        var normalized = ShapeEffectsPlanner.NormalizePreset(preset);
        RunDrawingObjectCommand(
            ShapeEffectsPlanner.BuildCommand(_session.ActiveSheet.Id, current.Id, normalized),
            normalized == DrawingShapeEffectPreset.None
                ? UiText.Get("ShapeEffects_Cleared")
                : UiText.Format("ShapeEffects_Applied", ShapeEffectPresetLabel(normalized)),
            UiText.Get("InsertLoc_ShapeEffectsLabel"));
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
            Height = 42,
            BorderBrush = HeaderForeground,
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetAutomationId(preview, "ShapeGradientPreview");
        AutomationProperties.SetName(preview, UiText.Get("ShapeGradient_PreviewLabel"));

        var startSwatch = CreateGradientColorButton(
            "ShapeGradientStartColorButton",
            "ShapeGradient_ChooseStartColorAutomationName",
            "ShapeGradient_ChooseStartColorHelpText");
        var endSwatch = CreateGradientColorButton(
            "ShapeGradientEndColorButton",
            "ShapeGradient_ChooseEndColorAutomationName",
            "ShapeGradient_ChooseEndColorHelpText");
        var startBox = CreateGradientTextBox(FormatRgb(startColor));
        var endBox = CreateGradientTextBox(FormatRgb(endColor));
        AutomationProperties.SetAutomationId(startBox, "ShapeGradientStartColorBox");
        AutomationProperties.SetAutomationId(endBox, "ShapeGradientEndColorBox");

        var directionBox = new ComboBox
        {
            Width = 292,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        ApplyDrawingComboBoxChrome(directionBox);
        foreach (var option in directionOptions)
            directionBox.Items.Add(UiText.Get(option.LabelKey));
        directionBox.SelectedIndex = ShapeGradientPlanner.FindDirectionIndex(directionOptions, values.Direction);
        AutomationProperties.SetAutomationId(directionBox, "ShapeGradientDirectionBox");
        AutomationProperties.SetName(directionBox, UiText.Get("ShapeGradient_DirectionLabel"));

        DrawingShapeGradientDirection SelectedDirection() =>
            ShapeGradientPlanner.DirectionAt(directionOptions, directionBox.SelectedIndex);

        TextBlock? startSummaryRef = null;
        TextBlock? endSummaryRef = null;

        void UpdatePreview()
        {
            startSwatch.Background = SolidColor(startColor);
            endSwatch.Background = SolidColor(endColor);
            startBox.Text = FormatRgb(startColor);
            endBox.Text = FormatRgb(endColor);
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
            if (startSummaryRef is { } ss) ss.Text = $"Start: {FormatRgb(startColor)}";
            if (endSummaryRef is { } es) es.Text = $"End: {FormatRgb(endColor)}";
        }

        directionBox.SelectionChanged += (_, _) => UpdatePreview();
        preview.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty)
                UpdatePreview();
        };

        // Explicit Width+Height (rather than SizeToContent.Height) so the window fits its content snugly:
        // the headless parity-capture render reads dialog.Bounds verbatim and SizeToContent.Height did not
        // collapse there, leaving a large dead band below the OK/Cancel buttons. The height is sized to the
        // gradient-stops group (two stop rows + direction + preview) plus the Start/End summary and button
        // row, matching the compact Windows "Gradient Fill" dialog.
        var dialog = new FreeXDialogWindow(DrawingDialogChromeStyle)
        {
            Title = UiText.Get("ShapeGradient_Title"),
            Width = ShapeGradientPlanner.DialogWidth,
            Height = ShapeGradientPlanner.DialogHeight,
            MinWidth = ShapeGradientPlanner.DialogWidth,
            Background = Brushes.White,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ShapeGradientDialog");

        var ok = CreateGradientDialogButton(UiText.Get("Common_Ok"), isDefault: true);
        AutomationProperties.SetAutomationId(ok, "ShapeGradientOkButton");
        var cancel = CreateGradientDialogButton(UiText.Get("Common_Cancel"), isDefault: false);
        cancel.IsCancel = true;
        AutomationProperties.SetAutomationId(cancel, "ShapeGradientCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            if (!ColorInputParser.TryParseRgbColorText(startBox.Text ?? string.Empty, out startColor) ||
                !ColorInputParser.TryParseRgbColorText(endBox.Text ?? string.Empty, out endColor))
            {
                ShowEditIssue(UiText.Get("ShapeGradient_InvalidRgbColorMessage"));
                return;
            }

            dialog.Close(true);
        };

        startBox.LostFocus += (_, _) => { if (ColorInputParser.TryParseRgbColorText(startBox.Text ?? string.Empty, out var parsed)) { startColor = parsed; UpdatePreview(); } };
        endBox.LostFocus += (_, _) => { if (ColorInputParser.TryParseRgbColorText(endBox.Text ?? string.Empty, out var parsed)) { endColor = parsed; UpdatePreview(); } };

        var stopGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("136,40,*,54"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            Margin = new Thickness(0, 0, 0, 0),
        };

        AddGradientStopRow(stopGrid, 0, UiText.Get("ShapeGradient_Stop1ColorLabel"), startSwatch, startBox, "0%");
        AddGradientStopRow(stopGrid, 1, UiText.Get("ShapeGradient_Stop2ColorLabel"), endSwatch, endBox, "100%");
        AddGradientDirectionRow(stopGrid, directionBox);
        Grid.SetRow(preview, 3);
        Grid.SetColumnSpan(preview, 4);
        // Bottom margin keeps the preview's own 1px border clear of the GroupBox's bottom border so it
        // is not clipped (the Windows dialog shows a clear gap below the gradient bar).
        preview.Margin = new Thickness(0, 13, 0, 4);
        stopGrid.Children.Add(preview);

        startSwatch.Click += async (_, _) => await ChooseGradientColorAsync(UiText.Get("ShapeGradient_StartColorLabel"), c => startColor = c);
        endSwatch.Click += async (_, _) => await ChooseGradientColorAsync(UiText.Get("ShapeGradient_EndColorLabel"), c => endColor = c);

        var gradientGroup = new GroupBox
        {
            Header = StripDisplayMnemonic(UiText.Get("ShapeGradient_GradientStopsGroup")),
            Content = stopGrid,
            Width = 446,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 12),
        };

        var startSummary = new TextBlock { Text = $"Start: {FormatRgb(startColor)}", Foreground = Brushes.DimGray, FontSize = 12, FontFamily = FormulaBarFontFamily };
        var endSummary = new TextBlock { Text = $"End: {FormatRgb(endColor)}", Foreground = Brushes.DimGray, FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 2, 0, 6) };
        startSummaryRef = startSummary;
        endSummaryRef = endSummary;
        var content = new StackPanel { Spacing = 0, Margin = new Thickness(18, 16, 18, 8) };
        content.Children.Add(gradientGroup);
        content.Children.Add(startSummary);
        content.Children.Add(endSummary);
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 16, 15, 0)));
        dialog.Content = content;

        // Match the WPF dialog's Loaded focus contract: the first gradient-stop editor owns
        // initial keyboard focus and its value is selected for immediate replacement.
        dialog.Opened += (_, _) =>
        {
            startBox.Focus();
            startBox.SelectAll();
        };

        UpdatePreview();

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;
        if (ResolveSelectedShape() is not { } current)
            return;

        var result = ShapeGradientPlanner.CreateResult(startColor, endColor, SelectedDirection());
        RunDrawingObjectCommand(
            ShapeGradientPlanner.BuildCommand(_session.ActiveSheet.Id, current.Id, result),
            FormatDrawingObjectResourceText(DrawingObjectActionPlanner.ShapeGradientSuccess(
                FormatHex(result.StartColor),
                FormatHex(result.EndColor))),
            DrawingObjectActionPlanner.ShapeGradientCommandTitle);

        async System.Threading.Tasks.Task ChooseGradientColorAsync(string title, Action<CellColor> apply)
        {
            var current = title == UiText.Get("ShapeGradient_StartColorLabel") ? startColor : endColor;
            var chosen = await ShowMoreColorsDialogAsync(title, current);
            if (chosen is not { } c)
                return;

            apply(c);
            UpdatePreview();
        }
    }

    private static void AddGradientStopRow(Grid grid, int row, string label, Control swatch, TextBox box, string stopText)
    {
        var labelBlock = new TextBlock
        {
            Text = StripDisplayMnemonic(label),
            Foreground = HeaderForeground,
            FontSize = 12,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        swatch.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        swatch.Margin = new Thickness(0, 0, 8, 6);
        Grid.SetRow(swatch, row);
        Grid.SetColumn(swatch, 1);
        grid.Children.Add(swatch);

        box.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
        box.Margin = new Thickness(0, 0, 8, 6);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 2);
        grid.Children.Add(box);

        var stopBlock = new TextBlock
        {
            Text = stopText,
            Foreground = HeaderForeground,
            FontSize = 12,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(stopBlock, row);
        Grid.SetColumn(stopBlock, 3);
        grid.Children.Add(stopBlock);
    }

    private static void AddGradientDirectionRow(Grid grid, ComboBox directionBox)
    {
        var labelBlock = new TextBlock
        {
            Text = UiText.Get("ShapeGradient_DirectionLabel"),
            Foreground = HeaderForeground,
            FontSize = 12,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        Grid.SetRow(labelBlock, 2);
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        directionBox.Margin = new Thickness(0, 2, 0, 0);
        Grid.SetRow(directionBox, 2);
        Grid.SetColumn(directionBox, 1);
        Grid.SetColumnSpan(directionBox, 3);
        grid.Children.Add(directionBox);
    }

    private static TextBox CreateGradientTextBox(string text)
    {
        var box = new TextBox
        {
            Text = text,
            Width = 198,
        };
        ApplyDrawingTextBoxChrome(box);
        return box;
    }

    private static Button CreateGradientColorButton(
        string automationId,
        string automationNameKey,
        string helpTextKey)
    {
        var button = new Button
        {
            Width = 30,
            Height = 24,
            MinWidth = 30,
            Padding = new Thickness(0),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, UiText.Get(automationNameKey));
        AutomationProperties.SetHelpText(button, UiText.Get(helpTextKey));
        return button;
    }

    private static Button CreateGradientDialogButton(string text, bool isDefault)
    {
        var button = new Button
        {
            Content = text,
            IsDefault = isDefault,
            Width = 76,
        };
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DrawingDialogChromeStyle with { ButtonHeight = 22, ButtonPadding = new Thickness(8, 1) },
            76,
            isDefault);
        return button;
    }

    private static string FormatRgb(CellColor color) =>
        ColorInputParser.FormatRgbColor(color);

    private static bool TryParseRgb(string? text, out CellColor color) =>
        ColorInputParser.TryParseRgbColorText(text ?? string.Empty, out color);

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
            new TextBlock { Text = StripDisplayMnemonic(label), Width = 96, VerticalAlignment = AvaloniaVerticalAlignment.Center, Foreground = HeaderForeground, FontSize = 12, FontFamily = FormulaBarFontFamily },
            control,
        },
    };
}
