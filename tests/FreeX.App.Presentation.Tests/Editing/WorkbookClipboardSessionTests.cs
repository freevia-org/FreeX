using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class WorkbookClipboardSessionTests
{
    [Fact]
    public void Capture_OwnsCollectionsAndCreatesUniqueApplicationMarker()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 2, 3);
        var cells = new List<(CellAddress Source, Cell Cell)>
        {
            (address, Cell.FromValue(new TextValue("copy")))
        };
        var sourceAreas = new List<GridRange> { new(address, address) };
        var session = new WorkbookClipboardSession();

        var snapshot = session.Capture(new WorkbookClipboardSnapshot(
            new GridRange(address, address),
            cells,
            [],
            "copy",
            IsCut: false,
            sourceAreas));
        cells.Clear();
        sourceAreas.Clear();

        snapshot.Marker.Should().MatchRegex("^[0-9a-f]{32}$");
        snapshot.Cells.Should().HaveCount(1);
        snapshot.SourceAreas.Should().HaveCount(1);
        session.HasContent.Should().BeTrue();

        var nextSnapshot = session.Capture(CreateSnapshot("next", isCut: false));
        nextSnapshot.Marker.Should().NotBe(snapshot.Marker);
    }

    [Fact]
    public void ResolvePaste_MatchingMarkerWinsOverRacyTextAndReadFailure()
    {
        var session = CreateSession(isCut: false);
        var snapshot = session.Content!;

        var resolution = session.ResolvePaste(new WorkbookClipboardReadObservation(
            Available: true,
            Text: "older projection",
            Marker: snapshot.Marker,
            ReadFailed: true));

        resolution.Plan.Should().Be(ClipboardPastePlan.UseInternalClipboard);
        resolution.Snapshot.Should().BeSameAs(snapshot);
        session.HasContent.Should().BeTrue();
    }

    [Fact]
    public void ResolvePaste_ChangedExternalTextClearsStaleSnapshot()
    {
        var session = CreateSession(isCut: false);

        var resolution = session.ResolvePaste(new WorkbookClipboardReadObservation(
            Available: true,
            Text: "external",
            Marker: null,
            ReadFailed: false));

        resolution.Plan.Should().Be(ClipboardPastePlan.UseExternalClipboardText);
        resolution.Snapshot.Should().BeNull();
        session.HasContent.Should().BeFalse();
    }

    [Fact]
    public void ResolvePaste_ReadFailureRetainsSnapshotForRetry()
    {
        var session = CreateSession(isCut: false);

        var resolution = session.ResolvePaste(new WorkbookClipboardReadObservation(
            Available: true,
            Text: null,
            Marker: null,
            ReadFailed: true));

        resolution.Plan.Should().Be(ClipboardPastePlan.ReadFailed);
        resolution.Snapshot.Should().BeNull();
        session.HasContent.Should().BeTrue();
    }

    [Fact]
    public void ResolvePaste_MatchingTextRemainsFallbackWhenMarkerIsMissing()
    {
        var session = CreateSession(isCut: false);
        var snapshot = session.Content!;

        var resolution = session.ResolvePaste(new WorkbookClipboardReadObservation(
            Available: true,
            Text: snapshot.Text,
            Marker: null,
            ReadFailed: false));

        resolution.Plan.Should().Be(ClipboardPastePlan.UseInternalClipboard);
        resolution.Snapshot.Should().BeSameAs(snapshot);
    }

    [Fact]
    public void CompletePaste_ClearsMatchingCutButNotCopyOrNewerCapture()
    {
        var session = CreateSession(isCut: true);
        var staleCut = session.Content!;
        var newerCopy = session.Capture(CreateSnapshot("newer", isCut: false));

        session.CompletePaste(staleCut);
        session.Content.Should().BeSameAs(newerCopy);

        session.CompletePaste(newerCopy);
        session.Content.Should().BeSameAs(newerCopy);

        var currentCut = session.Capture(CreateSnapshot("cut", isCut: true));
        session.CompletePaste(currentCut);
        session.HasContent.Should().BeFalse();
    }

    [Fact]
    public void AttachMarker_PreservesPayloadAndReplacesExistingMarker()
    {
        var original = new PlatformClipboardContent(
            Text: "copy",
            CustomData:
            [
                PlatformClipboardData.FromText("text/html", "<table />"),
                PlatformClipboardData.FromText(
                    WorkbookClipboardSession.MarkerFormatName,
                    "old",
                    PlatformClipboardFormatScope.Application)
            ]);

        var marked = WorkbookClipboardSession.AttachMarker(original, "new");

        marked.Text.Should().Be("copy");
        marked.GetText("text/html").Should().Be("<table />");
        marked.GetText(
                WorkbookClipboardSession.MarkerFormatName,
                PlatformClipboardFormatScope.Application)
            .Should().Be("new");
        marked.CustomData.Count(item => item.Format == WorkbookClipboardSession.MarkerFormat)
            .Should().Be(1);
    }

    [Theory]
    [InlineData(PlatformClipboardReadStatus.Empty, true, false)]
    [InlineData(PlatformClipboardReadStatus.Unavailable, false, false)]
    [InlineData(PlatformClipboardReadStatus.Unsupported, true, true)]
    [InlineData(PlatformClipboardReadStatus.Failed, true, true)]
    public void Observe_ProjectsPlatformOutcomes(
        PlatformClipboardReadStatus status,
        bool expectedAvailable,
        bool expectedReadFailed)
    {
        var observation = WorkbookClipboardSession.Observe(
            new PlatformClipboardReadResult<PlatformClipboardContent>(status));

        observation.Available.Should().Be(expectedAvailable);
        observation.ReadFailed.Should().Be(expectedReadFailed);
        observation.Text.Should().BeNull();
        observation.Marker.Should().BeNull();
    }

    [Fact]
    public void Observe_ProjectsTextAndApplicationMarkerFromSuccessfulRead()
    {
        var content = WorkbookClipboardSession.AttachMarker(
            new PlatformClipboardContent(Text: "copy"),
            "marker");

        var observation = WorkbookClipboardSession.Observe(
            PlatformClipboardReadResult<PlatformClipboardContent>.Success(content));

        observation.Should().Be(new WorkbookClipboardReadObservation(
            Available: true,
            Text: "copy",
            Marker: "marker",
            ReadFailed: false));
    }

    [Fact]
    public void WpfAvaloniaAndServices_UseSharedWorkbookClipboardSessionOwner()
    {
        var repoRoot = RepositoryFileLocator.FindDirectory("src");
        var wpf = File.ReadAllText(Path.Combine(
            repoRoot,
            "FreeX.App.Host",
            "MainWindow.ClipboardCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            repoRoot,
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        var services = File.ReadAllText(Path.Combine(
            repoRoot,
            "FreeX.App.Services",
            "WorkbookSession.cs"));
        var owner = File.ReadAllText(Path.Combine(
            repoRoot,
            "FreeX.App.Presentation",
            "Editing",
            "WorkbookClipboardSession.cs"));

        owner.Should().Contain("public sealed class WorkbookClipboardSession");
        owner.Should().Contain("MarkerFormatName = \"FreeX.InternalClipboard\"");
        wpf.Should().Contain("WorkbookClipboardSession.AttachMarker(");
        wpf.Should().Contain("_workbookClipboardSession.ResolvePaste(observation)");
        wpf.Should().Contain("WorkbookClipboardSession.PasteReadRequest");
        avalonia.Should().Contain("WorkbookClipboardSession.AttachMarker(");
        avalonia.Should().Contain("WorkbookClipboardSession.PasteReadRequest");
        avalonia.Should().Contain("clipboardMarker: textRead.Marker");
        services.Should().Contain("_workbookClipboardSession.Capture(");
        services.Should().Contain("_workbookClipboardSession.ResolvePaste(");

        var adapters = string.Concat(wpf, avalonia, services);
        adapters.Should().NotContain("private record InternalClipboard");
        adapters.Should().NotContain("private sealed record InternalClipboard");
        adapters.Should().NotContain("_internalClipboard");
        adapters.Should().NotContain("FreeX.InternalClipboard");
        wpf.Should().NotContain("Guid.NewGuid().ToString(\"N\")");
        services.Should().NotContain("Guid.NewGuid().ToString(\"N\")");
        services.Should().NotContain("ClipboardPastePlanner.PlanPaste(");
    }

    private static WorkbookClipboardSession CreateSession(bool isCut)
    {
        var session = new WorkbookClipboardSession();
        session.Capture(CreateSnapshot("copy", isCut));
        return session;
    }

    private static WorkbookClipboardSnapshot CreateSnapshot(string text, bool isCut)
    {
        var address = new CellAddress(SheetId.New(), 1, 1);
        return new WorkbookClipboardSnapshot(
            new GridRange(address, address),
            [(address, Cell.FromValue(new TextValue(text)))],
            [],
            text,
            isCut);
    }
}
