using System.Text;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-11 fix bucket R6 focused regression tests.
/// </summary>
public sealed class FreeXR11B6Tests
{
    // ── R11-avalonia-shell-1: CF_HTML descriptor header must wrap the "HTML Format" payload ──────

    [Fact]
    public void WrapAsCfHtml_ProducesValidCfHtmlDescriptorHeader_WithByteOffsetsMatchingFragment()
    {
        const string fragment = "<table border=\"1\"><tr><td>Bold Fill</td></tr></table>";

        var wrapped = MainWindow.WrapAsCfHtmlForTest(fragment);

        // The mandatory CF_HTML descriptor fields must all be present before any markup, exactly as
        // real Excel/Word/browsers require to parse the payload (Windows "HTML Format" clipboard type).
        wrapped.Should().StartWith("Version:0.9\r\n", "CF_HTML payloads must start with the Version descriptor");
        wrapped.Should().Contain("StartHTML:", "CF_HTML requires a StartHTML byte-offset field");
        wrapped.Should().Contain("EndHTML:", "CF_HTML requires an EndHTML byte-offset field");
        wrapped.Should().Contain("StartFragment:", "CF_HTML requires a StartFragment byte-offset field");
        wrapped.Should().Contain("EndFragment:", "CF_HTML requires an EndFragment byte-offset field");

        // Parse the four offsets out of the header and verify they point at the real byte positions
        // (UTF-8 byte counts, matching how Win32 CF_HTML consumers read the offsets) of the fragment
        // markers within the wrapped payload — a raw un-wrapped fragment would have none of this.
        var startHtml = ParseOffset(wrapped, "StartHTML:");
        var endHtml = ParseOffset(wrapped, "EndHTML:");
        var startFragment = ParseOffset(wrapped, "StartFragment:");
        var endFragment = ParseOffset(wrapped, "EndFragment:");

        var utf8 = Encoding.UTF8.GetBytes(wrapped);

        startHtml.Should().BeGreaterThan(0, "StartHTML must point past the descriptor header, not offset 0");
        endHtml.Should().Be(utf8.Length, "EndHTML must point at the end of the whole wrapped payload");
        startFragment.Should().BeGreaterThan(startHtml, "StartFragment must come after <html><body> markup");
        endFragment.Should().BeGreaterThan(startFragment, "EndFragment must come after the fragment content");

        // The bytes between StartFragment and EndFragment must be exactly the original fragment —
        // this is what every CF_HTML-aware paste target (Word/Outlook/Excel/browsers) extracts.
        var extractedFragment = Encoding.UTF8.GetString(utf8, startFragment, endFragment - startFragment);
        extractedFragment.Should().Be(fragment, "the byte offsets must bracket exactly the original HTML fragment");
    }

    [Fact]
    public void WrapAsCfHtml_IsDistinctFromRawFragment_SoWindowsHtmlFormatIsNeverBareMarkup()
    {
        const string fragment = "<table border=\"1\"><tr><td>X</td></tr></table>";

        var wrapped = MainWindow.WrapAsCfHtmlForTest(fragment);

        // Regression guard for the root bug: registering the bare fragment (with no descriptor
        // header) under the Windows "HTML Format" clipboard name corrupts every CF_HTML-aware paste
        // target. The wrapped payload must never equal the raw fragment.
        wrapped.Should().NotBe(fragment);
        wrapped.Should().StartWith("Version:0.9");
    }

    private static int ParseOffset(string header, string fieldName)
    {
        var idx = header.IndexOf(fieldName, StringComparison.Ordinal);
        idx.Should().BeGreaterThanOrEqualTo(0, $"header must contain {fieldName}");
        var start = idx + fieldName.Length;
        var end = header.IndexOf("\r\n", start, StringComparison.Ordinal);
        var digits = header.Substring(start, end - start);
        return int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
    }
}
