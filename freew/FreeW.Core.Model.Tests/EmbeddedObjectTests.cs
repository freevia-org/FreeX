using System.Text;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit coverage for the <see cref="EmbeddedObject"/> / <see cref="Run.EmbeddedObject"/> model (roadmap
/// item Y2): the inline-run-mark API, the constructor and the <see cref="EmbeddedObject.Create"/> factory.
/// </summary>
public class EmbeddedObjectTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("payload");
    private static readonly byte[] IconBytes = [1, 2, 3, 4];

    [Fact]
    public void Constructor_StoresPayloadAndProgId()
    {
        var obj = new EmbeddedObject(Payload, "Excel.Sheet.12");

        obj.Payload.Should().Equal(Payload);
        obj.ProgId.Should().Be("Excel.Sheet.12");
        obj.Icon.Should().BeNull();
        // Defaults to a Word-typical icon size.
        obj.WidthPt.Should().Be(96);
        obj.HeightPt.Should().Be(96);
    }

    [Fact]
    public void Create_WithIcon_DefaultsSizeToIconSize()
    {
        var icon = new InlineImage(IconBytes, 120, 80);

        var obj = EmbeddedObject.Create(Payload, "Excel.Sheet.12", icon);

        obj.Icon.Should().BeSameAs(icon);
        obj.WidthPt.Should().Be(120);
        obj.HeightPt.Should().Be(80);
    }

    [Fact]
    public void Create_ExplicitSize_OverridesIconSize()
    {
        var icon = new InlineImage(IconBytes, 120, 80);

        var obj = EmbeddedObject.Create(Payload, "Excel.Sheet.12", icon, widthPt: 200, heightPt: 150);

        obj.WidthPt.Should().Be(200);
        obj.HeightPt.Should().Be(150);
    }

    [Fact]
    public void Create_NoIconNoSize_KeepsDefaults()
    {
        var obj = EmbeddedObject.Create(Payload, "Word.Document.12");

        obj.Icon.Should().BeNull();
        obj.WidthPt.Should().Be(96);
        obj.HeightPt.Should().Be(96);
    }

    [Fact]
    public void CreateLinked_StoresExternalTargetWithoutPayload()
    {
        var icon = new InlineImage(IconBytes, 120, 80);

        var obj = EmbeddedObject.CreateLinked("file:///C:/Data/Book.xlsx", "Excel.Sheet.12", icon);

        obj.IsLinked.Should().BeTrue();
        obj.LinkedTarget.Should().Be("file:///C:/Data/Book.xlsx");
        obj.Payload.Should().BeEmpty();
        obj.Icon.Should().BeSameAs(icon);
        obj.WidthPt.Should().Be(120);
        obj.HeightPt.Should().Be(80);
    }

    [Fact]
    public void Clone_LinkedObjectRetainsTargetAndCopiesPresentation()
    {
        var source = EmbeddedObject.CreateLinked(
            "file:///C:/Data/Book.xlsx",
            "Excel.Sheet.12",
            new InlineImage(IconBytes, 120, 80));

        var clone = source.Clone();

        clone.Should().NotBeSameAs(source);
        clone.LinkedTarget.Should().Be(source.LinkedTarget);
        clone.IsLinked.Should().BeTrue();
        clone.Icon.Should().NotBeSameAs(source.Icon);
        clone.Icon!.PngBytes.Should().Equal(source.Icon!.PngBytes);
    }

    [Fact]
    public void FromEmbeddedObject_BuildsRunCarryingTheObjectAndNoText()
    {
        var obj = new EmbeddedObject(Payload, "Excel.Sheet.12");

        var run = Run.FromEmbeddedObject(obj);

        run.EmbeddedObject.Should().BeSameAs(obj);
        run.Text.Should().BeEmpty();
    }

    [Fact]
    public void ProgId_IsMutable()
    {
        var obj = new EmbeddedObject(Payload, "Excel.Sheet.12") { ProgId = "PowerPoint.Show.12" };

        obj.ProgId.Should().Be("PowerPoint.Show.12");
    }
}
