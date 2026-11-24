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
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.Presentation.TableUI;
using FreeX.App.Services;
using FreeX.Core.Commands;
using CellAddress = FreeX.Core.Model.CellAddress;
using Sheet = FreeX.Core.Model.Sheet;
using SheetId = FreeX.Core.Model.SheetId;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle InsertObjectDialogChromeStyle => new(FormulaBarFontFamily);

    // WPF CreateTableDialog leaves its TextBox, DialogReferencePicker button and DialogButtonRowFactory
    // buttons at their native 12 px heights (about 20 px), not the generic 24/26 px compact family.
    private static AvaloniaCompactDialogChromeStyle CreateTableDialogChromeStyle =>
        new(FormulaBarFontFamily)
        {
            ControlHeight = 20,
            TextBoxHeight = 20,
            ButtonHeight = 20,
            ButtonPadding = new Thickness(1),
            TextBoxPadding = new Thickness(2, 0),
        };

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

    // Keep the native item alive through its read without implementing Avalonia's non-client interfaces.
    internal sealed class InsertObjectPickedFile : IDisposable
    {
        private readonly Func<Task<Stream>> _openRead;
        private readonly IDisposable _owner;
        private int _disposed;

        internal InsertObjectPickedFile(string name, string? localPath, Func<Task<Stream>> openRead, IDisposable owner)
        {
            Name = name;
            LocalPath = localPath;
            _openRead = openRead;
            _owner = owner;
        }

        internal string Name { get; }
        internal string? LocalPath { get; }
        internal Task<Stream> OpenReadAsync()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _openRead();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.Dispose();
        }
    }

    partial void ResolveInsertObjectFilePicker(
        ref Func<AvaloniaFilePickerOpenRequest, Task<InsertObjectPickedFile?>>? picker);

    private bool CanPickInsertObjectFile()
    {
        Func<AvaloniaFilePickerOpenRequest, Task<InsertObjectPickedFile?>>? picker = null;
        ResolveInsertObjectFilePicker(ref picker);
        return picker is not null || StorageProvider.CanOpen;
    }

    private async Task<InsertObjectPickedFile?> PickInsertObjectFileAsync(AvaloniaFilePickerOpenRequest request)
    {
        Func<AvaloniaFilePickerOpenRequest, Task<InsertObjectPickedFile?>>? picker = null;
        ResolveInsertObjectFilePicker(ref picker);
        if (picker is not null)
            return await picker(request);
        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(StorageProvider, request);
        if (file is null)
            return null;
        try
        {
            return new InsertObjectPickedFile(file.Name, file.TryGetLocalPath(), file.OpenReadAsync, file);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Inserts a chosen picture at the captured primary selection start on every grouped sheet, through the shared
    /// session command path and the Core <see cref="FreeX.Core.Commands.InsertPictureCommand"/> the drawing
    /// overlay already paints. The native pixel size is decoded via Avalonia (falling back to a default when
    /// decoding fails); the user can then move/resize it with the existing drawing-object editing. Surfaces
    /// the Core guard message on failure.
    /// </summary>
    private async Task InsertPictureFromFileAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        using var ownerLifetime = new InsertObjectPickerLifetime(this);
        var session = _session;
        var target = new PicturePickerTarget(
            session, session.ActiveSheet, session.SelectedRange.Start,
            session.GetCurrentGroupedEditSheetIds().ToArray(), ownerLifetime);
        if (!IsPicturePickerTargetCurrent(target))
            return;

        if (!CanPickInsertObjectFile())
        {
            ShowEditIssue(UiText.Get("InsertLoc_PictureUnavailable"));
            return;
        }

        using var file = await PickInsertObjectFileAsync(
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                UiText.Get("InsertLoc_InsertPictureTitle"),
                [PictureFileType]));

        if (file is null || !IsPicturePickerTargetCurrent(target))
            return;

        var contentType = InsertPictureCommandFactory.ContentTypeForPath(file.Name);
        if (contentType is null)
        {
            ShowEditIssue(UiText.Get("InsertLoc_UnsupportedImageFormat"));
            return;
        }

        var readResult = await FileByteReadWorkflow.ReadStreamAsync(file.OpenReadAsync, ownerLifetime.Token);
        if (!IsPicturePickerTargetCurrent(target))
            return;
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
        var anchor = target.Address;
        // Keep the active sheet first so the existing mutation aftermath stays on this view's sheet.
        var commands = target.SheetIds.OrderBy(id => id == anchor.Sheet ? 0 : 1)
            .Select(sheetId => (IWorkbookCommand)PictureInsertionPlacementPlanner.CreateInsertPictureCommand(
                sheetId, new CellAddress(sheetId, anchor.Row, anchor.Col), imageBytes, contentType, size))
            .ToArray();
        IWorkbookCommand command = commands.Length == 1
            ? commands[0]
            : new CompositeWorkbookCommand("Insert Picture", commands);
        var result = session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("InsertLoc_InsertPictureFailed"));
            return;
        }

        ClearSelectedDrawingObject();
        RefreshShell(UiText.Format("InsertLoc_InsertedPictureAt", FormatCellReference(anchor)));
    }

    private sealed class InsertObjectPickerLifetime : IDisposable
    {
        private readonly Window _owner;
        private readonly CancellationTokenSource _cancellation = new();

        internal InsertObjectPickerLifetime(Window owner)
        {
            _owner = owner;
            owner.Closed += OnClosed;
        }

        internal bool IsClosed { get; private set; }
        internal CancellationToken Token => _cancellation.Token;

        private void OnClosed(object? sender, EventArgs args)
        {
            IsClosed = true;
            _cancellation.Cancel();
        }

        public void Dispose()
        {
            _owner.Closed -= OnClosed;
            _cancellation.Dispose();
        }
    }

    private sealed record PicturePickerTarget(
        WorkbookSession Session, Sheet Sheet, CellAddress Address,
        IReadOnlyList<SheetId> SheetIds, InsertObjectPickerLifetime Lifetime);

    private bool IsPicturePickerTargetCurrent(PicturePickerTarget target) =>
        !target.Lifetime.IsClosed && !_isOpening && !_isSaving && ReferenceEquals(_session, target.Session) &&
        ReferenceEquals(target.Session.ActiveSheet, target.Sheet) &&
        target.Sheet.Id == target.Address.Sheet &&
        ReferenceEquals(target.Session.Workbook.GetSheet(target.Address.Sheet), target.Sheet) &&
        target.SheetIds.ToHashSet().SetEquals(target.Session.GetCurrentGroupedEditSheetIds()) &&
        target.SheetIds.All(id => target.Session.Workbook.GetSheet(id) is not null);

    /// <summary>
    /// Inserts a drawing shape of <paramref name="kind"/> anchored at the active cell through the shared
    /// session command path and the Core <see cref="FreeX.Core.Commands.AddDrawingShapeCommand"/> the drawing
    /// overlay already renders. The shape is selectable and editable (move/resize/rotate) like other drawing
    /// objects. Surfaces the Core guard message on failure.
    /// </summary>
    private void InsertShapeAtActiveCell(DrawingShapeKind kind)
    {
        // r607: the shell stays interactive during an open or a save, and the save
        // hands the LIVE workbook to the adapter on a background thread -- so a mutation
        // from here races the serializer over the same object graph. 243 sibling handlers
        // carry this guard; these did not.
        if (_isOpening || _isSaving)
            return;

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
        // r607: the shell stays interactive during an open or a save, and the save
        // hands the LIVE workbook to the adapter on a background thread -- so a mutation
        // from here races the serializer over the same object graph. 243 sibling handlers
        // carry this guard; these did not.
        if (_isOpening || _isSaving)
            return;

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

    /// <summary>Inserts a supported legacy Form Control at the active cell through the shared undoable command path.</summary>
    private void InsertFormControlAtActiveCell(FreeX.Core.Model.FormControlKind kind)
    {
        // r607: the shell stays interactive during an open or a save, and the save
        // hands the LIVE workbook to the adapter on a background thread -- so a mutation
        // from here races the serializer over the same object graph. 243 sibling handlers
        // carry this guard; these did not.
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var anchor = _session.ActiveCell;
        var command = new AddFormControlCommand(_session.ActiveSheet.Id, anchor, kind);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Could not insert form control.");
            return;
        }

        ClearSelectedDrawingObject();
        RefreshShell("Insert " + FormControlDisplayName(kind));
    }

    private static string FormControlDisplayName(FreeX.Core.Model.FormControlKind kind) => kind switch
    {
        FreeX.Core.Model.FormControlKind.CheckBox => "Check Box",
        FreeX.Core.Model.FormControlKind.OptionButton => "Option Button",
        FreeX.Core.Model.FormControlKind.DropDown => "Drop-Down",
        FreeX.Core.Model.FormControlKind.ListBox => "List Box",
        FreeX.Core.Model.FormControlKind.Spinner => "Spin Button",
        FreeX.Core.Model.FormControlKind.ScrollBar => "Scroll Bar",
        _ => "Button",
    };

    private const double PictureDecodeDefaultDpi = 96d;

    /// <summary>
    /// Decodes the image's native size via Avalonia and converts pixels to 96-DPI device-independent units
    /// using the bitmap's own embedded DPI, or null when decoding fails. Mirrors the WPF host's
    /// <c>ImageDimensionDecoder.PixelsToDeviceIndependentUnits</c> formula (pixels*96/dpi) so the same image
    /// file inserts at the same physical size on both shells instead of Avalonia's raw pixel count being
    /// mistaken for DIPs at 96 DPI.
    /// </summary>
    private static PictureInsertionSize? DecodePictureSize(byte[] imageBytes)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes);
            using var bitmap = new Bitmap(stream);
            var width = PictureDecodePixelsToDip(bitmap.PixelSize.Width, bitmap.Dpi.X);
            var height = PictureDecodePixelsToDip(bitmap.PixelSize.Height, bitmap.Dpi.Y);
            return PictureInsertionPlacementPlanner.NormalizeSize(width, height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double PictureDecodePixelsToDip(int pixels, double dpi) =>
        pixels * PictureDecodeDefaultDpi / (double.IsFinite(dpi) && dpi > 0 ? dpi : PictureDecodeDefaultDpi);

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
        var defaultRangeText = DialogRangeSelectionFormatter.Format(
            sourceRange,
            DialogRangeSelectionFormat.AbsoluteRange,
            new DialogRangeSelectionFormatContext(null, null, UseR1C1ReferenceStyle: false));
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
        var chrome = CreateTableDialogChromeStyle;
        var dialog = new FreeXDialogWindow(chrome)
        {
            Title = UiText.Get(CreateTableDialogPlanner.TitleKey),
            // WPF CreateTableDialog is a Width x Height (224x144) NoResize window: an OUTER size. The
            // Avalonia window sets the client area, so both lose the WPF frame (16 x 39) like the
            // other fixed-size dialogs; the text stays at the 12 px dialog default.
            Width = CreateTableDialogPlanner.Width - WpfNoResizeFrameWidth,
            MinWidth = CreateTableDialogPlanner.Width - WpfNoResizeFrameWidth,
            MaxWidth = CreateTableDialogPlanner.Width - WpfNoResizeFrameWidth,
            Height = CreateTableDialogPlanner.Height - WpfNoResizeClientFrameHeight,
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
        AvaloniaCompactDialogChrome.ApplyTextBox(rangeBox, chrome);
        // Lighter selection highlight so the (auto-selected) range text stays readable in black —
        // Avalonia's default accent selection is too dark for black text (matches Windows' lighter selection).
        rangeBox.SelectionBrush = Brush(173, 214, 255);
        AutomationProperties.SetName(rangeBox, UiText.Get(CreateTableDialogPlanner.RangeAutomationNameKey));
        AutomationProperties.SetAutomationId(rangeBox, CreateTableDialogPlanner.RangeBoxAutomationId);
        AutomationProperties.SetHelpText(rangeBox, UiText.Get(CreateTableDialogPlanner.RangeAutomationHelpTextKey));

        var rangePicker = new Button
        {
            Content = "...",
            // WPF CreateReferenceEditor: the picker is docked to the RIGHT of the range box with the
            // gap on its left; the range box fills the rest of the row.
            Margin = new Thickness(CreateTableDialogPlanner.RangePickerGap, 0, 0, 0),
            Padding = new Thickness(0),
        };
        DockPanel.SetDock(rangePicker, Dock.Right);
        rangePicker.Width = CreateTableDialogPlanner.RangePickerWidth;
        AvaloniaCompactDialogChrome.ApplyButton(rangePicker, chrome, CreateTableDialogPlanner.RangePickerWidth);
        rangePicker.Padding = new Thickness(0);
        AutomationProperties.SetName(rangePicker, UiText.Get(CreateTableDialogPlanner.RangePickerAutomationNameKey));
        AutomationProperties.SetAutomationId(rangePicker, "CreateTableRangePicker");

        var headersBox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get(CreateTableDialogPlanner.HeadersCheckBoxKey)),
            IsChecked = CreateTableDialogPlanner.DefaultFirstRowHasHeaders,
            Margin = new Thickness(0, 0, 0, CreateTableDialogPlanner.HeadersBottomMargin),
        };
        AvaloniaCompactDialogChrome.ApplyCheckBox(headersBox, chrome);
        AutomationProperties.SetName(headersBox, UiText.Get(CreateTableDialogPlanner.HeadersAutomationNameKey));
        AutomationProperties.SetAutomationId(headersBox, CreateTableDialogPlanner.HeadersBoxAutomationId);
        AutomationProperties.SetHelpText(headersBox, UiText.Get(CreateTableDialogPlanner.HeadersAutomationHelpTextKey));

        var okButton = new Button
        {
            Content = UiText.Get("Common_Ok"),
            IsDefault = true,
        };
        okButton.Width = CreateTableDialogPlanner.ButtonWidth;
        AvaloniaCompactDialogChrome.ApplyButton(okButton, chrome, CreateTableDialogPlanner.ButtonWidth, isDefault: true);
        var cancelButton = new Button
        {
            Content = UiText.Get("Common_Cancel"),
            IsCancel = true,
        };
        cancelButton.Width = CreateTableDialogPlanner.ButtonWidth;
        // WPF buttons keep a 1px content padding (the CreateTable chrome's ButtonPadding), so the
        // fixed 56px buttons do not clip the Cancel caption.
        AvaloniaCompactDialogChrome.ApplyButton(cancelButton, chrome, CreateTableDialogPlanner.ButtonWidth);
        okButton.Click += (_, _) => RunGuarded(async () =>
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
        });
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
                new DockPanel
                {
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
        if (!CanPickInsertObjectFile())
        {
            ShowEditIssue(UiText.Get("WfInsertObject_Unavailable"));
            return;
        }

        if (!TryCommitPendingFormulaEdit())
            return;

        using var ownerLifetime = new InsertObjectPickerLifetime(this);

        var linkCheck = new CheckBox { Content = UiText.Get("WfInsertObject_LinkLabel") };
        AutomationProperties.SetAutomationId(linkCheck, "WfInsertObjectLinkCheck");

        var pathBlock = new TextBlock
        {
            Text = UiText.Get("WfInsertObject_NoFileChosen"),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(pathBlock, "WfInsertObjectChosenPath");

        InsertObjectPickedFile? chosen = null;
        Task<bool>? insertion = null;
        Task? browsingWork = null;
        var browsing = false;

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
        using var insertionCancellation = CancellationTokenSource.CreateLinkedTokenSource(ownerLifetime.Token);
        dialog.Closed += (_, _) => insertionCancellation.Cancel();

        async Task BrowseAsync()
        {
            if (browsing || insertion is { IsCompleted: false } || !dialog.IsVisible)
                return;
            browsing = true;
            browse.IsEnabled = false;
            insert.IsEnabled = false;
            try
            {
                var picked = await PickInsertObjectFileAsync(
                    AvaloniaFilePickerOpenRequest.FromFileTypes(
                        UiText.Get("WfInsertObject_Title"),
                        [AnyFileType]));
                if (picked is null)
                    return;
                if (!dialog.IsVisible || ownerLifetime.IsClosed || insertion is { IsCompleted: false })
                {
                    picked.Dispose();
                    return;
                }

                if (!ReferenceEquals(chosen, picked))
                    chosen?.Dispose();
                chosen = picked;
                pathBlock.Text = chosen.Name;
            }
            finally
            {
                browsing = false;
                if (dialog.IsVisible && insertion is not { IsCompleted: false })
                {
                    browse.IsEnabled = true;
                    insert.IsEnabled = chosen is not null;
                }
            }
        }
        browse.Click += (_, _) =>
        {
            if (browsing || insertion is { IsCompleted: false } || !dialog.IsVisible)
                return;
            var work = BrowseAsync();
            browsingWork = work;
            RunGuarded(() => work);
        };

        insert.Click += (_, _) => RunGuarded(async () =>
        {
            if (chosen is null || browsing || insertion is { IsCompleted: false } ||
                !dialog.IsVisible || ownerLifetime.IsClosed)
                return;
            browse.IsEnabled = false;
            insert.IsEnabled = false;
            insertion = TryInsertObjectAsync(
                chosen, linkCheck.IsChecked == true, () => dialog.IsVisible, ownerLifetime, insertionCancellation.Token);
            try
            {
                if (await insertion)
                    dialog.Close();
            }
            finally
            {
                if (dialog.IsVisible)
                {
                    browse.IsEnabled = true;
                    insert.IsEnabled = chosen is not null;
                }
            }
        });
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
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            // A canceled dialog must not dispose a picked item while its read is still running.
            try
            {
                await Task.WhenAll(
                    browsingWork ?? Task.CompletedTask,
                    (Task?)insertion ?? Task.CompletedTask);
            }
            finally
            {
                chosen?.Dispose();
            }
        }
    }

    /// <summary>
    /// Validates the chosen file through the portable planner and inserts it: an image is embedded as a real
    /// picture; anything else becomes a generated icon/label placeholder picture. Returns true on success.
    /// </summary>
    private async Task<bool> TryInsertObjectAsync(
        InsertObjectPickedFile file, bool linkToFile, Func<bool> canApply,
        InsertObjectPickerLifetime ownerLifetime, CancellationToken cancellationToken)
    {
        if (_isOpening || _isSaving)
            return false;

        var session = _session;
        var target = new PicturePickerTarget(
            session, session.ActiveSheet, session.ActiveCell,
            session.GetCurrentGroupedEditSheetIds().ToArray(), ownerLifetime);
        var path = file.LocalPath ?? file.Name;
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
            var readResult = await FileByteReadWorkflow.ReadStreamAsync(file.OpenReadAsync, cancellationToken);
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

        if (!canApply() || !IsPicturePickerTargetCurrent(target))
            return false;

        var anchor = target.Address;
        var command = PictureInsertionPlacementPlanner.CreateInsertPictureCommand(
            target.Sheet.Id, anchor, imageBytes, contentType, size);
        var result = session.ExecuteReviewCommand(command);
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
