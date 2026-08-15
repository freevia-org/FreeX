using System.IO;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.Presentation.TableUI;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle InsertObjectDialogChromeStyle => new(FormulaBarFontFamily);

    private static void ApplyInsertObjectFixedButtonChrome(Button button, double width, bool isDefault = false)
    {
        button.Width = width;
        AvaloniaCompactDialogChrome.ApplyButton(button, InsertObjectDialogChromeStyle, width, isDefault);
    }

    private static void ApplyInsertObjectTextBoxChrome(TextBox textBox)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, InsertObjectDialogChromeStyle);

    private static void ApplyInsertObjectCheckBoxChrome(CheckBox checkBox)
        => AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, InsertObjectDialogChromeStyle);

    /// <summary>Builds the native Insert ▸ Shape submenu from the common-shapes catalog.</summary>
    private NativeMenu CreateNativeShapeMenu()
    {
        var menu = new NativeMenu();
        foreach (var group in DrawingInsertionPlanner.ShapeGroups)
        {
            var groupMenu = new NativeMenuItem
            {
                Header = group.Label,
                Menu = new NativeMenu(),
            };

            foreach (var item in group.Items)
            {
                var kind = item.Kind;
                var menuItem = new NativeMenuItem { Header = item.Label };
                menuItem.Click += (_, _) => InsertShapeAtActiveCell(kind);
                groupMenu.Menu.Items.Add(menuItem);
            }

            menu.Items.Add(groupMenu);
        }

        return menu;
    }

    private static readonly FilePickerFileType PictureFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            "Images",
            ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp", "*.tif", "*.tiff"],
            ["image/*"]);

    /// <summary>
    /// Inserts a picture chosen from a file onto the active sheet at the active cell, through the shared
    /// session command path and the Core <see cref="FreeX.Core.Commands.InsertPictureCommand"/> the drawing
    /// overlay already paints. The native pixel size is decoded via Avalonia (falling back to a default when
    /// decoding fails); the user can then move/resize it with the existing drawing-object editing. Surfaces
    /// the Core guard message on failure.
    /// </summary>
    private async Task InsertPictureFromFileAsync()
    {
        if (!((IStorageProvider)StorageProvider).CanOpen)
        {
            ShowEditIssue(UiText.Get("InsertLoc_PictureUnavailable"));
            return;
        }

        if (!TryCommitPendingFormulaEdit())
            return;

        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                UiText.Get("InsertLoc_InsertPictureTitle"),
                [PictureFileType]));

        if (file is null)
            return;

        var contentType = InsertPictureCommandFactory.ContentTypeForPath(file.Name);
        if (contentType is null)
        {
            ShowEditIssue(UiText.Get("InsertLoc_UnsupportedImageFormat"));
            return;
        }

        var readResult = await FileByteReadWorkflow.ReadStreamAsync(file.OpenReadAsync);
        if (readResult.Outcome == FileByteReadOutcome.Canceled)
            return;
        if (readResult.Outcome == FileByteReadOutcome.Failed)
        {
            ShowEditIssue(UiText.Format("InsertLoc_CouldNotReadImage", readResult.FailureMessage));
            return;
        }

        if (readResult.Outcome == FileByteReadOutcome.Empty)
        {
            ShowEditIssue(UiText.Get("InsertLoc_SelectedImageEmpty"));
            return;
        }

        var imageBytes = readResult.Bytes;
        var size = DecodePictureSize(imageBytes);
        var anchor = _session.ActiveCell;
        var command = PictureInsertionPlacementPlanner.CreateInsertPictureCommand(
            _session.ActiveSheet.Id, anchor, imageBytes, contentType, size);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("InsertLoc_InsertPictureFailed"));
            return;
        }

        ClearSelectedDrawingObject();
        RefreshShell(UiText.Format("InsertLoc_InsertedPictureAt", FormatCellReference(anchor)));
    }

    /// <summary>
    /// Inserts a drawing shape of <paramref name="kind"/> anchored at the active cell through the shared
    /// session command path and the Core <see cref="FreeX.Core.Commands.AddDrawingShapeCommand"/> the drawing
    /// overlay already renders. The shape is selectable and editable (move/resize/rotate) like other drawing
    /// objects. Surfaces the Core guard message on failure.
    /// </summary>
    private void InsertShapeAtActiveCell(DrawingShapeKind kind)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var anchor = _session.ActiveCell;
        var command = DrawingInsertionPlanner.BuildShapeCommand(_session.ActiveSheet.Id, anchor, kind);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("InsertLoc_InsertShapeFailed"));
            return;
        }

        _selectedDrawingObjectKind = FreeX.Core.Model.SelectionPaneObjectKind.Shape;
        _selectedDrawingObjectId = command.ShapeId;
        RefreshShell(FormatDrawingObjectResourceText(
            DrawingObjectActionPlanner.InsertShapeSuccess(kind, FormatCellReference(anchor))));
    }

    /// <summary>
    /// Inserts a text box anchored at the active cell through the shared session command path and the Core
    /// <see cref="FreeX.Core.Commands.AddTextBoxCommand"/> the drawing overlay already renders. The box is
    /// selectable and editable (move/resize/rotate) like other drawing objects. Surfaces the Core guard
    /// message on failure.
    /// </summary>
    private void InsertTextBoxAtActiveCell()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var anchor = _session.ActiveCell;
        var command = DrawingInsertionPlanner.BuildInlineEditTextBoxCommand(_session.ActiveSheet.Id, anchor);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("InsertLoc_InsertTextBoxFailed"));
            return;
        }

        ClearSelectedDrawingObject();
        BeginTextBoxInlineEdit(command.TextBoxId);
        RefreshShell(FormatDrawingObjectResourceText(
            DrawingObjectActionPlanner.InsertTextBoxSuccess(FormatCellReference(anchor))));
    }

    /// <summary>Decodes the image's native pixel size via Avalonia, or null when decoding fails.</summary>
    private static PictureInsertionSize? DecodePictureSize(byte[] imageBytes)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes);
            using var bitmap = new Bitmap(stream);
            return PictureInsertionPlacementPlanner.NormalizeSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts the current selection into a structured table through the shared session command path,
    /// reusing the Core <see cref="FreeX.Core.Commands.CreateStructuredTableCommand"/>. Header detection
    /// reuses the shell's <see cref="QuickAnalysisSelectionReader"/> heuristic so the menu and (future)
    /// Quick Analysis agree on whether the first row is a header; the Avalonia grid paints the table styling
    /// on the next refresh. Surfaces the Core guard message (e.g. range must include a header row and a data
    /// row) on failure rather than silently no-opping.
    /// </summary>
    private async Task InsertTableFromSelectionAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var sourceRange = TableCreationPlanner.PlanSourceRange(_session.ActiveSheet, _session.SelectedRange);
        var defaultRangeText = FormatRangeReference(sourceRange);
        var defaultStyle = TableStyleGalleryPlanner.GetOption(0, _session.Workbook.Theme);
        var plan = await ShowCreateTableDialogAsync(defaultRangeText, defaultStyle.StyleName);
        if (plan is null)
            return;

        var command = TableCreationPlanner.BuildStyledCommand(
            _session.ActiveSheet.Id,
            plan.Range,
            plan.TableStyleName,
            plan.FirstRowHasHeaders,
            defaultStyle.Banding);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("InsertLoc_InsertTableFailed"));
            return;
        }

        ClearSelectedDrawingObject();
        RefreshShell(UiText.Format("InsertLoc_CreatedTableFrom", FormatRangeReference(plan.Range)));
    }

    private async Task<CreateTableDialogPlan?> ShowCreateTableDialogAsync(string defaultRangeText, string tableStyleName)
    {
        CreateTableDialogPlan? result = null;
        var dialog = new FreeXDialogWindow(InsertObjectDialogChromeStyle)
        {
            Title = UiText.Get(CreateTableDialogPlanner.TitleKey),
            Width = CreateTableDialogPlanner.Width,
            Height = CreateTableDialogPlanner.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, CreateTableDialogPlanner.DialogAutomationId);

        var rangeBox = new TextBox
        {
            Text = defaultRangeText,
            MinWidth = CreateTableDialogPlanner.RangeBoxMinimumWidth
        };
        ApplyInsertObjectTextBoxChrome(rangeBox);
        // Lighter selection highlight so the (auto-selected) range text stays readable in black —
        // Avalonia's default accent selection is too dark for black text (matches Windows' lighter selection).
        rangeBox.SelectionBrush = Brush(173, 214, 255);
        AutomationProperties.SetName(rangeBox, UiText.Get(CreateTableDialogPlanner.RangeAutomationNameKey));
        AutomationProperties.SetAutomationId(rangeBox, CreateTableDialogPlanner.RangeBoxAutomationId);
        AutomationProperties.SetHelpText(rangeBox, UiText.Get(CreateTableDialogPlanner.RangeAutomationHelpTextKey));

        var rangePicker = new Button
        {
            Content = "...",
            Margin = new Thickness(0, 0, CreateTableDialogPlanner.RangePickerGap, 0),
        };
        ApplyInsertObjectFixedButtonChrome(rangePicker, CreateTableDialogPlanner.RangePickerWidth);
        AutomationProperties.SetName(rangePicker, UiText.Get(CreateTableDialogPlanner.RangePickerAutomationNameKey));
        AutomationProperties.SetAutomationId(rangePicker, "CreateTableRangePicker");

        var headersBox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get(CreateTableDialogPlanner.HeadersCheckBoxKey)),
            IsChecked = CreateTableDialogPlanner.DefaultFirstRowHasHeaders,
            Margin = new Thickness(0, 0, 0, CreateTableDialogPlanner.HeadersBottomMargin),
        };
        ApplyInsertObjectCheckBoxChrome(headersBox);
        AutomationProperties.SetName(headersBox, UiText.Get(CreateTableDialogPlanner.HeadersAutomationNameKey));
        AutomationProperties.SetAutomationId(headersBox, CreateTableDialogPlanner.HeadersBoxAutomationId);
        AutomationProperties.SetHelpText(headersBox, UiText.Get(CreateTableDialogPlanner.HeadersAutomationHelpTextKey));

        var okButton = new Button
        {
            Content = UiText.Get("Common_Ok"),
            IsDefault = true,
        };
        ApplyInsertObjectFixedButtonChrome(okButton, CreateTableDialogPlanner.ButtonWidth, isDefault: true);
        var cancelButton = new Button
        {
            Content = UiText.Get("Common_Cancel"),
            IsCancel = true,
        };
        ApplyInsertObjectFixedButtonChrome(cancelButton, CreateTableDialogPlanner.ButtonWidth);
        okButton.Click += async (_, _) =>
        {
            if (!CreateTableDialogPlanner.TryParse(
                    _session.ActiveSheet.Id,
                    rangeBox.Text ?? string.Empty,
                    headersBox.IsChecked == true,
                    tableStyleName,
                    out var parsed,
                    out var errorKey))
            {
                await AvaloniaUserMessageDialog.ShowWarningAsync(
                    dialog,
                    UiText.Get(errorKey ?? CreateTableDialogPlanner.InvalidRangeMessageKey),
                    UiText.Get(CreateTableDialogPlanner.TitleKey));
                rangeBox.Focus();
                rangeBox.SelectAll();
                return;
            }

            result = parsed;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [okButton, cancelButton],
            new Thickness(0, CreateTableDialogPlanner.ActionRowTopMargin, 0, 0));

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(CreateTableDialogPlanner.ContentMargin),
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get(CreateTableDialogPlanner.RangeLabelKey)),
                    Margin = new Thickness(0, 0, 0, CreateTableDialogPlanner.RangeLabelBottomMargin),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, CreateTableDialogPlanner.RangeEditorBottomMargin),
                    Children = { rangePicker, rangeBox },
                },
                headersBox,
                buttonRow,
            },
        };
        AttachDialogRangePicker(dialog, rangePicker, rangeBox, "range.create-table.range");
        ConfigureDialogCancelOnEscape(dialog, cancelButton);
        dialog.Opened += (_, _) =>
        {
            rangeBox.Focus();
            rangeBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private static readonly FilePickerFileType AnyFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType("All Files", ["*.*"]);

    /// <summary>
    /// Insert ▸ Object (create from file) — honest scope. The FreeX Core model has no editable embedded-OLE
    /// part (OLE XML is only preserved on XLSX round-trip, never authored from the UI), so true OLE embedding
    /// is NOT implemented. The realistic subset, decided by the portable <see cref="InsertObjectPlanner"/>:
    /// an image file is embedded as a real picture; any other file is placed as an icon/label placeholder
    /// picture carrying the file name (and, when "link" is chosen, the source path). The placeholder uses the
    /// existing <see cref="FreeX.Core.Commands.InsertPictureCommand"/> rendering path, so it is selectable and
    /// movable like other drawing objects. This method is only Avalonia chrome + platform glue.
    /// </summary>
    private async Task ShowInsertObjectDialogAsync()
    {
        if (!((IStorageProvider)StorageProvider).CanOpen)
        {
            ShowEditIssue(UiText.Get("WfInsertObject_Unavailable"));
            return;
        }

        if (!TryCommitPendingFormulaEdit())
            return;

        var linkCheck = new CheckBox { Content = UiText.Get("WfInsertObject_LinkLabel") };
        AutomationProperties.SetAutomationId(linkCheck, "WfInsertObjectLinkCheck");

        var pathBlock = new TextBlock
        {
            Text = UiText.Get("WfInsertObject_NoFileChosen"),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(pathBlock, "WfInsertObjectChosenPath");

        IStorageFile? chosen = null;

        var browse = new Button { Content = UiText.Get("WfInsertObject_BrowseButton"), Width = 110 };
        AutomationProperties.SetAutomationId(browse, "WfInsertObjectBrowseButton");
        var insert = new Button
        {
            Content = UiText.Get("WfInsertObject_InsertButton"),
            Width = 90,
            IsEnabled = false,
            IsDefault = true,
        };
        AutomationProperties.SetAutomationId(insert, "WfInsertObjectInsertButton");
        var cancel = new Button
        {
            Content = UiText.Get("WfInsertObject_CancelButton"),
            Width = 90,
            IsCancel = true,
        };
        AutomationProperties.SetAutomationId(cancel, "WfInsertObjectCancelButton");

        browse.Click += async (_, _) =>
        {
            chosen = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
                StorageProvider,
                AvaloniaFilePickerOpenRequest.FromFileTypes(
                    UiText.Get("WfInsertObject_Title"),
                    [AnyFileType]));

            if (chosen is not null)
            {
                pathBlock.Text = chosen.Name;
                insert.IsEnabled = true;
            }
        };

        var layout = new StackPanel { Margin = new Thickness(16), Spacing = 10, Width = 380 };
        layout.Children.Add(new TextBlock { Text = UiText.Get("WfInsertObject_Heading"), FontWeight = FontWeight.SemiBold });
        layout.Children.Add(browse);
        layout.Children.Add(pathBlock);
        layout.Children.Add(linkCheck);
        layout.Children.Add(new TextBlock
        {
            Text = UiText.Get("WfInsertObject_Note"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(120, 120, 120),
        });

        var dialog = new FreeXDialogWindow(InsertObjectDialogChromeStyle)
        {
            Title = UiText.Get("WfInsertObject_Title"),
            Width = 420,
            Height = 280,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "WfInsertObjectDialog");

        insert.Click += async (_, _) =>
        {
            if (chosen is null)
                return;
            if (await TryInsertObjectAsync(chosen, linkCheck.IsChecked == true))
                dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { insert, cancel },
        };
        layout.Children.Add(buttons);

        dialog.Content = layout;
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Validates the chosen file through the portable planner and inserts it: an image is embedded as a real
    /// picture; anything else becomes a generated icon/label placeholder picture. Returns true on success.
    /// </summary>
    private async Task<bool> TryInsertObjectAsync(IStorageFile file, bool linkToFile)
    {
        if (_isOpening || _isSaving)
            return false;

        var path = file.TryGetLocalPath() ?? file.Name;
        if (!InsertObjectPlanner.TryPlan(path, fileExists: true, linkToFile, out var plan, out var error))
        {
            ShowEditIssue(error switch
            {
                InsertObjectValidationError.MissingFilePath => UiText.Get("WfInsertObject_ErrorMissingFile"),
                InsertObjectValidationError.FileNotFound => UiText.Get("WfInsertObject_ErrorFileNotFound"),
                _ => UiText.Get("WfInsertObject_ErrorGeneric"),
            });
            return false;
        }

        byte[] imageBytes;
        string contentType;
        PictureInsertionSize? size;

        if (plan.Rendering == InsertObjectRendering.EmbedImageAsPicture && plan.ImageContentType is not null)
        {
            var readResult = await FileByteReadWorkflow.ReadStreamAsync(file.OpenReadAsync);
            if (readResult.Outcome == FileByteReadOutcome.Canceled)
                return false;
            if (readResult.Outcome == FileByteReadOutcome.Failed)
            {
                ShowEditIssue(UiText.Format("WfInsertObject_ErrorRead", readResult.FailureMessage));
                return false;
            }

            if (readResult.Outcome == FileByteReadOutcome.Empty)
            {
                ShowEditIssue(UiText.Get("WfInsertObject_ErrorEmptyFile"));
                return false;
            }

            imageBytes = readResult.Bytes;
            contentType = plan.ImageContentType;
            size = DecodePictureSize(imageBytes);
        }
        else
        {
            // Non-image file: render an icon/label placeholder PNG standing in for the object.
            imageBytes = RenderObjectIconPlaceholder(plan.DisplayName, plan.LinkToFile);
            contentType = "image/png";
            size = new PictureInsertionSize(ObjectIconWidth, ObjectIconHeight);
        }

        var anchor = _session.ActiveCell;
        var command = PictureInsertionPlacementPlanner.CreateInsertPictureCommand(
            _session.ActiveSheet.Id, anchor, imageBytes, contentType, size);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("WfInsertObject_ErrorGeneric"));
            return false;
        }

        // Record the object's identity on the inserted picture so the placeholder is self-describing and a
        // future true-OLE implementation can recognise it. The picture is the last one added on the sheet.
        var picture = _session.ActiveSheet.Pictures.LastOrDefault(p => p.Id == command.PictureId);
        if (picture is not null)
        {
            picture.Name = plan.DisplayName;
            picture.Title = plan.DisplayName;
            picture.AltText = plan.LinkToFile && plan.LinkPath is not null
                ? UiText.Format("WfInsertObject_AltLinked", plan.LinkPath)
                : UiText.Format("WfInsertObject_AltEmbedded", plan.DisplayName);
        }

        ClearSelectedDrawingObject();
        RefreshShell(plan.Rendering == InsertObjectRendering.EmbedImageAsPicture
            ? UiText.Format("WfInsertObject_StatusEmbedded", plan.DisplayName)
            : UiText.Format("WfInsertObject_StatusPlaceholder", plan.DisplayName));
        return true;
    }

    private const double ObjectIconWidth = 160d;
    private const double ObjectIconHeight = 120d;

    /// <summary>
    /// Renders a small document-icon-with-label PNG used as the placeholder for a non-image object. Drawn
    /// with Avalonia so the placeholder is real picture content the existing drawing overlay paints.
    /// </summary>
    private static byte[] RenderObjectIconPlaceholder(string label, bool linked)
    {
        const int pixelWidth = (int)ObjectIconWidth;
        const int pixelHeight = (int)ObjectIconHeight;
        var pixelSize = new PixelSize(pixelWidth, pixelHeight);
        using var target = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.White, new Rect(0, 0, pixelWidth, pixelHeight));
            ctx.DrawRectangle(null, new Pen(Brushes.Gray, 1), new Rect(0.5, 0.5, pixelWidth - 1, pixelHeight - 1));

            // A simple "page with folded corner" glyph.
            var pageFill = new SolidColorBrush(Color.FromRgb(0xE8, 0xEF, 0xF7));
            var pageStroke = new Pen(new SolidColorBrush(Color.FromRgb(0x5B, 0x7A, 0xA6)), 1.5);
            var page = new Rect(pixelWidth / 2.0 - 22, 14, 44, 56);
            ctx.FillRectangle(pageFill, page);
            ctx.DrawRectangle(null, pageStroke, page);
            var foldStart = new Point(page.Right - 12, page.Top);
            var fold = new PathGeometry();
            using (var gctx = fold.Open())
            {
                gctx.BeginFigure(foldStart, isFilled: true);
                gctx.LineTo(new Point(page.Right, page.Top + 12));
                gctx.LineTo(new Point(page.Right - 12, page.Top + 12));
                gctx.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xC6, 0xD6, 0xEA)), pageStroke, fold);

            var caption = linked ? UiText.Format("WfInsertObject_IconLinkedCaption", label) : label;
            var formatted = new FormattedText(
                caption,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11,
                Brushes.Black)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = pixelWidth - 8,
            };
            ctx.DrawText(formatted, new Point(4, 78));
        }

        using var output = new MemoryStream();
        target.Save(output);
        return output.ToArray();
    }
}
