using FreeW.Core.IO;
using FreeW.Core.Model;

if (args.Length < 2
    || !string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(args[0], "inspect", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(args[0], "generate-nested", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(args[0], "inspect-nested", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(args[0], "generate-nested-text", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(args[0], "inspect-nested-text", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("usage: FreeW.GroupChildPhysicalFixture generate <path> | inspect <path> | generate-nested <path> | inspect-nested <path> | generate-nested-text <path> | inspect-nested-text <path>");
    return 2;
}

var path = Path.GetFullPath(args[1]);
if (string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase)
    || string.Equals(args[0], "generate-nested", StringComparison.OrdinalIgnoreCase)
    || string.Equals(args[0], "generate-nested-text", StringComparison.OrdinalIgnoreCase))
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var nestedText = string.Equals(args[0], "generate-nested-text", StringComparison.OrdinalIgnoreCase);
    var nested = string.Equals(args[0], "generate-nested", StringComparison.OrdinalIgnoreCase) || nestedText;
    var document = nestedText ? BuildNestedTextFixture() : nested ? BuildNestedFixture() : BuildFixture();
    DocxWriter.Write(document, path);
    Console.WriteLine($"generated={path}");
    if (nested)
    {
        Console.WriteLine("outer-offset-pt=180,150");
        Console.WriteLine("outer-size-pt=240,150");
        Console.WriteLine("outer-transform=22deg,flipH=false,flipV=false");
        Console.WriteLine("inner-offset-pt=58,38");
        Console.WriteLine("inner-size-pt=128,76");
        Console.WriteLine("inner-transform=-17deg,flipH=false,flipV=true");
        Console.WriteLine("child-path=0,1");
        Console.WriteLine("child-offset-pt=34,21");
        Console.WriteLine("child-size-pt=64,32");
        if (nestedText)
            Console.WriteLine("child-text=Nested leaf");
        return 0;
    }
    Console.WriteLine("group-offset-pt=180,150");
    Console.WriteLine("group-size-pt=210,130");
    Console.WriteLine("group-transform=25deg,flipH");
    Console.WriteLine("child-index=1");
    Console.WriteLine("child-offset-pt=110,55");
    Console.WriteLine("child-size-pt=65,35");
    return 0;
}

var loaded = DocxReader.Read(path);
var group = loaded.Blocks
    .OfType<Paragraph>()
    .SelectMany(paragraph => paragraph.Runs)
    .Select(run => run.DrawingGroup)
    .FirstOrDefault(candidate => candidate is not null);
if (group is null || group.Children.Count < 2)
{
    Console.Error.WriteLine("expected a two-child DrawingGroup");
    return 1;
}

if (string.Equals(args[0], "inspect-nested", StringComparison.OrdinalIgnoreCase)
    || string.Equals(args[0], "inspect-nested-text", StringComparison.OrdinalIgnoreCase))
{
    if (group.Children[0] is not DrawingGroup inner || inner.Children.Count < 2)
    {
        Console.Error.WriteLine("expected a nested DrawingGroup at child path 0,1");
        return 1;
    }

    var leaf = inner.Children[1];
    var innerOffset = group.ChildOffsets[0];
    var leafOffset = inner.ChildOffsets[1];
    Console.WriteLine($"outer-offset-pt={group.Placement.HorizontalOffsetPt:R},{group.Placement.VerticalOffsetPt:R}");
    Console.WriteLine($"outer-size-pt={group.WidthPt:R},{group.HeightPt:R}");
    Console.WriteLine($"outer-transform={group.RotationAngle:R}deg,flipH={group.FlipH},flipV={group.FlipV}");
    Console.WriteLine($"inner-offset-pt={innerOffset.X:R},{innerOffset.Y:R}");
    Console.WriteLine($"inner-size-pt={inner.WidthPt:R},{inner.HeightPt:R}");
    Console.WriteLine($"inner-transform={inner.RotationAngle:R}deg,flipH={inner.FlipH},flipV={inner.FlipV}");
    Console.WriteLine("child-path=0,1");
    Console.WriteLine($"child-offset-pt={leafOffset.X:R},{leafOffset.Y:R}");
    Console.WriteLine($"child-size-pt={inner.ChildWidthPt(1):R},{inner.ChildHeightPt(1):R}");
    Console.WriteLine($"child-kind={leaf.GetType().Name}");
        if (leaf is Shape leafShape)
    {
        Console.WriteLine($"child-transform={leafShape.RotationAngle:R}deg,flipH={leafShape.FlipH},flipV={leafShape.FlipV}");
        if (leafShape.CustomGeometry?.Segments.FirstOrDefault(segment => segment.Point is not null)
            is { Point: { } point })
            Console.WriteLine($"child-point-0={point.X:R},{point.Y:R}");
            if (leafShape.CustomGeometry is { } geometry)
        {
            var points = geometry.Segments
                .Where(segment => segment.Point is not null)
                .Select(segment => $"{segment.Point!.X:R},{segment.Point.Y:R}");
            Console.WriteLine($"child-points={string.Join(';', points)}");
        }
        }
        if (string.Equals(args[0], "inspect-nested-text", StringComparison.OrdinalIgnoreCase)
            && leaf is Shape textShape)
        {
            Console.WriteLine($"child-text={textShape.PlainText}");
            Console.WriteLine($"child-text-paragraphs={textShape.TextParagraphs.Count}");
            Console.WriteLine($"child-text-runs={textShape.TextParagraphs.Sum(paragraph => paragraph.Runs.Count)}");
            Console.WriteLine($"child-text-direction={textShape.TextDirection}");
            Console.WriteLine($"child-text-alignment={textShape.TextParagraphs.FirstOrDefault()?.Formatting.Alignment ?? TextAlignment.Left}");
        }
        return 0;
}

var child = group.Children[1];
var offset = group.ChildOffsets[1];
var width = group.ChildWidthPt(1);
var height = group.ChildHeightPt(1);
Console.WriteLine($"group-offset-pt={group.Placement.HorizontalOffsetPt:R},{group.Placement.VerticalOffsetPt:R}");
Console.WriteLine($"group-size-pt={group.WidthPt:R},{group.HeightPt:R}");
Console.WriteLine($"group-transform={group.RotationAngle:R}deg,flipH={group.FlipH},flipV={group.FlipV}");
Console.WriteLine("child-index=1");
Console.WriteLine($"child-offset-pt={offset.X:R},{offset.Y:R}");
Console.WriteLine($"child-size-pt={width:R},{height:R}");
Console.WriteLine($"child-kind={child.GetType().Name}");
return 0;

static TextDocument BuildFixture()
{
    var document = TextDocument.CreateEmpty();
    document.Blocks.Clear();
    document.Blocks.Add(new Paragraph("Wave 61 grouped child physical fixture"));

    var group = new DrawingGroup
    {
        WidthPt = 210,
        HeightPt = 130,
        RotationAngle = 25,
        FlipH = true,
        Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.Square,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page,
            HorizontalOffsetPt = 180,
            VerticalOffsetPt = 150,
            ZOrderIndex = 5
        }
    };
    group.Children.Add(new Shape(ShapeKind.Rectangle, 70, 40)
    {
        FillColorHex = "#D9EAF7",
        OutlineColorHex = "#1F4E79"
    });
    group.Children.Add(new Shape(ShapeKind.Ellipse, 65, 35)
    {
        FillColorHex = "#FCE4D6",
        OutlineColorHex = "#C65911",
        RotationAngle = 15,
        FlipV = true
    });
    group.ChildOffsets.Add((20, 20));
    group.ChildOffsets.Add((110, 55));

    var paragraph = new Paragraph();
    paragraph.Runs.Add(Run.FromDrawingGroup(group));
    document.Blocks.Add(paragraph);
    return document;
}

static TextDocument BuildNestedFixture()
{
    var document = TextDocument.CreateEmpty();
    document.Blocks.Clear();
    document.Blocks.Add(new Paragraph("Wave 62 nested grouped child physical fixture"));

    var outer = new DrawingGroup
    {
        WidthPt = 240,
        HeightPt = 150,
        RotationAngle = 22,
        Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.Square,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page,
            HorizontalOffsetPt = 180,
            VerticalOffsetPt = 150,
            ZOrderIndex = 5
        }
    };
    var inner = new DrawingGroup
    {
        WidthPt = 128,
        HeightPt = 76,
        RotationAngle = -17,
        FlipV = true
    };
    inner.Children.Add(new Shape(ShapeKind.Rectangle, 52, 28)
    {
        FillColorHex = "#D9EAF7",
        OutlineColorHex = "#1F4E79"
    });
    inner.Children.Add(new Shape(ShapeKind.Ellipse, 64, 32)
    {
        FillColorHex = "#FCE4D6",
        OutlineColorHex = "#C65911",
        RotationAngle = 10,
        FlipH = true,
        CustomGeometry = new CustomGeometry()
        {
            Segments =
            {
                new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(3_600, 1_800)),
                new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(18_000, 1_800)),
                new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(18_000, 19_800)),
                new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(3_600, 19_800)),
                new CustomSegment(CustomSegmentKind.Close)
            }
        }
    });
    inner.ChildOffsets.Add((8, 8));
    inner.ChildOffsets.Add((34, 21));
    outer.Children.Add(inner);
    outer.Children.Add(new Shape(ShapeKind.Rectangle, 58, 28)
    {
        FillColorHex = "#E2F0D9",
        OutlineColorHex = "#548235"
    });
    outer.ChildOffsets.Add((58, 38));
    outer.ChildOffsets.Add((166, 92));

    var paragraph = new Paragraph();
    paragraph.Runs.Add(Run.FromDrawingGroup(outer));
    document.Blocks.Add(paragraph);
    return document;
}

static TextDocument BuildNestedTextFixture()
{
    var document = BuildNestedFixture();
    var outer = document.Blocks.OfType<Paragraph>()
        .SelectMany(paragraph => paragraph.Runs)
        .Select(run => run.DrawingGroup)
        .First(group => group is not null)!;
    var inner = (DrawingGroup)outer.Children[0];
    var leaf = Shape.TextBoxWith("Nested leaf", 64, 32);
    leaf.FillColorHex = "#FCE4D6";
    leaf.OutlineColorHex = "#C65911";
    leaf.RotationAngle = 10;
    leaf.FlipH = true;
    inner.Children[1] = leaf;
    return document;
}
