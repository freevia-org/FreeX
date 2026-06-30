using FluentAssertions;
using FreeX.Core.IO;

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
}
