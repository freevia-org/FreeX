using System.Reflection;

namespace FreeX.App.Services;

public static class AppHelpInfo
{
    public const string ProductName = "FreeX";
    public const string HelpUrl = "https://github.com/tony-xmelon/FreeX";
    public const string FeedbackUrl = "https://github.com/tony-xmelon/FreeX/issues/new";
    public const string LatestReleaseUrl = "https://github.com/tony-xmelon/FreeX/releases/latest";
    public const string ReleaseChannel = "test";
    public const string TrademarkNotice = "FreeX is not affiliated with, endorsed by, or sponsored by Microsoft. Microsoft Excel is a trademark of Microsoft Corporation.";
    public const string ProjectLicenseNotice = "FreeX Source License: Copyright (c) 2026 FreeX contributors. All rights reserved. Tester binaries may be downloaded and run for personal evaluation and testing. Redistribution or commercial distribution requires separate written permission from the copyright holder.";
    public const string PrivacyNotice = "Privacy: FreeX is a local desktop app. Workbooks are opened, edited, and saved on this machine unless the user explicitly chooses an external sharing path. Local tester diagnostics stay on the user's machine unless the user chooses to share them. FreeX does not intentionally collect workbook contents, formulas, filenames, or file paths in diagnostics or crash reports.";
    public const string CompatibilityNotice = "Compatibility references: FreeX uses Microsoft product names only in plain text when describing file compatibility, interoperability, excluded Microsoft services, or test/reference behavior. FreeX does not use Microsoft logos, product icons, trade dress, or Microsoft-style app branding. File-format labels use neutral names such as XLSX Workbook.";
    public const string SourceNotice = "Full project license, legal notice, privacy notice, third-party notices, and bundled third-party license texts are available in Help > Legal Notices and are maintained with the FreeX release materials at https://github.com/tony-xmelon/FreeX.";

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

    public static string FormatVersionText(string? informationalVersion)
    {
        var displayVersion = NormalizeVersionForDisplay(informationalVersion);

        var versionParts = displayVersion.Split('.');
        if (versionParts.Length == 3 &&
            versionParts[2] == "0" &&
            versionParts[0].All(char.IsDigit) &&
            versionParts[1].All(char.IsDigit))
        {
            displayVersion = $"{versionParts[0]}.{versionParts[1]}";
        }

        return $"Version {displayVersion} (Tester Release)";
    }

    public static string FormatBuildVersionText(string? informationalVersion, string? assemblyVersion = null)
    {
        var displayVersion = NormalizeVersionForDisplay(informationalVersion);
        var buildVersion = NormalizeVersionForDisplay(assemblyVersion);

        return string.Equals(displayVersion, buildVersion, StringComparison.OrdinalIgnoreCase)
            ? $"Version {displayVersion} (Tester Release)"
            : $"Version {displayVersion} (build {buildVersion}, Tester Release)";
    }

    private static string NormalizeVersionForDisplay(string? version)
    {
        var displayVersion = string.IsNullOrWhiteSpace(version)
            ? "0.5.0"
            : version.Trim();
        var metadataIndex = displayVersion.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
            displayVersion = displayVersion[..metadataIndex];

        return string.IsNullOrWhiteSpace(displayVersion) ? "0.5.0" : displayVersion;
    }

    public static string BuildAboutText(string versionText, string platformSummary) =>
        $"""
        {ProductName}
        {versionText}

        A free spreadsheet app for XLSX editing with open-only legacy XLS/XLSB import.

        {platformSummary}

        {TrademarkNotice}

        {CompatibilityNotice}

        {ProjectLicenseNotice}

        {PrivacyNotice}

        {SourceNotice}
        """;
}
