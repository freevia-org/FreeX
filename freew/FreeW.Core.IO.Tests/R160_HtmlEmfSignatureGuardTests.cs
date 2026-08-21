using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r160 remediation. SECURITY.
///
/// <para>
/// The r159 remediation (see <see cref="R159_HtmlLocalImageDisclosureTests"/>) made
/// <c>HtmlFileAdapter</c>'s local-<c>&lt;img src="file:///..."&gt;</c> resolver require a genuine
/// image magic-byte signature before embedding a file the untrusted HTML named. Its EMF arm only
/// checked the leading 4 bytes (<c>01 00 00 00</c>), but the real EMF signature -- the one
/// <see cref="InlineImage.DetectFormat"/> actually enforces -- also requires the ASCII marker
/// " EMF" at byte offset 40. Any local file that happens to start with those 4 bytes (a plausible
/// prefix for plenty of unrelated binary/serialized formats) passed the guard, got embedded
/// verbatim, and was silently mislabeled Png because DetectFormat's own (correct) EMF check then
/// rejected it and fell back to its default.
/// </para>
///
/// <para>
/// These assert the boundary directly: a file with the loose 4-byte EMF prefix but no real EMF
/// marker must NOT be embedded, while a file carrying the genuine EMF signature (leading bytes AND
/// the offset-40 marker) still is.
/// </para>
/// </summary>
public sealed class R160_HtmlEmfSignatureGuardTests
{
    [Fact]
    public void A_file_with_only_the_loose_emf_prefix_is_not_embedded()
    {
        // Reproduces the r160 finding: first 4 bytes are the EMF record-type prefix (01 00 00 00),
        // but there is no " EMF" marker at offset 40 -- so this is NOT a real EMF, just a file whose
        // opening bytes happen to collide with the loose check the guard used to perform.
        var directory = Path.Combine(Path.GetTempPath(), "FreeW.R160." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var bytes = new byte[200];
            bytes[0] = 0x01;
            bytes[1] = 0x00;
            bytes[2] = 0x00;
            bytes[3] = 0x00;
            // Deliberately NOT the " EMF" marker at offset 40 (leave zeros there).
            var marker = Encoding.ASCII.GetBytes("PATIENT-RECORD-SECRET-12345");
            marker.CopyTo(bytes, 60);

            var secret = Path.Combine(directory, "secret.dat");
            File.WriteAllBytes(secret, bytes);

            var document = LoadHtmlReferencing(secret);

            AllImages(document).Should().BeEmpty(
                "a leading 01 00 00 00 alone is not a real EMF signature -- without the offset-40 "
                + "\" EMF\" marker that InlineImage.DetectFormat actually requires, this file must "
                + "not be embedded just because untrusted HTML pointed an <img> at it");

            document.PlainText.Should().NotContain(
                "PATIENT-RECORD-SECRET-12345",
                "and the disclosed bytes must not reach the document by any other route either");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_file_with_the_genuine_emf_signature_is_still_embedded()
    {
        // The sibling half: a real EMF -- leading 01 00 00 00 AND the " EMF" marker at offset 40 --
        // must keep working, or the fix is just a blanket removal of EMF support.
        var directory = Path.Combine(Path.GetTempPath(), "FreeW.R160." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var bytes = new byte[48];
            bytes[0] = 0x01;
            bytes[1] = 0x00;
            bytes[2] = 0x00;
            bytes[3] = 0x00;
            bytes[40] = 0x20; // ' '
            bytes[41] = 0x45; // 'E'
            bytes[42] = 0x4D; // 'M'
            bytes[43] = 0x46; // 'F'

            // Use an unrecognised extension so the resolver falls through to magic-byte detection
            // (InlineImage.DetectFormat) rather than trusting the (attacker-controlled) extension.
            var image = Path.Combine(directory, "clip_image001.dat");
            File.WriteAllBytes(image, bytes);

            var document = LoadHtmlReferencing(image);

            var images = AllImages(document);
            images.Should().ContainSingle("a genuine EMF must still be embedded");
            images[0].Bytes.Should().Equal(bytes);
            images[0].Format.Should().Be(ImageFormat.Emf,
                "the resolved format must agree with InlineImage.DetectFormat's own EMF check");
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

    private static IReadOnlyList<InlineImage> AllImages(TextDocument document) =>
        document.Blocks
            .OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Image)
            .Where(image => image is not null)
            .Select(image => image!)
            .ToList();
}
