using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class SourceLoadedShapeTextTests
{
    [Fact]
    public void TextEditSaveUndoAndRedo_SurviveSourcePackageFastPaths()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1), ShapeText = "Before" });
        var adapter = new XlsxFileAdapter();
        using var original = new MemoryStream();
        adapter.Save(workbook, original);
        original.Position = 0;
        var loaded = adapter.Load(original);
        var loadedSheet = loaded.Sheets.Single();
        var shape = loadedSheet.DrawingShapes.Single();
        shape.IsSourceLoaded.Should().BeTrue();
        var context = new TestCommandContext(loaded);
        var command = new SetDrawingShapeTextCommand(loadedSheet.Id, shape.Id, "After\nEdit");
        command.Apply(context).Success.Should().BeTrue();
        SaveAndRead(loaded, adapter).Should().Be("After\nEdit");
        command.Revert(context);
        SaveAndRead(loaded, adapter).Should().Be("Before");
        command.Apply(context).Success.Should().BeTrue();
        SaveAndRead(loaded, adapter).Should().Be("After\nEdit");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Insets_PreserveAbsentAndExplicitZeroThroughSourceAndRegeneration(bool regenerate)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1),
            ShapeText = "Label", ShapeTextInsets = new DrawingShapeTextInsets(0, 19050, null, 38100) });
        var adapter = new XlsxFileAdapter();
        using var original = new MemoryStream();
        adapter.Save(workbook, original);
        original.Position = 0;
        var loaded = adapter.Load(original);
        var shape = loaded.Sheets.Single().DrawingShapes.Single();
        shape.ShapeTextInsets.Should().Be(new DrawingShapeTextInsets(0, 19050, null, 38100));
        var context = new TestCommandContext(loaded);
        var command = new SetDrawingShapeTextInsetsCommand(loaded.Sheets.Single().Id, shape.Id, new DrawingShapeTextInsets(9525, null, 0, 38100));
        command.Apply(context).Success.Should().BeTrue();
        command.Revert(context);
        shape.ShapeTextInsets.Should().Be(new DrawingShapeTextInsets(0, 19050, null, 38100));
        command.Apply(context).Success.Should().BeTrue();
        if (regenerate) shape.IsSourceLoaded = false;
        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var result = new XlsxFileAdapter().Load(saved).Sheets.Single().DrawingShapes.Single();
        result.ShapeTextInsets.Should().Be(new DrawingShapeTextInsets(9525, null, 0, 38100));
        using var native = new MemoryStream();
        var nativeAdapter = new NativeJsonAdapter();
        nativeAdapter.Save(loaded, native);
        native.Position = 0;
        nativeAdapter.Load(native).Sheets.Single().DrawingShapes.Single().ShapeTextInsets.Should().Be(result.ShapeTextInsets);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConnectorWithTextOrInsets_IsRejectedInsteadOfSilentlyLosingData(bool sourceLoaded, bool insets)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Connector", Kind = DrawingShapeKind.Line, Anchor = new CellAddress(sheet.Id, 1, 1) });
        var adapter = new XlsxFileAdapter();
        if (sourceLoaded)
        {
            using var source = new MemoryStream();
            adapter.Save(workbook, source);
            source.Position = 0;
            workbook = adapter.Load(source);
            sheet = workbook.Sheets.Single();
        }
        var shape = sheet.DrawingShapes.Single();
        if (insets) shape.ShapeTextInsets = new DrawingShapeTextInsets(0, null, null, null);
        else shape.ShapeText = "not supported";
        Action save = () => { using var output = new MemoryStream(); adapter.Save(workbook, output); };
        save.Should().Throw<InvalidOperationException>().WithMessage("*Connector shapes cannot contain text*");
    }

    private static string? SaveAndRead(Workbook workbook, XlsxFileAdapter adapter)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream).Sheets.Single().DrawingShapes.Single().ShapeText;
    }
}
