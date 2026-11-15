using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public class CustomViewPrintPresenceTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void XlsxLoadedViewPreservesUnrepresentedValuesAndRestoresFalsePrintFlags_ThenUndoRestoresCapturedNull()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        sheet.PrintGridlines = false;
        sheet.PrintHeadings = false;
        var ctx = new TestCommandContext(workbook);
        new SaveCustomViewCommand("View").Apply(ctx).Success.Should().BeTrue();
        using var file = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, file);
        file.Position = 0;
        var loaded = adapter.Load(file);
        var live = loaded.GetSheet("Data")!;
        live.PrintGridlines = true;
        live.PrintHeadings = true;
        live.AutoPageBreaks = true;
        live.LegacyPrintSize = 4;
        live.PageSetupMetadata = new NativeXmlPreserveBag();
        live.PageSetupMetadata.Set("pageSetup", "<e custom=\"live\" />");
        var command = new ApplyCustomViewCommand("View");
        command.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        live.PrintGridlines.Should().BeFalse();
        live.PrintHeadings.Should().BeFalse();
        live.AutoPageBreaks.Should().BeTrue();
        live.LegacyPrintSize.Should().Be(4);
        live.PageSetupMetadata!.Get("pageSetup").Should().Contain("live");
        command.Revert(new TestCommandContext(loaded));
        live.PrintGridlines.Should().BeTrue();
        live.PrintHeadings.Should().BeTrue();
        live.AutoPageBreaks.Should().BeTrue();
        live.LegacyPrintSize.Should().Be(4);
        live.PageSetupMetadata!.Get("pageSetup").Should().Contain("live");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExcelStyleOmittedPrintOptions_RespectsWorkbookIncludeFlag(bool includePrintSettings)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var ctx = new TestCommandContext(workbook);
        new SaveCustomViewCommand("View", includePrintSettings: includePrintSettings).Apply(ctx).Success.Should().BeTrue();
        using var file = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, file);
        file.Position = 0;
        using (var zip = new ZipArchive(file, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            xml.Descendants(Ns + "customSheetView").Single().Element(Ns + "printOptions")?.Remove();
            entry.Delete();
            using var output = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            xml.Save(output);
        }
        file.Position = 0;
        var loaded = adapter.Load(file);
        var live = loaded.GetSheet("Data")!;
        live.PrintGridlines = true;
        live.PrintHeadings = true;
        var command = new ApplyCustomViewCommand("View");
        var loadedContext = new TestCommandContext(loaded);
        command.Apply(loadedContext).Success.Should().BeTrue();
        live.PrintGridlines.Should().Be(!includePrintSettings);
        live.PrintHeadings.Should().Be(!includePrintSettings);
        command.Revert(loadedContext);
        live.PrintGridlines.Should().BeTrue();
        live.PrintHeadings.Should().BeTrue();
    }

    [Fact]
    public void PresentEmptyPrintOptionsUsesFalseDefaults_WhileAbsentElementIsUncaptured()
    {
        var doc = XDocument.Parse("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <customSheetViews>
                <customSheetView guid="{11111111-1111-1111-1111-111111111111}"><printOptions /></customSheetView>
                <customSheetView guid="{22222222-2222-2222-2222-222222222222}" />
              </customSheetViews>
            </worksheet>
            """);
        var views = XlsxCustomViewMapper.ReadWorksheetViews(doc, Ns);
        views[0].State.PrintGridlines.Should().BeFalse();
        views[0].State.PrintHeadings.Should().BeFalse();
        views[1].State.PrintGridlines.Should().BeNull();
        views[1].State.PrintHeadings.Should().BeNull();
    }
}
