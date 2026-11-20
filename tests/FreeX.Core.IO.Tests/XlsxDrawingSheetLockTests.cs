using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxDrawingSheetLockTests
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewObjects_RoundTripSheetLockAndIndependentDrawingRestrictions(bool locked)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1), Locked = locked, DrawingNoMove = true, DrawingNoResize = false, DrawingLockAttributes = new Dictionary<string, string> { ["noRot"] = "1" } });
        sheet.TextBoxes.Add(new TextBoxModel { Name = "Text", Anchor = new CellAddress(sheet.Id, 2, 1), Locked = locked, DrawingNoMove = false });
        sheet.Pictures.Add(new PictureModel { Name = "Picture", Anchor = new CellAddress(sheet.Id, 3, 1), ImageBytes = Png, Locked = locked, LockAspectRatio = true, DrawingNoMove = true, DrawingNoResize = false, DrawingLockAttributes = new Dictionary<string, string> { ["noCrop"] = "1" } });
        using var saved = Save(workbook);
        var reloaded = new XlsxFileAdapter().Load(saved).Sheets.Single();
        var shape = reloaded.DrawingShapes.Single();
        var text = reloaded.TextBoxes.Single();
        var picture = reloaded.Pictures.Single();
        shape.Locked.Should().Be(locked);
        text.Locked.Should().Be(locked);
        picture.Locked.Should().Be(locked);
        shape.DrawingNoMove.Should().BeTrue();
        shape.DrawingNoResize.Should().BeFalse();
        shape.DrawingLockAttributes!["noRot"].Should().Be("1");
        text.DrawingNoMove.Should().BeFalse();
        picture.DrawingNoMove.Should().BeTrue();
        picture.DrawingNoResize.Should().BeFalse();
        picture.DrawingLockAttributes!["noCrop"].Should().Be("1");
        picture.LockAspectRatio.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PrintObject_RoundTripsOnAllDrawingKindsAndSourceEdit(bool print)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1), PrintObject = print });
        sheet.TextBoxes.Add(new TextBoxModel { Name = "Text", Anchor = new CellAddress(sheet.Id, 2, 1), PrintObject = print });
        sheet.Pictures.Add(new PictureModel { Name = "Picture", Anchor = new CellAddress(sheet.Id, 3, 1), ImageBytes = Png, PrintObject = print });
        sheet.Charts.Add(new ChartModel { Name = "Chart", Type = ChartType.Column, DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)), PrintObject = print });
        using var first = Save(workbook);
        var loaded = new XlsxFileAdapter().Load(first);
        var source = loaded.Sheets.Single();
        source.DrawingShapes.Single().PrintObject.Should().Be(print);
        source.TextBoxes.Single().PrintObject.Should().Be(print);
        source.Pictures.Single().PrintObject.Should().Be(print);
        source.Charts.Single().PrintObject.Should().Be(print);
        source.Pictures.Single().IsVisible.Should().BeTrue();
        source.DrawingShapes.Single().PrintObject = !print;
        source.TextBoxes.Single().PrintObject = !print;
        source.Pictures.Single().PrintObject = !print;
        source.Charts.Single().PrintObject = !print;
        using var second = Save(loaded);
        var result = new XlsxFileAdapter().Load(second).Sheets.Single();
        result.DrawingShapes.Single().PrintObject.Should().Be(!print);
        result.TextBoxes.Single().PrintObject.Should().Be(!print);
        result.Pictures.Single().PrintObject.Should().Be(!print);
        result.Charts.Single().PrintObject.Should().Be(!print);
        result.Pictures.Single().IsVisible.Should().BeTrue();
        using var native = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(loaded, native);
        native.Position = 0;
        var nativeSheet = adapter.Load(native).Sheets.Single();
        nativeSheet.DrawingShapes.Single().PrintObject.Should().Be(!print);
        nativeSheet.TextBoxes.Single().PrintObject.Should().Be(!print);
        nativeSheet.Pictures.Single().PrintObject.Should().Be(!print);
        nativeSheet.Charts.Single().PrintObject.Should().Be(!print);
    }

    [Fact]
    public void PrintObject_SourcePatchPreservesOtherClientDataAttributes()
    {
        using var input = Save(CreateTwoShapes());
        RewriteDrawing(input, root => root.Descendants(Xdr + "clientData").First().SetAttributeValue("fLocksWithSheet", "0"));
        var workbook = new XlsxFileAdapter().Load(input);
        var shape = workbook.Sheets.Single().DrawingShapes.Single(item => item.Name == "One");
        shape.Locked.Should().BeFalse();
        shape.PrintObject = false;
        using var output = Save(workbook);
        var result = new XlsxFileAdapter().Load(output).Sheets.Single().DrawingShapes.Single(item => item.Name == "One");
        result.PrintObject.Should().BeFalse();
        result.Locked.Should().BeFalse();
    }

    [Fact]
    public void ExcelShapeFixture_UnlockEditSurvivesSourcePackageSave()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "Excel_native_shapes_fill_outline_004.xlsx");
        using var input = new MemoryStream(File.ReadAllBytes(path));
        var workbook = new XlsxFileAdapter().Load(input);
        var shape = workbook.Sheets.Single().DrawingShapes.First();
        shape.Locked.Should().BeTrue();
        shape.Locked = false;
        using var saved = Save(workbook);
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Sheets.Single().DrawingShapes.First().Locked.Should().BeFalse();
    }

    [Fact]
    public void RenameAndUnlockSourcePicture_UsesOriginalIdentity()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel { Name = "First", Anchor = new CellAddress(sheet.Id, 1, 1), ImageBytes = Png });
        sheet.Pictures.Add(new PictureModel { Name = "Second", Anchor = new CellAddress(sheet.Id, 4, 1), ImageBytes = Png });
        using var input = Save(workbook);
        var loaded = new XlsxFileAdapter().Load(input);
        var first = loaded.Sheets.Single().Pictures.Single(picture => picture.Name == "First");
        first.Name = "Renamed";
        first.Locked = false;
        using var saved = Save(loaded);
        var pictures = new XlsxFileAdapter().Load(saved).Sheets.Single().Pictures;
        pictures.Single(picture => picture.Name == "First").Locked.Should().BeFalse();
        pictures.Single(picture => picture.Name == "Second").Locked.Should().BeTrue();
    }

    [Fact]
    public void MissingPictureLocks_UsesSchemaFalseAspectAndDefaultLockedSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel { Name = "Picture", Anchor = new CellAddress(sheet.Id, 1, 1), ImageBytes = Png, LockAspectRatio = false });
        using var saved = Save(workbook);
        var picture = new XlsxFileAdapter().Load(saved).Sheets.Single().Pictures.Single();
        picture.LockAspectRatio.Should().BeFalse();
        picture.Locked.Should().BeTrue();
    }

    [Fact]
    public void RegeneratedSourceShape_PreservesIndependentDrawingLockAttributes()
    {
        var workbook = CreateTwoShapes();
        var original = workbook.Sheets.Single().DrawingShapes[0];
        original.DrawingNoMove = true;
        original.DrawingLockAttributes = new Dictionary<string, string> { ["noRot"] = "1", ["noSelect"] = "1" };
        using var first = Save(workbook);
        var loaded = new XlsxFileAdapter().Load(first);
        var shape = loaded.Sheets.Single().DrawingShapes[0];
        shape.IsSourceLoaded = false;
        using var saved = Save(loaded);
        var result = new XlsxFileAdapter().Load(saved).Sheets.Single().DrawingShapes.Single(item => item.Name == "One");
        result.DrawingNoMove.Should().BeTrue();
        result.DrawingLockAttributes!["noRot"].Should().Be("1");
        result.DrawingLockAttributes["noSelect"].Should().Be("1");
    }

    [Fact]
    public void RegeneratedDrawings_PreserveLockExtensionChildren()
    {
        const string children = "<root><a:extLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:ext uri=\"urn:test\"><extra xmlns=\"urn:test\" value=\"x\" /></a:ext></a:extLst></root>";
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1), DrawingLockChildrenXml = children });
        sheet.TextBoxes.Add(new TextBoxModel { Name = "Text", Anchor = new CellAddress(sheet.Id, 3, 1), DrawingLockChildrenXml = children });
        sheet.Pictures.Add(new PictureModel { Name = "Picture", Anchor = new CellAddress(sheet.Id, 5, 1), ImageBytes = Png, DrawingLockChildrenXml = children });
        using var first = Save(workbook);
        var loaded = new XlsxFileAdapter().Load(first);
        var loadedSheet = loaded.Sheets.Single();
        loadedSheet.DrawingShapes.Single().IsSourceLoaded = false;
        loadedSheet.TextBoxes.Single().IsSourceLoaded = false;
        loadedSheet.Pictures.Single().IsSourceLoaded = false;
        using var second = Save(loaded);
        var roundTrip = new XlsxFileAdapter().Load(second).Sheets.Single();
        foreach (var xml in new[] { roundTrip.DrawingShapes.Single().DrawingLockChildrenXml, roundTrip.TextBoxes.Single().DrawingLockChildrenXml, roundTrip.Pictures.Single().DrawingLockChildrenXml })
            XElement.Parse(xml!).Descendants().Single(element => element.Name.LocalName == "extra").Attribute("value")!.Value.Should().Be("x");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChartSheetLock_RoundTripsAndSourceEditPersists(bool locked)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Charts.Add(new ChartModel { Name = "Chart", Type = ChartType.Column, DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)), Locked = locked,
            DrawingLockAttributes = new Dictionary<string, string> { ["noMove"] = "1", ["noSelect"] = "1" },
            DrawingLockChildrenXml = "<root><a:extLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:ext uri=\"urn:test\" /></a:extLst></root>" });
        using var first = Save(workbook);
        var loaded = new XlsxFileAdapter().Load(first);
        var chart = loaded.Sheets.Single().Charts.Single();
        chart.Locked.Should().Be(locked);
        chart.Locked = !locked;
        using var second = Save(loaded);
        var result = new XlsxFileAdapter().Load(second).Sheets.Single().Charts.Single();
        result.Locked.Should().Be(!locked);
        result.DrawingLockAttributes!["noMove"].Should().Be("1");
        result.DrawingLockAttributes["noSelect"].Should().Be("1");
        XElement.Parse(result.DrawingLockChildrenXml!).Descendants().Single(element => element.Name.LocalName == "ext").Attribute("uri")!.Value.Should().Be("urn:test");
        using var native = new MemoryStream();
        var nativeAdapter = new NativeJsonAdapter();
        nativeAdapter.Save(loaded, native);
        native.Position = 0;
        var nativeChart = nativeAdapter.Load(native).Sheets.Single().Charts.Single();
        nativeChart.Locked.Should().Be(!locked);
        nativeChart.DrawingLockAttributes!["noMove"].Should().Be("1");
        nativeChart.DrawingLockChildrenXml.Should().NotBeNull();
    }

    [Fact]
    public void RenamedSourceChart_LockEditSurvives()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Charts.Add(new ChartModel { Name = "Before", Type = ChartType.Column, DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)) });
        sheet.Pictures.Add(new PictureModel { Name = "Picture", Anchor = new CellAddress(sheet.Id, 6, 1), ImageBytes = Png });
        using var first = Save(workbook);
        var loaded = new XlsxFileAdapter().Load(first);
        var chart = loaded.Sheets.Single().Charts.Single();
        chart.Name = "After";
        chart.Locked = false;
        using var second = Save(loaded);
        var result = new XlsxFileAdapter().Load(second).Sheets.Single().Charts.Single();
        result.Name.Should().Be("After");
        result.Locked.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModifiedUnnamedOrDuplicateSourceDrawing_FailsRatherThanLosingLock(bool unnamed)
    {
        using var input = Save(CreateTwoShapes());
        RewriteDrawing(input, root =>
        {
            var properties = root.Descendants(Xdr + "cNvPr").ToArray();
            properties[0].SetAttributeValue("name", unnamed ? "" : "Duplicate");
            properties[1].SetAttributeValue("name", unnamed ? "" : "Duplicate");
        });
        var loaded = new XlsxFileAdapter().Load(input);
        loaded.Sheets.Single().DrawingShapes[0].Locked = false;
        Action save = () => { using var result = Save(loaded); };
        save.Should().Throw<InvalidOperationException>().WithMessage("*unnamed or duplicate-named*");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GroupChildrenWithConflictingAnchorFlags_FailRegardlessOfXmlOrder(bool reverse, bool printConflict)
    {
        using var input = Save(CreateTwoShapes());
        RewriteDrawing(input, root =>
        {
            var anchors = root.Elements().Where(element => element.Name == Xdr + "oneCellAnchor").ToArray();
            var first = anchors[0].Element(Xdr + "sp")!;
            var second = anchors[1].Element(Xdr + "sp")!;
            first.Remove();
            second.Remove();
            var group = new XElement(Xdr + "grpSp",
                new XElement(Xdr + "nvGrpSpPr", new XElement(Xdr + "cNvPr", new XAttribute("id", 99), new XAttribute("name", "Group")), new XElement(Xdr + "cNvGrpSpPr")),
                new XElement(Xdr + "grpSpPr"),
                reverse ? second : first,
                reverse ? first : second);
            anchors[0].Element(Xdr + "clientData")!.AddBeforeSelf(group);
            anchors[1].Remove();
        });
        var loaded = new XlsxFileAdapter().Load(input);
        loaded.Sheets.Single().DrawingShapes.Should().HaveCount(2);
        var shape = loaded.Sheets.Single().DrawingShapes.Single(shape => shape.Name == "One");
        if (printConflict) shape.PrintObject = false;
        else shape.Locked = false;
        Action save = () => { using var result = Save(loaded); };
        save.Should().Throw<InvalidOperationException>().WithMessage("*sharing one anchor*");
    }

    private static Workbook CreateTwoShapes()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "One", Anchor = new CellAddress(sheet.Id, 1, 1) });
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Two", Anchor = new CellAddress(sheet.Id, 4, 1) });
        return workbook;
    }

    private static void RewriteDrawing(MemoryStream stream, Action<XElement> rewrite)
    {
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
        var entry = zip.Entries.Single(item => item.FullName.StartsWith("xl/drawings/drawing", StringComparison.Ordinal) && item.FullName.EndsWith(".xml", StringComparison.Ordinal));
        XDocument document;
        using (var reader = entry.Open()) document = XDocument.Load(reader);
        rewrite(document.Root!);
        var path = entry.FullName;
        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(path).Open());
        document.Save(writer);
        }
        stream.Position = 0;
    }

    private static MemoryStream Save(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }
}
