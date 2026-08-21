using System.IO;
using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r160 remediation. SECURITY -- the second door.
///
/// <para>
/// Round 159 stopped untrusted HTML from naming an arbitrary local file and having its bytes
/// embedded. Round 160's audit found the same disclosure reachable through a different path that
/// nobody had gated: a .docx can carry a LINKED (not embedded) picture whose external relationship
/// target is any local path, and the preview resolver read it with only an existence and size
/// check. The bytes land in ResolvedLinkedImageBytes, which DisplayBytes hands to the renderers and
/// which the user may then save or forward.
/// </para>
///
/// <para>
/// Both readers now ask InlineImage.HasRecognisedSignature, which lives beside DetectFormat so the
/// guard and the decoder cannot drift apart -- the drift that produced the EMF bypass this round
/// also fixed.
/// </para>
/// </summary>
public sealed class R160_LinkedImageDisclosureTests
{
    private static readonly byte[] RealPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
    ];

    [Fact]
    public void A_linked_picture_pointing_at_a_non_image_file_resolves_nothing()
    {
        RunInTempDirectory(directory =>
        {
            var secret = Path.Combine(directory, "private-notes.txt");
            File.WriteAllText(secret, "PATIENT RECORD 12345 -- not an image, must never be embedded");

            var document = DocumentWithLinkedImage(secret);
            LinkedImagePreviewResolver.ResolveLocalPreviews(document, Path.Combine(directory, "host.docx"));

            var image = SingleImage(document);
            image.ResolvedLinkedImageBytes.Should().BeNullOrEmpty(
                "a document must not be able to pull an arbitrary local file into itself just by "
                + "naming it as a linked picture");
        });
    }

    [Fact]
    public void A_linked_picture_whose_bytes_share_the_emf_prefix_but_are_not_emf_resolves_nothing()
    {
        // The exact bypass shape: 0x01 0x00 0x00 0x00 is a common binary prefix, and a guard that
        // checks only those four bytes lets the file through while the decoder rejects it.
        RunInTempDirectory(directory =>
        {
            var disguised = Path.Combine(directory, "looks-like-emf.bin");
            var bytes = new byte[200];
            bytes[0] = 0x01;
            System.Text.Encoding.ASCII.GetBytes("SECRET-CONTENT").CopyTo(bytes, 60);
            File.WriteAllBytes(disguised, bytes);

            var document = DocumentWithLinkedImage(disguised);
            LinkedImagePreviewResolver.ResolveLocalPreviews(document, Path.Combine(directory, "host.docx"));

            SingleImage(document).ResolvedLinkedImageBytes.Should().BeNullOrEmpty(
                "the real EMF signature also requires the \" EMF\" marker at offset 40, which is "
                + "what the decoder checks, so the guard must check it too");
        });
    }

    [Fact]
    public void A_linked_picture_pointing_at_a_real_image_still_resolves()
    {
        // The sibling half: linked pictures are a real Word feature and must keep working, or the
        // fix is just a removal.
        RunInTempDirectory(directory =>
        {
            var image = Path.Combine(directory, "chart.png");
            File.WriteAllBytes(image, RealPng);

            var document = DocumentWithLinkedImage(image);
            LinkedImagePreviewResolver.ResolveLocalPreviews(document, Path.Combine(directory, "host.docx"));

            SingleImage(document).ResolvedLinkedImageBytes.Should().Equal(
                RealPng,
                "a genuinely linked image must still preview");
        });
    }

    private static TextDocument DocumentWithLinkedImage(string target)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(string.Empty)
        {
            Image = new InlineImage([], 96, 96, ImageFormat.Png) { LinkedImageTarget = target },
        });
        document.Blocks.Add(paragraph);
        return document;
    }

    private static InlineImage SingleImage(TextDocument document) =>
        document.Blocks
            .OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Image)
            .Single(image => image is not null)!;

    private static void RunInTempDirectory(Action<string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreeW.R160." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            body(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
