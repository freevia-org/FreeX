using FreeX.App.Services;

namespace FreeX.App.Host;

public sealed record AppIssueReportContext(
    string IssueBaseUrl,
    AppDiagnosticsMetadata Metadata,
    string CommitHash,
    bool DiagnosticsEnabled);

public static partial class AppIssueReporter
{
    public static AppIssueReportContext CreateContext(
        string issueBaseUrl,
        AppDiagnosticsMetadata metadata,
        bool diagnosticsEnabled,
        System.Reflection.Assembly? assembly = null)
    {
        var shared = FreeX.App.Services.AppIssueReporter.CreateContext(
            issueBaseUrl,
            metadata,
            diagnosticsEnabled,
            assembly ?? typeof(AppIssueReporter).Assembly);

        return new AppIssueReportContext(
            shared.IssueBaseUrl,
            shared.Metadata,
            shared.CommitHash,
            shared.DiagnosticsEnabled);
    }

    public static string CreateIssueUrl(AppIssueReportContext context) =>
        FreeX.App.Services.AppIssueReporter.CreateIssueUrl(ToShared(context));

    public static string CreateDiagnosticsText(AppIssueReportContext context) =>
        FreeX.App.Services.AppIssueReporter.CreateDiagnosticsText(ToShared(context));

    public static string ResolveCommitHash(string? informationalVersion) =>
        FreeX.App.Services.AppIssueReporter.ResolveCommitHash(informationalVersion);

    private static FreeX.App.Services.AppIssueReportContext ToShared(AppIssueReportContext context) =>
        new(context.IssueBaseUrl, context.Metadata, context.CommitHash, context.DiagnosticsEnabled);
}
