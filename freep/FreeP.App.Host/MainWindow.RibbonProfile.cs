using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    private FreePRibbonHostProfile CreateRibbonHostProfile() =>
        FreePRibbonHostProfileFactory.Create(new FreePRibbonHostPorts
        {
            ActionProfile = GetRibbonActionPortProfile(),
            QueryEndpoints = new FreePRibbonHostQueryEndpoints
            {
                BeginFormatPainter = () => SlideCanvas?.BeginFormatPainter() == true,
                EditPointsEnabled = () => SlideCanvas?.EditPointsEnabled,
                AnimationPaneVisible = () => IsAnimationPaneVisible,
                ViewShowState = () => _viewShowState,
                ViewZoomState = () => _viewZoomState,
                ViewModeState = () => _viewModeState,
            },
            TextActionTargets = CreateRibbonTextActionTargets(),
            DesignCommands = new FreePRibbonDesignCommandEndpoints
            {
                OpenCustomSlideSize = _ => OpenSlideSizeDialog(),
                OpenLayoutPicker = _ => OpenLayoutPicker(),
            },
            OleCommands = new FreePRibbonOleCommandEndpoints
            {
                InsertEmbeddedObject = InsertEmbeddedObjectFromFile,
                TryOpenInlineEmbeddedObject = () =>
                    SlideCanvas?.TextEditor?.TryActivateInlineOleObject() == true,
            },
            SupportCommands = new FreePRibbonSupportCommandEndpoints
            {
                OpenHelpOnline = () => OpenSupportUri(FreePProductInfo.HelpUrl, "FreeP Help"),
                OpenFeedback = () => OpenSupportUri(
                    FreePProductInfo.CreateFeedbackUrl(typeof(MainWindow).Assembly),
                    "FreeP Feedback"),
                CopyDiagnostics = CopySupportDiagnostics,
                TestCrashReporting = TestCrashReporting,
            },
        });

    private void OpenSupportUri(string uri, string title)
    {
        var result = DesktopExternalUriLauncher.Open(uri);
        if (result != ExternalUriLaunchResult.Launched)
            DialogMessageHelper.ShowWarning(this, $"Could not open the link.\n\n{uri}", title);
    }

    private void CopySupportDiagnostics()
    {
        try
        {
            Clipboard.SetText(FreePProductInfo.CreateDiagnosticsText(typeof(MainWindow).Assembly));
            DialogMessageHelper.ShowInfo(this, "FreeP diagnostics were copied to the clipboard.", "Copy Diagnostics");
        }
        catch (Exception ex)
        {
            DialogMessageHelper.ShowWarning(this, $"Could not copy diagnostics: {ex.Message}", "Copy Diagnostics");
        }
    }

    private void TestCrashReporting()
    {
        var result = AppCrashAnalyticsRuntime.SendTestReport();
        var message = AppCrashAnalyticsRuntime.UserMessage(result);
        if (result == CrashAnalyticsTestReportResult.Sent)
            DialogMessageHelper.ShowInfo(this, message, "Test Crash Reporting");
        else
            DialogMessageHelper.ShowWarning(this, message, "Test Crash Reporting");
    }

    private FreePRibbonTextActionTargets CreateRibbonTextActionTargets() => new()
    {
        Notes = FreePRibbonTextActionEndpointFactory.CreateFormattingTarget(
            TryApplyCurrentSlideNotesTextFormat,
            TryApplyCurrentSlideNotesValueFormat,
            TryApplyCurrentSlideNotesParagraphFormat),
        Shape = new FreePRibbonTextActionEndpoints
        {
            ToggleFormat = format => WithCanvas(canvas => ApplyShapeTextFormat(canvas, format)),
            SetParagraphAlignment = alignment => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphAlignment(alignment) == true),
            ApplyListPreset = preset => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphListPreset(preset) == true),
            ToggleBullets = () => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphBulletToggle() == true),
            ToggleNumbering = () => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphNumberingToggle() == true),
            Indent = () => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphIndent() == true),
            Outdent = () => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphOutdent() == true),
            SetFontFamily = family => WithShapeEditor(editor => editor.ApplyFont(family)),
            SetFontSize = sizePt => WithShapeEditor(editor => editor.ApplyFontSize(sizePt)),
            SetColor = color => WithCanvas(canvas => canvas.TextEditor?.ApplyColor(color) == true),
            RemoveHyperlink = () => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplySelectedShapeRunHyperlink(null) == true),
        },
        Table = new FreePRibbonTextActionEndpoints
        {
            ToggleFormat = format => WithCanvas(canvas => ApplyTableTextFormat(canvas, format)),
            SetParagraphAlignment = alignment => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellParagraphAlignment(alignment) == true),
            ApplyListPreset = preset => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellParagraphListPreset(preset) == true),
            ToggleBullets = () => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellParagraphBulletToggle() == true),
            ToggleNumbering = () => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellParagraphNumberingToggle() == true),
            Indent = () => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellParagraphIndent() == true),
            Outdent = () => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellParagraphOutdent() == true),
            SetFontFamily = family => WithTableEditor(editor => editor.ApplyFont(family)),
            SetFontSize = sizePt => WithTableEditor(editor => editor.ApplyFontSize(sizePt)),
            SetColor = color => WithCanvas(canvas => canvas.TableCellEditor?.ApplyColor(color) == true),
            SetTextVerticalType = verticalType => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellTextVerticalType(verticalType) == true),
            SetTableCellFill = color => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellFill(color) == true),
            SetTableCellAnchor = anchor => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellAnchor(anchor) == true),
            SetTableCellBorder = (side, outline) => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellBorder(side, outline) == true),
            SetTableCellInset = (side, value) => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableCellInset(side, value) == true),
            SetTableRowHeight = height => WithCanvas(canvas =>
                canvas.TableCellEditor?.TryApplyActiveTableRowHeight(height) == true),
        },
    };

    private void CopyRibbonSelection() =>
        _osClipboard.Copy(
            Editor,
            error => ReportClipboardWriteFailure(
                PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCopyCommand),
                error));

    private void CutRibbonSelection() =>
        _osClipboard.Cut(
            Editor,
            error => ReportClipboardWriteFailure(
                PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCutCommand),
                error));

    private void QueueAssetImport(PresentationAssetImportKind kind) =>
        _ = ImportPresentationAssetAsync(kind);

    private bool WithCanvas(Func<SlideCanvas, bool> execute) =>
        SlideCanvas is { } canvas && execute(canvas);

    private bool WithShapeEditor(Action<InCanvasTextEditor> execute) =>
        WithCanvas(canvas =>
        {
            if (canvas.TextEditor?.IsActive != true)
                return false;
            execute(canvas.TextEditor);
            return true;
        });

    private bool WithTableEditor(Action<InCanvasTableCellEditor> execute) =>
        WithCanvas(canvas =>
        {
            if (canvas.TableCellEditor?.IsCellRichEditActive != true)
                return false;
            execute(canvas.TableCellEditor);
            return true;
        });

    private static bool ApplyShapeTextFormat(SlideCanvas canvas, TableCellTextFormatKind format)
    {
        if (canvas.TextEditor?.IsActive != true)
            return false;

        return ApplyTextFormat(format, canvas.TextEditor);
    }

    private static bool ApplyTableTextFormat(SlideCanvas canvas, TableCellTextFormatKind format)
    {
        if (canvas.TableCellEditor?.IsCellRichEditActive != true)
            return false;

        return ApplyTextFormat(format, canvas.TableCellEditor);
    }

    private static bool ApplyTextFormat(TableCellTextFormatKind format, InCanvasTextEditor editor)
    {
        switch (format)
        {
            case TableCellTextFormatKind.Bold: editor.ApplyBold(); break;
            case TableCellTextFormatKind.Italic: editor.ApplyItalic(); break;
            case TableCellTextFormatKind.Underline: editor.ApplyUnderline(); break;
            case TableCellTextFormatKind.Strikethrough: editor.ApplyStrikethrough(); break;
            case TableCellTextFormatKind.Superscript: editor.ApplySuperscript(); break;
            case TableCellTextFormatKind.Subscript: editor.ApplySubscript(); break;
            default: return false;
        }

        return true;
    }

    private static bool ApplyTextFormat(TableCellTextFormatKind format, InCanvasTableCellEditor editor)
    {
        switch (format)
        {
            case TableCellTextFormatKind.Bold: editor.ApplyBold(); break;
            case TableCellTextFormatKind.Italic: editor.ApplyItalic(); break;
            case TableCellTextFormatKind.Underline: editor.ApplyUnderline(); break;
            case TableCellTextFormatKind.Strikethrough: editor.ApplyStrikethrough(); break;
            case TableCellTextFormatKind.Superscript: editor.ApplySuperscript(); break;
            case TableCellTextFormatKind.Subscript: editor.ApplySubscript(); break;
            default: return false;
        }

        return true;
    }
}
