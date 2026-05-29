using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeX.App.Host;

public sealed record AppIssueReportContext(
    string IssueBaseUrl,
    AppDiagnosticsMetadata Metadata,
    string CommitHash,
    bool DiagnosticsEnabled);

public static partial class AppIssueReporter
{
    private const string WhatHappenedPrompt = "What happened?";
    private const string WhatDidYouExpectPrompt = "What did you expect?";
    private const string PrivacySensitiveDataList = "workbook contents, formulas, file paths, or private data";
    private const string DiagnosticsPrivacyNote = $"Do not include {PrivacySensitiveDataList} unless you choose to share them.";
    private const string IssuePrivacyNote = $"Please do not include {PrivacySensitiveDataList} unless you choose to share them.";

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
        builder.AppendLine("FreeX Diagnostics");
        builder.AppendLine();
        AppendDiagnosticsMetadata(builder, context);
        builder.AppendLine();
        builder.AppendLine(WhatHappenedPrompt);
        builder.AppendLine();
        builder.AppendLine(WhatDidYouExpectPrompt);
        builder.AppendLine();
        builder.AppendLine("Can you reproduce it? If yes, list the steps:");
        builder.AppendLine();
        builder.Append("Privacy note: ");
        builder.AppendLine(DiagnosticsPrivacyNote);
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
        AppendIssueSectionHeading(builder, WhatHappenedPrompt);
        builder.AppendLine();
        AppendIssueSectionHeading(builder, WhatDidYouExpectPrompt);
        builder.AppendLine();
        builder.AppendLine("## Steps to reproduce");
        builder.AppendLine("1. ");
        builder.AppendLine();
        builder.AppendLine("## Privacy");
        builder.AppendLine(IssuePrivacyNote);
        return builder.ToString();
    }

    private static void AppendIssueSectionHeading(StringBuilder builder, string prompt) =>
        builder.AppendLine($"## {prompt}");

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
