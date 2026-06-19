using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Covers the read-only legacy <c>.doc</c>/<c>.dot</c> import adapter (design §5.5): its capability shape
/// (open-only; <c>.dot</c> is a template; Save throws) and the hop-1 error path. NOTE: a positive extraction
/// round-trip is not covered because the repo carries no committed <c>.doc</c> fixture and DocSharp converts
/// <em>from</em> .doc only (so one cannot be synthesized in-test); that golden test is a follow-up once a
/// license-clean sample .doc is added under Fixtures/.
/// </summary>
public sealed class LegacyDocFileAdapterTests
{
    [Fact]
    public void Capabilities_AreOpenOnly_WithTemplateDot()
    {
        var adapter = new LegacyDocFileAdapter();

        var doc = adapter.Formats.Should().ContainSingle(f => f.Extension == ".doc").Which;
        doc.CanOpen.Should().BeTrue();
        doc.CanSave.Should().BeFalse();
        doc.OpensAsTemplate.Should().BeFalse();

        var dot = adapter.Formats.Should().ContainSingle(f => f.Extension == ".dot").Which;
        dot.CanOpen.Should().BeTrue();
        dot.CanSave.Should().BeFalse();
        dot.OpensAsTemplate.Should().BeTrue();
    }

    [Fact]
    public void Save_Throws_NotSupported()
    {
        var adapter = new LegacyDocFileAdapter();
        using var stream = new MemoryStream();

        var act = () => adapter.Save(new TextDocument(), stream);

        act.Should().Throw<NotSupportedException>().WithMessage("*Save As .docx*");
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
