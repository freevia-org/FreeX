using System.Text;

namespace Free.Shared.AppServices;

/// <summary>Creates privacy-safe, prefilled feedback reports for every Free-family application.</summary>
public static class AppFeedbackReporter
{
    public const string DefaultIssueBaseUrl = "https://github.com/tony-xmelon/FreeX/issues/new";

    public static string CreateIssueUrl(
        string productName,
        AppDiagnosticsMetadata metadata,
        string issueBaseUrl = DefaultIssueBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(issueBaseUrl);

        var separator = issueBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return issueBaseUrl
            + separator
            + "title=" + Uri.EscapeDataString($"{productName} feedback: ")
            + "&body=" + Uri.EscapeDataString(CreateIssueBody(productName, metadata))
            + "&labels=" + Uri.EscapeDataString("user-testing,needs-triage");
    }

    public static string CreateDiagnosticsText(string productName, AppDiagnosticsMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentNullException.ThrowIfNull(metadata);

        var builder = new StringBuilder();
        builder.AppendLine($"{productName} Diagnostics");
        builder.AppendLine();
        AppendSafeMetadata(builder, productName, metadata);
        builder.AppendLine();
        builder.AppendLine("What happened?");
        builder.AppendLine();
        builder.AppendLine("What did you expect?");
        builder.AppendLine();
        builder.AppendLine("Steps to reproduce:");
        builder.AppendLine("1. ");
        builder.AppendLine();
        builder.AppendLine("Privacy note: do not include document contents, filenames, file paths, or private data unless you choose to share them.");
        return builder.ToString().TrimEnd();
    }

    private static string CreateIssueBody(string productName, AppDiagnosticsMetadata metadata)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## App information");
        AppendSafeMetadata(builder, productName, metadata);
        builder.AppendLine();
        builder.AppendLine("## What happened?");
        builder.AppendLine();
        builder.AppendLine("## What did you expect?");
        builder.AppendLine();
        builder.AppendLine("## Steps to reproduce");
        builder.AppendLine("1. ");
        builder.AppendLine();
        builder.AppendLine("## Privacy");
        builder.AppendLine("Please do not include document contents, filenames, file paths, or private data unless you choose to share them.");
        return builder.ToString();
    }

    private static void AppendSafeMetadata(
        StringBuilder builder,
        string productName,
        AppDiagnosticsMetadata metadata)
    {
        builder.AppendLine($"App: {productName}");
        builder.AppendLine($"Version: {metadata.AppVersion}");
        builder.AppendLine($"OS: {metadata.OperatingSystemDescription}");
        builder.AppendLine($".NET runtime: {metadata.RuntimeDescription}");
        builder.AppendLine($"Process architecture: {metadata.ProcessArchitecture}");
    }
}
