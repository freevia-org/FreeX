using FreeX.Core.IO;
using FreeX.Core.Model;

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
            var expectedPath = startupArguments.FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument));
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
                out var drawingObjectPreviewCount);
            if (previewObjectResult is not null)
                return previewObjectResult;

            var openedDisplayName = session.DisplayName;
            var openedSheetName = session.ActiveSheet.Name;
            var openedRowCount = session.Viewport.RowMetrics.Count;
            var openedColumnCount = session.Viewport.ColMetrics.Count;
            var roundTripResult = VerifyEditSaveReopen(
                session,
                requiresPreviewObjects,
                out var roundTripDrawingObjectPreviewCount);
            if (roundTripResult is not null)
                return roundTripResult;

            return new WorkbookStartupSmokeResult(
                true,
                $"Packaging smoke opened {openedDisplayName} on {openedSheetName} with {openedRowCount} rows and {openedColumnCount} columns; drawing_object_previews={drawingObjectPreviewCount}; edited, saved, and reopened a native workbook roundtrip; roundtrip_drawing_object_previews={roundTripDrawingObjectPreviewCount}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or WorkbookTooLargeException)
        {
            return new WorkbookStartupSmokeResult(false, $"Packaging smoke failed: {ex.Message}");
        }
    }

    private WorkbookStartupSmokeResult? VerifyEditSaveReopen(
        WorkbookSession session,
        bool requireDrawingObjectPreviews,
        out int roundTripDrawingObjectPreviewCount)
    {
        roundTripDrawingObjectPreviewCount = 0;
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
                out roundTripDrawingObjectPreviewCount);
            if (previewObjectResult is not null)
                return previewObjectResult;

            var reopenedCell = reopenedSession.Workbook.Sheets.FirstOrDefault()?.GetCell(SmokeEditRow, SmokeEditColumn);
            if (reopenedCell?.Value is not TextValue reopenedText ||
                !string.Equals(reopenedText.Value, marker, StringComparison.Ordinal))
            {
                return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: saved edit marker was not reopened.");
            }
        }
        finally
        {
            if (File.Exists(roundTripPath))
                File.Delete(roundTripPath);
        }

        return null;
    }

    private static WorkbookStartupSmokeResult? VerifyDrawingObjectPreviews(
        WorkbookSession session,
        bool required,
        string stage,
        out int count)
    {
        count = session.Viewport.DrawingObjects.Count;
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

        return null;
    }

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
}

public static class PackagingSmokeCommand
{
    public const string Argument = "--packaging-smoke";

    public static bool TryRun(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!args.Any(arg => string.Equals(arg, Argument, StringComparison.OrdinalIgnoreCase)))
        {
            exitCode = 0;
            return false;
        }

        var startupArguments = args
            .Where(arg => !string.Equals(arg, Argument, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var result = new WorkbookStartupSmokeService().Run(startupArguments);
        var writer = result.Success ? output : error;
        writer.WriteLine(result.Message);
        exitCode = result.ExitCode;
        return true;
    }
}
