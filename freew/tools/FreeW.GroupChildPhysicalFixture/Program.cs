using FreeW.Core.IO;
using FreeW.Core.Model;

if (args.Length < 2
    || !string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(args[0], "inspect", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("usage: FreeW.GroupChildPhysicalFixture generate <path> | inspect <path>");
    return 2;
}

var path = Path.GetFullPath(args[1]);
if (string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase))
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var document = BuildFixture();
    DocxWriter.Write(document, path);
    Console.WriteLine($"generated={path}");
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
