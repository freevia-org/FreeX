using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Covers the legacy Word 97-2003 binary adapter (<c>.doc</c>/<c>.dot</c>) — design §5.5: capability
/// shape, error paths, and basic round-trip. See <see cref="LegacyDocWriteTests"/> for the write
/// round-trip tests.
/// </summary>
public sealed class LegacyDocFileAdapterTests
{
    [Fact]
    public void Capabilities_AreReadWrite_WithTemplateDot()
    {
        var adapter = new LegacyDocFileAdapter();

        var doc = adapter.Formats.Should().ContainSingle(f => f.Extension == ".doc").Which;
        doc.CanOpen.Should().BeTrue();
        doc.CanSave.Should().BeTrue();
        doc.OpensAsTemplate.Should().BeFalse();

        var dot = adapter.Formats.Should().ContainSingle(f => f.Extension == ".dot").Which;
        dot.CanOpen.Should().BeTrue();
        dot.CanSave.Should().BeTrue();
        dot.OpensAsTemplate.Should().BeTrue();
    }

    [Fact]
    public void Load_OnNonDocBytes_ThrowsClearError()
    {
        var adapter = new LegacyDocFileAdapter();
        // Not an OLE2/CFB container — hop 1 must fail with a distinguishable, user-facing message.
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("this is not a .doc file"));

        var act = () => adapter.Load(stream);

        act.Should().Throw<InvalidDataException>().WithMessage("*Word 97-2003*");
    }

    [Fact]
    public void Save_WritesCfbContainerAndReloadsTextCompatibilitySubset()
    {
        var adapter = new LegacyDocFileAdapter();
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Legacy body one"));
        document.Blocks.Add(new Paragraph("Legacy body two"));

        using var stream = new MemoryStream();
        adapter.Save(document, stream);
        stream.Length.Should().BeGreaterThan(8);
        stream.ToArray().Take(8).Should().Equal(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });

        stream.Position = 0;
        var loadedText = string.Join("\n", adapter.Load(stream).Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText));
        loadedText.Should().Contain("Legacy body one");
        loadedText.Should().Contain("Legacy body two");
    }
}
