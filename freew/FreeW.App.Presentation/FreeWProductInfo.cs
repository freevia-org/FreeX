using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Free.Shared.AppServices;

namespace FreeW.App.Presentation;

/// <summary>Host-neutral FreeW product, version, legal, and diagnostics text.</summary>
public static class FreeWProductInfo
{
    public const string ProductName = "FreeW";
    public const string HelpUrl = "https://github.com/tony-xmelon/FreeX/tree/main/freew";
    public const string FeedbackUrl = "https://github.com/tony-xmelon/FreeX/issues/new?title=FreeW%20feedback";
    public const string LatestReleaseUrl = "https://github.com/tony-xmelon/FreeX/actions/workflows/freew-release.yml";
    public const string TrademarkNotice = "FreeW is not affiliated with, endorsed by, or sponsored by Microsoft. Microsoft Word is a trademark of Microsoft Corporation.";
    public const string ProjectLicenseNotice = "FreeW Source License: Copyright (c) 2026 FreeX contributors. All rights reserved. Tester binaries may be downloaded and run for personal evaluation and testing. Redistribution or commercial distribution requires separate written permission from the copyright holder.";
    public const string PrivacyNotice = "Privacy: FreeW is a local desktop app. Documents are opened, edited, and saved on this machine unless the user explicitly chooses an external sharing path. Local tester diagnostics stay on the user's machine unless the user chooses to share them. FreeW does not intentionally collect document contents, filenames, or file paths in diagnostics or crash reports.";
    public const string SourceNotice = "Full project license, legal notice, privacy notice, third-party notices, and bundled third-party license texts are available in Help > Legal Notices and are maintained with the FreeX/FreeW release materials at https://github.com/tony-xmelon/FreeX.";

    public static string GetVersionText(Assembly assembly)
    {
        var version = AssemblyVersionMetadata.FromAssembly(assembly);
        return AppVersionFormatter.FormatVersionText(
            version.InformationalVersion);
    }

    public static string GetBuildVersionText(Assembly assembly)
    {
        var version = AssemblyVersionMetadata.FromAssembly(assembly);
        return AppVersionFormatter.FormatBuildVersionText(
            version.InformationalVersion,
            version.AssemblyVersion);
    }

    public static string CreateAboutText(Assembly assembly, string uiFramework)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(uiFramework);

        return $"""
               {ProductName}
               {GetVersionText(assembly)}

               A free word processor for DOCX editing and format-fidelity work.

               Built with .NET 10 and {uiFramework}.

               {TrademarkNotice}

               {ProjectLicenseNotice}

               {PrivacyNotice}

               {SourceNotice}
               """;
    }

    public static string CreateDiagnosticsText(
        Assembly assembly,
        string diagnosticsDirectory,
        string optionsPath)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var builder = new StringBuilder();
        builder.AppendLine("FreeW Diagnostics");
        builder.AppendLine($"Version: {GetBuildVersionText(assembly)}");
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
