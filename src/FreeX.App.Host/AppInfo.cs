namespace FreeX.App.Host;

public static class AppInfo
{
    public const string VersionText = "Version 0.5 (Tester Release)";
    public const string HelpUrl = "https://github.com/tony-xmelon/FreeX";
    public const string FeedbackUrl = "https://github.com/tony-xmelon/FreeX/issues/new";
    public const string LatestReleaseUrl = "https://github.com/tony-xmelon/FreeX/releases/latest";
    public const string LatestTesterDownloadUrl = "https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.exe";
    public const string TrademarkNotice = "FreeX is not affiliated with, endorsed by, or sponsored by Microsoft. Microsoft Excel is a trademark of Microsoft Corporation.";
    public const string ProjectLicenseNotice = "FreeX Source License: Copyright (c) 2026 FreeX contributors. All rights reserved. Tester binaries may be downloaded and run for personal evaluation and testing. Redistribution or commercial distribution requires separate written permission from the copyright holder.";
    public const string PrivacyNotice = "Privacy: FreeX is a local Windows desktop app. Workbooks are opened, edited, and saved on this machine unless the user explicitly chooses an external sharing path. Tester diagnostics are written under %LOCALAPPDATA%\\FreeX\\Diagnostics and stay local unless the user chooses to share them. Start FreeX with FREEX_DIAGNOSTICS=0 to disable local diagnostics for that run. FreeX does not intentionally collect workbook contents, formulas, filenames, or file paths in diagnostics or crash reports.";
    public const string CompatibilityNotice = "Compatibility references: FreeX uses Microsoft product names only in plain text when describing file compatibility, interoperability, excluded Microsoft services, or test/reference behavior. FreeX does not use Microsoft logos, product icons, trade dress, or Microsoft-style app branding. File-format labels use neutral names such as XLSX Workbook.";
    public const string ThirdPartyRuntimeNotice =
        "Third-party runtime notices: Runtime dependencies remain governed by their own licenses. The publishable app dependency set is covered by MIT, Apache-2.0, and BSD-3-Clause style licenses. Runtime packages: ClosedXML, ClosedXML.Parser, DocumentFormat.OpenXml, DocumentFormat.OpenXml.Framework, ExcelDataReader, ExcelNumberFormat, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options, Microsoft.Extensions.Primitives, OxyPlot.Core, OxyPlot.Wpf, OxyPlot.Wpf.Shared, PDFsharp-WPF, RBush.Signed, Sentry, Serilog, Serilog.Extensions.Logging, Serilog.Sinks.Console, Serilog.Sinks.File, SharpVectors.Wpf, SixLabors.Fonts, and System.IO.Packaging. No package-provided NOTICE files were found in the restored runtime packages.";
    public const string SourceNotice = "Full legal, privacy, and third-party license texts are available in Help > Legal Notices and are maintained with the FreeX release materials at https://github.com/tony-xmelon/FreeX.";

    public static string AboutText { get; } =
        $"FreeX\n{VersionText}\n\nA free spreadsheet app for .xlsx files.\n\nBuilt with .NET 10, WPF, ClosedXML, OxyPlot.\n\n{TrademarkNotice}\n\n{CompatibilityNotice}\n\n{ProjectLicenseNotice}\n\n{PrivacyNotice}\n\n{ThirdPartyRuntimeNotice}\n\n{SourceNotice}";
}
