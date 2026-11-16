using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class ShapeTextStyleResolverTests
{
    [Fact]
    public void XlsxDefaults_AreResolvedAfterReload()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var document = new DrawingShapeTextDocument(
            [new DrawingShapeTextParagraph([new(DrawingShapeTextInlineKind.Run, "Bold", style: new DrawingShapeTextRunStyle(Bold: true)),
                new(DrawingShapeTextInlineKind.Run, "Plain")], defaultRunStyle: new DrawingShapeTextRunStyle(Italic: true))],
            new DrawingShapeTextParagraphDefaults(RunStyle: new DrawingShapeTextRunStyle(Bold: false, ThemeFont: "+mn-lt")));
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1), ShapeTextDocument = document });
        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var runs = ShapeTextStyleResolver.Resolve(loaded.Sheets.Single().DrawingShapes.Single().ShapeTextDocument!,
            loaded.Theme.WithFonts("Major", "Minor")).Single().Inlines;
        runs[0].Style.Bold.Should().BeTrue();
        runs[1].Style.Bold.Should().BeFalse();
        runs[1].Style.Italic.Should().BeTrue();
        runs[1].Style.FontFamily.Should().Be("Minor");
    }

    [Fact]
    public void ResolvesEachScopeAndRunIndependently_WithoutFirstRunBleed()
    {
        var doc = new DrawingShapeTextDocument(
            [new DrawingShapeTextParagraph(
                [new(DrawingShapeTextInlineKind.Run, "First", style: new DrawingShapeTextRunStyle(Bold: true, Color: new CellColor(3, 4, 5))),
                 new(DrawingShapeTextInlineKind.Run, "Second", style: new DrawingShapeTextRunStyle(Bold: false)),
                 new(DrawingShapeTextInlineKind.Run, "Third")],
                defaultRunStyle: new DrawingShapeTextRunStyle(Italic: true), level: 1)],
            new DrawingShapeTextParagraphDefaults(DrawingShapeTextHAlign.Center, new DrawingShapeTextRunStyle(Bold: true, FontSizePoints: 18)),
            [new DrawingShapeTextListLevel(1, new DrawingShapeTextParagraphDefaults(DrawingShapeTextHAlign.Right, new DrawingShapeTextRunStyle(Bold: false)))]);
        var result = ShapeTextStyleResolver.Resolve(doc, WorkbookTheme.Office,
            new DrawingShapeTextRunStyle(FontFamily: "Base", FontSizePoints: 10, Color: new CellColor(1, 2, 3)));
        var paragraph = result.Single();
        paragraph.Alignment.Should().Be(DrawingShapeTextHAlign.Right);
        paragraph.Level.Should().Be(1);
        paragraph.DefaultStyle.Bold.Should().BeFalse();
        paragraph.DefaultStyle.Italic.Should().BeTrue();
        paragraph.DefaultStyle.FontSizePoints.Should().Be(18);
        paragraph.Inlines[0].Style.Bold.Should().BeTrue();
        paragraph.Inlines[0].Style.Color.Should().Be(new CellColor(3, 4, 5));
        paragraph.Inlines[1].Style.Bold.Should().BeFalse();
        paragraph.Inlines[2].Style.Bold.Should().BeFalse();
        paragraph.Inlines[2].Style.Color.Should().Be(new CellColor(1, 2, 3));
        paragraph.Inlines[2].Style.FontFamily.Should().Be("Base");
    }

    [Fact]
    public void FontAndColorChoicesOverrideAsUnits_AndResolveAgainstCurrentTheme()
    {
        var accent = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1);
        var doc = new DrawingShapeTextDocument(
            [new DrawingShapeTextParagraph(
                [new(DrawingShapeTextInlineKind.Run, "Theme"),
                 new(DrawingShapeTextInlineKind.Run, "Explicit", style: new DrawingShapeTextRunStyle(FontFamily: "Explicit", Color: new CellColor(3, 4, 5))),
                 new(DrawingShapeTextInlineKind.Run, "Unknown", style: new DrawingShapeTextRunStyle(ThemeFont: "+mn-ea")),
                 new(DrawingShapeTextInlineKind.Run, "Major", style: new DrawingShapeTextRunStyle(ThemeFont: "+mj-lt"))])],
            new DrawingShapeTextParagraphDefaults(RunStyle: new DrawingShapeTextRunStyle(ThemeFont: "+mn-lt", ThemeColor: accent)));
        var theme = WorkbookTheme.Office.WithFonts("Major", "Minor").WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 20, 30));
        var runs = ShapeTextStyleResolver.Resolve(doc, theme).Single().Inlines;
        runs[0].Style.FontFamily.Should().Be("Minor");
        runs[0].Style.ThemeFont.Should().Be("+mn-lt");
        runs[0].Style.Color.Should().Be(new CellColor(10, 20, 30));
        runs[1].Style.FontFamily.Should().Be("Explicit");
        runs[1].Style.ThemeFont.Should().BeNull();
        runs[1].Style.Color.Should().Be(new CellColor(3, 4, 5));
        runs[1].Style.ThemeColor.Should().BeNull();
        runs[2].Style.FontFamily.Should().BeNull();
        runs[2].Style.ThemeFont.Should().Be("+mn-ea");
        runs[3].Style.FontFamily.Should().Be("Major");
        var changedTheme = theme.WithFonts("New Major", "New Minor").WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(40, 50, 60));
        var changed = ShapeTextStyleResolver.Resolve(doc, changedTheme).Single().Inlines;
        changed[0].Style.FontFamily.Should().Be("New Minor");
        changed[0].Style.Color.Should().Be(new CellColor(40, 50, 60));
        changed[1].Style.FontFamily.Should().Be("Explicit");
        changed[1].Style.Color.Should().Be(new CellColor(3, 4, 5));
        changed[3].Style.FontFamily.Should().Be("New Major");
    }

    [Fact]
    public void KeepsEmptyParagraphsBreaksFieldsAndUnspecifiedDefaults()
    {
        var doc = new DrawingShapeTextDocument(
            [new DrawingShapeTextParagraph([]),
             new DrawingShapeTextParagraph([new(DrawingShapeTextInlineKind.Run, "a"),
                 new(DrawingShapeTextInlineKind.Break, style: new DrawingShapeTextRunStyle(Underline: "none")),
                 new(DrawingShapeTextInlineKind.Field, "2", "field", "slidenum", new DrawingShapeTextRunStyle(Baseline: 30000))],
                 alignment: DrawingShapeTextHAlign.Left)]);
        var paragraphs = ShapeTextStyleResolver.Resolve(doc, WorkbookTheme.Office, baseAlignment: DrawingShapeTextHAlign.Center);
        paragraphs.Should().HaveCount(2);
        paragraphs[0].Inlines.Should().BeEmpty();
        paragraphs[0].Alignment.Should().Be(DrawingShapeTextHAlign.Center);
        paragraphs[0].DefaultStyle.Bold.Should().BeNull();
        paragraphs[1].Alignment.Should().Be(DrawingShapeTextHAlign.Left);
        paragraphs[1].Inlines.Select(inline => inline.Kind).Should().Equal(DrawingShapeTextInlineKind.Run, DrawingShapeTextInlineKind.Break, DrawingShapeTextInlineKind.Field);
        paragraphs[1].Inlines[1].Style.Underline.Should().Be("none");
        paragraphs[1].Inlines[2].FieldId.Should().Be("field");
        paragraphs[1].Inlines[2].FieldType.Should().Be("slidenum");
        paragraphs[1].Inlines[2].Style.Baseline.Should().Be(30000);
    }
}
