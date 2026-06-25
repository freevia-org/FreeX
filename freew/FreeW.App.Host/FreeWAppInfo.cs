using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace FreeW.App.Host;

public static class FreeWAppInfo
{
    public const string ProductName = "FreeW";
    public const string HelpUrl = "https://github.com/tony-xmelon/FreeX/tree/main/freew";
    public const string FeedbackUrl = "https://github.com/tony-xmelon/FreeX/issues/new?title=FreeW%20feedback";
    public const string LatestReleaseUrl = "https://github.com/tony-xmelon/FreeX/actions/workflows/freew-release.yml";
    public const string TrademarkNotice = "FreeW is not affiliated with, endorsed by, or sponsored by Microsoft. Microsoft Word is a trademark of Microsoft Corporation.";
    public const string ProjectLicenseNotice = "FreeW Source License: Copyright (c) 2026 FreeX contributors. All rights reserved. Tester binaries may be downloaded and run for personal evaluation and testing. Redistribution or commercial distribution requires separate written permission from the copyright holder.";
    public const string PrivacyNotice = "Privacy: FreeW is a local desktop app. Documents are opened, edited, and saved on this machine unless the user explicitly chooses an external sharing path. Local tester diagnostics stay on the user's machine unless the user chooses to share them. FreeW does not intentionally collect document contents, filenames, or file paths in diagnostics or crash reports.";
    public const string SourceNotice = "Full project license, legal notice, privacy notice, third-party notices, and bundled third-party license texts are available in Help > Legal Notices and are maintained with the FreeX/FreeW release materials at https://github.com/tony-xmelon/FreeX.";

    public static string VersionText { get; } = GetVersionText(typeof(FreeWAppInfo).Assembly);

    public static string ExactVersionText { get; } = GetBuildVersionText(typeof(FreeWAppInfo).Assembly);

    public static string AboutText { get; } =
        $"""
        {ProductName}
        {VersionText}

        A free word processor for DOCX editing and format-fidelity work.

        Built with .NET 10 and WPF.

        {TrademarkNotice}

        {ProjectLicenseNotice}

        {PrivacyNotice}

        {SourceNotice}
        """;

    public static string GetVersionText(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return FormatVersionText(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion);
    }

    public static string GetBuildVersionText(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return FormatBuildVersionText(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion,
            assembly.GetName().Version?.ToString());
    }

    /// <summary>
    /// Formats <paramref name="informationalVersion"/> as a display version string.
    /// FreeW preserves the full three-part version (e.g. <c>0.5.0</c> stays <c>0.5.0</c>);
    /// delegates to <see cref="AppVersionFormatter.FormatVersionText"/> with the default
    /// <c>dropTrailingZeroPatch: false</c>.
    /// </summary>
    public static string FormatVersionText(string? informationalVersion) =>
        AppVersionFormatter.FormatVersionText(informationalVersion);

    /// <inheritdoc cref="AppVersionFormatter.FormatBuildVersionText"/>
    public static string FormatBuildVersionText(string? informationalVersion, string? assemblyVersion = null) =>
        AppVersionFormatter.FormatBuildVersionText(informationalVersion, assemblyVersion);

    public static string CreateDiagnosticsText(string diagnosticsDirectory, string optionsPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FreeW Diagnostics");
        builder.AppendLine($"Version: {ExactVersionText}");
        builder.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"Diagnostics directory: {diagnosticsDirectory}");
        builder.AppendLine($"Options path: {optionsPath}");
        builder.AppendLine();
        builder.AppendLine("Review this text before sharing it. FreeW does not intentionally include document contents, filenames, or file paths in this diagnostics summary.");
        return builder.ToString();
    }

}
