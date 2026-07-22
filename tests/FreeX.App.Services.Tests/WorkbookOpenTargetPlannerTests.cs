using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookOpenTargetPlannerTests
{
    [Fact]
    public void TryCreateOpenTarget_UsesOpenCapableAdapterAndNormalizesLocalPath()
    {
        var adapter = new TestFileAdapter(formats: [
            new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false),
            new FileFormatDescriptor(".fxl", "FreeX Workbook", CanOpen: true, CanSave: true)
        ]);

        var resolved = WorkbookOpenTargetPlanner.TryCreateOpenTarget(
            [adapter],
            "  Book.XLSM  ",
            out var target,
            out var message);

        resolved.Should().BeTrue();
        message.Should().BeEmpty();
        target.Should().NotBeNull();
        target!.Path.Should().Be(Path.GetFullPath("Book.XLSM"));
        target.Adapter.Should().BeSameAs(adapter);
        target.Extension.Should().Be(".XLSM");
        target.Format.FormatName.Should().Be("XLSM Macro-Enabled Workbook");
        target.FileAccessIdentity.Should().NotBeNull();
        target.FileAccessIdentity!.LocalPath.Should().Be(target.Path);
        target.FileAccessIdentity.HasBookmark.Should().BeFalse();
    }

    [Fact]
    public void TryCreateOpenTarget_PreservesProvidedFileAccessIdentityForResolvedPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "Budget.fxl");
        var identity = new WorkbookFileAccessIdentity(
            "stale.fxl",
            "macos-security-scoped-bookmark",
            "bookmark-token");

        var resolved = WorkbookOpenTargetPlanner.TryCreateOpenTarget(
            WorkbookFileAdapterCatalog.CreateDefaultAdapters(),
            path,
            identity,
            out var target,
            out _);

        resolved.Should().BeTrue();
        target.Should().NotBeNull();
        target!.FileAccessIdentity.Should().NotBeNull();
        target.FileAccessIdentity!.LocalPath.Should().Be(path);
        target.FileAccessIdentity.BookmarkKind.Should().Be("macos-security-scoped-bookmark");
        target.FileAccessIdentity.BookmarkPayload.Should().Be("bookmark-token");
    }

    [Fact]
    public void TryCreateOpenTarget_RejectsUnsupportedAndMalformedPaths()
    {
        WorkbookOpenTargetPlanner.TryCreateOpenTarget(
                WorkbookFileAdapterCatalog.CreateDefaultAdapters(),
                "Book.unsupported",
                out var unsupportedTarget,
                out var unsupportedMessage)
            .Should()
            .BeFalse();
        unsupportedTarget.Should().BeNull();
        unsupportedMessage.Should().Be("Unsupported file type: .unsupported.");

        WorkbookOpenTargetPlanner.TryCreateOpenTarget(
                WorkbookFileAdapterCatalog.CreateDefaultAdapters(),
                "bad\0Book.xlsx",
                out var malformedTarget,
                out var malformedMessage)
            .Should()
            .BeFalse();
        malformedTarget.Should().BeNull();
        malformedMessage.Should().Be(WorkbookOpenTargetPlanner.LocalPathRequiredMessage);
    }

    [Fact]
    public void TryCreateOpenTarget_RejectsNonFileUri()
    {
        WorkbookOpenTargetPlanner.TryCreateOpenTarget(
                WorkbookFileAdapterCatalog.CreateDefaultAdapters(),
                "https://example.test/Book.xlsx",
                out var target,
                out var message)
            .Should()
            .BeFalse();

        target.Should().BeNull();
        message.Should().Be(WorkbookOpenTargetPlanner.LocalPathRequiredMessage);
    }

    // ── R69-services-file-open-save-6-1: extension/content signature mismatch ────────────────────

    [Fact]
    public void TryCreateOpenTarget_CsvContentRenamedXlsx_FallsBackToCsvAdapter_NoRawZipException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FreeXSignatureMismatch_{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(path, "Name,Amount\r\nWidget,12\r\n");
        try
        {
            var resolved = WorkbookOpenTargetPlanner.TryCreateOpenTarget(
                WorkbookFileAdapterCatalog.CreateDefaultAdapters(),
                path,
                out var target,
                out var message);

            // Before the fix, this extension-only resolution handed the CSV bytes straight to
            // XlsxFileAdapter, whose ZIP reader would throw a raw "End of Central Directory not
            // found"-style exception the first time something actually tried to Load() the stream.
            // The planner itself must not silently paper over that and must produce a target whose
            // adapter can actually parse the file's real (CSV) content.
            resolved.Should().BeTrue("a content/extension mismatch must fall back to a working adapter, not surface nothing");
            target.Should().NotBeNull();
            message.Should().BeEmpty();
            target!.Adapter.Should().BeOfType<CsvFileAdapter>(
                "the sniffed plain-text content should resolve to the CSV adapter instead of the ZIP-based XLSX adapter");

            // Actually Load()ing through the resolved adapter must not throw -- this is the concrete
            // "raw End of Central Directory exception" regression the fix closes. (Not asserting on
            // parsed cell values here since the CSV reader's default delimiter is locale-dependent.)
            using var stream = File.OpenRead(path);
            var load = () => target.Adapter.Load(stream);
            load.Should().NotThrow("the fallback adapter must actually be able to parse the file's real content");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryCreateOpenTarget_CsvContentRenamedXlsx_WithNoFallbackAdapter_SurfacesClearMismatchMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FreeXSignatureMismatchNoFallback_{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(path, "Name,Amount\r\nWidget,12\r\n");
        try
        {
            var xlsxOnlyAdapter = new TestFileAdapter(
                load: _ => throw new InvalidOperationException("End of Central Directory record could not be found."),
                extension: ".xlsx",
                formatName: "Excel Workbook (.xlsx)",
                formats: [new FileFormatDescriptor(".xlsx", "Excel Workbook (.xlsx)", CanOpen: true, CanSave: true)]);

            var resolved = WorkbookOpenTargetPlanner.TryCreateOpenTarget(
                [xlsxOnlyAdapter],
                path,
                out var target,
                out var message);

            resolved.Should().BeFalse(
                "with no content-appropriate fallback adapter registered, the mismatch must be surfaced as a clear " +
                "result instead of letting the caller hand the bytes to the ZIP-based adapter's Load and hit a raw exception");
            target.Should().BeNull();
            message.Should().NotBeNullOrWhiteSpace();
            message.Should().NotContain("Central Directory", "the caller-facing message must be a clear mismatch message, not a raw ZIP-parser exception string");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryCreateOpenTarget_GenuineXlsx_StillOpensNormally()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FreeXGenuineXlsx_{Guid.NewGuid():N}.xlsx");
        var workbook = new Workbook();
        workbook.AddSheet("Sheet1");
        using (var stream = File.Create(path))
        {
            new XlsxFileAdapter().Save(workbook, stream);
        }

        try
        {
            var resolved = WorkbookOpenTargetPlanner.TryCreateOpenTarget(
                WorkbookFileAdapterCatalog.CreateDefaultAdapters(),
                path,
                out var target,
                out var message);

            resolved.Should().BeTrue("a genuine .xlsx file must still open through the normal path");
            message.Should().BeEmpty();
            target.Should().NotBeNull();
            target!.Adapter.Should().BeOfType<XlsxFileAdapter>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryCreateOpenTarget_GenuineCsv_StillOpensNormally()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FreeXGenuineCsv_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "Name,Amount\r\nWidget,12\r\n");

        try
        {
            var resolved = WorkbookOpenTargetPlanner.TryCreateOpenTarget(
                WorkbookFileAdapterCatalog.CreateDefaultAdapters(),
                path,
                out var target,
                out var message);

            resolved.Should().BeTrue("a genuine .csv file must still open through the normal path");
            message.Should().BeEmpty();
            target.Should().NotBeNull();
            target!.Adapter.Should().BeOfType<CsvFileAdapter>();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
