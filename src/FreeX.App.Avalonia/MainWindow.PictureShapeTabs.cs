using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.DrawingUI;
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
/// (<see cref="WorkbookSession.ExecuteReviewCommand"/>). The shared contextual plan covers format/crop,
/// z-order, selection, rotation, size, alt text, fill, outline, gradient, and effect actions; this host only
/// adapts native dialogs and pointer-driven crop interaction to those shared operations.
/// </summary>
public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle PictureShapeDialogChromeStyle => new(FormulaBarFontFamily);

    /// <summary>
    /// Picture/Shape Format command id -> handler entries. Merge these into
    /// <see cref="BuildContextualTabCommands"/> to add the Arrange, Shape Styles, and Accessibility commands.
    /// </summary>
    private IEnumerable<KeyValuePair<string, Action>> BuildPictureShapeTabCommands()
    {
        var commands = new Dictionary<string, Action>(StringComparer.Ordinal);
        foreach (var spec in DrawingObjectContextualRibbonPlanner.CreatePictureShapeCommandSpecs())
            commands[spec.CommandId] = CreatePictureShapeTabCommand(spec);

        return commands;
    }

    private Action CreatePictureShapeTabCommand(DrawingObjectContextualCommandSpec spec) =>
        spec.Action switch
        {
            DrawingObjectContextualCommandAction.FormatPicture => () => RunGuarded(OpenFormatPictureDialogAsync),
            DrawingObjectContextualCommandAction.PictureCropMenuHint => BeginSelectedPictureCropMode,
            DrawingObjectContextualCommandAction.CropPicture => () => RunGuarded(OpenPictureCropDialogAsync),
            DrawingObjectContextualCommandAction.ResetPictureCrop => ResetSelectedPictureCrop,
            DrawingObjectContextualCommandAction.BringForward => () => ReorderSelectedDrawingObject(forward: true),
            DrawingObjectContextualCommandAction.SendBackward => () => ReorderSelectedDrawingObject(forward: false),
            DrawingObjectContextualCommandAction.SelectionPane => () => RunGuarded(OpenSelectionPaneDialogAsync),
            DrawingObjectContextualCommandAction.RotateObject => () => RunGuarded(RotateSelectedDrawingObjectAsync),
            DrawingObjectContextualCommandAction.ResizeObject => () => RunGuarded(ResizeSelectedDrawingObjectAsync),
            DrawingObjectContextualCommandAction.EditAltText => () => RunGuarded(EditSelectedDrawingObjectAltTextAsync),
            DrawingObjectContextualCommandAction.ShapeFill => () => RunGuarded(SetSelectedShapeFillColorAsync),
            DrawingObjectContextualCommandAction.ShapeOutline => () => RunGuarded(SetSelectedShapeOutlineColorAsync),
            DrawingObjectContextualCommandAction.ShapeGradient => () => RunGuarded(OpenShapeGradientDialogAsync),
            DrawingObjectContextualCommandAction.ShapeEffectsDialog => () => RunGuarded(OpenShapeEffectsDialogAsync),
            DrawingObjectContextualCommandAction.ShapeEffectPreset => () => ApplySelectedShapeEffect(spec.EffectPreset ?? DrawingShapeEffectPreset.None),
            _ => throw new NotSupportedException($"Unsupported picture/shape contextual action: {spec.Action}")
        };

    // -------------------------------------------------------------------------------------------------------
    // Selected-object resolution
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the selected picture on the active sheet, or reports an explanatory status and returns null.
    /// </summary>
    private RibbonCommandState GetDrawingObjectContextualRibbonCommandState(
        DrawingObjectContextualRibbonCommand command)
    {
        var plan = DrawingObjectContextualRibbonPlanner.Build(
            _session.ActiveSheet,
            _selectedDrawingObjectKind,
            _selectedDrawingObjectId);
        return new RibbonCommandState(IsEnabled: plan.IsEnabled(command));
    }

    private PictureModel? ResolveSelectedPicture()
    {
        var result = DrawingTargetResolver.ResolveSelectedPicture(
            _session.ActiveSheet,
            _selectedDrawingObjectKind,
            _selectedDrawingObjectId);
        if (result.Target is { } picture)
            return picture;

        if (result.Failure == DrawingObjectSelectionFailure.MissingSelection)
        {
            RefreshShell(UiText.Get("Drawing_SelectPictureFirst"));
            return null;
        }

        RefreshShell(UiText.Get("Drawing_ObjectNoLongerAvailable"));
        return null;
    }

    /// <summary>
    /// Resolves the selected drawing shape on the active sheet, or reports an explanatory status and returns null.
    /// </summary>
    private DrawingShapeModel? ResolveSelectedShape()
    {
        var result = DrawingTargetResolver.ResolveSelectedDrawingShape(
            _session.ActiveSheet,
            _selectedDrawingObjectKind,
            _selectedDrawingObjectId);
        if (result.Target is { } shape)
            return shape;

        if (result.Failure == DrawingObjectSelectionFailure.MissingSelection)
        {
            RefreshShell(UiText.Get("Drawing_SelectShapeFirst"));
            return null;
        }

        RefreshShell(UiText.Get("Drawing_ObjectNoLongerAvailable"));
        return null;
    }

    private DrawingObjectFormatTarget? ResolveSelectedFormatTarget()
    {
        var result = DrawingObjectFormatCommandPolicy.ResolveSelectedFormatTarget(
            _session.ActiveSheet,
            _selectedDrawingObjectKind,
            _selectedDrawingObjectId);
        if (result.Target is { } target)
            return target;

        RefreshShell(UiText.Get(result.Failure == DrawingObjectSelectionFailure.MissingSelection
            ? "Drawing_SelectObjectFirst"
            : "Drawing_ObjectNoLongerAvailable"));
        return null;
    }

    private DrawingObjectFormatTarget? ResolveSelectedFillOutlineTarget()
    {
        if (ResolveSelectedFormatTarget() is not { } target)
            return null;

        if (DrawingObjectFormatCommandPolicy.SupportsFillAndOutline(target.Kind))
            return target;

        RefreshShell(UiText.Get("Drawing_SelectObjectFirst"));
        return null;
    }

    /// <summary>Runs a drawing-object command and reports success/failure on the status bar.</summary>
    private void RunDrawingObjectCommand(IWorkbookCommand command, string successStatus, string failurePrefix)
    {
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Format("InsertLoc_DrawingCommandFailed", failurePrefix));
            return;
        }

        RefreshShell(successStatus);
    }

    private static string FormatDrawingObjectResourceText(DrawingObjectResourceText text) =>
        text.Arguments.Length == 0
            ? UiText.Get(text.ResourceKey)
            : UiText.Format(text.ResourceKey, text.Arguments);

    // -------------------------------------------------------------------------------------------------------
    // Drawing-object z-order (shared target policy + command planner)
    // -------------------------------------------------------------------------------------------------------

    // -------------------------------------------------------------------------------------------------------
    // Picture z-order (real: cross-kind MoveSelectionPaneObjectCommand, the same Core path as the Selection
    // Pane's one-step bring-forward / send-backward — so undo/redo and mixed-stack ordering behave identically)
    // -------------------------------------------------------------------------------------------------------

    private void ReorderSelectedDrawingObject(bool forward)
    {
        if (ResolveSelectedFormatTarget() is not { } target)
            return;

        RunDrawingObjectCommand(
            DrawingObjectCommandPlanner.BuildZOrderCommand(
                _session.ActiveSheet.Id,
                DrawingObjectCommandPlanner.ToSelectionPaneObjectKind(target.Kind),
                target.Id,
                forward),
            FormatDrawingObjectResourceText(DrawingObjectActionPlanner.ZOrderSuccess(target.Kind, forward)),
            forward ? UiText.Get("Drawing_BringForwardLabel") : UiText.Get("Drawing_SendBackwardLabel"));
    }

    // -------------------------------------------------------------------------------------------------------
    // Shape fill / outline / gradient / effects (real)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task SetSelectedShapeFillColorAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedFillOutlineTarget() is not { } target)
            return;

        var initial =
            DrawingObjectFormatCommandPolicy.ResolveFillColor(target.Target, _session.Workbook.Theme) ??
            DrawingShapeModel.ResolveDefaultFillColor(_session.Workbook.Theme);
        var color = await ShowMoreColorsDialogAsync(UiText.Get("InsertLoc_ShapeFillTitle"), initial);
        if (color is not { } chosen)
            return;
        if (ResolveSelectedFillOutlineTarget() is not { } current)
            return;

        RunDrawingObjectCommand(
            DrawingObjectCommandPlanner.BuildFillColorCommand(
                _session.ActiveSheet.Id,
                current.Kind,
                current.Id,
                chosen),
            FormatDrawingObjectResourceText(DrawingObjectActionPlanner.ShapeFillSuccess(FormatHex(chosen))),
            UiText.Get("InsertLoc_ShapeFillTitle"));
    }

    private async System.Threading.Tasks.Task SetSelectedShapeOutlineColorAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedFillOutlineTarget() is not { } target)
            return;

        var initial = DrawingObjectFormatCommandPolicy.ResolveOutlineColor(target.Target, _session.Workbook.Theme);
        var color = await ShowMoreColorsDialogAsync(UiText.Get("InsertLoc_ShapeOutlineTitle"), initial);
        if (color is not { } chosen)
            return;
        if (ResolveSelectedFillOutlineTarget() is not { } current)
            return;

        RunDrawingObjectCommand(
            DrawingObjectCommandPlanner.BuildOutlineColorCommand(
                _session.ActiveSheet.Id,
                current.Kind,
                current.Id,
                chosen),
            FormatDrawingObjectResourceText(DrawingObjectActionPlanner.ShapeOutlineSuccess(FormatHex(chosen))),
            UiText.Get("InsertLoc_ShapeOutlineTitle"));
    }

    private void ApplySelectedShapeEffect(DrawingShapeEffectPreset preset)
    {
        if (ResolveSelectedShape() is not { } shape)
            return;

        // Single-source the preset normalization through the portable ShapeEffectsPlanner so an unsupported
        // value collapses to None identically across shells.
        var normalized = ShapeEffectsPlanner.NormalizePreset(preset);
        var status = FormatDrawingObjectResourceText(
            DrawingObjectActionPlanner.ShapeEffectSuccess(normalized, ShapeEffectPresetLabel(normalized)));
        RunDrawingObjectCommand(
            ShapeEffectsPlanner.BuildCommand(_session.ActiveSheet.Id, shape.Id, normalized),
            status,
            UiText.Get("InsertLoc_ShapeEffectsLabel"));
    }

    /// <summary>Localized label for an effect preset, resolved from the shared ShapeEffectsPlanner catalog.</summary>
    private static string ShapeEffectPresetLabel(DrawingShapeEffectPreset preset)
    {
        foreach (var option in ShapeEffectsPlanner.CreateOptions())
        {
            if (option.Preset == preset)
                return UiText.Get(option.LabelKey);
        }

        return preset.ToString();
    }

    // -------------------------------------------------------------------------------------------------------
    // Rotation (real for both kinds via SetDrawingObjectRotationCommand)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task RotateSelectedDrawingObjectAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedFormatTarget() is not { } target)
            return;

        var input = await ShowSingleValueDialogAsync(
            UiText.Get("InsertLoc_RotateObjectTitle"),
            UiText.Get("InsertLoc_RotationDegreesPrompt"),
            target.Values.RotationDegrees.ToString("0.##", CultureInfo.CurrentCulture));
        if (input is null)
            return;
        if (!FormatPicturePlanner.TryCreateRotationResult(input, out var rotation) || rotation is null)
        {
            ShowEditIssue(UiText.Get("InsertLoc_EnterValidRotation"));
            return;
        }

        RunDrawingObjectCommand(
            DrawingObjectFormatCommandPolicy.BuildRotationCommand(_session.ActiveSheet.Id, target, rotation),
            FormatDrawingObjectResourceText(DrawingObjectActionPlanner.RotationSuccess(rotation)),
            UiText.Get("InsertLoc_RotateObjectTitle"));
    }

    // -------------------------------------------------------------------------------------------------------
    // Size (real: ResizePictureCommand / ResizeDrawingShapeCommand)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task ResizeSelectedDrawingObjectAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedFormatTarget() is not { } target)
            return;

        var size = await ShowSizeDialogAsync(target.Values.Width, target.Values.Height);
        if (size is not { } chosen)
            return;

        RunDrawingObjectCommand(
            DrawingObjectFormatCommandPolicy.BuildResizeCommand(
                _session.ActiveSheet.Id,
                target,
                chosen),
            FormatDrawingObjectResourceText(DrawingObjectActionPlanner.ResizeSuccess(chosen)),
            UiText.Get("InsertLoc_ObjectSizeTitle"));
    }

    // -------------------------------------------------------------------------------------------------------
    // Alt Text (real: SetPictureAltTextCommand / SetDrawingShapeAltTextCommand)
    // -------------------------------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task EditSelectedDrawingObjectAltTextAsync()
    {
        if (_isOpening || _isSaving)
            return;
        if (ResolveSelectedFormatTarget() is not { } target)
            return;

        var input = await ShowSingleValueDialogAsync(
            UiText.Get("InsertLoc_AltTextTitle"),
            UiText.Get("InsertLoc_AltTextPrompt"),
            target.Values.AltText,
            multiline: true);
        if (input is null)
            return;

        RunDrawingObjectCommand(
            DrawingObjectFormatCommandPolicy.BuildAltTextCommand(_session.ActiveSheet.Id, target, input),
            FormatDrawingObjectResourceText(DrawingObjectActionPlanner.AltTextSuccess(input)),
            UiText.Get("InsertLoc_AltTextTitle"));
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
            MinHeight = multiline ? 64 : 0,
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(input, PictureShapeDialogChromeStyle, fixedHeight: !multiline);
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

        var okButton = new Button { Content = UiText.Get("InsertLoc_OkButton"), Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, "PictureShapeInputOkButton");
        okButton.Click += (_, _) => dialog.Close(input.Text ?? string.Empty);

        var cancelButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "PictureShapeInputCancelButton");
        cancelButton.Click += (_, _) => dialog.Close((string?)null);
        AvaloniaCompactDialogChrome.ApplyButton(okButton, PictureShapeDialogChromeStyle, 80, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancelButton, PictureShapeDialogChromeStyle, 80);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            MinWidth = 300,
            Children =
            {
                new TextBlock { Text = prompt },
                input,
                AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]),
            },
        };

        return await dialog.ShowDialog<string?>(this);
    }

    private async System.Threading.Tasks.Task<ObjectSizeDialogSize?> ShowSizeDialogAsync(
        double initialWidth,
        double initialHeight)
    {
        var state = ObjectSizeDialogPlanner.CreateState(
            initialWidth,
            initialHeight,
            ObjectSizeDialogField.Width,
            ObjectSizeDialogField.Width,
            CultureInfo.CurrentCulture);
        var widthBox = new TextBox
        {
            Text = state.WidthText,
            Width = 120,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(widthBox, PictureShapeDialogChromeStyle);
        AutomationProperties.SetAutomationId(widthBox, "ObjectSizeWidthBox");

        var heightBox = new TextBox
        {
            Text = state.HeightText,
            Width = 120,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(heightBox, PictureShapeDialogChromeStyle);
        AutomationProperties.SetAutomationId(heightBox, "ObjectSizeHeightBox");

        var warning = new TextBlock();
        AvaloniaCompactDialogChrome.ApplyValidationStatus(warning, PictureShapeDialogChromeStyle);

        var dialog = new Window
        {
            Title = UiText.Get("InsertLoc_ObjectSizeTitle"),
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ObjectSizeDialog");

        var okButton = new Button { Content = UiText.Get("InsertLoc_OkButton"), Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, "ObjectSizeOkButton");
        okButton.Click += (_, _) =>
        {
            if (!ObjectSizeDialogPlanner.TryCreateSize(
                    new ObjectSizeDialogSubmission(widthBox.Text, heightBox.Text, state.FirstInvalidField),
                    out var size,
                    out _))
            {
                warning.Text = UiText.Get("InsertLoc_EnterPositiveSize");
                warning.IsVisible = true;
                return;
            }

            dialog.Close((ObjectSizeDialogSize?)size);
        };

        var cancelButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "ObjectSizeCancelButton");
        cancelButton.Click += (_, _) => dialog.Close((ObjectSizeDialogSize?)null);
        AvaloniaCompactDialogChrome.ApplyButton(okButton, PictureShapeDialogChromeStyle, 80, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancelButton, PictureShapeDialogChromeStyle, 80);

        StackPanel Row(string label, TextBox box) => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = StripDisplayMnemonic(label), Width = 64, VerticalAlignment = AvaloniaVerticalAlignment.Center },
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
                Row(UiText.Get("InsertLoc_WidthLabel"), widthBox),
                Row(UiText.Get("InsertLoc_HeightLabel"), heightBox),
                warning,
                AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]),
            },
        };

        return await dialog.ShowDialog<ObjectSizeDialogSize?>(this);
    }
}
