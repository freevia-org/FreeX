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
}
