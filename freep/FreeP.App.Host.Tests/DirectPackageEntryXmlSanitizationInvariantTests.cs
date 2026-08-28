using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// FreeP's half of the direct-package-entry sanitization tripwire. The scan itself, and the full
/// statement of what it can and cannot see, lives in
/// <see cref="PackageEntryXmlSanitizationScanner"/>; this file supplies FreeP's source root and the
/// writers that must stay covered.
/// <para>
/// FreeP is the app that got this right first -- both <c>PptxPackageWriter</c> and
/// <c>PptxChartWriter</c> already sanitized at their entry writers when the same bug class was being
/// fixed across FreeX and FreeW -- so this guard is here to KEEP it right. FreeP's .pptx writer is an
/// active area, and it is the app most likely to grow a new part writer; it also has no ODF writer yet,
/// so a future .odp adapter is exactly the kind of addition this exists to catch.
/// </para>
/// <para>
/// It lives in FreeP.App.Host.Tests because FreeP has no dedicated Core.IO test project; the scan reads
/// source text and needs no reference to the IO assembly.
/// </para>
/// </summary>
public class DirectPackageEntryXmlSanitizationInvariantTests
{
    // Asserted by name every run so a refactor that changes the call shape fails loudly, instead of the
    // scan silently matching nothing and passing vacuously.
    private static readonly string[] MustStayCovered =
    [
        "PptxPackageWriter.cs",
        "PptxChartWriter.cs",
    ];

    [Fact]
    public void EveryDirectPackageEntryXmlWrite_Sanitizes()
    {
        var sites = PackageEntryXmlSanitizationScanner.Scan(
            Path.Combine(
                TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
                "freep",
                "FreeP.Core.IO"),
            "SanitizeInPlace");

        foreach (var writer in MustStayCovered)
        {
            sites.Select(site => site.FileName).Should().Contain(
                writer,
                "this scan exists for {0}; if it no longer matches, the regex has gone stale rather than the bypass having gone away",
                writer);
        }

        // No allowlist: unlike FreeX and FreeW, every direct package-entry write in FreeP genuinely
        // sanitizes today. If that ever needs an exemption, add it here with the reason, rather than
        // loosening the scan.
        var offenders = sites.Where(site => !site.Sanitizes).Select(site => site.ToString());

        // Joined rather than asserted on the list: FluentAssertions' BeEmpty reports only the FIRST item.
        string.Join(Environment.NewLine, offenders).Should().BeEmpty(
            "a package part written straight to a zip entry skips the shared package writers' sanitize, so one control character or lone surrogate in its model text aborts the whole presentation save");
    }
}
