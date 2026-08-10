using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class NamedStyleApplicationPlannerTests
{
    [Fact]
    public void Resolve_SelectedText_UsesLinkedCharacterStyle()
    {
        var document = LinkedDocument();

        var plan = NamedStyleApplicationPlanner.Resolve(document, "Heading1", hasTextSelection: true);

        plan.Should().NotBeNull();
        plan!.RequestedStyleId.Should().Be("Heading1");
        plan.Kind.Should().Be(NamedStyleApplicationKind.Character);
        plan.EffectiveStyle.Id.Should().Be("Heading1Char");
    }

    [Fact]
    public void Resolve_CollapsedCaret_KeepsParagraphStyle()
    {
        var plan = NamedStyleApplicationPlanner.Resolve(
            LinkedDocument(),
            "Heading1",
            hasTextSelection: false);

        plan!.Kind.Should().Be(NamedStyleApplicationKind.Paragraph);
        plan.EffectiveStyle.Id.Should().Be("Heading1");
    }

    [Fact]
    public void Resolve_InvalidOrNonCharacterLink_KeepsParagraphStyle()
    {
        var document = LinkedDocument();
        document.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            LinkedStyleId = "Normal",
        };

        var plan = NamedStyleApplicationPlanner.Resolve(document, "Heading1", hasTextSelection: true);

        plan!.Kind.Should().Be(NamedStyleApplicationKind.Paragraph);
        plan.EffectiveStyle.Id.Should().Be("Heading1");
    }

    [Fact]
    public void Resolve_ExplicitCharacterStyle_RemainsCharacterAtCaret()
    {
        var plan = NamedStyleApplicationPlanner.Resolve(
            LinkedDocument(),
            "Heading1Char",
            hasTextSelection: false);

        plan!.Kind.Should().Be(NamedStyleApplicationKind.Character);
        plan.EffectiveStyle.Id.Should().Be("Heading1Char");
    }

    [Fact]
    public void Resolve_UnknownStyle_ReturnsNull()
    {
        NamedStyleApplicationPlanner.Resolve(
                LinkedDocument(),
                "Missing",
                hasTextSelection: true)
            .Should().BeNull();
    }

    [Fact]
    public void OverlayCharacterStyle_PreservesDirectFormattingAndAppliesStyleFields()
    {
        var direct = RunFormatting.Default with
        {
            FontFamily = "Georgia",
            FontSizePt = 14,
            ColorHex = "#123456",
        };
        var style = RunFormatting.Default with
        {
            Bold = true,
            Underline = true,
        };

        var result = NamedStyleApplicationPlanner.OverlayCharacterStyle(direct, style);

        result.Bold.Should().BeTrue();
        result.Underline.Should().BeTrue();
        result.FontFamily.Should().Be("Georgia");
        result.FontSizePt.Should().Be(14);
        result.ColorHex.Should().Be("#123456");
    }

    private static TextDocument LinkedDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            Type = StyleType.Paragraph,
            LinkedStyleId = "Heading1Char",
        };
        document.Styles["Heading1Char"] = new DocumentStyle
        {
            Id = "Heading1Char",
            Name = "Heading 1 Char",
            Type = StyleType.Character,
            LinkedStyleId = "Heading1",
            Run = RunFormatting.Default with { Bold = true, ColorHex = "#2F5496" },
        };
        return document;
    }
}
