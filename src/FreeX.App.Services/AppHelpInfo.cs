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
    public const string AvaloniaPlatformSummary = "Built with .NET 10, Avalonia, ClosedXML.";
    public const string WpfPlatformSummary = "Built with .NET 10, WPF, ClosedXML, OxyPlot.";
    public const string ThirdPartyRuntimeNotice =
        "Third-party runtime notices: Runtime dependencies remain governed by their own licenses. The publishable app dependency set is covered by MIT, Apache-2.0, and BSD-3-Clause style licenses. Runtime packages: ClosedXML, ClosedXML.Parser, DocumentFormat.OpenXml, DocumentFormat.OpenXml.Framework, ExcelDataReader, ExcelNumberFormat, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options, Microsoft.Extensions.Primitives, OxyPlot.Core, OxyPlot.Wpf, OxyPlot.Wpf.Shared, PDFsharp-WPF, RBush.Signed, Sentry, Serilog, Serilog.Extensions.Logging, Serilog.Sinks.Console, Serilog.Sinks.File, SharpVectors.Wpf, SixLabors.Fonts, and System.IO.Packaging. No package-provided NOTICE files were found in the restored runtime packages.";

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
    /// Formats <paramref name="informationalVersion"/> as a display version string,
    /// dropping a trailing <c>.0</c> patch component (e.g. <c>0.5.0</c> → <c>0.5</c>).
    /// Delegates to <see cref="AppVersionFormatter.FormatVersionText"/> with
    /// <c>dropTrailingZeroPatch: true</c> to preserve FreeX's existing display behavior.
    /// </summary>
    public static string FormatVersionText(string? informationalVersion) =>
        AppVersionFormatter.FormatVersionText(informationalVersion, dropTrailingZeroPatch: true);

    /// <inheritdoc cref="AppVersionFormatter.FormatBuildVersionText"/>
    public static string FormatBuildVersionText(string? informationalVersion, string? assemblyVersion = null) =>
        AppVersionFormatter.FormatBuildVersionText(informationalVersion, assemblyVersion);

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

    public static string BuildWpfAboutText(string versionText) =>
        $"FreeX\n{versionText}\n\nA free spreadsheet app for XLSX editing with open-only legacy XLS/XLSB import.\n\n{WpfPlatformSummary}\n\n{TrademarkNotice}\n\n{CompatibilityNotice}\n\n{ProjectLicenseNotice}\n\n{PrivacyNotice}\n\n{ThirdPartyRuntimeNotice}\n\n{SourceNotice}";
}
