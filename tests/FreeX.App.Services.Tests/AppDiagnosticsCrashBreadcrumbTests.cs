using System.Text.Json;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

/// <summary>
/// A crash report carries a stack trace, which says where the app died but not what the user was
/// doing to get there. The event trail lives in a separate events.jsonl that a tester sending in a
/// single crash file does not include, so crashes needed manual correlation by session and time.
/// The report must now embed the most recent events, bounded, and carrying only the same
/// allow-listed properties as the trail on disk.
/// </summary>
public sealed class AppDiagnosticsCrashBreadcrumbTests
{
    [Fact]
    public void RecordCrash_EmbedsPrecedingEventsAsBreadcrumbs()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = CreateMetadata();

        store.RecordEvent("workbook_opened", metadata, new Dictionary<string, string?> { ["extension"] = ".xlsx" });
        store.RecordEvent("command_invoked", metadata, new Dictionary<string, string?> { ["command"] = "InsertChart" });

        var reportPath = store.RecordCrash(new InvalidOperationException("boom"), "dispatcher", metadata);

        reportPath.Should().NotBeEmpty();
        var report = JsonDocument.Parse(File.ReadAllText(reportPath)).RootElement;
        var breadcrumbs = report.GetProperty("recentEvents").EnumerateArray().ToList();

        breadcrumbs.Should().HaveCount(2, "the crash file should be self-contained");
        breadcrumbs[0].GetProperty("eventName").GetString().Should().Be("workbook_opened");
        breadcrumbs[1].GetProperty("eventName").GetString().Should().Be("command_invoked");
        breadcrumbs[1].GetProperty("command").GetString().Should().Be("InsertChart");
    }

    [Fact]
    public void RecordCrash_BreadcrumbsDropDocumentDetailsLikeTheEventTrail()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = CreateMetadata();

        store.RecordEvent("workbook_opened", metadata, new Dictionary<string, string?>
        {
            ["extension"] = ".xlsx",
            ["workbookPath"] = "C:\\Users\\tester\\private.xlsx"
        });

        var reportPath = store.RecordCrash(new InvalidOperationException("boom"), "dispatcher", metadata);
        var text = File.ReadAllText(reportPath);

        text.Should().Contain("recentEvents");
        text.Should().NotContain("private.xlsx", "breadcrumbs must not leak document details");
        text.Should().NotContain("workbookPath");
    }

    [Fact]
    public void RecordCrash_RetainsOnlyTheMostRecentEvents()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = CreateMetadata();

        for (var i = 0; i < 60; i++)
            store.RecordEvent($"event_{i}", metadata);

        var reportPath = store.RecordCrash(new InvalidOperationException("boom"), "dispatcher", metadata);
        var breadcrumbs = JsonDocument.Parse(File.ReadAllText(reportPath))
            .RootElement.GetProperty("recentEvents").EnumerateArray().ToList();

        breadcrumbs.Should().HaveCount(25, "a long session must not grow the report without bound");
        breadcrumbs[^1].GetProperty("eventName").GetString().Should().Be("event_59", "the newest event is kept");
        breadcrumbs[0].GetProperty("eventName").GetString().Should().Be("event_35", "the oldest are dropped");
    }

    [Fact]
    public void RecordCrash_StillWritesReportWhenNoEventsPreceded()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));

        var reportPath = store.RecordCrash(new InvalidOperationException("boom"), "appdomain", CreateMetadata());

        reportPath.Should().NotBeEmpty();
        var report = JsonDocument.Parse(File.ReadAllText(reportPath)).RootElement;
        report.GetProperty("recentEvents").EnumerateArray().Should().BeEmpty();
        report.GetProperty("exceptionType").GetString().Should().Contain("InvalidOperationException");
    }

    private static AppDiagnosticsMetadata CreateMetadata() =>
        AppDiagnosticsMetadata.Create("Version 0.5 (Tester Release)");
}
