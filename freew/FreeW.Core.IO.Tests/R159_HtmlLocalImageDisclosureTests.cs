using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r159 remediation. SECURITY.
///
/// <para>
/// Round 159 taught the HTML reader to resolve a local <c>&lt;img src="file:///..."&gt;</c>, so a
/// document pasted or saved from Word -- which writes its images to temp files and references them
/// that way -- keeps its pictures. As written it read ANY path the HTML named, with no check that
/// the bytes were an image and no size limit.
/// </para>
///
/// <para>
/// The HTML is untrusted: it arrives from a downloaded .htm the user opens, or from clipboard HTML
/// a hostile page wrote. So the reader would read any file the user can read and embed its bytes
/// verbatim into the document, which the user might then save or send -- a local-file disclosure
/// triggered by their own Open or Paste. The bytes were even tagged as PNG, because
/// <see cref="InlineImage.DetectFormat"/> falls back to PNG for unrecognised data by design.
/// </para>
///
/// <para>
/// These assert the boundary directly: a real image still loads, and a non-image file referenced
/// the same way does not reach the document.
/// </para>
/// </summary>
public sealed class R159_HtmlLocalImageDisclosureTests
{
    private static readonly byte[] RealPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
    ];

    [Fact]
    public void A_non_image_file_referenced_by_untrusted_html_is_not_embedded()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreeW.R159." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var secret = Path.Combine(directory, "private-notes.txt");
            File.WriteAllText(secret, "PATIENT RECORD 12345 -- not an image, must never be embedded");

            var document = LoadHtmlReferencing(secret);

            AllImageBytes(document).Should().BeEmpty(
                "the reader must not pull an arbitrary local file into the document just because "
                + "untrusted HTML named it -- the user would then save or send it unknowingly");

            document.PlainText.Should().NotContain(
                "PATIENT RECORD",
                "and the contents must not reach the document by any other route either");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_real_local_image_still_loads()
    {
        // The sibling half: the feature this resolver exists for must keep working, or the fix is
        // just a removal. A genuine PNG on disk is exactly what Word's clipboard HTML points at.
        var directory = Path.Combine(Path.GetTempPath(), "FreeW.R159." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var image = Path.Combine(directory, "clip_image001.png");
            File.WriteAllBytes(image, RealPng);

            var document = LoadHtmlReferencing(image);

            AllImageBytes(document).Should().ContainSingle()
                .Which.Should().Equal(RealPng, "a real image must still be embedded");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_file_whose_extension_claims_png_but_whose_bytes_do_not_is_rejected()
    {
        // The extension is attacker-chosen too, so it cannot be what decides this.
        var directory = Path.Combine(Path.GetTempPath(), "FreeW.R159." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var disguised = Path.Combine(directory, "not-really.png");
            File.WriteAllText(disguised, "CONFIDENTIAL -- plain text wearing a png extension");

            var document = LoadHtmlReferencing(disguised);

            AllImageBytes(document).Should().BeEmpty(
                "the bytes decide whether something is an image, not the name the HTML gave it");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TextDocument LoadHtmlReferencing(string path)
    {
        var html =
            "<!doctype html><html><body><p>Report <img src=\"file:///"
            + path.Replace('\\', '/')
            + "\"></p></body></html>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return new HtmlFileAdapter().Load(stream);
    }

    private static IReadOnlyList<byte[]> AllImageBytes(TextDocument document) =>
        document.Blocks
            .OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Image)
            .Where(image => image is not null)
            .Select(image => image!.Bytes)
            .ToList();
}
