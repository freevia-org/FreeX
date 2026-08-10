using FreeX.Core.Calc;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Free.Shared.AppServices;

namespace FreeX.App.Services;

public sealed record WorkbookStartupSmokeResult(bool Success, string Message)
{
    public int ExitCode => Success ? 0 : 1;
}

public sealed class WorkbookStartupSmokeService
{
    private const double SmokeViewportHeight = 240;
    private const double SmokeViewportWidth = 320;
    private const string RoundTripExtension = ".fxl";
    private const uint SmokeEditRow = 2;
    private const uint SmokeEditColumn = 2;
    private const int LegacyPreviewObjectCount = 3;
    private const string SmokeFormatCellsNumberFormat = "@";
    private const double SmokeFormatCellsFontSize = 13;
    private const CellBorderPreset SmokeFormatCellsBorderPreset = CellBorderPreset.All;
    private const BorderStyle SmokeFormatCellsBorderStyle = BorderStyle.Medium;
    private static readonly CellColor SmokeFormatCellsFillColor = new(226, 239, 218);
    private static readonly CellColor SmokeFormatCellsFontColor = new(31, 78, 121);
    private static readonly CellColor SmokeFormatCellsBorderColor = new(112, 48, 160);
    private static readonly CellBorder SmokeFormatCellsBorder = new(
        SmokeFormatCellsBorderStyle,
        SmokeFormatCellsBorderColor);
    private static readonly FormatCellsCompactRequest SmokeFormatCellsRequest = new(
        NumberFormat: SmokeFormatCellsNumberFormat,
        HorizontalAlignment: HorizontalAlignment.Center,
        VerticalAlignment: VerticalAlignment.Center,
        WrapText: true,
        Bold: true,
        FontSize: SmokeFormatCellsFontSize,
        FillColor: SmokeFormatCellsFillColor,
        FontColor: SmokeFormatCellsFontColor,
        FillPatternStyle: CellFillPatternStyle.Solid);

    private readonly IReadOnlyList<IFileAdapter> _adapters;
    private readonly StartupWorkbookLoader _loader;
    private readonly WorkbookSessionFactory _sessionFactory;
    private readonly WorkbookSaveService _saveService;

    public WorkbookStartupSmokeService(
        StartupWorkbookLoader? loader = null,
        WorkbookSessionFactory? sessionFactory = null,
        WorkbookSaveService? saveService = null,
        IEnumerable<IFileAdapter>? adapters = null)
    {
        _adapters = (adapters ?? WorkbookFileAdapterCatalog.CreateDefaultAdapters()).ToList();
        _loader = loader ?? new StartupWorkbookLoader(adapters: _adapters);
        _sessionFactory = sessionFactory ?? new WorkbookSessionFactory();
        _saveService = saveService ?? new WorkbookSaveService();
    }

    public WorkbookStartupSmokeResult Run(IReadOnlyList<string> startupArguments)
    {
        try
        {
            var expectedPath = FindFirstStartupPath(startupArguments);
            if (expectedPath is not null && !File.Exists(expectedPath))
                return new WorkbookStartupSmokeResult(false, $"Packaging smoke failed: file not found: {expectedPath}");

            var source = _loader.Load(startupArguments);
            if (expectedPath is not null &&
                (source.IsFallback ||
                 string.IsNullOrWhiteSpace(source.SourcePath) ||
                 !PathsMatch(expectedPath, source.SourcePath)))
            {
                return new WorkbookStartupSmokeResult(false, $"Packaging smoke failed: requested file was not opened: {expectedPath}");
            }

            var session = _sessionFactory.Create(source, SmokeViewportHeight, SmokeViewportWidth, includeObjects: true);

            if (session.Workbook.Sheets.Count == 0)
                return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: workbook has no sheets.");
            if (session.Viewport.RowMetrics.Count == 0 || session.Viewport.ColMetrics.Count == 0)
                return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: viewport is empty.");

            var requiresPreviewObjects = expectedPath is null;
            var previewObjectResult = VerifyDrawingObjectPreviews(
                session,
                requiresPreviewObjects,
                "preview workbook",
                out var drawingObjectPreviewFacts);
            if (previewObjectResult is not null)
                return previewObjectResult;

            var openedDisplayName = session.DisplayName;
            var openedSheetName = session.ActiveSheet.Name;
            var openedRowCount = session.Viewport.RowMetrics.Count;
            var openedColumnCount = session.Viewport.ColMetrics.Count;
            var roundTripResult = VerifyEditSaveReopen(
                session,
                requiresPreviewObjects,
                out var roundTripDrawingObjectPreviewFacts);
            if (roundTripResult is not null)
                return roundTripResult;

            var drawingObjectPreviewCount = drawingObjectPreviewFacts.LegacyPreviewCount;
            var roundTripDrawingObjectPreviewCount = roundTripDrawingObjectPreviewFacts.LegacyPreviewCount;
            return new WorkbookStartupSmokeResult(
                true,
                $"Packaging smoke opened {openedDisplayName} on {openedSheetName} with {openedRowCount} rows and {openedColumnCount} columns; drawing_object_previews={drawingObjectPreviewCount}; drawing_object_viewport_objects={drawingObjectPreviewFacts.ViewportObjectCount}; drawing_object_render_plans={drawingObjectPreviewFacts.RenderPlanCount}; cropped_image_render_plans={drawingObjectPreviewFacts.CroppedImagePlanCount}; cell_range_snapshot_render_plans={drawingObjectPreviewFacts.CellRangeSnapshotPlanCount}; edited, saved, and reopened a native workbook roundtrip after applying compact Format Cells style to B2; format_cells_style_roundtrip=true; roundtrip_drawing_object_previews={roundTripDrawingObjectPreviewCount}; roundtrip_drawing_object_viewport_objects={roundTripDrawingObjectPreviewFacts.ViewportObjectCount}; roundtrip_drawing_object_render_plans={roundTripDrawingObjectPreviewFacts.RenderPlanCount}; roundtrip_cropped_image_render_plans={roundTripDrawingObjectPreviewFacts.CroppedImagePlanCount}; roundtrip_cell_range_snapshot_render_plans={roundTripDrawingObjectPreviewFacts.CellRangeSnapshotPlanCount}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or WorkbookTooLargeException)
        {
            return new WorkbookStartupSmokeResult(false, $"Packaging smoke failed: {ex.Message}");
        }
    }

    private WorkbookStartupSmokeResult? VerifyEditSaveReopen(
        WorkbookSession session,
        bool requireDrawingObjectPreviews,
        out DrawingObjectPreviewSmokeFacts roundTripDrawingObjectPreviewFacts)
    {
        roundTripDrawingObjectPreviewFacts = DrawingObjectPreviewSmokeFacts.Empty;
        session.SelectCell(new CellAddress(session.ActiveSheet.Id, 1, 1));
        session.MoveActiveCell(1, 1);
        var editAddress = session.ActiveCell;
        if (editAddress.Row != SmokeEditRow || editAddress.Col != SmokeEditColumn)
            return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: workbook navigation did not reach the edit marker cell.");

        var marker = $"FreeX packaging smoke {Guid.NewGuid():N}";
        var edit = session.CommitCellText(marker);
        if (!edit.Success)
        {
            return new WorkbookStartupSmokeResult(
                false,
                $"Packaging smoke failed: edit failed: {edit.ErrorMessage ?? "unknown error"}");
        }

        if (session.ActiveSheet.GetCell(SmokeEditRow, SmokeEditColumn)?.Value is not TextValue editedText ||
            !string.Equals(editedText.Value, marker, StringComparison.Ordinal))
        {
            return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: edited marker was not stored.");
        }

        var formatCellsResult = ApplyFormatCellsStartupSmokeStyle(session, editAddress);
        if (formatCellsResult is not null)
            return formatCellsResult;

        var saveAdapter = FileFormatResolver.FindSaveAdapter(_adapters, RoundTripExtension, out _);
        if (saveAdapter is null)
            return new WorkbookStartupSmokeResult(false, $"Packaging smoke failed: no {RoundTripExtension} save adapter.");

        var roundTripPath = Path.Combine(
            Path.GetTempPath(),
            $"freex-packaging-smoke-{Guid.NewGuid():N}{RoundTripExtension}");
        try
        {
            _saveService
                .SaveAsync(roundTripPath, saveAdapter, session.Workbook)
                .GetAwaiter()
                .GetResult();
            session.MarkSaved(roundTripPath);
            if (session.IsDirty)
                return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: session remained dirty after save.");

            var reopenedSource = new StartupWorkbookLoader(adapters: _adapters).Load([roundTripPath]);
            if (reopenedSource.IsFallback ||
                string.IsNullOrWhiteSpace(reopenedSource.SourcePath) ||
                !PathsMatch(roundTripPath, reopenedSource.SourcePath))
            {
                return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: saved roundtrip did not reopen.");
            }

            var reopenedSession = _sessionFactory.Create(
                reopenedSource,
                SmokeViewportHeight,
                SmokeViewportWidth,
                includeObjects: true,
                adapters: _adapters);
            if (reopenedSession.Viewport.RowMetrics.Count == 0 || reopenedSession.Viewport.ColMetrics.Count == 0)
                return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: reopened roundtrip viewport is empty.");

            var previewObjectResult = VerifyDrawingObjectPreviews(
                reopenedSession,
                requireDrawingObjectPreviews,
                "reopened roundtrip",
                out roundTripDrawingObjectPreviewFacts);
            if (previewObjectResult is not null)
                return previewObjectResult;

            var reopenedSheet = GetFirstSheet(reopenedSession.Workbook.Sheets);
            if (reopenedSheet is null)
                return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: reopened roundtrip has no sheets.");

            var reopenedCell = reopenedSheet.GetCell(SmokeEditRow, SmokeEditColumn);
            if (reopenedCell?.Value is not TextValue reopenedText ||
                !string.Equals(reopenedText.Value, marker, StringComparison.Ordinal))
            {
                return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: saved edit marker was not reopened.");
            }

            var reopenedFormatCellsResult = VerifyFormatCellsStartupSmokeStyle(
                reopenedSession.Workbook,
                reopenedSheet,
                new CellAddress(reopenedSheet.Id, SmokeEditRow, SmokeEditColumn),
                "reopened roundtrip");
            if (reopenedFormatCellsResult is not null)
                return reopenedFormatCellsResult;
        }
        finally
        {
            if (File.Exists(roundTripPath))
                File.Delete(roundTripPath);
        }

        return null;
    }

    private static WorkbookStartupSmokeResult? ApplyFormatCellsStartupSmokeStyle(
        WorkbookSession session,
        CellAddress editAddress)
    {
        if (!FormatCellsCompactPlanner.TryPlan(SmokeFormatCellsRequest, out var diff, out var errorMessage))
        {
            return new WorkbookStartupSmokeResult(
                false,
                $"Packaging smoke failed: Format Cells style planning failed: {errorMessage}");
        }

        var result = session.ApplySelectedRangeCompactFormat(
            diff,
            SmokeFormatCellsBorderPreset,
            SmokeFormatCellsBorderStyle,
            SmokeFormatCellsBorderColor,
            SmokeFormatCellsRequest.MergeCells);
        if (!result.Success)
        {
            return new WorkbookStartupSmokeResult(
                false,
                $"Packaging smoke failed: Format Cells style application failed: {result.ErrorMessage ?? "unknown error"}");
        }

        return VerifyFormatCellsStartupSmokeStyle(
            session.Workbook,
            session.ActiveSheet,
            editAddress,
            "edited workbook");
    }

    private static WorkbookStartupSmokeResult? VerifyFormatCellsStartupSmokeStyle(
        Workbook workbook,
        Sheet sheet,
        CellAddress address,
        string stage)
    {
        var style = GetCellStyle(workbook, sheet, address);
        if (style.NumberFormat != SmokeFormatCellsNumberFormat ||
            style.HorizontalAlignment != HorizontalAlignment.Center ||
            style.VerticalAlignment != VerticalAlignment.Center ||
            !style.WrapText ||
            !style.Bold ||
            style.FontSize != SmokeFormatCellsFontSize ||
            style.FillColor != SmokeFormatCellsFillColor ||
            style.FontColor != SmokeFormatCellsFontColor ||
            style.FillPatternStyle != CellFillPatternStyle.Solid ||
            style.BorderTop != SmokeFormatCellsBorder ||
            style.BorderRight != SmokeFormatCellsBorder ||
            style.BorderBottom != SmokeFormatCellsBorder ||
            style.BorderLeft != SmokeFormatCellsBorder)
        {
            return new WorkbookStartupSmokeResult(
                false,
                $"Packaging smoke failed: Format Cells style was not {(stage == "reopened roundtrip" ? "reopened" : "stored")} on B2.");
        }

        return null;
    }

    private static CellStyle GetCellStyle(Workbook workbook, Sheet sheet, CellAddress address)
    {
        var styleId = sheet.GetCell(address)?.StyleId ??
            sheet.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return workbook.GetStyle(styleId);
    }

    private static string? FindFirstStartupPath(IReadOnlyList<string> startupArguments)
    {
        foreach (var argument in startupArguments)
        {
            if (!string.IsNullOrWhiteSpace(argument))
                return argument;
        }

        return null;
    }

    private static Sheet? GetFirstSheet(IReadOnlyList<Sheet> sheets)
    {
        foreach (var sheet in sheets)
        {
            return sheet;
        }

        return null;
    }

    private static WorkbookStartupSmokeResult? VerifyDrawingObjectPreviews(
        WorkbookSession session,
        bool required,
        string stage,
        out DrawingObjectPreviewSmokeFacts facts)
    {
        var renderPlans = DrawingObjectRenderPlanner.Plan(session.Viewport);
        facts = new DrawingObjectPreviewSmokeFacts(
            required ? LegacyPreviewObjectCount : session.Viewport.DrawingObjects.Count,
            session.Viewport.DrawingObjects.Count,
            renderPlans.Count,
            renderPlans.Count(plan => plan.IsReady && plan.PrimitiveKind == DrawingObjectRenderPrimitiveKind.CroppedImage),
            renderPlans.Count(plan => plan.IsReady && plan.PrimitiveKind == DrawingObjectRenderPrimitiveKind.CellRangeSnapshot));
        if (!required)
            return null;

        var expected = new[]
        {
            (SelectionPaneObjectKind.Shape, PortPreviewWorkbookFactory.PreviewShapeName),
            (SelectionPaneObjectKind.TextBox, PortPreviewWorkbookFactory.PreviewTextBoxName),
            (SelectionPaneObjectKind.Picture, PortPreviewWorkbookFactory.PreviewPictureName)
        };
        foreach (var (kind, name) in expected)
        {
            if (!session.Viewport.DrawingObjects.Any(drawingObject =>
                    drawingObject.Kind == kind &&
                    string.Equals(drawingObject.DisplayName, name, StringComparison.Ordinal) &&
                    drawingObject.Width > 0 &&
                    drawingObject.Height > 0))
            {
                return new WorkbookStartupSmokeResult(
                    false,
                    $"Packaging smoke failed: {stage} is missing drawing object preview {kind} '{name}'.");
            }
        }

        if (!renderPlans.Any(IsPreviewCroppedImagePlan))
        {
            return new WorkbookStartupSmokeResult(
                false,
                $"Packaging smoke failed: {stage} is missing cropped image render plan for '{PortPreviewWorkbookFactory.PreviewPictureName}'.");
        }

        if (!renderPlans.Any(IsPreviewCellRangeSnapshotPlan))
        {
            return new WorkbookStartupSmokeResult(
                false,
                $"Packaging smoke failed: {stage} is missing cell-range snapshot render plan for '{PortPreviewWorkbookFactory.PreviewCellRangeSnapshotName}'.");
        }

        return null;
    }

    private static bool IsPreviewCroppedImagePlan(DrawingObjectRenderPlan plan) =>
        plan.IsReady &&
        plan.PrimitiveKind == DrawingObjectRenderPrimitiveKind.CroppedImage &&
        string.Equals(plan.Bounds.DisplayName, PortPreviewWorkbookFactory.PreviewPictureName, StringComparison.Ordinal) &&
        plan.Crop is { } crop &&
        (crop.Left > 0 || crop.Top > 0 || crop.Right > 0 || crop.Bottom > 0);

    private static bool IsPreviewCellRangeSnapshotPlan(DrawingObjectRenderPlan plan) =>
        plan.IsReady &&
        plan.PrimitiveKind == DrawingObjectRenderPrimitiveKind.CellRangeSnapshot &&
        string.Equals(plan.Bounds.DisplayName, PortPreviewWorkbookFactory.PreviewCellRangeSnapshotName, StringComparison.Ordinal) &&
        plan.PictureGrid is { } grid &&
        grid.RowCount >= 2 &&
        grid.ColumnCount >= 3 &&
        grid.Cells.Count > 0;

    private static bool PathsMatch(string expectedPath, string actualPath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(expectedPath),
                Path.GetFullPath(actualPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private sealed record DrawingObjectPreviewSmokeFacts(
        int LegacyPreviewCount,
        int ViewportObjectCount,
        int RenderPlanCount,
        int CroppedImagePlanCount,
        int CellRangeSnapshotPlanCount)
    {
        public static DrawingObjectPreviewSmokeFacts Empty { get; } = new(0, 0, 0, 0, 0);
    }
}

public static class PackagingSmokeCommand
{
    public const string Argument = SisterAppPackagingSmoke.Argument;

    public static bool TryRun(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!SisterAppPackagingSmoke.HasArgument(args))
        {
            exitCode = 0;
            return false;
        }

        var startupArguments = SisterAppPackagingSmoke.RemoveArgumentTokens(args);
        var result = new WorkbookStartupSmokeService().Run(startupArguments);
        var writer = result.Success ? output : error;
        writer.WriteLine(result.Message);
        exitCode = result.ExitCode;
        return true;
    }
}
