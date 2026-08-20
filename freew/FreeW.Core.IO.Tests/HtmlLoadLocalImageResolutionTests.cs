using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Covers freew-paste-formats finding F2: <see cref="HtmlFileAdapter.Load(Stream)"/> used to pass a
/// hard-coded null image resolver, so an &lt;img src&gt; that was neither a <c>data:</c> URI nor resolvable
/// by that null delegate vanished silently -- no run, no placeholder. This is exactly the shape of a
/// Word/WordPad "HTML Format" clipboard paste, whose &lt;img&gt; references a local per-copy temp file via
/// a <c>file:</c> URI (e.g. <c>file:///…/clip_image001.png</c>). <see cref="HtmlFileAdapter.Load(Stream)"/>
/// is exactly the method <c>FreeWClipboardApplicationWorkflow.TryParseHtmlDocument</c> calls
/// (FreeW.App.Presentation/Editing/FreeWClipboardApplicationWorkflow.cs:585) for the HTML clipboard-paste
/// fallback path, so this is the real production call site, not just <see cref="HtmlFileAdapter.LoadHtml"/>
/// (which already accepted a resolver and is unaffected by this bug -- callers such as
/// <see cref="MhtmlFileAdapter"/> that supply their own resolver were never broken).
/// </summary>
public class HtmlLoadLocalImageResolutionTests
{
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    // -------------------------------------------------------------------------------------------------
    // Fixed: a file:// image reference -- the shape a Word/WordPad HTML-clipboard paste uses for its
    // inline pictures -- now resolves instead of silently vanishing.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Load_ResolvesImageReferencedByFileUri()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"freew-htmlload-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempPath, OnePixelPng);
        try
        {
            var fileUri = new Uri(tempPath).AbsoluteUri;
            var html = $"""<!doctype html><html><body><p><img src="{fileUri}" alt="Clip image" width="24" height="18"></p></body></html>""";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
            var loaded = new HtmlFileAdapter().Load(stream);

            var image = loaded.Blocks.Should().ContainSingle().Which
                .Should().BeOfType<Paragraph>().Which.Runs.Should().ContainSingle().Which.Image;
            image.Should().NotBeNull(because: "a file: URI image (Word/WordPad's HTML-clipboard shape) must not be silently dropped");
            image!.Bytes.Should().Equal(OnePixelPng);
            image.AltText.Should().Be("Clip image");
            image.Format.Should().Be(ImageFormat.Png);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Load_ResolvesImageReferencedByBareAbsoluteLocalPath()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"freew-htmlload-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempPath, OnePixelPng);
        try
        {
            // No "file://" scheme at all -- just the raw filesystem path, as some clip producers emit it.
            var html = $"""<!doctype html><html><body><p><img src="{tempPath}" alt="Raw path image"></p></body></html>""";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
            var loaded = new HtmlFileAdapter().Load(stream);

            var image = loaded.Blocks.Should().ContainSingle().Which
                .Should().BeOfType<Paragraph>().Which.Runs.Should().ContainSingle().Which.Image;
            image.Should().NotBeNull();
            image!.Bytes.Should().Equal(OnePixelPng);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Sibling / no-regression cases: behaviour that was already correct, and must stay exactly as it was.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Load_StillDecodesDataUriImagesInline()
    {
        var base64 = Convert.ToBase64String(OnePixelPng);
        var html = $"""<!doctype html><html><body><p><img src="data:image/png;base64,{base64}" alt="Inline"></p></body></html>""";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        var loaded = new HtmlFileAdapter().Load(stream);

        var image = loaded.Blocks.Should().ContainSingle().Which
            .Should().BeOfType<Paragraph>().Which.Runs.Should().ContainSingle().Which.Image;
        image.Should().NotBeNull();
        image!.Bytes.Should().Equal(OnePixelPng);
    }

    [Fact]
    public void Load_StillDropsRemoteHttpImageReferenceWithoutThrowing()
    {
        // Deliberately unresolved: fetching an arbitrary remote URL during a load/paste would be a
        // surprising network side effect and a potential SSRF vector. The paragraph text must still load;
        // only the picture is absent, exactly as before this fix (no crash, no partial garbage image).
        const string html = """<!doctype html><html><body><p>Before<img src="https://example.test/photo.png" alt="Remote">After</p></body></html>""";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        var loaded = new HtmlFileAdapter().Load(stream);

        var paragraph = loaded.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;
        paragraph.Runs.Should().NotContain(run => run.Image != null);
        paragraph.PlainText.Should().Contain("Before").And.Contain("After");
    }

    [Fact]
    public void Load_StillDropsImageReferencedByNonexistentLocalFile()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"freew-htmlload-missing-{Guid.NewGuid():N}.png");
        var html = $"""<!doctype html><html><body><p><img src="{missingPath}" alt="Gone"></p></body></html>""";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        var loaded = new HtmlFileAdapter().Load(stream);

        var paragraph = loaded.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;
        paragraph.Runs.Should().NotContain(run => run.Image != null);
    }

    [Fact]
    public void LoadHtml_WithExplicitNullResolver_StillDropsNonDataImagesUnchanged()
    {
        // The lower-level LoadHtml(string, resolver) overload used by MhtmlFileAdapter and others already
        // honoured whatever resolver it was handed -- that contract is unchanged by this fix. A caller that
        // explicitly passes a null-returning resolver still gets no image, same as always.
        var tempPath = Path.Combine(Path.GetTempPath(), $"freew-htmlload-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempPath, OnePixelPng);
        try
        {
            var fileUri = new Uri(tempPath).AbsoluteUri;
            var html = $"""<!doctype html><html><body><p><img src="{fileUri}" alt="Clip image"></p></body></html>""";

            var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);

            var paragraph = loaded.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;
            paragraph.Runs.Should().NotContain(run => run.Image != null);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
