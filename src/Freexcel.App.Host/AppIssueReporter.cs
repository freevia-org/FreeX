using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Freexcel.App.Host;

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
        Assembly? assembly = null)
    {
        var informationalVersion = (assembly ?? typeof(AppIssueReporter).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return new AppIssueReportContext(
            issueBaseUrl,
            metadata,
            ResolveCommitHash(informationalVersion),
            diagnosticsEnabled);
    }

    public static string CreateIssueUrl(AppIssueReportContext context)
    {
        var separator = context.IssueBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return context.IssueBaseUrl
            + separator
            + "title="
            + Uri.EscapeDataString("Tester issue: ")
            + "&body="
            + Uri.EscapeDataString(CreateIssueBody(context))
            + "&labels="
            + Uri.EscapeDataString("tester-feedback");
    }

    public static string CreateDiagnosticsText(AppIssueReportContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Freexcel Diagnostics");
        builder.AppendLine();
        AppendDiagnosticsMetadata(builder, context);
        builder.AppendLine();
        builder.AppendLine("What happened?");
        builder.AppendLine();
        builder.AppendLine("What did you expect?");
        builder.AppendLine();
        builder.AppendLine("Can you reproduce it? If yes, list the steps:");
        builder.AppendLine();
        builder.AppendLine("Privacy note: Do not include workbook contents, formulas, file paths, or private data unless you choose to share them.");
        return builder.ToString().TrimEnd();
    }

    public static string ResolveCommitHash(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return "unknown";

        var match = CommitHashPattern().Match(informationalVersion);
        return match.Success ? match.Groups["sha"].Value[..8] : "unknown";
    }

    private static string CreateIssueBody(AppIssueReportContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Diagnostics");
        AppendDiagnosticsMetadata(builder, context);
        builder.AppendLine();
        builder.AppendLine("## What happened?");
        builder.AppendLine();
        builder.AppendLine("## What did you expect?");
        builder.AppendLine();
        builder.AppendLine("## Steps to reproduce");
        builder.AppendLine("1. ");
        builder.AppendLine();
        builder.AppendLine("## Privacy");
        builder.AppendLine("Please do not include workbook contents, formulas, file paths, or private data unless you choose to share them.");
        return builder.ToString();
    }

    private static void AppendDiagnosticsMetadata(StringBuilder builder, AppIssueReportContext context)
    {
        builder.AppendLine($"App version: {context.Metadata.AppVersion}");
        builder.AppendLine($"Commit: {NormalizeCommitHash(context.CommitHash)}");
        builder.AppendLine($"OS: {context.Metadata.OperatingSystemDescription}");
        builder.AppendLine($".NET runtime: {context.Metadata.RuntimeDescription}");
        builder.AppendLine($"Process architecture: {context.Metadata.ProcessArchitecture}");
        builder.AppendLine($"Diagnostics enabled: {(context.DiagnosticsEnabled ? "yes" : "no")}");
        builder.AppendLine($"Session ID: {context.Metadata.SessionId}");
    }

    private static string NormalizeCommitHash(string? commitHash) =>
        string.IsNullOrWhiteSpace(commitHash) ? "unknown" : commitHash;

    [GeneratedRegex(@"(?:\+|\.)(?<sha>[0-9a-fA-F]{8,40})(?:$|[^\da-fA-F])", RegexOptions.CultureInvariant)]
    private static partial Regex CommitHashPattern();
}
